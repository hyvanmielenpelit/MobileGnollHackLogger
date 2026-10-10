namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;

/// <summary>
/// The chat consistency fact sheet: deterministic, every key labeled, indexed keys from 1, control
/// models lettered, sample request ids for the Provider Issue Report only, the Provider Issue Report's
/// availability, and the chat consistency slot tables beside the unchanged slots of the other scopes.
/// </summary>
public class BenchmarkChatConsistencyReportFactsTests
{
    private const string ControlKey = "provider=OtherProvider;model=control-model-2";
    private const string ControlName = "Control Model Two";

    private static readonly DateTime Day1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ChatConsistencyAnalysisResult Result(IReadOnlyList<ChatConsistencyAttributionResult>? attributions = null) => new()
    {
        AnalysisId = 7,
        Name = "Weekly check",
        Headline = "Overseer chat with Test Model: quality degraded; speed equivalent within weekdays 04–12 UTC",
        HeadlineReliabilityIncreases = new[] { "429 responses rose" },
        Subject = new ChatConsistencySubject
        {
            Key = "provider=TestProvider;model=test-model-1",
            DisplayName = "Test Model",
            Provider = "TestProvider",
            ModelId = "test-model-1",
            ThinkingLevel = "high"
        },
        Scope = new ChatConsistencyScope
        {
            Text = "weekdays 04–12 UTC",
            StrataIndexes = new[] { 1, 2 },
            StrataUsed = new[] { "weekdays 04–08 UTC", "weekdays 08–12 UTC" },
            ExcludedShare = 0.125,
            OutsideBusinessHoursCovered = true
        },
        Baseline = new ChatConsistencyPeriodSummary
        {
            Name = "baseline",
            StartUtc = Day1,
            EndUtc = Day1.AddDays(7),
            RunIds = new long[] { 11, 10 },
            RunCount = 2,
            Days = new[] { "2026-09-01", "2026-09-02" },
            AnswerCount = 40,
            ItemCount = 20,
            SuiteNames = new[] { "Core suite" }
        },
        Comparison = new ChatConsistencyPeriodSummary
        {
            Name = "comparison",
            StartUtc = Day1.AddDays(14),
            EndUtc = Day1.AddDays(21),
            RunIds = new long[] { 20, 21 },
            RunCount = 2,
            Days = new[] { "2026-09-15", "2026-09-16" },
            AnswerCount = 40,
            ItemCount = 20,
            SuiteNames = new[] { "Core suite" },
            LegacyRunCount = 1
        },
        ProtocolLabel = "V1",
        Endpoints = new[]
        {
            new ChatConsistencyEndpointResult
            {
                Id = "P1", Name = "Quality", Unit = "index points", Scale = ChatConsistencyEffectScale.Difference,
                Margin = 3, MarginText = "±3 index points", Direction = "higherIsBetter", Computed = true,
                Estimate = -4.2, Ci95 = new ChatConsistencyInterval(-6.0, -3.1), Ci90 = new ChatConsistencyInterval(-5.6, -3.4),
                PValue = 0.004, AdjustedPValue = 0.016, Verdict = ConsistencyVerdict.ChangedDegraded, VerdictLabel = "degraded",
                Grade = ChatConsistencyEvidenceGrade.Indicated, GradeReasons = new[] { "a comparison run lacks call telemetry" },
                MinimumDetectableEffect = 2.5, MinimumSampleMet = true, CommonGrader = true,
                BaselineRunCount = 2, ComparisonRunCount = 2, ItemCount = 20,
                RobustnessChecks = new[]
                {
                    new ChatConsistencyCheck { EndpointId = "P1", Name = "leave one run out", Status = ChatConsistencyCheckStatus.Passed, Detail = "every fold degraded" }
                }
            },
            new ChatConsistencyEndpointResult
            {
                Id = "P2", Name = "Time to first answer text", Unit = "log ratio", Scale = ChatConsistencyEffectScale.LogRatio,
                Margin = Math.Log(1.15), Computed = true, Estimate = 0.02, EstimatePercent = 2.02,
                Ci95 = new ChatConsistencyInterval(-0.05, 0.09), Ci90 = new ChatConsistencyInterval(-0.04, 0.08),
                PValue = 0.61, AdjustedPValue = 1.0, Verdict = ConsistencyVerdict.Equivalent, VerdictLabel = "equivalent",
                Grade = ChatConsistencyEvidenceGrade.Established, MinimumDetectableEffect = 0.08, MinimumDetectableEffectPercent = 8.3,
                RunsPerPeriodForMargin = 3, MinimumSampleMet = true, BaselineRunCount = 2, ComparisonRunCount = 2, ItemCount = 20
            },
            new ChatConsistencyEndpointResult
            {
                Id = "P3", Name = "Answer streaming rate", Unit = "log ratio", Scale = ChatConsistencyEffectScale.LogRatio,
                Computed = false, NotComputedReason = "No telemetry run in the baseline.", VerdictLabel = "not computable"
            }
        },
        SecondaryFamilies = new[]
        {
            new ChatConsistencyFamilyResult
            {
                Id = "qualityDetail", Name = "Quality detail", Results = new[]
                {
                    new ChatConsistencySecondaryResult { Id = "accuracyLevel", Name = "Accuracy level", Unit = "level", BaselineValue = 3.1, ComparisonValue = 2.8, Estimate = -0.3, Ci95 = new ChatConsistencyInterval(-0.5, -0.1), PValue = 0.01, AdjustedPValue = 0.03, Rejected = true, ItemCount = 20 },
                    new ChatConsistencySecondaryResult { Id = "criticalErrors", Name = "Critical-error rate", Unit = "share of graded answers", BaselineValue = 0.05, ComparisonValue = 0.1, Estimate = 0.05, PValue = 0.4 },
                    new ChatConsistencySecondaryResult { Id = "flipRate", Name = "Per-item flips against the null flip rate", Unit = "share of answer pairs", ComparisonValue = 0.2, Note = "The null flip rate needs two baseline runs answering the same items." }
                }
            },
            new ChatConsistencyFamilyResult
            {
                Id = "reliability", Name = "Reliability", Results = new[]
                {
                    new ChatConsistencySecondaryResult { Id = "http429", Name = "429 responses", Unit = "share of calls", Estimate = 0.04 }
                }
            },
            new ChatConsistencyFamilyResult
            {
                Id = "toolUse", Name = "Tool use", Results = new[]
                {
                    new ChatConsistencySecondaryResult { Id = "toolCallsPerAnswer", Name = "Tool calls per answer", Unit = "calls", BaselineValue = 2.5, ComparisonValue = 3.0, Estimate = 0.5, PValue = 0.2, AdjustedPValue = 0.4, ItemCount = 20 },
                    new ChatConsistencySecondaryResult { Id = "toolMix.wiki_search", Name = "Share of tool calls to wiki_search", Unit = "share of tool calls", BaselineValue = 0.4, ComparisonValue = 0.5, Estimate = 0.1 }
                }
            },
            new ChatConsistencyFamilyResult
            {
                Id = "shiftFunction", Name = "Shift function", Note = "Pointwise 95 % intervals, not adjusted across deciles; descriptive.", Results = new[]
                {
                    new ChatConsistencySecondaryResult { Id = "P2.d10", Name = "Time to first answer text, decile 0.1", Unit = "log ratio", BaselineValue = -0.2, ComparisonValue = -0.1, Estimate = 0.1, Ci95 = new ChatConsistencyInterval(0.01, 0.2) }
                }
            },
            new ChatConsistencyFamilyResult
            {
                Id = "differenceInDifferences", Name = "Difference in differences per control", Results = new[]
                {
                    new ChatConsistencySecondaryResult { Id = "P1.control", Name = "Quality against " + ControlName, Unit = "index points", Estimate = -4.0 }
                }
            }
        },
        RobustnessChecks = new[]
        {
            new ChatConsistencyCheck { EndpointId = "P1", Name = "leave one run out", Status = ChatConsistencyCheckStatus.Passed, Detail = "every fold degraded" },
            new ChatConsistencyCheck { EndpointId = "P1", Name = "grader stability", Status = ChatConsistencyCheckStatus.Failed, Detail = "drift beyond the margin" }
        },
        Reliability = new[]
        {
            new ChatConsistencyRateResult { Id = "terminalFailures", Name = "Terminal failures", Denominator = "answers", BaselineCount = 0, BaselineTotal = 40, BaselineRate = 0, ComparisonCount = 1, ComparisonTotal = 40, ComparisonRate = 0.025, PValue = 1.0, AdjustedPValue = 1.0, Increased = true },
            new ChatConsistencyRateResult { Id = "http429", Name = "429 responses", Denominator = "calls", BaselineCount = 1, BaselineTotal = 100, BaselineRate = 0.01, ComparisonCount = 9, ComparisonTotal = 100, ComparisonRate = 0.09, PValue = 0.01, AdjustedPValue = 0.035, Increased = true, EstablishedIncrease = true }
        },
        Events = new[]
        {
            new ChatConsistencyEventView { AtUtc = Day1.AddDays(10), Kind = "toolGuides", Label = "tool guides edited on 2026-09-11", From = "abc123", To = "def456", RunId = 20, PreviousRunId = 11, SubjectKey = "provider=TestProvider;model=test-model-1", InTargetSeries = true },
            new ChatConsistencyEventView { AtUtc = Day1.AddDays(11), Kind = "systemPrompt", Label = "system prompt edited on 2026-09-12", RunId = 31, PreviousRunId = 30, SubjectKey = ControlKey }
        },
        Controls = new ChatConsistencyControls
        {
            Matches = new[]
            {
                new ChatConsistencyControlMatchView { Period = "baseline", TargetRunId = 10, ControlRunId = 30, ControlSubjectKey = ControlKey, PairedItemCount = 20 },
                new ChatConsistencyControlMatchView { Period = "comparison", TargetRunId = 20, ControlRunId = 31, ControlSubjectKey = ControlKey, PairedItemCount = 20 }
            },
            Effects = new[]
            {
                new ChatConsistencyControlEffect
                {
                    EndpointId = "P1", ControlSubjectKey = ControlKey, ControlDisplay = ControlName, ControlProvider = "OtherProvider",
                    ControlBaselineRunIds = new long[] { 30 }, ControlComparisonRunIds = new long[] { 31 }, ItemCount = 20,
                    ControlChange = -0.2, ControlChangeCi95 = new ChatConsistencyInterval(-1.5, 1.1),
                    DidEstimate = -4.0, DidCi95 = new ChatConsistencyInterval(-6.1, -2.0), DidPValue = 0.002, DidSeparatesTarget = true
                }
            },
            MissingControls = new[]
            {
                new ChatConsistencyMissingControlView { Period = "comparison", SuiteName = "Core suite", Fingerprint = "f1", SuggestedText = "Run " + ControlName + " on Core suite in the comparison period.", TargetRunId = 21 }
            },
            ControlRunIds = new long[] { 30, 31, 32 }
        },
        Attribution = new ChatConsistencyAttributionOutcome
        {
            Attributions = attributions ?? new[]
            {
                new ChatConsistencyAttributionResult
                {
                    Label = "Undeclared change of the model", Side = ChatConsistencyAttribution.SideProvider,
                    Grade = ChatConsistencyEvidenceGrade.Indicated, Rule = ChatConsistencyAttribution.RuleUndeclaredChange,
                    Endpoints = new[] { "P1" }, Evidence = "Quality degraded for the model under test while " + ControlName + " held steady."
                },
                new ChatConsistencyAttributionResult
                {
                    Label = "Overseer change", Side = ChatConsistencyAttribution.SideOurs,
                    Grade = ChatConsistencyEvidenceGrade.NotEstablished, Rule = ChatConsistencyAttribution.RuleNotAttributable,
                    Endpoints = new[] { "P1" }
                }
            }
        },
        ServedModels = new ChatConsistencyServedModels
        {
            Baseline = new[] { new ChatConsistencyServedModelCount("test-model-1-2026-08-01", 120) },
            Comparison = new[] { new ChatConsistencyServedModelCount("test-model-1-2026-08-01", 118) },
            BaselineCalls = 120,
            ComparisonCalls = 118,
            ComparisonServedSpeeds = new[] { "standard" }
        },
        OwnWaits = new[]
        {
            new ChatConsistencyOwnWaits { Period = "baseline", PermitWaitMs = 1200, BackoffWaitMs = 0, ModelTimeMs = 100_000, OwnWaitShare = 0.012, AnswersWithTelemetry = 40 },
            new ChatConsistencyOwnWaits { Period = "comparison", PermitWaitMs = 900, BackoffWaitMs = 3000, ModelTimeMs = 100_000, OwnWaitShare = 0.039, RetryAttemptCount = 2, AnswersWithTelemetry = 39 }
        },
        CommonGrader = new ChatConsistencyCommonGrader { SnapshotId = 5, Display = "Grader Model", CoveredRunIds = new long[] { 10, 11, 20, 21 } },
        GraderDrift = new[]
        {
            new ChatConsistencyGraderDrift { AnchorRunId = 10, SnapshotId = 5, Display = "Grader Model", EarliestAtUtc = Day1, LatestAtUtc = Day1.AddDays(15), Drift = 1.2, ItemCount = 20, WithinMargin = true }
        },
        PriceCard = new ChatConsistencyPriceCard { Available = true, Source = "current configuration pricing", InputPerMillion = 3m, OutputPerMillion = 15m, AsOf = "2026-09-01" },
        Annotations = new[]
        {
            new ChatConsistencyAnnotationView { Id = 1, AtUtc = Day1.AddDays(12), Provider = "TestProvider", Kind = ChatConsistencyAnnotationKind.ProviderConfirmedCause, Text = "The provider confirmed a serving change.", SourceUrl = "https://example.com/status" }
        },
        DataQuality = new[] { new ChatConsistencyNote { Kind = "legacy", Text = "One comparison run lacks call telemetry." } },
        Limitations = new[] { "Only weekday mornings were sampled.", "Only one control model, " + ControlName + ", was run." },
        NextRuns = new[]
        {
            new ChatConsistencyNextRun { Kind = "stratum", Period = "comparison", EndpointId = "P2", Reason = "US business hours were not sampled.", Suggestion = "Run during US business hours.", RepeatRunId = 21 }
        },
        InputSha256 = string.Concat(Enumerable.Repeat("0123456789abcdef", 4)),
        AnalysisCodeVersion = 4
    };

