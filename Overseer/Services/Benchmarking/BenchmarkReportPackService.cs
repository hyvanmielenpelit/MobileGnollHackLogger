namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking.RootCause;
using Overseer.Services.ChatConsistency;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

/// <summary>
/// The milliseconds of a comparison-scope preparation's stages
/// (<see cref="BenchmarkReportPackPreparation.PrepareComparisonAsync"/>): the paired tests, the runs or
/// battery results loaded with their answers, the comparison sheet built from them, and the prompt
/// builds that count each document's length, which the preview fills in.
/// </summary>
public sealed class BenchmarkReportPackPrepareTimings
{
    public long PairedMs { get; set; }
    public long LoadMs { get; set; }
    public long FactsMs { get; set; }
    public long PromptMs { get; set; }
}

/// <summary>
/// What a report pack is built from: the comparison, the fact sheet and its content snapshot. For
/// model scope the sheet is the subject's; for comparison scope (<see cref="Scope"/>) it describes every
/// covered model, and <see cref="Subject"/> is the covered model lettered A.
/// </summary>
public sealed class BenchmarkReportPackPreparation
{
    public BenchmarkModelComparisonDto Comparison { get; init; } = default!;
    public BenchmarkModelComparisonEntryDto Subject { get; init; } = default!;
    public BenchmarkReportFactSheet Sheet { get; init; } = default!;
    public BenchmarkReportContentSnapshot Content { get; init; } = default!;

    /// <summary>Per-model documents, or comparison-scope documents over <see cref="CoveredEntryKeys"/>.</summary>
    public BenchmarkReportScope Scope { get; init; } = BenchmarkReportScope.Model;

    /// <summary>The covered models of a comparison-scope preparation, in letter order; the subject alone for model scope.</summary>
    public IReadOnlyList<BenchmarkModelComparisonEntryDto> Covered { get; init; } = Array.Empty<BenchmarkModelComparisonEntryDto>();

    /// <summary>The covered entry keys, canonically sorted; the subject's key alone for model scope.</summary>
    public IReadOnlyList<string> CoveredEntryKeys { get; init; } = Array.Empty<string>();

    /// <summary><see cref="BenchmarkReportComparisonKey.ForCoveredSet"/> of <see cref="CoveredEntryKeys"/>.</summary>
    public string CoveredSetKey { get; init; } = string.Empty;

    /// <summary>A comparison-scope preparation covers every entry of the comparison that is not Excluded.</summary>
    public bool CoversAllEntries { get; init; }

    /// <summary>The comparison's entries that are not Excluded.</summary>
    public int ComparisonEntryCount { get; init; }

    /// <summary>
    /// A comparison-scope preparation's reference model, by entry key: the requested one, else the covered
    /// model with the highest Intelligence Index; null for model scope.
    /// </summary>
    public string? ReferenceEntryKey { get; init; }

    /// <summary>The subject's runs, in run-id order; for comparison scope, every covered model's runs.</summary>
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
    public const int DefaultMaxOutputTokens = 32000;

    /// <summary>Why the Report Pack refuses a subject with no peer: its documents compare models.</summary>
    public const string PeerlessReportRefusal =
        "A comparison report compares one model with at least one other. To write a run's or a battery run's own reports, use the AI Reports tab of its report.";

