namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;
using static Overseer.Tests.UnitTests.ChatConsistencyAnalysisTestData;

/// <summary>
/// The chat consistency analysis over an in-memory database: the minimum-sample and legacy caps, the
/// next runs that widen a baseline, and the run selection: how the runs not analyzed are classified,
/// how a malformed selection is refused, and how a saved selection round-trips.
/// No network.
/// </summary>
public class ChatConsistencyAnalysisRefusalTests
{
    // --- Caps ------------------------------------------------------------------------------------

    [Fact]
    public async Task AMinimumSampleShortfallCapsTheGradeAtIndicated()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, BaselineOnOneDay = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.Equal(ConsistencyVerdict.ChangedDegraded, p1.Verdict);
        Assert.False(p1.MinimumSampleMet);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p1.Grade);
        Assert.Contains(p1.GradeReasons, r => r.StartsWith("Below the minimum sample", StringComparison.Ordinal));

        // Work is equivalent and otherwise robust, so the shortfall alone holds it at Indicated.
        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.Equal(ConsistencyVerdict.Equivalent, p4.Verdict);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p4.Grade);

        // No earlier run of the model exists, so widening the baseline is not suggested.
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.RepeatRunId == null
            && n.Suggestion == "No other run of gpt-test exists for the baseline period. A later analysis can take this comparison period as its baseline.");
        Assert.DoesNotContain(result.NextRuns, n => n.Suggestion.StartsWith("Widen the baseline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEarlierUnusedRunOfTheModel_LetsTheBaselineBeWidened()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, BaselineOnOneDay = true, SelectionRuns = true });

        // Run #6 (2026-08-31) lies in the baseline window, before the step-1 dates, and before the first baseline run.
        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, Selection()), TestContext.Current.CancellationToken);

        Assert.Contains(result.RunSelection.UnanalyzedRuns, u => u.RunId == 6 && u.Reason == ChatConsistencyUnanalyzedReasons.OutsideDateRange);
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.RepeatRunId == 2
            && n.Suggestion.StartsWith("Widen the baseline period", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWellSampledRobustEquivalenceIsEstablished()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.Equal(ConsistencyVerdict.Equivalent, p4.Verdict);
        Assert.True(p4.MinimumSampleMet);
        Assert.Equal(ChatConsistencyEvidenceGrade.Established, p4.Grade);
    }

    [Fact]
    public async Task LegacyRunsUseTheLegacyProxyAndCapAtIndicated()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { Legacy = true, ComparisonDurationFactor = 2.0 });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p2 = EndpointOf(result, ChatConsistencyEndpointIds.TimeToFirstAnswerText);
        Assert.True(p2.Computed);
        Assert.True(p2.LegacyProxy);
        Assert.Equal(ConsistencyVerdict.ChangedDegraded, p2.Verdict);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p2.Grade);
        Assert.Contains(p2.GradeReasons, r => r.StartsWith("Legacy latency proxy", StringComparison.Ordinal));
        Assert.Contains("[legacy proxy]", result.Headline);

        var p3 = EndpointOf(result, ChatConsistencyEndpointIds.StreamingRate);
        Assert.False(p3.Computed);

        // Work is computed from legacy runs too, and capped.
        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.True(p4.Computed);
        Assert.True(p4.UsesLegacyData);
        Assert.NotEqual(ChatConsistencyEvidenceGrade.Established, p4.Grade);
        Assert.Contains(result.DataQuality, n => n.Kind == "estimatedStarts");
    }

    // --- Run selection ---------------------------------------------------------------------------

    [Fact]
    public async Task TheRunSelectionClassifiesEveryUsableRunNotAnalyzedInBothPeriods()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, Selection(10, 5, 10)), TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 1, 2 }, result.Baseline.RunIds);
        Assert.Equal(new long[] { 3, 4 }, result.Comparison.RunIds);

        var selection = result.RunSelection;
        Assert.True(selection.Recorded);
        Assert.Equal("2026-09-01 to 2026-09-30", selection.RangeLabel);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), selection.RangeFromUtc);
        Assert.Equal(new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc), selection.RangeToUtc);
        Assert.Equal(1L, selection.FirstRunId);
        Assert.Equal(4L, selection.LastRunId);
        Assert.Equal(new long[] { 5, 10 }, selection.LeftOutRunIds);

        // #6 is both outside the dates and before the first run: the first matching reason wins.
        Assert.Equal(
            new[]
            {
                (6L, "baseline", ChatConsistencyUnanalyzedReasons.OutsideDateRange),
                (7L, "baseline", ChatConsistencyUnanalyzedReasons.BeforeFirstRun),
                (5L, "baseline", ChatConsistencyUnanalyzedReasons.LeftOut),
                (9L, "comparison", ChatConsistencyUnanalyzedReasons.NotSelected),
                (10L, "comparison", ChatConsistencyUnanalyzedReasons.LeftOut),
                (8L, "comparison", ChatConsistencyUnanalyzedReasons.AfterLastRun)
            },
            selection.UnanalyzedRuns.Select(u => (u.RunId, u.Period, u.Reason)).ToList());
        Assert.Equal(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), selection.UnanalyzedRuns[0].StartedAtUtc);

        var note = Assert.Single(result.DataQuality, n => n.Kind == "runSelection");
        Assert.Equal(
            "6 usable runs of the model inside the periods were not analyzed — left out in step 1: #5 (baseline), #10 (comparison); "
            + "outside the step-1 dates: #6 (baseline); before the first run: #7 (baseline); after the last run: #8 (comparison); "
            + "not assigned to a period: #9 (comparison).",
            note.Text);

        string limitation = Assert.Single(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
        Assert.Equal(
            "The operator chose the runs: 6 usable runs of the model inside the periods were not analyzed (see the run selection). "
            + "The verdicts hold for the analyzed runs; leaving runs out after looking at the timeline can bias them.",
            limitation);
        Assert.DoesNotContain(
            BenchmarkReportPackValidator.ChatIntentWords.Concat(BenchmarkReportPackValidator.ChatMechanismWords),
            word => limitation.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExplicitRunsWithoutASelectionListTheOthersAsNotSelected()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, null), TestContext.Current.CancellationToken);

        var selection = result.RunSelection;
        Assert.False(selection.Recorded);
        Assert.Null(selection.RangeLabel);
        Assert.Null(selection.FirstRunId);
        Assert.Empty(selection.LeftOutRunIds);
        Assert.Equal(new long[] { 6, 7, 5, 9, 10, 8 }, selection.UnanalyzedRuns.Select(u => u.RunId).ToList());
        Assert.All(selection.UnanalyzedRuns, u => Assert.Equal(ChatConsistencyUnanalyzedReasons.NotSelected, u.Reason));

        var note = Assert.Single(result.DataQuality, n => n.Kind == "runSelection");
        Assert.Equal(
            "6 usable runs of the model inside the periods were not analyzed — not assigned to a period: #6 (baseline), #7 (baseline), "
            + "#5 (baseline), #9 (comparison), #10 (comparison), #8 (comparison).",
            note.Text);
        Assert.Single(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutExplicitRunsEveryUsableRunIsAnalyzedAndNoneIsListed()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });
        var request = Request(key) with { RunSelection = new ChatConsistencyRunSelection { RangeLabel = "All dates" } };

        var result = await Service(db).AnalyzeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 6, 7, 1, 2, 5 }, result.Baseline.RunIds);
        Assert.Equal(new long[] { 9, 3, 10, 4, 8 }, result.Comparison.RunIds);
        Assert.True(result.RunSelection.Recorded);
        Assert.Equal("All dates", result.RunSelection.RangeLabel);
        Assert.Empty(result.RunSelection.UnanalyzedRuns);
        Assert.DoesNotContain(result.DataQuality, n => n.Kind == "runSelection");
        Assert.DoesNotContain(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMarkWhoseRunIsNotFoundIsIgnoredWithANote()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });
        var selection = new ChatConsistencyRunSelection { FirstRunId = 999, LastRunId = 4 };

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, selection), TestContext.Current.CancellationToken);

        Assert.Contains(result.DataQuality, n => n.Kind == "runSelection" && n.Text == "The first run of the selection, #999, was not found.");
        Assert.Equal(
            new[]
            {
                (6L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (7L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (5L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (9L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (10L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (8L, ChatConsistencyUnanalyzedReasons.AfterLastRun)
            },
            result.RunSelection.UnanalyzedRuns.Select(u => (u.RunId, u.Reason)).ToList());
    }

    [Fact]
    public void AMalformedOrContradictoryRunSelectionIsRefused()
    {
        var request = Request("subject") with { BaselineRunIds = new long[] { 1, 2 }, ComparisonRunIds = new long[] { 3, 45 } };

        void Refused(ChatConsistencyRunSelection selection, string message)
        {
            var refusal = Assert.Throws<ChatConsistencyRequestException>(() => ChatConsistencyAnalysisService.Validate(request with { RunSelection = selection }));
            Assert.Equal(message, refusal.Message);
        }

        Refused(new ChatConsistencyRunSelection { RangeLabel = new string('x', 65) }, "The run selection's date label is at most 64 characters.");
        Refused(
            new ChatConsistencyRunSelection
            {
                RangeFromUtc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
                RangeToUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            "The run selection's dates end before they start.");
        Refused(
            new ChatConsistencyRunSelection { LeftOutRunIds = Enumerable.Range(1000, 5001).Select(i => (long)i).ToList() },
            "The run selection leaves out at most 5,000 runs.");
        Refused(new ChatConsistencyRunSelection { LeftOutRunIds = new long[] { 2 } }, "Run #2 is left out in step 1 but selected for the baseline.");
        Refused(new ChatConsistencyRunSelection { LeftOutRunIds = new long[] { 45 } }, "Run #45 is left out in step 1 but selected for the comparison.");

        // At the limits, the selection is accepted.
        ChatConsistencyAnalysisService.Validate(request with
        {
            RunSelection = new ChatConsistencyRunSelection
            {
                RangeLabel = new string('x', 64),
                LeftOutRunIds = Enumerable.Range(1000, 5000).Select(i => (long)i).ToList()
            }
        });
    }

    [Theory]
    [InlineData("group", "group:1", "The comparison set's kind must be \"battery\" or \"suite\".")]
    [InlineData("battery", "suite:id:7", "A battery comparison set's key is battery: followed by the battery definition hash.")]
    [InlineData("suite", "suite:", "A suite comparison set's key is suite: followed by the suite identity.")]
    [InlineData("battery", null, "A battery comparison set's key is battery: followed by the battery definition hash.")]
    public void AMalformedComparisonSetIsRefused_WithTheSameTextsAsTheEvidenceBuilder(string kind, string? key, string message)
    {
        var request = Request("subject") with { ComparisonSet = new ChatConsistencyComparisonSetRef { Kind = kind, Key = key! } };

        var refusal = Assert.Throws<ChatConsistencyRequestException>(() => ChatConsistencyAnalysisService.Validate(request));
        Assert.Equal(message, refusal.Message);
    }

    [Fact]
    public async Task ARefusedRunSelectionSavesNothing()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());

        var refusal = await Assert.ThrowsAsync<ChatConsistencyRequestException>(
            () => Service(db).AnalyzeAsync(ExplicitRequest(key, Selection(3)), TestContext.Current.CancellationToken));

        Assert.Equal("Run #3 is left out in step 1 but selected for the comparison.", refusal.Message);
        Assert.Empty(db.ChatConsistencyAnalyses.ToList());
    }

    [Fact]
    public async Task AnalysesDifferingOnlyInTheLeftOutRunsHaveDifferentFingerprints()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);

        var first = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5)), TestContext.Current.CancellationToken);
        var other = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5, 10)), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.InputSha256, other.InputSha256);
        Assert.Equal(first.Headline, other.Headline);
    }

    [Fact]
    public async Task ASavedAnalysisRoundTripsItsRunSelection()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);

        var result = await service.AnalyzeAsync(ExplicitRequest(key, Selection(10, 5)), TestContext.Current.CancellationToken);
        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        Assert.Contains("\"runSelection\":{\"recorded\":true,", row.ResultJson);

        var reloaded = await service.GetAnalysisAsync(row.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.RunSelection.Recorded);
        Assert.Equal(new long[] { 5, 10 }, reloaded.RunSelection.LeftOutRunIds);
        Assert.Equal(DateTimeKind.Utc, reloaded.RunSelection.RangeFromUtc!.Value.Kind);
        Assert.Equal(6, reloaded.RunSelection.UnanalyzedRuns.Count);
        Assert.Equal(
            JsonSerializer.Serialize(result.RunSelection, ChatConsistencyJson.Options),
            JsonSerializer.Serialize(reloaded.RunSelection, ChatConsistencyJson.Options));
    }

    [Fact]
    public async Task AStoredResultWithoutARunSelectionReadsAsNotRecorded()
    {
        using var db = NewDb();
        var result = ChatConsistencyReportTestData.Result() with
        {
            RunSelection = new ChatConsistencyRunSelectionView
            {
                Recorded = true,
                RangeLabel = "2026-09-01 to 2026-09-30",
                LeftOutRunIds = new long[] { 5 }
            }
        };
        int id = StoreResult(db, result, "runSelection");

        var reloaded = await Service(db).GetAnalysisAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal(result.Headline, reloaded!.Headline);
        Assert.False(reloaded.RunSelection.Recorded);
        Assert.Null(reloaded.RunSelection.RangeLabel);
        Assert.Empty(reloaded.RunSelection.LeftOutRunIds);
        Assert.Empty(reloaded.RunSelection.UnanalyzedRuns);
    }
}
