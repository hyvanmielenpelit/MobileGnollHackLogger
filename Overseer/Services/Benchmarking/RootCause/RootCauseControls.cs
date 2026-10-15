namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

/// <summary>One control's quality per item, keyed as <see cref="BenchmarkReportFacts.ItemKeyOf"/> keys a linked answer.</summary>
public sealed class RootCauseControlScores
{
    /// <summary>The control's <see cref="RootCauseControl.Number"/>.</summary>
    public int Number { get; init; }

    /// <summary><c>q&lt;question id&gt;@&lt;revision&gt;</c> → the control's mean quality over its runs.</summary>
    public IReadOnlyDictionary<string, double> ScoresByItem { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);
}

/// <summary>
/// The controls found for a run or battery-run subject (§ A9), with their item-level scores; empty
/// with <see cref="NoneReason"/> when none qualifies. <see cref="Controls"/> is what the
/// <c>root-cause-controls</c> endpoints return.
/// </summary>
public sealed class RootCauseControlSet
{
    public IReadOnlyList<RootCauseControl> Controls { get; init; } = Array.Empty<RootCauseControl>();

    /// <summary>Aligned with <see cref="Controls"/> by number.</summary>
    public IReadOnlyList<RootCauseControlScores> Scores { get; init; } = Array.Empty<RootCauseControlScores>();

    /// <summary>The subject's provider, against which "another provider" is read.</summary>
    public string SubjectProvider { get; init; } = string.Empty;

    /// <summary>Why no control qualifies, in words; null when one does.</summary>
    public string? NoneReason { get; init; }

    /// <summary>The candidates examined.</summary>
    public int CandidatesConsidered { get; init; }

    public static RootCauseControlSet None(string reason, string subjectProvider = "", int considered = 0)
        => new() { NoneReason = reason, SubjectProvider = subjectProvider, CandidatesConsidered = considered };
}

/// <summary>
/// Finds the controls of a run- or battery-completion Root Cause Analysis (§ A9): earlier, finished runs
/// of another model that answered the same exam under the same instrument, whose per-question scores
/// tell how hard each question was. Only the model differs.
///
/// <para>A run control is a completed run on the same suite that
/// <see cref="BenchmarkPairedTests.ClassifyRunPair"/> (not repriced) classifies as a
/// <see cref="BenchmarkRunPairKind.ModelComparison"/> with the subject, on the same item revisions. A battery
/// control is a finished battery run of the same definition (the same battery revision) with a current,
/// complete analysis in the same comparability class: the class is built from the
/// <see cref="BenchmarkCrossModelComparability.MustMatchSignature"/> of each suite's runs, so equal classes are
/// exactly what <see cref="BenchmarkCrossModelComparability.Resolve"/> leaves non-Excluded suite by suite. In
/// both cases the model differs (another model id, or the same model at another thinking level), the
/// control's own index is not withheld, and it shares at least <see cref="MinSharedQuestions"/> scored
/// questions with the subject.</para>
///
/// <para>At most <see cref="MaxControls"/> are kept, one model each: first one per provider other than the
/// subject's, then the rest; each pass prefers the most recent, then the most shared questions. Speed- and
/// cost-degraded candidates are allowed, because controls serve intelligence only.</para>
///
/// <para>A static service: <see cref="FindAsync"/> serves run- and battery-completion preparation and the
/// <c>root-cause-controls</c> endpoints alike, so the UI shows exactly the controls the document would use.</para>
/// </summary>
public static class RootCauseControls
{
    public const int MaxControls = 3;
    public const int MinSharedQuestions = 5;

    /// <summary>The newest candidates examined at most.</summary>
    public const int MaxCandidates = 60;

    /// <summary>The candidates whose answers are loaded at most, after classification.</summary>
    public const int MaxScoredCandidates = 12;

    public const string NoRunControlReason = "No earlier run of another model answered these questions under the same graders, scoring and prompt options.";
    public const string NoBatteryControlReason = "No earlier battery run of another model ran this battery revision with a current, complete analysis in the same comparability class.";

    private static readonly BenchmarkRunStatus[] CompletedStatuses =
    {
        BenchmarkRunStatus.Completed, BenchmarkRunStatus.CompletedWithLimits, BenchmarkRunStatus.CompletedWithErrors
    };