    /// <summary>
    /// True when the comparison holds another entry that is not Excluded, the entries the fact sheet
    /// takes as the subject's peers. Checked by the Report Pack endpoints only: a run's and a battery
    /// run's own documents are written from a one-entry comparison.
    /// </summary>
    public static bool HasPeers(BenchmarkModelComparisonDto comparison, BenchmarkModelComparisonEntryDto subject)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(subject);
        return comparison.Entries.Any(e => !e.Excluded && !string.Equals(e.Key, subject.Key, StringComparison.Ordinal));
    }

    public static int AnswerExcerptChars(IConfiguration configuration)
        => Math.Max(0, configuration.GetValue<int?>("Benchmark:ReportPack:AnswerExcerptChars") ?? DefaultAnswerExcerptChars);

    public static int MaxOutputTokens(IConfiguration configuration)
        => Math.Max(1024, configuration.GetValue<int?>("Benchmark:ReportPack:MaxOutputTokens") ?? DefaultMaxOutputTokens);

    /// <summary>
    /// The output token limit a writer call is sent, reasoning included: <see cref="MaxOutputTokens"/>, lowered to
    /// the writer model's catalog limit when the catalog knows one.
    /// </summary>
    public static int WriterMaxOutputTokens(IConfiguration configuration, string provider, string modelId)
    {
        int cap = MaxOutputTokens(configuration);
        int catalogLimit = PricingMetadata.Value.GetMetadata(provider, modelId).MaxOutputTokens;
        return catalogLimit > 0 ? Math.Min(cap, catalogLimit) : cap;
    }

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
    /// the battery result alone, as its own subject, replacing the battery-completion documents of
    /// <paramref name="replaceDocumentIds"/> once their successors are stored.
    /// </summary>
    public static BenchmarkReportPackRequest BatteryRequest(long batteryRunId, IEnumerable<BenchmarkReportAudience> audiences, long writerConfigId,
        IEnumerable<long>? replaceDocumentIds = null)
    {
        ArgumentNullException.ThrowIfNull(audiences);
        var replace = replaceDocumentIds?.Distinct().ToList();
        return new BenchmarkReportPackRequest
        {
            RunIds = new List<long>(),
            GroupIds = new List<long>(),
            BatteryRunIds = new List<long> { batteryRunId },
            SubjectKey = BatterySubjectKeyOf(batteryRunId),
            Audiences = audiences.ToList(),
            WriterModelConfigurationId = writerConfigId,
            ReplaceDocumentIds = replace is { Count: > 0 } ? replace : null
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
    public static Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        CancellationToken ct,
        IConfiguration? configuration = null)
        => PrepareAsync(db, comparisonService, request, answerExcerptChars, ct, configuration, comparison: null);

    /// <summary>
    /// As <see cref="PrepareAsync(ApplicationDbContext, BenchmarkModelComparisonService, BenchmarkReportPackRequest, int, CancellationToken, IConfiguration?)"/>,
    /// over <paramref name="comparison"/> when it is given, so the subjects of one job share one
    /// computation of the comparison; computed from the request otherwise.
    /// </summary>
    public static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        CancellationToken ct,
        IConfiguration? configuration,
        BenchmarkModelComparisonDto? comparison)
    {
        if (MixesSources(request)) return (null, BenchmarkBatteryModelComparison.MixedSourcesError);

        BenchmarkModelComparisonEntryDto? subject;
        string? refusal;
        if (comparison == null)
        {
            (comparison, subject, refusal) = await CompareAsync(comparisonService, request, ct);
        }
        else
        {
            (subject, refusal) = SubjectOf(comparison, request.SubjectKey);
        }
        if (refusal != null) return (null, refusal);

        var (prep, prepRefusal) = string.Equals(subject!.SourceKind, BenchmarkBatteryModelComparison.SourceKind, StringComparison.Ordinal)
            ? await PrepareBatteryAsync(db, comparison!, subject, answerExcerptChars,
                BatteryDetailQuestionsPerSuite(configuration), BatteryMaxPromptChars(configuration), ct, configuration)
            : await PrepareRunsAsync(db, comparison!, subject, answerExcerptChars, ct, configuration);
        if (prep == null) return (null, prepRefusal);

        var covered = new[] { subject.Key };
        return (new BenchmarkReportPackPreparation
        {
            Comparison = prep.Comparison,
            Subject = prep.Subject,
            Sheet = prep.Sheet,
            Content = prep.Content,
            SubjectRuns = prep.SubjectRuns,
            PeerRuns = prep.PeerRuns,
            Notes = prep.Notes,
            Scope = BenchmarkReportScope.Model,
            Covered = new[] { subject },
            CoveredEntryKeys = covered,
            CoveredSetKey = BenchmarkReportComparisonKey.ForCoveredSet(covered),
            ComparisonEntryCount = comparison!.Entries.Count(e => !e.Excluded)
        }, null);
    }

    /// <summary>The subject entry of a computed comparison, refused as <see cref="CompareAsync"/> refuses it.</summary>
    public static (BenchmarkModelComparisonEntryDto? Subject, string? Refusal) SubjectOf(BenchmarkModelComparisonDto comparison, string? subjectKey)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var subject = comparison.Entries.FirstOrDefault(e => string.Equals(e.Key, subjectKey, StringComparison.Ordinal));
        if (subject == null) return (null, $"'{subjectKey}' is not an entry of this comparison.");
        if (subject.Excluded)
        {
            return (subject, $"{subject.Label} is excluded from this comparison and cannot be reported on: {subject.Explanation}");
        }
        return (subject, null);
    }

    /// <summary>
    /// A run or group subject: the comparison's runs loaded with their answers, tool calls and candidate
    /// telemetry, their price cards on the comparison's basis, the fact sheet and the content. A run subject
    /// without peers (a run-completion document) also gets its root-cause controls.
    /// </summary>
    private static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareRunsAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonDto comparison,
        BenchmarkModelComparisonEntryDto subject,
        int answerExcerptChars,
        CancellationToken ct,
        IConfiguration? configuration = null)
    {
        var runIds = comparison.Entries
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
        await AttachCandidateTelemetryAsync(db, runs, ct);
        var runsById = runs.ToDictionary(r => r.Id);

        bool runCompletion = !HasPeers(comparison, subject!)
            && string.Equals(subject.SourceKind, "Run", StringComparison.Ordinal);
        var facts = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = subject.Key,
            Runs = runsById,
            Availability = await AvailabilityOfAsync(new BenchmarkProviderAvailabilityService(db), subject, ct),
            CandidatePricing = await CandidatePricingAsync(db, runs, comparison),
            Controls = runCompletion ? await RootCauseControls.FindAsync(db, subject.Key, ct) : null,
            Catalog = await RootCauseNextRuns.LoadCatalogAsync(db, configuration, ct)
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
    /// member runs with their answers, tool calls and candidate telemetry and their price cards on the
    /// comparison's basis, the battery fact sheet and the content of the questions given in detail. The
    /// peers' member runs are loaded without answers, for their fingerprints only. A battery subject without
    /// peers (a battery-completion document) also gets its root-cause controls.
    /// </summary>
    private static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareBatteryAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonDto comparison,
        BenchmarkModelComparisonEntryDto subject,
        int answerExcerptChars,
        int detailQuestionsPerSuite,
        int maxPromptChars,
        CancellationToken ct,
        IConfiguration? configuration = null)
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
            .Include(r => r.Answers).ThenInclude(a => a.ToolCalls)
            .Where(r => subjectRunIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        if (subjectRuns.Count == 0)
        {
            return (null, "The subject's runs no longer exist.");
        }
        await AttachCandidateTelemetryAsync(db, subjectRuns, ct);

        bool batteryCompletion = !HasPeers(comparison, subject);
        var batteryIds = await BatteryIdsAsync(db, subject.BatteryRunId.HasValue ? new[] { subject.BatteryRunId.Value } : Array.Empty<long>(), ct);
        var built = BenchmarkBatteryReportFacts.Build(new BenchmarkBatteryReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = subject.Key,
            Sources = sources,
            Runs = subjectRuns.ToDictionary(r => r.Id),
            AnswerOutcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, subjectRunIds, withRefutedSentences: false, ct),
            AnswerExcerptChars = answerExcerptChars,
            DetailQuestionsPerSuite = detailQuestionsPerSuite,
            MaxPromptChars = maxPromptChars,
            Availability = await AvailabilityOfAsync(new BenchmarkProviderAvailabilityService(db), subject, ct),
            CandidatePricing = await CandidatePricingAsync(db, subjectRuns, comparison),
            Controls = batteryCompletion ? await RootCauseControls.FindAsync(db, subject.Key, ct) : null,
            Catalog = await RootCauseNextRuns.LoadCatalogAsync(db, configuration, ct),
            BatteryId = subject.BatteryRunId is long batteryRunId ? batteryIds.GetValueOrDefault(batteryRunId) : null
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

    /// <summary>
    /// A comparison-scope preparation over the request's covered entries, or every entry that is not
    /// Excluded when it names none: each covered model's per-model sheet and content over the covered
    /// models only, the paired-test families requested for the covered models only, so each Holm
    /// adjustment counts the tests the document reports, each model's failure profile over its runs'
    /// answers, and the comparison sheet built from them (<see cref="BenchmarkComparisonReportFacts.Build"/>)
    /// against the request's reference model, the highest-Index one when it names none. Refused when a
    /// covered entry is not in the comparison or is Excluded, the covered models are fewer than two or
    /// more than twelve, or the reference is not a covered model
    /// (<see cref="BenchmarkComparisonReportFacts.ReferenceRefusal"/>). Over <paramref name="comparison"/>
    /// when it is given; computed from the request otherwise. <paramref name="releaseDate"/> gives a model's
    /// catalog release date (provider, model id → ISO date or null); without it the release prior is not
    /// evaluated. <paramref name="timings"/>, when given, receives the paired-test, loading and fact-sheet
    /// milliseconds. Makes no model call.
    /// </summary>
    public static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareComparisonAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        int? comparisonId,
        CancellationToken ct,
        IConfiguration? configuration = null,
        BenchmarkPairedTestsService? pairedTests = null,
        BenchmarkModelComparisonDto? comparison = null,
        Func<string, string, string?>? releaseDate = null,
        BenchmarkReportPackPrepareTimings? timings = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (MixesSources(request)) return (null, BenchmarkBatteryModelComparison.MixedSourcesError);

        if (comparison == null)
        {
            var (computed, error) = await comparisonService.CompareAsync(ComparisonRequest(request), ct);
            if (computed == null) return (null, error ?? "The comparison could not be computed.");
            comparison = computed;
        }

        var covered = BenchmarkComparisonReportFacts.CoveredKeysOf(comparison, request.CoveredEntryKeys);
        string? refusal = BenchmarkComparisonReportFacts.CoveredRefusal(comparison, covered)
            ?? BenchmarkComparisonReportFacts.BoundsRefusal(covered.Count)
            ?? BenchmarkComparisonReportFacts.ReferenceRefusalOf(comparison, covered, request.ReferenceEntryKey);
        if (refusal != null) return (null, refusal);

        var restricted = BenchmarkComparisonReportFacts.Restrict(comparison, covered);
        string referenceKey = string.IsNullOrWhiteSpace(request.ReferenceEntryKey)
            ? BenchmarkComparisonReportFacts.InLetterOrder(restricted.Entries)[0].Key
            : request.ReferenceEntryKey.Trim();
        var stage = Stopwatch.StartNew();
        var (reference, allPairs, pairedUnavailable) = await PairedFamiliesAsync(
            pairedTests ?? new BenchmarkPairedTestsService(db, comparisonService), restricted, request.PricingBasis, referenceKey, ct);
        if (timings != null) timings.PairedMs = stage.ElapsedMilliseconds;

        stage.Restart();
        bool batteries = string.Equals(comparison.SubjectKind, BenchmarkModelComparisonSubjectKinds.Batteries, StringComparison.Ordinal);
        var catalog = await RootCauseNextRuns.LoadCatalogAsync(db, configuration, ct);
        var releaseDates = ReleaseDatesOf(restricted.Entries, releaseDate);
        BenchmarkComparisonReportFactsResult built;
        List<BenchmarkRun> runs;
        if (batteries)
        {
            var (entries, batteryRuns, batteryRefusal, pricing) = await BatteryEntriesAsync(db, restricted, answerExcerptChars, BatteryDetailQuestionsPerSuite(configuration), ct);
            if (timings != null) timings.LoadMs = stage.ElapsedMilliseconds;
            if (entries == null) return (null, batteryRefusal);
            runs = batteryRuns;
            stage.Restart();
            built = BenchmarkComparisonReportFacts.Build(new BenchmarkComparisonReportFactsInput
            {
                Comparison = comparison,
                Entries = entries,
                ReferenceFamily = reference,
                ReferenceKey = referenceKey,
                AllPairsFamily = allPairs,
                PairedTestsUnavailableReason = pairedUnavailable,
                AnswerExcerptChars = answerExcerptChars,
                ComparisonId = comparisonId,
                CandidatePricing = pricing,
                Catalog = catalog,
                ReleaseDates = releaseDates
            });
            if (timings != null) timings.FactsMs = stage.ElapsedMilliseconds;
        }
        else
        {
            var runIds = restricted.Entries.SelectMany(e => e.RunIds).Distinct().ToList();
            runs = await db.BenchmarkRuns
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.Answers).ThenInclude(a => a.ToolCalls)
                .Where(r => runIds.Contains(r.Id))
                .OrderBy(r => r.Id)
                .ToListAsync(ct);
            await AttachCandidateTelemetryAsync(db, runs, ct);
            var availabilityService = new BenchmarkProviderAvailabilityService(db);
            var availability = new Dictionary<string, ProviderAvailabilitySummary>(StringComparer.Ordinal);
            foreach (var entry in restricted.Entries)
            {
                availability[entry.Key] = await AvailabilityOfAsync(availabilityService, entry, ct);
            }
            var candidatePricing = await CandidatePricingAsync(db, runs, comparison);
            if (timings != null) timings.LoadMs = stage.ElapsedMilliseconds;
            stage.Restart();
            built = BenchmarkComparisonReportFacts.BuildFromRuns(
                comparison, covered, runs.ToDictionary(r => r.Id), answerExcerptChars, reference, allPairs, pairedUnavailable, comparisonId, availability,
                referenceKey, candidatePricing, catalog, releaseDates);
            if (timings != null) timings.FactsMs = stage.ElapsedMilliseconds;
        }
        if (built.Sheet == null || built.Content == null) return (null, built.Refusal ?? "The fact sheet could not be computed.");
        if (runs.Count == 0) return (null, "The covered models' runs no longer exist.");

        var lettered = BenchmarkComparisonReportFacts.InLetterOrder(restricted.Entries);
        return (new BenchmarkReportPackPreparation
        {
            Comparison = comparison,
            Subject = lettered[0],
            Sheet = built.Sheet,
            Content = built.Content,
            Scope = BenchmarkReportScope.Comparison,
            Covered = lettered,
            CoveredEntryKeys = built.CoveredEntryKeys,
            CoveredSetKey = built.CoveredSetKey ?? string.Empty,
            CoversAllEntries = built.Sheet.CoversAllEntries == true,
            ComparisonEntryCount = built.Sheet.ComparisonEntryCount ?? comparison.Entries.Count(e => !e.Excluded),
            ReferenceEntryKey = referenceKey,
            SubjectRuns = runs,
            PeerRuns = Array.Empty<BenchmarkRun>(),
            Notes = built.Notes
        }, null);
    }

    /// <summary>
    /// The known release date of each entry's model, keyed by <see cref="RootCauseEvidence.ReleaseDateKey"/>;
    /// null without a lookup. Blank dates are left out.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? ReleaseDatesOf(
        IEnumerable<BenchmarkModelComparisonEntryDto> entries, Func<string, string, string?>? releaseDate)
    {
        if (releaseDate == null) return null;
        var dates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Provider) || string.IsNullOrWhiteSpace(entry.ModelId)) continue;
            string? date = releaseDate(entry.Provider, entry.ModelId);
            if (!string.IsNullOrWhiteSpace(date)) dates[RootCauseEvidence.ReleaseDateKey(entry.Provider, entry.ModelId)] = date.Trim();
        }
        return dates;
    }

    /// <summary>
    /// The preparation a request's documents are written from, with no model call: the covered set of a
    /// comparison-scope request (<see cref="PrepareComparisonAsync"/>), else the first subject of a
    /// model-scope request. What a preview, an estimate or a layout preview renders from.
    /// </summary>
    public static Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareForRequestAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        int? comparisonId,
        CancellationToken ct,
        IConfiguration? configuration = null,
        BenchmarkPairedTestsService? pairedTests = null,
        Func<string, string, string?>? releaseDate = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope == BenchmarkReportScope.Comparison)
        {
            return PrepareComparisonAsync(db, comparisonService, request, answerExcerptChars, comparisonId, ct, configuration, pairedTests,
                releaseDate: releaseDate);
        }

        var subjects = request.ModelSubjectKeys();
        var first = new BenchmarkReportPackRequest
        {
            RunIds = request.RunIds ?? new List<long>(),
            GroupIds = request.GroupIds ?? new List<long>(),
            BatteryRunIds = request.BatteryRunIds ?? new List<long>(),
            PricingBasis = request.PricingBasis,
            SubjectKey = subjects.Count > 0 ? subjects[0] : request.SubjectKey ?? string.Empty,
            Audiences = request.Audiences ?? new List<BenchmarkReportAudience>(),
            WriterModelConfigurationId = request.WriterModelConfigurationId
        };
        return PrepareAsync(db, comparisonService, first, answerExcerptChars, ct, configuration);
    }

    /// <summary>The comparison request of a report-pack request's sources.</summary>
    public static BenchmarkModelComparisonRequest ComparisonRequest(BenchmarkReportPackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new BenchmarkModelComparisonRequest
        {
            RunIds = request.RunIds ?? new List<long>(),
            GroupIds = request.GroupIds ?? new List<long>(),
            BatteryRunIds = request.BatteryRunIds ?? new List<long>(),
            PricingBasis = request.PricingBasis
        };
    }

    /// <summary>
    /// Each covered battery result's own battery sheet and content over the covered results: the
    /// battery analyses loaded once, the members' answers, tool calls and candidate telemetry loaded once
    /// with their price cards on the comparison's basis, each result's tool-call outcomes, and its failure
    /// profile over its member runs' answers. The prompt budget is applied over the whole comparison, so
    /// none is applied here.
    /// </summary>
    private static async Task<(List<BenchmarkComparisonReportEntry>? Entries, List<BenchmarkRun> Runs, string? Refusal, IReadOnlyDictionary<long, ModelPricing?>? Pricing)> BatteryEntriesAsync(
        ApplicationDbContext db, BenchmarkModelComparisonDto restricted, int answerExcerptChars, int detailQuestionsPerSuite, CancellationToken ct)
    {
        var batteryRunIds = restricted.Entries.Where(e => e.BatteryRunId.HasValue).Select(e => e.BatteryRunId!.Value).Distinct().ToList();
        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(db, batteryRunIds, ct);
        if (sources == null) return (null, new List<BenchmarkRun>(), error ?? "The battery results could not be loaded.", null);

        var runIds = restricted.Entries.SelectMany(e => e.RunIds).Distinct().ToList();
        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Answers).ThenInclude(a => a.ToolCalls)
            .Where(r => runIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        await AttachCandidateTelemetryAsync(db, runs, ct);
        var runsById = runs.ToDictionary(r => r.Id);
        var pricing = await CandidatePricingAsync(db, runs, restricted);
        var batteryIds = await BatteryIdsAsync(db, batteryRunIds, ct);

        var availabilityService = new BenchmarkProviderAvailabilityService(db);
        var entries = new List<BenchmarkComparisonReportEntry>();
        foreach (var entry in restricted.Entries)
        {
            var memberIds = entry.RunIds.Distinct().ToList();
            var built = BenchmarkBatteryReportFacts.Build(new BenchmarkBatteryReportFactsInput
            {
                Comparison = restricted,
                SubjectKey = entry.Key,
                Sources = sources,
                Runs = memberIds.Where(runsById.ContainsKey).ToDictionary(id => id, id => runsById[id]),
                AnswerOutcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, memberIds, withRefutedSentences: false, ct),
                AnswerExcerptChars = answerExcerptChars,
                DetailQuestionsPerSuite = detailQuestionsPerSuite,
                MaxPromptChars = 0,
                Availability = await AvailabilityOfAsync(availabilityService, entry, ct),
                CandidatePricing = pricing,
                IncludeRootCause = false
            });
            if (built.Sheet == null || built.Content == null)
            {
                return (null, runs, entry.Label + ": " + (built.Refusal ?? "The fact sheet could not be computed."), pricing);
            }

            var memberRuns = memberIds.Where(runsById.ContainsKey).OrderBy(id => id).Select(id => runsById[id]).ToList();
            var source = sources.FirstOrDefault(s => string.Equals(s.Key, entry.Key, StringComparison.Ordinal));
            var suiteOfRun = new Dictionary<long, int>();
            foreach (var suite in source?.Suites ?? Array.Empty<BenchmarkBatteryComparisonSourceSuite>())
            {
                foreach (var run in suite.Runs) suiteOfRun.TryAdd(run.Id, suite.SuiteIndex + 1);
            }
            entries.Add(new BenchmarkComparisonReportEntry
            {
                Entry = entry,
                Sheet = built.Sheet,
                Content = built.Content,
                Profile = BenchmarkComparisonModelProfile.FromRuns(memberRuns),
                Runs = memberRuns,
                SuiteOfRun = suiteOfRun,
                BatteryId = entry.BatteryRunId is long batteryRunId ? batteryIds.GetValueOrDefault(batteryRunId) : null
            });
        }

        return (entries, runs, null, pricing);
    }

    /// <summary>
    /// Attaches each answer's candidate <see cref="ModelCallTelemetry"/> rows to <see cref="BenchmarkRunAnswer.ModelCalls"/>,
    /// loaded as a projection of the columns the root-cause evidence reads, never whole rows and never a
    /// grader's calls. An answer without rows gets an empty list.
    /// </summary>
    private static async Task AttachCandidateTelemetryAsync(ApplicationDbContext db, IReadOnlyCollection<BenchmarkRun> runs, CancellationToken ct)
    {
        var runIds = runs.Select(r => r.Id).Distinct().ToList();
        if (runIds.Count == 0) return;

        var rows = await db.ModelCallTelemetry
            .AsNoTracking()
            .Where(c => c.Source == ModelCallSource.BenchmarkCandidate
                        && c.BenchmarkRunAnswerId != null
                        && runIds.Contains(c.BenchmarkRunAnswer!.BenchmarkRunId))
            .Select(c => new ModelCallTelemetry
            {
                Id = c.Id,
                Source = c.Source,
                BenchmarkRunAnswerId = c.BenchmarkRunAnswerId,
                Provider = c.Provider,
                RequestedModelId = c.RequestedModelId,
                ServiceTierRequested = c.ServiceTierRequested,
                ServedServiceTier = c.ServedServiceTier,
                IsRefusal = c.IsRefusal,
                StartedAtUtc = c.StartedAtUtc,
                CallIndex = c.CallIndex,
                PermitWaitMs = c.PermitWaitMs,
                BackoffWaitMs = c.BackoffWaitMs,
                FailedAttemptMs = c.FailedAttemptMs,
                AttemptCount = c.AttemptCount,
                Http429Count = c.Http429Count,
                Http5xxCount = c.Http5xxCount,
                FirstEventMs = c.FirstEventMs,
                FirstReasoningMs = c.FirstReasoningMs,
                FirstOutputMs = c.FirstOutputMs,
                LastDeltaMs = c.LastDeltaMs,
                CompletedMs = c.CompletedMs,
                StreamEndMs = c.StreamEndMs,
                VisibleOutputChars = c.VisibleOutputChars,
                Last80DecodeSpanMs = c.Last80DecodeSpanMs,
                Last80VisibleChars = c.Last80VisibleChars,
                InputTokens = c.InputTokens,
                CachedInputTokens = c.CachedInputTokens,
                CacheWriteTokens = c.CacheWriteTokens,
                OutputTokens = c.OutputTokens,
                ReasoningTokens = c.ReasoningTokens
            })
            .ToListAsync(ct);

        var byAnswer = rows
            .GroupBy(r => r.BenchmarkRunAnswerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.CallIndex).ThenBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList());
        foreach (var answer in runs.SelectMany(r => r.Answers ?? new List<BenchmarkRunAnswer>()))
        {
            answer.ModelCalls = byAnswer.TryGetValue(answer.Id, out var calls) ? calls : new List<ModelCallTelemetry>();
        }
    }

    /// <summary>The model catalog the price cards of the Current basis are resolved from, loaded once.</summary>
    private static readonly Lazy<ModelMetadataService> PricingMetadata = new(() => new ModelMetadataService());

    /// <summary>
    /// Each run's candidate price card on the comparison's pricing basis, as the comparison resolves it:
    /// today's catalog card for the Current basis, the run's stored card (else its live configuration) for
    /// As run. A card that cannot be resolved is null, which leaves that model's cost decomposition unavailable.
    /// </summary>
    private static async Task<IReadOnlyDictionary<long, ModelPricing?>> CandidatePricingAsync(
        ApplicationDbContext db, IEnumerable<BenchmarkRun> runs, BenchmarkModelComparisonDto comparison)
    {
        var cards = new Dictionary<long, ModelPricing?>();
        var pricing = new ModelPricingService(PricingMetadata.Value, db);
        bool current = BenchmarkReportFacts.PricingBasisKindOf(comparison) == BenchmarkReportFacts.PricingBasisCatalog;
        var today = DateOnly.FromDateTime(comparison.ComputedAtUtc == default ? DateTime.UtcNow : comparison.ComputedAtUtc);

        foreach (var run in runs)
        {
            if (cards.ContainsKey(run.Id)) continue;
            try
            {
                cards[run.Id] = current
                    ? pricing.ResolveDefault(run.TestedModelSnapshot?.Provider, run.TestedModelSnapshot?.ModelId, today)
                    : (await pricing.ResolveForRunAsync(run)).Candidate;
            }
            catch (Exception)
            {
                // An unresolvable card costs this run's cost decomposition, never the preparation.
                cards[run.Id] = null;
            }
        }
        return cards;
    }

    /// <summary>The battery each battery run ran (<see cref="BenchmarkBatteryRun.BenchmarkBatteryId"/>), by battery run id.</summary>
    private static async Task<Dictionary<long, long?>> BatteryIdsAsync(ApplicationDbContext db, IReadOnlyCollection<long> batteryRunIds, CancellationToken ct)
    {
        if (batteryRunIds.Count == 0) return new Dictionary<long, long?>();
        var ids = batteryRunIds.ToList();
        return await db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.BenchmarkBatteryId })
            .ToDictionaryAsync(b => b.Id, b => b.BenchmarkBatteryId, ct);
    }

    /// <summary>
    /// The paired-test families of the covered models, from the service the wizard's Paired tests view
    /// uses, requested for the covered models only: against the reference <paramref name="referenceKey"/>,
    /// and over all pairs from three to <see cref="BenchmarkComparisonReportFacts.AllPairsMaxEntries"/>
    /// models. Both are computed over <paramref name="restricted"/>, so the comparison is not computed
    /// again. A refusal or a failure is the reason the sheet states instead.
    /// </summary>
    private static async Task<(BenchmarkPairedComparisonDto? Reference, BenchmarkPairedComparisonDto? AllPairs, string? Unavailable)> PairedFamiliesAsync(
        BenchmarkPairedTestsService pairedTests, BenchmarkModelComparisonDto restricted, BenchmarkModelComparisonPricingBasis basis,
        string referenceKey, CancellationToken ct)
    {
        var lettered = BenchmarkComparisonReportFacts.InLetterOrder(restricted.Entries.Where(e => !e.Excluded));
        if (lettered.Count < 2) return (null, null, BenchmarkPairedTests.NeedsTwoEntriesError);

        var keys = lettered.Select(e => e.Key).ToList();
        var request = new BenchmarkPairedComparisonRequest
        {
            RunIds = IdsOf(keys, "run:"),
            GroupIds = IdsOf(keys, "group:"),
            BatteryRunIds = IdsOf(keys, "battery:"),
            PricingBasis = basis,
            Mode = BenchmarkPairedComparisonMode.Reference,
            ReferenceKey = keys.Contains(referenceKey, StringComparer.Ordinal) ? referenceKey : lettered[0].Key,
            Recompute = true
        };

        try
        {
            var (reference, error) = await pairedTests.CompareAsync(request, restricted, ct);
            if (reference == null) return (null, null, error ?? "The paired tests could not be computed.");

            BenchmarkPairedComparisonDto? allPairs = null;
            if (BenchmarkComparisonReportFacts.UsesAllPairs(lettered.Count))
            {
                request.Mode = BenchmarkPairedComparisonMode.AllPairs;
                (allPairs, _) = await pairedTests.CompareAsync(request, restricted, ct);
            }
            return (reference, allPairs, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, null, "The paired tests could not be computed: " + ExceptionDetails.DescribeShort(ex));
        }
    }

    private static List<long> IdsOf(IEnumerable<string> keys, string prefix)
        => keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => long.TryParse(k.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

    /// <summary>
    /// An entry's provider availability, as the comparison computes it: a battery result's over every member
    /// run of its battery run, superseded ones included, any other entry's over its runs; both with their
    /// lineage predecessors.
    /// </summary>
    private static async Task<ProviderAvailabilitySummary> AvailabilityOfAsync(
        BenchmarkProviderAvailabilityService service, BenchmarkModelComparisonEntryDto entry, CancellationToken ct)
    {
        if (entry.BatteryRunId is long batteryRunId && await service.ForBatteryRunAsync(batteryRunId, ct) is { } battery)
        {
            return battery.Pool.Summary;
        }
        return (await service.ForRunsAsync(entry.RunIds, ct)).Summary;
    }

    /// <summary>A stand-in configuration carrying only the subject's provider and model id, for the compliance checks.</summary>
    public static SystemAiApiConfiguration SubjectIdentity(BenchmarkModelComparisonEntryDto subject)
        => new() { Provider = subject.Provider, ModelId = subject.ModelId, DisplayName = subject.ModelDisplayName };

    public static string SameProviderWarning(BenchmarkModelComparisonEntryDto subject, SystemAiApiConfiguration writer)
        => $"The report writer ({writer.DisplayName}) belongs to the same provider as the model under report ({subject.Label}, {subject.Provider}). "
            + "A writer from the model's own family may describe it more favorably; a writer from another family is recommended.";

    /// <summary>
    /// The same-provider warning of a comparison-scope document, naming the covered models that share
    /// the writer's provider.
    /// </summary>
    public static string SameProviderWarning(IReadOnlyList<BenchmarkModelComparisonEntryDto> sharing, SystemAiApiConfiguration writer)
    {
        ArgumentNullException.ThrowIfNull(sharing);
        ArgumentNullException.ThrowIfNull(writer);
        string models = BenchmarkReportFormat.LetterList(sharing.Select(e => e.Label).ToList());
        return $"The report writer ({writer.DisplayName}) belongs to the same provider ({writer.Provider}) as {models}, "
            + (sharing.Count == 1 ? "a model" : "models") + " this document covers. "
            + "A writer from a model's own family may describe it more favorably; a writer from another family is recommended.";
    }

    /// <summary>
    /// The writer is the same configuration as a covered model: the same provider and model id (trimmed,
    /// ignoring case) and the same thinking level (an unset one equal to another unset one).
    /// </summary>
    public static BenchmarkModelComparisonEntryDto? WriterAsCoveredModel(
        IReadOnlyList<BenchmarkModelComparisonEntryDto> covered, SystemAiApiConfiguration writer)
    {
        ArgumentNullException.ThrowIfNull(covered);
        ArgumentNullException.ThrowIfNull(writer);

        static string Norm(string? value) => (value ?? string.Empty).Trim();
        return covered.FirstOrDefault(e =>
            string.Equals(Norm(e.Provider), Norm(writer.Provider), StringComparison.OrdinalIgnoreCase)
            && Norm(e.ModelId).Length > 0
            && string.Equals(Norm(e.ModelId), Norm(writer.ModelId), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Norm(e.ThinkingLevel), Norm(writer.ThinkingLevel), StringComparison.OrdinalIgnoreCase));
    }

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
    /// job's documents, its writer and the documents it replaces; the job must already hold the report-pack slot. A writer that
    /// writes run-completion documents only refuses with <see cref="NotSupportedException"/>.
    /// </summary>
    Task WriteBatteryCompletionDocumentsAsync(long batteryRunId, BenchmarkReportPackJob job, CancellationToken ct)
        => Task.FromException(new NotSupportedException("This report writer does not write battery-completion documents."));
}

