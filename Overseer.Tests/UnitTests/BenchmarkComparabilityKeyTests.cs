namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The tier ladder. These fixtures are shaped like runs 13 and 14 — same suite, same candidate,
/// same three instrument SHAs — because that pair is the case the feature exists to serve, and the
/// failure it exists to prevent is a pooled index computed over runs that only look like a
/// replicate set.
/// </summary>
public class BenchmarkComparabilityKeyTests
{
    private const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";
    private const string ToolIterationCaps = "{\"Simple\":22,\"Intermediate\":22,\"Advanced\":22}";

    private static BenchmarkRun Run(long id)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",

            TestedModelProviderUsed = "OpenAI",
            TestedModelIdUsed = "gpt-5.6-luna",
            TestedModelDisplayNameUsed = "GPT-5.6 Luna",
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
            ScoringMethodVersion = 8,
            ScoringProfileId = 1,
            ScoringProfileSnapshotJson = "{\"SpeedTargetMs\":15000,\"SpeedDecayK\":20.0}",

            MaxToolCallsPerQuestionUsed = 45,
            ToolIterationCapsJson = ToolIterationCaps,
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

    [Fact]
    public void IdenticalRuns_ResolveTierA_AndMayBePooled()
    {
        var result = BenchmarkComparabilityKey.Resolve(new[] { Run(13), Run(14) });

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.True(result.PoolingPermitted);
        Assert.Empty(result.Differences);
        Assert.False(result.SpeedAggregatesDegraded);
        Assert.False(result.CostAggregatesDegraded);
        Assert.Equal(new long[] { 13, 14 }, result.RunIds);

        // Same keys, therefore the same per-run hash. The set hash is derived from those.
        Assert.Equal(result.MemberKeyHashes[13], result.MemberKeyHashes[14]);
        Assert.NotEmpty(result.ComparabilityKeyHash);
    }

    [Fact]
    public void ChangingQuestionParallelism_ResolvesTierB_WithSpeedAndCostFlagged()
    {
        var a = Run(13);
        var b = Run(14);
        b.MaxParallelQuestionsUsed = 3;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.QualityComparable, result.Tier);

        // Quality still pools. Speed and cost do not, and say so.
        Assert.True(result.PoolingPermitted);
        Assert.True(result.SpeedAggregatesDegraded);
        Assert.True(result.CostAggregatesDegraded);

        var difference = Assert.Single(result.Differences);
        Assert.Equal(BenchmarkComparabilityKey.QuestionParallelismKey, difference.Name);
        Assert.Equal(BenchmarkComparabilityKeyKind.SpeedAndCost, difference.Kind);

