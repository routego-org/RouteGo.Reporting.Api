using Amazon.SQS.Model;

namespace RouteGo.Reporting.Api.Features.Settlements.RunDailySettlement;

/// <summary>
/// Reads the schedule queue for as long as the API runs. The nightly schedule is not a RouteGo service,
/// so its message has no RouteGo envelope: any message is a settlement trigger, and its body may name the
/// day to settle (<c>{"settlementDate":"2026-10-02"}</c>) for a manual re-run. Several running copies
/// may receive triggers; the settlement's own key makes an overlapping run harmless.
/// </summary>
public sealed class SettlementScheduleQueueService : EventQueueService
{
    private readonly RunDailySettlementHandler _handler;
    private readonly IClock _clock;

    public SettlementScheduleQueueService(
        IAmazonSQS sqs, EventQueue queue, RunDailySettlementHandler handler, IClock clock, ILogger<SettlementScheduleQueueService> logger)
        : base(sqs, queue, logger)
    {
        _handler = handler;
        _clock = clock;
    }

    protected override EventEnvelopeReading ReadMessage(Message message)
    {
        JsonElement data = ReadBody(message.Body);
        var receivedEvent = new ReceivedEvent
        {
            EventId = message.MessageId,
            EventType = RunDailySettlementHandler.SettlementDueType,
            OccurredAt = _clock.UtcNow,
            CorrelationId = message.MessageId,
            Data = data,
        };
        return new EventEnvelopeReading(receivedEvent, null);
    }

    protected override async Task<EventOutcome> HandleEventAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        return await _handler.HandleAsync(receivedEvent, cancellationToken);
    }

    // A body that is not a JSON object (an empty schedule payload, say) simply names no day.
    private static JsonElement ReadBody(string? body)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "{}");
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }
}
