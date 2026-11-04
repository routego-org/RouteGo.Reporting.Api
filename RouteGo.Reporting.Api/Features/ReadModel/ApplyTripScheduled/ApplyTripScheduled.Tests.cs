using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyTripScheduled;

[Collection(DatabaseCollection.Name)]
public sealed class ApplyTripScheduledTests
{
    private static readonly DateTimeOffset _announcedAt = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _tripId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string TripSql = "SELECT operator_id AS OperatorId, departure_at AS DepartureAt, seats_total AS SeatsTotal FROM reporting.trips WHERE trip_id = @TripId";
    private const string TripCountSql = "SELECT count(*) FROM reporting.trips";
    private const string BookingOperatorSql = "SELECT operator_id FROM reporting.bookings WHERE booking_id = @BookingId";
    private const string SeedBookingSql = """
        INSERT INTO reporting.bookings (booking_id, trip_id, status, status_changed_at)
        VALUES (@BookingId, @TripId, 'confirmed', @At)
        """;

    private readonly DatabaseFixture _database;

    public ApplyTripScheduledTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task HandleAsync_TripArrivingAfterItsBooking_AttributesTheBookingToItsOperator()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        var bookingId = Guid.NewGuid();
        await _database.SeedAsync(SeedBookingSql, new { BookingId = bookingId, TripId = _tripId, At = _announcedAt });
        ApplyTripScheduledHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Scheduled(_announcedAt, 10, 40, "Sunrise Lines"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        Assert.Equal("Sunrise Lines", await _database.QueryScalarAsync<string>(BookingOperatorSql, new { BookingId = bookingId }));
    }

    [Fact]
    public async Task HandleAsync_LaterAnnouncement_ReplacesDetails()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyTripScheduledHandler handler = CreateHandler();
        await handler.HandleAsync(Scheduled(_announcedAt, 10, 40, "Orbit Travels"), CancellationToken.None);

        // Act
        await handler.HandleAsync(Scheduled(_announcedAt.AddMinutes(5), 11, 40, "Orbit Travels"), CancellationToken.None);

        // Assert
        TripRow row = await TestQueries.QuerySingleAsync<TripRow>(_database, TripSql, new { TripId = _tripId });
        Assert.Equal(11, row.DepartureAt.UtcDateTime.Hour);
    }

    [Fact]
    public async Task HandleAsync_OlderAnnouncementAfterNewer_ChangesNothing()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyTripScheduledHandler handler = CreateHandler();
        await handler.HandleAsync(Scheduled(_announcedAt.AddMinutes(5), 11, 40, "Orbit Travels"), CancellationToken.None);

        // Act
        await handler.HandleAsync(Scheduled(_announcedAt, 10, 40, "Orbit Travels"), CancellationToken.None);

        // Assert
        TripRow row = await TestQueries.QuerySingleAsync<TripRow>(_database, TripSql, new { TripId = _tripId });
        Assert.Equal(11, row.DepartureAt.UtcDateTime.Hour);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_HoldsOneTrip()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyTripScheduledHandler handler = CreateHandler();
        ReceivedEvent receivedEvent = Scheduled(_announcedAt, 10, 40, "Orbit Travels");

        // Act
        await handler.HandleAsync(receivedEvent, CancellationToken.None);
        EventOutcome second = await handler.HandleAsync(receivedEvent, CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, second.Kind);
        Assert.Equal(1, await _database.QueryScalarAsync<int>(TripCountSql));
    }

    [Fact]
    public async Task HandleAsync_TripWithoutSeats_IsRejected()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyTripScheduledHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Scheduled(_announcedAt, 10, 0, "Orbit Travels"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(EventOutcome.InvalidEventData, outcome.Reason);
        Assert.Equal(0, await _database.QueryScalarAsync<int>(TripCountSql));
    }

    [Fact]
    public void EventTypes_Always_NamesTripScheduled()
    {
        // Arrange
        ApplyTripScheduledHandler handler = CreateHandler();

        // Act
        IReadOnlyList<string> eventTypes = handler.EventTypes;

        // Assert
        Assert.Equal(new[] { "TripScheduled" }, eventTypes);
    }

    private ApplyTripScheduledHandler CreateHandler()
    {
        IWriterConnectionFactory writerFactory = TestWriters.CreateWriter(_database);
        return new ApplyTripScheduledHandler(writerFactory, NullLogger<ApplyTripScheduledHandler>.Instance);
    }

    private static ReceivedEvent Scheduled(DateTimeOffset occurredAt, int departureHour, int seats, string operatorId)
    {
        string json = $$"""
            {"tripId":"{{_tripId}}","operatorId":"{{operatorId}}","source":{"cityName":"Pune"},
             "destination":{"cityName":"Goa"},"departureUtc":"2026-11-05T{{departureHour:00}}:00:00Z","totalSeats":{{seats}}}
            """;
        using var document = JsonDocument.Parse(json);
        return new ReceivedEvent
        {
            EventId = "evt-" + occurredAt.Ticks,
            EventType = "TripScheduled",
            OccurredAt = occurredAt,
            CorrelationId = "corr-1",
            Data = document.RootElement.Clone(),
        };
    }

    private sealed class TripRow
    {
        public string OperatorId { get; set; } = string.Empty;

        public DateTimeOffset DepartureAt { get; set; }

        public int SeatsTotal { get; set; }
    }
}
