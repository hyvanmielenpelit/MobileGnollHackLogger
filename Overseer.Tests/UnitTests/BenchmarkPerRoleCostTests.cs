namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The per-role split of a benchmark run's cost: that the finalizer rolls the four grading buckets up
/// without pooling them, that one function costs all five roles and derives both subtotals, that the
/// mid-run figure and the finalized one come out equal, and that a run recorded before any of this
/// existed still costs exactly what it always did.
/// </summary>
public class BenchmarkPerRoleCostTests
{
    // Four deliberately different cards, so a role costed on the wrong one produces a different number.
    private static readonly ModelPricing CandidateCard =
        new(10.00m, 50.00m, CachedInputPerMillion: 1.00m, CacheWritePerMillion: 12.50m);

    private static readonly ModelPricing AssessorCard =
        new(3.00m, 15.00m, CachedInputPerMillion: 0.30m, CacheWritePerMillion: 3.75m);

    private static readonly ModelPricing SecondOpinionCard =
        new(1.00m, 5.00m, CachedInputPerMillion: 0.10m, CacheWritePerMillion: 1.25m);

    private static readonly ModelPricing VerifierCard =
        new(2.00m, 8.00m, CachedInputPerMillion: 0.20m, CacheWritePerMillion: 2.50m);

    private static BenchmarkRunPricing Pricing() => new(
        Candidate: CandidateCard,
        Assessor: AssessorCard,
        ClaimVerifier: VerifierCard,
        SecondOpinion: SecondOpinionCard);

    /// <summary>One graded answer, with every grading role's usage populated and all four distinct.</summary>
    private static BenchmarkRunAnswer GradedAnswer(int orderIndex) => new()
    {
        OrderIndex = orderIndex,
        Status = BenchmarkAnswerStatus.Ok,
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        AnswerText = "An answer.",
        QualityScore = 80,
        SpeedScore = 70,
        Difficulty = BenchmarkDifficulty.Intermediate,
        AssessedDifficulty = 50,
        DurationMs = 4_000,

        InputTokens = 120_000,
        OutputTokens = 12_000,
        CacheReadInputTokens = 60_000,
        CacheCreationInputTokens = 6_000,

        AssessmentInputTokens = 50_000,
        AssessmentOutputTokens = 5_000,
        AssessmentCacheReadTokens = 40_000,
        AssessmentCacheCreationTokens = 4_000,
        AssessmentDurationMs = 900,

        SecondOpinionInputTokens = 30_000,
        SecondOpinionOutputTokens = 3_000,
        SecondOpinionCacheReadTokens = 20_000,
        SecondOpinionCacheCreationTokens = 2_000,
        SecondOpinionDurationMs = 700,

        ClaimVerificationInputTokens = 10_000,
        ClaimVerificationOutputTokens = 1_000,
        ClaimVerificationCacheReadTokens = 8_000,
        ClaimVerificationCacheCreationTokens = 800,
        ClaimVerificationDurationMs = 500
    };

    private static List<BenchmarkRunAnswer> GradedAnswers() =>
        new() { GradedAnswer(1), GradedAnswer(2) };

