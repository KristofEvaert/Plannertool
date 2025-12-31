using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/system-cost-settings")]
[Authorize(Policy = "RequireAdmin")]
public class SystemCostSettingsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    public SystemCostSettingsController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SystemCostSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemCostSettingsDto>> Get([FromQuery] int? ownerId, CancellationToken cancellationToken = default)
    {
        if (!TryResolveOwnerId(ownerId, out var resolvedOwnerId, out var errorResult))
        {
            return errorResult ?? Forbid();
        }

        var settings = await _dbContext.SystemCostSettings
            .AsNoTracking()
            .Where(s => s.OwnerId == resolvedOwnerId)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            return Ok(new SystemCostSettingsDto
            {
                OwnerId = resolvedOwnerId,
                FuelCostPerKm = 0,
                PersonnelCostPerHour = 0,
                CurrencyCode = "EUR"
            });
        }

        return Ok(new SystemCostSettingsDto
        {
            OwnerId = settings.OwnerId,
            FuelCostPerKm = settings.FuelCostPerKm,
            PersonnelCostPerHour = settings.PersonnelCostPerHour,
            CurrencyCode = settings.CurrencyCode
        });
    }

    [HttpGet("overview")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(List<SystemCostSettingsOverviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SystemCostSettingsOverviewDto>>> GetOverview(
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsSuperAdmin)
        {
            return Forbid();
        }

        var ownersQuery = _dbContext.ServiceLocationOwners.AsNoTracking();
        if (!includeInactive)
        {
            ownersQuery = ownersQuery.Where(owner => owner.IsActive);
        }

        var owners = await ownersQuery
            .OrderBy(owner => owner.Name)
            .Select(owner => new { owner.Id, owner.Code, owner.Name, owner.IsActive })
            .ToListAsync(cancellationToken);

        var settings = await _dbContext.SystemCostSettings
            .AsNoTracking()
            .Where(s => s.OwnerId != null)
            .OrderByDescending(s => s.Id)
            .ToListAsync(cancellationToken);

        var latestByOwner = settings
            .Where(s => s.OwnerId.HasValue)
            .GroupBy(s => s.OwnerId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var results = owners
            .Select(owner => {
                if (!latestByOwner.TryGetValue(owner.Id, out var current))
                {
                    return new SystemCostSettingsOverviewDto
                    {
                        OwnerId = owner.Id,
                        OwnerCode = owner.Code,
                        OwnerName = owner.Name,
                        OwnerIsActive = owner.IsActive,
                        FuelCostPerKm = 0,
                        PersonnelCostPerHour = 0,
                        CurrencyCode = "EUR",
                        UpdatedAtUtc = null
                    };
                }

                return new SystemCostSettingsOverviewDto
                {
                    OwnerId = owner.Id,
                    OwnerCode = owner.Code,
                    OwnerName = owner.Name,
                    OwnerIsActive = owner.IsActive,
                    FuelCostPerKm = current.FuelCostPerKm,
                    PersonnelCostPerHour = current.PersonnelCostPerHour,
                    CurrencyCode = current.CurrencyCode,
                    UpdatedAtUtc = current.UpdatedAtUtc
                };
            })
            .ToList();

        return Ok(results);
    }

    [HttpPut]
    [ProducesResponseType(typeof(SystemCostSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemCostSettingsDto>> Update(
        [FromBody] SystemCostSettingsDto request,
        [FromQuery] int? ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveOwnerId(ownerId ?? request.OwnerId, out var resolvedOwnerId, out var errorResult))
        {
            return errorResult ?? Forbid();
        }

        if (request.FuelCostPerKm < 0 || request.PersonnelCostPerHour < 0)
        {
            return BadRequest(new { message = "Costs must be >= 0." });
        }

        var settings = await _dbContext.SystemCostSettings
            .Where(s => s.OwnerId == resolvedOwnerId)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            settings = new Domain.Entities.SystemCostSettings
            {
                OwnerId = resolvedOwnerId
            };
            _dbContext.SystemCostSettings.Add(settings);
        }

        settings.OwnerId = resolvedOwnerId;
        settings.FuelCostPerKm = request.FuelCostPerKm;
        settings.PersonnelCostPerHour = request.PersonnelCostPerHour;
        settings.CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "EUR" : request.CurrencyCode.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new SystemCostSettingsDto
        {
            OwnerId = settings.OwnerId,
            FuelCostPerKm = settings.FuelCostPerKm,
            PersonnelCostPerHour = settings.PersonnelCostPerHour,
            CurrencyCode = settings.CurrencyCode
        });
    }

    private bool TryResolveOwnerId(int? requestedOwnerId, out int resolvedOwnerId, out ActionResult? errorResult)
    {
        if (IsSuperAdmin)
        {
            if (!requestedOwnerId.HasValue)
            {
                resolvedOwnerId = 0;
                errorResult = BadRequest(new { message = "OwnerId is required for super admins." });
                return false;
            }

            resolvedOwnerId = requestedOwnerId.Value;
            errorResult = null;
            return true;
        }

        if (!CurrentOwnerId.HasValue)
        {
            resolvedOwnerId = 0;
            errorResult = Forbid();
            return false;
        }

        if (requestedOwnerId.HasValue && requestedOwnerId.Value != CurrentOwnerId.Value)
        {
            resolvedOwnerId = 0;
            errorResult = Forbid();
            return false;
        }

        resolvedOwnerId = CurrentOwnerId.Value;
        errorResult = null;
        return true;
    }
}
