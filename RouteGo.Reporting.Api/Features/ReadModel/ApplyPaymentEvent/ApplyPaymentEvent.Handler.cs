namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyPaymentEvent;

/// <summary>
/// Records Payment's outcomes in Reporting's copy. A succeeded or failed payment is held by its own
/// identifier and never changes a booking's revenue. A completed refund is recorded on its booking at
/// the time it completed, once: a repeat or a second refund leaves the first in place. A refund of
/// nothing completes no refund and records nothing.
/// </summary>
public sealed class ApplyPaymentEventHandler : IEventWorker
{
    private const string AuditAction = "ApplyPaymentEvent";

    // A payment's outcome moves only to one that happened later; its amount is kept once known.
    private const string MergePaymentSql = """
        INSERT INTO reporting.payments (payment_id, booking_id, amount, status, occurred_at)
        VALUES (@PaymentId, @BookingId, @Amount, @Status, @OccurredAt)
        ON CONFLICT (payment_id) DO UPDATE
        SET amount = COALESCE(reporting.payments.amount, EXCLUDED.amount),
            status = CASE WHEN reporting.payments.occurred_at < EXCLUDED.occurred_at
                          THEN EXCLUDED.status ELSE reporting.payments.status END,
            occurred_at = GREATEST(reporting.payments.occurred_at, EXCLUDED.occurred_at)
        """;

    // A refund implies the booking was cancelled, so a refund that arrives before any booking event
    // starts the copy as cancelled; later booking events then fill in the rest.
    private const string RecordRefundSql = """
        INSERT INTO reporting.bookings (booking_id, status, refund_amount, refund_completed_at, status_changed_at)
        VALUES (@BookingId, 'cancelled', @Amount, @OccurredAt, @OccurredAt)
        ON CONFLICT (booking_id) DO UPDATE
        SET refund_amount = COALESCE(reporting.bookings.refund_amount, EXCLUDED.refund_amount),
            refund_completed_at = COALESCE(reporting.bookings.refund_completed_at, EXCLUDED.refund_completed_at)
        """;

    private static readonly string[] _eventTypes =
    {
        PaymentEventReader.SucceededType,
        PaymentEventReader.FailedType,
        PaymentEventReader.RefundCompletedType,
    };

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<ApplyPaymentEventHandler> _logger;

    public ApplyPaymentEventHandler(IWriterConnectionFactory writerFactory, ILogger<ApplyPaymentEventHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EventTypes
    {
        get { return _eventTypes; }
    }

    /// <summary>Applies one payment or refund event; Rejected means its data is unusable.</summary>
    public async Task<EventOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        PaymentEventReading reading = PaymentEventReader.Read(receivedEvent);
        if (reading.Message is null)
        {
            return EventOutcome.Rejected($"{receivedEvent.EventType} event {receivedEvent.EventId}: {reading.Problem}");
        }

        PaymentEventMessage message = reading.Message;
        bool isRefund = message.EventType == PaymentEventReader.RefundCompletedType;
        if (isRefund && message.Amount == 0)
        {
            return EventOutcome.Applied();
        }

        await WriteAsync(message, isRefund, cancellationToken);
        _logger.LogInformation(
            "Booking {BookingId} updated from {EventType} (correlation {CorrelationId})",
            message.BookingId, message.EventType, receivedEvent.CorrelationId);
        return EventOutcome.Applied();
    }

    private async Task WriteAsync(PaymentEventMessage message, bool isRefund, CancellationToken cancellationToken)
    {
        string sql = MergePaymentSql;
        if (isRefund)
        {
            sql = RecordRefundSql;
        }

        string status = "failed";
        if (message.EventType == PaymentEventReader.SucceededType)
        {
            status = "succeeded";
        }

        var values = new { message.BookingId, message.PaymentId, message.Amount, message.OccurredAt, Status = status };
        await using IWriterConnection writer = await _writerFactory.OpenWriteTransactionAsync(AuditAction, cancellationToken);
        await writer.Connection.ExecuteAsync(new CommandDefinition(sql, values, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
    }
}