    // ---------------------------------------------------------------------------------------------
    // The rollup.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ApplyTotals_RollsTheFourGradingBucketsUpWithoutPoolingThem()
    {
        // The synthesis figures are run-level and are written elsewhere; the rollup must leave them alone.
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 2,
            TotalSynthesisInputTokens = 70_000,
            TotalSynthesisOutputTokens = 7_000,
            TotalSynthesisCacheReadTokens = 5_000,
            TotalSynthesisCacheCreationTokens = 500,
            TotalSynthesisDurationMs = 3_300
        });

        var answers = GradedAnswers();

        BenchmarkRunFinalizer.ApplyTotals(run, answers);

        Assert.Equal(100_000, run.TotalAssessmentInputTokens);
        Assert.Equal(10_000, run.TotalAssessmentOutputTokens);
        Assert.Equal(80_000, run.TotalAssessmentCacheReadTokens);
        Assert.Equal(8_000, run.TotalAssessmentCacheCreationTokens);
        Assert.Equal(1_800, run.TotalAssessmentDurationMs);

        // The second opinion is a separate assessor call and lands in its own columns only.
        Assert.Equal(60_000, run.TotalSecondOpinionInputTokens);
        Assert.Equal(6_000, run.TotalSecondOpinionOutputTokens);
        Assert.Equal(40_000, run.TotalSecondOpinionCacheReadTokens);
        Assert.Equal(4_000, run.TotalSecondOpinionCacheCreationTokens);
        Assert.Equal(1_400, run.TotalSecondOpinionDurationMs);

        Assert.Equal(20_000, run.TotalClaimVerificationInputTokens);
        Assert.Equal(2_000, run.TotalClaimVerificationOutputTokens);
        Assert.Equal(16_000, run.TotalClaimVerificationCacheReadTokens);
        Assert.Equal(1_600, run.TotalClaimVerificationCacheCreationTokens);
        Assert.Equal(1_000, run.TotalClaimVerificationDurationMs);

        Assert.Equal(70_000, run.TotalSynthesisInputTokens);
        Assert.Equal(7_000, run.TotalSynthesisOutputTokens);
        Assert.Equal(5_000, run.TotalSynthesisCacheReadTokens);
        Assert.Equal(500, run.TotalSynthesisCacheCreationTokens);
        Assert.Equal(3_300, run.TotalSynthesisDurationMs);
    }

    // ---------------------------------------------------------------------------------------------
    // Costing.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputeRunRoleCosts_LegacyRunWithZeroesInEveryNewColumn_CostsWhatItAlwaysDid()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalInputTokens = 1_000_000,
            TotalOutputTokens = 100_000,
            TotalCacheReadTokens = 400_000,
            TotalCacheCreationTokens = 50_000,
            TotalAssessmentInputTokens = 200_000,
            TotalAssessmentOutputTokens = 20_000,
            TotalClaimVerificationInputTokens = 50_000,
            TotalClaimVerificationOutputTokens = 5_000
        });

        var costs = ModelPricingService.ComputeRunRoleCosts(run, Pricing());

        decimal candidate = ModelPricingService.ComputeCostFromTotals(
            CandidateCard, 1_000_000, 100_000, 400_000, 50_000);
        decimal assessor = ModelPricingService.ComputeCost(AssessorCard, 200_000, 20_000);
        decimal verifier = ModelPricingService.ComputeCost(VerifierCard, 50_000, 5_000);

        Assert.Equal(candidate, costs.Candidate);
        Assert.Equal(assessor, costs.Assessor);
        Assert.Equal(verifier, costs.ClaimVerifier);
        Assert.Equal(0m, costs.SecondOpinion);
        Assert.Equal(0m, costs.Synthesis);
        Assert.Equal(assessor + verifier, costs.Grading);
        Assert.Equal(candidate + assessor + verifier, costs.Total);
        Assert.False(costs.Incomplete);
        Assert.Equal("catalog", costs.Source);
    }

    [Fact]
    public void ComputeRunRoleCosts_GradingRoleTokens_AreChargedOnceEach()
    {
        // Each Total*InputTokens column is a total prompt figure that already contains the cache reads
        // and cache writes stored beside it, so the three must partition rather than overlap.
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalAssessmentInputTokens = 1_000_000,
            TotalAssessmentOutputTokens = 100_000,
            TotalAssessmentCacheReadTokens = 400_000,
            TotalAssessmentCacheCreationTokens = 50_000
        });

        var costs = ModelPricingService.ComputeRunRoleCosts(run, Pricing());

        decimal expected = (550_000 / 1_000_000m * 3.00m)     // prompt less cache reads and cache writes
            + (100_000 / 1_000_000m * 15.00m)                 // output
            + (400_000 / 1_000_000m * 0.30m)                  // cache reads, at the cached rate
            + (50_000 / 1_000_000m * 3.75m);                  // cache writes, at the write rate

        Assert.Equal(expected, costs.Assessor);

        // Charging the whole prompt at the base rate as well would be the cache double-count.
        Assert.True(
            costs.Assessor < ModelPricingService.ComputeCost(
                AssessorCard, 1_000_000, 100_000, 400_000, 50_000));
    }

    [Fact]
    public void ComputeRunRoleCosts_SumsFiveRolesIntoItsTotal_AndGradingIsTheLastFour()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun());
        BenchmarkRunFinalizer.ApplyTotals(run, GradedAnswers());
        run.TotalSynthesisInputTokens = 40_000;
        run.TotalSynthesisOutputTokens = 4_000;

        var costs = ModelPricingService.ComputeRunRoleCosts(run, Pricing());

        Assert.All(
            new[] { costs.Candidate, costs.Assessor, costs.SecondOpinion, costs.ClaimVerifier, costs.Synthesis },
            amount => Assert.True(amount > 0m));

        Assert.Equal(costs.Assessor + costs.SecondOpinion + costs.ClaimVerifier + costs.Synthesis, costs.Grading);
        Assert.Equal(costs.Candidate + costs.Grading, costs.Total);
        Assert.False(costs.Incomplete);
    }

    /// <summary>
    /// The regression guard for the whole per-role split: the figures the run detail reports while a run
    /// is still executing are summed from the answer rows, and the figures it reports afterwards come
    /// from the columns the finalizer wrote. Both go through one costing function, and on a run whose
    /// answers are all in they must produce the same numbers.
    /// </summary>
    [Fact]
    public void ComputeRunRoleCosts_LiveRecomputationAndTheFinalizedRun_Agree()
    {
        var answers = GradedAnswers();
        const long synthesisInput = 40_000;
        const long synthesisOutput = 4_000;

        var finalized = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalSynthesisInputTokens = synthesisInput,
            TotalSynthesisOutputTokens = synthesisOutput
        });
        BenchmarkRunFinalizer.ApplyTotals(finalized, answers);

        // The mid-run path: the same sums, while the run's own columns are still zero.
        var candidate = BenchmarkRunFinalizer.ComputeCandidateTotals(answers);
        var longContext = BenchmarkRunFinalizer.ComputeCandidateLongContextTotals(answers);
        var grading = BenchmarkRunFinalizer.SumGradingTotals(answers);

        var live = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalInputTokens = candidate.TotalInputTokens,
            TotalOutputTokens = candidate.TotalOutputTokens,
            TotalCacheReadTokens = candidate.TotalCacheReadTokens,
            TotalCacheCreationTokens = candidate.TotalCacheCreationTokens,
            TotalAnswerDurationMs = candidate.TotalAnswerDurationMs,

            TotalLongContextInputTokens = longContext.TotalLongContextInputTokens,
            TotalLongContextOutputTokens = longContext.TotalLongContextOutputTokens,
            TotalLongContextCacheReadTokens = longContext.TotalLongContextCacheReadTokens,
            TotalLongContextCacheCreationTokens = longContext.TotalLongContextCacheCreationTokens,

            TotalAssessmentInputTokens = grading.TotalAssessmentInputTokens,
            TotalAssessmentOutputTokens = grading.TotalAssessmentOutputTokens,
            TotalAssessmentCacheReadTokens = grading.TotalAssessmentCacheReadTokens,
            TotalAssessmentCacheCreationTokens = grading.TotalAssessmentCacheCreationTokens,
            TotalAssessmentDurationMs = grading.TotalAssessmentDurationMs,

            TotalSecondOpinionInputTokens = grading.TotalSecondOpinionInputTokens,
            TotalSecondOpinionOutputTokens = grading.TotalSecondOpinionOutputTokens,
            TotalSecondOpinionCacheReadTokens = grading.TotalSecondOpinionCacheReadTokens,
            TotalSecondOpinionCacheCreationTokens = grading.TotalSecondOpinionCacheCreationTokens,
            TotalSecondOpinionDurationMs = grading.TotalSecondOpinionDurationMs,

            TotalClaimVerificationInputTokens = grading.TotalClaimVerificationInputTokens,
            TotalClaimVerificationOutputTokens = grading.TotalClaimVerificationOutputTokens,
            TotalClaimVerificationCacheReadTokens = grading.TotalClaimVerificationCacheReadTokens,
            TotalClaimVerificationCacheCreationTokens = grading.TotalClaimVerificationCacheCreationTokens,
            TotalClaimVerificationDurationMs = grading.TotalClaimVerificationDurationMs,

            TotalSynthesisInputTokens = synthesisInput,
            TotalSynthesisOutputTokens = synthesisOutput
        });

        Assert.Equal(
            ModelPricingService.ComputeRunRoleCosts(finalized, Pricing()),
            ModelPricingService.ComputeRunRoleCosts(live, Pricing()));
    }

    [Fact]
    public void ComputeRunRoleCosts_PricesTheSynthesisOnTheAssessorsCard()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalSynthesisInputTokens = 200_000,
            TotalSynthesisOutputTokens = 20_000,
            TotalSynthesisCacheReadTokens = 10_000,
            TotalSynthesisCacheCreationTokens = 1_000
        });

        var costs = ModelPricingService.ComputeRunRoleCosts(run, Pricing());

        Assert.Equal(
            ModelPricingService.ComputeCostFromTotals(AssessorCard, 200_000, 20_000, 10_000, 1_000),
            costs.Synthesis);

        // Not the second opinion's card, which is the other assessor-shaped role and a different rate.
        Assert.NotEqual(
            ModelPricingService.ComputeCostFromTotals(SecondOpinionCard, 200_000, 20_000, 10_000, 1_000),
            costs.Synthesis);

        // The per-question assessments are a peer of the synthesis, so this run's assessor figure is nil.
        Assert.Equal(0m, costs.Assessor);
        Assert.Equal(costs.Synthesis, costs.Grading);
    }

    /// <summary>
    /// A total that cannot include every role is not printed. Overseer prices in one currency — the model
    /// catalog publishes USD and carries no currency field — so the only way a run reaches this state is
    /// a role that spent tokens against no resolved card, and the representation is the same either way:
    /// the per-role figures stand, the total is suppressed.
    /// </summary>
    [Fact]
    public void ComputeRunRoleCosts_WhenARoleThatSpentTokensHasNoCard_SuppressesTheTotal()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalInputTokens = 500_000,
            TotalOutputTokens = 50_000,
            TotalSecondOpinionInputTokens = 100_000,
            TotalSecondOpinionOutputTokens = 10_000
        });

        var pricing = new BenchmarkRunPricing(
            Candidate: CandidateCard,
            Assessor: AssessorCard,
            ClaimVerifier: VerifierCard,
            SecondOpinion: null);

        var costs = ModelPricingService.ComputeRunRoleCosts(run, pricing);

        Assert.True(costs.Incomplete);
        Assert.Equal(0m, costs.Total);

        // The roles that did resolve still report their own figures.
        Assert.True(costs.Candidate > 0m);
        Assert.Equal(0m, costs.SecondOpinion);
    }

    [Fact]
    public void ComputeRunRoleCosts_SourceIsMixedWhenOneParticipatingRoleIsOverridden()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalInputTokens = 500_000,
            TotalOutputTokens = 50_000,
            TotalAssessmentInputTokens = 100_000,
            TotalAssessmentOutputTokens = 10_000
        });

        var custom = AssessorCard with { Source = ModelPricingSource.Custom };
        var pricing = new BenchmarkRunPricing(
            Candidate: CandidateCard,
            Assessor: custom,
            ClaimVerifier: VerifierCard,
            SecondOpinion: SecondOpinionCard);

        // The claim verifier spent nothing, so its catalog card is not part of the question.
        Assert.Equal("mixed", ModelPricingService.ComputeRunRoleCosts(run, pricing).Source);

        var allCustom = new BenchmarkRunPricing(
            Candidate: CandidateCard with { Source = ModelPricingSource.Custom },
            Assessor: custom,
            ClaimVerifier: VerifierCard,
            SecondOpinion: SecondOpinionCard);

        Assert.Equal("custom", ModelPricingService.ComputeRunRoleCosts(run, allCustom).Source);
    }

    [Fact]
    public void ComputeRunRoleCosts_SynthesisAloneStillResolvesTheAssessorsSource()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            TotalInputTokens = 500_000,
            TotalOutputTokens = 50_000,
            TotalSynthesisInputTokens = 100_000,
            TotalSynthesisOutputTokens = 10_000
        });

        var pricing = new BenchmarkRunPricing(
            Candidate: CandidateCard,
            Assessor: AssessorCard with { Source = ModelPricingSource.Custom },
            ClaimVerifier: VerifierCard,
            SecondOpinion: SecondOpinionCard);

        // No per-question assessment tokens, yet the assessor's card priced the synthesis and so counts.
        Assert.Equal("mixed", ModelPricingService.ComputeRunRoleCosts(run, pricing).Source);
    }

    // ---------------------------------------------------------------------------------------------
    // The panel's member B and its own synthesis.
    // ---------------------------------------------------------------------------------------------

    private static readonly ModelPricing CoAssessorCard =
        new(5.00m, 25.00m, CachedInputPerMillion: 0.50m, CacheWritePerMillion: 6.25m);

    private static BenchmarkRunPricing PanelPricing(ModelPricing? coAssessor) => new(
        Candidate: CandidateCard,
        Assessor: AssessorCard,
        ClaimVerifier: VerifierCard,
        SecondOpinion: SecondOpinionCard,
        CoAssessor: coAssessor);

    private static BenchmarkRun PanelRunWithCoAssessorSpend() => BenchmarkModelSnapshots.Attach(new BenchmarkRun
    {
        CoAssessorModelConfigurationId = 9,
        TotalInputTokens = 500_000,
        TotalOutputTokens = 50_000,
        TotalAssessmentInputTokens = 200_000,
        TotalAssessmentOutputTokens = 20_000,
        TotalSynthesisInputTokens = 40_000,
        TotalSynthesisOutputTokens = 4_000,
        TotalCoAssessmentInputTokens = 300_000,
        TotalCoAssessmentOutputTokens = 30_000,
        TotalCoAssessmentCacheReadTokens = 100_000,
        TotalCoAssessmentCacheCreationTokens = 10_000,
        TotalCoSynthesisInputTokens = 60_000,
        TotalCoSynthesisOutputTokens = 6_000,
        TotalCoSynthesisCacheReadTokens = 20_000,
        TotalCoSynthesisCacheCreationTokens = 2_000
    });

    [Fact]
    public void ComputeRunRoleCosts_PricesTheCoAssessorAndItsSynthesisOnTheCoAssessorsCard()
    {
        var run = PanelRunWithCoAssessorSpend();

        var costs = ModelPricingService.ComputeRunRoleCosts(run, PanelPricing(CoAssessorCard));

        decimal coAssessor = ModelPricingService.ComputeCostFromTotals(CoAssessorCard, 300_000, 30_000, 100_000, 10_000);
        decimal coSynthesis = ModelPricingService.ComputeCostFromTotals(CoAssessorCard, 60_000, 6_000, 20_000, 2_000);

        Assert.Equal(coAssessor, costs.CoAssessor);
        Assert.Equal(coSynthesis, costs.CoSynthesis);

        // Not member A's card, which prices member A and member A's synthesis.
        Assert.NotEqual(
            ModelPricingService.ComputeCostFromTotals(AssessorCard, 300_000, 30_000, 100_000, 10_000),
            costs.CoAssessor);
        Assert.Equal(ModelPricingService.ComputeCostFromTotals(AssessorCard, 200_000, 20_000, 0, 0), costs.Assessor);
        Assert.Equal(ModelPricingService.ComputeCostFromTotals(AssessorCard, 40_000, 4_000, 0, 0), costs.Synthesis);

        Assert.Equal(
            costs.Assessor + costs.SecondOpinion + costs.ClaimVerifier + costs.Synthesis + costs.CoAssessor + costs.CoSynthesis,
            costs.Grading);
        Assert.Equal(costs.Candidate + costs.Grading, costs.Total);
        Assert.False(costs.Incomplete);
    }

    [Fact]
    public void ComputeRunRoleCostBreakdowns_CarryTheCoAssessorRolesAsTheirOwnBuckets()
    {
        var run = PanelRunWithCoAssessorSpend();

        var breakdowns = ModelPricingService.ComputeRunRoleCostBreakdowns(run, PanelPricing(CoAssessorCard));
        var costs = ModelPricingService.ComputeRunRoleCosts(run, PanelPricing(CoAssessorCard));

        Assert.Equal(costs.CoAssessor, breakdowns.CoAssessor.Total);
        Assert.Equal(costs.CoSynthesis, breakdowns.CoSynthesis.Total);
        Assert.True(breakdowns.CoAssessor.CacheRead > 0m);
    }

    [Fact]
    public void ComputeRunRoleCosts_WhenTheCoAssessorSpentTokensWithNoCard_IsIncomplete()
    {
        var run = PanelRunWithCoAssessorSpend();

        var costs = ModelPricingService.ComputeRunRoleCosts(run, PanelPricing(coAssessor: null));

        Assert.True(costs.Incomplete);
        Assert.Equal(0m, costs.Total);
        Assert.Equal(0m, costs.CoAssessor);
        Assert.Equal(0m, costs.CoSynthesis);

        // The roles that did resolve still report their own figures.
        Assert.True(costs.Candidate > 0m);
        Assert.True(costs.Assessor > 0m);
    }

    [Fact]
    public void ComputeRunRoleCosts_CoSynthesisSpendAloneWithNoCard_IsIncomplete()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            CoAssessorModelConfigurationId = 9,
            TotalInputTokens = 500_000,
            TotalOutputTokens = 50_000,
            TotalCoSynthesisInputTokens = 60_000,
            TotalCoSynthesisOutputTokens = 6_000
        });

        var costs = ModelPricingService.ComputeRunRoleCosts(run, PanelPricing(coAssessor: null));

        Assert.True(costs.Incomplete);
        Assert.Equal(0m, costs.Total);
    }

    [Fact]
    public void ComputeRunRoleCosts_SingleAssessorRun_HasZeroCoAssessorRoles_AndNeedsNoCoAssessorCard()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun());
        BenchmarkRunFinalizer.ApplyTotals(run, GradedAnswers());

        var withoutCard = ModelPricingService.ComputeRunRoleCosts(run, Pricing());
        var withCard = ModelPricingService.ComputeRunRoleCosts(run, PanelPricing(CoAssessorCard));

        Assert.Equal(0m, withoutCard.CoAssessor);
        Assert.Equal(0m, withoutCard.CoSynthesis);
        Assert.False(withoutCard.Incomplete);

        // A card with nothing to price changes nothing, not even the source.
        Assert.Equal(withoutCard, withCard);
    }

    [Fact]
    public void ComputeRunRoleCosts_SourceIsMixed_WhenOnlyTheCoAssessorsCardIsOverridden()
    {
        var run = PanelRunWithCoAssessorSpend();

        var costs = ModelPricingService.ComputeRunRoleCosts(
            run, PanelPricing(CoAssessorCard with { Source = ModelPricingSource.Custom }));

        Assert.Equal("mixed", costs.Source);
    }

    [Fact]
    public void GroupCostRoles_NameTheCoAssessorAndItsSynthesisApart()
    {
        Assert.Equal("Co-assessor", BenchmarkGroupAnalysisService.CoAssessorRole);
        Assert.Equal("Co-assessor synthesis", BenchmarkGroupAnalysisService.CoSynthesisRole);
        Assert.NotEqual(BenchmarkGroupAnalysisService.AssessorRole, BenchmarkGroupAnalysisService.CoAssessorRole);
        Assert.NotEqual(BenchmarkGroupAnalysisService.SynthesisRole, BenchmarkGroupAnalysisService.CoSynthesisRole);
    }

    /// <summary>A pricing service that returns one fixed card set for every run.</summary>
    private sealed class FixedPricingService : ModelPricingService
    {
        private readonly BenchmarkRunPricing _pricing;

        public FixedPricingService(ApplicationDbContext db, BenchmarkRunPricing pricing)
            : base(new ModelMetadataService(), db)
        {
            _pricing = pricing;
        }

        public override Task<BenchmarkRunPricing> ResolveForRunAsync(BenchmarkRun run) => Task.FromResult(_pricing);
    }

    [Fact]
    public async Task GroupCostRoles_KeyTheReaderAsReferenceReaderInAPanelRun_AndSecondReaderOtherwise()
    {
        Assert.Equal("Second reader", BenchmarkGroupAnalysisService.SecondOpinionRole);
        Assert.Equal("Reference reader", BenchmarkGroupAnalysisService.ReferenceReaderRole);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var single = BenchmarkModelSnapshots.Attach(new BenchmarkRun { Id = 1 });
        BenchmarkRunFinalizer.ApplyTotals(single, GradedAnswers());
        var panel = BenchmarkModelSnapshots.Attach(new BenchmarkRun { Id = 2, CoAssessorModelConfigurationId = 9 });
        BenchmarkRunFinalizer.ApplyTotals(panel, GradedAnswers());

        var costs = await BenchmarkGroupAnalysisService.ResolveCostsAsync(
            new FixedPricingService(db, Pricing()),
            NullLogger<BenchmarkGroupAnalysisService>.Instance,
            new List<BenchmarkRun> { single, panel });

        var singleRoles = costs.Single(c => c.RunId == 1).CostByRole;
        var panelRoles = costs.Single(c => c.RunId == 2).CostByRole;

        Assert.True(singleRoles[BenchmarkGroupAnalysisService.SecondOpinionRole] > 0);
        Assert.False(singleRoles.ContainsKey(BenchmarkGroupAnalysisService.ReferenceReaderRole));
        Assert.True(panelRoles[BenchmarkGroupAnalysisService.ReferenceReaderRole] > 0);
        Assert.False(panelRoles.ContainsKey(BenchmarkGroupAnalysisService.SecondOpinionRole));
        Assert.Equal(
            singleRoles[BenchmarkGroupAnalysisService.SecondOpinionRole],
            panelRoles[BenchmarkGroupAnalysisService.ReferenceReaderRole]);
    }
}
