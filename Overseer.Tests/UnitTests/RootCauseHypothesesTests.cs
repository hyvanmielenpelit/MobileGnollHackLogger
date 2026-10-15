namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The grading of the root-cause hypotheses (§ A2): every grade boundary, the null cap, the control
/// limit, the per-axis keep limits and the hypothesis a research question reserves, the amount a
/// hypothesis explains, and the axis status with and without pairs.
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
