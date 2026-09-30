namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;

// The fact sheet of a report pack: every figure a report-pack document prints or cites, computed
// here and never by the writer. Each fact has a stable dotted key, a raw value and a
// culture-invariant Display string. A fact that cannot be stated keeps its key with
// Available = false, Display "not available" and the reason.
//
// Fact keys (sorted by key, ordinal, on the sheet):
//
// Identity and configuration
//   subject.label, subject.provider, subject.modelId   the subject entry's identity
//   subject.thinkingLevel                              "not set" when the configuration has none
//   subject.runs                                       runs behind the subject
//   subject.state                                      Comparable or Degraded
//   suite.name, suite.questions                        the suite, and the questions its exam asked
//   config.chat                                        the candidate prompt options of the lowest-id run
//   comparison.models                                  non-excluded entries, subject included
//   comparison.peerRuns                                the peers' run counts in one clause: "every peer has 1 run"
//   comparison.pricingBasis                            the comparison's pricing-basis sentence
//   comparison.pricingBasisKind                        "catalog" (Current) or "snapshot" (AsRun)
//   comparison.pricedOn                                the catalog date of a catalog basis
//   comparison.signature                               the comparison's baseline signature
//
// Quality (Intelligence Index; higher is better)
//   quality.index            "80 / 100": the point estimate
//   quality.interval         "77–83": the 95 % interval, its bounds rounded as the point is
//   quality.intervalSpan     "6 points": the printed upper bound minus the printed lower bound
//   quality.intervalBasis    which sources of variation the interval covers
//   quality.rank             "2nd of 3" among entries with a quality figure
//   quality.intervalOverlap  descriptive: which peers' intervals overlap the subject's, by letter
//   quality.peerMedian       median of the peers' point estimates
//   quality.peerBest         best peer point estimate
//   quality.rawIndex         mean over runs of the pre-cap index
//   quality.unweightedMean   mean over runs of the equal-weight mean of answer quality
//   quality.scoredItems      "4 of 4 questions": items with a scored answer of the exam's items
//
// Dimensions (d = accuracy, completeness, conciseness, readability; the assessor's, or in a panel run
// the panel row of BenchmarkPanelDimensions, the mean of both members' averages)
//   dimension.<d>, dimension.<d>.peerMean, dimension.<d>.difference
//   (a .difference fact of a dimension or band keeps the unrounded value and displays the difference
//   of the two printed whole numbers)
//
// Difficulty bands (b = simple, intermediate, advanced; the answer's assessed difficulty, else its fallback)
//   band.<b>.questions, band.<b>.score, band.<b>.peerMean, band.<b>.difference
//
// Speed (median model time per answer, tool time excluded; lower is better)
//   speed.modelTimeP50, speed.modelTimeP90, speed.ttftP50, speed.rank
//
// Cost (candidate spend per question; lower is better)
//   cost.perQuestion, cost.perRun, cost.rank, cost.basis, cost.pricingAsOf, cost.totalRunPerRun
//
// Tokens (the model under test's, per answer that recorded them)
//   tokens.inputPerQuestion, tokens.outputPerQuestion
//
// Errors and claims
//   errors.critical          "1 of 4 answers": answers at least one grader flagged with a critical error
//   claims.supported, claims.refuted, claims.indeterminate   the claim verifier's rulings on the answers' own claims
//
// Tools (succeeded calls, from the per-call rows where the run has them, else the summary)
//   tools.callsPerQuestion, tools.callsPerQuestion.peerMean
//   tools.share.sourceCode, tools.share.wiki, tools.share.structuredLookup, tools.share.knowledgeBase, tools.share.other
//   tools.zeroKnowledgeBaseAnswers                      only when some question is a knowledge-base topic
//   tools.failed, tools.refusedByBudget                 per-call rows only (harness 17 and later)
//
// Panel
//   panel.meanAbsDelta, panel.icc, panel.disagreements, panel.memberAAlone, panel.memberBAlone
//   panel.referenceReaderIndex, panel.referenceReaderOffset   the reference reader's index and signed offset from the panel
//   panel.judgeDependentPairs                           peers, by letter, whose order against the subject depends on the member
//
// Style
//   style.responseStyleConflict                         BenchmarkChatTransfer.HasResponseStyleConflict over the pooled answers,
//                                                       on the panel's dimension averages in a panel run; the value is a boolean
//
// Scoring and provenance
//   scoring.weights, scoring.levels, scoring.criticalErrorCap   from the lowest-id run's profile snapshot
//   scoring.methodVersion, run.harnessVersion                   distinct values, in run-id order
//   run.ids, run.dates
//   run.promptSha256, run.toolGuidesSha256                      distinct 12-character prefixes
//
// Peers (X = each peer's letter; a figure of an axis the peer is degraded on is unavailable with its explanation)
//   peer.X.quality.index, peer.X.quality.interval, peer.X.quality.rank   the peer's own quality figures
//   peer.X.speed.medianSeconds, peer.X.cost.perQuestion                  the peer's median answer time and cost
//   peer.X.runs                                                          "3 runs"
//   peer.X.intervalOverlap                                               boolean: its 95 % interval overlaps the subject's
//   peer.X.pairedDifference, peer.X.pairedInterval, peer.X.sharedQuestions
//       the subject's mean per-question difference from the peer over the questions both scored on the
//       same item revision, and its 95 % paired-bootstrap interval; unavailable below
//       PairedMinimumQuestions shared questions
//   peer.X.pairedExcludesZero                                            boolean: the printed paired interval lies on one
//                                                                        side of zero; unavailable with the paired difference
//
// Each entry's Extra facts (BenchmarkReportEntryFigures.Extra) reuse dimension.<d>,
// tools.callsPerQuestion and errors.critical for that entry alone.
//
// A comparison with no peers is a stand-alone report: every fact that compares the subject with
// peers (the .peerMean and .difference facts, quality.peerMedian, quality.peerBest,
// quality.intervalOverlap, the three ranks, panel.judgeDependentPairs and comparison.peerRuns) keeps its key and is
// unavailable with StandaloneReason; it has no peer.X facts.
//
// Question numbering (shared with BenchmarkReportContent): items are keyed by question and item
// revision (an unlinked answer by its order index and revision). Numbers follow the lowest-id run's
// answers by order index; items only a later run asked follow, in run-id and order-index order.
// For a single run the number is the 1-based rank of the answer's order index.

/// <summary>What <see cref="BenchmarkReportFacts.Build"/> reads.</summary>
public sealed class BenchmarkReportFactsInput
{
    public BenchmarkModelComparisonDto Comparison { get; init; } = default!;

    public string SubjectKey { get; init; } = string.Empty;

    /// <summary>Every run behind every non-excluded entry, with answers and their tool calls, keyed by run id.</summary>
    public IReadOnlyDictionary<long, BenchmarkRun> Runs { get; init; } = default!;
}

/// <summary>A fact sheet, or the reason none can be built.</summary>
public sealed class BenchmarkReportFactsResult
{
    public BenchmarkReportFactSheet? Sheet { get; init; }

    public string? Refusal { get; init; }
}

/// <summary>One numbered item of the subject's exam.</summary>
public sealed record BenchmarkReportQuestionSlot(
    int Number,
    string ItemKey,
    string QuestionKey,
    int? ItemRevisionUsed,
    int OrderIndex);

/// <summary>
/// Builds the fact sheet of one comparison entry against its peers. Pure: no I/O, no clock, no
/// culture. Every collection on the sheet is in an explicit order.
/// </summary>
public static class BenchmarkReportFacts
{
    public const string SupportBothGraders = "Both graders";
    public const string SupportOneGraderDifferentProvider = "One grader — different provider";
    public const string SupportOneGraderSameProvider = "One grader — same provider as the model";
    public const string SupportGradersDisagree = "Graders disagree";
    public const string SupportSingleAssessor = "Single assessor";
    /// <summary>The stored support label of an item citing no row; documents print it as <see cref="SupportComputedDisplay"/>.</summary>
    public const string SupportComputed = "Computed";

    /// <summary>How <see cref="SupportComputed"/> is printed.</summary>
    public const string SupportComputedDisplay = "From per-question results";

    // The single-grader labels of fact sheets stored before format version 5.
    private const string LegacySupportOneGraderDifferentFamily = "One grader — different family";
    private const string LegacySupportOneGraderSameFamily = "One grader — same family as the model";

    public const string NotAvailable = "not available";

    /// <summary>Why a peer fact is unavailable on a sheet with no peers.</summary>
    public const string StandaloneReason = "A stand-alone run report has no peers.";

    /// <summary>Why <c>tools.zeroKnowledgeBaseAnswers</c> is unavailable when no question is a knowledge-base topic.</summary>
    public const string NoKnowledgeBaseTopicReason =
        "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base.";

    public const string PanelMemberARole = "Panel member A";
    public const string PanelMemberBRole = "Panel member B";
    public const string AssessorRole = "Assessor";
    public const string ReferenceReaderRole = "Reference reader";
    public const string SecondReaderRole = "Second reader";
    public const string ClaimVerifierRole = "Claim verifier";

    /// <summary>The <see cref="BenchmarkReportFindingRow.Status"/> of a non-panel synthesis finding.</summary>
    public const string SingleStatus = "Single";

    /// <summary>The value of <c>comparison.pricingBasisKind</c> for today's catalog prices.</summary>
    public const string PricingBasisCatalog = "catalog";

    /// <summary>The value of <c>comparison.pricingBasisKind</c> for the prices stored with each run.</summary>
    public const string PricingBasisSnapshot = "snapshot";

    /// <summary>A paired difference needs at least this many questions both sides scored.</summary>
    public const int PairedMinimumQuestions = 5;

    /// <summary>Resamples of the question list behind a paired-bootstrap interval.</summary>
    public const int PairedBootstrapResamples = 10_000;

    /// <summary>The bootstrap seed when no comparison key can be derived from the entry keys.</summary>
    public const int FallbackPairedSeed = 0;

    /// <summary>The display of a true <c>peer.X.pairedExcludesZero</c>.</summary>
    public const string PairedExcludesZeroDisplay = "the paired interval excludes zero";

    /// <summary>The display of a false <c>peer.X.pairedExcludesZero</c>.</summary>
    public const string PairedIncludesZeroDisplay = "the paired interval includes zero";

    /// <summary>
    /// A peer counts toward <see cref="BenchmarkReportQuestion.PeersAbove"/> when its mean on the item
    /// is more than this many quality points above the subject's.
    /// </summary>
    public const double PeerAboveMarginPoints = 5.0;

    /// <summary><see cref="BenchmarkReportEntryFigures.HarnessVersion"/> of an entry whose runs differ.</summary>
    public const string MixedHarnessVersion = "mixed";

