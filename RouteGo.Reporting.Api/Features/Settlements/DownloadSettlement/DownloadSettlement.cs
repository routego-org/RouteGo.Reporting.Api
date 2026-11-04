namespace RouteGo.Reporting.Api.Features.Settlements.DownloadSettlement;

/// <summary>Which settlement report to link to: the day in the path, the operator in the query.</summary>
public sealed class DownloadSettlementRequest
{
    /// <summary>The longest operator identifier a settlement can carry.</summary>
    public const int MaxOperatorIdLength = 64;

    /// <summary>The settlement date, a business day (yyyy-MM-dd).</summary>
    [FromRoute(Name = "date")]
    public DateOnly? Date { get; init; }

    /// <summary>The operator whose report is wanted.</summary>
    [FromQuery(Name = "operatorId")]
    public string? OperatorId { get; init; }
}

/// <summary>A link that delivers the report until it expires.</summary>
public sealed class SettlementDownloadLink
{
    public required string Url { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Where the reports are kept and how long a link to one works.</summary>
public sealed record SettlementLinkSettings(string Bucket, TimeSpan LinkLifetime);

public sealed class DownloadSettlementValidator : AbstractValidator<DownloadSettlementRequest>
{
    public DownloadSettlementValidator()
    {
        RuleFor(x => x.Date).NotNull().WithMessage("A day is required, as yyyy-MM-dd.");
        RuleFor(x => x.OperatorId).NotEmpty().WithMessage("An operator is required.");
        RuleFor(x => x.OperatorId).MaximumLength(DownloadSettlementRequest.MaxOperatorIdLength)
            .WithMessage("The operator is too long.");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "Settlements")]
[Route("reports/settlements/{date}/download")]
public sealed class DownloadSettlementController : ControllerBase
{
    private readonly DownloadSettlementHandler _handler;
    private readonly DownloadSettlementValidator _validator = new DownloadSettlementValidator();

    public DownloadSettlementController(IReaderConnectionFactory readerFactory, IAmazonS3 s3, IClock clock, IConfiguration configuration)
    {
        var settings = new SettlementLinkSettings(
            configuration[ConfigKeys.ReportsBucket]!,
            TimeSpan.FromMinutes(int.Parse(configuration[ConfigKeys.PresignMinutes]!, System.Globalization.CultureInfo.InvariantCulture)));
        _handler = new DownloadSettlementHandler(readerFactory, s3, settings, clock);
    }

    /// <summary>Get a short-lived link to one operator's settlement report for a day (Admin).</summary>
    /// <remarks>
    /// Returns a link, not the file. The link stops working at <c>expiresAt</c>; ask again for a new one.
    /// A day has one report per operator, so the operator is named by the <c>operatorId</c> query
    /// parameter. Admin only until Identity issues a Finance role.
    /// </remarks>
    /// <param name="request">The settlement date and the operator.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <response code="200">A short-lived link to the report.</response>
    /// <response code="400">The day is not a date, or the operator is missing (VALIDATION_FAILED).</response>
    /// <response code="401">No valid access token was presented (UNAUTHORIZED).</response>
    /// <response code="403">The caller is not an admin (FORBIDDEN).</response>
    /// <response code="404">No settlement exists for that operator and day (NOT_FOUND).</response>
    [Authorize(Policy = Policies.Admin)]
    [HttpGet]
    [ProducesResponseType(typeof(SettlementDownloadLink), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSettlementAsync(DownloadSettlementRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SettlementDownloadLink? link = await _handler.HandleAsync(request, cancellationToken);
        if (link is null)
        {
            return ProblemResult.NotFound(this, "No settlement exists for that operator and day.");
        }

        return Ok(link);
    }
}
