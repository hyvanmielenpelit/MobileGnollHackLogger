namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;

/// <summary>What a report pack is built from: the comparison, the subject's fact sheet and its content snapshot.</summary>
public sealed class BenchmarkReportPackPreparation
{
    public BenchmarkModelComparisonDto Comparison { get; init; } = default!;
    public BenchmarkModelComparisonEntryDto Subject { get; init; } = default!;
    public BenchmarkReportFactSheet Sheet { get; init; } = default!;
    public BenchmarkReportContentSnapshot Content { get; init; } = default!;

    /// <summary>The subject's runs, in run-id order.</summary>
    public IReadOnlyList<BenchmarkRun> SubjectRuns { get; init; } = default!;

    /// <summary>The peers' runs that still exist, in run-id order; never a subject run.</summary>
    public IReadOnlyList<BenchmarkRun> PeerRuns { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>
    /// What the preparation itself recorded, stored with every document it writes: for a battery
    /// subject, the question detail left out to keep the prompt within its budget. Empty for a run or
    /// group subject.
    /// </summary>
    public IReadOnlyList<BenchmarkReportValidationNote> Notes { get; init; } = Array.Empty<BenchmarkReportValidationNote>();

    public const int DefaultAnswerExcerptChars = 600;
    public const int DefaultMaxOutputTokens = 16000;

    public static int AnswerExcerptChars(IConfiguration configuration)
        => Math.Max(0, configuration.GetValue<int?>("Benchmark:ReportPack:AnswerExcerptChars") ?? DefaultAnswerExcerptChars);

    public static int MaxOutputTokens(IConfiguration configuration)
        => Math.Max(1024, configuration.GetValue<int?>("Benchmark:ReportPack:MaxOutputTokens") ?? DefaultMaxOutputTokens);

    /// <summary>The questions per suite a battery prompt gives in full detail at most.</summary>
    public static int BatteryDetailQuestionsPerSuite(IConfiguration? configuration)
        => Math.Max(0, configuration?.GetValue<int?>(BenchmarkBatteryReportFacts.DetailQuestionsPerSuiteKey)
            ?? BenchmarkBatteryReportFacts.DefaultDetailQuestionsPerSuite);

    /// <summary>The characters a battery writer prompt may hold before question detail is left out.</summary>
    public static int BatteryMaxPromptChars(IConfiguration? configuration)
        => Math.Max(0, configuration?.GetValue<int?>(BenchmarkBatteryReportFacts.MaxPromptCharsKey)
            ?? BenchmarkBatteryReportFacts.DefaultMaxPromptChars);

    /// <summary>The subject key of a battery run: <c>battery:&lt;id&gt;</c>.</summary>
    public static string BatterySubjectKeyOf(long batteryRunId) => BenchmarkBatteryModelComparison.KeyOf(batteryRunId);

    /// <summary>How a battery run's job is named where a job is listed: <c>Battery run #9</c>.</summary>
    public static string BatteryJobLabel(long batteryRunId)
        => "Battery run #" + batteryRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The report-pack request a battery run's battery-completion job writes, and its estimate prices:
    /// the battery result alone, as its own subject.
    /// </summary>
    public static BenchmarkReportPackRequest BatteryRequest(long batteryRunId, IEnumerable<BenchmarkReportAudience> audiences, long writerConfigId)
    {
        ArgumentNullException.ThrowIfNull(audiences);
        return new BenchmarkReportPackRequest
        {
            RunIds = new List<long>(),
            GroupIds = new List<long>(),
            BatteryRunIds = new List<long> { batteryRunId },
            SubjectKey = BatterySubjectKeyOf(batteryRunId),
            Audiences = audiences.ToList(),
            WriterModelConfigurationId = writerConfigId
        };
    }

    /// <summary>The request names battery results together with runs or analysis groups, which no comparison holds.</summary>
    public static bool MixesSources(BenchmarkReportPackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (request.BatteryRunIds?.Count ?? 0) > 0
               && ((request.RunIds?.Count ?? 0) > 0 || (request.GroupIds?.Count ?? 0) > 0);
    }

    /// <summary>
    /// The comparison and its subject entry. Refused when the comparison cannot be computed, the
    /// subject is not one of its entries, or the subject is Excluded. Makes no model call.
    /// </summary>
    public static async Task<(BenchmarkModelComparisonDto? Comparison, BenchmarkModelComparisonEntryDto? Subject, string? Refusal)> CompareAsync(
        BenchmarkModelComparisonService comparisonService, BenchmarkReportPackRequest request, CancellationToken ct)
    {
        var (comparison, error) = await comparisonService.CompareAsync(new BenchmarkModelComparisonRequest
        {
            RunIds = request.RunIds ?? new List<long>(),
            GroupIds = request.GroupIds ?? new List<long>(),
            BatteryRunIds = request.BatteryRunIds ?? new List<long>(),
            PricingBasis = request.PricingBasis
        }, ct);
        if (comparison == null)
        {
            return (null, null, error ?? "The comparison could not be computed.");
        }

        var subject = comparison.Entries.FirstOrDefault(e => string.Equals(e.Key, request.SubjectKey, StringComparison.Ordinal));
        if (subject == null)
        {
            return (comparison, null, $"'{request.SubjectKey}' is not an entry of this comparison.");
        }
        if (subject.Excluded)
        {
            return (comparison, subject, $"{subject.Label} is excluded from this comparison and cannot be reported on: {subject.Explanation}");
        }
        return (comparison, subject, null);
    }

    /// <summary>
    /// The comparison, the fact sheet and the content snapshot. Makes no model call. A battery subject
    /// takes its detail cap and prompt budget from <paramref name="configuration"/>, else the defaults.
    /// </summary>
    public static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        CancellationToken ct,
        IConfiguration? configuration = null)
    {
        if (MixesSources(request)) return (null, BenchmarkBatteryModelComparison.MixedSourcesError);

        var (comparison, subject, refusal) = await CompareAsync(comparisonService, request, ct);
        if (refusal != null) return (null, refusal);

        if (string.Equals(subject!.SourceKind, BenchmarkBatteryModelComparison.SourceKind, StringComparison.Ordinal))
        {
            return await PrepareBatteryAsync(db, comparison!, subject, answerExcerptChars,
                BatteryDetailQuestionsPerSuite(configuration), BatteryMaxPromptChars(configuration), ct);
        }

        var runIds = comparison!.Entries
            .Where(e => !e.Excluded)
            .SelectMany(e => e.RunIds)
            .Distinct()
            .ToList();

        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Answers).ThenInclude(a => a.ToolCalls)
            .Where(r => runIds.Contains(r.Id))
            .ToListAsync(ct);
        var runsById = runs.ToDictionary(r => r.Id);