/// <summary>
/// Writes report-pack documents: one writer call per document, one repair turn when validation
/// fails, then drop-and-notice; a headline that alone stays invalid is rescued from the first draft
/// or, in comparison scope, by one headline-only turn. A job writes its subjects one after another:
/// each subject of a model-scope job, sharing one computation of the comparison, or the one covered
/// set of a comparison-scope job. A Root Cause Analysis gets its server-drawn charts once stored.
/// A document that replaces another is stored first, the replaced row removed in the same save and
/// its chart folder after it, so a failed write deletes nothing. The only class of the feature that
/// calls a model; rendering is <see cref="BenchmarkReportRenderService"/>'s and never calls one.
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
    private readonly BenchmarkReportChartStore? _charts;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly Charts.RootCauseChartPublisher? _rootCauseCharts;
    private readonly ModelMetadataService _modelMetadata;

    /// <summary>The tokens a repair turn's message adds to the first call's input, for the estimates.</summary>
    public const int RepairMessageEstimatedTokens = 1000;

    /// <param name="scopeFactory">
    /// Opens the scope a chat consistency job writes in once its request has ended; without one (in
    /// tests) the job runs on this instance.
    /// </param>
    /// <param name="rootCauseCharts">
    /// Draws a stored Root Cause Analysis's server charts; without one, a publisher over
    /// <paramref name="charts"/>, and none when that is absent too.
    /// </param>
    /// <param name="modelMetadata">The model catalog the release dates come from; a fresh one when null.</param>
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
        ILogger<BenchmarkReportPackService> logger,
        BenchmarkReportChartStore? charts = null,
        IServiceScopeFactory? scopeFactory = null,
        Charts.RootCauseChartPublisher? rootCauseCharts = null,
        ModelMetadataService? modelMetadata = null)
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
        _charts = charts;
        _scopeFactory = scopeFactory;
        _rootCauseCharts = rootCauseCharts ?? (charts != null ? new Charts.RootCauseChartPublisher(charts) : null);
        _modelMetadata = modelMetadata ?? new ModelMetadataService();
    }

    /// <summary>
    /// The estimated cost of a document's first call and one repair turn: the repair's input is the first
    /// input, its estimated output and <see cref="RepairMessageEstimatedTokens"/>; its output the same
    /// estimated output.
    /// </summary>
    public static decimal EstimateCostWithRepair(ModelPricing pricing, long inputTokens, long outputTokens)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        return ModelPricingService.EstimateSingleRequestCost(pricing, inputTokens, outputTokens)
               + ModelPricingService.EstimateSingleRequestCost(pricing, inputTokens + outputTokens + RepairMessageEstimatedTokens, outputTokens);
    }

    /// <summary>One writer turn: the raw reply, the terminal error, and what it cost.</summary>
    private sealed record WriterTurn(string? FinalText, string? Error, int InputTokens, int OutputTokens, long DurationMs, decimal? CostUsd,
        string? FinishReason = null, int ReasoningTokens = 0);

    /// <summary>
    /// The messages a writer call sends and their characters: the system prompt and every message of its
    /// history, each counted by its <c>content</c> text.
    /// </summary>
    private static (int Messages, long Characters) RequestSize(AgentRunRequest request)
    {
        long characters = (request.SystemPrompt ?? string.Empty).Length;
        foreach (object message in request.SeedHistory ?? new List<object>())
        {
            if (message?.GetType().GetProperty("content")?.GetValue(message) is string content) characters += content.Length;
        }
        return (1 + (request.SeedHistory?.Count ?? 0), characters);
    }

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
            batteryRunId, job.Documents.Select(d => d.Audience), job.WriterConfigId, job.Request?.ReplaceDocumentIds);
        job.SubjectKey = job.Request.SubjectKey;
        if (string.IsNullOrWhiteSpace(job.SubjectLabel))
        {
            job.SubjectLabel = BenchmarkReportPackPreparation.BatteryJobLabel(batteryRunId);
        }
        return RunJobAsync(job, BenchmarkReportDocumentOrigin.BatteryCompletion, ct);
    }

    /// <summary>
    /// What a job's subjects share: the comparison computed once for a model-scope job's several
    /// subjects, and the numbered comparison its documents' titles name.
    /// </summary>
    private sealed class JobState
    {
        public BenchmarkModelComparisonDto? Comparison { get; set; }
        public Pdf.BenchmarkPdfComparison? Numbered { get; set; }
        public bool SeveralSubjects { get; init; }
    }

    /// <summary>
    /// Prepares each subject of the job once, in the order of its document rows, then writes and stores
    /// each of its documents with <paramref name="origin"/>. A subject that cannot be prepared fails its
    /// own documents; the job goes on with the next.
    /// </summary>
    private async Task RunJobAsync(BenchmarkReportPackJob job, BenchmarkReportDocumentOrigin origin, CancellationToken ct)
    {
        string jobId = job.Id;
        try
        {
            ct.ThrowIfCancellationRequested();
            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);

            var subjects = SubjectsOf(job);
            var state = new JobState { SeveralSubjects = subjects.Count > 1 };
            if (origin == BenchmarkReportDocumentOrigin.ReportPack)
            {
                await EnsureComparisonAsync(job, state, ct);
            }

            SetPhase(job, subjects[0].Rows, BenchmarkReportPackPhases.Preparing);
            var (firstPrep, firstRefusal) = await PrepareSubjectAsync(job, subjects[0].Key, state, excerptChars, ct);
            if (firstPrep == null && subjects.Count == 1)
            {
                FailAll(job, firstRefusal ?? "The report pack could not be prepared.");
                return;
            }

            var (binding, bindFailure) = await BindWriterAsync(job, ct);
            if (binding == null)
            {
                FailAll(job, bindFailure ?? "Writer model configuration not found or disabled.");
                return;
            }
            var (config, endpoint, apiKey, pricing, maxOutputTokens) = binding;

            int completed = 0;
            int failed = 0;
            for (int i = 0; i < subjects.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (subjectKey, rows) = subjects[i];
                if (i > 0) SetPhase(job, rows, BenchmarkReportPackPhases.Preparing);
                var (prep, refusal) = i == 0 ? (firstPrep, firstRefusal) : await PrepareSubjectAsync(job, subjectKey, state, excerptChars, ct);
                if (prep == null)
                {
                    string message = refusal ?? "The report pack could not be prepared.";
                    job.AddLog(message, "error");
                    foreach (var row in rows)
                    {
                        job.SetDocumentStatus(row, BenchmarkReportPackDocumentStatus.Failed, message);
                    }
                    failed += rows.Count;
                    continue;
                }

                job.AddLog(prep.Scope == BenchmarkReportScope.Comparison
                    ? $"Fact sheet computed: {prep.Sheet.Facts.Count} facts, {prep.Covered.Count} models, {prep.Sheet.Questions.Count} questions."
                    : $"Fact sheet computed: {prep.Sheet.Facts.Count} facts, {prep.Sheet.Peers.Count} peers, {prep.Sheet.Questions.Count} questions, {prep.Sheet.Rows.Count} findings.");
                foreach (var note in prep.Notes)
                {
                    job.AddLog(note.Message, "warning");
                }
                job.AddPreparationNotes(prep.Notes);
                if (origin == BenchmarkReportDocumentOrigin.ReportPack && job.Entries.Count == 0)
                {
                    SetComparisonContext(job, prep);
                }

                foreach (var row in rows)
                {
                    if (string.IsNullOrEmpty(row.SubjectKey)) row.SubjectKey = prep.Scope == BenchmarkReportScope.Comparison ? prep.Sheet.SubjectKey : prep.Subject.Key;
                    if (string.IsNullOrEmpty(row.SubjectLabel)) row.SubjectLabel = prep.Sheet.SubjectLabel;

                    ct.ThrowIfCancellationRequested();
                    bool ok = await WriteDocumentAsync(job, row, origin, prep, state, config, endpoint, apiKey, pricing, excerptChars, maxOutputTokens, ct);
                    if (ok) completed++; else failed++;
                }
            }

            job.SetStatus(failed == 0
                ? BenchmarkReportPackJobStatus.Completed
                : completed > 0 ? BenchmarkReportPackJobStatus.CompletedWithErrors : BenchmarkReportPackJobStatus.Failed);
            job.AddLog($"Report pack finished: {completed} document(s) written, {failed} failed.");
        }
        catch (OperationCanceledException)
        {
            foreach (var d in job.Documents.Where(d => d.Status is BenchmarkReportPackDocumentStatus.Pending
                         or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing).ToList())
            {
                job.SetDocumentStatus(d, BenchmarkReportPackDocumentStatus.Canceled);
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

    /// <summary>
    /// The writer a job writes with: its configuration bound to the job's snapshot, its endpoint, key, price card and
    /// the output token limit each call is sent.
    /// </summary>
    private sealed record WriterBinding(SystemAiApiConfiguration Config, AiEndpointDescriptor Endpoint, string ApiKey, ModelPricing? Pricing,
        int MaxOutputTokens);

    /// <summary>
    /// The job's writer, with the settings captured when the job started; the failure message when its
    /// configuration is gone or disabled, no longer matches the snapshot, or its endpoint is refused. A
    /// writer without a price card is logged and kept.
    /// </summary>
    private async Task<(WriterBinding? Binding, string? Failure)> BindWriterAsync(BenchmarkReportPackJob job, CancellationToken ct)
    {
        var liveConfig = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == job.WriterConfigId, ct);
        if (liveConfig == null || !liveConfig.IsEnabled)
        {
            return (null, "Writer model configuration not found or disabled.");
        }

        // Every document is written with the settings captured when the job started.
        var writerSnapshot = job.WriterSnapshotId > 0
            ? await _db.SystemAiConfigurationSnapshots.FindAsync(new object[] { job.WriterSnapshotId }, ct)
            : null;
        if (!SystemAiConfigurationSnapshotStore.TryBind(liveConfig, writerSnapshot, out var config, out var bindError))
        {
            return (null, bindError ?? SystemAiConfigurationSnapshotStore.MismatchMessage);
        }

        if (!_endpointPolicy.TryResolveStrict(config!.BaseUrl, config.CustomHeadersJson, config.ApiVersion, out var endpoint, out var endpointError))
        {
            return (null, $"Configuration '{config.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}");
        }

        job.WriterProvider = config.Provider;
        job.WriterModelId = config.ModelId;
        job.WriterThinkingLevel = config.ThinkingLevel;
        int maxOutputTokens = BenchmarkReportPackPreparation.WriterMaxOutputTokens(_configuration, config.Provider, config.ModelId);
        job.WriterMaxOutputTokens = maxOutputTokens;

        string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");
        var pricing = _pricingService.Resolve(config);
        if (pricing == null)
        {
            job.AddLog("No price card resolves for the writer; document costs are not recorded.", "warning");
        }

        return (new WriterBinding(config, endpoint!, apiKey, pricing, maxOutputTokens), null);
    }

    /// <summary>
    /// The job's subjects in the order of its document rows, each with its rows. A row without a subject
    /// key (a completion job's, and every row of a job started before rows carried one) belongs to the
    /// request's subject.
    /// </summary>
    private static List<(string Key, List<BenchmarkReportPackDocumentProgress> Rows)> SubjectsOf(BenchmarkReportPackJob job)
    {
        var subjects = new List<(string Key, List<BenchmarkReportPackDocumentProgress> Rows)>();
        foreach (var row in job.Documents.ToList())
        {
            string key = string.IsNullOrEmpty(row.SubjectKey) ? job.Request.SubjectKey ?? string.Empty : row.SubjectKey;
            int index = subjects.FindIndex(s => string.Equals(s.Key, key, StringComparison.Ordinal));
            if (index < 0)
            {
                subjects.Add((key, new List<BenchmarkReportPackDocumentProgress> { row }));
            }
            else
            {
                subjects[index].Rows.Add(row);
            }
        }
        if (subjects.Count == 0) subjects.Add((job.Request.SubjectKey ?? string.Empty, new List<BenchmarkReportPackDocumentProgress>()));
        return subjects;
    }

    /// <summary>
    /// Numbers the job's comparison when the start has not (a Report Pack document always belongs to a
    /// numbered comparison), and reads its name for the documents' titles. A comparison that cannot be
    /// numbered is logged; its documents are then stored without one.
    /// </summary>
    private async Task EnsureComparisonAsync(BenchmarkReportPackJob job, JobState state, CancellationToken ct)
    {
        if (job.ComparisonId == null)
        {
            var identity = new BenchmarkComparisonIdentityService(_db, _comparisonService);
            var (comparison, error) = await identity.EnsureAsync(
                job.Request.RunIds, job.Request.GroupIds, job.Request.BatteryRunIds, job.StartedByUserId, ct);
            if (comparison == null)
            {
                job.AddLog($"The comparison could not be numbered: {error}", "warning");
                return;
            }
            job.ComparisonId = comparison.Id;
        }

        var stored = await _db.BenchmarkComparisons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == job.ComparisonId, ct);
        state.Numbered = stored == null
            ? new Pdf.BenchmarkPdfComparison(job.ComparisonId!.Value, null, null)
            : new Pdf.BenchmarkPdfComparison(stored.Id, stored.DisplayName, stored.EntryCount > 0 ? stored.EntryCount : null);
    }

    /// <summary>
    /// One subject's preparation: the covered set of a comparison-scope job, or a model-scope subject
    /// over the comparison computed once for the job.
    /// </summary>
    private async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareSubjectAsync(
        BenchmarkReportPackJob job, string subjectKey, JobState state, int excerptChars, CancellationToken ct)
    {
        if (job.Scope == BenchmarkReportScope.Comparison)
        {
            return await BenchmarkReportPackPreparation.PrepareComparisonAsync(
                _db, _comparisonService, job.Request, excerptChars, job.ComparisonId, ct, _configuration,
                new BenchmarkPairedTestsService(_db, _comparisonService, null, _pricingService),
                releaseDate: (p, m) => _modelMetadata.GetMetadata(p, m).ReleaseDate);
        }

        var request = SubjectRequest(job.Request, subjectKey);
        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            _db, _comparisonService, request, excerptChars, ct, _configuration, state.SeveralSubjects ? state.Comparison : null);
        if (prep != null && state.SeveralSubjects) state.Comparison ??= prep.Comparison;
        return (prep, refusal);
    }

    /// <summary>A copy of the request for one model-scope subject.</summary>
    private static BenchmarkReportPackRequest SubjectRequest(BenchmarkReportPackRequest request, string subjectKey) => new()
    {
        RunIds = request.RunIds ?? new List<long>(),
        GroupIds = request.GroupIds ?? new List<long>(),
        BatteryRunIds = request.BatteryRunIds ?? new List<long>(),
        PricingBasis = request.PricingBasis,
        SubjectKey = subjectKey,
        Scope = BenchmarkReportScope.Model,
        Audiences = request.Audiences,
        WriterModelConfigurationId = request.WriterModelConfigurationId,
        AcknowledgeSameProvider = request.AcknowledgeSameProvider,
        ReplaceDocumentIds = request.ReplaceDocumentIds
    };

    private static void SetPhase(BenchmarkReportPackJob job, IEnumerable<BenchmarkReportPackDocumentProgress> rows, string phase)
    {
        foreach (var row in rows) job.SetDocumentPhase(row, phase);
    }

    /// <summary>The job's comparison as its diagnostics show it: the entry keys, and a comparison-scope job's reference model by its label.</summary>
    private static void SetComparisonContext(BenchmarkReportPackJob job, BenchmarkReportPackPreparation prep)
    {
        job.Entries = (prep.Comparison?.Entries ?? new List<BenchmarkModelComparisonEntryDto>()).Select(e => e.Key).ToList();
        job.ReferenceModel = prep.Scope == BenchmarkReportScope.Comparison && prep.ReferenceEntryKey is string reference
            ? prep.Covered.FirstOrDefault(c => string.Equals(c.Key, reference, StringComparison.Ordinal))?.Label
            : null;
    }

    private static void FailAll(BenchmarkReportPackJob job, string message)
    {
        job.AddLog(message, "error");
        foreach (var d in job.Documents.ToList())
        {
            job.SetDocumentStatus(d, BenchmarkReportPackDocumentStatus.Failed, message);
        }
        job.SetStatus(BenchmarkReportPackJobStatus.Failed);
    }

    /// <summary>
    /// Writes, validates, repairs once, drops what still fails, and persists one document. True when
    /// stored. When the headline is the only fatal cause after the repair, the first draft's headline is
    /// kept if it passes. When the repaired draft's only blocking problems are sections over their word caps
    /// and/or the headline, or the headline is still the only fatal cause, one final turn on the same
    /// conversation asks for those sections shortened and the headline rewritten (<see cref="BenchmarkReportPackCallKinds.Shorten"/>,
    /// or <see cref="BenchmarkReportPackCallKinds.Headline"/> for the headline alone); its reply is used only when
    /// it leaves no more blocking issues and nothing fatal. Every ending records the first-turn, revision and final
    /// issues and the word counts on the progress row, and a failure its last reply. A stored Root Cause Analysis
    /// then gets its server-drawn charts.
    /// </summary>
    private async Task<bool> WriteDocumentAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportPackDocumentProgress progress,
        BenchmarkReportDocumentOrigin origin,
        BenchmarkReportPackPreparation prep,
        JobState state,
        SystemAiApiConfiguration config,
        AiEndpointDescriptor endpoint,
        string apiKey,
        ModelPricing? pricing,
        int excerptChars,
        int maxOutputTokens,
        CancellationToken ct)
    {
        var audience = progress.Audience;
        bool comparisonScope = prep.Scope == BenchmarkReportScope.Comparison;
        string name = BenchmarkReportRenderService.AudienceName(audience) + (state.SeveralSubjects ? " about " + prep.Sheet.SubjectLabel : string.Empty);

        job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Writing);
        job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Writing);
        job.AddLog($"Writing the {name} with {config.DisplayName}...");

        var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
        job.WriterPromptSha256 ??= BenchmarkReportPackPrompt.PromptSha256(audience, prep.Scope);
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
            // One budget counts every turn of the document: the first turn, the repair turns and the final turn.
            Budget = new AgentRunBudget { MaxTotalModelCalls = MaxWriterCalls },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt.UserMessage }
            }
        };

        var turns = new List<WriterTurn>();
        IReadOnlyList<BenchmarkReportValidationNote> firstIssues = Array.Empty<BenchmarkReportValidationNote>();
        IReadOnlyList<BenchmarkReportValidationNote> finalIssues = Array.Empty<BenchmarkReportValidationNote>();
        IReadOnlyList<BenchmarkReportValidationNote>? revisionIssues = null;
        IReadOnlyList<BenchmarkReportSlotWordCount>? draftCounts = null;
        IReadOnlyList<BenchmarkReportSlotWordCount>? revisionCounts = null;
        IReadOnlyList<BenchmarkReportSlotWordCount>? finalCounts = null;
        var shortenedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        List<BenchmarkReportPackWordCountDto>? WordCounts() => WordCountDtos(draftCounts, revisionCounts, shortenedCounts, finalCounts);

        bool Fail(string message, bool log = true)
        {
            if (log) job.AddLog($"{name}: {message}", "error");
            job.SetDocumentDiagnostics(progress, firstIssues, finalIssues, LastReply(turns), revisionIssues, WordCounts());
            job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Failed, message);
            return false;
        }

        // The output limit a turn's reply stopped at, logged; null when the reply ended on its own.
        int? CutOffAt(WriterTurn turn)
        {
            if (turn.Error != null || !StoppedAtOutputLimit(turn.FinishReason)) return null;
            job.AddLog($"{name}: the reply stopped at the output token limit ({Thousands(maxOutputTokens)} tokens, {Thousands(turn.ReasoningTokens)} reasoning).", "warning");
            return maxOutputTokens;
        }

        BenchmarkReportWriterOutput output;
        var notes = new List<BenchmarkReportValidationNote>(prep.Notes);
        try
        {
            var first = await RunTurnAsync(job, progress, runRequest, config, pricing, BenchmarkReportPackCallKinds.Draft, ct);
            turns.Add(first);
            if (first.Error != null)
            {
                job.AddLog($"{name}: provider error: {first.Error}", "error");
                return Fail(first.Error, log: false);
            }

            job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Checking);
            var (parsed, issues) = ParseAndValidate(audience, first.FinalText, prep, CutOffAt(first));
            var firstOutput = parsed;
            if (parsed != null) draftCounts = BenchmarkReportPackValidator.SlotWordCounts(audience, parsed, prep.Sheet, prep.Content);
            firstIssues = issues;
            finalIssues = issues;
            job.SetFirstDraftClean(progress, issues.Count == 0);
            WriterTurn? repair = null;
            if (issues.Count > 0)
            {
                job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Repairing);
                job.SetFirstTurnIssues(progress, issues);
                job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Revising, RevisionDetail(issues));
                job.AddLog($"{name}: {issues.Count} validation issue(s) ({RuleCounts(issues)}); sending one repair turn.", "warning");

                string repairMessage = RepairMessage(issues, prep.Scope, parsed?.Headline, draftCounts, audience);
                runRequest.SeedHistory.Add(new { role = "assistant", content = first.FinalText ?? string.Empty });
                runRequest.SeedHistory.Add(new { role = "user", content = repairMessage });

                repair = await RunTurnAsync(job, progress, runRequest, config, pricing, BenchmarkReportPackCallKinds.Revision, ct, repairMessage);
                turns.Add(repair);
                job.SetDocumentPhase(progress, BenchmarkReportPackPhases.CheckingRevision);
                if (repair.Error != null)
                {
                    job.AddLog($"{name}: provider error on the repair turn: {repair.Error}", "error");
                    if (parsed == null) return Fail(repair.Error, log: false);
                }
                else
                {
                    var (repairedOutput, repairedIssues) = ParseAndValidate(audience, repair.FinalText, prep, CutOffAt(repair));
                    if (repairedOutput != null)
                    {
                        parsed = repairedOutput;
                        issues = repairedIssues;
                        finalIssues = repairedIssues;
                        revisionIssues = repairedIssues;
                        revisionCounts = BenchmarkReportPackValidator.SlotWordCounts(audience, repairedOutput, prep.Sheet, prep.Content);
                    }
                    else if (parsed != null)
                    {
                        string error = repairedIssues.FirstOrDefault()?.Message ?? "the reply is not a JSON object";
                        job.AddLog($"{name}: the repair reply could not be parsed ({error.TrimEnd('.')}); the first reply is used.", "warning");
                    }
                    else
                    {
                        finalIssues = repairedIssues;
                    }
                }
            }

            // An unreadable first reply spent the repair turn on rebuilding its JSON: the first readable reply gets a
            // repair turn of its own when it has issues. The final turn then follows the last of them.
            if (firstOutput == null && parsed != null && issues.Count > 0 && repair is { Error: null } && turns.Count < MaxWriterCalls)
            {
                job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Revising, RevisionDetail(issues));
                job.AddLog($"{name}: the first reply could not be read, so the revised reply gets a repair turn of its own: "
                    + $"{issues.Count} validation issue(s) ({RuleCounts(issues)}).", "warning");

                string secondMessage = RepairMessage(issues, prep.Scope, parsed.Headline, revisionCounts, audience);
                runRequest.SeedHistory.Add(new { role = "assistant", content = repair.FinalText ?? string.Empty });
                runRequest.SeedHistory.Add(new { role = "user", content = secondMessage });

                repair = await RunTurnAsync(job, progress, runRequest, config, pricing, BenchmarkReportPackCallKinds.Revision, ct, secondMessage);
                turns.Add(repair);
                job.SetDocumentPhase(progress, BenchmarkReportPackPhases.CheckingRevision);
                if (repair.Error != null)
                {
                    job.AddLog($"{name}: provider error on the second repair turn: {repair.Error}; the revised reply is used.", "warning");
                }
                else
                {
                    var (secondOutput, secondIssues) = ParseAndValidate(audience, repair.FinalText, prep, CutOffAt(repair));
                    if (secondOutput != null)
                    {
                        parsed = secondOutput;
                        issues = secondIssues;
                        finalIssues = secondIssues;
                        revisionIssues = secondIssues;
                        revisionCounts = BenchmarkReportPackValidator.SlotWordCounts(audience, secondOutput, prep.Sheet, prep.Content);
                    }
                    else
                    {
                        string error = secondIssues.FirstOrDefault()?.Message ?? "the reply is not a JSON object";
                        job.AddLog($"{name}: the second repair reply could not be parsed ({error.TrimEnd('.')}); the revised reply is used.", "warning");
                    }
                }
            }

            if (parsed == null)
            {
                return Fail(issues.FirstOrDefault()?.Message ?? "The writer's reply could not be parsed.");
            }

            if (issues.Count > 0)
            {
                var cleaned = BenchmarkReportPackValidator.DropInvalid(audience, parsed, prep.Sheet, prep.Content);
                var rescues = new List<BenchmarkReportValidationNote>();
                string? fatalReason = null;
                bool headlineRescued = false;
                if (cleaned.Fatal && cleaned.HeadlineOnlyFatal && firstOutput != null
                    && BlockingHeadlineIssue(audience, firstOutput.Headline, prep) == null)
                {
                    // The first draft's headline passes every blocking check: it replaces the repaired one.
                    string invalid = (BlockingHeadlineIssue(audience, parsed.Headline, prep)?.Message ?? "it is not valid").TrimEnd('.');
                    parsed.Headline = firstOutput.Headline;
                    cleaned = BenchmarkReportPackValidator.DropInvalid(audience, parsed, prep.Sheet, prep.Content);
                    rescues.Add(HeadlineRescueNote($"The repaired headline was invalid ({invalid}); the first draft's headline was kept."));
                    headlineRescued = true;
                }

                // One final turn, decided on the repaired draft's own issues before anything is trimmed: the sections
                // over their word caps when they and the headline are the only blocking problems left, and the
                // headline when it is invalid.
                var baseIssues = headlineRescued ? BenchmarkReportPackValidator.Validate(audience, parsed, prep.Sheet, prep.Content) : issues;
                var blocking = baseIssues.Where(BlocksForFinalTurn).ToList();
                var headlineBlocking = blocking.Where(IsHeadlineNote).ToList();
                bool capsOrHeadlineOnly = blocking.Count > 0 && blocking.All(n => IsHeadlineNote(n) || IsSlotCapNote(n));
                var slots = capsOrHeadlineOnly
                    ? BenchmarkReportPackValidator.SlotWordCounts(audience, parsed, prep.Sheet, prep.Content).Where(c => c.OverCap).ToList()
                    : new List<BenchmarkReportSlotWordCount>();
                bool askHeadline = headlineBlocking.Count > 0 && (capsOrHeadlineOnly || (cleaned.Fatal && cleaned.HeadlineOnlyFatal));
                if ((slots.Count > 0 || askHeadline) && repair is { Error: null } && turns.Count < MaxWriterCalls)
                {
                    string invalid = askHeadline ? headlineBlocking[0].Message.TrimEnd('.') : string.Empty;
                    if (slots.Count > 0)
                    {
                        string them = slots.Count == 1 ? "it" : "them";
                        job.AddLog($"{name}: {slots.Count} section(s) over their word caps after the repair ("
                            + string.Join("; ", slots.Select(c => c.LocationLabel + ", " + Number(c.Words ?? 0) + " of " + Number(c.Cap))) + ")"
                            + (askHeadline ? $" and the headline is invalid ({invalid})" : string.Empty)
                            + "; sending one shortening turn.", "warning");
                        job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Revising, askHeadline
                            ? $"The revised draft is over the word cap in {JoinAnd(slots.Select(c => c.LocationLabel))} and its headline is invalid ({invalid}); the writer is asked to shorten {them} and to rewrite the headline."
                            : $"The revised draft is over the word cap in {JoinAnd(slots.Select(c => c.LocationLabel))}; the writer is asked to shorten {them}.");
                    }
                    else
                    {
                        job.AddLog($"{name}: the repaired headline is invalid ({invalid}); sending one headline-only turn.", "warning");
                        job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Revising, $"The revised headline is invalid ({invalid}); the writer is asked for a new headline only.");
                    }

                    var headlineIssues = askHeadline
                        ? BenchmarkReportPackValidator.HeadlineIssues(audience, parsed.Headline, prep.Sheet, prep.Content)
                        : Array.Empty<BenchmarkReportValidationNote>();
                    string finalMessage = BenchmarkReportPackPrompt.BuildShortenMessage(
                        slots, headlineIssues, BenchmarkReportPackValidator.WordCount(parsed.Headline ?? string.Empty),
                        audience == BenchmarkReportAudience.RootCauseAnalysis);
                    runRequest.SeedHistory.Add(new { role = "assistant", content = repair.FinalText ?? string.Empty });
                    runRequest.SeedHistory.Add(new { role = "user", content = finalMessage });

                    var finalTurn = await RunTurnAsync(job, progress, runRequest, config, pricing,
                        slots.Count > 0 ? BenchmarkReportPackCallKinds.Shorten : BenchmarkReportPackCallKinds.Headline, ct, finalMessage);
                    turns.Add(finalTurn);
                    job.SetDocumentPhase(progress, BenchmarkReportPackPhases.CheckingRevision);
                    CutOffAt(finalTurn);

                    var reply = finalTurn.Error == null ? BenchmarkReportPackParser.Parse(finalTurn.FinalText) : null;
                    IReadOnlyList<BenchmarkReportValidationNote> turnHeadlineIssues = Array.Empty<BenchmarkReportValidationNote>();
                    string? turnFailure;
                    if (finalTurn.Error != null)
                    {
                        turnFailure = "Provider error on the final turn: " + finalTurn.Error;
                        job.AddLog($"{name}: provider error on the final turn: {finalTurn.Error}", "warning");
                    }
                    else if (reply is not { Success: true, Output: not null })
                    {
                        string error = reply?.Error ?? "it is not a JSON object.";
                        turnFailure = "The final turn's reply could not be parsed: " + error;
                        job.AddLog($"{name}: the final turn's reply could not be parsed ({error.TrimEnd('.')}); it is ignored.", "warning");
                    }
                    else
                    {
                        var rewritten = reply!.Output!;
                        var shortened = slots
                            .Where(c => rewritten.Sections.TryGetValue(c.Slot, out string? text) && !string.IsNullOrWhiteSpace(text))
                            .ToList();
                        foreach (var c in shortened)
                        {
                            shortenedCounts[c.Slot] = BenchmarkReportPackValidator.WordCount(rewritten.Sections[c.Slot]);
                        }
                        bool headlineGiven = askHeadline && !string.IsNullOrWhiteSpace(rewritten.Headline);
                        if (askHeadline)
                        {
                            turnHeadlineIssues = BenchmarkReportPackValidator.HeadlineIssues(audience, rewritten.Headline, prep.Sheet, prep.Content);
                        }

                        // The merge is kept only when it adds no blocking issue and nothing in it is fatal; when the
                        // shortened sections spoil it, the rewritten headline alone is tried.
                        BenchmarkReportWriterOutput current = parsed;
                        (BenchmarkReportWriterOutput Output, BenchmarkReportCleanResult Clean)? Merge(bool withSlots, bool withHeadline)
                        {
                            if (!withSlots && !withHeadline) return null;
                            var merged = CopyOutput(current);
                            if (withSlots)
                            {
                                foreach (var c in shortened) merged.Sections[c.Slot] = rewritten.Sections[c.Slot];
                            }
                            if (withHeadline) merged.Headline = rewritten.Headline;
                            int after = BenchmarkReportPackValidator.Validate(audience, merged, prep.Sheet, prep.Content)
                                .Count(BlocksForFinalTurn);
                            var mergedClean = BenchmarkReportPackValidator.DropInvalid(audience, merged, prep.Sheet, prep.Content);
                            return after <= blocking.Count && !mergedClean.Fatal ? (merged, mergedClean) : null;
                        }

                        bool usedSlots = shortened.Count > 0;
                        bool usedHeadline = headlineGiven;
                        var kept = Merge(usedSlots, usedHeadline);
                        if (kept == null && usedSlots && usedHeadline)
                        {
                            usedSlots = false;
                            kept = Merge(withSlots: false, withHeadline: true);
                        }

                        if (kept is { } accepted)
                        {
                            parsed = accepted.Output;
                            cleaned = accepted.Clean;
                            turnFailure = null;
                            if (usedSlots)
                            {
                                // A section still over its cap is trimmed by DropInvalid, which records its own note.
                                foreach (var c in shortened)
                                {
                                    int before = c.Words ?? 0;
                                    int now = shortenedCounts[c.Slot];
                                    job.AddLog($"{name}: {c.LocationLabel} was {Number(before)} words, {Number(before - c.Cap)} over its cap of {Number(c.Cap)}, "
                                        + $"after the repair; the shortening turn brought it to {Number(now)}"
                                        + (now > c.Cap ? ", still over its cap." : "."), "info");
                                }
                            }
                            if (usedHeadline)
                            {
                                rescues.Add(HeadlineRescueNote(slots.Count > 0
                                    ? $"The repaired headline was invalid ({invalid}); it was rewritten in a final turn."
                                    : $"The repaired headline was invalid ({invalid}); it was rewritten in a headline-only turn."));
                            }
                        }
                        else
                        {
                            job.AddLog($"{name}: the final turn's reply was not used: it left more blocking issues, or a required part failed validation.", "warning");
                            var stillInvalid = turnHeadlineIssues.FirstOrDefault(BenchmarkReportPackValidator.BlocksText);
                            turnFailure = !askHeadline ? null
                                : !headlineGiven ? "The final turn's reply gave no headline."
                                : stillInvalid != null ? "The headline is invalid and cannot be dropped: " + stillInvalid.Message
                                : null;
                        }
                    }

                    if (cleaned.Fatal && turnFailure != null)
                    {
                        fatalReason = turnFailure;
                        finalIssues = turnHeadlineIssues.Count > 0
                            ? turnHeadlineIssues.Concat(cleaned.Notes.Where(n => !IsHeadlineNote(n))).ToList()
                            : cleaned.Notes;
                    }
                }

                if (cleaned.Fatal)
                {
                    if (fatalReason == null) finalIssues = cleaned.Notes;
                    return Fail(fatalReason ?? cleaned.FatalReason ?? "Required content failed validation after the repair turn.");
                }

                var (restored, cleanedNotes) = RestoreUncitedAxes(parsed, cleaned.Output, prep.Sheet, cleaned.Notes);
                parsed = cleaned.Output;
                notes.AddRange(cleanedNotes);
                var removed = cleanedNotes.Where(n => n.Dropped).ToList();
                if (removed.Count > 0)
                {
                    var titles = BenchmarkReportRuleCatalog.SlotTitlesFor(audience);
                    job.AddLog($"{name}: {removed.Count} item(s) removed by validation: "
                        + string.Join("; ", removed.Select(n => BenchmarkReportRuleCatalog.LocationLabel(n.Location, titles)
                            + " (" + BenchmarkReportRuleCatalog.Name(n.Rule) + (n.SentenceOnly ? ", one sentence" : string.Empty) + ")"))
                        + ".", "warning");
                }
                foreach (string axis in restored)
                {
                    job.AddLog($"{name}: the conclusions' paragraph on {RootCauseAxes.Name(axis).ToLowerInvariant()} was removed; Overseer wrote the axis's status in its place.", "warning");
                }
                foreach (var rescued in rescues)
                {
                    notes.Add(rescued);
                    job.AddLog($"{name}: {rescued.Message}", "warning");
                }
                finalIssues = cleanedNotes.Concat(rescues).ToList();
            }

            output = parsed;
        }
        catch (OperationCanceledException)
        {
            job.SetDocumentDiagnostics(progress, firstIssues, finalIssues, revisionIssues: revisionIssues, wordCounts: WordCounts());
            throw;
        }
        finalCounts = BenchmarkReportPackValidator.SlotWordCounts(audience, output, prep.Sheet, prep.Content);
        job.SetDocumentDiagnostics(progress, firstIssues, finalIssues, revisionIssues: revisionIssues, wordCounts: WordCounts());
        job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Storing);

        // A warning note (BenchmarkReportPackValidator.IsWarningRule) keeps its text but still marks the document, and so do
        // a word-cap note recorded against kept text (a section kept over its cap) and a chat consistency document-level
        // note (C6 or C7 recorded against the kept text).
        bool chatConsistency = prep.Scope == BenchmarkReportScope.ChatConsistency;
        var status = notes.Any(n => n.Dropped || BenchmarkReportPackValidator.IsWarningRule(n.Rule) || IsKeptWordCapNote(n)
                                    || (chatConsistency && IsChatConsistencyDocumentNote(n)))
            ? BenchmarkReportDocumentStatus.CompletedWithWarnings
            : BenchmarkReportDocumentStatus.Completed;

        bool reportPack = origin == BenchmarkReportDocumentOrigin.ReportPack;
        var requestRunIds = job.Request.RunIds.OrderBy(id => id).ToList();
        var requestGroupIds = job.Request.GroupIds.OrderBy(id => id).ToList();
        var requestBatteryRunIds = (job.Request.BatteryRunIds ?? new List<long>()).OrderBy(id => id).ToList();
        string comparisonKey = BenchmarkReportComparisonKey.From(requestRunIds, requestGroupIds, requestBatteryRunIds);
        var document = new BenchmarkReportDocument
        {
            PackId = job.PackId,
            Audience = audience,
            Origin = origin,
            Scope = reportPack || chatConsistency ? prep.Scope : BenchmarkReportScope.Model,
            ComparisonId = reportPack ? job.ComparisonId : null,
            CoveredEntryKeysJson = reportPack ? BenchmarkReportJson.Serialize(prep.CoveredEntryKeys.ToList()) : null,
            CoveredSetKey = reportPack && prep.CoveredSetKey.Length > 0 ? prep.CoveredSetKey : null,
            ReferenceEntryKey = reportPack && comparisonScope ? prep.ReferenceEntryKey : null,
            SubjectKey = comparisonScope || chatConsistency ? prep.Sheet.SubjectKey : prep.Subject.Key,
            SubjectLabel = Truncate(prep.Sheet.SubjectLabel, 256),
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(chatConsistency
                ? prep.Sheet.SubjectRunIds.ToList()
                : prep.SubjectRuns.Select(r => r.Id).ToList()),
            ComparisonRequestJson = BenchmarkReportJson.Serialize(new BenchmarkModelComparisonRequest
            {
                RunIds = requestRunIds,
                GroupIds = requestGroupIds,
                BatteryRunIds = requestBatteryRunIds,
                PricingBasis = job.Request.PricingBasis
            }),
            ComparisonKey = chatConsistency ? null : comparisonKey,
            ChatConsistencyAnalysisId = chatConsistency ? prep.Sheet.ChatConsistency?.AnalysisId : null,
            SuiteId = prep.Sheet.SuiteId,
            SuiteName = Truncate(prep.Sheet.SuiteName, 256),
            WriterConfigId = config.Id,
            WriterModelSnapshotId = job.WriterSnapshotId > 0 ? job.WriterSnapshotId : null,
            WriterDisplayName = Truncate(config.DisplayName ?? config.ModelId, 256),
            WriterProvider = Truncate(config.Provider, 64),
            WriterModelId = Truncate(config.ModelId, 128),
            WriterThinkingLevel = config.ThinkingLevel,
            SameProviderAcknowledged = job.SameProviderAcknowledged,
            ReportFormatVersion = BenchmarkReportPackRenderer.CurrentFormatVersion(prep.Scope),
            WriterPromptSha256 = BenchmarkReportPackPrompt.PromptSha256(audience, prep.Scope),
            AnswerExcerptChars = excerptChars,
            FactsJson = BenchmarkReportJson.Serialize(prep.Sheet),
            ContentJson = BenchmarkReportJson.Serialize(prep.Content),
            WriterOutputJson = BenchmarkReportJson.Serialize(output),
            ValidationNotesJson = BenchmarkReportJson.Serialize(notes),
            Title = Truncate(comparisonScope
                ? BenchmarkReportPackRenderer.BuildComparisonTitle(audience, prep.Sheet, state.Numbered, BenchmarkReportPeerNaming.Named)
                : BenchmarkReportPackRenderer.BuildTitle(audience, prep.Sheet), 512),
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

        // Replace-after-persist: the replaced rows leave in the same save that stores the new one.
        bool completion = origin is BenchmarkReportDocumentOrigin.RunCompletion or BenchmarkReportDocumentOrigin.BatteryCompletion;
        var replaced = reportPack || completion
            ? await ReplacedDocumentsAsync(job, prep, origin, audience, document.SubjectKey, comparisonKey, ct)
            : new List<BenchmarkReportDocument>();
        _db.BenchmarkReportDocuments.Add(document);
        _db.BenchmarkReportDocuments.RemoveRange(replaced);
        await _db.SaveChangesAsync(CancellationToken.None);

        foreach (var old in replaced)
        {
            job.AddLog($"{name}: replaced document #{old.Id}.");
            await DeleteChartFolderAsync(old.Id);
        }

        job.SetDocumentStatus(progress,
            status == BenchmarkReportDocumentStatus.Completed
                ? BenchmarkReportPackDocumentStatus.Completed
                : BenchmarkReportPackDocumentStatus.CompletedWithWarnings,
            documentId: document.Id);
        job.AddLog($"{name} stored as document #{document.Id}.");

        // A drawing failure is logged and never fails the stored document.
        if (audience == BenchmarkReportAudience.RootCauseAnalysis && _rootCauseCharts != null)
        {
            job.SetDocumentPhase(progress, BenchmarkReportPackPhases.DrawingCharts);
            var drawn = await _rootCauseCharts.PublishAsync(document.Id, prep.Sheet, ct);
            if (drawn.Succeeded)
            {
                job.AddLog($"Root Cause Analysis: {drawn.ChartCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} charts drawn.");
            }
            else
            {
                job.AddLog($"Root Cause Analysis: the charts were not drawn: {drawn.Error}", "warning");
            }
        }
        job.SetDocumentPhase(progress, BenchmarkReportPackPhases.Done);
        return true;
    }

    /// <summary>
    /// The phase detail of a revision: how many issues the first draft had, by rule in plain words, the
    /// most frequent first: "The automatic check found 20 issues in the first draft: ranking without the
    /// overlap caveat (13), fact placed in the wrong sentence shape (5), …".
    /// </summary>
    public static string RevisionDetail(IReadOnlyList<BenchmarkReportValidationNote> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        string count = issues.Count == 1 ? "one issue" : issues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " issues";
        var groups = issues
            .GroupBy(i => string.Equals(i.Location, "reply", StringComparison.Ordinal) ? "the reply could not be read" : LowerFirst(BenchmarkReportRuleCatalog.Name(i.Rule)))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " (" + g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        return $"The automatic check found {count} in the first draft: {string.Join(", ", groups)}.";
    }

    private static string LowerFirst(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>
    /// After a Root Cause Analysis's drops: for each axis whose status the writer's conclusions cited before
    /// the drop and the kept conclusions no longer cite, inserts into <paramref name="cleaned"/>'s conclusions a
    /// code-written paragraph citing that status, marked as Overseer's, at its axis's place (intelligence, speed,
    /// cost, among the kept paragraphs by the first axis status each cites); it is not counted against the cap.
    /// Returns the axes restored, in axis order, and <paramref name="cleanedNotes"/> with each restored axis's rule-36
    /// note replaced by an added-by-Overseer note holding the paragraph: a rule-36 note keeps only the axes still
    /// uncited, and goes when none is.
    /// </summary>
    private static (List<string> Axes, List<BenchmarkReportValidationNote> Notes) RestoreUncitedAxes(
        BenchmarkReportWriterOutput written, BenchmarkReportWriterOutput cleaned, BenchmarkReportFactSheet sheet,
        IReadOnlyList<BenchmarkReportValidationNote> cleanedNotes)
    {
        var restored = new List<string>();
        var notes = cleanedNotes.ToList();
        if (sheet.RootCause == null
            || !written.Sections.TryGetValue(BenchmarkReportSlots.Conclusions, out string? before)
            || !cleaned.Sections.TryGetValue(BenchmarkReportSlots.Conclusions, out string? after)
            || string.IsNullOrWhiteSpace(after))
        {
            return (restored, notes);
        }

        var keys = new HashSet<string>(sheet.Facts.Select(f => f.Key), StringComparer.Ordinal);
        var paragraphs = BenchmarkReportPackValidator.SplitParagraphs(after);
        var added = new List<BenchmarkReportValidationNote>();
        foreach (var axis in sheet.RootCause.Axes.OrderBy(a => AxisRank(a.Axis)))
        {
            string status = "{{" + RootCauseClaimSupport.StatusKey(axis.Axis) + "}}";
            if (!keys.Contains(RootCauseClaimSupport.StatusKey(axis.Axis))
                || !(before ?? string.Empty).Contains(status, StringComparison.Ordinal)
                || after.Contains(status, StringComparison.Ordinal))
            {
                continue;
            }

            string text = AddedByOverseer + " " + RootCauseAxes.Name(axis.Axis) + ": " + status + ".";
            string statement = axis.BestHypothesis == null ? string.Empty : RootCauseClaimSupport.HypothesisPrefix + axis.BestHypothesis + ".statement";
            if (statement.Length > 0 && keys.Contains(statement))
            {
                text += " The best hypothesis on this axis: {{" + statement + "}}";
            }
            paragraphs.Insert(AxisInsertionIndex(paragraphs, AxisRank(axis.Axis)), text);
            restored.Add(axis.Axis);
            added.Add(new BenchmarkReportValidationNote
            {
                Rule = BenchmarkReportPackValidator.RootCauseAxisStatusRule,
                Location = "sections." + BenchmarkReportSlots.Conclusions,
                Message = $"The writer's paragraph on {RootCauseAxes.Name(axis.Axis).ToLowerInvariant()} was removed; Overseer wrote the axis's status in its place.",
                Dropped = false,
                AddedByOverseer = true,
                Text = text
            });
        }
        if (restored.Count == 0) return (restored, notes);

        string conclusions = string.Join("\n\n", paragraphs);
        cleaned.Sections[BenchmarkReportSlots.Conclusions] = conclusions;

        int axisNote = notes.FindIndex(n => n.Rule == BenchmarkReportPackValidator.RootCauseAxisStatusRule && !n.AddedByOverseer
                                            && n.Location == "sections." + BenchmarkReportSlots.Conclusions);
        if (axisNote >= 0)
        {
            var uncited = RootCauseClaimSupport.From(sheet).Axes
                .Where(a => !conclusions.Contains("{{" + RootCauseClaimSupport.StatusKey(a) + "}}", StringComparison.Ordinal))
                .ToList();
            if (uncited.Count == 0)
            {
                notes.RemoveAt(axisNote);
            }
            else
            {
                var old = notes[axisNote];
                notes[axisNote] = new BenchmarkReportValidationNote
                {
                    Rule = old.Rule,
                    Location = old.Location,
                    Message = BenchmarkReportPackValidator.AxisStatusMessage(uncited),
                    Dropped = old.Dropped,
                    Text = old.Text
                };
            }
        }
        notes.AddRange(added);
        return (restored, notes);
    }

    /// <summary>An axis's place in the order intelligence, speed, cost; an unknown axis after them.</summary>
    private static int AxisRank(string axis)
    {
        for (int i = 0; i < RootCauseAxes.All.Count; i++)
        {
            if (string.Equals(RootCauseAxes.All[i], axis, StringComparison.Ordinal)) return i;
        }
        return RootCauseAxes.All.Count;
    }

    /// <summary>
    /// Where a conclusions paragraph on the axis of <paramref name="rank"/> goes among <paramref name="paragraphs"/>:
    /// before the first paragraph whose first axis status is of a later axis; else after the last paragraph that
    /// cites an axis status; else at the end.
    /// </summary>
    private static int AxisInsertionIndex(IReadOnlyList<string> paragraphs, int rank)
    {
        int lastRanked = -1;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (FirstStatusRank(paragraphs[i]) is not int r) continue;
            if (r > rank) return i;
            lastRanked = i;
        }
        return lastRanked >= 0 ? lastRanked + 1 : paragraphs.Count;
    }

    /// <summary>The rank (<see cref="AxisRank"/>) of the first axis status a paragraph cites; null when it cites none.</summary>
    private static int? FirstStatusRank(string paragraph)
    {
        int? rank = null;
        int at = int.MaxValue;
        foreach (string axis in RootCauseAxes.All)
        {
            int index = paragraph.IndexOf("{{" + RootCauseClaimSupport.StatusKey(axis) + "}}", StringComparison.Ordinal);
            if (index >= 0 && index < at)
            {
                at = index;
                rank = AxisRank(axis);
            }
        }
        return rank;
    }

    /// <summary>The Markdown italics that open a conclusions paragraph Overseer wrote in place of a removed one.</summary>
    public const string AddedByOverseer = "*Added by Overseer, because the writer's paragraph on this axis was removed:*";

    /// <summary>
    /// The documents of <c>ReplaceDocumentIds</c> this new document replaces, always of its audience:
    /// for a Report Pack document, Report Pack documents of the same comparison and of the same covered
    /// set (comparison scope) or subject (model scope); for a run- or battery-completion document,
    /// run- or battery-completion documents of the same subject.
    /// </summary>
    private async Task<List<BenchmarkReportDocument>> ReplacedDocumentsAsync(
        BenchmarkReportPackJob job, BenchmarkReportPackPreparation prep, BenchmarkReportDocumentOrigin origin,
        BenchmarkReportAudience audience, string subjectKey, string comparisonKey, CancellationToken ct)
    {
        var ids = (job.Request.ReplaceDocumentIds ?? new List<long>()).Distinct().ToList();
        if (ids.Count == 0) return new List<BenchmarkReportDocument>();

        if (origin != BenchmarkReportDocumentOrigin.ReportPack)
        {
            return await _db.BenchmarkReportDocuments
                .IgnoreAutoIncludes()
                .Where(d => ids.Contains(d.Id)
                            && (d.Origin == BenchmarkReportDocumentOrigin.RunCompletion || d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion)
                            && d.Audience == audience
                            && d.SubjectKey == subjectKey)
                .ToListAsync(ct);
        }

        var candidates = await _db.BenchmarkReportDocuments
            .IgnoreAutoIncludes()
            .Where(d => ids.Contains(d.Id) && d.Origin == BenchmarkReportDocumentOrigin.ReportPack && d.Audience == audience)
            .ToListAsync(ct);

        return candidates
            .Where(d => (job.ComparisonId != null && d.ComparisonId == job.ComparisonId) || d.ComparisonKey == comparisonKey)
            .Where(d => prep.Scope == BenchmarkReportScope.Comparison
                ? d.Scope == BenchmarkReportScope.Comparison && string.Equals(d.CoveredSetKey, prep.CoveredSetKey, StringComparison.Ordinal)
                : d.Scope == BenchmarkReportScope.Model && string.Equals(d.SubjectKey, subjectKey, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// The repair turn's message: a chat consistency document's own, else the per-model or comparison one with the
    /// headline as written and the draft's word budget (<paramref name="wordCounts"/>).
    /// </summary>
    private static string RepairMessage(
        IReadOnlyList<BenchmarkReportValidationNote> issues, BenchmarkReportScope scope, string? headline,
        IReadOnlyList<BenchmarkReportSlotWordCount>? wordCounts, BenchmarkReportAudience audience)
        => scope == BenchmarkReportScope.ChatConsistency
            ? BenchmarkReportPackPrompt.BuildRepairMessage(issues, scope)
            : BenchmarkReportPackPrompt.BuildRepairMessage(issues, scope == BenchmarkReportScope.Comparison, headline,
                wordCounts, audience == BenchmarkReportAudience.RootCauseAnalysis);

    /// <summary>
    /// The writer calls a document may take: the first turn, the repair turn, a second repair turn when the first reply
    /// was unreadable, and one final turn.
    /// </summary>
    private const int MaxWriterCalls = 4;

    /// <summary>
    /// A reply cut off at its output token limit: Google's <c>MAX_TOKENS</c>, Anthropic's <c>max_tokens</c> (the same
    /// word, ignoring case) and the OpenAI Responses API's <c>max_output_tokens</c>.
    /// </summary>
    private static bool StoppedAtOutputLimit(string? finishReason)
        => string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
           || string.Equals(finishReason, "max_output_tokens", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeadlineNote(BenchmarkReportValidationNote note)
        => string.Equals(note.Location, "headline", StringComparison.Ordinal);

    /// <summary>
    /// A blocking note that removes text or fails the document: not a warning, and not one recorded against kept text
    /// with nothing to remove (a missing question topic, and the document-level halves of C6 and C7).
    /// </summary>
    private static bool BlocksForFinalTurn(BenchmarkReportValidationNote note)
        => BenchmarkReportPackValidator.BlocksText(note)
           && !string.Equals(note.Location, "questionTopics", StringComparison.Ordinal)
           && !(note.Rule is BenchmarkReportPackValidator.ChatHoursRule or BenchmarkReportPackValidator.ChatProviderReportRule
                && note.Location.StartsWith("sections", StringComparison.Ordinal) && !note.Location.Contains('[', StringComparison.Ordinal));

    /// <summary>A rule-7 note on a whole section, <c>sections.&lt;slot&gt;</c>: the section is over its word cap.</summary>
    private static bool IsSlotCapNote(BenchmarkReportValidationNote note)
        => note.Rule == 7 && note.Location.StartsWith("sections.", StringComparison.Ordinal) && !note.Location.Contains('[', StringComparison.Ordinal);

    /// <summary>A rule-7 note recorded against kept text: a section kept over its cap.</summary>
    private static bool IsKeptWordCapNote(BenchmarkReportValidationNote note)
        => note.Rule == 7 && !note.Dropped && note.Location.StartsWith("sections", StringComparison.Ordinal);

    /// <summary>A copy of the output whose sections can be replaced without touching the original.</summary>
    private static BenchmarkReportWriterOutput CopyOutput(BenchmarkReportWriterOutput output) => new()
    {
        Headline = output.Headline,
        Sections = new Dictionary<string, string>(output.Sections ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        Strengths = output.Strengths,
        Weaknesses = output.Weaknesses,
        QuestionTopics = output.QuestionTopics,
        Models = output.Models,
        Observations = output.Observations,
        WriterHypotheses = output.WriterHypotheses
    };

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string JoinAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"16,000".</summary>
    private static string Thousands(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The word count of every capped section, turn by turn, in slot order: the first draft's, the revision's, the
    /// final turn's and the stored text's, with the stored text's per paragraph; null when no turn was readable.
    /// </summary>
    private static List<BenchmarkReportPackWordCountDto>? WordCountDtos(
        IReadOnlyList<BenchmarkReportSlotWordCount>? draft,
        IReadOnlyList<BenchmarkReportSlotWordCount>? revision,
        IReadOnlyDictionary<string, int> shortened,
        IReadOnlyList<BenchmarkReportSlotWordCount>? final)
    {
        var shape = draft ?? revision ?? final;
        if (shape == null) return null;

        BenchmarkReportSlotWordCount? Of(IReadOnlyList<BenchmarkReportSlotWordCount>? counts, string slot)
            => counts?.FirstOrDefault(c => string.Equals(c.Slot, slot, StringComparison.Ordinal));

        return shape.Select(c => new BenchmarkReportPackWordCountDto
        {
            Location = c.Location,
            LocationLabel = c.LocationLabel,
            Cap = c.Cap,
            Draft = Of(draft, c.Slot)?.Words,
            Revision = Of(revision, c.Slot)?.Words,
            Shortened = shortened.TryGetValue(c.Slot, out int words) ? words : null,
            Final = Of(final, c.Slot)?.Words,
            FinalParagraphs = Of(final, c.Slot)?.Paragraphs.ToList() ?? new List<int>()
        }).ToList();
    }

    /// <summary>The issues counted by rule, ascending: <c>rule 2: 5, rule 16: 12</c>.</summary>
    private static string RuleCounts(IEnumerable<BenchmarkReportValidationNote> issues)
        => string.Join(", ", issues
            .GroupBy(i => i.Rule)
            .OrderBy(g => g.Key)
            .Select(g => "rule " + g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         + ": " + g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The headline's first blocking issue, or null when it passes every blocking check.</summary>
    private static BenchmarkReportValidationNote? BlockingHeadlineIssue(BenchmarkReportAudience audience, string? headline, BenchmarkReportPackPreparation prep)
        => BenchmarkReportPackValidator.HeadlineIssues(audience, headline, prep.Sheet, prep.Content)
            .FirstOrDefault(BenchmarkReportPackValidator.BlocksText);

    /// <summary>The note a rescued headline leaves: rule 7 at <c>headline</c>, marked dropped so the document carries a warning.</summary>
    private static BenchmarkReportValidationNote HeadlineRescueNote(string message)
        => new() { Rule = 7, Location = "headline", Dropped = true, Message = message };

    /// <summary>The last raw reply of the document's turns, for a failed document's diagnostics.</summary>
    private static string? LastReply(IReadOnlyList<WriterTurn> turns)
        => turns.LastOrDefault(t => !string.IsNullOrEmpty(t.FinalText))?.FinalText;

    /// <summary>Removes a replaced document's chart folder; a failure is logged and never fails the write.</summary>
    private async Task DeleteChartFolderAsync(long documentId)
    {
        if (_charts == null || !_charts.IsConfigured) return;
        try
        {
            await _charts.DeleteChartsAsync(documentId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ChartStoreException)
        {
            _logger.LogWarning(ex, "The charts of replaced report document {DocumentId} could not be removed.", documentId);
        }
    }

    /// <summary>
    /// The parsed output (null when unparseable) and every validation issue, a parse failure included. The parse failure
    /// of a reply that stopped at the output limit (<paramref name="cutOffAtTokens"/>) says so.
    /// </summary>
    private static (BenchmarkReportWriterOutput? Output, IReadOnlyList<BenchmarkReportValidationNote> Issues) ParseAndValidate(
        BenchmarkReportAudience audience, string? rawText, BenchmarkReportPackPreparation prep, int? cutOffAtTokens = null)
    {
        var parsed = BenchmarkReportPackParser.Parse(rawText);
        if (!parsed.Success || parsed.Output == null)
        {
            string error = parsed.Error ?? "The reply is not a JSON object.";
            return (null, new[]
            {
                new BenchmarkReportValidationNote
                {
                    Rule = 1,
                    Location = "reply",
                    Message = cutOffAtTokens is int limit
                        ? $"The reply stopped at the output token limit ({Thousands(limit)} tokens, reasoning included), so its JSON is incomplete: {error}"
                        : error
                }
            });
        }
        return (parsed.Output, BenchmarkReportPackValidator.Validate(audience, parsed.Output, prep.Sheet, prep.Content));
    }

    /// <summary>
    /// Sends the request once, records the call's usage under <see cref="UsageRoleContext"/>, and records the
    /// call on the document's progress row as <paramref name="kind"/> (<see cref="BenchmarkReportPackCallKinds"/>),
    /// with the user message a later turn added (<paramref name="requestMessage"/>).
    /// </summary>
    private async Task<WriterTurn> RunTurnAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportPackDocumentProgress progress,
        AgentRunRequest runRequest,
        SystemAiApiConfiguration config,
        ModelPricing? pricing,
        string kind,
        CancellationToken ct,
        string? requestMessage = null)
    {
        var runResult = new AgentRunResult();
        var (messages, characters) = RequestSize(runRequest);
        var startedAtUtc = DateTime.UtcNow;
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
                var (canceledInput, canceledOutput, canceledCost) = await TryRecordUsageAsync(job, progress, config, pricing, runResult, sw.ElapsedMilliseconds);
                RecordCall(job, progress, kind, startedAtUtc, sw.ElapsedMilliseconds, canceledInput, canceledOutput, canceledCost, runResult, messages, characters, "canceled", requestMessage);
            }
            throw;
        }
        catch (Exception ex)
        {
            terminalError = ExceptionDetails.DescribeShort(ex);
        }
        sw.Stop();

        var (inputTokens, outputTokens, cost) = await TryRecordUsageAsync(job, progress, config, pricing, runResult, sw.ElapsedMilliseconds);
        RecordCall(job, progress, kind, startedAtUtc, sw.ElapsedMilliseconds, inputTokens, outputTokens, cost, runResult, messages, characters, terminalError, requestMessage);
        return new WriterTurn(runResult.FinalText, terminalError, inputTokens, outputTokens, sw.ElapsedMilliseconds, cost,
            runResult.ProviderFinishReason, BenchmarkDescriptionService.SumReasoningTokens(runResult));
    }

    /// <summary>
    /// One writer call on the document's progress row: its kind, timing, tokens with their reasoning share, cost, finish
    /// reason and request size, with its raw reply, provider error and a later turn's user message kept on the job for
    /// the writer-replies download.
    /// </summary>
    private static void RecordCall(
        BenchmarkReportPackJob job, BenchmarkReportPackDocumentProgress progress, string kind, DateTime startedAtUtc, long durationMs,
        int inputTokens, int outputTokens, decimal? cost, AgentRunResult runResult, int messages, long characters, string? error,
        string? requestMessage)
    {
        int reasoning = BenchmarkDescriptionService.SumReasoningTokens(runResult);
        job.RecordCall(progress, new BenchmarkReportPackCallDto
        {
            Kind = kind,
            StartedAtUtc = startedAtUtc,
            DurationMs = durationMs,
            InputTokens = inputTokens,
            CachedInputTokens = runResult.CacheReadTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = reasoning > 0 ? reasoning : null,
            CostUsd = cost == null ? null : (double)cost.Value,
            FinishReason = runResult.ProviderFinishReason,
            RequestMessages = messages,
            RequestCharacters = characters
        }, runResult.FinalText, error, requestMessage);
    }

    private async Task<(int InputTokens, int OutputTokens, decimal? Cost)> TryRecordUsageAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportPackDocumentProgress progress,
        SystemAiApiConfiguration config,
        ModelPricing? pricing,
        AgentRunResult runResult,
        long durationMs)
    {
        var (inputTokens, outputTokens, _) = BenchmarkDescriptionService.NormalizeTokens(runResult);
        decimal? cost = pricing == null ? null : BenchmarkDescriptionService.ComputeCost(pricing, runResult, config.ServiceTier);
        job.AddUsage(progress, inputTokens, outputTokens, cost);

        try
        {
            if (runResult.ModelCallUsages.Count > 0)
            {
                await _configService.RecordCallsUsageAsync(
                    config.Id,
                    job.StartedByUserId,
                    roleContext: UsageRoleContext,
                    runResult.ModelCallUsages,
                    runResult.ActualServiceTier,
                    totalDurationMs: (int)Math.Min(int.MaxValue, durationMs));
            }
            else
            {
                // No call reported usage: the tokens are estimates.
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording report-pack usage failed.");
            job.AddLog($"Recording usage failed: {ExceptionDetails.DescribeShort(ex)}", "warning");
        }

        return (inputTokens, outputTokens, cost);
    }

    /// <summary>
    /// A chat consistency note recorded against kept text: C6's "never cites the hours" and C7's
    /// "ruledOut misses an Overseer event" drop nothing, yet the document carries the shortfall.
    /// </summary>
    private static bool IsChatConsistencyDocumentNote(BenchmarkReportValidationNote note)
        => !note.Dropped && note.Rule is BenchmarkReportPackValidator.ChatHoursRule or BenchmarkReportPackValidator.ChatProviderReportRule;

    // ---------------------------------------------------------------------------------------------
    // Chat consistency documents
    // ---------------------------------------------------------------------------------------------

    public const string ChatConsistencyAlreadyWritingMessage = "The reports of this analysis are already being written.";
    public const string ChatConsistencyAllWrittenMessage = "This analysis already has every AI-written report it can have. Delete one first to write it again.";
    public const string ChatConsistencyInvalidAudienceMessage =
        "Only the Executive Summary and the Provider Issue Report are written for a chat consistency analysis.";
    public const string ChatConsistencyNothingInProgressMessage = "No report writing is in progress for this analysis.";
    public const string ChatConsistencyAnalysisGoneMessage = "The chat consistency analysis no longer exists.";

    /// <summary>Rough output sizes per chat consistency document, for the estimate's cost only.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> ChatConsistencyOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 2500,
        [BenchmarkReportAudience.ProviderIssueReport] = 4500
    };

    private enum ChatJobPhase { Queued, Preparing, Writing, Finished }

    /// <summary>One analysis's job, as the Write step shows it. Mutable fields change under the registry's lock.</summary>
    private sealed class ChatConsistencyJobState
    {
        public required int AnalysisId { get; init; }
        public required BenchmarkReportPackJob Job { get; init; }
        public required List<BenchmarkReportAudience> Audiences { get; init; }
        public string WriterProvider { get; init; } = string.Empty;
        public string WriterModelId { get; init; } = string.Empty;
        public string? WriterThinkingLevel { get; init; }

        /// <summary>The <c>analysis.writtenOutOfDate</c> fact of every document; null when the analysis was current.</summary>
        public string? WrittenOutOfDate { get; init; }
        public ChatJobPhase Phase { get; set; } = ChatJobPhase.Queued;
        public BenchmarkRunReportDocumentsStatus Status { get; set; } = BenchmarkRunReportDocumentsStatus.Pending;
        public string? Message { get; set; }
        public DateTime QueuedAtUtc { get; set; }
        public DateTime? SlotAcquiredAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public DateTime? CancelRequestedAtUtc { get; set; }
        public Task Completion { get; set; } = Task.CompletedTask;
    }

    /// <summary>The chat consistency jobs this process knows, by analysis id: in memory only, like a run's.</summary>
    private sealed class ChatConsistencyJobRegistry
    {
        public object Lock { get; } = new();
        public Dictionary<int, ChatConsistencyJobState> Jobs { get; } = new();
    }

    // Kept beside the report-pack slot the jobs queue for, so the registry lives as long as that singleton.
    private static readonly ConditionalWeakTable<BenchmarkReportPackJobManager, ChatConsistencyJobRegistry> ChatConsistencyRegistries = new();

    private ChatConsistencyJobRegistry ChatJobs => ChatConsistencyRegistries.GetValue(_jobManager, _ => new ChatConsistencyJobRegistry());

    /// <summary>
    /// What writing the analysis's documents with the writer would cost, by the Report Pack preview's
    /// arithmetic over the chat consistency prompt, with the writer's refusal or same-provider warning
    /// and whether the Provider Issue Report can be written. Makes no model call. 404 for an unknown
    /// analysis; 400 for a document a chat consistency analysis is not written as. Null or empty
    /// <paramref name="audiences"/> estimates every document it can still have.
    /// </summary>
    public async Task<BenchmarkChatConsistencyReportResult<BenchmarkChatConsistencyReportEstimateDto>> EstimateChatConsistencyDocumentsAsync(
        int analysisId, long writerConfigId, IReadOnlyCollection<BenchmarkReportAudience>? audiences, CancellationToken ct)
    {
        var result = await LoadChatConsistencyAnalysisAsync(analysisId, ct);
        if (result == null) return new(StatusCodes.Status404NotFound);

        var (requested, invalid) = RequestedChatConsistencyAudiences(audiences);
        if (invalid) return new(StatusCodes.Status400BadRequest, Error: ChatConsistencyInvalidAudienceMessage);

        var (available, reason) = BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability(result);
        var toEstimate = requested ?? await MissingChatConsistencyAudiencesAsync(analysisId, available, ct);

        var writer = writerConfigId > 0
            ? await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == writerConfigId, ct)
            : null;
        var candidate = ChatConsistencyCandidate(result);
        var guard = new BenchmarkComplianceGuard(_configuration, _db);
        var estimate = new BenchmarkChatConsistencyReportEstimateDto
        {
            Refusal = BenchmarkRunReportDocumentService.WriterRefusal(writer, candidate, guard),
            ProviderIssueReportAvailable = available,
            ProviderIssueReportReason = reason
        };
        if (estimate.Refusal == null
            && !_endpointPolicy.TryResolveStrict(writer!.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            estimate.Refusal = WriterEndpointRefusal(writer, endpointError);
        }
        string? warning = BenchmarkRunReportDocumentService.WriterWarning(writer, candidate, guard);
        if (estimate.Refusal == null && warning != null)
        {
            estimate.SameProviderWarning = BenchmarkRunReportDocumentService.WriterWarningDto(writer!, candidate, warning);
        }

        int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);
        var pricing = writer != null ? _pricingService.Resolve(writer) : null;
        IReadOnlyList<string>? requestIds = toEstimate.Contains(BenchmarkReportAudience.ProviderIssueReport)
            ? await ChatConsistencySampleRequestIdsAsync(result, ct)
            : null;
        foreach (var audience in toEstimate)
        {
            var sheet = BenchmarkChatConsistencyReportFacts.Build(result, audience, requestIds);
            var prompt = BenchmarkReportPackPrompt.Build(audience, sheet, ChatConsistencyContent());
            int chars = prompt.SystemPrompt.Length + prompt.UserMessage.Length;
            int input = (chars + 3) / 4;
            int output = Math.Min(maxOutputTokens, ChatConsistencyOutputTokens[audience]);
            estimate.Estimates.Add(new BenchmarkReportPackAudienceEstimateDto
            {
                Audience = audience,
                SubjectKey = sheet.SubjectKey,
                PromptChars = chars,
                EstimatedInputTokens = input,
                EstimatedOutputTokens = output,
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.EstimateSingleRequestCost(pricing, input, output),
                EstimatedCostWithRepairUsd = pricing == null ? null : (double)EstimateCostWithRepair(pricing, input, output)
            });
        }
        bool priced = estimate.Estimates.Count > 0 && estimate.Estimates.All(e => e.EstimatedCostUsd != null);
        estimate.EstimatedTotalCostUsd = priced ? estimate.Estimates.Sum(e => e.EstimatedCostUsd!.Value) : null;
        estimate.EstimatedTotalCostWithRepairUsd = priced ? estimate.Estimates.Sum(e => e.EstimatedCostWithRepairUsd!.Value) : null;
        return new(StatusCodes.Status200OK, estimate);
    }

    /// <summary>
    /// Writes the analysis's requested documents, or every one it can still have when none is named,
    /// with the writer, in <see cref="BenchmarkReportSlots.ChatConsistencyAudiences"/> order, stored with
    /// <see cref="BenchmarkReportDocumentOrigin.ChatConsistencyReport"/>. The job queues for the shared
    /// report-pack slot and runs after this returns 202 with the documents it will write. Refusals, in
    /// order: unknown analysis (404); a job for it in progress (409); a document a chat consistency
    /// analysis is not written as (400); the Provider Issue Report while no provider-side finding is
    /// Established or Indicated (400, with the reason); a requested document already written (409), or
    /// with none requested, every one written (409); an unusable writer or the model under report (400);
    /// a writer of the model's provider, unacknowledged (409 with the warning); a refused endpoint (400);
    /// an out-of-date analysis (<see cref="ChatConsistencyAnalysisService.CheckFreshnessAsync"/>) without
    /// <paramref name="acknowledgeOutOfDate"/> (409, <see cref="BenchmarkChatConsistencyReportResult{T}.OutOfDate"/>);
    /// the spend cap (429). Written with the acknowledgment, every document carries <c>analysis.writtenOutOfDate</c>.
    /// </summary>
    public async Task<BenchmarkChatConsistencyReportResult<WriteRunReportDocumentsResponse>> WriteChatConsistencyDocumentsAsync(
        int analysisId,
        long writerConfigId,
        IReadOnlyCollection<BenchmarkReportAudience>? audiences,
        bool acknowledgeSameProvider,
        string? userId,
        CancellationToken ct,
        bool acknowledgeOutOfDate = false)
    {
        var result = await LoadChatConsistencyAnalysisAsync(analysisId, ct);
        if (result == null) return new(StatusCodes.Status404NotFound);

        if (IsChatConsistencyJobActive(analysisId)) return new(StatusCodes.Status409Conflict, Error: ChatConsistencyAlreadyWritingMessage);

        var (requested, invalid) = RequestedChatConsistencyAudiences(audiences);
        if (invalid) return new(StatusCodes.Status400BadRequest, Error: ChatConsistencyInvalidAudienceMessage);

        var (available, reason) = BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability(result);
        if (requested != null && requested.Contains(BenchmarkReportAudience.ProviderIssueReport) && !available)
        {
            return new(StatusCodes.Status400BadRequest, Error: reason ?? BenchmarkChatConsistencyReportFacts.ProviderIssueReportUnavailableReason);
        }

        var missing = await MissingChatConsistencyAudiencesAsync(analysisId, available, ct);
        List<BenchmarkReportAudience> toWrite;
        if (requested == null)
        {
            if (missing.Count == 0) return new(StatusCodes.Status409Conflict, Error: ChatConsistencyAllWrittenMessage);
            toWrite = missing;
        }
        else
        {
            var written = requested.Where(a => !missing.Contains(a)).ToList();
            if (written.Count > 0)
            {
                return new(StatusCodes.Status409Conflict,
                    Error: $"The {BenchmarkReportRenderService.AudienceName(written[0])} is already written. Delete it first to write it again.");
            }
            toWrite = requested;
        }

        var writer = await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == writerConfigId, ct);
        var candidate = ChatConsistencyCandidate(result);
        var guard = new BenchmarkComplianceGuard(_configuration, _db);
        string? refusal = BenchmarkRunReportDocumentService.WriterRefusal(writer, candidate, guard);
        if (refusal != null) return new(StatusCodes.Status400BadRequest, Error: refusal);

        string? warning = BenchmarkRunReportDocumentService.WriterWarning(writer, candidate, guard);
        if (warning != null && !acknowledgeSameProvider)
        {
            return new(StatusCodes.Status409Conflict, Error: warning,
                SameProviderWarning: BenchmarkRunReportDocumentService.WriterWarningDto(writer!, candidate, warning));
        }

        if (!_endpointPolicy.TryResolveStrict(writer!.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            return new(StatusCodes.Status400BadRequest, Error: WriterEndpointRefusal(writer, endpointError));
        }

        var freshness = await ChatConsistencyAnalysis().CheckFreshnessAsync(analysisId, ct);
        string? writtenOutOfDate = null;
        if (freshness is { OutOfDate: true })
        {
            if (!acknowledgeOutOfDate)
            {
                return new(StatusCodes.Status409Conflict,
                    Error: "Analysis #" + analysisId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is out of date: "
                        + OutOfDateReasons(freshness) + ". Analyze again, or confirm to write from it anyway.",
                    OutOfDate: true);
            }

            writtenOutOfDate = WrittenOutOfDateText(freshness);
        }

        var (canSpend, denialReason) = await guard.CanSpendAsync(ct: ct);
        if (!canSpend) return new(StatusCodes.Status429TooManyRequests, Error: denialReason ?? "The benchmark spend guard refused the reports.");

        string subjectKey = BenchmarkChatConsistencyReportFacts.SubjectKeyOf(analysisId);
        var job = new BenchmarkReportPackJob
        {
            SubjectKey = subjectKey,
            SubjectLabel = BenchmarkChatConsistencyReportFacts.SubjectLabelOf(result),
            Scope = BenchmarkReportScope.ChatConsistency,
            SuiteName = string.Join(", ", (result.Baseline?.SuiteNames ?? Array.Empty<string>())
                .Concat(result.Comparison?.SuiteNames ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)),
            WriterConfigId = writer.Id,
            WriterDisplayName = writer.DisplayName ?? writer.ModelId,
            SameProviderAcknowledged = warning != null,
            Request = new BenchmarkReportPackRequest
            {
                SubjectKey = subjectKey,
                Scope = BenchmarkReportScope.ChatConsistency,
                Audiences = toWrite.ToList(),
                WriterModelConfigurationId = writer.Id,
                AcknowledgeSameProvider = acknowledgeSameProvider
            },
            StartedByUserId = userId,
            Cts = new CancellationTokenSource(),
            Documents = toWrite.Select(a => new BenchmarkReportPackDocumentProgress
            {
                Audience = a,
                SubjectKey = subjectKey,
                SubjectLabel = BenchmarkChatConsistencyReportFacts.SubjectLabelOf(result)
            }).ToList()
        };
        var state = new ChatConsistencyJobState
        {
            AnalysisId = analysisId,
            Job = job,
            Audiences = toWrite.ToList(),
            WriterProvider = writer.Provider ?? string.Empty,
            WriterModelId = writer.ModelId ?? string.Empty,
            WriterThinkingLevel = writer.ThinkingLevel,
            WrittenOutOfDate = writtenOutOfDate
        };
        if (writtenOutOfDate != null) job.AddLog("Writing from an out-of-date analysis, as confirmed: " + writtenOutOfDate + ".", "warning");

        var registry = ChatJobs;
        lock (registry.Lock)
        {
            var now = DateTime.UtcNow;
            PruneChatJobs(registry, now);
            if (registry.Jobs.TryGetValue(analysisId, out var existing) && existing.Phase != ChatJobPhase.Finished)
            {
                return new(StatusCodes.Status409Conflict, Error: ChatConsistencyAlreadyWritingMessage);
            }
            state.QueuedAtUtc = now;
            registry.Jobs[analysisId] = state;
        }
        job.AddLog("Queued for the report writer.");

        // The job outlives the request; it writes in a scope of its own.
        state.Completion = Task.Run(() => RunChatConsistencyJobInScopeAsync(state));

        return new(StatusCodes.Status202Accepted, new WriteRunReportDocumentsResponse
        {
            RunId = analysisId,
            Status = BenchmarkRunReportDocumentsStatus.Pending,
            Audiences = toWrite.ToList()
        });
    }

    /// <summary>
    /// The analysis's current or last report-writing job: 200 with its view (its <c>runId</c> the
    /// analysis id), 204 when this process knows none (none since the last restart, or its finished job
    /// has expired), 404 for an unknown analysis.
    /// </summary>
    public async Task<BenchmarkChatConsistencyReportResult<BenchmarkRunReportJobDto>> GetChatConsistencyJobAsync(int analysisId, CancellationToken ct)
    {
        if (!await _db.ChatConsistencyAnalyses.AsNoTracking().AnyAsync(a => a.Id == analysisId, ct)) return new(StatusCodes.Status404NotFound);

        var view = ChatConsistencyJobView(analysisId, DateTime.UtcNow);
        return view == null ? new(StatusCodes.Status204NoContent) : new(StatusCodes.Status200OK, view);
    }

    /// <summary>
    /// Cancels the analysis's report-writing job: 202 with its view once asked; 409 when none is in
    /// progress; 404 for an unknown analysis. A queued job leaves the queue; a job that is writing keeps
    /// every document already stored.
    /// </summary>
    public async Task<BenchmarkChatConsistencyReportResult<BenchmarkRunReportJobDto>> CancelChatConsistencyJobAsync(int analysisId, CancellationToken ct)
    {
        if (!await _db.ChatConsistencyAnalyses.AsNoTracking().AnyAsync(a => a.Id == analysisId, ct)) return new(StatusCodes.Status404NotFound);

        BenchmarkReportPackJob job;
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            if (!registry.Jobs.TryGetValue(analysisId, out var state) || state.Phase == ChatJobPhase.Finished)
            {
                return new(StatusCodes.Status409Conflict, Error: ChatConsistencyNothingInProgressMessage);
            }

            job = state.Job;
            if (state.CancelRequestedAtUtc == null)
            {
                state.CancelRequestedAtUtc = DateTime.UtcNow;
                job.AddLog("Cancellation requested.", "warning");
            }
        }

        // Outside the lock: cancellation callbacks may run the job's continuations inline.
        try
        {
            job.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        return new(StatusCodes.Status202Accepted, ChatConsistencyJobView(analysisId, DateTime.UtcNow));
    }

    /// <summary>The task of the analysis's current or last job; a finished task when this process knows none.</summary>
    internal Task ChatConsistencyJobCompletion(int analysisId)
    {
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            return registry.Jobs.TryGetValue(analysisId, out var state) ? state.Completion : Task.CompletedTask;
        }
    }

    private bool IsChatConsistencyJobActive(int analysisId)
    {
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            return registry.Jobs.TryGetValue(analysisId, out var state) && state.Phase != ChatJobPhase.Finished;
        }
    }

    /// <summary>
    /// The job view of the run report contract, with the analysis id in <see cref="BenchmarkRunReportJobDto.RunId"/>
    /// and the job's own status and message; null when this process knows no job for the analysis.
    /// </summary>
    private BenchmarkRunReportJobDto? ChatConsistencyJobView(int analysisId, DateTime nowUtc)
    {
        ChatConsistencyJobState state;
        ChatJobPhase phase;
        BenchmarkRunReportDocumentsStatus status;
        string? message;
        DateTime queuedAt;
        DateTime? slotAcquiredAt, finishedAt, cancelRequestedAt;
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            PruneChatJobs(registry, nowUtc);
            if (!registry.Jobs.TryGetValue(analysisId, out var found)) return null;
            state = found;
            phase = state.Phase;
            status = state.Status;
            message = state.Message;
            queuedAt = state.QueuedAtUtc;
            slotAcquiredAt = state.SlotAcquiredAtUtc;
            finishedAt = state.FinishedAtUtc;
            cancelRequestedAt = state.CancelRequestedAtUtc;
        }

        // Outside the lock: the job manager has its own, and neither lock is taken inside the other.
        var (ahead, running) = _jobManager.QueueInfo(state.Job);
        bool queued = phase == ChatJobPhase.Queued;

        return new BenchmarkRunReportJobDto
        {
            RunId = analysisId,
            Status = status,
            Message = message,
            Phase = phase.ToString(),
            QueuedAtUtc = queuedAt,
            SlotAcquiredAtUtc = slotAcquiredAt,
            FinishedAtUtc = finishedAt,
            CancelRequestedAtUtc = cancelRequestedAt,
            JobsAhead = queued ? ahead : null,
            BlockingJobLabel = queued && running != null && !ReferenceEquals(running, state.Job) ? ChatBlockingLabel(running) : null,
            Audiences = state.Audiences.ToList(),
            WriterConfigId = state.Job.WriterConfigId,
            WriterDisplayName = state.Job.WriterDisplayName,
            WriterProvider = state.WriterProvider,
            WriterModelId = state.WriterModelId,
            WriterThinkingLevel = state.WriterThinkingLevel,
            Job = state.Job.ToDto(),
            ServerTimeUtc = nowUtc
        };
    }

    /// <summary>The running job that holds the slot, named by its subject: <c>Run #4: …</c>, <c>Chat consistency analysis #7: …</c> or <c>Report Pack: …</c>.</summary>
    private static string ChatBlockingLabel(BenchmarkReportPackJob running)
    {
        string key = running.SubjectKey ?? string.Empty;
        if (BenchmarkRunReportDocumentService.TryParseSubjectKey(key, out long runId))
        {
            return "Run #" + runId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + running.SubjectLabel;
        }
        if (key.StartsWith(BenchmarkChatConsistencyReportFacts.SubjectKeyPrefix, StringComparison.Ordinal))
        {
            return "Chat consistency analysis #" + key[BenchmarkChatConsistencyReportFacts.SubjectKeyPrefix.Length..] + ": " + running.SubjectLabel;
        }
        return "Report Pack: " + running.SubjectLabel;
    }

    /// <summary>Drops finished jobs older than <see cref="BenchmarkRunReportDocumentService.FinishedJobRetention"/>. The caller holds the lock.</summary>
    private static void PruneChatJobs(ChatConsistencyJobRegistry registry, DateTime nowUtc)
    {
        var expired = registry.Jobs
            .Where(e => e.Value.Phase == ChatJobPhase.Finished && e.Value.FinishedAtUtc is DateTime finished
                        && nowUtc - finished >= BenchmarkRunReportDocumentService.FinishedJobRetention)
            .Select(e => e.Key)
            .ToList();
        foreach (int id in expired) registry.Jobs.Remove(id);
    }

    private void SetChatPhase(ChatConsistencyJobState state, ChatJobPhase phase, BenchmarkRunReportDocumentsStatus? status = null, string? message = null)
    {
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            if (state.Phase == ChatJobPhase.Finished) return;
            state.Phase = phase;
            if (status != null)
            {
                state.Status = status.Value;
                state.Message = message == null || message.Length <= BenchmarkRunReportDocumentService.MaxMessageLength
                    ? message
                    : message[..BenchmarkRunReportDocumentService.MaxMessageLength];
            }
            if (phase == ChatJobPhase.Preparing) state.SlotAcquiredAtUtc = DateTime.UtcNow;
            if (phase == ChatJobPhase.Finished) state.FinishedAtUtc = DateTime.UtcNow;
        }
    }

    private ChatJobPhase ChatPhaseOf(ChatConsistencyJobState state)
    {
        var registry = ChatJobs;
        lock (registry.Lock)
        {
            return state.Phase;
        }
    }

    /// <summary>The job in a scope of its own; without a scope factory, on this instance.</summary>
    private async Task RunChatConsistencyJobInScopeAsync(ChatConsistencyJobState state)
    {
        try
        {
            if (_scopeFactory == null)
            {
                await RunChatConsistencyJobAsync(state);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<BenchmarkReportPackService>();
            await service.RunChatConsistencyJobAsync(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The chat consistency documents of analysis {AnalysisId} failed.", state.AnalysisId);
            if (state.Job.Status == BenchmarkReportPackJobStatus.Running) state.Job.SetStatus(BenchmarkReportPackJobStatus.Failed);
            SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Failed,
                "The reports could not be written: " + ExceptionDetails.DescribeShort(ex));
        }
    }

    /// <summary>
    /// Waits for the report-pack slot, checks the spend guard, the writer and the analysis, captures the
    /// writer's settings, writes the documents and settles the job's status as a run's job settles it.
    /// </summary>
    private async Task RunChatConsistencyJobAsync(ChatConsistencyJobState state)
    {
        var job = state.Job;
        try
        {
            await _jobManager.WaitForSlotAsync(job, job.Cts.Token);
            SetChatPhase(state, ChatJobPhase.Preparing);

            var (canSpend, denialReason) = await new BenchmarkComplianceGuard(_configuration, _db).CanSpendAsync(_db);
            if (!canSpend)
            {
                string reason = denialReason ?? "The benchmark spend guard refused the reports.";
                job.AddLog(reason, "warning");
                SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Skipped, reason);
                return;
            }

            var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == job.WriterConfigId);
            if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey))
            {
                job.AddLog(BenchmarkRunReportDocumentService.WriterUnavailableMessage, "error");
                SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Failed, BenchmarkRunReportDocumentService.WriterUnavailableMessage);
                return;
            }

            var result = await LoadChatConsistencyAnalysisAsync(state.AnalysisId, CancellationToken.None);
            if (result == null)
            {
                job.AddLog(ChatConsistencyAnalysisGoneMessage, "error");
                SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Failed, ChatConsistencyAnalysisGoneMessage);
                return;
            }

            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(_db, writer, CancellationToken.None);
            job.WriterSnapshotId = snapshot.Id;

            job.Cts.Token.ThrowIfCancellationRequested();
            SetChatPhase(state, ChatJobPhase.Writing, BenchmarkRunReportDocumentsStatus.Writing);
            job.AddLog($"Writing the chat consistency documents of analysis #{state.AnalysisId.ToString(System.Globalization.CultureInfo.InvariantCulture)} with {job.WriterDisplayName}.");

            await WriteChatConsistencyJobDocumentsAsync(job, result, state.WrittenOutOfDate, job.Cts.Token);

            var (status, message) = BenchmarkRunReportDocumentService.OutcomeOf(job);
            if (status == BenchmarkRunReportDocumentsStatus.Canceled) job.AddLog(message!, "warning");
            SetChatPhase(state, ChatJobPhase.Finished, status, message);
        }
        catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
        {
            bool writing = ChatPhaseOf(state) == ChatJobPhase.Writing;
            foreach (var d in job.Documents.ToList().Where(d => d.Status is BenchmarkReportPackDocumentStatus.Pending
                         or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing))
            {
                job.SetDocumentStatus(d, BenchmarkReportPackDocumentStatus.Canceled);
            }
            job.SetStatus(BenchmarkReportPackJobStatus.Canceled);

            string message = writing ? BenchmarkRunReportDocumentService.OutcomeOf(job).Message! : BenchmarkRunReportDocumentService.CanceledBeforeWritingMessage;
            job.AddLog(message, "warning");
            SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Canceled, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The chat consistency documents of analysis {AnalysisId} failed.", state.AnalysisId);
            SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Failed,
                "The reports could not be written: " + ExceptionDetails.DescribeShort(ex));
        }
        finally
        {
            if (job.Status == BenchmarkReportPackJobStatus.Running) job.SetStatus(BenchmarkReportPackJobStatus.Failed);
            if (ChatPhaseOf(state) != ChatJobPhase.Finished)
            {
                SetChatPhase(state, ChatJobPhase.Finished, BenchmarkRunReportDocumentsStatus.Failed, "The reports could not be written.");
            }
        }
    }

    /// <summary>
    /// Writes every document on the job's list about the analysis, one after another, through the
    /// Report Pack's own path: one fact sheet per document (the Provider Issue Report's with its sample
    /// request ids), validation, one repair turn, drops, storage and usage rows. The job must already
    /// hold the report-pack slot; it ends Completed, CompletedWithErrors, Failed or Canceled.
    /// </summary>
    private async Task WriteChatConsistencyJobDocumentsAsync(
        BenchmarkReportPackJob job, ChatConsistencyAnalysisResult result, string? writtenOutOfDate, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var (binding, bindFailure) = await BindWriterAsync(job, ct);
            if (binding == null)
            {
                FailAll(job, bindFailure ?? "Writer model configuration not found or disabled.");
                return;
            }
            var (config, endpoint, apiKey, pricing, maxOutputTokens) = binding;

            var (targets, controls) = await ChatConsistencyRunsAsync(result, ct);
            IReadOnlyList<string>? requestIds = null;
            var state = new JobState { SeveralSubjects = false };

            int completed = 0;
            int failed = 0;
            foreach (var row in job.Documents.ToList())
            {
                ct.ThrowIfCancellationRequested();
                job.SetDocumentPhase(row, BenchmarkReportPackPhases.Preparing);
                if (row.Audience == BenchmarkReportAudience.ProviderIssueReport)
                {
                    requestIds ??= await ChatConsistencySampleRequestIdsAsync(result, ct);
                }

                var sheet = BenchmarkChatConsistencyReportFacts.Build(result, row.Audience,
                    row.Audience == BenchmarkReportAudience.ProviderIssueReport ? requestIds : null, writtenOutOfDate);
                var prep = ChatConsistencyPreparation(sheet, targets, controls);
                if (string.IsNullOrEmpty(row.SubjectKey)) row.SubjectKey = sheet.SubjectKey;
                if (string.IsNullOrEmpty(row.SubjectLabel)) row.SubjectLabel = sheet.SubjectLabel;
                job.AddLog($"Fact sheet computed: {sheet.Facts.Count} facts, {sheet.Peers.Count} control models.");

                bool ok = await WriteDocumentAsync(job, row, BenchmarkReportDocumentOrigin.ChatConsistencyReport, prep, state,
                    config, endpoint, apiKey, pricing, excerptChars: 0, maxOutputTokens, ct);
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
                         or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing).ToList())
            {
                job.SetDocumentStatus(d, BenchmarkReportPackDocumentStatus.Canceled);
            }
            job.AddLog("Report pack generation was canceled.", "warning");
            job.SetStatus(BenchmarkReportPackJobStatus.Canceled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat consistency report job {JobId} failed.", job.Id);
            job.AddLog($"Unexpected failure: {ExceptionDetails.DescribeShort(ex)}", "error");
            job.SetStatus(BenchmarkReportPackJobStatus.Failed);
        }
    }

    /// <summary>
    /// A chat consistency document's preparation: its sheet, an empty content snapshot (the document
    /// quotes no question), the target runs as the subject's and the control runs as peers.
    /// </summary>
    private static BenchmarkReportPackPreparation ChatConsistencyPreparation(
        BenchmarkReportFactSheet sheet, IReadOnlyList<BenchmarkRun> targets, IReadOnlyList<BenchmarkRun> controls)
    {
        var subject = new BenchmarkModelComparisonEntryDto
        {
            Key = sheet.SubjectKey,
            Label = sheet.SubjectLabel,
            Provider = sheet.SubjectProvider,
            ModelId = sheet.SubjectModelId,
            ThinkingLevel = sheet.SubjectThinkingLevel
        };
        return new BenchmarkReportPackPreparation
        {
            Comparison = new BenchmarkModelComparisonDto(),
            Subject = subject,
            Sheet = sheet,
            Content = ChatConsistencyContent(),
            Scope = BenchmarkReportScope.ChatConsistency,
            Covered = new[] { subject },
            CoveredEntryKeys = new[] { sheet.SubjectKey },
            SubjectRuns = targets,
            PeerRuns = controls
        };
    }

    /// <summary>The content snapshot of a chat consistency document: empty, since it quotes no question or answer.</summary>
    private static BenchmarkReportContentSnapshot ChatConsistencyContent() => new() { AnswerExcerptChars = 0 };

    /// <summary>The analysis's target and control runs that still exist, each ascending, for the documents' fingerprint rows.</summary>
    private async Task<(List<BenchmarkRun> Targets, List<BenchmarkRun> Controls)> ChatConsistencyRunsAsync(ChatConsistencyAnalysisResult result, CancellationToken ct)
    {
        var documentRuns = BenchmarkChatConsistencyReportFacts.DocumentRuns(result);
        var ids = documentRuns.Select(r => r.RunId).Distinct().ToList();
        var runs = ids.Count == 0
            ? new Dictionary<long, BenchmarkRun>()
            : await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var targets = documentRuns.Where(r => !r.IsPeer && runs.ContainsKey(r.RunId)).Select(r => runs[r.RunId]).ToList();
        var targetIds = targets.Select(r => r.Id).ToHashSet();
        var controls = documentRuns.Where(r => r.IsPeer && runs.ContainsKey(r.RunId) && !targetIds.Contains(r.RunId)).Select(r => runs[r.RunId]).ToList();
        return (targets, controls);
    }

    /// <summary>
    /// The Provider Issue Report's sample request ids: at most ten distinct non-empty provider request
    /// ids of the candidate calls of the analysis's comparison-period runs, newest first.
    /// </summary>
    private async Task<IReadOnlyList<string>> ChatConsistencySampleRequestIdsAsync(ChatConsistencyAnalysisResult result, CancellationToken ct)
    {
        var runIds = (result.Comparison?.RunIds ?? Array.Empty<long>()).Distinct().ToList();
        if (runIds.Count == 0) return Array.Empty<string>();

        var ids = await _db.ModelCallTelemetry
            .AsNoTracking()
            .Where(m => m.Source == ModelCallSource.BenchmarkCandidate && m.RequestId != null && m.RequestId != string.Empty)
            .Where(m => (m.BenchmarkRunId != null && runIds.Contains(m.BenchmarkRunId.Value))
                        || (m.BenchmarkRunAnswer != null && runIds.Contains(m.BenchmarkRunAnswer.BenchmarkRunId)))
            .OrderByDescending(m => m.StartedAtUtc)
            .ThenByDescending(m => m.Id)
            .Select(m => m.RequestId!)
            .Take(BenchmarkChatConsistencyReportFacts.MaxSampleRequestIds * 20)
            .ToListAsync(ct);
        return BenchmarkChatConsistencyReportFacts.SampleOf(ids);
    }

    /// <summary>The saved analysis with its id, as the chat consistency API returns it; null when there is none.</summary>
    private Task<ChatConsistencyAnalysisResult?> LoadChatConsistencyAnalysisAsync(int analysisId, CancellationToken ct)
        => ChatConsistencyAnalysis().GetAnalysisAsync(analysisId, ct);

    private ChatConsistencyAnalysisService ChatConsistencyAnalysis()
        => new(_db, new ChatConsistencyEvidenceBuilder(_db, _pricingService), NullLogger<ChatConsistencyAnalysisService>.Instance);

    /// <summary>The reasons of an out-of-date analysis, as the refusal and the documents' box state them.</summary>
    public static string OutOfDateReasons(ChatConsistencyFreshness freshness)
    {
        var reasons = new List<string>();
        if (freshness.EarlierAnalysisCode)
        {
            reasons.Add("it was saved under analysis code version " + freshness.AnalysisCodeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", and Overseer now analyzes under version " + freshness.CurrentAnalysisCodeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (freshness.InputsChanged == true) reasons.Add("its runs, grades, controls, annotations or prices changed after it was saved");
        return string.Join("; and ", reasons);
    }

    /// <summary>The <c>analysis.writtenOutOfDate</c> fact: "earlier analysis code (version n)", "changed inputs", or both.</summary>
    public static string WrittenOutOfDateText(ChatConsistencyFreshness freshness)
    {
        var parts = new List<string>();
        if (freshness.EarlierAnalysisCode)
        {
            parts.Add("earlier analysis code (version " + freshness.AnalysisCodeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }
        if (freshness.InputsChanged == true) parts.Add("changed inputs");
        return string.Join(" and ", parts);
    }

    /// <summary>
    /// The documents the analysis has no chat consistency document for, in
    /// <see cref="BenchmarkReportSlots.ChatConsistencyAudiences"/> order; the Provider Issue Report only
    /// while it is available.
    /// </summary>
    private async Task<List<BenchmarkReportAudience>> MissingChatConsistencyAudiencesAsync(int analysisId, bool providerIssueReportAvailable, CancellationToken ct)
    {
        var written = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.ChatConsistencyAnalysisId == analysisId && d.Origin == BenchmarkReportDocumentOrigin.ChatConsistencyReport)
            .Select(d => d.Audience)
            .Distinct()
            .ToListAsync(ct);
        return BenchmarkReportSlots.ChatConsistencyAudiences
            .Where(a => !written.Contains(a))
            .Where(a => a != BenchmarkReportAudience.ProviderIssueReport || providerIssueReportAvailable)
            .ToList();
    }

    /// <summary>
    /// The requested documents in <see cref="BenchmarkReportSlots.ChatConsistencyAudiences"/> order, or
    /// null when none is named; invalid when one is not a chat consistency document.
    /// </summary>
    private static (List<BenchmarkReportAudience>? Audiences, bool Invalid) RequestedChatConsistencyAudiences(IReadOnlyCollection<BenchmarkReportAudience>? audiences)
    {
        if (audiences == null || audiences.Count == 0) return (null, false);
        if (audiences.Any(a => !BenchmarkReportSlots.ChatConsistencyAudiences.Contains(a))) return (null, true);
        return (BenchmarkReportSlots.ChatConsistencyAudiences.Where(audiences.Contains).ToList(), false);
    }

    /// <summary>A stand-in configuration carrying the analysis subject's provider and model id, for the writer checks.</summary>
    private static SystemAiApiConfiguration ChatConsistencyCandidate(ChatConsistencyAnalysisResult result) => new()
    {
        Provider = result.Subject?.Provider ?? string.Empty,
        ModelId = result.Subject?.ModelId ?? string.Empty,
        DisplayName = BenchmarkChatConsistencyReportFacts.SubjectLabelOf(result)
    };

    private static string WriterEndpointRefusal(SystemAiApiConfiguration writer, string? endpointError)
        => $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";

    private static BenchmarkReportDocumentRun Fingerprint(BenchmarkRun run, bool isPeer) => new()
    {
        RunId = run.Id,
        IsPeer = isPeer,
        FinalScore = run.FinalScore,
        QualityIndex = run.QualityIndex,
        SpeedIndex = run.SpeedIndex,
        ScoringMethodVersion = run.ScoringMethodVersion,
        RerunCompletedAtUtc = run.RerunCompletedAtUtc,
        SynthesisSha256 = run.SynthesisFingerprint
            ?? BenchmarkReportRenderService.SynthesisSha256(run.AssessmentJson, run.CoAssessorSynthesisJson)
    };

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>
/// What a chat consistency report request answered: its HTTP status, the value of a 200 or 202, the
/// refusal of any other status, and the same-provider warning of an unacknowledged writer (409).
/// </summary>
public sealed record BenchmarkChatConsistencyReportResult<T>(
    int StatusCode, T? Value = null, string? Error = null, SameProviderWarningDto? SameProviderWarning = null, bool OutOfDate = false)
    where T : class;

/// <summary>The run report estimate of a chat consistency analysis, with whether its Provider Issue Report can be written.</summary>
public class BenchmarkChatConsistencyReportEstimateDto : BenchmarkRunReportEstimateDto
{
    /// <summary>At least one provider-side attribution is graded Established or Indicated.</summary>
    public bool ProviderIssueReportAvailable { get; set; }

    /// <summary>Why the Provider Issue Report cannot be written; null when it can.</summary>
    public string? ProviderIssueReportReason { get; set; }
}
