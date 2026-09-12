namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The comparability index: what the model-comparison picker is told before anything is compared.
///
/// <para>The property being defended is agreement with the comparison itself. A picker that files a
/// source under "Condition A" and then watches Compare exclude it is worse than no picker at all, so
/// the load-bearing test here asserts the largest condition against
/// <see cref="BenchmarkCrossModelComparability.Resolve"/>'s own baseline over the same inputs.</para>
///
/// <para>The second property is the stubbed answer graph. The index loads three answer columns
/// instead of the whole graph, and a run signed from those stubs must sign identically to a fully
/// loaded one, or the index buckets runs the comparison then splits.</para>
/// </summary>
public class BenchmarkComparabilityIndexTests
{
    // --- Fixture ------------------------------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> NewDatabase()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    /// <summary>
    /// A completed run over suite 5, with three answered questions at item revision 1. Every
    /// comparability key is set, so a test moves exactly the one key it names.
    /// </summary>
    private static BenchmarkRun Run(long id, string modelId = "gpt-5.6-luna")
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(id),

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
                BenchmarkQuestionId = i + 1,
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

    private static BenchmarkRunGroup Group(long id, string name, params long[] runIds)
        => new()
        {
            Id = id,
            Name = name,
            BenchmarkSuiteId = 5,
            Members = runIds
                .Select((runId, i) => new BenchmarkRunGroupMember
                {
                    Id = id * 100 + i,
                    BenchmarkRunGroupId = id,
                    BenchmarkRunId = runId
                })
                .ToList()
        };

    private static async Task SeedAsync(
        DbContextOptions<ApplicationDbContext> options,
        IEnumerable<BenchmarkRun> runs,
        IEnumerable<BenchmarkRunGroup>? groups = null)
    {
        using var db = new ApplicationDbContext(options);
        db.BenchmarkRuns.AddRange(runs);
        if (groups != null) db.BenchmarkRunGroups.AddRange(groups);
        await db.SaveChangesAsync();
    }

    private static async Task<BenchmarkComparabilityIndexDto> IndexAsync(
        DbContextOptions<ApplicationDbContext> options,
        IEnumerable<long>? runIds = null,
        IEnumerable<long>? groupIds = null)
    {
        using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparabilityIndexService(db);

        var (result, error) = await service.BuildAsync(new BenchmarkComparabilityIndexRequest
        {
            RunIds = (runIds ?? Array.Empty<long>()).ToList(),
            GroupIds = (groupIds ?? Array.Empty<long>()).ToList()
        });

        Assert.Null(error);
        Assert.NotNull(result);
        return result!;
    }

    private static BenchmarkComparabilityIndexEntryDto Entry(
        BenchmarkComparabilityIndexDto dto, string key)
        => dto.Entries.Single(e => e.Key == key);

    private static BenchmarkComparabilityKeyValueDto KeyValue(
        BenchmarkComparabilityIndexDto dto, string name)
        => dto.LargestConditionKeys.Single(k => k.Name == name);

    // --- Bucketing ----------------------------------------------------------------------------------