        var facts = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = subject!.Key,
            Runs = runsById
        });
        if (facts.Sheet == null)
        {
            return (null, facts.Refusal ?? "The fact sheet could not be computed.");
        }

        var subjectRuns = subject.RunIds
            .Where(runsById.ContainsKey)
            .OrderBy(id => id)
            .Select(id => runsById[id])
            .ToList();
        if (subjectRuns.Count == 0)
        {
            return (null, "The subject's runs no longer exist.");
        }

        var subjectRunIds = subjectRuns.Select(r => r.Id).ToHashSet();
        var peerRuns = facts.Sheet.Peers
            .SelectMany(p => p.RunIds)
            .Distinct()
            .Where(id => !subjectRunIds.Contains(id) && runsById.ContainsKey(id))
            .OrderBy(id => id)
            .Select(id => runsById[id])
            .ToList();

        return (new BenchmarkReportPackPreparation
        {
            Comparison = comparison,
            Subject = subject,
            Sheet = facts.Sheet,
            Content = BenchmarkReportContent.Build(subjectRuns, answerExcerptChars),
            SubjectRuns = subjectRuns,
            PeerRuns = peerRuns
        }, null);
    }

    /// <summary>
    /// A battery subject: its battery results reloaded with their persisted analyses, the subject's
    /// member runs with their answers, the battery fact sheet and the content of the questions given in
    /// detail. The peers' member runs are loaded without answers, for their fingerprints only.
    /// </summary>
    private static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareBatteryAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonDto comparison,
        BenchmarkModelComparisonEntryDto subject,
        int answerExcerptChars,
        int detailQuestionsPerSuite,
        int maxPromptChars,
        CancellationToken ct)
    {
        var batteryRunIds = comparison.Entries
            .Where(e => e.BatteryRunId.HasValue)
            .Select(e => e.BatteryRunId!.Value)
            .Distinct()
            .ToList();
        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(db, batteryRunIds, ct);
        if (sources == null) return (null, error ?? "The battery results could not be loaded.");

        var subjectRunIds = subject.RunIds.Distinct().ToList();
        var subjectRuns = await db.BenchmarkRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Answers)
            .Where(r => subjectRunIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        if (subjectRuns.Count == 0)
        {
            return (null, "The subject's runs no longer exist.");
        }

        var built = BenchmarkBatteryReportFacts.Build(new BenchmarkBatteryReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = subject.Key,
            Sources = sources,
            Runs = subjectRuns.ToDictionary(r => r.Id),
            AnswerExcerptChars = answerExcerptChars,
            DetailQuestionsPerSuite = detailQuestionsPerSuite,
            MaxPromptChars = maxPromptChars
        });
        if (built.Sheet == null || built.Content == null)
        {
            return (null, built.Refusal ?? "The fact sheet could not be computed.");
        }

        var subjectIds = subjectRuns.Select(r => r.Id).ToHashSet();
        var peerRunIds = built.Sheet.Peers
            .SelectMany(p => p.RunIds)
            .Distinct()
            .Where(id => !subjectIds.Contains(id))
            .ToList();
        var peerRuns = peerRunIds.Count == 0
            ? new List<BenchmarkRun>()
            : await db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => peerRunIds.Contains(r.Id))
                .OrderBy(r => r.Id)
                .ToListAsync(ct);

        return (new BenchmarkReportPackPreparation
        {
            Comparison = comparison,
            Subject = subject,
            Sheet = built.Sheet,
            Content = built.Content,
            SubjectRuns = subjectRuns,
            PeerRuns = peerRuns,
            Notes = built.Notes
        }, null);
    }

    /// <summary>A stand-in configuration carrying only the subject's provider and model id, for the compliance checks.</summary>
    public static SystemAiApiConfiguration SubjectIdentity(BenchmarkModelComparisonEntryDto subject)
        => new() { Provider = subject.Provider, ModelId = subject.ModelId, DisplayName = subject.ModelDisplayName };

    public static string SameProviderWarning(BenchmarkModelComparisonEntryDto subject, SystemAiApiConfiguration writer)
        => $"The report writer ({writer.DisplayName}) belongs to the same provider as the model under report ({subject.Label}, {subject.Provider}). "
            + "A writer from the model's own family may describe it more favorably; a writer from another family is recommended.";

}

