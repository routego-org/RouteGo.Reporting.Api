namespace RouteGo.Reporting.Api.Tests.Support;

/// <summary>The facts of one seeded booking; only what a test cares about is set.</summary>
public sealed class BookingSeed
{
    public required string OperatorId { get; init; }

    public required DateTimeOffset ConfirmedAt { get; init; }

    public Guid? TripId { get; init; }

    public string Status { get; init; } = "confirmed";

    public long Amount { get; init; } = 1000;

    public int SeatsCount { get; init; } = 1;

    public long? RefundAmount { get; init; }

    public DateTimeOffset? RefundCompletedAt { get; init; }
}
