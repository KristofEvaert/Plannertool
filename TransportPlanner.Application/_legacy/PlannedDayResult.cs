namespace TransportPlanner.Application.DTOs;

public class PlannedDayResult
{
    public List<DriverRoute> DriverRoutes { get; set; } = new();
    public List<int> UnassignedPoleIds { get; set; } = new();
}

public class DriverRoute
{
    public int DriverId { get; set; }
    public List<PoleAssignment> PoleAssignments { get; set; } = new();
}

public class PoleAssignment
{
    public int PoleId { get; set; }
    public int Sequence { get; set; }
    public int TravelMinutesFromPrev { get; set; }
    public decimal TravelKmFromPrev { get; set; }
}

