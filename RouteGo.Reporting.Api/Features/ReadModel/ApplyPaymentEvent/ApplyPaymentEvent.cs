namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyPaymentEvent;

/// <summary>
/// The data of Payment's three events, read as they came: every member is optional so the reader can
/// say what is wrong with an event instead of the deserializer guessing. Payment also sends the failure
/// reason, the refund reason and the customer's email, which Reporting does not keep.
/// </summary>
internal sealed record PaymentEventData
{
    public string? BookingId { get; init; }

    public string? PaymentId { get; init; }

    public decimal? Amount { get; init; }
}

/// <summary>What one payment event says, checked and ready to apply.</summary>
internal sealed record PaymentEventMessage
{
    public required Guid BookingId { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public Guid? PaymentId { get; init; }

    public long? Amount { get; init; }
}

/// <summary>The checked message, or a problem naming everything that is missing or unusable.</summary>
internal sealed record PaymentEventReading(PaymentEventMessage? Message, string? Problem);

/// <summary>Reads and checks the data of a payment or refund event.</summary>
internal static class PaymentEventReader
{
    public const string SucceededType = "PaymentSucceeded";
    public const string FailedType = "PaymentFailed";
    public const string RefundCompletedType = "RefundCompleted";

    /// <summary>The checked message, or null with a problem naming every member that is missing or unusable.</summary>
    public static PaymentEventReading Read(ReceivedEvent receivedEvent)
    {
        PaymentEventData? raw = Deserialize(receivedEvent.Data);
        if (raw is null)
        {
            return new PaymentEventReading(null, "the data is not a payment object");
        }

        var problems = new List<string>();
        bool hasBookingId = Guid.TryParse(raw.BookingId, out Guid bookingId);
        if (!hasBookingId)
        {
            problems.Add($"bookingId (got {raw.BookingId})");
        }

        bool hasPaymentId = Guid.TryParse(raw.PaymentId, out Guid paymentId);
        if (receivedEvent.EventType != RefundCompletedType && !hasPaymentId)
        {
            problems.Add($"paymentId (got {raw.PaymentId})");
        }

        bool isAmountRequired = receivedEvent.EventType != FailedType;
        bool isWholeAmount = raw.Amount is not null && raw.Amount >= 0 && raw.Amount == decimal.Truncate(raw.Amount.Value);
        if (isAmountRequired && !isWholeAmount)
        {
            problems.Add($"amount (got {raw.Amount})");
        }

        if (problems.Count > 0)
        {
            return new PaymentEventReading(null, "unusable " + string.Join(", ", problems));
        }

        var message = new PaymentEventMessage
        {
            BookingId = bookingId,
            EventType = receivedEvent.EventType,
            OccurredAt = receivedEvent.OccurredAt,
            PaymentId = hasPaymentId ? paymentId : null,
            Amount = isWholeAmount ? (long)raw.Amount!.Value : null,
        };
        return new PaymentEventReading(message, null);
    }

    private static PaymentEventData? Deserialize(JsonElement data)
    {
        try
        {
            return data.Deserialize<PaymentEventData>(WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
