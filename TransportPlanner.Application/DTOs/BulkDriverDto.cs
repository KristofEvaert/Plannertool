namespace TransportPlanner.Application.DTOs;

public class BulkDriverDto
{
    public Guid? ToolId { get; set; } // Optional
    public int ErpId { get; set; } // Required
    public string Name { get; set; } = string.Empty; // Required
    public string? StartLocationLabel { get; set; }
    public string? StartAddress { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public int? DefaultServiceMinutes { get; set; } // Default 20
    public int? MaxWorkMinutesPerDay { get; set; } // Default 480
    public int? OwnerId { get; set; } // Preferred - lookup by id
    public string? OwnerCode { get; set; } // Optional alternative - lookup by code
    public bool? IsActive { get; set; } // MUST be ignored or rejected (no soft delete)
}
