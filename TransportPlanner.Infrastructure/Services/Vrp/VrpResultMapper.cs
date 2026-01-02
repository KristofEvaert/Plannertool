using Google.OrTools.ConstraintSolver;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public class VrpResultMapper : IVrpResultMapper
{
    private const string TimeDimensionName = "Time";

    public VrpSolution MapSolution(
        VrpInput input,
        RoutingModel routing,
        RoutingIndexManager manager,
        Assignment solution,
        MatrixResult matrix)
    {
        var timeDimension = routing.GetDimensionOrDie(TimeDimensionName);
        var unassigned = new List<int>();

        foreach (var jobEntry in input.JobNodeIndices)
        {
            var assigned = jobEntry.Value.Any(nodeIndex => IsAssigned(nodeIndex, routing, manager, solution));
            if (!assigned)
            {
                unassigned.Add(jobEntry.Key);
            }
        }

        var routes = new List<VrpRoutePlan>();

        for (var vehicleId = 0; vehicleId < input.Drivers.Count; vehicleId++)
        {
            var driver = input.Drivers[vehicleId].Driver;
            var stops = new List<VrpStopPlan>();
            var index = routing.Start(vehicleId);
            var startNode = manager.IndexToNode(index);
            var previousNode = startNode;
            var startMinute = (int)solution.Value(timeDimension.CumulVar(index));

            double totalDistance = 0;
            int totalTravelMinutes = 0;
            int totalServiceMinutes = 0;
            var sequence = 1;

            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                var nodeInfo = input.Nodes[node];

                if (nodeInfo.Type == VrpNodeType.Job && nodeInfo.JobId.HasValue)
                {
                    var job = input.JobsById[nodeInfo.JobId.Value];
                    var travelMinutes = matrix.TravelMinutes[previousNode, node];
                    var travelKm = matrix.DistanceKm[previousNode, node];
                    var arrivalMinute = (int)solution.Value(timeDimension.CumulVar(index));
                    var departureMinute = arrivalMinute + job.ServiceMinutes;

                    stops.Add(new VrpStopPlan(
                        job.LocationId,
                        job.ToolId,
                        job.Name,
                        sequence++,
                        job.Latitude,
                        job.Longitude,
                        job.ServiceMinutes,
                        arrivalMinute,
                        departureMinute,
                        travelMinutes,
                        travelKm));

                    totalDistance += travelKm;
                    totalTravelMinutes += travelMinutes;
                    totalServiceMinutes += job.ServiceMinutes;
                    previousNode = node;
                }

                index = solution.Value(routing.NextVar(index));
            }

            if (stops.Count == 0)
            {
                continue;
            }

            var endNode = manager.IndexToNode(index);
            if (previousNode != endNode)
            {
                totalDistance += matrix.DistanceKm[previousNode, endNode];
                totalTravelMinutes += matrix.TravelMinutes[previousNode, endNode];
            }

            var totalMinutes = Math.Max(0, totalTravelMinutes + totalServiceMinutes);

            routes.Add(new VrpRoutePlan(
                driver,
                stops,
                totalDistance,
                totalMinutes,
                totalServiceMinutes,
                totalTravelMinutes));
        }

        return new VrpSolution(routes, unassigned);
    }

    private static bool IsAssigned(int nodeIndex, RoutingModel routing, RoutingIndexManager manager, Assignment solution)
    {
        var index = manager.NodeToIndex(nodeIndex);
        return solution.Value(routing.NextVar(index)) != index;
    }
}
