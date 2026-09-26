namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Cross-run judge-family diagnostics. The family-gap fixtures follow the research report's model:
/// a grader of family j adds γ_j to every answer of its own family's candidate and grades the other
/// family's candidate at its true quality, and a third-family reference reader grades both at their
/// true quality. Member A is the OpenAI judge and member B the Anthropic judge, so for the pair of
/// their own providers GapA = γ_A, GapB = −γ_B, the interaction contrast is γ_A + γ_B, and the
/// asymmetry estimate is (γ_A − γ_B) / 2.
/// </summary>
public class BenchmarkPanelDiagnosticsTests
{
    private const string OpenAI = "OpenAI";
    private const string Anthropic = "Anthropic";
    private const string Google = "Google";

    /// <summary>The OpenAI candidate's true quality on each of six questions.</summary>
    private static readonly int[] TrueOpenAI = { 60, 70, 80, 50, 90, 65 };

    /// <summary>The Anthropic candidate's true quality minus the OpenAI candidate's; the offsets average 0.</summary>
    private static readonly int[] AnthropicOffset = { 0, 2, -2, 4, -4, 0 };

    private sealed record Score(int A, int B, int? Reference);

    private static SystemAiConfigurationSnapshot MemberA(string? thinkingLevel = null)
        => BenchmarkModelSnapshots.Model(provider: OpenAI, modelId: "openai-judge", thinkingLevel: thinkingLevel);

    private static SystemAiConfigurationSnapshot MemberB(string modelId = "anthropic-judge")
        => BenchmarkModelSnapshots.Model(provider: Anthropic, modelId: modelId);

    private static SystemAiConfigurationSnapshot Reader(string modelId = "google-reader")
        => BenchmarkModelSnapshots.Model(provider: Google, modelId: modelId);

