using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace RouteGo.Reporting.Api.Features.Settlements.RunDailySettlement;

[Collection(DatabaseCollection.Name)]
public sealed class RunDailySettlementTests
{
    // 01:00 on 3 October in India, the moment the nightly run fires.
    private static readonly DateTimeOffset _runAt = new DateTimeOffset(2026, 10, 2, 19, 30, 0, TimeSpan.Zero);

    // 10:00 India Standard Time on the 2nd, 3rd and 4th.
    private static readonly DateTimeOffset _october2 = new DateTimeOffset(2026, 10, 2, 4, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _october3 = _october2.AddDays(1);
    private static readonly DateTimeOffset _october4 = _october2.AddDays(2);

    private const string SettlementSql = """
        SELECT operator_id AS OperatorId, gross, refunds, net
        FROM reporting.settlements WHERE settlement_date = @Day ORDER BY operator_id
        """;
    private const string SettlementCountSql = "SELECT count(*) FROM reporting.settlements";

    private readonly DatabaseFixture _database;

    public RunDailySettlementTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task HandleAsync_NightlyRun_SettlesEachOperatorWithActivityForThePreviousDay()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedAsync("Orbit Travels", _october2, 8400, _october2, 1000);
        await SeedAsync("Sunrise Lines", _october2, 5000);
        await SeedAsync("Dormant Tours", _october3, 700);
        var reports = new RecordingReportWriter();
        RunDailySettlementHandler handler = CreateHandler(reports);

        // Act
        EventOutcome outcome = await handler.HandleAsync(Trigger("{}"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        IReadOnlyList<SettlementRow> rows = await QueryAsync(new DateOnly(2026, 10, 2));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new SettlementRow("Orbit Travels", 8400, 1000, 7400), rows[0]);
        Assert.Equal(new SettlementRow("Sunrise Lines", 5000, 0, 5000), rows[1]);
        Assert.Equal(2, reports.Written.Count);
    }

    [Fact]
    public async Task HandleAsync_BookingConfirmedAfterMidnightInIndia_BelongsToTheNextDay()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        var lateUtc = new DateTimeOffset(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);
        var earlyUtc = new DateTimeOffset(2026, 10, 2, 17, 0, 0, TimeSpan.Zero);
        await SeedAsync("Orbit Travels", lateUtc, 3000);
        await SeedAsync("Orbit Travels", earlyUtc, 1500);
        RunDailySettlementHandler handler = CreateHandler(new RecordingReportWriter());

        // Act
        await handler.HandleAsync(Trigger("{}"), CancellationToken.None);

        // Assert
        IReadOnlyList<SettlementRow> rows = await QueryAsync(new DateOnly(2026, 10, 2));
        Assert.Equal(1500, rows[0].Gross);
    }

    [Fact]
    public async Task HandleAsync_RefundCompletedLater_CountsOnItsCompletionDayOnly()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedAsync("Orbit Travels", _october2, 2400, _october4, 2160);
        RunDailySettlementHandler handler = CreateHandler(new RecordingReportWriter());

        // Act
        await handler.HandleAsync(Trigger("""{"settlementDate":"2026-10-02"}"""), CancellationToken.None);
        await handler.HandleAsync(Trigger("""{"settlementDate":"2026-10-04"}"""), CancellationToken.None);

