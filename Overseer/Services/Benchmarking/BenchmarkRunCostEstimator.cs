namespace Overseer.Services.Benchmarking;

using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services;

/// <summary>
/// One run's estimated cost, per role and in total, in US dollars. A role that spent nothing, or whose
/// price card did not resolve, is null: zero is a measurement and absence is not.
/// </summary>
public sealed record BenchmarkRunCostEstimate
{
    /// <summary>Every role; null when a role that spent tokens has no price card.</summary>
    public decimal? Total { get; init; }

    public decimal? Candidate { get; init; }
    public decimal? Assessor { get; init; }

    /// <summary>Panel member B.</summary>
    public decimal? CoAssessor { get; init; }

    /// <summary>The second or reference reader.</summary>
    public decimal? SecondOpinion { get; init; }

    public decimal? ClaimVerifier { get; init; }

    /// <summary>The assessor's synthesis, priced on the assessor's card.</summary>
    public decimal? Synthesis { get; init; }

    /// <summary>Panel member B's synthesis, priced on the co-assessor's card.</summary>
    public decimal? CoSynthesis { get; init; }

    /// <summary>Every grading role together; null when none of them has a figure.</summary>
    public decimal? Grading { get; init; }

    /// <summary><c>catalog</c>, <c>custom</c> or <c>mixed</c>; null when no card resolved.</summary>
    public string? PricingSource { get; init; }

    public bool PricingIncomplete { get; init; }

    /// <summary>The roles that spent tokens and had no price card.</summary>
    public BenchmarkCostRoles UnpricedRoles { get; init; }
}

/// <summary>The roles a benchmark run's cost is split across.</summary>
[Flags]
public enum BenchmarkCostRoles
{
    None = 0,
    Candidate = 1,
    Assessor = 2,
    CoAssessor = 4,
    SecondOpinion = 8,
    ClaimVerifier = 16,
    Synthesis = 32,
    CoSynthesis = 64
}

/// <summary>
/// Estimates a run's cost per role: mid-run from its answer rows, otherwise from its finalized
/// columns, on the run's own pricing snapshot. The arithmetic is
/// <see cref="ModelPricingService.ComputeRunRoleCosts"/>; this class adds which totals it is fed and
/// which roles report a figure at all.
/// </summary>
public class BenchmarkRunCostEstimator
{
    private readonly ModelPricingService? _pricingService;

    public BenchmarkRunCostEstimator(ModelPricingService? pricingService = null)
    {
        _pricingService = pricingService;
    }

    /// <summary>
    /// The totals a run is costed from: for a running run, a detached copy summed from its answer
    /// rows (which must be loaded), because the finalizer writes the run-level totals only at the
    /// end; otherwise the run itself.
    /// </summary>
    public static BenchmarkRun TotalsOf(BenchmarkRun run)
        => run.Status is BenchmarkRunStatus.Running ? BuildLiveTotals(run) : run;

