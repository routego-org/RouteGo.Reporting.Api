namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>Sales figures for a period, in whole rupees.</summary>
public sealed class SalesTotals
{
    /// <summary>Bookings confirmed in the period, including any cancelled later.</summary>
    public required int ConfirmedBookings { get; init; }

    /// <summary>Seats on those bookings.</summary>
    public required int ConfirmedSeats { get; init; }

    /// <summary>Gross: the sum of the amounts of the bookings confirmed in the period.</summary>
    public required long Revenue { get; init; }

    /// <summary>The sum of the refunds completed in the period.</summary>
    public required long Refunds { get; init; }

    /// <summary>Revenue minus refunds.</summary>
    public required long Net { get; init; }
}
