using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/exports")]
[Authorize(Policy = "RequireStaff")]
public class ExportsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public ExportsController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("routes")]
    public async Task<IActionResult> ExportRoutes(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int ownerId,
        [FromQuery] int? serviceTypeId,
        [FromQuery] int[]? serviceTypeIds,
        CancellationToken cancellationToken = default)
    {
        if (!CanAccessOwner(ownerId))
        {
            return Forbid();
        }

        var fromDate = from.Date;
        var toDate = to.Date;
        if (fromDate > toDate)
        {
            return BadRequest(new { message = "From date must be before To date." });
        }

        var routes = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
                .ThenInclude(s => s.ServiceLocation)
            .Where(r => r.OwnerId == ownerId && r.Date >= fromDate && r.Date <= toDate)
            .ToListAsync(cancellationToken);

        var serviceTypeLookup = await _dbContext.ServiceTypes
            .AsNoTracking()
            .ToDictionaryAsync(st => st.Id, st => st.Name, cancellationToken);

        var driverIds = routes.Select(r => r.DriverId).Distinct().ToList();
        var dates = routes.Select(r => r.Date.Date).Distinct().ToList();

        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(av => driverIds.Contains(av.DriverId) && dates.Contains(av.Date))
            .ToListAsync(cancellationToken);

        var availabilityMap = availabilities
            .GroupBy(av => (av.DriverId, av.Date.Date))
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<ExportRow>();

        var filteredServiceTypeIds = new HashSet<int>();
        if (serviceTypeId.HasValue)
        {
            filteredServiceTypeIds.Add(serviceTypeId.Value);
        }
        if (serviceTypeIds != null)
        {
            foreach (var id in serviceTypeIds)
            {
                filteredServiceTypeIds.Add(id);
            }
        }

        foreach (var route in routes)
        {
            var key = (route.DriverId, route.Date.Date);
            var startMinute = availabilityMap.TryGetValue(key, out var availability)
                ? availability.StartMinuteOfDay
                : 0;

            var currentMinute = startMinute;
            var orderedStops = route.Stops
                .Where(s => s.ServiceLocationId.HasValue && s.ServiceLocation != null)
                .OrderBy(s => s.Sequence)
                .ToList();

            foreach (var stop in orderedStops)
            {
                if (filteredServiceTypeIds.Count > 0 &&
                    !filteredServiceTypeIds.Contains(stop.ServiceLocation!.ServiceTypeId))
                {
                    continue;
                }

                var travelMinutes = Math.Max(0, stop.TravelMinutesFromPrev);
                var arrivalMinute = currentMinute + travelMinutes;
                var plannedStart = route.Date.Date.AddMinutes(arrivalMinute);
                var plannedEnd = plannedStart.AddMinutes(stop.ServiceMinutes);

                currentMinute = (int)Math.Round((plannedEnd - route.Date.Date).TotalMinutes);

                var expectedStart = plannedStart.AddHours(-1);
                var expectedEnd = plannedEnd.AddHours(1);

                rows.Add(new ExportRow
                {
                    PlannedDate = route.Date.Date,
                    PlannedStart = plannedStart,
                    ExpectedRange = $"{expectedStart:HH:mm} - {expectedEnd:HH:mm}",
                    ServiceLocationName = stop.ServiceLocation!.Name,
                    ServiceType = serviceTypeLookup.TryGetValue(stop.ServiceLocation!.ServiceTypeId, out var typeName)
                        ? typeName
                        : stop.ServiceLocation!.ServiceTypeId.ToString(),
                    Address = stop.ServiceLocation!.Address ?? string.Empty,
                    ServiceMinutes = stop.ServiceMinutes,
                    Note = stop.Note
                });
            }
        }

        var orderedRows = rows
            .OrderBy(r => r.PlannedStart)
            .ThenBy(r => r.ServiceLocationName)
            .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("RouteExport");
        worksheet.Cell(1, 1).Value = "PlannedDate";
        worksheet.Cell(1, 2).Value = "ExpectedRange";
        worksheet.Cell(1, 3).Value = "LocationName";
        worksheet.Cell(1, 4).Value = "ServiceType";
        worksheet.Cell(1, 5).Value = "Address";
        worksheet.Cell(1, 6).Value = "ServiceMinutes";
        worksheet.Cell(1, 7).Value = "Notes";

        var dateFormat = "yyyy-MM-dd";

        var rowIndex = 2;
        foreach (var row in orderedRows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.PlannedDate.ToString(dateFormat);
            worksheet.Cell(rowIndex, 2).Value = row.ExpectedRange;
            worksheet.Cell(rowIndex, 3).Value = row.ServiceLocationName;
            worksheet.Cell(rowIndex, 4).Value = row.ServiceType;
            worksheet.Cell(rowIndex, 5).Value = row.Address;
            worksheet.Cell(rowIndex, 6).Value = row.ServiceMinutes;
            worksheet.Cell(rowIndex, 7).Value = row.Note ?? string.Empty;
            rowIndex++;
        }

        worksheet.Columns().AdjustToContents();

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"route-export-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private sealed class ExportRow
    {
        public DateTime PlannedDate { get; set; }
        public DateTime PlannedStart { get; set; }
        public string ExpectedRange { get; set; } = string.Empty;
        public string ServiceLocationName { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int ServiceMinutes { get; set; }
        public string? Note { get; set; }
    }
}
