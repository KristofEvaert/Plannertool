namespace TransportPlanner.Domain.Entities;

public enum RouteStatus
{
    Temp = 0,      // Temporary route, not yet saved
    Fixed = 1,     // Fixed route, saved via "Save Day" button
    Planned = 2,   // Legacy: Planned route
    Started = 3,   // Legacy: Route execution started
    Completed = 4  // Legacy: Route execution completed
}