    /// <summary>
    /// Estimates <paramref name="run"/> with its answers loaded: the totals are <see cref="TotalsOf"/>
    /// and the served tier is resolved from those answers.
    /// </summary>
    public Task<BenchmarkRunCostEstimate> EstimateAsync(BenchmarkRun run)
        => EstimateAsync(run, TotalsOf(run), BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers));

    /// <summary>
    /// Estimates <paramref name="run"/> from <paramref name="totals"/>, which is <see cref="TotalsOf"/>
    /// of it. <paramref name="servedServiceTier"/> is the tier the provider served the candidate at,
    /// what the candidate is billed at; null falls back to the requested tier and then to 1.0x.
    /// </summary>
    public async Task<BenchmarkRunCostEstimate> EstimateAsync(BenchmarkRun run, BenchmarkRun totals, string? servedServiceTier)
    {
        BenchmarkRunPricing? pricing = null;
        if (_pricingService != null)
        {
            pricing = await _pricingService.ResolveForRunAsync(run);
        }

        var costs = pricing != null
            ? ModelPricingService.ComputeRunRoleCosts(totals, pricing, servedServiceTier)
            : default;

        bool hasAssessor = ModelPricingService.RoleHasTokens(
            totals.TotalAssessmentInputTokens, totals.TotalAssessmentOutputTokens,
            totals.TotalAssessmentCacheReadTokens, totals.TotalAssessmentCacheCreationTokens);
        bool hasSecondOpinion = ModelPricingService.RoleHasTokens(
            totals.TotalSecondOpinionInputTokens, totals.TotalSecondOpinionOutputTokens,
            totals.TotalSecondOpinionCacheReadTokens, totals.TotalSecondOpinionCacheCreationTokens);
        bool hasVerifier = ModelPricingService.RoleHasTokens(
            totals.TotalClaimVerificationInputTokens, totals.TotalClaimVerificationOutputTokens,
            totals.TotalClaimVerificationCacheReadTokens, totals.TotalClaimVerificationCacheCreationTokens);
        bool hasSynthesis = ModelPricingService.RoleHasTokens(
            totals.TotalSynthesisInputTokens, totals.TotalSynthesisOutputTokens,
            totals.TotalSynthesisCacheReadTokens, totals.TotalSynthesisCacheCreationTokens);
        bool hasCoAssessor = ModelPricingService.RoleHasTokens(
            totals.TotalCoAssessmentInputTokens, totals.TotalCoAssessmentOutputTokens,
            totals.TotalCoAssessmentCacheReadTokens, totals.TotalCoAssessmentCacheCreationTokens);
        bool hasCoSynthesis = ModelPricingService.RoleHasTokens(
            totals.TotalCoSynthesisInputTokens, totals.TotalCoSynthesisOutputTokens,
            totals.TotalCoSynthesisCacheReadTokens, totals.TotalCoSynthesisCacheCreationTokens);

        // A role that spent nothing, or whose card did not resolve, reports no figure at all: the cost
        // panel omits a null role and keeps a zero one, because zero is a measurement and absence is not.
        static decimal? Priced(bool participated, ModelPricing? card, decimal cost) =>
            participated && card != null ? cost : null;

        decimal? candidateCost = Priced(true, pricing?.Candidate, costs.Candidate);
        decimal? assessorCost = Priced(hasAssessor, pricing?.Assessor, costs.Assessor);
        decimal? secondOpinionCost = Priced(hasSecondOpinion, pricing?.SecondOpinion, costs.SecondOpinion);
        decimal? verifierCost = Priced(hasVerifier, pricing?.ClaimVerifier, costs.ClaimVerifier);
        // The synthesis runs on the assessor's configuration and is priced on the assessor's card.
        decimal? synthesisCost = Priced(hasSynthesis, pricing?.Assessor, costs.Synthesis);
        // Panel member B and its own synthesis are both priced on the co-assessor's card.
        decimal? coAssessorCost = Priced(hasCoAssessor, pricing?.CoAssessor, costs.CoAssessor);
        decimal? coSynthesisCost = Priced(hasCoSynthesis, pricing?.CoAssessor, costs.CoSynthesis);

        static BenchmarkCostRoles Unpriced(bool participated, ModelPricing? card, BenchmarkCostRoles role) =>
            participated && card == null ? role : BenchmarkCostRoles.None;

        BenchmarkCostRoles unpricedRoles =
            Unpriced(true, pricing?.Candidate, BenchmarkCostRoles.Candidate)
            | Unpriced(hasAssessor, pricing?.Assessor, BenchmarkCostRoles.Assessor)
            | Unpriced(hasSecondOpinion, pricing?.SecondOpinion, BenchmarkCostRoles.SecondOpinion)
            | Unpriced(hasVerifier, pricing?.ClaimVerifier, BenchmarkCostRoles.ClaimVerifier)
            | Unpriced(hasSynthesis, pricing?.Assessor, BenchmarkCostRoles.Synthesis)
            | Unpriced(hasCoAssessor, pricing?.CoAssessor, BenchmarkCostRoles.CoAssessor)
            | Unpriced(hasCoSynthesis, pricing?.CoAssessor, BenchmarkCostRoles.CoSynthesis);

        decimal? gradingCost =
            (assessorCost.HasValue || secondOpinionCost.HasValue || verifierCost.HasValue || synthesisCost.HasValue
                || coAssessorCost.HasValue || coSynthesisCost.HasValue)
                ? costs.Grading
                : null;

        return new BenchmarkRunCostEstimate
        {
            Total = costs.Incomplete ? null : costs.Total,
            Candidate = candidateCost,
            Assessor = assessorCost,
            CoAssessor = coAssessorCost,
            SecondOpinion = secondOpinionCost,
            ClaimVerifier = verifierCost,
            Synthesis = synthesisCost,
            CoSynthesis = coSynthesisCost,
            Grading = gradingCost,
            PricingSource = string.IsNullOrEmpty(costs.Source) ? null : costs.Source,
            PricingIncomplete = costs.Incomplete,
            UnpricedRoles = unpricedRoles
        };
    }

    /// <summary>
    /// A detached copy of a still-running run carrying the totals summed from its answer rows, so a
    /// mid-run figure is the same arithmetic as the finalized one.
    ///
    /// <para>Every sum comes from <see cref="BenchmarkRunFinalizer"/>, which stays the only writer of
    /// the run's own columns — nothing here touches the tracked entity. The synthesis totals are
    /// run-level, have no per-answer rows to sum from, and are carried across unchanged.</para>
    /// </summary>
    public static BenchmarkRun BuildLiveTotals(BenchmarkRun run)
    {
        var candidate = BenchmarkRunFinalizer.ComputeCandidateTotals(run.Answers);
        var longContext = BenchmarkRunFinalizer.ComputeCandidateLongContextTotals(run.Answers);
        var grading = BenchmarkRunFinalizer.SumGradingTotals(run.Answers);

        return new BenchmarkRun
        {
            Id = run.Id,
            TestedModelSnapshot = run.TestedModelSnapshot,

            TotalInputTokens = candidate.TotalInputTokens,
            TotalOutputTokens = candidate.TotalOutputTokens,
            TotalCacheReadTokens = candidate.TotalCacheReadTokens,
            TotalCacheCreationTokens = candidate.TotalCacheCreationTokens,
            TotalAnswerDurationMs = candidate.TotalAnswerDurationMs,
            TotalDurationMs = run.TotalDurationMs,

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

            TotalSynthesisInputTokens = run.TotalSynthesisInputTokens,
            TotalSynthesisOutputTokens = run.TotalSynthesisOutputTokens,
            TotalSynthesisCacheReadTokens = run.TotalSynthesisCacheReadTokens,
            TotalSynthesisCacheCreationTokens = run.TotalSynthesisCacheCreationTokens,
            TotalSynthesisDurationMs = run.TotalSynthesisDurationMs,

            TotalCoAssessmentInputTokens = grading.TotalCoAssessmentInputTokens,
            TotalCoAssessmentOutputTokens = grading.TotalCoAssessmentOutputTokens,
            TotalCoAssessmentCacheReadTokens = grading.TotalCoAssessmentCacheReadTokens,
            TotalCoAssessmentCacheCreationTokens = grading.TotalCoAssessmentCacheCreationTokens,
            TotalCoAssessmentDurationMs = grading.TotalCoAssessmentDurationMs,

            TotalCoSynthesisInputTokens = run.TotalCoSynthesisInputTokens,
            TotalCoSynthesisOutputTokens = run.TotalCoSynthesisOutputTokens,
            TotalCoSynthesisCacheReadTokens = run.TotalCoSynthesisCacheReadTokens,
            TotalCoSynthesisCacheCreationTokens = run.TotalCoSynthesisCacheCreationTokens,
            TotalCoSynthesisDurationMs = run.TotalCoSynthesisDurationMs
        };
    }
}
