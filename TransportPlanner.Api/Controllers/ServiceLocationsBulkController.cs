using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/service-locations/bulk")]
[Authorize(Policy = "RequireStaff")]
public class ServiceLocationsBulkController : ControllerBase
{
    private readonly ServiceLocationBulkInsertService _bulkInsertService;
    private readonly TransportPlannerDbContext _dbContext;
    private static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool TryResolveOwner(int ownerId, out int resolvedOwnerId, out ActionResult? forbidResult)
    {
        forbidResult = null;
        resolvedOwnerId = ownerId;
        if (IsSuperAdmin)
        {
            return true;
        }

        if (!CurrentOwnerId.HasValue)
        {
            forbidResult = Forbid();
            return false;
        }

        resolvedOwnerId = CurrentOwnerId.Value;
        if (ownerId > 0 && ownerId != resolvedOwnerId)
        {
            forbidResult = Forbid();
            return false;
        }

        return true;
    }

    private async Task<ActionResult?> ValidateServiceTypeForOwnerAsync(
        int serviceTypeId,
        int ownerId,
        CancellationToken cancellationToken)
    {
        var serviceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(st => st.Id == serviceTypeId && st.IsActive, cancellationToken);
        if (serviceType == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeId {serviceTypeId} is invalid or inactive."
            });
        }

        if (serviceType.OwnerId != ownerId)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeId {serviceTypeId} does not belong to OwnerId {ownerId}."
            });
        }

        return null;
    }

    public ServiceLocationsBulkController(
        ServiceLocationBulkInsertService bulkInsertService,
        TransportPlannerDbContext dbContext)
    {
        _bulkInsertService = bulkInsertService;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Download service locations bulk template as JSON
    /// </summary>
    [HttpGet("json")]
    [ProducesResponseType(typeof(ServiceLocationBulkExportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceLocationBulkExportResponse>> DownloadBulkTemplateJson(
        [FromQuery] int serviceTypeId,
        [FromQuery] int ownerId,
        CancellationToken cancellationToken = default)
    {
        var resolvedOwnerId = ownerId;
        if (User?.Identity?.IsAuthenticated == true
            && !TryResolveOwner(ownerId, out resolvedOwnerId, out var forbidResult))
        {
            return forbidResult!;
        }
        if (User?.Identity?.IsAuthenticated == true)
        {
            ownerId = resolvedOwnerId;
        }

        if (serviceTypeId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "serviceTypeId query parameter is required and must be greater than 0"
            });
        }

        if (ownerId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "ownerId query parameter is required and must be greater than 0"
            });
        }

        var serviceTypeError = await ValidateServiceTypeForOwnerAsync(serviceTypeId, ownerId, cancellationToken);
        if (serviceTypeError != null)
        {
            return serviceTypeError;
        }

        var ownerExists = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .AnyAsync(so => so.Id == ownerId && so.IsActive, cancellationToken);
        if (!ownerExists)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"OwnerId {ownerId} is invalid or inactive."
            });
        }

        var locations = await _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl => sl.OwnerId == ownerId && sl.ServiceTypeId == serviceTypeId && sl.IsActive)
            .OrderBy(sl => sl.ErpId)
            .Include(sl => sl.Constraint)
            .Include(sl => sl.OpeningHours)
            .Include(sl => sl.Exceptions)
            .ToListAsync(cancellationToken);

        var items = locations
            .Select(sl => new BulkServiceLocationInsertDto
            {
                ErpId = sl.ErpId,
                AccountId = sl.AccountId,
                SerialNumber = sl.SerialNumber,
                Name = sl.Name,
                Address = sl.Address,
                Latitude = sl.Latitude,
                Longitude = sl.Longitude,
                DueDate = DateOnly.FromDateTime(sl.DueDate),
                PriorityDate = sl.PriorityDate.HasValue ? DateOnly.FromDateTime(sl.PriorityDate.Value) : null,
                ServiceMinutes = sl.ServiceMinutes,
                DriverInstruction = sl.DriverInstruction,
                ExtraInstructions = sl.ExtraInstructions,
                MinVisitDurationMinutes = sl.Constraint != null ? sl.Constraint.MinVisitDurationMinutes : null,
                MaxVisitDurationMinutes = sl.Constraint != null ? sl.Constraint.MaxVisitDurationMinutes : null,
                OpeningHours = sl.OpeningHours
                    .OrderBy(x => x.DayOfWeek)
                    .Select(x => new ServiceLocationOpeningHoursDto
                    {
                        Id = x.Id,
                        DayOfWeek = x.DayOfWeek,
                        OpenTime = x.OpenTime.HasValue ? x.OpenTime.Value.ToString("hh\\:mm") : null,
                        CloseTime = x.CloseTime.HasValue ? x.CloseTime.Value.ToString("hh\\:mm") : null,
                        OpenTime2 = x.OpenTime2.HasValue ? x.OpenTime2.Value.ToString("hh\\:mm") : null,
                        CloseTime2 = x.CloseTime2.HasValue ? x.CloseTime2.Value.ToString("hh\\:mm") : null,
                        IsClosed = x.IsClosed
                    })
                    .ToList(),
                Exceptions = sl.Exceptions
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
                    .ToList()
            })
            .ToList();

        var response = new ServiceLocationBulkExportResponse
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ServiceTypeId = serviceTypeId,
            OwnerId = ownerId,
            Items = items
        };

        return Ok(response);
    }

    /// <summary>
    /// Bulk upsert service locations via JSON (insert or update)
    /// </summary>
    [HttpPost("json")]
    [ProducesResponseType(typeof(BulkInsertResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkInsertResultDto>> BulkUpsertJson(
        [FromBody] BulkInsertServiceLocationsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OwnerId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "OwnerId is required and must be greater than 0"
            });
        }

        if (request.ServiceTypeId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "ServiceTypeId is required and must be greater than 0"
            });
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Items are required"
            });
        }

        var resolvedOwnerId = request.OwnerId;
        if (User?.Identity?.IsAuthenticated == true
            && !TryResolveOwner(request.OwnerId, out resolvedOwnerId, out var forbidResult))
        {
            return forbidResult!;
        }

        if (User?.Identity?.IsAuthenticated == true)
        {
            request.OwnerId = resolvedOwnerId;
        }

        var result = await _bulkInsertService.InsertAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Download Excel template for service locations bulk insert
    /// </summary>
    [HttpGet("template")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DownloadTemplate(
        [FromQuery(Name = "serviceTypeId")] int serviceTypeId,
        [FromQuery(Name = "ownerId")] int ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveOwner(ownerId, out var resolvedOwnerId, out var forbidResult))
        {
            return forbidResult!;
        }
        ownerId = resolvedOwnerId;

        // Validate ServiceTypeId is provided and valid
        if (serviceTypeId <= 0)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Validation Error", 
                Detail = "serviceTypeId query parameter is required and must be greater than 0" 
            });
        }

        // Validate OwnerId is provided and valid
        if (ownerId <= 0)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Validation Error", 
                Detail = "ownerId query parameter is required and must be greater than 0" 
            });
        }

        // Validate ServiceTypeId exists
        var serviceType = await _dbContext.ServiceTypes
            .FirstOrDefaultAsync(st => st.Id == serviceTypeId && st.IsActive, cancellationToken);
        if (serviceType == null)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Validation Error", 
                Detail = $"ServiceTypeId {serviceTypeId} does not exist or is not active" 
            });
        }
        if (serviceType.OwnerId != ownerId)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"ServiceTypeId {serviceTypeId} does not belong to OwnerId {ownerId}"
            });
        }

        // Validate OwnerId exists
        var owner = await _dbContext.ServiceLocationOwners
            .FirstOrDefaultAsync(so => so.Id == ownerId, cancellationToken);
        if (owner == null)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Validation Error", 
                Detail = $"OwnerId {ownerId} does not exist" 
            });
        }

        // Create workbook
        using var workbook = new XLWorkbook();
        
        // Hidden sheet: _Lists (for data validation)
        var listsSheet = workbook.Worksheets.Add("_Lists");
        listsSheet.Hide();
        
        // Get all active service types for dropdown
        var allServiceTypes = await _dbContext.ServiceTypes
            .Where(st => st.IsActive && st.OwnerId == ownerId)
            .OrderBy(st => st.Name)
            .Select(st => new { st.Id, st.Name })
            .ToListAsync(cancellationToken);
        
        // Get all active owners for dropdown
        var allOwners = await _dbContext.ServiceLocationOwners
            .Where(so => so.IsActive)
            .OrderBy(so => so.Name)
            .Select(so => new { so.Id, so.Name })
            .ToListAsync(cancellationToken);
        
        // Populate service types list in _Lists sheet (Column A)
        listsSheet.Cell(1, 1).Value = "ServiceTypeId";
        listsSheet.Cell(1, 2).Value = "ServiceTypeName";
        for (int i = 0; i < allServiceTypes.Count; i++)
        {
            listsSheet.Cell(i + 2, 1).Value = allServiceTypes[i].Id;
            listsSheet.Cell(i + 2, 2).Value = allServiceTypes[i].Name;
        }
        
        // Populate owners list in _Lists sheet (Column C)
        listsSheet.Cell(1, 3).Value = "OwnerId";
        listsSheet.Cell(1, 4).Value = "OwnerName";
        for (int i = 0; i < allOwners.Count; i++)
        {
            listsSheet.Cell(i + 2, 3).Value = allOwners[i].Id;
            listsSheet.Cell(i + 2, 4).Value = allOwners[i].Name;
        }

        // Populate day list in _Lists sheet (Column E)
        listsSheet.Cell(1, 5).Value = "DayName";
        listsSheet.Cell(1, 6).Value = "DayOfWeek";
        for (int i = 0; i < DayNames.Length; i++)
        {
            listsSheet.Cell(i + 2, 5).Value = DayNames[i];
            listsSheet.Cell(i + 2, 6).Value = i;
        }

        // Populate IsClosed list in _Lists sheet (Column G)
        listsSheet.Cell(1, 7).Value = "IsClosed";
        listsSheet.Cell(2, 7).Value = "TRUE";
        listsSheet.Cell(3, 7).Value = "FALSE";
        
        // Sheet: ServiceLocations
        var sheet = workbook.Worksheets.Add("ServiceLocations");
        
        // Service type info rows (row 1)
        sheet.Cell(1, 1).Value = "ServiceTypeId:";
        sheet.Cell(1, 2).Value = serviceType.Id;
        // Add data validation dropdown for ServiceTypeId (cell B1)
        var serviceTypeIdValidation = sheet.Cell(1, 2).CreateDataValidation();
        serviceTypeIdValidation.List($"=_Lists!$A$2:$A${allServiceTypes.Count + 1}", true);
        serviceTypeIdValidation.IgnoreBlanks = false;
        serviceTypeIdValidation.ShowErrorMessage = true;
        serviceTypeIdValidation.ErrorTitle = "Invalid Service Type";
        serviceTypeIdValidation.ErrorMessage = "Please select a valid Service Type from the dropdown.";
        
        sheet.Cell(1, 3).Value = "ServiceTypeCode:";
        sheet.Cell(1, 4).Value = serviceType.Code;
        sheet.Cell(1, 5).Value = "ServiceTypeName:";
        sheet.Cell(1, 6).Value = serviceType.Name;
        // Add formula to show service type name based on selected ServiceTypeId
        sheet.Cell(1, 6).FormulaA1 = $"=IFERROR(VLOOKUP(B1,_Lists!$A$2:$B${allServiceTypes.Count + 1},2,FALSE),\"\")";
        
        // Owner info rows (row 2)
        sheet.Cell(2, 1).Value = "OwnerId:";
        sheet.Cell(2, 2).Value = owner.Id;
        // Add data validation dropdown for OwnerId (cell B2)
        var ownerIdValidation = sheet.Cell(2, 2).CreateDataValidation();
        ownerIdValidation.List($"=_Lists!$C$2:$C${allOwners.Count + 1}", true);
        ownerIdValidation.IgnoreBlanks = false;
        ownerIdValidation.ShowErrorMessage = true;
        ownerIdValidation.ErrorTitle = "Invalid Owner";
        ownerIdValidation.ErrorMessage = "Please select a valid Owner from the dropdown.";
        
        sheet.Cell(2, 3).Value = "OwnerCode:";
        sheet.Cell(2, 4).Value = owner.Code;
        sheet.Cell(2, 5).Value = "OwnerName:";
        sheet.Cell(2, 6).Value = owner.Name;
        // Add formula to show owner name based on selected OwnerId
        sheet.Cell(2, 6).FormulaA1 = $"=IFERROR(VLOOKUP(B2,_Lists!$C$2:$D${allOwners.Count + 1},2,FALSE),\"\")";
        
        // Header row (row 4)
        sheet.Cell(4, 1).Value = "ErpId";
        sheet.Cell(4, 2).Value = "Name";
        sheet.Cell(4, 3).Value = "Address";
        sheet.Cell(4, 4).Value = "Latitude";
        sheet.Cell(4, 5).Value = "Longitude";
        sheet.Cell(4, 6).Value = "DueDate";
        sheet.Cell(4, 7).Value = "PriorityDate";
        sheet.Cell(4, 8).Value = "ServiceMinutes";
        sheet.Cell(4, 9).Value = "DriverInstruction";
        sheet.Cell(4, 10).Value = "ExtraInstructions";
        sheet.Cell(4, 11).Value = "MinVisitDurationMinutes";
        sheet.Cell(4, 12).Value = "MaxVisitDurationMinutes";
        sheet.Cell(4, 13).Value = "AccountId";
        sheet.Cell(4, 14).Value = "SerialNumber";
        
        // Instruction row (row 5)
        sheet.Cell(5, 1).Value = "Required";
        sheet.Cell(5, 2).Value = "Required";
        sheet.Cell(5, 3).Value = "Required if no coordinates";
        sheet.Cell(5, 4).Value = "Required if no address (-90 to 90)";
        sheet.Cell(5, 5).Value = "Required if no address (-180 to 180)";
        sheet.Cell(5, 6).Value = "Required (yyyy-MM-dd)";
        sheet.Cell(5, 7).Value = "Optional (yyyy-MM-dd)";
        sheet.Cell(5, 8).Value = "Optional (1-240, default 20)";
        sheet.Cell(5, 9).Value = "Optional (shown to driver)";
        sheet.Cell(5, 10).Value = "Optional (pipe or newline separated)";
        sheet.Cell(5, 11).Value = "Optional (>= 0)";
        sheet.Cell(5, 12).Value = "Optional (>= 0)";
        sheet.Cell(5, 13).Value = "Optional";
        sheet.Cell(5, 14).Value = "Optional";
        
        // Format date columns (DueDate and PriorityDate) as text to preserve yyyy-MM-dd format
        // Set the entire column range to text format
        var dueDateColumn = sheet.Column(6);
        var priorityDateColumn = sheet.Column(7);
        dueDateColumn.Style.NumberFormat.Format = "@"; // "@" is the text format in Excel
        priorityDateColumn.Style.NumberFormat.Format = "@"; // "@" is the text format in Excel

        var existingLocations = await _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl => sl.OwnerId == ownerId && sl.ServiceTypeId == serviceTypeId && sl.IsActive)
            .OrderBy(sl => sl.ErpId)
            .Include(sl => sl.Constraint)
            .Include(sl => sl.OpeningHours)
            .Include(sl => sl.Exceptions)
            .ToListAsync(cancellationToken);

        // Populate existing data rows (starting at row 6)
        var startRow = 6;
        for (int i = 0; i < existingLocations.Count; i++)
        {
            var location = existingLocations[i];
            var row = startRow + i;

            sheet.Cell(row, 1).Value = location.ErpId;
            sheet.Cell(row, 2).Value = location.Name;
            sheet.Cell(row, 3).Value = location.Address ?? string.Empty;
            sheet.Cell(row, 4).Value = location.Latitude.HasValue ? location.Latitude.Value : string.Empty;
            sheet.Cell(row, 5).Value = location.Longitude.HasValue ? location.Longitude.Value : string.Empty;
            sheet.Cell(row, 6).Value = location.DueDate.ToString("yyyy-MM-dd");
            sheet.Cell(row, 7).Value = location.PriorityDate.HasValue ? location.PriorityDate.Value.ToString("yyyy-MM-dd") : string.Empty;
            sheet.Cell(row, 8).Value = location.ServiceMinutes;
            sheet.Cell(row, 9).Value = location.DriverInstruction ?? string.Empty;
            sheet.Cell(row, 10).Value = location.ExtraInstructions.Count > 0
                ? string.Join(" | ", location.ExtraInstructions)
                : string.Empty;
            sheet.Cell(row, 11).Value = location.Constraint?.MinVisitDurationMinutes?.ToString() ?? string.Empty;
            sheet.Cell(row, 12).Value = location.Constraint?.MaxVisitDurationMinutes?.ToString() ?? string.Empty;
            sheet.Cell(row, 13).Value = location.AccountId ?? string.Empty;
            sheet.Cell(row, 14).Value = location.SerialNumber ?? string.Empty;
        }
        
        // Style service type info rows
        var infoRange = sheet.Range(1, 1, 2, 4);
        infoRange.Style.Font.Bold = true;
        infoRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        
        // Style header
        var headerRange = sheet.Range(3, 1, 3, 14);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        
        // Style instruction row
        var instructionRange = sheet.Range(4, 1, 4, 14);
        instructionRange.Style.Font.Italic = true;
        instructionRange.Style.Font.FontColor = XLColor.DarkGray;
        
        // Auto-fit columns
        sheet.Columns().AdjustToContents();

        // Sheet: OpeningHours
        var openingHoursSheet = workbook.Worksheets.Add("OpeningHours");
        openingHoursSheet.Cell(1, 1).Value = "ErpId";
        openingHoursSheet.Cell(1, 2).Value = "Day";
        openingHoursSheet.Cell(1, 3).Value = "OpenTime";
        openingHoursSheet.Cell(1, 4).Value = "CloseTime";
        openingHoursSheet.Cell(1, 5).Value = "OpenTime2";
        openingHoursSheet.Cell(1, 6).Value = "CloseTime2";
        openingHoursSheet.Cell(1, 7).Value = "IsClosed";

        openingHoursSheet.Cell(2, 1).Value = "Required";
        openingHoursSheet.Cell(2, 2).Value = "Required (Sunday-Saturday)";
        openingHoursSheet.Cell(2, 3).Value = "Required unless closed (HH:mm)";
        openingHoursSheet.Cell(2, 4).Value = "Required unless closed (HH:mm)";
        openingHoursSheet.Cell(2, 5).Value = "Optional lunch (HH:mm)";
        openingHoursSheet.Cell(2, 6).Value = "Optional lunch (HH:mm)";
        openingHoursSheet.Cell(2, 7).Value = "Optional (TRUE/FALSE)";

        openingHoursSheet.Column(3).Style.NumberFormat.Format = "@";
        openingHoursSheet.Column(4).Style.NumberFormat.Format = "@";
        openingHoursSheet.Column(5).Style.NumberFormat.Format = "@";
        openingHoursSheet.Column(6).Style.NumberFormat.Format = "@";

        var openingHoursDayValidation = openingHoursSheet.Range(3, 2, 1000, 2).CreateDataValidation();
        openingHoursDayValidation.List("=_Lists!$E$2:$E$8", true);
        openingHoursDayValidation.IgnoreBlanks = false;

        var openingHoursClosedValidation = openingHoursSheet.Range(3, 7, 1000, 7).CreateDataValidation();
        openingHoursClosedValidation.List("=_Lists!$G$2:$G$3", true);
        openingHoursClosedValidation.IgnoreBlanks = true;

        var hoursRow = 3;
        foreach (var location in existingLocations)
        {
            foreach (var hour in location.OpeningHours.OrderBy(x => x.DayOfWeek))
            {
                openingHoursSheet.Cell(hoursRow, 1).Value = location.ErpId;
                openingHoursSheet.Cell(hoursRow, 2).Value = DayNames[Math.Clamp(hour.DayOfWeek, 0, 6)];
                openingHoursSheet.Cell(hoursRow, 3).Value = hour.OpenTime.HasValue ? hour.OpenTime.Value.ToString("hh\\:mm") : string.Empty;
                openingHoursSheet.Cell(hoursRow, 4).Value = hour.CloseTime.HasValue ? hour.CloseTime.Value.ToString("hh\\:mm") : string.Empty;
                openingHoursSheet.Cell(hoursRow, 5).Value = hour.OpenTime2.HasValue ? hour.OpenTime2.Value.ToString("hh\\:mm") : string.Empty;
                openingHoursSheet.Cell(hoursRow, 6).Value = hour.CloseTime2.HasValue ? hour.CloseTime2.Value.ToString("hh\\:mm") : string.Empty;
                openingHoursSheet.Cell(hoursRow, 7).Value = hour.IsClosed ? "TRUE" : "FALSE";
                hoursRow++;
            }
        }

        openingHoursSheet.Columns().AdjustToContents();

        // Sheet: Exceptions
        var exceptionsSheet = workbook.Worksheets.Add("Exceptions");
        exceptionsSheet.Cell(1, 1).Value = "ErpId";
        exceptionsSheet.Cell(1, 2).Value = "Date";
        exceptionsSheet.Cell(1, 3).Value = "OpenTime";
        exceptionsSheet.Cell(1, 4).Value = "CloseTime";
        exceptionsSheet.Cell(1, 5).Value = "IsClosed";
        exceptionsSheet.Cell(1, 6).Value = "Note";

        exceptionsSheet.Cell(2, 1).Value = "Required";
        exceptionsSheet.Cell(2, 2).Value = "Required (yyyy-MM-dd)";
        exceptionsSheet.Cell(2, 3).Value = "Required unless closed (HH:mm)";
        exceptionsSheet.Cell(2, 4).Value = "Required unless closed (HH:mm)";
        exceptionsSheet.Cell(2, 5).Value = "Optional (TRUE/FALSE)";
        exceptionsSheet.Cell(2, 6).Value = "Optional";

        exceptionsSheet.Column(2).Style.NumberFormat.Format = "@";
        exceptionsSheet.Column(3).Style.NumberFormat.Format = "@";
        exceptionsSheet.Column(4).Style.NumberFormat.Format = "@";

        var exceptionsClosedValidation = exceptionsSheet.Range(3, 5, 1000, 5).CreateDataValidation();
        exceptionsClosedValidation.List("=_Lists!$G$2:$G$3", true);
        exceptionsClosedValidation.IgnoreBlanks = true;

        var exceptionRow = 3;
        foreach (var location in existingLocations)
        {
            foreach (var exception in location.Exceptions.OrderBy(x => x.Date))
            {
                exceptionsSheet.Cell(exceptionRow, 1).Value = location.ErpId;
                exceptionsSheet.Cell(exceptionRow, 2).Value = exception.Date.ToString("yyyy-MM-dd");
                exceptionsSheet.Cell(exceptionRow, 3).Value = exception.OpenTime.HasValue ? exception.OpenTime.Value.ToString("hh\\:mm") : string.Empty;
                exceptionsSheet.Cell(exceptionRow, 4).Value = exception.CloseTime.HasValue ? exception.CloseTime.Value.ToString("hh\\:mm") : string.Empty;
                exceptionsSheet.Cell(exceptionRow, 5).Value = exception.IsClosed ? "TRUE" : "FALSE";
                exceptionsSheet.Cell(exceptionRow, 6).Value = exception.Note ?? string.Empty;
                exceptionRow++;
            }
        }

        exceptionsSheet.Columns().AdjustToContents();
        
        // Generate file
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ServiceLocations_Template_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Upload Excel file and bulk upsert service locations (insert or update)
    /// </summary>
    [HttpPost("excel")]
    [ProducesResponseType(typeof(BulkInsertResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkInsertResultDto>> UploadExcel(
        IFormFile file,
        [FromQuery] int? serviceTypeId,
        [FromQuery] int? ownerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file uploaded" });
            }

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "File must be an Excel file (.xlsx or .xls)" });
            }

            var request = new BulkInsertServiceLocationsRequest();
            var errors = new List<BulkErrorDto>();
            var rowValuesByRow = new Dictionary<int, string[]>();
            var itemRowNumbers = new List<int>();
            var errorRows = new HashSet<int>();
            var hasSheetErrors = false;
            var openingHoursByErpId = new Dictionary<int, List<ServiceLocationOpeningHoursDto>>();
            var openingHoursRowRefsByErpId = new Dictionary<int, List<int>>();
            var exceptionsByErpId = new Dictionary<int, List<ServiceLocationExceptionDto>>();
            var exceptionRowRefsByErpId = new Dictionary<int, List<int>>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet("ServiceLocations");
            
            if (worksheet == null)
            {
                return BadRequest(new { message = "Sheet 'ServiceLocations' not found in Excel file" });
            }

            // Determine ServiceTypeId from query param or Excel header
            int finalServiceTypeId;
            if (serviceTypeId.HasValue)
            {
                finalServiceTypeId = serviceTypeId.Value;
            }
            else
            {
                // Try to read from Excel header (Cell B1)
                var excelServiceTypeIdCell = worksheet.Cell(1, 2);
                var excelServiceTypeIdStr = excelServiceTypeIdCell.IsEmpty() ? string.Empty : excelServiceTypeIdCell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(excelServiceTypeIdStr) || !int.TryParse(excelServiceTypeIdStr, out finalServiceTypeId))
                {
                    return BadRequest(new ProblemDetails 
                    { 
                        Title = "Validation Error", 
                        Detail = "ServiceTypeId is required as query parameter or in Excel cell B1." 
                    });
                }
            }

            // Validate the determined ServiceTypeId
            var serviceType = await _dbContext.ServiceTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == finalServiceTypeId && st.IsActive, cancellationToken);

            if (serviceType == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = $"ServiceTypeId {finalServiceTypeId} is invalid or inactive."
                });
            }

            request.ServiceTypeId = finalServiceTypeId; // Set the service type for the bulk request

            // Determine OwnerId from query param or Excel header
            int finalOwnerId;
            if (ownerId.HasValue)
            {
                finalOwnerId = ownerId.Value;
            }
            else
            {
                // Try to read from Excel header (Cell B2)
                var excelOwnerIdCell = worksheet.Cell(2, 2);
                var excelOwnerIdStr = excelOwnerIdCell.IsEmpty() ? string.Empty : excelOwnerIdCell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(excelOwnerIdStr) || !int.TryParse(excelOwnerIdStr, out finalOwnerId))
                {
                    return BadRequest(new ProblemDetails 
                    { 
                        Title = "Validation Error", 
                        Detail = "OwnerId is required as query parameter or in Excel cell B2." 
                    });
                }
            }

            // Validate the determined OwnerId
            var owner = await _dbContext.ServiceLocationOwners
                .AsNoTracking()
                .FirstOrDefaultAsync(so => so.Id == finalOwnerId && so.IsActive, cancellationToken);

            if (owner == null)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Validation Error", 
                    Detail = $"OwnerId {finalOwnerId} is invalid or inactive." 
                });
            }

            if (serviceType.OwnerId != finalOwnerId)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = $"ServiceTypeId {finalServiceTypeId} does not belong to OwnerId {finalOwnerId}."
                });
            }

            // Check if both query params and Excel values are provided and they mismatch
            if (serviceTypeId.HasValue && ownerId.HasValue)
            {
                var excelServiceTypeIdCell = worksheet.Cell(1, 2);
                var excelOwnerIdCell = worksheet.Cell(2, 2);
                if (!excelServiceTypeIdCell.IsEmpty() && !excelOwnerIdCell.IsEmpty())
                {
                    var excelServiceTypeIdStr = excelServiceTypeIdCell.GetString().Trim();
                    var excelOwnerIdStr = excelOwnerIdCell.GetString().Trim();
                    if (int.TryParse(excelServiceTypeIdStr, out var excelServiceTypeId) && 
                        int.TryParse(excelOwnerIdStr, out var excelOwnerId))
                    {
                        if (excelServiceTypeId != finalServiceTypeId || excelOwnerId != finalOwnerId)
                        {
                            return BadRequest(new ProblemDetails 
                            { 
                                Title = "Validation Error", 
                                Detail = "Query parameters and Excel header values mismatch. Please ensure they match or remove one set." 
                            });
                        }
                    }
                }
            }

            request.OwnerId = finalOwnerId; // Set the owner for the bulk request

            // Parse OpeningHours sheet (optional)
            if (workbook.TryGetWorksheet("OpeningHours", out var openingHoursSheet))
            {
                var lastHoursRow = openingHoursSheet.LastRowUsed()?.RowNumber() ?? 0;
                var hourKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int row = 3; row <= lastHoursRow; row++)
                {
                    var rowRef = $"OpeningHours row {row}";
                    var erpIdCell = openingHoursSheet.Cell(row, 1);
                    if (erpIdCell.IsEmpty())
                    {
                        continue;
                    }

                    var erpIdStr = erpIdCell.GetString().Trim();
                    if (!int.TryParse(erpIdStr, out var erpId) || erpId <= 0)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing ErpId"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var dayCell = openingHoursSheet.Cell(row, 2);
                    if (!TryParseDayOfWeekCell(dayCell, out var dayOfWeek))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid Day (expected Sunday-Saturday)"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!TryParseBoolCell(openingHoursSheet.Cell(row, 7), out var isClosedValue))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid IsClosed value"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var isClosed = isClosedValue ?? false;

                    if (!TryParseTimeCell(openingHoursSheet.Cell(row, 3), out var openTime)
                        || !TryParseTimeCell(openingHoursSheet.Cell(row, 4), out var closeTime)
                        || !TryParseTimeCell(openingHoursSheet.Cell(row, 5), out var openTime2)
                        || !TryParseTimeCell(openingHoursSheet.Cell(row, 6), out var closeTime2))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid time format (expected HH:mm)"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!isClosed && (!openTime.HasValue || !closeTime.HasValue))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime and CloseTime are required when not closed"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime must be before CloseTime"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!isClosed && (openTime2.HasValue ^ closeTime2.HasValue))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime2 and CloseTime2 are required together when using a lunch break"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!isClosed && openTime2.HasValue && closeTime2.HasValue && openTime2.Value >= closeTime2.Value)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime2 must be before CloseTime2"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!isClosed && openTime.HasValue && closeTime.HasValue && openTime2.HasValue && closeTime2.HasValue
                        && closeTime.Value > openTime2.Value)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "CloseTime must be before OpenTime2 when using a lunch break"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var key = $"{erpId}:{dayOfWeek}";
                    if (!hourKeySet.Add(key))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Duplicate opening hours for the same day"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var dto = new ServiceLocationOpeningHoursDto
                    {
                        DayOfWeek = dayOfWeek,
                        OpenTime = isClosed || !openTime.HasValue ? null : openTime.Value.ToString("hh\\:mm"),
                        CloseTime = isClosed || !closeTime.HasValue ? null : closeTime.Value.ToString("hh\\:mm"),
                        OpenTime2 = isClosed || !openTime2.HasValue ? null : openTime2.Value.ToString("hh\\:mm"),
                        CloseTime2 = isClosed || !closeTime2.HasValue ? null : closeTime2.Value.ToString("hh\\:mm"),
                        IsClosed = isClosed
                    };

                    if (!openingHoursByErpId.TryGetValue(erpId, out var list))
                    {
                        list = new List<ServiceLocationOpeningHoursDto>();
                        openingHoursByErpId[erpId] = list;
                    }

                    list.Add(dto);

                    if (!openingHoursRowRefsByErpId.TryGetValue(erpId, out var rowRefs))
                    {
                        rowRefs = new List<int>();
                        openingHoursRowRefsByErpId[erpId] = rowRefs;
                    }

                    rowRefs.Add(row);
                }
            }

            // Parse Exceptions sheet (optional)
            if (workbook.TryGetWorksheet("Exceptions", out var exceptionsSheet))
            {
                var lastExceptionRow = exceptionsSheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int row = 3; row <= lastExceptionRow; row++)
                {
                    var rowRef = $"Exceptions row {row}";
                    var erpIdCell = exceptionsSheet.Cell(row, 1);
                    if (erpIdCell.IsEmpty())
                    {
                        continue;
                    }

                    var erpIdStr = erpIdCell.GetString().Trim();
                    if (!int.TryParse(erpIdStr, out var erpId) || erpId <= 0)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing ErpId"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!TryParseDateCell(exceptionsSheet.Cell(row, 2), out var exceptionDate))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing Date (expected yyyy-MM-dd)"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!TryParseBoolCell(exceptionsSheet.Cell(row, 5), out var isClosedValue))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid IsClosed value"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var isClosed = isClosedValue ?? false;

                    if (!TryParseTimeCell(exceptionsSheet.Cell(row, 3), out var openTime)
                        || !TryParseTimeCell(exceptionsSheet.Cell(row, 4), out var closeTime))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid time format (expected HH:mm)"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (!isClosed && (!openTime.HasValue || !closeTime.HasValue))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime and CloseTime are required when not closed"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "OpenTime must be before CloseTime"
                        });
                        hasSheetErrors = true;
                        continue;
                    }

                    var dto = new ServiceLocationExceptionDto
                    {
                        Date = exceptionDate.ToDateTime(TimeOnly.MinValue),
                        OpenTime = isClosed || !openTime.HasValue ? null : openTime.Value.ToString("hh\\:mm"),
                        CloseTime = isClosed || !closeTime.HasValue ? null : closeTime.Value.ToString("hh\\:mm"),
                        IsClosed = isClosed,
                        Note = string.IsNullOrWhiteSpace(exceptionsSheet.Cell(row, 6).GetString())
                            ? null
                            : exceptionsSheet.Cell(row, 6).GetString().Trim()
                    };

                    if (!exceptionsByErpId.TryGetValue(erpId, out var list))
                    {
                        list = new List<ServiceLocationExceptionDto>();
                        exceptionsByErpId[erpId] = list;
                    }

                    list.Add(dto);

                    if (!exceptionRowRefsByErpId.TryGetValue(erpId, out var rowRefs))
                    {
                        rowRefs = new List<int>();
                        exceptionRowRefsByErpId[erpId] = rowRefs;
                    }

                    rowRefs.Add(row);
                }
            }

            // Parse rows starting at row 6 (after service type info, owner info, headers, and instructions)
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            
            for (int row = 6; row <= lastRow; row++)
            {
                var rowRef = $"Excel row {row}";
                rowValuesByRow[row] = new[]
                {
                    worksheet.Cell(row, 1).GetFormattedString(),
                    worksheet.Cell(row, 2).GetFormattedString(),
                    worksheet.Cell(row, 3).GetFormattedString(),
                    worksheet.Cell(row, 4).GetFormattedString(),
                    worksheet.Cell(row, 5).GetFormattedString(),
                    worksheet.Cell(row, 6).GetFormattedString(),
                    worksheet.Cell(row, 7).GetFormattedString(),
                    worksheet.Cell(row, 8).GetFormattedString(),
                    worksheet.Cell(row, 9).GetFormattedString(),
                    worksheet.Cell(row, 10).GetFormattedString(),
                    worksheet.Cell(row, 11).GetFormattedString(),
                    worksheet.Cell(row, 12).GetFormattedString(),
                    worksheet.Cell(row, 13).GetFormattedString(),
                    worksheet.Cell(row, 14).GetFormattedString()
                };
                
                try
                {
                    // Skip empty rows
                    var erpIdCell = worksheet.Cell(row, 1);
                    if (erpIdCell.IsEmpty())
                    {
                        continue;
                    }

                    // Parse ErpId
                    var erpIdStr = erpIdCell.IsEmpty() ? string.Empty : erpIdCell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(erpIdStr) || !int.TryParse(erpIdStr, out var erpId) || erpId <= 0)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing ErpId"
                        });
                        errorRows.Add(row);
                        continue;
                    }

                    // Parse Name
                    var nameCell = worksheet.Cell(row, 2);
                    var name = nameCell.IsEmpty() ? string.Empty : nameCell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Name is required"
                        });
                        errorRows.Add(row);
                        continue;
                    }

                    // Parse Address (optional)
                    var addressCell = worksheet.Cell(row, 3);
                    var address = addressCell.IsEmpty() ? string.Empty : addressCell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(address))
                    {
                        address = null;
                    }

                // Parse Latitude (optional if address provided)
                double? latitude = null;
                var latitudeCell = worksheet.Cell(row, 4);
                if (!latitudeCell.IsEmpty())
                {
                    var latitudeStr = latitudeCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(latitudeStr) && double.TryParse(latitudeStr, out var parsedLatitude))
                    {
                        latitude = parsedLatitude;
                    }
                    else
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid Latitude"
                        });
                        errorRows.Add(row);
                        continue;
                    }
                }

                // Parse Longitude (optional if address provided)
                double? longitude = null;
                var longitudeCell = worksheet.Cell(row, 5);
                if (!longitudeCell.IsEmpty())
                {
                    var longitudeStr = longitudeCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(longitudeStr) && double.TryParse(longitudeStr, out var parsedLongitude))
                    {
                        longitude = parsedLongitude;
                    }
                    else
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid Longitude"
                        });
                        errorRows.Add(row);
                        continue;
                    }
                }

                if (latitude == 0)
                {
                    latitude = null;
                }
                if (longitude == 0)
                {
                    longitude = null;
                }

                if (latitude.HasValue != longitude.HasValue)
                {
                    errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = "Provide both Latitude and Longitude, or leave both empty"
                    });
                    errorRows.Add(row);
                    continue;
                }

                if (!latitude.HasValue && string.IsNullOrWhiteSpace(address))
                {
                    errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = "Address or Latitude/Longitude is required"
                    });
                    errorRows.Add(row);
                    continue;
                }

                // Parse DueDate
                var dueDateCell = worksheet.Cell(row, 6);
                DateOnly dueDate;
                if (dueDateCell.IsEmpty())
                {
                    errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = "Invalid or missing DueDate (expected yyyy-MM-dd)"
                    });
                    errorRows.Add(row);
                    continue;
                }
                
                // Handle different cell data types
                if (dueDateCell.DataType == XLDataType.DateTime)
                {
                    // Cell is formatted as DateTime in Excel
                    try
                    {
                        var dateTimeValue = dueDateCell.GetDateTime();
                        dueDate = DateOnly.FromDateTime(dateTimeValue);
                    }
                    catch
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing DueDate (expected yyyy-MM-dd)"
                        });
                        errorRows.Add(row);
                        continue;
                    }
                }
                else
                {
                    // Cell is formatted as text or number - try to parse as string
                    var dueDateStr = dueDateCell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(dueDateStr))
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = rowRef,
                            Message = "Invalid or missing DueDate (expected yyyy-MM-dd)"
                        });
                        errorRows.Add(row);
                        continue;
                    }
                    
                    // Try parsing as yyyy-MM-dd format first (expected format)
                    if (!DateOnly.TryParseExact(dueDateStr, "yyyy-MM-dd", out dueDate))
                    {
                        // If that fails, try parsing as a general date (fallback)
                        if (!DateOnly.TryParse(dueDateStr, out dueDate))
                        {
                            errors.Add(new BulkErrorDto
                            {
                                RowRef = rowRef,
                                Message = "Invalid or missing DueDate (expected yyyy-MM-dd)"
                            });
                            errorRows.Add(row);
                            continue;
                        }
                    }
                }

                // Parse PriorityDate (optional)
                DateOnly? priorityDate = null;
                var priorityDateCell = worksheet.Cell(row, 7);
                if (!priorityDateCell.IsEmpty())
                {
                    if (priorityDateCell.DataType == XLDataType.DateTime)
                    {
                        // Cell is formatted as DateTime in Excel
                        try
                        {
                            var dateTimeValue = priorityDateCell.GetDateTime();
                            priorityDate = DateOnly.FromDateTime(dateTimeValue);
                        }
                        catch
                        {
                            errors.Add(new BulkErrorDto
                            {
                                RowRef = rowRef,
                                Message = "Invalid PriorityDate (expected yyyy-MM-dd)"
                            });
                            continue;
                        }
                    }
                    else
                    {
                        // Cell is formatted as text or number - try to parse as string
                        var priorityDateStr = priorityDateCell.GetString().Trim();
                        if (!string.IsNullOrWhiteSpace(priorityDateStr))
                        {
                            // Try parsing as yyyy-MM-dd format first (expected format)
                            if (DateOnly.TryParseExact(priorityDateStr, "yyyy-MM-dd", out var parsedPriorityDate))
                            {
                                priorityDate = parsedPriorityDate;
                            }
                            else if (DateOnly.TryParse(priorityDateStr, out parsedPriorityDate))
                            {
                                // Try parsing as a general date (fallback)
                                priorityDate = parsedPriorityDate;
                            }
                            else
                            {
                                errors.Add(new BulkErrorDto
                                {
                                    RowRef = rowRef,
                                    Message = "Invalid PriorityDate (expected yyyy-MM-dd)"
                                });
                                errorRows.Add(row);
                                continue;
                            }
                        }
                    }
                }

                // Parse ServiceMinutes (optional)
                int? serviceMinutes = null;
                var serviceMinutesCell = worksheet.Cell(row, 8);
                var serviceMinutesStr = serviceMinutesCell.IsEmpty() ? string.Empty : serviceMinutesCell.GetString().Trim();
                if (!string.IsNullOrWhiteSpace(serviceMinutesStr))
                {
                    if (int.TryParse(serviceMinutesStr, out var parsedServiceMinutes))
                    {
                        serviceMinutes = parsedServiceMinutes;
                    }
                }

                // Parse DriverInstruction (optional)
                string? driverInstruction = null;
                var driverInstructionCell = worksheet.Cell(row, 9);
                if (!driverInstructionCell.IsEmpty())
                {
                    var driverInstructionStr = driverInstructionCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(driverInstructionStr))
                    {
                        driverInstruction = driverInstructionStr;
                    }
                }

                List<string>? extraInstructions = null;
                var extraInstructionsCell = worksheet.Cell(row, 10);
                if (!extraInstructionsCell.IsEmpty())
                {
                    extraInstructions = ParseInstructionCell(extraInstructionsCell);
                }

                int? minVisitDurationMinutes = null;
                var minVisitDurationCell = worksheet.Cell(row, 11);
                if (!minVisitDurationCell.IsEmpty())
                {
                    var minVisitDurationStr = minVisitDurationCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(minVisitDurationStr))
                    {
                        if (int.TryParse(minVisitDurationStr, out var parsedMinVisitDuration))
                        {
                            minVisitDurationMinutes = parsedMinVisitDuration;
                        }
                        else
                        {
                            errors.Add(new BulkErrorDto
                            {
                                RowRef = rowRef,
                                Message = "Invalid MinVisitDurationMinutes"
                            });
                            errorRows.Add(row);
                            continue;
                        }
                    }
                }

                int? maxVisitDurationMinutes = null;
                var maxVisitDurationCell = worksheet.Cell(row, 12);
                if (!maxVisitDurationCell.IsEmpty())
                {
                    var maxVisitDurationStr = maxVisitDurationCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(maxVisitDurationStr))
                    {
                        if (int.TryParse(maxVisitDurationStr, out var parsedMaxVisitDuration))
                        {
                            maxVisitDurationMinutes = parsedMaxVisitDuration;
                        }
                        else
                        {
                            errors.Add(new BulkErrorDto
                            {
                                RowRef = rowRef,
                                Message = "Invalid MaxVisitDurationMinutes"
                            });
                            errorRows.Add(row);
                            continue;
                        }
                    }
                }

                string? accountId = null;
                var accountIdCell = worksheet.Cell(row, 13);
                if (!accountIdCell.IsEmpty())
                {
                    var accountIdStr = accountIdCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(accountIdStr))
                    {
                        accountId = accountIdStr;
                    }
                }

                string? serialNumber = null;
                var serialNumberCell = worksheet.Cell(row, 14);
                if (!serialNumberCell.IsEmpty())
                {
                    var serialNumberStr = serialNumberCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(serialNumberStr))
                    {
                        serialNumber = serialNumberStr;
                    }
                }

                var item = new BulkServiceLocationInsertDto
                {
                    ErpId = erpId,
                    AccountId = accountId,
                    SerialNumber = serialNumber,
                    Name = name,
                    Address = address,
                    Latitude = latitude,
                    Longitude = longitude,
                    DueDate = dueDate,
                    PriorityDate = priorityDate,
                    ServiceMinutes = serviceMinutes,
                    DriverInstruction = driverInstruction,
                    ExtraInstructions = extraInstructions,
                    MinVisitDurationMinutes = minVisitDurationMinutes,
                    MaxVisitDurationMinutes = maxVisitDurationMinutes
                };

                    request.Items.Add(item);
                    itemRowNumbers.Add(row);
                }
                catch (Exception ex)
                {
                    errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = $"Error parsing row: {ex.Message}"
                    });
                    errorRows.Add(row);
                }
            }

            var itemsByErpId = request.Items.ToDictionary(item => item.ErpId);

            foreach (var kvp in openingHoursByErpId)
            {
                if (itemsByErpId.TryGetValue(kvp.Key, out var item))
                {
                    item.OpeningHours = kvp.Value;
                    continue;
                }

                if (openingHoursRowRefsByErpId.TryGetValue(kvp.Key, out var rowRefs))
                {
                    foreach (var row in rowRefs)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = $"OpeningHours row {row}",
                            Message = $"ErpId {kvp.Key} not found in ServiceLocations sheet"
                        });
                    }
                }
            }

            foreach (var kvp in exceptionsByErpId)
            {
                if (itemsByErpId.TryGetValue(kvp.Key, out var item))
                {
                    item.Exceptions = kvp.Value;
                    continue;
                }

                if (exceptionRowRefsByErpId.TryGetValue(kvp.Key, out var rowRefs))
                {
                    foreach (var row in rowRefs)
                    {
                        errors.Add(new BulkErrorDto
                        {
                            RowRef = $"Exceptions row {row}",
                            Message = $"ErpId {kvp.Key} not found in ServiceLocations sheet"
                        });
                    }
                }
            }

            // Call bulk insert service
            var result = await _bulkInsertService.InsertAsync(request, cancellationToken);
            
            // Merge parsing errors with insert errors
            result.Errors.AddRange(errors);

            foreach (var error in result.Errors)
            {
                if (error.RowRef.StartsWith("Excel row ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(error.RowRef.Replace("Excel row ", string.Empty), out var row))
                {
                    errorRows.Add(row);
                    continue;
                }

                if (error.RowRef.StartsWith("JSON item ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(error.RowRef.Replace("JSON item ", string.Empty), out var itemIndex))
                {
                    var itemRowIndex = itemIndex - 1;
                    if (itemRowIndex >= 0 && itemRowIndex < itemRowNumbers.Count)
                    {
                        errorRows.Add(itemRowNumbers[itemRowIndex]);
                    }
                }
            }

            if (errorRows.Count > 0 || hasSheetErrors)
            {
                var rows = errorRows
                    .OrderBy(r => r)
                    .Where(rowValuesByRow.ContainsKey)
                    .Select(r => rowValuesByRow[r])
                    .ToList();

                var allServiceTypes = await _dbContext.ServiceTypes
                    .Where(st => st.IsActive)
                    .OrderBy(st => st.Name)
                    .Select(st => new { st.Id, st.Name })
                    .ToListAsync(cancellationToken);

                var allOwners = await _dbContext.ServiceLocationOwners
                    .Where(so => so.IsActive)
                    .OrderBy(so => so.Name)
                    .Select(so => new { so.Id, so.Name })
                    .ToListAsync(cancellationToken);

                var serviceTypeList = allServiceTypes
                    .Select(st => (Id: st.Id, Name: st.Name))
                    .ToList();

                var ownerList = allOwners
                    .Select(so => (Id: so.Id, Name: so.Name))
                    .ToList();

                using var errorWorkbook = BuildServiceLocationErrorWorkbook(
                    serviceType,
                    owner,
                    serviceTypeList,
                    ownerList,
                    rows,
                    result.Errors);

                using var errorStream = new MemoryStream();
                errorWorkbook.SaveAs(errorStream);
                errorStream.Position = 0;

                return File(
                    errorStream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"ServiceLocations_Errors_{DateTime.Now:yyyyMMdd}.xlsx");
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Error processing Excel file",
                Detail = $"An error occurred while processing the Excel file: {ex.Message}",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private static List<string>? ParseInstructionCell(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var raw = cell.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Equals("[]", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>();
        }

        var parts = trimmed
            .Replace("\r\n", "\n")
            .Split(new[] { '|', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);

        return parts
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
    }

    private static bool TryParseBoolCell(IXLCell cell, out bool? value)
    {
        value = null;
        if (cell.IsEmpty())
        {
            return true;
        }

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (bool.TryParse(raw, out var parsedBool))
        {
            value = parsedBool;
            return true;
        }

        if (int.TryParse(raw, out var parsedInt))
        {
            value = parsedInt != 0;
            return true;
        }

        return false;
    }

    private static bool TryParseTimeCell(IXLCell cell, out TimeSpan? value)
    {
        value = null;
        if (cell.IsEmpty())
        {
            return true;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            try
            {
                value = cell.GetDateTime().TimeOfDay;
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (cell.DataType == XLDataType.TimeSpan)
        {
            try
            {
                value = cell.GetTimeSpan();
                return true;
            }
            catch
            {
                return false;
            }
        }

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (TimeSpan.TryParseExact(raw, "hh\\:mm", null, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (TimeSpan.TryParse(raw, out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseDateCell(IXLCell cell, out DateOnly date)
    {
        date = default;
        if (cell.IsEmpty())
        {
            return false;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            try
            {
                date = DateOnly.FromDateTime(cell.GetDateTime());
                return true;
            }
            catch
            {
                return false;
            }
        }

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", out date))
        {
            return true;
        }

        if (DateOnly.TryParse(raw, out date))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseDayOfWeekCell(IXLCell cell, out int dayOfWeek)
    {
        dayOfWeek = -1;
        if (cell.IsEmpty())
        {
            return false;
        }

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (int.TryParse(raw, out var parsedDay) && parsedDay >= 0 && parsedDay <= 6)
        {
            dayOfWeek = parsedDay;
            return true;
        }

        var normalized = raw.ToLowerInvariant();
        for (int i = 0; i < DayNames.Length; i++)
        {
            var dayLower = DayNames[i].ToLowerInvariant();
            if (normalized == dayLower)
            {
                dayOfWeek = i;
                return true;
            }

            if (dayLower.StartsWith(normalized))
            {
                dayOfWeek = i;
                return true;
            }
        }

        return false;
    }

    private static XLWorkbook BuildServiceLocationErrorWorkbook(
        ServiceType serviceType,
        ServiceLocationOwner owner,
        List<(int Id, string Name)> allServiceTypes,
        List<(int Id, string Name)> allOwners,
        List<string[]> rows,
        List<BulkErrorDto> errors)
    {
        var workbook = new XLWorkbook();

        var listsSheet = workbook.Worksheets.Add("_Lists");
        listsSheet.Hide();

        listsSheet.Cell(1, 1).Value = "ServiceTypeId";
        listsSheet.Cell(1, 2).Value = "ServiceTypeName";
        for (int i = 0; i < allServiceTypes.Count; i++)
        {
            listsSheet.Cell(i + 2, 1).Value = allServiceTypes[i].Id;
            listsSheet.Cell(i + 2, 2).Value = allServiceTypes[i].Name;
        }

        listsSheet.Cell(1, 3).Value = "OwnerId";
        listsSheet.Cell(1, 4).Value = "OwnerName";
        for (int i = 0; i < allOwners.Count; i++)
        {
            listsSheet.Cell(i + 2, 3).Value = allOwners[i].Id;
            listsSheet.Cell(i + 2, 4).Value = allOwners[i].Name;
        }

        var sheet = workbook.Worksheets.Add("ServiceLocations");

        sheet.Cell(1, 1).Value = "ServiceTypeId:";
        sheet.Cell(1, 2).Value = serviceType.Id;
        var serviceTypeIdValidation = sheet.Cell(1, 2).CreateDataValidation();
        serviceTypeIdValidation.List($"=_Lists!$A$2:$A${allServiceTypes.Count + 1}", true);
        serviceTypeIdValidation.IgnoreBlanks = false;

        sheet.Cell(1, 3).Value = "ServiceTypeCode:";
        sheet.Cell(1, 4).Value = serviceType.Code;
        sheet.Cell(1, 5).Value = "ServiceTypeName:";
        sheet.Cell(1, 6).Value = serviceType.Name;
        sheet.Cell(1, 6).FormulaA1 = $"=IFERROR(VLOOKUP(B1,_Lists!$A$2:$B${allServiceTypes.Count + 1},2,FALSE),\"\")";

        sheet.Cell(2, 1).Value = "OwnerId:";
        sheet.Cell(2, 2).Value = owner.Id;
        var ownerIdValidation = sheet.Cell(2, 2).CreateDataValidation();
        ownerIdValidation.List($"=_Lists!$C$2:$C${allOwners.Count + 1}", true);
        ownerIdValidation.IgnoreBlanks = false;

        sheet.Cell(2, 3).Value = "OwnerCode:";
        sheet.Cell(2, 4).Value = owner.Code;
        sheet.Cell(2, 5).Value = "OwnerName:";
        sheet.Cell(2, 6).Value = owner.Name;
        sheet.Cell(2, 6).FormulaA1 = $"=IFERROR(VLOOKUP(B2,_Lists!$C$2:$D${allOwners.Count + 1},2,FALSE),\"\")";

        sheet.Cell(4, 1).Value = "ErpId";
        sheet.Cell(4, 2).Value = "Name";
        sheet.Cell(4, 3).Value = "Address";
        sheet.Cell(4, 4).Value = "Latitude";
        sheet.Cell(4, 5).Value = "Longitude";
        sheet.Cell(4, 6).Value = "DueDate";
        sheet.Cell(4, 7).Value = "PriorityDate";
        sheet.Cell(4, 8).Value = "ServiceMinutes";
        sheet.Cell(4, 9).Value = "DriverInstruction";

        sheet.Cell(5, 1).Value = "Required";
        sheet.Cell(5, 2).Value = "Required";
        sheet.Cell(5, 3).Value = "Required if no coordinates";
        sheet.Cell(5, 4).Value = "Required if no address (-90 to 90)";
        sheet.Cell(5, 5).Value = "Required if no address (-180 to 180)";
        sheet.Cell(5, 6).Value = "Required (yyyy-MM-dd)";
        sheet.Cell(5, 7).Value = "Optional (yyyy-MM-dd)";
        sheet.Cell(5, 8).Value = "Optional (1-240, default 20)";
        sheet.Cell(5, 9).Value = "Optional (shown to driver)";

        var dueDateColumn = sheet.Column(6);
        var priorityDateColumn = sheet.Column(7);
        dueDateColumn.Style.NumberFormat.Format = "@";
        priorityDateColumn.Style.NumberFormat.Format = "@";

        var startRow = 6;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = startRow + i;
            var values = rows[i];
            for (int col = 1; col <= 14; col++)
            {
                sheet.Cell(row, col).Value = values.Length >= col ? values[col - 1] : string.Empty;
            }
        }

        var infoRange = sheet.Range(1, 1, 2, 4);
        infoRange.Style.Font.Bold = true;
        infoRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        var headerRange = sheet.Range(3, 1, 3, 14);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        
        var instructionRange = sheet.Range(4, 1, 4, 14);
        instructionRange.Style.Font.Italic = true;
        instructionRange.Style.Font.FontColor = XLColor.DarkGray;

        sheet.Columns().AdjustToContents();

        var errorsSheet = workbook.Worksheets.Add("Errors");
        errorsSheet.Cell(1, 1).Value = "RowRef";
        errorsSheet.Cell(1, 2).Value = "Message";
        for (int i = 0; i < errors.Count; i++)
        {
            errorsSheet.Cell(i + 2, 1).Value = errors[i].RowRef;
            errorsSheet.Cell(i + 2, 2).Value = errors[i].Message;
        }
        errorsSheet.Columns().AdjustToContents();

        return workbook;
    }
}