    [Fact]
    public async Task TwoRunsDifferingOnlyOnTheModel_ShareOneCondition()
    {
        // The model axis is the subject of a cross-model comparison, so a difference on it must not
        // split the picker's conditions.
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1, "gpt-5.6-luna"), Run(2, "gemini-3.8-flash-lite") });

        var dto = await IndexAsync(options, runIds: new long[] { 1, 2 });

        Assert.Single(dto.Conditions);
        Assert.Equal("Condition A", dto.Conditions[0].Label);
        Assert.Equal(2, dto.Conditions[0].SourceCount);
        Assert.Equal(2, dto.Conditions[0].RunCount);

        Assert.Equal(1, Entry(dto, "run:1").ConditionOrdinal);
        Assert.Equal(1, Entry(dto, "run:2").ConditionOrdinal);
        Assert.Equal(Entry(dto, "run:1").Signature, Entry(dto, "run:2").Signature);
        Assert.Empty(Entry(dto, "run:2").DifferencesFromLargest);
    }

    [Fact]
    public async Task ARunDifferingOnTheScoringMethodVersion_IsASecondCondition()
    {
        var old = Run(3, "claude-opus-5");
        old.ScoringMethodVersion = 8;

        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1), Run(2, "gemini-3.8-flash-lite"), old });

        var dto = await IndexAsync(options, runIds: new long[] { 1, 2, 3 });

        Assert.Equal(2, dto.Conditions.Count);
        Assert.Equal(2, Entry(dto, "run:3").ConditionOrdinal);
        Assert.Equal("Condition B", Entry(dto, "run:3").ConditionLabel);
        Assert.NotEqual(Entry(dto, "run:1").Signature, Entry(dto, "run:3").Signature);

        // Named, with both values, in the shape the tier dialog renders.
        var difference = Assert.Single(Entry(dto, "run:3").DifferencesFromLargest);
        Assert.Equal(BenchmarkComparabilityKey.ScoringMethodVersionKey, difference.Name);
        Assert.Equal(new[] { "9", "8" }, difference.Variants.Select(v => v.Value));
        Assert.Equal(new long[] { 1, 2 }, difference.Variants[0].RunIds);
        Assert.Equal(new long[] { 3 }, difference.Variants[1].RunIds);
    }

    [Fact]
    public async Task TheLargerCondition_IsOrdinalOne_EvenWhenTheSmallerOneComesFirst()
    {
        // Ordering by cohort size rather than by input order is the whole point: after a scoring
        // method version moves, the corpus really is split, and the index must name the larger half.
        var old = Run(3, "claude-opus-5");
        old.ScoringMethodVersion = 8;

        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1), Run(2, "gemini-3.8-flash-lite"), old });

        var dto = await IndexAsync(options, runIds: new long[] { 3, 1, 2 });

        Assert.Equal(2, dto.Conditions[0].SourceCount);
        Assert.Equal(1, Entry(dto, "run:1").ConditionOrdinal);
        Assert.Equal(1, Entry(dto, "run:2").ConditionOrdinal);
        Assert.Equal(2, Entry(dto, "run:3").ConditionOrdinal);

        // The legend prints the larger half's condition.
        Assert.Equal("9", KeyValue(dto, BenchmarkComparabilityKey.ScoringMethodVersionKey).Value);
    }

    [Fact]
    public async Task TheIndexEmitsTheThreeKeyNameLists_FromTheTaxonomy()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Equal(BenchmarkCrossModelComparability.ModelAxisKeys.ToList(), dto.ModelAxisKeyNames);
        Assert.Equal(BenchmarkCrossModelComparability.DegradingKeys.ToList(), dto.DegradingKeyNames);
        Assert.Contains(BenchmarkComparabilityKey.ScoringMethodVersionKey, dto.MustMatchKeyNames);
        Assert.DoesNotContain(BenchmarkComparabilityKey.CandidateModelKey, dto.MustMatchKeyNames);
        Assert.DoesNotContain(BenchmarkComparabilityKey.PricingSnapshotKey, dto.MustMatchKeyNames);

        // The degrading values travel on the entry, so the picker can warn before Compare.
        Assert.Equal("1", Entry(dto, "run:1").QuestionParallelism);
        Assert.NotEqual(string.Empty, Entry(dto, "run:1").PricingSnapshot);
    }

    // --- Self-inconsistent groups ----------------------------------------------------------------------

    [Fact]
    public async Task AGroupWhoseMembersDifferOnTheHarnessVersion_IsSelfInconsistent()
    {
        var moved = Run(2, "gpt-5.6-luna");
        moved.HarnessVersion = "13";

        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1), moved }, new[] { Group(7, "Mixed harness", 1, 2) });

        var dto = await IndexAsync(options, groupIds: new long[] { 7 });

        var entry = Entry(dto, "group:7");
        Assert.True(entry.SelfInconsistent);
        Assert.Equal(new[] { BenchmarkComparabilityKey.HarnessVersionKey }, entry.SelfInconsistentKeys);
        Assert.Equal(0, entry.ConditionOrdinal);
        Assert.Equal("Self-inconsistent", entry.ConditionLabel);

        // Not one point, so it defines no condition either.
        Assert.Empty(dto.Conditions);
    }

    [Fact]
    public async Task AGroupWhoseMembersDifferOnTheModel_IsSelfInconsistent()
    {
        // A model-axis difference is fine *between* points and fatal *within* one: such a group is
        // not one model measured several times.
        var options = NewDatabase();
        await SeedAsync(
            options,
            new[] { Run(1, "gpt-5.6-luna"), Run(2, "gemini-3.8-flash-lite"), Run(3, "gpt-5.6-luna") },
            new[] { Group(7, "Two models", 1, 2) });

        var dto = await IndexAsync(options, runIds: new long[] { 3 }, groupIds: new long[] { 7 });

        var entry = Entry(dto, "group:7");
        Assert.True(entry.SelfInconsistent);
        Assert.Equal(new[] { BenchmarkComparabilityKey.CandidateModelKey }, entry.SelfInconsistentKeys);
        Assert.Equal(0, entry.ConditionOrdinal);

        // The coherent run still gets a condition of its own.
        Assert.Equal(1, Entry(dto, "run:3").ConditionOrdinal);
        Assert.Single(dto.Conditions);
        Assert.Equal(1, dto.Conditions[0].SourceCount);
    }

    [Fact]
    public async Task AGroupWithNoLoadableMembers_IsUnassigned()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) }, new[] { Group(7, "Empty") });

        var dto = await IndexAsync(options, runIds: new long[] { 1 }, groupIds: new long[] { 7 });

        var entry = Entry(dto, "group:7");
        Assert.Equal(0, entry.ConditionOrdinal);
        Assert.False(entry.SelfInconsistent);
        Assert.Equal("No runs", entry.ConditionLabel);
        Assert.Equal(string.Empty, entry.Signature);
    }

    // --- Agreement with the comparison ------------------------------------------------------------------

    [Fact]
    public async Task TheLargestCondition_IsTheBaselineTheComparisonWouldChoose()
    {
        // The load-bearing test. Anything that makes the index and BenchmarkCrossModelComparability
        // disagree makes the picker promise a chart the comparison then refuses.
        var oldScoring = Run(3, "claude-opus-5");
        oldScoring.ScoringMethodVersion = 8;

        var otherGuides = Run(4, "gemini-3.8-flash-lite");
        otherGuides.ToolGuidesSha256 = "0000000000000000000000000000000a";

        var runs = new[] { Run(1), Run(2, "gemini-3.8-flash-lite"), oldScoring, otherGuides };
        var requested = new long[] { 3, 4, 1, 2 };

        var options = NewDatabase();
        await SeedAsync(options, runs);

        var dto = await IndexAsync(options, runIds: requested);

        var byId = runs.ToDictionary(r => r.Id);
        var resolved = BenchmarkCrossModelComparability.Resolve(
            requested
                .Select(id => new BenchmarkCrossModelEntry
                {
                    Key = $"run:{id}",
                    Runs = new[] { byId[id] }
                })
                .ToList());

        var largest = dto.Entries
            .Where(e => e.ConditionOrdinal == 1)
            .Select(e => e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            resolved.BaselineEntryKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            largest);

        // And the legend prints the same condition the comparison would report.
        foreach (var pair in resolved.BaselineKeyValues)
        {
            Assert.Equal(pair.Value, KeyValue(dto, pair.Key).Value);
        }
    }

    [Fact]
    public async Task TheLargestCondition_MatchesTheBaseline_WhenGroupsOutweighRuns()
    {
        // The tie-break is source count first, then total run count, then first appearance. A group
        // of three runs in a minority condition must not take the baseline from two single runs.
        var moved = Run(3, "claude-opus-5");
        moved.ScoringMethodVersion = 8;
        var movedToo = Run(4, "claude-opus-5");
        movedToo.ScoringMethodVersion = 8;
        var movedAgain = Run(5, "claude-opus-5");
        movedAgain.ScoringMethodVersion = 8;

        var runs = new[] { Run(1), Run(2, "gemini-3.8-flash-lite"), moved, movedToo, movedAgain };

        var options = NewDatabase();
        await SeedAsync(options, runs, new[] { Group(7, "Old scoring", 3, 4, 5) });

        var dto = await IndexAsync(options, runIds: new long[] { 1, 2 }, groupIds: new long[] { 7 });

        var byId = runs.ToDictionary(r => r.Id);
        var entries = new List<BenchmarkCrossModelEntry>
        {
            new() { Key = "run:1", Runs = new[] { byId[1] } },
            new() { Key = "run:2", Runs = new[] { byId[2] } },
            new() { Key = "group:7", Runs = new[] { byId[3], byId[4], byId[5] } }
        };

        var resolved = BenchmarkCrossModelComparability.Resolve(entries);

        Assert.Equal(
            resolved.BaselineEntryKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            dto.Entries.Where(e => e.ConditionOrdinal == 1).Select(e => e.Key)
                .OrderBy(k => k, StringComparer.Ordinal).ToList());

        Assert.Equal(2, Entry(dto, "group:7").ConditionOrdinal);
        Assert.Equal(3, dto.Conditions[1].RunCount);
    }

    // --- The stubbed answer graph ------------------------------------------------------------------------

    [Fact]
    public async Task AStubbedAnswerGraph_SignsIdenticallyToAFullyLoadedRun()
    {
        // The index loads four answer columns instead of the whole graph. The item revision and
        // assessed difficulty signatures read exactly those four, so the signatures must coincide.
        var run = Run(1);

        var options = NewDatabase();
        await SeedAsync(options, new[] { run });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Equal(BenchmarkCrossModelComparability.MustMatchSignature(run), Entry(dto, "run:1").Signature);
    }

    [Fact]
    public async Task ADifferingItemRevision_MovesTheSignature()
    {
        // The other half of the same invariant: the stubs must carry enough to *split* two runs
        // graded against different answer keys, not merely enough to match identical ones.
        var rubricEdited = Run(2, "gemini-3.8-flash-lite");
        rubricEdited.Answers[1].ItemRevisionUsed = 2;

        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1), rubricEdited });

        var dto = await IndexAsync(options, runIds: new long[] { 1, 2 });

        Assert.Equal(2, dto.Conditions.Count);
        Assert.Equal(
            BenchmarkComparabilityKey.ItemRevisionsKey,
            Assert.Single(Entry(dto, "run:2").DifferencesFromLargest).Name);
    }

    // --- The reference-condition methods block -----------------------------------------------------------

    [Fact]
    public async Task LargestConditionKeys_AreInCanonicalKeyOrder()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Equal(dto.MustMatchKeyNames, dto.LargestConditionKeys.Select(k => k.Name).ToList());
    }

    [Fact]
    public async Task LargestConditionKeys_CarryKindAndValueKind_AndTheValueVerbatim()
    {
        var options = NewDatabase();
        var run = Run(1);
        await SeedAsync(options, new[] { run });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });
        var extracted = BenchmarkComparabilityKey.Extract(run).ToDictionary(k => k.Name, k => k.Value);

        var suite = KeyValue(dto, BenchmarkComparabilityKey.SuiteKey);
        Assert.Equal(BenchmarkComparabilityKeyKind.Fundamental.ToString(), suite.Kind);
        Assert.Equal(BenchmarkComparabilityValueKind.Identifier.ToString(), suite.ValueKind);
        Assert.Equal(extracted[BenchmarkComparabilityKey.SuiteKey], suite.Value);

        // A hash row carries the full digest, never the 12-character prefix a view would show.
        var systemPrompt = KeyValue(dto, BenchmarkComparabilityKey.CandidateSystemPromptKey);
        Assert.Equal(BenchmarkComparabilityKeyKind.Instrument.ToString(), systemPrompt.Kind);
        Assert.Equal(BenchmarkComparabilityValueKind.Hash.ToString(), systemPrompt.ValueKind);
        Assert.Equal(run.CandidateSystemPromptSha256, systemPrompt.Value);
        Assert.Equal(extracted[BenchmarkComparabilityKey.CandidateSystemPromptKey], systemPrompt.Value);
    }

    [Fact]
    public async Task LargestConditionKeys_SetsDisplayValueOnTheSuiteRow_WhenTheSuiteHasAName()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });
        using (var db = new ApplicationDbContext(options))
        {
            db.BenchmarkSuites.Add(new BenchmarkSuite { Id = 5, Name = "NetHack Wiki Suite" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Equal("NetHack Wiki Suite (#5)", KeyValue(dto, BenchmarkComparabilityKey.SuiteKey).DisplayValue);

        // Suite is the one must-match value whose meaning lives in another table; every other row
        // has no friendlier rendering than its raw value.
        Assert.All(
            dto.LargestConditionKeys.Where(k => k.Name != BenchmarkComparabilityKey.SuiteKey),
            k => Assert.Null(k.DisplayValue));
    }

    [Fact]
    public async Task LargestConditionKeys_LeavesTheSuiteRowsDisplayValueNull_WhenTheSuiteHasNoName()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Null(KeyValue(dto, BenchmarkComparabilityKey.SuiteKey).DisplayValue);
    }

    [Fact]
    public async Task ReferenceSelectionRule_IsPopulated_WhenAConditionExists()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });

        var dto = await IndexAsync(options, runIds: new long[] { 1 });

        Assert.Equal(BenchmarkComparabilityIndexService.ReferenceSelectionRule, dto.ReferenceSelectionRule);
    }

    [Fact]
    public async Task ReferenceSelectionRule_IsPopulated_EvenOverAnOfferedSetThatProducesNoConditionAtAll()
    {
        // A group with no loadable members defines no condition, so the index reports zero of them —
        // but the sentence explaining how a reference would be chosen still cannot be blank.
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) }, new[] { Group(7, "Empty") });

        var dto = await IndexAsync(options, groupIds: new long[] { 7 });

        Assert.Empty(dto.Conditions);
        Assert.Equal(BenchmarkComparabilityIndexService.ReferenceSelectionRule, dto.ReferenceSelectionRule);
    }

    [Fact]
    public async Task Condition_CarriesANonEmptySignature_AndTheNewestRunDate()
    {
        var options = NewDatabase();
        var first = Run(1);
        var second = Run(2, "gemini-3.8-flash-lite");
        await SeedAsync(options, new[] { first, second });

        var dto = await IndexAsync(options, runIds: new long[] { 1, 2 });

        var condition = Assert.Single(dto.Conditions);
        Assert.NotEmpty(condition.Signature);
        Assert.Equal(BenchmarkCrossModelComparability.MustMatchSignature(first), condition.Signature);

        // Run 2 was started later than run 1 by construction of the fixture.
        Assert.Equal((DateTime?)second.StartedAtUtc, condition.NewestRunStartedAtUtc);
    }

    // --- Request limits -----------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyRequest_IsRefused()
    {
        using var db = new ApplicationDbContext(NewDatabase());
        var service = new BenchmarkComparabilityIndexService(db);

        var (result, error) = await service.BuildAsync(new BenchmarkComparabilityIndexRequest(), TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Contains("at least one run or group", error!);
    }

    [Fact]
    public async Task TooManyRuns_IsRefusedNamingTheCap()
    {
        using var db = new ApplicationDbContext(NewDatabase());
        var service = new BenchmarkComparabilityIndexService(db);

        var (result, error) = await service.BuildAsync(new BenchmarkComparabilityIndexRequest
        {
            RunIds = Enumerable.Range(1, BenchmarkComparabilityIndexService.MaxRunIds + 1)
                .Select(i => (long)i)
                .ToList()
        }, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Contains(BenchmarkComparabilityIndexService.MaxRunIds.ToString(), error!);
    }

    [Fact]
    public async Task AMissingRun_IsRefusedByName()
    {
        var options = NewDatabase();
        await SeedAsync(options, new[] { Run(1) });

        using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparabilityIndexService(db);

        var (result, error) = await service.BuildAsync(new BenchmarkComparabilityIndexRequest
        {
            RunIds = new List<long> { 1, 99 }
        }, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Contains("99", error!);
    }
}
