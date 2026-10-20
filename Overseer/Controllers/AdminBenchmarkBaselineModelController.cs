namespace Overseer.Controllers;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;

/// <summary>
/// The GnollBench baseline model: read it, set it from a configuration with the Benchmark role, or clear
/// it. Every change appends a history row; nothing is updated or deleted.
/// </summary>
[Route("api/admin/benchmark/baseline-model")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkBaselineModelController : ControllerBase
{
    private readonly BenchmarkBaselineModelService _baselineModel;

    public AdminBenchmarkBaselineModelController(BenchmarkBaselineModelService baselineModel)
    {
        _baselineModel = baselineModel;
    }

    /// <summary>The baseline model; 204 when none is set.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var baselineModel = await _baselineModel.GetAsync(ct);
            return baselineModel == null ? NoContent() : Ok(baselineModel);
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Sets the baseline model to the configuration's current settings, or clears it for a null id.
    /// Returns the new baseline model, or 204 when cleared. 400 for no body, a missing configuration or
    /// one without the Benchmark role.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] BenchmarkBaselineModelSetRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        try
        {
            string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var (result, error) = await _baselineModel.SetAsync(request.ConfigurationId, userId, ct);
            if (error != null) return BadRequest(new { error });

            return result == null ? NoContent() : Ok(result);
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }
}
