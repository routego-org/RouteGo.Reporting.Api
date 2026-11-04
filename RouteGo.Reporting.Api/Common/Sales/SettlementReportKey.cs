namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>
/// Where a settlement's report is kept in the reports bucket. Every settlement has exactly one report,
/// so the location is worked out from the settlement's identity rather than stored: the run that writes
/// the report and the download that links to it cannot disagree.
/// </summary>
public static class SettlementReportKey
{
    /// <summary>The object key for one operator's report on one settlement date.</summary>
    public static string For(DateOnly settlementDate, string operatorId)
    {
        // The operator id is escaped so no character in it can add a path segment to the key.
        string escapedOperator = Uri.EscapeDataString(operatorId);
        return $"settlement/{settlementDate:yyyy-MM-dd}/{escapedOperator}.csv";
    }
}
