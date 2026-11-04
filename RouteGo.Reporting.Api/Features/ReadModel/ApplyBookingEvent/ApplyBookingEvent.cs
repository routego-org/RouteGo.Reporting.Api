namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyBookingEvent;

/// <summary>The trip a confirmed booking names.</summary>
internal sealed record BookingTripData
{
    public string? TripId { get; init; }
}

/// <summary>The operator a confirmed booking names.</summary>
internal sealed record BookingOperatorData
{
    public string? OperatorId { get; init; }
}

/// <summary>
/// The data of Booking's four booking events, read as they came: every member is optional so the
/// reader can say what is wrong with an event instead of the deserializer guessing. A confirmation
/// names its trip and operator in objects; the other three carry a plain trip identifier. Booking also
/// sends passengers, seats and refund details, which Reporting does not keep.
/// </summary>
internal sealed record BookingEventData
{
    public string? BookingId { get; init; }

    public string? TripId { get; init; }

    public BookingTripData? Trip { get; init; }

    public BookingOperatorData? Operator { get; init; }

    public decimal? TotalAmount { get; init; }

    public JsonElement? Seats { get; init; }

    public DateTimeOffset? ConfirmedAt { get; init; }
}

/// <summary>What one booking event says about the booking, checked and ready to merge into its copy.</summary>
internal sealed record BookingEventMessage
{
    public required Guid BookingId { get; init; }

    public required string Status { get; init; }

    /// <summary>When the thing the event reports happened; "later state wins" compares this.</summary>
    public required DateTimeOffset ChangedAt { get; init; }

    public Guid? TripId { get; init; }

    public string? OperatorId { get; init; }

    public long? Amount { get; init; }

    public int? SeatsCount { get; init; }

    public DateTimeOffset? ConfirmedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }
}

/// <summary>The checked message, or a problem naming everything that is missing or unusable.</summary>
internal sealed record BookingEventReading(BookingEventMessage? Message, string? Problem);

/// <summary>Reads and checks the data of a booking event of any of the four kinds.</summary>
internal static class BookingEventReader
{
    public const string ConfirmedType = "BookingConfirmed";
    public const string CancelledType = "BookingCancelled";
    public const string FailedType = "BookingFailed";
    public const string ExpiredType = "BookingExpired";

    private const int MaxOperatorIdLength = 64;

    /// <summary>The checked message, or null with a problem naming every member that is missing or unusable.</summary>
    public static BookingEventReading Read(ReceivedEvent receivedEvent)
    {
        BookingEventData? raw = Deserialize(receivedEvent.Data);
        if (raw is null)
        {
            return new BookingEventReading(null, "the data is not a booking object");
        }

        var problems = new List<string>();
        bool hasBookingId = Guid.TryParse(raw.BookingId, out Guid bookingId);
        if (!hasBookingId)
        {
            problems.Add($"bookingId (got {raw.BookingId})");
        }

        BookingEventMessage message;
        if (receivedEvent.EventType == ConfirmedType)
        {
            message = ReadConfirmed(raw, bookingId, problems);
        }
        else
        {
            message = ReadOther(raw, receivedEvent, bookingId, problems);
        }

        if (problems.Count > 0)
        {
            return new BookingEventReading(null, "unusable " + string.Join(", ", problems));
        }

        return new BookingEventReading(message, null);
    }

    private static BookingEventMessage ReadConfirmed(BookingEventData raw, Guid bookingId, List<string> problems)
    {
        bool hasTripId = Guid.TryParse(raw.Trip?.TripId, out Guid tripId);
        if (!hasTripId)
        {
            problems.Add($"trip.tripId (got {raw.Trip?.TripId})");
        }

        long? amount = ReadAmount(raw, problems);
        int seatsCount = CountSeats(raw.Seats);
        if (seatsCount == 0)
        {
            problems.Add("seats");
        }

        if (raw.ConfirmedAt is null)
        {
            problems.Add("confirmedAt");
        }

        return new BookingEventMessage
        {
            BookingId = bookingId,
            Status = "confirmed",
            ChangedAt = raw.ConfirmedAt.GetValueOrDefault(),
            TripId = tripId,
            OperatorId = ReadOperatorId(raw, problems),
            Amount = amount,
            SeatsCount = seatsCount,
            ConfirmedAt = raw.ConfirmedAt,
        };
    }

    private static string? ReadOperatorId(BookingEventData raw, List<string> problems)
    {
        string? operatorId = raw.Operator?.OperatorId;
        if (string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > MaxOperatorIdLength)
        {
            problems.Add("operator.operatorId");
        }

        return operatorId;
    }

    // Money is whole rupees: an amount with paise is a producer fault, not something to round.
    private static long? ReadAmount(BookingEventData raw, List<string> problems)
    {
        decimal? total = raw.TotalAmount;
        bool isWholeAmount = total is not null && total >= 0 && total == decimal.Truncate(total.Value);
        if (!isWholeAmount)
        {
            problems.Add($"totalAmount (got {total})");
            return null;
        }

        return (long)total!.Value;
    }

    private static BookingEventMessage ReadOther(BookingEventData raw, ReceivedEvent receivedEvent, Guid bookingId, List<string> problems)
    {
        bool hasTripId = Guid.TryParse(raw.TripId, out Guid tripId);
        if (!hasTripId)
        {
            problems.Add($"tripId (got {raw.TripId})");
        }

        string status = StatusOf(receivedEvent.EventType);
        DateTimeOffset? cancelledAt = null;
        if (status == "cancelled")
        {
            cancelledAt = receivedEvent.OccurredAt;
        }

        return new BookingEventMessage
        {
            BookingId = bookingId,
            Status = status,
            ChangedAt = receivedEvent.OccurredAt,
            TripId = tripId,
            CancelledAt = cancelledAt,
        };
    }

    private static string StatusOf(string eventType)
    {
        switch (eventType)
        {
            case CancelledType:
                return "cancelled";
            case FailedType:
                return "failed";
            default:
                return "expired";
        }
    }

    private static int CountSeats(JsonElement? seats)
    {
        if (seats is null || seats.Value.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return seats.Value.GetArrayLength();
    }

    private static BookingEventData? Deserialize(JsonElement data)
    {
        try
        {
            return data.Deserialize<BookingEventData>(WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