/// <summary>Writes a run's run-completion documents; the job's document progress carries the outcome.</summary>
public interface IBenchmarkRunReportWriter
{
    /// <summary>
    /// Writes every document on the job's list, one after another, stored with
    /// <see cref="BenchmarkReportDocumentOrigin.RunCompletion"/>. The job's request names the one run
    /// and its writer; the job must already hold the report-pack slot.
    /// </summary>
    Task WriteRunCompletionDocumentsAsync(BenchmarkReportPackJob job, CancellationToken ct);

    /// <summary>
    /// Writes every document on the job's list about the battery run on its own, one after another,
    /// stored with <see cref="BenchmarkReportDocumentOrigin.BatteryCompletion"/>. The job's request is
    /// replaced by <see cref="BenchmarkReportPackPreparation.BatteryRequest"/> for the battery run, the
    /// job's documents and its writer; the job must already hold the report-pack slot. A writer that
    /// writes run-completion documents only refuses with <see cref="NotSupportedException"/>.
    /// </summary>
    Task WriteBatteryCompletionDocumentsAsync(long batteryRunId, BenchmarkReportPackJob job, CancellationToken ct)
        => Task.FromException(new NotSupportedException("This report writer does not write battery-completion documents."));
}

/// <summary>
/// Writes report-pack documents: one writer call per document, one repair turn when validation
/// fails, then drop-and-notice. The only class of the feature that calls a model; rendering is
/// <see cref="BenchmarkReportRenderService"/>'s and never calls one.
/// </summary>
public class BenchmarkReportPackService : IBenchmarkRunReportWriter
{
    /// <summary>The SystemAiUsageLog.RoleContext value for report-pack writing.</summary>
    public const int UsageRoleContext = 8;

