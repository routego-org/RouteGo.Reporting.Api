using System.Text.Json;

namespace RouteGo.Reporting.Api.Features.Reports.GetAdminSummary;

[Collection(DatabaseCollection.Name)]
public sealed class GetAdminSummaryTests
{
    // 2 October 2026, 10:00 India Standard Time, in UTC terms.
    private static readonly DateTimeOffset _october2 = new DateTimeOffset(2026, 10, 2, 4, 30, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public GetAdminSummaryTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task GetAdminSummary_TwoOperators_SumsBoth()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await ReportSeed.BookingAsync(_database, new BookingSeed { OperatorId = "Orbit Travels", ConfirmedAt = _october2, Amount = 8400, SeatsCount = 4 });
        await ReportSeed.BookingAsync(
            _database,
            new BookingSeed { OperatorId = "Sunrise Lines", ConfirmedAt = _october2, Amount = 5000, SeatsCount = 2, RefundAmount = 1000, RefundCompletedAt = _october2 });
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-02");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement totals = body.RootElement.GetProperty("totals");
        Assert.Equal(13400, totals.GetProperty("revenue").GetInt64());
        Assert.Equal(1000, totals.GetProperty("refunds").GetInt64());
        Assert.Equal(12400, totals.GetProperty("net").GetInt64());
        Assert.Equal(12400, body.RootElement.GetProperty("dailySeries")[0].GetProperty("net").GetInt64());
    }

    [Fact]
    public async Task GetAdminSummary_QuietDayInTheRange_AppearsAsZero()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-03&to=2026-10-05");

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement quietDay = body.RootElement.GetProperty("dailySeries")[1];
        Assert.Equal("2026-10-04", quietDay.GetProperty("date").GetString());
        Assert.Equal(0, quietDay.GetProperty("revenue").GetInt64());
    }

    [Fact]
    public async Task GetAdminSummary_Operator_IsRefusedForbidden()
    {
        // Arrange
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-02&to=2026-10-02");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetAdminSummary_RangeEndingBeforeItStarts_IsRefusedValidationFailed()
    {
        // Arrange
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?from=2026-10-05&to=2026-10-02");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string query)
    {
        using ApiFactory factory = ReportingTestHost.For(_database);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = TestAccessTokens.Authorized(HttpMethod.Get, "/reports/admin/summary" + query, accessToken);
        return await client.SendAsync(request);
    }
}
