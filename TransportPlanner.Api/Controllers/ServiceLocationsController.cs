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
[Route("api/service-locations")]
[Authorize(Policy = "RequireStaff")]
public class ServiceLocationsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IGeocodingService _geocodingService;

    public ServiceLocationsController(TransportPlannerDbContext dbContext, IGeocodingService geocodingService)
    {
        _dbContext = dbContext;
        _geocodingService = geocodingService;
    }

    [HttpGet("{toolId:guid}/opening-hours")]
    [ProducesResponseType(typeof(List<ServiceLocationOpeningHoursDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceLocationOpeningHoursDto>>> GetOpeningHours(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        var hours = await _dbContext.ServiceLocationOpeningHours
            .AsNoTracking()
            .Where(x => x.ServiceLocationId == serviceLocation.Id)
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new ServiceLocationOpeningHoursDto
            {
                Id = x.Id,
                DayOfWeek = x.DayOfWeek,
                OpenTime = x.OpenTime.HasValue ? x.OpenTime.Value.ToString("hh\\:mm") : null,
                CloseTime = x.CloseTime.HasValue ? x.CloseTime.Value.ToString("hh\\:mm") : null,
                IsClosed = x.IsClosed
            })
            .ToListAsync(cancellationToken);

        return Ok(hours);
    }

    [HttpPut("{toolId:guid}/opening-hours")]
    [ProducesResponseType(typeof(List<ServiceLocationOpeningHoursDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceLocationOpeningHoursDto>>> SaveOpeningHours(
        Guid toolId,
        [FromBody] SaveServiceLocationOpeningHoursRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        var normalized = new List<ServiceLocationOpeningHours>();
        foreach (var item in request.Items)
        {
            if (item.DayOfWeek < 0 || item.DayOfWeek > 6)
            {
                return BadRequest(new { message = "DayOfWeek must be between 0 and 6." });
            }

            var openTime = ParseTime(item.OpenTime);
            var closeTime = ParseTime(item.CloseTime);

            if (!item.IsClosed && (!openTime.HasValue || !closeTime.HasValue))
            {
                return BadRequest(new { message = "OpenTime and CloseTime are required when not closed." });
            }

            if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
            {
                return BadRequest(new { message = "OpenTime must be before CloseTime." });
            }

            normalized.Add(new ServiceLocationOpeningHours
            {
                ServiceLocationId = serviceLocation.Id,
                DayOfWeek = item.DayOfWeek,
                OpenTime = item.IsClosed ? null : openTime,
                CloseTime = item.IsClosed ? null : closeTime,
                IsClosed = item.IsClosed
            });
        }

        await _dbContext.ServiceLocationOpeningHours
            .Where(x => x.ServiceLocationId == serviceLocation.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.ServiceLocationOpeningHours.AddRange(normalized);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = normalized
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new ServiceLocationOpeningHoursDto
            {
                Id = x.Id,
                DayOfWeek = x.DayOfWeek,
                OpenTime = x.OpenTime.HasValue ? x.OpenTime.Value.ToString("hh\\:mm") : null,
                CloseTime = x.CloseTime.HasValue ? x.CloseTime.Value.ToString("hh\\:mm") : null,
                IsClosed = x.IsClosed
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{toolId:guid}/exceptions")]
    [ProducesResponseType(typeof(List<ServiceLocationExceptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceLocationExceptionDto>>> GetExceptions(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        var items = await _dbContext.ServiceLocationExceptions
            .AsNoTracking()
            .Where(x => x.ServiceLocationId == serviceLocation.Id)
            .OrderBy(x => x.Date)
            .Select(x => new ServiceLocationExceptionDto
            {
                Id = x.Id,
                Date = x.Date.Date,
                OpenTime = x.OpenTime.HasValue ? x.OpenTime.Value.ToString("hh\\:mm") : null,
                CloseTime = x.CloseTime.HasValue ? x.CloseTime.Value.ToString("hh\\:mm") : null,
                IsClosed = x.IsClosed,
                Note = x.Note
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPut("{toolId:guid}/exceptions")]
    [ProducesResponseType(typeof(List<ServiceLocationExceptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceLocationExceptionDto>>> SaveExceptions(
        Guid toolId,
        [FromBody] SaveServiceLocationExceptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        var normalized = new List<ServiceLocationException>();
        foreach (var item in request.Items)
        {
            var openTime = ParseTime(item.OpenTime);
            var closeTime = ParseTime(item.CloseTime);

            if (!item.IsClosed && (!openTime.HasValue || !closeTime.HasValue))
            {
                return BadRequest(new { message = "OpenTime and CloseTime are required when not closed." });
            }

            if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
            {
                return BadRequest(new { message = "OpenTime must be before CloseTime." });
            }

            normalized.Add(new ServiceLocationException
            {
                ServiceLocationId = serviceLocation.Id,
                Date = item.Date.Date,
                OpenTime = item.IsClosed ? null : openTime,
                CloseTime = item.IsClosed ? null : closeTime,
                IsClosed = item.IsClosed,
                Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim()
            });
        }

        await _dbContext.ServiceLocationExceptions
            .Where(x => x.ServiceLocationId == serviceLocation.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.ServiceLocationExceptions.AddRange(normalized);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = normalized
            .OrderBy(x => x.Date)
            .Select(x => new ServiceLocationExceptionDto
            {
                Id = x.Id,
                Date = x.Date.Date,
                OpenTime = x.OpenTime.HasValue ? x.OpenTime.Value.ToString("hh\\:mm") : null,
                CloseTime = x.CloseTime.HasValue ? x.CloseTime.Value.ToString("hh\\:mm") : null,
                IsClosed = x.IsClosed,
                Note = x.Note
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{toolId:guid}/constraints")]
    [ProducesResponseType(typeof(ServiceLocationConstraintDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceLocationConstraintDto>> GetConstraints(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        var constraint = await _dbContext.ServiceLocationConstraints
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceLocationId == serviceLocation.Id, cancellationToken);

        return Ok(new ServiceLocationConstraintDto
        {
            MinVisitDurationMinutes = constraint?.MinVisitDurationMinutes,
            MaxVisitDurationMinutes = constraint?.MaxVisitDurationMinutes
        });
    }

    [HttpPut("{toolId:guid}/constraints")]
    [ProducesResponseType(typeof(ServiceLocationConstraintDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceLocationConstraintDto>> SaveConstraints(
        Guid toolId,
        [FromBody] ServiceLocationConstraintDto request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        if (request.MinVisitDurationMinutes.HasValue && request.MinVisitDurationMinutes.Value < 0)
        {
            return BadRequest(new { message = "MinVisitDurationMinutes must be >= 0." });
        }

        if (request.MaxVisitDurationMinutes.HasValue && request.MaxVisitDurationMinutes.Value < 0)
        {
            return BadRequest(new { message = "MaxVisitDurationMinutes must be >= 0." });
        }

        if (request.MinVisitDurationMinutes.HasValue && request.MaxVisitDurationMinutes.HasValue
            && request.MinVisitDurationMinutes.Value > request.MaxVisitDurationMinutes.Value)
        {
            return BadRequest(new { message = "MinVisitDurationMinutes must be <= MaxVisitDurationMinutes." });
        }

        var constraint = await _dbContext.ServiceLocationConstraints
            .FirstOrDefaultAsync(x => x.ServiceLocationId == serviceLocation.Id, cancellationToken);

        if (constraint == null)
        {
            constraint = new ServiceLocationConstraint
            {
                ServiceLocationId = serviceLocation.Id
            };
            _dbContext.ServiceLocationConstraints.Add(constraint);
        }

        constraint.MinVisitDurationMinutes = request.MinVisitDurationMinutes;
        constraint.MaxVisitDurationMinutes = request.MaxVisitDurationMinutes;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ServiceLocationConstraintDto
        {
            MinVisitDurationMinutes = constraint.MinVisitDurationMinutes,
            MaxVisitDurationMinutes = constraint.MaxVisitDurationMinutes
        });
    }

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool TryResolveOwner(int? requestedOwnerId, out int ownerId, out ActionResult? forbidResult)
    {
        forbidResult = null;
        ownerId = 0;
        if (IsSuperAdmin)
        {
            ownerId = requestedOwnerId ?? 0;
            return true;
        }

        if (!CurrentOwnerId.HasValue)
        {
            forbidResult = Forbid();
            return false;
        }

        ownerId = CurrentOwnerId.Value;
        if (requestedOwnerId.HasValue && requestedOwnerId.Value != ownerId)
        {
            forbidResult = Forbid();
            return false;
        }

        return true;
    }

    private bool CanAccessOwner(int ownerId) =>
        IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    /// <summary>
    /// Helper method to get ServiceType name by ID (no FK constraint)
    /// </summary>
    private async Task<string> GetServiceTypeNameAsync(int serviceTypeId, CancellationToken cancellationToken)
    {
        if (serviceTypeId <= 0) return string.Empty;
        
        var serviceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(st => st.Id == serviceTypeId, cancellationToken);
        
        return serviceType?.Name ?? string.Empty;
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

    /// <summary>
    /// Gets paged list of service locations with filters
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServiceLocationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServiceLocationDto>>> GetServiceLocations(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromDue = null,
        [FromQuery] DateTime? toDue = null,
        [FromQuery] int? serviceTypeId = null,
        [FromQuery] int? ownerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string order = "priorityThenDue",
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        if (!TryResolveOwner(ownerId, out var resolvedOwnerId, out var forbidResult))
        {
            return forbidResult!;
        }

        var query = _dbContext.ServiceLocations
            .AsNoTracking();

        // Status filter
        if (Enum.TryParse<ServiceLocationStatus>(status, ignoreCase: true, out var statusEnum))
        {
            query = query.Where(sl => sl.Status == statusEnum);
        }

        // ServiceType filter
        if (serviceTypeId.HasValue)
        {
            query = query.Where(sl => sl.ServiceTypeId == serviceTypeId.Value);
        }

        // Owner filter
        if (resolvedOwnerId > 0)
        {
            query = query.Where(sl => sl.OwnerId == resolvedOwnerId);
        }

        // Search filter (name, address, or erpId)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            if (int.TryParse(search, out var erpIdSearch))
            {
                query = query.Where(sl => 
                    sl.Name.ToLower().Contains(searchLower) ||
                    (sl.Address != null && sl.Address.ToLower().Contains(searchLower)) ||
                    sl.ErpId == erpIdSearch);
            }
            else
            {
                query = query.Where(sl => 
                    sl.Name.ToLower().Contains(searchLower) ||
                    (sl.Address != null && sl.Address.ToLower().Contains(searchLower)));
            }
        }

        // Date range filter
        if (fromDue.HasValue)
        {
            var fromDate = fromDue.Value.Date;
            query = query.Where(sl => sl.DueDate >= fromDate);
        }
        if (toDue.HasValue)
        {
            var toDate = toDue.Value.Date;
            query = query.Where(sl => sl.DueDate <= toDate);
        }

        // Ordering
        if (order == "priorityThenDue")
        {
            query = query.OrderBy(sl => sl.PriorityDate ?? sl.DueDate)
                .ThenBy(sl => sl.DueDate)
                .ThenBy(sl => sl.Name);
        }
        else
        {
            query = query.OrderBy(sl => sl.DueDate)
                .ThenBy(sl => sl.Name);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Get ServiceTypes and Owners for name lookup (no FK constraint, so manual lookup)
        var serviceTypes = await _dbContext.ServiceTypes
            .AsNoTracking()
            .ToDictionaryAsync(st => st.Id, st => st.Name, cancellationToken);

        var owners = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .ToDictionaryAsync(so => so.Id, so => so.Name, cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = items.Select(sl => sl.Id).ToList();
        var latestNotes = await _dbContext.RouteStops
            .Include(rs => rs.Route)
            .Where(rs => rs.ServiceLocationId.HasValue && ids.Contains(rs.ServiceLocationId.Value) && rs.Note != null)
            .OrderBy(rs => rs.Route.Date)
            .ThenBy(rs => rs.Id)
            .ToListAsync(cancellationToken);

        var noteLookup = latestNotes
            .GroupBy(rs => rs.ServiceLocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().Note);

        var dtos = items.Select(sl => new ServiceLocationDto
        {
            Id = sl.Id,
            ToolId = sl.ToolId,
            ErpId = sl.ErpId,
            Name = sl.Name,
            Address = sl.Address,
            Latitude = sl.Latitude ?? 0,
            Longitude = sl.Longitude ?? 0,
            DueDate = sl.DueDate,
            PriorityDate = sl.PriorityDate,
            ServiceMinutes = sl.ServiceMinutes,
            Status = sl.Status.ToString(),
            IsActive = sl.IsActive,
            ServiceTypeId = sl.ServiceTypeId,
            ServiceTypeName = serviceTypes.ContainsKey(sl.ServiceTypeId) ? serviceTypes[sl.ServiceTypeId] : string.Empty,
            OwnerId = sl.OwnerId,
            OwnerName = owners.ContainsKey(sl.OwnerId) ? owners[sl.OwnerId] : string.Empty,
            DriverInstruction = sl.DriverInstruction,
            Remark = sl.Status == ServiceLocationStatus.NotVisited && noteLookup.TryGetValue(sl.Id, out var note)
                ? note
                : null
        }).ToList();

        return Ok(new PagedResult<ServiceLocationDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Gets a service location by ToolId
    /// </summary>
    [HttpGet("{toolId:guid}")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> GetServiceLocation(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue || serviceLocation.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
        }

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);
        var latestNote = serviceLocation.Status == ServiceLocationStatus.NotVisited
            ? await _dbContext.RouteStops
                .Include(rs => rs.Route)
                .Where(rs => rs.ServiceLocationId == serviceLocation.Id && rs.Note != null)
                .OrderBy(rs => rs.Route.Date)
                .ThenBy(rs => rs.Id)
                .Select(rs => rs.Note)
                .LastOrDefaultAsync(cancellationToken)
            : null;

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = latestNote
        };

        return Ok(dto);
    }

    /// <summary>
    /// Creates a new service location
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceLocationDto>> CreateServiceLocation(
        [FromBody] CreateServiceLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Name is required"
            });
        }

        if (request.ServiceMinutes < 1 || request.ServiceMinutes > 240)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "ServiceMinutes must be between 1 and 240"
            });
        }

        var address = request.Address?.Trim();
        if (request.Latitude == 0)
        {
            request.Latitude = null;
        }
        if (request.Longitude == 0)
        {
            request.Longitude = null;
        }
        var hasAddress = !string.IsNullOrWhiteSpace(address);
        var hasLatitude = request.Latitude.HasValue;
        var hasLongitude = request.Longitude.HasValue;
        if (hasLatitude != hasLongitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Provide both Latitude and Longitude, or leave both empty"
            });
        }

        if (!hasAddress && !hasLatitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Address or Latitude/Longitude is required"
            });
        }

        if (!hasLatitude && hasAddress)
        {
            var geocode = await _geocodingService.GeocodeAddressAsync(address!, cancellationToken);
            if (geocode == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Unable to resolve Latitude/Longitude from Address"
                });
            }
            request.Latitude = geocode.Latitude;
            request.Longitude = geocode.Longitude;
            hasLatitude = true;
            hasLongitude = true;
        }
        else if (!hasAddress && hasLatitude)
        {
            var reverseAddress = await _geocodingService.ReverseGeocodeAsync(request.Latitude!.Value, request.Longitude!.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(reverseAddress))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Unable to resolve Address from Latitude/Longitude"
                });
            }
            address = reverseAddress;
            hasAddress = true;
        }

        if (!hasLatitude || !hasLongitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Latitude and Longitude are required after geocoding"
            });
        }

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        if (latitude < -90 || latitude > 90)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Latitude must be between -90 and 90"
            });
        }

        if (longitude < -180 || longitude > 180)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Longitude must be between -180 and 180"
            });
        }

        // Check ErpId uniqueness
        var existingByErpId = await _dbContext.ServiceLocations
            .AnyAsync(sl => sl.ErpId == request.ErpId, cancellationToken);
        if (existingByErpId)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = $"Service location with ErpId {request.ErpId} already exists"
            });
        }

        if (request.OwnerId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId is required"
            });
        }

        if (!TryResolveOwner(request.OwnerId, out var resolvedOwnerId, out var forbidResult))
        {
            return forbidResult!;
        }

        request.OwnerId = resolvedOwnerId > 0 ? resolvedOwnerId : request.OwnerId;

        var ownerExists = await _dbContext.ServiceLocationOwners
            .AnyAsync(o => o.Id == request.OwnerId && o.IsActive, cancellationToken);
        if (!ownerExists)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"OwnerId {request.OwnerId} does not exist or is not active"
            });
        }

        var serviceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(st => st.Id == request.ServiceTypeId && st.IsActive, cancellationToken);
        if (serviceType == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeId {request.ServiceTypeId} does not exist or is not active"
            });
        }

        if (serviceType.OwnerId != request.OwnerId)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeId {request.ServiceTypeId} does not belong to OwnerId {request.OwnerId}"
            });
        }

        var now = DateTime.UtcNow;
        var serviceLocation = new ServiceLocation
        {
            ToolId = Guid.NewGuid(),
            ErpId = request.ErpId,
            Name = request.Name.Trim(),
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            DueDate = request.DueDate.Date,
            PriorityDate = request.PriorityDate?.Date,
            ServiceMinutes = request.ServiceMinutes ?? 20,
            ServiceTypeId = request.ServiceTypeId, // Just an int, no FK constraint
            OwnerId = request.OwnerId, // Just an int, no FK constraint
            Status = ServiceLocationStatus.Open,
            IsActive = true,
            DriverInstruction = string.IsNullOrWhiteSpace(request.DriverInstruction) ? null : request.DriverInstruction.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.ServiceLocations.Add(serviceLocation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return CreatedAtAction(nameof(GetServiceLocation), new { toolId = serviceLocation.ToolId }, dto);
    }

    /// <summary>
    /// Updates a service location
    /// </summary>
    [HttpPut("{toolId:guid}")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceLocationDto>> UpdateServiceLocation(
        Guid toolId,
        [FromBody] UpdateServiceLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue || serviceLocation.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
            if (request.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
        }

        // Validation
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Name is required"
            });
        }

        if (request.ServiceMinutes.HasValue && (request.ServiceMinutes < 1 || request.ServiceMinutes > 240))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "ServiceMinutes must be between 1 and 240"
            });
        }

        var address = request.Address?.Trim();
        if (request.Latitude == 0)
        {
            request.Latitude = null;
        }
        if (request.Longitude == 0)
        {
            request.Longitude = null;
        }
        var hasAddress = !string.IsNullOrWhiteSpace(address);
        var hasLatitude = request.Latitude.HasValue;
        var hasLongitude = request.Longitude.HasValue;
        if (hasLatitude != hasLongitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Provide both Latitude and Longitude, or leave both empty"
            });
        }

        if (!hasAddress && !hasLatitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Address or Latitude/Longitude is required"
            });
        }

        if (!hasLatitude && hasAddress)
        {
            var geocode = await _geocodingService.GeocodeAddressAsync(address!, cancellationToken);
            if (geocode == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Unable to resolve Latitude/Longitude from Address"
                });
            }
            request.Latitude = geocode.Latitude;
            request.Longitude = geocode.Longitude;
            hasLatitude = true;
            hasLongitude = true;
        }
        else if (!hasAddress && hasLatitude)
        {
            var reverseAddress = await _geocodingService.ReverseGeocodeAsync(request.Latitude!.Value, request.Longitude!.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(reverseAddress))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Unable to resolve Address from Latitude/Longitude"
                });
            }
            address = reverseAddress;
            hasAddress = true;
        }

        if (!hasLatitude || !hasLongitude)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Latitude and Longitude are required after geocoding"
            });
        }

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        if (latitude < -90 || latitude > 90)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Latitude must be between -90 and 90"
            });
        }

        if (longitude < -180 || longitude > 180)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Longitude must be between -180 and 180"
            });
        }

        if (request.OwnerId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId is required"
            });
        }

        if (!IsSuperAdmin)
        {
            request.OwnerId = CurrentOwnerId!.Value;
        }

        var ownerExists = await _dbContext.ServiceLocationOwners
            .AnyAsync(o => o.Id == request.OwnerId && o.IsActive, cancellationToken);
        if (!ownerExists)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"OwnerId {request.OwnerId} does not exist or is not active"
            });
        }

        // Check ErpId uniqueness (if changed)
        if (serviceLocation.ErpId != request.ErpId)
        {
            var existingByErpId = await _dbContext.ServiceLocations
                .AnyAsync(sl => sl.ErpId == request.ErpId && sl.ToolId != toolId, cancellationToken);
            if (existingByErpId)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = $"Service location with ErpId {request.ErpId} already exists"
                });
            }
        }

        serviceLocation.ErpId = request.ErpId;
        serviceLocation.Name = request.Name.Trim();
        serviceLocation.Address = address;
        serviceLocation.Latitude = latitude;
        serviceLocation.Longitude = longitude;
        serviceLocation.DueDate = request.DueDate.Date;
        serviceLocation.PriorityDate = request.PriorityDate?.Date;
        serviceLocation.ServiceTypeId = request.ServiceTypeId; // Just an int, no FK constraint
        serviceLocation.OwnerId = request.OwnerId; // Just an int, no FK constraint
        serviceLocation.DriverInstruction = string.IsNullOrWhiteSpace(request.DriverInstruction) ? null : request.DriverInstruction.Trim();
        if (request.ServiceMinutes.HasValue)
        {
            serviceLocation.ServiceMinutes = request.ServiceMinutes.Value;
        }
        // Note: Status is not updated via UpdateServiceLocationRequest - use dedicated endpoints
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return Ok(dto);
    }

    /// <summary>
    /// Sets or clears the priority date for a service location
    /// </summary>
    [HttpPost("{toolId:guid}/set-priority-date")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> SetPriorityDate(
        Guid toolId,
        [FromBody] SetPriorityDateRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        serviceLocation.PriorityDate = request.PriorityDate?.Date;
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return Ok(dto);
    }

    /// <summary>
    /// Marks a service location as Done
    /// </summary>
    [HttpPost("{toolId:guid}/mark-done")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> MarkDone(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        serviceLocation.Status = ServiceLocationStatus.Done;
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return Ok(dto);
    }

    /// <summary>
    /// Marks a service location as Open
    /// </summary>
    [HttpPost("{toolId:guid}/mark-open")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> MarkOpen(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        serviceLocation.Status = ServiceLocationStatus.Open;
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await RemoveFromFutureRoutesAsync(serviceLocation.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return Ok(dto);
    }

    /// <summary>
    /// Marks a service location as Cancelled
    /// </summary>
    [HttpPost("{toolId:guid}/mark-cancelled")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> MarkCancelled(
        Guid toolId,
        [FromBody] CancelServiceLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        serviceLocation.Status = ServiceLocationStatus.Cancelled;
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive,
            DriverInstruction = serviceLocation.DriverInstruction,
            Remark = null
        };

        return Ok(dto);
    }

    /// <summary>
    /// Marks a service location as Planned
    /// </summary>
    [HttpPost("{toolId:guid}/mark-planned")]
    [ProducesResponseType(typeof(ServiceLocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceLocationDto>> MarkPlanned(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        var serviceLocation = await _dbContext.ServiceLocations
            .FirstOrDefaultAsync(sl => sl.ToolId == toolId, cancellationToken);

        if (serviceLocation == null)
        {
            return NotFound();
        }

        if (!CanAccessOwner(serviceLocation.OwnerId))
        {
            return Forbid();
        }

        serviceLocation.Status = ServiceLocationStatus.Planned;
        serviceLocation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var serviceTypeName = await GetServiceTypeNameAsync(serviceLocation.ServiceTypeId, cancellationToken);
        var ownerName = await GetOwnerNameAsync(serviceLocation.OwnerId, cancellationToken);

        var dto = new ServiceLocationDto
        {
            Id = serviceLocation.Id,
            ToolId = serviceLocation.ToolId,
            ErpId = serviceLocation.ErpId,
            Name = serviceLocation.Name,
            Address = serviceLocation.Address,
            Latitude = serviceLocation.Latitude ?? 0,
            Longitude = serviceLocation.Longitude ?? 0,
            DueDate = serviceLocation.DueDate,
            PriorityDate = serviceLocation.PriorityDate,
            ServiceMinutes = serviceLocation.ServiceMinutes,
            ServiceTypeId = serviceLocation.ServiceTypeId,
            ServiceTypeName = serviceTypeName,
            OwnerId = serviceLocation.OwnerId,
            OwnerName = ownerName,
            Status = serviceLocation.Status.ToString(),
            IsActive = serviceLocation.IsActive
        };

        return Ok(dto);
    }

    private static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeSpan.TryParseExact(value, "hh\\:mm", null, out var parsed)
            ? parsed
            : null;
    }

    private async Task RemoveFromFutureRoutesAsync(int serviceLocationId, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var affectedRouteIds = await _dbContext.RouteStops
            .Where(rs => rs.ServiceLocationId == serviceLocationId && rs.Route.Date.Date >= today)
            .Select(rs => rs.RouteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (affectedRouteIds.Count == 0)
        {
            return;
        }

        await _dbContext.RouteStops
            .Where(rs => rs.ServiceLocationId == serviceLocationId && rs.Route.Date.Date >= today)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var routeId in affectedRouteIds)
        {
            var remainingCount = await _dbContext.RouteStops
                .Where(rs => rs.RouteId == routeId)
                .CountAsync(cancellationToken);

            if (remainingCount == 0)
            {
                var route = await _dbContext.Routes.FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
                if (route != null)
                {
                    _dbContext.Routes.Remove(route);
                }
            }
        }
    }
}
