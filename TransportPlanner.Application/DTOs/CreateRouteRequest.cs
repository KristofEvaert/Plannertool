namespace TransportPlanner.Application.DTOs;

public class CreateRouteRequest
{
    public DateTime Date { get; set; }
    public int OwnerId { get; set; }
    // ServiceTypeId removed - routes are identified by Date, DriverId, and OwnerId only
    public Guid DriverToolId { get; set; } // Use ToolId instead of DriverId
    public int TotalMinutes { get; set; }
    public double TotalKm { get; set; }
    public string? StartAddress { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public string? EndAddress { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public bool? StartIsHotel { get; set; }
    public bool? EndIsHotel { get; set; }
    public int? WeightTemplateId { get; set; }
    public List<CreateRouteStopRequest> Stops { get; set; } = new List<CreateRouteStopRequest>();
}

public class CreateRouteStopRequest
{
    public int Sequence { get; set; }
    public Guid? ServiceLocationToolId { get; set; } // Use ToolId instead of ServiceLocationId
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int ServiceMinutes { get; set; }
    public double TravelKmFromPrev { get; set; }
    public int TravelMinutesFromPrev { get; set; }
}

