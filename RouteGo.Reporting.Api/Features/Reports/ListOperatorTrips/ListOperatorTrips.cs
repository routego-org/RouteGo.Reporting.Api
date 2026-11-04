namespace RouteGo.Reporting.Api.Features.Reports.ListOperatorTrips;

/// <summary>The business day whose departures to list.</summary>
public sealed class ListOperatorTripsRequest
{
    /// <summary>The business day the trips depart on (yyyy-MM-dd).</summary>
    public DateOnly? Date { get; init; }
}

/// <summary>One trip and how many of its seats are sold; occupancy is seatsSold over seatsTotal.</summary>
public sealed class TripOccupancy
{
    public required Guid TripId { get; init; }

    /// <summary>Departure city.</summary>
    public required string Source { get; init; }

    /// <summary>Arrival city.</summary>
    public required string Destination { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    /// <summary>Seats on bookings that are confirmed now.</summary>
    public required int SeatsSold { get; init; }

    public required int SeatsTotal { get; init; }
}

/// <summary>The operator's trips for the day.</summary>
public sealed class OperatorTripList
{
    public required IReadOnlyList<TripOccupancy> Items { get; init; }

    public required int TotalCount { get; init; }
}

public sealed class ListOperatorTripsValidator : AbstractValidator<ListOperatorTripsRequest>
{
    public ListOperatorTripsValidator()
    {
        RuleFor(x => x.Date).NotNull().WithMessage("A day is required, as yyyy-MM-dd.");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "Reports")]
[Route("reports/operator/trips")]
public sealed class ListOperatorTripsController : ControllerBase
{
    private readonly ListOperatorTripsHandler _handler;
    private readonly ListOperatorTripsValidator _validator = new ListOperatorTripsValidator();

    public ListOperatorTripsController(IReaderConnectionFactory readerFactory)
    {
        _handler = new ListOperatorTripsHandler(readerFactory);
    }

    /// <summary>List the calling operator's trips departing on a day, with their occupancy (Operator).</summary>
    /// <remarks>
    /// Each trip of the operator the access token names that departs on the business day, with the seats
    /// sold on bookings confirmed now and its total seats. The request carries no operator identifier. A
    /// day with no trips is an empty list, never an error.
    /// </remarks>
    /// <param name="request">The business day.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <response code="200">The operator's trips for the day.</response>
    /// <response code="400">The day is missing or not a date (VALIDATION_FAILED).</response>
    /// <response code="401">No valid access token was presented (UNAUTHORIZED).</response>
    /// <response code="403">The caller is not an operator, or the token names no operator (FORBIDDEN).</response>
    [Authorize(Policy = Policies.Operator)]
    [HttpGet]
    [ProducesResponseType(typeof(OperatorTripList), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOperatorTripsAsync([FromQuery] ListOperatorTripsRequest request, CancellationToken cancellationToken)
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

        OperatorTripList trips = await _handler.HandleAsync(request, caller.OperatorId, cancellationToken);
        return Ok(trips);
    }
}
