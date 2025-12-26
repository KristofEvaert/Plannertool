namespace TransportPlanner.Domain;

/// <summary>
/// Represents a geographical location using latitude and longitude coordinates.
/// </summary>
public record GeoLocation
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }

    public GeoLocation(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }
}

