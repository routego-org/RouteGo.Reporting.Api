using System.Globalization;

namespace RouteGo.Reporting.Api.Features.Settlements.RunDailySettlement;

/// <summary>
/// Settles one business day: every operator with confirmed bookings or completed refunds on that day
/// and no settlement yet gets exactly one settlement and one report. The day is named by the trigger
/// or, when it names none, is the business day before the run. A settlement is final once written, so
/// an operator who already has one is left alone and a repeated or overlapping run changes nothing.
/// One operator failing never stops the others; the run is then postponed so a later delivery writes
/// only what is missing.
/// </summary>
public sealed class RunDailySettlementHandler : IEventWorker
{
    /// <summary>The event type the schedule queue's messages are read as.</summary>
    public const string SettlementDueType = "SettlementDue";

    private const string AuditAction = "RunDailySettlement";
    private const string DateFormat = "yyyy-MM-dd";

    // The primary key refuses a second settlement for the pair; an overlapping run waits on the first
    // writer's row and then inserts nothing.
    private const string InsertSettlementSql = """
        INSERT INTO reporting.settlements (settlement_date, operator_id, gross, refunds, net, generated_at)
        VALUES (@Day, @OperatorId, @Gross, @Refunds, @Net, @GeneratedAt)
        ON CONFLICT (settlement_date, operator_id) DO NOTHING
        """;

    private static readonly string[] _eventTypes = { SettlementDueType };

    private readonly IReaderConnectionFactory _readerFactory;
    private readonly IWriterConnectionFactory _writerFactory;
    private readonly SettlementReportWriter _reportWriter;
    private readonly ILogger<RunDailySettlementHandler> _logger;

    public RunDailySettlementHandler(
        IReaderConnectionFactory readerFactory,
        IWriterConnectionFactory writerFactory,
        SettlementReportWriter reportWriter,
        ILogger<RunDailySettlementHandler> logger)
    {
        _readerFactory = readerFactory;
        _writerFactory = writerFactory;
        _reportWriter = reportWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EventTypes
    {
        get { return _eventTypes; }
    }

    /// <summary>Settles the day; Rejected means the trigger named a day that is not a date.</summary>
    public async Task<EventOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        bool hasDay = TryReadDay(receivedEvent, out DateOnly day);
        if (!hasDay)
        {
            return EventOutcome.Rejected($"settlement trigger {receivedEvent.EventId}: settlementDate must be a date as {DateFormat}");
        }

        List<OperatorFigures> pending = await new PendingSettlementReader(_readerFactory).ListAsync(day, cancellationToken);
        int failedCount = 0;
        foreach (OperatorFigures figures in pending)
        {
            bool isWritten = await TrySettleAsync(day, figures, receivedEvent.OccurredAt, cancellationToken);
            if (!isWritten)
            {
                failedCount++;
            }
        }

        _logger.LogInformation(
            "Settlement of {SettlementDate}: {PendingCount} operators needed one, {FailedCount} could not be written",
            day, pending.Count, failedCount);
        if (failedCount > 0)
        {
            return EventOutcome.Postponed("settlementIncomplete", $"{failedCount} of {pending.Count} settlements for {day:yyyy-MM-dd} could not be written");
        }

        return EventOutcome.Applied();
    }

    // A trigger that names no day settles the business day before the one it arrived on.
    private static bool TryReadDay(ReceivedEvent receivedEvent, out DateOnly day)
    {
        JsonElement data = receivedEvent.Data;
        JsonElement member = default;
        bool hasMember = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("settlementDate", out member);
        if (!hasMember)
        {
            day = BusinessDay.Of(receivedEvent.OccurredAt).AddDays(-1);
            return true;
        }

        string? text = member.GetString();
        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    // The failure is reported here, once, with the code the operations alert matches on; the run goes on.
    private async Task<bool> TrySettleAsync(DateOnly day, OperatorFigures figures, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        try
        {
            await SettleAsync(day, figures, generatedAt, cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception, "{Code}: the settlement of operator {OperatorId} for {SettlementDate} could not be written",
                ProblemTypes.SettlementIncomplete.Code, figures.OperatorId, day);
            return false;
        }
    }

    // The row is inserted first and the report written before the commit: no settlement exists without its
    // report, and only the run that inserted the row writes the report, so the two always agree.
    private async Task SettleAsync(DateOnly day, OperatorFigures figures, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer = await _writerFactory.OpenWriteTransactionAsync(AuditAction, cancellationToken);
        var values = new { Day = day, figures.OperatorId, figures.Gross, figures.Refunds, figures.Net, GeneratedAt = generatedAt };
        int inserted = await writer.Connection.ExecuteAsync(new CommandDefinition(
            InsertSettlementSql, values, writer.Transaction, cancellationToken: cancellationToken));
        if (inserted == 0)
        {
            return;
        }

        await _reportWriter.WriteAsync(day, figures, cancellationToken);
        await writer.CommitAsync(cancellationToken);
    }
}
