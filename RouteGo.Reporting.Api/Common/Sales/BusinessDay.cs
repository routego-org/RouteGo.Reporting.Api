namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>
/// The business day: a calendar day in India Standard Time. Every date Reporting accepts or shows is
/// one, so the instants a day spans are fixed here once. India observes no daylight saving, so the
/// day always starts at the same offset.
/// </summary>
public static class BusinessDay
{
    /// <summary>The database's name for the zone, for grouping stored instants into days.</summary>
    public const string ZoneName = "Asia/Kolkata";

    private static readonly TimeSpan _offset = new TimeSpan(5, 30, 0);

    /// <summary>The business day an instant falls on.</summary>
    public static DateOnly Of(DateTimeOffset instant)
    {
        DateTimeOffset local = instant.ToOffset(_offset);
        return DateOnly.FromDateTime(local.DateTime);
    }

    /// <summary>The instant the business day begins, inclusive, in UTC: the database driver accepts no other offset.</summary>
    public static DateTimeOffset StartOf(DateOnly day)
    {
        var local = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), _offset);
        return local.ToUniversalTime();
    }

    /// <summary>The instant the next business day begins, so a day spans [StartOf, EndOf).</summary>
    public static DateTimeOffset EndOf(DateOnly day)
    {
        return StartOf(day.AddDays(1));
    }
}
