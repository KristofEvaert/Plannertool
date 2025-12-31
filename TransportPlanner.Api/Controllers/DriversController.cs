using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Exceptions;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireStaff")]
public class DriversController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IGeocodingService _geocodingService;

    public DriversController(TransportPlannerDbContext dbContext, IGeocodingService geocodingService)
    {
        _dbContext = dbContext;
        _geocodingService = geocodingService;
    }

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    private bool TryGetScopedOwner(out int ownerId)
    {
        ownerId = 0;
        if (IsSuperAdmin)
        {
            return true;
        }
        if (CurrentOwnerId.HasValue)
        {
            ownerId = CurrentOwnerId.Value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Helper method to get Owner name by ID (no FK constraint)
    /// </summary>
    private async Task<string> GetOwnerNameAsync(int ownerId, CancellationToken cancellationToken)
    {
        if (ownerId <= 0) return string.Empty;
        
        var owner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(so => so.Id == ownerId, cancellationToken);
        
        return owner?.Name ?? string.Empty;
    }

    private async Task<Dictionary<int, List<int>>> GetServiceTypeIdsByDriverAsync(
        List<int> driverIds,
        CancellationToken cancellationToken)
    {
        if (driverIds.Count == 0)
        {
            return new Dictionary<int, List<int>>();
        }

        return await _dbContext.DriverServiceTypes
            .AsNoTracking()
            .Where(dst => driverIds.Contains(dst.DriverId))
            .GroupBy(dst => dst.DriverId)
            .Select(g => new { DriverId = g.Key, ServiceTypeIds = g.Select(x => x.ServiceTypeId).ToList() })
            .ToDictionaryAsync(x => x.DriverId, x => x.ServiceTypeIds, cancellationToken);
    }

    /// <summary>
    /// Gets all drivers
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DriverDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DriverDto>>> GetDrivers(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Drivers.AsNoTracking();
        if (!IsSuperAdmin && TryGetScopedOwner(out var ownerId))
        {
            query = query.Where(d => d.OwnerId == ownerId);
        }
        else if (!IsSuperAdmin)
        {
            return Forbid();
        }

        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        // Get owners for name lookup (no FK constraint, so manual lookup)
        var owners = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .ToDictionaryAsync(so => so.Id, so => so.Name, cancellationToken);

        var drivers = await query
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var serviceTypeIdsByDriver = await GetServiceTypeIdsByDriverAsync(
            drivers.Select(d => d.Id).ToList(),
            cancellationToken);

        var dtos = drivers.Select(d => new DriverDto
        {
            ToolId = d.ToolId,
            ErpId = d.ErpId,
            Name = d.Name,
            StartAddress = d.StartAddress,
            StartLatitude = d.StartLatitude ?? 0,
            StartLongitude = d.StartLongitude ?? 0,
            DefaultServiceMinutes = d.DefaultServiceMinutes,
            MaxWorkMinutesPerDay = d.MaxWorkMinutesPerDay,
            OwnerId = d.OwnerId,
            OwnerName = owners.ContainsKey(d.OwnerId) ? owners[d.OwnerId] : string.Empty,
            IsActive = d.IsActive,
            ServiceTypeIds = serviceTypeIdsByDriver.TryGetValue(d.Id, out var ids) ? ids : new List<int>()
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Gets a driver by ToolId
    /// </summary>
    [HttpGet("{toolId:guid}")]
    [ProducesResponseType(typeof(DriverDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverDto>> GetDriver(
        [FromRoute] Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.Drivers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver != null && !IsSuperAdmin)
        {
            if (!TryGetScopedOwner(out var ownerId) || driver.OwnerId != ownerId)
            {
                return Forbid();
            }
        }

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        var ownerName = await GetOwnerNameAsync(driver.OwnerId, cancellationToken);
        var serviceTypeIds = await _dbContext.DriverServiceTypes
            .AsNoTracking()
            .Where(dst => dst.DriverId == driver.Id)
            .Select(dst => dst.ServiceTypeId)
            .ToListAsync(cancellationToken);

        var dto = new DriverDto
        {
            ToolId = driver.ToolId,
            ErpId = driver.ErpId,
            Name = driver.Name,
            StartAddress = driver.StartAddress,
            StartLatitude = driver.StartLatitude ?? 0,
            StartLongitude = driver.StartLongitude ?? 0,
            DefaultServiceMinutes = driver.DefaultServiceMinutes,
            MaxWorkMinutesPerDay = driver.MaxWorkMinutesPerDay,
            OwnerId = driver.OwnerId,
            OwnerName = ownerName,
            IsActive = driver.IsActive,
            ServiceTypeIds = serviceTypeIds
        };

        return Ok(dto);
    }

    /// <summary>
    /// Creates a new driver
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DriverDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DriverDto>> CreateDriver(
        [FromBody] CreateDriverRequest request,
        CancellationToken cancellationToken = default)
    {
        return Forbid();
    }

    /// <summary>
    /// Updates a driver
    /// </summary>
    [HttpPut("{toolId:guid}")]
    [ProducesResponseType(typeof(DriverDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<DriverDto>> UpdateDriver(
        [FromRoute] Guid toolId,
        [FromBody] UpdateDriverRequest request,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        if (!IsSuperAdmin)
        {
            if (!TryGetScopedOwner(out var ownerId) || driver.OwnerId != ownerId || request.OwnerId != ownerId)
            {
                return Forbid();
            }
        }

        // Validation
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(
                detail: "Name is required",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        var startAddress = request.StartAddress?.Trim();
        if (request.StartLatitude == 0)
        {
            request.StartLatitude = null;
        }
        if (request.StartLongitude == 0)
        {
            request.StartLongitude = null;
        }
        var hasAddress = !string.IsNullOrWhiteSpace(startAddress);
        var hasLatitude = request.StartLatitude.HasValue;
        var hasLongitude = request.StartLongitude.HasValue;
        if (hasLatitude != hasLongitude)
        {
            return Problem(
                detail: "Provide both StartLatitude and StartLongitude, or leave both empty",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (!hasAddress && !hasLatitude)
        {
            return Problem(
                detail: "StartAddress or StartLatitude/StartLongitude is required",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (!hasLatitude && hasAddress)
        {
            var geocode = await _geocodingService.GeocodeAddressAsync(startAddress!, cancellationToken);
            if (geocode == null)
            {
                return Problem(
                    detail: "Unable to resolve StartLatitude/StartLongitude from StartAddress",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error");
            }
            request.StartLatitude = geocode.Latitude;
            request.StartLongitude = geocode.Longitude;
            hasLatitude = true;
            hasLongitude = true;
        }
        else if (!hasAddress && hasLatitude)
        {
            var reverseAddress = await _geocodingService.ReverseGeocodeAsync(request.StartLatitude!.Value, request.StartLongitude!.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(reverseAddress))
            {
                return Problem(
                    detail: "Unable to resolve StartAddress from StartLatitude/StartLongitude",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error");
            }
            startAddress = reverseAddress;
            hasAddress = true;
        }

        if (!hasLatitude || !hasLongitude)
        {
            return Problem(
                detail: "StartLatitude and StartLongitude are required after geocoding",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        var latitude = request.StartLatitude!.Value;
        var longitude = request.StartLongitude!.Value;
        if (latitude < -90 || latitude > 90)
        {
            return Problem(
                detail: "StartLatitude must be between -90 and 90",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (longitude < -180 || longitude > 180)
        {
            return Problem(
                detail: "StartLongitude must be between -180 and 180",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (request.MaxWorkMinutesPerDay < 60 || request.MaxWorkMinutesPerDay > 900)
        {
            return Problem(
                detail: "MaxWorkMinutesPerDay must be between 60 and 900",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (request.DefaultServiceMinutes < 1 || request.DefaultServiceMinutes > 240)
        {
            return Problem(
                detail: "DefaultServiceMinutes must be between 1 and 240",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        // Validate OwnerId exists and is active
        var owner = await _dbContext.ServiceLocationOwners
            .FirstOrDefaultAsync(so => so.Id == request.OwnerId && so.IsActive, cancellationToken);
        if (owner == null)
        {
            return Problem(
                detail: $"OwnerId {request.OwnerId} does not exist or is not active",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        // Check ErpId uniqueness (if changed)
        if (driver.ErpId != request.ErpId)
        {
            var erpIdExists = await _dbContext.Drivers
                .AnyAsync(d => d.ErpId == request.ErpId && d.Id != driver.Id, cancellationToken);

            if (erpIdExists)
            {
                return Problem(
                    detail: $"Driver with ErpId {request.ErpId} already exists",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict");
            }
        }

        // Update
        driver.ErpId = request.ErpId;
        driver.Name = request.Name;
        driver.StartAddress = startAddress;
        driver.StartLatitude = latitude;
        driver.StartLongitude = longitude;
        driver.DefaultServiceMinutes = request.DefaultServiceMinutes;
        driver.MaxWorkMinutesPerDay = request.MaxWorkMinutesPerDay;
        driver.OwnerId = request.OwnerId;
        driver.IsActive = request.IsActive;
        driver.UpdatedAtUtc = DateTime.UtcNow;

        if (request.ServiceTypeIds != null)
        {
            var requestedIds = request.ServiceTypeIds
                .Distinct()
                .ToList();

            if (requestedIds.Any(id => id <= 0))
            {
                return Problem(
                    detail: "ServiceTypeIds must be positive integers",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error");
            }

            var validServiceTypeIds = await _dbContext.ServiceTypes
                .AsNoTracking()
                .Where(st => requestedIds.Contains(st.Id))
                .Select(st => st.Id)
                .ToListAsync(cancellationToken);

            var missing = requestedIds.Except(validServiceTypeIds).ToList();
            if (missing.Count > 0)
            {
                return Problem(
                    detail: $"ServiceTypeIds not found: {string.Join(", ", missing)}",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error");
            }

            var existingLinks = await _dbContext.DriverServiceTypes
                .Where(dst => dst.DriverId == driver.Id)
                .ToListAsync(cancellationToken);

            var existingIds = existingLinks.Select(x => x.ServiceTypeId).ToHashSet();
            var toRemove = existingLinks.Where(x => !requestedIds.Contains(x.ServiceTypeId)).ToList();
            if (toRemove.Count > 0)
            {
                _dbContext.DriverServiceTypes.RemoveRange(toRemove);
            }

            var now = DateTime.UtcNow;
            foreach (var serviceTypeId in requestedIds)
            {
                if (existingIds.Contains(serviceTypeId))
                {
                    continue;
                }

                _dbContext.DriverServiceTypes.Add(new DriverServiceType
                {
                    DriverId = driver.Id,
                    ServiceTypeId = serviceTypeId,
                    CreatedAtUtc = now
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var ownerName = await GetOwnerNameAsync(driver.OwnerId, cancellationToken);
        var serviceTypeIds = await _dbContext.DriverServiceTypes
            .AsNoTracking()
            .Where(dst => dst.DriverId == driver.Id)
            .Select(dst => dst.ServiceTypeId)
            .ToListAsync(cancellationToken);

        var dto = new DriverDto
        {
            ToolId = driver.ToolId,
            ErpId = driver.ErpId,
            Name = driver.Name,
            StartAddress = driver.StartAddress,
            StartLatitude = driver.StartLatitude ?? 0,
            StartLongitude = driver.StartLongitude ?? 0,
            DefaultServiceMinutes = driver.DefaultServiceMinutes,
            MaxWorkMinutesPerDay = driver.MaxWorkMinutesPerDay,
            OwnerId = driver.OwnerId,
            OwnerName = ownerName,
            IsActive = driver.IsActive,
            ServiceTypeIds = serviceTypeIds
        };

        return Ok(dto);
    }

    /// <summary>
    /// Soft deletes a driver (sets IsActive=false)
    /// </summary>
    [HttpDelete("{toolId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDriver(
        [FromRoute] Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        if (!IsSuperAdmin)
        {
            if (!TryGetScopedOwner(out var ownerId) || driver.OwnerId != ownerId)
            {
                return Forbid();
            }
        }

        driver.IsActive = false;
        driver.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
