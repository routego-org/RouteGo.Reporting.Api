using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyBookingEvent;

[Collection(DatabaseCollection.Name)]
public sealed class ApplyBookingEventTests
{
    private static readonly DateTimeOffset _confirmedAt = new DateTimeOffset(2026, 10, 31, 10, 50, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _cancelledAt = new DateTimeOffset(2026, 10, 31, 11, 5, 0, TimeSpan.Zero);
    private static readonly Guid _tripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _bookingId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string BookingSql = """
        SELECT status, operator_id AS OperatorId, amount, seats_count AS SeatsCount
        FROM reporting.bookings WHERE booking_id = @BookingId
        """;
    private const string BookingCountSql = "SELECT count(*) FROM reporting.bookings";
    private const string SeedTripSql = """
        INSERT INTO reporting.trips (trip_id, operator_id, source, destination, departure_at, seats_total, last_announced_at)
        VALUES (@TripId, 'Sunrise Lines', 'Pune', 'Goa', @At, 40, @At)
        """;

    private readonly DatabaseFixture _database;

    public ApplyBookingEventTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task HandleAsync_Confirmation_HoldsTheBookingForItsOperator()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyBookingEventHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Confirmed(2400), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        BookingRow row = await QueryBookingAsync();
        Assert.Equal("confirmed", row.Status);
        Assert.Equal("Orbit Travels", row.OperatorId);
        Assert.Equal(2400, row.Amount);
        Assert.Equal(2, row.SeatsCount);
    }

    [Fact]
    public async Task HandleAsync_ConfirmationTwice_HoldsOneBooking()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyBookingEventHandler handler = CreateHandler();
        ReceivedEvent confirmation = Confirmed(2400);

        // Act
        await handler.HandleAsync(confirmation, CancellationToken.None);
        await handler.HandleAsync(confirmation, CancellationToken.None);

        // Assert
        Assert.Equal(1, await _database.QueryScalarAsync<int>(BookingCountSql));
        Assert.Equal(2400, (await QueryBookingAsync()).Amount);
    }

    [Fact]
    public async Task HandleAsync_ConfirmationAfterALaterCancellation_KeepsCancelledAndTakesTheFacts()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyBookingEventHandler handler = CreateHandler();
        await handler.HandleAsync(Other("BookingCancelled", _cancelledAt), CancellationToken.None);

        // Act
        await handler.HandleAsync(Confirmed(2400), CancellationToken.None);

        // Assert
        BookingRow row = await QueryBookingAsync();
        Assert.Equal("cancelled", row.Status);
        Assert.Equal(2400, row.Amount);
        Assert.Equal(2, row.SeatsCount);
        Assert.Equal("Orbit Travels", row.OperatorId);
    }

    [Theory]
    [InlineData("BookingFailed", "failed")]
    [InlineData("BookingExpired", "expired")]
    public async Task HandleAsync_FailedAndExpired_HoldNoAmountOrSeats(string eventType, string expectedStatus)
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyBookingEventHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Other(eventType, _cancelledAt), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        BookingRow row = await QueryBookingAsync();
        Assert.Equal(expectedStatus, row.Status);
        Assert.Null(row.Amount);
        Assert.Null(row.SeatsCount);
    }

    [Fact]
    public async Task HandleAsync_CancellationForAKnownTrip_TakesTheTripsOperator()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(SeedTripSql, new { TripId = _tripId, At = _confirmedAt });
        ApplyBookingEventHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(Other("BookingCancelled", _cancelledAt), CancellationToken.None);

        // Assert
        Assert.Equal("Sunrise Lines", (await QueryBookingAsync()).OperatorId);
    }

    [Fact]
    public async Task HandleAsync_ConfirmationWithoutAmount_IsRejected()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyBookingEventHandler handler = CreateHandler();
        string json = $$"""
            {"bookingId":"{{_bookingId}}","operator":{"operatorId":"Orbit Travels"},"trip":{"tripId":"{{_tripId}}"},
             "seats":[{"seatNo":"1A"}],"confirmedAt":"2026-10-31T10:50:00Z"}
            """;

        // Act
        EventOutcome outcome = await handler.HandleAsync(Wrap("BookingConfirmed", json, _confirmedAt), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(EventOutcome.InvalidEventData, outcome.Reason);
        Assert.Equal(0, await _database.QueryScalarAsync<int>(BookingCountSql));
    }

    private ApplyBookingEventHandler CreateHandler()
    {
        IWriterConnectionFactory writerFactory = TestWriters.CreateWriter(_database);
        return new ApplyBookingEventHandler(writerFactory, NullLogger<ApplyBookingEventHandler>.Instance);
    }

    private async Task<BookingRow> QueryBookingAsync()
    {
        return await TestQueries.QuerySingleAsync<BookingRow>(_database, BookingSql, new { BookingId = _bookingId });
    }

    private static ReceivedEvent Confirmed(long amount)
    {
        string json = $$"""
            {"bookingId":"{{_bookingId}}","operator":{"operatorId":"Orbit Travels"},"trip":{"tripId":"{{_tripId}}"},
             "totalAmount":{{amount}},"seats":[{"seatNo":"1A"},{"seatNo":"1B"}],"confirmedAt":"2026-10-31T10:50:00Z","paymentId":"{{Guid.NewGuid()}}"}
            """;
        return Wrap("BookingConfirmed", json, _confirmedAt);
    }

    private static ReceivedEvent Other(string eventType, DateTimeOffset occurredAt)
    {
        string json = $$"""{"bookingId":"{{_bookingId}}","tripId":"{{_tripId}}","seatNos":["1A","1B"],"refundAmount":0}""";
        return Wrap(eventType, json, occurredAt);
    }

    private static ReceivedEvent Wrap(string eventType, string dataJson, DateTimeOffset occurredAt)
    {
        using var document = JsonDocument.Parse(dataJson);
        return new ReceivedEvent
        {
            EventId = "evt-" + eventType + occurredAt.Ticks,
            EventType = eventType,
            OccurredAt = occurredAt,
            CorrelationId = "corr-1",
            Data = document.RootElement.Clone(),
        };
    }

    private sealed class BookingRow
    {
        public string Status { get; set; } = string.Empty;

        public string? OperatorId { get; set; }

        public long? Amount { get; set; }

        public int? SeatsCount { get; set; }
    }
}
