using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RouteGo.Reporting.Api.Features.Settlements.DownloadSettlement;

[Collection(DatabaseCollection.Name)]
public sealed class DownloadSettlementTests
{
    private const string InsertSettlementSql = """
        INSERT INTO reporting.settlements (settlement_date, operator_id, gross, refunds, net, generated_at)
        VALUES ('2026-10-02', 'Orbit Travels', 8400, 1000, 7400, '2026-10-02T19:30:00Z')
        """;

    // Near real time: the link's signed lifetime is counted by the SDK against the wall clock.
    private static readonly DateTimeOffset _now = new DateTimeOffset(new RouteGo.Reporting.Api.Infrastructure.SystemClock().UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public DownloadSettlementTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task DownloadSettlement_ExistingSettlement_ReturnsALinkExpiringAfterTheConfiguredMinutes()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(InsertSettlementSql);
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "/reports/settlements/2026-10-02/download?operatorId=Orbit%20Travels");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string url = body.RootElement.GetProperty("url").GetString()!;
        Assert.Contains("settlement/2026-10-02/Orbit%2520Travels.csv", url);
        Assert.Contains("X-Amz-Expires=", url);
        Assert.Equal(_now.AddMinutes(5), body.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task DownloadSettlement_NoSettlement_IsRefusedNotFound()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "/reports/settlements/2026-10-03/download?operatorId=Orbit%20Travels");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DownloadSettlement_Operator_IsRefusedForbidden()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(InsertSettlementSql);
        string token = TestAccessTokens.CreateOperator("Orbit Travels");

        // Act
        using HttpResponseMessage response = await SendAsync(token, "/reports/settlements/2026-10-02/download?operatorId=Orbit%20Travels");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/reports/settlements/2026-10-02/download")]
    [InlineData("/reports/settlements/yesterday/download?operatorId=Orbit%20Travels")]
    public async Task DownloadSettlement_MissingOperatorOrMalformedDay_IsRefusedValidationFailed(string route)
    {
        // Arrange
        string token = TestAccessTokens.Create("Admin");

        // Act
        using HttpResponseMessage response = await SendAsync(token, route);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string route)
    {
        using ApiFactory factory = ReportingTestHost.For(_database, services => services.AddSingleton<IClock>(new FixedClock(_now)));
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = TestAccessTokens.Authorized(HttpMethod.Get, route, accessToken);
        return await client.SendAsync(request);
    }
}