    /// <summary>
    /// The controls of a subject key: <c>run:&lt;id&gt;</c> or <c>battery:&lt;id&gt;</c>; a group or comparison
    /// subject has none.
    /// </summary>
    public static Task<RootCauseControlSet> FindAsync(ApplicationDbContext db, string subjectKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (TryId(subjectKey, "run:", out long runId)) return FindForRunAsync(db, runId, ct);
        if (TryId(subjectKey, BenchmarkBatteryModelComparison.KeyPrefix, out long batteryRunId)) return FindForBatteryRunAsync(db, batteryRunId, ct);
        return Task.FromResult(RootCauseControlSet.None("Only a run or a battery run has controls."));
    }

    /// <summary>The controls of one run.</summary>
    public static async Task<RootCauseControlSet> FindForRunAsync(ApplicationDbContext db, long runId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var subject = await db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (subject == null) return RootCauseControlSet.None("The run does not exist.");

        string provider = subject.TestedModelSnapshot?.Provider ?? string.Empty;
        long? suiteId = subject.BenchmarkSuiteId ?? subject.BenchmarkSuiteIdUsed;
        if (suiteId == null) return RootCauseControlSet.None("The run's suite cannot be identified.", provider);

        var headers = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => r.Id != runId
                        && CompletedStatuses.Contains(r.Status)
                        && (r.BenchmarkSuiteId == suiteId || r.BenchmarkSuiteIdUsed == suiteId))
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(MaxCandidates)
            .ToListAsync(ct);

        var candidates = headers.Where(r => DifferentModel(subject, r)).ToList();
        if (candidates.Count == 0) return RootCauseControlSet.None(NoRunControlReason, provider, headers.Count);

        await HydrateItemStubsAsync(db, candidates, ct);
        var classified = candidates
            .Where(c =>
            {
                var classification = BenchmarkPairedTests.ClassifyRunPair(subject, c, repriced: false);
                return classification.Kind == BenchmarkRunPairKind.ModelComparison
                       && classification.Differences.All(d => d.Name != BenchmarkComparabilityKey.ItemRevisionsKey);
            })
            .Take(MaxScoredCandidates)
            .Select(c => c.Id)
            .ToList();
        if (classified.Count == 0) return RootCauseControlSet.None(NoRunControlReason, provider, candidates.Count);

        var loaded = await db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .Where(r => classified.Contains(r.Id))
            .ToListAsync(ct);

