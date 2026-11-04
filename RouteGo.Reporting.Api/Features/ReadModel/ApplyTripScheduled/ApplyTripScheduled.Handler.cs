namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyTripScheduled;

/// <summary>
/// Keeps Reporting's copy of a trip from Fleet's TripScheduled event: the first announcement stores the
/// trip, a later one replaces its details, and an older one that arrives afterwards changes nothing.
/// Bookings held for the trip before it was known take its operator in the same transaction.
/// </summary>
public sealed class ApplyTripScheduledHandler : IEventWorker
{
    private const string AuditAction = "ApplyTripScheduled";
    private const string TripScheduledType = "TripScheduled";

    private const string UpsertTripSql = """
        INSERT INTO reporting.trips (trip_id, operator_id, source, destination, departure_at, seats_total, last_announced_at)
        VALUES (@TripId, @OperatorId, @Source, @Destination, @DepartureAt, @SeatsTotal, @AnnouncedAt)
        ON CONFLICT (trip_id) DO UPDATE
        SET operator_id = EXCLUDED.operator_id,
            source = EXCLUDED.source,
            destination = EXCLUDED.destination,
            departure_at = EXCLUDED.departure_at,
            seats_total = EXCLUDED.seats_total,
            last_announced_at = EXCLUDED.last_announced_at
        WHERE reporting.trips.last_announced_at < EXCLUDED.last_announced_at
        """;

    private const string AttributeHeldBookingsSql = """
        UPDATE reporting.bookings
        SET operator_id = @OperatorId
        WHERE trip_id = @TripId AND operator_id IS NULL
        """;

    private static readonly string[] _eventTypes = { TripScheduledType };

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<ApplyTripScheduledHandler> _logger;

    public ApplyTripScheduledHandler(IWriterConnectionFactory writerFactory, ILogger<ApplyTripScheduledHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EventTypes
    {
        get { return _eventTypes; }
    }

    /// <summary>Applies one TripScheduled event; Rejected means its data is unusable.</summary>
    public async Task<EventOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        TripScheduledReading reading = TripScheduledReader.Read(receivedEvent.Data);
        if (reading.Message is null)
        {
            return EventOutcome.Rejected($"TripScheduled event {receivedEvent.EventId}: {reading.Problem}");
        }

        TripScheduledMessage trip = reading.Message;
        await using IWriterConnection writer = await _writerFactory.OpenWriteTransactionAsync(AuditAction, cancellationToken);
        var tripValues = new
        {
            trip.TripId,
            trip.OperatorId,
            trip.Source,
            trip.Destination,
            trip.DepartureAt,
            trip.SeatsTotal,
            AnnouncedAt = receivedEvent.OccurredAt,
        };
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            UpsertTripSql, tripValues, writer.Transaction, cancellationToken: cancellationToken));
        int attributed = await writer.Connection.ExecuteAsync(new CommandDefinition(
            AttributeHeldBookingsSql, new { trip.TripId, trip.OperatorId }, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Trip {TripId} of operator {OperatorId} recorded; {AttributedBookings} held bookings attributed (correlation {CorrelationId})",
            trip.TripId, trip.OperatorId, attributed, receivedEvent.CorrelationId);
        return EventOutcome.Applied();
    }
}
