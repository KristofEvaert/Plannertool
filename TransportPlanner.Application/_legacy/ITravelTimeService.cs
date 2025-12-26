using TransportPlanner.Domain;

namespace TransportPlanner.Application.Interfaces;

/// <summary>
/// Service for calculating travel times between geographical locations.
/// </summary>
public interface ITravelTimeService
{
    /// <summary>
    /// Calculates a travel time matrix between all provided locations.
    /// </summary>
    /// <param name="locations">List of geographical locations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A two-dimensional array where matrix[i, j] represents the travel time in minutes
    /// from locations[i] to locations[j]. Diagonal values (i == j) are 0.
    /// </returns>
    Task<int[,]> GetTravelTimeMatrixAsync(
        IReadOnlyList<GeoLocation> locations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates travel time between two locations.
    /// </summary>
    /// <param name="from">Starting location</param>
    /// <param name="to">Destination location</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Travel time in minutes</returns>
    Task<int> GetTravelTimeAsync(
        GeoLocation from,
        GeoLocation to,
        CancellationToken cancellationToken = default);
}

