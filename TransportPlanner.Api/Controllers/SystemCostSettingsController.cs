using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/system-cost-settings")]
[Authorize(Policy = "RequireAdmin")]
public class SystemCostSettingsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;

    public SystemCostSettingsController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SystemCostSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemCostSettingsDto>> Get(CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.SystemCostSettings
            .AsNoTracking()
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            return Ok(new SystemCostSettingsDto
            {
                FuelCostPerKm = 0,
                PersonnelCostPerHour = 0,
                CurrencyCode = "EUR"
            });
        }

        return Ok(new SystemCostSettingsDto
        {
            FuelCostPerKm = settings.FuelCostPerKm,
            PersonnelCostPerHour = settings.PersonnelCostPerHour,
            CurrencyCode = settings.CurrencyCode
        });
    }

    [HttpPut]
    [ProducesResponseType(typeof(SystemCostSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemCostSettingsDto>> Update(
        [FromBody] SystemCostSettingsDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.FuelCostPerKm < 0 || request.PersonnelCostPerHour < 0)
        {
            return BadRequest(new { message = "Costs must be >= 0." });
        }

        var settings = await _dbContext.SystemCostSettings
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            settings = new Domain.Entities.SystemCostSettings();
            _dbContext.SystemCostSettings.Add(settings);
        }

        settings.FuelCostPerKm = request.FuelCostPerKm;
        settings.PersonnelCostPerHour = request.PersonnelCostPerHour;
        settings.CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "EUR" : request.CurrencyCode.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new SystemCostSettingsDto
        {
            FuelCostPerKm = settings.FuelCostPerKm,
            PersonnelCostPerHour = settings.PersonnelCostPerHour,
            CurrencyCode = settings.CurrencyCode
        });
    }
}
