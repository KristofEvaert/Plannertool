namespace TransportPlanner.Application.DTOs;

public class CreateDriverRequest
{
    public int ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StartAddress { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public int DefaultServiceMinutes { get; set; } = 20;
    public int MaxWorkMinutesPerDay { get; set; } = 480;
    public int OwnerId { get; set; } // Required
    public bool IsActive { get; set; } = true;
}
