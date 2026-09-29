namespace Overseer.Services.Benchmarking;

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Privacy;
using Sentry;

/// <summary>
/// Why a launch was refused. The controller maps these onto HTTP status codes and the series
/// orchestrator maps them onto stop reasons, so neither has to interpret an error string.
/// </summary>
public enum BenchmarkRunLaunchOutcome
{
    Started = 0,

    /// <summary>A run is already in flight. Only one may execute at a time.</summary>
    Conflict = 1,

    /// <summary>The suite, or a referenced configuration, does not exist.</summary>
    NotFound = 2,

    /// <summary>The request is well-formed but not valid — an unusable configuration, an unassessed suite.</summary>
    Invalid = 3,

    /// <summary>The compliance guard refused the spend. Carries the guard's own wording.</summary>
    SpendDenied = 4,

    /// <summary>
    /// Candidate and assessor share a provider and the caller has not acknowledged it. The
    /// controller returns this as a 409 the operator can confirm through; the orchestrator never
    /// sees it, because a series stores an already-acknowledged request.
    /// </summary>
    SameProviderNotAcknowledged = 5,

    /// <summary>
    /// Candidate and report writer share a provider and the caller has not acknowledged it. Checked
    /// after <see cref="SameProviderNotAcknowledged"/>, so one confirmation is asked at a time; the
    /// controller returns it as the same kind of 409, with the warning's role <c>reportWriter</c>.
    /// </summary>
    ReportWriterSameProviderNotAcknowledged = 6
}

public sealed record BenchmarkRunLaunchResult
{
    public BenchmarkRunLaunchOutcome Outcome { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="BenchmarkRunLaunchOutcome.Started"/>.</summary>
    public long? RunId { get; init; }

    public string? Error { get; init; }

    public SameProviderWarningDto? SameProviderWarning { get; init; }

    public bool Started => Outcome == BenchmarkRunLaunchOutcome.Started;

    public static BenchmarkRunLaunchResult Ok(long runId) =>
        new() { Outcome = BenchmarkRunLaunchOutcome.Started, RunId = runId };

    public static BenchmarkRunLaunchResult Fail(BenchmarkRunLaunchOutcome outcome, string error) =>
        new() { Outcome = outcome, Error = error };
}

/// <summary>
/// The one path that validates a <see cref="StartBenchmarkRunRequest"/>, creates the
/// <see cref="BenchmarkRun"/> row and hands it to <see cref="BenchmarkService"/>.
///
/// <para><b>Why this is a service rather than a controller method.</b> A series launches its members
/// from the same request the operator submitted once, and it must launch them through exactly the
/// validation a single run goes through. Two copies of that validation would drift — one would gain
/// a check the other did not, and the divergence would show up as a series whose members were
/// admitted under different rules, which is precisely the thing a replicate set may not be. So the
/// controller's <c>StartRun</c> and <see cref="BenchmarkSeriesOrchestrator"/> both call
/// <see cref="CreateAndLaunchRunAsync"/> and neither reimplements it.</para>
///
/// <para>Scoped, because it holds a <see cref="ApplicationDbContext"/>. The orchestrator is a
/// singleton and creates its own scope per member.</para>
/// </summary>
public class BenchmarkRunLauncher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly BenchmarkService _benchmarkService;
    private readonly BenchmarkRunManager _runManager;
    private readonly BenchmarkComplianceGuard _complianceGuard;
    private readonly EndpointPolicy _endpointPolicy;
    private readonly ModelPricingService? _modelPricingService;

    public BenchmarkRunLauncher(
        ApplicationDbContext dbContext,
        BenchmarkService benchmarkService,
        BenchmarkRunManager runManager,
        BenchmarkComplianceGuard complianceGuard,
        EndpointPolicy endpointPolicy,
        ModelPricingService? modelPricingService = null)
    {
        _dbContext = dbContext;
        _benchmarkService = benchmarkService;
        _runManager = runManager;
        _complianceGuard = complianceGuard;
        _endpointPolicy = endpointPolicy;
        _modelPricingService = modelPricingService;
    }

