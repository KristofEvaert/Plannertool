namespace TransportPlanner.Application.DTOs;

public class BulkUpsertResultDto
{
    public int DriversCreated { get; set; }
    public int DriversUpdated { get; set; }
    public int AvailabilitiesUpserted { get; set; }
    public List<BulkErrorDto> Errors { get; set; } = new();
}

