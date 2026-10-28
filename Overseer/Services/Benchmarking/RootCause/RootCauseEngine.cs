namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>A configuration key that differs between two models; <see cref="First"/> and <see cref="Second"/> are null for a key named without its values.</summary>
public sealed record RootCauseConfigDifference(string Name, string? First, string? Second);

/// <summary>One analyzed model as the engine reads it: identity, configuration, entry figures and evidence.</summary>
public sealed class RootCauseModelInput
{
    /// <summary>The model's letter, or <c>subject</c>.</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Null for the subject of a per-model sheet.</summary>
    public string? Letter { get; init; }

    public string Label { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string? ReasoningMode { get; init; }
    public string? ServiceTier { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? ParallelMode { get; init; }
    public string? HarnessVersion { get; init; }

    /// <summary>The model's catalog release date (ISO); null when unknown, and the engine then looks it up in <see cref="RootCauseInput.ReleaseDates"/>.</summary>
    public string? ReleaseDate { get; init; }

    public string EntryKey { get; init; } = string.Empty;
    public bool IsSubject { get; init; }
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();

    public double? QualityIndex { get; init; }
    public double? QualityLower { get; init; }
    public double? QualityUpper { get; init; }

    /// <summary>The mean dimension scores, accuracy to readability, as the sheet states them; null where missing.</summary>
    public IReadOnlyList<double?> Dimensions { get; init; } = new double?[4];

    public bool SpeedDegraded { get; init; }
    public string? SpeedDegradedReason { get; init; }
    public bool CostDegraded { get; init; }
    public string? CostDegradedReason { get; init; }

    /// <summary>The entry's median model time and cost per question, for ordering the models on each axis.</summary>
    public double? ModelTimeP50Ms { get; init; }

    public double? CostPerQuestionUsd { get; init; }

    /// <summary>One run's cost with every role (a battery pass for a battery result), for the next-run estimates.</summary>
    public double? CostPerRunUsd { get; init; }

    public bool StyleConflict { get; init; }

    /// <summary>The comparability keys the entry differs from the baseline on.</summary>
    public IReadOnlyList<string> DifferenceKeys { get; init; } = Array.Empty<string>();

    /// <summary>A battery result's advisory verification-cleared Overall Index; null otherwise.</summary>
    public double? VerificationClearedIndex { get; init; }

    /// <summary>The model's index under each panel member alone; null without a panel.</summary>
    public double? MemberAIndex { get; init; }

    public double? MemberBIndex { get; init; }

    /// <summary>The panel's intraclass correlation, ICC(A,1), over the answers both members scored; null without a panel.</summary>
    public double? PanelIcc { get; init; }

    /// <summary>The model's index under the reference reader alone; null without one, or when it did not score every answer.</summary>
    public double? ReferenceReaderIndex { get; init; }

    public RootCauseModelEvidence Evidence { get; init; } = new();
}

/// <summary>One paired test of a comparison family, oriented by token: the first model earlier.</summary>
public sealed class RootCausePairTest
{
    public string First { get; init; } = string.Empty;
    public string Second { get; init; } = string.Empty;

    /// <summary><c>Intelligence</c>, <c>Speed</c>, <c>Cost</c>, or a dimension (<c>Accuracy</c> …).</summary>
    public string Measure { get; init; } = string.Empty;

    /// <summary><c>reference</c> or <c>allPairs</c>.</summary>
    public string Family { get; init; } = string.Empty;

    public double? Effect { get; init; }
    public double? Lower { get; init; }
    public double? Upper { get; init; }
    public bool Established { get; init; }
    public double? AdjustedPValue { get; init; }
    public int FamilySize { get; init; }

    /// <summary><c>First</c>, <c>Second</c> or <c>None</c>.</summary>
    public string Favors { get; init; } = "None";

    public string? NotTestedReason { get; init; }

    /// <summary>The verdict fact of the test on the sheet.</summary>
    public string? FactKey { get; init; }
}

/// <summary>A configured, enabled Benchmark-role model configuration a next run may use.</summary>
public sealed class RootCauseConfigOption
{
    public long Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string Label { get; init; } = string.Empty;
}

/// <summary>One <c>RecommendedModels</c> entry of <c>Overseer/appsettings.json</c>.</summary>
public sealed class RootCauseRecommendedModel
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
}

/// <summary>What the next-run settings are derived from beyond the runs: the configurations and the recommended models.</summary>
public sealed class RootCauseCatalog
{
    public IReadOnlyList<RootCauseConfigOption> Configurations { get; init; } = Array.Empty<RootCauseConfigOption>();
    public IReadOnlyList<RootCauseRecommendedModel> Recommended { get; init; } = Array.Empty<RootCauseRecommendedModel>();
}

/// <summary>A numbered question of the sheet, keyed as the evidence keys its answers.</summary>
public sealed class RootCauseQuestionRef
{
    public int Number { get; init; }

    /// <summary><c>Q7</c>, or a battery's <c>S2-Q7</c>.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>As <see cref="BenchmarkReportFacts.ItemKeyOf"/> keys its answers.</summary>
    public string ItemKey { get; init; } = string.Empty;
}

/// <summary>Everything the pure computation reads: plain data, no database.</summary>
public sealed class RootCauseInput
{
    /// <summary><c>Comparison</c> or <c>Model</c>.</summary>
    public string Scope { get; init; } = RootCauseEngine.ModelScope;

    /// <summary>One of <see cref="RootCauseOrigins"/>.</summary>
    public string Origin { get; init; } = RootCauseOrigins.ModelPack;

    public DateTime ComputedAtUtc { get; init; }
    public string PricingBasis { get; init; } = string.Empty;
    public string PricingBasisKind { get; init; } = string.Empty;

    /// <summary>The subject first in model scope; letter order in comparison scope.</summary>
    public IReadOnlyList<RootCauseModelInput> Models { get; init; } = Array.Empty<RootCauseModelInput>();

    /// <summary>The comparison's reference model; null in model scope.</summary>
    public string? ReferenceToken { get; init; }

    public IReadOnlyList<RootCausePairTest> PairTests { get; init; } = Array.Empty<RootCausePairTest>();

    /// <summary>Pairs whose order depends on the panel member, by token.</summary>
    public IReadOnlyList<RootCausePair> JudgeDependentPairs { get; init; } = Array.Empty<RootCausePair>();

    /// <summary>A per-model sheet's paired-bootstrap interval against each peer, by peer token.</summary>
    public IReadOnlyDictionary<string, (double Lower, double Upper)> PairedDifferences { get; init; }
        = new Dictionary<string, (double Lower, double Upper)>(StringComparer.Ordinal);

    /// <summary>Each grading role and its provider.</summary>
    public IReadOnlyList<(string Role, string Provider)> Graders { get; init; } = Array.Empty<(string, string)>();

    /// <summary>The panel diagnostics' grader-family gaps (<see cref="RootCauseEvidence.FamilyGaps"/>); empty without them.</summary>
    public IReadOnlyList<RootCauseFamilyGap> FamilyGaps { get; init; } = Array.Empty<RootCauseFamilyGap>();

    /// <summary>The controls of a run- or battery-completion analysis; null otherwise.</summary>
    public RootCauseControlSet? Controls { get; init; }

    public RootCauseCatalog Catalog { get; init; } = new();

    /// <summary>The questions are a battery's, numbered across suites.</summary>
    public bool Battery { get; init; }

    public IReadOnlyList<RootCauseQuestionRef> Questions { get; init; } = Array.Empty<RootCauseQuestionRef>();

    /// <summary>A battery subject's Overall Index change with each suite left out, by suite number.</summary>
    public IReadOnlyDictionary<int, double> LeaveOneSuiteOutChanges { get; init; } = new Dictionary<int, double>();

    /// <summary>A battery subject's suite names, by suite number.</summary>
    public IReadOnlyDictionary<int, string> SuiteNames { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// The catalog release dates (ISO), keyed <c>provider|modelId</c> in lower case
    /// (<see cref="RootCauseEvidence.ReleaseDateKey"/>), for the models without a <see cref="RootCauseModelInput.ReleaseDate"/>;
    /// null when unknown, and the release prior is then checked only for models that carry their own date.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ReleaseDates { get; init; }
}

/// <summary>
/// The root-cause engine: one pure computation for every origin (comparison, per-model pack, run and
/// battery completion). It reads plain inputs (<see cref="RootCauseInput"/>), never the database, and the
/// same inputs always give the same <see cref="RootCauseResult"/>: bootstraps use a fixed seed and every
/// list has an explicit order.
/// </summary>
public static class RootCauseEngine
{
    /// <summary>
    /// The version stored on every result. From version 9 a share whose 95% interval is wider than one hundred
    /// percentage points fails a robustness check, a lead share above the whole gap says that other components pull the
    /// other way, and a rule with several shares has its share span. From version 10 a cost pair whose two sides are not
    /// billed for the same token kinds is split into input and output with a caching effect, every decomposed cost pair
    /// has its cost ratio at equal prices, the analyzed pairs include the same model at adjacent thinking levels and the
    /// pairs of unexpected results, the unexpected results are stored, HQ5 is one hypothesis in comparison scope, and a
    /// profile has where the model gained points and leaves the shared lowest dimension out of where it lost them. From
    /// version 11 the comparison's HQ5 statement names the response-style margin it counts. From version 12 WS1 fails a
    /// robustness check when most of the model's visible text arrived in one burst, the summed time to first output is
    /// the wait before output, each share has a <c>.shareOf</c> fact, WQ4 and HQ6 need the profile's minimum of answers,
    /// and a time-of-day run is one pass launched once per time of day. From version 13 a ruled-out hypothesis is never
    /// an axis's best: an axis whose every hypothesis was ruled out has none. From version 14 a known fact about a
    /// component whose label is plural (<see cref="RootCauseKnownFacts.PluralComponentLabels"/>) takes a plural verb.
    /// From version 15 HQ8 states the direction the data shows, HQ9 shares the net narrowing of the gap, HC1 is not
    /// raised for one model at two levels on one price card, HS5 compares the waits as printed, WC2 states what its
    /// test checks, and a comparison states WQ6 once, for the model whose members are furthest apart.
    /// </summary>
    public const int Version = 15;

    /// <summary>The list output price ratio from which two models of one provider are in different price tiers.</summary>
    public const double TierPriceRatio = 1.5;

    /// <summary>The Intelligence Index points against a quality prior from which a result is unexpected without an established reversal.</summary>
    public const double AnomalyMinPoints = 2.0;

    /// <summary>The ratio of median model times against the speed prior from which a result is unexpected without an established reversal.</summary>
    public const double AnomalyTimeRatio = 1.25;

    /// <summary>The most pairs of unexpected results added to the analyzed pairs.</summary>
    public const int MaxAnomalyPairs = 6;

    public const string ComparisonScope = "Comparison";
    public const string ModelScope = "Model";

    /// <summary>The token of a per-model sheet's subject.</summary>
    public const string SubjectToken = "subject";

    /// <summary>The most pairs a model-scope analysis sets the subject against.</summary>
    public const int MaxPeers = 11;

    public static RootCauseResult Compute(RootCauseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ctx = new RootCauseContext(input);
        var result = new RootCauseResult
        {
            Scope = input.Scope,
            Origin = input.Origin,
            ComputedAtUtc = input.ComputedAtUtc,
            PricingBasis = input.PricingBasis,
            PricingBasisKind = input.PricingBasisKind,
            ReferenceToken = input.ReferenceToken,
            Models = input.Models.Select(m => new RootCauseModel
            {
                Token = m.Token,
                Letter = m.Letter,
                Label = m.Label,
                Provider = m.Provider,
                ModelId = m.ModelId,
                ThinkingLevel = m.ThinkingLevel,
                EntryKey = m.EntryKey,
                IsSubject = m.IsSubject,
                IsReference = m.Token == input.ReferenceToken,
                RunIds = m.RunIds.OrderBy(id => id).ToList()
            }).ToList()
        };

        if (input.Models.Count == 0)
        {
            result.Notes.Add("No model could be analyzed.");
            foreach (string axis in RootCauseAxes.All) result.Axes.Add(new RootCauseAxisStatus { Axis = axis, Reason = "No model could be analyzed." });
            return result;
        }

        // Every analyzed pair: decomposed where its difference is established, its difference alone otherwise.
        // The cost rows written for a pair split by component with unreported reasoning merge output and reasoning;
        // the rules read the components as decomposed.
        foreach (var pair in ctx.Pairs)
        {
            var speed = ctx.SpeedPair(pair);
            if (speed != null) result.SpeedPairs.Add(speed);
            var cost = ctx.CostPair(pair);
            if (cost != null) result.CostPairs.Add(ctx.WrittenCostPair(pair, cost));
            if (QualitySplitOf(ctx, pair) is { } split) result.QualitySplits.Add(split);
        }

        // The research questions' targets; question 1 reads the split of the leader against the runner-up.
        var targets = RootCauseProfiles.Targets(ctx);
        if (targets.FirstOrDefault(t => t.Number == 1) is { } highest)
        {
            var leaderPair = RootCauseContext.Precedes(highest.Model.Token, highest.Neighbor.Token)
                ? new RootCausePair(highest.Model.Token, highest.Neighbor.Token)
                : new RootCausePair(highest.Neighbor.Token, highest.Model.Token);
            bool analyzed = ctx.Pairs.Any(p => (p.First == leaderPair.First && p.Second == leaderPair.Second)
                                               || (p.First == leaderPair.Second && p.Second == leaderPair.First));
            if (!analyzed && QualitySplitOf(ctx, leaderPair) is { } leaderSplit) result.QualitySplits.Add(leaderSplit);
        }

        if (input.FamilyGaps.Count > 0) result.FamilyGaps = input.FamilyGaps.ToList();

        AddControls(ctx, result);

        // Hypotheses.
        var drafts = new List<RootCauseDraft>();
        var nullStands = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var pair in ctx.Pairs)
        {
            var first = ctx.Model(pair.First);
            var second = ctx.Model(pair.Second);
            var axes = new List<string>();
            if (first.QualityIndex.HasValue && second.QualityIndex.HasValue) axes.Add(RootCauseAxes.Intelligence);
            if (!first.SpeedDegraded && !second.SpeedDegraded) axes.Add(RootCauseAxes.Speed);
            if (!first.CostDegraded && !second.CostDegraded) axes.Add(RootCauseAxes.Cost);
            foreach (string axis in axes)
            {
                drafts.Add(RootCauseHypotheses.PairNull(ctx, pair, axis, out bool stands));
                nullStands[pair.Key + "|" + axis] = stands;
            }
        }
        foreach (var pair in ctx.Pairs) drafts.AddRange(RootCauseHypotheses.PairRules(ctx, pair, nullStands));
        if (ctx.Comparison)
        {
            if (RootCauseHypotheses.SharedStyleConflict(ctx) is { } hq5) drafts.Add(hq5);
        }
        else
        {
            foreach (var model in input.Models)
            {
                if (RootCauseHypotheses.StyleConflict(ctx, model) is { } hq5) drafts.Add(hq5);
            }
        }

        var shared = ctx.SharedFailureQuestions();
        if (RootCauseHypotheses.SharedFailures(ctx, shared.Questions, shared.Models) is { } hq3) drafts.Add(hq3);

        foreach (var (model, axes) in ctx.WithinTargets())
        {
            drafts.AddRange(RootCauseHypotheses.WithinRules(ctx, model, axes, ctx.ControlCap(model)));
        }
        if (RootCauseHypotheses.GraderDisagreement(ctx) is { } wq6) drafts.Add(wq6);
        if (RootCauseHypotheses.SharedLeadGroupNote(ctx, drafts) is { } sharedGroupNote) result.Notes.Add(sharedGroupNote);

        var graded = drafts.Select(d => (Hypothesis: RootCauseHypotheses.Grade(d, ctx.Name), Draft: d)).ToList();
        var kept = RootCauseHypotheses.Keep(graded, targets, RootCauseProfiles.Answers, (target, h) => RootCauseProfiles.PartnerRank(ctx, target, h));

        // By axis, nulls first; a price effect, which the price cards show, after every other hypothesis of its axis.
        kept = kept
            .OrderBy(k => RootCauseAxes.All.ToList().IndexOf(k.Hypothesis.Axis))
            .ThenBy(k => k.Hypothesis.Kind == "null" ? 0 : 1)
            .ThenBy(k => k.Hypothesis.PriceEffect ? 1 : 0)
            .ThenByDescending(k => RootCauseGrades.Rank(k.Hypothesis.Grade))
            .ThenBy(k => k.Draft.PairNullStands == true ? 1 : 0)
            .ThenBy(k => k.Hypothesis.Explains.HasValue ? 0 : 1)
            .ThenByDescending(k => k.Hypothesis.Explains ?? 0)
            .ThenBy(k => k.Hypothesis.Id, StringComparer.Ordinal)
            .ThenBy(k => k.Hypothesis.Number)
            .ToList();
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (hypothesis, draft) in kept)
        {
            int n = counters.GetValueOrDefault(hypothesis.Id) + 1;
            counters[hypothesis.Id] = n;
            hypothesis.Number = n;
            hypothesis.Key = hypothesis.Id + "." + n.ToString(CultureInfo.InvariantCulture);
            hypothesis.Label = draft.Label ?? RootCauseLabels.Of(hypothesis, ctx.Name);
        }
        result.Hypotheses = kept.Select(k => k.Hypothesis).ToList();
        result.Axes = RootCauseAxes.All.Select(axis => RootCauseHypotheses.AxisStatus(axis, kept, ctx.Pairs.Count > 0)).ToList();
        RootCauseAnomalies.AddHypotheses(ctx.Anomalies, result.Hypotheses);

        result.SystemFirst = kept.Any(k => k.Hypothesis.Kind != "null"
                                           && k.Hypothesis.Loci.Count > 0
                                           && k.Hypothesis.Loci[0] is RootCauseLoci.Harness or RootCauseLoci.Suite
                                           && RootCauseGrades.Rank(k.Hypothesis.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Moderate));

        var (runs, checks) = RootCauseNextRuns.Build(ctx, kept, result.SystemFirst);
        result.RecommendedRuns = runs;
        result.Checks = checks;

        result.Evidence = input.Models.Select(m => ctx.EvidenceSummary(m)).ToList();
        AddChartRows(ctx, result);
        result.KnownFacts = RootCauseKnownFacts.Build(ctx, result);
        result.Profiles = RootCauseProfiles.Build(ctx);
        result.SharedLowestDimension = RootCauseProfiles.SharedLowestDimension(ctx.Models);
        if (ctx.Comparison)
        {
            result.Anomalies = ctx.Anomalies.ToList();
            result.PriorsChecked = ctx.PriorsChecked.ToList();
        }
        result.Questions = RootCauseProfiles.Questions(ctx, result, targets);
        result.GradeCap = RootCauseProfiles.GradeCap(ctx, result.Hypotheses);

        if (input.PricingBasisKind == BenchmarkReportFacts.PricingBasisCatalog && input.Models.Any(m => m.Evidence.Priced))
        {
            result.Notes.Add("Costs are priced at today's catalog cards, as the comparison prices them.");
        }
        return result;
    }

    /// <summary>The controls, the control limit and the item-level control table of a run- or battery-completion analysis.</summary>
    private static void AddControls(RootCauseContext ctx, RootCauseResult result)
    {
        var set = ctx.Input.Controls;
        if (set == null)
        {
            // A model-scope subject is capped by its peers, as ControlCap grades it.
            if (!ctx.Comparison)
            {
                var (peerCap, peerReason) = ctx.ControlCap(ctx.Input.Models[0]);
                result.ControlLimit = peerCap;
                result.ControlLimitReason = peerReason;
            }
            return;
        }

        result.Controls = set.Controls.ToList();
        var (cap, reason) = ctx.ControlCap(ctx.Input.Models[0]);
        result.ControlLimit = cap;
        result.ControlLimitReason = reason;
        result.ControlItems = ctx.ControlItems.ToList();

        var differences = result.ControlItems.Select(i => i.Difference).ToList();
        if (differences.Count >= RootCauseControls.MinSharedQuestions)
        {
            var (mean, interval) = RootCauseStatistics.MeanWithInterval(differences);
            result.ControlDeficitMean = mean;
            result.ControlDeficitLower = interval?.Lower;
            result.ControlDeficitUpper = interval?.Upper;
        }
        if (set.Controls.Count == 0 && set.NoneReason != null) result.Notes.Add(set.NoneReason);
    }

