namespace TransportPlanner.Application.DTOs;

public class BulkUpsertDriversRequest
{
    public List<BulkDriverDto> Drivers { get; set; } = new();
    public List<BulkAvailabilityDto> Availabilities { get; set; } = new();
}