    private readonly ApplicationDbContext _db;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly SystemAiConfigService _configService;
    private readonly CryptoService _cryptoService;
    private readonly EndpointPolicy _endpointPolicy;
    private readonly ModelPricingService _pricingService;
    private readonly BenchmarkModelComparisonService _comparisonService;
    private readonly BenchmarkReportPackJobManager _jobManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkReportPackService> _logger;

    public BenchmarkReportPackService(
        ApplicationDbContext db,
        AgentLoopRunner agentLoopRunner,
        SystemAiConfigService configService,
        CryptoService cryptoService,
        EndpointPolicy endpointPolicy,
        ModelPricingService pricingService,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackJobManager jobManager,
        IConfiguration configuration,
        ILogger<BenchmarkReportPackService> logger)
    {
        _db = db;
        _agentLoopRunner = agentLoopRunner;
        _configService = configService;
        _cryptoService = cryptoService;
        _endpointPolicy = endpointPolicy;
        _pricingService = pricingService;
        _comparisonService = comparisonService;
        _jobManager = jobManager;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>One writer turn: the raw reply, the terminal error, and what it cost.</summary>
    private sealed record WriterTurn(string? FinalText, string? Error, int InputTokens, int OutputTokens, long DurationMs, decimal? CostUsd);

    public async Task RunAsync(string jobId, CancellationToken ct)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return;

        await RunJobAsync(job, BenchmarkReportDocumentOrigin.ReportPack, ct);
    }

    public Task WriteRunCompletionDocumentsAsync(BenchmarkReportPackJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return RunJobAsync(job, BenchmarkReportDocumentOrigin.RunCompletion, ct);
    }

    public Task WriteBatteryCompletionDocumentsAsync(long batteryRunId, BenchmarkReportPackJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        job.Request = BenchmarkReportPackPreparation.BatteryRequest(
            batteryRunId, job.Documents.Select(d => d.Audience), job.WriterConfigId);
        job.SubjectKey = job.Request.SubjectKey;
        if (string.IsNullOrWhiteSpace(job.SubjectLabel))
        {
            job.SubjectLabel = BenchmarkReportPackPreparation.BatteryJobLabel(batteryRunId);
        }
        return RunJobAsync(job, BenchmarkReportDocumentOrigin.BatteryCompletion, ct);
    }

    /// <summary>Prepares the job's subject once, then writes and stores each document with <paramref name="origin"/>.</summary>
    private async Task RunJobAsync(BenchmarkReportPackJob job, BenchmarkReportDocumentOrigin origin, CancellationToken ct)
    {
        string jobId = job.Id;
        try
        {
            ct.ThrowIfCancellationRequested();
            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);

            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(_db, _comparisonService, job.Request, excerptChars, ct, _configuration);
            if (prep == null)
            {
                FailAll(job, refusal ?? "The report pack could not be prepared.");
                return;
            }
            job.AddLog($"Fact sheet computed: {prep.Sheet.Facts.Count} facts, {prep.Sheet.Peers.Count} peers, {prep.Sheet.Questions.Count} questions, {prep.Sheet.Rows.Count} findings.");
            foreach (var note in prep.Notes)
            {
                job.AddLog(note.Message, "warning");
            }

            var liveConfig = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == job.WriterConfigId, ct);
            if (liveConfig == null || !liveConfig.IsEnabled)
            {
                FailAll(job, "Writer model configuration not found or disabled.");
                return;
            }

            // Every document is written with the settings captured when the job started.
            var writerSnapshot = job.WriterSnapshotId > 0
                ? await _db.SystemAiConfigurationSnapshots.FindAsync(new object[] { job.WriterSnapshotId }, ct)
                : null;
            if (!SystemAiConfigurationSnapshotStore.TryBind(liveConfig, writerSnapshot, out var config, out var bindError))
            {
                FailAll(job, bindError ?? SystemAiConfigurationSnapshotStore.MismatchMessage);
                return;
            }

            if (!_endpointPolicy.TryResolveStrict(config!.BaseUrl, config.CustomHeadersJson, config.ApiVersion, out var endpoint, out var endpointError))
            {
                FailAll(job, $"Configuration '{config.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}");
                return;
            }

            string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");
            var pricing = _pricingService.Resolve(config);
            if (pricing == null)
            {
                job.AddLog("No price card resolves for the writer; document costs are not recorded.", "warning");
            }

