namespace RouteGo.Reporting.Api.Features.Reports.GetAdminSummary;

/// <summary>The range of business days to summarise, both ends inclusive.</summary>
public sealed class GetAdminSummaryRequest
{
    /// <summary>First business day of the range (yyyy-MM-dd).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last business day of the range (yyyy-MM-dd), on or after <c>from</c>.</summary>
    public DateOnly? To { get; init; }
}

public sealed class GetAdminSummaryValidator : AbstractValidator<GetAdminSummaryRequest>
{
    public GetAdminSummaryValidator()
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
[Route("reports/admin/summary")]
public sealed class GetAdminSummaryController : ControllerBase
{
    private readonly GetAdminSummaryHandler _handler;
    private readonly GetAdminSummaryValidator _validator = new GetAdminSummaryValidator();

    public GetAdminSummaryController(IReaderConnectionFactory readerFactory)
    {
        _handler = new GetAdminSummaryHandler(readerFactory);
    }

    /// <summary>Get system-wide sales totals for a date range (Admin).</summary>
    /// <remarks>
    /// The figures an operator sees, summed over every operator, in total and for each business day of
    /// the range. Admin only until Identity issues a Finance role. A range with no activity is zero
    /// figures, never an error.
    /// </remarks>
    /// <param name="request">The range of business days.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="200">The system summary.</response>
    /// <response code="400">The range is missing, malformed, or ends before it starts (VALIDATION_FAILED).</response>
    /// <response code="401">No valid access token was presented (UNAUTHORIZED).</response>
    /// <response code="403">The caller is not an admin (FORBIDDEN).</response>
    [Authorize(Policy = Policies.Admin)]
    [HttpGet]
    [ProducesResponseType(typeof(SalesSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminSummaryAsync([FromQuery] GetAdminSummaryRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SalesSummary summary = await _handler.HandleAsync(request, cancellationToken);
        return Ok(summary);
    }
}