    private const string PairwiseSignificanceMeasure = "Pairwise significance";

    private static readonly string[] Dimensions = { "accuracy", "completeness", "conciseness", "readability" };

    private static readonly (string Name, BenchmarkDifficulty Band)[] Bands =
    {
        ("simple", BenchmarkDifficulty.Simple),
        ("intermediate", BenchmarkDifficulty.Intermediate),
        ("advanced", BenchmarkDifficulty.Advanced)
    };

    private static readonly string[] RoleOrder =
    {
        PanelMemberARole, AssessorRole, PanelMemberBRole, ReferenceReaderRole, SecondReaderRole, ClaimVerifierRole
    };

    /// <summary>
    /// Support labels from strongest to weakest. An item citing several rows takes the strongest;
    /// an item citing no row is <see cref="SupportComputed"/>.
    /// </summary>
    private static readonly string[] SupportStrength =
    {
        SupportBothGraders, SupportOneGraderDifferentProvider, SupportSingleAssessor, SupportOneGraderSameProvider, SupportGradersDisagree
    };

    public static BenchmarkReportFactsResult Build(BenchmarkReportFactsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comparison = input.Comparison ?? throw new ArgumentException("A comparison is required.", nameof(input));
        var runsById = input.Runs ?? new Dictionary<long, BenchmarkRun>();

        var subject = comparison.Entries.FirstOrDefault(e => string.Equals(e.Key, input.SubjectKey, StringComparison.Ordinal));
        if (subject == null)
        {
            return Refuse($"The comparison has no entry \"{input.SubjectKey}\".");
        }

        if (subject.Excluded)
        {
            return Refuse(string.IsNullOrWhiteSpace(subject.Explanation)
                ? "The subject is excluded from the comparison."
                : subject.Explanation);
        }

        var missing = subject.RunIds.Where(id => !runsById.ContainsKey(id)).OrderBy(id => id).ToList();
        if (subject.RunIds.Count == 0 || missing.Count > 0)
        {
            return Refuse(missing.Count > 0
                ? "Run(s) " + string.Join(", ", missing.Select(id => Inv(id))) + " of the subject were not loaded."
                : "The subject has no runs.");
        }

        var subjectRuns = RunsOf(subject, runsById);
        var subjectStats = EntryStats.Of(subjectRuns);

        var peerEntries = comparison.Entries
            .Where(e => !e.Excluded && !string.Equals(e.Key, subject.Key, StringComparison.Ordinal))
            .OrderByDescending(e => e.Quality?.PointEstimate ?? double.NegativeInfinity)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();

        var peers = new List<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)>();
        for (int i = 0; i < peerEntries.Count; i++)
        {
            peers.Add((peerEntries[i], LetterFor(i), EntryStats.Of(RunsOf(peerEntries[i], runsById))));
        }

        var slots = NumberQuestions(subjectRuns);
        var numberByItem = slots.ToDictionary(s => s.ItemKey, s => s.Number, StringComparer.Ordinal);

        var sheet = new BenchmarkReportFactSheet
        {
            SubjectKey = subject.Key,
            SubjectKind = subject.SourceKind,
            SubjectLabel = subject.Label,
            SubjectDisplayName = subject.ModelDisplayName,
            SubjectProvider = subject.Provider,
            SubjectModelId = subject.ModelId,
            SubjectThinkingLevel = subject.ThinkingLevel,
            SubjectRunIds = subject.RunIds.OrderBy(id => id).ToList(),
            SubjectState = subject.State,
            SubjectExplanation = subject.Explanation,
            SuiteId = subject.SuiteId,
            SuiteName = subject.SuiteName ?? comparison.BaselineSuiteName ?? string.Empty,
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
            Graders = BuildGraders(subjectRuns, subject.Provider),
            PurposeStatements = subjectRuns
                .Select(r => r.PurposeStatementUsed?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        var significance = comparison.ExcludedMeasures
            .FirstOrDefault(m => string.Equals(m.Measure, PairwiseSignificanceMeasure, StringComparison.Ordinal));
        sheet.NoSignificanceSummary = significance?.Summary ?? string.Empty;
        sheet.NoSignificanceInstead = significance?.Instead ?? string.Empty;

        var facts = new FactList();
        var eligible = new List<BenchmarkModelComparisonEntryDto> { subject };
        eligible.AddRange(peers.Select(p => p.Entry));

        AddIdentityFacts(facts, sheet, subject, subjectRuns, comparison, eligible.Count);
        AddQualityFacts(facts, subject, peers, eligible, subjectRuns);
        AddDimensionAndBandFacts(facts, subjectStats, peers.Select(p => p.Stats).ToList());
        AddSpeedFacts(facts, subject, eligible);
        AddCostFacts(facts, subject, eligible);
        AddTokenFacts(facts, subjectStats);
        AddErrorAndClaimFacts(facts, subjectStats);
        AddToolFacts(facts, subjectStats, subjectRuns, peers.Select(p => p.Stats).ToList());
        AddPanelFacts(facts, subject, subjectRuns, comparison, peers);
        AddStyleFact(facts, subjectRuns, subjectStats);
        AddScoringAndProvenanceFacts(facts, subjectRuns);
        AddPeerFacts(facts, subject, peers, eligible);

        sheet.Questions = BuildQuestions(slots, subjectStats, peers.Select(p => p.Stats).ToList());
        sheet.PairedDifferences = AddPairedDifferences(
            facts, sheet.Questions, peers, PairedSeed(comparison.Entries.Select(e => e.Key)));

        if (peers.Count == 0)
        {
            facts.Withhold(IsPeerFact, StandaloneReason);
        }

        sheet.Facts = facts.Sorted();
        sheet.Rows = BuildRows(subjectRuns, numberByItem, subject.Provider);
        sheet.Entries = BuildEntries(subject, subjectStats, peers, eligible, runsById);
        sheet.KnownNames = BuildKnownNames(sheet);

        return new BenchmarkReportFactsResult { Sheet = sheet };
    }

    /// <summary>
    /// The support label of a writer item from the evidence it cites: the strongest label among the
    /// <c>R&lt;n&gt;</c> rows it cites, in the order Both graders, One grader — different provider,
    /// Single assessor, One grader — same provider as the model, Graders disagree; <c>Computed</c> when
    /// it cites no row (fact keys and questions only).
    /// </summary>
    public static string SupportLabelFor(IReadOnlyList<string>? evidence, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int best = int.MaxValue;
        foreach (var id in evidence ?? Array.Empty<string>())
        {
            var row = sheet.Rows.FirstOrDefault(r => string.Equals(r.Id, id?.Trim(), StringComparison.Ordinal));
            if (row == null) continue;

            int strength = Array.IndexOf(SupportStrength, row.SupportLabel);
            if (strength >= 0 && strength < best) best = strength;
        }

        return best == int.MaxValue ? SupportComputed : SupportStrength[best];
    }

    /// <summary>
    /// Rewrites the single-grader support labels of a sheet stored before format version 5 to their
    /// provider wording. Idempotent.
    /// </summary>
    public static void NormalizeSupportLabels(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        foreach (var row in sheet.Rows)
        {
            row.SupportLabel = NormalizeSupportLabel(row.SupportLabel);
        }
    }

    /// <summary>A support label in its provider wording; any label but the two legacy ones is returned unchanged.</summary>
    public static string NormalizeSupportLabel(string supportLabel) => supportLabel switch
    {
        LegacySupportOneGraderDifferentFamily => SupportOneGraderDifferentProvider,
        LegacySupportOneGraderSameFamily => SupportOneGraderSameProvider,
        _ => supportLabel
    };

    /// <summary>
    /// The subject's exam, numbered: the lowest-id run's answers by order index (then answer id), then
    /// items only later runs asked, in run-id and order-index order.
    /// </summary>
    public static IReadOnlyList<BenchmarkReportQuestionSlot> NumberQuestions(IReadOnlyList<BenchmarkRun>? runs)
    {
        var slots = new List<BenchmarkReportQuestionSlot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in (runs ?? Array.Empty<BenchmarkRun>()).OrderBy(r => r.Id))
        {
            foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
            {
                string key = ItemKeyOf(answer);
                if (!seen.Add(key)) continue;

                slots.Add(new BenchmarkReportQuestionSlot(
                    slots.Count + 1, key, QuestionKeyOf(answer), answer.ItemRevisionUsed, answer.OrderIndex));
            }
        }

        return slots;
    }

    /// <summary>The item an answer belongs to: question and revision, or order index and revision when unlinked.</summary>
    public static string ItemKeyOf(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        string revision = answer.ItemRevisionUsed?.ToString(CultureInfo.InvariantCulture) ?? "?";
        return BenchmarkItemAnalysis.QuestionKey(answer) is long id
            ? "q" + Inv(id) + "@" + revision
            : "o" + Inv(answer.OrderIndex) + "@" + revision;
    }

    /// <summary>The question id as text, or <c>order:&lt;n&gt;</c> for an unlinked answer.</summary>
    public static string QuestionKeyOf(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return BenchmarkItemAnalysis.QuestionKey(answer) is long id ? Inv(id) : "order:" + Inv(answer.OrderIndex);
    }

    /// <summary>The difficulty band's name an answer is reported under.</summary>
    public static string BandNameOf(BenchmarkRunAnswer answer)
        => BandOf(answer).ToString();

