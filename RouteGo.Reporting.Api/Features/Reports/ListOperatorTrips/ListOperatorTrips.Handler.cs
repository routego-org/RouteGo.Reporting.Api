namespace RouteGo.Reporting.Api.Features.Reports.ListOperatorTrips;

/// <summary>Lists one operator's trips departing on a business day with the seats sold on each.</summary>
public sealed class ListOperatorTripsHandler
{
    // Seats sold counts only bookings confirmed now, so a cancelled booking frees its seats. The
    // operator is a WHERE clause, so another operator's trips are never read.
    private const string SelectTripsSql = """
        SELECT t.trip_id AS TripId, t.source AS Source, t.destination AS Destination, t.departure_at AS DepartureAt,
               COALESCE(sum(b.seats_count) FILTER (WHERE b.status = 'confirmed'), 0)::int AS SeatsSold,
               t.seats_total AS SeatsTotal
        FROM reporting.trips t
        LEFT JOIN reporting.bookings b ON b.trip_id = t.trip_id
        WHERE t.operator_id = @OperatorId AND t.departure_at >= @FromAt AND t.departure_at < @ToAt
        GROUP BY t.trip_id
        ORDER BY t.departure_at, t.trip_id
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public ListOperatorTripsHandler(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    /// <summary>The operator's trips for a validated day.</summary>
    public async Task<OperatorTripList> HandleAsync(ListOperatorTripsRequest request, string operatorId, CancellationToken cancellationToken)
    {
        DateOnly day = request.Date!.Value;
        var parameters = new { OperatorId = operatorId, FromAt = BusinessDay.StartOf(day), ToAt = BusinessDay.EndOf(day) };
        IEnumerable<TripRow> rows = await _readerFactory.ExecuteReadAsync(
            connection => connection.QueryAsync<TripRow>(new CommandDefinition(SelectTripsSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        return ToList(rows.ToList());
    }

    private static OperatorTripList ToList(List<TripRow> rows)
    {
        var items = new List<TripOccupancy>();
        foreach (TripRow row in rows)
        {
            items.Add(new TripOccupancy
            {
                TripId = row.TripId,
                Source = row.Source,
                Destination = row.Destination,
                DepartureAt = UtcTimes.ToOffset(row.DepartureAt),
                SeatsSold = row.SeatsSold,
                SeatsTotal = row.SeatsTotal,
            });
        }

        return new OperatorTripList { Items = items, TotalCount = items.Count };
    }

    private sealed class TripRow
    {
        public Guid TripId { get; set; }

        public string Source { get; set; } = string.Empty;

        public string Destination { get; set; } = string.Empty;

        public DateTime DepartureAt { get; set; }

        public int SeatsSold { get; set; }

        public int SeatsTotal { get; set; }
    }
}