            int completed = 0;
            int failed = 0;
            foreach (var progress in job.Documents.ToList())
            {
                ct.ThrowIfCancellationRequested();
                bool ok = await WriteDocumentAsync(job, progress.Audience, origin, prep, config, endpoint, apiKey, pricing, excerptChars, maxOutputTokens, ct);
                if (ok) completed++; else failed++;
            }

            job.SetStatus(failed == 0
                ? BenchmarkReportPackJobStatus.Completed
                : completed > 0 ? BenchmarkReportPackJobStatus.CompletedWithErrors : BenchmarkReportPackJobStatus.Failed);
            job.AddLog($"Report pack finished: {completed} document(s) written, {failed} failed.");
        }
        catch (OperationCanceledException)
        {
            foreach (var d in job.Documents.Where(d => d.Status is BenchmarkReportPackDocumentStatus.Pending
                         or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing))
            {
                job.SetDocumentStatus(d.Audience, BenchmarkReportPackDocumentStatus.Canceled);
            }
            job.AddLog("Report pack generation was canceled.", "warning");
            job.SetStatus(BenchmarkReportPackJobStatus.Canceled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report pack job {JobId} failed.", jobId);
            job.AddLog($"Unexpected failure: {ExceptionDetails.DescribeShort(ex)}", "error");
            job.SetStatus(BenchmarkReportPackJobStatus.Failed);
        }
    }

    private static void FailAll(BenchmarkReportPackJob job, string message)
    {
        job.AddLog(message, "error");
        foreach (var d in job.Documents)
        {
            job.SetDocumentStatus(d.Audience, BenchmarkReportPackDocumentStatus.Failed, message);
        }
        job.SetStatus(BenchmarkReportPackJobStatus.Failed);
    }

    /// <summary>Writes, validates, repairs once, drops what still fails, and persists one document. True when stored.</summary>
    private async Task<bool> WriteDocumentAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportAudience audience,
        BenchmarkReportDocumentOrigin origin,
        BenchmarkReportPackPreparation prep,
        SystemAiApiConfiguration config,
        AiEndpointDescriptor endpoint,
        string apiKey,
        ModelPricing? pricing,
        int excerptChars,
        int maxOutputTokens,
        CancellationToken ct)
    {
        string name = BenchmarkReportRenderService.AudienceName(audience);
        job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Writing);
        job.AddLog($"Writing the {name} with {config.DisplayName}...");

        var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
        var runRequest = new AgentRunRequest
        {
            ProviderName = config.Provider,
            ModelId = config.ModelId,
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelDisplayName = config.DisplayName,
            SystemPrompt = prompt.SystemPrompt,
            ThinkingLevel = config.ThinkingLevel,
            ReasoningMode = config.ReasoningMode,
            ReasoningSummary = config.ReasoningSummary,
            ServiceTier = config.ServiceTier,
            MaxOutputTokens = maxOutputTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = config.Id,
            PromptCacheKey = $"benchmark:report-pack:{config.ModelId}",
            CacheConversationTail = false,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt.UserMessage }
            }
        };

        var first = await RunTurnAsync(job, audience, runRequest, config, pricing, ct);
        var turns = new List<WriterTurn> { first };
        if (first.Error != null)
        {
            job.AddLog($"{name}: provider error: {first.Error}", "error");
            job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Failed, first.Error);
            return false;
        }

        var (output, issues) = ParseAndValidate(audience, first.FinalText, prep);
        if (issues.Count > 0)
        {
            job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Repairing);
            job.AddLog($"{name}: {issues.Count} validation issue(s); sending one repair turn.", "warning");

            runRequest.SeedHistory.Add(new { role = "assistant", content = first.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = BenchmarkReportPackPrompt.BuildRepairMessage(issues) });

            var repair = await RunTurnAsync(job, audience, runRequest, config, pricing, ct);
            turns.Add(repair);
            if (repair.Error != null)
            {
                job.AddLog($"{name}: provider error on the repair turn: {repair.Error}", "error");
                if (output == null)
                {
                    job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Failed, repair.Error);
                    return false;
                }
            }
            else
            {
                var (repairedOutput, repairedIssues) = ParseAndValidate(audience, repair.FinalText, prep);
                if (repairedOutput != null)
                {
                    output = repairedOutput;
                    issues = repairedIssues;
                }
            }
        }

        if (output == null)
        {
            string message = issues.FirstOrDefault()?.Message ?? "The writer's reply could not be parsed.";
            job.AddLog($"{name}: {message}", "error");
            job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Failed, message);
            return false;
        }

        var notes = new List<BenchmarkReportValidationNote>(prep.Notes);
        if (issues.Count > 0)
        {
            var cleaned = BenchmarkReportPackValidator.DropInvalid(audience, output, prep.Sheet, prep.Content);
            if (cleaned.Fatal)
            {
                string reason = cleaned.FatalReason ?? "Required content failed validation after the repair turn.";
                job.AddLog($"{name}: {reason}", "error");
                job.SetDocumentStatus(audience, BenchmarkReportPackDocumentStatus.Failed, reason);
                return false;
            }
            output = cleaned.Output;
            notes.AddRange(cleaned.Notes);
            job.AddLog($"{name}: {notes.Count(n => n.Dropped)} item(s) removed by validation.", "warning");
        }

        // A warning note (rules 12, 13 and 14) keeps its text but still marks the document.
        var status = notes.Any(n => n.Dropped || BenchmarkReportPackValidator.IsWarningRule(n.Rule))
            ? BenchmarkReportDocumentStatus.CompletedWithWarnings
            : BenchmarkReportDocumentStatus.Completed;

        var requestRunIds = job.Request.RunIds.OrderBy(id => id).ToList();
        var requestGroupIds = job.Request.GroupIds.OrderBy(id => id).ToList();
        var requestBatteryRunIds = (job.Request.BatteryRunIds ?? new List<long>()).OrderBy(id => id).ToList();
        var document = new BenchmarkReportDocument
        {
            PackId = job.PackId,
            Audience = audience,
            Origin = origin,
            SubjectKey = prep.Subject.Key,
            SubjectLabel = Truncate(prep.Sheet.SubjectLabel, 256),
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(prep.SubjectRuns.Select(r => r.Id).ToList()),
            ComparisonRequestJson = BenchmarkReportJson.Serialize(new BenchmarkModelComparisonRequest
            {
                RunIds = requestRunIds,
                GroupIds = requestGroupIds,
                BatteryRunIds = requestBatteryRunIds,
                PricingBasis = job.Request.PricingBasis
            }),
            ComparisonKey = BenchmarkReportComparisonKey.From(requestRunIds, requestGroupIds, requestBatteryRunIds),
            SuiteId = prep.Sheet.SuiteId,
            SuiteName = Truncate(prep.Sheet.SuiteName, 256),
            WriterConfigId = config.Id,
            WriterModelSnapshotId = job.WriterSnapshotId > 0 ? job.WriterSnapshotId : null,
            WriterDisplayName = Truncate(config.DisplayName ?? config.ModelId, 256),
            WriterProvider = Truncate(config.Provider, 64),
            WriterModelId = Truncate(config.ModelId, 128),
            WriterThinkingLevel = config.ThinkingLevel,
            SameProviderAcknowledged = job.SameProviderAcknowledged,
            ReportFormatVersion = BenchmarkReportPackRenderer.ReportFormatVersion,
            WriterPromptSha256 = BenchmarkReportPackPrompt.PromptSha256(audience),
            AnswerExcerptChars = excerptChars,
            FactsJson = BenchmarkReportJson.Serialize(prep.Sheet),
            ContentJson = BenchmarkReportJson.Serialize(prep.Content),
            WriterOutputJson = BenchmarkReportJson.Serialize(output),
            ValidationNotesJson = BenchmarkReportJson.Serialize(notes),
            Title = Truncate(BenchmarkReportPackRenderer.BuildTitle(audience, prep.Sheet), 512),
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = job.StartedByUserId,
            InputTokens = turns.Sum(t => (long)t.InputTokens),
            OutputTokens = turns.Sum(t => (long)t.OutputTokens),
            DurationMs = turns.Sum(t => t.DurationMs),
            CostUsd = turns.All(t => t.CostUsd != null) ? turns.Sum(t => t.CostUsd!.Value) : null,
            PricingSource = pricing == null ? null : pricing.Source == ModelPricingSource.Custom ? "custom" : "catalog",
            Runs = prep.SubjectRuns.Select(r => Fingerprint(r, isPeer: false))
                .Concat(prep.PeerRuns
                    .Where(r => prep.SubjectRuns.All(s => s.Id != r.Id))
                    .Select(r => Fingerprint(r, isPeer: true)))
                .ToList()
        };

        _db.BenchmarkReportDocuments.Add(document);
        await _db.SaveChangesAsync(CancellationToken.None);

        job.SetDocumentStatus(audience,
            status == BenchmarkReportDocumentStatus.Completed
                ? BenchmarkReportPackDocumentStatus.Completed
                : BenchmarkReportPackDocumentStatus.CompletedWithWarnings,
            documentId: document.Id);
        job.AddLog($"{name} stored as document #{document.Id}.");
        return true;
    }

    /// <summary>The parsed output (null when unparseable) and every validation issue, a parse failure included.</summary>
    private static (BenchmarkReportWriterOutput? Output, IReadOnlyList<BenchmarkReportValidationNote> Issues) ParseAndValidate(
        BenchmarkReportAudience audience, string? rawText, BenchmarkReportPackPreparation prep)
    {
        var parsed = BenchmarkReportPackParser.Parse(rawText);
        if (!parsed.Success || parsed.Output == null)
        {
            return (null, new[]
            {
                new BenchmarkReportValidationNote
                {
                    Rule = 1,
                    Location = "reply",
                    Message = parsed.Error ?? "The reply is not a JSON object."
                }
            });
        }
        return (parsed.Output, BenchmarkReportPackValidator.Validate(audience, parsed.Output, prep.Sheet, prep.Content));
    }

    /// <summary>Sends the request once and records the call's usage under <see cref="UsageRoleContext"/>.</summary>
    private async Task<WriterTurn> RunTurnAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportAudience audience,
        AgentRunRequest runRequest,
        SystemAiApiConfiguration config,
        ModelPricing? pricing,
        CancellationToken ct)
    {
        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, ct))
            {
                if (evt.Type == "error") terminalError = evt.Data?.ToString();
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            if (runResult.TotalPromptTokens > 0 || runResult.OutputTokens > 0)
            {
                await TryRecordUsageAsync(job, audience, config, pricing, runResult, sw.ElapsedMilliseconds);
            }
            throw;
        }
        catch (Exception ex)
        {
            terminalError = ExceptionDetails.DescribeShort(ex);
        }
        sw.Stop();

        var (inputTokens, outputTokens, cost) = await TryRecordUsageAsync(job, audience, config, pricing, runResult, sw.ElapsedMilliseconds);
        return new WriterTurn(runResult.FinalText, terminalError, inputTokens, outputTokens, sw.ElapsedMilliseconds, cost);
    }

    private async Task<(int InputTokens, int OutputTokens, decimal? Cost)> TryRecordUsageAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportAudience audience,
        SystemAiApiConfiguration config,
        ModelPricing? pricing,
        AgentRunResult runResult,
        long durationMs)
    {
        var (inputTokens, outputTokens, _) = BenchmarkDescriptionService.NormalizeTokens(runResult);
        decimal? cost = pricing == null ? null : BenchmarkDescriptionService.ComputeCost(pricing, runResult, config.ServiceTier);
        job.AddUsage(audience, inputTokens, outputTokens, cost);

        try
        {
            await _configService.RecordUsageAsync(
                config.Id,
                job.StartedByUserId,
                inputTokens,
                outputTokens,
                roleContext: UsageRoleContext,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)Math.Min(int.MaxValue, durationMs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording report-pack usage failed.");
            job.AddLog($"Recording usage failed: {ExceptionDetails.DescribeShort(ex)}", "warning");
        }

        return (inputTokens, outputTokens, cost);
    }

    private static BenchmarkReportDocumentRun Fingerprint(BenchmarkRun run, bool isPeer) => new()
    {
        RunId = run.Id,
        IsPeer = isPeer,
        FinalScore = run.FinalScore,
        QualityIndex = run.QualityIndex,
        SpeedIndex = run.SpeedIndex,
        ScoringMethodVersion = run.ScoringMethodVersion,
        RerunCompletedAtUtc = run.RerunCompletedAtUtc,
        SynthesisSha256 = BenchmarkReportRenderService.SynthesisSha256(run.AssessmentJson, run.CoAssessorSynthesisJson)
    };

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