        // Assert
        Assert.Equal(new SettlementRow("Orbit Travels", 2400, 0, 2400), (await QueryAsync(new DateOnly(2026, 10, 2)))[0]);
        Assert.Equal(new SettlementRow("Orbit Travels", 0, 2160, -2160), (await QueryAsync(new DateOnly(2026, 10, 4)))[0]);
    }

    [Fact]
    public async Task HandleAsync_SecondRunAfterALateConfirmation_ChangesNothing()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedAsync("Orbit Travels", _october2, 8400);
        var reports = new RecordingReportWriter();
        RunDailySettlementHandler handler = CreateHandler(reports);
        await handler.HandleAsync(Trigger("{}"), CancellationToken.None);
        await SeedAsync("Orbit Travels", _october2, 1200);

        // Act
        EventOutcome outcome = await handler.HandleAsync(Trigger("{}"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(8400, (await QueryAsync(new DateOnly(2026, 10, 2)))[0].Gross);
        Assert.Single(reports.Written);
    }

    [Fact]
    public async Task HandleAsync_OneOperatorFailing_SettlesTheOthersAndPostponesTheRun()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedAsync("Orbit Travels", _october2, 8400);
        await SeedAsync("Sunrise Lines", _october2, 5000);
        var reports = new RecordingReportWriter { FailFor = "Orbit Travels" };
        RunDailySettlementHandler handler = CreateHandler(reports);

        // Act
        EventOutcome outcome = await handler.HandleAsync(Trigger("{}"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Postponed, outcome.Kind);
        IReadOnlyList<SettlementRow> rows = await QueryAsync(new DateOnly(2026, 10, 2));
        Assert.Single(rows);
        Assert.Equal("Sunrise Lines", rows[0].OperatorId);
    }

    [Fact]
    public async Task HandleAsync_RerunAfterAFailure_WritesOnlyTheMissingSettlement()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedAsync("Orbit Travels", _october2, 8400);
        await SeedAsync("Sunrise Lines", _october2, 5000);
        var reports = new RecordingReportWriter { FailFor = "Orbit Travels" };
        RunDailySettlementHandler handler = CreateHandler(reports);
        await handler.HandleAsync(Trigger("{}"), CancellationToken.None);
        reports.FailFor = null;

        // Act
        EventOutcome outcome = await handler.HandleAsync(Trigger("{}"), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(2, await _database.QueryScalarAsync<int>(SettlementCountSql));
        Assert.Equal(new[] { "Sunrise Lines", "Orbit Travels" }, reports.Written);
    }

    [Fact]
    public async Task HandleAsync_TriggerNamingANonDate_IsRejected()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        RunDailySettlementHandler handler = CreateHandler(new RecordingReportWriter());

        // Act
        EventOutcome outcome = await handler.HandleAsync(Trigger("""{"settlementDate":"yesterday"}"""), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(EventOutcome.InvalidEventData, outcome.Reason);
    }

    private RunDailySettlementHandler CreateHandler(SettlementReportWriter reports)
    {
        return new RunDailySettlementHandler(
            TestWriters.CreateReader(_database),
            TestWriters.CreateWriter(_database),
            reports,
            NullLogger<RunDailySettlementHandler>.Instance);
    }

    private async Task SeedAsync(string operatorId, DateTimeOffset confirmedAt, long amount, DateTimeOffset? refundAt = null, long? refund = null)
    {
        var seed = new BookingSeed
        {
            OperatorId = operatorId,
            ConfirmedAt = confirmedAt,
            Amount = amount,
            RefundAmount = refund,
            RefundCompletedAt = refundAt,
        };
        await ReportSeed.BookingAsync(_database, seed);
    }

    private async Task<IReadOnlyList<SettlementRow>> QueryAsync(DateOnly day)
    {
        return await TestQueries.QueryAsync<SettlementRow>(_database, SettlementSql, new { Day = day });
    }

    private static ReceivedEvent Trigger(string dataJson)
    {
        using var document = JsonDocument.Parse(dataJson);
        return new ReceivedEvent
        {
            EventId = "trigger-1",
            EventType = RunDailySettlementHandler.SettlementDueType,
            OccurredAt = _runAt,
            CorrelationId = "trigger-1",
            Data = document.RootElement.Clone(),
        };
    }

    public sealed record SettlementRow(string OperatorId, long Gross, long Refunds, long Net);

    private sealed class RecordingReportWriter : SettlementReportWriter
    {
        public RecordingReportWriter()
            : base(null!, "test-bucket")
        {
        }

        public List<string> Written { get; } = new List<string>();

        public string? FailFor { get; set; }

        public override Task WriteAsync(DateOnly settlementDate, OperatorFigures figures, CancellationToken cancellationToken)
        {
            if (figures.OperatorId == FailFor)
            {
                throw new InvalidOperationException("the bucket is unreachable");
            }

            Written.Add(figures.OperatorId);
            return Task.CompletedTask;
        }
    }
}
