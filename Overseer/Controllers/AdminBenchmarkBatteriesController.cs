namespace Overseer.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>
/// Multi-suite benchmark batteries: battery definitions, battery runs, their analyses, the Markdown
/// report and the leaderboard of one definition.
///
/// <para>Execution belongs to <see cref="BenchmarkBatteryOrchestrator"/> and the arithmetic to
/// <see cref="BenchmarkBatteryAnalysisService"/>; this controller validates, persists definitions
/// and projects rows into DTOs.</para>
/// </summary>
[Route("api/admin/benchmark/batteries")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkBatteriesController : ControllerBase
{
    private const int NameMaxLength = 128;
    internal const int DefaultRunListSize = 50;
    internal const int MaxRunListSize = 1000;

    private static readonly BenchmarkBatteryWeightingScheme[] Schemes =
    {
        BenchmarkBatteryWeightingScheme.DifficultyMass,
        BenchmarkBatteryWeightingScheme.ItemCount,
        BenchmarkBatteryWeightingScheme.Equal,
        BenchmarkBatteryWeightingScheme.Custom
    };

    private static readonly BenchmarkRunSeriesStatus[] ActiveStatuses =
    {
        BenchmarkRunSeriesStatus.Pending,
        BenchmarkRunSeriesStatus.Running,
        BenchmarkRunSeriesStatus.WaitingForCap
    };

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkBatteryOrchestrator _orchestrator;
    private readonly BenchmarkBatteryAnalysisService _analysisService;
    private readonly BenchmarkBatteryLeaderboardService _leaderboard;
    private readonly BenchmarkRunManager _runManager;

    public AdminBenchmarkBatteriesController(
        ApplicationDbContext db,
        BenchmarkBatteryOrchestrator orchestrator,
        BenchmarkBatteryAnalysisService analysisService,
        BenchmarkBatteryLeaderboardService leaderboard,
        BenchmarkRunManager runManager)
    {
        _db = db;
        _orchestrator = orchestrator;
        _analysisService = analysisService;
        _leaderboard = leaderboard;
        _runManager = runManager;
    }

    private string? CurrentUserId()
    {
        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? null : userId;
    }

    // =======================================================================================
    // Battery definitions
    // =======================================================================================

    [HttpGet]
    public async Task<IActionResult> GetBatteries(CancellationToken ct)
    {
        var batteries = await _db.BenchmarkBatteries
            .AsNoTracking()
            .Include(b => b.Suites)
            .Include(b => b.CreatedByUser)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        return Ok(await BuildBatteryDtosAsync(batteries, ct));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetBattery(long id, CancellationToken ct)
    {
        var dto = await GetBatteryDtoAsync(id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBattery([FromBody] CreateBenchmarkBatteryRequest request, CancellationToken ct)
    {
        string? nameError = await NameErrorAsync(request.Name, excludeId: null, ct);
        if (nameError != null) return BadRequest(nameError);

        var (rows, rowsError) = await ResolveSuiteRowsAsync(request, ct);
        if (rowsError != null) return BadRequest(rowsError);

        DateTime now = DateTime.UtcNow;
        var battery = new BenchmarkBattery
        {
            Name = request.Name.Trim(),
            Description = NormalizeDescription(request.Description),
            WeightingScheme = request.WeightingScheme,
            Revision = 1,
            CreatedByUserId = CurrentUserId(),
            CreatedAtUtc = now,
            ModifiedAtUtc = now,
            Suites = rows!
        };

        var errors = BenchmarkBatteryDefinition.Validate(battery);
        if (errors.Count > 0) return BadRequest(string.Join(" ", errors));

        battery.DefinitionSha256 = BenchmarkBatteryDefinition.ComputeSha256(battery);

        _db.BenchmarkBatteries.Add(battery);
        await _db.SaveChangesAsync(ct);

        var dto = await GetBatteryDtoAsync(battery.Id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Replaces a battery's name, description, scheme, suites and weights. A change to the scheme,
    /// the suites, their order or the weights bumps <see cref="BenchmarkBattery.Revision"/> and
    /// recomputes the definition hash; a name or description change does neither. Battery runs keep
    /// their own definition snapshot.
    /// </summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateBattery(long id, [FromBody] UpdateBenchmarkBatteryRequest request, CancellationToken ct)
    {
        var battery = await _db.BenchmarkBatteries
            .Include(b => b.Suites)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (battery == null) return NotFound();

        string? nameError = await NameErrorAsync(request.Name, excludeId: id, ct);
        if (nameError != null) return BadRequest(nameError);

        var (desired, rowsError) = await ResolveSuiteRowsAsync(request, ct);
        if (rowsError != null) return BadRequest(rowsError);

        var errors = BenchmarkBatteryDefinition.Validate(
            request.WeightingScheme,
            desired!.Select(s => (s.BenchmarkSuiteId, s.SuiteName, s.CustomWeight)).ToList());
        if (errors.Count > 0) return BadRequest(string.Join(" ", errors));

        var current = battery.Suites.OrderBy(s => s.OrderIndex).ThenBy(s => s.Id).ToList();
        bool definitionChanged = battery.WeightingScheme != request.WeightingScheme
            || !current.Select(s => (s.BenchmarkSuiteId, s.CustomWeight))
                .SequenceEqual(desired!.Select(s => (s.BenchmarkSuiteId, s.CustomWeight)));

        if (definitionChanged)
        {
            ApplySuiteRows(battery, current, desired!);
            battery.WeightingScheme = request.WeightingScheme;
            battery.Revision++;
            battery.DefinitionSha256 = BenchmarkBatteryDefinition.ComputeSha256(
                request.WeightingScheme,
                desired!.Select(s => (s.BenchmarkSuiteId!.Value, s.CustomWeight)));
        }

        battery.Name = request.Name.Trim();
        battery.Description = NormalizeDescription(request.Description);
        battery.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        var dto = await GetBatteryDtoAsync(battery.Id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Deletes a battery. Refused while one of its battery runs is Pending, Running or WaitingForCap;
    /// otherwise its battery runs are kept with a null battery reference and their own snapshot.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteBattery(long id, CancellationToken ct)
    {
        var battery = await _db.BenchmarkBatteries
            .Include(b => b.Suites)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (battery == null) return NotFound();

        if (await _db.BenchmarkBatteryRuns.AnyAsync(r => r.BenchmarkBatteryId == id && ActiveStatuses.Contains(r.Status), ct))
        {
            return Conflict("Cannot delete this battery while one of its battery runs is in progress.");
        }

        // ClientSetNull is NO ACTION in the database: the reference is cleared on loaded rows.
        var batteryRuns = await _db.BenchmarkBatteryRuns.Where(r => r.BenchmarkBatteryId == id).ToListAsync(ct);
        foreach (var batteryRun in batteryRuns) batteryRun.BenchmarkBatteryId = null;

        _db.BenchmarkBatterySuites.RemoveRange(battery.Suites);
        _db.BenchmarkBatteries.Remove(battery);
        await _db.SaveChangesAsync(ct);

        return Ok();
    }

    /// <summary>Hides a battery from the launcher, or shows it again (<c>{ "archived": false }</c>).</summary>
    [HttpPost("{id:long}/archive")]
    public async Task<IActionResult> ArchiveBattery(long id, [FromBody] ArchiveBenchmarkBatteryRequest? request, CancellationToken ct)
    {
        var battery = await _db.BenchmarkBatteries.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (battery == null) return NotFound();

        battery.IsArchived = request?.Archived ?? true;
        battery.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var dto = await GetBatteryDtoAsync(id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private async Task<string?> NameErrorAsync(string? name, long? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Battery name is required.";

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength) return $"Battery name must be at most {NameMaxLength} characters.";

        bool taken = await _db.BenchmarkBatteries.AnyAsync(
            b => b.Name == trimmed && (!excludeId.HasValue || b.Id != excludeId.Value), ct);
        return taken ? "A battery with this name already exists." : null;
    }

    /// <summary>
    /// The suite rows a request asks for, in run order and not yet attached to a battery. Custom
    /// weights are kept only under the Custom scheme, which needs one per suite.
    /// </summary>
    private async Task<(List<BenchmarkBatterySuite>? Rows, string? Error)> ResolveSuiteRowsAsync(
        CreateBenchmarkBatteryRequest request,
        CancellationToken ct)
    {
        var suiteIds = request.SuiteIds ?? new List<long>();
        bool custom = request.WeightingScheme == BenchmarkBatteryWeightingScheme.Custom;

        if (custom && (request.CustomWeights == null || request.CustomWeights.Count != suiteIds.Count))
        {
            return (null, $"Custom weighting needs one weight per suite: {suiteIds.Count} suites, "
                + $"{request.CustomWeights?.Count.ToString() ?? "no"} weights.");
        }

        var distinctIds = suiteIds.Distinct().ToList();
        var names = await _db.BenchmarkSuites
            .AsNoTracking()
            .Where(s => distinctIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var missing = distinctIds.Where(id => !names.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return (null, $"Suite {string.Join(", ", missing.Select(id => "#" + id))} not found.");
        }

        var rows = suiteIds
            .Select((suiteId, index) => new BenchmarkBatterySuite
            {
                BenchmarkSuiteId = suiteId,
                SuiteName = names[suiteId],
                OrderIndex = index,
                CustomWeight = custom ? request.CustomWeights![index] : null
            })
            .ToList();

        return (rows, null);
    }

    /// <summary>
    /// Makes the battery's suite rows match <paramref name="desired"/>: a suite already present keeps
    /// its row with the new position and weight, a new one is added, and every other row (a deleted
    /// suite's included) is removed. Reusing rows keeps the unique (battery, suite) index satisfied.
    /// </summary>
    private void ApplySuiteRows(
        BenchmarkBattery battery,
        IReadOnlyList<BenchmarkBatterySuite> current,
        IReadOnlyList<BenchmarkBatterySuite> desired)
    {
        var existingBySuite = current
            .Where(s => s.BenchmarkSuiteId.HasValue)
            .GroupBy(s => s.BenchmarkSuiteId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var kept = new HashSet<BenchmarkBatterySuite>();
        foreach (var row in desired)
        {
            if (existingBySuite.TryGetValue(row.BenchmarkSuiteId!.Value, out var existing))
            {
                existing.OrderIndex = row.OrderIndex;
                existing.CustomWeight = row.CustomWeight;
                existing.SuiteName = row.SuiteName;
                kept.Add(existing);
            }
            else
            {
                row.BenchmarkBatteryId = battery.Id;
                battery.Suites.Add(row);
                kept.Add(row);
            }
        }

        foreach (var row in current.Where(r => !kept.Contains(r)))
        {
            battery.Suites.Remove(row);
            _db.BenchmarkBatterySuites.Remove(row);
        }
    }

    private async Task<BenchmarkBatteryDto?> GetBatteryDtoAsync(long id, CancellationToken ct)
    {
        var battery = await _db.BenchmarkBatteries
            .AsNoTracking()
            .Include(b => b.Suites)
            .Include(b => b.CreatedByUser)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (battery == null) return null;

        return (await BuildBatteryDtosAsync(new List<BenchmarkBattery> { battery }, ct)).Single();
    }

    private async Task<List<BenchmarkBatteryDto>> BuildBatteryDtosAsync(IReadOnlyList<BenchmarkBattery> batteries, CancellationToken ct)
    {
        var suiteIds = batteries
            .SelectMany(b => b.Suites)
            .Where(s => s.BenchmarkSuiteId.HasValue)
            .Select(s => s.BenchmarkSuiteId!.Value)
            .Distinct()
            .ToList();

        var suiteNames = await _db.BenchmarkSuites
            .AsNoTracking()
            .Where(s => suiteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var difficulties = (await _db.BenchmarkQuestions
                .AsNoTracking()
                .Where(q => suiteIds.Contains(q.BenchmarkSuiteId))
                .Select(q => new { q.BenchmarkSuiteId, q.AssessedDifficulty })
                .ToListAsync(ct))
            .GroupBy(q => q.BenchmarkSuiteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int?>)g.Select(q => q.AssessedDifficulty).ToList());

        var batteryIds = batteries.Select(b => b.Id).ToList();
        var runStatuses = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(r => r.BenchmarkBatteryId.HasValue && batteryIds.Contains(r.BenchmarkBatteryId.Value))
            .Select(r => new { BatteryId = r.BenchmarkBatteryId!.Value, r.Status })
            .ToListAsync(ct);
        var runsByBattery = runStatuses
            .GroupBy(r => r.BatteryId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Status).ToList());

        var summaries = await _leaderboard.LoadSummariesAsync(batteries.Select(b => b.DefinitionSha256), ct);

        return batteries
            .Select(b => ToBatteryDto(
                b,
                suiteNames,
                difficulties,
                runsByBattery.TryGetValue(b.Id, out var statuses) ? statuses : new List<BenchmarkRunSeriesStatus>(),
                summaries.TryGetValue((b.DefinitionSha256 ?? string.Empty).Trim().ToLowerInvariant(), out var summary) ? summary : null))
            .ToList();
    }

    private static BenchmarkBatteryDto ToBatteryDto(
        BenchmarkBattery battery,
        IReadOnlyDictionary<long, string> suiteNames,
        IReadOnlyDictionary<long, IReadOnlyList<int?>> difficultiesBySuite,
        IReadOnlyList<BenchmarkRunSeriesStatus> runStatuses,
        BenchmarkBatteryLeaderboardSummary? leaderboard)
    {
        var ordered = battery.Suites.OrderBy(s => s.OrderIndex).ThenBy(s => s.Id).ToList();

        IReadOnlyList<int?> DifficultiesOf(BenchmarkBatterySuite row)
            => row.BenchmarkSuiteId is long suiteId && difficultiesBySuite.TryGetValue(suiteId, out var list)
                ? list
                : Array.Empty<int?>();

        var suites = ordered
            .Select((row, index) =>
            {
                var questions = DifficultiesOf(row);
                return new BenchmarkBatterySuiteDto
                {
                    Index = index,
                    SuiteId = row.BenchmarkSuiteId,
                    SuiteName = row.BenchmarkSuiteId is long suiteId && suiteNames.TryGetValue(suiteId, out var name)
                        ? name
                        : row.SuiteName,
                    Deleted = !row.BenchmarkSuiteId.HasValue,
                    CustomWeight = row.CustomWeight,
                    QuestionCount = questions.Count,
                    AssessedQuestionCount = questions.Count(d => d.HasValue),
                    DifficultyFullyAssessed = questions.Count > 0 && questions.All(d => d.HasValue),
                    DifficultyMass = questions.Sum(d => BenchmarkBatteryDefinition.QuestionWeight(d))
                };
            })
            .ToList();

        var currentDifficulties = ordered.Select(DifficultiesOf).ToList();
        var customWeights = ordered.Select(s => s.CustomWeight).ToList();

        return new BenchmarkBatteryDto
        {
            Id = battery.Id,
            Name = battery.Name,
            Description = battery.Description,
            WeightingScheme = battery.WeightingScheme,
            WeightingSchemeLabel = BenchmarkBatteryReportBuilder.SchemeLabel(battery.WeightingScheme),
            Revision = battery.Revision,
            DefinitionSha256 = battery.DefinitionSha256,
            IsArchived = battery.IsArchived,
            BrokenSuiteNames = ordered.Where(s => !s.BenchmarkSuiteId.HasValue).Select(s => s.SuiteName).ToList(),
            ValidationErrors = BenchmarkBatteryDefinition.Validate(battery).ToList(),
            CreatedByUserName = battery.CreatedByUser?.UserName,
            CreatedAtUtc = battery.CreatedAtUtc,
            ModifiedAtUtc = battery.ModifiedAtUtc,
            BatteryRunCount = runStatuses.Count,
            HasActiveBatteryRun = runStatuses.Any(s => ActiveStatuses.Contains(s)),
            RankedResultCount = leaderboard?.RankedResultCount ?? 0,
            LatestAnalysisAtUtc = leaderboard?.LatestAnalysisAtUtc,
            Suites = suites,
            WeightPreviews = Schemes
                .Select(scheme => new BenchmarkBatteryWeightPreviewDto
                {
                    Scheme = scheme,
                    SchemeLabel = BenchmarkBatteryReportBuilder.SchemeLabel(scheme),
                    Declared = scheme == battery.WeightingScheme,
                    Weights = BenchmarkBatteryDefinition.PreviewWeights(scheme, currentDifficulties, customWeights).ToList()
                })
                .ToList()
        };
    }

    // =======================================================================================
    // Battery runs
    // =======================================================================================

    [HttpGet("runs")]
    public async Task<IActionResult> GetBatteryRuns([FromQuery] long? batteryId, [FromQuery] int? take, CancellationToken ct)
    {
        var query = _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Include(r => r.StartedByUser)
            .AsQueryable();

        if (batteryId.HasValue)
        {
            query = query.Where(r => r.BenchmarkBatteryId == batteryId.Value);
        }

        int limit = Math.Clamp(take ?? DefaultRunListSize, 1, MaxRunListSize);

        var batteryRuns = await query
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(await BuildRunDtosAsync(batteryRuns, ct));
    }

    /// <summary>Starts a battery run. 202 with <c>{ batteryRunId }</c>; refusals map as a series start's do.</summary>
    [HttpPost("runs")]
    public async Task<IActionResult> StartBatteryRun([FromBody] StartBenchmarkBatteryRunRequest request, CancellationToken ct)
    {
        var result = await _orchestrator.StartAsync(request, CurrentUserId(), ct);
        return StartResultToActionResult(result);
    }

    /// <summary>
    /// The battery run this process is driving, else the newest one that is live or stopped. Returns
    /// <c>204 No Content</c> when there is nothing to show, so the client can poll it cheaply.
    /// </summary>
    [HttpGet("runs/active")]
    public async Task<IActionResult> GetActiveBatteryRun(CancellationToken ct)
    {
        long? id = _orchestrator.ActiveBatteryRunId;

        if (id == null)
        {
            id = await _db.BenchmarkBatteryRuns
                .Where(r => r.Status == BenchmarkRunSeriesStatus.Running
                            || r.Status == BenchmarkRunSeriesStatus.WaitingForCap
                            || r.Status == BenchmarkRunSeriesStatus.Pending
                            || r.Status == BenchmarkRunSeriesStatus.Stopped)
                .OrderByDescending(r => r.StartedAtUtc)
                .ThenByDescending(r => r.Id)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (id == null) return NoContent();

        var dto = await GetRunDtoAsync(id.Value, ct);
        return dto == null ? NoContent() : Ok(dto);
    }

    [HttpGet("runs/{id:long}")]
    public async Task<IActionResult> GetBatteryRun(long id, CancellationToken ct)
    {
        var dto = await GetRunDtoAsync(id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Cancels a battery run and its in-flight member. A finished one cannot be canceled.</summary>
    [HttpPost("runs/{id:long}/cancel")]
    public async Task<IActionResult> CancelBatteryRun(long id, CancellationToken ct)
    {
        var status = await _db.BenchmarkBatteryRuns
            .Where(r => r.Id == id)
            .Select(r => (BenchmarkRunSeriesStatus?)r.Status)
            .FirstOrDefaultAsync(ct);

        if (status == null) return NotFound();

        if (status is BenchmarkRunSeriesStatus.Completed or BenchmarkRunSeriesStatus.Cancelled or BenchmarkRunSeriesStatus.Failed)
        {
            return BadRequest($"A {status} battery run cannot be canceled.");
        }

        bool canceled = await _orchestrator.CancelAsync(id, ct);
        return canceled ? Ok() : NotFound();
    }

    [HttpPost("runs/{id:long}/resume")]
    public async Task<IActionResult> ResumeBatteryRun(long id, [FromBody] ResumeBenchmarkBatteryRunRequest? request, CancellationToken ct)
    {
        var result = await _orchestrator.ResumeAsync(id, request?.Mode ?? BenchmarkBatteryResumeMode.Continue, ct);
        return StartResultToActionResult(result);
    }

    /// <summary>
    /// Deletes a battery run; its analyses, member rows and battery-completion documents go with it, and
    /// any report job of it is canceled. With <c>deleteMembers=true</c>
    /// each member run, superseded ones included, is deleted through the single-run delete, except a
    /// run that also serves another battery run, which is kept. 204 on success; 404 for an unknown
    /// battery run; 409 while it is driven or live, or while a member run to delete is in flight.
    /// </summary>
    [HttpDelete("runs/{id:long}")]
    public async Task<IActionResult> DeleteBatteryRun(
        long id,
        [FromQuery] bool deleteMembers = false,
        CancellationToken ct = default,
        [FromServices] BenchmarkBatteryReportDocumentService? documents = null)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (batteryRun == null) return NotFound();

        if (_orchestrator.IsDriving(id) || ActiveStatuses.Contains(batteryRun.Status))
        {
            return Conflict("Cannot delete a battery run while it is in progress.");
        }

        var memberRunIds = new List<long>();
        if (deleteMembers)
        {
            var ownRunIds = batteryRun.Members.Select(m => m.BenchmarkRunId).Distinct().ToList();
            var sharedRunIds = await _db.BenchmarkBatteryRunMembers
                .Where(m => ownRunIds.Contains(m.BenchmarkRunId) && m.BenchmarkBatteryRunId != id)
                .Select(m => m.BenchmarkRunId)
                .Distinct()
                .ToListAsync(ct);
            memberRunIds = ownRunIds.Except(sharedRunIds).OrderBy(runId => runId).ToList();

            if (_runManager.CurrentRunId is long current && memberRunIds.Contains(current))
            {
                return Conflict("Cannot delete a member run while it is running.");
            }
        }

        // Tracked, so the cascade reaches the analyses on providers that apply it to loaded rows only.
        await _db.BenchmarkBatteryAnalyses.Where(a => a.BenchmarkBatteryRunId == id).LoadAsync(ct);

        _db.BenchmarkBatteryRuns.Remove(batteryRun);
        await _db.SaveChangesAsync(ct);

        // The battery-completion documents and their charts, and any report job of this battery run.
        if (documents != null)
        {
            await documents.SettleAfterDeleteAsync(id, ct);
        }

        foreach (long runId in memberRunIds)
        {
            await AdminBenchmarkController.TryDeleteRunAsync(_db, _runManager, runId, ct);
        }

        return NoContent();
    }

    /// <summary>
    /// Which slots earlier runs would fill for a start request not yet sent: the start's own body,
    /// judged against the fingerprints a start would record now. Creates and spends nothing. 200 with
    /// the preview; 404 for an unknown battery; 400 for a request that cannot be judged.
    /// </summary>
    [HttpPost("runs/reuse-preview")]
    public async Task<IActionResult> PreviewBatteryReuse([FromBody] StartBenchmarkBatteryRunRequest request, CancellationToken ct)
    {
        var result = await _orchestrator.PreviewReuseAsync(request, ct);
        return result.Succeeded ? Ok(result.Preview) : AttachResultToActionResult(result);
    }

    /// <summary>
    /// Attaches an existing run to one slot of a battery run. 200 with the updated battery run; 404
    /// for an unknown battery run or run; 409 while it is running or in a state that takes no run;
    /// 400 with the reason when the run or the slot does not qualify.
    /// </summary>
    [HttpPost("runs/{id:long}/members")]
    public async Task<IActionResult> AttachBatteryMember(long id, [FromBody] BenchmarkBatteryAttachDto request, CancellationToken ct)
    {
        if (request == null) return BadRequest("The slot and the run are missing from the request.");

        var result = await _orchestrator.AttachAsync(id, request.SuiteIndex, request.Round, request.RunId, ct);
        if (!result.Succeeded) return AttachResultToActionResult(result);

        var dto = await GetRunDtoAsync(id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// The runs that may be attached to one slot, newest first, each with whether it qualifies and,
    /// when not, why. 404 for an unknown battery run; 400 for a slot outside the grid.
    /// </summary>
    [HttpGet("runs/{id:long}/members/candidates")]
    public async Task<IActionResult> GetBatteryAttachCandidates(
        long id,
        [FromQuery] int suiteIndex,
        [FromQuery] int round,
        CancellationToken ct)
    {
        var result = await _orchestrator.GetAttachCandidatesAsync(id, suiteIndex, round, ct);
        return result.Succeeded ? Ok(result.Candidates) : AttachResultToActionResult(result);
    }

    /// <summary>The refusals of attach, candidates and preview: 404 not found, 409 wrong state, 400 not eligible.</summary>
    internal static IActionResult AttachResultToActionResult(BenchmarkBatteryAttachResult result)
    {
        return result.Outcome switch
        {
            BenchmarkBatteryAttachOutcome.Ok => new OkResult(),
            BenchmarkBatteryAttachOutcome.NotFound => new NotFoundObjectResult(result.Error),
            BenchmarkBatteryAttachOutcome.Conflict => new ConflictObjectResult(result.Error),
            _ => new BadRequestObjectResult(result.Error)
        };
    }

    /// <summary>
    /// One mapping from the orchestrator's outcome vocabulary to HTTP, shared by start and resume and
    /// matching the series mapping: 202 started, 409 conflict, 404 not found, 429 spend denied, 409
    /// with the warning for an unacknowledged same-provider pair, 409 with the moved hashes for an
    /// instrument change, and 400 for an invalid request or one planning too many launches.
    /// </summary>
    internal static IActionResult StartResultToActionResult(BenchmarkBatteryStartResult result)
    {
        switch (result.Outcome)
        {
            case BenchmarkBatteryStartOutcome.Started:
                return new AcceptedResult((string?)null, new { batteryRunId = result.BatteryRunId!.Value });

            case BenchmarkBatteryStartOutcome.Conflict:
                return new ConflictObjectResult(result.Error);

            case BenchmarkBatteryStartOutcome.NotFound:
                return new NotFoundObjectResult(result.Error);

            case BenchmarkBatteryStartOutcome.SpendDenied:
                return new ObjectResult(result.Error) { StatusCode = StatusCodes.Status429TooManyRequests };

            case BenchmarkBatteryStartOutcome.SameProviderNotAcknowledged:
                return new ObjectResult(result.SameProviderWarning) { StatusCode = StatusCodes.Status409Conflict };

            case BenchmarkBatteryStartOutcome.InstrumentChanged:
                return new ObjectResult(new
                {
                    instrumentChanged = true,
                    batteryRunId = result.BatteryRunId,
                    changedHashes = result.ChangedInstrumentHashes,
                    message = result.Error
                })
                { StatusCode = StatusCodes.Status409Conflict };

            default:
                return new BadRequestObjectResult(result.Error);
        }
    }

    // --- Battery run projection ---------------------------------------------------------------

    /// <summary>The columns of a member run the grid needs, without its answers or snapshots.</summary>
    private sealed class MemberRunInfo
    {
        public long Id { get; set; }
        public BenchmarkRunStatus Status { get; set; }
        public int? QualityIndex { get; set; }
        public int? SpeedIndex { get; set; }
        public int? TerminalFailureAnswerCount { get; set; }
        public int TotalQuestionCount { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long TestedModelSnapshotId { get; set; }

        /// <summary>A stand-in carrying exactly what <see cref="BenchmarkBatteryPlanner.UnusableReason"/> reads.</summary>
        public BenchmarkRun ToUsabilityRun() => new()
        {
            Id = Id,
            Status = Status,
            QualityIndex = QualityIndex,
            TerminalFailureAnswerCount = TerminalFailureAnswerCount
        };
    }

    /// <summary>The member rows of some battery runs, with what the grid shows about each member run.</summary>
    private sealed class MemberState
    {
        public List<BenchmarkBatteryRunMember> Members { get; init; } = new();
        public Dictionary<long, MemberRunInfo> Runs { get; init; } = new();
        public Dictionary<long, int> AnsweredCounts { get; init; } = new();
        public Dictionary<long, string?> ModelLabels { get; init; } = new();

        public IEnumerable<BenchmarkBatteryRunMember> Of(long batteryRunId)
            => Members.Where(m => m.BenchmarkBatteryRunId == batteryRunId);

        public string? UnusableReason(BenchmarkBatteryRunMember member)
            => Runs.TryGetValue(member.BenchmarkRunId, out var run)
                ? BenchmarkBatteryPlanner.UnusableReason(member, run.ToUsabilityRun())
                : "run deleted";
    }

    private async Task<MemberState> LoadMemberStateAsync(IReadOnlyCollection<long> batteryRunIds, CancellationToken ct)
    {
        var ids = batteryRunIds.Distinct().ToList();

        var members = await _db.BenchmarkBatteryRunMembers
            .AsNoTracking()
            .Where(m => ids.Contains(m.BenchmarkBatteryRunId))
            .ToListAsync(ct);

        var runIds = members.Select(m => m.BenchmarkRunId).Distinct().ToList();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => runIds.Contains(r.Id))
            .Select(r => new MemberRunInfo
            {
                Id = r.Id,
                Status = r.Status,
                QualityIndex = r.QualityIndex,
                SpeedIndex = r.SpeedIndex,
                TerminalFailureAnswerCount = r.TerminalFailureAnswerCount,
                TotalQuestionCount = r.TotalQuestionCount,
                StartedAtUtc = r.StartedAtUtc,
                CompletedAtUtc = r.CompletedAtUtc,
                TestedModelSnapshotId = r.TestedModelSnapshotId
            })
            .ToDictionaryAsync(r => r.Id, ct);

        // Answer rows only for the runs in flight: the progress line of the running member.
        var runningIds = runs.Values.Where(r => r.Status == BenchmarkRunStatus.Running).Select(r => r.Id).ToList();
        var answered = runningIds.Count == 0
            ? new Dictionary<long, int>()
            : await _db.BenchmarkRunAnswers
                .Where(a => runningIds.Contains(a.BenchmarkRunId))
                .GroupBy(a => a.BenchmarkRunId)
                .Select(g => new { RunId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, ct);

        var snapshotIds = runs.Values.Select(r => r.TestedModelSnapshotId).Distinct().ToList();
        var snapshotLabels = await _db.SystemAiConfigurationSnapshots
            .AsNoTracking()
            .Where(s => snapshotIds.Contains(s.Id))
            .Select(s => new { s.Id, Label = s.DisplayName ?? s.ModelId })
            .ToDictionaryAsync(s => s.Id, s => (string?)s.Label, ct);

        return new MemberState
        {
            Members = members,
            Runs = runs,
            AnsweredCounts = answered,
            ModelLabels = runs.Values.ToDictionary(
                r => r.Id,
                r => snapshotLabels.TryGetValue(r.TestedModelSnapshotId, out var label) ? label : null)
        };
    }

    private async Task<BenchmarkBatteryRunDto?> GetRunDtoAsync(long id, CancellationToken ct)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Include(r => r.StartedByUser)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (batteryRun == null) return null;

        return (await BuildRunDtosAsync(new List<BenchmarkBatteryRun> { batteryRun }, ct)).Single();
    }

    private async Task<List<BenchmarkBatteryRunDto>> BuildRunDtosAsync(IReadOnlyList<BenchmarkBatteryRun> batteryRuns, CancellationToken ct)
    {
        if (batteryRuns.Count == 0) return new List<BenchmarkBatteryRunDto>();

        var ids = batteryRuns.Select(b => b.Id).ToList();
        var state = await LoadMemberStateAsync(ids, ct);
        var latest = await LoadLatestAnalysesAsync(ids, ct);
        var configLabels = await LoadConfigurationLabelsAsync(batteryRuns, ct);
        var identities = await _leaderboard.LoadIdentitiesAsync(batteryRuns, IdentityRunIds(batteryRuns, state), ct);

        var dtos = batteryRuns
            .Select(b => ToRunDto(
                b,
                state,
                latest.TryGetValue(b.Id, out var analysis) ? analysis : null,
                configLabels,
                identities.TryGetValue(b.Id, out var identity) ? identity : null))
            .ToList();

        foreach (var dto in dtos)
        {
            dto.IsDriving = _orchestrator.IsDriving(dto.Id);
        }

        return dtos;
    }

    /// <summary>The latest analysis of each battery run, by battery run id.</summary>
    private async Task<Dictionary<long, BenchmarkBatteryAnalysis>> LoadLatestAnalysesAsync(IReadOnlyCollection<long> batteryRunIds, CancellationToken ct)
    {
        var ids = batteryRunIds.Distinct().ToList();

        var heads = await _db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => ids.Contains(a.BenchmarkBatteryRunId))
            .Select(a => new { a.Id, a.BenchmarkBatteryRunId, a.ComputedAtUtc })
            .ToListAsync(ct);

        var latestIds = heads
            .GroupBy(a => a.BenchmarkBatteryRunId)
            .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First().Id)
            .ToList();

        if (latestIds.Count == 0) return new Dictionary<long, BenchmarkBatteryAnalysis>();

        return await _db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => latestIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.BenchmarkBatteryRunId, ct);
    }

    /// <summary>The tested configuration's name per configuration id, for battery runs without a member run.</summary>
    private async Task<Dictionary<long, string>> LoadConfigurationLabelsAsync(IReadOnlyList<BenchmarkBatteryRun> batteryRuns, CancellationToken ct)
    {
        var configIds = batteryRuns
            .Select(b => BenchmarkBatteryOrchestrator.DeserializeRequest(b)?.TestedModelConfigurationId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (configIds.Count == 0) return new Dictionary<long, string>();

        return await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .Where(c => configIds.Contains(c.Id))
            .Select(c => new { c.Id, Label = c.DisplayName ?? c.ModelId })
            .ToDictionaryAsync(c => c.Id, c => c.Label, ct);
    }

    private static BenchmarkBatteryDefinition? TryReadDefinition(string? json)
    {
        try
        {
            return BenchmarkBatteryDefinition.FromJson(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int SuiteCountOf(BenchmarkBatteryRun batteryRun, BenchmarkBatteryDefinition? definition)
        => definition?.Suites.Count
           ?? (batteryRun.RunsPerSuite > 0 ? batteryRun.RequestedMemberCount / batteryRun.RunsPerSuite : 0);

    /// <summary>The model under test: a member run's snapshot label, else the stored request's configuration name.</summary>
    private static string? TestedModelLabelOf(
        BenchmarkBatteryRun batteryRun,
        MemberState state,
        IReadOnlyDictionary<long, string> configLabels)
    {
        string? fromRun = state.Of(batteryRun.Id)
            .OrderBy(m => m.AddedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => state.ModelLabels.TryGetValue(m.BenchmarkRunId, out var label) ? label : null)
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));
        if (fromRun != null) return fromRun;

        long? configId = BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun)?.TestedModelConfigurationId;
        return configId.HasValue && configLabels.TryGetValue(configId.Value, out var configLabel) ? configLabel : null;
    }

    /// <summary>
    /// The member run a battery run's identity is read from: the newest non-superseded usable member
    /// in the grid, else the newest member whose run still exists; null when there is none.
    /// </summary>
    private static long? IdentityRunIdOf(BenchmarkBatteryRun batteryRun, MemberState state)
    {
        int suiteCount = SuiteCountOf(batteryRun, TryReadDefinition(batteryRun.DefinitionJson));

        var members = state.Of(batteryRun.Id)
            .Where(m => state.Runs.ContainsKey(m.BenchmarkRunId))
            .OrderByDescending(m => m.AddedAtUtc)
            .ThenByDescending(m => m.Id)
            .ToList();

        var usable = members.FirstOrDefault(m => !m.Superseded
                                                 && m.SuiteIndex >= 0 && m.SuiteIndex < suiteCount
                                                 && state.UnusableReason(m) == null);

        return (usable ?? members.FirstOrDefault())?.BenchmarkRunId;
    }

    /// <summary>The identity member run per battery run, for those that have one.</summary>
    private static Dictionary<long, long> IdentityRunIds(IEnumerable<BenchmarkBatteryRun> batteryRuns, MemberState state)
    {
        var ids = new Dictionary<long, long>();
        foreach (var batteryRun in batteryRuns)
        {
            if (IdentityRunIdOf(batteryRun, state) is long runId) ids[batteryRun.Id] = runId;
        }
        return ids;
    }

    /// <summary>
    /// The run ids of the non-superseded usable members whose suite index lies in the definition —
    /// the set <see cref="BenchmarkBatteryAnalysisService.LoadAsync"/> analyses.
    /// </summary>
    private static List<long> UsableRunIds(BenchmarkBatteryRun batteryRun, MemberState state, int suiteCount)
        => state.Of(batteryRun.Id)
            .Where(m => !m.Superseded
                        && m.SuiteIndex >= 0 && m.SuiteIndex < suiteCount
                        && state.UnusableReason(m) == null)
            .Select(m => m.BenchmarkRunId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

    private static BenchmarkBatteryRunDto ToRunDto(
        BenchmarkBatteryRun batteryRun,
        MemberState state,
        BenchmarkBatteryAnalysis? latestAnalysis,
        IReadOnlyDictionary<long, string> configLabels,
        BenchmarkBatteryRunIdentity? identity)
    {
        var definition = TryReadDefinition(batteryRun.DefinitionJson);
        int suiteCount = SuiteCountOf(batteryRun, definition);
        int runsPerSuite = batteryRun.RunsPerSuite;

        var members = state.Of(batteryRun.Id)
            .OrderBy(m => m.SuiteIndex)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.AddedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => ToMemberDto(m, state))
            .ToList();

        var live = members.Where(m => !m.Superseded).ToList();

        bool InGrid(BenchmarkBatteryMemberDto m)
            => m.SuiteIndex >= 0 && m.SuiteIndex < suiteCount && m.Round >= 1 && m.Round <= runsPerSuite;

        int usableSlots = live.Where(m => m.Usable && InGrid(m)).Select(m => (m.SuiteIndex, m.Round)).Distinct().Count();
        int completedSuites = live.Where(m => m.Usable && InGrid(m)).Select(m => m.SuiteIndex).Distinct().Count();

        var slots = new List<BenchmarkBatterySlotDto>();
        for (int round = 1; round <= runsPerSuite; round++)
        {
            for (int suiteIndex = 0; suiteIndex < suiteCount; suiteIndex++)
            {
                slots.Add(new BenchmarkBatterySlotDto
                {
                    SuiteIndex = suiteIndex,
                    Round = round,
                    Member = live
                        .Where(m => m.SuiteIndex == suiteIndex && m.Round == round)
                        .OrderByDescending(m => m.AddedAtUtc)
                        .ThenByDescending(m => m.MemberId)
                        .FirstOrDefault()
                });
            }
        }

        var fingerprints = BenchmarkBatteryOrchestrator.ReadFingerprints(batteryRun.SuiteFingerprintsJson);

        var dto = new BenchmarkBatteryRunDto
        {
            Id = batteryRun.Id,
            BatteryId = batteryRun.BenchmarkBatteryId,
            BatteryName = batteryRun.BatteryName,
            DefinitionRevision = definition?.Revision ?? 0,
            DefinitionSha256 = batteryRun.DefinitionSha256,
            WeightingScheme = definition?.Scheme ?? BenchmarkBatteryWeightingScheme.DifficultyMass,
            Suites = (definition?.Suites ?? Array.Empty<BenchmarkBatteryDefinitionSuite>())
                .OrderBy(s => s.Index)
                .Select(s => new BenchmarkBatteryRunSuiteDto
                {
                    Index = s.Index,
                    SuiteId = s.SuiteId,
                    SuiteName = s.SuiteName,
                    CustomWeight = s.CustomWeight,
                    CandidateSystemPromptSha256 = fingerprints.GetValueOrDefault(s.Index)?.CandidateSystemPromptSha256,
                    ToolGuidesSha256 = fingerprints.GetValueOrDefault(s.Index)?.ToolGuidesSha256,
                    KnowledgeBaseHeadSha = fingerprints.GetValueOrDefault(s.Index)?.KnowledgeBaseHeadSha,
                    WikiHeadSha = fingerprints.GetValueOrDefault(s.Index)?.WikiHeadSha,
                    SourceCodeHeadSha = fingerprints.GetValueOrDefault(s.Index)?.SourceCodeHeadSha
                })
                .ToList(),
            SuiteCount = suiteCount,
            RunsPerSuite = runsPerSuite,
            RequestedMemberCount = batteryRun.RequestedMemberCount,
            CompletedMemberCount = batteryRun.CompletedMemberCount,
            FailedMemberCount = batteryRun.FailedMemberCount,
            CompletedSuiteCount = completedSuites,
            Status = batteryRun.Status.ToString(),
            StopReason = batteryRun.StopReason?.ToString(),
            StopReasonText = AdminBenchmarkController.DescribeStopReason(batteryRun.StopReason),
            AllowCapWait = batteryRun.AllowCapWait,
            Resumable = BenchmarkBatteryOrchestrator.ResumeStatusRefusal(
                batteryRun.Status, batteryRun.RequestedMemberCount, usableSlots) == null,
            StartedAtUtc = batteryRun.StartedAtUtc,
            CompletedAtUtc = batteryRun.CompletedAtUtc,
            LastProgressAtUtc = batteryRun.LastProgressAtUtc,
            ErrorMessage = batteryRun.ErrorMessage,
            StartedByUserName = batteryRun.StartedByUser?.UserName,
            TestedModelConfigurationId = BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun)?.TestedModelConfigurationId,
            TestedModelLabel = TestedModelLabelOf(batteryRun, state, configLabels),
            TestedProvider = identity?.TestedProvider,
            TestedModelId = identity?.TestedModelId,
            TestedThinkingLevel = identity?.TestedThinkingLevel,
            TestedReasoningMode = identity?.TestedReasoningMode,
            TestedServiceTier = identity?.TestedServiceTier,
            AssessorLabel = identity?.AssessorLabel,
            CoAssessorLabel = identity?.CoAssessorLabel,
            ScoringProfileName = identity?.ScoringProfileName,
            VerboseMode = identity?.VerboseMode ?? false,
            ReportWriterModelConfigurationId = batteryRun.ReportWriterModelConfigurationId,
            ReportDocumentsStatus = batteryRun.ReportDocumentsStatus,
            ReportDocumentsMessage = batteryRun.ReportDocumentsMessage,
            Slots = slots,
            Members = members
        };

        SetCurrentPosition(dto, batteryRun, live, definition);
        SetLatestAnalysis(dto, latestAnalysis, UsableRunIds(batteryRun, state, suiteCount));

        return dto;
    }

    private static BenchmarkBatteryMemberDto ToMemberDto(BenchmarkBatteryRunMember member, MemberState state)
    {
        state.Runs.TryGetValue(member.BenchmarkRunId, out var run);
        string? reason = state.UnusableReason(member);

        return new BenchmarkBatteryMemberDto
        {
            MemberId = member.Id,
            SuiteIndex = member.SuiteIndex,
            Round = member.Round,
            RunId = member.BenchmarkRunId,
            RunStatus = run?.Status.ToString() ?? "Deleted",
            QualityIndex = run?.QualityIndex,
            SpeedIndex = run?.SpeedIndex,
            Origin = member.Origin.ToString(),
            Superseded = member.Superseded,
            Usable = reason == null,
            UnusableReason = reason,
            GuardFailure = member.GuardFailure,
            AddedAtUtc = member.AddedAtUtc,
            RunStartedAtUtc = run?.StartedAtUtc,
            RunCompletedAtUtc = run?.CompletedAtUtc,
            AnsweredQuestionCount = state.AnsweredCounts.TryGetValue(member.BenchmarkRunId, out int answered) ? answered : 0,
            TotalQuestionCount = run?.TotalQuestionCount ?? 0
        };
    }

    /// <summary>
    /// The banner position: the member in flight, else — while the battery run is live — the next slot
    /// the planner would launch.
    /// </summary>
    private static void SetCurrentPosition(
        BenchmarkBatteryRunDto dto,
        BenchmarkBatteryRun batteryRun,
        IReadOnlyList<BenchmarkBatteryMemberDto> live,
        BenchmarkBatteryDefinition? definition)
    {
        int? suiteIndex = null;
        int? round = null;

        var running = live
            .Where(m => m.RunStatus == nameof(BenchmarkRunStatus.Running))
            .OrderByDescending(m => m.AddedAtUtc)
            .FirstOrDefault();

        if (running != null)
        {
            suiteIndex = running.SuiteIndex;
            round = running.Round;
            dto.CurrentRunId = running.RunId;
        }
        else if (ActiveStatuses.Contains(batteryRun.Status) && dto.SuiteCount > 0 && dto.RunsPerSuite > 0)
        {
            var next = BenchmarkBatteryPlanner.NextMember(
                dto.SuiteCount,
                dto.RunsPerSuite,
                live.Select(m => (m.SuiteIndex, m.Round)).ToList());
            if (next.HasValue)
            {
                suiteIndex = next.Value.SuiteIndex;
                round = next.Value.Round;
            }
        }

        if (!suiteIndex.HasValue) return;

        dto.CurrentSuiteIndex = suiteIndex;
        dto.CurrentSuitePosition = suiteIndex + 1;
        dto.CurrentRound = round;
        dto.CurrentSuiteName = definition != null && suiteIndex.Value >= 0 && suiteIndex.Value < definition.Suites.Count
            ? definition.Suites[suiteIndex.Value].SuiteName
            : null;
    }

    private static void SetLatestAnalysis(BenchmarkBatteryRunDto dto, BenchmarkBatteryAnalysis? analysis, IReadOnlyList<long> usableRunIds)
    {
        if (analysis == null) return;

        var result = BenchmarkBatteryAnalysisService.DeserializeResult(analysis);

        dto.LatestAnalysisId = analysis.Id;
        dto.LatestAnalysisAtUtc = analysis.ComputedAtUtc;
        dto.LatestAnalysisComplete = analysis.Complete;
        dto.ComparabilityClassSha256 = analysis.ComparabilityClassSha256;
        dto.OverallIndex = result?.OverallIndex?.PointEstimate;
        dto.OverallIndexHalfWidth = result?.OverallIndex?.CombinedHalfWidth;
        dto.OverallIndexLower = result?.OverallIndex?.CombinedLower;
        dto.OverallIndexUpper = result?.OverallIndex?.CombinedUpper;
        dto.OverallSpeedIndex = result?.Speed?.OverallSpeedIndex;
        dto.TotalCost = result?.Cost?.TotalCost;
        dto.AnalysisStale = BenchmarkBatteryAnalysisService.IsStale(usableRunIds, analysis);
        dto.AnalysisHasExcludedMembers = result != null && result.ExcludedMembers.Count > 0;
    }

    // =======================================================================================
    // Analyses and report
    // =======================================================================================

    /// <summary>
    /// Computes and persists the battery analysis, paired against <c>compareWithBatteryRunId</c> when
    /// given. 400 with the explanation when the members do not form one composite or the comparison
    /// is not eligible.
    /// </summary>
    [HttpPost("runs/{id:long}/analysis")]
    public async Task<IActionResult> AnalyseBatteryRun(long id, [FromBody] BenchmarkBatteryCompareRequest? request, CancellationToken ct)
    {
        if (!await _db.BenchmarkBatteryRuns.AnyAsync(r => r.Id == id, ct)) return NotFound();

        var (analysis, _, _, error) = await _analysisService.AnalyseAsync(
            id, CurrentUserId(), request?.CompareWithBatteryRunId, ct);

        if (analysis == null)
        {
            return BadRequest(error ?? "The battery run could not be analyzed.");
        }

        var dto = await BuildAnalysisDtoAsync(analysis, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet("runs/{id:long}/analysis")]
    public async Task<IActionResult> GetBatteryRunAnalysis(long id, CancellationToken ct)
    {
        var analysis = await _analysisService.GetLatestAsync(id, ct);
        if (analysis == null) return NoContent();

        var dto = await BuildAnalysisDtoAsync(analysis, ct);
        return dto == null ? NoContent() : Ok(dto);
    }

    private async Task<BenchmarkBatteryAnalysisDto?> BuildAnalysisDtoAsync(BenchmarkBatteryAnalysis analysis, CancellationToken ct)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == analysis.BenchmarkBatteryRunId, ct);

        if (batteryRun == null) return null;

        var state = await LoadMemberStateAsync(new[] { batteryRun.Id }, ct);
        int suiteCount = SuiteCountOf(batteryRun, TryReadDefinition(batteryRun.DefinitionJson));
        long[] memberRunIds = BenchmarkBatteryLeaderboardService.ReadMemberRunIds(analysis);
        var result = BenchmarkBatteryAnalysisService.DeserializeResult(analysis);

        string? comparedName = null;
        if (analysis.ComparedWithBatteryRunId.HasValue)
        {
            comparedName = await _db.BenchmarkBatteryRuns
                .Where(r => r.Id == analysis.ComparedWithBatteryRunId.Value)
                .Select(r => r.BatteryName)
                .FirstOrDefaultAsync(ct);
        }

        return new BenchmarkBatteryAnalysisDto
        {
            Id = analysis.Id,
            BatteryRunId = batteryRun.Id,
            BatteryName = batteryRun.BatteryName,
            ComputedAtUtc = analysis.ComputedAtUtc,
            MemberRunIds = memberRunIds.ToList(),
            RunCount = memberRunIds.Length,
            DefinitionSha256 = analysis.DefinitionSha256,
            ComparabilityClassSha256 = analysis.ComparabilityClassSha256,
            Complete = analysis.Complete,
            HarnessVersion = analysis.HarnessVersion,
            ScoringMethodVersion = analysis.ScoringMethodVersion,
            Stale = BenchmarkBatteryAnalysisService.IsStale(UsableRunIds(batteryRun, state, suiteCount), analysis),
            ComparedWithBatteryRunId = analysis.ComparedWithBatteryRunId,
            ComparedWithBatteryName = comparedName,
            Result = result,
            Comparison = BenchmarkBatteryAnalysisService.DeserializeComparison(analysis),
            ExcludedMembers = result?.ExcludedMembers.ToList() ?? new List<BenchmarkBatteryExcludedMember>()
        };
    }

    /// <summary>
    /// The battery Markdown report, built from the latest <b>persisted</b> analysis, so it stays
    /// reproducible after a member run is deleted. File name
    /// <c>{battery}_{model}_battery_R{n}_{yyyyMMdd_HHmmss}.md</c>.
    /// </summary>
    [HttpGet("runs/{id:long}/report")]
    public async Task<IActionResult> GetBatteryRunReport(long id, CancellationToken ct)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (batteryRun == null) return NotFound();

        var analysis = await _analysisService.GetLatestAsync(id, ct);
        if (analysis == null)
        {
            return BadRequest("This battery run has no analysis yet. Compute the analysis before downloading a report.");
        }

        var result = BenchmarkBatteryAnalysisService.DeserializeResult(analysis);
        if (result == null)
        {
            return BadRequest("The stored analysis could not be read, so no report can be produced.");
        }

        var definition = TryReadDefinition(batteryRun.DefinitionJson);
        if (definition == null)
        {
            return BadRequest("The battery run's stored definition cannot be read, so no report can be produced.");
        }

        long[] memberRunIds = BenchmarkBatteryLeaderboardService.ReadMemberRunIds(analysis);

        var memberRuns = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => memberRunIds.Contains(r.Id))
            .OrderBy(r => r.StartedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, memberRuns, ct);

        // Each analysed run in the suite its membership names, at its first round.
        var suiteIndexByRun = batteryRun.Members
            .Where(m => !m.Superseded)
            .GroupBy(m => m.BenchmarkRunId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Round).First().SuiteIndex);

        var bySuite = memberRuns
            .Where(r => suiteIndexByRun.ContainsKey(r.Id))
            .GroupBy(r => suiteIndexByRun[r.Id])
            .OrderBy(g => g.Key)
            .Select(g => (SuiteIndex: g.Key, Runs: (IReadOnlyList<BenchmarkRun>)g.ToList()))
            .ToList();

        BenchmarkBatteryComparabilityResult? comparability = bySuite.Count > 0
            ? BenchmarkBatteryComparability.Resolve(bySuite)
            : null;

        var comparison = BenchmarkBatteryAnalysisService.DeserializeComparison(analysis);
        string? comparisonLabel = null;
        if (comparison != null && analysis.ComparedWithBatteryRunId.HasValue)
        {
            string? baselineName = await _db.BenchmarkBatteryRuns
                .Where(r => r.Id == analysis.ComparedWithBatteryRunId.Value)
                .Select(r => r.BatteryName)
                .FirstOrDefaultAsync(ct);
            comparisonLabel = baselineName == null
                ? $"battery run #{analysis.ComparedWithBatteryRunId.Value}"
                : $"battery run #{analysis.ComparedWithBatteryRunId.Value} ({baselineName})";
        }

        var answerOutcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(_db, memberRunIds, withRefutedSentences: true, ct);
        string markdown = BenchmarkBatteryReportBuilder.BuildMarkdownReport(
            batteryRun, definition, result, analysis, memberRuns, comparability, comparison, comparisonLabel,
            GetOverseerVersion(), answerOutcomes);

        string? modelName = memberRuns
            .Select(r => r.TestedModelSnapshot.Label())
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));

        string filename = BenchmarkBatteryReportBuilder.BuildFileName(
            batteryRun.BatteryName, modelName, batteryRun.RunsPerSuite, analysis.ComputedAtUtc);

        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", filename);
    }

    /// <summary>The running build, formatted as the run report's version line shows it.</summary>
    private static string? GetOverseerVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational) ? null : informational.Split('+')[0];
    }

    // =======================================================================================
    // Leaderboard
    // =======================================================================================

    /// <summary>
    /// The latest analysis of every battery run with the given definition hash, one ranked list per
    /// comparability class (Statistical Method M9), highest Overall Index first. Battery runs whose
    /// latest analysis is incomplete are listed apart, unranked.
    /// </summary>
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard([FromQuery] string? definitionSha256, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definitionSha256))
        {
            return BadRequest("A definition hash is required.");
        }

        string hash = definitionSha256.Trim().ToLowerInvariant();

        var heads = await _db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => a.DefinitionSha256 == hash)
            .Select(a => new { a.Id, a.BenchmarkBatteryRunId, a.ComputedAtUtc })
            .ToListAsync(ct);

        var latestIds = heads
            .GroupBy(a => a.BenchmarkBatteryRunId)
            .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First().Id)
            .ToList();

        var analyses = latestIds.Count == 0
            ? new List<BenchmarkBatteryAnalysis>()
            : await _db.BenchmarkBatteryAnalyses
                .AsNoTracking()
                .Where(a => latestIds.Contains(a.Id))
                .ToListAsync(ct);

        var batteryRunIds = analyses.Select(a => a.BenchmarkBatteryRunId).Distinct().ToList();
        var batteryRuns = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(r => batteryRunIds.Contains(r.Id))
            .ToListAsync(ct);
        var batteryRunById = batteryRuns.ToDictionary(r => r.Id);

        var state = await LoadMemberStateAsync(batteryRunIds, ct);
        var configLabels = await LoadConfigurationLabelsAsync(batteryRuns, ct);
        var identities = await _leaderboard.LoadIdentitiesAsync(batteryRuns, IdentityRunIds(batteryRuns, state), ct);

        var rows = analyses
            .Where(a => batteryRunById.ContainsKey(a.BenchmarkBatteryRunId))
            .Select(a => ToLeaderboardRow(
                a,
                batteryRunById[a.BenchmarkBatteryRunId],
                state,
                configLabels,
                identities.TryGetValue(a.BenchmarkBatteryRunId, out var identity) ? identity : null))
            .ToList();

        var ranked = rows
            .Where(r => BenchmarkBatteryLeaderboardService.IsRanked(r.Complete, r.ComparabilityClassSha256, r.OverallIndex))
            .ToList();
        var rankedIds = new HashSet<long>(ranked.Select(r => r.BatteryRunId));

        var classes = ranked
            .GroupBy(r => r.ComparabilityClassSha256!, StringComparer.Ordinal)
            .Select(g => new BenchmarkBatteryLeaderboardClassDto
            {
                ComparabilityClassSha256 = g.Key,
                HarnessVersion = g.OrderByDescending(r => r.ComputedAtUtc).First().HarnessVersion,
                ScoringMethodVersion = g.OrderByDescending(r => r.ComputedAtUtc).First().ScoringMethodVersion,
                Rows = g.OrderByDescending(r => r.OverallIndex).ThenBy(r => r.BatteryRunId).ToList()
            })
            .OrderByDescending(c => c.Rows.Max(r => r.ComputedAtUtc))
            .ToList();

        if (classes.Count >= 2)
        {
            var representatives = classes.ToDictionary(
                c => c.ComparabilityClassSha256,
                c => analyses.First(a => a.Id == c.Rows.OrderByDescending(r => r.ComputedAtUtc).First().AnalysisId));
            var distinguishing = await _leaderboard.DistinguishingKeysAsync(representatives, ct);
            foreach (var cls in classes)
            {
                cls.DistinguishingKeys = distinguishing.TryGetValue(cls.ComparabilityClassSha256, out var keys) ? keys : new List<string>();
            }
        }

        foreach (var cls in classes)
        {
            cls.Label = ClassLabel(cls.HarnessVersion, cls.ScoringMethodVersion, cls.DistinguishingKeys);
        }

        var battery = await _db.BenchmarkBatteries
            .AsNoTracking()
            .Where(b => b.DefinitionSha256 == hash)
            .OrderByDescending(b => b.ModifiedAtUtc)
            .Select(b => new { b.Id, b.Name })
            .FirstOrDefaultAsync(ct);

        var newestRun = batteryRuns.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).FirstOrDefault();

        return Ok(new BenchmarkBatteryLeaderboardDto
        {
            DefinitionSha256 = hash,
            BatteryId = battery?.Id ?? newestRun?.BenchmarkBatteryId,
            BatteryName = battery?.Name ?? newestRun?.BatteryName,
            Classes = classes,
            Incomplete = rows
                .Where(r => !rankedIds.Contains(r.BatteryRunId))
                .OrderByDescending(r => r.ComputedAtUtc)
                .ThenByDescending(r => r.BatteryRunId)
                .ToList()
        });
    }

    private static BenchmarkBatteryLeaderboardRowDto ToLeaderboardRow(
        BenchmarkBatteryAnalysis analysis,
        BenchmarkBatteryRun batteryRun,
        MemberState state,
        IReadOnlyDictionary<long, string> configLabels,
        BenchmarkBatteryRunIdentity? identity)
    {
        var result = BenchmarkBatteryAnalysisService.DeserializeResult(analysis);
        var definition = TryReadDefinition(batteryRun.DefinitionJson);

        return new BenchmarkBatteryLeaderboardRowDto
        {
            BatteryRunId = batteryRun.Id,
            BatteryId = batteryRun.BenchmarkBatteryId,
            BatteryName = batteryRun.BatteryName,
            DefinitionRevision = definition?.Revision ?? 0,
            AnalysisId = analysis.Id,
            ComputedAtUtc = analysis.ComputedAtUtc,
            TestedModelConfigurationId = BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun)?.TestedModelConfigurationId,
            TestedModelLabel = TestedModelLabelOf(batteryRun, state, configLabels),
            TestedProvider = identity?.TestedProvider,
            TestedModelId = identity?.TestedModelId,
            TestedThinkingLevel = identity?.TestedThinkingLevel,
            TestedReasoningMode = identity?.TestedReasoningMode,
            TestedServiceTier = identity?.TestedServiceTier,
            Status = batteryRun.Status.ToString(),
            RunsPerSuite = batteryRun.RunsPerSuite,
            SuiteCount = result?.SuiteCount ?? SuiteCountOf(batteryRun, definition),
            CompletedSuiteCount = result?.CompletedSuiteCount ?? 0,
            Complete = analysis.Complete,
            ComparabilityClassSha256 = analysis.ComparabilityClassSha256,
            HarnessVersion = analysis.HarnessVersion,
            ScoringMethodVersion = analysis.ScoringMethodVersion,
            OverallIndex = result?.OverallIndex?.PointEstimate,
            OverallIndexHalfWidth = result?.OverallIndex?.CombinedHalfWidth,
            OverallIndexLower = result?.OverallIndex?.CombinedLower,
            OverallIndexUpper = result?.OverallIndex?.CombinedUpper,
            OverallSpeedIndex = result?.Speed?.OverallSpeedIndex,
            TotalCost = result?.Cost?.TotalCost,
            PassCost = result?.Cost?.PassCost
        };
    }

    private static string ClassLabel(string? harnessVersion, int scoringMethodVersion, IReadOnlyList<string> distinguishingKeys)
    {
        string label = $"Harness {(string.IsNullOrWhiteSpace(harnessVersion) ? "unrecorded" : harnessVersion)} · scoring method {scoringMethodVersion}";
        return distinguishingKeys.Count > 0
            ? label + " · differs in " + string.Join(", ", distinguishingKeys)
            : label;
    }
}
