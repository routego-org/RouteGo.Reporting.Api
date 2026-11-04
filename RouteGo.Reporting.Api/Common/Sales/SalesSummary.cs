namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>Totals and a day-by-day series for a range of business days. The shape both the operator and the admin summary answer with.</summary>
public sealed class SalesSummary
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>Always INR.</summary>
    public required string Currency { get; init; }

    public required SalesTotals Totals { get; init; }

    /// <summary>One entry per business day in the range, including days with no activity.</summary>
    public required IReadOnlyList<SalesDay> DailySeries { get; init; }
}
