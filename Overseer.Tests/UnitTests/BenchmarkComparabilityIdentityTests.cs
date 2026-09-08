namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// What the comparability keys say once a suite has been deleted.
///
/// <para>Deleting a suite clears <see cref="BenchmarkRun.BenchmarkSuiteId"/> and cascades the
/// questions, which nulls every answer's <see cref="BenchmarkRunAnswer.BenchmarkQuestionId"/>. Both
/// Fundamental keys are derived from those columns, so without the identity snapshots they render as
/// the same "no value" sentinel on every affected run — and two runs that sat *different* exams then
/// agree on both, which is Tier A. The property under test is that identity survives the deletion,
/// and that where it genuinely cannot, the verdict fails closed instead of matching.</para>
/// </summary>
public class BenchmarkComparabilityIdentityTests
{
    // --- Fixture ------------------------------------------------------------------------------------

    /// <summary>
    /// A completed run with every comparability key set, so a test moves exactly the one it names.
    /// <paramref name="suiteId"/> is written to both the foreign key and the snapshot, as a live run
    /// has them; <see cref="DeleteSuite"/> then models what suite deletion leaves behind.
    /// </summary>
    private static BenchmarkRun Run(long id, long suiteId = 5, long firstQuestionId = 1)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = suiteId,
            BenchmarkSuiteIdUsed = suiteId,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(id),

            TestedModelProviderUsed = "OpenAI",
            TestedModelIdUsed = "gpt-5.6-luna",
            TestedModelDisplayNameUsed = "gpt-5.6-luna",
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

            CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
            CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
            ToolGuidesSha256 = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7",
            KnowledgeBaseHeadSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8",

