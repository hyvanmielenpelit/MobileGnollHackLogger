namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkRubricGapDetectorTests
{
    private static BenchmarkUnverifiedClaimSample Sample(
        string claim,
        string provider = "OpenAI",
        string modelId = "gpt-5.6-luna",
        long questionId = 1,
        int orderIndex = 1,
        int? revision = 1,
        long runId = 1,
        BenchmarkClaimVerdict? verdict = null) => new()
        {
            QuestionId = questionId,
            QuestionOrderIndex = orderIndex,
            ItemRevisionUsed = revision,
            RunId = runId,
            Provider = provider,
            ModelId = modelId,
            Claim = claim,
            VerificationVerdict = verdict
        };

    [Fact]
    public void Detect_ClustersNearDuplicateClaims()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision at experience level 1"),
            Sample("Gnolls gain infravision at experience level one", runId: 2),
            Sample("Orcs are immune to poison", runId: 3)
        };

        var clusters = BenchmarkRubricGapDetector.Detect(samples);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Occurrences == 2 && c.Claims.Count == 2);
        Assert.Contains(clusters, c => c.Occurrences == 1);
    }

    [Fact]
    public void Detect_KeepsUnrelatedClaimsApart()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision"),
            Sample("The Amulet of Yendor weighs twenty units", runId: 2)
        };

        var clusters = BenchmarkRubricGapDetector.Detect(samples);
        Assert.Equal(2, clusters.Count);
        Assert.All(clusters, c => Assert.Equal(1, c.Occurrences));
    }

    [Fact]
    public void Detect_NeverMergesClaimsAcrossQuestions()
    {
        // The same sentence means different things against two different rubrics.
        var samples = new[]
        {
            Sample("Gnolls gain infravision", questionId: 1, orderIndex: 1),
            Sample("Gnolls gain infravision", questionId: 2, orderIndex: 2, runId: 2)
        };

        var clusters = BenchmarkRubricGapDetector.Detect(samples);
        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void Detect_NeverMergesClaimsAcrossRevisions()
    {
        // An edited question is a different item, so a claim raised against the old wording is
        // not evidence about the new rubric.
        var samples = new[]
        {
            Sample("Gnolls gain infravision", revision: 1),
            Sample("Gnolls gain infravision", revision: 2, runId: 2)
        };

        Assert.Equal(2, BenchmarkRubricGapDetector.Detect(samples).Count);
    }

    [Fact]
    public void OneFamily_IsAModelFinding_NotASuiteIssue()
    {
        // Treating one family's invention as a rubric gap is how a benchmark absorbs a model's
        // hallucinations into its own answer key.
        var samples = new[]
        {
            Sample("Yeenaghu grants gnolls a bonus to speed", modelId: "gpt-5.6-luna"),
            Sample("Yeenaghu grants gnolls a bonus to speed", modelId: "gpt-5.6", runId: 2)
        };

        var cluster = Assert.Single(BenchmarkRubricGapDetector.Detect(samples));

        Assert.Equal(BenchmarkRubricGapVerdict.LikelyHallucination, cluster.Verdict);
        Assert.Single(cluster.ModelFamilies);
    }

    [Fact]
    public void TwoFamilies_RaisingTheSameClaim_IsALikelyRubricGap()
    {
        var samples = new[]
        {
            Sample("Yeenaghu grants gnolls a bonus to speed", provider: "OpenAI", modelId: "gpt-5.6-luna"),
            Sample("Yeenaghu grants gnolls a bonus to speed", provider: "Google", modelId: "gemini-3.7-flash", runId: 2)
        };

        var cluster = Assert.Single(BenchmarkRubricGapDetector.Detect(samples));

        Assert.Equal(BenchmarkRubricGapVerdict.LikelyRubricGap, cluster.Verdict);
        Assert.Equal(2, cluster.ModelFamilies.Count);
        // Verbatim, both of them, so a human reads what was actually said.
        Assert.Single(cluster.Claims);
        Assert.Contains("Yeenaghu", cluster.Claims[0]);
    }

    [Fact]
    public void ModelFamilyOf_TreatsAVariantAsTheSameFamily()
    {
        Assert.Equal(
            BenchmarkRubricGapDetector.ModelFamilyOf("OpenAI", "gpt-5.6"),
            BenchmarkRubricGapDetector.ModelFamilyOf("OpenAI", "gpt-5.6-luna"));
    }

    [Fact]
    public void ModelFamilyOf_TreatsDifferentProvidersAsDifferentFamilies()
    {
        // The provider is part of the key deliberately: the whole verdict rests on the families
        // being independent, and two ids that share a prefix across providers are not one family.
        Assert.NotEqual(
            BenchmarkRubricGapDetector.ModelFamilyOf("OpenAI", "gpt-5.6"),
            BenchmarkRubricGapDetector.ModelFamilyOf("Google", "gemini-3.7-flash"));
    }

    [Fact]
    public void Detect_IgnoresBlankClaimsAndHandlesAnEmptyInput()
    {
        Assert.Empty(BenchmarkRubricGapDetector.Detect(new[] { Sample("   ") }));
        Assert.Empty(BenchmarkRubricGapDetector.Detect(new List<BenchmarkUnverifiedClaimSample>()));
        Assert.Empty(BenchmarkRubricGapDetector.Detect(null!));
    }

    [Fact]
    public void Detect_PutsRubricGapsFirst()
    {
        var samples = new[]
        {
            Sample("Orcs are immune to poison", provider: "OpenAI", modelId: "gpt-5.6", questionId: 1, orderIndex: 1),
            Sample("Gnolls gain infravision", provider: "OpenAI", modelId: "gpt-5.6", questionId: 2, orderIndex: 2, runId: 2),
            Sample("Gnolls gain infravision", provider: "Google", modelId: "gemini-3.7-flash", questionId: 2, orderIndex: 2, runId: 3)
        };

        var clusters = BenchmarkRubricGapDetector.Detect(samples);

        Assert.Equal(BenchmarkRubricGapVerdict.LikelyRubricGap, clusters[0].Verdict);
        Assert.Equal(2, clusters[0].QuestionOrderIndex);
    }

    [Fact]
    public void Precedence_ClusterWithOneSupported_IsVerifiedRubricGap()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision at experience level 1", provider: "OpenAI", modelId: "gpt-5.6-luna", verdict: BenchmarkClaimVerdict.Supported),
            Sample("Gnolls gain infravision at experience level 1", provider: "OpenAI", modelId: "gpt-5.6-luna", verdict: BenchmarkClaimVerdict.Indeterminate, runId: 2)
        };

        var cluster = Assert.Single(BenchmarkRubricGapDetector.Detect(samples));

        Assert.Equal(BenchmarkRubricGapVerdict.VerifiedRubricGap, cluster.Verdict);
    }

    [Fact]
    public void Precedence_ClusterWithRefutedAndTwoFamilies_IsLikelyHallucination()
    {
        var samples = new[]
        {
            Sample("Yeenaghu grants gnolls a bonus to speed", provider: "OpenAI", modelId: "gpt-5.6-luna", verdict: BenchmarkClaimVerdict.Refuted),
            Sample("Yeenaghu grants gnolls a bonus to speed", provider: "Google", modelId: "gemini-3.7-flash", verdict: BenchmarkClaimVerdict.Indeterminate, runId: 2)
        };

        var cluster = Assert.Single(BenchmarkRubricGapDetector.Detect(samples));

        // Even though 2 independent families raised it, the claim was checked and refuted,
        // so it must stay LikelyHallucination rather than LikelyRubricGap or VerifiedRubricGap.
        Assert.Equal(BenchmarkRubricGapVerdict.LikelyHallucination, cluster.Verdict);
    }
}
