using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Infrastructure.Options;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// OR-Tools based route planner using Vehicle Routing Problem (VRP) solver.
/// To switch to this planner, set "Planning:Engine" to "OrTools" in appsettings.json
/// </summary>
public class OrToolsRoutePlanner : IRoutePlanner
{
    private readonly ILogger<OrToolsRoutePlanner> _logger;
    private readonly PlanningOptions _planningOptions;

    public OrToolsRoutePlanner(
        ILogger<OrToolsRoutePlanner> logger,
        IOptions<PlanningOptions> planningOptions)
    {
        _logger = logger;
        _planningOptions = planningOptions.Value;
    }

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

        // Total locations: driver starts + poles
        var numDrivers = input.Drivers.Count;
        var numPoles = input.Poles.Count;
        var numLocations = numDrivers + numPoles;

        // Create routing index manager
        // Each driver has its own start node (depot), ends at the same node
        var starts = new int[numDrivers];
        var ends = new int[numDrivers];
        for (int i = 0; i < numDrivers; i++)
        {
            starts[i] = i; // Driver start nodes are first numDrivers indices
            ends[i] = i;   // End at same start location
        }

        var manager = new RoutingIndexManager(numLocations, numDrivers, starts, ends);
        var routing = new RoutingModel(manager);

        // Create transit callback that includes both travel time and service time
        // Service time is added at the destination node
        int timeCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
        {
            var fromNode = manager.IndexToNode(fromIndex);
            var toNode = manager.IndexToNode(toIndex);
            var travelTime = travelTimeMatrix[fromNode, toNode];
            
            // Add service time at destination if it's a pole (not a driver start node)
            if (toNode >= numDrivers)
            {
                var poleIndex = toNode - numDrivers;
                var serviceTime = input.Poles[poleIndex].ServiceMinutes;
                return travelTime + serviceTime;
            }
            
            return travelTime;
        });

        // Set arc cost to minimize travel time
        routing.SetArcCostEvaluatorOfAllVehicles(timeCallbackIndex);

        // Create time dimension (includes travel time + service time)
        string time = "Time";
        routing.AddDimension(
            timeCallbackIndex,
            0, // Slack: no waiting time allowed
            int.MaxValue, // Vehicle maximum capacity (will be set per vehicle)
            false, // Start cumul to zero
            time);

        var timeDimension = routing.GetMutableDimension(time);

        // Add disjunction to allow dropping pole if it doesn't fit
        // Use constant HIGH penalty to encourage assignment over dropping
        // DueDate only affects ordering (candidates are pre-sorted), not penalty
        const long penalty = 1_000_000; // High constant penalty encourages packing as many as possible
        
        for (int i = 0; i < numPoles; i++)
        {
            var poleIndex = manager.NodeToIndex(numDrivers + i);
            
            routing.AddDisjunction(
                new long[] { poleIndex },
                penalty);
        }

        // Set vehicle constraints (max work time per driver)
        for (int vehicleId = 0; vehicleId < numDrivers; vehicleId++)
        {
            var driver = input.Drivers[vehicleId];
            var maxMinutes = Math.Min(driver.AvailabilityMinutes, driver.MaxWorkMinutesPerDay);
            
            // Set maximum time constraint for both start and end nodes
            var startIndex = routing.Start(vehicleId);
            var endIndex = routing.End(vehicleId);
            
            // Start at time 0
            timeDimension.CumulVar(startIndex).SetRange(0, 0);
            
            // End node must be within max work time (includes return trip)
            timeDimension.CumulVar(endIndex).SetMax(maxMinutes);
        }

        // Set objective: minimize travel time AND maximize number of assigned poles
        // We do this by setting a high penalty for unassigned poles (via disjunction)
        // and minimizing total travel time
        routing.SetArcCostEvaluatorOfAllVehicles(timeCallbackIndex);

        // Solve with configurable search parameters for performance
        var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        
        // Back to basics: Force bounded runtime for speed
        var orToolsConfig = _planningOptions.OrTools ?? new OrToolsOptions();
        
        // Force fast settings
        searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
        searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        searchParameters.TimeLimit = new Duration { Seconds = Math.Max(1, orToolsConfig.TimeLimitSeconds) }; // Default 2s from config
        searchParameters.SolutionLimit = 1; // Stop after first good solution

        var solution = routing.SolveWithParameters(searchParameters);

        if (solution == null)
        {
            // If no solution found, return all poles as unassigned
            result.UnassignedPoleIds = input.Poles.Select(p => p.Id).ToList();
            return Task.FromResult(result);
        }

        // Extract routes from solution
        for (int vehicleId = 0; vehicleId < numDrivers; vehicleId++)
        {
            var route = new DriverRoute
            {
                DriverId = input.Drivers[vehicleId].Id,
                PoleAssignments = new List<PoleAssignment>()
            };

            long index = routing.Start(vehicleId);
            int sequence = 0;
            int previousNode = -1;

            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                
                // Check if this is a pole node (not a driver start node)
                if (node >= numDrivers)
                {
                    var poleIndex = node - numDrivers;
                    var pole = input.Poles[poleIndex];
                    
                    int travelMinutes = 0;
                    if (previousNode >= 0)
                    {
                        travelMinutes = travelTimeMatrix[previousNode, node];
                    }

                    route.PoleAssignments.Add(new PoleAssignment
                    {
                        PoleId = pole.Id,
                        Sequence = ++sequence,
                        TravelMinutesFromPrev = travelMinutes
                    });
                }

                previousNode = node;
                index = solution.Value(routing.NextVar(index));
            }

            if (route.PoleAssignments.Any())
            {
                result.DriverRoutes.Add(route);
            }
        }

        // Find unassigned poles (those not in any route)
        var assignedPoleIds = result.DriverRoutes
            .SelectMany(r => r.PoleAssignments.Select(a => a.PoleId))
            .ToHashSet();

        result.UnassignedPoleIds = input.Poles
            .Where(p => !assignedPoleIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

        return Task.FromResult(result);
    }
}

