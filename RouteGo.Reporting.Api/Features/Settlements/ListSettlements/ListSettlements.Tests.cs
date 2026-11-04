using System.Text.Json;

namespace RouteGo.Reporting.Api.Features.Settlements.ListSettlements;

[Collection(DatabaseCollection.Name)]
public sealed class ListSettlementsTests
{
    private const string InsertSettlementSql = """
        INSERT INTO reporting.settlements (settlement_date, operator_id, gross, refunds, net, generated_at)
        VALUES ('2026-10-02', @OperatorId, @Gross, @Refunds, @Net, '2026-10-02T19:30:00Z')
        """;

    private readonly DatabaseFixture _database;

    public ListSettlementsTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ListSettlements_DayWithSettlements_ReturnsThemAllWithTotal()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(InsertSettlementSql, new { OperatorId = "Orbit Travels", Gross = 8400, Refunds = 1000, Net = 7400 });
        await _database.SeedAsync(InsertSettlementSql, new { OperatorId = "Sunrise Lines", Gross = 5000, Refunds = 0, Net = 5000 });
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-02");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
        JsonElement first = body.RootElement.GetProperty("items")[0];
        Assert.Equal("2026-10-02", first.GetProperty("settlementDate").GetString());
        Assert.Equal("Orbit Travels", first.GetProperty("operatorId").GetString());
        Assert.Equal(7400, first.GetProperty("net").GetInt64());
        Assert.Equal("INR", first.GetProperty("currency").GetString());
        Assert.True(first.TryGetProperty("generatedAt", out JsonElement _));
    }

    [Fact]
    public async Task ListSettlements_DayNotYetSettled_ReturnsEmptyList()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-03");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListSettlements_Operator_IsRefusedForbidden()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(InsertSettlementSql, new { OperatorId = "Orbit Travels", Gross = 8400, Refunds = 1000, Net = 7400 });
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "?date=2026-10-02");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=tomorrow")]
    public async Task ListSettlements_MissingOrMalformedDay_IsRefusedValidationFailed(string query)
    {
        // Arrange
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, query);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string query)
    {
        using ApiFactory factory = ReportingTestHost.For(_database);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = TestAccessTokens.Authorized(HttpMethod.Get, "/reports/settlements" + query, accessToken);
        return await client.SendAsync(request);
    }
}
