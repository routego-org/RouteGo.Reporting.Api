using System.Text.Json;

namespace RouteGo.Reporting.Api.Features.Reports.ListOperatorTrips;

[Collection(DatabaseCollection.Name)]
public sealed class ListOperatorTripsTests
{
    // 5 October 2026, 10:00 India Standard Time, in UTC terms.
    private static readonly DateTimeOffset _october5 = new DateTimeOffset(2026, 10, 5, 4, 30, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public ListOperatorTripsTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ListOperatorTrips_TripsWithBookings_ReturnsSeatsSoldAndTotal()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        Guid fullerTrip = await ReportSeed.TripAsync(_database, "Orbit Travels", _october5, 40);
        await ReportSeed.TripAsync(_database, "Orbit Travels", _october5.AddHours(1), 40);
        await ReportSeed.BookingAsync(_database, new BookingSeed { OperatorId = "Orbit Travels", TripId = fullerTrip, ConfirmedAt = _october5, SeatsCount = 30 });
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-05");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
        JsonElement first = body.RootElement.GetProperty("items")[0];
        Assert.Equal(30, first.GetProperty("seatsSold").GetInt32());
        Assert.Equal(40, first.GetProperty("seatsTotal").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items")[1].GetProperty("seatsSold").GetInt32());
    }

    [Fact]
    public async Task ListOperatorTrips_CancelledBooking_IsNotCountedAsSold()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        Guid tripId = await ReportSeed.TripAsync(_database, "Orbit Travels", _october5, 40);
        await ReportSeed.BookingAsync(
            _database,
            new BookingSeed { OperatorId = "Orbit Travels", TripId = tripId, ConfirmedAt = _october5, SeatsCount = 2, Status = "cancelled" });
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-05");

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("items")[0].GetProperty("seatsSold").GetInt32());
    }

    [Fact]
    public async Task ListOperatorTrips_DayWithNoTrips_ReturnsEmptyList()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await ReportSeed.TripAsync(_database, "Orbit Travels", _october5, 40);
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-06");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ListOperatorTrips_TwoOperators_SeesOnlyItsOwn()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await ReportSeed.TripAsync(_database, "Sunrise Lines", _october5, 40);
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-05");

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListOperatorTrips_MissingDay_IsRefusedValidationFailed()
    {
        // Arrange
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, string.Empty);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListOperatorTrips_TokenNamingNoOperator_IsRefusedForbidden()
    {
        // Arrange
        string token = TestAccessTokens.Create("Operator");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-05");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string query)
    {
        using ApiFactory factory = ReportingTestHost.For(_database);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = TestAccessTokens.Authorized(HttpMethod.Get, "/reports/operator/trips" + query, accessToken);
        return await client.SendAsync(request);
    }
}
