namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>Two analyzed models, the first earlier in letter order (or the subject).</summary>
public sealed record RootCausePair(string First, string Second)
{
    public string Key => First + "|" + Second;
}

/// <summary>A hypothesis under construction, before its grade.</summary>
internal sealed class RootCauseDraft
{
    public string Id { get; init; } = string.Empty;
    public string Axis { get; init; } = string.Empty;
    public string Kind { get; init; } = "pair";

    /// <summary>The pair's key, or <c>within:&lt;token&gt;</c>, or <c>all</c>: the group the keep rule ranks in.</summary>
    public string Group { get; init; } = string.Empty;

    public List<string> Models { get; init; } = new();
    public string Statement { get; set; } = string.Empty;
    public string Short { get; set; } = string.Empty;
    public List<string> Loci { get; init; } = new();
    public List<string> For { get; } = new();
    public List<string> Against { get; } = new();
    public string RefutedIf { get; set; } = string.Empty;
    public string ConfirmIf { get; set; } = string.Empty;

    public bool Quantified { get; set; } = true;
    public bool Established { get; set; }
    /// <summary>The grade reason when <see cref="Established"/> is false: the one condition that failed, as a plain clause.</summary>
    public string EstablishedText { get; set; } = "its result may be chance";
    public bool NotSupported { get; set; }
    public bool? PairNullStands { get; set; }
    public List<(string Cap, string Reason)> Caps { get; } = new();

    public double? Share { get; set; }
    public double? ShareLower { get; set; }
    public double? ShareUpper { get; set; }

    /// <summary>
    /// What <see cref="Share"/> is a share of, in the axis' own unit: the pair's gap (index points, ms or USD
    /// per question), or the model's points at risk, mean time or cost per question; null when the share is
    /// not a share of the axis' measure.
    /// </summary>
    public double? Scale { get; set; }

    /// <summary>
    /// The amount of <see cref="Scale"/> the hypothesis accounts for: its share, between zero and one, of it. A share
    /// pulling the other way accounts for none.
    /// </summary>
    public double? Explains => Share is double s && Scale is double g ? Math.Clamp(s, 0.0, 1.0) * Math.Abs(g) : null;

    public List<RootCauseRobustnessCheck> Robustness { get; } = new();
    public List<string> Confounds { get; } = new();

    /// <summary>The run kind that would settle it; null when none.</summary>
    public string? NextRunKind { get; set; }

    /// <summary>The check that would settle it, in words; null when none.</summary>
    public string? CheckText { get; set; }

    public string CheckKind { get; set; } = "rubric";
    public List<string> FactKeys { get; } = new();
}

/// <summary>
/// The hypothesis rule tables of § A4: the pair rules (HQ1–HQ9, HS1–HS6, HC1–HC5) between two models,
/// the within-model rules (WQ1–WQ6, WS1–WS5, WC1–WC3) of one model, the nulls H0-Q, H0-S, H0-C and
/// H0-W, the control limit, the robustness checks and the grading of § A2. Each rule fires on its
/// pre-declared condition and computes its evidence for and against; <see cref="Grade"/> turns the
/// evidence into a grade and <see cref="Keep"/> bounds the list. Pure and deterministic.
/// </summary>
public static class RootCauseHypotheses
{
    /// <summary>Hypotheses kept per axis and group (a pair, or one model's own rules).</summary>
    public const int KeepPerGroup = 3;

    /// <summary>
    /// Non-null hypotheses the rank cut keeps per axis at most; the keep rule then reserves at most one more for each
    /// research question on the axis.
    /// </summary>
    public const int MaxPerAxis = 12;

    /// <summary>A question scored below this is a miss.</summary>
    public const double MissBelow = 50.0;

    /// <summary>A question scored at least this is answered well.</summary>
    public const double WellAtLeast = 75.0;

    /// <summary>WQ2 fires only when the model's share of its points at risk in a group exceeds the other models' median share there by this much.</summary>
    public const double WithinGroupExcessOverPeers = 0.15;

    public const string LeaveOneOut = "Leave one question out";
    public const string AcrossSuites = "Across suites";
    public const string Replicates = "Replicates";
    public const string PanelAgreement = "Panel agreement";
    public const string Configuration = "Configuration";

    private static readonly string[] ThinkingOrder = { "none", "minimal", "low", "medium", "high", "xhigh", "max" };

    // ---------------------------------------------------------------------------------------------
    // Grade reasons: each one plain clause, the reason that applied
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reason of a hypothesis its own test refutes, before the first item against it.</summary>
    public const string RefutedReason = "its own test rules it out";

    /// <summary>The reason of a hypothesis no figure measures (graded Low).</summary>
    public const string QualitativeReason = "it is observed, not measured";

    /// <summary>
    /// The reason a pair hypothesis is capped at Low while the pair's null hypothesis stands; it takes the place of
    /// <see cref="PairTestReason"/>, which says the same.
    /// </summary>
    public const string NullStandsReason = "the gap between the two models may be chance, which caps the grade at Low (the pair's null hypothesis stands)";

    /// <summary>The reason of a pair hypothesis when no paired test of the pair is established.</summary>
    public const string PairTestReason = "no paired test shows that the gap between the two models is real";

    /// <summary>A share whose interval includes zero.</summary>
    private const string ShareIntervalReason = "the share may be zero (its 95% interval includes zero)";

    /// <summary>The cap of a null hypothesis that stands while a side ran once per suite.</summary>
    private const string OneRunCapReason = "with one run per suite, the tests can miss a real difference";

    /// <summary>The cap of a null hypothesis that stands while every side ran at least twice per suite.</summary>
    private const string NoProofCapReason = "failing to find a difference does not prove there is none";

    /// <summary>The cap of a critical-error hypothesis before scoring method 13.</summary>
    private const string UnconfirmedCapReason = "the critical errors are graders' flags, not confirmed errors";

    /// <summary><c>reason (grade capped at Low)</c>.</summary>
    public static string CapReason(string cap, string reason)
        => reason.TrimEnd('.') + " (grade capped at " + RootCauseGrades.Display(cap) + ")";

    /// <summary>
    /// The grade reason of a robustness check that failed or could not be made, as one plain clause: what the
    /// check found (<c>Model A ran only once per suite</c>, <c>the result depends on one question</c>), or that it
    /// is unknown, with the check's own detail in parentheses.
    /// </summary>
    public static string CheckReason(RootCauseRobustnessCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string detail = LowerFirst(check.Detail.Trim()).TrimEnd('.');
        if (check.Status == RootCauseCheckStatuses.Failed)
        {
            return check.Name switch
            {
                LeaveOneOut => "the result depends on one question (leave-one-out check)",
                _ => detail.Length > 0 ? detail : check.Name.ToLowerInvariant() + " check failed"
            };
        }

        string what = check.Name switch
        {
            LeaveOneOut => "the result depends on one question",
            AcrossSuites => "the effect holds in every suite",
            PanelAgreement => "both panel members agree",
            Replicates => "the result repeats",
            _ => "the " + check.Name.ToLowerInvariant() + " check holds"
        };
        return "it is unknown whether " + what + (detail.Length > 0 ? " (" + detail + ")" : string.Empty);
    }

