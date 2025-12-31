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
[Route("api/route-change-notifications")]
[Authorize]
public class RouteChangeNotificationsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    private bool IsDriver => User.IsInRole(AppRoles.Driver);
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public RouteChangeNotificationsController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RouteChangeNotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RouteChangeNotificationDto>>> GetNotifications(
        [FromQuery] int? routeId,
        [FromQuery] bool includeAcknowledged = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.RouteChangeNotifications
            .AsNoTracking()
            .Include(n => n.Route)
            .Include(n => n.RouteVersion)
            .AsQueryable();

        if (!includeAcknowledged)
        {
            query = query.Where(n => n.AcknowledgedUtc == null);
        }

        if (routeId.HasValue)
        {
            query = query.Where(n => n.RouteId == routeId.Value);
        }

        if (IsDriver)
        {
            var driver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == CurrentUserId, cancellationToken);
            if (driver == null)
            {
                return Ok(new List<RouteChangeNotificationDto>());
            }

            query = query.Where(n => n.DriverId == driver.Id);
        }
        else if (CurrentOwnerId.HasValue)
        {
            query = query.Where(n => n.Route.OwnerId == CurrentOwnerId.Value);
        }
        else if (!IsSuperAdmin)
        {
            return Forbid();
        }

        var items = await query
            .OrderByDescending(n => n.CreatedUtc)
            .Select(n => new RouteChangeNotificationDto
            {
                Id = n.Id,
                RouteId = n.RouteId,
                RouteVersionId = n.RouteVersionId,
                Severity = n.Severity.ToString(),
                CreatedUtc = n.CreatedUtc,
                AcknowledgedUtc = n.AcknowledgedUtc,
                ChangeSummary = n.RouteVersion.ChangeSummary
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("{notificationId:int}/ack")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Acknowledge(
        [FromRoute] int notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.RouteChangeNotifications
            .Include(n => n.Route)
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification == null)
        {
            return NotFound();
        }

        if (IsDriver)
        {
            var driver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == CurrentUserId, cancellationToken);
            if (driver == null || notification.DriverId != driver.Id)
            {
                return Forbid();
            }
        }
        else if (!CanAccessOwner(notification.Route.OwnerId))
        {
            return Forbid();
        }

        if (notification.AcknowledgedUtc == null)
        {
            notification.AcknowledgedUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }
}
