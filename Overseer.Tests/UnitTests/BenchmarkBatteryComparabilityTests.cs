namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

/// <summary>
/// Comparability across the suites of one battery run (M8), the comparability class (M9) and the
/// eligibility of two battery runs for comparison (M7). Every fixture is one model under one
/// instrument, with exactly one thing moved per test.
/// </summary>
public class BenchmarkBatteryComparabilityTests
{
    private const string NoBoardPromptSha = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string BoardPromptSha = "0f9e8d7c6b5a49382716051f2e3d4c5b6a7980f1e2d3c4b5a6978877665544aa";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";
    private const string BoardSha = "7d4c1b9a8e6f5d3c2b1a0f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a2f1e0d9c";

    /// <summary>
    /// One fully populated run of one suite. A board suite carries a game snapshot, the board flag in
    /// its prompt options and the board variant of the system prompt; every other key is shared.
    /// </summary>
    private static BenchmarkRun Run(long id, long suiteId, bool board = false, string modelId = "gpt-5.6-luna")
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = suiteId,
            BenchmarkSuiteIdUsed = suiteId,
            SuiteName = $"Suite {suiteId}",
            Status = BenchmarkRunStatus.Completed,
            QualityIndex = 70,

            GameSnapshotSha256Used = board ? BoardSha : null,

            TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "OpenAI",
                modelId: modelId,
                displayName: modelId,
                thinkingLevel: "high",
                reasoningMode: "enabled",
                reasoningSummary: "auto",
                serviceTier: "default",
                maxOutputTokens: 32000,
                parallelExecutionMode: ParallelExecutionMode.Enabled),

            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Google",
                modelId: "gemini-3.7-pro",
                displayName: "Gemini 3.7 Pro"),
            AssessorEffectiveMaxOutputTokens = 16000,

            CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Anthropic",
                modelId: "claude-opus-5"),
            CoAssessorEffectiveMaxOutputTokens = 16000,

            SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Google",
                modelId: "gemini-3.8-flash"),
            SecondOpinionModeUsed = 1,
            SecondOpinionBlindUsed = true,

            ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Anthropic",
                modelId: "claude-opus-5"),

            CandidatePromptOptionsJson = PromptOptions(verbose: false, board: board),
            CandidateSystemPromptSha256 = board ? BoardPromptSha : NoBoardPromptSha,
            ToolGuidesSha256 = GuidesSha,
            KnowledgeBaseHeadSha = KnowledgeSha,
            WikiHeadSha = "wiki-head-1",
            SourceCodeHeadSha = "source-head-1",

            HarnessVersion = "17",
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
                BenchmarkQuestionId = suiteId * 100 + q,
                BenchmarkQuestionIdUsed = suiteId * 100 + q,
                ItemRevisionUsed = 1,
                AssessedDifficulty = 40,
                OrderIndex = q,
                QuestionText = $"Q{q}",
                Status = BenchmarkAnswerStatus.Ok,
                QualityScore = 70
            });
        }

        return run;
    }

    private static string PromptOptions(bool verbose, bool board)
        => new BenchmarkCandidatePromptOptions { VerboseMode = verbose, HasGameSnapshot = board }.ToCanonicalJson();

    private static IReadOnlyList<(int SuiteIndex, IReadOnlyList<BenchmarkRun> Runs)> Members(params (int SuiteIndex, BenchmarkRun[] Runs)[] suites)
        => suites.Select(s => (s.SuiteIndex, (IReadOnlyList<BenchmarkRun>)s.Runs)).ToList();

    private static BenchmarkBatteryComparisonSide Side(string definition, params BenchmarkRun[] runs)
        => new(definition, runs
            .GroupBy(r => r.BenchmarkSuiteIdUsed!.Value)
            .Select((g, i) => new BenchmarkBatteryComparisonSuite(i, g.Key, g.ToList()))
            .ToList());

    // --- Classification (M8) ---------------------------------------------------------------------

    [Fact]
    public void Classify_ClassifiesEveryKeyExtractEmits()
    {
        // Walks Extract's own output, so a key added there later fails here until it is classified.
        var names = BenchmarkComparabilityKey.Extract(Run(1, 10, board: true)).Select(k => k.Name).ToList();

        Assert.Equal(27, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (string name in names)
        {
            var exception = Record.Exception(() => BenchmarkBatteryComparability.Classify(name));
            Assert.True(exception == null, $"Key '{name}' is not classified for batteries.");
        }
    }

    [Fact]
    public void Classify_ThrowsForAnUnknownKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkBatteryComparability.Classify("NotAKey"));
    }

    [Fact]
    public void Classify_SplitsTheKeysAsTheMethodSpecifies()
    {
        var names = BenchmarkComparabilityKey.Extract(Run(1, 10)).Select(k => k.Name).ToList();
        var byClass = names
            .GroupBy(n => BenchmarkBatteryComparability.Classify(n))
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n, StringComparer.Ordinal).ToList());

        Assert.Equal(
            new[]
            {
                BenchmarkComparabilityKey.SuiteKey,
                BenchmarkComparabilityKey.ItemRevisionsKey,
                BenchmarkComparabilityKey.AssessedDifficultiesKey,
                BenchmarkComparabilityKey.GameSnapshotKey,
                BenchmarkComparabilityKey.CandidateSystemPromptKey
            }.OrderBy(n => n, StringComparer.Ordinal),
            byClass[BenchmarkBatteryKeyClass.SuiteIntrinsic]);

        Assert.Equal(
            new[]
            {
                BenchmarkComparabilityKey.QuestionParallelismKey,
                BenchmarkComparabilityKey.SpeedCalibrationKey,
                BenchmarkComparabilityKey.PricingSnapshotKey
            }.OrderBy(n => n, StringComparer.Ordinal),
            byClass[BenchmarkBatteryKeyClass.SpeedAndCost]);

        Assert.Contains(BenchmarkComparabilityKey.CandidatePromptOptionsKey, byClass[BenchmarkBatteryKeyClass.BatteryWide]);
        Assert.Contains(BenchmarkComparabilityKey.HarnessVersionKey, byClass[BenchmarkBatteryKeyClass.BatteryWide]);
        Assert.Equal(19, byClass[BenchmarkBatteryKeyClass.BatteryWide].Count);
    }

    [Fact]
    public void BatteryWideSignature_IgnoresOnlyTheBoardFlag()
    {
        var withBoard = new BenchmarkCandidatePromptOptions { HasGameSnapshot = true };
        var withoutBoard = new BenchmarkCandidatePromptOptions { HasGameSnapshot = false };
        var verbose = new BenchmarkCandidatePromptOptions { HasGameSnapshot = true, VerboseMode = true };

        Assert.Equal(
            withoutBoard.BatteryWideSignature(ParallelExecutionMode.Enabled),
            withBoard.BatteryWideSignature(ParallelExecutionMode.Enabled));
        Assert.NotEqual(
            withoutBoard.BatteryWideSignature(ParallelExecutionMode.Enabled),
            verbose.BatteryWideSignature(ParallelExecutionMode.Enabled));
        Assert.NotEqual(
            withBoard.ComparabilitySignature(ParallelExecutionMode.Enabled),
            withoutBoard.ComparabilitySignature(ParallelExecutionMode.Enabled));
    }

    // --- Resolve (M8) ----------------------------------------------------------------------------

    [Fact]
    public void BoardAndNonBoardSuitesOfOneModel_AreBatteryWideEqual()
    {
        var plain = Run(1, 10);
        var board = Run(2, 20, board: true);

        Assert.Equal(
            BenchmarkBatteryComparability.BatteryWideKeys(plain).Select(k => (k.Name, k.Value)),
            BenchmarkBatteryComparability.BatteryWideKeys(board).Select(k => (k.Name, k.Value)));

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { plain }), (1, new[] { board })));

        Assert.True(result.CompositePermitted, result.Explanation);
        Assert.Empty(result.BatteryWideDifferences);
        Assert.Empty(result.PromptDifferences);
        Assert.False(result.SpeedDegraded);
        Assert.False(result.CostDegraded);
        Assert.Equal(2, result.Suites.Count);
        Assert.All(result.Suites, s => Assert.Equal(BenchmarkComparabilityTier.Replicate, s.Comparability.Tier));
        Assert.Empty(result.Caveats);
    }

    [Fact]
    public void DifferentVerboseModeAcrossSuites_RefusesTheComposite()
    {
        var verbose = Run(2, 20);
        verbose.CandidatePromptOptionsJson = PromptOptions(verbose: true, board: false);

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { verbose })));

        Assert.False(result.CompositePermitted);
        Assert.Contains(result.BatteryWideDifferences, d => d.Name == BenchmarkComparabilityKey.CandidatePromptOptionsKey);
    }

    [Fact]
    public void DifferentModelAcrossSuites_RefusesTheComposite()
    {
        var result = BenchmarkBatteryComparability.Resolve(Members(
            (0, new[] { Run(1, 10, modelId: "gpt-5.6-luna") }),
            (1, new[] { Run(2, 20, modelId: "gemini-3.8-flash-lite") })));

        Assert.False(result.CompositePermitted);
        Assert.Contains(result.BatteryWideDifferences, d => d.Name == BenchmarkComparabilityKey.CandidateModelKey);
        Assert.Contains("suites 1", result.Explanation);
    }

    [Fact]
    public void DifferentHarnessVersionAcrossSuites_RefusesTheComposite()
    {
        var bumped = Run(2, 20);
        bumped.HarnessVersion = "18";

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { bumped })));

        Assert.False(result.CompositePermitted);
        var difference = Assert.Single(result.BatteryWideDifferences);
        Assert.Equal(BenchmarkComparabilityKey.HarnessVersionKey, difference.Name);
    }

    [Fact]
    public void TwoNonBoardSuitesWithDifferentPromptHashes_RefuseTheComposite()
    {
        var edited = Run(2, 20);
        edited.CandidateSystemPromptSha256 = new string('b', 64);

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { edited })));

        Assert.False(result.CompositePermitted);
        Assert.Empty(result.BatteryWideDifferences);
        var difference = Assert.Single(result.PromptDifferences);
        Assert.Equal(BenchmarkComparabilityKey.CandidateSystemPromptKey, difference.Name);
        Assert.Contains("board class", result.Explanation);
    }

    [Fact]
    public void ABoardAndANonBoardSuiteWithDifferentPromptHashes_Pass()
    {
        var result = BenchmarkBatteryComparability.Resolve(Members(
            (0, new[] { Run(1, 10) }),
            (1, new[] { Run(2, 20, board: true) }),
            (2, new[] { Run(3, 30) })));

        Assert.True(result.CompositePermitted, result.Explanation);
        Assert.Empty(result.PromptDifferences);
    }

    [Fact]
    public void APricingSnapshotDifference_DegradesCostOnly()
    {
        var repriced = Run(2, 20);
        repriced.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":2.50}}";

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { repriced })));

        Assert.True(result.CompositePermitted, result.Explanation);
        Assert.True(result.CostDegraded);
        Assert.False(result.SpeedDegraded);
        var difference = Assert.Single(result.SpeedAndCostDifferences);
        Assert.Equal(BenchmarkComparabilityKey.PricingSnapshotKey, difference.Name);
    }

    [Fact]
    public void ADifferingWikiHead_AddsACaveatAndRefusesNothing()
    {
        var moved = Run(2, 20);
        moved.WikiHeadSha = "wiki-head-2";

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { moved })));

        Assert.True(result.CompositePermitted, result.Explanation);
        var caveat = Assert.Single(result.Caveats);
        Assert.Contains("WikiHeadSha", caveat);
    }

    [Fact]
    public void AnUnrecordedWikiHead_IsNotADifference()
    {
        var unrecorded = Run(2, 20);
        unrecorded.WikiHeadSha = null;

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { Run(1, 10) }), (1, new[] { unrecorded })));

        Assert.Empty(result.Caveats);
    }

    /// <summary>A corpus index fingerprint column with the GnollHack wiki at <paramref name="wikiSha"/>; null writes it as not indexed yet.</summary>
    private static string CorpusIndexFingerprints(string? wikiSha)
        => "{\"gnollhackWiki\":" + (wikiSha == null ? "null" : "{\"sha256\":\"" + wikiSha + "\",\"fileCount\":410,\"indexedAtUtc\":\"2026-10-04T09:30:00.000Z\"}")
            + ",\"gnollhackSource\":null,\"knowledgeBase\":null,\"nethackWiki\":null,\"nethackSource\":null}";

    private static readonly string WikiIndexOne = "1a2b3c4d5e6f" + new string('0', 52);
    private static readonly string WikiIndexTwo = "6f5e4d3c2b1a" + new string('0', 52);

    [Fact]
    public void ADifferingCorpusIndexFingerprint_AddsACaveatNamingTheCorpusAndTheRuns()
    {
        var first = Run(80, 10);
        first.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(WikiIndexOne);
        var second = Run(81, 20);
        second.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(WikiIndexTwo);

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { first }), (1, new[] { second })));

        Assert.True(result.CompositePermitted, result.Explanation);
        var caveat = Assert.Single(result.Caveats);
        Assert.StartsWith("The GnollHack wiki index differed between runs 80 and 81: 1a2b3c4d5e6f (runs 80; suites 0) vs 6f5e4d3c2b1a (runs 81; suites 1).", caveat);
        Assert.Contains("provenance, not a comparability key", caveat);
    }

    [Fact]
    public void AnEqualCorpusIndexFingerprint_AddsNoCaveat()
    {
        var first = Run(80, 10);
        first.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(WikiIndexOne);
        var second = Run(81, 20);
        second.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(WikiIndexOne);

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { first }), (1, new[] { second })));

        Assert.Empty(result.Caveats);
    }

    [Fact]
    public void ACorpusIndexFingerprintRecordedOnOneSideOnly_IsNotCompared()
    {
        // One member without the column, one with the corpus not indexed yet: neither is compared.
        var recorded = Run(80, 10);
        recorded.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(WikiIndexOne);
        var unrecorded = Run(81, 20);
        unrecorded.CorpusIndexFingerprintsJson = null;
        var notIndexed = Run(82, 20);
        notIndexed.CorpusIndexFingerprintsJson = CorpusIndexFingerprints(null);

        var result = BenchmarkBatteryComparability.Resolve(Members((0, new[] { recorded }), (1, new[] { unrecorded, notIndexed })));

        Assert.Empty(result.Caveats);
    }

    [Fact]
    public void ASuiteWhoseOwnRunsDoNotPool_RefusesAndIsNamed()
    {
        var revised = Run(3, 10);
        revised.Answers[0].ItemRevisionUsed = 2;

        var result = BenchmarkBatteryComparability.Resolve(Members(
            (0, new[] { Run(1, 10), revised }),
            (1, new[] { Run(2, 20) })));

        Assert.False(result.CompositePermitted);
        Assert.False(result.Suites.Single(s => s.SuiteIndex == 0).Comparability.PoolingPermitted);
        Assert.Contains("Suite 0 ('Suite 10')", result.Explanation);
    }

    [Fact]
    public void ASuiteWithNoRuns_IsNotJudged()
    {
        var result = BenchmarkBatteryComparability.Resolve(Members(
            (0, new[] { Run(1, 10) }),
            (1, Array.Empty<BenchmarkRun>())));

        Assert.True(result.CompositePermitted, result.Explanation);
        Assert.Equal(new[] { 0 }, result.Suites.Select(s => s.SuiteIndex));
    }

    [Fact]
    public void NoRunsAtAll_IsNotPermitted()
    {
        var result = BenchmarkBatteryComparability.Resolve(Members());

        Assert.False(result.CompositePermitted);
    }

    // --- Comparability class (M9) ----------------------------------------------------------------

    [Fact]
    public void ComparabilityClass_IsEqualForTwoModelsUnderOneInstrument()
    {
        string? first = BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { Run(1, 10, modelId: "a") }),
            (20L, (IReadOnlyList<BenchmarkRun>)new[] { Run(2, 20, board: true, modelId: "a") })
        });
        string? second = BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (20L, (IReadOnlyList<BenchmarkRun>)new[] { Run(4, 20, board: true, modelId: "b") }),
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { Run(3, 10, modelId: "b") })
        });

        Assert.NotNull(first);
        Assert.Equal(64, first!.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComparabilityClass_DiffersAfterAnItemRevisionOrAHarnessChange()
    {
        string? baseline = BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { Run(1, 10) }),
            (20L, (IReadOnlyList<BenchmarkRun>)new[] { Run(2, 20) })
        });

        var revised = Run(4, 20);
        revised.Answers[1].ItemRevisionUsed = 2;
        string? afterRevision = BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { Run(3, 10) }),
            (20L, (IReadOnlyList<BenchmarkRun>)new[] { revised })
        });

        var bumped1 = Run(5, 10);
        var bumped2 = Run(6, 20);
        bumped1.HarnessVersion = "18";
        bumped2.HarnessVersion = "18";
        string? afterHarness = BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { bumped1 }),
            (20L, (IReadOnlyList<BenchmarkRun>)new[] { bumped2 })
        });

        Assert.NotEqual(baseline, afterRevision);
        Assert.NotEqual(baseline, afterHarness);
    }

    [Fact]
    public void ComparabilityClass_IsNullWhenASuiteHasNoRun()
    {
        Assert.Null(BenchmarkBatteryComparability.ComparabilityClass(new[]
        {
            (10L, (IReadOnlyList<BenchmarkRun>)new[] { Run(1, 10) }),
            (20L, (IReadOnlyList<BenchmarkRun>)Array.Empty<BenchmarkRun>())
        }));
    }

    // --- CanCompare (M7) -------------------------------------------------------------------------

    [Fact]
    public void CanCompare_TwoModelsUnderOneInstrument_IsAModelComparison()
    {
        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10, modelId: "a"), Run(2, 20, board: true, modelId: "a")),
            Side("def", Run(3, 10, modelId: "b"), Run(4, 20, board: true, modelId: "b")));

        Assert.True(result.Allowed, result.Explanation);
        Assert.Equal(BenchmarkBatteryComparisonKind.ModelComparison, result.Kind);
        Assert.Equal(new[] { BenchmarkComparabilityKey.CandidateModelKey }, result.DifferingKeys);
        Assert.Equal(2, result.Differences.Count);
        Assert.False(result.SpeedDegraded);
        Assert.False(result.CostDegraded);
    }

    [Fact]
    public void CanCompare_AModelComparisonWithDifferentPrices_DegradesCostOnly()
    {
        var repriced = Run(3, 10, modelId: "b");
        repriced.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":9.00}}";

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10, modelId: "a"), Run(2, 20, modelId: "a")),
            Side("def", repriced, Run(4, 20, modelId: "b")));

        Assert.True(result.Allowed, result.Explanation);
        Assert.Equal(BenchmarkBatteryComparisonKind.ModelComparison, result.Kind);
        Assert.True(result.CostDegraded);
        Assert.False(result.SpeedDegraded);
    }

    [Fact]
    public void CanCompare_OnlyTheResponseStyleDiffers_IsRefused()
    {
        var verbose1 = Run(3, 10);
        var verbose2 = Run(4, 20);
        verbose1.CandidatePromptOptionsJson = PromptOptions(verbose: true, board: false);
        verbose2.CandidatePromptOptionsJson = PromptOptions(verbose: true, board: false);

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10), Run(2, 20)),
            Side("def", verbose1, verbose2));

        Assert.False(result.Allowed);
        Assert.Null(result.Kind);
        Assert.Contains(BenchmarkComparabilityKey.CandidatePromptOptionsKey, result.DifferingKeys);
    }

    [Fact]
    public void CanCompare_AToolGuidesChangeInEverySuite_IsAVerification()
    {
        var after1 = Run(3, 10);
        var after2 = Run(4, 20, board: true);
        after1.ToolGuidesSha256 = new string('c', 64);
        after2.ToolGuidesSha256 = new string('c', 64);

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10), Run(2, 20, board: true)),
            Side("def", after1, after2));

        Assert.True(result.Allowed, result.Explanation);
        Assert.Equal(BenchmarkBatteryComparisonKind.Verification, result.Kind);
        Assert.Equal(new[] { BenchmarkComparabilityKey.ToolGuidesKey }, result.DifferingKeys);
    }

    [Fact]
    public void CanCompare_APromptChangeInEverySuite_IsOneChangeAndAVerification()
    {
        // The board and the non-board suite move to two different new hashes; it is still one key name.
        var after1 = Run(3, 10);
        var after2 = Run(4, 20, board: true);
        after1.CandidateSystemPromptSha256 = new string('d', 64);
        after2.CandidateSystemPromptSha256 = new string('e', 64);

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10), Run(2, 20, board: true)),
            Side("def", after1, after2));

        Assert.True(result.Allowed, result.Explanation);
        Assert.Equal(BenchmarkBatteryComparisonKind.Verification, result.Kind);
        Assert.Equal(2, result.Differences.Count);
    }

    [Fact]
    public void CanCompare_ADefinitionMismatch_IsRefused()
    {
        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def-1", Run(1, 10, modelId: "a"), Run(2, 20, modelId: "a")),
            Side("def-2", Run(3, 10, modelId: "b"), Run(4, 20, modelId: "b")));

        Assert.False(result.Allowed);
        Assert.Contains("different definitions", result.Explanation);
    }

    [Fact]
    public void CanCompare_TwoDifferingInstrumentKeyNames_IsRefused()
    {
        var after1 = Run(3, 10);
        var after2 = Run(4, 20);
        after1.CandidateSystemPromptSha256 = new string('d', 64);
        after2.CandidateSystemPromptSha256 = new string('d', 64);
        after1.HarnessVersion = "18";
        after2.HarnessVersion = "18";

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10), Run(2, 20)),
            Side("def", after1, after2));

        Assert.False(result.Allowed);
        Assert.Contains("2 instrument keys differ", result.Explanation);
    }

    [Fact]
    public void CanCompare_TwoModelsUnderDifferentHarnesses_IsRefused()
    {
        var other1 = Run(3, 10, modelId: "b");
        var other2 = Run(4, 20, modelId: "b");
        other1.HarnessVersion = "18";
        other2.HarnessVersion = "18";

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10, modelId: "a"), Run(2, 20, modelId: "a")),
            Side("def", other1, other2));

        Assert.False(result.Allowed);
        Assert.Contains(BenchmarkComparabilityKey.HarnessVersionKey, result.DifferingKeys);
    }

    [Fact]
    public void CanCompare_DifferentExams_AreRefused()
    {
        var revised = Run(3, 10, modelId: "b");
        revised.Answers[0].ItemRevisionUsed = 2;

        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10, modelId: "a"), Run(2, 20, modelId: "a")),
            Side("def", revised, Run(4, 20, modelId: "b")));

        Assert.False(result.Allowed);
        Assert.Contains("different exams", result.Explanation);
    }

    [Fact]
    public void CanCompare_ASuiteWithoutRunsOnOneSide_IsRefused()
    {
        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10, modelId: "a"), Run(2, 20, modelId: "a")),
            Side("def", Run(3, 10, modelId: "b")));

        Assert.False(result.Allowed);
        Assert.Contains("#20 has no usable run on the treatment side", result.Explanation);
    }

    [Fact]
    public void CanCompare_TwoReplicatesOfOneCondition_IsRefused()
    {
        var result = BenchmarkBatteryComparability.CanCompare(
            Side("def", Run(1, 10), Run(2, 20)),
            Side("def", Run(3, 10), Run(4, 20)));

        Assert.False(result.Allowed);
        Assert.Empty(result.Differences);
    }

    // --- Attaching existing runs ------------------------------------------------------------------

    private const long AttachConfigId = 7;

    /// <summary>A run of <paramref name="suiteId"/> by the tested configuration the attach targets expect.</summary>
    private static BenchmarkRun AttachRun(long id, long suiteId, string modelId = "gpt-5.6-luna")
    {
        var run = Run(id, suiteId, modelId: modelId);
        run.TestedModelConfigurationId = AttachConfigId;
        return run;
    }

    /// <summary>
    /// Slot (suite index 1, suite #20) of a battery run whose suite index 0 (#10) holds
    /// <paramref name="members"/>, with the fixture's instrument as the recorded fingerprint.
    /// </summary>
    private static BenchmarkBatteryAttachTarget AttachTarget(params BenchmarkRun[] members) => new()
    {
        SuiteIndex = 1,
        SuiteId = 20,
        SuiteName = "Suite 20",
        TestedModelConfigurationId = AttachConfigId,
        Fingerprint = new BenchmarkInstrumentFingerprint(NoBoardPromptSha, GuidesSha, KnowledgeSha, "wiki-head-1", "source-head-1"),
        ScoringMethodVersion = 9,
        HarnessVersion = "17",
        UsableMembers = members.Length == 0
            ? Array.Empty<(int, IReadOnlyList<BenchmarkRun>)>()
            : Members((0, members)),
        OccupiedRunIds = members.Select(m => m.Id).ToList()
    };

    [Fact]
    public void CheckAttach_AcceptsARunMatchingTheSlotTheConfigurationTheInstrumentAndTheMembers()
    {
        var check = BenchmarkBatteryComparability.CheckAttach(AttachRun(2, 20), AttachTarget(AttachRun(1, 10)));

        Assert.True(check.Eligible);
        Assert.Null(check.Reason);
        Assert.True(BenchmarkBatteryComparability.CheckAttach(AttachRun(2, 20), AttachTarget()).Eligible);
    }

    [Fact]
    public void CheckAttach_RefusesARunOfAnotherSuite()
    {
        var check = BenchmarkBatteryComparability.CheckAttach(AttachRun(2, 30), AttachTarget());

        Assert.False(check.Eligible);
        Assert.Contains("suite #30", check.Reason);
        Assert.Contains("'Suite 20' (#20)", check.Reason);
    }

    [Fact]
    public void CheckAttach_ReadsTheSuiteUsedBeforeTheSuiteReference()
    {
        var run = AttachRun(2, 20);
        run.BenchmarkSuiteId = null;

        Assert.True(BenchmarkBatteryComparability.CheckAttach(run, AttachTarget()).Eligible);

        run.BenchmarkSuiteId = 20;
        run.BenchmarkSuiteIdUsed = 30;
        Assert.False(BenchmarkBatteryComparability.CheckAttach(run, AttachTarget()).Eligible);
    }

    [Theory]
    [InlineData(BenchmarkRunStatus.Running, 70, null, BenchmarkBatteryPlanner.RunNotFinishedReason)]
    [InlineData(BenchmarkRunStatus.Failed, null, null, BenchmarkBatteryPlanner.RunFailedReason)]
    [InlineData(BenchmarkRunStatus.Canceled, null, null, BenchmarkBatteryPlanner.RunCanceledReason)]
    [InlineData(BenchmarkRunStatus.CompletedWithErrors, null, 1, BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason)]
    public void CheckAttach_RefusesARunThatIsNotUsable(BenchmarkRunStatus status, int? qualityIndex, int? terminalFailures, string reason)
    {
        var run = AttachRun(2, 20);
        run.Status = status;
        run.QualityIndex = qualityIndex;
        run.TerminalFailureAnswerCount = terminalFailures;

        var check = BenchmarkBatteryComparability.CheckAttach(run, AttachTarget());

        Assert.False(check.Eligible);
        Assert.Contains(reason, check.Reason);
    }

    [Fact]
    public void CheckAttach_RefusesARunGradedUnderAnotherScoringMethod()
    {
        var run = AttachRun(2, 20);
        run.ScoringMethodVersion = 8;

        var check = BenchmarkBatteryComparability.CheckAttach(run, AttachTarget());

        Assert.False(check.Eligible);
        Assert.Contains("scoring method 8", check.Reason);
    }

    [Fact]
    public void CheckAttach_RefusesARunOfAnotherTestedConfiguration()
    {
        var run = AttachRun(2, 20);
        run.TestedModelConfigurationId = 8;

        var check = BenchmarkBatteryComparability.CheckAttach(run, AttachTarget());

        Assert.False(check.Eligible);
        Assert.Contains("tested configuration #8", check.Reason);
    }

    /// <summary>
    /// All five hashes are compared, the wiki and source HEADs included (decision 8), and a hash
    /// missing on either side does not count.
    /// </summary>
    [Fact]
    public void CheckAttach_RefusesAMovedInstrumentHash_ButNotAMissingOne()
    {
        var moved = AttachRun(2, 20);
        moved.WikiHeadSha = "wiki-head-2";

        var check = BenchmarkBatteryComparability.CheckAttach(moved, AttachTarget());
        Assert.False(check.Eligible);
        Assert.Contains("WikiHeadSha", check.Reason);

        var unrecorded = AttachRun(3, 20);
        unrecorded.WikiHeadSha = null;
        Assert.True(BenchmarkBatteryComparability.CheckAttach(unrecorded, AttachTarget()).Eligible);

        Assert.True(BenchmarkBatteryComparability.CheckAttach(moved, AttachTarget() with
        {
            Fingerprint = new BenchmarkInstrumentFingerprint(NoBoardPromptSha, GuidesSha, KnowledgeSha, null, "source-head-1")
        }).Eligible);
    }

    [Fact]
    public void CheckAttach_RefusesARunThatAlreadyFillsAnotherSlot()
    {
        var check = BenchmarkBatteryComparability.CheckAttach(AttachRun(2, 20), AttachTarget() with { OccupiedRunIds = new long[] { 2 } });

        Assert.False(check.Eligible);
        Assert.Contains("already fills another slot", check.Reason);
    }

    /// <summary>
    /// With no usable member to agree with, the run must carry this build's harness; with members,
    /// agreeing with them is what counts, and the composite verdict judges that.
    /// </summary>
    [Fact]
    public void CheckAttach_RequiresTheCurrentHarness_OnlyWhenThereIsNoMemberToAgreeWith()
    {
        var old = AttachRun(2, 20);
        old.HarnessVersion = "16";

        var alone = BenchmarkBatteryComparability.CheckAttach(old, AttachTarget());
        Assert.False(alone.Eligible);
        Assert.Contains("harness 16", alone.Reason);

        var oldMember = AttachRun(1, 10);
        oldMember.HarnessVersion = "16";
        Assert.True(BenchmarkBatteryComparability.CheckAttach(old, AttachTarget(oldMember)).Eligible);
    }

    [Fact]
    public void CheckAttach_RefusesARunThatWouldRefuseTheComposite()
    {
        var check = BenchmarkBatteryComparability.CheckAttach(
            AttachRun(2, 20, modelId: "other-model"), AttachTarget(AttachRun(1, 10)));

        Assert.False(check.Eligible);
        Assert.Contains("would not combine", check.Reason);
        Assert.Contains("refused", check.Reason);
    }

    [Fact]
    public void AttachSlotRefusal_AdmitsAnEmptySlot_AndOneHeldByAMemberThatIsNotUsable()
    {
        var usableRun = AttachRun(1, 10);
        var withheld = AttachRun(2, 10);
        withheld.Status = BenchmarkRunStatus.CompletedWithErrors;
        withheld.QualityIndex = null;
        var member = new BenchmarkBatteryRunMember { SuiteIndex = 0, Round = 1, BenchmarkRunId = 1 };

        Assert.Null(BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1, null, null));
        Assert.Null(BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1, member, withheld));
        Assert.Null(BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1, member, null));
        Assert.Null(BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1,
            new BenchmarkBatteryRunMember { BenchmarkRunId = 1, GuardFailure = "moved" }, usableRun));
        Assert.Null(BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1,
            new BenchmarkBatteryRunMember { BenchmarkRunId = 1, Superseded = true }, usableRun));

        Assert.Contains("already holds a usable member (run #1)",
            BenchmarkBatteryComparability.AttachSlotRefusal(2, 1, 0, 1, member, usableRun));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    public void AttachSlotRefusal_RefusesASlotOutsideTheGrid(int suiteIndex, int round)
    {
        Assert.NotNull(BenchmarkBatteryComparability.AttachSlotRefusal(2, 2, suiteIndex, round, null, null));
    }
}
