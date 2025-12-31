namespace TransportPlanner.Application.DTOs;

public class UpdateDriverRequest
{
    public int ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StartAddress { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public int DefaultServiceMinutes { get; set; }
    public int MaxWorkMinutesPerDay { get; set; }
    public int OwnerId { get; set; } // Required
    public bool IsActive { get; set; }
    public List<int>? ServiceTypeIds { get; set; }
}
