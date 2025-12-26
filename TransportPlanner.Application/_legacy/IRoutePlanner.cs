using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

/// <summary>
/// Interface for route planning algorithms (greedy, OR-Tools, etc.)
/// </summary>
public interface IRoutePlanner
{
    /// <summary>
    /// Plans routes for a single day, assigning poles to drivers.
    /// </summary>
    /// <param name="input">Input data including date, drivers, and poles</param>
    /// <param name="travelTimeMatrix">Precomputed travel time matrix in minutes. 
    /// Matrix dimensions: [locations.Count, locations.Count] where locations = driver starts + poles</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Planned routes with pole assignments per driver and unassigned poles</returns>
    Task<PlannedDayResult> PlanDayAsync(
        PlanDayInput input,
        int[,] travelTimeMatrix,
        CancellationToken cancellationToken = default);
}