            HarnessVersion = "12",
            ScoringMethodVersion = 9,
            ScoringProfileId = 1,
            MaxParallelQuestionsUsed = 1,
            PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":1.25}}"
        };

        for (int i = 0; i < 3; i++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = id * 100 + i,
                BenchmarkRunId = id,
                BenchmarkQuestionId = firstQuestionId + i,
                BenchmarkQuestionIdUsed = firstQuestionId + i,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = $"Q{i + 1}",
                AnswerText = $"A{i + 1}",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = 70 + i * 10,
                SpeedScore = 100,
                AssessedDifficulty = 50,
                DurationMs = 30000,
                TimeToFirstTokenMs = 900
            });
        }

        return run;
    }

    /// <summary>
    /// What the database holds after the run's suite is deleted: the suite foreign key cleared, and
    /// every answer's question foreign key nulled by the cascade's <c>SET NULL</c>. The snapshots are
    /// untouched — that is the point of them.
    /// </summary>
    private static BenchmarkRun DeleteSuite(BenchmarkRun run)
    {
        run.BenchmarkSuiteId = null;
        foreach (var answer in run.Answers) answer.BenchmarkQuestionId = null;
        return run;
    }

    /// <summary>A run from before the snapshot columns existed: identity in the foreign keys only.</summary>
    private static BenchmarkRun Legacy(BenchmarkRun run)
    {
        run.BenchmarkSuiteIdUsed = null;
        foreach (var answer in run.Answers) answer.BenchmarkQuestionIdUsed = null;
        return run;
    }

    /// <summary>A run whose identity is gone entirely: deleted suite, and no snapshot to fall back on.</summary>
    private static BenchmarkRun WithoutIdentity(BenchmarkRun run) => DeleteSuite(Legacy(run));

    // --- Identity survives suite deletion ----------------------------------------------------------

    [Fact]
    public void Resolve_TwoRunsFromDifferentDeletedSuites_IsNotComparable()
    {
        var a = DeleteSuite(Run(1, suiteId: 5, firstQuestionId: 1));
        var b = DeleteSuite(Run(2, suiteId: 9, firstQuestionId: 100));

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.False(result.PoolingPermitted);
        Assert.Contains(result.Differences, d => d.Name == BenchmarkComparabilityKey.SuiteKey);
    }

    [Fact]
    public void Resolve_TwoReplicatesFromOneDeletedSuite_StaysReplicate()
    {
        var a = DeleteSuite(Run(1));
        var b = DeleteSuite(Run(2));

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
        Assert.True(result.PoolingPermitted);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void Extract_SuiteKey_IsUnchangedByDeletion()
    {
        string before = SuiteKeyOf(Run(1));
        string after = SuiteKeyOf(DeleteSuite(Run(1)));

        Assert.Equal(before, after);
        Assert.Equal("5", after);
    }

    // --- Fail closed when identity is genuinely absent ----------------------------------------------

    [Fact]
    public void Resolve_TwoRunsWithNoIdentityAtAll_IsNotComparableAndNamesBothKeys()
    {
        var a = WithoutIdentity(Run(1, suiteId: 5, firstQuestionId: 1));
        var b = WithoutIdentity(Run(2, suiteId: 9, firstQuestionId: 100));

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.False(result.PoolingPermitted);

        // Nothing differs — that is precisely the danger — so the verdict has to come from absence.
        Assert.Empty(result.Differences);
        Assert.Contains(BenchmarkComparabilityKey.SuiteKey, result.Explanation);
        Assert.Contains(BenchmarkComparabilityKey.ItemRevisionsKey, result.Explanation);
        Assert.Contains("cannot be identified", result.Explanation);
    }

    [Fact]
    public void Resolve_SingleRunWithNoIdentity_IsStillReplicate()
    {
        var only = WithoutIdentity(Run(1));

        var result = BenchmarkComparabilityKey.Resolve(new[] { only });

        // A single run agrees with itself, which is what lets the series orchestrator assert a tier
        // as a group grows. The guard is a claim about two runs matching, so it does not apply.
        Assert.Equal(BenchmarkComparabilityTier.Replicate, result.Tier);
    }

    [Fact]
    public void HasAbsentFundamentalIdentity_IsTrueOnlyWhenIdentityIsMissing()
    {
        Assert.False(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity(Run(1)));
        Assert.False(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity(DeleteSuite(Run(1))));
        Assert.True(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity(WithoutIdentity(Run(1))));

        var suiteOnly = Legacy(Run(1));
        suiteOnly.BenchmarkSuiteId = null;
        Assert.True(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity(suiteOnly));
    }

    // --- Snapshot and foreign key render identically ------------------------------------------------

    [Fact]
    public void Extract_ItemRevisionSignature_ReadsSnapshotAndForeignKeyIdentically()
    {
        string fromSnapshot = ItemRevisionsKeyOf(Run(1));
        string fromForeignKey = ItemRevisionsKeyOf(Legacy(Run(1)));
        string afterDeletion = ItemRevisionsKeyOf(DeleteSuite(Run(1)));

        Assert.Equal(fromForeignKey, fromSnapshot);
        Assert.Equal(fromSnapshot, afterDeletion);
        Assert.Equal("1:1,2:1,3:1", fromSnapshot);
    }

    [Fact]
    public void ComputeKeyHash_IsUnchangedByDeletion()
    {
        // The hash is what recognises two groups as sharing a condition. Suite maintenance must not
        // move it, or a stored hash stops matching the runs it was computed from.
        Assert.Equal(
            BenchmarkComparabilityKey.ComputeKeyHash(Run(1)),
            BenchmarkComparabilityKey.ComputeKeyHash(DeleteSuite(Run(1))));
    }

    [Fact]
    public void Resolve_MembersUsingDifferentItemRevisions_IsNotComparable()
    {
        var a = Run(1);
        var b = Run(2);
        b.Answers.First().ItemRevisionUsed = 2;

        var result = BenchmarkComparabilityKey.Resolve(new[] { a, b });

        Assert.Equal(BenchmarkComparabilityTier.NotComparable, result.Tier);
        Assert.Contains(result.Differences, d => d.Name == BenchmarkComparabilityKey.ItemRevisionsKey);
    }

    // --- Helpers ------------------------------------------------------------------------------------

    private static string SuiteKeyOf(BenchmarkRun run)
        => BenchmarkComparabilityKey.Extract(run)
            .First(k => k.Name == BenchmarkComparabilityKey.SuiteKey).Value;

    private static string ItemRevisionsKeyOf(BenchmarkRun run)
        => BenchmarkComparabilityKey.Extract(run)
            .First(k => k.Name == BenchmarkComparabilityKey.ItemRevisionsKey).Value;
}
