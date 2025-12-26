using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Api.Services.AuditTrail;

public interface IAuditTrailStore
{
    Task WriteAsync(AuditTrailEntryDto entry, CancellationToken cancellationToken);
    Task<PagedResult<AuditTrailEntryDto>> QueryAsync(AuditTrailQuery query, CancellationToken cancellationToken);
}

public class AuditTrailQuery
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Method { get; set; }
    public string? PathContains { get; set; }
    public string? UserEmailContains { get; set; }
    public Guid? UserId { get; set; }
    public int? StatusCode { get; set; }
    public int? OwnerId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
