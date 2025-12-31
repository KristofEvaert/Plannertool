using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/location-groups")]
[Authorize(Policy = "RequireStaff")]
public class LocationGroupsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public LocationGroupsController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<LocationGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<LocationGroupDto>>> GetAll(
        [FromQuery] int? ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSuperAdmin)
        {
            ownerId = CurrentOwnerId;
        }

        if (ownerId.HasValue && ownerId.Value > 0 && !CanAccessOwner(ownerId.Value))
        {
            return Forbid();
        }

        var query = _dbContext.LocationGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .AsQueryable();

        if (ownerId.HasValue && ownerId.Value > 0)
        {
            query = query.Where(g => g.OwnerId == ownerId.Value);
        }

        var items = await query
            .OrderBy(g => g.Name)
            .Select(g => new LocationGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                OwnerId = g.OwnerId,
                ServiceLocationIds = g.Members.Select(m => m.ServiceLocationId).ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LocationGroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocationGroupDto>> GetById(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.LocationGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group == null)
        {
            return NotFound();
        }

        if (group.OwnerId.HasValue && !CanAccessOwner(group.OwnerId.Value))
        {
            return Forbid();
        }

        return Ok(new LocationGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            OwnerId = group.OwnerId,
            ServiceLocationIds = group.Members.Select(m => m.ServiceLocationId).ToList()
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(LocationGroupDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LocationGroupDto>> Create(
        [FromBody] SaveLocationGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        var ownerId = ResolveOwnerId(request.OwnerId);
        if (request.OwnerId.HasValue && ownerId == null)
        {
            return Forbid();
        }

        var serviceLocationIds = await ValidateServiceLocationsAsync(ownerId, request.ServiceLocationIds, cancellationToken);
        if (serviceLocationIds.Count != request.ServiceLocationIds.Count)
        {
            return BadRequest(new { message = "One or more service locations were not found." });
        }

        var group = new LocationGroup
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerId = ownerId
        };

        _dbContext.LocationGroups.Add(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (serviceLocationIds.Count > 0)
        {
            var members = serviceLocationIds.Select(id => new LocationGroupMember
            {
                LocationGroupId = group.Id,
                ServiceLocationId = id
            });
            _dbContext.LocationGroupMembers.AddRange(members);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(await BuildDtoAsync(group.Id, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(LocationGroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocationGroupDto>> Update(
        [FromRoute] int id,
        [FromBody] SaveLocationGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        var group = await _dbContext.LocationGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group == null)
        {
            return NotFound();
        }

        if (group.OwnerId.HasValue && !CanAccessOwner(group.OwnerId.Value))
        {
            return Forbid();
        }

        var ownerId = ResolveOwnerId(request.OwnerId) ?? group.OwnerId;
        if (request.OwnerId.HasValue && ownerId == null)
        {
            return Forbid();
        }

        var serviceLocationIds = await ValidateServiceLocationsAsync(ownerId, request.ServiceLocationIds, cancellationToken);
        if (serviceLocationIds.Count != request.ServiceLocationIds.Count)
        {
            return BadRequest(new { message = "One or more service locations were not found." });
        }

        group.Name = request.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        group.OwnerId = ownerId;

        group.Members.Clear();
        if (serviceLocationIds.Count > 0)
        {
            foreach (var idValue in serviceLocationIds)
            {
                group.Members.Add(new LocationGroupMember
                {
                    LocationGroupId = group.Id,
                    ServiceLocationId = idValue
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDtoAsync(group.Id, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.LocationGroups
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group == null)
        {
            return NotFound();
        }

        if (group.OwnerId.HasValue && !CanAccessOwner(group.OwnerId.Value))
        {
            return Forbid();
        }

        _dbContext.LocationGroups.Remove(group);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private int? ResolveOwnerId(int? requestedOwnerId)
    {
        if (IsSuperAdmin)
        {
            return requestedOwnerId;
        }

        if (!CurrentOwnerId.HasValue)
        {
            return null;
        }

        if (requestedOwnerId.HasValue && requestedOwnerId.Value != CurrentOwnerId.Value)
        {
            return null;
        }

        return CurrentOwnerId.Value;
    }

    private async Task<List<int>> ValidateServiceLocationsAsync(
        int? ownerId,
        List<int> serviceLocationIds,
        CancellationToken cancellationToken)
    {
        if (serviceLocationIds.Count == 0)
        {
            return new List<int>();
        }

        var query = _dbContext.ServiceLocations.AsNoTracking().Where(sl => serviceLocationIds.Contains(sl.Id));
        if (ownerId.HasValue)
        {
            query = query.Where(sl => sl.OwnerId == ownerId.Value);
        }

        return await query.Select(sl => sl.Id).ToListAsync(cancellationToken);
    }

    private async Task<LocationGroupDto> BuildDtoAsync(int groupId, CancellationToken cancellationToken)
    {
        var group = await _dbContext.LocationGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .FirstAsync(g => g.Id == groupId, cancellationToken);

        return new LocationGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            OwnerId = group.OwnerId,
            ServiceLocationIds = group.Members.Select(m => m.ServiceLocationId).ToList()
        };
    }
}
