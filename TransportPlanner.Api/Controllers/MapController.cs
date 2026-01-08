using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/map")]
[Authorize(Policy = "RequireStaff")]
public class MapController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<MapController> _logger;

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    public MapController(
        TransportPlannerDbContext dbContext,
        ILogger<MapController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get service locations for map display with urgency coloring.
    /// </summary>
    [HttpGet("service-locations")]
    [ProducesResponseType(typeof(ServiceLocationsMapResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceLocationsMapResponseDto>> GetServiceLocationsForMap(
        [FromQuery] int ownerId,
        [FromQuery] int? serviceTypeId,
        [FromQuery] List<int>? serviceTypeIds,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var selectedTypeIds = (serviceTypeIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (selectedTypeIds.Count == 0 && serviceTypeId.HasValue && serviceTypeId.Value > 0)
        {
            selectedTypeIds.Add(serviceTypeId.Value);
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue)
            {
                return Forbid();
            }
            ownerId = CurrentOwnerId.Value;
        }

        if (ownerId <= 0 || selectedTypeIds.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId and at least one ServiceTypeId are required"
            });
        }

        var validServiceTypeIds = await _dbContext.ServiceTypes
            .AsNoTracking()
            .Where(st => selectedTypeIds.Contains(st.Id) && st.IsActive && st.OwnerId == ownerId)
            .Select(st => st.Id)
            .ToListAsync(cancellationToken);

        if (validServiceTypeIds.Count != selectedTypeIds.Count)
        {
            var missing = selectedTypeIds.Except(validServiceTypeIds).ToList();
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeIds not valid for owner: {string.Join(", ", missing)}"
            });
        }

        selectedTypeIds = validServiceTypeIds;

        // Default date range: today to today + 60 days
        var fromDate = from?.Date ?? DateTime.Today;
        var toDate = to?.Date ?? DateTime.Today.AddDays(60);

        // Safety cap: max 5000 items
        const int maxItems = 5000;

        // Filter by order date (PriorityDate ?? DueDate) within range
        // Include both Open and Planned statuses
        var items = await _dbContext.ServiceLocations
            .Where(sl => sl.OwnerId == ownerId
                && selectedTypeIds.Contains(sl.ServiceTypeId)
                && (sl.Status == ServiceLocationStatus.Open || sl.Status == ServiceLocationStatus.Planned)
                && sl.IsActive
                && (sl.PriorityDate.HasValue ? sl.PriorityDate.Value.Date : sl.DueDate.Date) >= fromDate
                && (sl.PriorityDate.HasValue ? sl.PriorityDate.Value.Date : sl.DueDate.Date) <= toDate)
            .OrderBy(sl => sl.PriorityDate ?? sl.DueDate)
            .ThenBy(sl => sl.DueDate)
            .ThenBy(sl => sl.Name)
            .Take(maxItems)
            .ToListAsync(cancellationToken);

        var filteredItems = items;

        if (filteredItems.Count > maxItems)
        {
            _logger.LogWarning(
                "Service locations query returned {Count} items, capped to {MaxItems}",
                filteredItems.Count, maxItems);
            filteredItems = filteredItems.Take(maxItems).ToList();
        }

        var orderDates = filteredItems
            .Select(sl => (sl.PriorityDate?.Date ?? sl.DueDate.Date))
            .ToList();

        var locationIds = filteredItems.Select(sl => sl.Id).ToList();
        var plannedStops = await _dbContext.RouteStops
            .AsNoTracking()
            .Where(rs => rs.ServiceLocationId.HasValue
                && locationIds.Contains(rs.ServiceLocationId.Value)
                && rs.Route.Date >= fromDate
                && rs.Route.Date <= toDate)
            .Select(rs => new
            {
                ServiceLocationId = rs.ServiceLocationId!.Value,
                RouteDate = rs.Route.Date,
                DriverName = rs.Route.Driver.Name
            })
            .ToListAsync(cancellationToken);

        var plannedMap = plannedStops
            .GroupBy(x => x.ServiceLocationId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.RouteDate).First());

        var dto = new ServiceLocationsMapResponseDto
        {
            From = fromDate,
            To = toDate,
            TotalCount = filteredItems.Count,
            MinOrderDate = orderDates.Any() ? orderDates.Min() : (DateTime?)null,
            MaxOrderDate = orderDates.Any() ? orderDates.Max() : (DateTime?)null,
            Items = filteredItems.Select(sl => new ServiceLocationMapDto
            {
                ToolId = sl.ToolId,
                ErpId = sl.ErpId,
                Name = sl.Name,
                Address = sl.Address,
                Latitude = sl.Latitude ?? 0,
                Longitude = sl.Longitude ?? 0,
                DueDate = sl.DueDate.Date,
                PriorityDate = sl.PriorityDate?.Date,
                OrderDate = (sl.PriorityDate?.Date ?? sl.DueDate.Date),
                ServiceTypeId = sl.ServiceTypeId,
                Status = sl.Status.ToString(),
                ServiceMinutes = sl.ServiceMinutes,
                PlannedDate = plannedMap.TryGetValue(sl.Id, out var planned) ? planned.RouteDate.Date : null,
                PlannedDriverName = plannedMap.TryGetValue(sl.Id, out planned) ? planned.DriverName : null
            }).ToList()
        };

        return Ok(dto);
    }
}

// DTOs
public class ServiceLocationsMapResponseDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalCount { get; set; }
    public DateTime? MinOrderDate { get; set; }
    public DateTime? MaxOrderDate { get; set; }
    public List<ServiceLocationMapDto> Items { get; set; } = new();
}

public class ServiceLocationMapDto
{
    public Guid ToolId { get; set; }
    public int? ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PriorityDate { get; set; }
    public DateTime OrderDate { get; set; }
    public int ServiceTypeId { get; set; }
    public string Status { get; set; } = string.Empty; // Open / Planned
    public int ServiceMinutes { get; set; }
    public DateTime? PlannedDate { get; set; }
    public string? PlannedDriverName { get; set; }
}

