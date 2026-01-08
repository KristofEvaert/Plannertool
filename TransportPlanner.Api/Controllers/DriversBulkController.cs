using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/drivers")]
[Authorize(Policy = "RequireAdmin")]
public class DriversBulkController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    public DriversBulkController(
        TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
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
    /// Download driver availability template as JSON (keyed by email)
    /// </summary>
    [HttpGet("bulk/json")]
    [ProducesResponseType(typeof(DriverAvailabilityExportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DriverAvailabilityExportResponse>> DownloadAvailabilityTemplate(
        [FromQuery] int? ownerId,
        CancellationToken cancellationToken = default)
    {
        var driverQuery = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.IsActive);

        if (ownerId.HasValue && ownerId.Value > 0)
        {
            driverQuery = driverQuery.Where(d => d.OwnerId == ownerId.Value);
        }

        var response = new DriverAvailabilityExportResponse
        {
            GeneratedAtUtc = DateTime.UtcNow
        };

        var driversWithEmail = await driverQuery
            .Where(d => d.UserId.HasValue)
            .Join(_dbContext.Users.AsNoTracking(),
                d => d.UserId!.Value,
                u => u.Id,
                (d, u) => new
                {
                    Driver = d,
                    Email = (u.Email ?? u.UserName ?? u.NormalizedEmail ?? u.NormalizedUserName ?? string.Empty).Trim()
                })
            .ToListAsync(cancellationToken);

        if (driversWithEmail.Count == 0)
        {
            return Ok(response);
        }

        var availabilityByDriverId = new Dictionary<int, List<DriverAvailabilityExportEntry>>();
        var driversWithEmailIds = driversWithEmail.Select(d => d.Driver.Id).Distinct().ToList();
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(a => driversWithEmailIds.Contains(a.DriverId))
            .OrderBy(a => a.Date)
            .ToListAsync(cancellationToken);

        availabilityByDriverId = availabilities
            .GroupBy(a => a.DriverId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => new DriverAvailabilityExportEntry
                {
                    Date = a.Date.ToString("yyyy-MM-dd"),
                    StartMinuteOfDay = a.StartMinuteOfDay,
                    EndMinuteOfDay = a.EndMinuteOfDay
                }).ToList());

        foreach (var item in driversWithEmail)
        {
            var email = item.Email;
            if (string.IsNullOrWhiteSpace(email)) continue;
            var driver = item.Driver;

            response.Drivers[email] = new DriverAvailabilityExportDriver
            {
                Email = email,
                DriverToolId = driver.ToolId,
                Name = driver.Name,
                OwnerId = driver.OwnerId,
                Availabilities = availabilityByDriverId.TryGetValue(driver.Id, out var entries)
                    ? entries
                    : new List<DriverAvailabilityExportEntry>()
            };
        }

        return Ok(response);
    }


    /// <summary>
    /// Download driver service types template as JSON (keyed by email)
    /// </summary>
    [HttpGet("service-types/bulk/json")]
    [ProducesResponseType(typeof(DriverServiceTypesBulkExportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DriverServiceTypesBulkExportResponse>> DownloadDriverServiceTypesTemplate(
        CancellationToken cancellationToken = default)
    {
        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !TryGetScopedOwner(out var ownerId) && !IsSuperAdmin)
        {
            return Forbid();
        }

        var driverQuery = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.IsActive);

        var scopedOwnerId = 0;
        var hasScopedOwner = isAuthenticated && !IsSuperAdmin && TryGetScopedOwner(out scopedOwnerId);
        if (hasScopedOwner)
        {
            driverQuery = driverQuery.Where(d => d.OwnerId == scopedOwnerId);
        }

        var driversWithEmail = await driverQuery
            .Where(d => d.UserId.HasValue)
            .Join(_dbContext.Users.AsNoTracking(),
                d => d.UserId!.Value,
                u => u.Id,
                (d, u) => new
                {
                    Driver = d,
                    Email = (u.Email ?? u.UserName ?? u.NormalizedEmail ?? u.NormalizedUserName ?? string.Empty).Trim()
                })
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .ToListAsync(cancellationToken);

        var driverIds = driversWithEmail.Select(x => x.Driver.Id).Distinct().ToList();
        var serviceTypeIdsByDriver = await _dbContext.DriverServiceTypes
            .AsNoTracking()
            .Where(dst => driverIds.Contains(dst.DriverId))
            .GroupBy(dst => dst.DriverId)
            .Select(g => new { DriverId = g.Key, ServiceTypeIds = g.Select(x => x.ServiceTypeId).ToList() })
            .ToDictionaryAsync(x => x.DriverId, x => x.ServiceTypeIds, cancellationToken);

        var response = new DriverServiceTypesBulkExportResponse
        {
            GeneratedAtUtc = DateTime.UtcNow
        };

        foreach (var item in driversWithEmail)
        {
            var serviceTypeIds = serviceTypeIdsByDriver.TryGetValue(item.Driver.Id, out var ids)
                ? ids
                : new List<int>();

            response.Drivers.Add(new DriverServiceTypesBulkItem
            {
                Email = item.Email,
                DriverToolId = item.Driver.ToolId,
                DriverErpId = item.Driver.ErpId,
                ServiceTypeIds = FormatServiceTypeIds(serviceTypeIds)
            });
        }

        return Ok(response);
    }

    /// <summary>
    /// Bulk upsert driver service types via JSON (keyed by email)
    /// </summary>
    [HttpPost("service-types/bulk/json")]
    [ProducesResponseType(typeof(DriverServiceTypesBulkResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverServiceTypesBulkResult>> BulkUpsertDriverServiceTypesJson(
        [FromBody] DriverServiceTypesBulkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Drivers == null || request.Drivers.Count == 0)
        {
            return BadRequest(new { message = "No drivers provided." });
        }

        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !TryGetScopedOwner(out var ownerId) && !IsSuperAdmin)
        {
            return Forbid();
        }

        var result = await ProcessDriverServiceTypesUpsertAsync(
            request.Drivers,
            isAuthenticated && !IsSuperAdmin && TryGetScopedOwner(out var scopedOwnerId) ? scopedOwnerId : null,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Bulk upsert driver availability via JSON (keyed by email)
    /// </summary>
    [HttpPost("bulk/json")]
    [ProducesResponseType(typeof(DriverAvailabilityBulkUpsertResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverAvailabilityBulkUpsertResult>> BulkUpsertAvailability(
        [FromBody] DriverAvailabilityBulkUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Drivers == null || request.Drivers.Count == 0)
        {
            return BadRequest(new { message = "No drivers provided." });
        }

        var result = await ProcessAvailabilityUpsertAsync(request.Drivers, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Upload Excel file with availability grid
    /// </summary>
    [HttpPost("bulk/excel")]
    [ProducesResponseType(typeof(DriverAvailabilityBulkUpsertResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverAvailabilityBulkUpsertResult>> UploadAvailabilityExcel(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded." });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "File must be an Excel file (.xlsx or .xls)." });
        }

        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();

        if (sheet == null)
        {
            return BadRequest(new { message = "Worksheet not found." });
        }

        const int headerRow = 1;
        const int emailColumn = 1;
        const int dateStartColumn = 2;
        const int dataStartRow = 2;

        var dateByColumn = new Dictionary<int, DateTime>();
        for (var col = dateStartColumn; col <= sheet.LastColumnUsed().ColumnNumber(); col++)
        {
            var cell = sheet.Cell(headerRow, col);
            if (TryParseExcelDate(cell, out var date))
            {
                dateByColumn[col] = date.Date;
            }
            else
            {
                break;
            }
        }

        if (dateByColumn.Count == 0)
        {
            return BadRequest(new { message = "No valid date headers found in the selected range." });
        }

        var drivers = new Dictionary<string, DriverAvailabilityBulkDriver>(StringComparer.OrdinalIgnoreCase);
        var parseErrors = new List<BulkErrorDto>();
        var failedCells = new List<(string Email, string Date, string Value, string Message)>();

        for (var row = dataStartRow; row <= sheet.LastRowUsed().RowNumber(); row++)
        {
            var emailCell = sheet.Cell(row, emailColumn);
            var email = emailCell.GetString().Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                break;
            }

            if (!drivers.TryGetValue(email, out var driver))
            {
                driver = new DriverAvailabilityBulkDriver
                {
                    Email = email
                };
                drivers[email] = driver;
            }

            foreach (var kvp in dateByColumn)
            {
                var cell = sheet.Cell(row, kvp.Key);
                if (cell.IsEmpty())
                {
                    driver.Availabilities.Add(new DriverAvailabilityBulkEntry
                    {
                        Date = kvp.Value.ToString("yyyy-MM-dd"),
                        StartMinuteOfDay = null,
                        EndMinuteOfDay = null
                    });
                    continue;
                }

                var text = cell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    driver.Availabilities.Add(new DriverAvailabilityBulkEntry
                    {
                        Date = kvp.Value.ToString("yyyy-MM-dd"),
                        StartMinuteOfDay = null,
                        EndMinuteOfDay = null
                    });
                    continue;
                }

                var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end))
                {
                    parseErrors.Add(new BulkErrorDto
                    {
                        Scope = "Availability",
                        RowRef = $"Row {row}, Col {kvp.Key}",
                        Message = "Invalid availability format (use start;end minutes)."
                    });
                    failedCells.Add((email, kvp.Value.ToString("yyyy-MM-dd"), text, "Invalid availability format (use start;end minutes)."));
                    continue;
                }

                driver.Availabilities.Add(new DriverAvailabilityBulkEntry
                {
                    Date = kvp.Value.ToString("yyyy-MM-dd"),
                    StartMinuteOfDay = start,
                    EndMinuteOfDay = end
                });
            }
        }

        if (drivers.Count == 0)
        {
            return BadRequest(new { message = "No driver emails found in the selected range." });
        }

        var result = await ProcessAvailabilityUpsertAsync(drivers, cancellationToken);
        if (parseErrors.Count > 0)
        {
            result.Errors.AddRange(parseErrors);
        }

        foreach (var error in result.Errors)
        {
            if (error.RowRef.Contains('#'))
            {
                var parts = error.RowRef.Split('#', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && int.TryParse(parts[1], out var index))
                {
                    var emailKey = parts[0];
                    if (drivers.TryGetValue(emailKey, out var driver) && driver.Availabilities != null)
                    {
                        var entryIndex = index - 1;
                        if (entryIndex >= 0 && entryIndex < driver.Availabilities.Count)
                        {
                            var entry = driver.Availabilities[entryIndex];
                            var value = entry.StartMinuteOfDay.HasValue && entry.EndMinuteOfDay.HasValue
                                ? $"{entry.StartMinuteOfDay};{entry.EndMinuteOfDay}"
                                : string.Empty;
                            failedCells.Add((emailKey, entry.Date, value, error.Message));
                        }
                    }
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error.RowRef))
            {
                failedCells.Add((error.RowRef, string.Empty, string.Empty, error.Message));
            }
        }

        if (failedCells.Count > 0)
        {
            using var errorWorkbook = new XLWorkbook();
            var failedSheet = errorWorkbook.Worksheets.Add("FailedEntries");
            failedSheet.Cell(1, 1).Value = "Email";
            failedSheet.Cell(1, 2).Value = "Date";
            failedSheet.Cell(1, 3).Value = "Value";
            failedSheet.Cell(1, 4).Value = "Message";

            for (int i = 0; i < failedCells.Count; i++)
            {
                var row = i + 2;
                var item = failedCells[i];
                failedSheet.Cell(row, 1).Value = item.Email;
                failedSheet.Cell(row, 2).Value = item.Date;
                failedSheet.Cell(row, 3).Value = item.Value;
                failedSheet.Cell(row, 4).Value = item.Message;
            }
            failedSheet.Columns().AdjustToContents();

            var errorsSheet = errorWorkbook.Worksheets.Add("Errors");
            errorsSheet.Cell(1, 1).Value = "RowRef";
            errorsSheet.Cell(1, 2).Value = "Message";
            for (int i = 0; i < result.Errors.Count; i++)
            {
                errorsSheet.Cell(i + 2, 1).Value = result.Errors[i].RowRef;
                errorsSheet.Cell(i + 2, 2).Value = result.Errors[i].Message;
            }
            errorsSheet.Columns().AdjustToContents();

            using var errorStream = new MemoryStream();
            errorWorkbook.SaveAs(errorStream);
            errorStream.Position = 0;
            return File(
                errorStream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"driver-availability-errors-{DateTime.Today:yyyy-MM-dd}.xlsx");
        }

        return Ok(result);
    }

    /// <summary>
    /// Download Excel template for driver service types (includes existing data)
    /// </summary>
    [HttpGet("service-types/bulk/excel")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadDriverServiceTypesTemplateExcel(
        CancellationToken cancellationToken = default)
    {
        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !TryGetScopedOwner(out var ownerId) && !IsSuperAdmin)
        {
            return Forbid();
        }

        var driverQuery = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.IsActive);

        var scopedOwnerId = 0;
        var hasScopedOwner = isAuthenticated && !IsSuperAdmin && TryGetScopedOwner(out scopedOwnerId);
        if (hasScopedOwner)
        {
            driverQuery = driverQuery.Where(d => d.OwnerId == scopedOwnerId);
        }

        var driversWithEmail = await driverQuery
            .Where(d => d.UserId.HasValue)
            .Join(_dbContext.Users.AsNoTracking(),
                d => d.UserId!.Value,
                u => u.Id,
                (d, u) => new
                {
                    Driver = d,
                    Email = (u.Email ?? u.UserName ?? u.NormalizedEmail ?? u.NormalizedUserName ?? string.Empty).Trim()
                })
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .ToListAsync(cancellationToken);

        var driverIds = driversWithEmail.Select(x => x.Driver.Id).Distinct().ToList();
        var serviceTypeIdsByDriver = await _dbContext.DriverServiceTypes
            .AsNoTracking()
            .Where(dst => driverIds.Contains(dst.DriverId))
            .GroupBy(dst => dst.DriverId)
            .Select(g => new { DriverId = g.Key, ServiceTypeIds = g.Select(x => x.ServiceTypeId).ToList() })
            .ToDictionaryAsync(x => x.DriverId, x => x.ServiceTypeIds, cancellationToken);

        var serviceTypesQuery = _dbContext.ServiceTypes
            .AsNoTracking()
            .Where(st => st.IsActive);

        if (hasScopedOwner)
        {
            serviceTypesQuery = serviceTypesQuery.Where(st => st.OwnerId == scopedOwnerId);
        }

        var serviceTypes = await serviceTypesQuery
            .OrderBy(st => st.Name)
            .Select(st => new { st.Id, st.Name })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Drivers");
        sheet.Cell(1, 1).Value = "Email";
        sheet.Cell(1, 2).Value = "ServiceTypeIds";
        sheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < driversWithEmail.Count; i++)
        {
            var row = i + 2;
            var item = driversWithEmail[i];
            var serviceTypeIds = serviceTypeIdsByDriver.TryGetValue(item.Driver.Id, out var ids)
                ? ids
                : new List<int>();

            sheet.Cell(row, 1).Value = item.Email;
            sheet.Cell(row, 2).Value = FormatServiceTypeIds(serviceTypeIds);
        }

        sheet.Columns().AdjustToContents();

        var listsSheet = workbook.Worksheets.Add("_Lists");
        listsSheet.Cell(1, 1).Value = "ServiceTypeId";
        listsSheet.Cell(1, 2).Value = "ServiceTypeName";
        listsSheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < serviceTypes.Count; i++)
        {
            listsSheet.Cell(i + 2, 1).Value = serviceTypes[i].Id;
            listsSheet.Cell(i + 2, 2).Value = serviceTypes[i].Name;
        }
        listsSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"driver-service-types-template-{DateTime.Today:yyyy-MM-dd}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>
    /// Upload Excel file and bulk upsert driver service types
    /// </summary>
    [HttpPost("service-types/bulk/excel")]
    [ProducesResponseType(typeof(DriverServiceTypesBulkResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverServiceTypesBulkResult>> UploadDriverServiceTypesExcel(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded." });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "File must be an Excel file (.xlsx or .xls)." });
        }

        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && !TryGetScopedOwner(out var ownerId) && !IsSuperAdmin)
        {
            return Forbid();
        }

        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();

        if (sheet == null)
        {
            return BadRequest(new { message = "Worksheet not found." });
        }
        const int emailColumn = 1;
        const int serviceTypeColumn = 2;
        const int dataStartRow = 2;

        var items = new List<DriverServiceTypesBulkItem>();
        for (var row = dataStartRow; row <= sheet.LastRowUsed().RowNumber(); row++)
        {
            var emailCell = sheet.Cell(row, emailColumn);
            var email = emailCell.GetString().Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                break;
            }

            var serviceTypesCell = sheet.Cell(row, serviceTypeColumn);
            var serviceTypesRaw = serviceTypesCell.IsEmpty() ? string.Empty : serviceTypesCell.GetString().Trim();

            items.Add(new DriverServiceTypesBulkItem
            {
                Email = email,
                ServiceTypeIds = serviceTypesRaw
            });
        }

        if (items.Count == 0)
        {
            return BadRequest(new { message = "No driver emails found in the selected range." });
        }

        var result = await ProcessDriverServiceTypesUpsertAsync(
            items,
            isAuthenticated && !IsSuperAdmin && TryGetScopedOwner(out var scopedOwnerId) ? scopedOwnerId : null,
            cancellationToken);

        if (result.Errors.Count == 0)
        {
            return Ok(result);
        }

        using var errorWorkbook = new XLWorkbook();
        var failedSheet = errorWorkbook.Worksheets.Add("FailedItems");
        failedSheet.Cell(1, 1).Value = "Email";
        failedSheet.Cell(1, 2).Value = "ServiceTypeIds";
        failedSheet.Cell(1, 3).Value = "Message";
        failedSheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < result.FailedItems.Count; i++)
        {
            var row = i + 2;
            var item = result.FailedItems[i];
            failedSheet.Cell(row, 1).Value = item.Email ?? string.Empty;
            failedSheet.Cell(row, 2).Value = item.ServiceTypeIds ?? string.Empty;
            failedSheet.Cell(row, 3).Value = item.Message ?? string.Empty;
        }
        failedSheet.Columns().AdjustToContents();

        var errorsSheet = errorWorkbook.Worksheets.Add("Errors");
        errorsSheet.Cell(1, 1).Value = "RowRef";
        errorsSheet.Cell(1, 2).Value = "Message";
        errorsSheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < result.Errors.Count; i++)
        {
            errorsSheet.Cell(i + 2, 1).Value = result.Errors[i].RowRef;
            errorsSheet.Cell(i + 2, 2).Value = result.Errors[i].Message;
        }
        errorsSheet.Columns().AdjustToContents();

        using var errorStream = new MemoryStream();
        errorWorkbook.SaveAs(errorStream);
        errorStream.Position = 0;

        return File(
            errorStream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"driver-service-types-errors-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Download Excel template with availability grid
    /// </summary>
    [HttpGet("bulk/excel")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public IActionResult DownloadAvailabilityTemplateExcel()
    {
        var fromDate = DateTime.Today;
        var toDate = DateTime.Today.AddMonths(1);

        var driversWithEmail = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.IsActive && d.UserId.HasValue)
            .Join(_dbContext.Users.AsNoTracking(),
                d => d.UserId!.Value,
                u => u.Id,
                (d, u) => new
                {
                    DriverId = d.Id,
                    Email = (u.Email ?? u.UserName ?? u.NormalizedEmail ?? u.NormalizedUserName ?? string.Empty).Trim()
                })
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .ToList();

        var driverIds = driversWithEmail.Select(d => d.DriverId).Distinct().ToList();
        var availabilities = _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(a => driverIds.Contains(a.DriverId)
                && a.Date >= fromDate
                && a.Date <= toDate)
            .ToList();

        var availabilityMap = availabilities
            .GroupBy(a => a.DriverId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(a => a.Date.Date, a => $"{a.StartMinuteOfDay};{a.EndMinuteOfDay}"));

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Availability");

        sheet.Cell(1, 1).Value = "Email";
        sheet.Cell(1, 1).Style.Font.Bold = true;

        var col = 2;
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var cell = sheet.Cell(1, col);
            cell.Value = date;
            cell.Style.DateFormat.Format = "yyyy-MM-dd";
            cell.Style.Font.Bold = true;
            col++;
        }

        for (int i = 0; i < driversWithEmail.Count; i++)
        {
            var row = i + 2;
            var driver = driversWithEmail[i];
            sheet.Cell(row, 1).Value = driver.Email;

            if (!availabilityMap.TryGetValue(driver.DriverId, out var entries))
            {
                continue;
            }

            col = 2;
            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                if (entries.TryGetValue(date.Date, out var value))
                {
                    sheet.Cell(row, col).Value = value;
                }
                col++;
            }
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"driver-availability-template-{fromDate:yyyy-MM-dd}-to-{toDate:yyyy-MM-dd}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task<DriverAvailabilityBulkUpsertResult> ProcessAvailabilityUpsertAsync(
        Dictionary<string, DriverAvailabilityBulkDriver> drivers,
        CancellationToken cancellationToken)
    {
        var result = new DriverAvailabilityBulkUpsertResult();
        var nowUtc = DateTime.UtcNow;

        void AddFailed(
            string? email,
            DriverAvailabilityBulkEntry? entry,
            string rowRef,
            string message,
            string scope = "Availability")
        {
            result.Errors.Add(new BulkErrorDto
            {
                Scope = scope,
                RowRef = rowRef,
                Message = message
            });
            result.FailedEntries.Add(new DriverAvailabilityBulkFailedEntry
            {
                Email = email,
                Date = entry?.Date,
                StartMinuteOfDay = entry?.StartMinuteOfDay,
                EndMinuteOfDay = entry?.EndMinuteOfDay,
                RowRef = rowRef,
                Message = message
            });
        }

        var emails = drivers
            .Select(kvp => (key: kvp.Key, value: kvp.Value))
            .Select(item => (item.value.Email ?? item.key)?.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (emails.Count == 0)
        {
            AddFailed(null, null, "request", "No valid email keys provided.");
            return result;
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email != null && emails.Contains(u.Email.ToLower()))
            .Select(u => new { u.Id, Email = u.Email! })
            .ToListAsync(cancellationToken);

        var userIdByEmail = users.ToDictionary(u => u.Email.ToLowerInvariant(), u => u.Id);

        var driversQuery = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.UserId.HasValue && userIdByEmail.Values.Contains(d.UserId.Value));

        var dbDrivers = await driversQuery.ToListAsync(cancellationToken);
        var driverByUserId = dbDrivers.ToDictionary(d => d.UserId!.Value, d => d);

        var parsedEntries = new List<ParsedAvailabilityEntry>();
        var deleteEntries = new List<ParsedAvailabilityDeleteEntry>();

        foreach (var kvp in drivers)
        {
            var emailKey = (kvp.Value.Email ?? kvp.Key)?.Trim() ?? string.Empty;
            var emailLookup = emailKey.ToLowerInvariant();
            var entries = kvp.Value.Availabilities ?? new List<DriverAvailabilityBulkEntry>();

            if (!userIdByEmail.TryGetValue(emailLookup, out var userId))
            {
                AddFailed(emailKey, null, emailKey, "User not found for email.");
                continue;
            }

            if (!driverByUserId.TryGetValue(userId, out var driver))
            {
                AddFailed(emailKey, null, emailKey, "Driver not found for email.");
                continue;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rowRef = $"{emailKey}#{i + 1}";
                if (!DateTime.TryParse(entry.Date, out var date))
                {
                    AddFailed(emailKey, entry, rowRef, "Invalid date.");
                    continue;
                }

                var start = entry.StartMinuteOfDay;
                var end = entry.EndMinuteOfDay;

                if (!start.HasValue && !end.HasValue)
                {
                    deleteEntries.Add(new ParsedAvailabilityDeleteEntry(
                        driver.Id,
                        date.Date,
                        emailKey,
                        driver.Name,
                        rowRef));
                    continue;
                }

                if (!start.HasValue || !end.HasValue)
                {
                    AddFailed(emailKey, entry, rowRef, "Both start and end minutes are required.");
                    continue;
                }

                if (start.Value < 0 || start.Value > 1439 ||
                    end.Value < 1 || end.Value > 1440 ||
                    end.Value <= start.Value)
                {
                    AddFailed(emailKey, entry, rowRef, "Invalid start/end minutes.");
                    continue;
                }

                parsedEntries.Add(new ParsedAvailabilityEntry(
                    driver.Id,
                    date.Date,
                    start.Value,
                    end.Value,
                    emailKey,
                    driver.Name,
                    rowRef));
            }
        }

        if (parsedEntries.Count == 0 && deleteEntries.Count == 0)
        {
            return result;
        }

        var driverIds = parsedEntries.Select(p => p.DriverId)
            .Concat(deleteEntries.Select(p => p.DriverId))
            .Distinct()
            .ToList();
        var dates = parsedEntries.Select(p => p.Date)
            .Concat(deleteEntries.Select(p => p.Date))
            .Distinct()
            .ToList();

        var existing = await _dbContext.DriverAvailabilities
            .Where(a => driverIds.Contains(a.DriverId) && dates.Contains(a.Date))
            .ToListAsync(cancellationToken);

        var existingMap = existing.ToDictionary(a => (a.DriverId, a.Date));

        var conflictKeys = await _dbContext.RouteStops
            .AsNoTracking()
            .Where(rs => driverIds.Contains(rs.Route.DriverId) && dates.Contains(rs.Route.Date))
            .Select(rs => new { rs.Route.DriverId, Date = rs.Route.Date.Date })
            .Distinct()
            .ToListAsync(cancellationToken);

        var conflictSet = new HashSet<(int driverId, DateTime date)>(
            conflictKeys.Select(k => (k.DriverId, k.Date)));

        void AddConflict(
            int driverId,
            string driverName,
            string email,
            DateTime date,
            string rowRef,
            DriverAvailability? existingAvailability,
            int? newStartMinute,
            int? newEndMinute,
            string reason)
        {
            result.Conflicts.Add(new DriverAvailabilityBulkConflictEntry
            {
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                DriverName = string.IsNullOrWhiteSpace(driverName) ? null : driverName,
                Date = date.ToString("yyyy-MM-dd"),
                ExistingStartMinuteOfDay = existingAvailability?.StartMinuteOfDay,
                ExistingEndMinuteOfDay = existingAvailability?.EndMinuteOfDay,
                NewStartMinuteOfDay = newStartMinute,
                NewEndMinuteOfDay = newEndMinute,
                RowRef = rowRef,
                Reason = reason
            });
        }

        var entriesToApply = new List<ParsedAvailabilityEntry>();
        foreach (var entry in parsedEntries)
        {
            var key = (entry.DriverId, entry.Date);
            var hasConflict = conflictSet.Contains(key);
            if (existingMap.TryGetValue(key, out var availability))
            {
                if (availability.StartMinuteOfDay == entry.StartMinute
                    && availability.EndMinuteOfDay == entry.EndMinute)
                {
                    continue; // no change
                }

                if (hasConflict)
                {
                    var existingRange = $"{FormatMinutes(availability.StartMinuteOfDay)}-{FormatMinutes(availability.EndMinuteOfDay)}";
                    var newRange = $"{FormatMinutes(entry.StartMinute)}-{FormatMinutes(entry.EndMinute)}";
                    AddConflict(
                        entry.DriverId,
                        entry.DriverName,
                        entry.Email,
                        entry.Date,
                        entry.RowRef,
                        availability,
                        entry.StartMinute,
                        entry.EndMinute,
                        $"Availability change blocked ({existingRange} -> {newRange}) because driver has a route with stops.");
                    continue;
                }
            }
            else
            {
                if (hasConflict)
                {
                    var newRange = $"{FormatMinutes(entry.StartMinute)}-{FormatMinutes(entry.EndMinute)}";
                    AddConflict(
                        entry.DriverId,
                        entry.DriverName,
                        entry.Email,
                        entry.Date,
                        entry.RowRef,
                        null,
                        entry.StartMinute,
                        entry.EndMinute,
                        $"Availability create blocked ({newRange}) because driver has a route with stops.");
                    continue;
                }
            }

            entriesToApply.Add(entry);
        }

        var deletesToApply = new List<ParsedAvailabilityDeleteEntry>();
        foreach (var entry in deleteEntries)
        {
            var key = (entry.DriverId, entry.Date);
            if (!existingMap.TryGetValue(key, out var availability))
            {
                continue;
            }

            if (conflictSet.Contains(key))
            {
                var existingRange = $"{FormatMinutes(availability.StartMinuteOfDay)}-{FormatMinutes(availability.EndMinuteOfDay)}";
                AddConflict(
                    entry.DriverId,
                    entry.DriverName,
                    entry.Email,
                    entry.Date,
                    entry.RowRef,
                    availability,
                    null,
                    null,
                    $"Availability removal blocked ({existingRange}) because driver has a route with stops.");
                continue;
            }

            deletesToApply.Add(entry);
        }

        foreach (var entry in entriesToApply)
        {
            if (existingMap.TryGetValue((entry.DriverId, entry.Date), out var availability))
            {
                availability.StartMinuteOfDay = entry.StartMinute;
                availability.EndMinuteOfDay = entry.EndMinute;
                availability.UpdatedAtUtc = nowUtc;
                result.Updated++;
            }
            else
            {
                _dbContext.DriverAvailabilities.Add(new DriverAvailability
                {
                    DriverId = entry.DriverId,
                    Date = entry.Date,
                    StartMinuteOfDay = entry.StartMinute,
                    EndMinuteOfDay = entry.EndMinute,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
                result.Inserted++;
            }
        }

        foreach (var entry in deletesToApply)
        {
            if (existingMap.TryGetValue((entry.DriverId, entry.Date), out var availability))
            {
                _dbContext.DriverAvailabilities.Remove(availability);
                result.Deleted++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    private static bool TryParseExcelDate(IXLCell cell, out DateTime date)
    {
        date = default;
        if (cell == null || cell.IsEmpty())
        {
            return false;
        }

        if (cell.TryGetValue(out DateTime dt))
        {
            date = dt;
            return true;
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTime.TryParse(text, out date);
    }

    private static string FormatMinutes(int minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return $"{hours:00}:{mins:00}";
    }

    private sealed record ParsedAvailabilityEntry(
        int DriverId,
        DateTime Date,
        int StartMinute,
        int EndMinute,
        string Email,
        string DriverName,
        string RowRef);

    private sealed record ParsedAvailabilityDeleteEntry(
        int DriverId,
        DateTime Date,
        string Email,
        string DriverName,
        string RowRef);

    private static string FormatServiceTypeIds(IEnumerable<int> serviceTypeIds)
    {
        return JsonSerializer.Serialize(serviceTypeIds ?? Array.Empty<int>());
    }

    private static bool TryParseServiceTypeIds(string? raw, out List<int> ids, out string error)
    {
        ids = new List<int>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<int>>(trimmed) ?? new List<int>();
                ids = parsed.Distinct().ToList();
                return true;
            }
            catch (JsonException)
            {
                error = "ServiceTypeIds must be a JSON array like [1,2] or a comma-separated list.";
                return false;
            }
        }

        var parts = trimmed.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var id) || id <= 0)
            {
                error = "ServiceTypeIds must be a JSON array like [1,2] or a comma-separated list of integers.";
                return false;
            }
            ids.Add(id);
        }

        ids = ids.Distinct().ToList();
        return true;
    }

    private async Task<DriverServiceTypesBulkResult> ProcessDriverServiceTypesUpsertAsync(
        List<DriverServiceTypesBulkItem> items,
        int? ownerId,
        CancellationToken cancellationToken)
    {
        var result = new DriverServiceTypesBulkResult();
        var now = DateTime.UtcNow;

        var emails = items
            .Select(i => i.Email?.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.ToLowerInvariant())
            .Distinct()
            .ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email != null && emails.Contains(u.Email.ToLower()))
            .Select(u => new { u.Id, Email = u.Email! })
            .ToListAsync(cancellationToken);

        var userIdByEmail = users.ToDictionary(u => u.Email.ToLowerInvariant(), u => u.Id);

        var driversQuery = _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.IsActive);
        if (ownerId.HasValue)
        {
            driversQuery = driversQuery.Where(d => d.OwnerId == ownerId.Value);
        }

        var driversWithUsers = await driversQuery
            .Where(d => d.UserId.HasValue && userIdByEmail.Values.Contains(d.UserId.Value))
            .ToListAsync(cancellationToken);

        var driverByUserId = driversWithUsers.ToDictionary(d => d.UserId!.Value, d => d);

        var toolIds = items.Where(i => i.DriverToolId.HasValue).Select(i => i.DriverToolId!.Value).Distinct().ToList();
        var erpIds = items.Where(i => i.DriverErpId.HasValue).Select(i => i.DriverErpId!.Value).Distinct().ToList();

        var driversByToolId = toolIds.Count > 0
            ? await driversQuery.Where(d => toolIds.Contains(d.ToolId)).ToDictionaryAsync(d => d.ToolId, cancellationToken)
            : new Dictionary<Guid, Driver>();

        var driversByErpId = erpIds.Count > 0
            ? await driversQuery.Where(d => erpIds.Contains(d.ErpId)).ToDictionaryAsync(d => d.ErpId, cancellationToken)
            : new Dictionary<int, Driver>();

        var parsedItems = new List<(Driver Driver, List<int> ServiceTypeIds, string RowRef, string? Email, string? Raw)>();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var rowRef = $"Row {i + 1}";
            var email = item.Email?.Trim();

            Driver? driver = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailKey = email.ToLowerInvariant();
                if (userIdByEmail.TryGetValue(emailKey, out var userId))
                {
                    driverByUserId.TryGetValue(userId, out driver);
                }
            }

            if (driver == null && item.DriverToolId.HasValue)
            {
                driversByToolId.TryGetValue(item.DriverToolId.Value, out driver);
            }

            if (driver == null && item.DriverErpId.HasValue)
            {
                driversByErpId.TryGetValue(item.DriverErpId.Value, out driver);
            }

            if (driver == null)
            {
                AddDriverServiceTypeError(result, rowRef, email ?? item.DriverToolId?.ToString() ?? item.DriverErpId?.ToString(), item.ServiceTypeIds, "Driver not found or not in scope.");
                continue;
            }

            if (!TryParseServiceTypeIds(item.ServiceTypeIds, out var ids, out var parseError))
            {
                AddDriverServiceTypeError(result, rowRef, email ?? driver.ToolId.ToString(), item.ServiceTypeIds, parseError);
                continue;
            }

            parsedItems.Add((driver, ids, rowRef, email, item.ServiceTypeIds));
        }

        var allRequestedIds = parsedItems.SelectMany(x => x.ServiceTypeIds).Distinct().ToList();
        var serviceTypes = allRequestedIds.Count == 0
            ? new List<(int Id, int? OwnerId)>()
            : await _dbContext.ServiceTypes
                .AsNoTracking()
                .Where(st => allRequestedIds.Contains(st.Id) && st.IsActive)
                .Select(st => new ValueTuple<int, int?>(st.Id, st.OwnerId))
                .ToListAsync(cancellationToken);

        var serviceTypeOwners = serviceTypes.ToDictionary(s => s.Item1, s => s.Item2);
        var validServiceTypeIds = serviceTypeOwners.Keys.ToList();

        var missingIds = new HashSet<int>(allRequestedIds.Except(validServiceTypeIds));
        if (missingIds.Count > 0)
        {
            foreach (var entry in parsedItems.ToList())
            {
                var missingForRow = entry.ServiceTypeIds.Where(id => missingIds.Contains(id)).ToList();
                if (missingForRow.Count == 0)
                {
                    continue;
                }

                AddDriverServiceTypeError(
                    result,
                    entry.RowRef,
                    entry.Email ?? entry.Driver.ToolId.ToString(),
                    entry.Raw,
                    $"ServiceTypeIds not found: {string.Join(", ", missingForRow)}");

                parsedItems.Remove(entry);
            }
        }

        foreach (var entry in parsedItems.ToList())
        {
            var mismatched = entry.ServiceTypeIds
                .Where(id => serviceTypeOwners.TryGetValue(id, out var owner) && (!owner.HasValue || owner.Value != entry.Driver.OwnerId))
                .ToList();
            if (mismatched.Count == 0)
            {
                continue;
            }

            AddDriverServiceTypeError(
                result,
                entry.RowRef,
                entry.Email ?? entry.Driver.ToolId.ToString(),
                entry.Raw,
                $"ServiceTypeIds do not match driver owner: {string.Join(", ", mismatched)}");

            parsedItems.Remove(entry);
        }

        if (parsedItems.Count == 0)
        {
            return result;
        }

        var driverIds = parsedItems.Select(x => x.Driver.Id).Distinct().ToList();
        var existingLinks = await _dbContext.DriverServiceTypes
            .Where(dst => driverIds.Contains(dst.DriverId))
            .ToListAsync(cancellationToken);

        var existingByDriver = existingLinks
            .GroupBy(dst => dst.DriverId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entry in parsedItems)
        {
            var existing = existingByDriver.GetValueOrDefault(entry.Driver.Id, new List<DriverServiceType>());
            var existingIds = existing.Select(x => x.ServiceTypeId).ToHashSet();

            var toRemove = existing.Where(x => !entry.ServiceTypeIds.Contains(x.ServiceTypeId)).ToList();
            if (toRemove.Count > 0)
            {
                _dbContext.DriverServiceTypes.RemoveRange(toRemove);
            }

            foreach (var serviceTypeId in entry.ServiceTypeIds)
            {
                if (existingIds.Contains(serviceTypeId))
                {
                    continue;
                }

                _dbContext.DriverServiceTypes.Add(new DriverServiceType
                {
                    DriverId = entry.Driver.Id,
                    ServiceTypeId = serviceTypeId,
                    CreatedAtUtc = now
                });
            }

            result.Updated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static void AddDriverServiceTypeError(
        DriverServiceTypesBulkResult result,
        string rowRef,
        string? email,
        string? rawServiceTypeIds,
        string message)
    {
        result.Errors.Add(new BulkErrorDto
        {
            Scope = "DriverServiceTypes",
            RowRef = rowRef,
            Message = message
        });
        result.FailedItems.Add(new DriverServiceTypesBulkFailedItem
        {
            Email = email,
            ServiceTypeIds = rawServiceTypeIds,
            RowRef = rowRef,
            Message = message
        });
    }

}


