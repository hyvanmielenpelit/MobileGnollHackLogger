namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

// The fact sheet of a battery subject: a battery run's result as one entry of a comparison of
// battery results. It carries every generic key the renderer, the validator and the prompt read, so
// a battery document renders through the same code as a run document, plus the battery's own keys.
// Every analysis figure is read from the battery's persisted BenchmarkBatteryStatisticsResult and
// never recomputed; the per-answer figures (tokens, tool calls, refuted sentences, the response-style
// conflict) come from the member runs' answer rows, as a run document's do, and the tool-call
// outcomes from their per-call rows, as BenchmarkBatteryAnswerOutcomes.LoadAsync loads them.
//
// Generic keys and how a battery supplies them:
//   subject.*, comparison.*, config.chat     as for a run; subject.runs counts the usable member runs
//   suite.name                               unavailable: a battery spans several suites (see suite.<n>.name)
//   suite.questions                          the exam questions summed over the suites
//   quality.*                                from the comparison entry, which maps the persisted Overall Index;
//                                            quality.rawIndex and quality.unweightedMean are unavailable
//   dimension.<d>                            the persisted count-weighted dimension means; .peerMean over the
//                                            peers' persisted means
//   band.<b>.questions, bands.authored.<b>   counted over the question rows
//   band.<b>.score, .peerMean, .difference   unavailable: a battery weights suites, not bands
//   speed.*, cost.*                          from the comparison entry; cost.perRun and cost.totalRunPerRun are
//                                            per battery pass
//   tokens.*, tools.callsPerQuestion, tools.share.*, tools.zeroKnowledgeBaseAnswers, style.*
//                                            from the member runs' answers, as for a run
//   tools.failed, tools.refusedByBudget      counted, by the run-level rule, over the per-call rows of the usable
//                                            member runs' answers that count toward the index; unavailable when a
//                                            member run predates harness 17 or the rows were not loaded
//   tools.callsPerQuestion.peerMean          unavailable with peers: their answers are not loaded
//   answers.scored, errors.critical          summed over the persisted item rows
//   claims.supported, .refuted, .indeterminate
//                                            the persisted usage totals: rulings on the answers' own claims
//   claims.refutedAnswerSentences            the questions' refuted answer sentences summed, accused ones included
//   panel.*                                  as for a run, over the member runs; panel.judgeDependentPairs is
//                                            unavailable. With the persisted panel agreement block, panel.icc,
//                                            .meanAbsDelta, .referenceReaderOffset (pooled over the answers both
//                                            members scored) and .memberAAlone, .memberBAlone, .referenceReaderIndex
//                                            (composites under the declared weights) are read from it
//   scoring.*, run.*                         as for a run, over the member runs
//   peer.X.*                                 index, interval, rank, overlap, speed, cost and runs, as for a run;
//                                            no paired difference
//
// Battery keys:
//   battery.name, battery.revision, battery.scheme, battery.suiteCount, battery.runsPerSuite,
//   battery.memberRuns, battery.rounds, battery.definitionSha256, battery.classSha256,
//   battery.pooledIdentity, battery.criticalErrorRate, battery.speedIndex, battery.suiteIndexSd,
//   battery.suiteIndexRange, battery.excludedMembers, battery.caveat.<i>
//   suite.<n>.name, .weight, .index, .contribution, .interval, .scoredItems, .runs, .speedIndex,
//   .costPerRun, .criticalErrorRate                 n is the suite's number from 1
//   sensitivity.<scheme>                             the Overall Index under each weighting scheme
//   sensitivity.panelVerificationCleared             the advisory panel verification-cleared Overall Index;
//                                                    absent when no member is a panel run
//   loo.<n>                                          the Overall Index with suite n left out
//
// Questions are numbered 1..N across the battery, suite by suite, and each carries its suite-qualified
// reference S<n>-Q<m>, m counting the suite's questions by order index. Every question has a one-line
// row; at most DetailQuestionsPerSuite questions of each suite also carry their verbatim content
// (critical errors first, then the lowest and the highest mean scores, alternately), from the run
// whose score is the median of the question's rounds. When the largest writer prompt exceeds
// MaxPromptChars, detail is left out in reverse priority and a validation note says so.

/// <summary>What <see cref="BenchmarkBatteryReportFacts.Build"/> reads.</summary>
public sealed class BenchmarkBatteryReportFactsInput
{
    /// <summary>A comparison of battery results.</summary>
    public BenchmarkModelComparisonDto Comparison { get; init; } = default!;

    /// <summary><c>battery:&lt;id&gt;</c>.</summary>
    public string SubjectKey { get; init; } = string.Empty;

    /// <summary>Every battery result of the comparison, as <see cref="BenchmarkBatteryModelComparison.LoadAsync"/> loads them.</summary>
    public IReadOnlyList<BenchmarkBatteryComparisonSource> Sources { get; init; } = Array.Empty<BenchmarkBatteryComparisonSource>();

    /// <summary>The subject's usable member runs, with their answers, keyed by run id.</summary>
    public IReadOnlyDictionary<long, BenchmarkRun> Runs { get; init; } = new Dictionary<long, BenchmarkRun>();

    /// <summary>
    /// The usable member runs' tool-call outcomes, from <see cref="BenchmarkBatteryAnswerOutcomes.LoadAsync"/>;
    /// null when they were not loaded, which leaves <c>tools.failed</c> and <c>tools.refusedByBudget</c> unavailable.
    /// </summary>
    public BenchmarkBatteryAnswerOutcomes? AnswerOutcomes { get; init; }

    public int AnswerExcerptChars { get; init; } = BenchmarkReportPackPreparation.DefaultAnswerExcerptChars;

    public int DetailQuestionsPerSuite { get; init; } = BenchmarkBatteryReportFacts.DefaultDetailQuestionsPerSuite;

    public int MaxPromptChars { get; init; } = BenchmarkBatteryReportFacts.DefaultMaxPromptChars;
}

/// <summary>A battery subject's fact sheet, its content snapshot and the notes of its preparation, or the reason none can be built.</summary>
public sealed class BenchmarkBatteryReportFactsResult
{
    public BenchmarkReportFactSheet? Sheet { get; init; }

    /// <summary>The verbatim content of the questions given in detail, each under the run its excerpt comes from.</summary>
    public BenchmarkReportContentSnapshot? Content { get; init; }

    /// <summary>What the preparation left out to keep the prompt within its budget; stored with each document.</summary>
    public IReadOnlyList<BenchmarkReportValidationNote> Notes { get; init; } = Array.Empty<BenchmarkReportValidationNote>();

    public string? Refusal { get; init; }
}

/// <summary>
/// What a battery report counts over its usable member runs' rows beyond the persisted analysis: the
/// tool-call outcomes of the answers that count toward the index, classified as a run report classifies
/// them (<see cref="BenchmarkToolCallRecorder.Outcomes"/>), and the refuted answer sentences.
/// </summary>
public sealed class BenchmarkBatteryAnswerOutcomes
{
    /// <summary>Why the tool-call outcomes are unavailable when a member run predates per-call tool records.</summary>
    public const string ToolRecordsReason = "A run predates per-call tool records (harness 17).";

