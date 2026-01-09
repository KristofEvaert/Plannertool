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
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsSuperAdmin)
        {
            ownerId = CurrentOwnerId;
        }

        if (!IsSuperAdmin && !ownerId.HasValue)
        {
            return Forbid();
        }

        if (ownerId.HasValue && ownerId.Value > 0 && !CanAccessOwner(ownerId.Value))
        {
            return Forbid();
        }

        var query = _dbContext.WeightTemplates
            .AsNoTracking()
            .Where(t => t.OwnerId != null && t.ScopeType == WeightTemplateScopeType.Owner)
            .AsQueryable();

        if (ownerId.HasValue && ownerId.Value > 0)
        {
            query = query.Where(t => t.OwnerId == ownerId.Value);
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
                OwnerId = t.OwnerId,
                IsActive = t.IsActive,
                AlgorithmType = t.AlgorithmType,
                DueDatePriority = ClampInt(t.WeightDate, 0, 100),
                WorktimeDeviationPercent = ClampInt(t.WeightOvertime, 0, 50),
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
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template == null || template.OwnerId == null || template.ScopeType != WeightTemplateScopeType.Owner)
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
            OwnerId = template.OwnerId,
            IsActive = template.IsActive,
            AlgorithmType = template.AlgorithmType,
            DueDatePriority = ClampInt(template.WeightDate, 0, 100),
            WorktimeDeviationPercent = ClampInt(template.WeightOvertime, 0, 50),
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireStaff")]
    [ProducesResponseType(typeof(WeightTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WeightTemplateDto>> Create(
        [FromBody] SaveWeightTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        var ownerId = ResolveOwnerId(request.OwnerId);
        if (IsSuperAdmin && !request.OwnerId.HasValue)
        {
            return BadRequest(new { message = "OwnerId is required." });
        }

        if (ownerId == null)
        {
            return Forbid();
        }

        var algorithmType = NormalizeAlgorithmType(request.AlgorithmType);
        if (!IsSupportedAlgorithmType(algorithmType))
        {
            return BadRequest(new { message = $"Unsupported algorithm '{request.AlgorithmType}'." });
        }

        var dueDatePriority = ClampInt(request.DueDatePriority, 0, 100);
        var worktimeDeviationPercent = ClampInt(request.WorktimeDeviationPercent, 0, 50);
        var nowUtc = DateTime.UtcNow;
        var template = new WeightTemplate
        {
            Name = request.Name.Trim(),
            ScopeType = WeightTemplateScopeType.Owner,
            OwnerId = ownerId,
            IsActive = request.IsActive,
            AlgorithmType = algorithmType,
            WeightDate = dueDatePriority,
            WeightOvertime = worktimeDeviationPercent,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            CreatedBy = CurrentUserId ?? Guid.Empty
        };

        _dbContext.WeightTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDtoAsync(template.Id, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequireStaff")]
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

        var template = await _dbContext.WeightTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template == null || template.OwnerId == null || template.ScopeType != WeightTemplateScopeType.Owner)
        {
            return NotFound();
        }

        if (template.OwnerId.HasValue && !CanAccessOwner(template.OwnerId.Value))
        {
            return Forbid();
        }

        var ownerId = ResolveOwnerId(request.OwnerId) ?? template.OwnerId;
        if (ownerId == null)
        {
            return Forbid();
        }

        var algorithmType = string.IsNullOrWhiteSpace(request.AlgorithmType)
            ? template.AlgorithmType
            : NormalizeAlgorithmType(request.AlgorithmType);
        if (!IsSupportedAlgorithmType(algorithmType))
        {
            return BadRequest(new { message = $"Unsupported algorithm '{request.AlgorithmType}'." });
        }

        var dueDatePriority = ClampInt(request.DueDatePriority, 0, 100);
        var worktimeDeviationPercent = ClampInt(request.WorktimeDeviationPercent, 0, 50);
        template.Name = request.Name.Trim();
        template.ScopeType = WeightTemplateScopeType.Owner;
        template.OwnerId = ownerId;
        template.IsActive = request.IsActive;
        template.AlgorithmType = algorithmType;
        template.WeightDate = dueDatePriority;
        template.WeightOvertime = worktimeDeviationPercent;
        template.UpdatedUtc = DateTime.UtcNow;

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

        if (template == null || template.OwnerId == null || template.ScopeType != WeightTemplateScopeType.Owner)
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

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static int ClampInt(decimal value, int min, int max)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return ClampInt(rounded, min, max);
    }

    private async Task<WeightTemplateDto> BuildDtoAsync(int templateId, CancellationToken cancellationToken)
    {
        var template = await _dbContext.WeightTemplates
            .AsNoTracking()
            .FirstAsync(t => t.Id == templateId, cancellationToken);

        return new WeightTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            OwnerId = template.OwnerId,
            IsActive = template.IsActive,
            AlgorithmType = template.AlgorithmType,
            DueDatePriority = ClampInt(template.WeightDate, 0, 100),
            WorktimeDeviationPercent = ClampInt(template.WeightOvertime, 0, 50),
        };
    }

    private static string NormalizeAlgorithmType(string? algorithmType)
    {
        return string.IsNullOrWhiteSpace(algorithmType) ? "Lollipop" : algorithmType.Trim();
    }

    private static bool IsSupportedAlgorithmType(string algorithmType)
    {
        return string.Equals(algorithmType, "Lollipop", StringComparison.OrdinalIgnoreCase);
    }
}
