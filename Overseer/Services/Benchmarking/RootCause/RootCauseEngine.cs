namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
}

/// <summary>
/// The root-cause engine: one pure computation for every origin (comparison, per-model pack, run and
/// battery completion). It reads plain inputs (<see cref="RootCauseInput"/>), never the database, and the
/// same inputs always give the same <see cref="RootCauseResult"/>: bootstraps use a fixed seed and every
/// list has an explicit order.
/// </summary>
public static class RootCauseEngine
{
    /// <summary>The version stored on every result.</summary>
    public const int Version = 3;

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
        foreach (var pair in ctx.Pairs)
        {
            var first = ctx.Model(pair.First);
            var second = ctx.Model(pair.Second);
            var speed = ctx.SpeedPair(pair);
            if (speed != null) result.SpeedPairs.Add(speed);
            var cost = ctx.CostPair(pair);
            if (cost != null) result.CostPairs.Add(cost);
            if (first.QualityIndex is double qa && second.QualityIndex is double qb
                && RootCauseDecomposition.QualitySplit(pair.First, qa, first.Dimensions, pair.Second, qb, second.Dimensions, first.Evidence.Weights) is { } split)
            {
                result.QualitySplits.Add(split);
            }
        }

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

        var shared = ctx.SharedFailureQuestions();
        if (RootCauseHypotheses.SharedFailures(ctx, shared.Questions, shared.Models) is { } hq3) drafts.Add(hq3);

        foreach (var (model, axes) in ctx.WithinTargets())
        {
            drafts.AddRange(RootCauseHypotheses.WithinRules(ctx, model, axes, ctx.ControlCap(model)));
        }
        if (RootCauseHypotheses.SharedLeadGroupNote(ctx, drafts) is { } sharedGroupNote) result.Notes.Add(sharedGroupNote);

