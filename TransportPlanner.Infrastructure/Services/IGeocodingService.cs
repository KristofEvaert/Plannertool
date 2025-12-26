namespace TransportPlanner.Infrastructure.Services;

public interface IGeocodingService
{
    Task<GeocodeResult?> GeocodeAddressAsync(string address, CancellationToken cancellationToken = default);
    Task<string?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

public record GeocodeResult(double Latitude, double Longitude);