        // The values, and which runs carry them: a boolean verdict would be unusable in a dialog.
        Assert.Equal(2, difference.Variants.Count);
        Assert.Equal(new long[] { 13 }, difference.Variants.Single(v => v.Value == "1").RunIds);
        Assert.Equal(new long[] { 14 }, difference.Variants.Single(v => v.Value == "3").RunIds);
        Assert.Contains(BenchmarkComparabilityKey.QuestionParallelismKey, result.Explanation);
    }

    [Fact]
    public void ChangingToolGuides_ResolvesTierC_AndRefusesAPooledIndex()
    {
        // This is T15: the candidate is untouched and exactly one instrument key moved.
        var baseline = Run(15);
        var treatment = Run(16);
        treatment.ToolGuidesSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = BenchmarkComparabilityKey.Resolve(new[] { baseline, treatment });

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);

        // The whole point of Tier C: these runs may be compared and may never be averaged.
        Assert.False(result.PoolingPermitted);
        Assert.False(BenchmarkComparabilityKey.IsPoolable(result.Tier));

        var difference = Assert.Single(result.Differences);
        Assert.Equal(BenchmarkComparabilityKey.ToolGuidesKey, difference.Name);
        Assert.Equal(BenchmarkComparabilityKeyKind.Instrument, difference.Kind);
        Assert.Contains("Tier C", result.Explanation);
        Assert.Contains(BenchmarkComparabilityKey.ToolGuidesKey, result.Explanation);
    }

    [Fact]
    public void ChangingTheSuite_ResolvesBelowTierB_AndNamesTheDifferingKey()
    {
        var a = Run(13);
        var b = Run(14);
        b.BenchmarkSuiteId = 6;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.False(result.PoolingPermitted);
        Assert.Contains(result.Differences, d => d.Name == BenchmarkComparabilityKey.SuiteKey);
        Assert.Contains(BenchmarkComparabilityKey.SuiteKey, result.Explanation);

        // A group at this tier has no valid aggregates at all, so half-reporting a degraded speed
        // figure would be worse than reporting none.
        Assert.False(result.SpeedAggregatesDegraded);
        Assert.False(result.CostAggregatesDegraded);
    }

    [Fact]
    public void ARubricEditEndsTheReplicateSet()
    {
        // A bumped item revision is a changed answer key. It is Fundamental, not Instrument: there
        // is no cross-condition reading of "the same questions, marked differently".
        var a = Run(13);
        var b = Run(14);
        b.Answers[1].ItemRevisionUsed = 2;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.Contains(result.Differences, d => d.Name == BenchmarkComparabilityKey.ItemRevisionsKey);
    }

    [Fact]
    public void AssessedDifficultyChangeAlone_EndsTheReplicateSet_ButLeavesItemRevisionsUnchanged()
    {
        // Assess Difficulty rewrites BenchmarkRunAnswer.AssessedDifficulty without bumping
        // ItemRevisionUsed (H5), so this must move a Fundamental key of its own — distinct from a
        // rubric edit (ARubricEditEndsTheReplicateSet above), which moves ItemRevisionsKey instead.
        var a = Run(13);
        var b = Run(14);
        b.Answers[1].AssessedDifficulty = 62;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.Contains(result.Differences, d => d.Name == BenchmarkComparabilityKey.AssessedDifficultiesKey);
        Assert.DoesNotContain(result.Differences, d => d.Name == BenchmarkComparabilityKey.ItemRevisionsKey);
    }

    [Fact]
    public void TwoInstrumentDifferences_AreNotACrossConditionExperiment()
    {
        var a = Run(13);
        var b = Run(14);
        b.ToolGuidesSha256 = "1111111111111111111111111111111111111111111111111111111111111111";
        b.KnowledgeBaseHeadSha = "2222222222222222222222222222222222222222";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.Equal(2, result.Differences.Count);
        Assert.Contains("2 instrument keys differ", result.Explanation);
    }

    [Fact]
    public void ADifferentCandidateIsNotAGroup_ItIsTwoGroups()
    {
        var a = Run(13);
        var b = Run(14);
        b.TestedModelIdUsed = "claude-opus-5";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.Contains(result.Differences, d => d.Kind == BenchmarkComparabilityKeyKind.Candidate);
    }

    [Fact]
    public void TheDefaultProfileEditedInPlace_IsCaughtBySnapshotRatherThanId()
    {
        // Phase 3 edits the Default profile in place, so two runs can both name profile 1 and have
        // been scored under two different definitions of it. The id alone would miss this.
        var a = Run(13);
        var b = Run(14);
        b.ScoringProfileSnapshotJson = "{\"SpeedTargetMs\":15000,\"SpeedDecayK\":22.0}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(BenchmarkComparabilityKey.ScoringProfileKey, difference.Name);
    }

    [Fact]
    public void ASingleRun_IsTierATrivially_AndAnEmptySetIsNotComparable()
    {
        // The series orchestrator asserts the tier of its group as it grows, so one member must
        // resolve rather than throw.
        var single = BenchmarkComparabilityKey.Resolve(new[] { Run(13) });
        Assert.Equal(BenchmarkComparabilityTier.Replicate, single.Tier);

        var empty = BenchmarkComparabilityKey.Resolve(System.Array.Empty<BenchmarkRun>());
        Assert.Equal(BenchmarkComparabilityTier.NotComparable, empty.Tier);
        Assert.False(empty.PoolingPermitted);
    }

    [Fact]
    public void KeyHash_IsStableAcrossInstancesAndMovesWithAnyKey()
    {
        Assert.Equal(BenchmarkComparabilityKey.ComputeKeyHash(Run(13)), BenchmarkComparabilityKey.ComputeKeyHash(Run(13)));

        var moved = Run(13);
        moved.HarnessVersion = "13";
        Assert.NotEqual(BenchmarkComparabilityKey.ComputeKeyHash(Run(13)), BenchmarkComparabilityKey.ComputeKeyHash(moved));

        // The set hash does not depend on the order the members were passed in.
        var forwards = BenchmarkComparabilityKey.Resolve(new[] { Run(13), Run(14) }).ComparabilityKeyHash;
        var backwards = BenchmarkComparabilityKey.Resolve(new[] { Run(14), Run(13) }).ComparabilityKeyHash;
        Assert.Equal(forwards, backwards);
    }

    [Fact]
    public void Extract_CoversEveryTierAKeyTheTierModelNames()
    {
        var names = BenchmarkComparabilityKey.Extract(Run(13)).Select(k => k.Name).ToList();

        foreach (var expected in new[]
        {
            BenchmarkComparabilityKey.SuiteKey,
            BenchmarkComparabilityKey.ItemRevisionsKey,
            BenchmarkComparabilityKey.AssessedDifficultiesKey,
            BenchmarkComparabilityKey.CandidateProviderKey,
            BenchmarkComparabilityKey.CandidateModelKey,
            BenchmarkComparabilityKey.CandidateThinkingLevelKey,
            BenchmarkComparabilityKey.CandidateReasoningModeKey,
            BenchmarkComparabilityKey.CandidateReasoningSummaryKey,
            BenchmarkComparabilityKey.CandidateServiceTierKey,
            BenchmarkComparabilityKey.CandidateMaxOutputTokensKey,
            BenchmarkComparabilityKey.CandidateParallelExecutionModeKey,
            BenchmarkComparabilityKey.CandidatePromptOptionsKey,
            BenchmarkComparabilityKey.CandidateSystemPromptKey,
            BenchmarkComparabilityKey.ToolGuidesKey,
            BenchmarkComparabilityKey.KnowledgeBaseKey,
            BenchmarkComparabilityKey.HarnessVersionKey,
            BenchmarkComparabilityKey.ScoringMethodVersionKey,
            BenchmarkComparabilityKey.ScoringProfileKey,
            BenchmarkComparabilityKey.AssessorConfigurationKey,
            BenchmarkComparabilityKey.SecondOpinionConfigurationKey,
            BenchmarkComparabilityKey.ClaimVerifierConfigurationKey,
            BenchmarkComparabilityKey.PerQuestionBudgetsKey,
            BenchmarkComparabilityKey.QuestionParallelismKey,
            BenchmarkComparabilityKey.PricingSnapshotKey
        })
        {
            Assert.Contains(expected, names);
        }

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // --- Describe: the methods-statement metadata --------------------------------------------

    [Fact]
    public void Describe_GivesEveryExtractedKeyANonEmptyLabelAndDescription()
    {
        // A key added to Extract but never described here would render blank in a methods
        // statement, so this walks Extract's own output rather than a hard-coded key list.
        foreach (var key in BenchmarkComparabilityKey.Extract(Run(13)))
        {
            var info = BenchmarkComparabilityKey.Describe(key.Name);
            Assert.False(string.IsNullOrWhiteSpace(info.Label), $"{key.Name} has no label.");
            Assert.False(string.IsNullOrWhiteSpace(info.Description), $"{key.Name} has no description.");
        }
    }

    [Fact]
    public void Describe_AnUnrecognisedName_DegradesToItselfWithNoDescription()
    {
        var info = BenchmarkComparabilityKey.Describe("SomeFutureKey");

        Assert.Equal("SomeFutureKey", info.Label);
        Assert.Equal(string.Empty, info.Description);
        Assert.Equal(BenchmarkComparabilityValueKind.Text, info.ValueKind);
    }

    [Fact]
    public void Describe_ReturnsTheDocumentedValueKind_ForOneKeyOfEachKindInUse()
    {
        Assert.Equal(BenchmarkComparabilityValueKind.Identifier,
            BenchmarkComparabilityKey.Describe(BenchmarkComparabilityKey.SuiteKey).ValueKind);
        Assert.Equal(BenchmarkComparabilityValueKind.Hash,
            BenchmarkComparabilityKey.Describe(BenchmarkComparabilityKey.CandidateSystemPromptKey).ValueKind);
        Assert.Equal(BenchmarkComparabilityValueKind.Json,
            BenchmarkComparabilityKey.Describe(BenchmarkComparabilityKey.CandidatePromptOptionsKey).ValueKind);
        Assert.Equal(BenchmarkComparabilityValueKind.List,
            BenchmarkComparabilityKey.Describe(BenchmarkComparabilityKey.ItemRevisionsKey).ValueKind);
        Assert.Equal(BenchmarkComparabilityValueKind.Text,
            BenchmarkComparabilityKey.Describe(BenchmarkComparabilityKey.CandidateProviderKey).ValueKind);
    }

    [Fact]
    public void SecondOpinionSettings_AreTierAKeys()
    {
        var a = Run(13);
        var b = Run(14);
        b.SecondOpinionBlindUsed = false;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.SecondOpinionConfigurationKey, Assert.Single(result.Differences).Name);
    }

    [Fact]
    public void PricingSnapshot_DegradesCostAlone()
    {
        var a = Run(13);
        var b = Run(14);
        b.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":2.50}}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.QualityComparable, result.Tier);
        Assert.True(result.CostAggregatesDegraded);
        Assert.False(result.SpeedAggregatesDegraded);
    }

    /// <summary>
    /// The case Tier A depends on. Every member of a series is launched from one identical request,
    /// minutes apart, so the snapshots agree on every price and disagree on the instant they were
    /// taken. Keying on that instant put Tier A out of reach for every series ever run.
    /// </summary>
    [Fact]
    public void PricingSnapshotsDifferingOnlyInCaptureInstant_ResolveTierA()
    {
        const string Prices =
            "\"candidate\":{\"inputPerMillion\":1.25,\"outputPerMillion\":10.0,\"asOf\":\"2026-09-01\"},"
            + "\"assessor\":{\"inputPerMillion\":0.30,\"outputPerMillion\":2.50,\"asOf\":\"2026-09-01\"}";

        var a = Run(13);
        var b = Run(14);
        a.PricingSnapshotJson = "{\"capturedAtUtc\":\"2026-09-07T08:46:51.2836446Z\"," + Prices + "}";
        b.PricingSnapshotJson = "{\"capturedAtUtc\":\"2026-09-07T09:19:44.1120983Z\"," + Prices + "}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.Empty(result.Differences);
        Assert.False(result.CostAggregatesDegraded);
    }

    /// <summary>
    /// A price change under an identical capture instant must still degrade cost. This is the half
    /// that stops the exclusion above from becoming a blanket suppression.
    /// </summary>
    [Fact]
    public void PricingSnapshotsDifferingInPrice_StillDegradeCost_EvenAtOneCaptureInstant()
    {
        const string CapturedAt = "\"capturedAtUtc\":\"2026-09-07T08:46:51.2836446Z\"";

        var a = Run(13);
        var b = Run(14);
        a.PricingSnapshotJson = "{" + CapturedAt + ",\"candidate\":{\"inputPerMillion\":1.25}}";
        b.PricingSnapshotJson = "{" + CapturedAt + ",\"candidate\":{\"inputPerMillion\":2.50}}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.QualityComparable, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.PricingSnapshotKey, Assert.Single(result.Differences).Name);
        Assert.True(result.CostAggregatesDegraded);
        Assert.False(result.SpeedAggregatesDegraded);
    }

    /// <summary>A snapshot that will not parse still keys deterministically rather than to no value.</summary>
    [Fact]
    public void MalformedPricingSnapshot_KeysDeterministically()
    {
        var run = Run(13);
        run.PricingSnapshotJson = "{not valid json";

        string first = Value(BenchmarkComparabilityKey.Extract(run), BenchmarkComparabilityKey.PricingSnapshotKey);
        string second = Value(BenchmarkComparabilityKey.Extract(run), BenchmarkComparabilityKey.PricingSnapshotKey);

        Assert.Equal(first, second);
        Assert.NotEqual(BenchmarkComparabilityKey.NoValue, first);
    }

    /// <summary>
    /// The per-question caps are resolved from configuration at answer time, so without the run's
    /// own snapshot of them two runs straddling a cap change look like a replicate set. A cap that
    /// binds truncates an investigation, which moves what the candidate scored.
    /// </summary>
    [Fact]
    public void ChangingTheToolIterationCap_EndsTheReplicateSet()
    {
        var a = Run(13);
        var b = Run(14);
        b.ToolIterationCapsJson = "{\"Simple\":22,\"Intermediate\":22,\"Advanced\":35}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(BenchmarkComparabilityKey.PerQuestionBudgetsKey, difference.Name);
        Assert.Equal(BenchmarkComparabilityKeyKind.Instrument, difference.Kind);
    }

    [Fact]
    public void ChangingTheQuestionTimeout_EndsTheReplicateSet()
    {
        var a = Run(13);
        var b = Run(14);
        b.QuestionTimeoutSecondsJson = "{\"Simple\":420,\"Intermediate\":600,\"Advanced\":900}";

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.PerQuestionBudgetsKey, Assert.Single(result.Differences).Name);
    }

    /// <summary>
    /// Runs recorded before the budget snapshot existed carry all three columns null. Those runs
    /// match each other — the harness does not know what applied to either — and differ from a
    /// snapshotted run, which is the only honest reading of "one of these is unknown".
    /// </summary>
    [Fact]
    public void RunsWithoutABudgetSnapshot_MatchEachOther_AndDifferFromASnapshottedRun()
    {
        var oldA = Run(13);
        var oldB = Run(14);
        foreach (var run in new[] { oldA, oldB })
        {
            run.ToolIterationCapsJson = null;
            run.TotalModelCallCapsJson = null;
            run.QuestionTimeoutSecondsJson = null;
        }

        var amongOld = BenchmarkComparabilityKey.Resolve(new[] { oldA, oldB });
        Assert.Equal(BenchmarkComparabilityTier.Replicate, amongOld.Tier);
        Assert.Empty(amongOld.Differences);

        var straddling = BenchmarkComparabilityKey.Resolve(new[] { oldA, Run(15) });
        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, straddling.Tier);
        Assert.Equal(
            BenchmarkComparabilityKey.PerQuestionBudgetsKey,
            Assert.Single(straddling.Differences).Name);
    }

    // --- The scoring profile key reads scoring semantics, not the whole snapshot ----------------

    [Fact]
    public void ChangingADimensionWeight_EndsTheReplicateSet()
    {
        var result = ResolveWithProfileSnapshots(
            Snapshot(),
            Snapshot(p => p.WeightAccuracy = 0.60));

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.ScoringProfileKey, Assert.Single(result.Differences).Name);
    }

    [Fact]
    public void ChangingTheCriticalErrorCeiling_EndsTheReplicateSet()
    {
        var result = ResolveWithProfileSnapshots(
            Snapshot(),
            Snapshot(p => p.CriticalErrorCeiling = 30));

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.ScoringProfileKey, Assert.Single(result.Differences).Name);
    }

    [Fact]
    public void ChangingTheLevelScoreTable_EndsTheReplicateSet()
    {
        var result = ResolveWithProfileSnapshots(
            Snapshot(),
            Snapshot(p => p.LevelScoresJson = "[1, 15, 35, 55, 72, 90, 100]"));

        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, result.Tier);
        Assert.Equal(BenchmarkComparabilityKey.ScoringProfileKey, Assert.Single(result.Differences).Name);
    }

    /// <summary>
    /// A rename cannot move a score, and hashing the raw snapshot made it move this key — which
    /// ended a comparable series with no warning anywhere in the UI.
    /// </summary>
    [Fact]
    public void ProfileSnapshotsDifferingOnlyInName_ResolveTierA()
    {
        var result = ResolveWithProfileSnapshots(
            Snapshot(p => p.Name = "Standard Intelligence Index (Default)"),
            Snapshot(p => p.Name = "Standard Intelligence Index"));

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void ProfileSnapshotsDifferingOnlyInIsDefault_ResolveTierA()
    {
        // IsDefault decides which profile a run picks when none is named. It cannot change what a
        // run already named this profile scored.
        var result = ResolveWithProfileSnapshots(
            Snapshot(p => p.IsDefault = true),
            Snapshot(p => p.IsDefault = false));

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void ProfileSnapshotsDifferingOnlyInTimestamps_ResolveTierA()
    {
        // Editing a field and reverting it leaves the semantics identical and ModifiedAtUtc moved.
        var result = ResolveWithProfileSnapshots(
            Snapshot(p => p.ModifiedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
            Snapshot(p =>
            {
                p.CreatedAtUtc = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);
                p.ModifiedAtUtc = new DateTime(2026, 9, 6, 18, 30, 0, DateTimeKind.Utc);
            }));

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void LevelScoreTableFormatting_DoesNotMoveTheProfileKey()
    {
        var result = ResolveWithProfileSnapshots(
            Snapshot(p => p.LevelScoresJson = "[1, 15, 35, 55, 72, 87, 100]"),
            Snapshot(p => p.LevelScoresJson = "[1,15,35,55,72,87,100.0]"));

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.Empty(result.Differences);
    }

    /// <summary>
    /// A snapshot that will not deserialise degrades to a blob comparison rather than matching
    /// every other run, which would be a false replicate.
    /// </summary>
    [Fact]
    public void MalformedProfileSnapshot_KeysDeterministically_AndDiffersFromAValidOne()
    {
        var run = Run(13);
        run.ScoringProfileSnapshotJson = "{not valid json";

        string first = Value(BenchmarkComparabilityKey.Extract(run), BenchmarkComparabilityKey.ScoringProfileKey);
        string second = Value(BenchmarkComparabilityKey.Extract(run), BenchmarkComparabilityKey.ScoringProfileKey);

        Assert.Equal(first, second);
        Assert.DoesNotContain(BenchmarkComparabilityKey.NoValue, first);

        var valid = Run(14);
        valid.ScoringProfileSnapshotJson = Snapshot();
        Assert.NotEqual(
            first,
            Value(BenchmarkComparabilityKey.Extract(valid), BenchmarkComparabilityKey.ScoringProfileKey));
    }

    private static BenchmarkComparabilityResult ResolveWithProfileSnapshots(string first, string second)
    {
        var a = Run(13);
        var b = Run(14);
        a.ScoringProfileSnapshotJson = first;
        b.ScoringProfileSnapshotJson = second;

        return BenchmarkComparabilityKey.Resolve(new[] { a, b });
    }

    /// <summary>
    /// A full profile snapshot in the shape <c>BenchmarkService</c> writes — the serialized entity,
    /// name and timestamps included — optionally mutated before serialization.
    /// </summary>
    private static string Snapshot(Action<BenchmarkScoringProfile>? mutate = null)
    {
        var profile = new BenchmarkScoringProfile
        {
            Id = 1,
            Name = "Standard Intelligence Index",
            IsDefault = true,
            WeightAccuracy = 0.55,
            WeightCompleteness = 0.25,
            WeightConciseness = 0.10,
            WeightReadability = 0.10,
            LevelScoresJson = "[1, 15, 35, 55, 72, 87, 100]",
            CriticalErrorCeiling = 25,
            SecondOpinionQualityThreshold = 50,
            SecondOpinionMode = (int)BenchmarkSecondOpinionMode.FlaggedPlusSample,
            SecondOpinionBlind = true,
            SecondOpinionOutlierDeltaPoints = 25,
            SecondOpinionMinimumSample = 4,
            SpeedTargetMs = 15000,
            SpeedDecayK = 20.0,
            SpeedDifficultyScaling = 1.0,
            MaxParallelQuestions = 1,
            CreatedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        mutate?.Invoke(profile);
        return JsonSerializer.Serialize(profile);
    }

    private static string Value(IReadOnlyList<BenchmarkComparabilityKeyEntry> keys, string name)
        => keys.Single(k => k.Name == name).Value;
}
