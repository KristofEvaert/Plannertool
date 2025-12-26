namespace TransportPlanner.Application.DTOs;

public class UpdateRouteRequest
{
    public int TotalMinutes { get; set; }
    public double TotalKm { get; set; }
    public List<UpdateRouteStopItemRequest> Stops { get; set; } = new List<UpdateRouteStopItemRequest>();
}

public class UpdateRouteStopItemRequest
{
    public int? Id { get; set; } // null for new stops
    public int Sequence { get; set; }
    public int? ServiceLocationId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int ServiceMinutes { get; set; }
    public double TravelKmFromPrev { get; set; }
    public int TravelMinutesFromPrev { get; set; }
}

