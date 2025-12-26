using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Api.Services.AuditTrail;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/audit-trail")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class AuditTrailController : ControllerBase
{
    private readonly IAuditTrailStore _store;

    public AuditTrailController(IAuditTrailStore store)
    {
        _store = store;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditTrailEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditTrailEntryDto>>> Get(
        [FromQuery] AuditTrailQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _store.QueryAsync(query, cancellationToken);
        return Ok(result);
    }
}
