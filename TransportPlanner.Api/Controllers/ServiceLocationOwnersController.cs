using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/service-location-owners")]
[Authorize(Policy = "RequireStaff")]
public class ServiceLocationOwnersController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    public ServiceLocationOwnersController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets list of service location owners (read-only)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ServiceLocationOwnerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceLocationOwnerDto>>> GetServiceLocationOwners(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ServiceLocationOwners.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(so => so.IsActive);
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue)
            {
                return Forbid();
            }
            query = query.Where(so => so.Id == CurrentOwnerId.Value);
        }

        var owners = await query
            .OrderBy(so => so.Name)
            .Select(so => new ServiceLocationOwnerDto
            {
                Id = so.Id,
                Code = so.Code,
                Name = so.Name,
                IsActive = so.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(owners);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceLocationOwnerDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ServiceLocationOwnerDto>> CreateOwner(
        [FromBody] CreateServiceLocationOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Code and Name are required." });
        }

        var exists = await _dbContext.ServiceLocationOwners
            .AnyAsync(o => o.Code == request.Code, cancellationToken);
        if (exists)
        {
            return Conflict(new { message = "An owner with this code already exists." });
        }

        var now = DateTime.UtcNow;
        var owner = new Domain.Entities.ServiceLocationOwner
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.ServiceLocationOwners.Add(owner);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceLocationOwnerDto
        {
            Id = owner.Id,
            Code = owner.Code,
            Name = owner.Name,
            IsActive = owner.IsActive
        };

        return CreatedAtAction(nameof(GetServiceLocationOwners), new { id = owner.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceLocationOwnerDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceLocationOwnerDto>> UpdateOwner(
        [FromRoute] int id,
        [FromBody] UpdateServiceLocationOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = await _dbContext.ServiceLocationOwners.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (owner == null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Code and Name are required." });
        }

        var duplicate = await _dbContext.ServiceLocationOwners
            .AnyAsync(o => o.Id != id && o.Code == request.Code, cancellationToken);
        if (duplicate)
        {
            return Conflict(new { message = "Another owner with this code already exists." });
        }

        owner.Code = request.Code.Trim();
        owner.Name = request.Name.Trim();
        owner.IsActive = request.IsActive;
        owner.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceLocationOwnerDto
        {
            Id = owner.Id,
            Code = owner.Code,
            Name = owner.Name,
            IsActive = owner.IsActive
        };

        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOwner(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var owner = await _dbContext.ServiceLocationOwners.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (owner == null) return NotFound();

        // Soft delete: deactivate
        owner.IsActive = false;
        owner.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public class ServiceLocationOwnerDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public record CreateServiceLocationOwnerRequest(string Code, string Name, bool IsActive = true);
    public record UpdateServiceLocationOwnerRequest(string Code, string Name, bool IsActive = true);
}
