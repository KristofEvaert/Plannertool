namespace TransportPlanner.Application.DTOs;

public class UpsertAvailabilityRequest
{
    public int StartMinuteOfDay { get; set; } // 0..1439
    public int EndMinuteOfDay { get; set; }   // 1..1440, must be > StartMinuteOfDay
}