        var subjectScores = RunScores(new[] { subject });
        var qualifying = new List<Candidate>();
        foreach (var run in loaded.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id))
        {
            if (BenchmarkRunFinalizer.IndexWithheld(run, run.Answers)) continue;
            var scores = RunScores(new[] { run });
            int shared = scores.Keys.Count(subjectScores.ContainsKey);
            if (shared < MinSharedQuestions) continue;

            var snapshot = run.TestedModelSnapshot;
            qualifying.Add(new Candidate(
                snapshot?.Label() ?? run.TestedModelConfigurationId?.ToString(CultureInfo.InvariantCulture) ?? "Unknown model",
                snapshot?.Provider ?? string.Empty,
                snapshot?.ModelId ?? string.Empty,
                snapshot?.ThinkingLevel,
                run.Id,
                null,
                run.StartedAtUtc,
                shared,
                scores));
        }

        return Choose(qualifying, provider, NoRunControlReason, headers.Count);
    }

    /// <summary>The controls of one battery run.</summary>
    public static async Task<RootCauseControlSet> FindForBatteryRunAsync(ApplicationDbContext db, long batteryRunId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var (subjectSources, error) = await BenchmarkBatteryModelComparison.LoadAsync(db, new[] { batteryRunId }, ct);
        var subject = subjectSources?.FirstOrDefault();
        if (subject == null) return RootCauseControlSet.None(error ?? "The battery run does not exist.");

        var identity = subject.IdentityRuns.OrderBy(r => r.StartedAtUtc).LastOrDefault();
        string provider = identity?.TestedModelSnapshot?.Provider ?? string.Empty;
        if (subject.Refusal != null || subject.Result == null || string.IsNullOrEmpty(subject.ComparabilityClassSha256))
        {
            return RootCauseControlSet.None("The battery run's own analysis is not current and complete, so no control can be matched to it.", provider);
        }

        string definition = subject.DefinitionSha256;
        var candidateIds = await db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(b => b.Id != batteryRunId && b.DefinitionSha256 == definition)
            .OrderByDescending(b => b.StartedAtUtc)
            .ThenByDescending(b => b.Id)
            .Select(b => b.Id)
            .Take(MaxCandidates)
            .ToListAsync(ct);
        if (candidateIds.Count == 0) return RootCauseControlSet.None(NoBatteryControlReason, provider);

        var (sources, _) = await BenchmarkBatteryModelComparison.LoadAsync(db, candidateIds, ct);
        var batteryRuns = await db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(b => candidateIds.Contains(b.Id))
            .Select(b => new { b.Id, b.StartedAtUtc })
            .ToDictionaryAsync(b => b.Id, b => b.StartedAtUtc, ct);

        var subjectScores = BatteryScores(subject);
        var qualifying = new List<Candidate>();
        foreach (var source in sources ?? Array.Empty<BenchmarkBatteryComparisonSource>())
        {
            if (source.Refusal != null || source.Result == null) continue;
            if (!string.Equals(source.DefinitionSha256, subject.DefinitionSha256, StringComparison.Ordinal)) continue;
            if (!string.Equals(source.ComparabilityClassSha256, subject.ComparabilityClassSha256, StringComparison.Ordinal)) continue;

            var run = source.IdentityRuns.OrderBy(r => r.StartedAtUtc).LastOrDefault();
            if (run == null || identity == null || !DifferentModel(identity, run)) continue;

            var scores = BatteryScores(source);
            int shared = scores.Keys.Count(subjectScores.ContainsKey);
            if (shared < MinSharedQuestions) continue;

            var snapshot = run.TestedModelSnapshot;
            qualifying.Add(new Candidate(
                snapshot?.Label() ?? "Unknown model",
                snapshot?.Provider ?? string.Empty,
                snapshot?.ModelId ?? string.Empty,
                snapshot?.ThinkingLevel,
                null,
                source.BatteryRunId,
                batteryRuns.TryGetValue(source.BatteryRunId, out var started) ? started : run.StartedAtUtc,
                shared,
                scores));
        }

        return Choose(qualifying, provider, NoBatteryControlReason, candidateIds.Count);
    }

    /// <summary>
    /// The kept controls, at most <see cref="MaxControls"/> and one per model: first the best candidate of
    /// each provider other than <paramref name="subjectProvider"/> (providers in the order of their best
    /// candidate), then the best remaining models; best is the most recent, then the most shared questions.
    /// </summary>
    private static RootCauseControlSet Choose(IReadOnlyList<Candidate> qualifying, string subjectProvider, string noneReason, int considered)
    {
        if (qualifying.Count == 0) return RootCauseControlSet.None(noneReason, subjectProvider, considered);

        var ranked = qualifying
            .OrderByDescending(c => c.DateUtc)
            .ThenByDescending(c => c.Shared)
            .ThenBy(c => c.RunId ?? c.BatteryRunId ?? 0)
            .ToList();

        // The best run of each model.
        var bestPerModel = ranked
            .GroupBy(c => ModelKey(c.ModelId, c.ThinkingLevel), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        var byRank = bestPerModel.OrderBy(c => ranked.IndexOf(c)).ToList();

        var chosen = new List<Candidate>();
        foreach (var group in byRank
            .Where(c => !SameProvider(c.Provider, subjectProvider))
            .GroupBy(c => (c.Provider ?? string.Empty).Trim().ToLowerInvariant(), StringComparer.Ordinal))
        {
            if (chosen.Count >= MaxControls) break;
            chosen.Add(group.First());
        }
        foreach (var candidate in byRank)
        {
            if (chosen.Count >= MaxControls) break;
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        }

        var controls = new List<RootCauseControl>();
        var scores = new List<RootCauseControlScores>();
        for (int i = 0; i < chosen.Count; i++)
        {
            var c = chosen[i];
            controls.Add(new RootCauseControl
            {
                Number = i + 1,
                ModelLabel = c.Label,
                Provider = c.Provider,
                ModelId = c.ModelId,
                ThinkingLevel = c.ThinkingLevel,
                RunId = c.RunId,
                BatteryRunId = c.BatteryRunId,
                DateUtc = c.DateUtc,
                SharedQuestions = c.Shared
            });
            scores.Add(new RootCauseControlScores { Number = i + 1, ScoresByItem = c.Scores });
        }

        return new RootCauseControlSet
        {
            Controls = controls,
            Scores = scores,
            SubjectProvider = subjectProvider,
            CandidatesConsidered = considered
        };
    }

    /// <summary>
    /// The control limit of § A4 for <paramref name="count"/> controls of which <paramref name="otherProviders"/>
    /// come from a provider other than the subject's: null (no cap) from two with one of another provider,
    /// Moderate for one or for two or more of the subject's provider only, Low for none.
    /// </summary>
    public static (string? Cap, string? Reason) Limit(int count, int otherProviders)
    {
        if (count >= 2 && otherProviders >= 1) return (null, null);
        if (count == 1) return (RootCauseGrades.Moderate, "only one other model answered these questions");
        if (count >= 2) return (RootCauseGrades.Moderate, "every other model that answered these questions comes from the model's own provider");
        return (RootCauseGrades.Low, "no other model answered these questions");
    }

    /// <summary>Per item, the mean published quality over the runs' counting answers.</summary>
    public static Dictionary<string, double> RunScores(IEnumerable<BenchmarkRun> runs)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
            foreach (var answer in run.Answers ?? new List<BenchmarkRunAnswer>())
            {
                if (!BenchmarkRunFinalizer.CountsTowardQualityIndex(answer)) continue;
                if (BenchmarkItemAnalysis.QuestionKey(answer) is not long) continue;
                if (BenchmarkScoring.IndexQuality(answer, panel) is not double quality) continue;
                string key = BenchmarkReportFacts.ItemKeyOf(answer);
                if (!values.TryGetValue(key, out var list)) values[key] = list = new List<double>();
                list.Add(quality);
            }
        }
        return values.ToDictionary(kv => kv.Key, kv => kv.Value.Average(), StringComparer.Ordinal);
    }

    /// <summary>Per item, the persisted item mean of every suite of a battery result.</summary>
    public static Dictionary<string, double> BatteryScores(BenchmarkBatteryComparisonSource source)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var suite in source.Result?.Suites ?? Array.Empty<BenchmarkBatterySuiteProfile>())
        {
            foreach (var item in suite.Statistics?.Items ?? Array.Empty<BenchmarkGroupItemStatistics>())
            {
                if (item.RunCount <= 0) continue;
                scores.TryAdd(ItemKey(item.QuestionId, item.ItemRevision), item.Mean);
            }
        }
        return scores;
    }

    /// <summary>The item key of a linked question, as <see cref="BenchmarkReportFacts.ItemKeyOf"/> writes it.</summary>
    public static string ItemKey(long questionId, int? revision)
        => "q" + questionId.ToString(CultureInfo.InvariantCulture) + "@" + (revision?.ToString(CultureInfo.InvariantCulture) ?? "?");

    /// <summary>Another model id, or the same model at another thinking level.</summary>
    public static bool DifferentModel(BenchmarkRun subject, BenchmarkRun candidate)
        => !string.Equals(ModelKey(subject.TestedModelSnapshot?.ModelId, subject.TestedModelSnapshot?.ThinkingLevel),
            ModelKey(candidate.TestedModelSnapshot?.ModelId, candidate.TestedModelSnapshot?.ThinkingLevel), StringComparison.Ordinal);

    private static string ModelKey(string? modelId, string? thinkingLevel)
        => (modelId ?? string.Empty).Trim().ToLowerInvariant() + "|" + (thinkingLevel ?? string.Empty).Trim().ToLowerInvariant();

    private static bool SameProvider(string? a, string? b)
        => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool TryId(string? key, string prefix, out long id)
    {
        id = 0;
        return key != null && key.StartsWith(prefix, StringComparison.Ordinal)
               && long.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    /// <summary>
    /// The item-revision and assessed-difficulty stubs <see cref="BenchmarkComparabilityKey"/> reads, as
    /// <c>BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync</c> builds them: four columns, never whole rows.
    /// </summary>
    private static async Task HydrateItemStubsAsync(ApplicationDbContext db, IReadOnlyList<BenchmarkRun> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var rows = await db.BenchmarkRunAnswers
            .AsNoTracking()
            .Where(a => ids.Contains(a.BenchmarkRunId) && (a.BenchmarkQuestionIdUsed != null || a.BenchmarkQuestionId != null))
            .Select(a => new { a.BenchmarkRunId, a.BenchmarkQuestionIdUsed, a.BenchmarkQuestionId, a.ItemRevisionUsed, a.AssessedDifficulty })
            .ToListAsync(ct);

        var byRun = rows.GroupBy(a => a.BenchmarkRunId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var run in runs)
        {
            run.Answers = byRun.TryGetValue(run.Id, out var list)
                ? list.Select(a => new BenchmarkRunAnswer
                {
                    BenchmarkRunId = a.BenchmarkRunId,
                    BenchmarkQuestionIdUsed = a.BenchmarkQuestionIdUsed,
                    BenchmarkQuestionId = a.BenchmarkQuestionId,
                    ItemRevisionUsed = a.ItemRevisionUsed,
                    AssessedDifficulty = a.AssessedDifficulty
                }).ToList()
                : new List<BenchmarkRunAnswer>();
        }
    }

    private sealed record Candidate(
        string Label,
        string Provider,
        string ModelId,
        string? ThinkingLevel,
        long? RunId,
        long? BatteryRunId,
        DateTime DateUtc,
        int Shared,
        IReadOnlyDictionary<string, double> Scores);
}
