namespace TransportPlanner.Application.Interfaces;

public interface IGeocodingService
{
    Task<string?> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

