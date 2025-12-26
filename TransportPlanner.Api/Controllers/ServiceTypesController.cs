using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/service-types")]
[Authorize(Policy = "RequireStaff")]
public class ServiceTypesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    public ServiceTypesController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get all service types
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ServiceTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceTypeDto>>> GetServiceTypes(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ServiceTypes.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(st => st.IsActive);
        }

        var serviceTypes = await query
            .OrderBy(st => st.Name)
            .Select(st => new ServiceTypeDto
            {
                Id = st.Id,
                Code = st.Code,
                Name = st.Name,
                Description = st.Description,
                IsActive = st.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(serviceTypes);
    }

    /// <summary>
    /// Get service type by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> GetServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .Where(st => st.Id == id)
            .Select(st => new ServiceTypeDto
            {
                Id = st.Id,
                Code = st.Code,
                Name = st.Name,
                Description = st.Description,
                IsActive = st.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (serviceType == null)
        {
            return NotFound();
        }

        return Ok(serviceType);
    }

    /// <summary>
    /// Create a new service type
    /// </summary>
    [HttpPost]
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

        var now = DateTime.UtcNow;
        var serviceType = new ServiceType
        {
            Code = codeUpper,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
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
            IsActive = serviceType.IsActive
        };

        return CreatedAtAction(nameof(GetServiceType), new { id = serviceType.Id }, dto);
    }

    /// <summary>
    /// Update a service type
    /// </summary>
    [HttpPut("{id:int}")]
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

        serviceType.Code = codeUpper;
        serviceType.Name = request.Name.Trim();
        serviceType.Description = request.Description?.Trim();
        if (request.IsActive.HasValue)
        {
            serviceType.IsActive = request.IsActive.Value;
        }
        serviceType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ServiceTypeDto
        {
            Id = serviceType.Id,
            Code = serviceType.Code,
            Name = serviceType.Name,
            Description = serviceType.Description,
            IsActive = serviceType.IsActive
        };

        return Ok(dto);
    }

    /// <summary>
    /// Activate a service type
    /// </summary>
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> ActivateServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
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
            IsActive = serviceType.IsActive
        };

        return Ok(dto);
    }

    /// <summary>
    /// Deactivate a service type
    /// </summary>
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTypeDto>> DeactivateServiceType(
        int id,
        CancellationToken cancellationToken = default)
    {
        var serviceType = await _dbContext.ServiceTypes
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
            IsActive = serviceType.IsActive
        };

        return Ok(dto);
    }
}

