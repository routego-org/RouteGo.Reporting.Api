using System.Text.Json;

namespace RouteGo.Reporting.Api.Features.Reports.GetOperatorSummary;

[Collection(DatabaseCollection.Name)]
public sealed class GetOperatorSummaryTests
{
    // 2 October 2026, 10:00 India Standard Time, in UTC terms.
    private static readonly DateTimeOffset _october2 = new DateTimeOffset(2026, 10, 2, 4, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _october3 = _october2.AddDays(1);

    private readonly DatabaseFixture _database;

    public GetOperatorSummaryTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task GetOperatorSummary_TwoDaysOfActivity_ReturnsTotalsAndSeries()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedConfirmedAsync("Orbit Travels", _october2, 8400, 6, 3);
        await SeedConfirmedAsync("Orbit Travels", _october3, 2400, 2, 1, 1000, _october3);
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-03");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement totals = body.RootElement.GetProperty("totals");
        Assert.Equal(4, totals.GetProperty("confirmedBookings").GetInt32());
        Assert.Equal(8, totals.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(10800, totals.GetProperty("revenue").GetInt64());
        Assert.Equal(1000, totals.GetProperty("refunds").GetInt64());
        Assert.Equal(9800, totals.GetProperty("net").GetInt64());
        JsonElement series = body.RootElement.GetProperty("dailySeries");
        Assert.Equal(2, series.GetArrayLength());
        Assert.Equal("2026-10-02", series[0].GetProperty("date").GetString());
        Assert.Equal(8400, series[0].GetProperty("revenue").GetInt64());
        Assert.Equal(1000, series[1].GetProperty("refunds").GetInt64());
        Assert.Equal("INR", body.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task GetOperatorSummary_QuietDayInTheRange_AppearsAsZero()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedConfirmedAsync("Orbit Travels", _october2, 1000, 1, 1);
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-04");

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement quietDay = body.RootElement.GetProperty("dailySeries")[2];
        Assert.Equal("2026-10-04", quietDay.GetProperty("date").GetString());
        Assert.Equal(0, quietDay.GetProperty("confirmedBookings").GetInt32());
        Assert.Equal(0, quietDay.GetProperty("revenue").GetInt64());
    }

    [Fact]
    public async Task GetOperatorSummary_NoActivityAtAll_ReturnsZeroTotals()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-01&to=2026-10-31");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totals").GetProperty("revenue").GetInt64());
        Assert.Equal(31, body.RootElement.GetProperty("dailySeries").GetArrayLength());
    }

    [Fact]
    public async Task GetOperatorSummary_TwoOperators_SeesOnlyItsOwn()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await SeedConfirmedAsync("Orbit Travels", _october2, 8400, 7, 3);
        await SeedConfirmedAsync("Sunrise Lines", _october2, 5000, 4, 1);
        string token = TestAccessTokens.CreateOperator("Sunrise Lines");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-02");

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(5000, body.RootElement.GetProperty("totals").GetProperty("revenue").GetInt64());
    }

    [Theory]
    [InlineData("?from=2026-10-05&to=2026-10-02")]
    [InlineData("?from=2026-10-05")]
    [InlineData("?from=not-a-date&to=2026-10-02")]
    public async Task GetOperatorSummary_RangeEndingBeforeItStarts_IsRefusedValidationFailed(string query)
    {
        // Arrange
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, query);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOperatorSummary_TokenNamingNoOperator_IsRefusedForbidden()
    {
        // Arrange
        string token = TestAccessTokens.Create("Operator");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-03");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOperatorSummary_Admin_IsRefusedForbidden()
    {
        // Arrange
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-03");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOperatorSummary_WithoutAccessToken_IsRefusedUnauthorized()
    {
        // Arrange
        using ApiFactory factory = ReportingTestHost.For(_database);
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/reports/operator/summary?from=2026-10-02&to=2026-10-03");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task SeedConfirmedAsync(
        string operatorId, DateTimeOffset confirmedAt, long amount, int seats, int bookings, long? refund = null, DateTimeOffset? refundAt = null)
    {
        long amountEach = amount / bookings;
        int seatsEach = seats / bookings;
        for (int i = 0; i < bookings; i++)
        {
            var seed = new BookingSeed
            {
                OperatorId = operatorId,
                ConfirmedAt = confirmedAt,
                Amount = amountEach,
                SeatsCount = seatsEach,
                RefundAmount = refund,
                RefundCompletedAt = refundAt,
            };
            await ReportSeed.BookingAsync(_database, seed);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string query)
    {
        using ApiFactory factory = ReportingTestHost.For(_database);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = TestAccessTokens.Authorized(HttpMethod.Get, "/reports/operator/summary" + query, accessToken);
        return await client.SendAsync(request);
    }
}