    /// <summary><c>A</c>, <c>B</c>, … <c>Z</c>, <c>AA</c>, … for a 0-based index.</summary>
    public static string LetterFor(int index)
    {
        var sb = new StringBuilder();
        int n = index;
        do
        {
            sb.Insert(0, (char)('A' + n % 26));
            n = n / 26 - 1;
        }
        while (n >= 0);
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    private static void AddIdentityFacts(
        FactList facts,
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

        facts.Add("suite.name", sheet.SuiteName, sheet.SuiteName);
        int examItems = subject.Quality?.ExamItemCount ?? NumberQuestions(subjectRuns).Count;
        facts.Add("suite.questions", examItems, Inv(examItems));

        var options = BenchmarkCandidatePromptOptions.FromJson(subjectRuns[0].CandidatePromptOptionsJson);
        string chat = options.Describe();
        facts.Add("config.chat", chat, chat);

        facts.Add("comparison.models", eligibleCount, Inv(eligibleCount));
        facts.Add("comparison.pricingBasis", comparison.PricingBasisLabel, comparison.PricingBasisLabel);
        AddPricingBasisKind(facts, comparison);
        if (string.IsNullOrWhiteSpace(comparison.BaselineSignature))
        {
            facts.Unavailable("comparison.signature", "The comparison recorded no baseline signature.");
        }
        else
        {
            facts.Add("comparison.signature", comparison.BaselineSignature, comparison.BaselineSignature);
        }
    }

    /// <summary>
    /// Which prices the cost figures use: <c>catalog</c> for the Current basis, with the catalog date
    /// the comparison was computed on, or <c>snapshot</c> for AsRun, each run's stored prices.
    /// </summary>
    private static void AddPricingBasisKind(FactList facts, BenchmarkModelComparisonDto comparison)
    {
        if (string.Equals(comparison.PricingBasis, nameof(BenchmarkModelComparisonPricingBasis.Current), StringComparison.Ordinal))
        {
            facts.Add("comparison.pricingBasisKind", PricingBasisCatalog, PricingBasisCatalog);
            if (comparison.ComputedAtUtc == default)
            {
                facts.Unavailable("comparison.pricedOn", "The comparison recorded no computation date.");
            }
            else
            {
                string date = comparison.ComputedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                facts.Add("comparison.pricedOn", date, date);
            }
            return;
        }

        if (string.Equals(comparison.PricingBasis, nameof(BenchmarkModelComparisonPricingBasis.AsRun), StringComparison.Ordinal))
        {
            facts.Add("comparison.pricingBasisKind", PricingBasisSnapshot, PricingBasisSnapshot);
            facts.Unavailable("comparison.pricedOn", "Each run is priced at the prices stored with it.");
            return;
        }

        facts.Unavailable("comparison.pricingBasisKind", "The comparison recorded an unknown pricing basis.");
        facts.Unavailable("comparison.pricedOn", "The comparison recorded an unknown pricing basis.");
    }

    private static void AddQualityFacts(
        FactList facts,
        BenchmarkModelComparisonEntryDto subject,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers,
        IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible,
        IReadOnlyList<BenchmarkRun> subjectRuns)
    {
        var quality = subject.Quality;
        if (quality == null)
        {
            foreach (var key in new[] { "quality.index", "quality.interval", "quality.intervalSpan", "quality.intervalBasis", "quality.rank", "quality.intervalOverlap", "quality.scoredItems" })
            {
                facts.Unavailable(key, "The comparison computed no quality figure for this model.");
            }
        }
        else
        {
            // The point and both bounds are rounded the same way; the half-width is not printed.
            facts.Add("quality.index", quality.PointEstimate, BenchmarkReportFormat.Whole(quality.PointEstimate) + " / 100");

            if (quality.IntervalLower.HasValue && quality.IntervalUpper.HasValue)
            {
                string lower = BenchmarkReportFormat.Whole(quality.IntervalLower.Value);
                string upper = BenchmarkReportFormat.Whole(quality.IntervalUpper.Value);
                facts.Text("quality.interval", lower + "–" + upper);

                // From the printed bounds, so the span always matches the interval's own arithmetic.
                int span = int.Parse(upper, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                    - int.Parse(lower, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                facts.Add("quality.intervalSpan", span, Inv(span) + (span == 1 ? " point" : " points"));
            }
            else
            {
                facts.Unavailable("quality.interval", "No interval could be computed.");
                facts.Unavailable("quality.intervalSpan", "No interval could be computed.");
            }

            facts.Add("quality.intervalBasis", quality.IntervalBasis, quality.IntervalBasis);

            var ranked = eligible.Where(e => e.Quality != null).Select(e => e.Quality!.PointEstimate).ToList();
            int rank = RankOf(quality.PointEstimate, ranked, higherIsBetter: true);
            facts.Add("quality.rank", rank, BenchmarkReportFormat.Rank(rank, ranked.Count));

            AddIntervalOverlap(facts, quality, peers);

            facts.Add("quality.scoredItems", quality.ItemCount,
                Inv(quality.ItemCount) + " of " + Inv(quality.ExamItemCount) + (quality.ExamItemCount == 1 ? " question" : " questions"));
        }

        var peerEstimates = peers.Where(p => p.Entry.Quality != null).Select(p => p.Entry.Quality!.PointEstimate).ToList();
        if (peerEstimates.Count == 0)
        {
            facts.Unavailable("quality.peerMedian", "There are no peers with a quality figure.");
            facts.Unavailable("quality.peerBest", "There are no peers with a quality figure.");
        }
        else
        {
            double median = Median(peerEstimates);
            double best = peerEstimates.Max();
            facts.Add("quality.peerMedian", median, BenchmarkReportFormat.Whole(median));
            facts.Add("quality.peerBest", best, BenchmarkReportFormat.Whole(best));
        }

        var rawIndices = subjectRuns.Select(RawIndexOf).ToList();
        if (rawIndices.All(v => v.HasValue))
        {
            double raw = rawIndices.Average(v => v!.Value);
            facts.Add("quality.rawIndex", raw, BenchmarkReportFormat.Whole(raw) + " / 100");
        }
        else
        {
            facts.Unavailable("quality.rawIndex", "A run has no scored answer.");
        }

        var unweighted = subjectRuns.Select(UnweightedMeanOf).ToList();
        if (unweighted.All(v => v.HasValue))
        {
            double mean = unweighted.Average(v => v!.Value);
            facts.Add("quality.unweightedMean", mean, BenchmarkReportFormat.Whole(mean) + " / 100");
        }
        else
        {
            facts.Unavailable("quality.unweightedMean", "A run has no scored answer.");
        }
    }

    /// <summary>
    /// Which peers' 95 % intervals overlap the subject's, by letter. Descriptive only: the comparison
    /// runs no significance test, and the wording never implies one.
    /// </summary>
    private static void AddIntervalOverlap(
        FactList facts,
        BenchmarkModelComparisonQualityDto quality,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers)
    {
        if (!quality.IntervalLower.HasValue || !quality.IntervalUpper.HasValue)
        {
            facts.Unavailable("quality.intervalOverlap", "The subject's quality interval could not be computed.");
            return;
        }

        double lower = quality.IntervalLower.Value;
        double upper = quality.IntervalUpper.Value;

        var letters = peers
            .Where(p => p.Entry.Quality?.IntervalLower is double l && p.Entry.Quality?.IntervalUpper is double u
                        && lower <= u && l <= upper)
            .Select(p => p.Letter)
            .ToList();

        facts.Add("quality.intervalOverlap", letters.Count, OverlapSentence(letters));
    }

    /// <summary>"its 95 % interval overlaps those of Models A and B", or of one model, or of none.</summary>
    public static string OverlapSentence(IReadOnlyList<string> letters)
    {
        ArgumentNullException.ThrowIfNull(letters);
        return letters.Count switch
        {
            0 => "its 95 % interval overlaps no other model's",
            1 => "its 95 % interval overlaps that of Model " + letters[0],
            _ => "its 95 % interval overlaps those of Models " + BenchmarkReportFormat.LetterList(letters)
        };
    }

    private static void AddDimensionAndBandFacts(FactList facts, EntryStats subject, IReadOnlyList<EntryStats> peers)
    {
        foreach (string dimension in Dimensions)
        {
            AddComparedFact(facts, "dimension." + dimension, subject.DimensionMean(dimension),
                PeerMean(peers, s => s.DimensionMean(dimension)), subject.DimensionMissingReason);
        }

        foreach (var (name, band) in Bands)
        {
            int questions = subject.ItemCountIn(band);
            facts.Add("band." + name + ".questions", questions, Inv(questions));

            string key = "band." + name;
            var score = subject.BandMean(band);
            var peerMean = PeerMean(peers, s => s.BandMean(band));

            if (score.HasValue)
            {
                facts.Add(key + ".score", score.Value, BenchmarkReportFormat.Whole(score.Value));
            }
            else
            {
                facts.Unavailable(key + ".score", "The model has no scored answer in this band.");
            }

            AddPeerComparison(facts, key, score, peerMean);
        }
    }

    /// <summary><paramref name="key"/> itself, <c>.peerMean</c> and <c>.difference</c>.</summary>
    private static void AddComparedFact(FactList facts, string key, double? value, double? peerMean, string missingReason)
    {
        if (value.HasValue)
        {
            facts.Add(key, value.Value, BenchmarkReportFormat.Whole(value.Value));
        }
        else
        {
            facts.Unavailable(key, missingReason);
        }

        AddPeerComparison(facts, key, value, peerMean);
    }

    /// <summary>
    /// <c>.peerMean</c> and <c>.difference</c>. The difference's value is unrounded; its display is the
    /// difference of the two printed whole numbers, so it always matches the figures beside it.
    /// </summary>
    private static void AddPeerComparison(FactList facts, string key, double? value, double? peerMean)
    {
        if (peerMean.HasValue)
        {
            facts.Add(key + ".peerMean", peerMean.Value, BenchmarkReportFormat.Whole(peerMean.Value));
        }
        else
        {
            facts.Unavailable(key + ".peerMean", "No peer has this figure.");
        }

        if (value.HasValue && peerMean.HasValue)
        {
            double difference = value.Value - peerMean.Value;
            facts.Add(key + ".difference", difference, BenchmarkReportFormat.WholeDifference(value.Value, peerMean.Value));
        }
        else
        {
            facts.Unavailable(key + ".difference", "The model or its peers lack this figure.");
        }
    }

    private static void AddSpeedFacts(
        FactList facts, BenchmarkModelComparisonEntryDto subject, IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible)
    {
        string[] keys = { "speed.modelTimeP50", "speed.modelTimeP90", "speed.ttftP50", "speed.rank" };
        if (subject.SpeedDegraded)
        {
            foreach (var key in keys) facts.Unavailable(key, subject.Explanation);
            return;
        }

        var speed = subject.Speed;
        AddSeconds(facts, "speed.modelTimeP50", speed?.ModelTimeP50Ms);
        AddSeconds(facts, "speed.modelTimeP90", speed?.ModelTimeP90Ms);
        AddSeconds(facts, "speed.ttftP50", speed?.TtftP50Ms);

        if (speed?.ModelTimeP50Ms is double p50)
        {
            var ranked = eligible.Where(e => !e.SpeedDegraded && e.Speed?.ModelTimeP50Ms != null)
                .Select(e => e.Speed!.ModelTimeP50Ms!.Value).ToList();
            int rank = RankOf(p50, ranked, higherIsBetter: false);
            facts.Add("speed.rank", rank, BenchmarkReportFormat.Rank(rank, ranked.Count));
        }
        else
        {
            facts.Unavailable("speed.rank", "No model time was recorded.");
        }
    }

    private static void AddSeconds(FactList facts, string key, double? milliseconds)
    {
        if (milliseconds.HasValue)
        {
            facts.Add(key, milliseconds.Value, BenchmarkReportFormat.Seconds(milliseconds.Value));
        }
        else
        {
            facts.Unavailable(key, "Not recorded.");
        }
    }

    private static void AddCostFacts(
        FactList facts, BenchmarkModelComparisonEntryDto subject, IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible)
    {
        var cost = subject.Cost;
        string[] axisKeys = { "cost.perQuestion", "cost.perRun", "cost.rank" };

        if (subject.CostDegraded)
        {
            foreach (var key in axisKeys) facts.Unavailable(key, subject.Explanation);
        }
        else if (cost?.CandidateCostPerQuestionUsd is not double perQuestion)
        {
            foreach (var key in axisKeys) facts.Unavailable(key, "No price card was resolved for every run behind this model.");
        }
        else
        {
            facts.Add("cost.perQuestion", perQuestion, BenchmarkReportFormat.Usd(perQuestion));
            if (cost.CandidateCostPerRunUsd is double perRun)
            {
                facts.Add("cost.perRun", perRun, BenchmarkReportFormat.Usd(perRun));
            }
            else
            {
                facts.Unavailable("cost.perRun", "Not recorded.");
            }

            var ranked = eligible.Where(e => !e.CostDegraded && e.Cost?.CandidateCostPerQuestionUsd != null)
                .Select(e => e.Cost!.CandidateCostPerQuestionUsd!.Value).ToList();
            int rank = RankOf(perQuestion, ranked, higherIsBetter: false);
            facts.Add("cost.rank", rank, BenchmarkReportFormat.Rank(rank, ranked.Count));
        }

        if (cost == null)
        {
            facts.Unavailable("cost.basis", "The comparison computed no cost figure for this model.");
            facts.Unavailable("cost.pricingAsOf", "The comparison computed no cost figure for this model.");
            facts.Unavailable("cost.totalRunPerRun", "The comparison computed no cost figure for this model.");
            return;
        }

        facts.Add("cost.basis", cost.Basis, cost.Basis);

        if (string.IsNullOrWhiteSpace(cost.PricingAsOf))
        {
            facts.Unavailable("cost.pricingAsOf", "The price card publishes no date.");
        }
        else
        {
            facts.Add("cost.pricingAsOf", cost.PricingAsOf, cost.PricingAsOf);
        }

        if (cost.TotalRunCostPerRunUsd is double total)
        {
            facts.Add("cost.totalRunPerRun", total, BenchmarkReportFormat.Usd(total));
        }
        else
        {
            facts.Unavailable("cost.totalRunPerRun", cost.TotalRunCostUnavailableReason ?? "Not recorded.");
        }
    }

    /// <summary>The model under test's mean input and output tokens over the answers that recorded them.</summary>
    private static void AddTokenFacts(FactList facts, EntryStats subject)
    {
        foreach (var (key, tokens) in new (string Key, Func<BenchmarkRunAnswer, int?> Tokens)[]
        {
            ("tokens.inputPerQuestion", a => a.InputTokens),
            ("tokens.outputPerQuestion", a => a.OutputTokens)
        })
        {
            var recorded = subject.All.Select(tokens).Where(t => t.HasValue).Select(t => (double)t!.Value).ToList();
            if (recorded.Count == 0)
            {
                facts.Unavailable(key, "No answer recorded its token counts.");
                continue;
            }

            double mean = recorded.Average();
            facts.Add(key, mean, BenchmarkReportFormat.Count(mean));
        }
    }

    private static void AddErrorAndClaimFacts(FactList facts, EntryStats subject)
    {
        facts.Add("errors.critical", subject.CriticalCount,
            Inv(subject.CriticalCount) + " of " + Inv(subject.Counting.Count) + " answers");

        bool verified = subject.Counting.Any(a =>
            a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue);
        if (!verified)
        {
            foreach (var key in new[] { "claims.supported", "claims.refuted", "claims.indeterminate" })
            {
                facts.Unavailable(key, "No claim verifier ruled on these answers.");
            }
            return;
        }

        int supported = subject.Counting.Sum(a => a.ClaimsSupportedCount ?? 0);
        int refuted = subject.Counting.Sum(a => a.ClaimsRefutedCount ?? 0);
        int indeterminate = subject.Counting.Sum(a => a.ClaimsIndeterminateCount ?? 0);
        facts.Add("claims.supported", supported, Inv(supported));
        facts.Add("claims.refuted", refuted, Inv(refuted));
        facts.Add("claims.indeterminate", indeterminate, Inv(indeterminate));
    }

    private static void AddToolFacts(
        FactList facts, EntryStats subject, IReadOnlyList<BenchmarkRun> subjectRuns, IReadOnlyList<EntryStats> peers)
    {
        AddComparedCalls(facts, subject.ToolCallsPerQuestion, PeerMean(peers, s => s.ToolCallsPerQuestion));

        var routing = BenchmarkChatTransfer.AnalyzeToolRouting(
            subject.All, BenchmarkRunFinalizer.IsPanelRun(subjectRuns[0]));

        var shares = new (string Key, BenchmarkToolFamily Family)[]
        {
            ("tools.share.sourceCode", BenchmarkToolFamily.SourceCode),
            ("tools.share.wiki", BenchmarkToolFamily.Wiki),
            ("tools.share.structuredLookup", BenchmarkToolFamily.StructuredLookup),
            ("tools.share.knowledgeBase", BenchmarkToolFamily.KnowledgeBase),
            ("tools.share.other", BenchmarkToolFamily.Other)
        };

        foreach (var (key, family) in shares)
        {
            if (routing.TotalCalls == 0)
            {
                facts.Unavailable(key, "No tool call succeeded.");
                continue;
            }

            double share = routing.RunWideStats.First(s => s.Family == family).SharePercentage;
            facts.Add(key, share, BenchmarkReportFormat.Percent(share));
        }

        if (BenchmarkChatTransfer.HasKnowledgeBaseRoutingQuestion(subject.All))
        {
            facts.Add("tools.zeroKnowledgeBaseAnswers", routing.ZeroKnowledgeBaseAnswerCount,
                Inv(routing.ZeroKnowledgeBaseAnswerCount) + " of " + Inv(routing.AnsweredQuestionCount));
        }
        else
        {
            facts.Unavailable("tools.zeroKnowledgeBaseAnswers", NoKnowledgeBaseTopicReason);
        }

        bool recorded = subjectRuns.All(r => HarnessOf(r) is int h && h >= 17);
        if (!recorded)
        {
            facts.Unavailable("tools.failed", "A run predates per-call tool records (harness 17).");
            facts.Unavailable("tools.refusedByBudget", "A run predates per-call tool records (harness 17).");
            return;
        }

        var (_, failed, refused) = BenchmarkToolCallRecorder.Outcomes(
            subject.Counting.SelectMany(a => a.ToolCalls ?? new List<BenchmarkRunAnswerToolCall>()));
        facts.Add("tools.failed", failed, Inv(failed));
        facts.Add("tools.refusedByBudget", refused, Inv(refused));
    }

    private static void AddComparedCalls(FactList facts, double? calls, double? peerMean)
    {
        if (calls.HasValue)
        {
            facts.Add("tools.callsPerQuestion", calls.Value, BenchmarkReportFormat.OneDecimal(calls.Value));
        }
        else
        {
            facts.Unavailable("tools.callsPerQuestion", "No answer counts toward the index.");
        }

        if (peerMean.HasValue)
        {
            facts.Add("tools.callsPerQuestion.peerMean", peerMean.Value, BenchmarkReportFormat.OneDecimal(peerMean.Value));
        }
        else
        {
            facts.Unavailable("tools.callsPerQuestion.peerMean", "No peer has this figure.");
        }
    }

    private static void AddPanelFacts(
        FactList facts,
        BenchmarkModelComparisonEntryDto subject,
        IReadOnlyList<BenchmarkRun> subjectRuns,
        BenchmarkModelComparisonDto comparison,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers)
    {
        string[] runKeys =
        {
            "panel.meanAbsDelta", "panel.icc", "panel.disagreements", "panel.memberAAlone", "panel.memberBAlone",
            "panel.referenceReaderIndex", "panel.referenceReaderOffset"
        };

        if (!subjectRuns.All(BenchmarkRunFinalizer.IsPanelRun))
        {
            foreach (var key in runKeys) facts.Unavailable(key, "The model was not graded by an assessor panel in every run.");
        }
        else
        {
            AddMean(facts, "panel.meanAbsDelta", subjectRuns.Select(r => r.PanelMeanAbsDelta).ToList(),
                v => BenchmarkReportFormat.OneDecimal(v) + " points");
            AddMean(facts, "panel.icc", subjectRuns.Select(r => r.PanelIntraclassCorrelation).ToList(),
                v => v.ToString("0.00", CultureInfo.InvariantCulture));

            int graded = subjectRuns.Sum(r => r.PanelGradedAnswerCount ?? 0);
            int disagreements = subjectRuns.Sum(r => r.PanelDisagreementCount ?? 0);
            facts.Add("panel.disagreements", disagreements, Inv(disagreements) + " of " + Inv(graded) + " answers");

            AddMean(facts, "panel.memberAAlone", subjectRuns.Select(r => (double?)r.AssessorOnlyQualityIndex).ToList(),
                v => BenchmarkReportFormat.Whole(v) + " / 100");
            AddMean(facts, "panel.memberBAlone", subjectRuns.Select(r => (double?)r.CoAssessorOnlyQualityIndex).ToList(),
                v => BenchmarkReportFormat.Whole(v) + " / 100");
            AddReferenceReaderFacts(facts, subjectRuns);
        }

        var diagnostics = comparison.PanelDiagnostics;
        if (diagnostics == null)
        {
            facts.Unavailable("panel.judgeDependentPairs", "No run in the comparison was graded by a panel.");
            return;
        }

        if (!diagnostics.Applicable)
        {
            facts.Unavailable("panel.judgeDependentPairs",
                diagnostics.NotApplicableReason ?? "The compared runs were not all graded by the same panel.");
            return;
        }

        var partners = new List<string>();
        foreach (var (entry, letter, _) in peers)
        {
            bool dependent = diagnostics.JudgeDependentPairs.Any(p =>
                (string.Equals(p.FirstEntryKey, subject.Key, StringComparison.Ordinal) && string.Equals(p.SecondEntryKey, entry.Key, StringComparison.Ordinal))
                || (string.Equals(p.SecondEntryKey, subject.Key, StringComparison.Ordinal) && string.Equals(p.FirstEntryKey, entry.Key, StringComparison.Ordinal)));
            if (dependent) partners.Add(letter);
        }

        string display = partners.Count switch
        {
            0 => "none",
            1 => "with Model " + partners[0],
            _ => "with Models " + BenchmarkReportFormat.LetterList(partners)
        };
        facts.Add("panel.judgeDependentPairs", partners.Count, display);
    }

    /// <summary>
    /// The reference reader's Intelligence Index and its mean signed offset from the panel score, each
    /// the mean over the runs whose reader graded of the run report's own per-run figure.
    /// </summary>
    private static void AddReferenceReaderFacts(FactList facts, IReadOnlyList<BenchmarkRun> subjectRuns)
    {
        const string reason = "No reference reader graded these answers.";
        var indices = new List<double>();
        var offsets = new List<double>();

        foreach (var run in subjectRuns.Where(r => r.SecondOpinionAssessorModelConfigurationId.HasValue))
        {
            var answers = run.Answers ?? new List<BenchmarkRunAnswer>();
            if (!answers.Any(a => a.SecondOpinionQualityScore.HasValue)) continue;

            if (BenchmarkReportBuilder.ReferenceReaderIndex(run, answers, out _) is int index) indices.Add(index);
            if (BenchmarkReportBuilder.ReferenceReaderSignedOffset(answers) is double offset) offsets.Add(offset);
        }

        if (indices.Count == 0)
        {
            facts.Unavailable("panel.referenceReaderIndex", reason);
        }
        else
        {
            double mean = indices.Average();
            facts.Add("panel.referenceReaderIndex", mean, BenchmarkReportFormat.Whole(mean) + " / 100");
        }

        if (offsets.Count == 0)
        {
            facts.Unavailable("panel.referenceReaderOffset", reason);
        }
        else
        {
            double mean = offsets.Average();
            facts.Add("panel.referenceReaderOffset", mean, BenchmarkReportFormat.SignedOneDecimal(mean) + " points");
        }
    }

    private static void AddMean(FactList facts, string key, IReadOnlyList<double?> values, Func<double, string> display)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (present.Count == 0)
        {
            facts.Unavailable(key, "Not recorded.");
            return;
        }

        double mean = present.Average();
        facts.Add(key, mean, display(mean));
    }

    /// <summary>
    /// The response-style conflict, read on the panel's dimension averages in a panel entry and on the
    /// assessor's otherwise, as the run report reads it. The value is the boolean; the display reads
    /// as a clause.
    /// </summary>
    private static void AddStyleFact(FactList facts, IReadOnlyList<BenchmarkRun> subjectRuns, EntryStats subject)
    {
        bool conflict;
        double gap = 0.0;
        if (subject.Panel)
        {
            var panel = subject.PanelAverages?.Panel;
            conflict = panel != null
                && BenchmarkChatTransfer.HasResponseStyleConflict(
                    BenchmarkCandidatePromptOptions.FromJson(subjectRuns[0].CandidatePromptOptionsJson).VerboseMode,
                    panel[0].Points, panel[1].Points, panel[2].Points, panel[3].Points);
            if (conflict) gap = panel![0].Points - panel[1].Points;
        }
        else
        {
            conflict = BenchmarkChatTransfer.HasResponseStyleConflict(subjectRuns[0], subject.All, out gap);
        }

        facts.Add("style.responseStyleConflict", conflict,
            conflict
                ? "Completeness is the lowest dimension, " + BenchmarkReportFormat.OneDecimal(gap) + " points below Accuracy"
                : "No response-style conflict");
    }

    private static void AddScoringAndProvenanceFacts(FactList facts, IReadOnlyList<BenchmarkRun> subjectRuns)
    {
        var scoring = ScoringOf(subjectRuns[0]);
        string weights = "Accuracy " + BenchmarkReportFormat.Percent(scoring.Accuracy * 100)
            + ", Completeness " + BenchmarkReportFormat.Percent(scoring.Completeness * 100)
            + ", Conciseness " + BenchmarkReportFormat.Percent(scoring.Conciseness * 100)
            + ", Readability " + BenchmarkReportFormat.Percent(scoring.Readability * 100);
        facts.Text("scoring.weights", weights);

        string levels = string.Join(", ", scoring.Levels.Select(l => Inv(l)));
        facts.Text("scoring.levels", levels);
        facts.Add("scoring.criticalErrorCap", scoring.CriticalErrorCap, Inv(scoring.CriticalErrorCap));

        string methods = string.Join(", ", subjectRuns.Select(r => Inv(r.ScoringMethodVersion)).Distinct(StringComparer.Ordinal));
        facts.Text("scoring.methodVersion", methods);

        string harness = string.Join(", ", subjectRuns.Select(r => r.HarnessVersion ?? "not recorded").Distinct(StringComparer.Ordinal));
        facts.Text("run.harnessVersion", harness);

        string ids = string.Join(", ", subjectRuns.Select(r => Inv(r.Id)));
        facts.Text("run.ids", ids);

        var dates = subjectRuns.Select(r => r.StartedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(d => d, StringComparer.Ordinal).ToList();
        string dateRange = string.Equals(dates[0], dates[^1], StringComparison.Ordinal) ? dates[0] : dates[0] + " to " + dates[^1];
        facts.Text("run.dates", dateRange);

        facts.Text("run.promptSha256", Prefixes(subjectRuns.Select(r => r.CandidateSystemPromptSha256)));
        facts.Text("run.toolGuidesSha256", Prefixes(subjectRuns.Select(r => r.ToolGuidesSha256)));
    }

    private static string Prefixes(IEnumerable<string?> hashes)
        => string.Join(", ", hashes
            .Select(h => string.IsNullOrWhiteSpace(h) ? "not recorded" : h.Length > 12 ? h[..12] : h)
            .Distinct(StringComparer.Ordinal));

    /// <summary>The key prefix of a peer's own facts: <c>peer.A.</c>.</summary>
    public static string PeerPrefix(string letter) => "peer." + letter + ".";

    /// <summary>
    /// Each peer's own figures: its index, interval and rank, median answer time, cost per question,
    /// runs, and whether its 95 % interval overlaps the subject's. A figure of an axis the peer is
    /// degraded on is unavailable with the peer's explanation.
    /// </summary>
    private static void AddPeerFacts(
        FactList facts,
        BenchmarkModelComparisonEntryDto subject,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers,
        IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible)
    {
        var qualities = eligible.Where(e => e.Quality != null).Select(e => e.Quality!.PointEstimate).ToList();

        foreach (var (entry, letter, _) in peers)
        {
            string prefix = PeerPrefix(letter);
            var quality = entry.Quality;

            if (quality == null)
            {
                foreach (var key in new[] { "quality.index", "quality.interval", "quality.rank", "intervalOverlap" })
                {
                    facts.Unavailable(prefix + key, "The comparison computed no quality figure for this model.");
                }
            }
            else
            {
                facts.Add(prefix + "quality.index", quality.PointEstimate, BenchmarkReportFormat.Whole(quality.PointEstimate) + " / 100");

                if (quality.IntervalLower is double lower && quality.IntervalUpper is double upper)
                {
                    facts.Text(prefix + "quality.interval", BenchmarkReportFormat.Whole(lower) + "–" + BenchmarkReportFormat.Whole(upper));
                }
                else
                {
                    facts.Unavailable(prefix + "quality.interval", "No interval could be computed.");
                }

                int rank = RankOf(quality.PointEstimate, qualities, higherIsBetter: true);
                facts.Add(prefix + "quality.rank", rank, BenchmarkReportFormat.Rank(rank, qualities.Count));

                if (subject.Quality?.IntervalLower is not double subjectLower || subject.Quality?.IntervalUpper is not double subjectUpper)
                {
                    facts.Unavailable(prefix + "intervalOverlap", "The subject's quality interval could not be computed.");
                }
                else if (quality.IntervalLower is not double peerLower || quality.IntervalUpper is not double peerUpper)
                {
                    facts.Unavailable(prefix + "intervalOverlap", "This model's quality interval could not be computed.");
                }
                else
                {
                    bool overlaps = subjectLower <= peerUpper && peerLower <= subjectUpper;
                    facts.Add(prefix + "intervalOverlap", overlaps,
                        overlaps ? "its 95 % interval overlaps the subject's" : "its 95 % interval does not overlap the subject's");
                }
            }

            if (entry.SpeedDegraded)
            {
                facts.Unavailable(prefix + "speed.medianSeconds", entry.Explanation);
            }
            else
            {
                AddSeconds(facts, prefix + "speed.medianSeconds", entry.Speed?.ModelTimeP50Ms);
            }

            if (entry.CostDegraded)
            {
                facts.Unavailable(prefix + "cost.perQuestion", entry.Explanation);
            }
            else if (entry.Cost?.CandidateCostPerQuestionUsd is double perQuestion)
            {
                facts.Add(prefix + "cost.perQuestion", perQuestion, BenchmarkReportFormat.Usd(perQuestion));
            }
            else
            {
                facts.Unavailable(prefix + "cost.perQuestion", "No price card was resolved for every run behind this model.");
            }

            int runs = entry.RunIds.Count;
            facts.Add(prefix + "runs", runs, Inv(runs) + (runs == 1 ? " run" : " runs"));
        }

        AddPeerRuns(facts, peers.Select(p => p.Entry.RunIds.Count).ToList());
    }

    /// <summary>
    /// <c>comparison.peerRuns</c>: the peers' run counts in one clause, "every peer has 1 run" or
    /// "peers have 1 to 3 runs"; the value is the largest count. Unavailable without peers.
    /// </summary>
    private static void AddPeerRuns(FactList facts, IReadOnlyList<int> runCounts)
    {
        if (runCounts.Count == 0)
        {
            facts.Unavailable("comparison.peerRuns", StandaloneReason);
            return;
        }

        facts.Add("comparison.peerRuns", runCounts.Max(), PeerRunsSentence(runCounts));
    }

    /// <summary>"every peer has 2 runs" when the counts agree, "peers have 1 to 3 runs" when they differ.</summary>
    public static string PeerRunsSentence(IReadOnlyList<int> runCounts)
    {
        ArgumentNullException.ThrowIfNull(runCounts);
        if (runCounts.Count == 0) return string.Empty;

        int min = runCounts.Min();
        int max = runCounts.Max();
        return min == max
            ? "every peer has " + Inv(max) + (max == 1 ? " run" : " runs")
            : "peers have " + Inv(min) + " to " + Inv(max) + " runs";
    }

    /// <summary>
    /// The subject's paired difference from each peer, in letter order: over the questions both
    /// scored on the same item revision, the subject's per-question score (a mean over runs for a
    /// group) minus the peer's per-question mean, and its 95 % paired-bootstrap interval. Adds the
    /// <c>peer.X.paired*</c> and <c>peer.X.sharedQuestions</c> facts, unavailable below
    /// <see cref="PairedMinimumQuestions"/> shared questions.
    /// </summary>
    private static List<BenchmarkReportPairedDifference> AddPairedDifferences(
        FactList facts,
        IReadOnlyList<BenchmarkReportQuestion> questions,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers,
        int seed)
    {
        var result = new List<BenchmarkReportPairedDifference>();

        foreach (var (_, letter, stats) in peers)
        {
            var differences = new List<double>();
            foreach (var q in questions.OrderBy(q => q.Number))
            {
                if (q.Score is not double score || q.ItemRevisionUsed is not int revision) continue;
                if (!long.TryParse(q.QuestionKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out long questionId)) continue;
                if (stats.ItemMean(questionId, revision) is double peerMean) differences.Add(score - peerMean);
            }

            string prefix = PeerPrefix(letter);
            var paired = new BenchmarkReportPairedDifference { PeerLetter = letter, SharedQuestions = differences.Count };

            if (differences.Count < PairedMinimumQuestions)
            {
                string reason = "Fewer than five questions were scored for both this model and the subject on the same item revision, "
                    + "too few for a paired difference.";
                facts.Unavailable(prefix + "pairedDifference", reason);
                facts.Unavailable(prefix + "pairedInterval", reason);
                facts.Unavailable(prefix + "pairedExcludesZero", reason);
                facts.Unavailable(prefix + "sharedQuestions", reason);
            }
            else
            {
                var (mean, lower, upper) = PairedBootstrap(differences, seed);
                paired.MeanDifference = mean;
                paired.Lower = lower;
                paired.Upper = upper;

                facts.Add(prefix + "pairedDifference", mean, BenchmarkReportFormat.SignedOneDecimal(mean) + " points");
                facts.Text(prefix + "pairedInterval",
                    BenchmarkReportFormat.SignedOneDecimal(lower) + " to " + BenchmarkReportFormat.SignedOneDecimal(upper));
                bool excludesZero = PairedIntervalExcludesZero(lower, upper);
                facts.Add(prefix + "pairedExcludesZero", excludesZero,
                    excludesZero ? PairedExcludesZeroDisplay : PairedIncludesZeroDisplay);
                facts.Add(prefix + "sharedQuestions", differences.Count, Inv(differences.Count) + " questions");
            }

            result.Add(paired);
        }

        return result;
    }

    /// <summary>
    /// A paired interval excludes zero when both bounds as printed (one decimal) lie on the same side
    /// of zero, so the statement always agrees with the printed interval.
    /// </summary>
    public static bool PairedIntervalExcludesZero(double lower, double upper)
    {
        double printedLower = Math.Round(lower, 1, MidpointRounding.AwayFromZero);
        double printedUpper = Math.Round(upper, 1, MidpointRounding.AwayFromZero);
        return printedLower > 0 || printedUpper < 0;
    }

    /// <summary>
    /// The mean of <paramref name="differences"/> and its 95 % percentile interval: the means of
    /// <see cref="PairedBootstrapResamples"/> resamples of the list with replacement, drawn from
    /// <c>new Random(seed)</c> and sorted, cut at the 2.5th and 97.5th percentiles (the 251st mean
    /// from each end). The same list and seed always give the same interval.
    /// </summary>
    public static (double Mean, double Lower, double Upper) PairedBootstrap(IReadOnlyList<double> differences, int seed)
    {
        ArgumentNullException.ThrowIfNull(differences);
        if (differences.Count == 0) throw new ArgumentException("A paired bootstrap needs at least one difference.", nameof(differences));

        var random = new Random(seed);
        int n = differences.Count;
        var means = new double[PairedBootstrapResamples];
        for (int b = 0; b < means.Length; b++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += differences[random.Next(n)];
            }
            means[b] = sum / n;
        }
        Array.Sort(means);

        int tail = PairedBootstrapResamples * 25 / 1000;
        return (differences.Average(), means[tail], means[means.Length - 1 - tail]);
    }

    /// <summary>
    /// The paired-bootstrap seed of a comparison: the first eight hex digits of its
    /// <see cref="BenchmarkReportComparisonKey"/> over every entry key, excluded entries included, as
    /// an int; <see cref="FallbackPairedSeed"/> when no key can be derived.
    /// </summary>
    public static int PairedSeed(IEnumerable<string>? entryKeys)
    {
        if (entryKeys == null
            || !BenchmarkReportComparisonKey.TryFromEntryKeys(entryKeys, out string key)
            || key.Length < 8
            || !uint.TryParse(key.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint prefix))
        {
            return FallbackPairedSeed;
        }

        return unchecked((int)prefix);
    }

    // ---------------------------------------------------------------------------------------------
    // Questions, rows, entries, graders
    // ---------------------------------------------------------------------------------------------

    private static List<BenchmarkReportQuestion> BuildQuestions(
        IReadOnlyList<BenchmarkReportQuestionSlot> slots, EntryStats subject, IReadOnlyList<EntryStats> peers)
    {
        var questions = new List<BenchmarkReportQuestion>();

        foreach (var slot in slots)
        {
            var answers = subject.All.Where(a => string.Equals(ItemKeyOf(a), slot.ItemKey, StringComparison.Ordinal)).ToList();
            var scored = subject.Scored.Where(s => string.Equals(ItemKeyOf(s.Answer), slot.ItemKey, StringComparison.Ordinal)).ToList();
            var counting = answers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex).ToList();
            var timed = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();

            double? score = scored.Count > 0 ? scored.Average(s => s.Quality) : null;

            double? peerMean = null;
            double? peerMin = null;
            double? peerMax = null;
            int peerCount = 0;
            int peersAbove = 0;
            if (long.TryParse(slot.QuestionKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out long questionId)
                && slot.ItemRevisionUsed is int revision)
            {
                var peerMeans = peers.Select(p => p.ItemMean(questionId, revision)).Where(m => m.HasValue).Select(m => m!.Value).ToList();
                peerCount = peerMeans.Count;
                if (peerCount > 0)
                {
                    peerMean = peerMeans.Average();
                    peerMin = peerMeans.Min();
                    peerMax = peerMeans.Max();
                    if (score is double subjectScore)
                    {
                        peersAbove = peerMeans.Count(m => m - subjectScore > PeerAboveMarginPoints);
                    }
                }
            }

            questions.Add(new BenchmarkReportQuestion
            {
                Number = slot.Number,
                QuestionKey = slot.QuestionKey,
                ItemRevisionUsed = slot.ItemRevisionUsed,
                OrderIndex = slot.OrderIndex,
                Band = answers.Count > 0 ? BandNameOf(answers[0]) : string.Empty,
                Score = score,
                PeerMean = peerMean,
                Difference = score.HasValue && peerMean.HasValue ? score.Value - peerMean.Value : null,
                PeerCount = peerCount,
                PeerMin = peerMin,
                PeerMax = peerMax,
                PeersAbove = peersAbove,
                CriticalError = counting.Any(HasCriticalError),
                RefutedClaims = answers.Sum(a => a.ClaimsRefutedCount ?? 0),
                RefutedAnswerSentences = RefutedAnswerSentencesOf(answers),
                ToolCalls = counting.Count > 0 ? counting.Average(a => (double)SucceededCalls(a)) : 0,
                ModelTimeMs = timed.Count > 0 ? timed.Average(a => (double)a.ModelTimeMs) : null,
                RunCount = scored.Count
            });
        }

        return questions;
    }

    /// <summary>
    /// The distinct answer sentences the verifier refuted (<see cref="BenchmarkReportContent.IsAnswerSentenceRole"/>),
    /// read from the stored verifications by role; a grader's statement never counts. Within one
    /// answer, rulings on one sentence count once under the union manifest's markup-insensitive key
    /// (<see cref="BenchmarkService.ItemKey"/>, ignoring case), so a sentence both accused and quoted
    /// as a critical error is one sentence. Null when an answer's verifications are unreadable or
    /// carry no roles.
    /// </summary>
    internal static int? RefutedAnswerSentencesOf(IEnumerable<BenchmarkRunAnswer> answers)
    {
        int count = 0;
        foreach (var answer in answers)
        {
            if (string.IsNullOrWhiteSpace(answer.ClaimVerificationJson)) continue;

            var verifications = BenchmarkReportContent.ReadVerifications(answer.ClaimVerificationJson);
            if (verifications == null) return null;
            if (verifications.Count == 0) continue;
            if (!BenchmarkClaimRoles.HasRoles(verifications)) return null;

            var refuted = verifications
                .Where(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted
                            && BenchmarkReportContent.IsAnswerSentenceRole(BenchmarkReportContent.RoleOf(v, listHasRoles: true)))
                .Select(v => BenchmarkService.ItemKey(v.Claim))
                .ToList();

            // A ruling without text cannot be matched to another, so each counts on its own.
            count += refuted.Count(k => k.Length == 0)
                     + refuted.Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
        return count;
    }

    /// <summary>
    /// The finding rows: per run, the convergence of both members' findings in a panel run, the
    /// assessor's findings otherwise, their question numbers mapped to the report's numbering; then
    /// merged across runs on kind, category, status and questions, in order of first appearance.
    /// </summary>
    private static List<BenchmarkReportFindingRow> BuildRows(
        IReadOnlyList<BenchmarkRun> subjectRuns, IReadOnlyDictionary<string, int> numberByItem, string subjectProvider)
    {
        var rows = new List<BenchmarkReportFindingRow>();
        var keys = new List<string>();

        foreach (var run in subjectRuns)
        {
            var seenInRun = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in RowsOf(run, numberByItem, subjectProvider))
            {
                string key = row.Kind + "|" + row.Category + "|" + row.Status + "|" + string.Join(",", row.Questions.Select(q => Inv(q)));
                if (!seenInRun.Add(key)) continue;

                int existing = keys.IndexOf(key);
                if (existing >= 0)
                {
                    rows[existing].Recurrence++;
                    continue;
                }

                keys.Add(key);
                rows.Add(row);
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Id = "R" + Inv(i + 1);
        }

        return rows;
    }

    private static IEnumerable<BenchmarkReportFindingRow> RowsOf(
        BenchmarkRun run, IReadOnlyDictionary<string, int> numberByItem, string subjectProvider)
    {
        var answers = run.Answers ?? new List<BenchmarkRunAnswer>();

        List<int> Map(IReadOnlyList<int> orderIndices) => orderIndices
            .Select(q => answers.Where(a => a.OrderIndex == q).OrderBy(a => a.Id).FirstOrDefault())
            .Where(a => a != null)
            .Select(a => numberByItem.TryGetValue(ItemKeyOf(a!), out int n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var findingsA = BenchmarkAssessmentParser.ParseSynthesisFindings(run.AssessmentJson);

        if (!BenchmarkRunFinalizer.IsPanelRun(run))
        {
            foreach (var finding in findingsA)
            {
                yield return new BenchmarkReportFindingRow
                {
                    Kind = finding.Kind,
                    Category = finding.Category,
                    Questions = Map(finding.Questions),
                    Status = SingleStatus,
                    SupportLabel = SupportSingleAssessor,
                    MemberAText = finding.Text
                };
            }
            yield break;
        }

        var findingsB = BenchmarkAssessmentParser.ParseSynthesisFindings(run.CoAssessorSynthesisJson);
        foreach (var row in BenchmarkSynthesisConvergence.Compute(findingsA, findingsB))
        {
            string label = row.Status switch
            {
                BenchmarkConvergenceStatus.Convergent => SupportBothGraders,
                BenchmarkConvergenceStatus.MemberAOnly => SameProvider(run.AssessorModelSnapshot?.Provider, subjectProvider)
                    ? SupportOneGraderSameProvider : SupportOneGraderDifferentProvider,
                BenchmarkConvergenceStatus.MemberBOnly => SameProvider(run.CoAssessorModelSnapshot?.Provider, subjectProvider)
                    ? SupportOneGraderSameProvider : SupportOneGraderDifferentProvider,
                _ => SupportGradersDisagree
            };

            yield return new BenchmarkReportFindingRow
            {
                Kind = row.Kind,
                Category = row.Category,
                Questions = Map(row.Questions),
                Status = row.Status.ToString(),
                SupportLabel = label,
                MemberAText = row.MemberAText,
                MemberBText = row.MemberBText
            };
        }
    }

    private static List<BenchmarkReportEntryFigures> BuildEntries(
        BenchmarkModelComparisonEntryDto subject,
        EntryStats subjectStats,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string Letter, EntryStats Stats)> peers,
        IReadOnlyList<BenchmarkModelComparisonEntryDto> eligible,
        IReadOnlyDictionary<long, BenchmarkRun> runsById)
    {
        var qualities = eligible.Where(e => e.Quality != null).Select(e => e.Quality!.PointEstimate).ToList();
        var speeds = eligible.Where(e => !e.SpeedDegraded && e.Speed?.ModelTimeP50Ms != null)
            .Select(e => e.Speed!.ModelTimeP50Ms!.Value).ToList();
        var costs = eligible.Where(e => !e.CostDegraded && e.Cost?.CandidateCostPerQuestionUsd != null)
            .Select(e => e.Cost!.CandidateCostPerQuestionUsd!.Value).ToList();

        BenchmarkReportEntryFigures Figures(BenchmarkModelComparisonEntryDto entry, string? letter, EntryStats stats)
        {
            double? p50 = entry.SpeedDegraded ? null : entry.Speed?.ModelTimeP50Ms;
            double? perQuestion = entry.CostDegraded ? null : entry.Cost?.CandidateCostPerQuestionUsd;

            var extra = new FactList();
            foreach (string dimension in Dimensions)
            {
                var mean = stats.DimensionMean(dimension);
                if (mean.HasValue) extra.Add("dimension." + dimension, mean.Value, BenchmarkReportFormat.Whole(mean.Value));
                else extra.Unavailable("dimension." + dimension, stats.DimensionMissingReason);
            }
            if (stats.ToolCallsPerQuestion is double calls)
            {
                extra.Add("tools.callsPerQuestion", calls, BenchmarkReportFormat.OneDecimal(calls));
            }
            else
            {
                extra.Unavailable("tools.callsPerQuestion", "No answer counts toward the index.");
            }
            extra.Add("errors.critical", stats.CriticalCount,
                Inv(stats.CriticalCount) + " of " + Inv(stats.Counting.Count) + " answers");

            var runs = RunsOf(entry, runsById);
            var dates = runs.Select(r => r.CompletedAtUtc ?? r.StartedAtUtc).ToList();

            return new BenchmarkReportEntryFigures
            {
                EntryKey = entry.Key,
                PeerLetter = letter,
                IsSubject = letter == null,
                QualityIndex = entry.Quality?.PointEstimate,
                QualityLower = entry.Quality?.IntervalLower,
                QualityUpper = entry.Quality?.IntervalUpper,
                QualityRank = entry.Quality != null ? RankOf(entry.Quality.PointEstimate, qualities, true) : null,
                ModelTimeP50Ms = p50,
                SpeedRank = p50.HasValue ? RankOf(p50.Value, speeds, false) : null,
                SpeedDegraded = entry.SpeedDegraded,
                CostPerQuestionUsd = perQuestion,
                CostRank = perQuestion.HasValue ? RankOf(perQuestion.Value, costs, false) : null,
                CostDegraded = entry.CostDegraded,
                RunCount = entry.RunCount,
                HarnessVersion = EntryHarnessVersion(runs),
                FirstRunUtc = dates.Count > 0 ? dates.Min() : null,
                LastRunUtc = dates.Count > 0 ? dates.Max() : null,
                Extra = extra.Sorted()
            };
        }

        var entries = new List<BenchmarkReportEntryFigures> { Figures(subject, null, subjectStats) };
        entries.AddRange(peers.Select(p => Figures(p.Entry, p.Letter, p.Stats)));
        return entries;
    }

    /// <summary>The runs' harness version, <see cref="MixedHarnessVersion"/> when they differ; null without runs.</summary>
    private static string? EntryHarnessVersion(IReadOnlyList<BenchmarkRun> runs)
    {
        var versions = runs
            .Select(r => string.IsNullOrWhiteSpace(r.HarnessVersion) ? "not recorded" : r.HarnessVersion.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return versions.Count switch
        {
            0 => null,
            1 => versions[0],
            _ => MixedHarnessVersion
        };
    }

    private static List<BenchmarkReportGrader> BuildGraders(IReadOnlyList<BenchmarkRun> runs, string subjectProvider)
    {
        var graders = new List<BenchmarkReportGrader>();

        void Add(string role, SystemAiConfigurationSnapshot? snapshot)
        {
            if (snapshot == null) return;
            if (graders.Any(g => g.Role == role && g.Provider == snapshot.Provider && g.ModelId == snapshot.ModelId
                                 && g.ThinkingLevel == snapshot.ThinkingLevel))
            {
                return;
            }

            graders.Add(new BenchmarkReportGrader
            {
                Role = role,
                Label = snapshot.Label() ?? snapshot.ModelId,
                Provider = snapshot.Provider,
                ModelId = snapshot.ModelId,
                ThinkingLevel = snapshot.ThinkingLevel,
                SameFamilyAsSubject = SameProvider(snapshot.Provider, subjectProvider)
            });
        }

        foreach (var run in runs)
        {
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
            Add(panel ? PanelMemberARole : AssessorRole, run.AssessorModelSnapshot);
            if (panel) Add(PanelMemberBRole, run.CoAssessorModelSnapshot);
            Add(panel ? ReferenceReaderRole : SecondReaderRole, run.SecondOpinionAssessorModelSnapshot);
            Add(ClaimVerifierRole, run.ClaimVerifierModelSnapshot);
        }

        return graders
            .Select((g, i) => (Grader: g, Index: i))
            .OrderBy(x => Array.IndexOf(RoleOrder, x.Grader.Role))
            .ThenBy(x => x.Index)
            .Select(x => x.Grader)
            .ToList();
    }

    private static List<string> BuildKnownNames(BenchmarkReportFactSheet sheet)
    {
        var names = new List<string?>
        {
            sheet.SubjectLabel, sheet.SubjectDisplayName, sheet.SubjectModelId, sheet.SubjectProvider, sheet.SuiteName
        };

        foreach (var peer in sheet.Peers)
        {
            names.Add(peer.Label);
            names.Add(peer.DisplayName);
            names.Add(peer.ModelId);
            names.Add(peer.Provider);
        }

        foreach (var grader in sheet.Graders)
        {
            names.Add(grader.Label);
            names.Add(grader.ModelId);
            names.Add(grader.Provider);
        }

        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static BenchmarkReportFactsResult Refuse(string reason) => new() { Refusal = reason };

    /// <summary>A fact that compares the subject with its peers.</summary>
    public static bool IsPeerFact(string key)
        => key.StartsWith("peer.", StringComparison.Ordinal)
           || key.EndsWith(".peerMean", StringComparison.Ordinal)
           || key.EndsWith(".difference", StringComparison.Ordinal)
           || key is "quality.peerMedian" or "quality.peerBest" or "quality.intervalOverlap"
               or "quality.rank" or "speed.rank" or "cost.rank" or "panel.judgeDependentPairs" or "comparison.peerRuns";

    private static List<BenchmarkRun> RunsOf(BenchmarkModelComparisonEntryDto entry, IReadOnlyDictionary<long, BenchmarkRun> runsById)
        => entry.RunIds
            .Where(runsById.ContainsKey)
            .Select(id => runsById[id])
            .OrderBy(r => r.Id)
            .ToList();

    /// <summary>Competition rank: one more than the number of values strictly better.</summary>
    private static int RankOf(double value, IReadOnlyList<double> values, bool higherIsBetter)
        => 1 + values.Count(v => higherIsBetter ? v > value : v < value);

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static double? PeerMean(IReadOnlyList<EntryStats> peers, Func<EntryStats, double?> figure)
    {
        var values = peers.Select(figure).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    private static bool SameProvider(string? provider, string? subjectProvider)
        => !string.IsNullOrWhiteSpace(provider) && string.Equals(provider?.Trim(), subjectProvider?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static BenchmarkDifficulty BandOf(BenchmarkRunAnswer answer)
        => BenchmarkDifficultyBands.BandOf(answer.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(answer.Difficulty));

    /// <summary>At least one grader flagged a critical error: member A's flag, or member B's in a panel run.</summary>
    private static bool HasCriticalError(BenchmarkRunAnswer answer)
        => answer.CriticalError || answer.CoAssessmentCriticalError == true;

    private static int SucceededCalls(BenchmarkRunAnswer answer)
        => BenchmarkChatTransfer.ToolCallCountsFor(answer).Values.Sum();

    private static int? HarnessOf(BenchmarkRun run)
        => int.TryParse(run.HarnessVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : null;

    /// <summary>The run's pre-cap Intelligence Index, weighted as the published one.</summary>
    private static double? RawIndexOf(BenchmarkRun run)
    {
        bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
        var items = (run.Answers ?? new List<BenchmarkRunAnswer>())
            .Where(BenchmarkRunFinalizer.CountsTowardQualityIndex)
            .Select(a => (BenchmarkScoring.IndexRawQuality(a, panel), (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty))))
            .ToList();
        return BenchmarkScoring.QualityIndex(items);
    }

    private static double? UnweightedMeanOf(BenchmarkRun run)
    {
        bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
        return BenchmarkScoring.UnweightedQualityMean(
            (run.Answers ?? new List<BenchmarkRunAnswer>())
                .Where(BenchmarkRunFinalizer.CountsTowardQualityIndex)
                .Select(a => BenchmarkScoring.IndexQuality(a, panel)));
    }

    private sealed record ScoringFigures(
        double Accuracy, double Completeness, double Conciseness, double Readability, IReadOnlyList<int> Levels, int CriticalErrorCap);

    /// <summary>The weights, level scores and cap from the run's scoring-profile snapshot, else the defaults.</summary>
    private static ScoringFigures ScoringOf(BenchmarkRun run)
    {
        var defaults = BenchmarkScoringConstants.Default;
        var figures = new ScoringFigures(
            defaults.WeightAccuracy, defaults.WeightCompleteness, defaults.WeightConciseness, defaults.WeightReadability,
            defaults.LevelScores, defaults.CriticalErrorCeiling);

        if (string.IsNullOrWhiteSpace(run.ScoringProfileSnapshotJson)) return figures;

        try
        {
            using var doc = JsonDocument.Parse(run.ScoringProfileSnapshotJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return figures;

            double Weight(string name, double fallback)
                => root.TryGetProperty(name, out var p) && p.TryGetDouble(out double v) ? v : fallback;

            IReadOnlyList<int> levels = figures.Levels;
            if (root.TryGetProperty("LevelScoresJson", out var l) && l.ValueKind == JsonValueKind.String)
            {
                try
                {
                    levels = JsonSerializer.Deserialize<List<int>>(l.GetString() ?? string.Empty) ?? levels;
                }
                catch (JsonException)
                {
                    // An unreadable table reads as the default.
                }
            }

            int cap = root.TryGetProperty("CriticalErrorCeiling", out var c) && c.TryGetInt32(out int cv) ? cv : figures.CriticalErrorCap;

            return new ScoringFigures(
                Weight("WeightAccuracy", figures.Accuracy),
                Weight("WeightCompleteness", figures.Completeness),
                Weight("WeightConciseness", figures.Conciseness),
                Weight("WeightReadability", figures.Readability),
                levels,
                cap);
        }
        catch (JsonException)
        {
            return figures;
        }
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Facts under construction; <see cref="Sorted"/> orders them by key, ordinal.</summary>
    private sealed class FactList
    {
        private readonly List<BenchmarkReportFact> _facts = new();

        public void Add(string key, JsonNode? value, string display)
            => _facts.Add(new BenchmarkReportFact { Key = key, Value = value, Display = display });

        public void Add(string key, string? value, string display)
            => Add(key, value == null ? null : (JsonNode?)JsonValue.Create(value), display);

        /// <summary>A fact whose display is its only form: no raw value.</summary>
        public void Text(string key, string display) => Add(key, (JsonNode?)null, display);

        public void Add(string key, double value, string display) => Add(key, (JsonNode)JsonValue.Create(value), display);

        public void Add(string key, int value, string display) => Add(key, (JsonNode)JsonValue.Create(value), display);

        public void Add(string key, bool value, string display) => Add(key, (JsonNode)JsonValue.Create(value), display);

        public void Unavailable(string key, string? reason)
            => _facts.Add(new BenchmarkReportFact
            {
                Key = key,
                Display = NotAvailable,
                Available = false,
                UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Not recorded." : reason
            });

        /// <summary>Every fact whose key matches becomes unavailable with <paramref name="reason"/>, keeping its key.</summary>
        public void Withhold(Func<string, bool> matches, string reason)
        {
            for (int i = 0; i < _facts.Count; i++)
            {
                if (!matches(_facts[i].Key)) continue;
                _facts[i] = new BenchmarkReportFact
                {
                    Key = _facts[i].Key,
                    Display = NotAvailable,
                    Available = false,
                    UnavailableReason = reason
                };
            }
        }

        public List<BenchmarkReportFact> Sorted()
            => _facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>One entry's answers and the per-entry figures the sheet compares.</summary>
    private sealed class EntryStats
    {
        /// <summary>Every answer of the entry's runs, in run-id and order-index order.</summary>
        public List<BenchmarkRunAnswer> All { get; } = new();

        /// <summary>The answers that count toward the quality index.</summary>
        public List<BenchmarkRunAnswer> Counting { get; } = new();

        /// <summary>Counting answers with a published score.</summary>
        public List<(BenchmarkRun Run, BenchmarkRunAnswer Answer, double Quality)> Scored { get; } = new();

        public int CriticalCount => Counting.Count(HasCriticalError);

        public double? ToolCallsPerQuestion => Counting.Count > 0 ? Counting.Average(a => (double)SucceededCalls(a)) : null;

        /// <summary>Every run of the entry was graded by an assessor panel.</summary>
        public bool Panel { get; private set; }

        private BenchmarkPanelDimensionAverages? _panelAverages;
        private bool _panelAveragesComputed;

        /// <summary>
        /// A panel entry's dimension averages over its graded answers, pooled across runs, each run's
        /// level table applied to its own answers; null for a single-assessor entry or no graded answer.
        /// </summary>
        public BenchmarkPanelDimensionAverages? PanelAverages
        {
            get
            {
                if (!_panelAveragesComputed)
                {
                    _panelAverages = Panel
                        ? BenchmarkPanelDimensions.Averages(Scored
                            .Where(s => s.Answer.Status == BenchmarkAnswerStatus.Ok)
                            .Select(s => (s.Answer, (IReadOnlyList<int>?)BenchmarkScoring.ConstantsFromSnapshot(s.Run.ScoringProfileSnapshotJson).LevelScores))
                            .ToList())
                        : null;
                    _panelAveragesComputed = true;
                }
                return _panelAverages;
            }
        }

        public static EntryStats Of(IReadOnlyList<BenchmarkRun> runs)
        {
            var stats = new EntryStats { Panel = runs.Count > 0 && runs.All(BenchmarkRunFinalizer.IsPanelRun) };
            foreach (var run in runs.OrderBy(r => r.Id))
            {
                bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
                foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
                {
                    stats.All.Add(answer);
                    if (!BenchmarkRunFinalizer.CountsTowardQualityIndex(answer)) continue;

                    stats.Counting.Add(answer);
                    if (BenchmarkScoring.IndexQuality(answer, panel) is double quality)
                    {
                        stats.Scored.Add((run, answer, quality));
                    }
                }
            }
            return stats;
        }

        /// <summary>Why <see cref="DimensionMean"/> is null.</summary>
        public string DimensionMissingReason => Panel && PanelAverages is { Panel: null }
            ? "No member B record carries all four dimension levels, so the panel's dimension figures cannot be formed."
            : "No scored answer carries this dimension.";

        /// <summary>The panel row's points in a panel entry, the assessor's mean score otherwise.</summary>
        public double? DimensionMean(string dimension)
        {
            if (Panel)
            {
                int index = Array.IndexOf(Dimensions, dimension);
                return PanelAverages?.Panel is { } panel && index >= 0 ? panel[index].Points : null;
            }

            var values = Scored
                .Select(s => dimension switch
                {
                    "accuracy" => s.Answer.AccuracyScore,
                    "completeness" => s.Answer.CompletenessScore,
                    "conciseness" => s.Answer.ConcisenessScore,
                    _ => s.Answer.ReadabilityScore
                })
                .Where(v => v.HasValue)
                .Select(v => (double)v!.Value)
                .ToList();
            return values.Count > 0 ? values.Average() : null;
        }

        public int ItemCountIn(BenchmarkDifficulty band)
            => All.Where(a => BandOf(a) == band).Select(ItemKeyOf).Distinct(StringComparer.Ordinal).Count();

        public double? BandMean(BenchmarkDifficulty band)
        {
            var values = Scored.Where(s => BandOf(s.Answer) == band).Select(s => s.Quality).ToList();
            return values.Count > 0 ? values.Average() : null;
        }

        /// <summary>Mean published quality on one question and item revision; null when no answer pairs.</summary>
        public double? ItemMean(long questionId, int revision)
        {
            var values = Scored
                .Where(s => BenchmarkItemAnalysis.QuestionKey(s.Answer) == questionId && s.Answer.ItemRevisionUsed == revision)
                .Select(s => s.Quality)
                .ToList();
            return values.Count > 0 ? values.Average() : null;
        }
    }
}

/// <summary>Culture-invariant formatting shared by the fact sheet and the renderer.</summary>
public static class BenchmarkReportFormat
{
    /// <summary>Rounded half away from zero to a whole number; never "-0".</summary>
    public static string Whole(double value)
    {
        double rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary><see cref="Whole"/> with a leading <c>+</c> above zero.</summary>
    public static string Signed(double value)
    {
        string whole = Whole(value);
        return Math.Round(value, 0, MidpointRounding.AwayFromZero) > 0 ? "+" + whole : whole;
    }

    /// <summary>
    /// <see cref="Signed"/> of <c>Whole(value) − Whole(reference)</c>: the difference of the two printed
    /// whole numbers, never a separately rounded difference.
    /// </summary>
    public static string WholeDifference(double value, double reference)
        => Signed(Math.Round(value, 0, MidpointRounding.AwayFromZero) - Math.Round(reference, 0, MidpointRounding.AwayFromZero));

    public static string OneDecimal(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary><see cref="OneDecimal"/> with a leading <c>+</c> above zero: "+16.8", "-2.0", "0.0".</summary>
    public static string SignedOneDecimal(double value)
    {
        string text = OneDecimal(value);
        return Math.Round(value, 1, MidpointRounding.AwayFromZero) > 0 ? "+" + text : text;
    }

    /// <summary>At most one decimal, and none when it is zero: "3", "2.5".</summary>
    public static string CompactDecimal(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>A whole count with thousands separators: "12,345".</summary>
    public static string Count(double value)
    {
        double rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("#,0", CultureInfo.InvariantCulture);
    }

    public static string Seconds(double milliseconds) => OneDecimal(milliseconds / 1000.0) + " s";

    /// <summary>Three decimals, four below one cent.</summary>
    public static string Usd(double value)
    {
        double rounded = Math.Round(value, value >= 0.01 ? 3 : 4, MidpointRounding.AwayFromZero);
        return "$" + rounded.ToString(value >= 0.01 ? "0.000" : "0.0000", CultureInfo.InvariantCulture);
    }

    public static string Percent(double value) => Whole(value) + " %";

    public static string Ordinal(int value)
    {
        int mod100 = Math.Abs(value) % 100;
        string suffix = mod100 is 11 or 12 or 13
            ? "th"
            : (Math.Abs(value) % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    public static string Rank(int rank, int of) => Ordinal(rank) + " of " + of.ToString(CultureInfo.InvariantCulture);

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string LetterList(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
        };
    }
}
