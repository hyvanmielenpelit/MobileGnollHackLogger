namespace Overseer.Controllers;

using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Services;
using Overseer.Services.SourceIndexing;

/// <summary>
/// The Admin page's Developer Tools: the GnollHack and NetHack source index status and its manual
/// jobs, and the configuration health report.
/// </summary>
[Route("api/admin/devtools")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminDevToolsController : ControllerBase
{
    private readonly SourceCodeService _gnollHackSource;
    private readonly NetHackSourceCodeService _netHackSource;
    private readonly ConfigHealthService _configHealth;

    public AdminDevToolsController(SourceCodeService gnollHackSource, NetHackSourceCodeService netHackSource,
        ConfigHealthService configHealth)
    {
        _gnollHackSource = gnollHackSource;
        _netHackSource = netHackSource;
        _configHealth = configHealth;
    }

    [HttpGet("source/status")]
    public IActionResult GetSourceStatus() => Ok(new SourceDashboardDto
    {
        GnollHack = _gnollHackSource.GetStatus(),
        NetHack = _netHackSource.GetStatus(),
        ServerTimeUtc = DateTime.UtcNow
    });

    /// <summary>Re-indexes the GnollHack source; makedefs runs only when its inputs changed.</summary>
    [HttpPost("source/reindex")]
    public IActionResult Reindex() => Started(_gnollHackSource.RequestReindex(SourceIndexTrigger.Manual));

    /// <summary>Rebuilds makedefs and regenerates the headers, then re-indexes when they changed.</summary>
    [HttpPost("source/makedefs")]
    public IActionResult BuildMakedefs() => Started(_gnollHackSource.RequestMakedefsBuild());

    [HttpPost("source/cancel")]
    public IActionResult Cancel() => Ok(new { canceled = _gnollHackSource.CancelCurrentJob() });

    [HttpGet("config-health")]
    public IActionResult GetConfigHealth() => Ok(_configHealth.GetHealthReport());

    private IActionResult Started(SourceIndexStartResult result) => result switch
    {
        SourceIndexStartResult.Started => Accepted(new { jobId = _gnollHackSource.CurrentJobId ?? _gnollHackSource.LastStartedJobId }),
        SourceIndexStartResult.AlreadyRunning => Conflict(new { message = "A source job is already running." }),
        _ => BadRequest(new { message = "SourceCodePath is not configured." })
    };
}

/// <summary>Both source services' status for the GnollHack Source dashboard.</summary>
public sealed class SourceDashboardDto
{
    public SourceIndexStatusDto GnollHack { get; init; } = null!;
    public SourceIndexStatusDto NetHack { get; init; } = null!;
    public DateTime ServerTimeUtc { get; init; }
}
