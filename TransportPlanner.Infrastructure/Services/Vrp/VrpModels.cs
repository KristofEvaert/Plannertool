using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public sealed record VrpSolveRequest(
    DateTime Date,
    int OwnerId,
    IReadOnlyList<Guid>? ServiceLocationToolIds,
    int? MaxStopsPerDriver,
    VrpWeightSet Weights,
    VrpCostSettings CostSettings,
    bool RequireServiceTypeMatch,
    bool NormalizeWeights,
    int? WeightTemplateId);

public sealed record VrpSolveResult(
    IReadOnlyList<RouteDto> Routes,
    IReadOnlyList<string> SkippedDrivers,
    IReadOnlyList<int> UnassignedServiceLocationIds);

public sealed record VrpWeightSet(double Time, double Distance, double Date, double Cost, double Overtime);

public sealed record VrpCostSettings(decimal FuelCostPerKm, decimal PersonnelCostPerHour, string CurrencyCode);

public sealed record VrpTimeWindow(int StartMinute, int EndMinute);

public sealed record VrpJob(
    int LocationId,
    Guid ToolId,
    string Name,
    int ServiceTypeId,
    double Latitude,
    double Longitude,
    int ServiceMinutes,
    double DuePenalty,
    IReadOnlyList<VrpTimeWindow> Windows);

public sealed record VrpDriver(
    Driver Driver,
    int AvailabilityStartMinute,
    int AvailabilityEndMinute,
    int MaxRouteMinutes,
    IReadOnlyList<int> ServiceTypeIds);

public enum VrpNodeType
{
    Start,
    Job
}

public sealed record VrpNode(
    int NodeIndex,
    VrpNodeType Type,
    int? JobId,
    double Latitude,
    double Longitude,
    int ServiceMinutes,
    double DuePenalty,
    int? ServiceTypeId);

public sealed record VrpInput(
    DateTime Date,
    int OwnerId,
    IReadOnlyList<VrpDriver> Drivers,
    IReadOnlyList<VrpJob> Jobs,
    IReadOnlyList<VrpNode> Nodes,
    IReadOnlyDictionary<int, VrpTimeWindow> NodeWindows,
    IReadOnlyDictionary<int, List<int>> JobNodeIndices,
    IReadOnlyDictionary<int, VrpJob> JobsById);

public sealed record VrpInputBuildResult(
    VrpInput Input,
    IReadOnlyList<string> SkippedDrivers,
    IReadOnlyList<int> ExcludedLocationIds);

public sealed record VrpSolution(
    IReadOnlyList<VrpRoutePlan> Routes,
    IReadOnlyList<int> UnassignedLocationIds);

public sealed record MatrixPoint(double Latitude, double Longitude);

public sealed record MatrixResult(int[,] TravelMinutes, double[,] DistanceKm);

public sealed record VrpStopPlan(
    int ServiceLocationId,
    Guid ServiceLocationToolId,
    string Name,
    int Sequence,
    double Latitude,
    double Longitude,
    int ServiceMinutes,
    int ArrivalMinute,
    int DepartureMinute,
    int TravelMinutesFromPrev,
    double TravelKmFromPrev);

public sealed record VrpRoutePlan(
    Driver Driver,
    IReadOnlyList<VrpStopPlan> Stops,
    double TotalDistanceKm,
    int TotalMinutes,
    int TotalServiceMinutes,
    int TotalTravelMinutes);
