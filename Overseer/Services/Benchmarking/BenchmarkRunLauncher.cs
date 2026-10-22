namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
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

/// <summary>The run a launch replaces, and why it was replaced (<see cref="BenchmarkRun.ReplacesRunId"/>).</summary>
public sealed record BenchmarkRunReplacement(long RunId, BenchmarkReplacementCause Cause);

/// <summary>What <see cref="BenchmarkReplacementCauses.Decide"/> reads from a replaced run.</summary>
/// <param name="Status">The replaced run's status.</param>
/// <param name="Recorded">The replaced run kept a provider ledger (<see cref="BenchmarkRun.ProviderLedgerVersion"/> set).</param>
/// <param name="AnyProviderFailure">A terminal answer of the replaced run failed with origin <see cref="BenchmarkFailureOrigin.Provider"/>.</param>
public sealed record BenchmarkReplacedRunFacts(BenchmarkRunStatus Status, bool Recorded, bool AnyProviderFailure);

/// <summary>
/// Decides <see cref="BenchmarkRun.ReplacementCause"/> from the replaced run, for every orchestrator that
/// launches or attaches a replacement.
/// </summary>
public static class BenchmarkReplacementCauses
{
    /// <summary>
    /// The first that applies: <c>InstrumentChange</c> when the run was replaced under the current
    /// instrument; <c>OperatorRequest</c> when an attach replaced it or it was canceled; <c>ProviderFailure</c>
    /// when a terminal answer failed with provider origin; <c>OurFailure</c> for any other recorded run;
    /// <c>Unknown</c> for a run without a provider ledger, or one no longer stored (<paramref name="replaced"/> null).
    /// </summary>
    public static BenchmarkReplacementCause Decide(
        BenchmarkReplacedRunFacts? replaced, bool instrumentChange = false, bool attached = false)
    {
        if (instrumentChange) return BenchmarkReplacementCause.InstrumentChange;
        if (attached || replaced?.Status == BenchmarkRunStatus.Canceled) return BenchmarkReplacementCause.OperatorRequest;
        if (replaced == null) return BenchmarkReplacementCause.Unknown;
        if (replaced.AnyProviderFailure) return BenchmarkReplacementCause.ProviderFailure;
        return replaced.Recorded ? BenchmarkReplacementCause.OurFailure : BenchmarkReplacementCause.Unknown;
    }

