namespace RouteGo.Reporting.Api.Features.Settlements.ListSettlements;

/// <summary>Lists what the nightly run wrote for one settlement date, for every operator.</summary>
public sealed class ListSettlementsHandler
{
    private const string Currency = "INR";

    private const string SelectSettlementsSql = """
        SELECT settlement_date AS SettlementDate, operator_id AS OperatorId, gross AS Gross, refunds AS Refunds,
               net AS Net, generated_at AS GeneratedAt
        FROM reporting.settlements
        WHERE settlement_date = @Day
        ORDER BY operator_id
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public ListSettlementsHandler(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    /// <summary>The settlements of a validated day, ordered by operator.</summary>
    public async Task<SettlementList> HandleAsync(ListSettlementsRequest request, CancellationToken cancellationToken)
    {
        var parameters = new { Day = request.Date!.Value };
        IEnumerable<SettlementRow> rows = await _readerFactory.ExecuteReadAsync(
            connection => connection.QueryAsync<SettlementRow>(new CommandDefinition(SelectSettlementsSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        return ToList(rows.ToList());
    }

    private static SettlementList ToList(List<SettlementRow> rows)
    {
        var items = new List<SettlementSummary>();
        foreach (SettlementRow row in rows)
        {
            items.Add(new SettlementSummary
            {
                SettlementDate = DateOnly.FromDateTime(row.SettlementDate),
                OperatorId = row.OperatorId,
                Gross = row.Gross,
                Refunds = row.Refunds,
                Net = row.Net,
                Currency = Currency,
                GeneratedAt = UtcTimes.ToOffset(row.GeneratedAt),
            });
        }

        return new SettlementList { Items = items, TotalCount = items.Count };
    }

    private sealed class SettlementRow
    {
        public DateTime SettlementDate { get; set; }

        public string OperatorId { get; set; } = string.Empty;

        public long Gross { get; set; }

        public long Refunds { get; set; }

        public long Net { get; set; }

        public DateTime GeneratedAt { get; set; }
    }
}
