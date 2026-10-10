namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The two kinds of instrument difference between runs made over time, and the opposite treatment
/// each gets: a measurement change (graders, scoring, timing, prices) is bridged or segmented, while
/// an Overseer change (prompt, guides, corpora, budgets) becomes a dated event and never excludes
/// data. Also covers item pairing across revisions and control-run matching.
/// </summary>
public class ChatConsistencyComparabilityTests
{
    private const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string OtherGuidesSha = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
    private const string OtherPromptSha = "7c2e9a4f1b8d3e6a0c5f9b2d7e4a1c8f3b6d0e9a5c2f7b4d1e8a3c6f0b9d5e2a";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";

    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One run of the shared condition: suite 7, the current harness, one assessor, sequential
    /// questions with call telemetry, and four questions at revision 1 unless items are given.
    /// </summary>
    private static BenchmarkRun Run(
        long id,
        string provider = "OpenAI",
        string modelId = "gpt-test",
        params (long Question, int? Revision)[] items)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 7,
            BenchmarkSuiteIdUsed = 7,
            SuiteName = "Core Suite",
            StartedAtUtc = T0.AddHours(id),
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: provider, modelId: modelId),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-test"),
            AssessorModelSnapshotId = 100,
            HarnessVersion = HarnessImpactLedger.CurrentVersion,
            ScoringMethodVersion = 14,
            MaxParallelQuestionsUsed = 1,
            CallTelemetryVersion = 1,
            CandidatePromptOptionsJson = "{\"verboseMode\":false}",
            CandidateSystemPromptSha256 = PromptSha,
            ToolGuidesSha256 = GuidesSha,
            KnowledgeBaseHeadSha = KnowledgeSha
        };

        var spec = items.Length == 0
            ? new (long Question, int? Revision)[] { (1, 1), (2, 1), (3, 1), (4, 1) }
            : items;

        int order = 0;
        foreach (var (question, revision) in spec)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = id * 100 + order,
                BenchmarkRunId = id,
                BenchmarkRun = run,
                OrderIndex = order,
                BenchmarkQuestionId = question,
                BenchmarkQuestionIdUsed = question,
                ItemRevisionUsed = revision,
                QuestionText = "Question " + question,
                AnswerText = "Answer"
            });
            order++;
        }

        return run;
    }

    // --- Subject ------------------------------------------------------------------------------

    [Fact]
    public void ModelAxisKeyIsEqualForOneConfigurationAndDiffersByModel()
    {
        Assert.Equal(
            ChatConsistencyComparability.ModelAxisKey(Run(1)),
            ChatConsistencyComparability.ModelAxisKey(Run(2)));
        Assert.NotEqual(
            ChatConsistencyComparability.ModelAxisKey(Run(1)),
            ChatConsistencyComparability.ModelAxisKey(Run(2, modelId: "gpt-other")));
        Assert.Contains("CandidateModelId=gpt-test", ChatConsistencyComparability.ModelAxisKey(Run(1)));
    }

    // --- Measurement change versus Overseer change --------------------------------------------

    [Fact]
    public void AToolGuidesChangeIsAnEventAndNoSegmentBoundary()
    {
        var first = Run(1);
        var second = Run(2);
        second.ToolGuidesSha256 = OtherGuidesSha;

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });
        var events = ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second });

        Assert.Empty(assessment.Boundaries);
        foreach (var axis in Enum.GetValues<ChatConsistencyAxis>())
        {
            Assert.True(assessment.AreComparable(1, 2, axis), $"Runs 1 and 2 split on {axis}.");
        }

        var change = Assert.Single(events);
        Assert.Equal(OverseerEventKinds.ToolGuides, change.Kind);
        Assert.Equal(GuidesSha, change.From);
        Assert.Equal(OtherGuidesSha, change.To);
        Assert.Equal(2, change.RunId);
        Assert.Equal(1, change.PreviousRunId);
        Assert.Equal(second.StartedAtUtc, change.AtUtc);
    }

    [Fact]
    public void AGraderSnapshotChangeIsAGradingBoundary()
    {
        var first = Run(1);
        var second = Run(2);
        second.AssessorModelSnapshotId = 101;
        second.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-test");

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        var boundary = Assert.Single(assessment.Boundaries);
        Assert.Equal(MeasurementChangeKind.Grading, boundary.Kind);
        Assert.False(boundary.Bridged);
        Assert.Equal(new[] { ChatConsistencyAxis.Quality }, boundary.Axes);
        Assert.Equal(1, boundary.FromRunId);
        Assert.Equal(2, boundary.ToRunId);
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.Quality));
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedTelemetry));
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.Work));
        Assert.Empty(ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second }));
    }

    [Fact]
    public void AGraderChangeCoveredByACommonRegradeIsBridged()
    {
        var first = Run(1);
        var second = Run(2);
        second.AssessorModelSnapshotId = 101;

        var assessment = ChatConsistencyComparability.AssessMeasurement(
            new[] { first, second }, commonGraderCovers: new long[] { 1, 2 });

        var boundary = Assert.Single(assessment.Boundaries);
        Assert.Equal(MeasurementChangeKind.Grading, boundary.Kind);
        Assert.True(boundary.Bridged);
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.Quality));
    }

    [Fact]
    public void ACommonRegradeOfOnlyOneSideDoesNotBridge()
    {
        var first = Run(1);
        var second = Run(2);
        second.AssessorModelSnapshotId = 101;

        var assessment = ChatConsistencyComparability.AssessMeasurement(
            new[] { first, second }, commonGraderCovers: new long[] { 2 });

        Assert.False(Assert.Single(assessment.Boundaries).Bridged);
    }

    [Fact]
    public void AScoringMethodChangeIsAScoringBoundaryThatARescoreBridges()
    {
        var first = Run(1);
        var second = Run(2);
        second.ScoringMethodVersion = 15;

        var segmented = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });
        var bridged = ChatConsistencyComparability.AssessMeasurement(
            new[] { first, second }, rescoredUnderOneProfile: new long[] { 1, 2 });

        var boundary = Assert.Single(segmented.Boundaries);
        Assert.Equal(MeasurementChangeKind.Scoring, boundary.Kind);
        Assert.False(segmented.AreComparable(1, 2, ChatConsistencyAxis.Quality));
        Assert.True(bridged.AreComparable(1, 2, ChatConsistencyAxis.Quality));
    }

    [Fact]
    public void AGradingOnlyHarnessBumpSegmentsQualityAndEmitsNoEvent()
    {
        var first = Run(1);
        var second = Run(2);
        first.HarnessVersion = "52";
        second.HarnessVersion = "53";

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        var boundary = Assert.Single(assessment.Boundaries);
        Assert.Equal(MeasurementChangeKind.Grading, boundary.Kind);
        Assert.Contains("52", boundary.Reason);
        Assert.Empty(ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second }));
    }

    [Fact]
    public void ACandidateInputHarnessBumpIsAnEvent()
    {
        var first = Run(1);
        var second = Run(2);
        first.HarnessVersion = "51";
        second.HarnessVersion = "52";

        var events = ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second });

        var change = Assert.Single(events);
        Assert.Equal(OverseerEventKinds.HarnessVersion, change.Kind);
        Assert.Equal("51", change.From);
        Assert.Equal("52", change.To);
    }

    [Fact]
    public void ACandidateTimingHarnessBumpSegmentsLegacySpeedOnly()
    {
        var first = Run(1);
        var second = Run(2);
        first.HarnessVersion = "20";
        second.HarnessVersion = "21";

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        Assert.Contains(assessment.Boundaries, b => b.Kind == MeasurementChangeKind.CandidateTiming);
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedLegacy));
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedTelemetry));
    }

    [Fact]
    public void ACallTelemetryVersionChangeSegmentsTelemetrySpeed()
    {
        var first = Run(1);
        var second = Run(2);
        second.CallTelemetryVersion = 2;

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        var boundary = Assert.Single(assessment.Boundaries);
        Assert.Equal(MeasurementChangeKind.CallTelemetry, boundary.Kind);
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedTelemetry));
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedLegacy));
    }

    [Fact]
    public void AnAccountingHarnessBumpSegmentsWorkAndCost()
    {
        var first = Run(1);
        var second = Run(2);
        first.HarnessVersion = "33";
        second.HarnessVersion = "34";

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        Assert.Contains(assessment.Boundaries, b => b.Kind == MeasurementChangeKind.CandidateAccounting);
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.Work));
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.Cost));
    }

    [Fact]
    public void AnUnrecordedHarnessVersionIsTreatedAsEveryMeasurementChange()
    {
        var first = Run(1);
        var second = Run(2);
        first.HarnessVersion = null;

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        foreach (var axis in Enum.GetValues<ChatConsistencyAxis>().Where(a => a != ChatConsistencyAxis.SpeedTelemetry))
        {
            Assert.False(assessment.AreComparable(1, 2, axis), $"Runs 1 and 2 still compare on {axis}.");
        }

        Assert.Empty(ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second }));
    }

    [Fact]
    public void AParallelRunIsExcludedFromSpeedAndKeptForQuality()
    {
        var first = Run(1);
        var second = Run(2);
        second.MaxParallelQuestionsUsed = 4;

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });
        var segments = assessment.RunOf(2)!;

        var exclusion = Assert.Single(assessment.SpeedExclusions);
        Assert.Equal(2, exclusion.RunId);
        Assert.Equal(SpeedExclusionReason.ParallelQuestions, exclusion.Reason);
        Assert.Null(segments.SpeedTelemetry);
        Assert.Null(segments.SpeedLegacy);
        Assert.Equal(0, segments.Quality);
        Assert.Equal(0, segments.Work);
        Assert.Equal(0, segments.Cost);
        Assert.Empty(assessment.Boundaries);
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.Quality));
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedLegacy));
    }

    [Fact]
    public void ARunWithoutTelemetryIsExcludedFromTelemetrySpeedOnly()
    {
        var first = Run(1);
        var second = Run(2);
        second.CallTelemetryVersion = null;

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        Assert.Equal(SpeedExclusionReason.NoCallTelemetry, Assert.Single(assessment.SpeedExclusions).Reason);
        Assert.Null(assessment.RunOf(2)!.SpeedTelemetry);
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.SpeedLegacy));
    }

    [Fact]
    public void APricingChangeIsReportedAndBridged()
    {
        var first = Run(1);
        var second = Run(2);
        first.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":1.0}}";
        second.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":2.0}}";

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { first, second });

        var boundary = Assert.Single(assessment.Boundaries);
        Assert.Equal(MeasurementChangeKind.Pricing, boundary.Kind);
        Assert.True(boundary.Bridged);
        Assert.Equal(new[] { ChatConsistencyAxis.Cost }, boundary.Axes);
        Assert.True(assessment.AreComparable(1, 2, ChatConsistencyAxis.Cost));
    }

    [Fact]
    public void SubjectsAreSeparateSeries()
    {
        var a1 = Run(1);
        var b2 = Run(2, provider: "Anthropic", modelId: "claude-test");
        b2.AssessorModelSnapshotId = 101;
        var a3 = Run(3);

        var assessment = ChatConsistencyComparability.AssessMeasurement(new[] { a1, b2, a3 });

        Assert.Empty(assessment.Boundaries);
        Assert.True(assessment.AreComparable(1, 3, ChatConsistencyAxis.Quality));
        Assert.False(assessment.AreComparable(1, 2, ChatConsistencyAxis.Quality));
    }

    // --- Overseer events ---------------------------------------------------------------------

    [Fact]
    public void EventsAreOrderedByTimeAndANullIsNotRecorded()
    {
        var r1 = Run(1);
        var r2 = Run(2);
        var r3 = Run(3);
        var r4 = Run(4);
        r1.KnowledgeBaseHeadSha = "aaaa";
        r2.KnowledgeBaseHeadSha = null;
        r3.KnowledgeBaseHeadSha = "bbbb";
        r4.KnowledgeBaseHeadSha = "bbbb";
        r4.CandidateSystemPromptSha256 = "cccc";

        var events = ChatConsistencyComparability.DetectOverseerEvents(new[] { r4, r2, r3, r1 });

        Assert.Equal(2, events.Count);
        Assert.Equal(OverseerEventKinds.KnowledgeBase, events[0].Kind);
        Assert.Equal("aaaa", events[0].From);
        Assert.Equal("bbbb", events[0].To);
        Assert.Equal(3, events[0].RunId);
        Assert.Equal(1, events[0].PreviousRunId);
        Assert.Equal(OverseerEventKinds.CandidateSystemPrompt, events[1].Kind);
        Assert.Equal(4, events[1].RunId);
    }

    [Fact]
    public void BudgetAndPromptOptionChangesAreEvents()
    {
        var first = Run(1);
        var second = Run(2);
        first.MaxToolCallsPerQuestionUsed = 45;
        second.MaxToolCallsPerQuestionUsed = 30;
        second.CandidatePromptOptionsJson = "{\"verboseMode\":true}";

        var kinds = ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second })
            .Select(e => e.Kind)
            .ToList();

        Assert.Equal(new[] { OverseerEventKinds.CandidatePromptOptions, OverseerEventKinds.MaxToolCallsPerQuestion }, kinds);
    }

    [Fact]
    public void PromptOptionsAreComparedInCanonicalForm()
    {
        var first = Run(1);
        var second = Run(2);
        second.CandidatePromptOptionsJson = "{ \"verboseMode\" : false }";

        Assert.Empty(ChatConsistencyComparability.DetectOverseerEvents(new[] { first, second }));
    }

    /// <summary>
    /// Runs 1–4 of one model alternating between two suites, A, B, A, B, as the members of a battery
    /// do: suite B runs with another system prompt and with the prompt option on.
    /// </summary>
    private static BenchmarkRun[] AlternatingSuites()
    {
        var runs = new[] { Run(1), Run(2), Run(3), Run(4) };
        foreach (var run in new[] { runs[1], runs[3] })
        {
            run.BenchmarkSuiteId = 8;
            run.BenchmarkSuiteIdUsed = 8;
            run.SuiteName = "Snapshot Suite";
            run.CandidateSystemPromptSha256 = OtherPromptSha;
            run.CandidatePromptOptionsJson = "{\"verboseMode\":true}";
        }

        return runs;
    }

    [Fact]
    public void SuitesAlternatingInABatteryAreNoEventAndAHarnessBumpIsOne()
    {
        var runs = AlternatingSuites();
        runs[0].HarnessVersion = "51";
        runs[1].HarnessVersion = "51";
        runs[2].HarnessVersion = "52";
        runs[3].HarnessVersion = "52";

        var events = ChatConsistencyComparability.DetectOverseerEvents(runs);

        var change = Assert.Single(events);
        Assert.Equal(OverseerEventKinds.HarnessVersion, change.Kind);
        Assert.Equal("51", change.From);
        Assert.Equal("52", change.To);
        Assert.Equal(3, change.RunId);
        Assert.Equal(1, change.PreviousRunId);
        Assert.Equal(ChatConsistencyComparability.ModelAxisKey(runs[0]), change.SubjectKey);
    }

    [Fact]
    public void SuitesAlternatingInABatteryWithoutAChangeAreNoEvent()
    {
        Assert.Empty(ChatConsistencyComparability.DetectOverseerEvents(AlternatingSuites()));
    }

    [Fact]
    public void AChangeSeenInBothSuitesIsOneEventDatedByTheEarliest()
    {
        var runs = AlternatingSuites();
        runs[2].ToolGuidesSha256 = OtherGuidesSha;
        runs[3].ToolGuidesSha256 = OtherGuidesSha;

        var change = Assert.Single(ChatConsistencyComparability.DetectOverseerEvents(runs.OrderByDescending(r => r.Id)));

        Assert.Equal(OverseerEventKinds.ToolGuides, change.Kind);
        Assert.Equal(GuidesSha, change.From);
        Assert.Equal(OtherGuidesSha, change.To);
        Assert.Equal(3, change.RunId);
        Assert.Equal(1, change.PreviousRunId);
        Assert.Equal(runs[2].StartedAtUtc, change.AtUtc);
    }

    [Fact]
    public void TheSameChangeRecurringAfterARevertIsTwoEvents()
    {
        var runs = new[] { Run(1), Run(2), Run(3), Run(4) };
        runs[1].ToolGuidesSha256 = OtherGuidesSha;
        runs[3].ToolGuidesSha256 = OtherGuidesSha;

        var events = ChatConsistencyComparability.DetectOverseerEvents(runs);

        Assert.Equal(3, events.Count);
        Assert.Equal(new long[] { 2, 3, 4 }, events.Select(e => e.RunId));
        Assert.Equal(2, events.Count(e => e.From == GuidesSha && e.To == OtherGuidesSha));
    }

    [Fact]
    public void ChangesOfTwoModelsAreNotMerged()
    {
        var a1 = Run(1);
        var b2 = Run(2, provider: "Anthropic", modelId: "claude-test");
        var a3 = Run(3);
        var b4 = Run(4, provider: "Anthropic", modelId: "claude-test");
        a3.ToolGuidesSha256 = OtherGuidesSha;
        b4.ToolGuidesSha256 = OtherGuidesSha;

        var events = ChatConsistencyComparability.DetectOverseerEvents(new[] { a1, b2, a3, b4 });

        Assert.Equal(new long[] { 3, 4 }, events.Select(e => e.RunId));
        Assert.NotEqual(events[0].SubjectKey, events[1].SubjectKey);
    }

    // --- Items -------------------------------------------------------------------------------

    [Fact]
    public void ItemKeyRendersQuestionAndRevision()
    {
        var run = Run(1, items: new (long, int?)[] { (12, 3), (13, null) });
        var unlinked = new BenchmarkRunAnswer { OrderIndex = 2, ItemRevisionUsed = null };

        Assert.Equal("12@3", ChatConsistencyComparability.ItemKey(run.Answers[0]).ToString());
        Assert.Equal("13@?", ChatConsistencyComparability.ItemKey(run.Answers[1]).ToString());
        Assert.Equal("order:2@?", ChatConsistencyComparability.ItemKey(unlinked).ToString());
    }

    [Fact]
    public void PairingExcludesARevisedItemAndReportsTheExcludedShare()
    {
        var baseline = Run(1, items: new (long, int?)[] { (1, 1), (2, 1), (3, 1), (4, null) });
        var comparison = Run(2, items: new (long, int?)[] { (1, 1), (2, 2), (4, null), (5, 1) });

        var pairing = ChatConsistencyComparability.PairItems(new[] { baseline }, new[] { comparison });

        Assert.Equal(new[] { "1@1", "4@?" }, pairing.Paired.Select(p => p.Key.ToString()));
        Assert.Equal(new[] { "2@1", "3@1" }, pairing.BaselineOnly.Select(k => k.ToString()));
        Assert.Equal(new[] { "2@2", "5@1" }, pairing.ComparisonOnly.Select(k => k.ToString()));
        Assert.Equal(new[] { "2" }, pairing.RevisedQuestions);
        Assert.Equal(new[] { "4@?" }, pairing.NullRevisionItems.Select(k => k.ToString()));
        Assert.True(pairing.Paired.Single(p => p.Key.Question == "4").NullRevision);
        Assert.Equal(6, pairing.TotalItemCount);
        Assert.Equal(4.0 / 6.0, pairing.ExcludedShare, 10);
    }

    [Fact]
    public void ANullRevisionPairsOnlyWithANullRevision()
    {
        var baseline = Run(1, items: new (long, int?)[] { (6, null) });
        var comparison = Run(2, items: new (long, int?)[] { (6, 1) });

        var pairing = ChatConsistencyComparability.PairItems(new[] { baseline }, new[] { comparison });

        Assert.Empty(pairing.Paired);
        Assert.Equal(new[] { "6" }, pairing.RevisedQuestions);
        Assert.Equal(new[] { "6@?" }, pairing.NullRevisionItems.Select(k => k.ToString()));
        Assert.Equal(1.0, pairing.ExcludedShare, 10);
    }

    [Fact]
    public void PairingCountsAnswersPerPeriod()
    {
        var pairing = ChatConsistencyComparability.PairItems(
            new[] { Run(1), Run(2) },
            new[] { Run(3) });

        Assert.Equal(4, pairing.Paired.Count);
        Assert.All(pairing.Paired, p =>
        {
            Assert.Equal(2, p.BaselineAnswerCount);
            Assert.Equal(1, p.ComparisonAnswerCount);
        });
        Assert.Equal(0.0, pairing.ExcludedShare, 10);
    }

    // --- Instrument fingerprint --------------------------------------------------------------

    [Fact]
    public void TheFingerprintCoversOverseerFieldsOnly()
    {
        var baseline = Run(1);
        var otherModel = Run(2, provider: "Anthropic", modelId: "claude-test");
        otherModel.AssessorModelSnapshotId = 101;
        otherModel.ScoringMethodVersion = 13;
        otherModel.PricingSnapshotJson = "{\"candidate\":{}}";
        var otherGuides = Run(3);
        otherGuides.ToolGuidesSha256 = OtherGuidesSha;
        var otherHarness = Run(4);
        otherHarness.HarnessVersion = "52";

        string fingerprint = ChatConsistencyComparability.OverseerInstrumentFingerprint(baseline);

        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, ChatConsistencyComparability.OverseerInstrumentFingerprint(otherModel));
        Assert.NotEqual(fingerprint, ChatConsistencyComparability.OverseerInstrumentFingerprint(otherGuides));
        Assert.NotEqual(fingerprint, ChatConsistencyComparability.OverseerInstrumentFingerprint(otherHarness));
    }

    // --- Control runs ------------------------------------------------------------------------

    [Fact]
    public void AnotherSubjectUnderTheSameBuildAndSuiteQualifiesAsAControl()
    {
        var target = Run(10);
        var control = Run(11, provider: "Anthropic", modelId: "claude-test", items: new (long, int?)[] { (1, 1), (2, 1), (9, 1) });

        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("after", new[] { target }) },
            new[] { target, control });

        var match = Assert.Single(matching.Matches);
        Assert.Equal("after", match.Period);
        Assert.Equal(10, match.TargetRunId);
        Assert.Equal(11, match.ControlRunId);
        Assert.Equal(2, match.PairedItemCount);
        Assert.Equal(ChatConsistencyComparability.ModelAxisKey(control), match.ControlSubjectKey);
        Assert.Empty(matching.MissingControls);
    }

    [Fact]
    public void AControlUnderAnotherBuildSuiteOrTheSameSubjectDoesNotQualify()
    {
        var target = Run(10);
        var otherBuild = Run(11, provider: "Anthropic", modelId: "claude-test");
        otherBuild.ToolGuidesSha256 = OtherGuidesSha;
        var otherSuite = Run(12, provider: "Anthropic", modelId: "claude-test");
        otherSuite.BenchmarkSuiteIdUsed = 8;
        otherSuite.BenchmarkSuiteId = 8;
        var sameSubject = Run(13);

        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("after", new[] { target }) },
            new[] { otherBuild, otherSuite, sameSubject });

        Assert.Empty(matching.Matches);
        Assert.Single(matching.MissingControls);
    }

    [Fact]
    public void ASuiteWithoutAnIdMatchesOnItsName()
    {
        var target = Run(10);
        var control = Run(11, provider: "Anthropic", modelId: "claude-test");
        control.BenchmarkSuiteIdUsed = null;
        control.BenchmarkSuiteId = null;

        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("after", new[] { target }) },
            new[] { control });

        Assert.Single(matching.Matches);
    }

    [Fact]
    public void APeriodWithoutAControlGetsANoteSuggestingAnotherProvidersModel()
    {
        var target = Run(10);

        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("after", new[] { target }) },
            Array.Empty<BenchmarkRun>(),
            new[] { "OpenAI/gpt-other", "Anthropic/claude-test" });

        var note = Assert.Single(matching.MissingControls);
        Assert.Equal("after", note.Period);
        Assert.Equal("Core Suite", note.SuiteName);
        Assert.Equal(10, note.TargetRunId);
        Assert.Equal(ChatConsistencyComparability.OverseerInstrumentFingerprint(target), note.Fingerprint);
        Assert.Contains("Anthropic/claude-test", note.SuggestedText);
        Assert.DoesNotContain("gpt-other", note.SuggestedText);
        Assert.Contains("Core Suite", note.SuggestedText);
        Assert.EndsWith("under the same Overseer build as run #10.", note.SuggestedText);
        Assert.DoesNotContain("instrument", note.SuggestedText);
    }

    [Fact]
    public void AMissingControlNoteWithoutAvailableModelsNamesTheProviderToAvoid()
    {
        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("before", new[] { Run(10) }) },
            Array.Empty<BenchmarkRun>());

        var note = Assert.Single(matching.MissingControls);
        Assert.Contains("a provider other than OpenAI", note.SuggestedText);
    }
}
