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
[Route("api/service-types")]
[Authorize(Policy = "RequireStaff")]
public class ServiceTypesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    public ServiceTypesController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get all service types
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ServiceTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceTypeDto>>> GetServiceTypes(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue)
            {
                return Forbid();
            }
            ownerId = CurrentOwnerId.Value;
        }

        var query = _dbContext.ServiceTypes
            .AsNoTracking()
            .Include(st => st.Owner)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(st => st.IsActive);
        }

        if (ownerId.HasValue && ownerId.Value > 0)
        {
            query = query.Where(st => st.OwnerId == ownerId.Value);
        }

        var serviceTypes = await query
            .OrderBy(st => st.Name)
            .Select(st => new ServiceTypeDto
            {
                Id = st.Id,
                Code = st.Code,
                Name = st.Name,
                Description = st.Description,
                IsActive = st.IsActive,
                OwnerId = st.OwnerId,
                OwnerName = st.Owner != null ? st.Owner.Name : null
            })
            .ToListAsync(cancellationToken);

        return Ok(serviceTypes);
    }

    /// <summary>
    /// Get service type by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> GetServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .Include(st => st.Owner)
            .FirstOrDefaultAsync(st => st.Id == id, cancellationToken);

        if (serviceType == null)
        {
            return NotFound();
        }

        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue || serviceType.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
        }

        return Ok(new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive,
            OwnerId = serviceType.OwnerId,
            OwnerName = serviceType.Owner?.Name
        });
    }

    /// <summary>
    /// Create a new service type
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceTypeDto>> CreateServiceType(
        [FromBody] CreateServiceTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Code is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Name is required"
            });
        }

        if (!request.OwnerId.HasValue || request.OwnerId.Value <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId is required"
            });
        }

        // Validate code format: uppercase, no spaces, alphanumeric + underscore
        var codeUpper = request.Code.ToUpperInvariant().Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(codeUpper, @"^[A-Z0-9_]+$"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Code must contain only uppercase letters, numbers, and underscores"
            });
        }

        // Check uniqueness
        var existing = await _dbContext.ServiceTypes
            .AnyAsync(st => st.Code == codeUpper, cancellationToken);
        if (existing)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = $"Service type with code '{codeUpper}' already exists"
            });
        }

        var owner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OwnerId.Value && o.IsActive, cancellationToken);
        if (owner == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"OwnerId {request.OwnerId.Value} does not exist or is not active"
            });
        }

        var now = DateTime.UtcNow;
        var serviceType = new ServiceType
        {
            Code = codeUpper,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            OwnerId = request.OwnerId.Value,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.ServiceTypes.Add(serviceType);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive,
            OwnerId = serviceType.OwnerId,
            OwnerName = owner.Name
        };

        return CreatedAtAction(nameof(GetServiceType), new { id = serviceType.Id }, dto);
    }

    /// <summary>
    /// Update a service type
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceTypeDto>> UpdateServiceType(
        int id,
        [FromBody] UpdateServiceTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
            .FirstOrDefaultAsync(st => st.Id == id, cancellationToken);

        if (serviceType == null)
        {
            return NotFound();
        }

        // Validation
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Code is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Name is required"
            });
        }

        if (!request.OwnerId.HasValue || request.OwnerId.Value <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId is required"
            });
        }

        // Validate code format
        var codeUpper = request.Code.ToUpperInvariant().Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(codeUpper, @"^[A-Z0-9_]+$"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Code must contain only uppercase letters, numbers, and underscores"
            });
        }

        // Check uniqueness (if code changed)
        if (serviceType.Code != codeUpper)
        {
            var existing = await _dbContext.ServiceTypes
                .AnyAsync(st => st.Code == codeUpper && st.Id != id, cancellationToken);
            if (existing)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = $"Service type with code '{codeUpper}' already exists"
                });
            }
        }

        var owner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OwnerId.Value && o.IsActive, cancellationToken);
        if (owner == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"OwnerId {request.OwnerId.Value} does not exist or is not active"
            });
        }

        if (serviceType.OwnerId != request.OwnerId.Value)
        {
            var inUseByLocations = await _dbContext.ServiceLocations
                .AnyAsync(sl => sl.ServiceTypeId == serviceType.Id && sl.OwnerId != request.OwnerId.Value, cancellationToken);
            if (inUseByLocations)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = "Service type is already used by service locations for a different owner."
                });
            }

            var inUseByDrivers = await _dbContext.DriverServiceTypes
                .AnyAsync(dst => dst.ServiceTypeId == serviceType.Id && dst.Driver.OwnerId != request.OwnerId.Value, cancellationToken);
            if (inUseByDrivers)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = "Service type is already assigned to drivers for a different owner."
                });
            }
        }

        serviceType.Code = codeUpper;
        serviceType.Name = request.Name.Trim();
        serviceType.Description = request.Description?.Trim();
        if (request.IsActive.HasValue)
        {
            serviceType.IsActive = request.IsActive.Value;
        }
        serviceType.OwnerId = request.OwnerId.Value;
        serviceType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive,
            OwnerId = serviceType.OwnerId,
            OwnerName = owner.Name
        };

        return Ok(dto);
    }

    /// <summary>
    /// Activate a service type
    /// </summary>
    [HttpPost("{id:int}/activate")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> ActivateServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
            .Include(st => st.Owner)
            .FirstOrDefaultAsync(st => st.Id == id, cancellationToken);

        if (serviceType == null)
        {
            return NotFound();
        }

        serviceType.IsActive = true;
        serviceType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive,
            OwnerId = serviceType.OwnerId,
            OwnerName = serviceType.Owner?.Name
        };

        return Ok(dto);
    }

    /// <summary>
    /// Deactivate a service type
    /// </summary>
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> DeactivateServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
            .Include(st => st.Owner)
            .FirstOrDefaultAsync(st => st.Id == id, cancellationToken);

        if (serviceType == null)
        {
            return NotFound();
        }

        serviceType.IsActive = false;
        serviceType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive,
            OwnerId = serviceType.OwnerId,
            OwnerName = serviceType.Owner?.Name
        };

        return Ok(dto);
    }
}

