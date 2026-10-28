namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The grading of the root-cause hypotheses (§ A2): every grade boundary, the null cap, the control
/// limit, the per-axis keep limits and the hypothesis a research question reserves (question 1 only one about the
/// pair itself, the slowest model its preferred partner's), the amount a hypothesis explains, the axis status
/// with and without pairs, beside a price effect and with every hypothesis ruled out, the noun phrase of a check a draft proposes, the cost rules of
/// a pair split into input and output, WS1 on a model whose visible text arrives in one burst, and the answers WQ4 and
/// HQ6 need; HQ8 in both directions, HQ9 on the net narrowing of the gap, HC1 between two levels on one price card, HS5
/// at the precision it prints, WC2's statement, and the model a comparison's WQ6 is stated for.
/// </summary>
public class RootCauseHypothesesTests
{
    private static RootCauseRobustnessCheck Check(string name, string status)
        => new() { Name = name, Status = status, Detail = name + " detail" };

    private static RootCauseDraft Draft(
        string id = "HS1", string axis = RootCauseAxes.Speed, string kind = "pair", string group = "A|B",
        bool established = true, bool quantified = true, bool notSupported = false, bool? nullStands = false,
        double? share = 0.6, double? scale = 10.0, params RootCauseRobustnessCheck[] checks)
    {
        var draft = new RootCauseDraft
        {
            Id = id,
            Axis = axis,
            Kind = kind,
            Group = group,
            Models = { "A", "B" },
            Statement = id + " statement",
            Short = id,
            Quantified = quantified,
            Established = established,
            NotSupported = notSupported,
            PairNullStands = nullStands,
            Share = share,
            Scale = scale
        };
        draft.Against.Add("The other share is larger.");
        foreach (var check in checks) draft.Robustness.Add(check);
        return draft;
    }

    private static RootCauseHypothesis Grade(RootCauseDraft draft) => RootCauseHypotheses.Grade(draft, t => "Model " + t);

    [Theory]
    [InlineData("A Mann–Whitney contrast is established", "a Mann–Whitney contrast is established")]
    [InlineData("The ρ interval includes zero", "the ρ interval includes zero")]
    [InlineData("Model A answers faster", "Model A answers faster")]
    [InlineData("HQ1 is refuted", "HQ1 is refuted")]
    [InlineData("Its share falls", "its share falls")]
    public void LowerFirst_LowersALeadingWordOrArticle_ButKeepsAName(string text, string expected)
        => Assert.Equal(expected, RootCauseHypotheses.LowerFirst(text));

