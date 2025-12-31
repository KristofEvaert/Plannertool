namespace TransportPlanner.Application.DTOs;

public class WeightTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
    public int? ServiceTypeId { get; set; }
    public bool IsActive { get; set; }
    public decimal WeightDistance { get; set; }
    public decimal WeightTravelTime { get; set; }
    public decimal WeightOvertime { get; set; }
    public decimal WeightCost { get; set; }
    public decimal WeightDate { get; set; }
    public List<int> ServiceLocationIds { get; set; } = new();
}

public class SaveWeightTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "Global";
    public int? OwnerId { get; set; }
    public int? ServiceTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal WeightDistance { get; set; } = 1;
    public decimal WeightTravelTime { get; set; } = 1;
    public decimal WeightOvertime { get; set; } = 1;
    public decimal WeightCost { get; set; } = 1;
    public decimal WeightDate { get; set; } = 1;
    public List<int> ServiceLocationIds { get; set; } = new();
}
