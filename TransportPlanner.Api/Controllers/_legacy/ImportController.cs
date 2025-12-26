using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly IPoleImportService _poleImportService;

    public ImportController(IPoleImportService poleImportService)
    {
        _poleImportService = poleImportService;
    }

    /// <summary>
    /// Imports poles from ERP system for the specified number of days
    /// </summary>
    /// <param name="days">Number of days to import (1-31)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result</returns>
    [HttpPost("poles")]
    [ProducesResponseType(typeof(ImportPolesResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportPolesResultDto>> ImportPoles(
        [FromQuery] int days = 14,
        CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > 31)
        {
            return BadRequest(new { error = "Days must be between 1 and 31" });
        }

        var result = await _poleImportService.ImportDueWithinAsync(days, cancellationToken);
        return Ok(result);
    }
}
