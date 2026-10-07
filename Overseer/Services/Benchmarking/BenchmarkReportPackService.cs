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
using Overseer.Services.ChatConsistency;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

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
    public const int DefaultMaxOutputTokens = 16000;

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
                BatteryDetailQuestionsPerSuite(configuration), BatteryMaxPromptChars(configuration), ct)
            : await PrepareRunsAsync(db, comparison!, subject, answerExcerptChars, ct);
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

    /// <summary>A run or group subject: the comparison's runs loaded with their answers, the fact sheet and the content.</summary>
    private static async Task<(BenchmarkReportPackPreparation? Preparation, string? Refusal)> PrepareRunsAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonDto comparison,
        BenchmarkModelComparisonEntryDto subject,
        int answerExcerptChars,
        CancellationToken ct)
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
            AnswerOutcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, subjectRunIds, withRefutedSentences: false, ct),
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

    /// <summary>
    /// A comparison-scope preparation over the request's covered entries, or every entry that is not
    /// Excluded when it names none: each covered model's per-model sheet and content over the covered
    /// models only, the paired-test families requested for the covered models only, so each Holm
    /// adjustment counts the tests the document reports, and the comparison sheet built from them
    /// (<see cref="BenchmarkComparisonReportFacts.Build"/>). Refused when a covered entry is not in the
    /// comparison or is Excluded, or the covered models are fewer than two or more than twelve. Over
    /// <paramref name="comparison"/> when it is given; computed from the request otherwise. Makes no
    /// model call.
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
        BenchmarkModelComparisonDto? comparison = null)
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
            ?? BenchmarkComparisonReportFacts.BoundsRefusal(covered.Count);
        if (refusal != null) return (null, refusal);

        var restricted = BenchmarkComparisonReportFacts.Restrict(comparison, covered);
        var (reference, allPairs, pairedUnavailable) = await PairedFamiliesAsync(
            pairedTests ?? new BenchmarkPairedTestsService(db, comparisonService), restricted, request.PricingBasis, ct);

        bool batteries = string.Equals(comparison.SubjectKind, BenchmarkModelComparisonSubjectKinds.Batteries, StringComparison.Ordinal);
        BenchmarkComparisonReportFactsResult built;
        List<BenchmarkRun> runs;
        if (batteries)
        {
            var (entries, batteryRuns, batteryRefusal) = await BatteryEntriesAsync(db, restricted, answerExcerptChars, BatteryDetailQuestionsPerSuite(configuration), ct);
            if (entries == null) return (null, batteryRefusal);
            runs = batteryRuns;
            built = BenchmarkComparisonReportFacts.Build(new BenchmarkComparisonReportFactsInput
            {
                Comparison = comparison,
                Entries = entries,
                ReferenceFamily = reference,
                AllPairsFamily = allPairs,
                PairedTestsUnavailableReason = pairedUnavailable,
                AnswerExcerptChars = answerExcerptChars,
                ComparisonId = comparisonId
            });
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
            built = BenchmarkComparisonReportFacts.BuildFromRuns(
                comparison, covered, runs.ToDictionary(r => r.Id), answerExcerptChars, reference, allPairs, pairedUnavailable, comparisonId);
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
            SubjectRuns = runs,
            PeerRuns = Array.Empty<BenchmarkRun>(),
            Notes = built.Notes
        }, null);
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
        BenchmarkPairedTestsService? pairedTests = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope == BenchmarkReportScope.Comparison)
        {
            return PrepareComparisonAsync(db, comparisonService, request, answerExcerptChars, comparisonId, ct, configuration, pairedTests);
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
    /// battery analyses loaded once, the members' answers loaded once, and each result's tool-call
    /// outcomes. The prompt budget is applied over the whole comparison, so none is applied here.
    /// </summary>
    private static async Task<(List<BenchmarkComparisonReportEntry>? Entries, List<BenchmarkRun> Runs, string? Refusal)> BatteryEntriesAsync(
        ApplicationDbContext db, BenchmarkModelComparisonDto restricted, int answerExcerptChars, int detailQuestionsPerSuite, CancellationToken ct)
    {
        var batteryRunIds = restricted.Entries.Where(e => e.BatteryRunId.HasValue).Select(e => e.BatteryRunId!.Value).Distinct().ToList();
        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(db, batteryRunIds, ct);
        if (sources == null) return (null, new List<BenchmarkRun>(), error ?? "The battery results could not be loaded.");

        var runIds = restricted.Entries.SelectMany(e => e.RunIds).Distinct().ToList();
        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Answers)
            .Where(r => runIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        var runsById = runs.ToDictionary(r => r.Id);

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
                MaxPromptChars = 0
            });
            if (built.Sheet == null || built.Content == null)
            {
                return (null, runs, entry.Label + ": " + (built.Refusal ?? "The fact sheet could not be computed."));
            }
            entries.Add(new BenchmarkComparisonReportEntry { Entry = entry, Sheet = built.Sheet, Content = built.Content });
        }

        return (entries, runs, null);
    }

    /// <summary>
    /// The paired-test families of the covered models, from the service the wizard's Paired tests view
    /// uses, requested for the covered models only: against the highest-Index one, and over all pairs
    /// from three to <see cref="BenchmarkComparisonReportFacts.AllPairsMaxEntries"/> models. A refusal or a
    /// failure is the reason the sheet states instead.
    /// </summary>
    private static async Task<(BenchmarkPairedComparisonDto? Reference, BenchmarkPairedComparisonDto? AllPairs, string? Unavailable)> PairedFamiliesAsync(
        BenchmarkPairedTestsService pairedTests, BenchmarkModelComparisonDto restricted, BenchmarkModelComparisonPricingBasis basis, CancellationToken ct)
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
            ReferenceKey = lettered[0].Key,
            Recompute = true
        };

        try
        {
            var (reference, error) = await pairedTests.CompareAsync(request, ct);
            if (reference == null) return (null, null, error ?? "The paired tests could not be computed.");

            BenchmarkPairedComparisonDto? allPairs = null;
            if (BenchmarkComparisonReportFacts.UsesAllPairs(lettered.Count))
            {
                request.Mode = BenchmarkPairedComparisonMode.AllPairs;
                (allPairs, _) = await pairedTests.CompareAsync(request, ct);
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
    /// job's documents and its writer; the job must already hold the report-pack slot. A writer that
    /// writes run-completion documents only refuses with <see cref="NotSupportedException"/>.
    /// </summary>
    Task WriteBatteryCompletionDocumentsAsync(long batteryRunId, BenchmarkReportPackJob job, CancellationToken ct)
        => Task.FromException(new NotSupportedException("This report writer does not write battery-completion documents."));
}

/// <summary>
/// Writes report-pack documents: one writer call per document, one repair turn when validation
/// fails, then drop-and-notice. A job writes its subjects one after another: each subject of a
/// model-scope job, sharing one computation of the comparison, or the one covered set of a
/// comparison-scope job, whose question topics are written once and given to its later documents.
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

    /// <param name="scopeFactory">
    /// Opens the scope a chat consistency job writes in once its request has ended; without one (in
    /// tests) the job runs on this instance.
    /// </param>
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
        IServiceScopeFactory? scopeFactory = null)
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
            int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);

            var subjects = SubjectsOf(job);
            var state = new JobState { SeveralSubjects = subjects.Count > 1 };
            if (origin == BenchmarkReportDocumentOrigin.ReportPack)
            {
                await EnsureComparisonAsync(job, state, ct);
            }

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
            var (config, endpoint, apiKey, pricing) = binding;

            int completed = 0;
            int failed = 0;
            for (int i = 0; i < subjects.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (subjectKey, rows) = subjects[i];
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

    /// <summary>The writer a job writes with: its configuration bound to the job's snapshot, its endpoint, key and price card.</summary>
    private sealed record WriterBinding(SystemAiApiConfiguration Config, AiEndpointDescriptor Endpoint, string ApiKey, ModelPricing? Pricing);

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

        string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");
        var pricing = _pricingService.Resolve(config);
        if (pricing == null)
        {
            job.AddLog("No price card resolves for the writer; document costs are not recorded.", "warning");
        }

        return (new WriterBinding(config, endpoint!, apiKey, pricing), null);
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
                new BenchmarkPairedTestsService(_db, _comparisonService, null, _pricingService));
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

    private static void FailAll(BenchmarkReportPackJob job, string message)
    {
        job.AddLog(message, "error");
        foreach (var d in job.Documents.ToList())
        {
            job.SetDocumentStatus(d, BenchmarkReportPackDocumentStatus.Failed, message);
        }
        job.SetStatus(BenchmarkReportPackJobStatus.Failed);
    }

    /// <summary>Writes, validates, repairs once, drops what still fails, and persists one document. True when stored.</summary>
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
        var spec = BenchmarkReportSlots.For(audience, prep.Scope);
        string name = BenchmarkReportRenderService.AudienceName(audience) + (state.SeveralSubjects ? " about " + prep.Sheet.SubjectLabel : string.Empty);
        job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Writing);
        job.AddLog($"Writing the {name} with {config.DisplayName}...");

        // A comparison-scope document set writes its question topics once and gives them to its later documents.
        var sharedTopics = comparisonScope && spec.RequiresQuestionTopics && job.SharedTopics.TryGetValue(prep.Sheet.SubjectKey, out var topics)
            ? topics
            : null;

        var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content, sharedTopics);
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

        var first = await RunTurnAsync(job, progress, runRequest, config, pricing, ct);
        var turns = new List<WriterTurn> { first };
        if (first.Error != null)
        {
            job.AddLog($"{name}: provider error: {first.Error}", "error");
            job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Failed, first.Error);
            return false;
        }

        var (output, issues) = ParseAndValidate(audience, first.FinalText, prep, sharedTopics);
        if (issues.Count > 0)
        {
            job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Repairing);
            job.AddLog($"{name}: {issues.Count} validation issue(s); sending one repair turn.", "warning");

            runRequest.SeedHistory.Add(new { role = "assistant", content = first.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = BenchmarkReportPackPrompt.BuildRepairMessage(issues, prep.Scope) });

            var repair = await RunTurnAsync(job, progress, runRequest, config, pricing, ct);
            turns.Add(repair);
            if (repair.Error != null)
            {
                job.AddLog($"{name}: provider error on the repair turn: {repair.Error}", "error");
                if (output == null)
                {
                    job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Failed, repair.Error);
                    return false;
                }
            }
            else
            {
                var (repairedOutput, repairedIssues) = ParseAndValidate(audience, repair.FinalText, prep, sharedTopics);
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
            job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Failed, message);
            return false;
        }

        var notes = new List<BenchmarkReportValidationNote>(prep.Notes);
        if (issues.Count > 0)
        {
            var cleaned = BenchmarkReportPackValidator.DropInvalid(audience, output, prep.Sheet, prep.Content, sharedTopics);
            if (cleaned.Fatal)
            {
                string reason = cleaned.FatalReason ?? "Required content failed validation after the repair turn.";
                job.AddLog($"{name}: {reason}", "error");
                job.SetDocumentStatus(progress, BenchmarkReportPackDocumentStatus.Failed, reason);
                return false;
            }
            output = cleaned.Output;
            notes.AddRange(cleaned.Notes);
            job.AddLog($"{name}: {notes.Count(n => n.Dropped)} item(s) removed by validation.", "warning");
        }

        if (comparisonScope && spec.RequiresQuestionTopics)
        {
            if (sharedTopics != null)
            {
                output.QuestionTopics = sharedTopics.Select(t => new BenchmarkReportQuestionTopic { Question = t.Question, Topic = t.Topic }).ToList();
            }
            else if (output.QuestionTopics.Count > 0)
            {
                job.SharedTopics[prep.Sheet.SubjectKey] = output.QuestionTopics
                    .Select(t => new BenchmarkReportQuestionTopic { Question = t.Question, Topic = t.Topic })
                    .ToList();
            }
        }

        // A warning note (rules 12 to 19 and 21) keeps its text but still marks the document, and so does
        // a chat consistency document-level note (C6 or C7 recorded against the kept text).
        bool chatConsistency = prep.Scope == BenchmarkReportScope.ChatConsistency;
        var status = notes.Any(n => n.Dropped || BenchmarkReportPackValidator.IsWarningRule(n.Rule)
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
        var replaced = reportPack
            ? await ReplacedDocumentsAsync(job, prep, audience, document.SubjectKey, comparisonKey, ct)
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
        return true;
    }

    /// <summary>
    /// The documents of <c>ReplaceDocumentIds</c> this new document replaces: Report Pack documents of
    /// the same audience and comparison, of the same covered set (comparison scope) or subject (model scope).
    /// </summary>
    private async Task<List<BenchmarkReportDocument>> ReplacedDocumentsAsync(
        BenchmarkReportPackJob job, BenchmarkReportPackPreparation prep, BenchmarkReportAudience audience,
        string subjectKey, string comparisonKey, CancellationToken ct)
    {
        var ids = (job.Request.ReplaceDocumentIds ?? new List<long>()).Distinct().ToList();
        if (ids.Count == 0) return new List<BenchmarkReportDocument>();

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

    /// <summary>The parsed output (null when unparseable) and every validation issue, a parse failure included.</summary>
    private static (BenchmarkReportWriterOutput? Output, IReadOnlyList<BenchmarkReportValidationNote> Issues) ParseAndValidate(
        BenchmarkReportAudience audience, string? rawText, BenchmarkReportPackPreparation prep,
        IReadOnlyList<BenchmarkReportQuestionTopic>? sharedTopics)
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
        return (parsed.Output, BenchmarkReportPackValidator.Validate(audience, parsed.Output, prep.Sheet, prep.Content, sharedTopics));
    }

    /// <summary>Sends the request once and records the call's usage under <see cref="UsageRoleContext"/>.</summary>
    private async Task<WriterTurn> RunTurnAsync(
        BenchmarkReportPackJob job,
        BenchmarkReportPackDocumentProgress progress,
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
                await TryRecordUsageAsync(job, progress, config, pricing, runResult, sw.ElapsedMilliseconds);
            }
            throw;
        }
        catch (Exception ex)
        {
            terminalError = ExceptionDetails.DescribeShort(ex);
        }
        sw.Stop();

        var (inputTokens, outputTokens, cost) = await TryRecordUsageAsync(job, progress, config, pricing, runResult, sw.ElapsedMilliseconds);
        return new WriterTurn(runResult.FinalText, terminalError, inputTokens, outputTokens, sw.ElapsedMilliseconds, cost);
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
        "Only the Executive Summary, the Report for AI Researchers and Developers, the Internal Improvement Brief and the Provider Issue Report are written for a chat consistency analysis.";
    public const string ChatConsistencyNothingInProgressMessage = "No report writing is in progress for this analysis.";
    public const string ChatConsistencyAnalysisGoneMessage = "The chat consistency analysis no longer exists.";

    /// <summary>Rough output sizes per chat consistency document, for the estimate's cost only.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> ChatConsistencyOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 2500,
        [BenchmarkReportAudience.TechnicalReport] = 5500,
        [BenchmarkReportAudience.InternalBrief] = 3500,
        [BenchmarkReportAudience.ProviderIssueReport] = 4500
    };

    private enum ChatJobPhase { Queued, Preparing, Writing, Finished }

    /// <summary>One analysis's job, as the Reports step shows it. Mutable fields change under the registry's lock.</summary>
    private sealed class ChatConsistencyJobState
    {
        public required int AnalysisId { get; init; }
        public required BenchmarkReportPackJob Job { get; init; }
        public required List<BenchmarkReportAudience> Audiences { get; init; }
        public string WriterProvider { get; init; } = string.Empty;
        public string WriterModelId { get; init; } = string.Empty;
        public string? WriterThinkingLevel { get; init; }
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
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.ComputeCost(pricing, input, output, 0, 0)
            });
        }
        estimate.EstimatedTotalCostUsd = estimate.Estimates.Count == 0 || estimate.Estimates.Any(e => e.EstimatedCostUsd == null)
            ? null
            : estimate.Estimates.Sum(e => e.EstimatedCostUsd!.Value);
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
    /// the spend cap (429).
    /// </summary>
    public async Task<BenchmarkChatConsistencyReportResult<WriteRunReportDocumentsResponse>> WriteChatConsistencyDocumentsAsync(
        int analysisId,
        long writerConfigId,
        IReadOnlyCollection<BenchmarkReportAudience>? audiences,
        bool acknowledgeSameProvider,
        string? userId,
        CancellationToken ct)
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
            WriterThinkingLevel = writer.ThinkingLevel
        };

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

            await WriteChatConsistencyJobDocumentsAsync(job, result, job.Cts.Token);

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
    private async Task WriteChatConsistencyJobDocumentsAsync(BenchmarkReportPackJob job, ChatConsistencyAnalysisResult result, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);

            var (binding, bindFailure) = await BindWriterAsync(job, ct);
            if (binding == null)
            {
                FailAll(job, bindFailure ?? "Writer model configuration not found or disabled.");
                return;
            }
            var (config, endpoint, apiKey, pricing) = binding;

            var (targets, controls) = await ChatConsistencyRunsAsync(result, ct);
            IReadOnlyList<string>? requestIds = null;
            var state = new JobState { SeveralSubjects = false };

            int completed = 0;
            int failed = 0;
            foreach (var row in job.Documents.ToList())
            {
                ct.ThrowIfCancellationRequested();
                if (row.Audience == BenchmarkReportAudience.ProviderIssueReport)
                {
                    requestIds ??= await ChatConsistencySampleRequestIdsAsync(result, ct);
                }

                var sheet = BenchmarkChatConsistencyReportFacts.Build(result, row.Audience,
                    row.Audience == BenchmarkReportAudience.ProviderIssueReport ? requestIds : null);
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
        => new ChatConsistencyAnalysisService(_db, new ChatConsistencyEvidenceBuilder(_db), NullLogger<ChatConsistencyAnalysisService>.Instance)
            .GetAnalysisAsync(analysisId, ct);

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
        SynthesisSha256 = BenchmarkReportRenderService.SynthesisSha256(run.AssessmentJson, run.CoAssessorSynthesisJson)
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
    int StatusCode, T? Value = null, string? Error = null, SameProviderWarningDto? SameProviderWarning = null)
    where T : class;

/// <summary>The run report estimate of a chat consistency analysis, with whether its Provider Issue Report can be written.</summary>
public class BenchmarkChatConsistencyReportEstimateDto : BenchmarkRunReportEstimateDto
{
    /// <summary>At least one provider-side attribution is graded Established or Indicated.</summary>
    public bool ProviderIssueReportAvailable { get; set; }

    /// <summary>Why the Provider Issue Report cannot be written; null when it can.</summary>
    public string? ProviderIssueReportReason { get; set; }
}
