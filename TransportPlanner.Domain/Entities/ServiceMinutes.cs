namespace TransportPlanner.Domain.Entities;

public class ServiceMinutes
{
    public int Id { get; set; }
    public DateTime? FixedDate { get; set; }
    public ServiceStatus Status { get; set; }
}