    private static RootCauseRobustnessCheck[] AllPassed() => new[]
    {
        Check(RootCauseHypotheses.LeaveOneOut, RootCauseCheckStatuses.Passed),
        Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Passed),
        Check(RootCauseHypotheses.Configuration, RootCauseCheckStatuses.Passed)
    };

    [Fact]
    public void AQuantifiedEstablishedHypothesis_WithEveryCheckPassed_IsHigh()
    {
        var graded = Grade(Draft(checks: AllPassed()));

        Assert.Equal(RootCauseGrades.High, graded.Grade);
        Assert.Empty(graded.GradeReasons);
    }

    [Theory]
    [InlineData(RootCauseCheckStatuses.Failed)]
    [InlineData(RootCauseCheckStatuses.NotAssessable)]
    public void ExactlyOneCondition_IsModerate_WithItsReason(string status)
    {
        var checks = AllPassed();
        checks[1] = Check(RootCauseHypotheses.Replicates, status);

        var graded = Grade(Draft(checks: checks));

        Assert.Equal(RootCauseGrades.Moderate, graded.Grade);
        Assert.Contains(RootCauseHypotheses.CheckReason(checks[1]), graded.GradeReasons);
    }

    [Fact]
    public void ATestNotEstablished_IsACondition_SoAloneItGivesModerate()
    {
        var graded = Grade(Draft(established: false, checks: AllPassed()));

        Assert.Equal(RootCauseGrades.Moderate, graded.Grade);
        Assert.Single(graded.GradeReasons);
    }

    [Fact]
    public void AShareIntervalWiderThanOneHundredPoints_FailsARobustnessCheck_OneGradeLower()
    {
        var checks = AllPassed();
        checks[1] = Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed);
        var wide = Draft(share: 2.19, checks: checks);
        wide.ShareLower = 1.39;
        wide.ShareUpper = 8.25;

        var graded = Grade(wide);

        Assert.Equal(RootCauseGrades.Low, graded.Grade);
        var failed = Assert.Single(graded.Robustness, r => r.Name == RootCauseHypotheses.ShareInterval);
        Assert.Equal(RootCauseCheckStatuses.Failed, failed.Status);
        Assert.Contains("the share's 95% interval, 139–825%, is wider than one hundred percentage points", graded.GradeReasons);

        // Graded again, the draft keeps one such check.
        Assert.Single(Grade(wide).Robustness, r => r.Name == RootCauseHypotheses.ShareInterval);

        var narrow = Draft(share: 0.74, checks: AllPassed());
        narrow.ShareLower = 0.37;
        narrow.ShareUpper = 1.12;
        Assert.DoesNotContain(Grade(narrow).Robustness, r => r.Name == RootCauseHypotheses.ShareInterval);
    }

    [Theory]
    [InlineData(2.19, "Model A is slower than Model B mainly from longer latency per call, which alone exceeds the gap while other components pull the other way.")]
    [InlineData(0.93, "Model A is slower than Model B mainly from longer latency per call.")]
    public void AMainlyFromStatement_WhoseShareExceedsTheGap_SaysOtherComponentsPullTheOtherWay(double share, string expected)
    {
        var draft = Draft(share: share, checks: AllPassed());
        draft.Statement = "Model A is slower than Model B mainly from longer latency per call.";

        Assert.Equal(expected, Grade(draft).Statement);
    }

    [Fact]
    public void TwoConditions_AreLow()
    {
        var checks = AllPassed();
        checks[0] = Check(RootCauseHypotheses.LeaveOneOut, RootCauseCheckStatuses.Failed);
        checks[2] = Check(RootCauseHypotheses.Configuration, RootCauseCheckStatuses.Failed);

        Assert.Equal(RootCauseGrades.Low, Grade(Draft(checks: checks)).Grade);
    }

    [Fact]
    public void AQualitativeHypothesis_IsLow()
    {
        var graded = Grade(Draft(quantified: false, checks: AllPassed()));

        Assert.Equal(RootCauseGrades.Low, graded.Grade);
        Assert.Contains(RootCauseHypotheses.QualitativeReason, graded.GradeReasons);
    }

    [Fact]
    public void ARefutedHypothesis_IsNotSupported_AndNoCapRaisesOrChangesIt()
    {
        var draft = Draft(notSupported: true, nullStands: true, checks: AllPassed());
        draft.Caps.Add((RootCauseGrades.Low, "no other model answered these questions"));

        var graded = Grade(draft);

        Assert.Equal(RootCauseGrades.NotSupported, graded.Grade);
        Assert.Single(graded.GradeReasons);
    }

    [Fact]
    public void APairWhoseNullStands_IsCappedAtLow()
    {
        var graded = Grade(Draft(nullStands: true, checks: AllPassed()));

        Assert.Equal(RootCauseGrades.Low, graded.Grade);
        Assert.Contains(RootCauseHypotheses.NullStandsReason, graded.GradeReasons);
    }

    [Theory]
    [InlineData(0, 0, RootCauseGrades.Low)]
    [InlineData(1, 1, RootCauseGrades.Moderate)]
    [InlineData(2, 0, RootCauseGrades.Moderate)]
    [InlineData(3, 0, RootCauseGrades.Moderate)]
    [InlineData(2, 1, null)]
    [InlineData(3, 2, null)]
    public void TheControlLimit_FollowsTheControlsAndTheirProviders(int count, int otherProviders, string? cap)
    {
        var (limit, reason) = RootCauseControls.Limit(count, otherProviders);

        Assert.Equal(cap, limit);
        Assert.Equal(cap == null, reason == null);
    }

    [Theory]
    [InlineData(0, RootCauseGrades.Low)]
    [InlineData(1, RootCauseGrades.Moderate)]
    [InlineData(2, RootCauseGrades.High)]
    public void TheControlLimit_CapsAWithinModelIntelligenceHypothesis(int controlsAcrossProviders, string expected)
    {
        var (cap, reason) = RootCauseControls.Limit(controlsAcrossProviders, controlsAcrossProviders == 0 ? 0 : 1);
        var draft = Draft(id: "WQ1", axis: RootCauseAxes.Intelligence, kind: "within", group: "subject", checks: AllPassed());
        if (cap != null) draft.Caps.Add((cap, reason!));

        var graded = Grade(draft);

        Assert.Equal(expected, graded.Grade);
        if (cap != null) Assert.Contains(graded.GradeReasons, r => r.Contains(reason!, StringComparison.Ordinal));
    }

    [Fact]
    public void Keep_KeepsEveryNull_TheStrongestThreePerGroup_AndAtMostTwelvePerAxis()
    {
        var graded = new List<(RootCauseHypothesis, RootCauseDraft)>();
        void Add(RootCauseDraft draft) => graded.Add((Grade(draft), draft));

        Add(Draft(id: "H0-S", kind: "null", group: "A|B", checks: AllPassed()));
        for (int g = 0; g < 6; g++)
        {
            for (int i = 0; i < 4; i++)
            {
                Add(Draft(id: "HS" + (i + 1), group: "G" + g, share: 0.1 * (i + 1), checks: AllPassed()));
            }
        }

        var kept = RootCauseHypotheses.Keep(graded);
        var speed = kept.Where(k => k.Hypothesis.Axis == RootCauseAxes.Speed).ToList();

        Assert.Single(speed, k => k.Hypothesis.Kind == "null");
        var nonNull = speed.Where(k => k.Hypothesis.Kind != "null").ToList();
        Assert.Equal(RootCauseHypotheses.MaxPerAxis, nonNull.Count);
        Assert.All(nonNull.GroupBy(k => k.Draft.Group), g => Assert.True(g.Count() <= RootCauseHypotheses.KeepPerGroup));
        Assert.DoesNotContain(nonNull, k => k.Hypothesis.Id == "HS1");
        Assert.All(kept, k => Assert.Equal(k.Hypothesis.Id + "." + k.Hypothesis.Number, k.Hypothesis.Key));
    }

    [Fact]
    public void TheAxisStatus_IsEstablished_ProbableOrNotEstablished()
    {
        List<(RootCauseHypothesis, RootCauseDraft)> Kept(params RootCauseDraft[] drafts)
            => RootCauseHypotheses.Keep(drafts.Select(d => (Grade(d), d)).ToList());

        var high = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, Kept(Draft(checks: AllPassed())));
        Assert.Equal(RootCauseStatuses.Established, high.Status);
        Assert.Equal("HS1.1", high.BestHypothesis);

        var moderateChecks = AllPassed();
        moderateChecks[1] = Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed);
        var probable = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, Kept(Draft(checks: moderateChecks)));
        Assert.Equal(RootCauseStatuses.Probable, probable.Status);

        var low = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, Kept(Draft(nullStands: true, checks: AllPassed())));
        Assert.Equal(RootCauseStatuses.NotEstablished, low.Status);

        var none = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, Kept(Draft(checks: AllPassed())));
        Assert.Equal(RootCauseStatuses.NotEstablished, none.Status);
        Assert.Null(none.BestHypothesis);
    }

    [Fact]
    public void HypothesesRank_ByTheAmountTheyExplain_NotByTheirShareOfTheGap()
    {
        // A 0.7-point gap explained at 218 % accounts for 0.7 points; a 6.7-point gap explained at 80 % for 5.36.
        var tiny = Draft(id: "HQ2", axis: RootCauseAxes.Intelligence, group: "A|B", share: 2.18, scale: 0.7, checks: AllPassed());
        var large = Draft(id: "HQ2", axis: RootCauseAxes.Intelligence, group: "C|D", share: 0.80, scale: 6.7, checks: AllPassed());
        var kept = RootCauseHypotheses.Keep(new[] { tiny, large }.Select(d => (Grade(d), d)).ToList());

        Assert.Equal(new[] { "C|D", "A|B" }, kept.Select(k => k.Draft.Group));
        Assert.Equal(0.7, kept[1].Hypothesis.Explains!.Value, 9);
        Assert.Equal(5.36, kept[0].Hypothesis.Explains!.Value, 9);
        Assert.Equal(2.18, kept[1].Hypothesis.Share);

        var status = RootCauseHypotheses.AxisStatus(RootCauseAxes.Intelligence, kept);
        Assert.Equal(kept[0].Hypothesis.Key, status.BestHypothesis);
    }

    [Fact]
    public void AShare_PullingTheOtherWay_ExplainsNone_AndAShareAboveOne_TheWholeScale()
    {
        Assert.Equal(0.0, Draft(share: -0.4, scale: 5.0).Explains!.Value, 9);
        Assert.Equal(5.0, Draft(share: 1.5, scale: -5.0).Explains!.Value, 9);
        Assert.Equal(2.0, Draft(share: 0.4, scale: 5.0).Explains!.Value, 9);
    }

    /// <summary>A pair hypothesis between <paramref name="worse"/> and <paramref name="better"/>, or a within-model one of <paramref name="worse"/>.</summary>
    private static RootCauseDraft About(RootCauseDraft draft, string worse, string? better = null)
    {
        draft.Models.Clear();
        draft.Models.Add(worse);
        if (better != null) draft.Models.Add(better);
        return draft;
    }

    /// <summary>The question why <paramref name="token"/> scored lowest, its neighbor <paramref name="neighbor"/>.</summary>
    private static RootCauseQuestionTarget Lowest(string token, string neighbor)
        => new(2, RootCauseAxes.Intelligence, new RootCauseModelInput { Token = token }, new RootCauseModelInput { Token = neighbor },
            "Why did Model " + token + " score lowest?", true);

    /// <summary>
    /// Twelve High intelligence hypotheses about models A to G, one per group, which fill the axis; and the
    /// hypotheses about the last of eight models, H, that <paramref name="last"/> adds.
    /// </summary>
    private static List<(RootCauseHypothesis, RootCauseDraft)> EightModels(Action<Action<RootCauseDraft>> last)
    {
        var graded = new List<(RootCauseHypothesis, RootCauseDraft)>();
        void Add(RootCauseDraft draft) => graded.Add((Grade(draft), draft));
        string[] others = { "A", "B", "C", "D", "E", "F", "G" };
        for (int g = 0; g < RootCauseHypotheses.MaxPerAxis; g++)
        {
            string worse = others[g % others.Length], better = others[(g + 1) % others.Length];
            Add(About(Draft(id: "HQ2", axis: RootCauseAxes.Intelligence, group: "G" + g, share: 0.5, checks: AllPassed()), worse, better));
        }
        last(Add);
        return graded;
    }

    [Fact]
    public void Keep_ReservesTheBestAnsweringHypothesis_ForAQuestionTheCutLeftWithNone()
    {
        RootCauseDraft? weak = null, weaker = null, own = null;
        var graded = EightModels(add =>
        {
            // Low: the pair's null stands. The within-model one is Moderate, yet a comparative one is reserved first.
            add(weaker = About(Draft(id: "HQ1", axis: RootCauseAxes.Intelligence, group: "G|H", nullStands: true, share: 0.2, checks: AllPassed()), "H", "G"));
            add(weak = About(Draft(id: "HQ7", axis: RootCauseAxes.Intelligence, group: "F|H", nullStands: true, share: 0.5, checks: AllPassed()), "H", "F"));
            add(own = About(Draft(id: "WQ1", axis: RootCauseAxes.Intelligence, kind: "within", group: "within:H", established: false, checks: AllPassed()), "H"));
        });
        var target = Lowest("H", "G");

        var plain = RootCauseHypotheses.Keep(graded);
        Assert.Equal(RootCauseHypotheses.MaxPerAxis, plain.Count(k => k.Hypothesis.Kind != "null"));
        Assert.DoesNotContain(plain, k => k.Hypothesis.Models[0] == "H");

        var kept = RootCauseHypotheses.Keep(graded, new[] { target }, RootCauseProfiles.Answers);
        var nonNull = kept.Where(k => k.Hypothesis.Kind != "null").ToList();
        Assert.Equal(RootCauseHypotheses.MaxPerAxis + 1, nonNull.Count);
        var reserved = Assert.Single(nonNull, k => k.Hypothesis.Models[0] == "H");
        Assert.Same(weak, reserved.Draft);
        Assert.Equal(RootCauseGrades.Low, reserved.Hypothesis.Grade);
        Assert.Equal(RootCauseGrades.Moderate, Grade(own!).Grade);
        Assert.DoesNotContain(kept, k => ReferenceEquals(k.Draft, weaker) || ReferenceEquals(k.Draft, own));
        Assert.All(kept, k => Assert.Equal(k.Hypothesis.Id + "." + k.Hypothesis.Number, k.Hypothesis.Key));
    }

    [Fact]
    public void Keep_ReservesAComparativeHypothesis_ForAQuestionTheCutLeftWithOnlyAWithinModelOne()
    {
        RootCauseDraft? comparative = null, own = null;
        var graded = EightModels(add =>
        {
            // The within-model one explains more than any filler, so the cut keeps it.
            add(own = About(Draft(id: "WQ1", axis: RootCauseAxes.Intelligence, kind: "within", group: "within:H", nullStands: null, share: 0.9, checks: AllPassed()), "H"));
            add(comparative = About(Draft(id: "HQ7", axis: RootCauseAxes.Intelligence, group: "F|H", nullStands: true, share: 0.5, checks: AllPassed()), "H", "F"));
        });

        var plain = RootCauseHypotheses.Keep(graded);
        Assert.Contains(plain, k => ReferenceEquals(k.Draft, own));
        Assert.DoesNotContain(plain, k => ReferenceEquals(k.Draft, comparative));

        var kept = RootCauseHypotheses.Keep(graded, new[] { Lowest("H", "G") }, RootCauseProfiles.Answers);
        Assert.Contains(kept, k => ReferenceEquals(k.Draft, own));
        var reserved = Assert.Single(kept, k => ReferenceEquals(k.Draft, comparative));
        Assert.Equal(RootCauseGrades.Low, reserved.Hypothesis.Grade);
        Assert.Equal(RootCauseHypotheses.MaxPerAxis + 1, kept.Count(k => k.Hypothesis.Kind != "null"));
    }

    [Fact]
    public void Keep_ReservesNothingGradedNotSupported()
    {
        var graded = EightModels(add =>
        {
            add(About(Draft(id: "HQ1", axis: RootCauseAxes.Intelligence, group: "G|H", notSupported: true, checks: AllPassed()), "H", "G"));
            add(About(Draft(id: "WQ1", axis: RootCauseAxes.Intelligence, kind: "within", group: "within:H", notSupported: true, checks: AllPassed()), "H"));
        });

        var kept = RootCauseHypotheses.Keep(graded, new[] { Lowest("H", "G") }, RootCauseProfiles.Answers);

        Assert.Equal(RootCauseHypotheses.MaxPerAxis, kept.Count(k => k.Hypothesis.Kind != "null"));
        Assert.DoesNotContain(kept, k => k.Hypothesis.Models[0] == "H");
    }

    /// <summary>The question why <paramref name="token"/> scored highest, its runner-up <paramref name="neighbor"/>.</summary>
    private static RootCauseQuestionTarget Highest(string token, string neighbor)
        => new(1, RootCauseAxes.Intelligence, new RootCauseModelInput { Token = token }, new RootCauseModelInput { Token = neighbor },
            "Why did Model " + token + " score highest?", true);

    [Fact]
    public void Keep_ReservesForQuestionOne_OnlyAHypothesisAboutThePairItself()
    {
        // H leads G. G's own failures (HQ1) say nothing about why H leads; the thinking level (HQ8) does.
        RootCauseDraft? ownFailures = null, thinking = null;
        var graded = EightModels(add =>
        {
            add(ownFailures = About(Draft(id: "HQ1", axis: RootCauseAxes.Intelligence, group: "G|H", nullStands: true, share: 0.9, checks: AllPassed()), "G", "H"));
            add(thinking = About(Draft(id: "HQ8", axis: RootCauseAxes.Intelligence, group: "G|H", nullStands: true, share: 0.2, checks: AllPassed()), "G", "H"));
        });

        var plain = RootCauseHypotheses.Keep(graded);
        Assert.DoesNotContain(plain, k => k.Hypothesis.Models.Contains("H"));

        var kept = RootCauseHypotheses.Keep(graded, new[] { Highest("H", "G") }, RootCauseProfiles.Answers);
        var reserved = Assert.Single(kept, k => k.Hypothesis.Models.Contains("H"));
        Assert.Same(thinking, reserved.Draft);
        Assert.DoesNotContain(kept, k => ReferenceEquals(k.Draft, ownFailures));

        // Without a hypothesis about the pair itself, question 1 reserves nothing.
        var onlyFailures = EightModels(add => add(About(Draft(id: "HQ1", axis: RootCauseAxes.Intelligence, group: "G|H", nullStands: true, checks: AllPassed()), "G", "H")));
        var none = RootCauseHypotheses.Keep(onlyFailures, new[] { Highest("H", "G") }, RootCauseProfiles.Answers);
        Assert.Equal(RootCauseHypotheses.MaxPerAxis, none.Count(k => k.Hypothesis.Kind != "null"));
        Assert.DoesNotContain(none, k => k.Hypothesis.Models.Contains("H"));
    }

    [Fact]
    public void Keep_ReservesThePreferredPartnersHypothesis_ForTheSlowestModel_EvenWhenAnotherIsKept()
    {
        // Twelve High speed hypotheses about models A to G fill the axis; H, the slowest, has one against A, which ranks
        // high enough to be kept, and a weaker one against its sibling S, which the cut leaves out.
        var graded = new List<(RootCauseHypothesis, RootCauseDraft)>();
        void Add(RootCauseDraft draft) => graded.Add((Grade(draft), draft));
        string[] others = { "A", "B", "C", "D", "E", "F", "G" };
        for (int g = 0; g < RootCauseHypotheses.MaxPerAxis - 1; g++)
        {
            Add(About(Draft(id: "HS1", group: "G" + g, share: 0.5, checks: AllPassed()), others[g % others.Length], others[(g + 1) % others.Length]));
        }
        var againstReference = About(Draft(id: "HS2", group: "A|H", share: 0.9, checks: AllPassed()), "H", "A");
        var againstSibling = About(Draft(id: "HS2", group: "H|S", nullStands: true, share: 0.2, checks: AllPassed()), "H", "S");
        Add(againstReference);
        Add(againstSibling);
        Add(About(Draft(id: "HS1", group: "G11", share: 0.6, checks: AllPassed()), "B", "C"));
        var slowest = new RootCauseQuestionTarget(3, RootCauseAxes.Speed, new RootCauseModelInput { Token = "H" }, new RootCauseModelInput { Token = "G" },
            "Why is Model H slowest?", null);
        int Preference(RootCauseQuestionTarget target, RootCauseHypothesis h) => h.Models.Contains("S") ? 0 : h.Models.Contains("A") ? 1 : 2;

        var plain = RootCauseHypotheses.Keep(graded, new[] { slowest }, RootCauseProfiles.Answers);
        Assert.Contains(plain, k => ReferenceEquals(k.Draft, againstReference));
        Assert.DoesNotContain(plain, k => ReferenceEquals(k.Draft, againstSibling));

        var kept = RootCauseHypotheses.Keep(graded, new[] { slowest }, RootCauseProfiles.Answers, Preference);
        Assert.Contains(kept, k => ReferenceEquals(k.Draft, againstReference));
        Assert.Contains(kept, k => ReferenceEquals(k.Draft, againstSibling));
        Assert.Equal(RootCauseHypotheses.MaxPerAxis + 1, kept.Count(k => k.Hypothesis.Kind != "null"));
    }

    [Fact]
    public void TheAxisStatus_SetsAPriceEffectAside()
    {
        var price = Draft(id: "HC2", axis: RootCauseAxes.Cost, group: "A|B", checks: AllPassed());
        price.PriceEffect = true;
        var kept = RootCauseHypotheses.Keep(new[] { price }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(RootCauseGrades.High, kept[0].Hypothesis.Grade);
        Assert.True(kept[0].Hypothesis.PriceEffect);

        // A High price effect alone does not establish the axis, but it is named as the axis' best hypothesis.
        var alone = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, kept, hasPairs: true);
        Assert.Equal(RootCauseStatuses.NotEstablished, alone.Status);
        Assert.Equal(RootCauseHypotheses.PriceOnlyAxisReason, alone.Reason);
        Assert.Equal("HC2.1", alone.BestHypothesis);
        Assert.Equal(RootCauseStatuses.NotEstablished, RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, kept).Status);

        // Beside a hypothesis that was ruled out, the price effect is named, and the axis is still not established.
        var caching = Draft(id: "HC4", axis: RootCauseAxes.Cost, group: "A|D", notSupported: true, nullStands: true, checks: AllPassed());
        var ruledOut = RootCauseHypotheses.Keep(new[] { price, caching }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(RootCauseGrades.NotSupported, ruledOut.Single(k => k.Hypothesis.Id == "HC4").Hypothesis.Grade);
        var priceCards = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, ruledOut, hasPairs: true);
        Assert.Equal(RootCauseStatuses.NotEstablished, priceCards.Status);
        Assert.Equal(RootCauseHypotheses.PriceCardsAxisReason, priceCards.Reason);
        Assert.Equal("HC2.1", priceCards.BestHypothesis);

        // Beside a weaker hypothesis that is no price effect, that one sets the status.
        var volumeChecks = AllPassed();
        volumeChecks[1] = Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed);
        var volume = Draft(id: "HC1", axis: RootCauseAxes.Cost, group: "A|C", checks: volumeChecks);
        var both = RootCauseHypotheses.Keep(new[] { price, volume }.Select(d => (Grade(d), d)).ToList());
        var status = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, both, hasPairs: true);
        Assert.Equal(RootCauseStatuses.Probable, status.Status);
        Assert.Equal("HC1.1", status.BestHypothesis);
        Assert.Equal(RootCauseGrades.PriceCardWordWithGrade(RootCauseGrades.High), RootCauseGrades.HypothesisWordWithGrade(both.Single(k => k.Hypothesis.Id == "HC2").Hypothesis));

        // A Low one sets the status too, even beside a ruled-out neighbor.
        var weak = Draft(id: "HC1", axis: RootCauseAxes.Cost, group: "A|C", quantified: false, checks: AllPassed());
        var withLow = RootCauseHypotheses.Keep(new[] { price, caching, weak }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(RootCauseGrades.Low, withLow.Single(k => k.Hypothesis.Id == "HC1").Hypothesis.Grade);
        var lowStatus = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, withLow, hasPairs: true);
        Assert.Equal(RootCauseStatuses.NotEstablished, lowStatus.Status);
        Assert.Equal("HC1.1", lowStatus.BestHypothesis);
        Assert.StartsWith(RootCauseGrades.WordWithGrade(RootCauseGrades.Low), lowStatus.Reason, StringComparison.Ordinal);
    }

    /// <summary>A price effect (HC2) about a pair whose cost difference is <paramref name="scale"/>.</summary>
    private static RootCauseDraft PriceEffect(string group, double scale, double share, bool notSupported = false)
    {
        var draft = Draft(id: "HC2", axis: RootCauseAxes.Cost, group: group, share: share, scale: scale, notSupported: notSupported, checks: AllPassed());
        draft.PriceEffect = true;
        return draft;
    }

    [Fact]
    public void TheAxisStatus_BesidePriceEffectsThatCoverLargerDifferences_IsSetByItsBestHypothesis()
    {
        var volumeChecks = AllPassed();
        volumeChecks[1] = Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed);
        var volume = Draft(id: "HC1", axis: RootCauseAxes.Cost, group: "A|C", scale: 0.1, checks: volumeChecks);
        var largest = PriceEffect("A|B", scale: 0.19, share: 1.03);
        var larger = PriceEffect("A|D", scale: 0.15, share: 0.9);
        var smaller = PriceEffect("A|E", scale: 0.05, share: 0.6);
        var ruledOut = PriceEffect("A|F", scale: 0.5, share: 0.9, notSupported: true);
        var kept = RootCauseHypotheses.Keep(new[] { volume, largest, larger, smaller, ruledOut }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(new[] { "HC2.1", "HC2.2", "HC2.3" }, new[] { largest, larger, smaller }.Select(d => kept.Single(k => ReferenceEquals(k.Draft, d)).Hypothesis.Key));

        // The price effects leave the status to HC1.1, whatever differences they cover.
        var status = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, kept, hasPairs: true);
        Assert.Equal(RootCauseStatuses.Probable, status.Status);
        Assert.Equal("HC1.1", status.BestHypothesis);
        Assert.DoesNotContain("price card", status.Reason, StringComparison.Ordinal);

        // The price effect is the axis's best hypothesis itself: the axis says the runs show nothing beyond the cards.
        var alone = RootCauseHypotheses.Keep(new[] { largest }.Select(d => (Grade(d), d)).ToList());
        var priceOnly = RootCauseHypotheses.AxisStatus(RootCauseAxes.Cost, alone, hasPairs: true);
        Assert.Equal(RootCauseStatuses.NotEstablished, priceOnly.Status);
        Assert.Equal(RootCauseHypotheses.PriceOnlyAxisReason, priceOnly.Reason);
        Assert.Equal("The runs show no cost mechanism beyond the price cards: every hypothesis that fired on this axis is a price-card difference.", priceOnly.Reason);
        Assert.Equal("No cost mechanism beyond the price cards remains: every other hypothesis on this axis was ruled out.", RootCauseHypotheses.PriceCardsAxisReason);
    }

    [Theory]
    [InlineData(new[] { "HC2.1", "HC2.2", "HC2.3" }, "HC2.1–HC2.3")]
    [InlineData(new[] { "HC2.4", "HC2.1", "HC2.2" }, "HC2.1–HC2.2 and HC2.4")]
    [InlineData(new[] { "HC2.3" }, "HC2.3")]
    [InlineData(new[] { "HC2.1", "HC1.2", "HC2.2" }, "HC2.1–HC2.2 and HC1.2")]
    public void KeyRanges_JoinConsecutiveNumbersOfOneRule(string[] keys, string expected)
        => Assert.Equal(expected, RootCauseHypotheses.KeyRanges(keys));

    [Fact]
    public void EveryDraftThatProposesACheck_NamesItAsANounPhrase()
    {
        var ctx = Compared(Timed("A", 1000, 0, reasoningReported: true), Timed("B", 1000, 0, reasoningReported: true));

        var one = RootCauseHypotheses.SharedFailures(ctx, new[] { (3, "Q3", 2, 2, false) }, new[] { "A", "B" });
        Assert.NotNull(one);
        Assert.StartsWith("Check the rubric of Q3 ", one!.CheckText, StringComparison.Ordinal);
        Assert.Equal("a review of the rubric of Q3", one.CheckSummary);

        var two = RootCauseHypotheses.SharedFailures(ctx, new[] { (3, "Q3", 2, 2, false), (7, "Q7", 1, 2, true) }, new[] { "A", "B" });
        Assert.Equal("a review of the rubrics of Q3 and Q7", two!.CheckSummary);
    }

    [Fact]
    public void TheAxisStatus_WithPairs_NeedsAPairHypothesis()
    {
        var within = Draft(id: "WS1", kind: "within", group: "within:A", nullStands: null, checks: AllPassed());
        var kept = RootCauseHypotheses.Keep(new[] { within }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(RootCauseGrades.High, kept[0].Hypothesis.Grade);

        var withPairs = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, kept, hasPairs: true);
        Assert.Equal(RootCauseStatuses.NotEstablished, withPairs.Status);
        Assert.Equal("Only hypotheses about one model's own breakdown fired on this axis; none compares the models.", withPairs.Reason);
        Assert.Null(withPairs.BestHypothesis);

        // A run or battery completion has no pairs: its own breakdown sets the status, as before.
        var withoutPairs = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, kept, hasPairs: false);
        Assert.Equal(RootCauseStatuses.Established, withoutPairs.Status);
        Assert.Equal("WS1.1", withoutPairs.BestHypothesis);
        Assert.Equal(RootCauseStatuses.Established, RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, kept).Status);

        // With pairs, a pair hypothesis is preferred over a stronger within-model one.
        var pairChecks = AllPassed();
        pairChecks[1] = Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed);
        var pair = Draft(id: "HS1", group: "A|B", checks: pairChecks);
        var both = RootCauseHypotheses.Keep(new[] { within, pair }.Select(d => (Grade(d), d)).ToList());
        Assert.Same(within, both[0].Draft);
        var status = RootCauseHypotheses.AxisStatus(RootCauseAxes.Speed, both, hasPairs: true);
        Assert.Equal(RootCauseStatuses.Probable, status.Status);
        Assert.Equal("HS1.1", status.BestHypothesis);
    }

    [Fact]
    public void AnAxisWhoseOnlyHypothesisWasRuledOut_IsNotEstablished_WithNoBestHypothesis()
    {
        // Run 123: WQ2.1, the run's only intelligence hypothesis, was Not supported by its own test.
        var wq2 = Draft(id: "WQ2", axis: RootCauseAxes.Intelligence, kind: "within", group: "subject",
            notSupported: true, nullStands: null, checks: AllPassed());
        var kept = RootCauseHypotheses.Keep(new[] { wq2 }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal(RootCauseGrades.NotSupported, kept.Single().Hypothesis.Grade);

        var status = RootCauseHypotheses.AxisStatus(RootCauseAxes.Intelligence, kept);

        Assert.Equal(RootCauseStatuses.NotEstablished, status.Status);
        Assert.Null(status.BestHypothesis);
        Assert.Equal("Every hypothesis on this axis was ruled out by its own test.", status.Reason);
        Assert.Equal(RootCauseAxisStatus.AllRuledOutReason, status.Reason);

        // Beside a Low one, the Low one is the best, whatever order the ruled-out one ranks in.
        var weak = Draft(id: "WQ1", axis: RootCauseAxes.Intelligence, kind: "within", group: "subject",
            quantified: false, nullStands: null, checks: AllPassed());
        var both = RootCauseHypotheses.Keep(new[] { wq2, weak }.Select(d => (Grade(d), d)).ToList());
        Assert.Equal("WQ1.1", RootCauseHypotheses.AxisStatus(RootCauseAxes.Intelligence, both).BestHypothesis);
    }

    [Fact]
    public void AHypothesisWithNoAmount_RanksAfterThoseWithOne_AndARejectedNullBeforeAStandingOne()
    {
        var unmeasured = Draft(id: "HS1", group: "A|B", share: null, checks: AllPassed());
        var measured = Draft(id: "HS2", group: "C|D", share: 0.1, scale: 1.0, checks: AllPassed());
        var standing = Draft(id: "HS1", group: "E|F", nullStands: true, share: 1.0, scale: 100.0, checks: AllPassed());
        var rejectedLow = Draft(id: "HS3", group: "G|H", share: 0.01, scale: 1.0, checks: new[]
        {
            Check(RootCauseHypotheses.LeaveOneOut, RootCauseCheckStatuses.Failed),
            Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed)
        });

        var ranked = RootCauseHypotheses.Ranked(new[] { unmeasured, standing, measured, rejectedLow }.Select(d => (Grade(d), d)).ToList())
            .Select(r => r.Draft.Group)
            .ToList();

        // High first (measured, then unmeasured); then Low, the rejected null before the standing one.
        Assert.Equal(new[] { "C|D", "A|B", "G|H", "E|F" }, ranked);
    }

    // --- Reasoning a provider does not report separately ---------------------------------------------------

    /// <summary>
    /// One of two compared models with twelve timed and priced questions: its output tokens per question, its
    /// reasoning cost per question and the <paramref name="reasoningTokens"/> it buys, and whether its provider reports
    /// reasoning apart from output (when it does not, its reasoning tokens are not recorded and its reasoning cost slot is 0).
    /// </summary>
    private static RootCauseModelInput Timed(string token, double outputTokens, double reasoningCost, bool reasoningReported, double reasoningTokens = 5000)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = reasoningReported ? "Provider " + token : "Anthropic",
            ModelId = "model-" + token.ToLowerInvariant(),
            ThinkingLevel = "high",
            EntryKey = "run:" + token,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 1,
                RunsPerSuite = 1,
                Priced = true,
                ReasoningReported = reasoningReported,
                Answers = questions.Select(q => new RootCauseAnswerRecord
                {
                    RunId = 1,
                    ItemKey = "q" + q + "@1",
                    QuestionKey = "q" + q,
                    Band = "Simple",
                    Ok = true,
                    OutputTokens = (int)outputTokens,
                    ReasoningTokens = reasoningReported ? (int)(outputTokens / 2) : null,
                    ReasoningReported = reasoningReported
                }).ToList(),
                SpeedItems = questions.Select(q =>
                {
                    double decode = outputTokens * 10 * (1 + 0.01 * (q % 3));
                    return new RootCauseItem
                    {
                        Key = "q" + q,
                        Band = "Simple",
                        TtftMs = 2000,
                        DecodeMs = decode,
                        ModelTimeMs = 2000 + decode + 500,
                        DurationMs = 2000 + decode + 1500,
                        ToolMs = 1000,
                        Calls = 2,
                        OutputTokens = outputTokens
                    };
                }).ToList(),
                CostItems = questions.Select(q => new RootCauseItem
                {
                    Key = "q" + q,
                    Band = "Simple",
                    Tokens = new double[] { 1000, 0, 0, outputTokens, reasoningCost > 0 ? reasoningTokens : 0 },
                    Cost = new double[] { 0.001, 0, 0, outputTokens * 4e-6 * (1 + 0.01 * (q % 4)), reasoningCost * (1 + 0.01 * (q % 4)) }
                }).ToList()
            }
        };
    }

    private static RootCauseContext Compared(RootCauseModelInput a, RootCauseModelInput b)
        => new(new RootCauseInput { Scope = RootCauseEngine.ComparisonScope, Models = new[] { a, b }, ReferenceToken = a.Token });

    [Fact]
    public void HS3_WithUnreportedThinking_SaysSo_AndIsNotEstablished_ButNotRuledOut()
    {
        // Model B writes three times the output at the same rate, and does not report its thinking apart from it.
        var ctx = Compared(Timed("A", 1000, 0, reasoningReported: true), Timed("B", 3000, 0, reasoningReported: false));

        var hs3 = Assert.Single(RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>()), d => d.Id == "HS3");

        Assert.Equal(new[] { "B", "A" }, hs3.Models);
        Assert.Contains("Thinking is not reported separately for Model B.", hs3.Against);
        Assert.False(hs3.NotSupported);
        Assert.False(hs3.Established);
        Assert.NotEqual(RootCauseGrades.NotSupported, Grade(hs3).Grade);

        // Both models report it: the shares are compared and the line is absent.
        var reported = Compared(Timed("A", 1000, 0, reasoningReported: true), Timed("B", 3000, 0, reasoningReported: true));
        var both = Assert.Single(RootCauseHypotheses.PairRules(reported, new RootCausePair("A", "B"), new Dictionary<string, bool>()), d => d.Id == "HS3");
        Assert.DoesNotContain(both.Against, line => line.StartsWith("Thinking is not reported", StringComparison.Ordinal));
        Assert.True(both.Established);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void HC3_FiresOnlyWhenBothModelsReportTheirThinking(bool otherReports, bool fires)
    {
        // Model A's reasoning tokens carry its higher cost. Model B reports a little reasoning of its own, or counts its
        // thinking inside its output, which splits the pair into input and output.
        var ctx = Compared(Timed("A", 500, 0.02, reasoningReported: true),
            Timed("B", 1500, otherReports ? 0.001 : 0, reasoningReported: otherReports, reasoningTokens: 250));

        var drafts = RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>());

        Assert.Contains(drafts, d => d.Axis == RootCauseAxes.Cost && d.Models[0] == "A");
        Assert.Equal(fires, drafts.Any(d => d.Id == "HC3"));
        Assert.Equal(!otherReports, ctx.CostPair(new RootCausePair("A", "B"))!.Grouped);
    }

    // --- A pair split into input and output ----------------------------------------------------------------

    /// <summary>
    /// One of two compared models on twelve priced questions, billed on <paramref name="card"/> (USD per million tokens, by
    /// cost component): an Anthropic-shaped side writes to the cache and counts its thinking inside output; the other
    /// writes nothing to the cache and reports its reasoning apart. Both read the same input and output token totals.
    /// </summary>
    private static RootCauseModelInput Billed(string token, bool anthropic, double[] card)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = anthropic ? "Anthropic" : "OpenAI",
            ModelId = "model-" + token.ToLowerInvariant(),
            ThinkingLevel = "high",
            EntryKey = "run:" + token,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 1,
                RunsPerSuite = 1,
                Priced = true,
                ReasoningReported = !anthropic,
                PricesPerMillion = card.Select(p => (double?)p).ToArray(),
                Answers = questions.Select(q => new RootCauseAnswerRecord
                {
                    RunId = 1,
                    ItemKey = "q" + q + "@1",
                    QuestionKey = "q" + q,
                    Band = "Simple",
                    Ok = true,
                    ReasoningReported = !anthropic
                }).ToList(),
                CostItems = questions.Select(q =>
                {
                    double[] tokens = anthropic
                        ? new double[] { 0, 10000, 10000 * (1 + 0.01 * (q % 3)), 1000, 0 }
                        : new double[] { 10000, 10000, 0, 500, 500 * (1 + 0.01 * (q % 3)) };
                    return new RootCauseItem
                    {
                        Key = "q" + q,
                        Band = "Simple",
                        Tokens = tokens,
                        Cost = tokens.Select((t, k) => t * card[k] / 1_000_000).ToArray()
                    };
                }).ToList()
            }
        };
    }

    [Fact]
    public void InAGroupedPair_ACachingLeadGivesHC4_AndAListPriceLeadGivesAnHC2PriceEffect()
    {
        var pair = new RootCausePair("A", "B");
        double[] card = { 3, 0.3, 3.75, 15, 15 };

        // One card: Model A's cache writes, billed above the list input price, raise its price per input token.
        var caching = Compared(Billed("A", anthropic: true, card), Billed("B", anthropic: false, card));
        var cost = caching.CostPair(pair)!;
        Assert.True(cost.Grouped);
        var drafts = RootCauseHypotheses.PairRules(caching, pair, new Dictionary<string, bool>()).Where(d => d.Axis == RootCauseAxes.Cost).ToList();
        var hc4 = Assert.Single(drafts, d => d.Id == "HC4");
        Assert.Equal("Prompt caching raises Model A's price per input token against Model B.", hc4.Statement);
        Assert.Equal(cost.Components.Single(c => c.Key == RootCauseCostKeys.Input && c.Effect == RootCauseCostKeys.Caching).Share, hc4.Share);
        Assert.Contains("rca.cost.A.B.input.caching.share", hc4.FactKeys);
        Assert.False(hc4.PriceEffect);
        Assert.DoesNotContain(drafts, d => d.Id is "HC1" or "HC2" or "HC3");

        // Model A's card is twice as dear: the price-card effect leads, a price effect.
        var listPrice = Compared(Billed("A", anthropic: true, card.Select(p => 2 * p).ToArray()), Billed("B", anthropic: false, card));
        Assert.True(listPrice.CostPair(pair)!.Grouped);
        var listDrafts = RootCauseHypotheses.PairRules(listPrice, pair, new Dictionary<string, bool>()).Where(d => d.Axis == RootCauseAxes.Cost).ToList();
        var hc2 = Assert.Single(listDrafts, d => d.Id == "HC2");
        Assert.True(hc2.PriceEffect);
        Assert.Contains(hc2.Against, line => line.StartsWith("The caching effect is ", StringComparison.Ordinal));
        Assert.DoesNotContain(listDrafts, d => d.Id is "HC1" or "HC3" or "HC4");
    }

    // --- WS1 on a decode that cannot be measured; WQ4 and HQ6 on too few answers ------------------------------

    /// <summary>
    /// A model on twelve scored and timed questions, two runs per suite, whose time is mostly the wait before its output
    /// (WS1 fires): <paramref name="wrong"/> answers Incorrect, the next <paramref name="notAttempted"/> NotAttempted and the
    /// rest Correct; the first <paramref name="burst"/> answers' visible text arrived in one burst, and with
    /// <paramref name="telemetry"/> false none says either way.
    /// </summary>
    private static RootCauseModelInput Answering(string token, double index, int wrong = 0, int notAttempted = 0, int burst = 0, bool telemetry = true)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = "Provider " + token,
            ModelId = "model-" + token.ToLowerInvariant(),
            ThinkingLevel = "high",
            EntryKey = "run:" + token,
            QualityIndex = index,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 2,
                RunsPerSuite = 2,
                CriticalErrorsConfirmed = true,
                Answers = questions.Select(q => new RootCauseAnswerRecord
                {
                    RunId = 1,
                    ItemKey = "q" + q + "@1",
                    QuestionKey = "q" + q,
                    Band = "Simple",
                    Ok = true,
                    Counting = true,
                    Quality = q <= wrong + notAttempted ? 20 : 80 + q,
                    Outcome = q <= wrong ? Overseer.Services.Benchmarking.BenchmarkOutcomeClass.Incorrect
                        : q <= wrong + notAttempted ? Overseer.Services.Benchmarking.BenchmarkOutcomeClass.NotAttempted
                        : Overseer.Services.Benchmarking.BenchmarkOutcomeClass.Correct,
                    ModelTimeMs = 12000 + 150 * q,
                    DurationMs = 14000 + 150 * q,
                    Telemetry = true,
                    TtftSumMs = 9000 + 100 * q,
                    DecodeSumMs = 2000 + 50 * q,
                    StreamingRateUnmeasurable = telemetry ? q <= burst : null
                }).ToList(),
                SpeedItems = questions.Select(q => new RootCauseItem
                {
                    Key = "q" + q,
                    Band = "Simple",
                    TtftMs = 9000 + 100 * q,
                    DecodeMs = 2000 + 50 * q,
                    ModelTimeMs = 12000 + 150 * q,
                    DurationMs = 14000 + 150 * q,
                    ToolMs = 1000,
                    Calls = 2,
                    OutputTokens = 800
                }).ToList()
            }
        };
    }

    private static List<RootCauseDraft> Within(RootCauseModelInput model, string axis)
        => RootCauseHypotheses.WithinRules(new RootCauseContext(new RootCauseInput { Scope = RootCauseEngine.ModelScope, Models = new[] { model } }),
            model, new[] { axis }, (null, null));

    [Fact]
    public void WS1_OnAModelWhoseTextArrivesInOneBurst_FailsDecodeMeasurable_AndGradesOneLevelLower()
    {
        var measurable = Assert.Single(Within(Answering("A", 80), RootCauseAxes.Speed), d => d.Id == "WS1");
        var burst = Assert.Single(Within(Answering("A", 80, burst: 12), RootCauseAxes.Speed), d => d.Id == "WS1");

        const string reason = "On 12 of 12 answers the visible text arrived in one burst after hidden thinking, "
                              + "so the wait before output cannot be told from deliberation, and the decode share cannot be measured.";
        var check = Assert.Single(burst.Robustness, r => r.Name == RootCauseHypotheses.DecodeMeasurable);
        Assert.Equal(RootCauseCheckStatuses.Failed, check.Status);
        Assert.Equal(reason, check.Detail);
        Assert.Contains(reason, burst.Against);

        var high = Grade(measurable);
        var lower = Grade(burst);
        Assert.Equal(RootCauseGrades.High, high.Grade);
        Assert.Equal(RootCauseGrades.Moderate, lower.Grade);
        Assert.Contains(RootCauseHypotheses.CheckReason(check), lower.GradeReasons);
        Assert.StartsWith("Wait before output: ", burst.Short, StringComparison.Ordinal);
        Assert.Contains(burst.For, line => line.StartsWith("Wait before output is ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(12, false)]
    public void WS1_WithAMeasurableDecode_OrNoTelemetryFlag_IsUnchanged(int burst, bool telemetry)
    {
        // Half the answers or fewer in one burst, or answers that say nothing either way, add no check.
        var ws1 = Assert.Single(Within(Answering("A", 80, burst: burst, telemetry: telemetry), RootCauseAxes.Speed), d => d.Id == "WS1");

        Assert.DoesNotContain(ws1.Robustness, r => r.Name == RootCauseHypotheses.DecodeMeasurable);
        Assert.DoesNotContain(ws1.Against, line => line.Contains("one burst", StringComparison.Ordinal));
        Assert.Equal(RootCauseGrades.High, Grade(ws1).Grade);
    }

    [Fact]
    public void WS1_FailsDecodeMeasurable_OnMoreThanHalfOfTheAnswers()
    {
        var ws1 = Assert.Single(Within(Answering("A", 80, burst: 7), RootCauseAxes.Speed), d => d.Id == "WS1");

        var check = Assert.Single(ws1.Robustness, r => r.Name == RootCauseHypotheses.DecodeMeasurable);
        Assert.StartsWith("On 7 of 12 answers the visible text arrived in one burst", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBurstAnswersFact_CountsWhatWS1Reads_OfTheTimedAnswers_AndTheWaitBeforeOutputSaysItIsSummed()
    {
        var model = Answering("A", 80, burst: 7);
        Assert.Equal((7, 12), RootCauseEvidence.BurstAnswers(model.Evidence));
        Assert.Equal((0, 0), RootCauseEvidence.BurstAnswers(Answering("A", 80, burst: 7, telemetry: false).Evidence));

        var summary = new RootCauseContext(new RootCauseInput { Scope = RootCauseEngine.ModelScope, Models = new[] { model } }).EvidenceSummary(model);
        var facts = new Overseer.Services.Benchmarking.BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, new RootCauseResult { Evidence = { summary } }, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);

        Assert.Equal("7 of 12 answers whose visible text arrived in one burst", byKey["rca.within.A.speed.burstAnswers"].Display);
        Assert.Equal("Answers whose visible text arrived in one burst, Model A",
            Overseer.Services.Benchmarking.BenchmarkReportFactLabels.Label("rca.within.A.speed.burstAnswers"));

        Assert.EndsWith(" s per question, summed over its model calls", byKey["rca.within.A.time.ttft.seconds"].Display, StringComparison.Ordinal);
        Assert.DoesNotContain("summed", byKey["rca.within.A.time.outputGeneration.seconds"].Display, StringComparison.Ordinal);
        Assert.Equal("Wait before output, summed over the model calls of a question, Model A",
            Overseer.Services.Benchmarking.BenchmarkReportFactLabels.Label("rca.within.A.time.ttft.seconds"));
        Assert.Equal("Wait before output, summed over the model calls of a question, Model A: share of the time per question",
            Overseer.Services.Benchmarking.BenchmarkReportFactLabels.Label("rca.within.A.time.ttft.share"));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void WQ4_NeedsTheProfilesMinimumOfWrongOrNotAttemptedAnswers(int wrong, bool raised)
    {
        Assert.Equal(3, Overseer.Services.Benchmarking.BenchmarkComparisonModelProfile.WrongInsteadOfAbstainingMinAnswers);

        var drafts = Within(Answering("A", 70, wrong: wrong), RootCauseAxes.Intelligence);

        Assert.Equal(raised, drafts.Any(d => d.Id == "WQ4"));
    }

    [Theory]
    [InlineData(2, 1, 2, false)]
    [InlineData(3, 1, 2, true)]
    [InlineData(3, 0, 2, false)]
    public void HQ6_NeedsTheProfilesMinimumOnEachSide(int worseWrong, int betterWrong, int betterNotAttempted, bool raised)
    {
        // The worse model was wrong on every such answer; the better one abstained on most of its own.
        var ctx = Compared(Answering("A", 60, wrong: worseWrong), Answering("B", 80, wrong: betterWrong, notAttempted: betterNotAttempted));

        var drafts = RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>());

        Assert.Equal(raised, drafts.Any(d => d.Id == "HQ6"));
    }

    // --- HQ8, HQ9, HC1, HS5, WC2 and a comparison's WQ6 ------------------------------------------------------

    /// <summary>
    /// A battery entry of <paramref name="modelId"/> at <paramref name="thinkingLevel"/> on twelve scored questions, two runs
    /// per suite, with its Overall Index, an interval of ± <paramref name="halfWidth"/>, the advisory
    /// verification-cleared index <paramref name="cleared"/>, the index under each panel member, the panel's ICC, and
    /// <paramref name="disagreements"/> of <paramref name="panelAnswers"/> answers on which the panel disagreed.
    /// </summary>
    private static RootCauseModelInput Entry(string token, double index, string modelId, string thinkingLevel,
        double? cleared = null, double? memberA = null, double? memberB = null, double halfWidth = 2.0, double? icc = null,
        int disagreements = 0, int panelAnswers = 0)
    {
        var basis = Answering(token, index);
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = "OpenAI",
            ModelId = modelId,
            ThinkingLevel = thinkingLevel,
            EntryKey = "battery:" + token,
            QualityIndex = index,
            QualityLower = index - halfWidth,
            QualityUpper = index + halfWidth,
            VerificationClearedIndex = cleared,
            MemberAIndex = memberA,
            MemberBIndex = memberB,
            PanelIcc = icc,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 2,
                RunsPerSuite = 2,
                CriticalErrorsConfirmed = true,
                Panel = panelAnswers > 0,
                PanelAnswers = panelAnswers > 0 ? panelAnswers : null,
                PanelDisagreements = panelAnswers > 0 ? disagreements : null,
                Answers = basis.Evidence.Answers,
                SpeedItems = basis.Evidence.SpeedItems
            }
        };
    }

    private static List<RootCauseDraft> IntelligenceDrafts(RootCauseModelInput a, RootCauseModelInput b)
        => RootCauseHypotheses.PairRules(Compared(a, b), new RootCausePair(a.Token, b.Token), new Dictionary<string, bool>())
            .Where(d => d.Axis == RootCauseAxes.Intelligence)
            .ToList();

    [Fact]
    public void HQ8_WhenTheLowerThinkingLevelScoredHigher_StatesTheInversion_WithNoShare()
    {
        // Comparison #5: GPT-6.1 Terra at high (84.67) below the same model at medium (85.78).
        var hq8 = Assert.Single(IntelligenceDrafts(Entry("A", 84.67, "gpt-6.1-terra", "high"), Entry("B", 85.78, "gpt-6.1-terra", "medium")),
            d => d.Id == "HQ8");

        Assert.Equal(new[] { "A", "B" }, hq8.Models);
        Assert.Equal("Model B at the lower thinking level scored higher than Model A.", hq8.Statement);
        Assert.Equal(RootCauseHypotheses.ThinkingInversionShort + ": Model B", hq8.Short);
        Assert.True(hq8.NotSupported);
        Assert.Null(hq8.Share);
        Assert.Contains("The lower thinking level scored higher: a thinking-level inversion.", hq8.Against);

        var graded = Grade(hq8);
        Assert.Equal(RootCauseGrades.NotSupported, graded.Grade);
        Assert.Null(graded.Share);
        Assert.Null(graded.Explains);
        string label = RootCauseLabels.Of(graded, t => "Model " + t);
        Assert.Equal("Model B's higher score at the lower thinking level, compared with Model A", label);
        Assert.Contains(RootCauseLabels.Topic(graded), label, StringComparison.Ordinal);
    }

    [Fact]
    public void HQ8_WhenTheLowerThinkingLevelScoredLower_NamesItAsTheGap()
    {
        var hq8 = Assert.Single(IntelligenceDrafts(Entry("A", 84.0, "gpt-6.1-terra", "medium"), Entry("B", 86.0, "gpt-6.1-terra", "high")),
            d => d.Id == "HQ8");

        Assert.Equal("Model A's lower thinking level explains its gap to Model B.", hq8.Statement);
        Assert.Equal("Thinking level: Model A", hq8.Short);
        Assert.False(hq8.NotSupported);
        Assert.Equal(1.0, hq8.Share!.Value, 9);
        Assert.Equal("Model A's lower thinking level, compared with Model B", RootCauseLabels.Of(Grade(hq8), t => "Model " + t));
    }

    [Fact]
    public void HQ9_WithComparisonFiveFigures_RaisesNothing_SinceBothModelsGainAlike()
    {
        // GPT-6 Luna 81.60 → 82.35 and GPT-6.1 Terra (medium) 85.78 → 86.61: the gap narrows by 0.75 − 0.83 < 0.5 points.
        var drafts = IntelligenceDrafts(Entry("A", 81.60, "gpt-6-luna", "high", cleared: 82.35), Entry("B", 85.78, "gpt-6.1-terra", "medium", cleared: 86.61));

        Assert.DoesNotContain(drafts, d => d.Id == "HQ9");
    }

    [Fact]
    public void HQ9_SharesTheNetNarrowingOfTheGap_OrMarksAOneSidedShare_WhenTheOtherSensitivityIsUnknown()
    {
        double gap = 85.78 - 81.60;

        // Both sensitivities known: the gap narrows by (83.60 − 81.60) − (86.61 − 85.78) points.
        var net = Assert.Single(IntelligenceDrafts(Entry("A", 81.60, "gpt-6-luna", "high", cleared: 83.60), Entry("B", 85.78, "gpt-6.1-terra", "medium", cleared: 86.61)),
            d => d.Id == "HQ9");
        Assert.Equal((83.60 - 81.60 - (86.61 - 85.78)) / gap, net.Share!.Value, 9);
        Assert.Contains("Model B's verification-cleared Overall Index is 86.6 against its published 85.8, so the gap narrows by 1.2 points.", net.For);
        Assert.DoesNotContain(net.Against, line => line.Contains("one-sided", StringComparison.Ordinal));

        // The better model's sensitivity unknown: Model A's gain alone, said to be one-sided.
        var oneSided = Assert.Single(IntelligenceDrafts(Entry("A", 81.60, "gpt-6-luna", "high", cleared: 82.35), Entry("B", 85.78, "gpt-6.1-terra", "medium")),
            d => d.Id == "HQ9");
        Assert.Equal((82.35 - 81.60) / gap, oneSided.Share!.Value, 9);
        Assert.Contains("Model B's verification-cleared sensitivity is unknown, so the share is one-sided: it counts Model A's gain alone.", oneSided.Against);
    }

    /// <summary>
    /// One of two models on twelve priced questions billed on <paramref name="card"/>, reasoning reported apart, with
    /// <paramref name="scale"/> times the base input and output tokens.
    /// </summary>
    private static RootCauseModelInput Volumed(string token, string modelId, string thinkingLevel, double scale, double[] card)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = "OpenAI",
            ModelId = modelId,
            ThinkingLevel = thinkingLevel,
            EntryKey = "run:" + token,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 1,
                RunsPerSuite = 1,
                Priced = true,
                ReasoningReported = true,
                PricesPerMillion = card.Select(p => (double?)p).ToArray(),
                Answers = questions.Select(q => new RootCauseAnswerRecord
                {
                    RunId = 1,
                    ItemKey = "q" + q + "@1",
                    QuestionKey = "q" + q,
                    Band = "Simple",
                    Ok = true,
                    ReasoningReported = true
                }).ToList(),
                CostItems = questions.Select(q =>
                {
                    double[] tokens = { 10000 * scale * (1 + 0.01 * (q % 3)), 2000, 0, 500 * scale, 500 * scale * (1 + 0.01 * (q % 4)) };
                    return new RootCauseItem
                    {
                        Key = "q" + q,
                        Band = "Simple",
                        Tokens = tokens,
                        Cost = tokens.Select((t, k) => t * card[k] / 1_000_000).ToArray()
                    };
                }).ToList()
            }
        };
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HC1_IsNotRaised_ForTheSameModelAtTwoLevelsOnOnePriceCard(bool sameModel, bool raised)
    {
        // Model A reads and writes twice the tokens on the same card; the price effect is zero by construction.
        double[] card = { 1.25, 0.125, 0, 10, 10 };
        var ctx = Compared(Volumed("A", "gpt-6.1-terra", "high", 2.0, card), Volumed("B", sameModel ? "gpt-6.1-terra" : "gpt-6-luna", "medium", 1.0, card));
        var pair = new RootCausePair("A", "B");

        var cost = ctx.CostPair(pair)!;
        Assert.False(cost.Grouped);
        Assert.Equal(0.0, cost.Components.Single(c => c.Key == RootCauseCostKeys.All && c.Effect == RootCauseCostKeys.Price).Share!.Value, 9);
        var drafts = RootCauseHypotheses.PairRules(ctx, pair, new Dictionary<string, bool>()).Where(d => d.Axis == RootCauseAxes.Cost).ToList();

        Assert.Equal(raised, drafts.Any(d => d.Id == "HC1"));
        Assert.DoesNotContain(drafts, d => d.Id == "HC2");
        Assert.Equal(sameModel, RootCauseHypotheses.SameModelAtTwoLevels(ctx.Model("A"), ctx.Model("B")));
        Assert.True(RootCauseHypotheses.SamePriceCards(ctx.Model("A"), ctx.Model("B")));
    }

    /// <summary>
    /// One of two timed models on twelve questions writing <paramref name="outputTokens"/> per question, whose own waits
    /// are <paramref name="waitShare"/> of its model time.
    /// </summary>
    private static RootCauseModelInput Waiting(string token, double outputTokens, double waitShare)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        double Decode(int q) => outputTokens * 10 * (1 + 0.01 * (q % 3));
        double ModelTime(int q) => 2000 + Decode(q) + 500;
        return new RootCauseModelInput
        {
            Token = token,
            Letter = token,
            Label = "Model label " + token,
            Provider = "Provider " + token,
            ModelId = "model-" + token.ToLowerInvariant(),
            ThinkingLevel = "high",
            EntryKey = "run:" + token,
            Evidence = new RootCauseModelEvidence
            {
                RunCount = 1,
                RunsPerSuite = 1,
                ReasoningReported = true,
                Answers = questions.Select(q => new RootCauseAnswerRecord
                {
                    RunId = 1,
                    ItemKey = "q" + q + "@1",
                    QuestionKey = "q" + q,
                    Band = "Simple",
                    Ok = true,
                    Telemetry = true,
                    ModelTimeMs = ModelTime(q),
                    OwnWaitMs = waitShare * ModelTime(q)
                }).ToList(),
                SpeedItems = questions.Select(q => new RootCauseItem
                {
                    Key = "q" + q,
                    Band = "Simple",
                    TtftMs = 2000,
                    DecodeMs = Decode(q),
                    ModelTimeMs = ModelTime(q),
                    DurationMs = ModelTime(q) + 1000,
                    ToolMs = 1000,
                    WaitMs = waitShare * ModelTime(q),
                    Calls = 2,
                    OutputTokens = outputTokens
                }).ToList()
            }
        };
    }

    [Theory]
    [InlineData(0.004, 0.001, false)]
    [InlineData(0.034, 0.026, false)]
    [InlineData(0.05, 0.01, true)]
    public void HS5_FiresOnlyOnePrintedPercentagePointAboveTheMedian(double waitA, double waitB, bool fires)
    {
        // Model A is the slower; its waits print as 0% and 3% against a median that prints the same, or 5% against 3%.
        var ctx = Compared(Waiting("A", 3000, waitA), Waiting("B", 1000, waitB));

        var drafts = RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>());

        Assert.Contains(drafts, d => d.Axis == RootCauseAxes.Speed && d.Models[0] == "A");
        var hs5 = drafts.Where(d => d.Id == "HS5").ToList();
        Assert.Equal(fires, hs5.Count == 1);
        if (fires) Assert.Contains("Model A's own waits are 5% of its model time, above the models' median.", hs5[0].For);
    }

    [Fact]
    public void WC2_StatesTheHypothesisItsTestChecks()
    {
        var wc2 = Assert.Single(Within(Billed("A", anthropic: false, new double[] { 3, 0.3, 3.75, 15, 15 }), RootCauseAxes.Cost), d => d.Id == "WC2");

        Assert.Equal("A low cache-read share makes uncached input Model A's largest cost component (where its cost goes, not a comparison).", wc2.Statement);
        Assert.Equal("The cache-read share is at least 70%.", wc2.RefutedIf);
    }

    /// <summary>
    /// Comparison #5's three battery entries, whose member gaps and interval half-widths are constructed: GPT-6 Luna (high),
    /// whose members agree; GPT-6.1 Terra (medium), ICC 0.66 and no disagreement, members 2.4 points apart against a
    /// half-width of 2.0; and GPT-6.1 Terra (high), ICC 0.27 and one disagreement, members 5.9 points apart against 2.3.
    /// </summary>
    private static RootCauseContext ComparisonFive()
        => new(new RootCauseInput
        {
            Scope = RootCauseEngine.ComparisonScope,
            ReferenceToken = "A",
            Models = new[]
            {
                Entry("A", 81.60, "gpt-6-luna", "high", memberA: 82.0, memberB: 81.2, halfWidth: 2.1, icc: 0.71, panelAnswers: 36),
                Entry("B", 85.78, "gpt-6.1-terra", "medium", memberA: 87.0, memberB: 84.6, halfWidth: 2.0, icc: 0.66, panelAnswers: 36),
                Entry("C", 84.67, "gpt-6.1-terra", "high", memberA: 87.6, memberB: 81.7, halfWidth: 2.3, icc: 0.27, disagreements: 1, panelAnswers: 36)
            }
        });

    [Fact]
    public void WQ6_InAComparison_IsStatedForTheModelWhoseMembersAreFurthestApartAgainstItsHalfWidth()
    {
        // Model C is the middle model on intelligence, so it is no within-rule target; the rule is stated for it all the same.
        var ctx = ComparisonFive();

        var wq6 = RootCauseHypotheses.GraderDisagreement(ctx)!;

        Assert.Equal("WQ6", wq6.Id);
        Assert.Equal(new[] { "C" }, wq6.Models);
        Assert.Equal("within:C", wq6.Group);
        Assert.Equal("Grader disagreement makes Model C's index uncertain.", wq6.Statement);
        Assert.Contains("The members' indices are 5.9 points apart, against an interval half-width of 2.3.", wq6.For);
        Assert.Contains("The panel's intraclass correlation over Model C's answers is 0.27.", wq6.For);
        Assert.Contains("Of the 2 models whose graders disagree, Model C's members are the furthest apart against its interval half-width.", wq6.For);
        Assert.Equal(ctx.AtRisk("C").Total, wq6.Scale!.Value, 9);

        // A within-rule target of a comparison no longer states it on its own.
        foreach (var model in ctx.Models)
        {
            Assert.DoesNotContain(RootCauseHypotheses.WithinRules(ctx, model, new[] { RootCauseAxes.Intelligence }, (null, null)), d => d.Id == "WQ6");
        }
    }

    [Theory]
    [InlineData(0.66, 0.27, "C")]
    [InlineData(0.27, 0.66, "B")]
    public void WQ6_InAComparison_BreaksATieOnTheMemberGapByTheLowerIcc(double iccB, double iccC, string expected)
    {
        var ctx = new RootCauseContext(new RootCauseInput
        {
            Scope = RootCauseEngine.ComparisonScope,
            ReferenceToken = "B",
            Models = new[]
            {
                Entry("B", 86, "gpt-6.1-terra", "medium", memberA: 87.5, memberB: 84.5, icc: iccB, panelAnswers: 36),
                Entry("C", 85, "gpt-6.1-terra", "high", memberA: 86.5, memberB: 83.5, icc: iccC, panelAnswers: 36)
            }
        });

        Assert.Equal(new[] { expected }, RootCauseHypotheses.GraderDisagreement(ctx)!.Models);
    }

    [Fact]
    public void WQ6_InModelScope_StaysTheSubjectsOwnRule()
    {
        var subject = Entry("A", 84.67, "gpt-6.1-terra", "high", memberA: 87.6, memberB: 81.7, halfWidth: 2.3, icc: 0.27, disagreements: 1, panelAnswers: 36);
        var ctx = new RootCauseContext(new RootCauseInput { Scope = RootCauseEngine.ModelScope, Models = new[] { subject } });

        Assert.Single(RootCauseHypotheses.WithinRules(ctx, subject, new[] { RootCauseAxes.Intelligence }, (null, null)), d => d.Id == "WQ6");
        Assert.Null(RootCauseHypotheses.GraderDisagreement(ctx));
    }

    [Fact]
    public void GradesDisplayAndRankInOrder()
    {
        Assert.Equal("Not supported", RootCauseGrades.Display(RootCauseGrades.NotSupported));
        Assert.True(RootCauseGrades.Rank(RootCauseGrades.High) > RootCauseGrades.Rank(RootCauseGrades.Moderate));
        Assert.True(RootCauseGrades.Rank(RootCauseGrades.Moderate) > RootCauseGrades.Rank(RootCauseGrades.Low));
        Assert.True(RootCauseGrades.Rank(RootCauseGrades.Low) > RootCauseGrades.Rank(RootCauseGrades.NotSupported));
        Assert.Equal(RootCauseGrades.NotSupported, RootCauseGrades.Capped(RootCauseGrades.NotSupported, RootCauseGrades.Moderate));
    }
}
