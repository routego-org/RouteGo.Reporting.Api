namespace RouteGo.Reporting.Api.Features.Reports.GetOperatorSummary;

/// <summary>The range of business days to summarise, both ends inclusive.</summary>
public sealed class GetOperatorSummaryRequest
{
    /// <summary>First business day of the range (yyyy-MM-dd).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last business day of the range (yyyy-MM-dd), on or after <c>from</c>.</summary>
    public DateOnly? To { get; init; }
}

public sealed class GetOperatorSummaryValidator : AbstractValidator<GetOperatorSummaryRequest>
{
    public GetOperatorSummaryValidator()
    {
        RuleFor(x => x.From).NotNull().WithMessage("A start day is required, as yyyy-MM-dd.");
        RuleFor(x => x.To).NotNull().WithMessage("An end day is required, as yyyy-MM-dd.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From != null && x.To != null)
            .WithMessage("The end of the range must be on or after its start.");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "Reports")]
[Route("reports/operator/summary")]
public sealed class GetOperatorSummaryController : ControllerBase
{
    private readonly GetOperatorSummaryHandler _handler;
    private readonly GetOperatorSummaryValidator _validator = new GetOperatorSummaryValidator();

    public GetOperatorSummaryController(IReaderConnectionFactory readerFactory)
    {
        _handler = new GetOperatorSummaryHandler(readerFactory);
    }

    /// <summary>Get the calling operator's sales summary for a date range (Operator).</summary>
    /// <remarks>
    /// Confirmed bookings, seats, revenue, refunds and net, in total and for each business day of the
    /// range, for the operator the access token names. The request carries no operator identifier. A
    /// range with no activity is zero figures, never an error.
    /// </remarks>
    /// <param name="request">The range of business days.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="200">The operator's summary.</response>
    /// <response code="400">The range is missing, malformed, or ends before it starts (VALIDATION_FAILED).</response>
    /// <response code="401">No valid access token was presented (UNAUTHORIZED).</response>
    /// <response code="403">The caller is not an operator, or the token names no operator (FORBIDDEN).</response>
    [Authorize(Policy = Policies.Operator)]
    [HttpGet]
    [ProducesResponseType(typeof(SalesSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperatorSummaryAsync([FromQuery] GetOperatorSummaryRequest request, CancellationToken cancellationToken)
    {
        Caller caller = CallerReader.Read(User);
        if (string.IsNullOrWhiteSpace(caller.OperatorId))
        {
            return ProblemResult.Forbidden(this, "The access token names no operator.");
        }

        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SalesSummary summary = await _handler.HandleAsync(request, caller.OperatorId, cancellationToken);
        return Ok(summary);
    }
}