    /// <summary>Calls that ran and did not complete; null when <see cref="ToolCallsUnavailableReason"/> is set.</summary>
    public int? ToolCallsFailed { get; init; }

    /// <summary>Calls the tool budget refused; null when <see cref="ToolCallsUnavailableReason"/> is set.</summary>
    public int? ToolCallsRefusedByBudget { get; init; }

    public string? ToolCallsUnavailableReason { get; init; }

    /// <summary>
    /// Refuted answer sentences, accused ones included, as <see cref="BenchmarkReportFacts.RefutedAnswerSentencesOf"/>
    /// counts them; null when they were not loaded, or an answer's verifications are unreadable or carry no roles.
    /// </summary>
    public int? RefutedAnswerSentences { get; init; }

    /// <summary>
    /// Counts over the runs <paramref name="runIds"/>. The tool-call query loads only the columns the
    /// classification reads (the call's status and error, its answer's status and finish reason), never
    /// a call's arguments or result. With <paramref name="withRefutedSentences"/>, the answers'
    /// claim verifications are loaded as well, for <see cref="RefutedAnswerSentences"/>.
    /// </summary>
    public static async Task<BenchmarkBatteryAnswerOutcomes> LoadAsync(
        ApplicationDbContext db, IReadOnlyCollection<long> runIds, bool withRefutedSentences, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ids = (runIds ?? Array.Empty<long>()).Distinct().ToList();

        int? refutedSentences = null;
        if (withRefutedSentences)
        {
            var verifications = await db.BenchmarkRunAnswers
                .AsNoTracking()
                .Where(a => ids.Contains(a.BenchmarkRunId))
                .Select(a => a.ClaimVerificationJson)
                .ToListAsync(ct);
            refutedSentences = BenchmarkReportFacts.RefutedAnswerSentencesOf(
                verifications.Select(json => new BenchmarkRunAnswer { ClaimVerificationJson = json }));
        }

        var harnessVersions = await db.BenchmarkRuns
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => ids.Contains(r.Id))
            .Select(r => r.HarnessVersion)
            .ToListAsync(ct);
        bool recorded = harnessVersions.Count > 0 && harnessVersions.All(v =>
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h >= 17);
        if (!recorded)
        {
            return new BenchmarkBatteryAnswerOutcomes
            {
                ToolCallsUnavailableReason = ToolRecordsReason,
                RefutedAnswerSentences = refutedSentences
            };
        }

        var rows = await db.BenchmarkRunAnswerToolCalls
            .AsNoTracking()
            .Where(t => ids.Contains(t.BenchmarkRunAnswer!.BenchmarkRunId))
            .Select(t => new
            {
                t.Status,
                t.Error,
                AnswerStatus = t.BenchmarkRunAnswer!.Status,
                t.BenchmarkRunAnswer!.ProviderFinishReason
            })
            .ToListAsync(ct);

        var (_, failed, refused) = BenchmarkToolCallRecorder.Outcomes(rows
            .Where(r => BenchmarkRunFinalizer.CountsTowardQualityIndex(
                new BenchmarkRunAnswer { Status = r.AnswerStatus, ProviderFinishReason = r.ProviderFinishReason }))
            .Select(r => new BenchmarkRunAnswerToolCall { Status = r.Status, Error = r.Error }));

        return new BenchmarkBatteryAnswerOutcomes
        {
            ToolCallsFailed = failed,
            ToolCallsRefusedByBudget = refused,
            RefutedAnswerSentences = refutedSentences
        };
    }
}

/// <summary>
/// Builds the fact sheet of a battery result against the other battery results of its comparison.
/// Pure: no I/O, no clock, no culture.
/// </summary>
public static class BenchmarkBatteryReportFacts
{
    /// <summary>The sheet's <see cref="BenchmarkReportFactSheet.SubjectKind"/>.</summary>
    public const string SubjectKind = "Battery";

    public const int DefaultDetailQuestionsPerSuite = 6;

    /// <summary>About 90,000 tokens.</summary>
    public const int DefaultMaxPromptChars = 360_000;

    /// <summary>The validation-note rule of a prompt that left out question detail to stay within its budget.</summary>
    public const int PromptBudgetRule = 20;

    /// <summary>Why a peer fact is unavailable on a battery sheet with no peers.</summary>
    public const string StandaloneReason = "A stand-alone battery report has no peers.";

    public const string SuiteNameReason = "A battery result spans several suites; each suite's name is its suite.<n>.name fact.";

    public const string CompositeIndexReason =
        "A battery's Overall Index is a weighted composite of its suite indices; it has no pooled figure of this kind.";

    public const string BandReason =
        "A battery weights its suites, not its difficulty bands, so it states no pooled band score; each question's row carries its band.";

    public const string ToolRowsReason =
        "A battery report does not load per-call tool rows; each member run's Tool-call log has them.";

    public const string PeerAnswersReason = "A comparison of battery results does not load the peers' answers.";

    public const string RefutedSentencesReason =
        "Some answers' claim verifications are unreadable or record no roles, so refuted answer sentences cannot be told from graders' statements.";

    public const string JudgeDependentReason = "Judge-dependent pairs are not computed across battery results.";

    /// <summary>The configuration keys of the detail cap and the prompt budget.</summary>
    public const string DetailQuestionsPerSuiteKey = "Benchmark:ReportPack:BatteryDetailQuestionsPerSuite";

    public const string MaxPromptCharsKey = "Benchmark:ReportPack:BatteryMaxPromptChars";

    private static readonly (string Key, string Name)[] DimensionKeys =
    {
        ("accuracy", "Accuracy"), ("completeness", "Completeness"), ("conciseness", "Conciseness"), ("readability", "Readability")
    };

    private static readonly (string Key, BenchmarkDifficulty Band)[] Bands =
    {
        ("simple", BenchmarkDifficulty.Simple),
        ("intermediate", BenchmarkDifficulty.Intermediate),
        ("advanced", BenchmarkDifficulty.Advanced)
    };

    /// <summary><c>S2-Q7</c>: question <paramref name="question"/> of suite <paramref name="suite"/>, both from 1.</summary>
    public static string ReferenceOf(int suite, int question)
        => "S" + Inv(suite) + "-Q" + Inv(question);

    /// <summary>A weighting scheme as the battery screens name it.</summary>
    public static string SchemeName(BenchmarkBatteryWeightingScheme scheme) => scheme switch
    {
        BenchmarkBatteryWeightingScheme.DifficultyMass => "Questions and difficulty",
        BenchmarkBatteryWeightingScheme.ItemCount => "Questions only",
        BenchmarkBatteryWeightingScheme.Equal => "Equal per suite",
        BenchmarkBatteryWeightingScheme.Custom => "Custom",
        _ => scheme.ToString()
    };

