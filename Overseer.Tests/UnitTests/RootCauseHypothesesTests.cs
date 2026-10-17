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
/// with and without pairs and beside a price effect, the noun phrase of a check a draft proposes, and the cost rules of
/// a pair split into input and output.
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