    /// <summary>Everything the creation step needs, once the request has passed every check.</summary>
    private sealed record ValidatedLaunch(
        BenchmarkSuite Suite,
        SystemAiApiConfiguration TestedConfig,
        SystemAiApiConfiguration AssessorConfig,
        SystemAiApiConfiguration? SecondOpinionConfig,
        SystemAiApiConfiguration? ClaimVerifierConfig,
        int? RequestedMode,
        bool IsSameProvider,
        SystemAiApiConfiguration? CoAssessorConfig,
        SystemAiApiConfiguration? ReportWriterConfig);

    /// <summary>
    /// Every rule a run must satisfy, in one place. Creates nothing, so a caller that only needs
    /// to know whether a request <i>would</i> be accepted can ask without side effects.
    /// </summary>
    private async Task<(BenchmarkRunLaunchResult? Failure, ValidatedLaunch? Validated)> ValidateAsync(
        StartBenchmarkRunRequest request, CancellationToken ct)
    {
        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(_dbContext, ct);
        if (!canSpend)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.SpendDenied,
                denialReason ?? "The benchmark spend guard refused this run."), null);
        }

        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);

        if (suite == null)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.NotFound, "Benchmark suite not found."), null);
        }

        if (suite.Questions.Count == 0)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid, "Benchmark suite has no questions."), null);
        }

        int unassessedCount = suite.Questions.Count(q => q.AssessedDifficulty == null);
        if (unassessedCount > 0)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid,
                $"Benchmark suite '{suite.Name}' has {unassessedCount} of {suite.Questions.Count} " +
                "question(s) without an assessed difficulty. Assess question difficulty for the whole " +
                "suite before running a benchmark."), null);
        }

        var testedConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(
            new object?[] { request.TestedModelConfigurationId }, ct);
        var assessorConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(
            new object?[] { request.AssessorModelConfigurationId }, ct);

        if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey) || (testedConfig.ModelRole & 4) != 4)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid,
                "Tested model configuration is invalid, missing an API key, or not configured with the Benchmark role."), null);
        }

        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey) || (assessorConfig.ModelRole & 4) != 4)
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid,
                "Assessor model configuration is invalid, missing an API key, or not configured with the Benchmark role."), null);
        }

        // Optional: a co-assessor makes the run a two-member panel. Held to the same bar as the
        // second opinion, and the panel must span two providers with neither member being the
        // model under test.
        SystemAiApiConfiguration? coAssessorConfig = null;
        if (request.CoAssessorModelConfigurationId.HasValue)
        {
            coAssessorConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(new object?[] { request.CoAssessorModelConfigurationId.Value }, ct);

            if (coAssessorConfig == null || !coAssessorConfig.IsEnabled ||
                string.IsNullOrWhiteSpace(coAssessorConfig.EncryptedApiKey) ||
                (coAssessorConfig.ModelRole & 4) != 4)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    "Co-assessor configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role."), null);
            }

            if (coAssessorConfig.Id == assessorConfig.Id)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    "The co-assessor must be a different configuration from the assessor."), null);
            }

            if (_complianceGuard.IsSameProvider(assessorConfig, coAssessorConfig))
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    $"A panel needs members from two providers. The assessor and the co-assessor both belong to {assessorConfig.Provider}."), null);
            }

            if (_complianceGuard.IsSameModel(testedConfig, assessorConfig) ||
                _complianceGuard.IsSameModel(testedConfig, coAssessorConfig))
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    "A model under test cannot grade itself. Choose an older or other model of that family as the panel member."), null);
            }
        }

        // Optional: a second-opinion assessor re-grades severe verdicts. Held to the same bar as
        // the assessor, and simply absent when the operator did not pick one.
        SystemAiApiConfiguration? secondOpinionConfig = null;
        if (request.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            secondOpinionConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(new object?[] { request.SecondOpinionAssessorModelConfigurationId.Value }, ct);

            if (secondOpinionConfig == null || !secondOpinionConfig.IsEnabled ||
                string.IsNullOrWhiteSpace(secondOpinionConfig.EncryptedApiKey) ||
                (secondOpinionConfig.ModelRole & 4) != 4)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    $"{(request.CoAssessorModelConfigurationId.HasValue ? "Reference reader" : "Second reader")} configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role."), null);
            }
        }

        // Optional: a claim verifier checks unverified claims against source and wiki using read-only tools.
        SystemAiApiConfiguration? claimVerifierConfig = null;
        if (request.ClaimVerifierModelConfigurationId.HasValue)
        {
            claimVerifierConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(new object?[] { request.ClaimVerifierModelConfigurationId.Value }, ct);

            if (claimVerifierConfig == null || !claimVerifierConfig.IsEnabled ||
                string.IsNullOrWhiteSpace(claimVerifierConfig.EncryptedApiKey) ||
                (claimVerifierConfig.ModelRole & 4) != 4)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    "Claim verifier configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role."), null);
            }
        }

        // Optional: a report writer writes the run's two AI-written documents once it completes. It
        // is neither a grader nor a comparability key, and it is never the model under test; a writer
        // of the candidate's provider needs an acknowledgment, asked below.
        SystemAiApiConfiguration? reportWriterConfig = null;
        if (request.ReportWriterModelConfigurationId.HasValue)
        {
            reportWriterConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(new object?[] { request.ReportWriterModelConfigurationId.Value }, ct);

            string? writerRefusal = BenchmarkRunReportDocumentService.WriterRefusal(reportWriterConfig, testedConfig, _complianceGuard);
            if (writerRefusal != null)
            {
                return (BenchmarkRunLaunchResult.Fail(BenchmarkRunLaunchOutcome.Invalid, writerRefusal), null);
            }
        }

        // The mode that will actually apply, resolved here rather than in the service: only this
        // method sees the start dialog's override, and only the service sees the profile. An
        // explicit Off drops the second-opinion assessor from the run, because the enum defines
        // the two as the same thing — the mode is inert without an assessor, and an assessor is
        // inert under Off — and because the run column cannot otherwise distinguish "the operator
        // chose Never" from "nothing was stamped yet", which is what the service's own fallback
        // reads a zero as.
        int? requestedMode = request.SecondOpinionMode;
        if (requestedMode.HasValue && !Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), requestedMode.Value))
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid,
                "SecondOpinionMode must be Off (0), Flagged (1), FlaggedAndOutliers (2), All (3), or FlaggedPlusSample (4)."), null);
        }

        if (requestedMode == (int)BenchmarkSecondOpinionMode.Off)
        {
            secondOpinionConfig = null;
        }

        // In a panel run the second opinion is the reference reader, which grades every answer: the
        // requested mode is ignored. The service forces blind reading at run start.
        bool isPanelRun = coAssessorConfig != null;
        if (isPanelRun && secondOpinionConfig != null)
        {
            requestedMode = (int)BenchmarkSecondOpinionMode.All;
        }

        // Full validation, DNS included: the run records this endpoint and will call nothing else.
        foreach (var (role, config) in new (string, SystemAiApiConfiguration?)[]
        {
            ("Tested model", testedConfig), ("Assessor", assessorConfig), ("Co-assessor", coAssessorConfig),
            (isPanelRun ? "Reference reader" : "Second reader", secondOpinionConfig), ("Claim verifier", claimVerifierConfig),
            ("Report writer", reportWriterConfig)
        })
        {
            if (config == null) continue;
            var endpoint = _endpointPolicy.Validate(config.BaseUrl, config.CustomHeadersJson, config.ApiVersion);
            if (!endpoint.IsValid)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    $"{role} configuration '{config.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpoint.Error}"), null);
            }
        }

        // A panel spans two providers by construction, and its balance is what answers same-family
        // preference: the acknowledgeable gate applies to single-assessor runs only, and a panel run
        // records no acknowledgement.
        bool isSameProvider = !isPanelRun && _complianceGuard.IsSameProvider(testedConfig, assessorConfig);
        if (isSameProvider && !request.AcknowledgeSameProvider)
        {
            return (new BenchmarkRunLaunchResult
            {
                Outcome = BenchmarkRunLaunchOutcome.SameProviderNotAcknowledged,
                SameProviderWarning = new SameProviderWarningDto
                {
                    SameProvider = true,
                    Provider = testedConfig.Provider,
                    TestedModelDisplayName = testedConfig.DisplayName,
                    AssessorModelDisplayName = assessorConfig.DisplayName,
                    Message = $"Both the model under test ({testedConfig.DisplayName}) and the assessor model ({assessorConfig.DisplayName}) belong to the same provider ({testedConfig.Provider}). Evaluation of a model by its own provider family may produce biased grading."
                }
            }, null);
        }

        string? writerWarning = BenchmarkRunReportDocumentService.WriterWarning(reportWriterConfig, testedConfig, _complianceGuard);
        if (writerWarning != null && !request.AcknowledgeSameProviderReportWriter)
        {
            return (new BenchmarkRunLaunchResult
            {
                Outcome = BenchmarkRunLaunchOutcome.ReportWriterSameProviderNotAcknowledged,
                SameProviderWarning = BenchmarkRunReportDocumentService.WriterWarningDto(reportWriterConfig!, testedConfig, writerWarning)
            }, null);
        }
        return (null, new ValidatedLaunch(
            suite, testedConfig, assessorConfig, secondOpinionConfig, claimVerifierConfig,
            requestedMode, isSameProvider, coAssessorConfig, reportWriterConfig));
    }

    /// <summary>
    /// Validates the request, creates the run row, registers it with
    /// <see cref="BenchmarkRunManager"/> and starts the background execution.
    /// </summary>
    /// <param name="request">The start request. Never mutated.</param>
    /// <param name="userId">The launching admin, or null when a series resumed without one.</param>
    /// <param name="seriesId">Set when this run is a member of a series; null for a standalone run.</param>
    /// <param name="seriesIndex">1-based member position. Null for a standalone run.</param>
    /// <param name="ct">Cancels the validation and the creation, not the run itself.</param>
    /// <summary>
    /// Runs every validation <see cref="CreateAndLaunchRunAsync"/> performs, and creates nothing.
    ///
    /// <para>This exists for the series path. A series validates the request <b>once, before the
    /// series row is written</b>, so an operator who has not yet acknowledged a same-provider pair
    /// gets the 409 they can confirm through — rather than a series that was created, launched its
    /// first member, failed it, and stopped. The rules are not duplicated: this method and the
    /// launch share <see cref="ValidateAsync"/>, so a check added to one applies to both.</para>
    /// </summary>
    /// <returns>Null when the request is valid; otherwise the refusal.</returns>
    public async Task<BenchmarkRunLaunchResult?> ValidateRequestAsync(
        StartBenchmarkRunRequest request, CancellationToken ct = default)
    {
        var (failure, _) = await ValidateAsync(request, ct);
        return failure;
    }

    public async Task<BenchmarkRunLaunchResult> CreateAndLaunchRunAsync(
        StartBenchmarkRunRequest request,
        string? userId,
        long? seriesId = null,
        int? seriesIndex = null,
        CancellationToken ct = default)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict, "A benchmark run is already in progress.");
        }


        var (failure, validated) = await ValidateAsync(request, ct);
        if (failure != null) return failure;

        var suite = validated!.Suite;
        var testedConfig = validated.TestedConfig;
        var assessorConfig = validated.AssessorConfig;
        var secondOpinionConfig = validated.SecondOpinionConfig;
        var claimVerifierConfig = validated.ClaimVerifierConfig;
        var coAssessorConfig = validated.CoAssessorConfig;
        int? requestedMode = validated.RequestedMode;
        bool isSameProvider = validated.IsSameProvider;

        string? pricingSnapshotJson = BuildPricingSnapshotJson(
            testedConfig, assessorConfig, secondOpinionConfig, claimVerifierConfig, coAssessorConfig);

        // Captured before the run is added, so each capture on the still-clean context saves at once.
        var testedSnapshot = await SystemAiConfigurationSnapshotStore.CaptureAsync(_dbContext, testedConfig, ct);
        var assessorSnapshot = await SystemAiConfigurationSnapshotStore.CaptureAsync(_dbContext, assessorConfig, ct);
        var coAssessorSnapshot = coAssessorConfig != null
            ? await SystemAiConfigurationSnapshotStore.CaptureAsync(_dbContext, coAssessorConfig, ct)
            : null;
        var secondOpinionSnapshot = secondOpinionConfig != null
            ? await SystemAiConfigurationSnapshotStore.CaptureAsync(_dbContext, secondOpinionConfig, ct)
            : null;
        var claimVerifierSnapshot = claimVerifierConfig != null
            ? await SystemAiConfigurationSnapshotStore.CaptureAsync(_dbContext, claimVerifierConfig, ct)
            : null;

        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            BenchmarkSuiteIdUsed = suite.Id,
            SuiteName = suite.Name,
            DefaultSuiteKeyUsed = suite.DefaultSuiteKey,
            DefaultSuiteVersionUsed = suite.DefaultSuiteVersion,
            TestedModelConfigurationId = testedConfig.Id,
            TestedModelSnapshot = testedSnapshot,

            AssessorModelConfigurationId = assessorConfig.Id,
            AssessorModelSnapshot = assessorSnapshot,
            AssessorEffectiveMaxOutputTokens = _benchmarkService.ResolveAssessorOutputCap(assessorSnapshot.MaxOutputTokens),

            CoAssessorModelConfigurationId = coAssessorConfig?.Id,
            CoAssessorModelSnapshot = coAssessorSnapshot,
            CoAssessorEffectiveMaxOutputTokens = coAssessorSnapshot != null
                ? _benchmarkService.ResolveAssessorOutputCap(coAssessorSnapshot.MaxOutputTokens)
                : null,

            SecondOpinionAssessorModelConfigurationId = secondOpinionConfig?.Id,
            SecondOpinionAssessorModelSnapshot = secondOpinionSnapshot,
            SecondOpinionEffectiveMaxOutputTokens = secondOpinionSnapshot != null
                ? _benchmarkService.ResolveAssessorOutputCap(secondOpinionSnapshot.MaxOutputTokens)
                : null,

            ClaimVerifierModelConfigurationId = claimVerifierConfig?.Id,
            ClaimVerifierModelSnapshot = claimVerifierSnapshot,
            ClaimVerifierEffectiveMaxOutputTokens = claimVerifierSnapshot != null
                ? _benchmarkService.ClaimVerifierOutputCap
                : null,

            // Every member of a series is launched from the same request, so each carries the writer.
            ReportWriterModelConfigurationId = validated.ReportWriterConfig?.Id,

            // Left at Off (0) when the operator did not override, so the service stamps the
            // scoring profile's own default at run start.
            SecondOpinionModeUsed = secondOpinionConfig != null && requestedMode.HasValue
                ? requestedMode.Value
                : (int)BenchmarkSecondOpinionMode.Off,

            ScoringProfileId = request.ScoringProfileId,
            StartedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
            Status = BenchmarkRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = suite.Questions.Count,
            PurposeStatementUsed = _complianceGuard.GetPurposeStatement(),
            SameProviderAcknowledged = isSameProvider && request.AcknowledgeSameProvider,
            PricingSnapshotJson = pricingSnapshotJson,

            RunSeriesId = seriesId,
            RunSeriesIndex = seriesIndex
        };

        _dbContext.BenchmarkRuns.Add(run);
        await _dbContext.SaveChangesAsync(ct);

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            // Lost the race between the CurrentRunId check above and here. The row is already
            // written, so mark it failed rather than leaving a Running row nothing will advance.
            run.Status = BenchmarkRunStatus.Failed;
            run.ErrorMessage = "A benchmark run was already in progress when this run was launched.";
            run.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict, "A benchmark run is already in progress.");
        }

        // Deliberately not awaited and deliberately not bound to ct: ct scopes the *launch*, and the
        // run outlives the request that started it. Cancellation goes through the run manager's cts.
        //
        // The run gets a Sentry scope of its own for the same reason it gets its own cancellation:
        // Sentry's scope stack is async-local, so without this the run reports under the launch
        // request's context and collects a tag from every log scope opened beneath it while it runs.
        long runId = run.Id;
        bool verboseMode = request.VerboseMode ?? false;
        // async, not a Task-returning lambda: the scope must be disposed when the run finishes, and
        // a non-async lambda would dispose it the moment RunAsync hit its first await.
        _ = Task.Run(async () =>
        {
            using var sentryScope = SentrySdk.PushScope();
            SentrySdk.ConfigureScope(scope =>
            {
                scope.Clear();
                scope.SetTag("RunId", runId.ToString(CultureInfo.InvariantCulture));
            });

            await _benchmarkService.RunAsync(runId, cts.Token, verboseMode);
        });

        return BenchmarkRunLaunchResult.Ok(run.Id);
    }

    /// <summary>
    /// The rates written here are the <i>resolved</i> card — a scheduled change has already been folded
    /// into them by ResolveDefault. That is why scheduledChange is deliberately not serialised: the
    /// snapshot's job is to record what applied when the run started, so replaying it must never
    /// re-evaluate a date. longContext and serviceTierMultipliers are conditions of the request and
    /// the served tier, not of the calendar, so they do have to survive.
    /// </summary>
    private string? BuildPricingSnapshotJson(
        SystemAiApiConfiguration testedConfig,
        SystemAiApiConfiguration assessorConfig,
        SystemAiApiConfiguration? secondOpinionConfig,
        SystemAiApiConfiguration? claimVerifierConfig,
        SystemAiApiConfiguration? coAssessorConfig)
    {
        if (_modelPricingService == null) return null;

        var candidatePricing = _modelPricingService.Resolve(testedConfig);
        var assessorPricing = _modelPricingService.Resolve(assessorConfig);
        var coAssessorPricing = coAssessorConfig != null ? _modelPricingService.Resolve(coAssessorConfig) : null;
        var secondOpinionPricing = secondOpinionConfig != null ? _modelPricingService.Resolve(secondOpinionConfig) : null;
        var claimVerifierPricing = claimVerifierConfig != null ? _modelPricingService.Resolve(claimVerifierConfig) : null;

        object? ToSnapshotObj(ModelPricing? p) => p == null ? null : new
        {
            inputPerMillion = p.InputPerMillion,
            outputPerMillion = p.OutputPerMillion,
            cachedInputPerMillion = p.CachedInputPerMillion,
            cacheWritePerMillion = p.CacheWritePerMillion,
            source = p.Source == ModelPricingSource.Custom ? "custom" : "catalog",
            asOf = p.AsOf,
            longContext = p.LongContext == null ? null : new
            {
                thresholdInputTokens = p.LongContext.ThresholdInputTokens,
                inputPerMillion = p.LongContext.InputPerMillion,
                outputPerMillion = p.LongContext.OutputPerMillion,
                cachedInputPerMillion = p.LongContext.CachedInputPerMillion,
                cacheWritePerMillion = p.LongContext.CacheWritePerMillion
            },
            serviceTierMultipliers = p.ServiceTierMultipliers
        };

        var capturedAtUtc = DateTime.UtcNow;

        // The coAssessor key is written only in a panel run, so a single-assessor snapshot keeps
        // the shape, and therefore the pricing fingerprint, of every earlier run.
        if (coAssessorConfig == null)
        {
            return JsonSerializer.Serialize(new
            {
                capturedAtUtc,
                candidate = ToSnapshotObj(candidatePricing),
                assessor = ToSnapshotObj(assessorPricing),
                secondOpinion = ToSnapshotObj(secondOpinionPricing),
                claimVerifier = ToSnapshotObj(claimVerifierPricing)
            });
        }

        return JsonSerializer.Serialize(new
        {
            capturedAtUtc,
            candidate = ToSnapshotObj(candidatePricing),
            assessor = ToSnapshotObj(assessorPricing),
            coAssessor = ToSnapshotObj(coAssessorPricing),
            secondOpinion = ToSnapshotObj(secondOpinionPricing),
            claimVerifier = ToSnapshotObj(claimVerifierPricing)
        });
    }
}
