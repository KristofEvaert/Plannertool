namespace TransportPlanner.Application.DTOs;

public class ImportPolesResultDto
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Total { get; set; }
    public DateTime FromDueDate { get; set; }
    public DateTime ToDueDate { get; set; }
}