        var graded = drafts.Select(d => (Hypothesis: RootCauseHypotheses.Grade(d, ctx.Name), Draft: d)).ToList();
        var kept = RootCauseHypotheses.Keep(graded);
        kept = kept
            .OrderBy(k => RootCauseAxes.All.ToList().IndexOf(k.Hypothesis.Axis))
            .ThenBy(k => k.Hypothesis.Kind == "null" ? 0 : 1)
            .ThenByDescending(k => RootCauseGrades.Rank(k.Hypothesis.Grade))
            .ThenBy(k => k.Draft.PairNullStands == true ? 1 : 0)
            .ThenBy(k => k.Hypothesis.Explains.HasValue ? 0 : 1)
            .ThenByDescending(k => k.Hypothesis.Explains ?? 0)
            .ThenBy(k => k.Hypothesis.Id, StringComparer.Ordinal)
            .ThenBy(k => k.Hypothesis.Number)
            .ToList();
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (hypothesis, _) in kept)
        {
            int n = counters.GetValueOrDefault(hypothesis.Id) + 1;
            counters[hypothesis.Id] = n;
            hypothesis.Number = n;
            hypothesis.Key = hypothesis.Id + "." + n.ToString(CultureInfo.InvariantCulture);
            hypothesis.Label = RootCauseLabels.Of(hypothesis, ctx.Name);
        }
        result.Hypotheses = kept.Select(k => k.Hypothesis).ToList();
        result.Axes = RootCauseAxes.All.Select(axis => RootCauseHypotheses.AxisStatus(axis, kept)).ToList();

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
        result.Questions = RootCauseProfiles.Questions(ctx, result);
        result.GradeCap = RootCauseProfiles.GradeCap(ctx);

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
            result.CostRows.Add(CostRow(label, model.Letter, model.Token, "model", model.CostDegraded ? model.CostDegradedReason : model.Evidence.Priced ? null : RootCauseEvidence.NoPriceReason, model.Evidence.CostItems));
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
                subject.Evidence.CostItems.Where(group.Item).ToList()));
        }
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

    private static RootCauseChartRow CostRow(string label, string? letter, string? token, string kind, string? unavailable, IReadOnlyList<RootCauseItem> items)
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
        for (int k = 0; k < 5; k++)
        {
            string key = RootCauseSegments.CostComponents[k];
            row.Segments.Add(new RootCauseSegment { Key = key, Label = RootCauseSegments.Label(key), Value = items.Average(i => i.Cost[k]) });
        }
        row.Total = row.Segments.Sum(s => s.Value);
        return row;
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

    public IReadOnlyList<RootCauseControlItem> ControlItems { get; }

    public int ModelSpecificCount { get; }

    public int InstrumentSuspectCount { get; }

    /// <summary>Fewer than two qualifying controls (completion), or fewer than two comparable peers (per-model pack).</summary>
    public bool NeedsControlRun { get; }

    public RootCauseModelInput Model(string token) => _byToken[token];

    public RootCauseAtRisk AtRisk(string token) => _atRisk[token];

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
    /// 95 % bootstrap interval of the per-question difference excluding zero.
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

    public bool? PairedBootstrapExcludesZero(RootCausePair pair)
    {
        string peer = Model(pair.First).IsSubject ? pair.Second : pair.First;
        return Input.PairedDifferences.TryGetValue(peer, out var interval)
            ? BenchmarkReportFacts.PairedIntervalExcludesZero(interval.Lower, interval.Upper)
            : null;
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
        return single.Count == 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.Replicates, Status = RootCauseCheckStatuses.Passed, Detail = "Every model has at least two runs per suite." }
            : new RootCauseRobustnessCheck
            {
                Name = RootCauseHypotheses.Replicates,
                Status = RootCauseCheckStatuses.Failed,
                Detail = BenchmarkReportFormat.LetterList(single.Select(m => Name(m.Token)).ToList()) + (single.Count == 1 ? " has" : " have") + " a single run per suite."
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

        if (!model.Evidence.Panel || Contrast(a => a.MemberAQuality) is not double ca || Contrast(a => a.MemberBQuality) is not double cb)
        {
            return new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.NotAssessable, Detail = "The model was not graded by a panel, or a group has no score from both members." };
        }
        return Math.Sign(ca) == Math.Sign(cb) && ca != 0
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Passed, Detail = "Both panel members show the same contrast." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.PanelAgreement, Status = RootCauseCheckStatuses.Failed, Detail = "The panel members show different contrasts." };
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

    public RootCauseRobustnessCheck LeaveOneOutCost(RootCausePair pair, string component, string? effect)
    {
        var (a, b) = RootCauseDecomposition.Shared(Model(pair.First).Evidence.CostItems, Model(pair.Second).Evidence.CostItems);
        if (component == "all")
        {
            int which = effect == "price" ? 1 : 0;
            return LeaveOneOut(a.Count, which, drop =>
            {
                var sample = Enumerable.Range(0, a.Count).Where(i => i != drop).ToList();
                var (effects, total, _, _) = RootCauseDecomposition.CostEffects(a, b, sample);
                if (total == 0) return null;
                return new[] { effects.Where((_, i) => i % 2 == 0).Sum() / total, effects.Where((_, i) => i % 2 == 1).Sum() / total };
            });
        }

        int k = RootCauseSegments.CostComponents.ToList().IndexOf(component);
        return LeaveOneOut(a.Count, k, drop =>
        {
            var sample = Enumerable.Range(0, a.Count).Where(i => i != drop).ToList();
            var (effects, total, _, _) = RootCauseDecomposition.CostEffects(a, b, sample);
            return total == 0 ? null : Enumerable.Range(0, 5).Select(c => (effects[2 * c] + effects[2 * c + 1]) / total).ToArray();
        });
    }

    public RootCauseRobustnessCheck AcrossSuitesCost(RootCausePair pair, string component, string? effect)
    {
        var (a, b) = RootCauseDecomposition.Shared(Model(pair.First).Evidence.CostItems, Model(pair.Second).Evidence.CostItems);
        double Effect(IReadOnlyList<int>? sample)
        {
            var (effects, _, _, _) = RootCauseDecomposition.CostEffects(a, b, sample);
            if (component == "all") return effects.Where((_, i) => i % 2 == (effect == "price" ? 1 : 0)).Sum();
            int k = RootCauseSegments.CostComponents.ToList().IndexOf(component);
            return effects[2 * k] + effects[2 * k + 1];
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

        bool holds = worst[index] > 0 && worst[index] >= worst.Max();
        return holds
            ? new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The deciding share keeps its sign and stays the largest without the question that contributes most." }
            : new RootCauseRobustnessCheck { Name = RootCauseHypotheses.LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "Without one question the deciding share changes sign or another component is larger." };
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
    /// Every configuration key that differs between two models, for <c>pair.&lt;L&gt;.&lt;M&gt;.configDifferences</c>.
    /// The provider and the model are named as keys only, never by value, so the list names no model.
    /// </summary>
    public static List<string> ConfigDifferences(RootCauseModelInput a, RootCauseModelInput b)
    {
        var list = new List<string>();
        void Differs(string name, string? x, string? y)
        {
            if (!string.Equals((x ?? string.Empty).Trim(), (y ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                list.Add(name + " (" + (string.IsNullOrWhiteSpace(x) ? "not set" : x.Trim()) + " vs " + (string.IsNullOrWhiteSpace(y) ? "not set" : y.Trim()) + ")");
            }
        }
        void KeyDiffers(string name, string? x, string? y)
        {
            if (!string.Equals((x ?? string.Empty).Trim(), (y ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) list.Add(name);
        }
        KeyDiffers("provider", a.Provider, b.Provider);
        KeyDiffers("model", a.ModelId, b.ModelId);
        Differs("thinking level", a.ThinkingLevel, b.ThinkingLevel);
        Differs("reasoning mode", a.ReasoningMode, b.ReasoningMode);
        Differs("service tier", a.ServiceTier, b.ServiceTier);
        Differs("output token cap", a.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture), b.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture));
        Differs("parallel execution", a.ParallelMode, b.ParallelMode);
        Differs("harness", a.HarnessVersion, b.HarnessVersion);
        foreach (string key in a.DifferenceKeys.Concat(b.DifferenceKeys)
                     .Where(k => a.DifferenceKeys.Contains(k) != b.DifferenceKeys.Contains(k))
                     .Where(k => !BenchmarkCrossModelComparability.IsModelAxisKey(k))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(k => k, StringComparer.Ordinal))
        {
            list.Add(key);
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // Evidence summaries
    // ---------------------------------------------------------------------------------------------

    public RootCauseModelEvidenceSummary EvidenceSummary(RootCauseModelInput model)
    {
        var evidence = model.Evidence;
        var at = AtRisk(model.Token);
        var scored = evidence.Scored.ToList();
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
            summary.Cost = Enumerable.Range(0, 5).Select(k => new RootCauseSegment { Key = RootCauseSegments.CostComponents[k], Label = RootCauseSegments.Label(RootCauseSegments.CostComponents[k]), Value = evidence.CostItems.Average(i => i.Cost[k]) }).ToList();
            summary.Tokens = Enumerable.Range(0, 5).Select(k => new RootCauseSegment { Key = RootCauseSegments.CostComponents[k], Label = RootCauseSegments.Label(RootCauseSegments.CostComponents[k]), Value = evidence.CostItems.Average(i => i.Tokens[k]) }).ToList();
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
///   rca.axis.&lt;axis&gt;.status, rca.axis.&lt;axis&gt;.best           axis = intelligence, speed, cost
///   rca.known.&lt;n&gt;
///   rca.hypothesis.&lt;id&gt;.&lt;n&gt;.statement, .label, .axis, .models, .locus, .grade, .gradeReasons, .for, .against,
///     .refutedIf, .nextRun or .check, .share, .interval
///   rca.question.&lt;n&gt;.text, .model, .status, .hypotheses, .signals      n = 1 highest score, 2 lowest, 3 slowest, 4 most expensive
///   rca.profile.&lt;T&gt;.lostPointsOn, .weakestDimension, .lowestRank, .strength.&lt;n&gt;, .flag.&lt;n&gt;, .flags
///   rca.speed.&lt;T&gt;.&lt;T&gt;.total (a difference not established is stated, not decomposed);
///     rca.speed.&lt;T&gt;.&lt;T&gt;.&lt;component&gt;.share, .interval
///   rca.cost.&lt;T&gt;.&lt;T&gt;.total; rca.cost.&lt;T&gt;.&lt;T&gt;.&lt;component&gt;.&lt;volume|price&gt;.share (component all: every component)
///   rca.quality.&lt;T&gt;.&lt;T&gt;.total; rca.quality.&lt;T&gt;.&lt;T&gt;.dimension.&lt;d&gt;.points, .other.points
///   rca.atRisk.&lt;T&gt;.&lt;segment&gt;, rca.atRisk.&lt;T&gt;.total
///   rca.within.&lt;T&gt;.time.&lt;segment&gt;.seconds, .time.&lt;segment&gt;.share; rca.within.&lt;T&gt;.cost.&lt;component&gt;.share
///   rca.controls.count, .providers, .limit; rca.controls.&lt;n&gt;.model, .runId or .batteryRunId, .date, .provider, .sharedQuestions
///   rca.control.deficit.mean, .interval; rca.control.instrumentSuspect, rca.control.modelSpecific
///   rca.run.&lt;n&gt;.summary, rca.check.&lt;n&gt;, rca.systemFirst, rca.pricingBasis
/// </summary>
public static class RootCauseFacts
{
    public const string Prefix = "rca.";

    private static readonly Regex ModelKeyPrefix = new(@"^model\.[A-Z]+\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> EvidenceKeys = new(RootCauseEvidence.FactKeys, StringComparer.Ordinal);

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
            facts.Text(prefix + "axis", RootCauseAxes.Name(h.Axis));
            facts.Text(prefix + "models", h.ModelsText);
            facts.Text(prefix + "locus", h.Locus);
            facts.Add(prefix + "grade", h.Grade, RootCauseGrades.Display(h.Grade));
            facts.Text(prefix + "gradeReasons", h.GradeReasons.Count == 0 ? "every condition of the grade is met" : string.Join("; ", h.GradeReasons));
            facts.Text(prefix + "for", h.For.Count == 0 ? "none recorded" : string.Join(" ", h.For));
            facts.Text(prefix + "against", h.Against.Count == 0 ? "none recorded" : string.Join(" ", h.Against));
            facts.Text(prefix + "refutedIf", h.RefutedIf);
            if (h.NextRunNumber is int run) facts.Add(prefix + "nextRun", run, "next run " + Inv(run));
            else if (h.CheckNumber is int check) facts.Add(prefix + "check", check, "check " + Inv(check));
            if (h.Share is double share)
            {
                facts.Add(prefix + "share", share, RootCauseHypotheses.Pct(share));
                if (h.ShareLower is double lo && h.ShareUpper is double hi) facts.Text(prefix + "interval", RootCauseHypotheses.Interval(lo, hi));
            }
        }

        foreach (var pair in result.SpeedPairs)
        {
            string prefix = "rca.speed." + pair.First + "." + pair.Second + ".";
            string notDecomposed = pair.NotEstablished ? ", not established, so not decomposed" : string.Empty;
            if ((pair.UnavailableReason != null && !pair.NotEstablished) || pair.Difference is not double total)
            {
                facts.Unavailable(prefix + "total", pair.UnavailableReason ?? "Not computed.");
                continue;
            }
            facts.Add(prefix + "total", total / 1000.0,
                BenchmarkReportFormat.SignedOneDecimal(total / 1000.0) + " s per question (" + name(pair.First) + " minus " + name(pair.Second) + ", model time" + notDecomposed + ")");
            foreach (var c in pair.Components) AddShare(facts, prefix + c.Key, c);
        }

        foreach (var pair in result.CostPairs)
        {
            string prefix = "rca.cost." + pair.First + "." + pair.Second + ".";
            string notDecomposed = pair.NotEstablished ? ", not established, so not decomposed" : string.Empty;
            if ((pair.UnavailableReason != null && !pair.NotEstablished) || pair.Difference is not double total)
            {
                facts.Unavailable(prefix + "total", pair.UnavailableReason ?? "Not computed.");
                continue;
            }
            facts.Add(prefix + "total", total, Signed(BenchmarkReportFormat.Usd(Math.Abs(total)), total) + " per question (" + name(pair.First) + " minus " + name(pair.Second) + notDecomposed + ")");
            foreach (var c in pair.Components) AddShare(facts, prefix + c.Key + "." + c.Effect, c);
        }

        foreach (var question in result.Questions)
        {
            string prefix = "rca.question." + Inv(question.Number) + ".";
            facts.Text(prefix + "text", question.Text);
            facts.Add(prefix + "model", question.Token, name(question.Token));
            facts.Add(prefix + "status", question.Grade ?? RootCauseGrades.Word(null), RootCauseGrades.WordWithGrade(question.Grade));
            facts.Add(prefix + "hypotheses", question.HypothesisKeys.Count,
                question.HypothesisKeys.Count == 0 ? "none fired" : BenchmarkReportFormat.LetterList(question.HypothesisKeys));
            facts.Text(prefix + "signals", question.Signals.Count == 0 ? "none recorded" : string.Join("; ", question.Signals));
        }

        foreach (var profile in result.Profiles)
        {
            string prefix = "rca.profile." + profile.Token + ".";
            var lost = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.LostPointsOn);
            if (lost != null) facts.Text(prefix + "lostPointsOn", lost.Text);
            else facts.Unavailable(prefix + "lostPointsOn", "No scored answer fell below the model's median answer.");
            var weakest = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.WeakestDimension);
            if (weakest != null) facts.Text(prefix + "weakestDimension", weakest.Text);
            else facts.Unavailable(prefix + "weakestDimension", "No dimension is below the model's Accuracy.");
            var lowest = profile.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.LowestRank);
            if (lowest != null) facts.Text(prefix + "lowestRank", lowest.Text);
            for (int i = 0; i < profile.Strengths.Count; i++) facts.Text(prefix + "strength." + Inv(i + 1), profile.Strengths[i].Text);
            for (int i = 0; i < profile.Flags.Count; i++) facts.Text(prefix + "flag." + Inv(i + 1), profile.Flags[i].Text);
            facts.Add(prefix + "flags", profile.Flags.Count, profile.Flags.Count == 0 ? "none" : string.Join("; ", profile.Flags.Select(f => f.Text)));
        }

        foreach (var split in result.QualitySplits)
        {
            string prefix = "rca.quality." + split.First + "." + split.Second + ".";
            facts.Add(prefix + "total", split.IndexDifference,
                BenchmarkReportFormat.SignedOneDecimal(split.IndexDifference) + " index points (" + name(split.First) + " minus " + name(split.Second) + ")");
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
                foreach (var s in summary.AtRisk) facts.Add(prefix + s.Key, s.Value, BenchmarkReportFormat.OneDecimal(s.Value) + " index points at risk (upper bound, not a causal estimate)");
                double total = summary.AtRisk.Sum(s => s.Value);
                facts.Add(prefix + "total", total, BenchmarkReportFormat.OneDecimal(total) + " index points at risk against the model's median answer of "
                    + BenchmarkReportFormat.Whole(summary.MedianQuality.Value) + " (upper bound, not a causal estimate)");
            }

            string within = "rca.within." + summary.Token + ".";
            if (summary.TimeUnavailableReason != null)
            {
                facts.Unavailable(within + "time.total", summary.TimeUnavailableReason);
            }
            else
            {
                double total = summary.Time.Sum(s => s.Value);
                facts.Add(within + "time.total", total, BenchmarkReportFormat.OneDecimal(total) + " s per question, mean");
                foreach (var s in summary.Time)
                {
                    facts.Add(within + "time." + s.Key + ".seconds", s.Value, BenchmarkReportFormat.OneDecimal(s.Value) + " s per question, mean");
                    if (total > 0) facts.Add(within + "time." + s.Key + ".share", s.Value / total, RootCauseHypotheses.Pct(s.Value / total));
                }
            }

            if (summary.CostUnavailableReason != null)
            {
                facts.Unavailable(within + "cost.total", summary.CostUnavailableReason);
            }
            else
            {
                double total = summary.Cost.Sum(s => s.Value);
                facts.Add(within + "cost.total", total, BenchmarkReportFormat.Usd(total) + " per question, mean");
                foreach (var s in summary.Cost)
                {
                    if (total > 0) facts.Add(within + "cost." + s.Key + ".share", s.Value / total, RootCauseHypotheses.Pct(s.Value / total));
                }
            }
        }

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
                facts.Text(prefix + "model", control.ModelLabel);
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
        }
        foreach (var check in result.Checks) facts.Text("rca.check." + Inv(check.Number), check.Text);

        facts.Add("rca.systemFirst", result.SystemFirst, result.SystemFirst
            ? "a harness or suite hypothesis is graded Moderate or higher, so model-selection runs wait until it is settled"
            : "no harness or suite hypothesis holds model-selection runs back");
        facts.Text("rca.pricingBasis", string.IsNullOrWhiteSpace(result.PricingBasis) ? "not recorded" : result.PricingBasis);
    }

    private static void AddShare(BenchmarkReportFacts.FactList facts, string prefix, RootCauseComponent component)
    {
        if (component.Share is not double share)
        {
            facts.Unavailable(prefix + ".share", "The difference is zero.");
            return;
        }
        facts.Add(prefix + ".share", share, RootCauseHypotheses.Pct(share) + " of the difference");
        if (component.ShareLower is double lo && component.ShareUpper is double hi) facts.Text(prefix + ".interval", RootCauseHypotheses.Interval(lo, hi));
        else facts.Unavailable(prefix + ".interval", "No interval could be computed.");
    }

    private static string Signed(string text, double value) => value > 0 ? "+" + text : value < 0 ? "-" + text : text;

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The hypothesis labels: a noun phrase of at most <see cref="MaxWords"/> words naming what a rule measures
/// and its models, as the models are named in fact displays (<c>critical errors of Model F against Model E</c>).
/// </summary>
public static class RootCauseLabels
{
    public const int MaxWords = 10;

    private static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HQ1"] = "answers without tools",
        ["HQ2"] = "critical errors",
        ["HQ5"] = "the concise response style",
        ["HQ6"] = "guessing instead of abstaining",
        ["HQ7"] = "budget, truncation or output defects",
        ["HQ8"] = "the lower thinking level",
        ["HQ9"] = "verifier-cleared accuracy",
        ["HS1"] = "more model calls",
        ["HS2"] = "longer latency per call",
        ["HS3"] = "more output tokens",
        ["HS4"] = "slower decoding",
        ["HS5"] = "own waits and retries",
        ["HS6"] = "a served-tier mismatch",
        ["HC1"] = "higher token volume",
        ["HC2"] = "higher price per token",
        ["HC3"] = "reasoning tokens",
        ["HC4"] = "weak prompt caching",
        ["HC5"] = "tool rounds in the input tokens",
        ["WQ1"] = "answers without tools",
        ["WQ3"] = "critical errors",
        ["WQ4"] = "guessing instead of abstaining",
        ["WQ5"] = "budget, truncation or output defects",
        ["WQ6"] = "grader disagreement",
        ["WS1"] = "time to first token",
        ["WS2"] = "output generation",
        ["WS3"] = "tool rounds in the time",
        ["WS4"] = "tool execution time",
        ["WS5"] = "own waits and retries",
        ["WC1"] = "output cost",
        ["WC2"] = "uncached input cost",
        ["WC3"] = "tool rounds in the input cost"
    };

    /// <summary>The label of <paramref name="hypothesis"/>; <paramref name="name"/> names a model by token.</summary>
    public static string Of(RootCauseHypothesis hypothesis, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        ArgumentNullException.ThrowIfNull(name);

        string first = hypothesis.Models.Count > 0 ? name(hypothesis.Models[0]) : "the models";
        string? second = hypothesis.Models.Count > 1 ? name(hypothesis.Models[1]) : null;
        string full;
        string brief;
        switch (hypothesis.Id)
        {
            case "H0-Q":
            case "H0-S":
            case "H0-C":
                string axis = hypothesis.Id == "H0-S" ? "speed" : hypothesis.Id == "H0-C" ? "cost" : "intelligence";
                full = "no " + axis + " difference between " + first + " and " + (second ?? "the others");
                brief = "no " + axis + " difference for " + first;
                break;
            case "H0-W":
                full = brief = "no contrast within " + first + "'s own questions";
                break;
            case "HQ3":
                full = brief = "shared failures on the same questions";
                break;
            case "HQ4":
                full = "grader-dependent order of " + first + (second != null ? " and " + second : string.Empty);
                brief = "grader-dependent order of " + first;
                break;
            case "WQ2":
                const string LostIn = "Lost in ";
                string group = hypothesis.ShortStatement.StartsWith(LostIn, StringComparison.Ordinal)
                    ? hypothesis.ShortStatement[LostIn.Length..]
                    : "one group";
                full = brief = "points lost in " + group + " by " + first;
                break;
            default:
                string topic = Topics.TryGetValue(hypothesis.Id, out var known)
                    ? known
                    : RootCauseHypotheses.LowerFirst(hypothesis.ShortStatement.Split(':')[0].Trim());
                full = topic + " of " + first + (second != null && hypothesis.Kind == "pair" ? " against " + second : string.Empty);
                brief = topic + " of " + first;
                break;
        }

        string label = Words(full).Length <= MaxWords ? full : brief;
        var words = Words(label);
        return words.Length <= MaxWords ? label : string.Join(' ', words.Take(MaxWords));
    }

    /// <summary>The words of <paramref name="text"/>, split on whitespace.</summary>
    public static string[] Words(string text) => (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// The model profiles, the research questions and the grade cap of a result: each a measured count, share or
/// rank with its questions, built from the evidence the engine read. No entry states a cause.
/// </summary>
public static class RootCauseProfiles
{
    public const string Strength = "strength";
    public const string LostPointsOn = "lostPointsOn";
    public const string WeakestDimension = "weakestDimension";
    public const string LowestRank = "lowestRank";
    public const string CriticalErrors = "criticalErrors";
    public const string NoTool = "noTool";
    public const string PanelDisagreement = "panelDisagreement";
    public const string IsolatedMisses = "isolatedMisses";
    public const string OutputDefects = "outputDefects";

    public const int MaxStrengths = 2;
    public const int MaxWeaknesses = 3;

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
        double? panelMedian = Median(models
            .Where(m => m.Evidence.PanelAnswers is > 0)
            .Select(m => m.Evidence.PanelDisagreements.GetValueOrDefault() / (double)m.Evidence.PanelAnswers!.Value));
        var means = models.GroupBy(m => m.Token, StringComparer.Ordinal).ToDictionary(g => g.Key, g => RootCauseContext.ItemMeans(g.First()), StringComparer.Ordinal);

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
            profile.Strengths.AddRange(Strengths(models, model).Take(MaxStrengths));

            var weaknesses = new List<RootCauseProfileEntry>();
            if (Lost(ctx, model, refs) is { } lost) weaknesses.Add(lost);
            if (Weakest(model) is { } weakest) weaknesses.Add(weakest);
            if (Lowest(models, model) is { } lowest) weaknesses.Add(lowest);
            profile.Weaknesses.AddRange(weaknesses.Take(MaxWeaknesses));

            profile.Flags.AddRange(Flags(models, model, refs, noToolMedian, panelMedian, means));
            profiles.Add(profile);
        }
        return profiles;
    }

    /// <summary>
    /// The research questions whose axis has at least two comparable models: why the model with the highest
    /// Intelligence Index scored highest (1), why the lowest scored lowest (2), why the slowest by median model
    /// time is slowest (3) and why the most expensive per question is the most expensive (4).
    /// </summary>
    internal static List<RootCauseQuestion> Questions(RootCauseContext ctx, RootCauseResult result)
    {
        var models = ctx.Models;
        var quality = models.Where(m => m.QualityIndex.HasValue).OrderByDescending(m => m.QualityIndex!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        var speed = models.Where(m => !m.SpeedDegraded && m.ModelTimeP50Ms.HasValue).OrderBy(m => m.ModelTimeP50Ms!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();
        var cost = models.Where(m => !m.CostDegraded && m.CostPerQuestionUsd.HasValue).OrderBy(m => m.CostPerQuestionUsd!.Value).ThenBy(m => m.Token, StringComparer.Ordinal).ToList();

        var questions = new List<RootCauseQuestion>();
        void Add(int number, string axis, RootCauseModelInput model, string text)
        {
            // Question 1 asks why a model did well: the pair hypotheses where it is the better side. The others ask
            // why it did badly: the pair hypotheses where it is the worse side, and its own within-model ones.
            // result.Hypotheses is already in rank order.
            var hypotheses = result.Hypotheses
                .Where(h => h.Axis == axis && h.Id != "HQ3" && h.Models.Count > 0)
                .Where(h => number == 1
                    ? h.Kind == "pair" && h.Models.Count == 2 && h.Models[1] == model.Token
                    : (h.Kind == "pair" && h.Models.Count == 2 && h.Models[0] == model.Token) || (h.Kind == "within" && h.Models[0] == model.Token))
                .ToList();
            var best = hypotheses.FirstOrDefault(h => RootCauseGrades.Rank(h.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Low));
            questions.Add(new RootCauseQuestion
            {
                Number = number,
                Axis = axis,
                Token = model.Token,
                Text = text,
                Grade = best?.Grade,
                HypothesisKeys = hypotheses.Select(h => h.Key).ToList(),
                Signals = Signals(number, model, result)
            });
        }

        if (quality.Count >= 2)
        {
            Add(1, RootCauseAxes.Intelligence, quality[0], "Why did " + ctx.Name(quality[0].Token) + " score highest?");
            Add(2, RootCauseAxes.Intelligence, quality[^1], "Why did " + ctx.Name(quality[^1].Token) + " score lowest?");
        }
        if (speed.Count >= 2) Add(3, RootCauseAxes.Speed, speed[^1], "Why is " + ctx.Name(speed[^1].Token) + " slowest?");
        if (cost.Count >= 2) Add(4, RootCauseAxes.Cost, cost[^1], "Why is " + ctx.Name(cost[^1].Token) + " the most expensive?");
        return questions;
    }

    /// <summary>
    /// The highest grade the run counts allow: a model with a single run per suite fails the replicates check of
    /// every hypothesis about it, so none can be graded above Moderate. Null when every model has two or more.
    /// </summary>
    internal static string? GradeCap(RootCauseContext ctx)
    {
        var single = ctx.Models.Where(m => m.Evidence.RunsPerSuite < 2).ToList();
        if (single.Count == 0) return null;

        string cap = RootCauseGrades.WordWithGrade(RootCauseGrades.Moderate);
        if (single.Count == ctx.Models.Count)
        {
            return (ctx.Models.Count == 1 ? "One run per suite" : "One run per suite for every model")
                   + ": the replicates check fails for every hypothesis, so no cause can be graded above " + cap + ".";
        }
        return "One run per suite for " + BenchmarkReportFormat.LetterList(single.Select(m => ctx.Name(m.Token)).ToList())
               + ": the replicates check fails for every hypothesis about " + (single.Count == 1 ? "it" : "them")
               + ", so none of those can be graded above " + cap + ".";
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
            string place = (tied ? "joint " : string.Empty) + (rank == 1 ? "first" : "second") + " on " + DimensionNames[d];
            ranked.Add((rank, d, place));
        }
        return ranked.OrderBy(r => r.Rank).ThenBy(r => r.Dimension)
            .Select(r => new RootCauseProfileEntry
            {
                Kind = Strength,
                Text = r.Text + " of " + Inv(models.Count(m => DimensionOf(m, r.Dimension).HasValue)) + " models",
                Short = r.Text
            });
    }

    /// <summary>The largest segment of the model's index points at risk, with its points and the questions behind it.</summary>
    private static RootCauseProfileEntry? Lost(RootCauseContext ctx, RootCauseModelInput model, IReadOnlyDictionary<string, RootCauseQuestionRef> refs)
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
        string shortLabel = label;
        if (RootCauseSegments.AtRisk[segment] == RootCauseSegments.OtherLow && MostlyLost(model, at) is { } dimension)
        {
            shortLabel = "mostly " + dimension;
            label = "low-scoring answers, " + shortLabel;
        }
        string text = label + ": " + points + " index points on " + Inv(contributing.Count) + (contributing.Count == 1 ? " question" : " questions")
                      + Listed(listed, byContribution: true);
        return new RootCauseProfileEntry
        {
            Kind = LostPointsOn,
            Text = text,
            Short = shortLabel + ", " + points + " points",
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
            string place = (tied ? "joint last" : "last") + " on " + DimensionNames[d];
            return new RootCauseProfileEntry
            {
                Kind = LowestRank,
                Text = place + " of " + Inv(values.Count) + " models",
                Short = place
            };
        }
        return null;
    }

    /// <summary>
    /// What stands out in the model's answers: critical errors; answers without tools at a rate above the
    /// models' median; panel disagreement at twice the median rate or more; isolated misses; budget,
    /// tool-limit or output defects.
    /// </summary>
    private static IEnumerable<RootCauseProfileEntry> Flags(
        IReadOnlyList<RootCauseModelInput> models, RootCauseModelInput model, IReadOnlyDictionary<string, RootCauseQuestionRef> refs,
        double? noToolMedian, double? panelMedian, IReadOnlyDictionary<string, Dictionary<string, double>> means)
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
                Text = count + Listed(listed) + ", above the models' median of " + RootCauseHypotheses.Pct(median),
                Short = count,
                Questions = listed.Select(r => r.Reference).ToList()
            };
        }

        if (model.Evidence.PanelAnswers is > 0 && panelMedian is double panel && panel > 0)
        {
            double rate = model.Evidence.PanelDisagreements.GetValueOrDefault() / (double)model.Evidence.PanelAnswers.Value;
            if (rate >= 2 * panel)
            {
                yield return new RootCauseProfileEntry
                {
                    Kind = PanelDisagreement,
                    Text = "panel disagreement on " + RootCauseHypotheses.Pct(rate) + " of the answers both members graded, at least twice the models' median of "
                           + RootCauseHypotheses.Pct(panel),
                    Short = "panel disagreement " + RootCauseHypotheses.Pct(rate)
                };
            }
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
                    Text = count + Listed(listed) + ", scored 50 or less where the other models' median is 75 or more",
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

    /// <summary>The model's top measured signals for a research question.</summary>
    private static List<string> Signals(int number, RootCauseModelInput model, RootCauseResult result)
    {
        var profile = result.Profiles.FirstOrDefault(p => p.Token == model.Token);
        var evidence = result.Evidence.FirstOrDefault(e => e.Token == model.Token);
        var signals = new List<string>();
        string? lost = profile?.Weaknesses.FirstOrDefault(w => w.Kind == LostPointsOn)?.Text;
        string? weakest = profile?.Weaknesses.FirstOrDefault(w => w.Kind == WeakestDimension)?.Text;

        switch (number)
        {
            case 1:
                signals.AddRange(profile?.Strengths.Select(s => s.Text) ?? Enumerable.Empty<string>());
                break;
            case 2:
                if (lost != null) signals.Add("lost points on " + lost);
                if (weakest != null) signals.Add("weakest dimension " + weakest);
                signals.AddRange((profile?.Flags ?? new List<RootCauseProfileEntry>()).Take(2).Select(f => f.Text));
                break;
            case 3:
                if (model.ModelTimeP50Ms is double p50) signals.Add("median model time " + BenchmarkReportFormat.Seconds(p50));
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

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

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
