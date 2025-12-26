namespace TransportPlanner.Domain.Entities;

public enum RouteStopStatus
{
    Pending = 0,
    Arrived = 1,
    Completed = 2,
    Skipped = 3,
    NotVisited = 4
}

