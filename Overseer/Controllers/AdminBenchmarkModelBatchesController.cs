namespace Overseer.Controllers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Telemetry;

/// <summary>
/// GnollBench model batches: the preflight the launcher renders, start, the progress dialog's polling,
/// cancel, resume, skip, diagnostics and delete.
///
/// <para>Execution belongs to <see cref="BenchmarkModelBatchOrchestrator"/> and the rules to
/// <see cref="BenchmarkModelBatchGuardrails"/>; this controller maps their outcomes to HTTP and
/// projects rows into DTOs, reading each member's result from its run, its series' group analysis
/// or its battery run's analysis.</para>
/// </summary>
[Route("api/admin/benchmark/model-batches")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkModelBatchesController : ControllerBase
{
    internal const int DefaultListSize = 50;
    internal const int MaxListSize = 500;

    private static readonly BenchmarkRunSeriesStatus[] LiveStatuses =
    {
        BenchmarkRunSeriesStatus.Pending,
        BenchmarkRunSeriesStatus.Running,
        BenchmarkRunSeriesStatus.WaitingForCap
    };

    private static readonly BenchmarkRunStatus[] CompletedRunStatuses =
    {
        BenchmarkRunStatus.Completed,
        BenchmarkRunStatus.CompletedWithLimits,
        BenchmarkRunStatus.CompletedWithErrors
    };

    /// <summary>A finished member's result, keyed by member id and what it was computed over.</summary>
    private static readonly ConcurrentDictionary<long, (string Key, BenchmarkModelBatchMemberResultDto Result)> ResultCache = new();

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkModelBatchOrchestrator _orchestrator;
    private readonly BenchmarkRunManager _runManager;
    private readonly IConfiguration _configuration;
    private readonly ModelPricingService? _pricing;

    public AdminBenchmarkModelBatchesController(
        ApplicationDbContext db,
        BenchmarkModelBatchOrchestrator orchestrator,
        BenchmarkRunManager runManager,
        IConfiguration configuration,
        ModelPricingService? pricing = null)
    {
        _db = db;
        _orchestrator = orchestrator;
        _runManager = runManager;
        _configuration = configuration;
        _pricing = pricing;
    }

    private string? CurrentUserId()
    {
        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? null : userId;
    }

    // =======================================================================================
    // Preflight and start
    // =======================================================================================

    /// <summary>Every guardrail finding and the projection for a start request not yet sent. Creates and spends nothing.</summary>
    [HttpPost("preflight")]
    public async Task<IActionResult> Preflight(
        [FromBody] StartBenchmarkModelBatchRequest request,
        [FromServices] BenchmarkModelBatchGuardrailService guardrails,
        CancellationToken ct)
    {
        if (request == null) return BadRequest("The request is missing.");

        try
        {
            var (findings, projection) = await guardrails.EvaluateAsync(request, _orchestrator.ActiveBatchDescription(), ct);
            return Ok(new BenchmarkModelBatchPreflightResponse
            {
                Findings = findings.ToList(),
                Projection = projection,
                MaxModels = BenchmarkModelBatchOptions.From(_configuration).MaxModels
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Starts a model batch. 201 with the batch; 400 with the blockers; 409 with the warnings still to
    /// acknowledge, or with the busy blocker while something else runs; 404 for an unknown target.
    /// </summary>
    [HttpPost("runs")]
    public async Task<IActionResult> StartBatch([FromBody] StartBenchmarkModelBatchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _orchestrator.StartAsync(request, CurrentUserId(), ct);
            if (!result.Succeeded) return ResultToActionResult(result);

            var dto = await GetDtoAsync(result.BatchId!.Value, ct);
            return StatusCode(StatusCodes.Status201Created, dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // =======================================================================================
    // Reading
    // =======================================================================================

    /// <summary>The batch this process drives, else the newest live or stopped one; 204 when there is none.</summary>
    [HttpGet("runs/active")]
    public async Task<IActionResult> GetActiveBatch(CancellationToken ct)
    {
        try
        {
            long? id = _orchestrator.ActiveBatchId;
            id ??= await _db.BenchmarkModelBatchRuns
                .Where(b => LiveStatuses.Contains(b.Status) || b.Status == BenchmarkRunSeriesStatus.Stopped)
                .OrderByDescending(b => b.CreatedAtUtc)
                .ThenByDescending(b => b.Id)
                .Select(b => (long?)b.Id)
                .FirstOrDefaultAsync(ct);

            if (id == null) return NoContent();

            var dto = await GetDtoAsync(id.Value, ct);
            return dto == null ? NoContent() : Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Batches newest first, without member results, for Run History.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> GetBatches([FromQuery] int? skip, [FromQuery] int? take, CancellationToken ct)
    {
        try
        {
            int offset = Math.Max(0, skip ?? 0);
            int limit = Math.Clamp(take ?? DefaultListSize, 1, MaxListSize);

            var batches = await _db.BenchmarkModelBatchRuns
                .AsNoTracking()
                .Include(b => b.Members)
                .OrderByDescending(b => b.CreatedAtUtc)
                .ThenByDescending(b => b.Id)
                .Skip(offset)
                .Take(limit)
                .ToListAsync(ct);

            var dtos = new List<BenchmarkModelBatchRunDto>();
            foreach (var batch in batches)
            {
                dtos.Add(await BuildDtoAsync(batch, includeDetails: false, ct));
            }

            return Ok(dtos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>One batch with its members' progress and results: what the progress dialog polls.</summary>
    [HttpGet("runs/{id:long}")]
    public async Task<IActionResult> GetBatch(long id, CancellationToken ct)
    {
        try
        {
            var dto = await GetDtoAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The batch's diagnostics as plain text.</summary>
    [HttpGet("runs/{id:long}/diagnostics")]
    public async Task<IActionResult> GetDiagnostics(long id, CancellationToken ct)
    {
        try
        {
            var dto = await GetDtoAsync(id, ct);
            if (dto == null) return NotFound();

            var labels = await ConfigurationLabelsAsync(dto, ct);
            string text = BenchmarkModelBatchDiagnostics.BuildText(dto, labels);
            return Content(text, "text/plain", Encoding.UTF8);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // =======================================================================================
    // Cancel, resume, skip, delete
    // =======================================================================================

    [HttpPost("runs/{id:long}/cancel")]
    public async Task<IActionResult> CancelBatch(long id, CancellationToken ct)
    {
        try
        {
            var result = await _orchestrator.CancelAsync(id, ct);
            if (!result.Succeeded) return ResultToActionResult(result);

            var dto = await GetDtoAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("runs/{id:long}/resume")]
    public async Task<IActionResult> ResumeBatch(long id, [FromBody] ResumeBenchmarkModelBatchRequest? request, CancellationToken ct)
    {
        try
        {
            var result = await _orchestrator.ResumeAsync(id, request?.Mode ?? BenchmarkModelBatchResumeMode.Continue, ct);
            if (!result.Succeeded) return ResultToActionResult(result);

            var dto = await GetDtoAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("runs/{id:long}/members/{memberId:long}/skip")]
    public async Task<IActionResult> SkipMember(long id, long memberId, CancellationToken ct)
    {
        try
        {
            var result = await _orchestrator.SkipPendingAsync(id, memberId, ct);
            if (!result.Succeeded) return ResultToActionResult(result);

            var dto = await GetDtoAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Deletes a finished batch's record: Completed, CompletedWithErrors, Cancelled or Failed. Its
    /// member runs, series and battery runs are kept. 204; 404 unknown; 409 while it is live or stopped.
    /// </summary>
    [HttpDelete("runs/{id:long}")]
    public async Task<IActionResult> DeleteBatch(long id, CancellationToken ct)
    {
        try
        {
            var batch = await _db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch == null) return NotFound();

            if (_orchestrator.IsDriving(id) || !BenchmarkModelBatchOrchestrator.IsTerminal(batch.Status))
            {
                return Conflict("Only a finished or canceled model batch can be deleted; cancel it first.");
            }

            _db.BenchmarkModelBatchRuns.Remove(batch);
            await _db.SaveChangesAsync(ct);
            foreach (var member in batch.Members) ResultCache.TryRemove(member.Id, out _);
            return NoContent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>One mapping from the orchestrator's outcomes to HTTP.</summary>
    internal static IActionResult ResultToActionResult(BenchmarkModelBatchResult result)
    {
        var refusal = new BenchmarkModelBatchRefusalDto
        {
            Message = result.Error ?? string.Empty,
            Findings = result.Findings.ToList()
        };

        return result.Outcome switch
        {
            BenchmarkModelBatchOutcome.Ok => new OkResult(),
            BenchmarkModelBatchOutcome.NotFound => new NotFoundObjectResult(result.Error),
            BenchmarkModelBatchOutcome.Blocked => new BadRequestObjectResult(refusal),
            BenchmarkModelBatchOutcome.NeedsAcknowledgment => new ConflictObjectResult(refusal),
            BenchmarkModelBatchOutcome.Conflict => result.Findings.Count > 0
                ? new ConflictObjectResult(refusal)
                : new ConflictObjectResult(result.Error),
            BenchmarkModelBatchOutcome.InstrumentChanged => new ConflictObjectResult(new
            {
                instrumentChanged = true,
                batchId = result.BatchId,
                changedKeys = result.ChangedKeys,
                message = result.Error
            }),
            _ => new BadRequestObjectResult(result.Error)
        };
    }

    // =======================================================================================
    // Projection
    // =======================================================================================

    private async Task<BenchmarkModelBatchRunDto?> GetDtoAsync(long id, CancellationToken ct)
    {
        var batch = await _db.BenchmarkModelBatchRuns
            .AsNoTracking()
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        return batch == null ? null : await BuildDtoAsync(batch, includeDetails: true, ct);
    }

    private async Task<BenchmarkModelBatchRunDto> BuildDtoAsync(BenchmarkModelBatchRun batch, bool includeDetails, CancellationToken ct)
    {
        var options = BenchmarkModelBatchOptions.From(_configuration);
        bool isDriving = _orchestrator.IsDriving(batch.Id);
        var now = DateTime.UtcNow;

        var dto = new BenchmarkModelBatchRunDto
        {
            Id = batch.Id,
            Status = batch.Status.ToString(),
            StopReason = batch.StopReason?.ToString(),
            StopReasonText = BenchmarkModelBatchOrchestrator.DescribeStopReason(batch.StopReason),
            StopDetail = batch.StopDetail,
            TargetKind = batch.TargetKind.ToString(),
            SuiteId = batch.BenchmarkSuiteId,
            BatteryId = batch.BenchmarkBatteryId,
            TargetName = batch.TargetName,
            BatteryRevision = batch.BatteryRevision,
            BatteryDefinitionSha256 = batch.BatteryDefinitionSha256,
            RunsPerModel = batch.RunsPerModel,
            Order = batch.Order.ToString(),
            OrderSeed = batch.OrderSeed,
            AllowCapWait = batch.AllowCapWait,
            Run = BenchmarkModelBatchOrchestrator.ReadTemplate(batch),
            CreatedAtUtc = batch.CreatedAtUtc,
            StartedAtUtc = batch.StartedAtUtc,
            CompletedAtUtc = batch.CompletedAtUtc,
            CreatedByUserName = batch.CreatedByUserId == null
                ? null
                : await _db.Users.Where(u => u.Id == batch.CreatedByUserId).Select(u => u.UserName).FirstOrDefaultAsync(ct),
            CurrentMemberIndex = batch.CurrentMemberIndex,
            RequestedMemberCount = batch.RequestedMemberCount,
            CompletedMemberCount = batch.CompletedMemberCount,
            FailedMemberCount = batch.FailedMemberCount,
            SkippedMemberCount = batch.SkippedMemberCount,
            AcknowledgedFindings = BenchmarkModelBatchOrchestrator.ReadFindings(batch.AcknowledgedFindingsJson),
            AdviceAtStart = BenchmarkModelBatchOrchestrator.ReadFindings(batch.AdviceAtStartJson),
            InstrumentChangeAcknowledged = batch.InstrumentChangeAcknowledged,
            IsDriving = isDriving,
            ResumeOptions = BenchmarkModelBatchOrchestrator.ResumeOptionsFor(batch, isDriving).ToList(),
            LastProgressAtUtc = batch.LastProgressAtUtc,
            StallMinutes = options.StallMinutes,
            SupersededMembersJson = batch.SupersededMembersJson
        };

        dto.Resumable = dto.ResumeOptions.Count > 0;
        dto.Stalled = LiveStatuses.Contains(batch.Status)
                      && batch.LastProgressAtUtc.HasValue
                      && now - batch.LastProgressAtUtc.Value > TimeSpan.FromMinutes(options.StallMinutes);

        if (batch.FirstMemberHarnessVersion != null || batch.FirstMemberCandidateSystemPromptSha256 != null
            || batch.FirstMemberScoringMethodVersion != null)
        {
            dto.FirstMemberInstrument = new BenchmarkModelBatchInstrumentDto
            {
                CandidateSystemPromptSha256 = batch.FirstMemberCandidateSystemPromptSha256,
                ToolGuidesSha256 = batch.FirstMemberToolGuidesSha256,
                KnowledgeBaseHeadSha = batch.FirstMemberKnowledgeBaseHeadSha,
                WikiHeadSha = batch.FirstMemberWikiHeadSha,
                SourceCodeHeadSha = batch.FirstMemberSourceCodeHeadSha,
                HarnessVersion = batch.FirstMemberHarnessVersion,
                ScoringMethodVersion = batch.FirstMemberScoringMethodVersion
            };
        }

        int suiteCount = 1;
        if (batch.TargetKind == BenchmarkModelBatchTargetKind.Battery && batch.BenchmarkBatteryId.HasValue)
        {
            dto.SuiteNames = await _db.BenchmarkBatterySuites
                .AsNoTracking()
                .Where(s => s.BenchmarkBatteryId == batch.BenchmarkBatteryId.Value)
                .OrderBy(s => s.OrderIndex)
                .ThenBy(s => s.Id)
                .Select(s => s.SuiteName)
                .ToListAsync(ct);
            suiteCount = Math.Max(1, dto.SuiteNames.Count);
        }
        else if (!string.IsNullOrWhiteSpace(batch.TargetName))
        {
            dto.SuiteNames = new List<string> { batch.TargetName };
        }

        int stepCount = batch.TargetKind == BenchmarkModelBatchTargetKind.Battery
            ? suiteCount * Math.Max(1, batch.RunsPerModel)
            : Math.Max(1, batch.RunsPerModel);

        var orderedMembers = batch.Members.OrderBy(m => m.OrderIndex).ToList();
        var baseModel = orderedMembers
            .Where(m => m.BenchmarkRunId != null || m.BenchmarkRunSeriesId != null || m.BenchmarkBatteryRunId != null)
            .Select(BenchmarkModelBatchOrchestrator.ReadModel)
            .FirstOrDefault();

        var estimator = new BenchmarkRunCostEstimator(_pricing);
        decimal? liveCandidate = 0m;
        decimal? liveTotal = 0m;
        bool anyRun = false;

        foreach (var member in orderedMembers)
        {
            var memberDto = new BenchmarkModelBatchMemberDto
            {
                Id = member.Id,
                OrderIndex = member.OrderIndex,
                Model = BenchmarkModelBatchOrchestrator.ReadModel(member),
                Status = member.Status.ToString(),
                RunId = member.BenchmarkRunId,
                SeriesId = member.BenchmarkRunSeriesId,
                BatteryRunId = member.BenchmarkBatteryRunId,
                StepCount = stepCount,
                StartedAtUtc = member.StartedAtUtc,
                CompletedAtUtc = member.CompletedAtUtc,
                ErrorMessage = member.ErrorMessage
            };
            dto.Members.Add(memberDto);

            if (!includeDetails) continue;

            var runs = await BenchmarkModelBatchOrchestrator.MemberRunsAsync(_db, member, completedOnly: false, ct);
            memberDto.RunIds = runs.Select(r => r.Id).ToList();
            if (runs.Count == 0) continue;
            anyRun = true;

            var first = runs.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).First();
            memberDto.Instrument = new BenchmarkModelBatchInstrumentDto
            {
                CandidateSystemPromptSha256 = first.CandidateSystemPromptSha256,
                ToolGuidesSha256 = first.ToolGuidesSha256,
                KnowledgeBaseHeadSha = first.KnowledgeBaseHeadSha,
                WikiHeadSha = first.WikiHeadSha,
                SourceCodeHeadSha = first.SourceCodeHeadSha,
                HarnessVersion = first.HarnessVersion,
                ScoringMethodVersion = first.ScoringMethodVersion,
                CorpusIndexFingerprintsJson = first.CorpusIndexFingerprintsJson
            };
            memberDto.InstrumentDriftKeys = DriftKeys(dto.FirstMemberInstrument, memberDto.Instrument,
                comparePrompt: baseModel == null || string.Equals(baseModel.ParallelExecutionMode, memberDto.Model.ParallelExecutionMode, StringComparison.Ordinal));

            var running = runs.FirstOrDefault(r => r.Status == BenchmarkRunStatus.Running);
            if (running != null)
            {
                memberDto.CurrentRunId = running.Id;
                memberDto.CurrentStage = _runManager.GetStage(running.Id)?.ToString();
                memberDto.CurrentStepIndex = member.BenchmarkRunSeriesId != null && running.RunSeriesIndex.HasValue
                    ? running.RunSeriesIndex
                    : runs.Count(r => r.Id <= running.Id);
                memberDto.AnsweredQuestionCount = await _db.BenchmarkRunAnswers.CountAsync(a => a.BenchmarkRunId == running.Id, ct);
                memberDto.TotalQuestionCount = running.TotalQuestionCount;
            }

            // Live cost over every run so far: a running run from its answer rows, others from their columns.
            foreach (var run in runs)
            {
                BenchmarkRunCostEstimate estimate;
                if (run.Status == BenchmarkRunStatus.Running)
                {
                    var withAnswers = await _db.BenchmarkRuns.AsNoTracking().Include(r => r.Answers).FirstAsync(r => r.Id == run.Id, ct);
                    estimate = await estimator.EstimateAsync(withAnswers);
                }
                else
                {
                    estimate = await estimator.EstimateAsync(run, run, null);
                }

                liveCandidate = liveCandidate.HasValue && estimate.Candidate.HasValue ? liveCandidate + estimate.Candidate : null;
                liveTotal = liveTotal.HasValue && estimate.Total.HasValue ? liveTotal + estimate.Total : null;
            }

            var completed = runs.Where(r => CompletedRunStatuses.Contains(r.Status)).ToList();
            if (completed.Count > 0)
            {
                memberDto.Result = await MemberResultAsync(member, completed, estimator, ct);
            }
        }

        if (includeDetails && anyRun)
        {
            dto.LiveCandidateCostUsd = liveCandidate;
            dto.LiveTotalCostUsd = liveTotal;
        }

        return dto;
    }

    /// <summary>
    /// The instrument keys on which a member's first run differs from the batch's first member. The
    /// prompt hash is compared only between members of one parallel mode, which the prompt depends on.
    /// </summary>
    internal static List<string> DriftKeys(BenchmarkModelBatchInstrumentDto? baseline, BenchmarkModelBatchInstrumentDto? member, bool comparePrompt)
    {
        var keys = new List<string>();
        if (baseline == null || member == null) return keys;

        void Compare(string name, string? recorded, string? now)
        {
            if (string.IsNullOrEmpty(recorded) || string.IsNullOrEmpty(now)) return;
            if (!string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase)) keys.Add(name);
        }

        if (comparePrompt) Compare("CandidateSystemPromptSha256", baseline.CandidateSystemPromptSha256, member.CandidateSystemPromptSha256);
        Compare("ToolGuidesSha256", baseline.ToolGuidesSha256, member.ToolGuidesSha256);
        Compare("KnowledgeBaseHeadSha", baseline.KnowledgeBaseHeadSha, member.KnowledgeBaseHeadSha);
        Compare("WikiHeadSha", baseline.WikiHeadSha, member.WikiHeadSha);
        Compare("SourceCodeHeadSha", baseline.SourceCodeHeadSha, member.SourceCodeHeadSha);
        Compare("HarnessVersion", baseline.HarnessVersion, member.HarnessVersion);
        if (baseline.ScoringMethodVersion.HasValue && member.ScoringMethodVersion.HasValue
            && baseline.ScoringMethodVersion != member.ScoringMethodVersion)
        {
            keys.Add("ScoringMethodVersion");
        }

        return keys;
    }

    /// <summary>
    /// A finished member's result: the index from its run, its series' latest group analysis or its
    /// battery run's latest analysis, and the speed, cost and error figures over its completed runs'
    /// answers. Cached per member until the runs it covers or their completion change.
    /// </summary>
    private async Task<BenchmarkModelBatchMemberResultDto> MemberResultAsync(
        BenchmarkModelBatchMember member,
        IReadOnlyList<BenchmarkRun> completed,
        BenchmarkRunCostEstimator estimator,
        CancellationToken ct)
    {
        string key = string.Join(",", completed.Select(r => $"{r.Id}:{r.Status}:{r.CompletedAtUtc?.Ticks}:{r.QualityIndex}"))
                     + $"|{member.Status}";
        if (ResultCache.TryGetValue(member.Id, out var cached) && cached.Key == key) return cached.Result;

        var result = new BenchmarkModelBatchMemberResultDto();
        var runIds = completed.Select(r => r.Id).ToList();

        if (member.BenchmarkBatteryRunId is long batteryRunId)
        {
            var analysis = await _db.BenchmarkBatteryAnalyses
                .AsNoTracking()
                .Where(a => a.BenchmarkBatteryRunId == batteryRunId)
                .OrderByDescending(a => a.ComputedAtUtc)
                .ThenByDescending(a => a.Id)
                .FirstOrDefaultAsync(ct);
            var statistics = analysis == null ? null : BenchmarkBatteryAnalysisService.DeserializeResult(analysis);
            if (statistics?.OverallIndex != null)
            {
                result.OverallIndex = statistics.OverallIndex.PointEstimate;
                result.IndexHalfWidth = statistics.OverallIndex.CombinedHalfWidth;
                result.IndexSource = "batteryAnalysis";
            }
        }
        else if (member.BenchmarkRunSeriesId is long seriesId)
        {
            long? groupId = await _db.BenchmarkRunSeries.Where(s => s.Id == seriesId).Select(s => s.AutoCreatedGroupId).FirstOrDefaultAsync(ct);
            var analysis = groupId == null
                ? null
                : await _db.BenchmarkGroupAnalyses
                    .AsNoTracking()
                    .Where(a => a.BenchmarkRunGroupId == groupId.Value)
                    .OrderByDescending(a => a.ComputedAtUtc)
                    .ThenByDescending(a => a.Id)
                    .FirstOrDefaultAsync(ct);
            var statistics = BenchmarkGroupAnalysisService.DeserialiseResult(analysis);
            if (statistics != null && statistics.Index.RunCount > 0)
            {
                result.IntelligenceIndex = statistics.Index.PointEstimate;
                result.IndexHalfWidth = statistics.Index.CombinedHalfWidth;
                result.IndexSource = "groupAnalysis";
            }
            else
            {
                var indices = completed.Where(r => r.QualityIndex.HasValue).Select(r => (double)r.QualityIndex!.Value).ToList();
                if (indices.Count > 0)
                {
                    result.IntelligenceIndex = indices.Average();
                    result.IndexSource = "runMean";
                }
            }
        }
        else
        {
            var run = completed[0];
            if (run.QualityIndex.HasValue)
            {
                result.IntelligenceIndex = run.QualityIndex.Value;
                result.IndexHalfWidth = run.QualityIndexStandardError is double se ? 1.96 * se : null;
                result.IndexSource = "run";
            }
        }

        var answers = await _db.BenchmarkRunAnswers
            .AsNoTracking()
            .Include(a => a.ModelCalls)
            .Where(a => runIds.Contains(a.BenchmarkRunId))
            .ToListAsync(ct);
        var ok = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();

        result.MedianModelTimeMs = BenchmarkGroupStatistics.Median(ok.Select(a => (double)a.ModelTimeMs).ToList());
        result.TtftP50Ms = BenchmarkGroupStatistics.Median(ok
            .Select(CallTelemetryMeasures.TimeToFirstAnswerTextMs)
            .Where(t => t.HasValue)
            .Select(t => (double)t!.Value)
            .ToList());
        result.OwnWaitShare = CallTelemetryMeasures.OwnWaitShare(answers);
        result.FailedAnswers = answers.Count(a => a.Status != BenchmarkAnswerStatus.Ok);
        result.ProviderErrors = answers.Count(a => a.Status == BenchmarkAnswerStatus.ProviderError);
        result.Retries = answers.Sum(a => CallTelemetryMeasures.CandidateCalls(a).Sum(c => Math.Max(0, c.AttemptCount - 1)));

        decimal? candidate = 0m;
        decimal? total = 0m;
        int answered = 0;
        foreach (var run in completed)
        {
            var runAnswers = answers.Where(a => a.BenchmarkRunId == run.Id).ToList();
            var estimate = await estimator.EstimateAsync(run, run, BenchmarkRunFinalizer.ResolveServedServiceTier(runAnswers));
            candidate = candidate.HasValue && estimate.Candidate.HasValue ? candidate + estimate.Candidate : null;
            total = total.HasValue && estimate.Total.HasValue ? total + estimate.Total : null;
            answered += run.AnsweredQuestionCount;

            result.RefutedClaims += run.ClaimsRefutedCount;
            result.ConfirmedCriticalErrors += BenchmarkOutcomeSummary.Compute(run, runAnswers)?.ConfirmedCriticalErrorCount ?? 0;
        }

        result.CandidateCostUsd = candidate;
        result.TotalCostUsd = total;
        result.CandidateCostPerQuestionUsd = candidate.HasValue && answered > 0 ? candidate.Value / answered : null;

        ResultCache[member.Id] = (key, result);
        return result;
    }

    /// <summary>The names of the configurations the batch's settings and members name, for the diagnostics.</summary>
    private async Task<Dictionary<long, string>> ConfigurationLabelsAsync(BenchmarkModelBatchRunDto dto, CancellationToken ct)
    {
        var run = dto.Run;
        var ids = new List<long?>
        {
            run?.AssessorModelConfigurationId,
            run?.CoAssessorModelConfigurationId,
            run?.SecondOpinionAssessorModelConfigurationId,
            run?.ClaimVerifierModelConfigurationId,
            run?.ReportWriterModelConfigurationId
        }
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .ToList();

        if (ids.Count == 0) return new Dictionary<long, string>();

        return await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, Label = c.DisplayName ?? c.ModelId })
            .ToDictionaryAsync(c => c.Id, c => c.Label, ct);
    }
}