    /// <summary>The facts <see cref="Decide"/> reads, per stored run id; a run no longer stored is absent.</summary>
    public static async Task<IReadOnlyDictionary<long, BenchmarkReplacedRunFacts>> LoadFactsAsync(
        ApplicationDbContext db, IEnumerable<long> runIds, CancellationToken ct = default)
    {
        var ids = runIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, BenchmarkReplacedRunFacts>();

        var rows = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.Status,
                Recorded = r.ProviderLedgerVersion != null,
                AnyProviderFailure = r.Answers.Any(a =>
                    (a.Status == BenchmarkAnswerStatus.ProviderError || a.Status == BenchmarkAnswerStatus.Failed)
                    && a.FailureOrigin == BenchmarkFailureOrigin.Provider)
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => new BenchmarkReplacedRunFacts(r.Status, r.Recorded, r.AnyProviderFailure));
    }

    /// <summary>The replacement of one run, its cause decided from the stored run.</summary>
    public static async Task<BenchmarkRunReplacement> ReplacementAsync(
        ApplicationDbContext db, long replacedRunId, bool instrumentChange = false, bool attached = false, CancellationToken ct = default)
    {
        var facts = await LoadFactsAsync(db, new[] { replacedRunId }, ct);
        return new BenchmarkRunReplacement(
            replacedRunId, Decide(facts.GetValueOrDefault(replacedRunId), instrumentChange, attached));
    }

    /// <summary>The replacement of each run in <paramref name="replacedRunIds"/>, under the same key.</summary>
    public static async Task<IReadOnlyDictionary<TKey, BenchmarkRunReplacement>> ReplacementsAsync<TKey>(
        ApplicationDbContext db, IReadOnlyDictionary<TKey, long> replacedRunIds, bool instrumentChange = false, CancellationToken ct = default)
        where TKey : notnull
    {
        var facts = await LoadFactsAsync(db, replacedRunIds.Values, ct);
        return replacedRunIds.ToDictionary(
            p => p.Key,
            p => new BenchmarkRunReplacement(p.Value, Decide(facts.GetValueOrDefault(p.Value), instrumentChange)));
    }
}

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
    private readonly ModelAvailabilityService _modelAvailability;

    private static readonly Lazy<ModelAvailabilityService> SharedModelAvailability =
        new(() => new ModelAvailabilityService(new ModelMetadataService()));

    /// <summary>
    /// A catalog classifier over the embedded model catalogs, for callers without the registered
    /// <see cref="ModelAvailabilityService"/>.
    /// </summary>
    internal static ModelAvailabilityService DefaultModelAvailability => SharedModelAvailability.Value;

    /// <param name="modelAvailabilityService">Null uses <see cref="DefaultModelAvailability"/>.</param>
    public BenchmarkRunLauncher(
        ApplicationDbContext dbContext,
        BenchmarkService benchmarkService,
        BenchmarkRunManager runManager,
        BenchmarkComplianceGuard complianceGuard,
        EndpointPolicy endpointPolicy,
        ModelPricingService? modelPricingService = null,
        ModelAvailabilityService? modelAvailabilityService = null)
    {
        _dbContext = dbContext;
        _benchmarkService = benchmarkService;
        _runManager = runManager;
        _complianceGuard = complianceGuard;
        _endpointPolicy = endpointPolicy;
        _modelPricingService = modelPricingService;
        _modelAvailability = modelAvailabilityService ?? DefaultModelAvailability;
    }

    /// <summary>
    /// The refusal for a configuration whose model ID the catalog lists as retired, or null. A custom
    /// catalog mode, a custom endpoint and a model the catalog does not know are all admitted.
    /// </summary>
    /// <param name="role">The role label the refusal starts with, such as "Assessor".</param>
    public static string? RetiredModelRefusal(
        string role, SystemAiApiConfiguration config, ModelAvailabilityService modelAvailability)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(modelAvailability);

        var availability = modelAvailability.Evaluate(config.Provider, config.ModelId, config.ModelCatalogMode, config.BaseUrl);
        if (availability.Kind != ModelAvailabilityStatus.Retired)
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(config.DisplayName) ? config.ModelId : config.DisplayName;
        string removed = string.IsNullOrWhiteSpace(availability.RetiredOn)
            ? "which was removed from the model catalog"
            : $"which was removed from the model catalog on {availability.RetiredOn}";
        return $"{role}: '{name}' uses {config.ModelId}, {removed}. "
            + "Switch it to another model or keep it as a custom model in System Configs, then start again.";
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

        if (!BenchmarkInformationConditions.TryParse(request.InformationCondition, out _))
        {
            return (BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Invalid,
                $"Unknown information condition '{request.InformationCondition}'."), null);
        }

        if (!string.IsNullOrWhiteSpace(request.InstructionVariant))
        {
            string variantName = request.InstructionVariant.Trim();
            var variant = _benchmarkService.FindInstructionVariant(variantName);
            if (variant == null)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid, $"Unknown instruction variant '{variantName}'."), null);
            }
            if (!variant.IsValid)
            {
                return (BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Invalid,
                    $"The instruction variant '{variantName}' is invalid: {variant.InvalidReason}"), null);
            }
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

        // Optional: a report writer writes the run's AI-written documents once it completes. It
        // is neither a grader nor a comparability key, and it is never the model under test; a writer
        // of the candidate's provider needs an acknowledgment, asked below.
        SystemAiApiConfiguration? reportWriterConfig = null;
        if (request.ReportWriterModelConfigurationId.HasValue)
        {
            reportWriterConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(new object?[] { request.ReportWriterModelConfigurationId.Value }, ct);

            string? writerRefusal = BenchmarkRunReportDocumentService.WriterRefusal(
                reportWriterConfig, testedConfig, _complianceGuard, _modelAvailability);
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
        // A model the catalog lists as retired is refused after its endpoint check; the report
        // writer's was already refused by WriterRefusal above.
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

            string? retiredRefusal = RetiredModelRefusal(role, config, _modelAvailability);
            if (retiredRefusal != null)
            {
                return (BenchmarkRunLaunchResult.Fail(BenchmarkRunLaunchOutcome.Invalid, retiredRefusal), null);
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
    /// <param name="batteryMember">Set when this run fills a battery slot; saved with the run.</param>
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

    /// <param name="batchOwner">The model batch claim the launch runs under; null outside a batch.</param>
    /// <param name="replacesRunId">The run this launch replaces, stamped as <see cref="BenchmarkRun.ReplacesRunId"/>.</param>
    /// <param name="replacementCause">Why that run was replaced; stamped only with <paramref name="replacesRunId"/>.</param>
    public async Task<BenchmarkRunLaunchResult> CreateAndLaunchRunAsync(
        StartBenchmarkRunRequest request,
        string? userId,
        long? seriesId = null,
        int? seriesIndex = null,
        CancellationToken ct = default,
        BenchmarkBatteryRunMember? batteryMember = null,
        string? batchOwner = null,
        long? replacesRunId = null,
        BenchmarkReplacementCause? replacementCause = null)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict, "A benchmark run is already in progress.");
        }

        if (_runManager.BatchOwner is { } heldBatch && heldBatch != batchOwner)
        {
            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(heldBatch));
        }

        string? orchestratorOwner = OrchestratorOwnerFor(seriesId, batteryMember);
        if (_runManager.OrchestratorOwner is { } claimOwner && claimOwner != orchestratorOwner)
        {
            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(claimOwner));
        }

        // One live member per slot; the filtered unique index that also says so is SQL Server only.
        if (batteryMember != null)
        {
            bool slotOccupied = await _dbContext.BenchmarkBatteryRunMembers.AnyAsync(m =>
                m.BenchmarkBatteryRunId == batteryMember.BenchmarkBatteryRunId
                && m.SuiteIndex == batteryMember.SuiteIndex
                && m.Round == batteryMember.Round
                && !m.Superseded, ct);
            if (slotOccupied)
            {
                return BenchmarkRunLaunchResult.Fail(
                    BenchmarkRunLaunchOutcome.Conflict,
                    $"Suite {batteryMember.SuiteIndex + 1}, round {batteryMember.Round} of battery run " +
                    $"#{batteryMember.BenchmarkBatteryRunId} already has a member.");
            }
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
            RunSeriesIndex = seriesIndex,

            ReplacesRunId = replacesRunId,
            ReplacementCause = replacesRunId.HasValue ? replacementCause ?? BenchmarkReplacementCause.Unknown : null
        };

        _dbContext.BenchmarkRuns.Add(run);

        // Saved with the run, so a run is never visible without its battery membership.
        if (batteryMember != null)
        {
            batteryMember.BenchmarkRun = run;
            _dbContext.BenchmarkBatteryRunMembers.Add(batteryMember);
        }

        await _dbContext.SaveChangesAsync(ct);

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _, orchestratorOwner, batchOwner))
        {
            // Lost the race between the checks above and here. The row is already written, so mark
            // it failed rather than leaving a Running row nothing will advance, and free its slot.
            run.Status = BenchmarkRunStatus.Failed;
            run.ErrorMessage = "A benchmark run was already in progress when this run was launched.";
            run.CompletedAtUtc = DateTime.UtcNow;
            if (batteryMember != null)
            {
                batteryMember.Superseded = true;
            }
            await _dbContext.SaveChangesAsync(ct);

            string? raceBatch = _runManager.BatchOwner;
            string? raceOwner = raceBatch != null && raceBatch != batchOwner ? raceBatch : _runManager.OrchestratorOwner;
            return BenchmarkRunLaunchResult.Fail(
                BenchmarkRunLaunchOutcome.Conflict,
                raceOwner != null && raceOwner != orchestratorOwner && raceOwner != batchOwner
                    ? BenchmarkRunManager.ClaimConflictMessage(raceOwner)
                    : "A benchmark run is already in progress.");
        }

        // Deliberately not awaited and deliberately not bound to ct: ct scopes the *launch*, and the
        // run outlives the request that started it. Cancellation goes through the run manager's cts.
        //
        // The run gets a Sentry scope of its own for the same reason it gets its own cancellation:
        // Sentry's scope stack is async-local, so without this the run reports under the launch
        // request's context and collects a tag from every log scope opened beneath it while it runs.
        long runId = run.Id;
        var switches = ResolvePromptSwitches(request);
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

            await _benchmarkService.RunAsync(
                runId, cts.Token, switches.VerboseMode, switches.AllowSourceCodeReferences,
                switches.InformationCondition, switches.InstructionVariant, switches.WithholdFeedbackTool);
        });

        return BenchmarkRunLaunchResult.Ok(run.Id);
    }

    /// <summary>
    /// The request's candidate prompt switches as a run records them: a null
    /// <see cref="StartBenchmarkRunRequest.VerboseMode"/> is the concise style, a null
    /// <see cref="StartBenchmarkRunRequest.AllowSourceCodeReferences"/> is disallowed, a user's default,
    /// a null information condition is Production, a null variant is production instructions and a
    /// null Feedback Tool setting offers the tool.
    /// </summary>
    internal static BenchmarkPromptSwitches ResolvePromptSwitches(StartBenchmarkRunRequest request)
        => new(
            request.VerboseMode ?? false,
            request.AllowSourceCodeReferences ?? false,
            BenchmarkInformationConditions.FromWire(request.InformationCondition),
            string.IsNullOrWhiteSpace(request.InstructionVariant) ? null : request.InstructionVariant.Trim(),
            request.WithholdFeedbackTool ?? false);

    /// <summary>
    /// The orchestrator claim a launch runs under: the series' for a series member, the battery
    /// run's for a battery member, else none.
    /// </summary>
    internal static string? OrchestratorOwnerFor(long? seriesId, BenchmarkBatteryRunMember? batteryMember)
        => seriesId.HasValue
            ? BenchmarkRunManager.SeriesOwner(seriesId.Value)
            : batteryMember != null
                ? BenchmarkRunManager.BatteryOwner(batteryMember.BenchmarkBatteryRunId)
                : null;

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

/// <summary>The candidate prompt switches of a launch request, as a run records them.</summary>
internal readonly record struct BenchmarkPromptSwitches(
    bool VerboseMode,
    bool AllowSourceCodeReferences,
    BenchmarkInformationCondition InformationCondition,
    string? InstructionVariant,
    bool WithholdFeedbackTool);