    /// <summary>
    /// A panel run on questions 1..n at item revision 1 and equal difficulty. With
    /// <paramref name="withReader"/> the run carries a reference reader, and every answer with a
    /// reference score records it as an <c>All</c>-mode verdict.
    /// </summary>
    private static BenchmarkRun PanelRun(
        long id,
        string candidateProvider,
        IReadOnlyList<Score> scores,
        bool withReader = true,
        SystemAiConfigurationSnapshot? memberA = null,
        SystemAiConfigurationSnapshot? memberB = null,
        SystemAiConfigurationSnapshot? reader = null,
        int coAssessorCap = 16000)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            Status = BenchmarkRunStatus.Completed,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: candidateProvider, modelId: $"candidate-{id}"),
            AssessorModelSnapshot = memberA ?? MemberA(),
            AssessorEffectiveMaxOutputTokens = 16000,
            CoAssessorModelConfigurationId = 2,
            CoAssessorModelSnapshot = memberB ?? MemberB(),
            CoAssessorEffectiveMaxOutputTokens = coAssessorCap
        };

        if (withReader)
        {
            run.SecondOpinionAssessorModelConfigurationId = 3;
            run.SecondOpinionAssessorModelSnapshot = reader ?? Reader();
            run.SecondOpinionEffectiveMaxOutputTokens = 16000;
            run.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.All;
            run.SecondOpinionBlindUsed = true;
        }

        for (int i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = id * 100 + i,
                BenchmarkRunId = id,
                BenchmarkQuestionId = i + 1,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = $"Q{i + 1}",
                AnswerText = $"Answer {i + 1}",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = s.A,
                CoAssessmentQualityScore = s.B,
                PanelQualityScore = (s.A + s.B) / 2.0,
                AssessedDifficulty = 50,
                SecondOpinionQualityScore = s.Reference,
                SecondOpinionTrigger = s.Reference.HasValue ? "All" : null
            });
        }

        return run;
    }

    /// <summary>The same scores on every one of <paramref name="questions"/> questions.</summary>
    private static IReadOnlyList<Score> Uniform(int a, int b, int? reference = null, int questions = 6)
        => Enumerable.Range(0, questions).Select(_ => new Score(a, b, reference)).ToList();

    /// <summary>
    /// The two entries of the research report's model at the given same-family preferences: an OpenAI
    /// candidate (run 1) and an Anthropic candidate (run 2), both graded by the same panel.
    /// </summary>
    private static List<(string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs)> FamilyModel(
        int gammaA, int gammaB, bool withReader = true, int questions = 6)
    {
        var openAiScores = new List<Score>();
        var anthropicScores = new List<Score>();
        for (int q = 0; q < questions; q++)
        {
            int trueOpenAI = TrueOpenAI[q];
            int trueAnthropic = TrueOpenAI[q] + AnthropicOffset[q];
            openAiScores.Add(new Score(trueOpenAI + gammaA, trueOpenAI, withReader ? trueOpenAI : (int?)null));
            anthropicScores.Add(new Score(trueAnthropic, trueAnthropic + gammaB, withReader ? trueAnthropic : (int?)null));
        }

        return new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            ("openai", "OpenAI candidate", new[] { PanelRun(1, OpenAI, openAiScores, withReader) }),
            ("anthropic", "Anthropic candidate", new[] { PanelRun(2, Anthropic, anthropicScores, withReader) })
        };
    }

    private static (string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs) Entry(
        string key, params BenchmarkRun[] runs)
        => (key, $"Model {key.ToUpperInvariant()}", runs);

    private static BenchmarkPanelFamilyGap MemberPairGap(BenchmarkPanelDiagnosticsResult result)
        => result.FamilyGaps.Single(g => g.IsMemberProviderPair);

    // --- Research report examples A–C ---------------------------------------------------------------

    [Fact]
    public void ExampleA_SymmetricSelfPreference_CancelsInThePanel()
    {
        // γ_A = γ_B = 6: both members favor their own family by the same amount.
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: 6));

        Assert.True(result.Applicable);
        var gap = MemberPairGap(result);

        // For the members' own providers, Provider 1 is member A's and Provider 2 member B's.
        Assert.Equal(OpenAI, gap.Provider1);
        Assert.Equal(Anthropic, gap.Provider2);
        Assert.Equal(6, gap.PairedQuestionCount);
        Assert.False(gap.InsufficientData);

        Assert.Equal(6.0, gap.GapA.Value!.Value, 9);
        Assert.Equal(-6.0, gap.GapB.Value!.Value, 9);
        Assert.Equal(0.0, gap.GapPanel.Value!.Value, 9);
        Assert.Equal(0.0, gap.GapRef!.Value!.Value, 9);

        // The contrast sees both preferences; the panel mean carries neither.
        Assert.Equal(12.0, gap.InteractionContrast!.Value!.Value, 9);
        Assert.Equal(0.0, gap.AsymmetryEstimate!.Value!.Value, 9);
        Assert.Equal(BenchmarkPanelDiagnostics.InteractionContrastLabel, gap.InteractionContrastLabel);
        Assert.Equal(BenchmarkPanelDiagnostics.AsymmetryEstimateLabel, gap.AsymmetryEstimateLabel);
    }

    [Fact]
    public void ExampleB_OppositeSigns_GiveAZeroContrast_AndTheBiasShowsOnlyAgainstTheReference()
    {
        // γ_A = +6, γ_B = −6: member A favors its own family, member B penalizes its own. The
        // interaction contrast is 0, yet the panel mean favors the OpenAI candidate by 6.
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: -6));

        var gap = MemberPairGap(result);
        Assert.Equal(0.0, gap.InteractionContrast!.Value!.Value, 9);
        Assert.Equal(6.0, gap.GapPanel.Value!.Value, 9);
        Assert.Equal(0.0, gap.GapRef!.Value!.Value, 9);
        Assert.Equal(6.0, gap.AsymmetryEstimate!.Value!.Value, 9);
    }

    [Fact]
    public void ExampleB_WithoutAReferenceReader_TheBiasIsInvisible()
    {
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: -6, withReader: false));

        Assert.True(result.Applicable);
        Assert.Null(result.ReferenceLabel);
        var gap = MemberPairGap(result);

        // The only contrast left reads 0, and nothing reports the panel's lean.
        Assert.Equal(0.0, gap.InteractionContrast!.Value!.Value, 9);
        Assert.Null(gap.GapRef);
        Assert.Null(gap.AsymmetryEstimate);
        Assert.Null(gap.AsymmetryEstimateLabel);
    }

    [Fact]
    public void ExampleC_AsymmetricSelfPreference_LeavesAResidualPanelBias()
    {
        // γ_A = 10, γ_B = 2: the same total preference as example A, but unequal.
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 10, gammaB: 2));

        var gap = MemberPairGap(result);
        Assert.Equal(10.0, gap.GapA.Value!.Value, 9);
        Assert.Equal(-2.0, gap.GapB.Value!.Value, 9);

        // The contrast matches example A's, so it cannot tell the two apart; the asymmetry can.
        Assert.Equal(12.0, gap.InteractionContrast!.Value!.Value, 9);
        Assert.Equal(4.0, gap.GapPanel.Value!.Value, 9);
        Assert.Equal(4.0, gap.AsymmetryEstimate!.Value!.Value, 9);
    }

    [Fact]
    public void FamilyGap_Intervals_AreStudentTIntervalsOverThePairedQuestions()
    {
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 10, gammaB: 2));
        var gap = MemberPairGap(result);

        // The interaction is γ_A + γ_B on every question, so its interval has no width.
        Assert.Equal(12.0, gap.InteractionContrast!.CiLow!.Value, 9);
        Assert.Equal(12.0, gap.InteractionContrast.CiHigh!.Value, 9);

        // GapA's per-question differences are 10 minus the offsets: 10 8 12 6 14 10. Sample SD
        // sqrt(40 / 5) = sqrt(8), so the half-width is t(5) * sqrt(8) / sqrt(6).
        double halfWidth = BenchmarkGroupStatistics.StudentTCritical95(5) * Math.Sqrt(8.0) / Math.Sqrt(6.0);
        Assert.Equal(10.0 - halfWidth, gap.GapA.CiLow!.Value, 9);
        Assert.Equal(10.0 + halfWidth, gap.GapA.CiHigh!.Value, 9);
    }

    [Fact]
    public void FamilyGap_BelowFivePairedQuestions_IsFlaggedInsufficient_WithNoEstimates()
    {
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: 6, questions: 4));

        var gap = MemberPairGap(result);
        Assert.Equal(4, gap.PairedQuestionCount);
        Assert.True(gap.InsufficientData);
        Assert.Null(gap.GapA.Value);
        Assert.Null(gap.GapA.CiLow);
        Assert.Null(gap.GapPanel.Value);
        Assert.Null(gap.InteractionContrast!.Value);
        Assert.Null(gap.AsymmetryEstimate!.Value);
    }

    [Fact]
    public void FamilyGap_PairsQuestionsOnItemRevision()
    {
        var entries = FamilyModel(gammaA: 6, gammaB: 6);
        // The Anthropic candidate answered Q1 under a revised rubric, so Q1 no longer pairs.
        entries[1].Runs[0].Answers.First().ItemRevisionUsed = 2;

        var gap = MemberPairGap(BenchmarkPanelDiagnostics.Compute(entries));

        Assert.Equal(5, gap.PairedQuestionCount);
        Assert.False(gap.InsufficientData);
    }

    [Fact]
    public void FamilyGap_PairsOutsideTheMembersProviders_AreAlphabetical_AndCarryNoContrasts()
    {
        var entries = FamilyModel(gammaA: 6, gammaB: 6);
        entries.Add(("google", "Google candidate",
            new[] { PanelRun(3, Google, TrueOpenAI.Select(t => new Score(t, t, t)).ToList()) }));

        var result = BenchmarkPanelDiagnostics.Compute(entries);

        Assert.Equal(
            new[] { (OpenAI, Anthropic), (Anthropic, Google), (Google, OpenAI) },
            result.FamilyGaps.Select(g => (g.Provider1, g.Provider2)));

        var others = result.FamilyGaps.Where(g => !g.IsMemberProviderPair).ToList();
        Assert.Equal(2, others.Count);
        Assert.All(others, g =>
        {
            Assert.Null(g.InteractionContrast);
            Assert.Null(g.InteractionContrastLabel);
            Assert.Null(g.AsymmetryEstimate);
            Assert.Null(g.AsymmetryEstimateLabel);
        });
    }

    // --- Indices and rankings -----------------------------------------------------------------------

    [Fact]
    public void Entries_CarryEachGradersIndex_AndTheResultNamesThePanel()
    {
        // Mean true quality 415 / 6 = 69.17. Member A adds 6 to the OpenAI candidate, member B to the
        // Anthropic one; the panel adds 3 to each.
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: 6));

        var openAi = result.Entries.Single(e => e.EntryKey == "openai");
        var anthropic = result.Entries.Single(e => e.EntryKey == "anthropic");

        Assert.Equal(OpenAI, openAi.CandidateProvider);
        Assert.Equal(75, openAi.MemberAIndex);
        Assert.Equal(69, openAi.MemberBIndex);
        Assert.Equal(72, openAi.PanelIndex);
        Assert.Equal(69, openAi.ReferenceIndex);
        Assert.Equal(69, anthropic.MemberAIndex);
        Assert.Equal(75, anthropic.MemberBIndex);
        Assert.Equal(72, anthropic.PanelIndex);

        Assert.Equal("openai-judge", result.MemberALabel);
        Assert.Equal(OpenAI, result.MemberAProvider);
        Assert.Equal("anthropic-judge", result.MemberBLabel);
        Assert.Equal(Anthropic, result.MemberBProvider);
        Assert.Equal("google-reader", result.ReferenceLabel);
        Assert.Equal(Google, result.ReferenceProvider);
        Assert.Equal(BenchmarkPanelDiagnostics.Caveats, result.Caveats);

        // Each member ranks its own family's candidate first.
        var pair = Assert.Single(result.JudgeDependentPairs);
        Assert.Equal("openai", pair.FirstEntryKey);
        Assert.Equal("anthropic", pair.SecondEntryKey);

        // The panel ties them, and a tie is not a reversal.
        Assert.Empty(result.ReferenceDependentPairs);
    }

    [Fact]
    public void JudgeDependentPairs_AreTheEntriesTheMembersOrderOppositely_IgnoringTies()
    {
        // Under A: X 80, T 80, Y 72, Z 60. Under B: Y 78, X 70, T 65, Z 60.
        //   X–Y and T–Y reverse; X–T is tied under A; every pair with Z agrees.
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("x", PanelRun(1, OpenAI, Uniform(80, 70), withReader: false)),
            Entry("y", PanelRun(2, Anthropic, Uniform(72, 78), withReader: false)),
            Entry("t", PanelRun(3, OpenAI, Uniform(80, 65), withReader: false)),
            Entry("z", PanelRun(4, Anthropic, Uniform(60, 60), withReader: false))
        };

        var result = BenchmarkPanelDiagnostics.Compute(entries);

        Assert.Equal(2, result.JudgeDependentPairs.Count);
        var xy = result.JudgeDependentPairs.Single(p => p.FirstEntryKey == "x");
        Assert.Equal("y", xy.SecondEntryKey);
        Assert.Equal(
            "Member A ranks Model X above Model Y (80 vs 72); member B ranks Model Y above Model X (78 vs 70).",
            xy.Description);
        var ty = result.JudgeDependentPairs.Single(p => p.FirstEntryKey == "t");
        Assert.Equal("y", ty.SecondEntryKey);

        // Competition ranks: tied entries share a rank.
        var byKey = result.Entries.ToDictionary(e => e.EntryKey);
        Assert.Equal(1, byKey["x"].RankA);
        Assert.Equal(1, byKey["t"].RankA);
        Assert.Equal(3, byKey["y"].RankA);
        Assert.Equal(4, byKey["z"].RankA);
        Assert.Equal(1, byKey["y"].RankB);
        Assert.Equal(2, byKey["x"].RankB);
        Assert.Equal(3, byKey["t"].RankB);
        Assert.Equal(4, byKey["z"].RankB);

        // No reference reader: no reference index, rank or reversal.
        Assert.All(result.Entries, e => Assert.Null(e.ReferenceIndex));
        Assert.Empty(result.ReferenceDependentPairs);
    }

    [Fact]
    public void ReferenceDependentPairs_AreTheEntriesThePanelAndTheReaderOrderOppositely()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("p", PanelRun(1, OpenAI, Uniform(80, 80, reference: 60))),
            Entry("q", PanelRun(2, Anthropic, Uniform(70, 70, reference: 75)))
        };

        var result = BenchmarkPanelDiagnostics.Compute(entries);

        Assert.Empty(result.JudgeDependentPairs);
        var pair = Assert.Single(result.ReferenceDependentPairs);
        Assert.Equal("p", pair.FirstEntryKey);
        Assert.Equal(
            "The panel ranks Model P above Model Q (80 vs 70); the reference reader ranks Model Q above Model P (75 vs 60).",
            pair.Description);
    }

    [Fact]
    public void ReferenceIndex_IsWithheld_UnlessTheReaderGradedEveryAnswer()
    {
        var partial = PanelRun(1, OpenAI, Uniform(80, 80, reference: 70));
        partial.Answers.First().SecondOpinionQualityScore = null;

        var trial = PanelRun(2, Anthropic, Uniform(70, 70, reference: 70));
        // A manual trial verdict is not the reference reader's.
        trial.Answers.First().SecondOpinionTrigger = "Manual";

        var result = BenchmarkPanelDiagnostics.Compute(new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("partial", partial),
            Entry("trial", trial)
        });

        Assert.All(result.Entries, e =>
        {
            Assert.Null(e.ReferenceIndex);
            Assert.Null(e.RankReference);
        });
    }

    [Fact]
    public void AnAnswerWithoutAPanelScore_IsLeftOutOfEveryIndex()
    {
        var run = PanelRun(1, OpenAI, new[] { new Score(90, 90, null), new Score(50, 50, null) }, withReader: false);
        var failed = run.Answers.Last();
        failed.CoAssessmentStatus = BenchmarkAssessmentStatus.Failed;
        failed.CoAssessmentQualityScore = null;
        failed.PanelQualityScore = null;

        var entry = Assert.Single(BenchmarkPanelDiagnostics.Compute(
            new List<(string, string, IReadOnlyList<BenchmarkRun>)> { Entry("a", run) }).Entries);

        // Member A's 50 on the answer the panel did not score is not counted either.
        Assert.Equal(90, entry.MemberAIndex);
        Assert.Equal(90, entry.MemberBIndex);
        Assert.Equal(90, entry.PanelIndex);
    }

    // --- Accusation audit ---------------------------------------------------------------------------

    private static BenchmarkClaimVerification Charge(
        string role,
        BenchmarkClaimVerdict verdict,
        string[]? raisedBy,
        bool suspectedFalse = false,
        string? citationNote = null)
        => new(0, "A charged sentence.", verdict, null, null)
        {
            Roles = new[] { role },
            RaisedBy = raisedBy,
            SuspectedFalse = suspectedFalse ? true : (bool?)null,
            CitationNote = citationNote
        };

    private static IEnumerable<BenchmarkClaimVerification> Repeat(int count, Func<BenchmarkClaimVerification> item)
        => Enumerable.Range(0, count).Select(_ => item());

    private static readonly string[] ByA = { "A" };
    private static readonly string[] ByB = { "B" };

    /// <summary>
    /// An OpenAI candidate (run 1) and an Anthropic candidate (run 2), each with every charge on its
    /// first answer. Tallies, with overturned / upheld / indeterminate:
    ///   A vs OpenAI (same family):      1 / 9 / 1   — rate 0.1
    ///   A vs Anthropic (other family):  4 / 6 / 2   — rate 0.4
    ///   B vs OpenAI (other family):     3 / 7 / 1   — rate 0.3
    ///   B vs Anthropic (same family):   0 / 9 (+ <paramref name="extraUpheldForB"/>) / 0
    /// </summary>
    private static List<(string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs)> AuditFixture(int extraUpheldForB = 0)
    {
        var onOpenAI = new List<BenchmarkClaimVerification>();
        onOpenAI.Add(Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Supported, ByA));
        onOpenAI.AddRange(Repeat(9, () => Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Refuted, ByA)));
        onOpenAI.AddRange(Repeat(3, () => Charge(BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimVerdict.Supported, ByB)));
        onOpenAI.AddRange(Repeat(7, () => Charge(BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimVerdict.Refuted, ByB)));
        // Raised by both members: one charge in each member's tally.
        onOpenAI.Add(Charge(BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimVerdict.Indeterminate, new[] { "A", "B" }, suspectedFalse: true));
        // Not charges: an ordinary unverified claim, an assessor statement, and an item no member raised.
        onOpenAI.Add(Charge(BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimVerdict.Refuted, ByA));
        onOpenAI.Add(Charge(BenchmarkClaimRoles.AssessorStatement, BenchmarkClaimVerdict.Refuted, ByB));
        onOpenAI.Add(Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Supported, null));

        var onAnthropic = new List<BenchmarkClaimVerification>();
        onAnthropic.AddRange(Repeat(3, () => Charge(BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimVerdict.Supported, ByA)));
        // A refuted out-of-rubric basis overturns the member: the basis is its own statement.
        onAnthropic.Add(Charge(BenchmarkClaimRoles.OutOfRubricBasis, BenchmarkClaimVerdict.Refuted, ByA));
        onAnthropic.AddRange(Repeat(5, () => Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Refuted, ByA)));
        onAnthropic.Add(Charge(BenchmarkClaimRoles.OutOfRubricBasis, BenchmarkClaimVerdict.Supported, ByA));
        onAnthropic.Add(Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Indeterminate, ByA));
        // A citation note demotes a Supported verdict to Indeterminate.
        onAnthropic.Add(Charge(BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimVerdict.Supported, ByA,
            suspectedFalse: true, citationNote: "The cited function has no live call site."));
        onAnthropic.AddRange(Repeat(9 + extraUpheldForB, () => Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Refuted, ByB)));

        var openAiRun = PanelRun(1, OpenAI, Uniform(70, 70, reference: 70));
        openAiRun.Answers.First().ClaimVerificationJson = JsonSerializer.Serialize(onOpenAI);
        var anthropicRun = PanelRun(2, Anthropic, Uniform(70, 70, reference: 70));
        anthropicRun.Answers.First().ClaimVerificationJson = JsonSerializer.Serialize(onAnthropic);

        return new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            ("openai", "OpenAI candidate", new[] { openAiRun }),
            ("anthropic", "Anthropic candidate", new[] { anthropicRun })
        };
    }

    private static BenchmarkPanelAuditCell Cell(BenchmarkPanelDiagnosticsResult result, string member, string candidateProvider)
        => result.AccusationAudit.Single(c => c.Member == member && c.CandidateProvider == candidateProvider);

    [Fact]
    public void AccusationAudit_CountsEachMembersChargesByCandidateFamily()
    {
        var result = BenchmarkPanelDiagnostics.Compute(AuditFixture());

        Assert.Equal(4, result.AccusationAudit.Count);

        var aSame = Cell(result, BenchmarkPanelDiagnostics.MemberA, OpenAI);
        Assert.True(aSame.SameFamily);
        Assert.Equal(OpenAI, aSame.MemberProvider);
        Assert.Equal((11, 1, 9, 1), (aSame.Charges, aSame.Overturned, aSame.Upheld, aSame.Indeterminate));
        Assert.Equal(0.1, aSame.OverturnRate!.Value, 9);

        var aOther = Cell(result, BenchmarkPanelDiagnostics.MemberA, Anthropic);
        Assert.False(aOther.SameFamily);
        Assert.Equal((12, 4, 6, 2), (aOther.Charges, aOther.Overturned, aOther.Upheld, aOther.Indeterminate));
        Assert.Equal(0.4, aOther.OverturnRate!.Value, 9);

        var bOther = Cell(result, BenchmarkPanelDiagnostics.MemberB, OpenAI);
        Assert.False(bOther.SameFamily);
        Assert.Equal((11, 3, 7, 1), (bOther.Charges, bOther.Overturned, bOther.Upheld, bOther.Indeterminate));

        var bSame = Cell(result, BenchmarkPanelDiagnostics.MemberB, Anthropic);
        Assert.True(bSame.SameFamily);
        Assert.Equal(Anthropic, bSame.MemberProvider);
        Assert.Equal((9, 0, 9, 0), (bSame.Charges, bSame.Overturned, bSame.Upheld, bSame.Indeterminate));
        Assert.Equal(0.0, bSame.OverturnRate!.Value, 9);
    }

    [Fact]
    public void FamilyOverturnGap_IsOtherFamilyRateMinusSameFamilyRate_AtTenRuledChargesPerSide()
    {
        var result = BenchmarkPanelDiagnostics.Compute(AuditFixture());

        // Member A: 10 ruled on each side; 0.4 − 0.1.
        var a = result.AuditSummaries.Single(s => s.Member == BenchmarkPanelDiagnostics.MemberA);
        Assert.Equal(OpenAI, a.MemberProvider);
        Assert.Equal(0.3, a.FamilyOverturnGap!.Value, 9);

        // Member B: only 9 ruled on its own family, one short of the threshold.
        var b = result.AuditSummaries.Single(s => s.Member == BenchmarkPanelDiagnostics.MemberB);
        Assert.Null(b.FamilyOverturnGap);
        Assert.Equal(10, BenchmarkPanelDiagnostics.MinRuledChargesPerSide);
    }

    [Fact]
    public void FamilyOverturnGap_AppearsOnceTheTenthRuledChargeArrives()
    {
        var result = BenchmarkPanelDiagnostics.Compute(AuditFixture(extraUpheldForB: 1));

        // Member B: 0.3 on the other family, 0 of 10 on its own.
        var b = result.AuditSummaries.Single(s => s.Member == BenchmarkPanelDiagnostics.MemberB);
        Assert.Equal(0.3, b.FamilyOverturnGap!.Value, 9);
    }

    [Fact]
    public void AccusationAudit_IndeterminateVerdicts_DoNotCountTowardTheThreshold()
    {
        // Member B gets a tenth charge on its own family, but an indeterminate one.
        var entries = AuditFixture();
        var answer = entries[1].Runs[0].Answers.First();
        var items = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(answer.ClaimVerificationJson!)!;
        items.Add(Charge(BenchmarkClaimRoles.AccusedQuote, BenchmarkClaimVerdict.Indeterminate, ByB));
        answer.ClaimVerificationJson = JsonSerializer.Serialize(items);

        var result = BenchmarkPanelDiagnostics.Compute(entries);

        Assert.Equal(10, Cell(result, BenchmarkPanelDiagnostics.MemberB, Anthropic).Charges);
        Assert.Null(result.AuditSummaries.Single(s => s.Member == BenchmarkPanelDiagnostics.MemberB).FamilyOverturnGap);
    }

    [Fact]
    public void AccusationAudit_WithNoCharges_ReportsEmptyCells_AndNoRate()
    {
        var result = BenchmarkPanelDiagnostics.Compute(FamilyModel(gammaA: 6, gammaB: 6));

        Assert.Equal(4, result.AccusationAudit.Count);
        Assert.All(result.AccusationAudit, c =>
        {
            Assert.Equal(0, c.Charges);
            Assert.Null(c.OverturnRate);
        });
        Assert.All(result.AuditSummaries, s => Assert.Null(s.FamilyOverturnGap));
    }

    // --- Applicability ------------------------------------------------------------------------------

    private static void AssertNotApplicable(BenchmarkPanelDiagnosticsResult result, string reasonFragment)
    {
        Assert.False(result.Applicable);
        Assert.Contains(reasonFragment, result.NotApplicableReason);
        Assert.Null(result.MemberALabel);
        Assert.Null(result.MemberBLabel);
        Assert.Empty(result.Entries);
        Assert.Empty(result.JudgeDependentPairs);
        Assert.Empty(result.ReferenceDependentPairs);
        Assert.Empty(result.FamilyGaps);
        Assert.Empty(result.AccusationAudit);
        Assert.Empty(result.AuditSummaries);
        Assert.Empty(result.Caveats);
    }

    [Fact]
    public void NotApplicable_ForSingleAssessorRuns()
    {
        var entries = FamilyModel(gammaA: 6, gammaB: 6);
        foreach (var run in entries.SelectMany(e => e.Runs))
        {
            run.CoAssessorModelConfigurationId = null;
            run.CoAssessorModelSnapshot = null;
        }

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "Every run must be a panel run; 2 runs (1, 2)");
    }

    [Fact]
    public void NotApplicable_WhenAnyOneRunHadASingleAssessor()
    {
        var entries = FamilyModel(gammaA: 6, gammaB: 6);
        entries[1].Runs[0].CoAssessorModelConfigurationId = null;

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "1 run (2) had a single assessor");
    }

    [Fact]
    public void NotApplicable_WhenMemberBDiffersBetweenRuns()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, OpenAI, Uniform(70, 70))),
            Entry("b", PanelRun(2, Anthropic, Uniform(70, 70), memberB: MemberB("anthropic-judge-2")))
        };

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "member B of run 2 differs from that of run 1");
    }

    [Fact]
    public void NotApplicable_WhenMemberBsOutputCapDiffersBetweenRuns()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, OpenAI, Uniform(70, 70))),
            Entry("b", PanelRun(2, Anthropic, Uniform(70, 70), coAssessorCap: 32000))
        };

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "member B");
    }

    [Fact]
    public void NotApplicable_WhenMemberADiffersBetweenRuns()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, OpenAI, Uniform(70, 70))),
            Entry("b", PanelRun(2, Anthropic, Uniform(70, 70), memberA: MemberA(thinkingLevel: "high")))
        };

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "member A of run 2 differs from that of run 1");
    }

    [Fact]
    public void NotApplicable_WhenTheReferenceReaderDiffersBetweenRuns()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, OpenAI, Uniform(70, 70, reference: 70))),
            Entry("b", PanelRun(2, Anthropic, Uniform(70, 70, reference: 70), reader: Reader("google-reader-2")))
        };

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "the reference reader");
    }

    [Fact]
    public void Applicable_WhenOnlySomeRunsCarryAReferenceReader()
    {
        // The reader gate compares the runs that have one; a run without one does not break it.
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, OpenAI, Uniform(70, 70, reference: 70))),
            Entry("b", PanelRun(2, Anthropic, Uniform(70, 70), withReader: false))
        };

        Assert.True(BenchmarkPanelDiagnostics.Compute(entries).Applicable);
    }

    [Fact]
    public void NotApplicable_WithNoRuns()
    {
        AssertNotApplicable(
            BenchmarkPanelDiagnostics.Compute(new List<(string, string, IReadOnlyList<BenchmarkRun>)>()),
            "The comparison holds no runs.");
    }

    [Fact]
    public void NotApplicable_WhenNoEntryHasACandidateProvider()
    {
        var entries = new List<(string, string, IReadOnlyList<BenchmarkRun>)>
        {
            Entry("a", PanelRun(1, " ", Uniform(70, 70)))
        };

        AssertNotApplicable(BenchmarkPanelDiagnostics.Compute(entries), "No entry has a candidate provider");
    }
}
