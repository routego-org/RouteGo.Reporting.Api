namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyTripScheduled;

/// <summary>A city as Fleet names it; the name member is called <c>cityName</c> or <c>name</c>.</summary>
internal sealed record TripCityData
{
    public string? CityName { get; init; }

    public string? Name { get; init; }
}

/// <summary>
/// The data of Fleet's TripScheduled event, read as it came: every member is optional so the reader
/// can say what is wrong with an event instead of the deserializer guessing. Fleet also sends the bus,
/// the fares and the seats, which Reporting does not keep.
/// </summary>
internal sealed record TripScheduledData
{
    public string? TripId { get; init; }

    public string? OperatorId { get; init; }

    public TripCityData? Source { get; init; }

    public TripCityData? Destination { get; init; }

    public DateTimeOffset? DepartureUtc { get; init; }

    public int? TotalSeats { get; init; }
}

/// <summary>The trip details Reporting keeps, checked and ready to store.</summary>
internal sealed record TripScheduledMessage
{
    public required Guid TripId { get; init; }

    public required string OperatorId { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required int SeatsTotal { get; init; }
}

/// <summary>The checked message, or a problem naming everything that is missing or unusable.</summary>
internal sealed record TripScheduledReading(TripScheduledMessage? Message, string? Problem);

/// <summary>Reads and checks the data of a TripScheduled event.</summary>
internal static class TripScheduledReader
{
    private const int MaxOperatorIdLength = 64;
    private const int MaxCityNameLength = 200;

    /// <summary>The checked message, or null with a problem naming every member that is missing or unusable.</summary>
    public static TripScheduledReading Read(JsonElement data)
    {
        TripScheduledData? raw = Deserialize(data);
        if (raw is null)
        {
            return new TripScheduledReading(null, "the data is not a trip object");
        }

        var problems = new List<string>();
        bool hasTripId = Guid.TryParse(raw.TripId, out Guid tripId);
        if (!hasTripId)
        {
            problems.Add($"tripId (got {raw.TripId})");
        }

        string? operatorId = Text(raw.OperatorId, MaxOperatorIdLength, "operatorId", problems);
        string? source = Text(CityName(raw.Source), MaxCityNameLength, "source.cityName", problems);
        string? destination = Text(CityName(raw.Destination), MaxCityNameLength, "destination.cityName", problems);
        if (raw.DepartureUtc is null)
        {
            problems.Add("departureUtc");
        }

        if (raw.TotalSeats is null || raw.TotalSeats <= 0)
        {
            problems.Add($"totalSeats (got {raw.TotalSeats})");
        }

        if (problems.Count > 0)
        {
            return new TripScheduledReading(null, "unusable " + string.Join(", ", problems));
        }

        var message = new TripScheduledMessage
        {
            TripId = tripId,
            OperatorId = operatorId!,
            Source = source!,
            Destination = destination!,
            DepartureAt = raw.DepartureUtc!.Value,
            SeatsTotal = raw.TotalSeats!.Value,
        };
        return new TripScheduledReading(message, null);
    }

    private static string? CityName(TripCityData? city)
    {
        if (city is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(city.CityName))
        {
            return city.CityName;
        }

        return city.Name;
    }

    private static string? Text(string? value, int maxLength, string member, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            problems.Add(member);
            return null;
        }

        return value;
    }

    private static TripScheduledData? Deserialize(JsonElement data)
    {
        try
        {
            return data.Deserialize<TripScheduledData>(WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
