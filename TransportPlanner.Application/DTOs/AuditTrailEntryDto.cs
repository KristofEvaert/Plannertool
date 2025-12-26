namespace TransportPlanner.Application.DTOs;

public class AuditTrailEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Query { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public List<string> Roles { get; set; } = new();
    public int? OwnerId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Endpoint { get; set; }
    public string? RequestHeaders { get; set; }
    public string? Body { get; set; }
    public bool BodyTruncated { get; set; }
    public string? ResponseBody { get; set; }
    public bool ResponseBodyTruncated { get; set; }
    public string? TraceId { get; set; }
}
