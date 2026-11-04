using Amazon.S3.Model;

namespace RouteGo.Reporting.Api.Features.Settlements.DownloadSettlement;

/// <summary>
/// Issues a short-lived link to a settlement's report. A link is issued only for a settlement that
/// exists, so the bucket's contents are never probed for names a caller guesses, and it is issued
/// afresh each time it is asked for.
/// </summary>
public sealed class DownloadSettlementHandler
{
    private const string ExistsSql = """
        SELECT EXISTS (SELECT 1 FROM reporting.settlements WHERE settlement_date = @Day AND operator_id = @OperatorId)
        """;

    private readonly IReaderConnectionFactory _readerFactory;
    private readonly IAmazonS3 _s3;
    private readonly SettlementLinkSettings _settings;
    private readonly IClock _clock;

    public DownloadSettlementHandler(IReaderConnectionFactory readerFactory, IAmazonS3 s3, SettlementLinkSettings settings, IClock clock)
    {
        _readerFactory = readerFactory;
        _s3 = s3;
        _settings = settings;
        _clock = clock;
    }

    /// <summary>The link for a validated request, or null when that operator has no settlement on that day.</summary>
    public async Task<SettlementDownloadLink?> HandleAsync(DownloadSettlementRequest request, CancellationToken cancellationToken)
    {
        DateOnly day = request.Date!.Value;
        string operatorId = request.OperatorId!;
        var parameters = new { Day = day, OperatorId = operatorId };
        bool exists = await _readerFactory.ExecuteReadAsync(
            connection => connection.ExecuteScalarAsync<bool>(new CommandDefinition(ExistsSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        if (!exists)
        {
            return null;
        }

        DateTimeOffset expiresAt = _clock.UtcNow.Add(_settings.LinkLifetime);
        return new SettlementDownloadLink { Url = Sign(day, operatorId, expiresAt), ExpiresAt = expiresAt };
    }

    // Signing is local arithmetic over the execution role's credentials: no call to the bucket is made.
    private string Sign(DateOnly day, string operatorId, DateTimeOffset expiresAt)
    {
        string key = SettlementReportKey.For(day, operatorId);
        string fileName = Path.GetFileName(key);
        var signing = new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
        };
        if (_s3.Config.UseHttp)
        {
            signing.Protocol = Protocol.HTTP;
        }

        signing.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"settlement-{day:yyyy-MM-dd}-{fileName}\"";
        return _s3.GetPreSignedURL(signing);
    }
}
