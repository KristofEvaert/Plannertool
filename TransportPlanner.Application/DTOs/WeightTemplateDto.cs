namespace TransportPlanner.Application.DTOs;

public class WeightTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
    public bool IsActive { get; set; }
    public string AlgorithmType { get; set; } = "Lollipop";
    public int DueDatePriority { get; set; }
    public int WorktimeDeviationPercent { get; set; }
}

public class SaveWeightTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
    public bool IsActive { get; set; } = true;
    public string AlgorithmType { get; set; } = "Lollipop";
    public int DueDatePriority { get; set; } = 50;
    public int WorktimeDeviationPercent { get; set; } = 10;
}
