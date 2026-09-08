namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The gate on the cross-model comparison view. Each fixture differs from the baseline on exactly
/// one key, because the rule these tests defend is per-key: some keys are the subject of the
/// comparison, some cost an axis, and the rest disqualify the point entirely.
///
/// <para>The failure being prevented is the one a chart makes easy and a table does not — several
/// confident bars whose runs were graded under different rubrics or answered under a different
/// prompt configuration.</para>
/// </summary>
public class BenchmarkCrossModelComparabilityTests
{
    private const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";

    /// <summary>
    /// One run of the shared condition. Only the candidate model id varies by default, which is the
    /// difference the view exists to plot.
    /// </summary>
    private static BenchmarkRun Run(long id, string modelId = "gpt-5.6-luna")
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",

            TestedModelProviderUsed = "OpenAI",
            TestedModelIdUsed = modelId,
            TestedModelDisplayNameUsed = modelId,
            TestedModelThinkingLevelUsed = "high",
            TestedModelReasoningModeUsed = "enabled",
            TestedModelReasoningSummaryUsed = "auto",
            TestedModelServiceTierUsed = "default",
            TestedModelMaxOutputTokensUsed = 32000,
            TestedModelParallelExecutionModeUsed = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,

            AssessorModelProviderUsed = "Google",
            AssessorModelIdUsed = "gemini-3.7-pro",
            AssessorModelDisplayNameUsed = "Gemini 3.7 Pro",
            AssessorModelParallelExecutionModeUsed = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,

            SecondOpinionAssessorModelProviderUsed = "Anthropic",
            SecondOpinionAssessorModelIdUsed = "claude-opus-5",
            SecondOpinionModeUsed = 1,
            SecondOpinionBlindUsed = true,

            ClaimVerifierProviderUsed = "Anthropic",
            ClaimVerifierModelIdUsed = "claude-opus-5",

            CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
            CandidateSystemPromptSha256 = PromptSha,
            ToolGuidesSha256 = GuidesSha,
            KnowledgeBaseHeadSha = KnowledgeSha,

            HarnessVersion = "12",
            ScoringMethodVersion = 9,
            ScoringProfileId = 1,
            ScoringProfileSnapshotJson = "{\"SpeedTargetMs\":15000,\"SpeedDecayK\":20.0}",

