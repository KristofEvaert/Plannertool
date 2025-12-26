namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityDto
{
    public string Date { get; set; } = string.Empty; // ISO date string (yyyy-MM-dd)
    public int StartMinuteOfDay { get; set; }
    public int EndMinuteOfDay { get; set; }
    public int AvailableMinutes { get; set; } // Derived: EndMinuteOfDay - StartMinuteOfDay
}

