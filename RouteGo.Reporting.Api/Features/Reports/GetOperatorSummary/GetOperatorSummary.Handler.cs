namespace RouteGo.Reporting.Api.Features.Reports.GetOperatorSummary;

/// <summary>Reads one operator's sales summary; the operator is always the caller's own, passed in by the controller.</summary>
public sealed class GetOperatorSummaryHandler
{
    private readonly SalesSummaryReader _reader;

    public GetOperatorSummaryHandler(IReaderConnectionFactory readerFactory)
    {
        _reader = new SalesSummaryReader(readerFactory);
    }

    /// <summary>The operator's figures for a validated range, in whole rupees.</summary>
    public async Task<SalesSummary> HandleAsync(GetOperatorSummaryRequest request, string operatorId, CancellationToken cancellationToken)
    {
        return await _reader.ReadAsync(request.From!.Value, request.To!.Value, operatorId, cancellationToken);
    }
}