    private static ChatConsistencyUnitView Unit(long id, string period, params long[] members) => new()
    {
        UnitId = id,
        Kind = ChatConsistencyComparisonSetKinds.BatteryRunUnit,
        Period = period,
        StartedAtUtc = Day1,
        MemberRunIds = members
    };

    private static BenchmarkReportFact Fact(BenchmarkReportFactSheet sheet, string key)
        => Assert.Single(sheet.Facts, f => f.Key == key);

    private static IReadOnlyList<string> RequestIds()
        => Enumerable.Range(1, 14).Select(i => "request-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .Concat(new[] { "request-01", " ", "" })
            .ToList();

    [Fact]
    public void Build_IsDeterministic()
    {
        foreach (var audience in BenchmarkReportSlots.ChatConsistencyAudiences)
        {
            string first = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(Result(), audience, RequestIds()));
            string second = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(Result(), audience, RequestIds()));
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void Facts_AreSortedByKey_AndEachKeyAppearsOnce()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ProviderIssueReport, RequestIds());
        var keys = sheet.Facts.Select(f => f.Key).ToList();

        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryEmittedKey_HasALabel()
    {
        var keys = BenchmarkReportSlots.ChatConsistencyAudiences
            .SelectMany(a => BenchmarkChatConsistencyReportFacts.Build(Result(), a, RequestIds()).Facts)
            .Select(f => f.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("endpoint.P1.ci95", keys);
        Assert.Contains("quality.dimensions.accuracy.estimate", keys);
        Assert.Contains("tools.toolMixWikiSearch.estimate", keys);
        Assert.Contains("secondary.shiftFunction.P2D10.estimate", keys);
        Assert.Contains("secondary.shiftFunction.note", keys);
        Assert.Contains("reliability.http429.establishedIncrease", keys);
        Assert.Contains("controls.missing.1.suggestion", keys);
        Assert.Contains("requestIds.sample.10", keys);

        var unlabeled = keys.Where(k => !BenchmarkReportFactLabels.TryLabel(k, out _)).ToList();
        Assert.Empty(unlabeled);

        foreach (string key in keys)
        {
            string label = BenchmarkReportFactLabels.Label(key);
            Assert.NotEqual(key, label);
            Assert.DoesNotContain(".", label);
            Assert.True(char.IsUpper(label[0]) || char.IsDigit(label[0]), key + " → " + label);
        }
    }

    [Theory]
    [InlineData("verdict.overall", "Overall verdict on the chat")]
    [InlineData("scope.hours", "Hours the comparison holds for")]
    [InlineData("endpoint.P1.ci95", "Quality (P1): 95 % interval")]
    [InlineData("endpoint.P2.percent", "Time to first answer text (P2): change in percent")]
    [InlineData("protocol.margin.P5", "Equivalence margin of cost per question (P5)")]
    [InlineData("period.baseline.start", "Baseline period: start")]
    [InlineData("events.2.at", "Overseer event 2: time")]
    [InlineData("attribution.1.side", "Attribution 1: side")]
    [InlineData("controls.missing.1.suggestion", "Missing control 1: suggestion")]
    [InlineData("reliability.http429.baseline", "429 responses: baseline")]
    [InlineData("annotation.1.kind", "Annotation 1: kind")]
    [InlineData("limitation.2", "Limitation 2")]
    [InlineData("requestIds.sample.3", "Sample request ID 3")]
    public void ChatConsistencyLabels_FollowTheirPattern(string key, string expected)
    {
        Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label));
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("events.0.at")]
    [InlineData("events.01.at")]
    [InlineData("endpoint.P9.estimate")]
    [InlineData("endpoint.P1.unknownFigure")]
    [InlineData("tools.notFound")]
    [InlineData("quality.dim.accuracy")]
    public void AKeyOutsideThePatterns_HasNoLabel(string key)
        => Assert.False(BenchmarkReportFactLabels.TryLabel(key, out _));

    [Fact]
    public void VerdictOverallAndScopeHours_ArePresent()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);

        var overall = Fact(sheet, "verdict.overall");
        Assert.Equal("degraded", overall.Value!.GetValue<string>());
        Assert.StartsWith("Degraded: quality degraded (indicated)", overall.Display);
        Assert.Equal("The chat changed", Fact(sheet, "verdict.short").Display);

        Assert.Equal("weekdays 04–12 UTC", Fact(sheet, "scope.hours").Display);
        Assert.Equal("degraded (Indicated)", Fact(sheet, "verdict.quality").Display);
        Assert.False(Fact(sheet, "serving.timeOfDayAssessable").Value!.GetValue<bool>());
        Assert.Equal("12.5\u00A0%", Fact(sheet, "scope.excludedShare").Display);
    }

    [Fact]
    public void Endpoints_StateTheirFigures_AndANotComputedEndpointKeepsItsKeys()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.TechnicalReport);

        Assert.Equal("\u22124.2\u00A0index points", Fact(sheet, "endpoint.P1.estimate").Display);
        Assert.Equal("\u22126.0 to \u22123.1\u00A0index points", Fact(sheet, "endpoint.P1.ci95").Display);
        Assert.Equal("±2.5\u00A0index points", Fact(sheet, "endpoint.P1.mde").Display);
        Assert.Equal("0.016", Fact(sheet, "endpoint.P1.adjustedP").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Key == "endpoint.P1.percent");
        Assert.Equal("+2.0\u00A0%", Fact(sheet, "endpoint.P2.percent").Display);
        Assert.Equal("+2.0\u00A0%", Fact(sheet, "endpoint.P2.estimate").Display);
        Assert.Equal("±8.3\u00A0%", Fact(sheet, "endpoint.P2.mde").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Display.Contains("log ratio", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("endpoint.", StringComparison.Ordinal) && f.Display.Contains('-'));

        var p3 = Fact(sheet, "endpoint.P3.estimate");
        Assert.False(p3.Available);
        Assert.Equal("No telemetry run in the baseline.", p3.UnavailableReason);
        Assert.Equal("not computable", Fact(sheet, "endpoint.P3.verdict").Display);
    }

    [Fact]
    public void SampleRequestIds_OnlyInTheProviderIssueReport_AtMostTen()
    {
        var provider = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ProviderIssueReport, RequestIds());
        var samples = provider.Facts.Where(f => f.Key.StartsWith("requestIds.sample.", StringComparison.Ordinal) && f.Key != "requestIds.sample.count").ToList();

        Assert.Equal(BenchmarkChatConsistencyReportFacts.MaxSampleRequestIds, samples.Count);
        Assert.Equal(10, Fact(provider, "requestIds.sample.count").Value!.GetValue<int>());
        Assert.Equal("request-01", Fact(provider, "requestIds.sample.1").Display);
        Assert.Equal("request-10", Fact(provider, "requestIds.sample.10").Display);
        Assert.DoesNotContain(provider.Facts, f => f.Key == "requestIds.sample.11");

        foreach (var audience in new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief })
        {
            var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), audience, RequestIds());
            Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("requestIds.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IndexedKeys_CountFromOne()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.InternalBrief);
        var keys = sheet.Facts.Select(f => f.Key).ToList();

        Assert.Contains("events.1.at", keys);
        Assert.Contains("events.2.at", keys);
        Assert.Contains("attribution.1.side", keys);
        Assert.Contains("attribution.2.side", keys);
        Assert.Contains("coverage.strata.1", keys);
        Assert.Contains("limitation.1", keys);
        Assert.DoesNotContain(keys, k => k.Split('.').Any(p => p == "0"));

        Assert.Equal("provider", Fact(sheet, "attribution.1.side").Value!.GetValue<string>());
        Assert.Equal("Indicated", Fact(sheet, "attribution.1.grade").Display);
        Assert.Equal(2, Fact(sheet, "events.count").Value!.GetValue<int>());
        Assert.Equal("Model A", Fact(sheet, "events.2.series").Display);
        Assert.Equal("the model under test", Fact(sheet, "events.1.series").Display);
        Assert.Equal("ProviderConfirmedCause", Fact(sheet, "annotation.1.kind").Value!.GetValue<string>());
    }

    [Fact]
    public void TheRunSelectionNoteAndLimitation_BecomeLimitationFacts()
    {
        const string note = "2 usable runs of the model inside the periods were not analyzed — left out in step 1: #45 (baseline); "
            + "not selected in step 4: #60 (comparison).";
        const string limitation = "The operator chose the runs: 2 usable runs of the model inside the periods were not analyzed "
            + "(see the run selection). The verdicts hold for the analyzed runs; leaving runs out after looking at the timeline can bias them.";
        var baseResult = Result();
        var result = baseResult with
        {
            DataQuality = baseResult.DataQuality.Append(new ChatConsistencyNote { Kind = "runSelection", Text = note }).ToList(),
            Limitations = baseResult.Limitations.Append(limitation).ToList(),
            RunSelection = new ChatConsistencyRunSelectionView
            {
                Recorded = true,
                RangeLabel = "Last 30 days",
                LeftOutRunIds = new long[] { 45 },
                UnanalyzedRuns = new[]
                {
                    new ChatConsistencyUnanalyzedRun { RunId = 45, Period = "baseline", StartedAtUtc = Day1.AddDays(1), Reason = ChatConsistencyUnanalyzedReasons.LeftOut },
                    new ChatConsistencyUnanalyzedRun { RunId = 60, Period = "comparison", StartedAtUtc = Day1.AddDays(15), Reason = ChatConsistencyUnanalyzedReasons.NotSelected }
                }
            }
        };

        foreach (var audience in BenchmarkReportSlots.ChatConsistencyAudiences)
        {
            var sheet = BenchmarkChatConsistencyReportFacts.Build(result, audience, RequestIds());

            Assert.Equal(3, Fact(sheet, "limitation.count").Value!.GetValue<int>());
            Assert.Equal(limitation, Fact(sheet, "limitation.3").Display);
            Assert.Equal(2, Fact(sheet, "limitation.dataQuality.count").Value!.GetValue<int>());
            Assert.Equal(note, Fact(sheet, "limitation.dataQuality.2").Display);
        }
    }

    [Fact]
    public void TheComparedFact_StatesTheBatteryOrSuite_AndItsAnalyzedUnits()
    {
        var battery = Result() with
        {
            ComparisonSet = new ChatConsistencyComparedSet { Kind = ChatConsistencyComparisonSetKinds.Battery, Key = "battery:abc", Label = "Two initial suites (revision 1)" },
            UnitKind = ChatConsistencyComparisonSetKinds.BatteryRunUnit,
            Units = new[] { Unit(101, "baseline", 10, 40), Unit(102, "baseline", 11, 41), Unit(103, "comparison", 20, 50), Unit(104, "comparison", 21, 51) }
        };
        var sheet = BenchmarkChatConsistencyReportFacts.Build(battery, BenchmarkReportAudience.TechnicalReport);
        var compared = Fact(sheet, "analysis.compared");
        Assert.Equal("Battery Two initial suites (revision 1), 4 battery runs", compared.Display);
        Assert.Equal("battery:abc", compared.Value!.GetValue<string>());
        Assert.Contains("Two initial suites (revision 1)", sheet.KnownNames);
        Assert.True(BenchmarkReportFactLabels.TryLabel("analysis.compared", out _), "analysis.compared needs a label in BenchmarkReportFactLabels.");

        // A suite set without recorded units counts the periods' runs.
        var suite = Result() with
        {
            ComparisonSet = new ChatConsistencyComparedSet { Kind = ChatConsistencyComparisonSetKinds.Suite, Key = "suite:id:7", Label = "Core suite" }
        };
        Assert.Equal("Suite Core suite, 4 runs", Fact(BenchmarkChatConsistencyReportFacts.Build(suite, BenchmarkReportAudience.TechnicalReport), "analysis.compared").Display);

        // Before code version 4, and without a set, there is no such fact.
        foreach (var result in new[] { battery with { AnalysisCodeVersion = 3 }, battery with { ComparisonSet = null } })
        {
            Assert.DoesNotContain(BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.TechnicalReport).Facts, f => f.Key == "analysis.compared");
        }
    }

    [Fact]
    public void ControlModels_AreLettered_AndNeverNamedInTheFacts()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ProviderIssueReport, RequestIds());

        var peer = Assert.Single(sheet.Peers);
        Assert.Equal("A", peer.Letter);
        Assert.Equal(ControlName, peer.Label);
        Assert.Equal(new long[] { 30, 31 }, peer.RunIds);

        Assert.Equal("Model A", Fact(sheet, "did.1.model").Display);
        Assert.Equal("Model A", Fact(sheet, "controls.1.model").Display);
        Assert.Contains("Model A", Fact(sheet, "attribution.1.evidence").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Display.Contains(ControlName, StringComparison.OrdinalIgnoreCase)
                                                || (f.UnavailableReason ?? string.Empty).Contains(ControlName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ControlName, sheet.KnownNames);
        Assert.Contains("Test Model", sheet.KnownNames);
    }

    [Fact]
    public void Sheet_IsAChatConsistencySheet_WithItsAnalysisBlock()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);

        Assert.True(sheet.IsChatConsistency);
        Assert.False(sheet.IsComparison);
        Assert.Equal("chat-consistency:7", sheet.SubjectKey);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.SubjectKind, sheet.SubjectKind);
        Assert.Equal(new long[] { 10, 11, 20, 21 }, sheet.SubjectRunIds);
        Assert.NotNull(sheet.ChatConsistency);
        Assert.Equal(7, sheet.ChatConsistency!.AnalysisId);
        Assert.Equal(new long[] { 30, 31, 32 }, sheet.ChatConsistency.ControlRunIds);
        Assert.True(sheet.ChatConsistency.ProviderIssueReportAvailable);
        Assert.Equal("Overseer Chat Consistency Report: Test Model", BenchmarkChatConsistencyReportFacts.Title(Result()));
    }

    [Fact]
    public void DocumentRuns_ListTheTargetRuns_ThenTheControlRunsAsPeers()
    {
        var runs = BenchmarkChatConsistencyReportFacts.DocumentRuns(Result());

        Assert.Equal(new (long, bool)[] { (10, false), (11, false), (20, false), (21, false), (30, true), (31, true), (32, true) }, runs);
    }

    [Fact]
    public void ProviderIssueReport_IsAvailable_ForAProviderFindingGradedIndicated()
    {
        var (available, reason) = BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability(Result());

        Assert.True(available);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(ChatConsistencyAttribution.SideProvider, ChatConsistencyEvidenceGrade.NotEstablished)]
    [InlineData(ChatConsistencyAttribution.SideOurs, ChatConsistencyEvidenceGrade.Established)]
    [InlineData(ChatConsistencyAttribution.SideInfrastructure, ChatConsistencyEvidenceGrade.Indicated)]
    public void ProviderIssueReport_IsUnavailable_WithoutAProviderFindingGradedEstablishedOrIndicated(string side, ChatConsistencyEvidenceGrade grade)
    {
        var result = Result(new[] { new ChatConsistencyAttributionResult { Label = "x", Side = side, Grade = grade, Rule = "R11 undetermined" } });

        var (available, reason) = BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability(result);

        Assert.False(available);
        Assert.Equal("No provider-side finding graded Established or Indicated in this analysis.", reason);
        Assert.False(BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary).ChatConsistency!.ProviderIssueReportAvailable);
    }

    [Fact]
    public void ChatConsistencySlots_ExistForTheFourAudiences()
    {
        var expected = new Dictionary<BenchmarkReportAudience, string[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new[] { "asGoodAsBefore", "playerImpact", "ourChanges", "providerChanges", "confidenceAndScope", "nextRuns" },
            [BenchmarkReportAudience.TechnicalReport] = new[] { "questionAndDesign", "runsAndCoverage", "overseerEvents", "endpointResults", "attribution", "robustness", "limitations", "reproducibility" },
            [BenchmarkReportAudience.InternalBrief] = new[] { "chatFindings", "changeEffects", "infrastructureIssues", "nextRuns", "actions" },
            [BenchmarkReportAudience.ProviderIssueReport] = new[] { "issueSummary", "affectedModel", "timeline", "measurements", "hoursObserved", "ruledOut", "sampleRequestIds", "providerRequest" },
        };
        var chatSheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(expected.Keys, BenchmarkReportSlots.ChatConsistencyAudiences);
        foreach (var (audience, slots) in expected)
        {
            var spec = BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency);
            Assert.Same(spec, BenchmarkReportSlots.ForChatConsistency(audience));
            Assert.Same(spec, BenchmarkReportSlots.For(audience, chatSheet));
            Assert.Equal(audience, spec.Audience);
            Assert.Equal(slots, spec.RequiredSlots);
            Assert.True(spec.ChatConsistencyScope);
            Assert.False(spec.ComparisonScope);
            Assert.False(spec.UsesStrengthsAndWeaknesses);
            Assert.False(spec.UsesRecommendations);
            Assert.False(spec.UsesLeads);
            Assert.False(spec.RequiresQuestionTopics);
            foreach (string slot in slots) Assert.False(string.IsNullOrWhiteSpace(BenchmarkReportSlots.ChatConsistencySlotTitles[slot]));
        }

        Assert.Equal("Is the Overseer chat with this model as good as before?", BenchmarkReportSlots.ChatConsistencySlotTitles["asGoodAsBefore"]);
        Assert.Equal("What we ruled out (our changes, infrastructure)", BenchmarkReportSlots.ChatConsistencySlotTitles["ruledOut"]);
    }

    [Fact]
    public void ExistingScopes_KeepTheirSlots()
    {
        var model = new Dictionary<BenchmarkReportAudience, string[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new[] { "comparison", "meaning", "confidence" },
            [BenchmarkReportAudience.TechnicalReport] = new[] { "abstract", "whyItScored", "whatWorked", "limitations" },
            [BenchmarkReportAudience.InternalBrief] = new[] { "overseerChat", "benchmarkSystem", "modelResult" },
        };
        var comparison = new Dictionary<BenchmarkReportAudience, string[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new[] { "overview", "whichModel", "tradeOffs", "reliability" },
            [BenchmarkReportAudience.TechnicalReport] = new[] { "abstract", "results", "dimensionProfiles", "frontier", "questionPatterns", "graderReliability", "limitations" },
            [BenchmarkReportAudience.InternalBrief] = new[] { "sharedGaps", "modelGaps", "benchmarkSystem" },
        };
        var modelSheet = new BenchmarkReportFactSheet();
        var comparisonSheet = new BenchmarkReportFactSheet { Scope = BenchmarkReportFactSheet.ComparisonScopeValue };

        foreach (var (audience, slots) in model)
        {
            var spec = BenchmarkReportSlots.For(audience, BenchmarkReportScope.Model);
            Assert.Same(BenchmarkReportSlots.For(audience), spec);
            Assert.Same(spec, BenchmarkReportSlots.For(audience, modelSheet));
            Assert.Equal(slots, spec.RequiredSlots);
            Assert.False(spec.ChatConsistencyScope);
            Assert.False(spec.ComparisonScope);
        }

        foreach (var (audience, slots) in comparison)
        {
            var spec = BenchmarkReportSlots.For(audience, BenchmarkReportScope.Comparison);
            Assert.Same(spec, BenchmarkReportSlots.For(audience, comparisonSheet));
            Assert.Equal(slots, spec.RequiredSlots);
            Assert.False(spec.ChatConsistencyScope);
            Assert.True(spec.ComparisonScope);
        }

        Assert.Equal(new[] { "comparison" }, BenchmarkReportSlots.ExecutiveSummary.PeerOnlySlots);
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkReportSlots.For(BenchmarkReportAudience.ProviderIssueReport));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkReportSlots.For(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportScope.Comparison));
        Assert.False(modelSheet.IsChatConsistency);
        Assert.False(comparisonSheet.IsChatConsistency);
    }

    [Fact]
    public void Events_AreInWords_GroupedIntoUpdates_AndNeverCarryTheirRawValues()
    {
        var result = Result() with
        {
            Events = new[]
            {
                new ChatConsistencyEventView
                {
                    AtUtc = Day1.AddDays(10).AddHours(14).AddMinutes(49), Kind = OverseerEventKinds.HarnessVersion, Label = "harness changed candidate input on 2026-09-11 (run #98)",
                    From = "53", To = "54", RunId = 98, PreviousRunId = 97, SubjectKey = "provider=TestProvider;model=test-model-1", InTargetSeries = true
                },
                new ChatConsistencyEventView
                {
                    AtUtc = Day1.AddDays(10).AddHours(14).AddMinutes(49), Kind = OverseerEventKinds.Wiki, Label = "wiki updated on 2026-09-11 (run #98)",
                    From = "a8fa85a" + new string('0', 33), To = "4bb80dc" + new string('1', 33), RunId = 98, PreviousRunId = 97,
                    SubjectKey = "provider=TestProvider;model=test-model-1", InTargetSeries = true
                },
                new ChatConsistencyEventView
                {
                    AtUtc = Day1.AddDays(10).AddHours(15), Kind = OverseerEventKinds.CandidatePromptOptions, Label = "candidate prompt options changed on 2026-09-11 (run #99)",
                    From = "{\"hasGameSnapshot\":false,\"verboseMode\":false}", To = "{\"hasGameSnapshot\":true,\"verboseMode\":false}", RunId = 99, PreviousRunId = 97,
                    SubjectKey = "provider=TestProvider;model=test-model-1", InTargetSeries = true
                },
                new ChatConsistencyEventView
                {
                    AtUtc = Day1.AddDays(11), Kind = OverseerEventKinds.CandidateSystemPrompt, Label = "system prompt changed on 2026-09-12 (run #31)",
                    From = new string('a', 64), To = new string('b', 64), RunId = 31, PreviousRunId = 30, SubjectKey = ControlKey
                }
            }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.TechnicalReport);

        Assert.Equal(OverseerEventKinds.Wiki, Fact(sheet, "events.2.kind").Value!.GetValue<string>());
        Assert.Equal("Wiki revision", Fact(sheet, "events.2.kind").Display);
        Assert.Equal("harness 53 → 54", Fact(sheet, "events.1.change").Display);
        Assert.Equal("wiki revision a8fa85a → 4bb80dc", Fact(sheet, "events.2.change").Display);
        Assert.Equal("game snapshot: off → on", Fact(sheet, "events.3.change").Display);
        Assert.Equal("system prompt changed", Fact(sheet, "events.4.change").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("events.", StringComparison.Ordinal) && (f.Key.EndsWith(".from", StringComparison.Ordinal) || f.Key.EndsWith(".to", StringComparison.Ordinal)));

        Assert.Equal(2, Fact(sheet, "eventGroups.count").Value!.GetValue<int>());
        Assert.Equal("2026-09-11 14:49 UTC", Fact(sheet, "eventGroups.1.at").Display);
        Assert.Equal("runs #98 and #99", Fact(sheet, "eventGroups.1.run").Display);
        Assert.Equal("harness 53 → 54; wiki revision a8fa85a → 4bb80dc; game snapshot: off → on", Fact(sheet, "eventGroups.1.changes").Display);
        Assert.Equal("the model under test", Fact(sheet, "eventGroups.1.series").Display);
        Assert.Equal("Model A", Fact(sheet, "eventGroups.2.series").Display);

        Assert.DoesNotContain(sheet.Facts, f => !BenchmarkChatConsistencyReportFacts.WriterHidden(f.Key)
            && (f.Display.Contains('{') || System.Text.RegularExpressions.Regex.IsMatch(f.Display, "[0-9a-f]{12,}")));
    }

    [Theory]
    [InlineData(OverseerEventKinds.CorpusIndex, "{not json", "{}", "changed")]
    [InlineData(OverseerEventKinds.CandidatePromptOptions, "[1]", "{\"hasGameSnapshot\":true}", "changed")]
    [InlineData(OverseerEventKinds.Wiki, "0123456789abcdef0123456789abcdef", "a8fa85a4bb80dc4e5f6a7b8c9d0e1f2a3b4c5d6e", "wiki revision 0123456 → a8fa85a")]
    [InlineData(OverseerEventKinds.ToolIterationCaps, "{\"Simple\":8,\"Intermediate\":8,\"Advanced\":8}", "{\"Simple\":10,\"Intermediate\":10,\"Advanced\":10}", "tool iterations per question: 8 → 10")]
    [InlineData(OverseerEventKinds.QuestionTimeouts, "{\"Simple\":300,\"Intermediate\":300,\"Advanced\":300}", "{\"Simple\":300,\"Intermediate\":300,\"Advanced\":600}", "question timeout (Advanced): 300 → 600\u00A0s")]
    [InlineData(OverseerEventKinds.MaxToolCallsPerQuestion, "12", "16", "12 → 16")]
    [InlineData(OverseerEventKinds.HarnessVersion, "53", "54 (re-run 55)", "harness 53 → 54 (re-run 55)")]
    [InlineData(OverseerEventKinds.ToolGuides, "aaaa", "bbbb", "changed")]
    [InlineData("SomethingNewSha256", "0123456789abcdef0123", "fedcba9876543210fedc", "changed")]
    [InlineData(OverseerEventKinds.CorpusIndex, "{\"gnollhackWiki\":{\"sha256\":\"a\",\"fileCount\":5,\"indexedAtUtc\":\"x\"}}", "{\"gnollhackWiki\":{\"sha256\":\"a\",\"fileCount\":5,\"indexedAtUtc\":\"y\"}}", "re-indexed, contents unchanged")]
    public void AnEventChange_ReadsAsAShortBeforeAndAfter_OrChanged(string kind, string from, string to, string expected)
    {
        string change = ChatConsistencyEventText.Change(kind, from, to);

        Assert.Equal(expected, change);
        Assert.True(change.Length <= ChatConsistencyEventText.MaxChars);
        Assert.DoesNotContain("{", change, StringComparison.Ordinal);
        Assert.DoesNotMatch("[0-9a-fA-F]{12,}", change);
    }

    [Fact]
    public void AnEventChange_IsCutToItsLimit_AndALabelNeverIsAnIdentifier()
    {
        var options = Enumerable.Range(1, 30).Select(i => "\"option" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\":");
        string before = "{" + string.Join(",", options.Select(o => o + "false")) + "}";
        string after = "{" + string.Join(",", options.Select(o => o + "true")) + "}";

        string change = ChatConsistencyEventText.Change(OverseerEventKinds.CandidatePromptOptions, before, after);

        Assert.Equal(ChatConsistencyEventText.MaxChars, change.Length);
        Assert.EndsWith("…", change, StringComparison.Ordinal);
        Assert.Equal("Corpus index", ChatConsistencyEventText.Label(OverseerEventKinds.CorpusIndex));
        Assert.Equal("Something new", ChatConsistencyEventText.Label("SomethingNewSha256"));
        Assert.Equal("tool calls per question: 12 → 16", ChatConsistencyEventText.Describe(OverseerEventKinds.MaxToolCallsPerQuestion, "12", "16"));
        Assert.Equal("tool guides changed", ChatConsistencyEventText.Describe(OverseerEventKinds.ToolGuides, "a", "b"));
        Assert.All(OverseerEventKinds.All, kind => Assert.DoesNotContain(kind, ChatConsistencyEventText.Label(kind), StringComparison.Ordinal));
    }

    [Fact]
    public void TheInputHash_StaysOnTheSheet_ButIsHiddenFromTheWriter()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.TechnicalReport);

        Assert.Equal(string.Concat(Enumerable.Repeat("0123456789abcdef", 4)), Fact(sheet, BenchmarkChatConsistencyReportFacts.InputSha256Key).Value!.GetValue<string>());
        Assert.True(BenchmarkChatConsistencyReportFacts.WriterHidden("analysis.inputSha256"));
        Assert.True(BenchmarkChatConsistencyReportFacts.WriterHidden("events.3.from"));
        Assert.True(BenchmarkChatConsistencyReportFacts.WriterHidden("events.12.to"));
        Assert.False(BenchmarkChatConsistencyReportFacts.WriterHidden("events.3.change"));
        Assert.False(BenchmarkChatConsistencyReportFacts.WriterHidden("analysis.id"));

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.TechnicalReport, sheet, new BenchmarkReportContentSnapshot()).UserMessage;
        Assert.DoesNotContain("analysis.inputSha256", message, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnitsAndTheMinimumSample_AreStatedInBatteryRuns()
    {
        var battery = Result() with
        {
            ComparisonSet = new ChatConsistencyComparedSet { Kind = ChatConsistencyComparisonSetKinds.Battery, Key = "battery:abc", Label = "Two initial suites (revision 1)" },
            UnitKind = ChatConsistencyComparisonSetKinds.BatteryRunUnit,
            Units = new[] { Unit(101, "baseline", 10, 11), Unit(102, "comparison", 20, 21) },
            Baseline = Result().Baseline with { Days = new[] { "2026-09-01" } },
            Comparison = Result().Comparison with { Days = new[] { "2026-09-15" } }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(battery, BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(1, Fact(sheet, "period.baseline.units").Value!.GetValue<int>());
        Assert.Equal("1 battery run", Fact(sheet, "period.baseline.units").Display);
        Assert.Equal("battery run", Fact(sheet, "period.comparison.unitNoun").Display);
        Assert.Equal("battery run #101 (runs #10 and #11)", Fact(sheet, "period.baseline.memberRuns").Display);
        Assert.Equal("2 battery runs per period", Fact(sheet, "sample.minimumUnits").Display);
        Assert.Equal("2 days per period", Fact(sheet, "sample.minimumDays").Display);
        Assert.Equal("20 paired items", Fact(sheet, "sample.minimumPairedItems").Display);
        Assert.False(Fact(sheet, "sample.met").Value!.GetValue<bool>());
        Assert.Equal("1 battery run per period on 1 day; the minimum is 2 battery runs on 2 days per period and 20 paired items",
            Fact(sheet, "sample.shortfall").Display);

        // Two runs per period on two days meet the minimum, and the shortfall is then unavailable.
        var runs = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);
        Assert.True(Fact(runs, "sample.met").Value!.GetValue<bool>());
        Assert.False(Fact(runs, "sample.shortfall").Available);
        Assert.Equal("2 runs", Fact(runs, "period.baseline.units").Display);
        Assert.Equal("runs #10 and #11", Fact(runs, "period.baseline.memberRuns").Display);
    }

    [Fact]
    public void TheMdeNote_TheMissingControlLabel_AndTheInstrumentNote_ReadAsWords()
    {
        var baseResult = Result();
        var result = baseResult with
        {
            Endpoints = baseResult.Endpoints.Select(e => e.Id == "P1"
                ? e with { MinimumDetectableEffectNote = "one run per period: run-to-run noise not estimable" }
                : e).ToList(),
            Controls = baseResult.Controls with
            {
                MissingControls = new[]
                {
                    new ChatConsistencyMissingControlView { Period = "comparison", SuiteName = "Core suite", Fingerprint = "f1", TargetRunId = 21,
                        SuggestedText = "Run " + ControlName + " on Core suite as run #21 (instrument 39113903b9b2)." },
                    new ChatConsistencyMissingControlView { Period = "baseline", SuiteName = "Core suite", Fingerprint = "f1", TargetRunId = 11,
                        SuggestedText = "Run " + ControlName + " on Core suite as run #11." }
                }
            }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.InternalBrief);

        Assert.Equal("one run per period: run-to-run noise not estimable", Fact(sheet, "endpoint.P1.mdeNote").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Key == "endpoint.P2.mdeNote");
        Assert.Equal("2 missing-control notes", Fact(sheet, "controls.missing.count").Display);
        Assert.Equal("Run Model A on Core suite as run #21.", Fact(sheet, "controls.missing.1.suggestion").Display);
        Assert.Equal("Run Model A on Core suite as run #21.", BenchmarkChatConsistencyReportFacts.WithoutInstrument("Run Model A on Core suite as run #21 (instrument 39113903b9b2)."));
    }

    [Theory]
    [InlineData(new[] { "degraded", "equivalent" }, "The chat changed")]
    [InlineData(new[] { "equivalent", "changed, negligible" }, "No meaningful change")]
    [InlineData(new[] { "equivalent", "not computable" }, "No change on the computed endpoints")]
    [InlineData(new[] { "inconclusive", "equivalent" }, "Not enough evidence yet")]
    [InlineData(new[] { "not computable", "not computable" }, "Nothing could be computed")]
    public void TheOutcomeTitle_FollowsTheResultsTab(string[] verdicts, string title)
    {
        var endpoints = verdicts.Select((v, i) => new ChatConsistencyEndpointResult
        {
            Id = "P" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Computed = v != "not computable",
            VerdictLabel = v
        }).ToList();

        Assert.Equal(title, BenchmarkChatConsistencyReportFacts.OutcomeTitle(endpoints));
    }

    [Fact]
    public void AnExistingSheet_SerializesWithoutTheChatConsistencyBlock()
    {
        var json = BenchmarkReportJson.Serialize(new BenchmarkReportFactSheet { SubjectKey = "run:1" });

        Assert.DoesNotContain("chatConsistency", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutCommonHours_ScopeHoursIsUnavailable_AndTimeOfDaySaysThePeriodsShareNone()
    {
        var result = Result() with { Scope = new ChatConsistencyScope { Text = "no common time stratum" } };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary);

        var hours = Fact(sheet, "scope.hours");
        Assert.False(hours.Available);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.NoSharedHoursReason, hours.UnavailableReason);
        Assert.Equal("not assessable: the periods share no hours", Fact(sheet, "serving.timeOfDayAssessable").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Display.Contains("no common time stratum", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOutOfDateAcknowledgment_IsAFact_OnlyWhenTheJobRecordedIt()
    {
        var current = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);
        Assert.DoesNotContain(current.Facts, f => f.Key == "analysis.writtenOutOfDate");

        var written = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary, null, "earlier analysis code (version 4) and changed inputs");
        Assert.Equal("earlier analysis code (version 4) and changed inputs", Fact(written, "analysis.writtenOutOfDate").Display);
    }

    [Fact]
    public void EachPeriodsHoursAndLevels_AreFacts_AndUnavailableOnAnEarlierAnalysis()
    {
        var result = Result() with
        {
            PeriodHours = new[]
            {
                new ChatConsistencyPeriodHours { Period = "baseline", Strata = new[] { "Weekday 04–08 UTC" }, Text = "weekdays 04–08 UTC" },
                new ChatConsistencyPeriodHours { Period = "comparison", Strata = new[] { "Weekday 12–16 UTC" }, Text = "weekdays 12–16 UTC" }
            },
            PeriodLevels = new[]
            {
                new ChatConsistencyPeriodLevels
                {
                    Period = "baseline", AnswerCount = 36, NativeMeanQuality = 81.04, OverallIndex = 82.06, OverallIndexHalfWidth = 2.45,
                    OverallIndexIntervalNote = "question sampling only", MedianTimeToFirstAnswerTextMs = 39_312, MedianStreamingRate = 61.24,
                    MeanOutputTokensPerAnswer = 9044.4, MeanCostPerQuestionUsd = 0.00712, FailedAnswerCount = 0
                },
                new ChatConsistencyPeriodLevels { Period = "comparison", AnswerCount = 36, MeanCostPerQuestionUsd = 0.1, FailedAnswerCount = 1 }
            }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal("weekdays 04–08 UTC", Fact(sheet, "period.baseline.hours").Display);
        Assert.Equal("weekdays 12–16 UTC", Fact(sheet, "period.comparison.hours").Display);
        Assert.Equal("82.1 (95 % interval 79.6–84.5, question sampling only)", Fact(sheet, "level.baseline.overallIndex").Display);
        Assert.Equal("mean score 81.0 points", Fact(sheet, "level.baseline.quality").Display);
        Assert.Equal("median 39.3 s", Fact(sheet, "level.baseline.timeToFirstAnswerText").Display);
        Assert.Equal("median 61.2 tokens/s", Fact(sheet, "level.baseline.streamingRate").Display);
        Assert.Equal("mean 9,044 output tokens per answer", Fact(sheet, "level.baseline.outputTokens").Display);
        Assert.Equal("mean $0.0071 per question", Fact(sheet, "level.baseline.costPerQuestion").Display);
        Assert.Equal("mean $0.10 per question", Fact(sheet, "level.comparison.costPerQuestion").Display);
        Assert.Equal("0 of 36 answers", Fact(sheet, "level.baseline.failedAnswers").Display);
        Assert.Equal("1 of 36 answers", Fact(sheet, "level.comparison.failedAnswers").Display);
        Assert.False(Fact(sheet, "level.comparison.overallIndex").Available);

        var earlier = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.ExecutiveSummary);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.BeforeLevelsReason, Fact(earlier, "period.baseline.hours").UnavailableReason);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.BeforeLevelsReason, Fact(earlier, "level.comparison.quality").UnavailableReason);
    }

    [Fact]
    public void TheEventGroups_FollowTheSharedFixture()
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(EventGroupsFixturePath()));
        var root = fixture.RootElement;
        var events = root.GetProperty("events").EnumerateArray().Select(e => new ChatConsistencyEventView
        {
            AtUtc = e.GetProperty("atUtc").GetDateTime().ToUniversalTime(),
            Kind = e.GetProperty("kind").GetString()!,
            Label = e.GetProperty("label").GetString()!,
            From = e.GetProperty("from").GetString(),
            To = e.GetProperty("to").GetString(),
            RunId = e.GetProperty("runId").GetInt64(),
            PreviousRunId = e.GetProperty("previousRunId").GetInt64(),
            SubjectKey = e.GetProperty("subjectKey").GetString()!,
            InTargetSeries = e.GetProperty("inTargetSeries").GetBoolean()
        }).ToList();
        var expected = root.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("tag").GetString()).ToList();

        var groups = BenchmarkChatConsistencyReportFacts.EventGroups(
            events, root.GetProperty("baselineEndUtc").GetDateTime().ToUniversalTime(), root.GetProperty("comparisonStartUtc").GetDateTime().ToUniversalTime());

        var tagOf = groups.SelectMany(g => g.Events.Select(e => (Event: e, g.Tag))).ToDictionary(x => x.Event, x => x.Tag);
        Assert.Equal(expected, events.Select(e => tagOf[e]));
    }

    [Fact]
    public void AnUpdateOfOneDay_IsDatedFromItsFirstToItsLastEvent_AndTagged()
    {
        var result = Result() with
        {
            Baseline = Result().Baseline with { EndUtc = Day1.AddDays(10).AddHours(12) },
            Comparison = Result().Comparison with { StartUtc = Day1.AddDays(10).AddHours(13) },
            Events = new[]
            {
                new ChatConsistencyEventView { AtUtc = Day1.AddDays(10).AddHours(14).AddMinutes(49), Kind = OverseerEventKinds.HarnessVersion, From = "53", To = "54", RunId = 98, PreviousRunId = 97, SubjectKey = "s", InTargetSeries = true },
                new ChatConsistencyEventView { AtUtc = Day1.AddDays(10).AddHours(16), Kind = OverseerEventKinds.Wiki, From = "a", To = "b", RunId = 99, PreviousRunId = 98, SubjectKey = "s", InTargetSeries = true }
            }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.TechnicalReport);

        Assert.Equal(1, Fact(sheet, "eventGroups.count").Value!.GetValue<int>());
        Assert.Equal("E1", Fact(sheet, "eventGroups.1.tag").Display);
        Assert.Equal("2026-09-11 14:49 UTC", Fact(sheet, "eventGroups.1.at").Display);
        Assert.Equal("2026-09-11 16:00 UTC", Fact(sheet, "eventGroups.1.lastAt").Display);
    }

    [Fact]
    public void RobustnessSummaries_StateEndpointsSharingAStatusAndDetailTogether()
    {
        var result = Result() with
        {
            RobustnessChecks = new[]
            {
                new ChatConsistencyCheck { EndpointId = "P4", Name = "Leave-one-run-out stability", Status = ChatConsistencyCheckStatus.NotAssessable, Detail = "No decisive verdict to check." },
                new ChatConsistencyCheck { EndpointId = "P5", Name = "Leave-one-run-out stability", Status = ChatConsistencyCheckStatus.NotAssessable, Detail = "No decisive verdict to check." },
                new ChatConsistencyCheck { EndpointId = "P1", Name = "Leave-one-run-out stability", Status = ChatConsistencyCheckStatus.Failed, Detail = "Without run #10 the verdict does not hold." }
            }
        };

        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.TechnicalReport);

        Assert.Equal("Not assessable for P4 and P5: no decisive verdict to check; failed for P1: without run #10 the verdict does not hold.",
            Fact(sheet, "robustness.leaveOneRunOutStability.summary").Display);
    }

    [Fact]
    public void ServedModelIDs_AreWrittenInCapitals()
    {
        var sheet = BenchmarkChatConsistencyReportFacts.Build(Result(), BenchmarkReportAudience.TechnicalReport);

        Assert.Equal("the served model IDs are the same in both periods", Fact(sheet, "identity.changed").Display);
    }

    /// <summary>The event-group fixture the client's report-chart spec reads too, from the repository root.</summary>
    private static string EventGroupsFixturePath()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "MobileGnollHackLogger.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "Overseer", "ClientApp", "src", "app", "admin", "benchmark", "chat-consistency-tab", "cc-event-groups.fixture.json");
    }
}
