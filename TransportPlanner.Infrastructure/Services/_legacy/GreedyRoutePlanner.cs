using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// Greedy route planner using nearest-neighbor heuristic.
/// </summary>
public class GreedyRoutePlanner : IRoutePlanner
{
    public Task<PlannedDayResult> PlanDayAsync(
        PlanDayInput input,
        int[,] travelTimeMatrix,
        CancellationToken cancellationToken = default)
    {
        var result = new PlannedDayResult();
        
        if (!input.Drivers.Any() || !input.Poles.Any())
        {
            result.UnassignedPoleIds = input.Poles.Select(p => p.Id).ToList();
            return Task.FromResult(result);
        }

        // Separate fixed-date poles from others and sort
        var fixedPoles = input.Poles
            .Where(p => p.IsFixedForDate)
            .OrderBy(p => p.FixedDate)
            .ThenBy(p => p.DueDate)
            .ToList();

        var otherPoles = input.Poles
            .Where(p => !p.IsFixedForDate)
            .OrderBy(p => p.DueDate)
            .ToList();

        var workingPoles = fixedPoles.Concat(otherPoles).ToList();

        // Create mapping: poleId -> index in travelTimeMatrix
        // First N indices are driver starts, then poles
        var poleIdToMatrixIndex = new Dictionary<int, int>();
        for (int i = 0; i < input.Poles.Count; i++)
        {
            poleIdToMatrixIndex[input.Poles[i].Id] = input.Drivers.Count + i;
        }

        // Initialize driver states
        var driverStates = input.Drivers.Select((driver, driverIndex) =>
        {
            var maxMinutes = Math.Min(driver.AvailabilityMinutes, driver.MaxWorkMinutesPerDay);
            return new DriverState
            {
                DriverId = driver.Id,
                DriverIndex = driverIndex,
                RemainingMinutes = maxMinutes,
                CurrentLocationMatrixIndex = driverIndex, // Driver start index
                Assignments = new List<PoleAssignment>()
            };
        }).ToList();

        // Greedy assignment loop
        // Safety: maximum iterations to prevent infinite loops
        // Worst case: each iteration assigns at most one pole per driver
        int maxIterations = input.Poles.Count * input.Drivers.Count;
        int iterationCount = 0;
        bool progressMade = true;
        
        while (progressMade && workingPoles.Any() && iterationCount < maxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            progressMade = false;
            iterationCount++;

            foreach (var driverState in driverStates)
            {
                if (driverState.RemainingMinutes <= 0)
                    continue;

                var driver = input.Drivers[driverState.DriverIndex];

                // Find best next pole
                PoleInput? bestPole = null;
                int bestTravelMinutes = int.MaxValue;

                foreach (var pole in workingPoles)
                {
                    var poleMatrixIndex = poleIdToMatrixIndex[pole.Id];
                    var travelMinutes = travelTimeMatrix[driverState.CurrentLocationMatrixIndex, poleMatrixIndex];
                    var requiredMinutes = travelMinutes + pole.ServiceMinutes;

                    if (requiredMinutes <= driverState.RemainingMinutes)
                    {
                        if (travelMinutes < bestTravelMinutes || 
                            (travelMinutes == bestTravelMinutes && (bestPole == null || pole.DueDate < bestPole.DueDate)))
                        {
                            bestPole = pole;
                            bestTravelMinutes = travelMinutes;
                        }
                    }
                }

                if (bestPole != null)
                {
                    var poleMatrixIndex = poleIdToMatrixIndex[bestPole.Id];
                    var serviceMinutes = bestPole.ServiceMinutes;
                    driverState.RemainingMinutes -= (bestTravelMinutes + serviceMinutes);
                    driverState.CurrentLocationMatrixIndex = poleMatrixIndex;
                    
                    var assignment = new PoleAssignment
                    {
                        PoleId = bestPole.Id,
                        Sequence = driverState.Assignments.Count + 1,
                        TravelMinutesFromPrev = bestTravelMinutes
                    };
                    
                    driverState.Assignments.Add(assignment);
                    workingPoles.Remove(bestPole);
                    progressMade = true;
                }
            }
        }

        // Build result
        foreach (var driverState in driverStates)
        {
            if (driverState.Assignments.Any())
            {
                result.DriverRoutes.Add(new DriverRoute
                {
                    DriverId = driverState.DriverId,
                    PoleAssignments = driverState.Assignments
                });
            }
        }

        result.UnassignedPoleIds = workingPoles.Select(p => p.Id).ToList();

        return Task.FromResult(result);
    }

    private class DriverState
    {
        public int DriverId { get; set; }
        public int DriverIndex { get; set; }
        public int RemainingMinutes { get; set; }
        public int CurrentLocationMatrixIndex { get; set; }
        public List<PoleAssignment> Assignments { get; set; } = new();
    }
}

