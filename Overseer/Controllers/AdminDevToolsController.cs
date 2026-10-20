namespace Overseer.Controllers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.CorpusIndexing;
using Overseer.Services.SourceIndexing;
using Overseer.Services.Tools;

/// <summary>
/// The Admin page's Services tab: the GnollHack and NetHack source index status and its manual
/// jobs, the GnollHack wiki, NetHack wiki and knowledge base indexes with their re-index actions,
/// search probes and chat tool usage, and the configuration health report.
/// </summary>
[Route("api/admin/devtools")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminDevToolsController : ControllerBase
{
    /// <summary>The longest query a search probe accepts.</summary>
    public const int MaxProbeQueryLength = 500;

    private const int DefaultProbeMaxResultLength = 10000;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly SourceCodeService _gnollHackSource;
    private readonly NetHackSourceCodeService _netHackSource;
    private readonly ConfigHealthService _configHealth;
    private readonly WikiService _wiki;
    private readonly NetHackWikiService _netHackWiki;
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly IEnumerable<IToolHandler> _toolHandlers;
    private readonly CorpusToolUsageService _toolUsage;
    private readonly ApplicationDbContext _db;

    public AdminDevToolsController(SourceCodeService gnollHackSource, NetHackSourceCodeService netHackSource,
        ConfigHealthService configHealth, WikiService wiki, NetHackWikiService netHackWiki,
        KnowledgeBaseService knowledgeBase, IEnumerable<IToolHandler> toolHandlers,
        CorpusToolUsageService toolUsage, ApplicationDbContext db)
    {
        _gnollHackSource = gnollHackSource;
        _netHackSource = netHackSource;
        _configHealth = configHealth;
        _wiki = wiki;
        _netHackWiki = netHackWiki;
        _knowledgeBase = knowledgeBase;
        _toolHandlers = toolHandlers;
        _toolUsage = toolUsage;
        _db = db;
    }

    [HttpGet("source/status")]
    public IActionResult GetSourceStatus() => Ok(new SourceDashboardDto
    {
        GnollHack = _gnollHackSource.GetStatus(),
        NetHack = _netHackSource.GetStatus(),
        ServerTimeUtc = DateTime.UtcNow
    });

    /// <summary>
    /// Re-indexes the GnollHack source; makedefs runs only when its inputs changed. The same GnollBench
    /// rule as the GnollHack wiki.
    /// </summary>
    [HttpPost("source/reindex")]
    public async Task<IActionResult> Reindex([FromQuery] bool acknowledgeActiveBenchmark = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await BenchmarkRunConflict(acknowledgeActiveBenchmark, "the GnollHack source", cancellationToken)
                ?? Started(_gnollHackSource.RequestReindex(SourceIndexTrigger.Manual));
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Rebuilds makedefs and regenerates the headers, then re-indexes when they changed. The same
    /// GnollBench rule as the GnollHack wiki.
    /// </summary>
    [HttpPost("source/makedefs")]
    public async Task<IActionResult> BuildMakedefs([FromQuery] bool acknowledgeActiveBenchmark = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await BenchmarkRunConflict(acknowledgeActiveBenchmark, "the GnollHack source", cancellationToken)
                ?? Started(_gnollHackSource.RequestMakedefsBuild());
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("source/cancel")]
    public IActionResult Cancel() => Ok(new { canceled = _gnollHackSource.CancelCurrentJob() });

    [HttpGet("config-health")]
    public IActionResult GetConfigHealth() => Ok(_configHealth.GetHealthReport());

    [HttpGet("gnollhack-wiki/status")]
    public IActionResult GetGnollHackWikiStatus() => Ok(_wiki.GetStatus());

    [HttpGet("nethack-wiki/status")]
    public IActionResult GetNetHackWikiStatus() => Ok(_netHackWiki.GetStatus());

    [HttpGet("knowledge-base/status")]
    public IActionResult GetKnowledgeBaseStatus() => Ok(_knowledgeBase.GetStatus());

    /// <summary>
    /// Re-indexes the GnollHack wiki. While a GnollBench run executes, answers 409
    /// <c>benchmarkRunActive</c> unless <paramref name="acknowledgeActiveBenchmark"/> is set.
    /// </summary>
    [HttpPost("gnollhack-wiki/reindex")]
    public async Task<IActionResult> ReindexGnollHackWiki([FromQuery] bool acknowledgeActiveBenchmark = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StartCorpusReindex(acknowledgeActiveBenchmark, "the GnollHack wiki", _wiki.GetStatus,
                _wiki.RequestReindex, () => _wiki.LastStartedJobId, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Indexes the NetHack wiki once, now; the same GnollBench rule as the GnollHack wiki.</summary>
    [HttpPost("nethack-wiki/reindex")]
    public async Task<IActionResult> ReindexNetHackWiki([FromQuery] bool acknowledgeActiveBenchmark = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StartCorpusReindex(acknowledgeActiveBenchmark, "the NetHack wiki", _netHackWiki.GetStatus,
                _netHackWiki.RequestReindex, () => _netHackWiki.LastStartedJobId, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Reloads the knowledge base articles; the same GnollBench rule as the GnollHack wiki.</summary>
    [HttpPost("knowledge-base/reindex")]
    public async Task<IActionResult> ReindexKnowledgeBase([FromQuery] bool acknowledgeActiveBenchmark = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StartCorpusReindex(acknowledgeActiveBenchmark, "the knowledge base", _knowledgeBase.GetStatus,
                _knowledgeBase.RequestReindex, () => _knowledgeBase.LastStartedJobId, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Runs the chat's <c>wiki_search</c> against the live index; nothing is stored.</summary>
    [HttpPost("gnollhack-wiki/probe")]
    public async Task<IActionResult> ProbeGnollHackWiki([FromBody] CorpusProbeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await Probe("wiki_search", "query", "category", request, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Runs the chat's <c>nethack_wiki_search</c> against the live index; nothing is stored. The tool
    /// takes no namespace, so a filter is ignored.
    /// </summary>
    [HttpPost("nethack-wiki/probe")]
    public async Task<IActionResult> ProbeNetHackWiki([FromBody] CorpusProbeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await Probe("nethack_wiki_search", "query", null, request, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Runs the chat's <c>get_knowledge_article</c>; nothing is stored.</summary>
    [HttpPost("knowledge-base/probe")]
    public async Task<IActionResult> ProbeKnowledgeBase([FromBody] CorpusProbeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await Probe("get_knowledge_article", "topic", null, request, cancellationToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>A knowledge base article's raw text as loaded, front matter included.</summary>
    [HttpGet("knowledge-base/article")]
    public IActionResult GetKnowledgeArticle([FromQuery] string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return BadRequest(new { message = "A topic is required." });

        string? source = _knowledgeBase.GetArticleSource(topic);
        if (source == null)
            return NotFound(new { message = "No article has this topic." });

        var info = _knowledgeBase.GetStatus().Articles
            .FirstOrDefault(a => string.Equals(a.Topic, topic, StringComparison.OrdinalIgnoreCase));
        return Ok(new KnowledgeArticleSourceDto
        {
            Topic = info?.Topic ?? topic,
            Title = info is { HasTitle: true } ? info.Title : null,
            Source = source
        });
    }

    /// <summary>How the chat used the corpus tools over the last 7, 30 or 90 days.</summary>
    [HttpGet("corpus-tool-usage")]
    public async Task<IActionResult> GetCorpusToolUsage([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _toolUsage.GetAsync(days, cancellationToken));
        }
        catch (Exception ex) when (ex.IsCanceledBy(cancellationToken))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private IActionResult Started(SourceIndexStartResult result) => result switch
    {
        SourceIndexStartResult.Started => Accepted(new { jobId = _gnollHackSource.CurrentJobId ?? _gnollHackSource.LastStartedJobId }),
        SourceIndexStartResult.AlreadyRunning => Conflict(new { code = "alreadyRunning", message = "A source job is already running." }),
        _ => BadRequest(new { message = "SourceCodePath is not configured." })
    };

    /// <summary>409 <c>benchmarkRunActive</c> while a GnollBench run executes, unless acknowledged; otherwise null.</summary>
    private async Task<IActionResult?> BenchmarkRunConflict(bool acknowledgeActiveBenchmark, string corpusName,
        CancellationToken cancellationToken)
    {
        if (acknowledgeActiveBenchmark ||
            !await _db.BenchmarkRuns.AnyAsync(r => r.Status == BenchmarkRunStatus.Running, cancellationToken))
        {
            return null;
        }
        return Conflict(new
        {
            code = "benchmarkRunActive",
            message = $"A GnollBench run is executing and reads {corpusName}."
        });
    }

    private async Task<IActionResult> StartCorpusReindex(bool acknowledgeActiveBenchmark, string corpusName,
        Func<CorpusIndexStatusDto> getStatus, Func<CorpusIndexTrigger, CorpusIndexStartResult> requestReindex,
        Func<string?> lastStartedJobId, CancellationToken cancellationToken)
    {
        if (await BenchmarkRunConflict(acknowledgeActiveBenchmark, corpusName, cancellationToken) is { } conflict)
            return conflict;

        return requestReindex(CorpusIndexTrigger.Manual) switch
        {
            CorpusIndexStartResult.Started => Accepted(new { jobId = lastStartedJobId() }),
            CorpusIndexStartResult.AlreadyRunning => Conflict(new
            {
                code = "alreadyRunning",
                message = $"A pass over {corpusName} is already running."
            }),
            CorpusIndexStartResult.PathMissing => BadRequest(new { message = PathMissingMessage(getStatus()) }),
            _ => BadRequest(new { message = $"{getStatus().ConfigKey} is not configured." })
        };
    }

    /// <summary>Names the key, never its value; an unset key read through a built-in default says so.</summary>
    private static string PathMissingMessage(CorpusIndexStatusDto status) => status.Configured
        ? $"The {status.ConfigKey} folder does not exist."
        : $"{status.ConfigKey} is not set, and the built-in default folder does not exist.";

    private async Task<IActionResult> Probe(string toolName, string queryParameter, string? filterParameter,
        CorpusProbeRequest? request, CancellationToken cancellationToken)
    {
        string query = request?.Query?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return BadRequest(new { message = "Enter something to search for." });
        if (query.Length > MaxProbeQueryLength)
            return BadRequest(new { message = $"A probe query is at most {MaxProbeQueryLength} characters." });

        var handler = _toolHandlers.FirstOrDefault(h => string.Equals(h.ToolName, toolName, StringComparison.Ordinal));
        if (handler == null)
            return BadRequest(new { message = $"The {toolName} tool is not registered." });

        var arguments = new Dictionary<string, string> { [queryParameter] = query };
        string? filter = request?.Filter?.Trim();
        if (filterParameter != null && !string.IsNullOrEmpty(filter))
            arguments[filterParameter] = filter;
        using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));

        int maxResultLength = handler.MaxResultLengthOverride is int handlerMax
            ? Math.Max(DefaultProbeMaxResultLength, handlerMax)
            : DefaultProbeMaxResultLength;
        var context = new ToolExecutionContext
        {
            UserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            MaxResultLength = maxResultLength
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var stopwatch = Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = await handler.ExecuteAsync(argumentsDocument.RootElement, context, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            result = new ToolResult
            {
                Success = false,
                ErrorMessage = $"The tool did not answer within {ProbeTimeout.TotalSeconds:0} seconds."
            };
        }
        stopwatch.Stop();

        return Ok(new CorpusProbeResultDto
        {
            ToolName = toolName,
            Success = result.Success,
            Content = result.Content,
            Error = result.ErrorMessage,
            LengthChars = result.Content?.Length ?? 0,
            MaxResultLength = maxResultLength,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        });
    }
}

/// <summary>Both source services' status for the GnollHack Source dashboard.</summary>
public sealed class SourceDashboardDto
{
    public SourceIndexStatusDto GnollHack { get; init; } = null!;
    public SourceIndexStatusDto NetHack { get; init; } = null!;
    public DateTime ServerTimeUtc { get; init; }
}

/// <summary>A search probe: the query, and the filter the tool takes, if any.</summary>
public sealed class CorpusProbeRequest
{
    public string? Query { get; init; }
    public string? Filter { get; init; }
}

/// <summary>What a corpus tool returned to a search probe, uncapped, with the cap the chat applies.</summary>
public sealed class CorpusProbeResultDto
{
    public string ToolName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int LengthChars { get; init; }
    public int MaxResultLength { get; init; }
    public long ElapsedMs { get; init; }
}

/// <summary>A knowledge base article's raw text for the dashboard's preview.</summary>
public sealed class KnowledgeArticleSourceDto
{
    public string Topic { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Source { get; init; } = string.Empty;
}
