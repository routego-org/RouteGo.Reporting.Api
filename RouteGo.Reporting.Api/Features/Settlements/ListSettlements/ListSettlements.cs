namespace RouteGo.Reporting.Api.Features.Settlements.ListSettlements;

/// <summary>The settlement date whose settlements to list.</summary>
public sealed class ListSettlementsRequest
{
    /// <summary>The settlement date, a business day (yyyy-MM-dd).</summary>
    public DateOnly? Date { get; init; }
}

/// <summary>The final figures for one operator on one business day, in whole rupees.</summary>
public sealed class SettlementSummary
{
    public required DateOnly SettlementDate { get; init; }

    public required string OperatorId { get; init; }

    public required long Gross { get; init; }

    public required long Refunds { get; init; }

    /// <summary>Gross minus refunds; negative when a day's refunds exceed its gross.</summary>
    public required long Net { get; init; }

    /// <summary>Always INR.</summary>
    public required string Currency { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>Every operator's settlement for the day.</summary>
public sealed class SettlementList
{
    public required IReadOnlyList<SettlementSummary> Items { get; init; }

    public required int TotalCount { get; init; }
}

public sealed class ListSettlementsValidator : AbstractValidator<ListSettlementsRequest>
{
    public ListSettlementsValidator()
    {
        RuleFor(x => x.Date).NotNull().WithMessage("A day is required, as yyyy-MM-dd.");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "Settlements")]
[Route("reports/settlements")]
public sealed class ListSettlementsController : ControllerBase
{
    private readonly ListSettlementsHandler _handler;
    private readonly ListSettlementsValidator _validator = new ListSettlementsValidator();

    public ListSettlementsController(IReaderConnectionFactory readerFactory)
    {
        _handler = new ListSettlementsHandler(readerFactory);
    }

    /// <summary>List every operator's settlement for one business day (Admin).</summary>
    /// <remarks>
    /// What the nightly run wrote for the day. A day not yet settled, or an operator with no activity, is
    /// simply absent: an empty list, never an error. Admin only until Identity issues a Finance role.
    /// </remarks>
    /// <param name="request">The settlement date.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="200">The day's settlements.</response>
    /// <response code="400">The day is missing or not a date (VALIDATION_FAILED).</response>
    /// <response code="401">No valid access token was presented (UNAUTHORIZED).</response>
    /// <response code="403">The caller is not an admin (FORBIDDEN).</response>
    [Authorize(Policy = Policies.Admin)]
    [HttpGet]
    [ProducesResponseType(typeof(SettlementList), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSettlementsAsync([FromQuery] ListSettlementsRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SettlementList settlements = await _handler.HandleAsync(request, cancellationToken);
        return Ok(settlements);
    }
}
