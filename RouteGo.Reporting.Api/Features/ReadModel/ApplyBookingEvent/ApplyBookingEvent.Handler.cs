namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyBookingEvent;

/// <summary>
/// Merges Booking's confirmed, cancelled, failed and expired events into Reporting's copy of the booking.
/// Each event adds the facts it carries and removes none; the booking's status moves only to a state that
/// happened later than the one it holds, so a late older event never undoes a newer one; and the booking
/// takes the operator of its trip when the event does not name one. A repeat changes nothing.
/// </summary>
public sealed class ApplyBookingEventHandler : IEventWorker
{
    private const string AuditAction = "ApplyBookingEvent";

    private const string MergeBookingSql = """
        INSERT INTO reporting.bookings
            (booking_id, trip_id, operator_id, status, amount, seats_count, confirmed_at, cancelled_at, status_changed_at)
        VALUES
            (@BookingId, @TripId,
             COALESCE(@OperatorId, (SELECT operator_id FROM reporting.trips WHERE trip_id = @TripId)),
             @Status, @Amount, @SeatsCount, @ConfirmedAt, @CancelledAt, @ChangedAt)
        ON CONFLICT (booking_id) DO UPDATE
        SET trip_id = COALESCE(reporting.bookings.trip_id, EXCLUDED.trip_id),
            operator_id = COALESCE(reporting.bookings.operator_id, EXCLUDED.operator_id),
            amount = COALESCE(reporting.bookings.amount, EXCLUDED.amount),
            seats_count = COALESCE(reporting.bookings.seats_count, EXCLUDED.seats_count),
            confirmed_at = COALESCE(reporting.bookings.confirmed_at, EXCLUDED.confirmed_at),
            cancelled_at = COALESCE(reporting.bookings.cancelled_at, EXCLUDED.cancelled_at),
            status = CASE WHEN reporting.bookings.status_changed_at < EXCLUDED.status_changed_at
                          THEN EXCLUDED.status ELSE reporting.bookings.status END,
            status_changed_at = GREATEST(reporting.bookings.status_changed_at, EXCLUDED.status_changed_at)
        """;

    private static readonly string[] _eventTypes =
    {
        BookingEventReader.ConfirmedType,
        BookingEventReader.CancelledType,
        BookingEventReader.FailedType,
        BookingEventReader.ExpiredType,
    };

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<ApplyBookingEventHandler> _logger;

    public ApplyBookingEventHandler(IWriterConnectionFactory writerFactory, ILogger<ApplyBookingEventHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EventTypes
    {
        get { return _eventTypes; }
    }

    /// <summary>Applies one booking event; Rejected means its data is unusable.</summary>
    public async Task<EventOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        BookingEventReading reading = BookingEventReader.Read(receivedEvent);
        if (reading.Message is null)
        {
            return EventOutcome.Rejected($"{receivedEvent.EventType} event {receivedEvent.EventId}: {reading.Problem}");
        }

        await using IWriterConnection writer = await _writerFactory.OpenWriteTransactionAsync(AuditAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            MergeBookingSql, reading.Message, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Booking {BookingId} merged from {EventType} (correlation {CorrelationId})",
            reading.Message.BookingId, receivedEvent.EventType, receivedEvent.CorrelationId);
        return EventOutcome.Applied();
    }
}
