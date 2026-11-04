namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>One business day's sales figures, in whole rupees.</summary>
public sealed class SalesDay
{
    public required DateOnly Date { get; init; }

    public required int ConfirmedBookings { get; init; }

    public required int ConfirmedSeats { get; init; }

    public required long Revenue { get; init; }

    public required long Refunds { get; init; }

    public required long Net { get; init; }
}
