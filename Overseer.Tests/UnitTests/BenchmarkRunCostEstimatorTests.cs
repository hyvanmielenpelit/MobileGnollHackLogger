namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// <see cref="BenchmarkRunCostEstimator"/>: a running run is costed from its answer rows and a finished
/// one from its finalized columns, both to the same figures; a role that spent nothing reports null;
/// and a role that spent tokens against no card suppresses the total.
/// </summary>
public class BenchmarkRunCostEstimatorTests
{
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

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>One answer with candidate, assessor and second-opinion usage; no claim verification.</summary>
    private static BenchmarkRunAnswer Answer(int orderIndex) => new()
    {
        OrderIndex = orderIndex,
        Status = BenchmarkAnswerStatus.Ok,
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        AnswerText = "An answer.",
        DurationMs = 4_000,

        InputTokens = 120_000,
        OutputTokens = 12_000,
        CacheReadInputTokens = 60_000,
        CacheCreationInputTokens = 6_000,

        AssessmentInputTokens = 50_000,
        AssessmentOutputTokens = 5_000,
        AssessmentCacheReadTokens = 40_000,
        AssessmentCacheCreationTokens = 4_000,

        SecondOpinionInputTokens = 30_000,
        SecondOpinionOutputTokens = 3_000
    };

    private static List<BenchmarkRunAnswer> Answers() => new() { Answer(1), Answer(2) };

    [Fact]
    public async Task RunningRun_IsCostedFromItsAnswers_AndMatchesTheSameRunFinalized()
    {
        await using var db = CreateDbContext();
        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(db, Pricing()));

        // Mid-run the run-level columns are still zero; only the answer rows carry usage.
        var running = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 1,
            Status = BenchmarkRunStatus.Running,
            Answers = Answers()
        });

        var finalized = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 2,
            Status = BenchmarkRunStatus.Completed
        });
        BenchmarkRunFinalizer.ApplyTotals(finalized, Answers());

        var live = await estimator.EstimateAsync(running);
        var done = await estimator.EstimateAsync(finalized);

        Assert.Equal(done, live);
        Assert.NotNull(live.Total);
        Assert.True(live.Total > 0m);
        Assert.True(live.Candidate > 0m);
        Assert.True(live.Assessor > 0m);
        Assert.True(live.SecondOpinion > 0m);
        Assert.Equal(live.Candidate + live.Grading, live.Total);
        Assert.False(live.PricingIncomplete);
        Assert.Equal("catalog", live.PricingSource);
    }

    [Fact]
    public async Task CompletedRun_IsCostedFromItsFinalizedColumns_NotItsAnswers()
    {
        await using var db = CreateDbContext();
        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(db, Pricing()));

        // Answers loaded, columns zero: a finished run reads the columns, so nothing is costed.
        var completed = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 1,
            Status = BenchmarkRunStatus.Completed,
            Answers = Answers()
        });

        var estimate = await estimator.EstimateAsync(completed);

        Assert.Equal(0m, estimate.Candidate);
        Assert.Equal(0m, estimate.Total);
        Assert.Same(completed, BenchmarkRunCostEstimator.TotalsOf(completed));
    }

    [Fact]
    public async Task RolesThatSpentNothing_ReportNull_AndGradingIsNullWithoutAnyGradingRole()
    {
        await using var db = CreateDbContext();
        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(db, Pricing()));

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 1,
            Status = BenchmarkRunStatus.Completed,
            TotalInputTokens = 500_000,
            TotalOutputTokens = 50_000
        });

        var estimate = await estimator.EstimateAsync(run);

        Assert.True(estimate.Candidate > 0m);
        Assert.Null(estimate.Assessor);
        Assert.Null(estimate.SecondOpinion);
        Assert.Null(estimate.ClaimVerifier);
        Assert.Null(estimate.Synthesis);
        Assert.Null(estimate.CoAssessor);
        Assert.Null(estimate.CoSynthesis);
        Assert.Null(estimate.Grading);
        Assert.Equal(estimate.Candidate, estimate.Total);
    }

    [Fact]
    public async Task IncompletePricing_GivesANullTotal_AndKeepsTheRolesThatResolved()
    {
        await using var db = CreateDbContext();
        var pricing = new BenchmarkRunPricing(
            Candidate: CandidateCard,
            Assessor: AssessorCard,
            ClaimVerifier: VerifierCard,
            SecondOpinion: null);
        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(db, pricing));

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 1,
            Status = BenchmarkRunStatus.Running,
            Answers = Answers()
        });

        var estimate = await estimator.EstimateAsync(run);

        Assert.True(estimate.PricingIncomplete);
        Assert.Null(estimate.Total);
        Assert.Null(estimate.SecondOpinion);
        Assert.True(estimate.Candidate > 0m);
        Assert.True(estimate.Assessor > 0m);
    }
}