            MaxToolCallsPerQuestionUsed = 45,
            ToolIterationCapsJson = "{\"Simple\":22,\"Intermediate\":22,\"Advanced\":22}",
            TotalModelCallCapsJson = "{\"Simple\":28,\"Intermediate\":28,\"Advanced\":28}",
            QuestionTimeoutSecondsJson = "{\"Simple\":420,\"Intermediate\":600,\"Advanced\":720}",
            MaxParallelQuestionsUsed = 1,
            PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":1.25}}"
        };

        for (int q = 1; q <= 3; q++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = id * 100 + q,
                BenchmarkRunId = id,
                BenchmarkQuestionId = q,
                ItemRevisionUsed = 1,
                OrderIndex = q,
                QuestionText = $"Q{q}",
                Status = BenchmarkAnswerStatus.Ok,
                QualityScore = 80
            });
        }

        return run;
    }

    private static BenchmarkCrossModelEntry Entry(string key, params BenchmarkRun[] runs)
        => new() { Key = key, Runs = runs };

    private static BenchmarkCrossModelVerdict Verdict(BenchmarkCrossModelComparabilityResult result, string key)
        => result.Entries.Single(v => v.Key == key);

    // --- The model axis: what these points are allowed to differ on -------------------------------

    [Fact]
    public void ModelAxisKeys_AreTheEightCandidateKeys_AndPromptOptionsIsNotOneOfThem()
    {
        Assert.Equal(8, BenchmarkCrossModelComparability.ModelAxisKeys.Count);

        Assert.Contains(BenchmarkComparabilityKey.CandidateModelKey, BenchmarkCrossModelComparability.ModelAxisKeys);
        Assert.Contains(BenchmarkComparabilityKey.CandidateThinkingLevelKey, BenchmarkCrossModelComparability.ModelAxisKeys);
        Assert.Contains(BenchmarkComparabilityKey.CandidateParallelExecutionModeKey, BenchmarkCrossModelComparability.ModelAxisKeys);

        // The configuration is not the model. A set with mixed prompt options is not comparable.
        Assert.DoesNotContain(BenchmarkComparabilityKey.CandidatePromptOptionsKey, BenchmarkCrossModelComparability.ModelAxisKeys);
        Assert.True(BenchmarkCrossModelComparability.IsMustMatchKey(BenchmarkComparabilityKey.CandidatePromptOptionsKey));
    }

    [Fact]
    public void TwoModelsUnderOneInstrument_AreBothComparable()
    {
        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna")),
            Entry("b", Run(14, "gemini-3.8-flash-lite"))
        });

        Assert.All(result.Entries, v => Assert.True(v.IsComparable));
        Assert.Equal(new[] { "a", "b" }, result.BaselineEntryKeys);
        Assert.False(result.ThinkingLevelsDiffer);
        Assert.Null(result.SpeedAxisCaveat);
    }

    [Fact]
    public void ChangingOnlyTheParallelExecutionMode_StaysComparable()
    {
        // The shared taxonomy folds the parallel execution mode into the prompt-options signature.
        // That mode is a model-axis key here, so a point that differs only on batching mode must not
        // also read as a prompt-options difference and exclude itself.
        var b = Run(14);
        b.TestedModelParallelExecutionModeUsed = MobileGnollHackLogger.Data.ParallelExecutionMode.Disabled;

        var result = BenchmarkCrossModelComparability.Resolve(new[] { Entry("a", Run(13)), Entry("b", b) });

        Assert.All(result.Entries, v => Assert.True(v.IsComparable));
    }

    // --- Exclusion: the instrument moved -----------------------------------------------------------

    [Fact]
    public void MixedCandidatePromptOptions_ExcludeTheOddEntry()
    {
        // verboseMode differing caps Completeness, Conciseness and Readability by design, so the
        // three quality dimensions are measuring different things. Not comparable, not degraded.
        var odd = Run(15, "claude-opus-5");
        odd.CandidatePromptOptionsJson = "{\"verboseMode\":true,\"spoilerFreeMode\":false,\"overseerMode\":0}";

        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna")),
            Entry("b", Run(14, "gemini-3.8-flash-lite")),
            Entry("c", odd)
        });

        Assert.True(Verdict(result, "a").IsComparable);
        Assert.True(Verdict(result, "b").IsComparable);

        var excluded = Verdict(result, "c");
        Assert.True(excluded.IsExcluded);
        Assert.Equal(new[] { BenchmarkComparabilityKey.CandidatePromptOptionsKey }, excluded.ExcludingKeys);
        Assert.Contains(BenchmarkComparabilityKey.CandidatePromptOptionsKey, excluded.Explanation);

        // The values, and the runs carrying them: a UI must be able to say why, not only that.
        var difference = Assert.Single(excluded.Differences);
        Assert.Equal(BenchmarkComparabilityKeyKind.Candidate, difference.Kind);
        Assert.Equal(2, difference.Variants.Count);
        Assert.Equal(new long[] { 15 }, difference.Variants.Last().RunIds);
    }

    [Fact]
    public void DifferingScoringMethodVersion_ExcludesTheMinorityHalfOfTheCorpus()
    {
        // The live scenario: the scoring method version moved 8 to 9, so the stored corpus really is
        // split. The view charts the larger half and names the smaller one — it does not quietly
        // plot a v8-graded run beside a v9-graded one.
        var old = Run(15, "claude-opus-5");
        old.ScoringMethodVersion = 8;

        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna")),
            Entry("b", Run(14, "gemini-3.8-flash-lite")),
            Entry("c", old)
        });

        Assert.Equal(new[] { "a", "b" }, result.BaselineEntryKeys);
        Assert.True(Verdict(result, "a").IsComparable);
        Assert.True(Verdict(result, "b").IsComparable);

        var excluded = Verdict(result, "c");
        Assert.True(excluded.IsExcluded);
        Assert.Equal(new[] { BenchmarkComparabilityKey.ScoringMethodVersionKey }, excluded.ExcludingKeys);
        Assert.Equal(BenchmarkComparabilityKeyKind.Instrument, Assert.Single(excluded.Differences).Kind);

        Assert.Equal("9", result.BaselineKeyValues[BenchmarkComparabilityKey.ScoringMethodVersionKey]);
    }

    [Fact]
    public void ADifferentSuite_IsExcluded()
    {
        var other = Run(15, "claude-opus-5");
        other.BenchmarkSuiteId = 6;

        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13)), Entry("b", Run(14, "gemini-3.8-flash-lite")), Entry("c", other)
        });

        Assert.Contains(BenchmarkComparabilityKey.SuiteKey, Verdict(result, "c").ExcludingKeys);
    }

    [Fact]
    public void AnEntryWhoseOwnRunsAreTwoDifferentModels_IsExcludedBeforeTheBaselineIsChosen()
    {
        // Two models in one entry is not one point, whatever else the set agrees on.
        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna"), Run(14, "gemini-3.8-flash-lite")),
            Entry("b", Run(15, "claude-opus-5"))
        });

        Assert.True(Verdict(result, "a").IsExcluded);
        Assert.Contains(BenchmarkComparabilityKey.CandidateModelKey, Verdict(result, "a").ExcludingKeys);

        // And it did not drag the baseline with it.
        Assert.Equal(new[] { "b" }, result.BaselineEntryKeys);
        Assert.True(Verdict(result, "b").IsComparable);
    }

    // --- Degradation: the point is plotted, the axis is flagged ------------------------------------

    [Fact]
    public void DifferingPricingSnapshot_DegradesCost_AndLeavesQualityIntact()
    {
        var b = Run(14, "gemini-3.8-flash-lite");
        b.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":0.10}}";

        var result = BenchmarkCrossModelComparability.Resolve(new[] { Entry("a", Run(13)), Entry("b", b) });

        Assert.All(result.Entries, v =>
        {
            Assert.False(v.IsExcluded);
            Assert.True(v.IsCostDegraded);
            Assert.False(v.IsSpeedDegraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.PricingSnapshotKey }, v.CostDegradingKeys);
        });
    }

    [Fact]
    public void RepricingToOneBasis_ClearsThePricingSnapshotDegradation()
    {
        // With every entry recomputed from one catalog, the stored snapshots are no longer what the
        // cost axis shows, so they cannot degrade it.
        var b = Run(14, "gemini-3.8-flash-lite");
        b.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":0.10}}";

        var result = BenchmarkCrossModelComparability.Resolve(
            new[] { Entry("a", Run(13)), Entry("b", b) }, repricedToOneBasis: true);

        Assert.All(result.Entries, v => Assert.True(v.IsComparable));
    }

    [Fact]
    public void DifferingQuestionParallelism_DegradesSpeed_AndLeavesQualityIntact()
    {
        var b = Run(14, "gemini-3.8-flash-lite");
        b.MaxParallelQuestionsUsed = 3;

        var result = BenchmarkCrossModelComparability.Resolve(new[] { Entry("a", Run(13)), Entry("b", b) });

        Assert.All(result.Entries, v =>
        {
            Assert.False(v.IsExcluded);
            Assert.True(v.IsSpeedDegraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, v.SpeedDegradingKeys);

            // Cost too, and from the shared taxonomy's own flags rather than a second opinion here:
            // running questions concurrently changes prompt-cache behaviour and therefore spend.
            Assert.True(v.IsCostDegraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, v.CostDegradingKeys);
        });
    }

    // --- Thinking level: disclosed, never blocking --------------------------------------------------

    [Fact]
    public void DifferingThinkingLevel_IsACaveatOnSpeed_NotAnExclusion()
    {
        // Comparing models as configured is the point of the view, and thinking level dominates
        // model time. So it is disclosed rather than blocked.
        var b = Run(14, "gemini-3.8-flash-lite");
        b.TestedModelThinkingLevelUsed = "low";

        var result = BenchmarkCrossModelComparability.Resolve(new[] { Entry("a", Run(13)), Entry("b", b) });

        Assert.All(result.Entries, v => Assert.True(v.IsComparable));
        Assert.True(result.ThinkingLevelsDiffer);
        Assert.Equal(BenchmarkCrossModelComparability.ThinkingLevelSpeedCaveat, result.SpeedAxisCaveat);
    }

    // --- The baseline signature: the citable instrument identifier ---------------------------------

    [Fact]
    public void BaselineSignature_EqualsTheMustMatchSignatureOfABaselineRun()
    {
        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna")),
            Entry("b", Run(14, "gemini-3.8-flash-lite"))
        });

        Assert.Equal(BenchmarkCrossModelComparability.MustMatchSignature(Run(13)), result.BaselineSignature);
    }

    [Fact]
    public void BaselineSignature_IsEmpty_WhenNoEntryReachesTheBaseline()
    {
        // An empty set reaches no baseline at all.
        var empty = BenchmarkCrossModelComparability.Resolve(new List<BenchmarkCrossModelEntry>());
        Assert.Equal(string.Empty, empty.BaselineSignature);

        // Every entry here is internally inconsistent — two models inside one entry — so each is
        // excluded before a baseline is chosen, and nothing composes it either.
        var result = BenchmarkCrossModelComparability.Resolve(new[]
        {
            Entry("a", Run(13, "gpt-5.6-luna"), Run(14, "gemini-3.8-flash-lite")),
            Entry("b", Run(15, "claude-opus-5"), Run(16, "claude-haiku-5"))
        });

        Assert.Equal(string.Empty, result.BaselineSignature);
    }

    // --- Degenerate sets ------------------------------------------------------------------------

    [Fact]
    public void AnEmptySet_IsUndefinedRatherThanComparable()
    {
        var result = BenchmarkCrossModelComparability.Resolve(new List<BenchmarkCrossModelEntry>());

        Assert.Empty(result.Entries);
        Assert.Empty(result.BaselineEntryKeys);
        Assert.Contains("empty set", result.Explanation);
    }

    [Fact]
    public void ASingleEntry_IsComparableWithItself()
    {
        var result = BenchmarkCrossModelComparability.Resolve(new[] { Entry("a", Run(13)) });

        Assert.True(Assert.Single(result.Entries).IsComparable);
        Assert.False(result.ThinkingLevelsDiffer);
    }
}
