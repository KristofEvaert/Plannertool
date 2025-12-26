namespace TransportPlanner.Application.DTOs;

public class BulkAvailabilityDto
{
    public Guid? DriverToolId { get; set; } // Optional
    public int? DriverErpId { get; set; } // Optional
    public DateOnly Date { get; set; } // Required
    public int StartMinuteOfDay { get; set; } // Required
    public int EndMinuteOfDay { get; set; } // Required
}