    /// <summary>The key of a scheme's sensitivity fact: <c>sensitivity.difficultyMass</c>.</summary>
    public static string SensitivityKey(BenchmarkBatteryWeightingScheme scheme)
    {
        string name = scheme.ToString();
        return "sensitivity." + char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// The key of the panel verification-cleared Accuracy sensitivity of the Overall Index
    /// (<see cref="BenchmarkBatteryStatisticsResult.PanelVerificationClearedOverall"/>).
    /// </summary>
    public const string PanelVerificationClearedSensitivityKey = "sensitivity.panelVerificationCleared";

    public static BenchmarkBatteryReportFactsResult Build(BenchmarkBatteryReportFactsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comparison = input.Comparison ?? throw new ArgumentException("A comparison is required.", nameof(input));
        var runsById = input.Runs ?? new Dictionary<long, BenchmarkRun>();
        var sources = (input.Sources ?? Array.Empty<BenchmarkBatteryComparisonSource>())
            .Where(s => s != null)
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var subject = comparison.Entries.FirstOrDefault(e => string.Equals(e.Key, input.SubjectKey, StringComparison.Ordinal));
        if (subject == null) return Refuse($"The comparison has no entry \"{input.SubjectKey}\".");
        if (subject.Excluded)
        {
            return Refuse(string.IsNullOrWhiteSpace(subject.Explanation)
                ? "The subject is excluded from the comparison."
                : subject.Explanation);
        }

        if (!sources.TryGetValue(subject.Key, out var source) || source.Result == null)
        {
            return Refuse("The battery result's analysis was not loaded.");
        }

        var missing = subject.RunIds.Where(id => !runsById.ContainsKey(id)).OrderBy(id => id).ToList();
        if (subject.RunIds.Count == 0 || missing.Count > 0)
        {
            return Refuse(missing.Count > 0
                ? "Member run(s) " + string.Join(", ", missing.Select(id => Inv(id))) + " of the battery result were not loaded."
                : "The battery result has no usable member runs.");
        }

        var result = source.Result;
        var subjectRuns = subject.RunIds.Distinct().OrderBy(id => id).Select(id => runsById[id]).ToList();
        var subjectStats = BenchmarkReportFacts.EntryStats.Of(subjectRuns);
        var noRuns = BenchmarkReportFacts.EntryStats.Of(Array.Empty<BenchmarkRun>());

        var peerEntries = comparison.Entries
            .Where(e => !e.Excluded && !string.Equals(e.Key, subject.Key, StringComparison.Ordinal))
            .OrderByDescending(e => e.Quality?.PointEstimate ?? double.NegativeInfinity)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var peers = peerEntries
            .Select((e, i) => (Entry: e, Letter: BenchmarkReportFacts.LetterFor(i), Stats: noRuns))
            .ToList();

        var eligible = new List<BenchmarkModelComparisonEntryDto> { subject };
        eligible.AddRange(peerEntries);

        var suites = result.Suites.OrderBy(s => s.SuiteIndex).ToList();
        var (questions, items) = BuildQuestions(suites, source, subjectRuns);

        var sheet = new BenchmarkReportFactSheet
        {
            SubjectKey = subject.Key,
            SubjectKind = SubjectKind,
            SubjectLabel = subject.Label,
            SubjectDisplayName = subject.ModelDisplayName,
            SubjectProvider = subject.Provider,
            SubjectModelId = subject.ModelId,
            SubjectThinkingLevel = subject.ThinkingLevel,
            SubjectRunIds = subjectRuns.Select(r => r.Id).ToList(),
            SubjectState = subject.State,
            SubjectExplanation = subject.Explanation,
            SuiteId = null,
            SuiteName = source.BatteryName,
            Peers = peers.Select(p => new BenchmarkReportPeer
            {
                Letter = p.Letter,
                EntryKey = p.Entry.Key,
                Label = p.Entry.Label,
                DisplayName = p.Entry.ModelDisplayName,
                Provider = p.Entry.Provider,
                ModelId = p.Entry.ModelId,
                ThinkingLevel = p.Entry.ThinkingLevel,
                RunIds = p.Entry.RunIds.OrderBy(id => id).ToList(),
                State = p.Entry.State,
                SpeedDegraded = p.Entry.SpeedDegraded,
                CostDegraded = p.Entry.CostDegraded,
                Explanation = p.Entry.Explanation
            }).ToList(),
            Graders = BenchmarkReportFacts.BuildGraders(subjectRuns, subject.Provider),
            PurposeStatements = subjectRuns
                .Select(r => r.PurposeStatementUsed?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Questions = questions,
            Battery = new BenchmarkReportBatterySubject
            {
                BatteryRunId = source.BatteryRunId,
                Name = source.BatteryName,
                Revision = source.DefinitionRevision,
                Scheme = SchemeName(result.Scheme),
                SuiteCount = result.SuiteCount > 0 ? result.SuiteCount : suites.Count,
                RunsPerSuite = source.RunsPerSuite,
                MemberRunCount = subjectRuns.Count,
                Suites = suites.Select(s => new BenchmarkReportBatterySuite
                {
                    Number = s.SuiteIndex + 1,
                    SuiteId = s.SuiteId,
                    Name = s.SuiteName,
                    QuestionCount = questions.Count(q => q.Suite == s.SuiteIndex + 1)
                }).ToList()
            }
        };

        (sheet.NoSignificanceSummary, sheet.NoSignificanceInstead) = BenchmarkReportFacts.NoSignificanceStatement(comparison);

        var facts = new BenchmarkReportFacts.FactList();
        AddIdentityFacts(facts, sheet, subject, subjectRuns, comparison, eligible.Count);
        BenchmarkReportFacts.AddQualityFacts(facts, subject, peers, eligible, subjectRuns);
        facts.Withhold(k => k is "quality.rawIndex" or "quality.unweightedMean", CompositeIndexReason);
        AddDimensionFacts(facts, result, peerEntries.Select(e => sources.GetValueOrDefault(e.Key)?.Result).ToList());
        AddBandFacts(facts, questions);
        BenchmarkReportFacts.AddSpeedFacts(facts, subject, eligible);
        BenchmarkReportFacts.AddCostFacts(facts, subject, eligible);
        BenchmarkReportFacts.AddTokenFacts(facts, subjectStats);
        AddErrorAndClaimFacts(facts, result, questions);
        BenchmarkReportFacts.AddToolFacts(facts, subjectStats, subjectRuns, Array.Empty<BenchmarkReportFacts.EntryStats>());
        var outcomes = input.AnswerOutcomes;
        if (outcomes is not { ToolCallsFailed: not null, ToolCallsRefusedByBudget: not null })
        {
            facts.Withhold(k => k is "tools.failed" or "tools.refusedByBudget", outcomes?.ToolCallsUnavailableReason ?? ToolRowsReason);
        }
        if (peers.Count > 0) facts.Withhold(k => k == "tools.callsPerQuestion.peerMean", PeerAnswersReason);
        BenchmarkReportFacts.AddPanelFacts(facts, subject, subjectRuns, comparison, peers);
        AddPanelAgreementFacts(facts, result.PanelAgreement);
        facts.Withhold(k => k == "panel.judgeDependentPairs", JudgeDependentReason);
        BenchmarkReportFacts.AddStyleFact(facts, subjectRuns, subjectStats);
        BenchmarkReportFacts.AddScoringAndProvenanceFacts(facts, subjectRuns);
        BenchmarkReportFacts.AddPeerFacts(facts, subject, peers, eligible);
        AddBatteryFacts(facts, source, result, subjectRuns.Count);
        AddSuiteFacts(facts, suites);
        AddSensitivityFacts(facts, result);

        if (peers.Count == 0)
        {
            facts.Withhold(BenchmarkReportFacts.IsPeerFact, StandaloneReason);
        }

        sheet.Facts = facts.Sorted();
        SetToolOutcomes(sheet.Facts, outcomes);
        sheet.Entries = BuildEntries(subject, source, subjectRuns, subjectStats, peers.Select(p => (p.Entry, p.Letter)).ToList(), eligible, sources);

        var names = BenchmarkReportFacts.BuildKnownNames(sheet);
        names.AddRange(suites.Select(s => s.SuiteName));
        sheet.KnownNames = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var (content, priority) = BuildDetail(sheet, items, runsById, input.AnswerExcerptChars, Math.Max(0, input.DetailQuestionsPerSuite));
        var notes = FitPromptBudget(sheet, content, priority, input.MaxPromptChars);

        return new BenchmarkBatteryReportFactsResult { Sheet = sheet, Content = content, Notes = notes };
    }

    /// <summary>
    /// The largest writer prompt of the three audiences for this sheet and content, system prompt and
    /// user message together, in characters.
    /// </summary>
    public static int PromptChars(BenchmarkReportFactSheet sheet, BenchmarkReportContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(content);

        return new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief }
            .Select(a => BenchmarkReportPackPrompt.Build(a, sheet, content))
            .Max(p => p.SystemPrompt.Length + p.UserMessage.Length);
    }

    // ---------------------------------------------------------------------------------------------
    // Questions and detail
    // ---------------------------------------------------------------------------------------------

    /// <summary>One question row and what it was built from.</summary>
    private sealed record QuestionItem(
        BenchmarkReportQuestion Question, BenchmarkBatterySuiteProfile Suite, BenchmarkGroupItemStatistics Item);

    /// <summary>
    /// One row per persisted item of every suite, in suite order and, within a suite, by order index:
    /// the persisted mean, rounds and critical errors, and from the answer rows the band, the refuted
    /// sentences, the tool calls and the model time.
    /// </summary>
    private static (List<BenchmarkReportQuestion> Questions, List<QuestionItem> Items) BuildQuestions(
        IReadOnlyList<BenchmarkBatterySuiteProfile> suites, BenchmarkBatteryComparisonSource source, IReadOnlyList<BenchmarkRun> subjectRuns)
    {
        var questions = new List<BenchmarkReportQuestion>();
        var items = new List<QuestionItem>();

        foreach (var suite in suites)
        {
            int suiteNumber = suite.SuiteIndex + 1;
            var suiteRunIds = new HashSet<long>(
                source.Suites.Where(s => s.SuiteIndex == suite.SuiteIndex).SelectMany(s => s.Runs).Select(r => r.Id));
            var suiteRuns = subjectRuns.Where(r => suiteRunIds.Contains(r.Id)).OrderBy(r => r.Id).ToList();

            var suiteItems = (suite.Statistics?.Items ?? Array.Empty<BenchmarkGroupItemStatistics>())
                .OrderBy(i => i.OrderIndex)
                .ThenBy(i => i.QuestionId)
                .ThenBy(i => i.ItemRevision)
                .ToList();

            for (int k = 0; k < suiteItems.Count; k++)
            {
                var item = suiteItems[k];
                var answers = suiteRuns
                    .SelectMany(r => (r.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
                    .Where(a => BenchmarkItemAnalysis.QuestionKey(a) == item.QuestionId && a.ItemRevisionUsed == item.ItemRevision)
                    .ToList();
                var counting = answers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex).ToList();
                var timed = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();

                var question = new BenchmarkReportQuestion
                {
                    Number = questions.Count + 1,
                    QuestionKey = Inv(item.QuestionId),
                    ItemRevisionUsed = item.ItemRevision,
                    OrderIndex = item.OrderIndex,
                    Band = answers.Count > 0
                        ? BenchmarkReportFacts.BandNameOf(answers[0])
                        : BenchmarkDifficultyBands.BandOf(item.AssessedDifficulty ?? (int)Math.Round(item.Weight, MidpointRounding.AwayFromZero)).ToString(),
                    AuthoredBand = answers.Select(a => a.Difficulty).Where(d => Enum.IsDefined(d)).Select(d => d.ToString()).FirstOrDefault(),
                    Score = item.Mean,
                    CriticalError = item.CriticalErrorCount > 0,
                    CriticalErrorCount = item.CriticalErrorCount,
                    RefutedClaims = answers.Sum(a => a.ClaimsRefutedCount ?? 0),
                    RefutedAnswerSentences = BenchmarkReportFacts.RefutedAnswerSentencesOf(answers),
                    ToolCalls = counting.Count > 0 ? counting.Average(a => (double)BenchmarkChatTransfer.ToolCallCountsFor(a).Values.Sum()) : 0,
                    ModelTimeMs = timed.Count > 0 ? timed.Average(a => (double)a.ModelTimeMs) : null,
                    RunCount = item.RunCount,
                    Reference = ReferenceOf(suiteNumber, k + 1),
                    Suite = suiteNumber,
                    Detailed = false
                };

                questions.Add(question);
                items.Add(new QuestionItem(question, suite, item));
            }
        }

        return (questions, items);
    }

    /// <summary>
    /// The questions given in detail and their content. Per suite, at most <paramref name="perSuite"/>:
    /// those with critical errors first (most rounds first, then the lowest mean), then the lowest and
    /// the highest remaining mean scores alternately. The priority order takes each suite's first
    /// choice, then each suite's second, and so on. Each question's content is its answer in the run
    /// whose score is the median of its rounds (the lower middle at an even count, ties by run id).
    /// </summary>
    private static (BenchmarkReportContentSnapshot Content, List<BenchmarkReportQuestion> Priority) BuildDetail(
        BenchmarkReportFactSheet sheet,
        IReadOnlyList<QuestionItem> items,
        IReadOnlyDictionary<long, BenchmarkRun> runsById,
        int answerExcerptChars,
        int perSuite)
    {
        var chosenBySuite = items
            .GroupBy(i => i.Question.Suite ?? 0)
            .OrderBy(g => g.Key)
            .Select(g => Choose(g.ToList(), perSuite))
            .ToList();

        var priority = new List<QuestionItem>();
        for (int rank = 0; rank < perSuite; rank++)
        {
            foreach (var chosen in chosenBySuite)
            {
                if (rank < chosen.Count) priority.Add(chosen[rank]);
            }
        }

        var picks = new List<(QuestionItem Item, long RunId)>();
        foreach (var candidate in priority)
        {
            var scored = candidate.Item.Scores
                .Zip(candidate.Item.RunIds, (score, runId) => (Score: score, RunId: runId))
                .Where(x => runsById.ContainsKey(x.RunId))
                .OrderBy(x => x.Score)
                .ThenBy(x => x.RunId)
                .ToList();
            if (scored.Count == 0) continue;
            picks.Add((candidate, scored[(scored.Count - 1) / 2].RunId));
        }

        var content = new BenchmarkReportContentSnapshot { AnswerExcerptChars = Math.Max(0, answerExcerptChars) };
        var detailed = new List<BenchmarkReportQuestion>();
        foreach (var group in picks.GroupBy(p => p.RunId).OrderBy(g => g.Key))
        {
            var runContent = BenchmarkReportContent.Build(new[] { runsById[group.Key] }, answerExcerptChars);
            var block = new BenchmarkReportContentRun { RunId = group.Key };

            foreach (var (item, _) in group)
            {
                var captured = runContent.Runs
                    .SelectMany(r => r.Questions)
                    .FirstOrDefault(c => string.Equals(c.QuestionKey, item.Question.QuestionKey, StringComparison.Ordinal)
                                         && c.ItemRevisionUsed == item.Question.ItemRevisionUsed);
                if (captured == null) continue;

                captured.Number = item.Question.Number;
                block.Questions.Add(captured);
                item.Question.Detailed = true;
                detailed.Add(item.Question);
            }

            if (block.Questions.Count == 0) continue;
            block.Questions = block.Questions.OrderBy(q => q.Number).ToList();
            content.Runs.Add(block);
        }

        // The priority order of the questions that carry content.
        var order = priority.Select(p => p.Question).Where(q => q.Detailed == true).ToList();
        return (content, order);
    }

    /// <summary>One suite's choice, in its own priority order.</summary>
    private static List<QuestionItem> Choose(IReadOnlyList<QuestionItem> suiteItems, int perSuite)
    {
        var chosen = suiteItems
            .Where(i => i.Item.CriticalErrorCount > 0)
            .OrderByDescending(i => i.Item.CriticalErrorCount)
            .ThenBy(i => i.Item.Mean)
            .ThenBy(i => i.Question.Number)
            .Take(perSuite)
            .ToList();

        var rest = suiteItems
            .Where(i => i.Item.CriticalErrorCount == 0)
            .OrderBy(i => i.Item.Mean)
            .ThenBy(i => i.Question.Number)
            .ToList();

        bool lowest = true;
        while (chosen.Count < perSuite && rest.Count > 0)
        {
            var next = lowest ? rest[0] : rest[^1];
            rest.Remove(next);
            chosen.Add(next);
            lowest = !lowest;
        }

        return chosen;
    }

    /// <summary>
    /// Leaves out the detail of the questions at the end of <paramref name="priority"/> until the
    /// largest prompt fits in <paramref name="maxPromptChars"/>, and says what was left out. A prompt
    /// still over the budget with no detail left is noted as well.
    /// </summary>
    private static List<BenchmarkReportValidationNote> FitPromptBudget(
        BenchmarkReportFactSheet sheet,
        BenchmarkReportContentSnapshot content,
        List<BenchmarkReportQuestion> priority,
        int maxPromptChars)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        if (maxPromptChars <= 0) return notes;

        int size = PromptChars(sheet, content);
        if (size <= maxPromptChars) return notes;

        var dropped = new List<BenchmarkReportQuestion>();
        var remaining = new List<BenchmarkReportQuestion>(priority);

        while (size > maxPromptChars && remaining.Count > 0)
        {
            // Each pass drops by the blocks' own sizes, then measures the whole prompt again.
            int excess = size - maxPromptChars;
            while (excess > 0 && remaining.Count > 0)
            {
                var last = remaining[^1];
                excess -= BenchmarkReportPackPrompt.DetailBlockLength(sheet, content, last.Number);
                Drop(content, last);
                remaining.RemoveAt(remaining.Count - 1);
                dropped.Add(last);
            }
            size = PromptChars(sheet, content);
        }

        if (dropped.Count > 0)
        {
            var references = dropped.OrderBy(q => q.Number).Select(q => q.Reference ?? "Q" + Inv(q.Number)).ToList();
            notes.Add(new BenchmarkReportValidationNote
            {
                Rule = PromptBudgetRule,
                Location = "prompt",
                Message = "The writer's prompt is limited to " + maxPromptChars.ToString("#,0", CultureInfo.InvariantCulture)
                    + " characters, so the full detail of " + Inv(dropped.Count) + (dropped.Count == 1 ? " question" : " questions")
                    + " was left out and only " + (dropped.Count == 1 ? "its row was" : "their rows were") + " given: "
                    + string.Join(", ", references) + "."
            });
        }

        if (size > maxPromptChars)
        {
            notes.Add(new BenchmarkReportValidationNote
            {
                Rule = PromptBudgetRule,
                Location = "prompt",
                Message = "The writer's prompt is " + size.ToString("#,0", CultureInfo.InvariantCulture)
                    + " characters with no question in detail, above its limit of "
                    + maxPromptChars.ToString("#,0", CultureInfo.InvariantCulture) + "."
            });
        }

        return notes;
    }

    private static void Drop(BenchmarkReportContentSnapshot content, BenchmarkReportQuestion question)
    {
        question.Detailed = false;
        foreach (var run in content.Runs)
        {
            run.Questions.RemoveAll(q => q.Number == question.Number);
        }
        content.Runs.RemoveAll(r => r.Questions.Count == 0);
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    private static void AddIdentityFacts(
        BenchmarkReportFacts.FactList facts,
        BenchmarkReportFactSheet sheet,
        BenchmarkModelComparisonEntryDto subject,
        IReadOnlyList<BenchmarkRun> subjectRuns,
        BenchmarkModelComparisonDto comparison,
        int eligibleCount)
    {
        facts.Add("subject.label", subject.Label, subject.Label);
        facts.Add("subject.provider", subject.Provider, subject.Provider);
        facts.Add("subject.modelId", subject.ModelId, subject.ModelId);
        facts.Add("subject.thinkingLevel", subject.ThinkingLevel, subject.ThinkingLevel ?? "not set");
        facts.Add("subject.runs", subjectRuns.Count, Inv(subjectRuns.Count));
        facts.Add("subject.state", subject.State, subject.State);

        facts.Unavailable("suite.name", SuiteNameReason);
        int examItems = subject.Quality?.ExamItemCount ?? sheet.Questions.Count;
        facts.Add("suite.questions", examItems, Inv(examItems));

        string chat = BenchmarkCandidatePromptOptions.FromJson(subjectRuns[0].CandidatePromptOptionsJson).Describe();
        facts.Add("config.chat", chat, chat);

        facts.Add("comparison.models", eligibleCount, Inv(eligibleCount));
        facts.Add("comparison.pricingBasis", comparison.PricingBasisLabel, comparison.PricingBasisLabel);
        BenchmarkReportFacts.AddPricingBasisKind(facts, comparison);
        if (string.IsNullOrWhiteSpace(comparison.BaselineSignature))
        {
            facts.Unavailable("comparison.signature", "The comparison recorded no baseline signature.");
        }
        else
        {
            facts.Add("comparison.signature", comparison.BaselineSignature, comparison.BaselineSignature);
        }
    }

    /// <summary>The persisted count-weighted dimension means, and the peers' mean of theirs.</summary>
    private static void AddDimensionFacts(
        BenchmarkReportFacts.FactList facts,
        BenchmarkBatteryStatisticsResult result,
        IReadOnlyList<BenchmarkBatteryStatisticsResult?> peerResults)
    {
        foreach (var (key, name) in DimensionKeys)
        {
            string prefix = "dimension." + key;
            double? own = DimensionOf(result, name);
            var peerValues = peerResults.Select(r => r == null ? null : DimensionOf(r, name)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            double? peerMean = peerValues.Count > 0 ? peerValues.Average() : null;

            if (own.HasValue)
            {
                facts.Add(prefix, own.Value, BenchmarkReportFormat.Whole(own.Value));
            }
            else
            {
                facts.Unavailable(prefix, "The battery analysis withheld this dimension: a suite has no figure for it.");
            }

            if (peerMean.HasValue)
            {
                facts.Add(prefix + ".peerMean", peerMean.Value, BenchmarkReportFormat.Whole(peerMean.Value));
            }
            else
            {
                facts.Unavailable(prefix + ".peerMean", "No peer has this figure.");
            }

            if (own.HasValue && peerMean.HasValue)
            {
                facts.Add(prefix + ".difference", own.Value - peerMean.Value, BenchmarkReportFormat.WholeDifference(own.Value, peerMean.Value));
            }
            else
            {
                facts.Unavailable(prefix + ".difference", "The model or its peers lack this figure.");
            }
        }
    }

    private static double? DimensionOf(BenchmarkBatteryStatisticsResult result, string name)
        => result.Dimensions.FirstOrDefault(d => string.Equals(d.Dimension, name, StringComparison.OrdinalIgnoreCase))?.Mean;

    /// <summary>The questions per assessed and per authored band; no band score.</summary>
    private static void AddBandFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        foreach (var (key, band) in Bands)
        {
            string name = band.ToString();
            int assessed = questions.Count(q => string.Equals(q.Band, name, StringComparison.Ordinal));
            facts.Add("band." + key + ".questions", assessed, Inv(assessed));
            facts.Unavailable("band." + key + ".score", BandReason);
            facts.Unavailable("band." + key + ".peerMean", BandReason);
            facts.Unavailable("band." + key + ".difference", BandReason);

            int authored = questions.Count(q => string.Equals(q.AuthoredBand, name, StringComparison.Ordinal));
            facts.Add("bands.authored." + key, authored, Inv(authored));
        }
    }

    /// <summary>
    /// Scored answers and critical errors from the persisted item rows; the claim rulings from the
    /// persisted usage; the refuted answer sentences summed over the questions.
    /// </summary>
    private static void AddErrorAndClaimFacts(
        BenchmarkReportFacts.FactList facts, BenchmarkBatteryStatisticsResult result, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        int scored = questions.Sum(q => q.RunCount);
        int critical = questions.Sum(q => q.CriticalErrorCount ?? 0);
        facts.Add("answers.scored", scored, Inv(scored));
        facts.Add("errors.critical", critical, Inv(critical) + " of " + Inv(scored) + " answers");

        var usage = result.Usage;
        if (usage == null || usage.ClaimsChecked == 0)
        {
            foreach (var key in new[] { "claims.supported", "claims.refuted", "claims.indeterminate", "claims.refutedAnswerSentences" })
            {
                facts.Unavailable(key, "No claim verifier ruled on these answers.");
            }
            return;
        }

        facts.Add("claims.supported", usage.ClaimsSupported, Inv(usage.ClaimsSupported));
        facts.Add("claims.refuted", usage.ClaimsRefuted, Inv(usage.ClaimsRefuted));
        facts.Add("claims.indeterminate", usage.ClaimsIndeterminate, Inv(usage.ClaimsIndeterminate));

        if (questions.Any(q => q.RefutedAnswerSentences == null))
        {
            facts.Unavailable("claims.refutedAnswerSentences", RefutedSentencesReason);
        }
        else
        {
            int sentences = questions.Sum(q => q.RefutedAnswerSentences!.Value);
            facts.Add("claims.refutedAnswerSentences", sentences, Inv(sentences));
        }
    }

    /// <summary>
    /// <c>tools.failed</c> and <c>tools.refusedByBudget</c> from the member runs' loaded per-call rows, in
    /// place of what the answers' unloaded tool rows gave; left as they are when the outcomes are unavailable.
    /// </summary>
    private static void SetToolOutcomes(List<BenchmarkReportFact> facts, BenchmarkBatteryAnswerOutcomes? outcomes)
    {
        if (outcomes == null || outcomes.ToolCallsFailed is not int failed || outcomes.ToolCallsRefusedByBudget is not int refused) return;

        for (int i = 0; i < facts.Count; i++)
        {
            int? count = facts[i].Key switch
            {
                "tools.failed" => failed,
                "tools.refusedByBudget" => refused,
                _ => null
            };
            if (count is int n)
            {
                facts[i] = new BenchmarkReportFact { Key = facts[i].Key, Value = JsonValue.Create(n), Display = Inv(n) };
            }
        }
    }

    private static void AddBatteryFacts(
        BenchmarkReportFacts.FactList facts, BenchmarkBatteryComparisonSource source, BenchmarkBatteryStatisticsResult result, int memberRuns)
    {
        facts.Text("battery.name", source.BatteryName);
        if (source.DefinitionRevision is int revision)
        {
            facts.Add("battery.revision", revision, Inv(revision));
        }
        else
        {
            facts.Unavailable("battery.revision", "The battery run's definition snapshot cannot be read.");
        }

        facts.Text("battery.scheme", SchemeName(result.Scheme));
        int suiteCount = result.SuiteCount > 0 ? result.SuiteCount : result.Suites.Count;
        facts.Add("battery.suiteCount", suiteCount, Inv(suiteCount) + (suiteCount == 1 ? " suite" : " suites"));
        facts.Add("battery.runsPerSuite", source.RunsPerSuite, Inv(source.RunsPerSuite) + (source.RunsPerSuite == 1 ? " run" : " runs"));
        facts.Add("battery.memberRuns", memberRuns, Inv(memberRuns) + (memberRuns == 1 ? " run" : " runs"));

        int rounds = result.OverallIndex?.RoundCount ?? 0;
        if (rounds > 0)
        {
            facts.Add("battery.rounds", rounds, Inv(rounds) + (rounds == 1 ? " complete round" : " complete rounds"));
        }
        else
        {
            facts.Unavailable("battery.rounds", "The rounds are ragged: no round has an index in every suite.");
        }

        facts.Text("battery.definitionSha256", Prefix(source.DefinitionSha256));
        if (string.IsNullOrWhiteSpace(source.ComparabilityClassSha256))
        {
            facts.Unavailable("battery.classSha256", "The battery analysis recorded no comparability class.");
        }
        else
        {
            facts.Text("battery.classSha256", Prefix(source.ComparabilityClassSha256));
        }

        facts.Add("battery.pooledIdentity", result.PooledIdentityHolds,
            result.PooledIdentityHolds
                ? "the Overall Index equals one difficulty-weighted index over every question of every suite"
                : "the Overall Index is a composite of the suite indices, not one pooled index over every question");

        if (result.CriticalErrorRate is double rate)
        {
            facts.Add("battery.criticalErrorRate", rate, BenchmarkReportFormat.Percent(rate * 100));
        }
        else
        {
            facts.Unavailable("battery.criticalErrorRate", "The battery analysis is incomplete.");
        }

        if (result.Speed?.OverallSpeedIndex is double speedIndex)
        {
            facts.Add("battery.speedIndex", speedIndex, BenchmarkReportFormat.Whole(speedIndex) + " / 100");
        }
        else
        {
            facts.Unavailable("battery.speedIndex", "A suite has no Speed Index.");
        }

        if (result.BetweenSuiteStandardDeviation is double sd)
        {
            facts.Add("battery.suiteIndexSd", sd, BenchmarkReportFormat.OneDecimal(sd) + " points");
        }
        else
        {
            facts.Unavailable("battery.suiteIndexSd", "Fewer than two suites have an index.");
        }

        if (result.BetweenSuiteRange is double range)
        {
            facts.Add("battery.suiteIndexRange", range, BenchmarkReportFormat.OneDecimal(range) + " points");
        }
        else
        {
            facts.Unavailable("battery.suiteIndexRange", "Fewer than two suites have an index.");
        }

        int excluded = result.ExcludedMembers.Count;
        facts.Add("battery.excludedMembers", excluded, Inv(excluded) + (excluded == 1 ? " member run" : " member runs"));

        var caveats = result.Caveats.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        for (int i = 0; i < caveats.Count; i++)
        {
            facts.Text("battery.caveat." + Inv(i + 1), caveats[i].Trim());
        }
    }

    /// <summary>Each suite's row of the persisted profile, as <c>suite.&lt;n&gt;.*</c>.</summary>
    private static void AddSuiteFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<BenchmarkBatterySuiteProfile> suites)
    {
        const string incomplete = "The suite has no usable member run with a scored question.";

        foreach (var suite in suites)
        {
            string prefix = "suite." + Inv(suite.SuiteIndex + 1) + ".";
            facts.Text(prefix + "name", suite.SuiteName);

            if (suite.Weight is double weight)
            {
                facts.Add(prefix + "weight", weight, BenchmarkReportFormat.OneDecimal(weight * 100) + " %");
            }
            else
            {
                facts.Unavailable(prefix + "weight", "The battery's weights could not be computed.");
            }

            if (suite.Index is double index)
            {
                facts.Add(prefix + "index", index, BenchmarkReportFormat.Whole(index) + " / 100");
            }
            else
            {
                facts.Unavailable(prefix + "index", incomplete);
            }

            if (suite.Contribution is double contribution)
            {
                facts.Add(prefix + "contribution", contribution, BenchmarkReportFormat.OneDecimal(contribution) + " points");
            }
            else
            {
                facts.Unavailable(prefix + "contribution", suite.Index.HasValue ? "The battery's weights could not be computed." : incomplete);
            }

            if (suite.CombinedLower is double lower && suite.CombinedUpper is double upper)
            {
                facts.Text(prefix + "interval", BenchmarkReportFormat.Whole(lower) + "–" + BenchmarkReportFormat.Whole(upper));
            }
            else
            {
                facts.Unavailable(prefix + "interval", "No interval could be computed for this suite.");
            }

            facts.Add(prefix + "scoredItems", suite.ScoredItemCount,
                Inv(suite.ScoredItemCount) + " of " + Inv(suite.ExamItemCount) + (suite.ExamItemCount == 1 ? " question" : " questions"));
            facts.Add(prefix + "runs", suite.UsableMemberCount, Inv(suite.UsableMemberCount) + (suite.UsableMemberCount == 1 ? " run" : " runs"));

            if (suite.MeanSpeedIndex is double speed)
            {
                facts.Add(prefix + "speedIndex", speed, BenchmarkReportFormat.Whole(speed) + " / 100");
            }
            else
            {
                facts.Unavailable(prefix + "speedIndex", "No member run of this suite recorded a Speed Index.");
            }

            if (suite.MeanCostPerRun is double cost)
            {
                facts.Add(prefix + "costPerRun", cost, BenchmarkReportFormat.Usd(cost));
            }
            else
            {
                facts.Unavailable(prefix + "costPerRun", "No price card was resolved for every member run of this suite.");
            }

            if (suite.CriticalErrorRate is double rate)
            {
                facts.Add(prefix + "criticalErrorRate", rate, BenchmarkReportFormat.Percent(rate * 100));
            }
            else
            {
                facts.Unavailable(prefix + "criticalErrorRate", incomplete);
            }
        }
    }

    /// <summary>
    /// The panel facts the battery's persisted panel agreement block states, in place of the means of
    /// the runs' figures: the pooled ICC, the member-alone and reference-reader composites under the
    /// declared weights, the reader's offset and the mean absolute difference. Nothing changes when
    /// the result has no block.
    /// </summary>
    private static void AddPanelAgreementFacts(BenchmarkReportFacts.FactList facts, BenchmarkBatteryPanelAgreement? panel)
    {
        if (panel == null) return;

        if (panel.IntraclassCorrelation is double icc)
        {
            facts.Replace("panel.icc", icc,
                icc.ToString("0.00", CultureInfo.InvariantCulture)
                + " (pooled over " + Inv(panel.PairCount ?? 0) + " answers both members scored)");
        }

        if (panel.MemberAAloneIndex is double memberA)
        {
            facts.Replace("panel.memberAAlone", memberA, BenchmarkReportFormat.OneDecimal(memberA) + " / 100");
        }

        if (panel.MemberBAloneIndex is double memberB)
        {
            facts.Replace("panel.memberBAlone", memberB, BenchmarkReportFormat.OneDecimal(memberB) + " / 100");
        }

        if (panel.ReferenceReaderIndex is double reader)
        {
            facts.Replace("panel.referenceReaderIndex", reader, BenchmarkReportFormat.OneDecimal(reader) + " / 100");
        }

        if (panel.ReferenceReaderOffset is double offset)
        {
            facts.Replace("panel.referenceReaderOffset", offset, BenchmarkReportFormat.SignedOneDecimal(offset) + " points");
        }

        if (panel.MeanAbsoluteDelta is double meanAbs)
        {
            facts.Replace("panel.meanAbsDelta", meanAbs, BenchmarkReportFormat.OneDecimal(meanAbs) + " points");
        }
    }

    /// <summary>The Overall Index under each weighting scheme, and with each suite left out.</summary>
    private static void AddSensitivityFacts(BenchmarkReportFacts.FactList facts, BenchmarkBatteryStatisticsResult result)
    {
        foreach (var row in result.WeightingSensitivity.GroupBy(r => r.Scheme).Select(g => g.First()))
        {
            facts.Add(SensitivityKey(row.Scheme), row.Index,
                BenchmarkReportFormat.Whole(row.Index) + " / 100 under the " + SchemeName(row.Scheme) + " weights"
                + (row.Declared ? " (the declared scheme)" : string.Empty));
        }

        // Advisory, and only for a battery with a panel member: a battery without one has no such figure.
        if (result.PanelVerificationClearedOverall is double panelOverall)
        {
            facts.Add(PanelVerificationClearedSensitivityKey, panelOverall,
                BenchmarkReportFormat.Whole(panelOverall) + " / 100 with each panel member's Accuracy one level higher where the claim verifier"
                + " supported every sentence that member charged, or every out-of-rubric claim it raised; advisory, a lower bound, moves no score");
        }

        foreach (var row in result.LeaveOneSuiteOut.GroupBy(r => r.SuiteIndex).Select(g => g.First()))
        {
            string key = "loo." + Inv(row.SuiteIndex + 1);
            if (row.Index is double index)
            {
                facts.Add(key, index,
                    BenchmarkReportFormat.Whole(index) + " / 100 without " + row.SuiteName
                    + (row.Change is double change ? ", a change of " + BenchmarkReportFormat.SignedOneDecimal(change) + " points" : string.Empty));
            }
            else
            {
                facts.Unavailable(key, "The Overall Index without this suite could not be computed.");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Entries
    // ---------------------------------------------------------------------------------------------

    private static List<BenchmarkReportEntryFigures> BuildEntries(
        BenchmarkModelComparisonEntryDto subject,
        BenchmarkBatteryComparisonSource subjectSource,
        IReadOnlyList<BenchmarkRun> subjectRuns,
        BenchmarkReportFacts.EntryStats subjectStats,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter)> peers,
        IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible,
        IReadOnlyDictionary<string, BenchmarkBatteryComparisonSource> sources)
    {
        var qualities = eligible.Where(e => e.Quality != null).Select(e => e.Quality!.PointEstimate).ToList();
        var speeds = eligible.Where(e => !e.SpeedDegraded && e.Speed?.ModelTimeP50Ms != null)
            .Select(e => e.Speed!.ModelTimeP50Ms!.Value).ToList();
        var costs = eligible.Where(e => !e.CostDegraded && e.Cost?.CandidateCostPerQuestionUsd != null)
            .Select(e => e.Cost!.CandidateCostPerQuestionUsd!.Value).ToList();

        BenchmarkReportEntryFigures Figures(
            BenchmarkModelComparisonEntryDto entry, string? letter, BenchmarkBatteryComparisonSource? source, IReadOnlyList<BenchmarkRun> runs)
        {
            double? p50 = entry.SpeedDegraded ? null : entry.Speed?.ModelTimeP50Ms;
            double? perQuestion = entry.CostDegraded ? null : entry.Cost?.CandidateCostPerQuestionUsd;

            var extra = new BenchmarkReportFacts.FactList();
            foreach (var (key, name) in DimensionKeys)
            {
                var mean = source?.Result == null ? null : DimensionOf(source.Result, name);
                if (mean.HasValue) extra.Add("dimension." + key, mean.Value, BenchmarkReportFormat.Whole(mean.Value));
                else extra.Unavailable("dimension." + key, "The battery analysis withheld this dimension.");
            }

            if (letter == null && subjectStats.ToolCallsPerQuestion is double calls)
            {
                extra.Add("tools.callsPerQuestion", calls, BenchmarkReportFormat.OneDecimal(calls));
            }
            else
            {
                extra.Unavailable("tools.callsPerQuestion", letter == null ? "No answer counts toward the index." : PeerAnswersReason);
            }

            var items = (source?.Result?.Suites ?? Array.Empty<BenchmarkBatterySuiteProfile>())
                .SelectMany(s => s.Statistics?.Items ?? Array.Empty<BenchmarkGroupItemStatistics>())
                .ToList();
            if (items.Count > 0)
            {
                int scored = items.Sum(i => i.RunCount);
                int critical = items.Sum(i => i.CriticalErrorCount);
                extra.Add("errors.critical", critical, Inv(critical) + " of " + Inv(scored) + " answers");
            }
            else
            {
                extra.Unavailable("errors.critical", "The battery analysis holds no item rows.");
            }

            var dates = runs.Select(r => r.CompletedAtUtc ?? r.StartedAtUtc).ToList();

            return new BenchmarkReportEntryFigures
            {
                EntryKey = entry.Key,
                PeerLetter = letter,
                IsSubject = letter == null,
                QualityIndex = entry.Quality?.PointEstimate,
                QualityLower = entry.Quality?.IntervalLower,
                QualityUpper = entry.Quality?.IntervalUpper,
                QualityRank = entry.Quality != null ? BenchmarkReportFacts.RankOf(entry.Quality.PointEstimate, qualities, true) : null,
                ModelTimeP50Ms = p50,
                SpeedRank = p50.HasValue ? BenchmarkReportFacts.RankOf(p50.Value, speeds, false) : null,
                SpeedDegraded = entry.SpeedDegraded,
                CostPerQuestionUsd = perQuestion,
                CostRank = perQuestion.HasValue ? BenchmarkReportFacts.RankOf(perQuestion.Value, costs, false) : null,
                CostDegraded = entry.CostDegraded,
                RunCount = entry.RunCount,
                HarnessVersion = BenchmarkReportFacts.EntryHarnessVersion(runs),
                FirstRunUtc = dates.Count > 0 ? dates.Min() : null,
                LastRunUtc = dates.Count > 0 ? dates.Max() : null,
                Extra = extra.Sorted()
            };
        }

        var entries = new List<BenchmarkReportEntryFigures> { Figures(subject, null, subjectSource, subjectRuns) };
        foreach (var (entry, letter) in peers)
        {
            var source = sources.GetValueOrDefault(entry.Key);
            var runs = (source?.Runs ?? Array.Empty<BenchmarkRun>()).OrderBy(r => r.Id).ToList();
            entries.Add(Figures(entry, letter, source, runs));
        }
        return entries;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static BenchmarkBatteryReportFactsResult Refuse(string reason) => new() { Refusal = reason };

    private static string Prefix(string? hash)
        => string.IsNullOrWhiteSpace(hash) ? "not recorded" : hash.Length > 12 ? hash[..12] : hash;

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