    /// <summary>The chart rows R1–R3: one per model with peers; the subject, then each difficulty band or suite, without.</summary>
    private static void AddChartRows(RootCauseContext ctx, RootCauseResult result)
    {
        var input = ctx.Input;
        foreach (var model in input.Models)
        {
            string label = model.Label;
            result.PointsAtRisk.Add(AtRiskRow(label, model.Letter, model.Token, "model", ctx.AtRisk(model.Token)));
            result.SpeedRows.Add(SpeedRow(label, model.Letter, model.Token, "model", model.SpeedDegraded ? model.SpeedDegradedReason : null, model.Evidence.SpeedItems));
            result.CostRows.Add(CostRow(label, model.Letter, model.Token, "model", model.CostDegraded ? model.CostDegradedReason : model.Evidence.Priced ? null : RootCauseEvidence.NoPriceReason, model.Evidence.CostItems, model.Evidence.ReasoningReported));
        }

        if (input.Models.Count != 1) return;

        var subject = input.Models[0];
        double? median = ctx.AtRisk(subject.Token).Median;
        bool battery = subject.Evidence.Answers.Any(a => a.Suite.HasValue);
        var groups = battery
            ? subject.Evidence.Answers.Where(a => a.Suite.HasValue).Select(a => a.Suite!.Value).Distinct().OrderBy(s => s)
                .Select(s => (Key: (Func<RootCauseAnswerRecord, bool>)(a => a.Suite == s), Item: (Func<RootCauseItem, bool>)(i => i.Suite == s),
                    Label: "Suite " + s.ToString(CultureInfo.InvariantCulture) + (input.SuiteNames.TryGetValue(s, out var name) ? ": " + name : string.Empty), Kind: "suite"))
                .ToList()
            : new[] { "Simple", "Intermediate", "Advanced" }
                .Where(b => subject.Evidence.Answers.Any(a => a.Band == b))
                .Select(b => (Key: (Func<RootCauseAnswerRecord, bool>)(a => a.Band == b), Item: (Func<RootCauseItem, bool>)(i => i.Band == b),
                    Label: b + " questions", Kind: "band"))
                .ToList();

        foreach (var group in groups)
        {
            var answers = subject.Evidence.Answers.Where(group.Key).ToList();
            result.PointsAtRisk.Add(AtRiskRow(group.Label, null, null, group.Kind, RootCauseDecomposition.AtRisk(answers, median)));
            result.SpeedRows.Add(SpeedRow(group.Label, null, null, group.Kind, subject.SpeedDegraded ? subject.SpeedDegradedReason : null,
                subject.Evidence.SpeedItems.Where(group.Item).ToList()));
            result.CostRows.Add(CostRow(group.Label, null, null, group.Kind,
                subject.CostDegraded ? subject.CostDegradedReason : subject.Evidence.Priced ? null : RootCauseEvidence.NoPriceReason,
                subject.Evidence.CostItems.Where(group.Item).ToList(), subject.Evidence.ReasoningReported));
        }
    }

    /// <summary>The pair's Intelligence Index difference split by the first model's scoring weights; null when a figure is missing.</summary>
    private static RootCauseQualitySplit? QualitySplitOf(RootCauseContext ctx, RootCausePair pair)
    {
        var first = ctx.Model(pair.First);
        var second = ctx.Model(pair.Second);
        return first.QualityIndex is double qa && second.QualityIndex is double qb
            ? RootCauseDecomposition.QualitySplit(pair.First, qa, first.Dimensions, pair.Second, qb, second.Dimensions, first.Evidence.Weights)
            : null;
    }

    private static RootCauseChartRow AtRiskRow(string label, string? letter, string? token, string kind, RootCauseAtRisk at)
    {
        var row = new RootCauseChartRow { Label = label, Letter = letter, Token = token, Kind = kind, Unit = "points" };
        if (at.Answers == 0 || at.Median == null)
        {
            row.UnavailableReason = "no scored answer";
            return row;
        }
        for (int k = 0; k < 4; k++)
        {
            row.Segments.Add(new RootCauseSegment { Key = RootCauseSegments.AtRisk[k], Label = RootCauseSegments.Label(RootCauseSegments.AtRisk[k]), Value = at.Points[k] });
        }
        row.Total = row.Segments.Sum(s => s.Value);
        return row;
    }

    /// <summary>
    /// Seconds per question by segment. The total is the median per-question time (the sum of the segments
    /// per question); the segments split it in proportion to their means, so they sum to it exactly.
    /// </summary>
    private static RootCauseChartRow SpeedRow(string label, string? letter, string? token, string kind, string? unavailable, IReadOnlyList<RootCauseItem> items)
    {
        var row = new RootCauseChartRow { Label = label, Letter = letter, Token = token, Kind = kind, Unit = "seconds" };
        if (unavailable != null)
        {
            row.UnavailableReason = unavailable;
            return row;
        }
        var means = RootCauseDecomposition.TimeSegments(items);
        if (means == null)
        {
            row.UnavailableReason = "no candidate call telemetry";
            return row;
        }
        var totals = items.Select(i => RootCauseDecomposition.TimeSegments(new[] { i })!.Sum()).ToList();
        double median = RootCauseStatistics.Median(totals) ?? 0;
        double meanTotal = means.Sum();
        double scale = meanTotal > 0 ? median / meanTotal : 0;
        for (int k = 0; k < 5; k++)
        {
            string key = RootCauseSegments.Time[k];
            row.Segments.Add(new RootCauseSegment { Key = key, Label = RootCauseSegments.Label(key), Value = means[k] * scale / 1000.0 });
        }
        row.Total = row.Segments.Sum(s => s.Value);
        return row;
    }

    /// <summary>
    /// USD per question by cost component. Without <paramref name="reasoningReported"/>, the reasoning segment is
    /// left out and the output segment, which holds the thinking, is keyed <see cref="RootCauseSegments.OutputWithThinking"/>.
    /// </summary>
    private static RootCauseChartRow CostRow(string label, string? letter, string? token, string kind, string? unavailable, IReadOnlyList<RootCauseItem> items, bool reasoningReported)
    {
        var row = new RootCauseChartRow { Label = label, Letter = letter, Token = token, Kind = kind, Unit = "usd" };
        if (unavailable != null)
        {
            row.UnavailableReason = unavailable;
            return row;
        }
        if (items.Count == 0)
        {
            row.UnavailableReason = "no token counts";
            return row;
        }
        row.Segments.AddRange(CostSegments(k => items.Average(i => i.Cost[k]), reasoningReported));
        row.Total = row.Segments.Sum(s => s.Value);
        return row;
    }

    /// <summary>
    /// One segment per cost component with <paramref name="value"/>'s figure; without <paramref name="reasoningReported"/>,
    /// output and reasoning as one segment keyed <see cref="RootCauseSegments.OutputWithThinking"/>.
    /// </summary>
    internal static IEnumerable<RootCauseSegment> CostSegments(Func<int, double> value, bool reasoningReported)
    {
        for (int k = 0; k < 5; k++)
        {
            string key = RootCauseSegments.CostComponents[k];
            if (!reasoningReported && key == RootCauseSegments.Reasoning) continue;
            double figure = value(k);
            if (!reasoningReported && key == RootCauseSegments.Output)
            {
                key = RootCauseSegments.OutputWithThinking;
                figure += value(k + 1);
            }
            yield return new RootCauseSegment { Key = key, Label = RootCauseSegments.Label(key), Value = figure };
        }
    }
}

/// <summary>
/// The shared state of one computation: the models by token, their points at risk, the analyzed pairs and
/// their decompositions, the controls' item-level table, and the robustness checks the rules call.
/// </summary>
internal sealed class RootCauseContext
{
    private readonly Dictionary<string, RootCauseModelInput> _byToken;
    private readonly Dictionary<string, RootCauseAtRisk> _atRisk = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RootCausePairDecomposition?> _speed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RootCausePairDecomposition?> _cost = new(StringComparer.Ordinal);

    public RootCauseContext(RootCauseInput input)
    {
        Input = input;
        _byToken = input.Models.GroupBy(m => m.Token, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var model in input.Models)
        {
            var scored = model.Evidence.Scored.Select(a => a.Quality!.Value).ToList();
            _atRisk[model.Token] = RootCauseDecomposition.AtRisk(model.Evidence.Answers, RootCauseStatistics.Median(scored));
        }
        // The unexpected results come first: their pairs join the analyzed pairs.
        var unexpected = RootCauseAnomalies.Find(this);
        Anomalies = unexpected.Anomalies;
        PriorsChecked = unexpected.Checked;
        Pairs = BuildPairs();
        ControlItems = BuildControlItems();
        ModelSpecificCount = ControlItems.Count(i => i.ModelSpecific);
        InstrumentSuspectCount = ControlItems.Count(i => i.InstrumentSuspect);
        NeedsControlRun = ComputeNeedsControlRun();
    }

    public RootCauseInput Input { get; }

    public IReadOnlyList<RootCauseModelInput> Models => Input.Models;

    public bool Battery => Input.Battery;

    public bool Comparison => Input.Scope == RootCauseEngine.ComparisonScope;

    public IReadOnlyList<(string Role, string Provider)> Graders => Input.Graders;

    public IReadOnlyList<RootCausePair> Pairs { get; }

    /// <summary>The unexpected results of a comparison (<see cref="RootCauseAnomalies.Find"/>), numbered; empty in model scope.</summary>
    public IReadOnlyList<RootCauseAnomaly> Anomalies { get; }

    /// <summary>The priors at least one pair could be checked against, in <see cref="RootCausePriors.All"/> order.</summary>
    public IReadOnlyList<string> PriorsChecked { get; }

    public IReadOnlyList<RootCauseControlItem> ControlItems { get; }

    public int ModelSpecificCount { get; }

    public int InstrumentSuspectCount { get; }

    /// <summary>Fewer than two qualifying controls (completion), or fewer than two comparable peers (per-model pack).</summary>
    public bool NeedsControlRun { get; }

    public RootCauseModelInput Model(string token) => _byToken[token];

    public RootCauseAtRisk AtRisk(string token) => _atRisk[token];

    /// <summary>The model's release date: its own, else the input's <see cref="RootCauseInput.ReleaseDates"/> entry; null when blank or unknown.</summary>
    public string? ReleaseDate(RootCauseModelInput model)
    {
        string? date = model.ReleaseDate;
        if (string.IsNullOrWhiteSpace(date) && Input.ReleaseDates != null)
        {
            Input.ReleaseDates.TryGetValue(RootCauseEvidence.ReleaseDateKey(model.Provider, model.ModelId), out date);
        }
        return string.IsNullOrWhiteSpace(date) ? null : date.Trim();
    }

    /// <summary>A model as fact displays name it: <c>Model C</c>, or the subject's label.</summary>
    public string Name(string token)
    {
        if (!_byToken.TryGetValue(token, out var model)) return token;
        return model.Letter != null ? "Model " + model.Letter : model.Label;
    }

    /// <summary>A per-model fact key of a model: <c>model.A.&lt;suffix&gt;</c>, the bare key for the subject, empty for a peer.</summary>
    public string Key(string token, string suffix)
    {
        var model = Model(token);
        if (Comparison) return BenchmarkComparisonReportFacts.ModelPrefix(model.Letter ?? token) + suffix;
        return model.IsSubject ? suffix : string.Empty;
    }

    public string WithinKey(string token, string suffix) => "rca.within." + token + "." + suffix;

    /// <summary>The fact key of the pair's intelligence test or paired difference.</summary>
    public string QualityFactKey(RootCausePair pair)
    {
        var test = TestsFor(pair, RootCauseAxes.Intelligence).FirstOrDefault(t => t.FactKey != null);
        if (test != null) return test.FactKey!;
        if (!Comparison)
        {
            var peer = Model(pair.First).IsSubject ? Model(pair.Second) : Model(pair.First);
            if (peer.Letter != null) return BenchmarkReportFacts.PeerPrefix(peer.Letter) + "pairedDifference";
        }
        return string.Empty;
    }

    /// <summary>
    /// The pair's speed decomposition: its components only where the difference is established
    /// (<see cref="DifferenceEstablished"/>), else the difference and its interval, marked not established.
    /// </summary>
    public RootCausePairDecomposition? SpeedPair(RootCausePair pair)
    {
        if (!_speed.TryGetValue(pair.Key, out var value))
        {
            var first = Model(pair.First);
            var second = Model(pair.Second);
            if (first.SpeedDegraded || second.SpeedDegraded)
            {
                value = new RootCausePairDecomposition
                {
                    Axis = RootCauseAxes.Speed, First = pair.First, Second = pair.Second,
                    UnavailableReason = first.SpeedDegraded ? first.SpeedDegradedReason ?? "The speed axis is degraded." : second.SpeedDegradedReason ?? "The speed axis is degraded."
                };
            }
            else
            {
                value = RootCauseDecomposition.Speed(pair.First, first.Evidence, pair.Second, second.Evidence, decompose: false);
                if (value.NotEstablished && DifferenceEstablished(pair, RootCauseAxes.Speed, value))
                {
                    value = RootCauseDecomposition.Speed(pair.First, first.Evidence, pair.Second, second.Evidence);
                }
            }
            _speed[pair.Key] = value;
        }
        return value;
    }

    /// <summary>The pair's cost decomposition, decomposed only where the difference is established, as <see cref="SpeedPair"/>.</summary>
    public RootCausePairDecomposition? CostPair(RootCausePair pair)
    {
        if (!_cost.TryGetValue(pair.Key, out var value))
        {
            var first = Model(pair.First);
            var second = Model(pair.Second);
            if (first.CostDegraded || second.CostDegraded)
            {
                value = new RootCausePairDecomposition
                {
                    Axis = RootCauseAxes.Cost, First = pair.First, Second = pair.Second,
                    UnavailableReason = first.CostDegraded ? first.CostDegradedReason ?? "The cost axis is degraded." : second.CostDegradedReason ?? "The cost axis is degraded."
                };
            }
            else
            {
                value = RootCauseDecomposition.Cost(pair.First, first.Evidence, pair.Second, second.Evidence, decompose: false);
                if (value.NotEstablished && DifferenceEstablished(pair, RootCauseAxes.Cost, value))
                {
                    value = RootCauseDecomposition.Cost(pair.First, first.Evidence, pair.Second, second.Evidence);
                }
            }
            _cost[pair.Key] = value;
        }
        return value;
    }

    /// <summary>
    /// Whether the pair's difference on a speed or cost axis is established, as its null hypothesis is
    /// rejected: a family test established after its adjustment; without a family test that was made, the
    /// 95% bootstrap interval of the per-question difference excluding zero.
    /// </summary>
    public bool DifferenceEstablished(RootCausePair pair, string axis, RootCausePairDecomposition difference)
    {
        var tests = TestsFor(pair, axis);
        if (tests.Any(t => t.Established && t.Favors != "None")) return true;
        if (tests.Any(t => t.NotTestedReason == null)) return false;
        return difference.DifferenceLower is double lo && difference.DifferenceUpper is double hi && (lo > 0 || hi < 0);
    }

