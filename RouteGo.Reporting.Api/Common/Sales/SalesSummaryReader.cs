namespace RouteGo.Reporting.Api.Common.Sales;

/// <summary>
/// Reads the sales figures for a range of business days from the reporting copy, for one operator or
/// for everyone. The operator summary and the admin summary both read through it, so the two count a
/// booking, a seat and a refund the same way and can never disagree.
/// </summary>
public sealed class SalesSummaryReader
{
    private const string Currency = "INR";

    // Gross counts on the day a booking was confirmed and keeps counting if it is cancelled later; a
    // refund counts on the day it completed. Days come from the calendar, not from the data, so a day
    // with no activity still appears, as zeros. A null operator means every operator.
    private const string SelectDaysSql = """
        WITH days AS (
            SELECT d::date AS day FROM generate_series(@From::date, @To::date, interval '1 day') AS d
        ),
        gross AS (
            SELECT (confirmed_at AT TIME ZONE @Zone)::date AS day,
                   count(*) AS bookings, COALESCE(sum(seats_count), 0) AS seats, COALESCE(sum(amount), 0) AS revenue
            FROM reporting.bookings
            WHERE confirmed_at >= @FromAt AND confirmed_at < @ToAt
              AND (@OperatorId::text IS NULL OR operator_id = @OperatorId)
            GROUP BY 1
        ),
        refunded AS (
            SELECT (refund_completed_at AT TIME ZONE @Zone)::date AS day, COALESCE(sum(refund_amount), 0) AS refunds
            FROM reporting.bookings
            WHERE refund_completed_at >= @FromAt AND refund_completed_at < @ToAt
              AND (@OperatorId::text IS NULL OR operator_id = @OperatorId)
            GROUP BY 1
        )
        SELECT days.day AS Date,
               COALESCE(gross.bookings, 0)::int AS ConfirmedBookings,
               COALESCE(gross.seats, 0)::int AS ConfirmedSeats,
               COALESCE(gross.revenue, 0)::bigint AS Revenue,
               COALESCE(refunded.refunds, 0)::bigint AS Refunds
        FROM days
        LEFT JOIN gross ON gross.day = days.day
        LEFT JOIN refunded ON refunded.day = days.day
        ORDER BY days.day
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public SalesSummaryReader(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    /// <summary>The figures for the days from <paramref name="from"/> to <paramref name="to"/> inclusive; <paramref name="operatorId"/> null reads every operator.</summary>
    public async Task<SalesSummary> ReadAsync(DateOnly from, DateOnly to, string? operatorId, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            From = from,
            To = to,
            FromAt = BusinessDay.StartOf(from),
            ToAt = BusinessDay.EndOf(to),
            Zone = BusinessDay.ZoneName,
            OperatorId = operatorId,
        };
        IEnumerable<DayRow> rows = await _readerFactory.ExecuteReadAsync(
            connection => connection.QueryAsync<DayRow>(new CommandDefinition(SelectDaysSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        return ToSummary(from, to, rows.ToList());
    }

    private static SalesSummary ToSummary(DateOnly from, DateOnly to, List<DayRow> rows)
    {
        var series = new List<SalesDay>();
        foreach (DayRow row in rows)
        {
            series.Add(new SalesDay
            {
                Date = DateOnly.FromDateTime(row.Date),
                ConfirmedBookings = row.ConfirmedBookings,
                ConfirmedSeats = row.ConfirmedSeats,
                Revenue = row.Revenue,
                Refunds = row.Refunds,
                Net = row.Revenue - row.Refunds,
            });
        }

        return new SalesSummary { From = from, To = to, Currency = Currency, Totals = Sum(series), DailySeries = series };
    }

    private static SalesTotals Sum(List<SalesDay> series)
    {
        long revenue = series.Sum(day => day.Revenue);
        long refunds = series.Sum(day => day.Refunds);
        return new SalesTotals
        {
            ConfirmedBookings = series.Sum(day => day.ConfirmedBookings),
            ConfirmedSeats = series.Sum(day => day.ConfirmedSeats),
            Revenue = revenue,
            Refunds = refunds,
            Net = revenue - refunds,
        };
    }

    private sealed class DayRow
    {
        public DateTime Date { get; set; }

        public int ConfirmedBookings { get; set; }

        public int ConfirmedSeats { get; set; }

        public long Revenue { get; set; }

        public long Refunds { get; set; }
    }
}
