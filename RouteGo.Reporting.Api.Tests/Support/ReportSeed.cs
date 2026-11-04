namespace RouteGo.Reporting.Api.Tests.Support;

/// <summary>Writes reporting rows straight into the test database, for tests of what reads them.</summary>
public static class ReportSeed
{
    private const string InsertBookingSql = """
        INSERT INTO reporting.bookings
            (booking_id, trip_id, operator_id, status, amount, seats_count, confirmed_at,
             refund_amount, refund_completed_at, status_changed_at)
        VALUES
            (@BookingId, @TripId, @OperatorId, @Status, @Amount, @SeatsCount, @ConfirmedAt,
             @RefundAmount, @RefundCompletedAt, @ConfirmedAt)
        """;

    private const string InsertTripSql = """
        INSERT INTO reporting.trips (trip_id, operator_id, source, destination, departure_at, seats_total, last_announced_at)
        VALUES (@TripId, @OperatorId, 'Pune', 'Goa', @DepartureAt, @SeatsTotal, @DepartureAt)
        """;

    /// <summary>A booking confirmed at <paramref name="confirmedAt"/>, optionally refunded.</summary>
    public static async Task<Guid> BookingAsync(DatabaseFixture database, BookingSeed seed)
    {
        var bookingId = Guid.NewGuid();
        var values = new
        {
            BookingId = bookingId,
            seed.TripId,
            seed.OperatorId,
            seed.Status,
            seed.Amount,
            seed.SeatsCount,
            seed.ConfirmedAt,
            seed.RefundAmount,
            seed.RefundCompletedAt,
        };
        await database.SeedAsync(InsertBookingSql, values);
        return bookingId;
    }

    /// <summary>A trip departing at <paramref name="departureAt"/>.</summary>
    public static async Task<Guid> TripAsync(DatabaseFixture database, string operatorId, DateTimeOffset departureAt, int seatsTotal)
    {
        var tripId = Guid.NewGuid();
        await database.SeedAsync(InsertTripSql, new { TripId = tripId, OperatorId = operatorId, DepartureAt = departureAt, SeatsTotal = seatsTotal });
        return tripId;
    }
}