    /// <summary>The family tests of a pair on an axis, the reference family first.</summary>
    public IReadOnlyList<RootCausePairTest> TestsFor(RootCausePair pair, string axis)
    {
        string measure = axis switch
        {
            RootCauseAxes.Speed => BenchmarkPairedTests.SpeedMeasure,
            RootCauseAxes.Cost => BenchmarkPairedTests.CostMeasure,
            _ => BenchmarkPairedTests.IntelligenceMeasure
        };
        return Input.PairTests
            .Where(t => t.Measure == measure
                        && ((t.First == pair.First && t.Second == pair.Second) || (t.First == pair.Second && t.Second == pair.First)))
            .OrderBy(t => t.Family == BenchmarkComparisonReportFacts.ReferenceFamilyName ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// The cost decomposition as the result writes it: when a pair split by component has a side that does not report
    /// reasoning separately, its output and reasoning components become one component keyed
    /// <see cref="RootCauseSegments.OutputWithThinking"/>, whose volume and price effects are the sums of the two, so the
    /// effects still sum exactly to the difference; otherwise, a grouped pair included, <paramref name="cost"/> itself.
    /// </summary>
    public RootCausePairDecomposition WrittenCostPair(RootCausePair pair, RootCausePairDecomposition cost)
    {
        var first = Model(pair.First);
        var second = Model(pair.Second);
        if ((first.Evidence.ReasoningReported && second.Evidence.ReasoningReported) || cost.Components.Count == 0 || cost.Grouped) return cost;

        var (a, b) = RootCauseDecomposition.Shared(first.Evidence.CostItems, second.Evidence.CostItems);
        int output = RootCauseSegments.CostComponents.ToList().IndexOf(RootCauseSegments.Output);
        var intervals = RootCauseStatistics.Bootstrap(a.Count, 2, sample =>
        {
            var (e, t, _, _) = RootCauseDecomposition.CostEffects(a, b, sample);
            return t == 0 ? null : new double?[] { (e[2 * output] + e[2 * output + 2]) / t, (e[2 * output + 1] + e[2 * output + 3]) / t };
        });

        var written = new RootCausePairDecomposition
        {
            Axis = cost.Axis,
            First = cost.First,
            Second = cost.Second,
            SharedQuestions = cost.SharedQuestions,
            Difference = cost.Difference,
            DifferenceLower = cost.DifferenceLower,
            DifferenceUpper = cost.DifferenceUpper,
            FirstTotal = cost.FirstTotal,
            SecondTotal = cost.SecondTotal,
            UnavailableReason = cost.UnavailableReason,
            NotEstablished = cost.NotEstablished,
            Grouped = cost.Grouped,
            VolumeRatio = cost.VolumeRatio
        };
        foreach (var component in cost.Components)
        {
            if (component.Key == RootCauseSegments.Reasoning) continue;
            if (component.Key != RootCauseSegments.Output)
            {
                written.Components.Add(component);
                continue;
            }

            var reasoning = cost.Components.First(c => c.Key == RootCauseSegments.Reasoning && c.Effect == component.Effect);
            double amount = component.Amount + reasoning.Amount;
            var interval = intervals[component.Effect == "price" ? 1 : 0];
            written.Components.Add(new RootCauseComponent
            {
                Key = RootCauseSegments.OutputWithThinking,
                Effect = component.Effect,
                Label = RootCauseSegments.Label(RootCauseSegments.OutputWithThinking) + (component.Effect == "price" ? ", price per token" : ", token volume"),
                Amount = amount,
                Share = cost.Difference is double total && total != 0 ? amount / total : null,
                ShareLower = interval?.Lower,
                ShareUpper = interval?.Upper
            });
        }
        return written;
    }

    /// <summary>
    /// Whether the 95% paired-bootstrap interval of the pair's Intelligence Index difference excludes zero: a
    /// per-model sheet's interval against the peer where the sheet has one, else the interval of the mean
    /// per-question difference over the questions both models scored (at least
    /// <see cref="RootCauseDecomposition.MinSharedQuestions"/>). Null when neither can be computed.
    /// </summary>
    public bool? PairedBootstrapExcludesZero(RootCausePair pair)
    {
        if (SheetPairedDifference(pair) is { } interval) return BenchmarkReportFacts.PairedIntervalExcludesZero(interval.Lower, interval.Upper);

        var first = ItemMeans(Model(pair.First));
        var second = ItemMeans(Model(pair.Second));
        var differences = first.Keys
            .Where(second.ContainsKey)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => first[k] - second[k])
            .ToList();
        if (differences.Count < RootCauseDecomposition.MinSharedQuestions) return null;
        var (_, bootstrap) = RootCauseStatistics.MeanWithInterval(differences);
        return bootstrap is { } ci ? BenchmarkReportFacts.PairedIntervalExcludesZero(ci.Lower, ci.Upper) : null;
    }

    /// <summary>A per-model sheet's paired-bootstrap interval of the subject against the pair's peer; null without one.</summary>
    public (double Lower, double Upper)? SheetPairedDifference(RootCausePair pair)
    {
        string peer = Model(pair.First).IsSubject ? pair.Second : pair.First;
        return Input.PairedDifferences.TryGetValue(peer, out var interval) ? interval : null;
    }

    public bool? IntervalsOverlap(RootCausePair pair)
    {
        var a = Model(pair.First);
        var b = Model(pair.Second);
        if (a.QualityLower is not double al || a.QualityUpper is not double au || b.QualityLower is not double bl || b.QualityUpper is not double bu) return null;
        return al <= bu && bl <= au;
    }

    public bool JudgeDependent(RootCausePair pair)
        => Input.JudgeDependentPairs.Any(p => (p.First == pair.First && p.Second == pair.Second) || (p.First == pair.Second && p.Second == pair.First));

    // ---------------------------------------------------------------------------------------------
    // Pairs and targets
    // ---------------------------------------------------------------------------------------------

    private List<RootCausePair> BuildPairs()
    {
        var models = Input.Models;
        var pairs = new List<RootCausePair>();
        if (models.Count < 2) return pairs;

        if (!Comparison)
        {
            var subject = models[0];
            foreach (var peer in models.Skip(1).Take(RootCauseEngine.MaxPeers)) pairs.Add(new RootCausePair(subject.Token, peer.Token));
            return pairs;
        }

        void Add(string a, string b)
        {
            if (a == b) return;
            var pair = Precedes(a, b) ? new RootCausePair(a, b) : new RootCausePair(b, a);
            if (!pairs.Contains(pair)) pairs.Add(pair);
        }

        string reference = Input.ReferenceToken ?? models[0].Token;
        foreach (var model in models) Add(reference, model.Token);
        foreach (var (first, last) in AxisExtremes()) Add(first.Token, last.Token);

        // The same model at adjacent thinking levels, so that HQ8 can compare them.
        foreach (var group in models
                     .Where(m => !string.IsNullOrWhiteSpace(m.ModelId) && RootCauseHypotheses.ThinkingRank(m.ThinkingLevel) >= 0)
                     .GroupBy(m => m.ModelId.Trim().ToLowerInvariant(), StringComparer.Ordinal))
        {
            var levels = group
                .OrderBy(m => RootCauseHypotheses.ThinkingRank(m.ThinkingLevel))
                .ThenBy(m => m.Token.Length)
                .ThenBy(m => m.Token, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i + 1 < levels.Count; i++)
            {
                if (RootCauseHypotheses.SameModelAtTwoLevels(levels[i], levels[i + 1])) Add(levels[i].Token, levels[i + 1].Token);
            }
        }

        // The pairs of the unexpected results, in their order, up to the cap.
        int added = 0;
        foreach (var anomaly in Anomalies)
        {
            if (added >= RootCauseEngine.MaxAnomalyPairs) break;
            int before = pairs.Count;
            Add(anomaly.Expected, anomaly.Other);
            if (pairs.Count > before) added++;
        }
        return pairs;
    }

    /// <summary>The leader and the last model on each axis, among the models comparable on it.</summary>
    private IEnumerable<(RootCauseModelInput First, RootCauseModelInput Last)> AxisExtremes()
    {
        var quality = Input.Models.Where(m => m.QualityIndex.HasValue).OrderByDescending(m => m.QualityIndex!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        if (quality.Count >= 2) yield return (quality[0], quality[^1]);
        var speed = Input.Models.Where(m => !m.SpeedDegraded && m.ModelTimeP50Ms.HasValue).OrderBy(m => m.ModelTimeP50Ms!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        if (speed.Count >= 2) yield return (speed[0], speed[^1]);
        var cost = Input.Models.Where(m => !m.CostDegraded && m.CostPerQuestionUsd.HasValue).OrderBy(m => m.CostPerQuestionUsd!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        if (cost.Count >= 2) yield return (cost[0], cost[^1]);
    }

    /// <summary>
    /// The models the within-model rules apply to, with the axes: the subject on every axis in model scope;
    /// in comparison scope the reference on every axis and the first and last model of each axis on it.
    /// </summary>
    public IEnumerable<(RootCauseModelInput Model, IReadOnlyCollection<string> Axes)> WithinTargets()
    {
        if (!Comparison)
        {
            yield return (Input.Models[0], RootCauseAxes.All.ToList());
            yield break;
        }

        var axesByToken = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string token, string axis)
        {
            if (!axesByToken.TryGetValue(token, out var set)) axesByToken[token] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(axis);
        }

        string reference = Input.ReferenceToken ?? Input.Models[0].Token;
        foreach (string axis in RootCauseAxes.All) Add(reference, axis);
        var extremes = AxisExtremes().ToList();
        string[] order = { RootCauseAxes.Intelligence, RootCauseAxes.Speed, RootCauseAxes.Cost };
        int index = 0;
        foreach (string axis in order)
        {
            bool present = axis switch
            {
                RootCauseAxes.Intelligence => Input.Models.Count(m => m.QualityIndex.HasValue) >= 2,
                RootCauseAxes.Speed => Input.Models.Count(m => !m.SpeedDegraded && m.ModelTimeP50Ms.HasValue) >= 2,
                _ => Input.Models.Count(m => !m.CostDegraded && m.CostPerQuestionUsd.HasValue) >= 2
            };
            if (!present) continue;
            var (first, last) = extremes[index++];
            Add(first.Token, axis);
            Add(last.Token, axis);
        }

        foreach (var model in Input.Models.Where(m => axesByToken.ContainsKey(m.Token)))
        {
            var axes = RootCauseAxes.All.Where(axesByToken[model.Token].Contains).ToList();
            yield return (model, axes);
        }
    }

    /// <summary>
    /// The control limit on a model's model-versus-instrument intelligence hypotheses: from the controls in a
    /// run or battery completion, from the peers in a per-model pack, from the other covered models in a comparison.
    /// </summary>
    public (string? Cap, string? Reason) ControlCap(RootCauseModelInput model)
    {
        if (Input.Controls != null)
        {
            var controls = Input.Controls.Controls;
            return RootCauseControls.Limit(controls.Count, controls.Count(c => !RootCauseHypotheses.SameProvider(c.Provider, model.Provider)));
        }
        var others = Input.Models.Where(m => m.Token != model.Token).ToList();
        return RootCauseControls.Limit(others.Count, others.Count(m => !RootCauseHypotheses.SameProvider(m.Provider, model.Provider)));
    }

    private bool ComputeNeedsControlRun()
    {
        if (Comparison || Input.Models.Count == 0) return false;
        if (Input.Controls != null || Input.Origin is RootCauseOrigins.RunCompletion or RootCauseOrigins.BatteryCompletion)
        {
            var (cap, _) = ControlCap(Input.Models[0]);
            return cap != null;
        }
        return Input.Models.Count - 1 < 2;
    }

    // ---------------------------------------------------------------------------------------------
    // Controls and shared failures
    // ---------------------------------------------------------------------------------------------

    private List<RootCauseControlItem> BuildControlItems()
    {
        var set = Input.Controls;
        if (set == null || set.Controls.Count == 0 || Input.Models.Count == 0) return new List<RootCauseControlItem>();

        var subjectScores = ItemMeans(Input.Models[0]);
        var refs = Input.Questions.GroupBy(q => q.ItemKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var items = new List<RootCauseControlItem>();
        foreach (var (key, score) in subjectScores)
        {
            var controlScores = set.Scores.Select(s => s.ScoresByItem.TryGetValue(key, out double v) ? (double?)v : null).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (controlScores.Count == 0) continue;
            double mean = controlScores.Average();
            var reference = refs.GetValueOrDefault(key);
            items.Add(new RootCauseControlItem
            {
                Number = reference?.Number ?? 0,
                Reference = reference?.Reference ?? key,
                SubjectScore = score,
                ControlsMean = mean,
                Difference = score - mean,
                ControlCount = controlScores.Count,
                InstrumentSuspect = score < RootCauseHypotheses.MissBelow && controlScores.All(c => c < RootCauseHypotheses.MissBelow),
                ModelSpecific = score < RootCauseHypotheses.MissBelow && controlScores.All(c => c >= RootCauseHypotheses.WellAtLeast)
            });
        }
        return items.OrderBy(i => i.Number == 0 ? int.MaxValue : i.Number).ThenBy(i => i.Reference, StringComparer.Ordinal).ToList();
    }

    /// <summary>A model's mean published quality per item.</summary>
    public static Dictionary<string, double> ItemMeans(RootCauseModelInput model)
        => model.Evidence.Scored
            .GroupBy(a => a.ItemKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Average(a => a.Quality!.Value), StringComparer.Ordinal);

    /// <summary>
    /// The questions HQ3 reads: per item, how many models scored it, how many below 50, and whether one
    /// scored 75 or above; over the analyzed models, or over the subject and its controls.
    /// </summary>
    public (IReadOnlyList<(int Number, string Reference, int Low, int Scored, bool WellAnswered)> Questions, IReadOnlyList<string> Models) SharedFailureQuestions()
    {
        var refs = Input.Questions.GroupBy(q => q.ItemKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rows = new List<(int Number, string Reference, int Low, int Scored, bool WellAnswered)>();

        if (Input.Controls != null)
        {
            foreach (var item in ControlItems.Where(i => i.InstrumentSuspect))
            {
                rows.Add((item.Number, item.Reference, item.ControlCount + 1, item.ControlCount + 1, false));
            }
            return (rows, new[] { Input.Models[0].Token });
        }

        if (Input.Models.Count < 2) return (rows, Array.Empty<string>());
        var means = Input.Models.Select(ItemMeans).ToList();
        foreach (string key in means.SelectMany(m => m.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => refs.TryGetValue(k, out var r) ? r.Number : int.MaxValue).ThenBy(k => k, StringComparer.Ordinal))
        {
            var scores = means.Where(m => m.ContainsKey(key)).Select(m => m[key]).ToList();
            var reference = refs.GetValueOrDefault(key);
            rows.Add((reference?.Number ?? 0, reference?.Reference ?? key,
                scores.Count(s => s < RootCauseHypotheses.MissBelow), scores.Count, scores.Any(s => s >= RootCauseHypotheses.WellAtLeast)));
        }
        return (rows, Input.Models.Select(m => m.Token).ToList());
    }

    // ---------------------------------------------------------------------------------------------
    // Figures the rules read
    // ---------------------------------------------------------------------------------------------

    public double? ReasoningShare(RootCauseModelInput model)
    {
        var rows = model.Evidence.Answers.Where(a => a.ReasoningTokens.HasValue && a.OutputTokens is > 0).ToList();
        double output = rows.Sum(a => (double)a.OutputTokens!.Value);
        return rows.Count == 0 || output <= 0 ? null : rows.Sum(a => (double)a.ReasoningTokens!.Value) / output;
    }

    public double? RetriesPerAnswer(RootCauseModelInput model)
    {
        var rows = model.Evidence.Answers.Where(a => a.Ok && a.RetryAttempts.HasValue).ToList();
        return rows.Count == 0 ? null : rows.Average(a => (double)a.RetryAttempts!.Value);
    }

    public double? CacheReadShare(RootCauseModelInput model)
    {
        var rows = model.Evidence.Answers.Where(a => a.InputTokens is > 0).ToList();
        return rows.Count == 0 ? null : rows.Sum(a => (double)(a.CacheReadTokens ?? 0)) / rows.Sum(a => (double)a.InputTokens!.Value);
    }

    public double? CallsPerAnswer(RootCauseModelInput model)
    {
        var rows = model.Evidence.Answers.Where(a => a.Ok && a.ModelCalls.HasValue).ToList();
        return rows.Count == 0 ? null : rows.Average(a => (double)a.ModelCalls!.Value);
    }

    /// <summary>The worse model's mean own waits per question minus the other's, over the shared timed questions.</summary>
    public double? WaitDifference(RootCausePair pair, string worseToken)
    {
        var (a, b) = RootCauseDecomposition.Shared(Model(pair.First).Evidence.SpeedItems, Model(pair.Second).Evidence.SpeedItems);
        if (a.Count == 0) return null;
        double difference = a.Average(i => i.WaitMs) - b.Average(i => i.WaitMs);
        return worseToken == pair.First ? difference : -difference;
    }

    // ---------------------------------------------------------------------------------------------
    // Robustness checks
    // ---------------------------------------------------------------------------------------------

    public RootCauseRobustnessCheck ReplicatesCheck(params RootCauseModelInput[] models)
    {
        var single = models.Where(m => m.Evidence.RunsPerSuite < 2).ToList();
        string who = single.Count == 2 && models.Length == 2 ? "Both models" : BenchmarkReportFormat.LetterList(single.Select(m => Name(m.Token)).ToList());
        return single.Count == 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.Replicates, Status = RootCauseCheckStatuses.Passed, Detail = "Every model ran at least twice per suite." }
            : new RootCauseRobustnessCheck
            {
                Name = RootCauseHypotheses.Replicates,
                Status = RootCauseCheckStatuses.Failed,
                Detail = who + " ran only once per suite."
            };
    }

    /// <summary>Both panel members order the pair as the published index does.</summary>
    public RootCauseRobustnessCheck PanelAgreementCheck(RootCauseModelInput a, RootCauseModelInput b)
    {
        double? MemberMean(RootCauseModelInput m, bool memberB)
        {
            var values = m.Evidence.Scored.Select(x => memberB ? x.MemberBQuality : x.MemberAQuality).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count > 0 ? values.Average() : null;
        }

        double? aA = a.MemberAIndex ?? MemberMean(a, false), aB = a.MemberBIndex ?? MemberMean(a, true);
        double? bA = b.MemberAIndex ?? MemberMean(b, false), bB = b.MemberBIndex ?? MemberMean(b, true);
        if (!a.Evidence.Panel || !b.Evidence.Panel || aA is not double x1 || aB is not double x2 || bA is not double y1 || bB is not double y2)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.NotAssessable, Detail = "The two models were not both graded by a panel." };
        }
        int published = Math.Sign((a.QualityIndex ?? 0) - (b.QualityIndex ?? 0));
        bool alike = Math.Sign(x1 - y1) == published && Math.Sign(x2 - y2) == published;
        return alike
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Passed, Detail = "Both panel members order the two models alike." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Failed, Detail = "The panel members order the two models differently." };
    }

    /// <summary>Both panel members show the same within-model contrast (the group against the rest).</summary>
    public RootCauseRobustnessCheck WithinPanelCheck(RootCauseModelInput model, Func<RootCauseAnswerRecord, bool> inGroup)
    {
        var scored = model.Evidence.Scored.ToList();
        double? Contrast(Func<RootCauseAnswerRecord, double?> score)
        {
            var group = scored.Where(inGroup).Select(score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var rest = scored.Where(a => !inGroup(a)).Select(score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return group.Count > 0 && rest.Count > 0 ? group.Average() - rest.Average() : null;
        }

        if (!model.Evidence.Panel)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.NotAssessable, Detail = "The model was not graded by a panel." };
        }
        if (Contrast(a => a.MemberAQuality) is not double ca || Contrast(a => a.MemberBQuality) is not double cb)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.NotAssessable, Detail = "A group has no score from both panel members." };
        }
        return Math.Sign(ca) == Math.Sign(cb) && ca != 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Passed, Detail = "Both panel members see the difference in the same direction." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Failed, Detail = "The two panel members do not see the difference in the same direction." };
    }

    /// <summary>The within-model contrast has the same sign in every suite with at least five scored answers.</summary>
    public RootCauseRobustnessCheck WithinAcrossSuites(RootCauseModelInput model, Func<RootCauseAnswerRecord, bool> inGroup)
    {
        var signs = new List<int>();
        foreach (var suite in model.Evidence.Scored.Where(a => a.Suite.HasValue).GroupBy(a => a.Suite!.Value).OrderBy(g => g.Key))
        {
            var answers = suite.ToList();
            if (answers.Count < RootCauseDecomposition.MinSharedQuestions) continue;
            var group = answers.Where(inGroup).Select(a => a.Quality!.Value).ToList();
            var rest = answers.Where(a => !inGroup(a)).Select(a => a.Quality!.Value).ToList();
            if (group.Count == 0 || rest.Count == 0) continue;
            signs.Add(Math.Sign(group.Average() - rest.Average()));
        }
        if (signs.Count == 0) return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.NotAssessable, Detail = "No suite has five scored answers in both groups." };
        return signs.Distinct().Count() == 1 && signs[0] != 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Passed, Detail = "The contrast has the same sign in every suite with five scored answers." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Failed, Detail = "The contrast changes sign between suites." };
    }

    /// <summary>The battery's Overall Index moves by more than the interval half-width with the group's suite left out.</summary>
    public RootCauseRobustnessCheck LeaveOneSuiteOutCheck(string group)
    {
        var subject = Input.Models[0];
        if (!group.StartsWith('S') || !int.TryParse(group.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int suite)
            || !Input.LeaveOneSuiteOutChanges.TryGetValue(suite, out double change)
            || subject.QualityLower is not double lo || subject.QualityUpper is not double hi)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.NotAssessable, Detail = "The leave-one-suite-out index is not recorded." };
        }
        double half = (hi - lo) / 2;
        return Math.Abs(change) > half
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Passed, Detail = "Leaving the suite out moves the Overall Index by " + BenchmarkReportFormat.OneDecimal(Math.Abs(change)) + " points, more than the interval half-width." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Failed, Detail = "Leaving the suite out moves the Overall Index by less than the interval half-width." };
    }

    /// <summary>The deciding speed component keeps its sign and stays the largest share without the question that lowers it most.</summary>
    public RootCauseRobustnessCheck LeaveOneOutSpeed(RootCausePair pair, string component)
    {
        var (a, b) = RootCauseDecomposition.Shared(Model(pair.First).Evidence.SpeedItems, Model(pair.Second).Evidence.SpeedItems);
        int index = RootCauseDecomposition.SpeedComponents.ToList().IndexOf(component);
        return LeaveOneOut(a.Count, index, drop =>
        {
            var sample = Enumerable.Range(0, a.Count).Where(i => i != drop).ToList();
            var (effects, total, _, _) = RootCauseDecomposition.SpeedEffects(a, b, sample);
            return total == 0 ? null : effects.Select(e => e / total).ToArray();
        });
    }

    /// <summary>The deciding speed component's effect has the same sign in every suite with at least five shared questions.</summary>
    public RootCauseRobustnessCheck AcrossSuitesSpeed(RootCausePair pair, string component)
    {
        var (a, b) = RootCauseDecomposition.Shared(Model(pair.First).Evidence.SpeedItems, Model(pair.Second).Evidence.SpeedItems);
        int index = RootCauseDecomposition.SpeedComponents.ToList().IndexOf(component);
        var (overall, _, _, _) = RootCauseDecomposition.SpeedEffects(a, b);
        return AcrossSuites(a, overall[index], sample => RootCauseDecomposition.SpeedEffects(a, b, sample).Effects[index]);
    }

    /// <summary>
    /// The deciding cost share keeps its sign and stays the largest without the question that lowers it most: for
    /// <c>all</c> and for a grouped pair's caching effect, among the all-components effects; for a component, among
    /// the components (volume and price together) of the pair's split.
    /// </summary>
    public RootCauseRobustnessCheck LeaveOneOutCost(RootCausePair pair, string component, string? effect)
    {
        var first = Model(pair.First).Evidence;
        var second = Model(pair.Second).Evidence;
        var (a, b) = RootCauseDecomposition.Shared(first.CostItems, second.CostItems);
        var split = RootCauseDecomposition.CostSplitOf(first, second);
        if (component == RootCauseCostKeys.All || effect == RootCauseCostKeys.Caching)
        {
            int which = Math.Max(0, split.Effects.ToList().IndexOf(effect ?? RootCauseCostKeys.Volume));
            return LeaveOneOut(a.Count, which, drop =>
            {
                var sample = Enumerable.Range(0, a.Count).Where(i => i != drop).ToList();
                var (effects, total, _, _) = RootCauseDecomposition.CostEffects(a, b, split, sample);
                if (total == 0) return null;
                return split.Effects.Select(e => split.Sum(effects, null, e) / total).ToArray();
            });
        }

        int k = split.Components.ToList().IndexOf(component);
        return LeaveOneOut(a.Count, k, drop =>
        {
            var sample = Enumerable.Range(0, a.Count).Where(i => i != drop).ToList();
            var (effects, total, _, _) = RootCauseDecomposition.CostEffects(a, b, split, sample);
            return total == 0 ? null : split.Components.Select(c => split.Sum(effects, c, null) / total).ToArray();
        });
    }

    /// <summary>
    /// The deciding cost effect has the same sign in every suite with at least five shared questions: an all-components
    /// effect, a grouped pair's caching effect, or a component's volume and price together.
    /// </summary>
    public RootCauseRobustnessCheck AcrossSuitesCost(RootCausePair pair, string component, string? effect)
    {
        var first = Model(pair.First).Evidence;
        var second = Model(pair.Second).Evidence;
        var (a, b) = RootCauseDecomposition.Shared(first.CostItems, second.CostItems);
        var split = RootCauseDecomposition.CostSplitOf(first, second);
        string which = effect is RootCauseCostKeys.Price or RootCauseCostKeys.Caching ? effect : RootCauseCostKeys.Volume;
        double Effect(IReadOnlyList<int>? sample)
        {
            var (effects, _, _, _) = RootCauseDecomposition.CostEffects(a, b, split, sample);
            if (component == RootCauseCostKeys.All) return split.Sum(effects, null, which);
            if (effect == RootCauseCostKeys.Caching) return split.Sum(effects, component, RootCauseCostKeys.Caching);
            return split.Sum(effects, component, null);
        }
        return AcrossSuites(a, Effect(null), sample => Effect(sample));
    }

    /// <summary>One model's model-time component stays the largest without the question that lowers its share most.</summary>
    public RootCauseRobustnessCheck WithinLeaveOneOutTime(RootCauseModelInput model, int component)
    {
        var items = model.Evidence.SpeedItems;
        return LeaveOneOut(items.Count, component, drop =>
        {
            var sample = Enumerable.Range(0, items.Count).Where(i => i != drop).ToList();
            double total = sample.Average(i => items[i].ModelTimeMs);
            if (total <= 0) return null;
            double ttft = sample.Average(i => items[i].TtftMs), decode = sample.Average(i => items[i].DecodeMs);
            return new[] { ttft / total, decode / total, (total - ttft - decode) / total };
        });
    }

    /// <summary>Spearman's ρ stays at or above 0.5 without the question whose removal lowers it most.</summary>
    public RootCauseRobustnessCheck WithinLeaveOneOutRho(IReadOnlyList<RootCauseItem> items, Func<RootCauseItem, double> value)
    {
        if (items.Count <= RootCauseStatistics.MinSpearmanCount)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.NotAssessable, Detail = "Too few questions to drop one." };
        }
        double lowest = double.MaxValue;
        for (int drop = 0; drop < items.Count; drop++)
        {
            var rest = items.Where((_, i) => i != drop).ToList();
            var rho = RootCauseStatistics.Spearman(rest.Select(value).ToList(), rest.Select(i => i.Calls).ToList());
            lowest = Math.Min(lowest, rho?.Rho ?? double.MinValue);
        }
        return lowest >= 0.5
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "ρ stays at or above 0.5 without any one question." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "Without one question ρ falls below 0.5." };
    }

    /// <summary>
    /// The deciding share (position <paramref name="index"/> of what <paramref name="shares"/> returns without
    /// one question) keeps its sign and stays the largest for the question whose removal lowers it most.
    /// </summary>
    private static RootCauseRobustnessCheck LeaveOneOut(int count, int index, Func<int, double[]?> shares)
    {
        if (count <= RootCauseDecomposition.MinSharedQuestions || index < 0)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.NotAssessable, Detail = "Too few shared questions to drop one." };
        }

        double[]? worst = null;
        for (int drop = 0; drop < count; drop++)
        {
            var values = shares(drop);
            if (values == null || index >= values.Length) continue;
            if (worst == null || values[index] < worst[index]) worst = values;
        }
        if (worst == null)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.NotAssessable, Detail = "The share cannot be recomputed without a question." };
        }

        bool keepsSign = worst[index] > 0;
        bool largest = worst[index] >= worst.Max();
        return keepsSign && largest
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The deciding share keeps its sign and stays the largest without the question that contributes most." }
            : new RootCauseRobustnessCheck
            {
                Name = RootCauseHypotheses.LeaveOneOut,
                Status = RootCauseCheckStatuses.Failed,
                Detail = keepsSign ? "Without one question another component is larger." : "Without one question the deciding share changes sign."
            };
    }

    private static RootCauseRobustnessCheck AcrossSuites(IReadOnlyList<RootCauseItem> items, double overall, Func<IReadOnlyList<int>, double> effect)
    {
        var signs = new List<int>();
        foreach (var suite in items.Select((item, i) => (item.Suite, i)).Where(x => x.Suite.HasValue).GroupBy(x => x.Suite!.Value).OrderBy(g => g.Key))
        {
            var indices = suite.Select(x => x.i).ToList();
            if (indices.Count < RootCauseDecomposition.MinSharedQuestions) continue;
            signs.Add(Math.Sign(effect(indices)));
        }
        if (signs.Count == 0) return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.NotAssessable, Detail = "No suite has five shared questions." };
        return signs.All(s => s == Math.Sign(overall)) && overall != 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Passed, Detail = "The effect has the same sign in every suite with five shared questions." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.AcrossSuites, Status = RootCauseCheckStatuses.Failed, Detail = "The effect changes sign between suites." };
    }

    // ---------------------------------------------------------------------------------------------
    // Confounds
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The configuration keys that differ between two models and could carry the effect on an axis: the
    /// harness; the thinking level, reasoning mode and output cap of one model at two settings; on speed and
    /// cost the service tier; on speed the parallel execution mode; and the degrading keys either differs on.
    /// </summary>
    public List<string> Confounds(RootCauseModelInput a, RootCauseModelInput b, string axis, bool skipThinking)
    {
        var list = new List<string>();
        bool sameModel = string.Equals(a.ModelId?.Trim(), b.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase);

        void Differs(string name, string? x, string? y)
        {
            if (!string.Equals((x ?? string.Empty).Trim(), (y ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                list.Add(name + " (" + (string.IsNullOrWhiteSpace(x) ? "not set" : x.Trim()) + " vs " + (string.IsNullOrWhiteSpace(y) ? "not set" : y.Trim()) + ")");
            }
        }

        Differs("harness", a.HarnessVersion, b.HarnessVersion);
        if (sameModel && !skipThinking) Differs("thinking level", a.ThinkingLevel, b.ThinkingLevel);
        if (sameModel && axis == RootCauseAxes.Intelligence)
        {
            Differs("reasoning mode", a.ReasoningMode, b.ReasoningMode);
            Differs("output token cap", a.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture), b.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture));
        }
        if (axis != RootCauseAxes.Intelligence) Differs("service tier", a.ServiceTier, b.ServiceTier);
        if (axis == RootCauseAxes.Speed) Differs("parallel execution", a.ParallelMode, b.ParallelMode);
        if (axis != RootCauseAxes.Intelligence)
        {
            foreach (string key in a.DifferenceKeys.Concat(b.DifferenceKeys)
                         .Where(BenchmarkCrossModelComparability.IsDegradingKey)
                         .Where(k => a.DifferenceKeys.Contains(k) != b.DifferenceKeys.Contains(k))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(k => k, StringComparer.Ordinal))
            {
                list.Add(key);
            }
        }
        return list;
    }

    /// <summary>
    /// Every configuration key other than the model that differs between two models, as <c>thinking level (medium vs high)</c>.
    /// The provider is named as a key only, never by value, so the list names no model.
    /// </summary>
    public static List<string> ConfigDifferences(RootCauseModelInput a, RootCauseModelInput b)
        => ConfigDifferenceParts(a, b)
            .Select(d => d.First == null ? d.Name : d.Name + " (" + d.First + " vs " + d.Second + ")")
            .ToList();

    /// <summary>
    /// Every configuration key other than the model that differs between two models: its name and, for a valued key, both
    /// values (<c>not set</c> when blank); the provider and a key one model's configuration lacks carry no value.
    /// </summary>
    public static List<RootCauseConfigDifference> ConfigDifferenceParts(RootCauseModelInput a, RootCauseModelInput b)
    {
        var list = new List<RootCauseConfigDifference>();
        if (!SameSetting(a.Provider, b.Provider)) list.Add(new RootCauseConfigDifference("provider", null, null));
        foreach (var (name, read) in ValuedConfigKeys)
        {
            string? x = read(a), y = read(b);
            if (!SameSetting(x, y)) list.Add(new RootCauseConfigDifference(name, SettingText(x), SettingText(y)));
        }
        foreach (string key in a.DifferenceKeys.Concat(b.DifferenceKeys)
                     .Where(k => a.DifferenceKeys.Contains(k) != b.DifferenceKeys.Contains(k))
                     .Where(k => !BenchmarkCrossModelComparability.IsModelAxisKey(k))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(k => k, StringComparer.Ordinal))
        {
            list.Add(new RootCauseConfigDifference(key, null, null));
        }
        return list;
    }

    /// <summary>The valued configuration keys a pair or a comparison is compared on, in display order.</summary>
    public static readonly IReadOnlyList<(string Name, Func<RootCauseModelInput, string?> Read)> ValuedConfigKeys = new (string, Func<RootCauseModelInput, string?>)[]
    {
        ("thinking level", m => m.ThinkingLevel),
        ("reasoning mode", m => m.ReasoningMode),
        ("service tier", m => m.ServiceTier),
        ("output token cap", m => m.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture)),
        ("parallel execution", m => m.ParallelMode),
        ("harness", m => m.HarnessVersion),
    };

    /// <summary>Two settings are the same when they match ignoring case and surrounding space.</summary>
    public static bool SameSetting(string? x, string? y)
        => string.Equals((x ?? string.Empty).Trim(), (y ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A setting as the configuration differences print it: <c>not set</c> when blank.</summary>
    public static string SettingText(string? value) => string.IsNullOrWhiteSpace(value) ? "not set" : value.Trim();

    // ---------------------------------------------------------------------------------------------
    // Evidence summaries
    // ---------------------------------------------------------------------------------------------

    public RootCauseModelEvidenceSummary EvidenceSummary(RootCauseModelInput model)
    {
        var evidence = model.Evidence;
        var at = AtRisk(model.Token);
        var scored = evidence.Scored.ToList();
        var bursts = RootCauseEvidence.BurstAnswers(evidence);
        var summary = new RootCauseModelEvidenceSummary
        {
            Token = model.Token,
            Label = model.Label,
            ScoredAnswers = scored.Count,
            NoToolAnswers = scored.Count(a => a.NoTool),
            BudgetOrDefectAnswers = scored.Count(a => a.BudgetOrDefect),
            CriticalAnswers = scored.Count(a => a.CriticalError),
            MedianQuality = at.Median,
            AtRisk = Enumerable.Range(0, 4).Select(k => new RootCauseSegment { Key = RootCauseSegments.AtRisk[k], Label = RootCauseSegments.Label(RootCauseSegments.AtRisk[k]), Value = at.Points[k] }).ToList(),
            TelemetryAnswers = evidence.Answers.Count(a => a.Ok && a.Telemetry),
            BurstAnswers = bursts.Burst,
            TimedAnswers = bursts.Timed,
            CallsPerAnswer = CallsPerAnswer(model),
            TtftPerCallP50Ms = RootCauseStatistics.Median(evidence.Answers.Where(a => a.Ok).SelectMany(a => a.CallTtftMs).ToList()),
            DecodeTokensPerSecond = RootCauseStatistics.Median(evidence.Answers.Where(a => a.Ok && a.DecodeRate.HasValue).Select(a => a.DecodeRate!.Value).ToList()),
            ReasoningShare = ReasoningShare(model),
            CacheReadShare = CacheReadShare(model)
        };

        var time = RootCauseDecomposition.TimeSegments(evidence.SpeedItems);
        if (model.SpeedDegraded) summary.TimeUnavailableReason = model.SpeedDegradedReason ?? "The speed axis is degraded.";
        else if (time == null) summary.TimeUnavailableReason = RootCauseEvidence.NoTelemetryReason;
        else
        {
            summary.Time = Enumerable.Range(0, 5).Select(k => new RootCauseSegment { Key = RootCauseSegments.Time[k], Label = RootCauseSegments.Label(RootCauseSegments.Time[k]), Value = time[k] / 1000.0 }).ToList();
            summary.ModelTimeMs = evidence.SpeedItems.Average(i => i.ModelTimeMs);
            summary.OutputTokens = evidence.SpeedItems.Average(i => i.OutputTokens);
            var reasoning = evidence.SpeedItems.Where(i => i.ReasoningTokens.HasValue).Select(i => i.ReasoningTokens!.Value).ToList();
            summary.ReasoningTokens = reasoning.Count == evidence.SpeedItems.Count && reasoning.Count > 0 ? reasoning.Average() : null;
        }

        if (model.CostDegraded) summary.CostUnavailableReason = model.CostDegradedReason ?? "The cost axis is degraded.";
        else if (!evidence.Priced) summary.CostUnavailableReason = RootCauseEvidence.NoPriceReason;
        else if (evidence.CostItems.Count == 0) summary.CostUnavailableReason = RootCauseEvidence.NoTokensReason;
        else
        {
            summary.Cost = RootCauseEngine.CostSegments(k => evidence.CostItems.Average(i => i.Cost[k]), evidence.ReasoningReported).ToList();
            summary.Tokens = RootCauseEngine.CostSegments(k => evidence.CostItems.Average(i => i.Tokens[k]), evidence.ReasoningReported).ToList();
        }
        return summary;
    }

    /// <summary>Letter order before ordinal: <c>Z</c> before <c>AA</c>.</summary>
    public static bool Precedes(string a, string b) => a.Length != b.Length ? a.Length < b.Length : string.CompareOrdinal(a, b) < 0;
}

/// <summary>
/// The <c>rca.*</c> facts: a flattened copy of a <see cref="RootCauseResult"/> for the writer and the
/// validator, and the predicate that keeps every fact the Root Cause Analysis adds off the Executive
/// Summary's prompt.
///
/// Fact keys (T is a model token: a letter, or <c>subject</c>):
///   rca.axis.&lt;axis&gt;.status, rca.axis.&lt;axis&gt;.best                                      axis = intelligence, speed, cost
///   rca.known.&lt;n&gt;
///   rca.hypothesis.&lt;id&gt;.&lt;n&gt;.statement, .label, .ref, .axis, .models, .locus, .grade, .gradeReasons, .for, .against,
///     .refutedIf, .nextRun or .check, .share, .shareOf, .interval, .priceCard (a price effect only)
///   rca.rule.&lt;id&gt;.shareSpan, .count: a rule with two or more hypotheses that carry a share, as a group
///   rca.graders.familyGap.count; rca.graders.familyGap.&lt;n&gt;.providers, .underA, .underB, .contrast, .notable
///   rca.question.&lt;n&gt;.text, .model, .neighbor (questions 1 and 2), .status, .hypotheses, .signals
///     n = 1 highest score, 2 lowest, 3 slowest, 4 most expensive
///   rca.profile.&lt;T&gt;.gainedPointsOn, .lostPointsOn, .weakestDimension, .lowestRank, .strength.&lt;n&gt; (the dimension ranks),
///     .flag.&lt;n&gt;, .flags
///   rca.anomaly.count, .priorsChecked, .untestedPairs (comparison scope);
///     rca.anomaly.&lt;n&gt;.ref, .summary, .prior, .confounds, .graders, .settledBy, .tested
///   rca.speed.&lt;T&gt;.&lt;T&gt;.total, first minus second; .decomposed (false: a difference not established is stated, not
///     decomposed); rca.speed.&lt;T&gt;.&lt;T&gt;.&lt;component&gt;.share, .interval
///   rca.cost.&lt;T&gt;.&lt;T&gt;.total, .decomposed, .grouped and .volumeRatio (a decomposed pair);
///     rca.cost.&lt;T&gt;.&lt;T&gt;.&lt;component&gt;.&lt;volume|price&gt;.share (component all: every component); a grouped pair's
///     components are input and output, and input and all also carry .caching.share when both list prices are known
///   rca.quality.&lt;T&gt;.&lt;T&gt;.total; rca.quality.&lt;T&gt;.&lt;T&gt;.dimension.&lt;d&gt;.points, .other.points
///   rca.atRisk.&lt;T&gt;.&lt;segment&gt;, rca.atRisk.&lt;T&gt;.total
///   rca.within.&lt;T&gt;.time.&lt;segment&gt;.seconds, .time.&lt;segment&gt;.share (the ttft segment summed over a question's model
///     calls); rca.within.&lt;T&gt;.cost.&lt;component&gt;.share; rca.within.&lt;T&gt;.speed.burstAnswers, of the timed answers
///   rca.controls.count, .providers, .limit; rca.controls.&lt;n&gt;.model, .runId or .batteryRunId, .date, .provider, .sharedQuestions
///   rca.control.deficit.mean, .interval; rca.control.instrumentSuspect, rca.control.modelSpecific
///   rca.run.&lt;n&gt;.summary, .purpose, rca.check.&lt;n&gt;, rca.check.&lt;n&gt;.summary, rca.systemFirst, rca.pricingBasis
/// </summary>
public static class RootCauseFacts
{
    public const string Prefix = "rca.";

    private static readonly Regex ModelKeyPrefix = new(@"^model\.[A-Z]+\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> EvidenceKeys = new(RootCauseEvidence.FactKeys, StringComparer.Ordinal);

    /// <summary>The display of a pair's <c>.decomposed</c> fact, stated when the difference itself may be chance.</summary>
    public const string NotDecomposedDisplay = "not split into parts: the difference may be chance";

    /// <summary>The display of a price effect's <c>.priceCard</c> fact.</summary>
    public const string PriceCardDisplay = "known from the price cards, not a cause the runs found";

    /// <summary>The display of a cost pair's <c>.grouped</c> fact when its components are the input and output groups.</summary>
    public const string GroupedDisplay = "split into input and output, because the two models are not billed for the same token kinds";

    /// <summary>The display of a cost pair's <c>.grouped</c> fact when it is split by component.</summary>
    public const string NotGroupedDisplay = "split by token kind";

    /// <summary>A cost pair's <c>.volumeRatio</c> display: one decimal below 10, else a whole number, then <c>times</c>.</summary>
    public static string RatioDisplay(double ratio)
        => (Math.Round(ratio, 1, MidpointRounding.AwayFromZero) < 10 ? BenchmarkReportFormat.OneDecimal(ratio) : BenchmarkReportFormat.Whole(ratio)) + " times";

    /// <summary>The display of question 4's status when its best comparative hypothesis is a price effect.</summary>
    public const string PriceCardStatusDisplay = RootCauseGrades.PriceCardWord;

    /// <summary>
    /// Whether a fact stays off the Executive Summary's fact list: every <c>rca.*</c> fact, and every
    /// evidence fact only the Root Cause Analysis reads (the per-model facts of § A3, a per-model sheet's
    /// <c>profile.*</c>, and the <c>pair.&lt;L&gt;.&lt;M&gt;.dimension.*</c>, <c>.configDifferences</c> and
    /// <c>.&lt;axis&gt;.short</c> facts), whether bare or under a model's <c>model.&lt;L&gt;.</c> prefix. The facts
    /// stay on the sheet.
    /// </summary>
    public static bool WriterHidden(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        if (key.StartsWith("profile.", StringComparison.Ordinal)) return true;
        if (key.StartsWith("pair.", StringComparison.Ordinal)
            && (key.Contains(".dimension.", StringComparison.Ordinal)
                || key.EndsWith(".configDifferences", StringComparison.Ordinal)
                || key.EndsWith(".short", StringComparison.Ordinal)))
        {
            return true;
        }
        string bare = ModelKeyPrefix.Replace(key, string.Empty);
        return EvidenceKeys.Contains(bare) || bare.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which facts of <paramref name="result"/> stay off the Root Cause Analysis writer's own fact list: every
    /// <c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.*</c> fact of a price effect (<see cref="RootCauseHypothesis.PriceEffect"/>), which
    /// the price cards show rather than the runs, and the <c>rca.rule.&lt;id&gt;.*</c> facts of a rule whose hypotheses are
    /// all price effects. The facts stay on the sheet for the rendered blocks; a null result hides nothing.
    /// </summary>
    public static Func<string?, bool> RootCauseWriterHidden(RootCauseResult? result)
    {
        if (result?.Hypotheses == null || !result.Hypotheses.Any(h => h.PriceEffect)) return _ => false;
        var prefixes = result.Hypotheses
            .Where(h => h.PriceEffect)
            .Select(h => "rca.hypothesis." + h.Id + "." + Inv(h.Number) + ".")
            .Concat(result.Hypotheses
                .GroupBy(h => h.Id, StringComparer.Ordinal)
                .Where(rule => rule.All(h => h.PriceEffect))
                .Select(rule => "rca.rule." + rule.Key + "."))
            .ToList();
        return key => key != null && prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="key"/> stays off the Root Cause Analysis writer's fact list (<see cref="RootCauseWriterHidden(RootCauseResult?)"/>).</summary>
    public static bool RootCauseWriterHidden(RootCauseResult? result, string? key) => RootCauseWriterHidden(result)(key);

    /// <summary>
    /// What a hypothesis' share is a share of, as a noun phrase: <c>the gap</c> for a pair hypothesis; for a within-model
    /// one, by axis, <c>its model time</c>, <c>its cost</c> or <c>its points at risk</c>, except WQ4's rate of answers
    /// (<c>its wrong or not-attempted answers</c>) and WS4's share of the wall time (<c>its wall time</c>).
    /// </summary>
    public static string ShareTotal(string kind, string axis, string id)
    {
        if (kind != "within") return "the gap";
        return id switch
        {
            "WQ4" => "its wrong or not-attempted answers",
            "WS4" => "its wall time",
            _ => axis switch
            {
                RootCauseAxes.Speed => "its model time",
                RootCauseAxes.Cost => "its cost",
                _ => "its points at risk"
            }
        };
    }

    /// <summary>The display of a hypothesis' <c>.shareOf</c> fact, which follows its <c>.share</c>: <c>of</c> and <see cref="ShareTotal"/>.</summary>
    public static string ShareOf(string kind, string axis, string id) => "of " + ShareTotal(kind, axis, id);

    /// <summary>The <c>rca.*</c> facts of <paramref name="result"/>; <paramref name="name"/> names a model by token.</summary>
    internal static void Add(BenchmarkReportFacts.FactList facts, RootCauseResult result, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var axis in result.Axes)
        {
            string prefix = "rca.axis." + axis.Axis + ".";
            facts.Add(prefix + "status", axis.Status, RootCauseStatuses.Display(axis.Status));
            if (axis.BestHypothesis != null) facts.Add(prefix + "best", axis.BestHypothesis, axis.BestHypothesis);
            else facts.Unavailable(prefix + "best", axis.Reason);
        }

        foreach (var known in result.KnownFacts) facts.Text("rca.known." + Inv(known.Number), known.Text);

        foreach (var h in result.Hypotheses)
        {
            string prefix = "rca.hypothesis." + h.Id + "." + Inv(h.Number) + ".";
            facts.Text(prefix + "statement", h.Statement);
            if (!string.IsNullOrWhiteSpace(h.Label)) facts.Text(prefix + "label", h.Label);
            facts.Text(prefix + "ref", h.Key);
            facts.Text(prefix + "axis", RootCauseAxes.Name(h.Axis));
            facts.Text(prefix + "models", h.ModelsText);
            facts.Text(prefix + "locus", h.Locus);
            facts.Add(prefix + "grade", h.Grade, h.Kind == "null" ? RootCauseGrades.NullWordWithGrade(h.Grade)
                : h.PriceEffect ? RootCauseGrades.PriceCardWordWithGrade(h.Grade)
                : RootCauseGrades.Display(h.Grade));
            if (h.PriceEffect) facts.Add(prefix + "priceCard", true, PriceCardDisplay);
            facts.Text(prefix + "gradeReasons", h.GradeReasons.Count == 0 ? "every condition of the grade is met" : string.Join("; ", h.GradeReasons));
            facts.Text(prefix + "for", h.For.Count == 0 ? "none recorded" : string.Join(" ", h.For));
            facts.Text(prefix + "against", h.Against.Count == 0 ? "none recorded" : string.Join(" ", h.Against));
            facts.Text(prefix + "refutedIf", h.RefutedIf);
            if (h.NextRunNumber is int run) facts.Add(prefix + "nextRun", run, "next run " + Inv(run));
            else if (h.CheckNumber is int check) facts.Add(prefix + "check", check, "check " + Inv(check));
            if (h.Share is double share)
            {
                facts.Add(prefix + "share", share, RootCauseHypotheses.Pct(share));
                facts.Text(prefix + "shareOf", ShareOf(h.Kind, h.Axis, h.Id));
                if (h.ShareLower is double lo && h.ShareUpper is double hi) facts.Text(prefix + "interval", RootCauseHypotheses.Interval(lo, hi));
            }
        }

        // A rule with two or more hypotheses that carry a share is described as a group by the span of its shares.
        foreach (var rule in result.Hypotheses.Where(h => h.Kind != "null" && h.Share.HasValue).GroupBy(h => h.Id, StringComparer.Ordinal))
        {
            var members = rule.ToList();
            if (members.Count < 2) continue;
            string prefix = "rca.rule." + rule.Key + ".";
            double low = members.Min(h => h.Share!.Value), high = members.Max(h => h.Share!.Value);
            string lowText = RootCauseHypotheses.Pct(low), highText = RootCauseHypotheses.Pct(high);
            bool pairs = members.All(h => h.Kind == "pair"), within = members.All(h => h.Kind == "within");
            // A pair's share is of the gap between two models; a within-model share is of the model's own total.
            string of = pairs ? " of each gap"
                : within ? members[0].Axis switch
                {
                    RootCauseAxes.Speed => " of each model's own time",
                    RootCauseAxes.Cost => " of each model's own cost",
                    _ => " of each model's own points at risk"
                }
                : " of each gap or model total";
            facts.Add(prefix + "shareSpan", high - low, (lowText == highText ? lowText : lowText + " to " + highText) + of);
            string noun = pairs ? "pairs" : within ? "models" : "hypotheses";
            facts.Add(prefix + "count", members.Count, Inv(members.Count) + " " + noun);
        }

        // A pair total is the first model minus the second, the amount alone; a difference that may be chance is not
        // decomposed, which its own fact says.
        foreach (var pair in result.SpeedPairs)
        {
            string prefix = "rca.speed." + pair.First + "." + pair.Second + ".";
            if ((pair.UnavailableReason != null && !pair.NotEstablished) || pair.Difference is not double total)
            {
                facts.Unavailable(prefix + "total", pair.UnavailableReason ?? "Not computed.");
                continue;
            }
            facts.Add(prefix + "total", total / 1000.0, BenchmarkReportFormat.SignedOneDecimal(total / 1000.0) + " s per question");
            if (pair.NotEstablished) facts.Add(prefix + "decomposed", false, NotDecomposedDisplay);
            foreach (var c in pair.Components) AddShare(facts, prefix + c.Key, c);
        }

        foreach (var pair in result.CostPairs)
        {
            string prefix = "rca.cost." + pair.First + "." + pair.Second + ".";
            if ((pair.UnavailableReason != null && !pair.NotEstablished) || pair.Difference is not double total)
            {
                facts.Unavailable(prefix + "total", pair.UnavailableReason ?? "Not computed.");
                continue;
            }
            facts.Add(prefix + "total", total, Signed(BenchmarkReportFormat.Usd(Math.Abs(total)), total) + " per question");
            if (pair.NotEstablished) facts.Add(prefix + "decomposed", false, NotDecomposedDisplay);
            if (pair.Components.Count > 0) facts.Add(prefix + "grouped", pair.Grouped, pair.Grouped ? GroupedDisplay : NotGroupedDisplay);
            if (pair.VolumeRatio is double ratio) facts.Add(prefix + "volumeRatio", ratio, RatioDisplay(ratio));
            foreach (var c in pair.Components) AddShare(facts, prefix + c.Key + "." + c.Effect, c);
        }

        foreach (var question in result.Questions)
        {
            string prefix = "rca.question." + Inv(question.Number) + ".";
            facts.Text(prefix + "text", question.Text);
            facts.Add(prefix + "model", question.Token, name(question.Token));
            if (question.Neighbor != null) facts.Add(prefix + "neighbor", question.Neighbor, name(question.Neighbor));
            facts.Add(prefix + "status", question.Grade ?? RootCauseGrades.Word(null),
                question.PriceCard ? PriceCardStatusDisplay : RootCauseGrades.WordWithGrade(question.Grade));
            facts.Add(prefix + "hypotheses", question.HypothesisKeys.Count,
                question.HypothesisKeys.Count == 0 ? "none fired" : BenchmarkReportFormat.LetterList(question.HypothesisKeys));
            facts.Text(prefix + "signals", question.Signals.Count == 0 ? "none recorded" : string.Join("; ", question.Signals));
            if (question.ComparativeKeys != null)
            {
                facts.Add(prefix + "withinOnly", question.WithinOnly,
                    question.WithinOnly ? "only the model's own breakdown answers it" : "not answered by the model's own breakdown alone");
            }
            if (question.OrderEstablished is bool order)
            {
                facts.Add(prefix + "orderEstablished", order, order ? "established" : "not established");
            }
        }

        foreach (var profile in result.Profiles)
        {
            string prefix = "rca.profile." + profile.Token + ".";
            var gained = profile.Strengths.FirstOrDefault(s => s.Kind == RootCauseProfiles.GainedPointsOn);
            if (gained != null) facts.Text(prefix + "gainedPointsOn", gained.Text);
            var lost = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.LostPointsOn);
            if (lost != null) facts.Text(prefix + "lostPointsOn", lost.Text);
            else facts.Unavailable(prefix + "lostPointsOn", "No scored answer fell below the model's median answer.");
            var weakest = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.WeakestDimension);
            if (weakest != null) facts.Text(prefix + "weakestDimension", weakest.Text);
            else facts.Unavailable(prefix + "weakestDimension", "No dimension is below the model's Accuracy.");
            var lowest = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.LowestRank);
            if (lowest != null) facts.Text(prefix + "lowestRank", lowest.Text);
            var ranks = profile.Strengths.Where(s => s.Kind != RootCauseProfiles.GainedPointsOn).ToList();
            for (int i = 0; i < ranks.Count; i++) facts.Text(prefix + "strength." + Inv(i + 1), ranks[i].Text);
            for (int i = 0; i < profile.Flags.Count; i++) facts.Text(prefix + "flag." + Inv(i + 1), profile.Flags[i].Text);
            facts.Add(prefix + "flags", profile.Flags.Count, profile.Flags.Count == 0 ? "none" : string.Join("; ", profile.Flags.Select(f => f.Text)));
        }

        foreach (var split in result.QualitySplits)
        {
            string prefix = "rca.quality." + split.First + "." + split.Second + ".";
            facts.Add(prefix + "total", split.IndexDifference, BenchmarkReportFormat.SignedOneDecimal(split.IndexDifference) + " index points");
            foreach (var c in split.Components)
            {
                string key = c.Key == "other" ? prefix + "other.points" : prefix + "dimension." + c.Key + ".points";
                facts.Add(key, c.Amount, BenchmarkReportFormat.SignedOneDecimal(c.Amount) + " points");
            }
        }

        foreach (var summary in result.Evidence)
        {
            string prefix = "rca.atRisk." + summary.Token + ".";
            if (summary.ScoredAnswers == 0 || summary.MedianQuality == null)
            {
                facts.Unavailable(prefix + "total", "The model has no scored answer.");
            }
            else
            {
                foreach (var s in summary.AtRisk) facts.Add(prefix + s.Key, s.Value, BenchmarkReportFormat.OneDecimal(s.Value) + " index points");
                double total = summary.AtRisk.Sum(s => s.Value);
                facts.Add(prefix + "total", total, BenchmarkReportFormat.OneDecimal(total) + " index points below a median answer of "
                    + BenchmarkReportFormat.Whole(summary.MedianQuality.Value));
            }

            string within = "rca.within." + summary.Token + ".";
            if (summary.TimeUnavailableReason != null)
            {
                facts.Unavailable(within + "time.total", summary.TimeUnavailableReason);
            }
            else
            {
                double total = summary.Time.Sum(s => s.Value);
                facts.Add(within + "time.total", total, "a mean of " + BenchmarkReportFormat.OneDecimal(total) + " s per question");
                foreach (var s in summary.Time)
                {
                    facts.Add(within + "time." + s.Key + ".seconds", s.Value, "a mean of " + BenchmarkReportFormat.OneDecimal(s.Value) + " s per question"
                        + (s.Key == RootCauseSegments.Ttft ? ", summed over its model calls" : string.Empty));
                    if (total > 0) facts.Add(within + "time." + s.Key + ".share", s.Value / total, RootCauseHypotheses.Pct(s.Value / total));
                }
            }

            if (summary.TimedAnswers is int timed && summary.BurstAnswers is int burst)
            {
                if (timed > 0)
                {
                    facts.Add(within + "speed.burstAnswers", burst, BenchmarkReportFormat.CountOf(burst, timed, "answers", "whose visible text arrived in one burst"));
                }
                else
                {
                    facts.Unavailable(within + "speed.burstAnswers", "No answer recorded whether its visible text arrived in one burst.");
                }
            }

            if (summary.CostUnavailableReason != null)
            {
                facts.Unavailable(within + "cost.total", summary.CostUnavailableReason);
            }
            else
            {
                double total = summary.Cost.Sum(s => s.Value);
                facts.Add(within + "cost.total", total, "a mean of " + BenchmarkReportFormat.Usd(total) + " per question");
                foreach (var s in summary.Cost)
                {
                    if (total > 0) facts.Add(within + "cost." + s.Key + ".share", s.Value / total, RootCauseHypotheses.Pct(s.Value / total));
                }
            }
        }

        AddFamilyGaps(facts, result.FamilyGaps);
        AddAnomalies(facts, result, name);

        if (result.Origin is RootCauseOrigins.RunCompletion or RootCauseOrigins.BatteryCompletion)
        {
            facts.Add("rca.controls.count", result.Controls.Count, Inv(result.Controls.Count) + (result.Controls.Count == 1 ? " control" : " controls"));
            var providers = result.Controls.Select(c => c.Provider.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            facts.Add("rca.controls.providers", providers.Count, providers.Count == 0 ? "none" : string.Join(", ", providers));
            facts.Text("rca.controls.limit", result.ControlLimit == null
                ? "no cap: two or more controls, at least one from another provider"
                : "capped at " + RootCauseGrades.Display(result.ControlLimit) + ": " + result.ControlLimitReason);
            foreach (var control in result.Controls)
            {
                string prefix = "rca.controls." + Inv(control.Number) + ".";
                facts.Text(prefix + "model", control.LabelWithLevel());
                if (control.RunId is long runId) facts.Add(prefix + "runId", (JsonNode)JsonValue.Create(runId), "run #" + runId.ToString(CultureInfo.InvariantCulture));
                if (control.BatteryRunId is long batteryRunId) facts.Add(prefix + "batteryRunId", (JsonNode)JsonValue.Create(batteryRunId), "battery run #" + batteryRunId.ToString(CultureInfo.InvariantCulture));
                facts.Text(prefix + "date", control.DateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                facts.Text(prefix + "provider", control.Provider);
                facts.Add(prefix + "sharedQuestions", control.SharedQuestions, Inv(control.SharedQuestions) + " questions");
            }

            if (result.ControlDeficitMean is double mean)
            {
                facts.Add("rca.control.deficit.mean", mean, BenchmarkReportFormat.SignedOneDecimal(mean) + " points against the controls' mean on "
                    + Inv(result.ControlItems.Count) + " shared questions");
                if (result.ControlDeficitLower is double lo && result.ControlDeficitUpper is double hi)
                {
                    facts.Text("rca.control.deficit.interval", BenchmarkReportFormat.SignedOneDecimal(lo) + " to " + BenchmarkReportFormat.SignedOneDecimal(hi));
                }
                else
                {
                    facts.Unavailable("rca.control.deficit.interval", "No interval could be computed.");
                }
            }
            else
            {
                string reason = result.Controls.Count == 0 ? "No other model answered these questions." : "Fewer than five questions are shared with the controls.";
                facts.Unavailable("rca.control.deficit.mean", reason);
                facts.Unavailable("rca.control.deficit.interval", reason);
            }

            int suspect = result.ControlItems.Count(i => i.InstrumentSuspect);
            int specific = result.ControlItems.Count(i => i.ModelSpecific);
            if (result.Controls.Count == 0)
            {
                facts.Unavailable("rca.control.instrumentSuspect", "No other model answered these questions.");
                facts.Unavailable("rca.control.modelSpecific", "No other model answered these questions.");
            }
            else
            {
                facts.Add("rca.control.instrumentSuspect", suspect, Inv(suspect) + (suspect == 1 ? " question" : " questions") + " the subject and every control scored below 50");
                facts.Add("rca.control.modelSpecific", specific, Inv(specific) + (specific == 1 ? " question" : " questions") + " the subject scored below 50 while every control scored 75 or above");
            }
        }

        // A model of a run is named as the facts name it: an analyzed model by its token's name; another
        // configured model by its label in model scope, and never in comparison scope.
        var analyzed = result.Models
            .GroupBy(m => m.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => name(g.First().Token), StringComparer.Ordinal);
        bool comparison = result.Scope == RootCauseEngine.ComparisonScope;
        string RunModelName(string label)
            => analyzed.TryGetValue(label, out var known) ? known : comparison ? "another configured model" : label;
        foreach (var run in result.RecommendedRuns)
        {
            facts.Text("rca.run." + Inv(run.Number) + ".summary", RootCauseNextRuns.Summary(run, RunModelName, result.Models.Count));
            facts.Text("rca.run." + Inv(run.Number) + ".purpose", RootCausePurposes.Text(run.Purpose));
        }
        foreach (var check in result.Checks)
        {
            facts.Text("rca.check." + Inv(check.Number), check.Text);
            facts.Text("rca.check." + Inv(check.Number) + ".summary", RootCauseNextRuns.Summary(check));
        }

        facts.Add("rca.systemFirst", result.SystemFirst, result.SystemFirst
            ? "a harness or suite hypothesis is graded Moderate or higher, so model-selection runs wait until it is settled"
            : "no harness or suite hypothesis holds model-selection runs back");
        facts.Text("rca.pricingBasis", string.IsNullOrWhiteSpace(result.PricingBasis) ? "not recorded" : result.PricingBasis);
    }

    /// <summary>Why a grader-family estimate is missing when the diagnostics had too few paired questions.</summary>
    public const string FamilyGapInsufficientReason = "Fewer than 5 paired questions.";

    /// <summary>
    /// The grader-family gaps, numbered from 1 in the diagnostics' order: <c>rca.graders.familyGap.count</c>, and per
    /// gap its providers, its gap under each panel member alone, the interaction contrast of the members' own
    /// provider pair, and whether that contrast's 95% interval excludes zero. Nothing for a result without them.
    /// </summary>
    private static void AddFamilyGaps(BenchmarkReportFacts.FactList facts, IReadOnlyList<RootCauseFamilyGap>? gaps)
    {
        if (gaps == null || gaps.Count == 0) return;
        facts.Add("rca.graders.familyGap.count", gaps.Count, Inv(gaps.Count) + (gaps.Count == 1 ? " provider pair" : " provider pairs"));
        for (int i = 0; i < gaps.Count; i++)
        {
            var gap = gaps[i];
            string prefix = "rca.graders.familyGap." + Inv(i + 1) + ".";
            facts.Text(prefix + "providers", gap.Provider1 + " − " + gap.Provider2);
            Estimate(prefix + "underA", gap.GapA, gap.InsufficientData);
            Estimate(prefix + "underB", gap.GapB, gap.InsufficientData);
            if (gap.Contrast is { } contrast) Estimate(prefix + "contrast", contrast, gap.InsufficientData);
            bool notable = !gap.InsufficientData && gap.Contrast?.Value.HasValue == true && gap.Contrast.ExcludesZero();
            facts.Add(prefix + "notable", notable, notable ? "the contrast's 95% interval excludes zero" : "no contrast whose 95% interval excludes zero");
        }

        void Estimate(string key, RootCauseGapEstimate estimate, bool insufficient)
        {
            if (insufficient) facts.Unavailable(key, FamilyGapInsufficientReason);
            else if (estimate.Value is double value) facts.Add(key, value, GapText(estimate));
            else facts.Unavailable(key, "Not computed.");
        }
    }

    /// <summary>
    /// The unexpected results of a comparison-scope result: their count, the priors checked, the pairs without a paired
    /// test, and per result its reference, its summary sentence, its prior, its configuration differences, its graders,
    /// what settles it and whether the pair had a paired test. Nothing where <see cref="RootCauseResult.Anomalies"/> is null.
    /// </summary>
    private static void AddAnomalies(BenchmarkReportFacts.FactList facts, RootCauseResult result, Func<string, string> name)
    {
        if (result.Anomalies is not { } anomalies) return;
        facts.Add("rca.anomaly.count", anomalies.Count, anomalies.Count == 0 ? "none"
            : Inv(anomalies.Count) + (anomalies.Count == 1 ? " unexpected result" : " unexpected results"));
        if (result.PriorsChecked is { Count: > 0 } priors) facts.Text("rca.anomaly.priorsChecked", RootCauseAnomalies.PriorsCheckedText(priors));
        else facts.Unavailable("rca.anomaly.priorsChecked", RootCauseAnomalies.NoPriorReason(result.Models));
        int untested = RootCauseAnomalies.UntestedPairs(anomalies);
        facts.Add("rca.anomaly.untestedPairs", untested, Inv(untested) + (untested == 1 ? " pair" : " pairs"));

        foreach (var anomaly in anomalies)
        {
            string prefix = "rca.anomaly." + Inv(anomaly.Number) + ".";
            facts.Text(prefix + "ref", anomaly.Ref);
            facts.Text(prefix + "summary", RootCauseAnomalies.Summary(anomaly, name));
            facts.Add(prefix + "prior", anomaly.Prior, RootCausePriors.Name(anomaly.Prior) + ": " + RootCausePriors.Prediction(anomaly.Prior));
            facts.Text(prefix + "confounds", RootCauseAnomalies.ConfoundsText(anomaly));
            if (RootCauseAnomalies.GradersText(anomaly, name) is { } graders) facts.Text(prefix + "graders", graders);
            else facts.Unavailable(prefix + "graders", "No panel member or reference reader scored both models alone.");
            if (anomaly.SettledByRun is int run) facts.Add(prefix + "settledBy", run, "next run " + Inv(run));
            else if (anomaly.SettledByCheck is int check) facts.Add(prefix + "settledBy", check, "check " + Inv(check));
            else facts.Unavailable(prefix + "settledBy", "No run or check settles it.");
            facts.Add(prefix + "tested", anomaly.Tested, anomaly.Tested ? "the pair had a paired test" : "the pair had no paired test");
        }
    }

    /// <summary><c>+3.9 points (95% interval +1.2 to +6.6)</c>: the estimate with its interval where it has one.</summary>
    internal static string GapText(RootCauseGapEstimate estimate)
        => BenchmarkReportFormat.SignedOneDecimal(estimate.Value ?? 0) + " points"
           + (estimate.Lower is double lo && estimate.Upper is double hi
               ? " (95% interval " + BenchmarkReportFormat.SignedOneDecimal(lo) + " to " + BenchmarkReportFormat.SignedOneDecimal(hi) + ")"
               : string.Empty);

    private static void AddShare(BenchmarkReportFacts.FactList facts, string prefix, RootCauseComponent component)
    {
        if (component.Share is not double share)
        {
            facts.Unavailable(prefix + ".share", "The difference is zero.");
            return;
        }
        facts.Add(prefix + ".share", share, RootCauseHypotheses.Pct(share));
        if (component.ShareLower is double lo && component.ShareUpper is double hi) facts.Text(prefix + ".interval", RootCauseHypotheses.Interval(lo, hi));
        else facts.Unavailable(prefix + ".interval", "No interval could be computed.");
    }

    private static string Signed(string text, double value) => value > 0 ? "+" + text : value < 0 ? "-" + text : text;

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The hypothesis labels: a noun phrase that reads inside a sentence, naming the models as fact displays
/// name them. A pair hypothesis reads <c>Model F's critical errors, compared with Model E</c>; a within-model
/// one <c>Model F's critical errors</c>. <see cref="MaxWords"/> caps the topic only, so a model name is never cut.
/// </summary>
public static class RootCauseLabels
{
    /// <summary>The most words of a label's topic; the model names around it are never cut.</summary>
    public const int MaxWords = 10;

    private static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HQ1"] = "answers without tools",
        ["HQ2"] = "critical errors",
        ["HQ4"] = "order that depends on the graders",
        ["HQ5"] = "Completeness under the concise response style",
        ["HQ6"] = "guessing instead of abstaining",
        ["HQ7"] = "budget, truncation or output defects",
        ["HQ8"] = "lower thinking level",
        ["HQ9"] = "accuracy once the claim verifier's rulings are applied",
        ["HS1"] = "more model calls",
        ["HS2"] = "longer latency per call",
        ["HS3"] = "more output tokens",
        ["HS4"] = "slower decoding",
        ["HS5"] = "own waits and retries",
        ["HS6"] = "answers served at another tier than requested",
        ["HC1"] = "higher token volume",
        ["HC2"] = "higher price per token",
        ["HC3"] = "reasoning tokens",
        ["HC4"] = "weak prompt caching",
        ["HC5"] = "input tokens from tool rounds",
        ["WQ1"] = "answers without tools",
        ["WQ3"] = "critical errors",
        ["WQ4"] = "guessing instead of abstaining",
        ["WQ5"] = "budget, truncation or output defects",
        ["WQ6"] = "grader disagreement",
        ["WS1"] = "wait before output",
        ["WS2"] = "time spent generating output",
        ["WS3"] = "time spent on tool rounds",
        ["WS4"] = "tool execution time",
        ["WS5"] = "own waits and retries",
        ["WC1"] = "output cost",
        ["WC2"] = "uncached input cost",
        ["WC3"] = "input cost from tool rounds"
    };

    /// <summary>The label of <paramref name="hypothesis"/>; <paramref name="name"/> names a model by token.</summary>
    public static string Of(RootCauseHypothesis hypothesis, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        ArgumentNullException.ThrowIfNull(name);

        string first = hypothesis.Models.Count > 0 ? name(hypothesis.Models[0]) : "the models";
        string? second = hypothesis.Models.Count > 1 ? name(hypothesis.Models[1]) : null;
        switch (hypothesis.Id)
        {
            case "H0-Q":
            case "H0-S":
            case "H0-C":
                return Topic(hypothesis) + " between " + first + " and " + (second ?? "the others");
            case "H0-W":
                return "no difference within " + Possessive(first) + " own questions";
            case "HQ3":
                return Topic(hypothesis);
            case "HQ5" when hypothesis.Kind == "pair":
                return Topic(hypothesis) + ", " + hypothesis.Models.Count.ToString(CultureInfo.InvariantCulture) + " models";
            case "HQ8" when second != null && IsThinkingInversion(hypothesis):
                // The entry at the lower thinking level, the better side of an inversion, is named first.
                return Possessive(second) + " " + Topic(hypothesis) + ", compared with " + first;
            default:
                return Possessive(first) + " " + Topic(hypothesis)
                       + (second != null && hypothesis.Kind == "pair" ? ", compared with " + second : string.Empty);
        }
    }

    /// <summary>
    /// The rule's topic alone, with no model name: <c>answers without tools</c>, <c>no intelligence difference</c>,
    /// <c>points lost in the advanced band</c>. The labels read it; the hypothesis' reference fact is its key alone, <c>HQ1.1</c>.
    /// </summary>
    internal static string Topic(RootCauseHypothesis hypothesis)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        switch (hypothesis.Id)
        {
            case "H0-Q":
                return "no intelligence difference";
            case "H0-S":
                return "no speed difference";
            case "H0-C":
                return "no cost difference";
            case "H0-W":
                return "no difference within one model's own questions";
            case "HQ3":
                return "shared failures on the same questions";
            case "HQ5" when hypothesis.Kind == "pair":
                return "the concise response style";
            case "HC4" when hypothesis.ShortStatement.StartsWith(RootCauseHypotheses.CachingPriceShort, StringComparison.Ordinal):
                return "higher price per input token from prompt caching";
            case "HQ8" when IsThinkingInversion(hypothesis):
                return "higher score at the lower thinking level";
            case "WQ2":
                const string LostIn = "Lost in ";
                string group = hypothesis.ShortStatement.StartsWith(LostIn, StringComparison.Ordinal)
                    ? hypothesis.ShortStatement[LostIn.Length..]
                    : "one group";
                return Cut("points lost in " + group);
            default:
                return Cut(Topics.TryGetValue(hypothesis.Id, out var known)
                    ? known
                    : RootCauseHypotheses.LowerFirst(hypothesis.ShortStatement.Split(':')[0].Trim()));
        }
    }

    /// <summary>An HQ8 whose lower thinking level scored higher (<see cref="RootCauseHypotheses.ThinkingInversionShort"/>).</summary>
    private static bool IsThinkingInversion(RootCauseHypothesis hypothesis)
        => hypothesis.ShortStatement.StartsWith(RootCauseHypotheses.ThinkingInversionShort, StringComparison.Ordinal);

    /// <summary>The words of <paramref name="text"/>, split on whitespace.</summary>
    public static string[] Words(string text) => (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The topic, cut to <see cref="MaxWords"/> words.</summary>
    private static string Cut(string topic)
    {
        var words = Words(topic);
        return words.Length <= MaxWords ? topic : string.Join(' ', words.Take(MaxWords));
    }

    /// <summary><c>Model F's</c>: a named copy puts the model's name in place of <c>Model F</c> and keeps the <c>'s</c>.</summary>
    private static string Possessive(string name) => name + "'s";
}

/// <summary>
/// A research question before its hypotheses: its number, axis and model, the model's neighbor on the axis (the
/// next model toward the middle), its text, and for questions 1 and 2 whether the order against the neighbor is
/// established (null for questions 3 and 4).
/// </summary>
internal sealed record RootCauseQuestionTarget(int Number, string Axis, RootCauseModelInput Model, RootCauseModelInput Neighbor, string Text, bool? OrderEstablished);

/// <summary>
/// The model profiles, the research questions and the grade cap of a result: each a measured count, share or
/// rank with its questions, built from the evidence the engine read. No entry states a cause.
/// </summary>
public static class RootCauseProfiles
{
    public const string GainedPointsOn = "gainedPointsOn";
    public const string Strength = "strength";
    public const string LostPointsOn = "lostPointsOn";
    public const string WeakestDimension = "weakestDimension";
    public const string LowestRank = "lowestRank";
    public const string CriticalErrors = "criticalErrors";
    public const string NoTool = "noTool";
    public const string PanelDisagreement = "panelDisagreement";
    public const string IsolatedMisses = "isolatedMisses";
    public const string OutputDefects = "outputDefects";

    /// <summary>The gained-points entry, then at most two dimension ranks.</summary>
    public const int MaxStrengths = 3;

    public const int MaxWeaknesses = 3;

    /// <summary>The margin above the other models' median from which a question counts among the points a model gained.</summary>
    public const double GainMargin = 10.0;

    /// <summary>A profile entry lists at most this many questions, then counts the rest.</summary>
    private const int ListedQuestions = 3;

    private static readonly string[] DimensionNames = { "Accuracy", "Completeness", "Conciseness", "Readability" };

    /// <summary>Each analyzed model's profile, in model order.</summary>
    internal static List<RootCauseModelProfile> Build(RootCauseContext ctx)
    {
        var models = ctx.Models;
        var refs = ctx.Input.Questions.GroupBy(q => q.ItemKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        double? noToolMedian = Median(models
            .Select(m => m.Evidence.Scored.ToList())
            .Where(s => s.Count > 0)
            .Select(s => s.Count(a => a.NoTool) / (double)s.Count));
        var means = models.GroupBy(m => m.Token, StringComparer.Ordinal).ToDictionary(g => g.Key, g => RootCauseContext.ItemMeans(g.First()), StringComparer.Ordinal);
        string? sharedLowest = SharedLowestDimension(models);

        var profiles = new List<RootCauseModelProfile>();
        foreach (var model in models)
        {
            var profile = new RootCauseModelProfile
            {
                Token = model.Token,
                Letter = model.Letter,
                QualityIndex = model.QualityIndex,
                Dimensions = Enumerable.Range(0, 4).Select(k => k < model.Dimensions.Count ? model.Dimensions[k] : null).ToList()
            };
            if (Gained(models, model, refs, means) is { } gained) profile.Strengths.Add(gained);
            profile.Strengths.AddRange(Strengths(models, model).Take(MaxStrengths - 1));

            var weaknesses = new List<RootCauseProfileEntry>();
            if (Lost(ctx, model, refs, sharedLowest) is { } lost) weaknesses.Add(lost);
            if (Weakest(model) is { } weakest) weaknesses.Add(weakest);
            if (Lowest(models, model) is { } lowest) weaknesses.Add(lowest);
            profile.Weaknesses.AddRange(weaknesses.Take(MaxWeaknesses));

            profile.Flags.AddRange(Flags(models, model, refs, noToolMedian, means));
            profiles.Add(profile);
        }
        return profiles;
    }

    /// <summary>
    /// The research questions' targets, from the inputs alone: why the model with the highest Intelligence Index
    /// scored highest (1), why the lowest scored lowest (2), why the slowest by median model time is slowest (3)
    /// and why the most expensive per question is the most expensive (4). A question is asked only when its axis
    /// has at least two comparable models and its model is strictly ahead of its neighbor there. Questions 1 and 2
    /// say when the order against the neighbor is not established.
    /// </summary>
    internal static List<RootCauseQuestionTarget> Targets(RootCauseContext ctx)
    {
        var models = ctx.Models;
        var quality = models.Where(m => m.QualityIndex.HasValue).OrderByDescending(m => m.QualityIndex!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        var speed = models.Where(m => !m.SpeedDegraded && m.ModelTimeP50Ms.HasValue).OrderBy(m => m.ModelTimeP50Ms!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        var cost = models.Where(m => !m.CostDegraded && m.CostPerQuestionUsd.HasValue).OrderBy(m => m.CostPerQuestionUsd!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();

        var targets = new List<RootCauseQuestionTarget>();
        if (quality.Count >= 2)
        {
            var (top, next) = (quality[0], quality[1]);
            if (top.QualityIndex!.Value > next.QualityIndex!.Value)
            {
                bool established = OrderEstablished(ctx, top, next);
                string text = "Why did " + ctx.Name(top.Token) + " score highest"
                              + (established ? "?" : ", though its lead over " + ctx.Name(next.Token) + " is not established?");
                targets.Add(new RootCauseQuestionTarget(1, RootCauseAxes.Intelligence, top, next, text, established));
            }

            var (last, above) = (quality[^1], quality[^2]);
            if (last.QualityIndex!.Value < above.QualityIndex!.Value)
            {
                bool established = OrderEstablished(ctx, last, above);
                string text = "Why did " + ctx.Name(last.Token) + " score lowest"
                              + (established ? "?" : ", though its gap to " + ctx.Name(above.Token) + " is not established?");
                targets.Add(new RootCauseQuestionTarget(2, RootCauseAxes.Intelligence, last, above, text, established));
            }
        }
        if (speed.Count >= 2 && speed[^1].ModelTimeP50Ms!.Value > speed[^2].ModelTimeP50Ms!.Value)
        {
            targets.Add(new RootCauseQuestionTarget(3, RootCauseAxes.Speed, speed[^1], speed[^2], "Why is " + ctx.Name(speed[^1].Token) + " slowest?", null));
        }
        if (cost.Count >= 2 && cost[^1].CostPerQuestionUsd!.Value > cost[^2].CostPerQuestionUsd!.Value)
        {
            targets.Add(new RootCauseQuestionTarget(4, RootCauseAxes.Cost, cost[^1], cost[^2], "Why is " + ctx.Name(cost[^1].Token) + " the most expensive?", null));
        }
        return targets;
    }

    /// <summary>
    /// Whether <paramref name="model"/>'s place on the Intelligence Index against <paramref name="neighbor"/> is
    /// established: a paired test of the two is established and favors the model, or their 95% intervals do not
    /// overlap (touching bounds overlap).
    /// </summary>
    private static bool OrderEstablished(RootCauseContext ctx, RootCauseModelInput model, RootCauseModelInput neighbor)
    {
        var pair = new RootCausePair(model.Token, neighbor.Token);
        bool tested = ctx.TestsFor(pair, RootCauseAxes.Intelligence)
            .Any(t => t.Established && (t.Favors == "First" ? t.First : t.Favors == "Second" ? t.Second : null) == model.Token);
        return tested || ctx.IntervalsOverlap(pair) == false;
    }

    /// <summary>The rules whose pair hypotheses answer question 1: mechanisms of the pair itself, the graders (HQ4) and the thinking level (HQ8).</summary>
    private static readonly string[] LeadRules = { "HQ4", "HQ8" };

    /// <summary>
    /// Whether <paramref name="hypothesis"/> answers the research question of <paramref name="target"/>. Question 1
    /// asks why a model did well: only the pair hypotheses that locate the gap in the pair itself (HQ4, HQ8) with the
    /// model as the better side, never one that locates it in the other model's own failures. The others ask why
    /// it did badly: the pair hypotheses on the axis where it is the worse side, and its own within-model ones.
    /// HQ3 and the comparison's single HQ5, which are about every model, answer none.
    /// </summary>
    internal static bool Answers(RootCauseQuestionTarget target, RootCauseHypothesis hypothesis)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hypothesis);
        var h = hypothesis;
        string token = target.Model.Token;
        if (h.Axis != target.Axis || RootCauseHypotheses.IsDocumentWide(h) || h.Models.Count == 0) return false;
        return target.Number == 1
            ? h.Kind == "pair" && h.Models.Count == 2 && h.Models[1] == token && LeadRules.Contains(h.Id, StringComparer.Ordinal)
            : (h.Kind == "pair" && h.Models.Count == 2 && h.Models[0] == token) || (h.Kind == "within" && h.Models[0] == token);
    }

    /// <summary>
    /// The models a question 3 or 4 compares its model with, most telling first: its same-model siblings at another
    /// thinking level, the nearest level first; then the reference; then the fastest model (question 3) or the
    /// cheapest (question 4); then every other model in model order. The model itself is never among them.
    /// </summary>
    internal static List<string> PartnerPreference(RootCauseContext ctx, RootCauseQuestionTarget target)
    {
        var model = target.Model;
        var order = ctx.Models
            .Where(m => m.Token != model.Token && RootCauseHypotheses.SameModelAtTwoLevels(model, m))
            .OrderBy(m => Math.Abs(RootCauseHypotheses.ThinkingRank(m.ThinkingLevel) - RootCauseHypotheses.ThinkingRank(model.ThinkingLevel)))
            .ThenBy(m => m.Token, StringComparer.Ordinal)
            .Select(m => m.Token)
            .ToList();
        if (ctx.Input.ReferenceToken is string reference) order.Add(reference);
        var extreme = target.Number == 4
            ? ctx.Models.Where(m => !m.CostDegraded && m.CostPerQuestionUsd.HasValue)
                .OrderBy(m => m.CostPerQuestionUsd!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).FirstOrDefault()
            : ctx.Models.Where(m => !m.SpeedDegraded && m.ModelTimeP50Ms.HasValue)
                .OrderBy(m => m.ModelTimeP50Ms!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).FirstOrDefault();
        if (extreme != null) order.Add(extreme.Token);
        order.AddRange(ctx.Models.Select(m => m.Token));
        return order.Where(t => t != model.Token && ctx.Models.Any(m => m.Token == t)).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Where a hypothesis stands in its question's preference, lower first: for questions 3 and 4, the place of the
    /// pair hypothesis' other model in <see cref="PartnerPreference"/> (a within-model one, or a model outside it,
    /// last); 0 for questions 1 and 2, which keep rank order.
    /// </summary>
    internal static int PartnerRank(RootCauseContext ctx, RootCauseQuestionTarget target, RootCauseHypothesis hypothesis)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hypothesis);
        if (target.Number is not (3 or 4)) return 0;
        if (hypothesis.Kind != "pair" || hypothesis.Models.Count != 2) return int.MaxValue;
        string partner = hypothesis.Models[0] == target.Model.Token ? hypothesis.Models[1] : hypothesis.Models[0];
        int index = PartnerPreference(ctx, target).IndexOf(partner);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>
    /// The research questions of <paramref name="targets"/>, each with the kept hypotheses that answer it: the
    /// comparative (pair) ones, then the model's own within-model ones, each in rank order, the comparative ones
    /// of questions 3 and 4 first by <see cref="PartnerRank"/>. The grade is that of the first comparative one at
    /// Low or higher; when there is none, the best within-model grade at Low or higher, capped at Low. Question 4
    /// is answered from the price cards when that first comparative one is a price effect.
    /// </summary>
    internal static List<RootCauseQuestion> Questions(RootCauseContext ctx, RootCauseResult result, IReadOnlyList<RootCauseQuestionTarget> targets)
    {
        int lowRank = RootCauseGrades.Rank(RootCauseGrades.Low);
        var questions = new List<RootCauseQuestion>();
        foreach (var target in targets)
        {
            // result.Hypotheses is already in rank order; the stable sort keeps it within a partner.
            var answering = result.Hypotheses.Where(h => Answers(target, h)).ToList();
            var comparative = answering.Where(h => h.Kind == "pair").OrderBy(h => PartnerRank(ctx, target, h)).ToList();
            var within = answering.Where(h => h.Kind == "within").ToList();
            var bestComparative = comparative.FirstOrDefault(h => RootCauseGrades.Rank(h.Grade) >= lowRank);
            var bestWithin = within.FirstOrDefault(h => RootCauseGrades.Rank(h.Grade) >= lowRank);
            questions.Add(new RootCauseQuestion
            {
                Number = target.Number,
                Axis = target.Axis,
                Token = target.Model.Token,
                Text = target.Text,
                Grade = bestComparative?.Grade ?? (bestWithin != null ? RootCauseGrades.Capped(bestWithin.Grade, RootCauseGrades.Low) : null),
                HypothesisKeys = comparative.Concat(within).Select(h => h.Key).ToList(),
                ComparativeKeys = comparative.Select(h => h.Key).ToList(),
                WithinKeys = within.Select(h => h.Key).ToList(),
                WithinOnly = bestComparative == null && within.Count > 0,
                OrderEstablished = target.OrderEstablished,
                Neighbor = target.Number is 1 or 2 ? target.Neighbor.Token : null,
                PriceCard = target.Number == 4 && bestComparative?.PriceEffect == true,
                Signals = Signals(ctx, target, result)
            });
        }
        return questions;
    }

    /// <summary>
    /// The highest grade the run counts allow: a model with a single run per suite fails the replicates check of
    /// every hypothesis about it, so none can be graded above Moderate. A price effect (HC2) among
    /// <paramref name="hypotheses"/> is exempt, since it follows from the price cards rather than from a run. Null
    /// when every model has two or more.
    /// </summary>
    internal static string? GradeCap(RootCauseContext ctx, IReadOnlyList<RootCauseHypothesis> hypotheses)
    {
        ArgumentNullException.ThrowIfNull(hypotheses);
        var single = ctx.Models.Where(m => m.Evidence.RunsPerSuite < 2).ToList();
        if (single.Count == 0) return null;

        string cap = RootCauseGrades.WordWithGrade(RootCauseGrades.Moderate);
        bool priceEffect = hypotheses.Any(h => h.PriceEffect && h.Grade != RootCauseGrades.NotSupported);
        string secondRun = " until a second run of each suite is made"
                           + (priceEffect ? ", apart from a price effect, which follows from the price cards." : ".");
        if (single.Count == ctx.Models.Count)
        {
            return (ctx.Models.Count == 1
                       ? "The model ran only once per suite, so this document cannot tell whether its results would repeat. "
                       : "Each model ran only once per suite, so this document cannot tell whether its results would repeat. ")
                   + "No cause can be graded above " + cap + secondRun;
        }
        bool one = single.Count == 1;
        return BenchmarkReportFormat.LetterList(single.Select(m => ctx.Name(m.Token)).ToList())
               + " ran only once per suite, so this document cannot tell whether " + (one ? "its" : "their") + " results would repeat. "
               + "No cause involving " + (one ? "it" : "them") + " can be graded above " + cap + secondRun;
    }

    // ---------------------------------------------------------------------------------------------
    // Profile entries
    // ---------------------------------------------------------------------------------------------

    /// <summary>The dimensions the model ranks first or second on among the models with that dimension, best rank first.</summary>
    private static IEnumerable<RootCauseProfileEntry> Strengths(IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model)
    {
        var ranked = new List<(int Rank, int Dimension, string Text)>();
        for (int d = 0; d < 4; d++)
        {
            if (DimensionOf(model, d) is not double own) continue;
            var values = models.Select(m => DimensionOf(m, d)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            int above = values.Count(v => v > own + 1e-9);
            int below = values.Count(v => v < own - 1e-9);
            bool tied = values.Count(v => Math.Abs(v - own) <= 1e-9) > 1;
            int rank = above + 1;
            if (below == 0 || (rank == 1 && values.Count < 2) || (rank == 2 && values.Count < 3) || rank > 2) continue;
            string place = (tied ? "tied for " : string.Empty) + (rank == 1 ? "first" : "second");
            ranked.Add((rank, d, place));
        }
        return ranked.OrderBy(r => r.Rank).ThenBy(r => r.Dimension)
            .Select(r => new RootCauseProfileEntry
            {
                Kind = Strength,
                Text = r.Text + " of " + Inv(models.Count(m => DimensionOf(m, r.Dimension).HasValue)) + " models on " + DimensionNames[r.Dimension],
                Short = r.Text + " on " + DimensionNames[r.Dimension]
            });
    }

    /// <summary>
    /// Where the model's score came from against the other models: the questions it scored at least
    /// <see cref="GainMargin"/> above the median of the other models that scored them (at least
    /// <see cref="BenchmarkComparisonModelProfile.IsolatedMissMinOthers"/>), largest margin first, and the index points
    /// those margins add up to over its questions. Null with fewer than
    /// <see cref="BenchmarkComparisonModelProfile.MatrixMinModels"/> scored models, or when the model scored none; with no
    /// such question, an entry that says so.
    /// </summary>
    private static RootCauseProfileEntry? Gained(IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model,
        IReadOnlyDictionary<string, RootCauseQuestionRef> refs, IReadOnlyDictionary<string, Dictionary<string, double>> means)
    {
        if (models.Count(m => means.TryGetValue(m.Token, out var scored) && scored.Count > 0) < BenchmarkComparisonModelProfile.MatrixMinModels) return null;
        if (!means.TryGetValue(model.Token, out var own) || own.Count == 0) return null;

        var gained = new List<(string Key, double Margin)>();
        foreach (var (key, score) in own)
        {
            var others = models
                .Where(m => m.Token != model.Token && means.TryGetValue(m.Token, out var theirs) && theirs.ContainsKey(key))
                .Select(m => means[m.Token][key])
                .ToList();
            if (others.Count < BenchmarkComparisonModelProfile.IsolatedMissMinOthers || RootCauseStatistics.Median(others) is not double median) continue;
            double margin = score - median;
            if (margin >= GainMargin - 1e-9) gained.Add((key, margin));
        }

        string threshold = BenchmarkReportFormat.Whole(GainMargin);
        if (gained.Count == 0)
        {
            return new RootCauseProfileEntry
            {
                Kind = GainedPointsOn,
                Text = "no answer " + threshold + " or more above the other models' median",
                Short = "none"
            };
        }

        var ordered = gained
            .OrderByDescending(g => g.Margin)
            .ThenBy(g => refs.TryGetValue(g.Key, out var r) ? r.Number : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var listed = ordered.Select(g => refs.GetValueOrDefault(g.Key)).OfType<RootCauseQuestionRef>().ToList();
        string points = "+" + BenchmarkReportFormat.OneDecimal(ordered.Sum(g => g.Margin) / own.Count);
        string count = Inv(ordered.Count) + (ordered.Count == 1 ? " question" : " questions");
        return new RootCauseProfileEntry
        {
            Kind = GainedPointsOn,
            Text = "answers above the other models' median by " + threshold + " or more: " + points + " index points on " + count
                   + Listed(listed, byContribution: true),
            Short = points + " points on " + count,
            Questions = listed.Select(r => r.Reference).ToList()
        };
    }

    /// <summary>
    /// The dimension with the lowest mean for every model with all four dimensions, by name, as the shared-weakness
    /// finding tests it: null with fewer than two such models, when one of them has no spread across its dimensions,
    /// or when their lowest dimensions differ.
    /// </summary>
    internal static string? SharedLowestDimension(IReadOnlyList<RootCauseModelInput> models)
    {
        var complete = models
            .Select(m => Enumerable.Range(0, 4).Select(d => DimensionOf(m, d)).ToArray())
            .Where(d => d.All(v => v.HasValue))
            .Select(d => d.Select(v => v!.Value).ToArray())
            .ToList();
        if (complete.Count < 2 || complete.Any(d => d.Max() - d.Min() <= 0.05)) return null;
        var lowest = complete.Select(d => Array.IndexOf(d, d.Min())).Distinct().ToList();
        return lowest.Count == 1 ? DimensionNames[lowest[0]] : null;
    }

    /// <summary>
    /// The largest segment of the model's index points at risk, with its points and the questions behind it. The catch-all
    /// segment names the dimension that lost most on it (<see cref="MostlyLost"/>), unless that is <paramref name="sharedLowest"/>.
    /// </summary>
    private static RootCauseProfileEntry? Lost(RootCauseContext ctx, RootCauseModelInput model, IReadOnlyDictionary<string, RootCauseQuestionRef> refs, string? sharedLowest)
    {
        var at = ctx.AtRisk(model.Token);
        if (at.Total <= 0) return null;

        int segment = Array.IndexOf(at.Points, at.Points.Max());
        var questionRefs = model.Evidence.Scored
            .GroupBy(a => a.QuestionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => refs.GetValueOrDefault(g.First().ItemKey), StringComparer.Ordinal);
        var contributing = at.ByQuestion
            .Where(kv => kv.Value[segment] > 0)
            .OrderByDescending(kv => kv.Value[segment])
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        var listed = contributing
            .Select(kv => questionRefs.GetValueOrDefault(kv.Key))
            .OfType<RootCauseQuestionRef>()
            .ToList();

        string label = RootCauseSegments.Label(RootCauseSegments.AtRisk[segment]);
        string points = BenchmarkReportFormat.OneDecimal(at.Points[segment]);
        string? shortLabel = label;
        if (RootCauseSegments.AtRisk[segment] == RootCauseSegments.OtherLow && MostlyLost(model, at) is { } dimension)
        {
            // The dimension every model is weakest on is stated once for the document, not per model.
            bool shared = string.Equals(dimension, sharedLowest, StringComparison.Ordinal);
            shortLabel = shared ? null : "mostly " + dimension;
            label = "low-scoring answers" + (shared ? string.Empty : ", " + shortLabel);
        }
        string text = label + ": " + points + " index points on " + Inv(contributing.Count) + (contributing.Count == 1 ? " question" : " questions")
                      + Listed(listed, byContribution: true);
        return new RootCauseProfileEntry
        {
            Kind = LostPointsOn,
            Text = text,
            Short = (shortLabel == null ? string.Empty : shortLabel + ", ") + points + " points",
            Questions = listed.Select(r => r.Reference).ToList()
        };
    }

    /// <summary>
    /// Accuracy or Completeness, whichever loses more on the answers in the catch-all at-risk segment (below the
    /// model's median, with no stronger signal): the sum over them of each dimension's weight times its shortfall
    /// from 100, unweighted when the profile gives both no weight. Null when neither dimension was scored or the two tie.
    /// </summary>
    private static string? MostlyLost(RootCauseModelInput model, RootCauseAtRisk at)
    {
        if (at.Median is not double median) return null;
        var answers = model.Evidence.Scored
            .Where(a => a.Quality < median && !a.CriticalError && !a.BudgetOrDefect && !a.NoTool)
            .ToList();
        var weights = model.Evidence.Weights;
        double wa = weights.Length > 0 ? weights[0] : 0, wc = weights.Length > 1 ? weights[1] : 0;
        if (wa <= 0 && wc <= 0) wa = wc = 1;
        double accuracy = answers.Where(a => a.Accuracy.HasValue).Sum(a => wa * (100 - a.Accuracy!.Value));
        double completeness = answers.Where(a => a.Completeness.HasValue).Sum(a => wc * (100 - a.Completeness!.Value));
        if (!answers.Any(a => a.Accuracy.HasValue || a.Completeness.HasValue) || Math.Abs(completeness - accuracy) < 1e-9) return null;
        return completeness > accuracy ? DimensionNames[1] : DimensionNames[0];
    }

    /// <summary>The dimension furthest below the model's Accuracy, with the gap; null when none is below it.</summary>
    private static RootCauseProfileEntry? Weakest(RootCauseModelInput model)
    {
        if (DimensionOf(model, 0) is not double accuracy) return null;
        var lowest = Enumerable.Range(1, 3)
            .Select(d => (Dimension: d, Value: DimensionOf(model, d)))
            .Where(x => x.Value.HasValue)
            .OrderBy(x => x.Value!.Value)
            .ThenBy(x => x.Dimension)
            .FirstOrDefault();
        if (lowest.Value is not double value || accuracy - value < 0.05) return null;

        string gap = BenchmarkReportFormat.OneDecimal(accuracy - value);
        return new RootCauseProfileEntry
        {
            Kind = WeakestDimension,
            Text = DimensionNames[lowest.Dimension] + ", " + gap + " points below Accuracy",
            Short = DimensionNames[lowest.Dimension] + " " + gap + " below Accuracy"
        };
    }

    /// <summary>The first dimension the model ranks last on among three or more models; null when there is none.</summary>
    private static RootCauseProfileEntry? Lowest(IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model)
    {
        for (int d = 0; d < 4; d++)
        {
            if (DimensionOf(model, d) is not double own) continue;
            var values = models.Select(m => DimensionOf(m, d)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (values.Count < 3 || values.Any(v => v < own - 1e-9) || !values.Any(v => v > own + 1e-9)) continue;
            bool tied = values.Count(v => Math.Abs(v - own) <= 1e-9) > 1;
            string place = tied ? "tied for last" : "last";
            return new RootCauseProfileEntry
            {
                Kind = LowestRank,
                Text = place + " of " + Inv(values.Count) + " models on " + DimensionNames[d],
                Short = place + " on " + DimensionNames[d]
            };
        }
        return null;
    }

    /// <summary>
    /// What stands out in the model's answers: critical errors; answers without tools at a rate above the
    /// models' median; panel disagreement that stands out against the other models
    /// (<see cref="BenchmarkNotableFindings.DisagreementConcentration"/>, the notable findings' own rule);
    /// isolated misses; budget, tool-limit or output defects. Each display is a compact noun phrase, a count
    /// with its questions; the rules behind the entries are stated once, by the document's profile note.
    /// </summary>
    private static IEnumerable<RootCauseProfileEntry> Flags(
        IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model, IReadOnlyDictionary<string, RootCauseQuestionRef> refs,
        double? noToolMedian, IReadOnlyDictionary<string, Dictionary<string, double>> means)
    {
        var scored = model.Evidence.Scored.ToList();
        List<RootCauseQuestionRef> RefsOf(IEnumerable<RootCauseAnswerRecord> answers)
            => answers.Select(a => refs.GetValueOrDefault(a.ItemKey)).OfType<RootCauseQuestionRef>()
                .GroupBy(r => r.Reference, StringComparer.Ordinal).Select(g => g.First()).ToList();

        var critical = scored.Where(a => a.CriticalError).ToList();
        if (critical.Count > 0)
        {
            var listed = RefsOf(critical);
            string count = Inv(critical.Count) + (critical.Count == 1 ? " critical error" : " critical errors");
            yield return new RootCauseProfileEntry
            {
                Kind = CriticalErrors,
                Text = count + Listed(listed),
                Short = count,
                Questions = listed.Select(r => r.Reference).ToList()
            };
        }

        var noTool = scored.Where(a => a.NoTool).ToList();
        if (scored.Count > 0 && noToolMedian is double median && noTool.Count / (double)scored.Count > median + 1e-9)
        {
            var listed = RefsOf(noTool);
            string count = Inv(noTool.Count) + " of " + Inv(scored.Count) + " answers without tools";
            yield return new RootCauseProfileEntry
            {
                Kind = NoTool,
                Text = count + Listed(listed),
                Short = count,
                Questions = listed.Select(r => r.Reference).ToList()
            };
        }

        if (PanelDisagreementOf(models, model) is { } panel)
        {
            yield return new RootCauseProfileEntry
            {
                Kind = PanelDisagreement,
                Text = BenchmarkNotableFindings.ProfileDisagreementPhrase(panel.Rate, panel.OthersMedian),
                Short = "panel disagreement " + RootCauseHypotheses.Pct(panel.Rate)
            };
        }

        if (models.Count >= BenchmarkComparisonModelProfile.MatrixMinModels && means.TryGetValue(model.Token, out var own))
        {
            var isolated = own
                .Where(kv => kv.Value <= BenchmarkComparisonModelProfile.IsolatedMissAtMost)
                .Where(kv =>
                {
                    var others = models.Where(m => m.Token != model.Token && means.TryGetValue(m.Token, out var theirs) && theirs.ContainsKey(kv.Key))
                        .Select(m => means[m.Token][kv.Key])
                        .ToList();
                    return others.Count >= BenchmarkComparisonModelProfile.IsolatedMissMinOthers
                           && RootCauseStatistics.Median(others) >= BenchmarkComparisonModelProfile.IsolatedMissOthersMedianAtLeast;
                })
                .Select(kv => kv.Key)
                .ToList();
            if (isolated.Count > 0)
            {
                var listed = isolated.Select(k => refs.GetValueOrDefault(k)).OfType<RootCauseQuestionRef>().ToList();
                string count = Inv(isolated.Count) + (isolated.Count == 1 ? " isolated miss" : " isolated misses");
                yield return new RootCauseProfileEntry
                {
                    Kind = IsolatedMisses,
                    Text = count + Listed(listed),
                    Short = count,
                    Questions = listed.Select(r => r.Reference).ToList()
                };
            }
        }

        var defects = scored.Where(a => a.BudgetOrDefect).ToList();
        if (defects.Count > 0)
        {
            var listed = RefsOf(defects);
            string count = Inv(defects.Count) + (defects.Count == 1 ? " answer" : " answers");
            yield return new RootCauseProfileEntry
            {
                Kind = OutputDefects,
                Text = count + " with budget, tool-limit, truncation or output defects" + Listed(listed),
                Short = count + " with defects",
                Questions = listed.Select(r => r.Reference).ToList()
            };
        }
    }

    /// <summary>
    /// The model's top measured signals for a research question. Question 1 opens with the dimension that carries
    /// most of the lead over the runner-up; question 3 adds the component that carries most of the time difference
    /// against a decomposed neighbor.
    /// </summary>
    private static List<string> Signals(RootCauseContext ctx, RootCauseQuestionTarget target, RootCauseResult result)
    {
        var model = target.Model;
        var profile = result.Profiles.FirstOrDefault(p => p.Token == model.Token);
        var evidence = result.Evidence.FirstOrDefault(e => e.Token == model.Token);
        var signals = new List<string>();
        string? lost = profile?.Weaknesses.FirstOrDefault(w => w.Kind == LostPointsOn)?.Text;
        string? weakest = profile?.Weaknesses.FirstOrDefault(w => w.Kind == WeakestDimension)?.Text;

        switch (target.Number)
        {
            case 1:
                if (LeadSignal(ctx, target, result) is { } leadSignal) signals.Add(leadSignal);
                signals.AddRange(profile?.Strengths.Where(s => s.Kind != GainedPointsOn || s.Questions.Count > 0).Select(s => s.Text) ?? Enumerable.Empty<string>());
                break;
            case 2:
                if (lost != null) signals.Add("lost points on " + lost);
                if (weakest != null) signals.Add("weakest dimension " + weakest);
                signals.AddRange((profile?.Flags ?? new List<RootCauseProfileEntry>()).Take(2).Select(f => f.Text));
                break;
            case 3:
                if (model.ModelTimeP50Ms is double p50) signals.Add("median model time " + BenchmarkReportFormat.Seconds(p50));
                if (SlowestSignal(ctx, target, result) is { } slowestSignal) signals.Add(slowestSignal);
                if (evidence != null && evidence.Time.Count > 0 && evidence.Time.Sum(s => s.Value) > 0)
                {
                    var lead = evidence.Time.OrderByDescending(s => s.Value).First();
                    signals.Add(lead.Label + " " + RootCauseHypotheses.Pct(lead.Value / evidence.Time.Sum(s => s.Value)) + " of its time per question");
                }
                if (evidence?.CallsPerAnswer is double calls) signals.Add(BenchmarkReportFormat.CompactDecimal(calls) + " model calls per answer");
                break;
            default:
                if (model.CostPerQuestionUsd is double usd) signals.Add(BenchmarkReportFormat.Usd(usd) + " per question");
                if (evidence != null && evidence.Cost.Count > 0 && evidence.Cost.Sum(s => s.Value) > 0)
                {
                    var lead = evidence.Cost.OrderByDescending(s => s.Value).First();
                    signals.Add(lead.Label + " " + RootCauseHypotheses.Pct(lead.Value / evidence.Cost.Sum(s => s.Value)) + " of its cost per question");
                }
                if (evidence?.CacheReadShare is double cache) signals.Add(RootCauseHypotheses.Pct(cache) + " of input tokens read from the cache");
                break;
        }
        return signals;
    }

    /// <summary>
    /// Question 1: the dimension contributing most to the leader's lead over the runner-up, from the pair's quality
    /// split oriented leader first: <c>most of the lead over Model B is Completeness (+2.7 of +4.4 points)</c>. Null
    /// without the split, or when no dimension adds to the lead.
    /// </summary>
    private static string? LeadSignal(RootCauseContext ctx, RootCauseQuestionTarget target, RootCauseResult result)
    {
        string leader = target.Model.Token, runnerUp = target.Neighbor.Token;
        var split = result.QualitySplits.FirstOrDefault(s => (s.First == leader && s.Second == runnerUp) || (s.First == runnerUp && s.Second == leader));
        if (split == null) return null;

        double sign = split.First == leader ? 1 : -1;
        double lead = sign * split.IndexDifference;
        var top = split.Components
            .Where(c => c.Key != "other")
            .Select(c => (c.Label, Amount: sign * c.Amount))
            .OrderByDescending(c => c.Amount)
            .FirstOrDefault();
        if (lead <= 0 || top.Label == null || top.Amount <= 0) return null;

        return (top.Amount >= lead / 2 ? "most of the lead over " : "the largest part of the lead over ") + ctx.Name(runnerUp) + " is " + top.Label
               + " (" + BenchmarkReportFormat.SignedOneDecimal(top.Amount) + " of " + BenchmarkReportFormat.SignedOneDecimal(lead) + " points)";
    }

    /// <summary>
    /// Question 3: the component carrying most of the slowest model's time difference against a decomposed
    /// neighbor, the first in <see cref="PartnerPreference"/>: its analyzed same-model sibling at another thinking
    /// level (the nearest level first), else the reference, else the fastest model, else any other.
    /// <c>against Model D: latency per call (time to first token) carries 62% of the 4.1 s difference</c>. Null
    /// when none of those pairs is decomposed.
    /// </summary>
    private static string? SlowestSignal(RootCauseContext ctx, RootCauseQuestionTarget target, RootCauseResult result)
    {
        var slowest = target.Model;
        foreach (string other in PartnerPreference(ctx, target))
        {
            var pair = result.SpeedPairs.FirstOrDefault(p =>
                ((p.First == slowest.Token && p.Second == other) || (p.First == other && p.Second == slowest.Token))
                && p.UnavailableReason == null && !p.NotEstablished && p.Difference is double d && d != 0);
            var lead = pair?.Components.Where(c => c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
            if (pair == null || lead == null) continue;
            return "against " + ctx.Name(other) + ": " + lead.Label + " carries " + RootCauseHypotheses.Pct(lead.Share!.Value)
                   + " of the " + BenchmarkReportFormat.Seconds(Math.Abs(pair.Difference!.Value)) + " difference";
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The model's panel disagreement when it stands out against the other analyzed models, by the notable
    /// findings' rule (<see cref="BenchmarkNotableFindings.DisagreementConcentration"/>): its share of the answers
    /// both panel members graded, and the other models' median share. Null otherwise, or without a panel.
    /// </summary>
    internal static (double Rate, double OthersMedian)? PanelDisagreementOf(IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model)
        => BenchmarkNotableFindings.DisagreementConcentration(
            model.Evidence.PanelDisagreements, model.Evidence.PanelAnswers,
            models.Where(m => m.Token != model.Token).Select(m => (m.Evidence.PanelDisagreements, m.Evidence.PanelAnswers)));

    private static double? DimensionOf(RootCauseModelInput model, int dimension)
        => dimension < model.Dimensions.Count ? model.Dimensions[dimension] : null;

    /// <summary>
    /// <c> (Q3, Q5 and Q7)</c>: at most <see cref="ListedQuestions"/> references, the rest counted; empty for none.
    /// By contribution the first ones are kept, then shown in question order.
    /// </summary>
    private static string Listed(IReadOnlyList<RootCauseQuestionRef> refs, bool byContribution = false)
    {
        if (refs.Count == 0) return string.Empty;
        var shown = (byContribution ? refs.Take(ListedQuestions) : refs.OrderBy(r => r.Number).ThenBy(r => r.Reference, StringComparer.Ordinal).Take(ListedQuestions))
            .OrderBy(r => r.Number)
            .ThenBy(r => r.Reference, StringComparer.Ordinal)
            .Select(r => r.Reference)
            .ToList();
        int more = refs.Count - shown.Count;
        return " (" + (more > 0 ? string.Join(", ", shown) + " and " + Inv(more) + " more" : BenchmarkReportFormat.LetterList(shown)) + ")";
    }

    private static double? Median(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count >= 2 ? RootCauseStatistics.Median(list) : null;
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}

// ---------------------------------------------------------------------------------------------
// Priors and unexpected results
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The unexpected results of a comparison. Every pair of analyzed models of one provider with different model ids is
/// checked against the priors of <see cref="RootCausePriors"/>: the price-tier priors when both list output prices are
/// known and one is at least <see cref="RootCauseEngine.TierPriceRatio"/> times the other, the release prior within one
/// model line at one thinking level when both release dates are known and differ. A result is unexpected when the
/// reversal is established, or when it reaches <see cref="RootCauseEngine.AnomalyMinPoints"/> index points (quality) or
/// <see cref="RootCauseEngine.AnomalyTimeRatio"/> times the median model time (speed); speed-degraded models are
/// skipped. Each carries what the engine measured about its pair; its hypotheses and what settles it are added once the
/// hypotheses and the next runs are known.
/// </summary>
public static class RootCauseAnomalies
{
    private static readonly Regex NumberSegment = new(@"^\d+(\.\d+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] DimensionNames = { "Accuracy", "Completeness", "Conciseness", "Readability" };

    private static readonly int OutputPriceIndex = RootCauseSegments.CostComponents.ToList().IndexOf(RootCauseSegments.Output);

    /// <summary>The most dimensions, questions and hypotheses an unexpected result lists.</summary>
    private const int MaxDimensions = 2;

    private const int MaxQuestions = 3;
    private const int MaxHypotheses = 2;

    /// <summary>
    /// A model id's line: its segments split on <c>-</c>, lower case, without the segments that are version numbers
    /// (<c>6</c>, <c>5.6</c>), so <c>gpt-6-luna</c> and <c>gpt-5.6-luna</c> are both <c>gpt-luna</c>.
    /// </summary>
    public static string ModelLine(string? modelId)
        => string.Join("-", (modelId ?? string.Empty).Trim().ToLowerInvariant()
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !NumberSegment.IsMatch(s)));

    /// <summary>
    /// The unexpected results of <paramref name="ctx"/>, ordered by prior (<see cref="RootCausePriors.All"/>), then by gap,
    /// largest first, and numbered U1, U2, …; with the priors at least one pair was checked against. Both empty in model scope.
    /// </summary>
    internal static (List<RootCauseAnomaly> Anomalies, List<string> Checked) Find(RootCauseContext ctx)
    {
        var found = new List<RootCauseAnomaly>();
        var checkedPriors = new HashSet<string>(StringComparer.Ordinal);
        if (!ctx.Comparison) return (found, new List<string>());

        var models = ctx.Models;
        for (int i = 0; i < models.Count; i++)
        {
            for (int j = i + 1; j < models.Count; j++)
            {
                var a = models[i];
                var b = models[j];
                if (!RootCauseHypotheses.SameProvider(a.Provider, b.Provider) || !RootCauseHypotheses.DifferentModels(a, b)) continue;

                if (OutputPrice(a) is double pa && OutputPrice(b) is double pb && Math.Max(pa, pb) >= RootCauseEngine.TierPriceRatio * Math.Min(pa, pb) - 1e-12)
                {
                    var (dearer, cheaper) = pa >= pb ? (a, b) : (b, a);
                    if (dearer.QualityIndex.HasValue && cheaper.QualityIndex.HasValue)
                    {
                        checkedPriors.Add(RootCausePriors.PriceTierQuality);
                        if (Quality(ctx, RootCausePriors.PriceTierQuality, dearer, cheaper) is { } quality) found.Add(quality);
                    }
                    if (!dearer.SpeedDegraded && !cheaper.SpeedDegraded && dearer.ModelTimeP50Ms is > 0.0 && cheaper.ModelTimeP50Ms is > 0.0)
                    {
                        checkedPriors.Add(RootCausePriors.PriceTierSpeed);
                        if (Speed(ctx, cheaper, dearer) is { } speed) found.Add(speed);
                    }
                }

                string line = ModelLine(a.ModelId);
                if (line.Length > 0 && line == ModelLine(b.ModelId) && RootCauseContext.SameSetting(a.ThinkingLevel, b.ThinkingLevel)
                    && a.QualityIndex.HasValue && b.QualityIndex.HasValue
                    && ctx.ReleaseDate(a) is string ra && ctx.ReleaseDate(b) is string rb && !string.Equals(ra, rb, StringComparison.Ordinal))
                {
                    checkedPriors.Add(RootCausePriors.NewerRelease);
                    var (newer, older) = string.CompareOrdinal(ra, rb) > 0 ? (a, b) : (b, a);
                    if (Quality(ctx, RootCausePriors.NewerRelease, newer, older) is { } release) found.Add(release);
                }
            }
        }

        var ordered = found
            .OrderBy(x => RootCausePriors.All.ToList().IndexOf(x.Prior))
            .ThenByDescending(x => x.Gap)
            .ThenBy(x => x.Expected.Length).ThenBy(x => x.Expected, StringComparer.Ordinal)
            .ThenBy(x => x.Other.Length).ThenBy(x => x.Other, StringComparer.Ordinal)
            .ToList();
        for (int n = 0; n < ordered.Count; n++)
        {
            ordered[n].Number = n + 1;
            ordered[n].Ref = "U" + (n + 1).ToString(CultureInfo.InvariantCulture);
        }
        return (ordered, RootCausePriors.All.Where(checkedPriors.Contains).ToList());
    }

    /// <summary>
    /// A quality prior against <paramref name="expected"/>: unexpected when a paired test of the pair is established and
    /// favors <paramref name="other"/>, or, without a test, when the two 95% intervals do not overlap with
    /// <paramref name="other"/> ahead; or when <paramref name="other"/> scores at least <see cref="RootCauseEngine.AnomalyMinPoints"/> higher.
    /// </summary>
    private static RootCauseAnomaly? Quality(RootCauseContext ctx, string prior, RootCauseModelInput expected, RootCauseModelInput other)
    {
        double e = expected.QualityIndex!.Value, o = other.QualityIndex!.Value;
        var pair = PairOf(expected.Token, other.Token);
        var tests = ctx.TestsFor(pair, RootCauseAxes.Intelligence);
        bool tested = tests.Any(t => t.NotTestedReason == null);
        bool? overlap = ctx.IntervalsOverlap(pair);
        bool established = tested
            ? tests.Any(t => t.Established && Favored(t) == other.Token)
            : o > e && overlap == false;
        if (!established && o - e < RootCauseEngine.AnomalyMinPoints - 1e-9) return null;

        var anomaly = New(ctx, prior, expected, other, tested, established);
        anomaly.ExpectedValue = e;
        anomaly.OtherValue = o;
        anomaly.Gap = o - e;
        anomaly.IntervalsOverlap = overlap;
        anomaly.Questions = Questions(ctx, expected, other);
        return anomaly;
    }

    /// <summary>
    /// The speed prior against the cheaper <paramref name="expected"/>: unexpected when a paired test of the pair is
    /// established and favors <paramref name="other"/>, or, without a test, when the 95% bootstrap interval of the time
    /// difference excludes zero with <paramref name="expected"/> slower; or when its median model time is at least
    /// <see cref="RootCauseEngine.AnomalyTimeRatio"/> times the other's.
    /// </summary>
    private static RootCauseAnomaly? Speed(RootCauseContext ctx, RootCauseModelInput expected, RootCauseModelInput other)
    {
        double e = expected.ModelTimeP50Ms!.Value, o = other.ModelTimeP50Ms!.Value;
        var pair = PairOf(expected.Token, other.Token);
        var tests = ctx.TestsFor(pair, RootCauseAxes.Speed);
        bool tested = tests.Any(t => t.NotTestedReason == null);
        var decomposition = ctx.SpeedPair(pair);
        bool established = tested
            ? tests.Any(t => t.Established && Favored(t) == other.Token)
            : decomposition is { DifferenceLower: double lo, DifferenceUpper: double hi } && (pair.First == expected.Token ? lo > 0 : hi < 0);
        double ratio = e / o;
        if (!established && ratio < RootCauseEngine.AnomalyTimeRatio - 1e-9) return null;

        var anomaly = New(ctx, RootCausePriors.PriceTierSpeed, expected, other, tested, established);
        anomaly.ExpectedValue = e;
        anomaly.OtherValue = o;
        anomaly.Gap = ratio;
        var lead = decomposition is { UnavailableReason: null, NotEstablished: false }
            ? decomposition.Components.Where(c => c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault()
            : null;
        if (lead != null)
        {
            anomaly.SpeedComponent = new RootCauseComponent
            {
                Key = lead.Key,
                Effect = lead.Effect,
                Label = lead.Label,
                Amount = lead.Amount,
                Share = lead.Share,
                ShareLower = lead.ShareLower,
                ShareUpper = lead.ShareUpper
            };
        }
        return anomaly;
    }

    /// <summary>An unexpected result with the figures every prior records: prices or release dates, confounds, graders and dimensions.</summary>
    private static RootCauseAnomaly New(RootCauseContext ctx, string prior, RootCauseModelInput expected, RootCauseModelInput other, bool tested, bool established)
    {
        bool release = prior == RootCausePriors.NewerRelease;
        return new RootCauseAnomaly
        {
            Prior = prior,
            Axis = RootCausePriors.Axis(prior),
            Expected = expected.Token,
            Other = other.Token,
            ExpectedOutputPrice = release ? null : OutputPrice(expected),
            OtherOutputPrice = release ? null : OutputPrice(other),
            ExpectedReleaseDate = release ? ctx.ReleaseDate(expected) : null,
            OtherReleaseDate = release ? ctx.ReleaseDate(other) : null,
            Tested = tested,
            Established = established,
            Confounds = RootCauseContext.ConfigDifferences(expected, other),
            Graders = Graders(expected, other),
            Dimensions = Dimensions(expected, other)
        };
    }

    /// <summary>Both models under each panel member alone and the reference reader; null when no grader index is recorded for either.</summary>
    private static RootCauseAnomalyGraders? Graders(RootCauseModelInput expected, RootCauseModelInput other)
    {
        if (new[] { expected.MemberAIndex, expected.MemberBIndex, expected.ReferenceReaderIndex, other.MemberAIndex, other.MemberBIndex, other.ReferenceReaderIndex }
            .All(v => !v.HasValue))
        {
            return null;
        }

        string? larger = null;
        if (expected.MemberAIndex is double ea && other.MemberAIndex is double oa && expected.MemberBIndex is double eb && other.MemberBIndex is double ob)
        {
            double splitA = Math.Abs(oa - ea), splitB = Math.Abs(ob - eb);
            larger = splitA > splitB + 1e-9 ? "A" : splitB > splitA + 1e-9 ? "B" : null;
        }
        return new RootCauseAnomalyGraders
        {
            ExpectedMemberA = expected.MemberAIndex,
            ExpectedMemberB = expected.MemberBIndex,
            ExpectedReader = expected.ReferenceReaderIndex,
            OtherMemberA = other.MemberAIndex,
            OtherMemberB = other.MemberBIndex,
            OtherReader = other.ReferenceReaderIndex,
            LargerSplit = larger
        };
    }

    /// <summary>The dimensions both models have, by the size of their difference, largest first, at most <see cref="MaxDimensions"/>.</summary>
    private static List<RootCauseAnomalyDimension> Dimensions(RootCauseModelInput expected, RootCauseModelInput other)
    {
        var rows = new List<(int Dimension, double Expected, double Other)>();
        for (int d = 0; d < DimensionNames.Length; d++)
        {
            if (d < expected.Dimensions.Count && expected.Dimensions[d] is double e && d < other.Dimensions.Count && other.Dimensions[d] is double o)
            {
                rows.Add((d, e, o));
            }
        }
        return rows
            .OrderByDescending(r => Math.Abs(r.Other - r.Expected))
            .ThenBy(r => r.Dimension)
            .Take(MaxDimensions)
            .Select(r => new RootCauseAnomalyDimension { Name = DimensionNames[r.Dimension], Expected = r.Expected, Other = r.Other })
            .ToList();
    }

    /// <summary>
    /// The questions on which <paramref name="other"/> scored furthest above <paramref name="expected"/>, at most
    /// <see cref="MaxQuestions"/>, with both scores and the median of the other analyzed models that scored them.
    /// </summary>
    private static List<RootCauseAnomalyQuestion> Questions(RootCauseContext ctx, RootCauseModelInput expected, RootCauseModelInput other)
    {
        var refs = ctx.Input.Questions.GroupBy(q => q.ItemKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var own = RootCauseContext.ItemMeans(expected);
        var theirs = RootCauseContext.ItemMeans(other);
        var rest = ctx.Models.Where(m => m.Token != expected.Token && m.Token != other.Token).Select(RootCauseContext.ItemMeans).ToList();
        return own.Keys
            .Where(theirs.ContainsKey)
            .Select(k => (Key: k, Expected: own[k], Other: theirs[k]))
            .Where(x => x.Other - x.Expected > 0)
            .OrderByDescending(x => x.Other - x.Expected)
            .ThenBy(x => refs.TryGetValue(x.Key, out var r) ? r.Number : int.MaxValue)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(MaxQuestions)
            .Select(x =>
            {
                var scores = rest.Where(m => m.ContainsKey(x.Key)).Select(m => m[x.Key]).ToList();
                return new RootCauseAnomalyQuestion
                {
                    Reference = refs.TryGetValue(x.Key, out var r) ? r.Reference : x.Key,
                    Expected = x.Expected,
                    Other = x.Other,
                    OthersMedian = scores.Count > 0 ? RootCauseStatistics.Median(scores) : null
                };
            })
            .ToList();
    }

    /// <summary>
    /// Each unexpected result's hypotheses: the keys of at most <see cref="MaxHypotheses"/> kept pair hypotheses of its two
    /// models on its axis, not ruled out, in <paramref name="hypotheses"/>' order (best first).
    /// </summary>
    internal static void AddHypotheses(IReadOnlyList<RootCauseAnomaly> anomalies, IReadOnlyList<RootCauseHypothesis> hypotheses)
    {
        foreach (var anomaly in anomalies)
        {
            anomaly.Hypotheses = hypotheses
                .Where(h => h.Kind == "pair" && h.Axis == anomaly.Axis && h.Grade != RootCauseGrades.NotSupported && h.Models.Count == 2
                            && h.Models.Contains(anomaly.Expected) && h.Models.Contains(anomaly.Other))
                .Take(MaxHypotheses)
                .Select(h => h.Key)
                .ToList();
        }
    }

    /// <summary>
    /// The result as one sentence, the display of its <c>rca.anomaly.&lt;n&gt;.summary</c> fact; <paramref name="name"/> names a
    /// model by token: <c>Model B scored 79, below the cheaper Model C at 81; not established, the intervals overlap.</c>
    /// </summary>
    public static string Summary(RootCauseAnomaly anomaly, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        ArgumentNullException.ThrowIfNull(name);
        string expected = name(anomaly.Expected), other = name(anomaly.Other);
        string body;
        if (anomaly.Prior == RootCausePriors.PriceTierSpeed)
        {
            body = expected + " was slower than the dearer " + other + ": a median model time of "
                   + BenchmarkReportFormat.Seconds(anomaly.ExpectedValue ?? 0) + " against " + BenchmarkReportFormat.Seconds(anomaly.OtherValue ?? 0);
        }
        else
        {
            var (e, o) = Scores(anomaly.ExpectedValue ?? 0, anomaly.OtherValue ?? 0);
            body = expected + " scored " + e + ", below the " + (anomaly.Prior == RootCausePriors.NewerRelease ? "older " : "cheaper ") + other + " at " + o;
        }
        return body + "; " + Ending(anomaly) + ".";
    }

    /// <summary>
    /// How the result stands, the end of its summary: <c>established by the paired test</c> or <c>not established by the
    /// paired test</c> for a tested pair; without a test, <c>established, the intervals do not overlap</c> (the speed prior:
    /// <c>established, the 95% interval of the time difference excludes zero</c>), <c>not established, the intervals
    /// overlap</c>, or <c>not established, and the pair had no paired test</c> when neither interval comparison applies.
    /// </summary>
    public static string Ending(RootCauseAnomaly anomaly)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        bool speed = anomaly.Prior == RootCausePriors.PriceTierSpeed;
        if (anomaly.Tested) return anomaly.Established ? "established by the paired test" : "not established by the paired test";
        if (anomaly.Established) return speed ? "established, the 95% interval of the time difference excludes zero" : "established, the intervals do not overlap";
        return !speed && anomaly.IntervalsOverlap == true ? "not established, the intervals overlap" : "not established, and the pair had no paired test";
    }

    /// <summary>The configuration differences in words: <c>none: only the model differs</c> without one.</summary>
    public static string ConfoundsText(RootCauseAnomaly anomaly)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        return anomaly.Confounds.Count == 0 ? "none: only the model differs" : string.Join("; ", anomaly.Confounds);
    }

    /// <summary>
    /// <c>under panel member A, Model B 78 and Model C 82; …; the two lie further apart under panel member A</c>: both models
    /// under each grader with an index for both; null when there is none.
    /// </summary>
    public static string? GradersText(RootCauseAnomaly anomaly, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        ArgumentNullException.ThrowIfNull(name);
        if (anomaly.Graders is not { } g) return null;
        var parts = new List<string>();
        void Part(string grader, double? expected, double? other)
        {
            if (expected is double e && other is double o)
            {
                var (es, os) = Scores(e, o);
                parts.Add("under " + grader + ", " + name(anomaly.Expected) + " " + es + " and " + name(anomaly.Other) + " " + os);
            }
        }
        Part("panel member A", g.ExpectedMemberA, g.OtherMemberA);
        Part("panel member B", g.ExpectedMemberB, g.OtherMemberB);
        Part("the reference reader", g.ExpectedReader, g.OtherReader);
        if (parts.Count == 0) return null;
        return string.Join("; ", parts) + (g.LargerSplit != null ? "; the two lie further apart under panel member " + g.LargerSplit : string.Empty);
    }

    /// <summary><c>the price-tier order of quality and the release order of quality</c>: the priors checked, in words.</summary>
    public static string PriorsCheckedText(IReadOnlyList<string> priors)
    {
        ArgumentNullException.ThrowIfNull(priors);
        return BenchmarkReportFormat.LetterList(priors.Select(RootCausePriors.Name).ToList());
    }

    /// <summary>Whether two of <paramref name="models"/> share a provider and differ in model id, the pairs the priors apply to.</summary>
    public static bool HasSameProviderPair(IReadOnlyList<RootCauseModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return models
            .Where(m => !string.IsNullOrWhiteSpace(m.Provider))
            .GroupBy(m => m.Provider.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Any(g => g.Select(m => (m.ModelId ?? string.Empty).Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() >= 2);
    }

    /// <summary>Why no prior could be checked: no two models of one provider, or none with the prices or release dates a prior needs.</summary>
    public static string NoPriorReason(IReadOnlyList<RootCauseModel> models)
        => HasSameProviderPair(models)
            ? "No two models of one provider have list output prices in different tiers, or release dates within one model line."
            : "No two models share a provider.";

    /// <summary>The pairs of <paramref name="anomalies"/> without a paired test, each counted once.</summary>
    public static int UntestedPairs(IReadOnlyList<RootCauseAnomaly> anomalies)
    {
        ArgumentNullException.ThrowIfNull(anomalies);
        return anomalies.Where(a => !a.Tested).Select(a => PairOf(a.Expected, a.Other).Key).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>Two scores as whole numbers, or with one decimal when the whole numbers would be equal.</summary>
    private static (string Expected, string Other) Scores(double expected, double other)
    {
        string e = BenchmarkReportFormat.Whole(expected), o = BenchmarkReportFormat.Whole(other);
        return e == o ? (BenchmarkReportFormat.OneDecimal(expected), BenchmarkReportFormat.OneDecimal(other)) : (e, o);
    }

    private static RootCausePair PairOf(string a, string b) => RootCauseContext.Precedes(a, b) ? new RootCausePair(a, b) : new RootCausePair(b, a);

    /// <summary>The token a paired test favors; null when it favors neither.</summary>
    private static string? Favored(RootCausePairTest test) => test.Favors switch
    {
        "First" => test.First,
        "Second" => test.Second,
        _ => null
    };

    /// <summary>The model's list output price, USD per million tokens; null when unknown or not positive.</summary>
    private static double? OutputPrice(RootCauseModelInput model)
    {
        var prices = model.Evidence.PricesPerMillion;
        return OutputPriceIndex >= 0 && OutputPriceIndex < prices.Length && prices[OutputPriceIndex] is double price && price > 0 ? price : null;
    }
}
