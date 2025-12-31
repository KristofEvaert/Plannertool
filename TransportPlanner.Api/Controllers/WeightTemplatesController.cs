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
[Route("api/weight-templates")]
[Authorize(Policy = "RequireStaff")]
public class WeightTemplatesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public WeightTemplatesController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<WeightTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WeightTemplateDto>>> GetAll(
        [FromQuery] int? ownerId,
        [FromQuery] int? serviceTypeId,
        [FromQuery] bool includeInactive = false,
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

        var query = _dbContext.WeightTemplates
            .AsNoTracking()
            .Include(t => t.LocationLinks)
            .Include(t => t.LocationGroups)
            .AsQueryable();

        if (ownerId.HasValue && ownerId.Value > 0)
        {
            query = query.Where(t => t.OwnerId == null || t.OwnerId == ownerId.Value);
        }

        if (serviceTypeId.HasValue && serviceTypeId.Value > 0)
        {
            query = query.Where(t => t.ServiceTypeId == null || t.ServiceTypeId == serviceTypeId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        var items = await query
            .OrderBy(t => t.Name)
            .Select(t => new WeightTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                ScopeType = t.ScopeType.ToString(),
                OwnerId = t.OwnerId,
                ServiceTypeId = t.ServiceTypeId,
                IsActive = t.IsActive,
                WeightDistance = t.WeightDistance,
                WeightTravelTime = t.WeightTravelTime,
                WeightOvertime = t.WeightOvertime,
                WeightCost = t.WeightCost,
                WeightDate = t.WeightDate,
                ServiceLocationIds = t.LocationLinks.Select(l => l.ServiceLocationId).ToList(),
                LocationGroupIds = t.LocationGroups.Select(g => g.Id).ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WeightTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeightTemplateDto>> GetById(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.WeightTemplates
            .AsNoTracking()
            .Include(t => t.LocationLinks)
            .Include(t => t.LocationGroups)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template == null)
        {
            return NotFound();
        }

        if (template.OwnerId.HasValue && !CanAccessOwner(template.OwnerId.Value))
        {
            return Forbid();
        }

        return Ok(new WeightTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            ScopeType = template.ScopeType.ToString(),
            OwnerId = template.OwnerId,
            ServiceTypeId = template.ServiceTypeId,
            IsActive = template.IsActive,
            WeightDistance = template.WeightDistance,
            WeightTravelTime = template.WeightTravelTime,
            WeightOvertime = template.WeightOvertime,
            WeightCost = template.WeightCost,
            WeightDate = template.WeightDate,
            ServiceLocationIds = template.LocationLinks.Select(l => l.ServiceLocationId).ToList(),
            LocationGroupIds = template.LocationGroups.Select(g => g.Id).ToList()
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(WeightTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WeightTemplateDto>> Create(
        [FromBody] SaveWeightTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        if (!TryParseScopeType(request.ScopeType, out var scopeType, out var scopeError))
        {
            return BadRequest(new { message = scopeError });
        }

        if (!ValidateScopeRequirements(scopeType, request, out var validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var ownerId = ResolveOwnerId(request.OwnerId);
        if (request.OwnerId.HasValue && ownerId == null)
        {
            return Forbid();
        }

        var locationIds = await ValidateServiceLocationsAsync(ownerId, request.ServiceLocationIds, cancellationToken);
        var groupIds = await ValidateLocationGroupsAsync(ownerId, request.LocationGroupIds, cancellationToken);
        if (locationIds.Count != request.ServiceLocationIds.Count)
        {
            return BadRequest(new { message = "One or more service locations were not found." });
        }
        if (groupIds.Count != request.LocationGroupIds.Count)
        {
            return BadRequest(new { message = "One or more location groups were not found." });
        }

        var nowUtc = DateTime.UtcNow;
        var template = new WeightTemplate
        {
            Name = request.Name.Trim(),
            ScopeType = scopeType,
            OwnerId = ownerId,
            ServiceTypeId = request.ServiceTypeId,
            IsActive = request.IsActive,
            WeightDistance = NormalizeWeight(request.WeightDistance),
            WeightTravelTime = NormalizeWeight(request.WeightTravelTime),
            WeightOvertime = NormalizeWeight(request.WeightOvertime),
            WeightCost = NormalizeWeight(request.WeightCost),
            WeightDate = NormalizeWeight(request.WeightDate),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            CreatedBy = CurrentUserId ?? Guid.Empty
        };

        _dbContext.WeightTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (locationIds.Count > 0)
        {
            var links = locationIds.Select(id => new WeightTemplateLocationLink
            {
                WeightTemplateId = template.Id,
                ServiceLocationId = id
            });
            _dbContext.WeightTemplateLocationLinks.AddRange(links);
        }

        if (groupIds.Count > 0)
        {
            var groups = await _dbContext.LocationGroups
                .Where(g => groupIds.Contains(g.Id))
                .ToListAsync(cancellationToken);
            foreach (var group in groups)
            {
                template.LocationGroups.Add(group);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDtoAsync(template.Id, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(WeightTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeightTemplateDto>> Update(
        [FromRoute] int id,
        [FromBody] SaveWeightTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        if (!TryParseScopeType(request.ScopeType, out var scopeType, out var scopeError))
        {
            return BadRequest(new { message = scopeError });
        }

        if (!ValidateScopeRequirements(scopeType, request, out var validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var template = await _dbContext.WeightTemplates
            .Include(t => t.LocationGroups)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template == null)
        {
            return NotFound();
        }

        if (template.OwnerId.HasValue && !CanAccessOwner(template.OwnerId.Value))
        {
            return Forbid();
        }

        var ownerId = ResolveOwnerId(request.OwnerId) ?? template.OwnerId;
        if (request.OwnerId.HasValue && ownerId == null)
        {
            return Forbid();
        }

        var locationIds = await ValidateServiceLocationsAsync(ownerId, request.ServiceLocationIds, cancellationToken);
        var groupIds = await ValidateLocationGroupsAsync(ownerId, request.LocationGroupIds, cancellationToken);
        if (locationIds.Count != request.ServiceLocationIds.Count)
        {
            return BadRequest(new { message = "One or more service locations were not found." });
        }
        if (groupIds.Count != request.LocationGroupIds.Count)
        {
            return BadRequest(new { message = "One or more location groups were not found." });
        }

        template.Name = request.Name.Trim();
        template.ScopeType = scopeType;
        template.OwnerId = ownerId;
        template.ServiceTypeId = request.ServiceTypeId;
        template.IsActive = request.IsActive;
        template.WeightDistance = NormalizeWeight(request.WeightDistance);
        template.WeightTravelTime = NormalizeWeight(request.WeightTravelTime);
        template.WeightOvertime = NormalizeWeight(request.WeightOvertime);
        template.WeightCost = NormalizeWeight(request.WeightCost);
        template.WeightDate = NormalizeWeight(request.WeightDate);
        template.UpdatedUtc = DateTime.UtcNow;

        var existingLinks = await _dbContext.WeightTemplateLocationLinks
            .Where(l => l.WeightTemplateId == template.Id)
            .ToListAsync(cancellationToken);
        if (existingLinks.Count > 0)
        {
            _dbContext.WeightTemplateLocationLinks.RemoveRange(existingLinks);
        }

        if (locationIds.Count > 0)
        {
            var links = locationIds.Select(id => new WeightTemplateLocationLink
            {
                WeightTemplateId = template.Id,
                ServiceLocationId = id
            });
            _dbContext.WeightTemplateLocationLinks.AddRange(links);
        }

        template.LocationGroups.Clear();
        if (groupIds.Count > 0)
        {
            var groups = await _dbContext.LocationGroups
                .Where(g => groupIds.Contains(g.Id))
                .ToListAsync(cancellationToken);
            foreach (var group in groups)
            {
                template.LocationGroups.Add(group);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDtoAsync(template.Id, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.WeightTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template == null)
        {
            return NotFound();
        }

        if (template.OwnerId.HasValue && !CanAccessOwner(template.OwnerId.Value))
        {
            return Forbid();
        }

        _dbContext.WeightTemplates.Remove(template);
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

    private static decimal NormalizeWeight(decimal value)
    {
        if (value < 1) return 1;
        if (value > 100) return 100;
        return value;
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

        var validIds = await query.Select(sl => sl.Id).ToListAsync(cancellationToken);
        return validIds;
    }

    private async Task<List<int>> ValidateLocationGroupsAsync(
        int? ownerId,
        List<int> locationGroupIds,
        CancellationToken cancellationToken)
    {
        if (locationGroupIds.Count == 0)
        {
            return new List<int>();
        }

        var query = _dbContext.LocationGroups.AsNoTracking().Where(g => locationGroupIds.Contains(g.Id));
        if (ownerId.HasValue)
        {
            query = query.Where(g => g.OwnerId == ownerId.Value);
        }

        var validIds = await query.Select(g => g.Id).ToListAsync(cancellationToken);
        return validIds;
    }

    private static bool TryParseScopeType(string scopeType, out WeightTemplateScopeType parsed, out string? error)
    {
        if (Enum.TryParse<WeightTemplateScopeType>(scopeType, ignoreCase: true, out parsed))
        {
            error = null;
            return true;
        }

        parsed = WeightTemplateScopeType.Global;
        error = "Invalid scope type.";
        return false;
    }

    private static bool ValidateScopeRequirements(
        WeightTemplateScopeType scopeType,
        SaveWeightTemplateRequest request,
        out string? error)
    {
        error = null;
        return scopeType switch
        {
            WeightTemplateScopeType.Owner when !request.OwnerId.HasValue =>
                Fail("OwnerId is required for Owner scope.", out error),
            WeightTemplateScopeType.ServiceType when !request.ServiceTypeId.HasValue =>
                Fail("ServiceTypeId is required for ServiceType scope.", out error),
            WeightTemplateScopeType.Location when request.ServiceLocationIds.Count == 0 =>
                Fail("ServiceLocationIds are required for Location scope.", out error),
            WeightTemplateScopeType.LocationGroup when request.LocationGroupIds.Count == 0 =>
                Fail("LocationGroupIds are required for LocationGroup scope.", out error),
            _ => true
        };
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    private async Task<WeightTemplateDto> BuildDtoAsync(int templateId, CancellationToken cancellationToken)
    {
        var template = await _dbContext.WeightTemplates
            .AsNoTracking()
            .Include(t => t.LocationLinks)
            .Include(t => t.LocationGroups)
            .FirstAsync(t => t.Id == templateId, cancellationToken);

        return new WeightTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            ScopeType = template.ScopeType.ToString(),
            OwnerId = template.OwnerId,
            ServiceTypeId = template.ServiceTypeId,
            IsActive = template.IsActive,
            WeightDistance = template.WeightDistance,
            WeightTravelTime = template.WeightTravelTime,
            WeightOvertime = template.WeightOvertime,
            WeightCost = template.WeightCost,
            WeightDate = template.WeightDate,
            ServiceLocationIds = template.LocationLinks.Select(l => l.ServiceLocationId).ToList(),
            LocationGroupIds = template.LocationGroups.Select(g => g.Id).ToList()
        };
    }
}
