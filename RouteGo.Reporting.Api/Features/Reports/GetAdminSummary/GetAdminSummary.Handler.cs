namespace RouteGo.Reporting.Api.Features.Reports.GetAdminSummary;

/// <summary>Reads the sales summary over every operator, counting exactly as an operator's own summary does.</summary>
public sealed class GetAdminSummaryHandler
{
    private readonly SalesSummaryReader _reader;

    public GetAdminSummaryHandler(IReaderConnectionFactory readerFactory)
    {
        _reader = new SalesSummaryReader(readerFactory);
    }

    /// <summary>The system-wide figures for a validated range, in whole rupees.</summary>
    public async Task<SalesSummary> HandleAsync(GetAdminSummaryRequest request, CancellationToken cancellationToken)
    {
        return await _reader.ReadAsync(request.From!.Value, request.To!.Value, null, cancellationToken);
    }
}