    /// <summary>Whether <paramref name="reason"/> is the grade reason of one of <paramref name="hypothesis"/>' robustness checks.</summary>
    public static bool IsRobustnessReason(RootCauseHypothesis hypothesis, string reason)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        return hypothesis.Robustness.Any(r => r.Status is RootCauseCheckStatuses.Failed or RootCauseCheckStatuses.NotAssessable
                                              && string.Equals(CheckReason(r), reason, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // Grading
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The grade of § A2. Not supported when the rule's own test refutes it. Otherwise Low when
    /// qualitative only; else High with no condition, Moderate with exactly one, Low with two or more,
    /// where each robustness check that failed or could not be assessed is a condition, and so is a test
    /// (or share interval) not established. A pair whose null stands caps it at Low, and each other cap
    /// (the control limit, a rule's own ceiling) applies with its reason.
    /// </summary>
    internal static RootCauseHypothesis Grade(RootCauseDraft draft, Func<string, string> name)
    {
        var reasons = new List<string>();
        string grade;

        if (draft.NotSupported)
        {
            grade = RootCauseGrades.NotSupported;
            reasons.Add(RefutedReason + (draft.Against.Count > 0 ? ": " + LowerFirst(draft.Against[0]).TrimEnd('.') : string.Empty));
        }
        else if (!draft.Quantified)
        {
            grade = RootCauseGrades.Low;
            reasons.Add(QualitativeReason);
        }
        else
        {
            foreach (var check in draft.Robustness)
            {
                if (check.Status is RootCauseCheckStatuses.Failed or RootCauseCheckStatuses.NotAssessable) reasons.Add(CheckReason(check));
            }
            if (!draft.Established) reasons.Add(draft.EstablishedText);
            grade = reasons.Count == 0 ? RootCauseGrades.High : reasons.Count == 1 ? RootCauseGrades.Moderate : RootCauseGrades.Low;
        }

        if (grade != RootCauseGrades.NotSupported)
        {
            if (draft.PairNullStands == true && RootCauseGrades.Rank(grade) > RootCauseGrades.Rank(RootCauseGrades.Low))
            {
                grade = RootCauseGrades.Low;
                reasons.Remove(PairTestReason);
                reasons.Add(NullStandsReason);
            }
            foreach (var (cap, reason) in draft.Caps)
            {
                if (RootCauseGrades.Rank(grade) > RootCauseGrades.Rank(cap))
                {
                    grade = cap;
                    reasons.Add(CapReason(cap, reason));
                }
            }
        }

        return new RootCauseHypothesis
        {
            Id = draft.Id,
            Axis = draft.Axis,
            Kind = draft.Kind,
            Models = draft.Models.ToList(),
            ModelsText = BenchmarkReportFormat.LetterList(draft.Models.Select(name).ToList()),
            Statement = draft.Statement,
            ShortStatement = draft.Short,
            Loci = draft.Loci.ToList(),
            Locus = draft.Loci.Count == 0 ? "—" : string.Join(" / ", draft.Loci),
            For = draft.For.ToList(),
            Against = draft.Against.ToList(),
            RefutedIf = draft.RefutedIf,
            Grade = grade,
            GradeReasons = reasons,
            Share = draft.Share,
            ShareLower = draft.ShareLower,
            ShareUpper = draft.ShareUpper,
            Explains = draft.Explains,
            Robustness = draft.Robustness.ToList(),
            Confounds = draft.Confounds.ToList(),
            FactKeys = draft.FactKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList()
        };
    }

    /// <summary>The hypotheses kept, with no research question to reserve a hypothesis for.</summary>
    internal static List<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> Keep(IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> graded)
        => Keep(graded, Array.Empty<RootCauseQuestionTarget>(), (_, _) => false);

    /// <summary>
    /// The hypotheses kept: every null, then per axis and group the strongest <see cref="KeepPerGroup"/>
    /// non-null hypotheses (in <see cref="Ranked"/> order), and at most <see cref="MaxPerAxis"/> per axis.
    /// A research question of <paramref name="targets"/> that no kept comparative hypothesis graded Low or higher
    /// answers (as <paramref name="answers"/> decides) then has the best comparative one that does reserved for
    /// it, beyond <see cref="MaxPerAxis"/>; with none, and no kept within-model one either, the best within-model
    /// one. Each in <see cref="Ranked"/> order, at most one per question. Each numbered within its rule.
    /// </summary>
    internal static List<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> Keep(
        IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> graded,
        IReadOnlyList<RootCauseQuestionTarget> targets,
        Func<RootCauseQuestionTarget, RootCauseHypothesis, bool> answers)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(answers);
        int lowRank = RootCauseGrades.Rank(RootCauseGrades.Low);
        var kept = new List<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)>();
        foreach (string axis in RootCauseAxes.All)
        {
            var nulls = graded.Where(g => g.Hypothesis.Axis == axis && g.Hypothesis.Kind == "null").ToList();
            var ranked = graded
                .Where(g => g.Hypothesis.Axis == axis && g.Hypothesis.Kind != "null")
                .GroupBy(g => g.Draft.Group, StringComparer.Ordinal)
                .SelectMany(group => Ranked(group).Take(KeepPerGroup));
            kept.AddRange(nulls);
            kept.AddRange(Ranked(ranked).Take(MaxPerAxis));

            foreach (var target in targets.Where(t => t.Axis == axis))
            {
                bool Supported((RootCauseHypothesis Hypothesis, RootCauseDraft Draft) g)
                    => RootCauseGrades.Rank(g.Hypothesis.Grade) >= lowRank && answers(target, g.Hypothesis);

                if (kept.Any(k => k.Hypothesis.Kind == "pair" && Supported(k))) continue;
                var candidates = graded
                    .Where(g => g.Hypothesis.Axis == axis && g.Hypothesis.Kind != "null" && Supported(g))
                    .ToList();
                var comparative = Ranked(candidates.Where(g => g.Hypothesis.Kind == "pair")).FirstOrDefault();
                if (comparative.Hypothesis != null)
                {
                    kept.Add(comparative);
                    continue;
                }
                if (kept.Any(Supported)) continue;
                var within = Ranked(candidates.Where(g => g.Hypothesis.Kind != "pair")).FirstOrDefault();
                if (within.Hypothesis != null) kept.Add(within);
            }
        }

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (hypothesis, _) in kept)
        {
            int n = counters.GetValueOrDefault(hypothesis.Id) + 1;
            counters[hypothesis.Id] = n;
            hypothesis.Number = n;
            hypothesis.Key = hypothesis.Id + "." + n.ToString(CultureInfo.InvariantCulture);
        }
        return kept;
    }

    /// <summary>
    /// The ranking of hypotheses: by grade; then a pair whose null is rejected before one whose null stands;
    /// then by the amount explained in the axis' own unit (none last); then by rule id and group.
    /// </summary>
    internal static IOrderedEnumerable<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> Ranked(IEnumerable<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> items)
        => items
            .OrderByDescending(g => RootCauseGrades.Rank(g.Hypothesis.Grade))
            .ThenBy(g => g.Draft.PairNullStands == true ? 1 : 0)
            .ThenBy(g => g.Hypothesis.Explains.HasValue ? 0 : 1)
            .ThenByDescending(g => g.Hypothesis.Explains ?? 0)
            .ThenBy(g => g.Hypothesis.Id, StringComparer.Ordinal)
            .ThenBy(g => g.Draft.Group, StringComparer.Ordinal);

    /// <summary>The reason of an axis whose only non-null hypotheses are within-model ones in a document with pairs.</summary>
    public const string WithinOnlyAxisReason = "Only hypotheses about one model's own breakdown fired on this axis; none compares the models.";

    /// <summary>The status of an axis in a document without pairs (a run or battery completion).</summary>
    internal static RootCauseAxisStatus AxisStatus(string axis, IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> kept)
        => AxisStatus(axis, kept, hasPairs: false);

    /// <summary>
    /// The status of an axis (§ A2): established when a non-null hypothesis is High and its null is
    /// rejected; probable when the best is Moderate; otherwise not established. With
    /// <paramref name="hasPairs"/> (a comparison or a per-model pack), only pair hypotheses count, and an axis
    /// with within-model hypotheses alone is not established. The reason names the best hypothesis' grade and
    /// the grade reasons that applied to it, each one clause.
    /// </summary>
    internal static RootCauseAxisStatus AxisStatus(string axis, IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> kept, bool hasPairs)
    {
        var nonNull = kept
            .Where(k => k.Hypothesis.Axis == axis && k.Hypothesis.Kind != "null")
            .ToList();
        var candidates = hasPairs ? nonNull.Where(k => k.Hypothesis.Kind == "pair").ToList() : nonNull;
        var best = candidates.FirstOrDefault();
        if (best.Hypothesis == null)
        {
            return new RootCauseAxisStatus
            {
                Axis = axis,
                Status = RootCauseStatuses.NotEstablished,
                Reason = nonNull.Count > 0 ? WithinOnlyAxisReason : "No rule found a possible cause on this axis."
            };
        }

        var established = candidates.FirstOrDefault(k => k.Hypothesis.Grade == RootCauseGrades.High && k.Draft.PairNullStands != true);
        if (established.Hypothesis != null)
        {
            return new RootCauseAxisStatus
            {
                Axis = axis,
                Status = RootCauseStatuses.Established,
                BestHypothesis = established.Hypothesis.Key,
                Reason = RootCauseGrades.WordWithGrade(RootCauseGrades.High) + ": it passed every check"
                         + (established.Draft.PairNullStands == false ? ", and a real difference between the two models was shown." : ".")
            };
        }

        return new RootCauseAxisStatus
        {
            Axis = axis,
            Status = best.Hypothesis.Grade == RootCauseGrades.Moderate ? RootCauseStatuses.Probable : RootCauseStatuses.NotEstablished,
            BestHypothesis = best.Hypothesis.Key,
            Reason = RootCauseGrades.WordWithGrade(best.Hypothesis.Grade)
                     + (best.Hypothesis.GradeReasons.Count > 0 ? " because " + ReasonList(best.Hypothesis.GradeReasons) : string.Empty) + "."
        };
    }

    /// <summary><c>A</c>; <c>A, and B</c>; <c>A; B; and C</c>: clauses that may hold commas of their own.</summary>
    internal static string ReasonList(IReadOnlyList<string> reasons)
    {
        var clauses = reasons.Select(r => r.Trim().TrimEnd('.')).Where(r => r.Length > 0).ToList();
        return clauses.Count switch
        {
            0 => string.Empty,
            1 => clauses[0],
            2 => clauses[0] + ", and " + clauses[1],
            _ => string.Join("; ", clauses.Take(clauses.Count - 1)) + "; and " + clauses[^1]
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Nulls
    // ---------------------------------------------------------------------------------------------

    /// <summary>What a replicate decides about a pair null that stands.</summary>
    public const string StandingNullConfirmIf = "the paired test still finds no difference with three runs per model";

    /// <summary>
    /// H0-Q, H0-S or H0-C for a pair: the difference is within sampling noise. Rejected (graded Not
    /// supported) when a paired test of the pair on the axis is established after its family's
    /// adjustment, or, where no family test exists, when the 95% bootstrap interval of the difference
    /// excludes zero (on the Intelligence Index, of the per-question difference: the per-model sheet's
    /// interval, else <see cref="RootCauseContext.PairedBootstrapExcludesZero"/>'s). When it stands it is
    /// graded Moderate, or Low when either side has a single run per suite.
    /// </summary>
    internal static RootCauseDraft PairNull(RootCauseContext ctx, RootCausePair pair, string axis, out bool stands)
    {
        string id = axis switch { RootCauseAxes.Speed => "H0-S", RootCauseAxes.Cost => "H0-C", _ => "H0-Q" };
        string measure = axis switch { RootCauseAxes.Speed => "model time", RootCauseAxes.Cost => "cost per question", _ => "Intelligence Index" };
        var draft = new RootCauseDraft
        {
            Id = id,
            Axis = axis,
            Kind = "null",
            Group = pair.Key,
            Models = { pair.First, pair.Second },
            Statement = "The " + measure + " difference between " + ctx.Name(pair.First) + " and " + ctx.Name(pair.Second) + " is within sampling noise.",
            Short = "Noise: " + ctx.Name(pair.First) + " vs " + ctx.Name(pair.Second),
            RefutedIf = "A paired test of the two on the " + measure + " is established after adjustment.",
            ConfirmIf = StandingNullConfirmIf
        };

        bool rejected = false;
        var tests = ctx.TestsFor(pair, axis);
        foreach (var test in tests)
        {
            if (test.FactKey != null) draft.FactKeys.Add(test.FactKey);
            if (test.Established && test.Favors != "None")
            {
                rejected = true;
                draft.Against.Add("A paired " + measure + " test is established" + FamilyText(test) + ".");
            }
            else if (test.NotTestedReason == null)
            {
                draft.For.Add("The paired " + measure + " test is not established" + FamilyText(test) + ".");
            }
        }

        if (tests.Count == 0 || tests.All(t => t.NotTestedReason != null))
        {
            var decomposition = axis == RootCauseAxes.Speed ? ctx.SpeedPair(pair) : axis == RootCauseAxes.Cost ? ctx.CostPair(pair) : null;
            if (decomposition?.DifferenceLower is double lo && decomposition.DifferenceUpper is double hi)
            {
                bool excludes = lo > 0 || hi < 0;
                if (excludes)
                {
                    rejected = true;
                    draft.Against.Add("No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).");
                }
                else
                {
                    draft.For.Add("No family test exists; the 95% bootstrap interval of the per-question difference includes zero.");
                }
            }
            else if (axis == RootCauseAxes.Intelligence && ctx.PairedBootstrapExcludesZero(pair) is bool pairedExcludes)
            {
                bool sheet = ctx.SheetPairedDifference(pair) != null;
                if (pairedExcludes)
                {
                    rejected = true;
                    draft.Against.Add(sheet
                        ? "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted)."
                        : "No family test covers this pair; the 95% paired-bootstrap interval of the per-question difference excludes zero (not adjusted).");
                }
                else
                {
                    draft.For.Add(sheet
                        ? "The subject's paired-bootstrap interval against this peer includes zero."
                        : "No family test covers this pair; the 95% paired-bootstrap interval of the per-question difference includes zero.");
                }
            }
            else
            {
                draft.For.Add("No paired test could be made on this axis.");
            }
        }

        if (axis == RootCauseAxes.Intelligence && ctx.IntervalsOverlap(pair) is bool overlap)
        {
            (overlap ? draft.For : draft.Against).Add(overlap ? "The two 95% intervals overlap." : "The two 95% intervals do not overlap.");
        }

        stands = !rejected;
        if (rejected)
        {
            draft.NotSupported = true;
        }
        else
        {
            draft.Quantified = true;
            draft.Established = true;
            if (ctx.Model(pair.First).Evidence.RunsPerSuite < 2 || ctx.Model(pair.Second).Evidence.RunsPerSuite < 2)
            {
                draft.Caps.Add((RootCauseGrades.Low, OneRunCapReason));
            }
            else
            {
                draft.Caps.Add((RootCauseGrades.Moderate, NoProofCapReason));
            }
        }
        return draft;
    }

    private static string FamilyText(RootCausePairTest test)
        => test.FamilySize > 1
            ? " after the Holm adjustment across " + Inv(test.FamilySize) + " tests"
            : test.FamilySize == 1 ? " as a single test" : string.Empty;

    // ---------------------------------------------------------------------------------------------
    // Pair rules
    // ---------------------------------------------------------------------------------------------

    /// <summary>The pair rules of § A4 for one pair, every axis the two are comparable on.</summary>
    internal static List<RootCauseDraft> PairRules(RootCauseContext ctx, RootCausePair pair, IReadOnlyDictionary<string, bool> nullStands)
    {
        var drafts = new List<RootCauseDraft>();
        var first = ctx.Model(pair.First);
        var second = ctx.Model(pair.Second);

        if (first.QualityIndex is double qa && second.QualityIndex is double qb && qa != qb)
        {
            bool stands = nullStands.GetValueOrDefault(pair.Key + "|" + RootCauseAxes.Intelligence, true);
            var (worse, better) = qa < qb ? (first, second) : (second, first);
            double gap = Math.Abs(qa - qb);
            drafts.AddRange(Scaled(IntelligencePairRules(ctx, pair, worse, better, gap, stands), gap));
        }

        if (!first.SpeedDegraded && !second.SpeedDegraded && ctx.SpeedPair(pair) is { UnavailableReason: null, Difference: double dt } speed && dt != 0)
        {
            bool stands = nullStands.GetValueOrDefault(pair.Key + "|" + RootCauseAxes.Speed, true);
            var (worse, better) = dt > 0 ? (first, second) : (second, first);
            drafts.AddRange(Scaled(SpeedPairRules(ctx, pair, speed, worse, better, stands), Math.Abs(dt)));
        }

        if (!first.CostDegraded && !second.CostDegraded && ctx.CostPair(pair) is { UnavailableReason: null, Difference: double dc } cost && dc != 0)
        {
            bool stands = nullStands.GetValueOrDefault(pair.Key + "|" + RootCauseAxes.Cost, true);
            var (worse, better) = dc > 0 ? (first, second) : (second, first);
            drafts.AddRange(Scaled(CostPairRules(ctx, pair, cost, worse, better, stands), Math.Abs(dc)));
        }

        return drafts;
    }

    /// <summary>The drafts with <paramref name="scale"/> as what their shares are a share of.</summary>
    private static IEnumerable<RootCauseDraft> Scaled(IEnumerable<RootCauseDraft> drafts, double? scale)
    {
        foreach (var draft in drafts)
        {
            draft.Scale = scale;
            yield return draft;
        }
    }

    private static RootCauseDraft PairDraft(RootCauseContext ctx, RootCausePair pair, string id, string axis, RootCauseModelInput worse, RootCauseModelInput better,
        string statement, string shortText, string refutedIf, string confirmIf, bool nullStands, params string[] loci)
    {
        var draft = new RootCauseDraft
        {
            Id = id,
            Axis = axis,
            Kind = "pair",
            Group = pair.Key,
            Models = { worse.Token, better.Token },
            Statement = statement,
            Short = shortText,
            RefutedIf = refutedIf,
            ConfirmIf = confirmIf,
            PairNullStands = nullStands
        };
        draft.Loci.AddRange(loci);
        return draft;
    }

    private static IEnumerable<RootCauseDraft> IntelligencePairRules(
        RootCauseContext ctx, RootCausePair pair, RootCauseModelInput a, RootCauseModelInput b, double gap, bool stands)
    {
        string A = ctx.Name(a.Token), B = ctx.Name(b.Token);
        var atA = ctx.AtRisk(a.Token);
        var atB = ctx.AtRisk(b.Token);
        var scoredA = a.Evidence.Scored.ToList();
        var scoredB = b.Evidence.Scored.ToList();
        var intelligenceTests = ctx.TestsFor(pair, RootCauseAxes.Intelligence);
        bool testEstablished = intelligenceTests.Any(t => t.Established && t.Favors != "None")
                               || (intelligenceTests.All(t => t.NotTestedReason != null) && ctx.PairedBootstrapExcludesZero(pair) == true);
        string qualityKey = ctx.QualityFactKey(pair);
        bool sameModel = SameModelAtTwoLevels(a, b);

        // Of the same model at two levels, the thinking level is what the pair compares, not a confound.
        void Common(RootCauseDraft d, bool intelligenceConfound = true)
        {
            d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            d.Robustness.Add(ctx.PanelAgreementCheck(a, b));
            var confounds = ctx.Confounds(a, b, RootCauseAxes.Intelligence, skipThinking: !intelligenceConfound || sameModel);
            d.Confounds.AddRange(confounds);
            d.Robustness.Add(ConfigurationCheck(confounds, ModelDifferences(a, b)));
            if (sameModel) d.For.Add(SameModelLine(A, B, a, b, confounds.Count > 0));
            d.FactKeys.Add(qualityKey);
        }

        // HQ1: answers without tools.
        if (scoredA.Count > 0 && scoredB.Count > 0)
        {
            int noA = scoredA.Count(x => x.NoTool), noB = scoredB.Count(x => x.NoTool);
            double rateA = noA / (double)scoredA.Count, rateB = noB / (double)scoredB.Count;
            if (rateA > rateB)
            {
                var d = PairDraft(ctx, pair, "HQ1", RootCauseAxes.Intelligence, a, b,
                    A + " scored lower than " + B + " because it answered without tools more often.",
                    "Fewer tool calls: " + A,
                    "Answers without tools score as well as those with tools, or the rate differs by fewer than two answers.",
                    "the without-tool answers still score lower and stay more frequent",
                    stands, RootCauseLoci.Model, RootCauseLoci.Chat);
                d.For.Add(A + " answered " + Inv(noA) + " of " + Inv(scoredA.Count) + " scored questions without a tool call (" + Pct(rateA) + "); "
                          + B + " " + Inv(noB) + " of " + Inv(scoredB.Count) + " (" + Pct(rateB) + ").");
                var withA = scoredA.Where(x => !x.NoTool).Select(x => x.Quality!.Value).ToList();
                var withoutA = scoredA.Where(x => x.NoTool).Select(x => x.Quality!.Value).ToList();
                double extra = noA - rateB * scoredA.Count;
                var mw = withA.Count > 0 && withoutA.Count > 0 ? RootCauseStatistics.MannWhitney(withoutA, withA) : null;
                if (withA.Count > 0 && withoutA.Count > 0)
                {
                    string line = A + "'s answers without tools scored " + Whole(withoutA.Average()) + ", with tools " + Whole(withA.Average()) + ".";
                    (withoutA.Average() < withA.Average() ? d.For : d.Against).Add(line);
                }
                if (withA.Count == 0 || withoutA.Count == 0 || withoutA.Average() >= withA.Average() || extra < 2)
                {
                    d.NotSupported = true;
                    if (extra < 2) d.Against.Add("The rate differs by fewer than two answers.");
                    else if (withA.Count == 0 || withoutA.Count == 0) d.Against.Add("There is no contrast between answers with and without tools.");
                }
                double points = atA.Points[0] - atB.Points[0];
                if (points <= 0)
                {
                    d.NotSupported = true;
                    d.Against.Add("Answers without tools put no more of " + A + "'s index points at risk than of " + B + "'s.");
                }
                d.Share = gap > 0 ? points / gap : null;
                d.For.Add("Answers without tools put " + OneDecimal(atA.Points[0]) + " index points of " + A + " at risk (upper bound), " + OneDecimal(atB.Points[0]) + " of " + B + "'s.");
                bool contrast = mw != null && mw.PValue < RootCauseStatistics.SignificanceLevel;
                d.Established = testEstablished && contrast;
                d.EstablishedText = Both(
                    testEstablished ? null : PairTestReason,
                    contrast ? null : "answers without tools do not score clearly lower than answers with tools");
                d.Robustness.Add(LeaveOneOutAtRisk(atA, atB, 0));
                Common(d);
                d.FactKeys.Add(ctx.Key(a.Token, "profile.noToolAnswers"));
                d.NextRunKind = RootCauseRunKinds.Replicate;
                yield return d;
            }
        }

        // HQ2: confirmed critical errors.
        if (atA.Points[2] > 0 && atA.Points[2] >= 0.5 * gap)
        {
            var d = PairDraft(ctx, pair, "HQ2", RootCauseAxes.Intelligence, a, b,
                A + "'s shortfall against " + B + " is concentrated in confirmed critical errors.",
                "Critical errors: " + A,
                "The caps are unresolved or overturned, or the shortfall is spread across many questions.",
                "the critical-error points at risk stay at least half the gap",
                stands, RootCauseLoci.Model);
            int critical = scoredA.Count(x => x.CriticalError);
            d.For.Add("Confirmed critical errors put " + OneDecimal(atA.Points[2]) + " index points of " + A + " at risk (upper bound), against a gap of " + OneDecimal(gap) + " points.");
            d.For.Add(A + " has " + Inv(critical) + " critical " + (critical == 1 ? "error" : "errors") + " in " + Inv(scoredA.Count) + " scored answers.");
            if (!a.Evidence.CriticalErrorsConfirmed)
            {
                d.Against.Add("Before scoring method 13 the caps are graders' flags, not confirmed errors.");
                d.Caps.Add((RootCauseGrades.Low, UnconfirmedCapReason));
            }
            if (critical < 2) d.Against.Add("A single critical error carries the whole effect.");
            d.Share = gap > 0 ? atA.Points[2] / gap : null;
            d.Established = testEstablished;
            d.EstablishedText = PairTestReason;
            d.Robustness.Add(LeaveOneOutThreshold(atA, 2, 0.5 * gap));
            Common(d);
            d.FactKeys.Add(ctx.Key(a.Token, "errors.critical"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // HQ4: graders. Only a scoring grader (a panel member, or the assessor of a single-assessor run) that shares
        // a provider with exactly one of the two can favor one side; the readers and the verifier score nothing.
        bool judgeDependent = ctx.JudgeDependent(pair);
        var sharedGrader = ctx.Graders
            .Where(g => ScoringRoles.Contains(g.Role.Trim(), StringComparer.Ordinal)
                        && SameProvider(g.Provider, a.Provider) != SameProvider(g.Provider, b.Provider))
            .ToList();
        var highDisagreement = RootCauseProfiles.PanelDisagreementOf(ctx.Models, a);
        var family = MemberFamilyGap(ctx, a, b);
        if (judgeDependent || sharedGrader.Count > 0 || highDisagreement != null || family != null)
        {
            var d = PairDraft(ctx, pair, "HQ4", RootCauseAxes.Intelligence, a, b,
                "The order of " + A + " and " + B + " depends on the graders.",
                "Grader-dependent order",
                "Both panel members rank the two alike and the reference reader agrees.",
                "a panel from two other providers reverses or erases the gap",
                stands, RootCauseLoci.Harness);
            if (judgeDependent) d.For.Add("The panel members order the two differently (a judge-dependent pair).");
            foreach (var grader in sharedGrader) d.For.Add(Capitalize(grader.Role.Trim()) + " shares a provider with " + (SameProvider(grader.Provider, a.Provider) ? A : B) + ".");
            if (highDisagreement is { } panel)
            {
                d.For.Add(A + " has " + BenchmarkNotableFindings.DisagreementPhrase(panel.Rate, panel.OthersMedian) + ".");
            }
            if (!judgeDependent && a.MemberAIndex.HasValue && b.MemberAIndex.HasValue && a.MemberBIndex.HasValue && b.MemberBIndex.HasValue)
            {
                d.Against.Add("Both panel members order the two alike.");
            }
            d.Quantified = judgeDependent;
            d.Established = judgeDependent;
            d.EstablishedText = "the panel members do not order the two models differently";

            // The members' own provider pair: the interaction contrast measures how much the members' families
            // move the gap. A panel score is the mean of the two members, so a contrast of d moves it by about d ÷ 2.
            if (family is { Contrast: { Value: double contrast } c } && gap > 0)
            {
                double sign = contrast < 0 ? -1 : 1;
                d.Quantified = true;
                d.Share = 0.5 * Math.Abs(contrast) / gap;
                if (c.Lower is double lo && c.Upper is double hi)
                {
                    d.ShareLower = 0.5 * Math.Min(sign * lo, sign * hi) / gap;
                    d.ShareUpper = 0.5 * Math.Max(sign * lo, sign * hi) / gap;
                }
                bool excludes = c.ExcludesZero();
                d.Established = judgeDependent || excludes;
                d.EstablishedText = "the interaction contrast's 95% interval includes zero";
                d.For.Add("Under panel member A alone, " + family.Provider1 + " − " + family.Provider2 + " is " + RootCauseFacts.GapText(family.GapA)
                          + "; under panel member B alone, " + RootCauseFacts.GapText(family.GapB)
                          + ": an interaction contrast of " + RootCauseFacts.GapText(c) + ", which moves the panel's gap by about "
                          + BenchmarkReportFormat.OneDecimal(Math.Abs(contrast) / 2) + " points.");
            }

            // Its own test: both members and the reference reader order the two as the panel does.
            d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            var agreement = ctx.PanelAgreementCheck(a, b);
            d.Robustness.Add(agreement);
            if (!judgeDependent && agreement.Status == RootCauseCheckStatuses.Passed
                && a.ReferenceReaderIndex is double ra && b.ReferenceReaderIndex is double rb
                && a.QualityIndex is double pa && b.QualityIndex is double pb
                && Math.Sign(ra - rb) == Math.Sign(pa - pb) && Math.Sign(pa - pb) != 0)
            {
                d.NotSupported = true;
                d.Against.Remove("Both panel members order the two alike.");
                d.Against.Insert(0, "Both panel members and the reference reader order the two alike.");
            }
            d.FactKeys.Add("panel.judgeDependentPairs");
            d.NextRunKind = RootCauseRunKinds.PanelSwap;
            yield return d;
        }

        // HQ5: the concise response style.
        if (a.StyleConflict)
        {
            var d = PairDraft(ctx, pair, "HQ5", RootCauseAxes.Intelligence, a, b,
                "Completeness was " + A + "'s lowest dimension under the concise response style.",
                "Concise style: " + A,
                "A run of the same model with the detailed response style shows that Completeness is no longer its lowest dimension.",
                "a Detailed run raises Completeness",
                stands, RootCauseLoci.Chat);
            d.For.Add(A + "'s response-style conflict is true: Completeness is its lowest dimension, well below Accuracy.");
            d.Quantified = false;
            d.Caps.Add((RootCauseGrades.Low, "no run with the other response style exists to compare"));
            d.FactKeys.Add(ctx.Key(a.Token, "style.responseStyleConflict"));
            d.NextRunKind = RootCauseRunKinds.StylePair;
            yield return d;
        }

        // HQ6: guessing instead of abstaining.
        var (wrongA, deniedA) = WrongInsteadOfAbstaining(a);
        var (wrongB, deniedB) = WrongInsteadOfAbstaining(b);
        if (deniedA > 0 && deniedB > 0 && wrongA / (double)deniedA > wrongB / (double)deniedB)
        {
            var d = PairDraft(ctx, pair, "HQ6", RootCauseAxes.Intelligence, a, b,
                A + " guessed where it should have abstained more often than " + B + ".",
                "Guessing: " + A,
                "The two Wilson intervals overlap.",
                "the wrong-instead-of-abstaining rates stay apart",
                stands, RootCauseLoci.Model);
            var wa = RootCauseStatistics.Wilson(wrongA, deniedA);
            var wb = RootCauseStatistics.Wilson(wrongB, deniedB);
            d.For.Add(A + " was wrong in " + Inv(wrongA) + " of " + Inv(deniedA) + " answers that were wrong or not attempted (" + Pct(wrongA / (double)deniedA) + "); "
                      + B + " in " + Inv(wrongB) + " of " + Inv(deniedB) + " (" + Pct(wrongB / (double)deniedB) + ").");
            bool apart = wa is { } x && wb is { } y && (x.Low > y.High || y.Low > x.High);
            if (!apart)
            {
                d.NotSupported = true;
                d.Against.Add("The two 95% Wilson intervals overlap.");
            }
            d.Established = apart;
            d.EstablishedText = "the two rates' 95% intervals overlap (Wilson intervals)";
            Common(d);
            d.FactKeys.Add(ctx.Key(a.Token, "profile.outcome.wrongInsteadOfAbstaining"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // HQ7: budget, truncation or output defects.
        if (atA.Points[1] >= 1 && atA.Points[1] > atB.Points[1])
        {
            var d = PairDraft(ctx, pair, "HQ7", RootCauseAxes.Intelligence, a, b,
                "Budget, truncation or output defects cost " + A + " points against " + B + ".",
                "Defects and budget: " + A,
                "There are no such signals, or they put under one point at risk.",
                "the defect points at risk stay at least one point",
                stands, RootCauseLoci.Harness, RootCauseLoci.Provider);
            d.For.Add("Budget, truncation or output defects put " + OneDecimal(atA.Points[1]) + " index points of " + A + " at risk (upper bound), "
                      + OneDecimal(atB.Points[1]) + " of " + B + "'s.");
            d.Share = gap > 0 ? (atA.Points[1] - atB.Points[1]) / gap : null;
            d.Established = testEstablished;
            d.EstablishedText = PairTestReason;
            d.Robustness.Add(LeaveOneOutThreshold(atA, 1, 1.0));
            Common(d);
            d.FactKeys.Add(ctx.Key(a.Token, "profile.toolLimitHits"));
            d.FactKeys.Add(ctx.Key(a.Token, "profile.outputDefects"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // HQ8: the same model at two thinking levels.
        if (SameModelAtTwoLevels(a, b))
        {
            bool lowerIsWorse = ThinkingRank(a.ThinkingLevel) < ThinkingRank(b.ThinkingLevel);
            var d = PairDraft(ctx, pair, "HQ8", RootCauseAxes.Intelligence, a, b,
                A + "'s lower thinking level explains its gap to " + B + ".",
                "Thinking level: " + A,
                "The lower thinking level scores higher (a thinking-level inversion).",
                "the higher level keeps scoring higher on the same questions",
                stands, RootCauseLoci.Model);
            d.For.Add(A + " and " + B + " are the same model at " + (a.ThinkingLevel ?? "no") + " and " + (b.ThinkingLevel ?? "no") + " thinking.");
            if (!lowerIsWorse)
            {
                d.NotSupported = true;
                d.Against.Add("The lower thinking level scored higher: a thinking-level inversion.");
            }
            d.Share = 1.0;
            d.Established = testEstablished;
            d.EstablishedText = PairTestReason;
            d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            d.Robustness.Add(ctx.PanelAgreementCheck(a, b));
            var confounds = ctx.Confounds(a, b, RootCauseAxes.Intelligence, skipThinking: true);
            d.Confounds.AddRange(confounds);
            d.Robustness.Add(ConfigurationCheck(confounds, ModelDifferences(a, b)));
            d.FactKeys.Add(qualityKey);
            d.NextRunKind = RootCauseRunKinds.ThinkingPair;
            yield return d;
        }

        // HQ9: the verifier would clear part of the Accuracy shortfall.
        if (a.VerificationClearedIndex is double cleared && a.QualityIndex is double index && cleared - index >= 0.5)
        {
            var d = PairDraft(ctx, pair, "HQ9", RootCauseAxes.Intelligence, a, b,
                "The claim verifier would clear part of " + A + "'s Accuracy shortfall against " + B + ".",
                "Verifier would clear: " + A,
                "The verification-cleared sensitivity equals the index.",
                "re-grading with the verifier's rulings narrows the gap",
                stands, RootCauseLoci.Harness);
            d.For.Add(A + "'s verification-cleared Overall Index is " + OneDecimal(cleared) + " against its published " + OneDecimal(index) + ".");
            d.Share = gap > 0 ? (cleared - index) / gap : null;
            d.Established = false;
            d.EstablishedText = "the claim verifier's rulings are advisory, not a test";
            d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            d.FactKeys.Add(ctx.Key(a.Token, BenchmarkBatteryReportFacts.PanelVerificationClearedSensitivityKey));
            d.NextRunKind = RootCauseRunKinds.PanelSwap;
            yield return d;
        }
    }

    private static IEnumerable<RootCauseDraft> SpeedPairRules(
        RootCauseContext ctx, RootCausePair pair, RootCausePairDecomposition speed, RootCauseModelInput a, RootCauseModelInput b, bool stands)
    {
        string A = ctx.Name(a.Token), B = ctx.Name(b.Token);
        var shares = speed.Components.Where(c => c.Share.HasValue).ToList();
        if (shares.Count == 0) yield break;
        var lead = shares.OrderByDescending(c => c.Share!.Value).First();
        string total = BenchmarkReportFormat.Seconds(Math.Abs(speed.Difference ?? 0));
        string decompositionKey = "rca.speed." + pair.First + "." + pair.Second;
        bool sameModel = SameModelAtTwoLevels(a, b);

        RootCauseDraft Make(string id, string statement, string shortText, string refutedIf, string confirmIf, RootCauseComponent? component, params string[] loci)
        {
            var d = PairDraft(ctx, pair, id, RootCauseAxes.Speed, a, b, statement, shortText, refutedIf, confirmIf, stands, loci);
            if (component != null)
            {
                d.Share = component.Share;
                d.ShareLower = component.ShareLower;
                d.ShareUpper = component.ShareUpper;
                d.For.Add(Capitalize(component.Label) + " accounts for " + SharePct(component) + " of the " + total + " per-question difference in model time.");
                d.Established = component.ShareLower is double lo && component.ShareUpper is double hi && (lo > 0 || hi < 0);
                d.EstablishedText = ShareIntervalReason;
                d.Robustness.Add(ctx.LeaveOneOutSpeed(pair, component.Key));
                if (ctx.Battery) d.Robustness.Add(ctx.AcrossSuitesSpeed(pair, component.Key));
                d.FactKeys.Add(decompositionKey + "." + component.Key + ".share");
            }
            d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            var confounds = ctx.Confounds(a, b, RootCauseAxes.Speed, skipThinking: id == "HS3" || sameModel);
            d.Confounds.AddRange(confounds);
            d.Robustness.Add(ConfigurationCheck(confounds, ModelDifferences(a, b)));
            if (sameModel) d.For.Add(SameModelLine(A, B, a, b, confounds.Count > 0));
            d.FactKeys.Add(decompositionKey + ".total");
            return d;
        }

        switch (lead.Key)
        {
            case "callCount":
            {
                var d = Make("HS1", A + " is slower than " + B + " mainly from more model calls (tool rounds).", "More model calls: " + A,
                    "The per-call latency or decode share is larger.", "the call-count share stays the largest", lead, RootCauseLoci.Model, RootCauseLoci.Chat);
                d.FactKeys.Add(ctx.Key(a.Token, "speed.modelCallsPerAnswer"));
                d.NextRunKind = RootCauseRunKinds.Replicate;
                yield return d;
                break;
            }
            case "perCallLatency":
            {
                var d = Make("HS2", A + " is slower than " + B + " mainly from longer latency per call (time to first token).", "Latency per call: " + A,
                    "Decode or the call count dominates.", "the per-call latency share stays the largest at other times of day", lead, RootCauseLoci.Provider, RootCauseLoci.Model);
                d.FactKeys.Add(ctx.Key(a.Token, "speed.ttftPerCallP50"));
                d.NextRunKind = RootCauseRunKinds.TimeStratum;
                yield return d;
                break;
            }
            case "outputTokens":
            {
                var d = Make("HS3", A + " is slower than " + B + " mainly from more output (reasoning) tokens.", "More output tokens: " + A,
                    "The decode-rate share is larger.", "the output-token share stays the largest", lead, RootCauseLoci.Model);
                double? ra = ctx.ReasoningShare(a), rb = ctx.ReasoningShare(b);
                if (ra is double x && rb is double y)
                {
                    (x > y ? d.For : d.Against).Add(A + "'s reasoning share of output is " + Pct(x) + ", " + B + "'s " + Pct(y) + ".");
                }
                else
                {
                    // Without both reasoning shares the output tokens cannot be told apart from the thinking.
                    if (!a.Evidence.ReasoningReported) d.Against.Add("Thinking is not reported separately for " + A + ".");
                    if (!b.Evidence.ReasoningReported) d.Against.Add("Thinking is not reported separately for " + B + ".");
                    d.Established = false;
                    d.EstablishedText = "the reasoning share of output is not recorded for both models";
                }
                d.FactKeys.Add(ctx.Key(a.Token, "tokens.reasoningShare"));
                d.NextRunKind = RootCauseRunKinds.ThinkingPair;
                yield return d;
                break;
            }
            case "decodeRate":
            {
                var d = Make("HS4", A + " is slower than " + B + " mainly from a slower decode rate.", "Slower decoding: " + A,
                    "The token share is larger.", "the decode-rate share stays the largest at other times of day", lead, RootCauseLoci.Provider);
                d.FactKeys.Add(ctx.Key(a.Token, "speed.decodeRate"));
                d.NextRunKind = RootCauseRunKinds.TimeStratum;
                yield return d;
                break;
            }
        }

        // HS5: own waits or retries.
        double? waitA = RootCauseEvidence.OwnWaitShare(a.Evidence);
        var waitShares = ctx.Models.Select(m => RootCauseEvidence.OwnWaitShare(m.Evidence)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        double? retriesA = ctx.RetriesPerAnswer(a);
        var retries = ctx.Models.Select(ctx.RetriesPerAnswer).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        bool aboveWait = waitA is double w && waitShares.Count > 0 && w > (RootCauseStatistics.Median(waitShares) ?? 0);
        bool aboveRetries = retriesA is double rt && retries.Count > 0 && rt > (RootCauseStatistics.Median(retries) ?? 0);
        if (aboveWait || aboveRetries)
        {
            double? waitDifference = ctx.WaitDifference(pair, a.Token);
            double? share = waitDifference is double wd && speed.Difference is double t && t != 0 ? wd / Math.Abs(t) : null;
            var d = Make("HS5", "Own waits or retries inflated " + A + "'s time against " + B + ".", "Waits and retries: " + A,
                "They account for under 5% of the gap.", "the waits stay above the models' median", null, RootCauseLoci.Harness, RootCauseLoci.Provider);
            if (aboveWait) d.For.Add(A + "'s own waits are " + Pct(waitA!.Value) + " of its model time, above the models' median.");
            if (aboveRetries) d.For.Add(A + " retried " + OneDecimal(retriesA!.Value) + " attempts per answer, above the models' median.");
            d.Share = share;
            if (share is double s && s < 0.05)
            {
                d.NotSupported = true;
                d.Against.Add("Own waits and retries account for " + Pct(s) + " of the gap, under 5%.");
            }
            d.Established = share is double s2 && s2 >= 0.05;
            d.EstablishedText = "the waits' share of the gap could not be measured";
            d.FactKeys.Add(ctx.Key(a.Token, "speed.ownWaitShare"));
            d.NextRunKind = RootCauseRunKinds.TimeStratum;
            yield return d;
        }

        // HS6: served at another tier.
        int mismatchA = a.Evidence.Answers.Count(x => x.Ok && x.ServedTierMismatch);
        int mismatchB = b.Evidence.Answers.Count(x => x.Ok && x.ServedTierMismatch);
        if (mismatchA > mismatchB)
        {
            var d = Make("HS6", A + " was served at another tier than it requested.", "Tier mismatch: " + A,
                "No answer was served at another tier.", "the mismatch recurs", null, RootCauseLoci.Provider);
            d.For.Add(Inv(mismatchA) + " of " + A + "'s answers were served at another tier than requested; " + Inv(mismatchB) + " of " + B + "'s.");
            d.Quantified = false;
            d.FactKeys.Add(ctx.Key(a.Token, "speed.servedTierMismatch"));
            d.NextRunKind = RootCauseRunKinds.TimeStratum;
            yield return d;
        }
    }

    private static IEnumerable<RootCauseDraft> CostPairRules(
        RootCauseContext ctx, RootCausePair pair, RootCausePairDecomposition cost, RootCauseModelInput a, RootCauseModelInput b, bool stands)
    {
        string A = ctx.Name(a.Token), B = ctx.Name(b.Token);
        string decompositionKey = "rca.cost." + pair.First + "." + pair.Second;
        string total = BenchmarkReportFormat.Usd(Math.Abs(cost.Difference ?? 0));
        var volume = cost.Components.FirstOrDefault(c => c.Key == "all" && c.Effect == "volume");
        var price = cost.Components.FirstOrDefault(c => c.Key == "all" && c.Effect == "price");
        if (volume?.Share == null || price?.Share == null) yield break;

        // The component (volume plus price) with the largest share.
        var byComponent = RootCauseSegments.CostComponents
            .Select(k => (Key: k, Share: cost.Components.Where(c => c.Key == k).Sum(c => c.Share ?? 0)))
            .OrderByDescending(x => x.Share)
            .ToList();
        string leadComponent = byComponent[0].Key;
        bool sameModel = SameModelAtTwoLevels(a, b);

        RootCauseDraft Make(string id, string statement, string shortText, string refutedIf, string confirmIf, RootCauseComponent? component, params string[] loci)
        {
            var d = PairDraft(ctx, pair, id, RootCauseAxes.Cost, a, b, statement, shortText, refutedIf, confirmIf, stands, loci);
            if (component != null)
            {
                d.Share = component.Share;
                d.ShareLower = component.ShareLower;
                d.ShareUpper = component.ShareUpper;
                d.For.Add(Capitalize(component.Label) + " accounts for " + SharePct(component) + " of the " + total + " per-question cost difference.");
                d.Established = component.ShareLower is double lo && component.ShareUpper is double hi && (lo > 0 || hi < 0);
                d.EstablishedText = ShareIntervalReason;
                d.Robustness.Add(ctx.LeaveOneOutCost(pair, component.Key, component.Effect));
                if (ctx.Battery) d.Robustness.Add(ctx.AcrossSuitesCost(pair, component.Key, component.Effect));
                d.FactKeys.Add(decompositionKey + "." + component.Key + "." + component.Effect + ".share");
            }
            // A price effect follows from the price cards, which a rerun cannot change, unless the runs' own cards differ.
            bool priceFromCards = id == "HC2"
                                  && (ctx.Input.PricingBasisKind == BenchmarkReportFacts.PricingBasisCatalog || (!a.Evidence.PricesDiffer && !b.Evidence.PricesDiffer));
            if (!priceFromCards) d.Robustness.Add(ctx.ReplicatesCheck(a, b));
            var confounds = ctx.Confounds(a, b, RootCauseAxes.Cost, skipThinking: id == "HC3" || sameModel);
            d.Confounds.AddRange(confounds);
            d.Robustness.Add(ConfigurationCheck(confounds, ModelDifferences(a, b)));
            if (sameModel) d.For.Add(SameModelLine(A, B, a, b, confounds.Count > 0));
            d.FactKeys.Add(decompositionKey + ".total");
            return d;
        }

        if (volume.Share!.Value >= price.Share!.Value)
        {
            var d = Make("HC1", A + " costs more than " + B + " mainly from token volume.", "Token volume: " + A,
                "The price effect is larger.", "the volume effect stays the larger", volume, RootCauseLoci.Model, RootCauseLoci.Chat);
            d.Against.Add("The price effect is " + SharePct(price) + " of the difference.");
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }
        else
        {
            var d = Make("HC2", A + " costs more than " + B + " mainly from its price per token.", "Price per token: " + A,
                "The volume effect is larger.", "the price effect stays the larger", price, RootCauseLoci.Model);
            d.Against.Add("The volume effect is " + SharePct(volume) + " of the difference.");
            d.FactKeys.Add(ctx.Key(a.Token, "price.output.perMillion"));
            d.CheckKind = "configuration";
            d.CheckText = "Compare the two models' price cards; the price effect follows from them.";
            yield return d;
        }

        // HC3 needs both models' thinking apart from their output: for a provider that counts it inside output,
        // the lead component would be the other model's thinking alone.
        if (leadComponent == RootCauseSegments.Reasoning && a.Evidence.ReasoningReported && b.Evidence.ReasoningReported)
        {
            var reasoningVolume = cost.Components.First(c => c.Key == RootCauseSegments.Reasoning && c.Effect == "volume");
            var d = Make("HC3", "Reasoning tokens drive " + A + "'s higher cost against " + B + ".", "Reasoning cost: " + A,
                "Another component is larger.", "the reasoning component stays the largest", reasoningVolume, RootCauseLoci.Model);
            d.FactKeys.Add(ctx.Key(a.Token, "tokens.reasoningShare"));
            d.NextRunKind = RootCauseRunKinds.ThinkingPair;
            yield return d;
        }

        if (leadComponent == RootCauseSegments.UncachedInput)
        {
            var uncached = cost.Components.First(c => c.Key == RootCauseSegments.UncachedInput && c.Effect == "volume");
            var d = Make("HC4", "Weak prompt caching raises " + A + "'s cost against " + B + ".", "Weak caching: " + A,
                "The cache-read share is comparable to the other models'.", "the cache-read share stays low", uncached, RootCauseLoci.Chat, RootCauseLoci.Provider);
            double? shareA = ctx.CacheReadShare(a);
            var cacheShares = ctx.Models.Select(ctx.CacheReadShare).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            double? median = RootCauseStatistics.Median(cacheShares);
            if (shareA is double s && median is double m)
            {
                string line = A + "'s cache-read share is " + Pct(s) + " against the models' median of " + Pct(m) + ".";
                if (s <= m - 0.20) d.For.Add(line);
                else
                {
                    d.Against.Add(line);
                    d.NotSupported = true;
                }
            }
            else
            {
                d.Quantified = false;
            }
            d.FactKeys.Add(ctx.Key(a.Token, "tokens.cacheReadShare"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // HC5: tool rounds multiply input tokens.
        double inputVolume = cost.Components
            .Where(c => c.Effect == "volume" && c.Key is RootCauseSegments.UncachedInput or RootCauseSegments.CacheRead or RootCauseSegments.CacheWrite)
            .Sum(c => c.Share ?? 0);
        double outputVolume = cost.Components
            .Where(c => c.Effect == "volume" && c.Key is RootCauseSegments.Output or RootCauseSegments.Reasoning)
            .Sum(c => c.Share ?? 0);
        if (inputVolume > outputVolume && inputVolume > price.Share!.Value)
        {
            double? callsA = ctx.CallsPerAnswer(a), callsB = ctx.CallsPerAnswer(b);
            var d = Make("HC5", "More tool rounds multiply " + A + "'s input tokens against " + B + ".", "Tool rounds, input: " + A,
                "The calls per answer are comparable.", "the input volume effect and the call count stay higher", null, RootCauseLoci.Chat, RootCauseLoci.Model);
            d.Share = inputVolume;
            d.For.Add("The input token volume accounts for " + Pct(inputVolume) + " of the cost difference.");
            if (callsA is double x && callsB is double y)
            {
                string line = A + " made " + OneDecimal(x) + " model calls per answer, " + B + " " + OneDecimal(y) + ".";
                if (x > y * 1.1) d.For.Add(line);
                else
                {
                    d.Against.Add(line);
                    d.NotSupported = true;
                }
                d.Established = x > y * 1.1;
                d.EstablishedText = A + " makes at most a tenth more model calls per answer";
            }
            else
            {
                d.Quantified = false;
            }
            d.FactKeys.Add(ctx.Key(a.Token, "speed.modelCallsPerAnswer"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Shared failures (HQ3)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// HQ3 across every analyzed model (comparison scope), or across the subject and its controls (a run or
    /// battery completion): questions every model, or every model but one, scored below 50 point to the
    /// instrument. A check is proposed for each.
    /// </summary>
    internal static RootCauseDraft? SharedFailures(RootCauseContext ctx, IReadOnlyList<(int Number, string Reference, int Low, int Scored, bool WellAnswered)> questions, IReadOnlyList<string> models)
    {
        var flagged = questions.Where(q => q.Scored >= 2 && q.Low >= q.Scored - 1).ToList();
        if (flagged.Count == 0) return null;

        var draft = new RootCauseDraft
        {
            Id = "HQ3",
            Axis = RootCauseAxes.Intelligence,
            Kind = "pair",
            Group = "all",
            Statement = "Shared failures on specific questions point to the instrument, not the models.",
            Short = "Shared failures: instrument",
            RefutedIf = "One model answered the question well.",
            ConfirmIf = "the rubric or the corpus of the flagged questions is found wanting",
            Quantified = false,
            CheckKind = "rubric"
        };
        draft.Models.AddRange(models);
        draft.Loci.AddRange(new[] { RootCauseLoci.Suite, RootCauseLoci.Corpus, RootCauseLoci.Chat });
        foreach (var q in flagged.Take(8))
        {
            draft.For.Add(q.Reference + ": " + Inv(q.Low) + " of " + Inv(q.Scored) + " models scored below 50.");
        }
        foreach (var q in flagged.Where(q => q.WellAnswered).Take(4))
        {
            draft.Against.Add(q.Reference + ": one model scored 75 or above.");
        }
        draft.CheckText = "Check the rubric of " + BenchmarkReportFormat.LetterList(flagged.Take(6).Select(q => q.Reference).ToList())
                          + " against the source, and whether the tools' corpus holds the facts the question needs.";
        draft.FactKeys.Add("questions.sharedLow");
        return draft;
    }

    // ---------------------------------------------------------------------------------------------
    // Within-model rules
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The within-model rules of § A4 for <paramref name="model"/> on <paramref name="axes"/>, and its H0-W
    /// (the contrasts of WQ1 and WQ2, Holm-adjusted together). Intelligence hypotheses whose locus could be the
    /// instrument as well as the model are capped by <paramref name="controlCap"/>.
    /// </summary>
    internal static List<RootCauseDraft> WithinRules(
        RootCauseContext ctx, RootCauseModelInput model, IReadOnlyCollection<string> axes, (string? Cap, string? Reason) controlCap)
    {
        var drafts = new List<RootCauseDraft>();
        if (axes.Contains(RootCauseAxes.Intelligence)) drafts.AddRange(WithinIntelligence(ctx, model, controlCap));
        if (axes.Contains(RootCauseAxes.Speed) && !model.SpeedDegraded) drafts.AddRange(WithinSpeed(ctx, model));
        if (axes.Contains(RootCauseAxes.Cost) && !model.CostDegraded) drafts.AddRange(WithinCost(ctx, model));

        var speedItems = model.Evidence.SpeedItems;
        var costItems = model.Evidence.CostItems;
        foreach (var draft in drafts.Where(d => d.Kind != "null"))
        {
            draft.Scale = draft.Id switch
            {
                // A rate of answers, not a share of the points at risk.
                "WQ4" => null,
                "WS4" => speedItems.Count > 0 ? speedItems.Average(i => i.DurationMs) : null,
                _ => draft.Axis switch
                {
                    RootCauseAxes.Speed => speedItems.Count > 0 ? speedItems.Average(i => i.ModelTimeMs) : null,
                    RootCauseAxes.Cost => costItems.Count > 0 ? costItems.Average(i => i.TotalCost) : null,
                    _ => ctx.AtRisk(model.Token).Total
                }
            };
        }
        return drafts;
    }

    private static RootCauseDraft WithinDraft(RootCauseContext ctx, RootCauseModelInput model, string id, string axis,
        string statement, string shortText, string refutedIf, string confirmIf, params string[] loci)
    {
        var draft = new RootCauseDraft
        {
            Id = id,
            Axis = axis,
            Kind = "within",
            Group = "within:" + model.Token,
            Models = { model.Token },
            Statement = statement,
            Short = shortText,
            RefutedIf = refutedIf,
            ConfirmIf = confirmIf
        };
        draft.Loci.AddRange(loci);
        draft.Robustness.Add(ctx.ReplicatesCheck(model));
        return draft;
    }

    private static IEnumerable<RootCauseDraft> WithinIntelligence(RootCauseContext ctx, RootCauseModelInput model, (string? Cap, string? Reason) controlCap)
    {
        string M = ctx.Name(model.Token);
        var at = ctx.AtRisk(model.Token);
        var scored = model.Evidence.Scored.ToList();
        if (scored.Count == 0) yield break;

        // The contrasts behind H0-W, Holm-adjusted together.
        var withTools = scored.Where(a => !a.NoTool).Select(a => a.Quality!.Value).ToList();
        var withoutTools = scored.Where(a => a.NoTool).Select(a => a.Quality!.Value).ToList();
        var toolContrast = withoutTools.Count >= 3 && withTools.Count > 0 ? RootCauseStatistics.MannWhitney(withoutTools, withTools) : null;

        bool battery = scored.Any(a => a.Suite.HasValue);
        var groups = scored.GroupBy(a => GroupOf(a, battery), StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Answers: g.ToList(), Points: AtRiskOf(at, g.Select(x => x.QuestionKey).Distinct(StringComparer.Ordinal))))
            .OrderByDescending(g => g.Points)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        var leadGroup = groups.Count >= 2 ? groups[0] : default;
        RootCauseMannWhitney? groupContrast = null;
        if (groups.Count >= 2)
        {
            var inGroup = leadGroup.Answers.Select(a => a.Quality!.Value).ToList();
            var rest = groups.Skip(1).SelectMany(g => g.Answers).Select(a => a.Quality!.Value).ToList();
            groupContrast = RootCauseStatistics.MannWhitney(inGroup, rest);
        }

        var pValues = new List<double>();
        if (toolContrast != null) pValues.Add(toolContrast.PValue);
        if (groupContrast != null) pValues.Add(groupContrast.PValue);
        var adjusted = RootCauseStatistics.Holm(pValues);
        double? toolP = toolContrast != null ? adjusted[0] : null;
        double? groupP = groupContrast != null ? adjusted[toolContrast != null ? 1 : 0] : null;

        var h0 = new RootCauseDraft
        {
            Id = "H0-W",
            Axis = RootCauseAxes.Intelligence,
            Kind = "null",
            Group = "within:" + model.Token,
            Models = { model.Token },
            Statement = "The contrasts between groups of " + M + "'s own questions are within sampling noise.",
            Short = "Noise within " + M,
            RefutedIf = "A Mann–Whitney contrast is established after the Holm adjustment.",
            ConfirmIf = "the contrasts still find no difference with three runs per model"
        };
        bool rejected = false;
        if (toolP is double tp)
        {
            (tp < RootCauseStatistics.SignificanceLevel ? h0.Against : h0.For).Add("Answers without tools against answers with tools: adjusted p " + P(tp) + ".");
            rejected |= tp < RootCauseStatistics.SignificanceLevel;
        }
        if (groupP is double gp)
        {
            (gp < RootCauseStatistics.SignificanceLevel ? h0.Against : h0.For).Add(GroupName(leadGroup.Name) + " against the rest: adjusted p " + P(gp) + ".");
            rejected |= gp < RootCauseStatistics.SignificanceLevel;
        }
        if (pValues.Count == 0) h0.For.Add("No contrast could be made.");
        if (rejected)
        {
            h0.NotSupported = true;
        }
        else
        {
            h0.Established = true;
            h0.Caps.Add((model.Evidence.RunsPerSuite < 2 ? RootCauseGrades.Low : RootCauseGrades.Moderate,
                model.Evidence.RunsPerSuite < 2 ? OneRunCapReason : NoProofCapReason));
        }
        yield return h0;

        void Cap(RootCauseDraft d)
        {
            if (controlCap.Cap != null) d.Caps.Add((controlCap.Cap, controlCap.Reason ?? "too few controls"));
            if (ctx.ModelSpecificCount > 0) d.For.Add(Inv(ctx.ModelSpecificCount) + " questions were missed by " + M + " while every control scored 75 or above.");
            if (ctx.InstrumentSuspectCount > 0) d.Against.Add(Inv(ctx.InstrumentSuspectCount) + " questions " + M + " missed were missed by every control too.");
        }

        // WQ1: points lost on answers without tool calls.
        if (withoutTools.Count >= 3 && withTools.Count > 0 && withoutTools.Average() < withTools.Average())
        {
            var d = WithinDraft(ctx, model, "WQ1", RootCauseAxes.Intelligence,
                M + " loses most of its points on answers without tool calls (where its points go, not a comparison).",
                "No-tool answers: " + M,
                "The contrast stands within noise, or fewer than three answers lack tools.",
                "the no-tool answers keep losing the most points",
                RootCauseLoci.Model, RootCauseLoci.Chat);
            d.For.Add(M + "'s answers without tools scored " + Whole(withoutTools.Average()) + " (" + Inv(withoutTools.Count) + "), with tools " + Whole(withTools.Average()) + " (" + Inv(withTools.Count) + ").");
            double share = at.Total > 0 ? at.Points[0] / at.Total : 0;
            d.Share = share;
            bool largest = at.Points[0] >= at.Points.Max() && at.Points[0] > 0;
            (largest ? d.For : d.Against).Add("Answers without tools hold " + Pct(share) + " of the points at risk.");
            if (toolP is double p && p >= RootCauseStatistics.SignificanceLevel)
            {
                d.NotSupported = true;
                d.Against.Add("The contrast with answers that used tools is within noise (adjusted p " + P(p) + ").");
            }
            bool contrast = toolP is double p2 && p2 < RootCauseStatistics.SignificanceLevel;
            d.Established = contrast && largest;
            d.EstablishedText = Both(
                contrast ? null : "answers without tools do not score clearly lower than answers with tools",
                largest ? null : "answers without tools are not the largest share of the lost points");
            d.Robustness.Add(LeaveOneOutLargest(at, 0));
            d.Robustness.Add(ctx.WithinPanelCheck(model, a => a.NoTool));
            if (battery) d.Robustness.Add(ctx.WithinAcrossSuites(model, a => a.NoTool));
            Cap(d);
            d.FactKeys.Add(ctx.Key(model.Token, "score.withoutTools"));
            d.FactKeys.Add(ctx.Key(model.Token, "score.withTools"));
            d.NextRunKind = ctx.NeedsControlRun ? RootCauseRunKinds.Control : RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WQ2: points lost mostly in one band or suite, more than the other models lose there.
        double? peerMedian = groups.Count >= 2 && leadGroup.Points > 0 ? PeerGroupShareMedian(ctx, model, leadGroup.Name) : null;
        double leadShare = at.Total > 0 ? leadGroup.Points / at.Total : 0;
        if (groups.Count >= 2 && leadGroup.Points > 0 && (peerMedian is not double peers || leadShare - peers >= WithinGroupExcessOverPeers))
        {
            string groupName = GroupName(leadGroup.Name);
            var d = WithinDraft(ctx, model, "WQ2", RootCauseAxes.Intelligence,
                M + "'s points are lost mostly in " + groupName + " (where its points go, not a comparison).",
                "Lost in " + groupName,
                "The contrast stands within noise, or the shares are even.",
                "the same group keeps holding the largest share",
                RootCauseLoci.Model, RootCauseLoci.Suite);
            double share = leadShare;
            d.Share = share;
            d.For.Add(groupName + " holds " + Pct(share) + " of " + M + "'s points at risk.");
            if (peerMedian is double median) d.For.Add("The other models' median share there is " + Pct(median) + ".");
            bool even = share < 1.0 / groups.Count + 0.10;
            if (even) d.Against.Add("The shares are nearly even across the groups.");
            if (groupP is double p && p >= RootCauseStatistics.SignificanceLevel)
            {
                d.Against.Add("Its contrast with the rest is within noise (adjusted p " + P(p) + ").");
            }
            d.NotSupported = even || (groupP is double p3 && p3 >= RootCauseStatistics.SignificanceLevel);
            d.Established = groupP is double p4 && p4 < RootCauseStatistics.SignificanceLevel;
            d.EstablishedText = "the gap between " + groupName + " and the rest may be chance";
            if (battery) d.Robustness.Add(ctx.LeaveOneSuiteOutCheck(leadGroup.Name));
            else d.Robustness.Add(LeaveOneOutGroup(at, leadGroup.Answers.Select(a => a.QuestionKey).Distinct(StringComparer.Ordinal).ToList(), groups.Skip(1).Select(g => g.Points).DefaultIfEmpty(0).Max()));
            d.Robustness.Add(ctx.WithinPanelCheck(model, a => GroupOf(a, battery) == leadGroup.Name));
            Cap(d);
            d.NextRunKind = ctx.NeedsControlRun ? RootCauseRunKinds.Control : RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WQ3: confirmed critical errors.
        if (at.Points[2] > 0 && at.Points[2] >= at.Points.Max())
        {
            var d = WithinDraft(ctx, model, "WQ3", RootCauseAxes.Intelligence,
                "Confirmed critical errors cost " + M + " most of its points (where its points go, not a comparison).",
                "Critical errors: " + M,
                "The caps are unconfirmed.",
                "the critical-error share stays the largest",
                RootCauseLoci.Model);
            double share = at.Total > 0 ? at.Points[2] / at.Total : 0;
            d.Share = share;
            d.For.Add("Confirmed critical errors hold " + Pct(share) + " of the points at risk (" + OneDecimal(at.Points[2]) + " index points, upper bound).");
            var lead = LeadOverNext(at, 2);
            d.Established = lead?.ExcludesZero == true;
            d.EstablishedText = "critical errors may not clearly hold more of the lost points than the next cause";
            if (!model.Evidence.CriticalErrorsConfirmed)
            {
                d.Against.Add("Before scoring method 13 the caps are graders' flags, not confirmed errors.");
                d.Caps.Add((RootCauseGrades.Low, UnconfirmedCapReason));
            }
            d.Robustness.Add(LeaveOneOutLargest(at, 2));
            d.Robustness.Add(ctx.WithinPanelCheck(model, a => a.CriticalError));
            d.FactKeys.Add(ctx.Key(model.Token, "outcome.confirmedCriticalRate"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WQ4: guessing instead of abstaining.
        var (wrong, denied) = WrongInsteadOfAbstaining(model);
        if (denied > 0 && wrong / (double)denied > 0.5)
        {
            var d = WithinDraft(ctx, model, "WQ4", RootCauseAxes.Intelligence,
                M + " guesses where it should abstain.",
                "Guessing: " + M,
                "The Wilson interval includes 0.33.",
                "the rate stays above one half with its interval above one third",
                RootCauseLoci.Model);
            var wilson = RootCauseStatistics.Wilson(wrong, denied);
            d.For.Add(M + " was wrong in " + Inv(wrong) + " of " + Inv(denied) + " answers that were wrong or not attempted (" + Pct(wrong / (double)denied) + ").");
            bool above = wilson is { } w && w.Low > 0.33;
            if (!above)
            {
                d.NotSupported = true;
                d.Against.Add("The 95% Wilson interval includes one third.");
            }
            d.Share = wrong / (double)denied;
            d.Established = above;
            d.EstablishedText = "the rate's 95% interval reaches one third (Wilson interval)";
            Cap(d);
            d.FactKeys.Add(ctx.Key(model.Token, "profile.outcome.wrongInsteadOfAbstaining"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WQ5: budget, truncation or output defects.
        if (at.Points[1] >= 1)
        {
            var d = WithinDraft(ctx, model, "WQ5", RootCauseAxes.Intelligence,
                "Budget, truncation or output defects cost " + M + " points.",
                "Defects and budget: " + M,
                "There are no such signals.",
                "the defect points stay at least one point",
                RootCauseLoci.Harness, RootCauseLoci.Provider);
            d.Share = at.Total > 0 ? at.Points[1] / at.Total : null;
            d.For.Add("Budget, truncation or output defects put " + OneDecimal(at.Points[1]) + " index points at risk (upper bound).");
            var interval = SegmentInterval(at, 1);
            d.Established = interval is { Lower: > 0 };
            d.EstablishedText = "the points lost to defects may be zero (their 95% interval includes zero)";
            d.Robustness.Add(LeaveOneOutThreshold(at, 1, 1.0));
            d.FactKeys.Add(ctx.Key(model.Token, "profile.toolLimitHits"));
            d.FactKeys.Add(ctx.Key(model.Token, "profile.outputDefects"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WQ6: grader disagreement.
        double? disagreement = model.Evidence.PanelAnswers is > 0 ? model.Evidence.PanelDisagreements / (double)model.Evidence.PanelAnswers : null;
        double? memberGap = model.MemberAIndex is double ma && model.MemberBIndex is double mb ? Math.Abs(ma - mb) : null;
        double? halfWidth = model.QualityLower is double lo && model.QualityUpper is double hi ? (hi - lo) / 2 : null;
        bool disagree = disagreement is > 0.15;
        bool gapWide = memberGap is double g && halfWidth is double h && g > h;
        if (disagree || gapWide)
        {
            var d = WithinDraft(ctx, model, "WQ6", RootCauseAxes.Intelligence,
                "Grader disagreement makes " + M + "'s index uncertain.",
                "Grader disagreement: " + M,
                "The panel members agree.",
                "the disagreement recurs under another panel",
                RootCauseLoci.Harness);
            if (disagreement is double dr) d.For.Add("The panel disagreed on " + Pct(dr) + " of the answers both members graded.");
            if (memberGap is double mg) d.For.Add("The members' indices are " + OneDecimal(mg) + " points apart" + (halfWidth is double hw ? ", against an interval half-width of " + OneDecimal(hw) : string.Empty) + ".");
            var wilson = disagreement.HasValue ? RootCauseStatistics.Wilson(model.Evidence.PanelDisagreements!.Value, model.Evidence.PanelAnswers!.Value) : null;
            d.Established = (wilson is { } w && w.Low > 0.15) || gapWide;
            d.EstablishedText = Both(
                disagreement.HasValue ? "the disagreement rate may be 15% or less (its 95% interval reaches that low)" : null,
                memberGap.HasValue && halfWidth.HasValue ? "the two members' indices differ by no more than the interval half-width" : null);
            d.FactKeys.Add(ctx.Key(model.Token, "panel.disagreements"));
            d.NextRunKind = RootCauseRunKinds.PanelSwap;
            yield return d;
        }
    }

    private static IEnumerable<RootCauseDraft> WithinSpeed(RootCauseContext ctx, RootCauseModelInput model)
    {
        string M = ctx.Name(model.Token);
        var items = model.Evidence.SpeedItems;
        if (items.Count < RootCauseDecomposition.MinSharedQuestions) yield break;
        var shares = RootCauseDecomposition.ModelTimeShares(items);
        if (shares is not { } s) yield break;
        string[] names = { "time to first token", "output generation", "time outside the calls" };
        int lead = Array.IndexOf(s.Shares, s.Shares.Max());
        var leadInterval = s.LeadOverNext[0];

        // WS1: deliberation before output.
        if (lead == 0)
        {
            var d = WithinDraft(ctx, model, "WS1", RootCauseAxes.Speed,
                "Most of " + M + "'s time is model deliberation before output (where its time goes, not a comparison).",
                "Time to first token: " + M,
                "The decode share is larger.",
                "the time-to-first-token share stays the largest at other times of day",
                RootCauseLoci.Provider, RootCauseLoci.Model);
            FillShare(d, s.Shares[0], s.Intervals[0], names[0], "model time");
            d.Established = leadInterval?.Lower > 0;
            d.EstablishedText = "the wait before the first word may not clearly be the largest part of the time";
            d.Robustness.Add(ctx.WithinLeaveOneOutTime(model, 0));
            d.FactKeys.Add(ctx.WithinKey(model.Token, "time.ttft.share"));
            d.NextRunKind = RootCauseRunKinds.TimeStratum;
            yield return d;
        }

        // WS2: output generation driven by reasoning tokens.
        if (lead == 1)
        {
            var d = WithinDraft(ctx, model, "WS2", RootCauseAxes.Speed,
                "Most of " + M + "'s time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
                "Output generation: " + M,
                "The reasoning share is under one half.",
                "the decode share stays the largest and reasoning stays at least half the output",
                RootCauseLoci.Model);
            FillShare(d, s.Shares[1], s.Intervals[1], names[1], "model time");
            double? reasoning = ctx.ReasoningShare(model);
            if (reasoning is double r)
            {
                (r >= 0.5 ? d.For : d.Against).Add("Reasoning is " + Pct(r) + " of " + M + "'s output tokens.");
                if (r < 0.5) d.NotSupported = true;
            }
            else
            {
                d.Against.Add("The reasoning tokens are not recorded separately.");
            }
            bool leads = leadInterval?.Lower > 0;
            d.Established = leads && reasoning is >= 0.5;
            d.EstablishedText = Both(
                leads ? null : "output generation may not clearly be the largest part of the time",
                reasoning.HasValue ? null : "the reasoning tokens are not recorded separately");
            d.Robustness.Add(ctx.WithinLeaveOneOutTime(model, 1));
            d.FactKeys.Add(ctx.WithinKey(model.Token, "time.outputGeneration.share"));
            d.FactKeys.Add(ctx.Key(model.Token, "tokens.reasoningShare"));
            d.NextRunKind = RootCauseRunKinds.ThinkingPair;
            yield return d;
        }

        // WS3: tool rounds multiply the time.
        var rho = RootCauseStatistics.Spearman(items.Select(i => i.ModelTimeMs).ToList(), items.Select(i => i.Calls).ToList());
        if (rho != null && rho.Rho >= 0.5)
        {
            var d = WithinDraft(ctx, model, "WS3", RootCauseAxes.Speed,
                "Tool rounds multiply " + M + "'s time (where its time goes, not a comparison).",
                "Tool rounds, time: " + M,
                "The ρ interval includes zero.",
                "model time keeps rising with the calls per answer",
                RootCauseLoci.Model, RootCauseLoci.Chat);
            d.For.Add("Across " + M + "'s questions, model time rises with model calls per answer: Spearman ρ " + Two(rho.Rho) + " (95% interval " + Two(rho.Lower) + " to " + Two(rho.Upper) + ", n " + Inv(rho.Count) + ").");
            if (!rho.ExcludesZero)
            {
                d.NotSupported = true;
                d.Against.Add("The interval of ρ includes zero.");
            }
            d.Established = rho.ExcludesZero;
            d.EstablishedText = "the interval of ρ includes zero";
            d.Robustness.Add(ctx.WithinLeaveOneOutRho(items, i => i.ModelTimeMs));
            d.FactKeys.Add(ctx.Key(model.Token, "speed.modelCallsPerAnswer"));
            d.NextRunKind = RootCauseRunKinds.Replicate;
            yield return d;
        }

        // WS4: tool execution adds to the wall time.
        double duration = items.Average(i => i.DurationMs);
        double toolShare = duration > 0 ? items.Average(i => i.ToolMs) / duration : 0;
        if (toolShare >= 0.25)
        {
            var d = WithinDraft(ctx, model, "WS4", RootCauseAxes.Speed,
                "Tool execution adds materially to " + M + "'s wall time (where its time goes, not a comparison).",
                "Tool execution: " + M,
                "Tool time is under 10% of the total.",
                "tool time stays at least a quarter of the total",
                RootCauseLoci.Chat, RootCauseLoci.Corpus);
            var interval = RootCauseStatistics.Bootstrap(items.Count, sample =>
            {
                double total = sample.Average(i => items[i].DurationMs);
                return total > 0 ? sample.Average(i => items[i].ToolMs) / total : null;
            });
            d.Share = toolShare;
            d.ShareLower = interval?.Lower;
            d.ShareUpper = interval?.Upper;
            d.For.Add("Tool execution is " + Pct(toolShare) + IntervalText(interval) + " of " + M + "'s wall time per question.");
            d.Established = interval?.Lower >= 0.10;
            d.EstablishedText = "tool time may be under 10% of the total (its 95% interval reaches below 10%)";
            d.FactKeys.Add(ctx.Key(model.Token, "speed.toolTimeP50"));
            yield return d;
        }

        // WS5: own waits or retries.
        double? waitShare = RootCauseEvidence.OwnWaitShare(model.Evidence);
        var ok = model.Evidence.Answers.Where(a => a.Ok).ToList();
        double retriedShare = ok.Count > 0 ? ok.Count(a => a.RetryAttempts is > 0) / (double)ok.Count : 0;
        if (waitShare is >= 0.10 || retriedShare >= 0.10)
        {
            var d = WithinDraft(ctx, model, "WS5", RootCauseAxes.Speed,
                "Own waits or retries inflate " + M + "'s time (where its time goes, not a comparison).",
                "Waits and retries: " + M,
                "Waits are under 10% of model time and retries under 10% of answers.",
                "the waits or retries recur at other times of day",
                RootCauseLoci.Harness, RootCauseLoci.Provider);
            if (waitShare is double w) d.For.Add("Own waits and failed attempts are " + Pct(w) + " of " + M + "'s model time.");
            d.For.Add(Pct(retriedShare) + " of " + M + "'s answers retried an attempt.");
            d.Share = waitShare;
            d.Established = true;
            d.FactKeys.Add(ctx.Key(model.Token, "speed.ownWaitShare"));
            d.FactKeys.Add(ctx.Key(model.Token, "speed.retriesPerAnswer"));
            d.NextRunKind = RootCauseRunKinds.TimeStratum;
            yield return d;
        }
    }

    private static IEnumerable<RootCauseDraft> WithinCost(RootCauseContext ctx, RootCauseModelInput model)
    {
        string M = ctx.Name(model.Token);
        var items = model.Evidence.CostItems;
        if (!model.Evidence.Priced || items.Count < RootCauseDecomposition.MinSharedQuestions) yield break;
        var shares = RootCauseDecomposition.CostShares(items);
        if (shares is not { } s) yield break;

        // Output with reasoning as one component, against uncached input, cache reads and cache writes.
        double[] grouped = { s.Shares[0], s.Shares[1], s.Shares[2], s.Shares[3] + s.Shares[4] };
        int lead = Array.IndexOf(grouped, grouped.Max());
        int next = Enumerable.Range(0, 4).Where(k => k != lead).OrderByDescending(k => grouped[k]).First();
        var leadInterval = RootCauseStatistics.Bootstrap(items.Count, sample =>
        {
            double total = sample.Average(i => items[i].TotalCost);
            if (total <= 0) return null;
            double Part(int k) => k == 3
                ? (sample.Average(i => items[i].Cost[3]) + sample.Average(i => items[i].Cost[4])) / total
                : sample.Average(i => items[i].Cost[k]) / total;
            return Part(lead) - Part(next);
        });

        if (lead == 3)
        {
            var d = WithinDraft(ctx, model, "WC1", RootCauseAxes.Cost,
                "Output (with reasoning) is " + M + "'s largest cost component (where its cost goes, not a comparison).",
                "Output cost: " + M,
                "Another component is larger.",
                "the output share stays the largest",
                RootCauseLoci.Model);
            d.Share = grouped[3];
            d.For.Add("Visible output and reasoning are " + Pct(grouped[3]) + " of " + M + "'s cost per question.");
            d.Established = leadInterval?.Lower > 0;
            d.EstablishedText = "output may not clearly be the largest part of the cost";
            d.FactKeys.Add(ctx.Key(model.Token, "cost.component.output.perQuestion"));
            d.FactKeys.Add(ctx.Key(model.Token, "cost.component.reasoning.perQuestion"));
            yield return d;
        }

        if (lead == 0)
        {
            double? cacheShare = ctx.CacheReadShare(model);
            var d = WithinDraft(ctx, model, "WC2", RootCauseAxes.Cost,
                "Uncached input is " + M + "'s largest cost component (where its cost goes, not a comparison).",
                "Uncached input: " + M,
                "The cache-read share is at least 70%.",
                "the uncached share stays the largest",
                RootCauseLoci.Chat, RootCauseLoci.Provider);
            d.Share = grouped[0];
            d.ShareLower = s.Intervals[0]?.Lower;
            d.ShareUpper = s.Intervals[0]?.Upper;
            d.For.Add("Uncached input is " + Pct(grouped[0]) + IntervalText(s.Intervals[0]) + " of " + M + "'s cost per question.");
            if (cacheShare is double c)
            {
                (c < 0.5 ? d.For : d.Against).Add(M + " read " + Pct(c) + " of its input from the cache.");
                if (c >= 0.7) d.NotSupported = true;
            }
            bool leads = leadInterval?.Lower > 0;
            d.Established = leads && cacheShare is < 0.5;
            d.EstablishedText = Both(
                leads ? null : "uncached input may not clearly be the largest part of the cost",
                cacheShare is null ? "the share of input read from the cache is not recorded"
                    : cacheShare >= 0.5 ? M + " read at least half its input from the cache" : null);
            d.FactKeys.Add(ctx.Key(model.Token, "tokens.cacheReadShare"));
            d.FactKeys.Add(ctx.Key(model.Token, "cost.component.uncachedInput.perQuestion"));
            yield return d;
        }

        var rho = RootCauseStatistics.Spearman(
            items.Select(i => i.Cost[0] + i.Cost[1] + i.Cost[2]).ToList(),
            items.Select(i => i.Calls).ToList());
        if (rho != null && rho.Rho >= 0.5)
        {
            var d = WithinDraft(ctx, model, "WC3", RootCauseAxes.Cost,
                "Tool rounds multiply " + M + "'s input cost (where its cost goes, not a comparison).",
                "Tool rounds, cost: " + M,
                "The ρ interval includes zero.",
                "input cost keeps rising with the calls per answer",
                RootCauseLoci.Chat, RootCauseLoci.Model);
            d.For.Add("Across " + M + "'s questions, input cost rises with model calls per answer: Spearman ρ " + Two(rho.Rho) + " (95% interval " + Two(rho.Lower) + " to " + Two(rho.Upper) + ").");
            if (!rho.ExcludesZero)
            {
                d.NotSupported = true;
                d.Against.Add("The interval of ρ includes zero.");
            }
            d.Established = rho.ExcludesZero;
            d.EstablishedText = "the interval of ρ includes zero";
            d.Robustness.Add(ctx.WithinLeaveOneOutRho(items, i => i.Cost[0] + i.Cost[1] + i.Cost[2]));
            d.FactKeys.Add(ctx.Key(model.Token, "speed.modelCallsPerAnswer"));
            yield return d;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Robustness helpers on the points at risk
    // ---------------------------------------------------------------------------------------------

    /// <summary>The grading roles whose scores make the published index: the panel members, or a single run's assessor.</summary>
    private static readonly string[] ScoringRoles =
    {
        BenchmarkReportFacts.PanelMemberARole, BenchmarkReportFacts.PanelMemberBRole, BenchmarkReportFacts.AssessorRole
    };

    /// <summary>
    /// The grader-family gap of the panel members' own provider pair when the two models are one of each provider
    /// and its interaction contrast was computed; null otherwise.
    /// </summary>
    internal static RootCauseFamilyGap? MemberFamilyGap(RootCauseContext ctx, RootCauseModelInput a, RootCauseModelInput b)
    {
        if (SameProvider(a.Provider, b.Provider)) return null;
        return ctx.Input.FamilyGaps.FirstOrDefault(g =>
            g.IsMemberProviderPair && !g.InsufficientData && g.Contrast?.Value != null
            && ((SameProvider(g.Provider1, a.Provider) && SameProvider(g.Provider2, b.Provider))
                || (SameProvider(g.Provider1, b.Provider) && SameProvider(g.Provider2, a.Provider))));
    }

    /// <summary>The settings of a model's own configuration, as <see cref="RootCauseContext.ConfigDifferences"/> names them.</summary>
    private static readonly string[] ModelSettings = { "thinking level", "reasoning mode", "service tier", "output token cap", "parallel execution" };

    /// <summary>Every configuration key that differs between the two beyond the provider and the model id, as the known facts state them.</summary>
    internal static List<string> ModelDifferences(RootCauseModelInput a, RootCauseModelInput b)
        => RootCauseContext.ConfigDifferences(a, b).Where(d => d != "provider" && d != "model").ToList();

    /// <summary>
    /// Passed when no confound is open, Failed otherwise. A passed check names the model's own settings among
    /// <paramref name="modelDifferences"/> that differ (<c>Beyond the model, only its thinking level differs (high vs
    /// max), which is part of its configuration.</c>), so it never reads as if the two configurations were alike.
    /// </summary>
    internal static RootCauseRobustnessCheck ConfigurationCheck(IReadOnlyList<string> confounds, IReadOnlyList<string>? modelDifferences = null)
    {
        if (confounds.Count > 0)
        {
            return new RootCauseRobustnessCheck { Name = Configuration, Status = RootCauseCheckStatuses.Failed, Detail = "The two models also differ in " + BenchmarkReportFormat.LetterList(confounds) + "." };
        }

        var settings = (modelDifferences ?? Array.Empty<string>())
            .Where(d => ModelSettings.Any(s => d.StartsWith(s + " (", StringComparison.Ordinal)))
            .ToList();
        string detail;
        if (settings.Count == 0)
        {
            detail = "No configuration key differs beyond the model.";
        }
        else if (settings.Count == 1)
        {
            int open = settings[0].IndexOf(" (", StringComparison.Ordinal);
            detail = "Beyond the model, only its " + settings[0][..open] + " differs" + settings[0][open..] + ", which is part of its configuration.";
        }
        else
        {
            detail = "Beyond the model, only its " + BenchmarkReportFormat.LetterList(settings) + " differ, which are part of its configuration.";
        }
        return new RootCauseRobustnessCheck { Name = Configuration, Status = RootCauseCheckStatuses.Passed, Detail = detail };
    }

    /// <summary>The first model's points of a segment, minus the second's, keep their sign without the question contributing most to the first's.</summary>
    private static RootCauseRobustnessCheck LeaveOneOutAtRisk(RootCauseAtRisk first, RootCauseAtRisk second, int segment)
    {
        if (first.ByQuestion.Count < 2) return NotAssessableLoo("too few questions put points at risk");
        var top = first.ByQuestion.OrderByDescending(kv => kv.Value[segment]).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        double without = first.Points[segment] - top.Value[segment] - second.Points[segment];
        return without > 0
            ? new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The difference keeps its sign without the question that contributes most." }
            : new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "One question carries the difference." };
    }

    /// <summary>A segment's points stay at or above <paramref name="threshold"/> without the question contributing most.</summary>
    private static RootCauseRobustnessCheck LeaveOneOutThreshold(RootCauseAtRisk at, int segment, double threshold)
    {
        var contributing = at.ByQuestion.Where(kv => kv.Value[segment] > 0).ToList();
        if (contributing.Count == 0) return NotAssessableLoo("no question puts points at risk in this segment");
        double top = contributing.Max(kv => kv.Value[segment]);
        return at.Points[segment] - top >= threshold
            ? new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The points stay above the threshold without the question that contributes most." }
            : new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "One question carries the points." };
    }

    /// <summary>A segment stays the largest without the question contributing most to it.</summary>
    private static RootCauseRobustnessCheck LeaveOneOutLargest(RootCauseAtRisk at, int segment)
    {
        var contributing = at.ByQuestion.Where(kv => kv.Value[segment] > 0).ToList();
        if (contributing.Count < 2) return NotAssessableLoo("fewer than two questions put points at risk in this segment");
        var top = contributing.OrderByDescending(kv => kv.Value[segment]).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        var points = at.Points.ToArray();
        for (int k = 0; k < 4; k++) points[k] -= top.Value[k];
        return points[segment] >= points.Max() && points[segment] > 0
            ? new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The segment stays the largest without the question that contributes most." }
            : new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "Without one question another segment is larger." };
    }

    /// <summary>A group stays above the next group's points without its question contributing most.</summary>
    private static RootCauseRobustnessCheck LeaveOneOutGroup(RootCauseAtRisk at, IReadOnlyList<string> questions, double nextPoints)
    {
        var rows = questions.Where(at.ByQuestion.ContainsKey).Select(q => at.ByQuestion[q].Sum()).OrderByDescending(v => v).ToList();
        if (rows.Count < 2) return NotAssessableLoo("fewer than two questions of the group put points at risk");
        return rows.Sum() - rows[0] > nextPoints
            ? new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Passed, Detail = "The group stays the largest without its question that contributes most." }
            : new RootCauseRobustnessCheck { Name = LeaveOneOut, Status = RootCauseCheckStatuses.Failed, Detail = "Without one question another group holds more points." };
    }

    private static RootCauseRobustnessCheck NotAssessableLoo(string detail)
        => new() { Name = LeaveOneOut, Status = RootCauseCheckStatuses.NotAssessable, Detail = Capitalize(detail) + "." };

    /// <summary>The bootstrap interval, over questions, of a segment's lead over the next largest.</summary>
    private static RootCauseInterval? LeadOverNext(RootCauseAtRisk at, int segment)
    {
        var rows = at.ByQuestion.Values.ToList();
        if (rows.Count < 2) return null;
        return RootCauseStatistics.Bootstrap(rows.Count, sample =>
        {
            var sums = new double[4];
            foreach (int i in sample) for (int k = 0; k < 4; k++) sums[k] += rows[i][k];
            double next = Enumerable.Range(0, 4).Where(k => k != segment).Max(k => sums[k]);
            return sums[segment] - next;
        });
    }

    /// <summary>The bootstrap interval, over the questions that put points at risk, of one segment's points.</summary>
    private static RootCauseInterval? SegmentInterval(RootCauseAtRisk at, int segment)
    {
        var rows = at.ByQuestion.Values.ToList();
        if (rows.Count < 2) return null;
        return RootCauseStatistics.Bootstrap(rows.Count, sample => sample.Sum(i => rows[i][segment]));
    }

    private static double AtRiskOf(RootCauseAtRisk at, IEnumerable<string> questions)
        => questions.Where(at.ByQuestion.ContainsKey).Sum(q => at.ByQuestion[q].Sum());

    // ---------------------------------------------------------------------------------------------
    // Groups of questions (WQ2)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The group of a model's answer: its suite (<c>S2</c>) when the model's answers carry suites, else its band.</summary>
    private static string GroupOf(RootCauseAnswerRecord answer, bool battery) => battery ? "S" + Inv(answer.Suite ?? 0) : answer.Band;

    /// <summary>The share of a model's points at risk in <paramref name="group"/>; null when it has no points at risk.</summary>
    private static double? GroupShare(RootCauseContext ctx, RootCauseModelInput model, string group)
    {
        var at = ctx.AtRisk(model.Token);
        if (at.Total <= 0) return null;
        var scored = model.Evidence.Scored.ToList();
        bool battery = scored.Any(a => a.Suite.HasValue);
        return AtRiskOf(at, scored.Where(a => GroupOf(a, battery) == group).Select(a => a.QuestionKey).Distinct(StringComparer.Ordinal)) / at.Total;
    }

    /// <summary>The other models' median share of their points at risk in <paramref name="group"/>; null with no other model.</summary>
    private static double? PeerGroupShareMedian(RootCauseContext ctx, RootCauseModelInput model, string group)
    {
        var shares = ctx.Models
            .Where(m => m.Token != model.Token)
            .Select(m => GroupShare(ctx, m, group))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();
        return shares.Count > 0 ? RootCauseStatistics.Median(shares) : null;
    }

    /// <summary>The group holding the most of a model's points at risk; null with fewer than two groups or none at risk.</summary>
    private static string? LeadGroup(RootCauseContext ctx, RootCauseModelInput model)
    {
        var at = ctx.AtRisk(model.Token);
        var scored = model.Evidence.Scored.ToList();
        if (at.Total <= 0) return null;
        bool battery = scored.Any(a => a.Suite.HasValue);
        var groups = scored.GroupBy(a => GroupOf(a, battery), StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Points: AtRiskOf(at, g.Select(x => x.QuestionKey).Distinct(StringComparer.Ordinal))))
            .OrderByDescending(g => g.Points)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        return groups.Count >= 2 && groups[0].Points > 0 ? groups[0].Name : null;
    }

    /// <summary>
    /// The note for a group that holds the most points at risk for at least half the models (two or more)
    /// while WQ2 does not fire for every one of them; null otherwise.
    /// </summary>
    internal static string? SharedLeadGroupNote(RootCauseContext ctx, IReadOnlyList<RootCauseDraft> drafts)
    {
        int total = ctx.Models.Count;
        if (total < 2) return null;
        var common = ctx.Models
            .Select(m => (m.Token, Group: LeadGroup(ctx, m)))
            .Where(x => x.Group != null)
            .GroupBy(x => x.Group!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (common == null) return null;
        int count = common.Count();
        if (count < 2 || 2 * count < total) return null;
        bool firedForAll = common.All(x => drafts.Any(d => d.Id == "WQ2" && d.Group == "within:" + x.Token));
        if (firedForAll) return null;
        return Capitalize(GroupName(common.Key)) + " holds most of the points at risk for " + Inv(count) + " of " + Inv(total)
               + " models; a contrast within one model there reflects the questions, not the model.";
    }

    // ---------------------------------------------------------------------------------------------
    // Text helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The grade reason of the conditions that failed: one clause, or two joined by <c>and</c>; the general
    /// reason when neither is given.
    /// </summary>
    private static string Both(string? first, string? second)
        => first != null && second != null ? first + ", and " + second
            : first ?? second ?? "its result may be chance";

    private static void FillShare(RootCauseDraft d, double share, RootCauseInterval? interval, string component, string of)
    {
        d.Share = share;
        d.ShareLower = interval?.Lower;
        d.ShareUpper = interval?.Upper;
        d.For.Add(Capitalize(component) + " is " + Pct(share) + IntervalText(interval) + " of " + of + " per question.");
    }

    internal static (int Wrong, int Denominator) WrongInsteadOfAbstaining(RootCauseModelInput model)
    {
        int wrong = model.Evidence.Answers.Count(a => a.Outcome == BenchmarkOutcomeClass.Incorrect);
        int notAttempted = model.Evidence.Answers.Count(a => a.Outcome == BenchmarkOutcomeClass.NotAttempted);
        return (wrong, wrong + notAttempted);
    }

    internal static int ThinkingRank(string? level)
        => Array.IndexOf(ThinkingOrder, (level ?? "none").Trim().ToLowerInvariant());

    /// <summary>The two are one model id at two different thinking levels, both of known rank.</summary>
    internal static bool SameModelAtTwoLevels(RootCauseModelInput a, RootCauseModelInput b)
        => string.Equals(a.ModelId?.Trim(), b.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && !string.Equals(a.ThinkingLevel?.Trim(), b.ThinkingLevel?.Trim(), StringComparison.OrdinalIgnoreCase)
           && ThinkingRank(a.ThinkingLevel) >= 0 && ThinkingRank(b.ThinkingLevel) >= 0;

    /// <summary>The two compare different model ids.</summary>
    internal static bool DifferentModels(RootCauseModelInput a, RootCauseModelInput b)
        => !string.Equals(a.ModelId?.Trim(), b.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Model A and Model B are the same model at high and max thinking</c>, followed by <c>; the thinking level is
    /// the only configuration difference.</c> when no other confound is open.
    /// </summary>
    private static string SameModelLine(string first, string second, RootCauseModelInput a, RootCauseModelInput b, bool otherConfounds)
        => first + " and " + second + " are the same model at " + (a.ThinkingLevel ?? "no") + " and " + (b.ThinkingLevel ?? "no") + " thinking"
           + (otherConfounds ? "." : "; the thinking level is the only configuration difference.");

    internal static bool SameProvider(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string GroupName(string group)
        => group.StartsWith('S') && group.Length > 1 && char.IsAsciiDigit(group[1]) ? "suite " + group[1..] : "the " + group.ToLowerInvariant() + " band";

    internal static string Pct(double value) => BenchmarkReportFormat.Percent(value * 100);

    internal static string SharePct(RootCauseComponent component)
        => component.Share is double s ? Pct(s) + (component.ShareLower is double lo && component.ShareUpper is double hi ? " (" + Interval(lo, hi) + ")" : string.Empty) : "an unknown share";

    internal static string Interval(double lower, double upper)
        => BenchmarkReportFormat.Whole(lower * 100) + "–" + BenchmarkReportFormat.Whole(upper * 100) + "%";

    private static string IntervalText(RootCauseInterval? interval)
        => interval == null ? string.Empty : " (" + Interval(interval.Lower, interval.Upper) + ")";

    private static string Whole(double value) => BenchmarkReportFormat.Whole(value);

    private static string OneDecimal(double value) => BenchmarkReportFormat.OneDecimal(value);

    private static string Two(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string P(double p) => BenchmarkComparisonReportFacts.PValue(p);

    internal static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The text with its first letter lower-cased, unless it opens with a name (<c>Model B</c>, <c>Orion Max</c>).</summary>
    internal static string LowerFirst(string text)
    {
        if (text.Length < 2 || !char.IsUpper(text[0]) || char.IsUpper(text[1])) return text;
        int space = text.IndexOf(' ');
        bool article = space > 0 && text[..space] is "A" or "An" or "The";
        if (!article && space > 0 && space + 1 < text.Length && char.IsUpper(text[space + 1])) return text;
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
