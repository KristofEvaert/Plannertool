using ClosedXML.Excel;
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

    /// <summary>
    /// Download driver availability template as JSON (keyed by email)
    /// </summary>
    [AllowAnonymous]
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
    /// Bulk upsert driver availability via JSON (keyed by email)
    /// </summary>
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    /// Download Excel template with availability grid
    /// </summary>
    [AllowAnonymous]
    [HttpGet("bulk/excel")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public IActionResult DownloadAvailabilityTemplateExcel()
    {
        var fromDate = DateTime.Today;
        var toDate = DateTime.Today.AddMonths(1);

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

        var parsedEntries = new List<(int driverId, DateTime date, int startMinute, int endMinute)>();
        var deleteEntries = new List<(int driverId, DateTime date)>();

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
                if (!DateTime.TryParse(entry.Date, out var date))
                {
                    AddFailed(emailKey, entry, $"{emailKey}#{i + 1}", "Invalid date.");
                    continue;
                }

                var start = entry.StartMinuteOfDay;
                var end = entry.EndMinuteOfDay;

                if (!start.HasValue && !end.HasValue)
                {
                    deleteEntries.Add((driver.Id, date.Date));
                    continue;
                }

                if (!start.HasValue || !end.HasValue)
                {
                    AddFailed(emailKey, entry, $"{emailKey}#{i + 1}", "Both start and end minutes are required.");
                    continue;
                }

                if (start.Value < 0 || start.Value > 1439 ||
                    end.Value < 1 || end.Value > 1440 ||
                    end.Value <= start.Value)
                {
                    AddFailed(emailKey, entry, $"{emailKey}#{i + 1}", "Invalid start/end minutes.");
                    continue;
                }

                parsedEntries.Add((driver.Id, date.Date, start.Value, end.Value));
            }
        }

        if (parsedEntries.Count == 0 && deleteEntries.Count == 0)
        {
            return result;
        }

        var driverIds = parsedEntries.Select(p => p.driverId)
            .Concat(deleteEntries.Select(p => p.driverId))
            .Distinct()
            .ToList();
        var dates = parsedEntries.Select(p => p.date)
            .Concat(deleteEntries.Select(p => p.date))
            .Distinct()
            .ToList();

        var existing = await _dbContext.DriverAvailabilities
            .Where(a => driverIds.Contains(a.DriverId) && dates.Contains(a.Date))
            .ToListAsync(cancellationToken);

        var existingMap = existing.ToDictionary(a => (a.DriverId, a.Date));

        foreach (var entry in parsedEntries)
        {
            if (existingMap.TryGetValue((entry.driverId, entry.date), out var availability))
            {
                availability.StartMinuteOfDay = entry.startMinute;
                availability.EndMinuteOfDay = entry.endMinute;
                availability.UpdatedAtUtc = nowUtc;
                result.Updated++;
            }
            else
            {
                _dbContext.DriverAvailabilities.Add(new DriverAvailability
                {
                    DriverId = entry.driverId,
                    Date = entry.date,
                    StartMinuteOfDay = entry.startMinute,
                    EndMinuteOfDay = entry.endMinute,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
                result.Inserted++;
            }
        }

        foreach (var entry in deleteEntries)
        {
            if (existingMap.TryGetValue((entry.driverId, entry.date), out var availability))
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

}
