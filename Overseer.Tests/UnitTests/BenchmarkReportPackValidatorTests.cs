namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;

/// <summary>A small fact sheet, content snapshot and valid writer outputs for the report-pack writer tests.</summary>
internal static class ReportPackWriterTestData
{
    public const string Headline =
        "{{subject}} gave accurate item advice and placed near the top on quality at {{quality.index}}, though it missed rules about cursed items.";

    public static BenchmarkReportFactSheet Sheet() => new()
    {
        SubjectKey = "run:12",
        SubjectKind = "Run",
        SubjectLabel = "Claude Opus 5.5 (high)",
        SubjectDisplayName = "Claude Opus 5.5",
        SubjectProvider = "Anthropic",
        SubjectModelId = "claude-opus-5-5",
        SubjectRunIds = new List<long> { 12 },
        SubjectState = "Comparable",
        SuiteId = 3,
        SuiteName = "Early Game Suite 2",
        Peers = new List<BenchmarkReportPeer>
        {
            new()
            {
                Letter = "A", EntryKey = "run:14", Label = "GPT-5.2 (high)", DisplayName = "GPT-5.2",
                Provider = "OpenAI", ModelId = "gpt-5.2", State = "Comparable", RunIds = new List<long> { 14 }
            },
            new()
            {
                Letter = "B", EntryKey = "run:15", Label = "Gemini 3.8 Flash (medium)", DisplayName = "Gemini 3.8 Flash",
                Provider = "Google", ModelId = "gemini-3.8-flash", State = "Degraded", SpeedDegraded = true,
                RunIds = new List<long> { 15 }
            },
        },
        Graders = new List<BenchmarkReportGrader>
        {
            new() { Role = BenchmarkReportFacts.PanelMemberARole, Label = "Gemini 3.8 Flash", Provider = "Google", ModelId = "gemini-3.8-flash", SameFamilyAsSubject = false },
            new() { Role = BenchmarkReportFacts.PanelMemberBRole, Label = "Claude Sonnet 5.5", Provider = "Anthropic", ModelId = "claude-sonnet-5-5", SameFamilyAsSubject = true },
        },
        Facts = new List<BenchmarkReportFact>
        {
            new() { Key = "quality.dim.accuracy", Value = JsonValue.Create(4.1), Display = "4.1 / 5" },
            new() { Key = "quality.index", Value = JsonValue.Create(80.0), Display = "80 ± 3 / 100" },
            new() { Key = "speed.p50Ms", Value = null, Display = string.Empty, Available = false, UnavailableReason = "the speed axis is degraded" },
            new() { Key = "style.responseStyleConflict", Value = JsonValue.Create(true), Display = "yes" },
            new() { Key = "tools.notFound", Value = JsonValue.Create(2), Display = "2" },
        },
        Questions = new List<BenchmarkReportQuestion>
        {
            new() { Number = 1, QuestionKey = "k1", OrderIndex = 0, Band = "Simple", Score = 90, PeerMean = 85, Difference = 5, PeerCount = 2, RunCount = 1 },
            new() { Number = 2, QuestionKey = "k2", OrderIndex = 1, Band = "Intermediate", Score = 50, PeerMean = 72, Difference = -22, PeerCount = 2, RunCount = 1 },
            new() { Number = 3, QuestionKey = "k3", OrderIndex = 2, Band = "Advanced", Score = 70, PeerMean = 73, Difference = -3, PeerCount = 2, CriticalError = true, RefutedClaims = 1, RunCount = 1 },
            new() { Number = 4, QuestionKey = "k4", OrderIndex = 3, Band = "Advanced", Score = null, PeerMean = null, Difference = null, RunCount = 1 },
        },
        Rows = new List<BenchmarkReportFindingRow>
        {
            new() { Id = "R1", Kind = "strength", Category = "accuracy", Questions = new List<int> { 1 }, Status = "Convergent", SupportLabel = "Both graders", MemberAText = "Correct item lore.", MemberBText = "Accurate lore." },
            new() { Id = "R2", Kind = "weakness", Category = "completeness", Questions = new List<int> { 2 }, Status = "Convergent", SupportLabel = "Both graders", MemberAText = "Ignored the board.", MemberBText = "Missed board state." },
            new() { Id = "R3", Kind = "weakness", Category = "accuracy", Questions = new List<int> { 3 }, Status = "Conflicting", SupportLabel = "Graders disagree", MemberAText = "Unsafe prayer advice.", MemberBText = "Reasonable prayer advice." },
            new() { Id = "R4", Kind = "strength", Category = "readability", Questions = new List<int> { 4 }, Status = "MemberBOnly", SupportLabel = "Co-assessor only", MemberBText = "Clear wording." },
        },
        KnownNames = new List<string> { "Claude Opus 5.5", "Claude Sonnet 5.5", "Early Game Suite 2", "GPT-5.2", "Gemini 3.8 Flash" },
        NoSignificanceSummary = "No pairwise significance test is run.",
        NoSignificanceInstead = "Compare the intervals.",
    };

    public static BenchmarkReportContentSnapshot Content() => new()
    {
        AnswerExcerptChars = 600,
        Runs = new List<BenchmarkReportContentRun>
        {
            new()
            {
                RunId = 12,
                Questions = new List<BenchmarkReportContentQuestion>
                {
                    new()
                    {
                        Number = 1, QuestionKey = "k1", OrderIndex = 0, Band = "Simple",
                        QuestionText = "What happens when you dip a long sword into a fountain while at experience level five or higher?",
                        ExpectedPoints = "A lawful character may receive Excalibur from the Lady of the Lake. The chance is one in six per dip.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "Dipping a long sword into a fountain can grant Excalibur to lawful characters.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Assessor", Label = "Gemini 3.8 Flash", Score = 5, Comment = "The answer names Excalibur and the lawful alignment requirement correctly.", Evidence = new List<string> { "grant Excalibur to lawful characters" } },
                        },
                    },
                    new()
                    {
                        Number = 2, QuestionKey = "k2", OrderIndex = 1, Band = "Intermediate",
                        QuestionText = "Which items in the inventory shown on the board are cursed and how can you tell?",
                        ExpectedPoints = "The bag of holding and the ring are cursed; the altar test reveals it.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "Drop items on an altar to see a black flash.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Assessor", Label = "Gemini 3.8 Flash", Score = 2, Comment = "Misses that the ring on the board is already known to be cursed." },
                        },
                        ClaimRulings = new List<BenchmarkReportContentClaimRuling>
                        {
                            new() { Claim = "Dropping items on an altar shows a black flash for cursed items", Verdict = "supported", Rationale = "The source prints a black flash for cursed objects." },
                        },
                    },
                    new()
                    {
                        Number = 3, QuestionKey = "k3", OrderIndex = 2, Band = "Advanced",
                        QuestionText = "Should the player pray right now given the board state?",
                        ExpectedPoints = "No, the prayer timeout is too high.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "Yes, pray now.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Assessor", Label = "Gemini 3.8 Flash", Score = 1, Comment = "Critical error: recommends praying during prayer timeout." },
                        },
                    },
                    new()
                    {
                        Number = 4, QuestionKey = "k4", OrderIndex = 3, Band = "Advanced",
                        QuestionText = "What does the wand of digging do when zapped downward?",
                        ExpectedPoints = "It digs a hole through the floor and you fall to the level below.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "It digs a hole and you fall through to the next level.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Assessor", Label = "Gemini 3.8 Flash", Score = 5, Comment = "Correct and complete." },
                        },
                    },
                },
            },
        },
    };

    public static BenchmarkReportWriterOutput ValidOutput(BenchmarkReportAudience audience)
    {
        var output = new BenchmarkReportWriterOutput
        {
            Headline = Headline,
            Strengths = { new BenchmarkReportWriterItem { Text = "Explained item lore accurately.", Questions = { 1 }, Evidence = { "R1", "quality.dim.accuracy" } } },
            Weaknesses = { new BenchmarkReportWriterItem { Text = "Overlooked what the board already showed about cursed items.", Questions = { 2 }, Evidence = { "R2", "Q2" } } },
        };

        switch (audience)
        {
            case BenchmarkReportAudience.ExecutiveSummary:
                output.Sections[BenchmarkReportSlots.Comparison] =
                    "{{subject}} sits close to {{peer:A}} on quality, and the order between them is not established.";
                output.Sections[BenchmarkReportSlots.Meaning] =
                    "A player asking {{subject}} about item lore will usually get sound advice.\n\nAdvice that depends on reading the current board needs a second look.";
                output.Sections[BenchmarkReportSlots.Confidence] =
                    "The quality result is {{quality.index}}, and the graders mostly agreed. Its interval overlaps that of {{peer:A}}, so their order is not established.";
                output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 1, Topic = "dipping a sword into a fountain" });
                break;

            case BenchmarkReportAudience.TechnicalReport:
                output.Sections[BenchmarkReportSlots.Abstract] =
                    "The benchmark measured how well {{subject}} answers questions about the game. Its quality result, {{quality.index}}, places it close to {{peer:A}}, with overlapping intervals.";
                output.Sections[BenchmarkReportSlots.WhyItScored] =
                    "Reading the game state was the main weakness, most visibly on Q2.\n\nOn Q3 the graders disagree about whether the advice was safe.";
                output.Sections[BenchmarkReportSlots.WhatWorked] = "Item lore was consistently accurate, as on Q1.";
                output.Sections[BenchmarkReportSlots.Limitations] =
                    "{{peer:B}} is degraded on speed, and each model rests on a single run.";
                output.Recommendations.Add(new BenchmarkReportWriterRecommendation
                {
                    For = BenchmarkReportSlots.TargetModelDevelopers,
                    Text = "Check the board before answering questions about item status.",
                    Evidence = { "R2" }
                });
                AddAllTopics(output);
                AddNotes(output);
                break;

            case BenchmarkReportAudience.InternalBrief:
                output.Sections[BenchmarkReportSlots.OverseerChat] =
                    "Check whether the chat assistant reads inventory status from the board before suggesting tests.";
                output.Sections[BenchmarkReportSlots.BenchmarkSystem] =
                    "The graders disagree on Q3, which may point to an ambiguous rubric.";
                output.Sections[BenchmarkReportSlots.ModelResult] = "{{subject}} performed close to {{peer:A}} on quality.";
                output.Recommendations.Add(new BenchmarkReportWriterRecommendation
                {
                    For = BenchmarkReportSlots.TargetOverseerChat,
                    Text = "Remind the assistant to read item status from the board first.",
                    Evidence = { "R2" }
                });
                output.Leads.Add(new BenchmarkReportLead
                {
                    Triage = "suite",
                    Text = "Check whether the rubric for Q3 is too strict; the graders disagree.",
                    Questions = { 3 },
                    Evidence = { "R3" }
                });
                AddAllTopics(output);
                AddNotes(output);
                break;
        }

        return output;
    }

    private static void AddAllTopics(BenchmarkReportWriterOutput output)
    {
        output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 1, Topic = "dipping a sword into a fountain" });
        output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 2, Topic = "spotting cursed items in the inventory" });
        output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 3, Topic = "whether to pray now" });
        output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 4, Topic = "zapping a digging wand downward" });
    }

    private static void AddNotes(BenchmarkReportWriterOutput output)
    {
        output.QuestionNotes.Add(new BenchmarkReportQuestionNote { Question = 2, Note = "Relied on a general test instead of what the board already showed." });
        output.QuestionNotes.Add(new BenchmarkReportQuestionNote { Question = 3, Note = "Recommended an unsafe prayer." });
    }
}

/// <summary>
/// A small chat consistency analysis, its fact sheets and valid writer outputs for the claim
/// discipline tests and the chat consistency prompt goldens. Quality (P1) degraded, graded Indicated;
/// time to first answer text (P2) is equivalent, graded Established; P3 is not computable; cost per
/// answer (P5) is inconclusive. Attribution 1 is on the provider's side, attribution 2 on ours;
/// annotation 1 is a provider-confirmed cause, annotation 2 a provider statement; two Overseer events
/// with realistic raw values (the prompt options turning the game snapshot on, in the model's series,
/// and a corpus re-index, in the control's), and one missing control whose suggestion still carries
/// the instrument note analyses saved before code version 5 stored. It is saved under the current
/// analysis code version, with each period's hours and levels.
/// </summary>
internal static class ChatConsistencyReportTestData
{
    public const string ControlKey = "provider=OtherProvider;model=control-model-2";
    public const string ControlName = "Control Model Two";
    public const string ControlProvider = "OtherProvider";

    private static readonly DateTime Day1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Event 1's raw values: the candidate prompt options before and after the game snapshot was turned on.</summary>
    public static readonly string PromptOptionsBefore = new BenchmarkCandidatePromptOptions().ToCanonicalJson();
    public static readonly string PromptOptionsAfter = new BenchmarkCandidatePromptOptions { HasGameSnapshot = true }.ToCanonicalJson();

    /// <summary>Event 2's raw values: a corpus index fingerprint before and after the GnollHack wiki was re-indexed with three more files.</summary>
    public static readonly string CorpusIndexBefore = CorpusIndex(string.Concat(Enumerable.Repeat("a1b2", 16)), 812);
    public static readonly string CorpusIndexAfter = CorpusIndex(string.Concat(Enumerable.Repeat("c3d4", 16)), 815);

    private static string CorpusIndex(string wikiSha256, int wikiFiles)
        => "{\"gnollhackWiki\":{\"sha256\":\"" + wikiSha256 + "\",\"fileCount\":" + wikiFiles + ",\"indexedAtUtc\":\"2026-09-10T08:00:00.000Z\"},"
           + "\"gnollhackSource\":{\"sha256\":\"" + string.Concat(Enumerable.Repeat("e5f6", 16)) + "\",\"fileCount\":1204,\"indexedAtUtc\":\"2026-09-10T08:00:00.000Z\"},"
           + "\"knowledgeBase\":null,\"nethackWiki\":null,\"nethackSource\":null}";

    public static ChatConsistencyAnalysisResult Result(bool timeOfDayAssessable = false) => new()
    {
        AnalysisId = 7,
        Name = "Weekly check",
        Headline = "Overseer chat with Test Model: quality degraded; speed equivalent within weekdays 04–12 UTC",
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
            UsBusinessHoursCovered = timeOfDayAssessable,
            OutsideBusinessHoursCovered = true,
            TimeOfDayAssessable = timeOfDayAssessable
        },
        Baseline = new ChatConsistencyPeriodSummary
        {
            Name = "baseline",
            StartUtc = Day1,
            EndUtc = Day1.AddDays(7),
            RunIds = new long[] { 10, 11 },
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
            SuiteNames = new[] { "Core suite" }
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
                BaselineRunCount = 2, ComparisonRunCount = 2, ItemCount = 20
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
                Computed = false, NotComputedReason = "No telemetry run in the baseline.", NotComputedKind = ChatConsistencyNotComputedKinds.NoTelemetry,
                VerdictLabel = "not computable", Grade = ChatConsistencyEvidenceGrade.NotEstablished
            },
            new ChatConsistencyEndpointResult
            {
                Id = "P5", Name = "Cost per answer", Unit = "log ratio", Scale = ChatConsistencyEffectScale.LogRatio,
                Margin = Math.Log(1.1), Computed = true, Estimate = 0.03, EstimatePercent = 3.05,
                Ci95 = new ChatConsistencyInterval(-0.04, 0.10), Ci90 = new ChatConsistencyInterval(-0.03, 0.09),
                PValue = 0.4, AdjustedPValue = 0.8, Verdict = ConsistencyVerdict.Inconclusive, VerdictLabel = "inconclusive",
                Grade = ChatConsistencyEvidenceGrade.NotEstablished, MinimumDetectableEffect = 0.12, MinimumDetectableEffectPercent = 12.7,
                RunsPerPeriodForMargin = 6, MinimumSampleMet = true, BaselineRunCount = 2, ComparisonRunCount = 2, ItemCount = 20
            }
        },
        Events = new[]
        {
            new ChatConsistencyEventView
            {
                AtUtc = Day1.AddDays(10), Kind = OverseerEventKinds.CandidatePromptOptions, Label = "candidate prompt options changed on 2026-09-11 (run #20)",
                From = PromptOptionsBefore, To = PromptOptionsAfter, RunId = 20, PreviousRunId = 11,
                SubjectKey = "provider=TestProvider;model=test-model-1", InTargetSeries = true
            },
            new ChatConsistencyEventView
            {
                AtUtc = Day1.AddDays(11), Kind = OverseerEventKinds.CorpusIndex, Label = "corpus index changed on 2026-09-12 (run #31)",
                From = CorpusIndexBefore, To = CorpusIndexAfter, RunId = 31, PreviousRunId = 30, SubjectKey = ControlKey
            }
        },
        Controls = new ChatConsistencyControls
        {
            Matches = new[]
            {
                new ChatConsistencyControlMatchView { Period = "baseline", TargetRunId = 10, ControlRunId = 30, ControlSubjectKey = ControlKey, PairedItemCount = 20 },
                new ChatConsistencyControlMatchView { Period = "comparison", TargetRunId = 20, ControlRunId = 31, ControlSubjectKey = ControlKey, PairedItemCount = 20 }
            },
            MissingControls = new[]
            {
                new ChatConsistencyMissingControlView
                {
                    Period = "comparison", SuiteName = "Core suite", Fingerprint = "f1", TargetRunId = 21,
                    SuggestedText = "Run " + ControlName + " on Core suite under the build of run #21 (instrument " + string.Concat(Enumerable.Repeat("3b9", 4)) + ")."
                }
            },
            Effects = new[]
            {
                new ChatConsistencyControlEffect
                {
                    EndpointId = "P1", ControlSubjectKey = ControlKey, ControlDisplay = ControlName, ControlProvider = ControlProvider,
                    ControlBaselineRunIds = new long[] { 30 }, ControlComparisonRunIds = new long[] { 31 }, ItemCount = 20,
                    ControlChange = -0.2, ControlChangeCi95 = new ChatConsistencyInterval(-1.5, 1.1),
                    DidEstimate = -4.0, DidCi95 = new ChatConsistencyInterval(-6.1, -2.0), DidPValue = 0.002, DidSeparatesTarget = true
                }
            },
            ControlRunIds = new long[] { 30, 31 }
        },
        Attribution = new ChatConsistencyAttributionOutcome
        {
            Attributions = new[]
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
                    Endpoints = new[] { "P5" }
                }
            }
        },
        ServedModels = new ChatConsistencyServedModels
        {
            Baseline = new[] { new ChatConsistencyServedModelCount("test-model-1-2026-08-01", 120) },
            Comparison = new[] { new ChatConsistencyServedModelCount("test-model-1-2026-08-01", 118) },
            BaselineCalls = 120,
            ComparisonCalls = 118
        },
        OwnWaits = new[]
        {
            new ChatConsistencyOwnWaits { Period = "comparison", PermitWaitMs = 900, BackoffWaitMs = 3000, ModelTimeMs = 100_000, OwnWaitShare = 0.039, RetryAttemptCount = 2, AnswersWithTelemetry = 39 }
        },
        Annotations = new[]
        {
            new ChatConsistencyAnnotationView { Id = 1, AtUtc = Day1.AddDays(12), Provider = "TestProvider", Kind = ChatConsistencyAnnotationKind.ProviderConfirmedCause, Text = "The provider confirmed a serving change.", SourceUrl = "https://example.com/status" },
            new ChatConsistencyAnnotationView { Id = 2, AtUtc = Day1.AddDays(13), Provider = "TestProvider", Kind = ChatConsistencyAnnotationKind.ProviderStatement, Text = "The provider published a status note." }
        },
        Limitations = new[] { "Only weekday mornings were sampled." },
        NextRuns = new[]
        {
            new ChatConsistencyNextRun { Kind = "stratum", Period = "comparison", EndpointId = "P5", Reason = "The cost change is inconclusive.", Suggestion = "Repeat the comparison runs during US business hours.", RepeatRunId = 21 }
        },
        PeriodHours = new[]
        {
            new ChatConsistencyPeriodHours { Period = "baseline", Strata = new[] { "Weekday 04–08 UTC", "Weekday 08–12 UTC" }, Text = "weekdays 04–12 UTC" },
            new ChatConsistencyPeriodHours { Period = "comparison", Strata = new[] { "Weekday 04–08 UTC", "Weekday 08–12 UTC", "Weekday 12–16 UTC" }, Text = "weekdays 04–16 UTC" }
        },
        PeriodLevels = new[]
        {
            new ChatConsistencyPeriodLevels
            {
                Period = "baseline", AnswerCount = 40, NativeMeanQuality = 82.4, MedianTimeToFirstAnswerTextMs = 39_300, MedianStreamingRate = 61.2,
                MeanOutputTokensPerAnswer = 9044, MeanCostPerQuestionUsd = 0.0071, FailedAnswerCount = 0
            },
            new ChatConsistencyPeriodLevels
            {
                Period = "comparison", AnswerCount = 40, NativeMeanQuality = 78.1, MedianTimeToFirstAnswerTextMs = 40_100, MedianStreamingRate = 59.8,
                MeanOutputTokensPerAnswer = 8233, MeanCostPerQuestionUsd = 0.0066, FailedAnswerCount = 1
            }
        },
        InputSha256 = string.Concat(Enumerable.Repeat("0123456789abcdef", 4)),
        AnalysisCodeVersion = ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion
    };

    /// <summary>The sample request ids a Provider Issue Report's sheet states.</summary>
    public static readonly IReadOnlyList<string> RequestIds = new[] { "req-alpha", "req-beta" };

    public static BenchmarkReportFactSheet Sheet(BenchmarkReportAudience audience, bool timeOfDayAssessable = false)
        => BenchmarkChatConsistencyReportFacts.Build(
            Result(timeOfDayAssessable), audience, audience == BenchmarkReportAudience.ProviderIssueReport ? RequestIds : null);

    public static BenchmarkReportContentSnapshot Content() => new();

    /// <summary>A writer output of the audience that keeps every rule, C1 to C7 included.</summary>
    public static BenchmarkReportWriterOutput ValidOutput(BenchmarkReportAudience audience)
    {
        var output = new BenchmarkReportWriterOutput();
        var s = output.Sections;
        switch (audience)
        {
            case BenchmarkReportAudience.ExecutiveSummary:
                output.Headline = "The Overseer chat with {{subject}} is {{verdict.overall}} within {{scope.hours}}.";
                s[BenchmarkReportSlots.AsGoodAsBefore] = "Answer quality degraded by {{endpoint.P1.estimate}} within {{scope.hours}}. Waiting time is {{endpoint.P2.verdict}}.";
                s[BenchmarkReportSlots.PlayerImpact] = "Players got answers of lower quality, by {{endpoint.P1.estimate}}. Cost per answer is inconclusive at {{endpoint.P5.estimate}}, and the runs could only detect a change of {{endpoint.P5.mde}}.";
                s[BenchmarkReportSlots.OurChanges] = "The Overseer edited its tool guides on {{events.1.at}}. Their effect is not established, graded {{attribution.2.grade}}.";
                s[BenchmarkReportSlots.ProviderChanges] = "The analysis places the quality change on the provider's side, graded {{attribution.1.grade}}.";
                s[BenchmarkReportSlots.ConfidenceAndScope] = "The result rests on {{n.targetRuns}} and {{n.answers}}, and time of day is {{serving.timeOfDayAssessable}}.";
                s[BenchmarkReportSlots.NextRuns] = "The analysis suggests one more run: {{nextRuns.1.suggestion}}";
                break;

            case BenchmarkReportAudience.TechnicalReport:
                output.Headline = "Quality of the Overseer chat with {{subject}} degraded by {{endpoint.P1.estimate}} within {{scope.hours}}.";
                s[BenchmarkReportSlots.QuestionAndDesign] = "The analysis asks whether the Overseer chat with {{subject}} changed between a baseline and a comparison period. It compares matched runs of the same suites under {{protocol.label}}.";
                s[BenchmarkReportSlots.RunsAndCoverage] = "The baseline holds {{period.baseline.runs}} and the comparison {{period.comparison.runs}}, within {{scope.hours}}.";
                s[BenchmarkReportSlots.OverseerEvents] = "Two Overseer events fall between the periods: {{events.1.label}} and {{events.2.label}}.";
                s[BenchmarkReportSlots.EndpointResults] = "Quality degraded by {{endpoint.P1.estimate}}, with an interval of {{endpoint.P1.ci95}}, graded {{endpoint.P1.grade}}. Time to first answer text is {{endpoint.P2.verdict}}, graded {{endpoint.P2.grade}}.";
                s[BenchmarkReportSlots.Attribution] = "The analysis places the quality change on the provider's side, graded {{attribution.1.grade}} under {{attribution.1.rule}}.";
                s[BenchmarkReportSlots.Robustness] = "The analysis ran {{robustness.count}}, and {{robustness.failed}} failed.";
                s[BenchmarkReportSlots.Limitations] = "The analysis records one limitation: {{limitation.1}}";
                s[BenchmarkReportSlots.Reproducibility] = "Analysis {{analysis.id}} used code version {{analysis.codeVersion}} under {{protocol.label}}.";
                break;

            case BenchmarkReportAudience.InternalBrief:
                output.Headline = "Quality of the Overseer chat with {{subject}} degraded by {{endpoint.P1.estimate}} within {{scope.hours}}.";
                s[BenchmarkReportSlots.ChatFindings] = "Check the chat tools first: quality degraded by {{endpoint.P1.estimate}} within {{scope.hours}}.";
                s[BenchmarkReportSlots.ChangeEffects] = "The effect of {{events.1.label}} is not established, graded {{attribution.2.grade}}.";
                s[BenchmarkReportSlots.InfrastructureIssues] = "Our own waits took {{ownWaits.comparison.share}} of model time in the comparison period.";
                s[BenchmarkReportSlots.NextRuns] = "Run the suggested follow-up: {{nextRuns.1.suggestion}}";
                s[BenchmarkReportSlots.Actions] = "Prepare a Provider Issue Report on {{attribution.1.label}}, graded {{attribution.1.grade}}.";
                break;

            case BenchmarkReportAudience.ProviderIssueReport:
                output.Headline = "Answer quality of the Overseer chat with {{subject}} degraded by {{endpoint.P1.estimate}} within {{scope.hours}}.";
                s[BenchmarkReportSlots.IssueSummary] = "Answer quality degraded by {{endpoint.P1.estimate}}, and the analysis places it on the provider's side as {{attribution.1.grade}}.";
                s[BenchmarkReportSlots.AffectedModel] = "The model is {{subject.label}} with thinking level {{subject.thinkingLevel}}, served as {{identity.servedModels}}.";
                s[BenchmarkReportSlots.Timeline] = "The baseline ran from {{period.baseline.start}} to {{period.baseline.end}}, and the comparison from {{period.comparison.start}} to {{period.comparison.end}}.";
                s[BenchmarkReportSlots.Measurements] = "Quality degraded by {{endpoint.P1.estimate}}, with an interval of {{endpoint.P1.ci95}}, graded {{endpoint.P1.grade}}.";
                s[BenchmarkReportSlots.HoursObserved] = "We observed {{scope.hours}} only, and time of day is {{serving.timeOfDayAssessable}}.";
                s[BenchmarkReportSlots.RuledOut] = "We checked {{events.1.label}} and {{events.2.label}} on our side, graded {{attribution.2.grade}}.";
                s[BenchmarkReportSlots.SampleRequestIds] = "These request ids come from the comparison period.\n\n- {{requestIds.sample.1}}\n- {{requestIds.sample.2}}";
                s[BenchmarkReportSlots.ProviderRequest] = "Please tell us whether anything changed on your side in the period of {{attribution.1.label}}.";
                break;
        }
        return output;
    }
}

public class BenchmarkReportPackValidatorTests
{
    private const BenchmarkReportAudience Es = BenchmarkReportAudience.ExecutiveSummary;
    private const BenchmarkReportAudience Tr = BenchmarkReportAudience.TechnicalReport;
    private const BenchmarkReportAudience Ib = BenchmarkReportAudience.InternalBrief;
    private const string MeaningP1 = "sections.meaning[p1]";

    private static IReadOnlyList<BenchmarkReportValidationNote> Validate(
        BenchmarkReportAudience audience,
        BenchmarkReportWriterOutput output,
        BenchmarkReportFactSheet? sheet = null)
        => BenchmarkReportPackValidator.Validate(audience, output, sheet ?? ReportPackWriterTestData.Sheet(), ReportPackWriterTestData.Content());

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateMeaning(string text, BenchmarkReportFactSheet? sheet = null)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Meaning] = text;
        return Validate(Es, output, sheet);
    }

    private static BenchmarkReportCleanResult Drop(BenchmarkReportAudience audience, BenchmarkReportWriterOutput output)
        => BenchmarkReportPackValidator.DropInvalid(audience, output, ReportPackWriterTestData.Sheet(), ReportPackWriterTestData.Content());

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
    public void ValidOutput_HasNoIssues(BenchmarkReportAudience audience)
    {
        var notes = Validate(audience, ReportPackWriterTestData.ValidOutput(audience));

        Assert.Empty(notes);
    }

    // Rule 1 ------------------------------------------------------------------------------------

    [Fact]
    public void Rule1_EmptyHeadline()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Headline = "  ";

        var notes = Validate(Es, output);

        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "headline");
    }

    [Fact]
    public void Rule1_MissingRequiredSlot()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections.Remove(BenchmarkReportSlots.Confidence);

        var notes = Validate(Es, output);

        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "sections.confidence");
    }

    [Fact]
    public void Rule1_SlotOfAnotherAudience()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Abstract] = "A short summary.";

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(1, note.Rule);
        Assert.Equal("sections.abstract", note.Location);
    }

    [Fact]
    public void Rule1_ListTheAudienceDoesNotUse()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation { For = "model_developers", Text = "Improve board reading." });
        output.Leads.Add(new BenchmarkReportLead { Triage = "chat", Text = "Check the tools.", Evidence = { "R2" } });

        var notes = Validate(Es, output);

        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "recommendations");
        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "leads");
    }

    [Fact]
    public void Rule1_RecommendationTargetOutsideTheAudience()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].For = BenchmarkReportSlots.TargetOverseerChat;

        var notes = Validate(Tr, output);

        var note = Assert.Single(notes);
        Assert.Equal(1, note.Rule);
        Assert.Equal("recommendations[0]", note.Location);
    }

    [Fact]
    public void Rule1_UnknownLeadTriage()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Leads[0].Triage = "finding";

        var notes = Validate(Ib, output);

        var note = Assert.Single(notes);
        Assert.Equal(1, note.Rule);
        Assert.Equal("leads[0]", note.Location);
    }

    // Rule 2 ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("The result is {{quality.nope}}.")]
    [InlineData("It trails {{peer:Z}} on quality.")]
    [InlineData("The result is {{ quality.index }}.")]
    [InlineData("The result is {{Subject}}.")]
    [InlineData("The result is {{quality.index.")]
    [InlineData("The result is quality.index}}.")]
    public void Rule2_InvalidTokens(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 2 && n.Location == MeaningP1);
    }

    [Fact]
    public void Rule2_ValidTokens()
    {
        var notes = ValidateMeaning("{{subject}} and {{peer:B}} differ; {{tools.notFound}} lookups found nothing, and {{speed.p50Ms}} is unavailable.");

        Assert.Empty(notes);
    }

    // Rule 3 ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("It scored 80 points.")]
    [InlineData("1. It answered well.")]
    [InlineData("It was right 75% of the time.")]
    [InlineData("It ranked 1st.")]
    [InlineData("It resembles GPT 5.2 in style.")]
    public void Rule3_BareDigits(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 3 && n.Location == MeaningP1);
    }

    [Theory]
    [InlineData("Unlike GPT-5.2 and Gemini 3.8 Flash, Claude Opus 5.5 answered well.")]
    [InlineData("On the Early Game Suite 2 it did well.")]
    [InlineData("gemini-3.8-flash and claude-opus-5-5 were both there.")]
    public void Rule3_VersionNumbersInsideKnownNamesPass(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.DoesNotContain(notes, n => n.Rule == 3);
    }

    [Theory]
    [InlineData("It answered three of the four questions well, twice as many as expected.")]
    [InlineData("The co-assessor was Claude Sonnet 5.5.")]
    [InlineData("It failed Q2 and Q3 but not Q1.")]
    [InlineData("The finding R1 holds.")]
    public void Rule3_NumberWordsNamesAndReferencesPass(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Empty(notes);
    }

    // Rule 4 ------------------------------------------------------------------------------------

    [Fact]
    public void Rule4_ItemQuestionOutsideTheExam()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Strengths[0].Questions.Add(9);

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Equal("strengths[0]", note.Location);
    }

    [Fact]
    public void Rule4_ProseReferenceOutsideTheExam()
    {
        var notes = ValidateMeaning("It struggled on Q9.");

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Contains("Q9", note.Message);
    }

    [Fact]
    public void Rule4_EvidenceQuestionOutsideTheExam_IsRule4NotRule5()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Weaknesses[0].Evidence.Add("Q9");

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Equal("weaknesses[0]", note.Location);
    }

    [Fact]
    public void Rule4_TopicQuestionOutsideTheExam()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.QuestionTopics[0].Question = 9;

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Equal("questionTopics[0]", note.Location);
    }

    [Fact]
    public void Rule4_TechnicalReportNeedsATopicForEveryQuestion()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.QuestionTopics.RemoveAll(t => t.Question == 4);

        var notes = Validate(Tr, output);

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Equal("questionTopics", note.Location);
        Assert.Contains("Q4", note.Message);
    }

    [Fact]
    public void Rule4_ExecutiveSummaryTopicsAreOptional()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.QuestionTopics.Clear();

        Assert.Empty(Validate(Es, output));
    }

    [Fact]
    public void Rule4_DuplicateTopic()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = 2, Topic = "reading item status" });

        var notes = Validate(Tr, output);

        var note = Assert.Single(notes);
        Assert.Equal(4, note.Rule);
        Assert.Equal("questionTopics[4]", note.Location);
    }

    // Rule 5 ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("R99")]
    [InlineData("made.up.key")]
    [InlineData("q2")]
    public void Rule5_UnknownEvidence(string id)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Strengths[0].Evidence.Add(id);

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(5, note.Rule);
        Assert.Equal("strengths[0]", note.Location);
    }

    [Fact]
    public void Rule5_StrengthWithoutEvidence()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Strengths[0].Evidence.Clear();

        var notes = Validate(Es, output);

        var note = Assert.Single(notes);
        Assert.Equal(5, note.Rule);
    }

    [Fact]
    public void Rule5_LeadWithoutEvidence()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Leads[0].Evidence.Clear();

        var notes = Validate(Ib, output);

        var note = Assert.Single(notes);
        Assert.Equal(5, note.Rule);
        Assert.Equal("leads[0]", note.Location);
    }

    [Fact]
    public void Rule5_AResearcherReportRecommendationMustCiteEvidence()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Evidence.Clear();

        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(5, note.Rule);
        Assert.Equal("recommendations[0]", note.Location);
    }

    [Fact]
    public void Rule5_AnInternalBriefRecommendationMayCiteNothing()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Recommendations[0].Evidence.Clear();

        Assert.Empty(Validate(Ib, output));
    }

    [Fact]
    public void Rule5_ProseRowReferenceMustExist()
    {
        var notes = ValidateMeaning("The finding R9 holds.");

        var note = Assert.Single(notes);
        Assert.Equal(5, note.Rule);
    }

    [Fact]
    public void Rule5_EveryKindOfEvidenceIdResolves()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Strengths[0].Evidence = new List<string> { "R1", "R4", "Q1", "quality.index", "tools.notFound" };

        Assert.Empty(Validate(Es, output));
    }

    // Rule 6 ------------------------------------------------------------------------------------

    [Fact]
    public void Rule6_StrengthCitesAWeaknessRow()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Strengths[0].Evidence.Add("R2");

        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(6, note.Rule);
        Assert.Equal("strengths[0]", note.Location);
    }

    [Fact]
    public void Rule6_WeaknessCitesAStrengthRow()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Weaknesses[0].Evidence.Add("R1");

        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(6, note.Rule);
        Assert.Equal("weaknesses[0]", note.Location);
    }

    [Fact]
    public void Rule6_OnlyConflictingRowsWithoutDisagreement()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Weaknesses[0] = new BenchmarkReportWriterItem { Text = "Gave unsafe prayer advice.", Questions = { 3 }, Evidence = { "R3" } };

        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(6, note.Rule);
    }

    [Theory]
    [InlineData("The graders disagreed about whether its prayer advice was safe.")]
    [InlineData("Its prayer advice drew disagreement between the graders.")]
    public void Rule6_OnlyConflictingRowsSayingTheGradersDisagree(string text)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Weaknesses[0] = new BenchmarkReportWriterItem { Text = text, Questions = { 3 }, Evidence = { "R3" } };

        Assert.Empty(Validate(Es, output));
    }

    [Fact]
    public void Rule6_ConflictingRowBesideAConvergentOne()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Weaknesses[0] = new BenchmarkReportWriterItem { Text = "Gave unsafe advice on the board.", Questions = { 2, 3 }, Evidence = { "R2", "R3" } };

        Assert.Empty(Validate(Es, output));
    }

    // Rule 7 ------------------------------------------------------------------------------------

    [Fact]
    public void Rule7_HeadlineWordLimit()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);

        output.Headline = "{{subject}} " + string.Join(" ", Enumerable.Repeat("well", 34));
        Assert.Empty(Validate(Es, output));

        output.Headline = "{{subject}} " + string.Join(" ", Enumerable.Repeat("well", 35));
        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("headline", note.Location);
    }

    [Fact]
    public void Rule7_ExecutiveSummaryHoldsThreeStrengths()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        var strength = output.Strengths[0];
        output.Strengths.Add(strength);
        output.Strengths.Add(strength);
        Assert.Empty(Validate(Es, output));

        output.Strengths.Add(strength);
        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("strengths[3]", note.Location);
    }

    [Fact]
    public void Rule7_TopicWordLimit()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);

        output.QuestionTopics[0].Topic = string.Join(" ", Enumerable.Repeat("item", 12));
        Assert.Empty(Validate(Es, output));

        output.QuestionTopics[0].Topic = string.Join(" ", Enumerable.Repeat("item", 13));
        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("questionTopics[0]", note.Location);
    }

    [Fact]
    public void Rule7_AbstractWordLimit()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);

        output.Sections[BenchmarkReportSlots.Abstract] = string.Join(" ", Enumerable.Repeat("result", 150));
        Assert.Empty(Validate(Tr, output));

        output.Sections[BenchmarkReportSlots.Abstract] = string.Join(" ", Enumerable.Repeat("result", 151));
        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections.abstract", note.Location);
    }

    [Theory]
    [InlineData(BenchmarkReportSlots.Meaning, 90)]
    [InlineData(BenchmarkReportSlots.Confidence, 60)]
    [InlineData(BenchmarkReportSlots.Comparison, 70)]
    public void Rule7_ExecutiveSummarySlotWordLimits(string slot, int limit)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("advice", limit));
        Assert.Empty(Validate(Es, output));

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("advice", limit + 1));
        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections." + slot, note.Location);
    }

    [Theory]
    [InlineData(BenchmarkReportSlots.WhyItScored, 300)]
    [InlineData(BenchmarkReportSlots.WhatWorked, 150)]
    [InlineData(BenchmarkReportSlots.Limitations, 120)]
    public void Rule7_ResearcherReportSlotWordLimits_AskForTheRepairTurn(string slot, int limit)
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("pattern", limit));
        Assert.Empty(Validate(Tr, output));

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("pattern", limit + 1));
        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections." + slot, note.Location);
        Assert.Contains(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), note.Message);

        // A note from Validate is what sends the writer its repair turn.
        Assert.Contains("- rule 7 at sections." + slot + ":", BenchmarkReportPackPrompt.BuildRepairMessage(new[] { note }));

        // Without a repair, the paragraphs past the cap are dropped.
        output.Sections[slot] = "A first paragraph about the patterns.\n\n" + string.Join(" ", Enumerable.Repeat("pattern", limit));
        var cleaned = Drop(Tr, output);
        Assert.Equal("A first paragraph about the patterns.", cleaned.Output.Sections[slot]);
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "sections." + slot + "[p2]");
    }

    [Fact]
    public void Rule7_TheExecutiveSummaryHasNoCapOnTheResearcherSlots()
    {
        Assert.Null(BenchmarkReportPackValidator.SlotMaxWords(Es, BenchmarkReportSlots.WhyItScored));
        Assert.Null(BenchmarkReportPackValidator.SlotMaxWords(Ib, BenchmarkReportSlots.WhatWorked));
        Assert.Equal(300, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.WhyItScored));
        Assert.Equal(150, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.WhatWorked));
        Assert.Equal(120, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.Limitations));
        Assert.Equal(70, BenchmarkReportPackValidator.SlotMaxWords(Es, BenchmarkReportSlots.Comparison));
        Assert.Equal(200, BenchmarkReportPackValidator.SlotMaxWords(Ib, BenchmarkReportSlots.OverseerChat));
        Assert.Equal(150, BenchmarkReportPackValidator.SlotMaxWords(Ib, BenchmarkReportSlots.BenchmarkSystem));
        Assert.Equal(150, BenchmarkReportPackValidator.SlotMaxWords(Ib, BenchmarkReportSlots.ModelResult));
        Assert.Null(BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.OverseerChat));
    }

    [Theory]
    [InlineData(BenchmarkReportSlots.OverseerChat, 200)]
    [InlineData(BenchmarkReportSlots.BenchmarkSystem, 150)]
    [InlineData(BenchmarkReportSlots.ModelResult, 150)]
    public void Rule7_InternalBriefSlotWordLimits_AskForTheRepairTurn_AndDropTheLastParagraphs(string slot, int limit)
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("check", limit));
        Assert.Empty(Validate(Ib, output));

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("check", limit + 1));
        var note = Assert.Single(Validate(Ib, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections." + slot, note.Location);
        Assert.Contains(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), note.Message);

        // Four words, then enough to reach the cap exactly, then four more: only the last paragraph goes.
        string kept = "Check the first thing.\n\n" + string.Join(" ", Enumerable.Repeat("check", limit - 4));
        output.Sections[slot] = kept + "\n\nCheck the last thing.";
        var cleaned = Drop(Ib, output);
        Assert.False(cleaned.Fatal);
        Assert.Equal(kept, cleaned.Output.Sections[slot]);
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "sections." + slot + "[p3]");
    }

    [Fact]
    public void Rule7_TheInternalBriefHoldsEightRecommendationsAndSixLeads()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        var recommendation = output.Recommendations[0];
        var lead = output.Leads[0];
        for (int i = 0; i < 7; i++) output.Recommendations.Add(recommendation);
        for (int i = 0; i < 5; i++) output.Leads.Add(lead);
        Assert.Empty(Validate(Ib, output));

        output.Recommendations.Add(recommendation);
        output.Leads.Add(lead);
        var notes = Validate(Ib, output);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Rule == 7 && n.Location == "recommendations[8]");
        Assert.Contains(notes, n => n.Rule == 7 && n.Location == "leads[6]");

        var cleaned = Drop(Ib, output);
        Assert.Equal(8, cleaned.Output.Recommendations.Count);
        Assert.Equal(6, cleaned.Output.Leads.Count);
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "recommendations[8]");
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "leads[6]");
        Assert.Equal(8, BenchmarkReportPackValidator.MaxRecommendations(Ib));
        Assert.Equal(6, BenchmarkReportPackValidator.MaxLeads);
    }

    [Fact]
    public void Rule7_ExecutiveSummaryItemWordLimit()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);

        output.Strengths[0].Text = string.Join(" ", Enumerable.Repeat("lore", 30));
        Assert.Empty(Validate(Es, output));

        output.Strengths[0].Text = string.Join(" ", Enumerable.Repeat("lore", 31));
        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("strengths[0]", note.Location);
    }

    [Fact]
    public void Rule7_TheResearcherReportItemsHaveNoThirtyWordCap()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Strengths[0].Text = string.Join(" ", Enumerable.Repeat("lore", 31));

        Assert.Empty(Validate(Tr, output));
    }

    [Fact]
    public void Rule7_TheResearcherReportHoldsSixRecommendations()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        var recommendation = output.Recommendations[0];
        for (int i = 0; i < 5; i++) output.Recommendations.Add(recommendation);
        Assert.Empty(Validate(Tr, output));

        output.Recommendations.Add(recommendation);
        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("recommendations[6]", note.Location);
    }

    // Rule 8 ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("# Result\nIt answered well.")]
    [InlineData("It answered well.\n  ## Details")]
    [InlineData("Result\n===")]
    [InlineData("| Model | Quality |\n|---|---|\n| one | two |")]
    [InlineData("Model | Quality\n---|---")]
    [InlineData("It answered <b>well</b>.")]
    [InlineData("It answered well.<br/>Mostly.")]
    [InlineData("It answered well. <!-- hidden -->")]
    public void Rule8_HeadingsTablesAndHtml(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 8 && n.Location == MeaningP1);
    }

    [Theory]
    [InlineData("Bullet lists are fine:\n- one point\n- another point")]
    [InlineData("It is **clear** and *brief*, and a < b comparisons read as prose.")]
    [InlineData("The #hashtag style is not a heading.")]
    public void Rule8_PlainMarkdownPasses(string text)
    {
        Assert.Empty(ValidateMeaning(text));
    }

    // Rule 9 ------------------------------------------------------------------------------------

    [Fact]
    public void Rule9_EightSharedWordsFire()
    {
        var notes = ValidateMeaning("It explained what happens when you dip a long sword poorly.");

        var note = Assert.Single(notes);
        Assert.Equal(9, note.Rule);
        Assert.Contains("what happens when you dip a long sword", note.Message);
        Assert.Contains("question text of Q1", note.Message);
    }

    [Fact]
    public void Rule9_SevenSharedWordsPass()
    {
        Assert.Empty(ValidateMeaning("It explained what happens when you dip a long blade poorly."));
    }

    [Theory]
    [InlineData("WHAT happens, when -- you dip; a Long SWORD!")]
    [InlineData("What happens when {{subject}} you dip a long sword")]
    public void Rule9_IgnoresCasePunctuationAndTokens(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 9);
    }

    [Theory]
    [InlineData("A lawful character may receive Excalibur from the Lady.", "rubric of Q1")]
    [InlineData("It misses that the ring on the board is already known.", "grader comment on Q2")]
    [InlineData("It says dipping a long sword into a fountain can grant it.", "answer excerpt of Q1")]
    [InlineData("It knew dropping items on an altar shows a black flash.", "verifier claim on Q2")]
    public void Rule9_EveryContentFieldIsProtected(string text, string source)
    {
        var note = Assert.Single(ValidateMeaning(text));
        Assert.Equal(9, note.Rule);
        Assert.Contains(source, note.Message);
    }

    [Fact]
    public void Rule9_AppliesToItemsTopicsAndNotes()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.QuestionNotes[0].Note = "Should the player pray right now given the board state";

        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(9, note.Rule);
        Assert.Equal("questionNotes[0]", note.Location);
    }

    // Rule 10 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("It beat GPT-5.2 on lore.")]
    [InlineData("It beat the OpenAI model on lore.")]
    [InlineData("It beat gemini-3.8-flash on lore.")]
    [InlineData("It beat the google entry on lore.")]
    public void Rule10_PeerNames(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 10 && n.Location == MeaningP1);
    }

    [Theory]
    [InlineData("As an Anthropic model it answered well.")]
    [InlineData("Claude Opus 5.5 answered well.")]
    [InlineData("It visited the Googleplex once.")]
    public void Rule10_SubjectNamesAndPartialWordsPass(string text)
    {
        Assert.Empty(ValidateMeaning(text));
    }

    [Fact]
    public void Rule10_ShortNamesAreNotChecked()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Peers[0].Label = "Ox";

        Assert.Empty(ValidateMeaning("Ox carts are slow.", sheet));
    }

    [Fact]
    public void Rule10_PeerNameInsideTheSubjectsNamePasses()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.SubjectDisplayName = "GPT-5.2 Pro";
        sheet.KnownNames.Add("GPT-5.2 Pro");

        Assert.Empty(ValidateMeaning("GPT-5.2 Pro answered well.", sheet));
        Assert.Contains(ValidateMeaning("GPT-5.2 answered well.", sheet), n => n.Rule == 10);
    }

    // Rule 11 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("It was significantly better.")]
    [InlineData("The gap is Significant.")]
    [InlineData("It is statistically ahead.")]
    [InlineData("It is reliably better than {{peer:A}}.")]
    [InlineData("It is reliably  worse on tools.")]
    [InlineData("It clearly outperforms {{peer:B}}.")]
    public void Rule11_SignificanceClaims(string text)
    {
        var notes = ValidateMeaning(text);

        Assert.Contains(notes, n => n.Rule == 11 && n.Location == MeaningP1);
    }

    [Theory]
    [InlineData("The gap is insignificant in practice.")]
    [InlineData("No significance test was run.")]
    [InlineData("It answered reliably and clearly.")]
    [InlineData("A statistical test was not part of the comparison.")]
    public void Rule11_WholeWordsOnly(string text)
    {
        Assert.Empty(ValidateMeaning(text));
    }

    // Rule 12 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Its behaviour was sound.", "behaviour")]
    [InlineData("It analysed the board well.", "analysed")]
    [InlineData("The Grey dragon answer was right.", "Grey")]
    [InlineData("Its judgement on prayer was cautious.", "judgement")]
    public void Rule12_BritishSpellings(string text, string word)
    {
        var note = Assert.Single(ValidateMeaning(text));

        Assert.Equal(BenchmarkReportPackValidator.UsSpellingRule, note.Rule);
        Assert.Equal(12, note.Rule);
        Assert.Equal(MeaningP1, note.Location);
        Assert.Contains(word, note.Message);
    }

    [Theory]
    [InlineData("Its behavior was sound, and it analyzed the gray dragon's color.")]
    [InlineData("The greyhound and the recentred word are not whole-word matches.")]
    public void Rule12_UsEnglishAndPartialWordsPass(string text)
    {
        Assert.Empty(ValidateMeaning(text));
    }

    [Fact]
    public void Rule12_AppliesToItems()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Weaknesses[0].Text = "Overlooked the colour of cursed items on the board.";

        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(12, note.Rule);
        Assert.Equal("weaknesses[0]", note.Location);
    }

    // DropInvalid -------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
    public void Drop_ValidOutputIsCopiedUnchanged(BenchmarkReportAudience audience)
    {
        var output = ReportPackWriterTestData.ValidOutput(audience);

        var result = Drop(audience, output);

        Assert.False(result.Fatal);
        Assert.Null(result.FatalReason);
        Assert.Empty(result.Notes);
        Assert.NotSame(output, result.Output);
        Assert.NotSame(output.Strengths, result.Output.Strengths);
        Assert.Equal(JsonSerializer.Serialize(output), JsonSerializer.Serialize(result.Output));
    }

    [Fact]
    public void Drop_RemovesOnlyTheOffendingParagraph()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Meaning] = "A player gets sound advice.\n\nIt scored 80 points.\n\nBoard reading needs care.";
        string before = JsonSerializer.Serialize(output);

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Equal("A player gets sound advice.\n\nBoard reading needs care.", result.Output.Sections[BenchmarkReportSlots.Meaning]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(3, note.Rule);
        Assert.Equal("sections.meaning[p2]", note.Location);
        Assert.True(note.Dropped);
        Assert.Equal(before, JsonSerializer.Serialize(output));
    }

    [Fact]
    public void Drop_RemovesOnlyTheOffendingItems()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Strengths.Add(new BenchmarkReportWriterItem { Text = "Handled tools well.", Evidence = { "R99" } });
        output.Strengths.Add(new BenchmarkReportWriterItem { Text = "Wrote clearly.", Questions = { 4 }, Evidence = { "R4" } });
        output.QuestionNotes.Add(new BenchmarkReportQuestionNote { Question = 2, Note = "A second note." });
        string before = JsonSerializer.Serialize(output);

        var result = Drop(Tr, output);

        Assert.False(result.Fatal);
        Assert.Equal(new[] { "Explained item lore accurately.", "Wrote clearly." }, result.Output.Strengths.Select(s => s.Text));
        Assert.Equal(2, result.Output.QuestionNotes.Count);
        Assert.Contains(result.Notes, n => n.Rule == 5 && n.Location == "strengths[1]" && n.Dropped);
        Assert.Contains(result.Notes, n => n.Rule == 4 && n.Location == "questionNotes[2]" && n.Dropped);
        Assert.Equal(before, JsonSerializer.Serialize(output));
        Assert.Equal(3, output.Strengths.Count);
    }

    [Fact]
    public void Drop_TrimsListsToTheirLimit()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        for (int i = 0; i < 4; i++)
        {
            output.Weaknesses.Add(new BenchmarkReportWriterItem { Text = "Missed board state.", Evidence = { "R2" } });
        }

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Equal(3, result.Output.Weaknesses.Count);
        Assert.Equal(new[] { "weaknesses[3]", "weaknesses[4]" }, result.Notes.Where(n => n.Rule == 7 && n.Dropped).Select(n => n.Location));
    }

    [Fact]
    public void Drop_RemovesListsAndSlotsTheAudienceDoesNotUse()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation { For = "model_developers", Text = "Improve board reading." });
        output.Sections[BenchmarkReportSlots.Abstract] = "A short summary.";

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Empty(result.Output.Recommendations);
        Assert.False(result.Output.Sections.ContainsKey(BenchmarkReportSlots.Abstract));
        Assert.Contains(result.Notes, n => n.Rule == 1 && n.Location == "recommendations" && n.Dropped);
        Assert.Contains(result.Notes, n => n.Rule == 1 && n.Location == "sections.abstract" && n.Dropped);
        Assert.Single(output.Recommendations);
    }

    [Fact]
    public void Drop_TrimsAnOverlongAbstractByParagraph()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        string first = string.Join(" ", Enumerable.Repeat("result", 100));
        string second = string.Join(" ", Enumerable.Repeat("outcome", 100));
        output.Sections[BenchmarkReportSlots.Abstract] = first + "\n\n" + second;

        var result = Drop(Tr, output);

        Assert.False(result.Fatal);
        Assert.Equal(first, result.Output.Sections[BenchmarkReportSlots.Abstract]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections.abstract[p2]", note.Location);
        Assert.True(note.Dropped);
    }

    [Fact]
    public void Drop_InvalidHeadlineIsFatal()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Headline = "It scored 80 points.";

        var result = Drop(Es, output);

        Assert.True(result.Fatal);
        Assert.False(string.IsNullOrWhiteSpace(result.FatalReason));
        Assert.Contains(result.Notes, n => n.Rule == 3 && n.Location == "headline" && !n.Dropped);
    }

    [Fact]
    public void Drop_RequiredSlotEmptiedIsFatal()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Meaning] = "It scored 80 points.\n\nIt clearly outperforms {{peer:A}}.";

        var result = Drop(Es, output);

        Assert.True(result.Fatal);
        Assert.Contains("meaning", result.FatalReason);
        Assert.Contains(result.Notes, n => n.Location == "sections.meaning[p1]" && n.Dropped);
        Assert.Contains(result.Notes, n => n.Location == "sections.meaning[p2]" && n.Dropped);
        Assert.Contains(result.Notes, n => n.Rule == 1 && n.Location == "sections.meaning" && !n.Dropped);
    }

    [Fact]
    public void Drop_MissingRequiredSlotIsFatal()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Sections.Remove(BenchmarkReportSlots.ModelResult);

        var result = Drop(Ib, output);

        Assert.True(result.Fatal);
        Assert.Contains(result.Notes, n => n.Rule == 1 && n.Location == "sections.modelResult" && !n.Dropped);
    }

    [Fact]
    public void Drop_KeepsABritishSpelling_AndRecordsItsNote()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Headline = "{{subject}} showed sound behaviour on item lore at {{quality.index}}.";
        output.Strengths[0].Text = "Explained the colour of item lore accurately.";

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Equal(output.Headline, result.Output.Headline);
        Assert.Equal("Explained the colour of item lore accurately.", Assert.Single(result.Output.Strengths).Text);
        Assert.Contains(result.Notes, n => n.Rule == 12 && n.Location == "headline" && !n.Dropped);
        Assert.Contains(result.Notes, n => n.Rule == 12 && n.Location == "strengths[0]" && !n.Dropped);
        Assert.DoesNotContain(result.Notes, n => n.Dropped);
    }

    // Rule 13 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("The interval is narrow, and the graders mostly agreed.", "narrow")]
    [InlineData("A Wide interval leaves the order open.", "Wide")]
    [InlineData("Its interval is tighter than most.", "tighter")]
    [InlineData("The broad interval overlaps that of {{peer:A}}.", "broad")]
    public void Rule13_IntervalWidthAdjectives_InTheConfidenceSlot(string text, string word)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Confidence] = text;

        var note = Assert.Single(Validate(Es, output));

        Assert.Equal(BenchmarkReportPackValidator.IntervalWidthRule, note.Rule);
        Assert.Equal(13, note.Rule);
        Assert.Equal("sections.confidence[p1]", note.Location);
        Assert.Contains(word, note.Message);
        Assert.Contains("the sentence appended after this paragraph states the interval and its span", note.Message);
        Assert.DoesNotContain("{{quality.intervalSpan}}", note.Message);
    }

    [Fact]
    public void Rule13_AppliesOnlyToTheExecutiveSummarysConfidenceSlot_AndToWholeWords()
    {
        Assert.Empty(ValidateMeaning("A narrow lead on item lore and a wide range of topics."));

        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Confidence] = "The graders agreed broadly, and the answers were widely accepted.";
        Assert.Empty(Validate(Es, output));

        var technical = ReportPackWriterTestData.ValidOutput(Tr);
        technical.Sections[BenchmarkReportSlots.WhyItScored] = "A narrow reading of the board was the main weakness, most visibly on Q2.";
        Assert.Empty(Validate(Tr, technical));
    }

    [Fact]
    public void Drop_KeepsAnIntervalWidthAdjective_AndRecordsItsNote()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        const string confidence = "The interval is narrow, and the graders mostly agreed.";
        output.Sections[BenchmarkReportSlots.Confidence] = confidence;

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Equal(confidence, result.Output.Sections[BenchmarkReportSlots.Confidence]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(BenchmarkReportPackValidator.IntervalWidthRule, note.Rule);
        Assert.False(note.Dropped);
    }

    // Rule 14 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("{{subject}} answered well, but the claim verifier refuted two of its claims.", "verifier")]
    [InlineData("{{subject}} scored {{quality.index}}; the Verifier found errors.", "Verifier")]
    [InlineData("{{subject}} scored {{quality.index}}, and both verifiers disagreed.", "verifiers")]
    public void Rule14_TheHeadlineNeverMentionsTheClaimVerifier(string headline, string word)
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Headline = headline;

        var note = Assert.Single(Validate(Es, output));

        Assert.Equal(BenchmarkReportPackValidator.VerifierInSummaryRule, note.Rule);
        Assert.Equal(14, note.Rule);
        Assert.Equal("headline", note.Location);
        Assert.Contains("\"" + word + "\"", note.Message);
        Assert.Contains("attributed to the claim verifier", note.Message);
    }

    [Fact]
    public void Rule14_TheAbstractNeverMentionsTheClaimVerifier_ButOtherSlotsMay()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Sections[BenchmarkReportSlots.Abstract] = "{{subject}} scored {{quality.index}} on the benchmark."
            + "\n\nThe claim verifier judged one answer sentence false on Q3.";

        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(BenchmarkReportPackValidator.VerifierInSummaryRule, note.Rule);
        Assert.Equal("sections.abstract[p2]", note.Location);

        var elsewhere = ReportPackWriterTestData.ValidOutput(Tr);
        elsewhere.Sections[BenchmarkReportSlots.WhyItScored] = "The claim verifier judged one answer sentence false on Q3.";
        Assert.Empty(Validate(Tr, elsewhere));

        // A word merely containing the letters is not the verifier.
        var unrelated = ReportPackWriterTestData.ValidOutput(Es);
        unrelated.Headline = "{{subject}} gave verifiable answers across the benchmark.";
        Assert.Empty(Validate(Es, unrelated));
    }

    [Fact]
    public void Drop_KeepsAMentionOfTheVerifier_AndRecordsItsNote()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Headline = "{{subject}} answered well, but the claim verifier refuted two of its claims.";
        output.Sections[BenchmarkReportSlots.Abstract] = "The claim verifier judged one answer sentence false on Q3.";

        var result = Drop(Tr, output);

        Assert.False(result.Fatal);
        Assert.Equal(output.Headline, result.Output.Headline);
        Assert.Equal("The claim verifier judged one answer sentence false on Q3.", result.Output.Sections[BenchmarkReportSlots.Abstract]);
        Assert.Contains(result.Notes, n => n.Rule == 14 && n.Location == "headline" && !n.Dropped);
        Assert.Contains(result.Notes, n => n.Rule == 14 && n.Location == "sections.abstract[p1]" && !n.Dropped);
        Assert.DoesNotContain(result.Notes, n => n.Dropped);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(14));
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(12));
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(13));
        Assert.False(BenchmarkReportPackValidator.IsWarningRule(9));
    }

    [Fact]
    public void Drop_TrimsAnOverlongMeaningByParagraph()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        string first = string.Join(" ", Enumerable.Repeat("advice", 60));
        string second = string.Join(" ", Enumerable.Repeat("guidance", 40));
        output.Sections[BenchmarkReportSlots.Meaning] = first + "\n\n" + second;

        var result = Drop(Es, output);

        Assert.False(result.Fatal);
        Assert.Equal(first, result.Output.Sections[BenchmarkReportSlots.Meaning]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections.meaning[p2]", note.Location);
        Assert.True(note.Dropped);
    }

    [Fact]
    public void Drop_MissingTopicIsRecordedButNotFatal()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.QuestionTopics[3].Topic = "zapping the 2nd wand";

        var result = Drop(Tr, output);

        Assert.False(result.Fatal);
        Assert.Equal(3, result.Output.QuestionTopics.Count);
        Assert.Contains(result.Notes, n => n.Rule == 3 && n.Location == "questionTopics[3]" && n.Dropped);
        var coverage = Assert.Single(result.Notes, n => n.Location == "questionTopics");
        Assert.Equal(4, coverage.Rule);
        Assert.False(coverage.Dropped);
        Assert.Contains("Q4", coverage.Message);
    }

    // The comparison slot and the stand-alone form ---------------------------------------------

    /// <summary><see cref="ReportPackWriterTestData.Sheet"/> with no peers, as a run-completion document's.</summary>
    private static BenchmarkReportFactSheet StandaloneSheet()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Peers.Clear();
        foreach (var q in sheet.Questions)
        {
            q.PeerMean = null;
            q.Difference = null;
        }
        return sheet;
    }

    /// <summary>The Executive Summary's valid output without its comparison slot or any peer token.</summary>
    private static BenchmarkReportWriterOutput StandaloneExecutiveOutput()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections.Remove(BenchmarkReportSlots.Comparison);
        output.Sections[BenchmarkReportSlots.Confidence] = "The quality result is {{quality.index}}, and the graders mostly agreed.";
        return output;
    }

    [Fact]
    public void TheComparisonSlot_IsRequiredWithPeers()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections.Remove(BenchmarkReportSlots.Comparison);

        var note = Assert.Single(Validate(Es, output));
        Assert.Equal(1, note.Rule);
        Assert.Equal("sections.comparison", note.Location);

        var result = Drop(Es, output);
        Assert.True(result.Fatal);
        Assert.Contains("comparison", result.FatalReason);
    }

    [Fact]
    public void AStandaloneExecutiveSummary_NeedsNoComparisonSlot_AndHasNone()
    {
        var sheet = StandaloneSheet();

        Assert.Empty(BenchmarkReportPackValidator.Validate(Es, StandaloneExecutiveOutput(), sheet, ReportPackWriterTestData.Content()));

        var withComparison = StandaloneExecutiveOutput();
        withComparison.Sections[BenchmarkReportSlots.Comparison] = "{{subject}} stands alone here.";
        var note = Assert.Single(BenchmarkReportPackValidator.Validate(Es, withComparison, sheet, ReportPackWriterTestData.Content()));
        Assert.Equal(1, note.Rule);
        Assert.Equal("sections.comparison", note.Location);
        Assert.Contains("stand-alone", note.Message);

        var result = BenchmarkReportPackValidator.DropInvalid(Es, withComparison, sheet, ReportPackWriterTestData.Content());
        Assert.False(result.Fatal);
        Assert.False(result.Output.Sections.ContainsKey(BenchmarkReportSlots.Comparison));
        Assert.Contains(result.Notes, n => n.Rule == 1 && n.Location == "sections.comparison" && n.Dropped);

        Assert.Equal(new[] { BenchmarkReportSlots.Meaning, BenchmarkReportSlots.Confidence }, BenchmarkReportSlots.ExecutiveSummary.SlotsFor(hasPeers: false));
        Assert.Equal(
            new[] { BenchmarkReportSlots.Comparison, BenchmarkReportSlots.Meaning, BenchmarkReportSlots.Confidence },
            BenchmarkReportSlots.ExecutiveSummary.SlotsFor(hasPeers: true));
    }

    [Fact]
    public void AStandaloneInternalBrief_WithLeadsAndRecommendations_HasNoIssues()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Sections[BenchmarkReportSlots.ModelResult] = "{{subject}} scored {{quality.index}}, held back by how it read the board on Q2.";
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetBenchmark,
            Text = "Review whether the rubric for Q3 is ambiguous; the graders disagree.",
            Evidence = { "R3" }
        });
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetModelDevelopers,
            Text = "Check the board state before answering questions about item status.",
            Evidence = { "R2" }
        });

        Assert.NotEmpty(output.Leads);
        Assert.Empty(Validate(Ib, output, StandaloneSheet()));
        Assert.Equal(BenchmarkReportSlots.InternalBrief.RequiredSlots, BenchmarkReportSlots.InternalBrief.SlotsFor(hasPeers: false));
    }

    [Fact]
    public void TheLimitationsSlot_IsRequiredInBothForms()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Sections.Remove(BenchmarkReportSlots.Limitations);
        Assert.Contains(Validate(Tr, output), n => n.Rule == 1 && n.Location == "sections.limitations");
        Assert.Contains(Validate(Tr, output, StandaloneSheet()), n => n.Rule == 1 && n.Location == "sections.limitations");

        Assert.Contains(BenchmarkReportSlots.Limitations, BenchmarkReportSlots.TechnicalReport.SlotsFor(hasPeers: false));
        Assert.Contains(BenchmarkReportSlots.Limitations, BenchmarkReportSlots.TechnicalReport.SlotsFor(hasPeers: true));
    }

    // Rule 15 -----------------------------------------------------------------------------------

    [Fact]
    public void Rule15_EveryQuestionNeedingANote_HasOne()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.QuestionNotes.RemoveAll(n => n.Question == 3);

        var note = Assert.Single(Validate(Tr, output));

        Assert.Equal(BenchmarkReportPackValidator.MissingQuestionNoteRule, note.Rule);
        Assert.Equal(15, note.Rule);
        Assert.Equal("questionNotes", note.Location);
        Assert.Contains("Q3", note.Message);
        Assert.DoesNotContain("Q2", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(15));
        Assert.Contains("- rule 15 at questionNotes:", BenchmarkReportPackPrompt.BuildRepairMessage(new[] { note }));
    }

    [Fact]
    public void Rule15_ANoteDroppedByAnotherRule_LeavesItsQuestionWithoutOne_WhichIsRecordedButNotFatal()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.QuestionNotes[1].Note = "Should the player pray right now given the board state";

        var result = Drop(Ib, output);

        Assert.False(result.Fatal);
        Assert.Single(result.Output.QuestionNotes);
        Assert.Contains(result.Notes, n => n.Rule == 9 && n.Location == "questionNotes[1]" && n.Dropped);
        var missing = Assert.Single(result.Notes, n => n.Rule == 15);
        Assert.False(missing.Dropped);
        Assert.Contains("Q3", missing.Message);
    }

    [Fact]
    public void Rule15_DoesNotApplyWhereTheDocumentHasNoNotes()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);

        Assert.DoesNotContain(Validate(Es, output), n => n.Rule == 15);
    }

    // Rule 16 -----------------------------------------------------------------------------------

    /// <summary>The writer test sheet with Model A's interval overlapping the subject's and Model B's not.</summary>
    private static BenchmarkReportFactSheet OverlapSheet()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.A.intervalOverlap", Value = JsonValue.Create(true), Display = "its 95 % interval overlaps the subject's" });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.B.intervalOverlap", Value = JsonValue.Create(false), Display = "its 95 % interval does not overlap the subject's" });
        return sheet;
    }

    [Theory]
    [InlineData("{{subject}} scored higher than {{peer:A}} on item lore.")]
    [InlineData("{{subject}} scored {{quality.index}}, better than {{peer:A}}.")]
    [InlineData("{{peer:A}} OUTPERFORMED {{subject}} on board reading.")]
    [InlineData("{{subject}} trails {{peer:A}} and {{peer:B}} on tool use.")]
    public void Rule16_ARankingAgainstAnOverlappingPeer_MustSaySo(string text)
    {
        var note = Assert.Single(ValidateMeaning(text, OverlapSheet()));

        Assert.Equal(BenchmarkReportPackValidator.OverlapHedgeRule, note.Rule);
        Assert.Equal(16, note.Rule);
        Assert.Equal(MeaningP1, note.Location);
        Assert.Contains("{{peer:A}}", note.Message);
        Assert.DoesNotContain("{{peer:B}}", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(16));
    }

    [Theory]
    [InlineData("{{subject}} scored higher than {{peer:A}}, but their intervals overlap.")]
    [InlineData("{{subject}} is ahead of {{peer:A}}, though the order is not established.")]
    [InlineData("{{subject}} scored higher than {{peer:B}} on item lore.")]
    [InlineData("{{subject}} answered well. It scored higher than {{peer:A}}.")]
    [InlineData("{{subject}} sits close to {{peer:A}} on quality.")]
    [InlineData("It scored higher than {{peer:A}} on item lore.")]
    public void Rule16_AHedgedSentence_ANonOverlappingPeer_OrNoRankingPasses(string text)
    {
        Assert.Empty(ValidateMeaning(text, OverlapSheet()));
    }

    [Fact]
    public void Rule16_WithoutOverlapFacts_NeverFires()
    {
        Assert.Empty(ValidateMeaning("{{subject}} scored higher than {{peer:A}} on item lore."));
    }

    [Fact]
    public void Rule16_KeepsTheText_AndRecordsItsNote()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        const string meaning = "{{subject}} scored higher than {{peer:A}} on item lore.";
        output.Sections[BenchmarkReportSlots.Meaning] = meaning;

        var result = BenchmarkReportPackValidator.DropInvalid(Es, output, OverlapSheet(), ReportPackWriterTestData.Content());

        Assert.False(result.Fatal);
        Assert.Equal(meaning, result.Output.Sections[BenchmarkReportSlots.Meaning]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(16, note.Rule);
        Assert.False(note.Dropped);
    }

    // Rule 17 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("{{subject}} gave impressive answers on item lore.", "impressive")]
    [InlineData("Its Remarkable grasp of prayer rules showed.", "Remarkable")]
    [InlineData("It gave robust advice on the board.", "robust")]
    [InlineData("It used cutting-edge reasoning on the board.", "cutting-edge")]
    [InlineData("Its Game-Changing tool use stood out.", "Game-Changing")]
    [InlineData("It did not delve into the board state.", "delve")]
    [InlineData("Speed and cost are settled on these questions.", "settled")]
    [InlineData("The order is definitively proven.", "definitively")]
    public void Rule17_HypeWords(string text, string word)
    {
        var note = Assert.Single(ValidateMeaning(text));

        Assert.Equal(BenchmarkReportPackValidator.HypeWordRule, note.Rule);
        Assert.Equal(17, note.Rule);
        Assert.Equal(MeaningP1, note.Location);
        Assert.Contains("\"" + word + "\"", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(17));
    }

    [Theory]
    [InlineData("Its robustness under pressure showed on the board.")]
    [InlineData("It stayed at the edge of the board and leveraged nothing.")]
    [InlineData("It answered impressively fast.")]
    public void Rule17_WholeWordsOnly(string text)
    {
        Assert.Empty(ValidateMeaning(text));
    }

    [Fact]
    public void Rule17_AppliesToItems_AndKeepsTheText()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Weaknesses[0].Text = "Overlooked the board, despite outstanding lore elsewhere.";

        var note = Assert.Single(Validate(Tr, output));
        Assert.Equal(17, note.Rule);
        Assert.Equal("weaknesses[0]", note.Location);

        var result = Drop(Tr, output);
        Assert.Equal("Overlooked the board, despite outstanding lore elsewhere.", result.Output.Weaknesses[0].Text);
        Assert.DoesNotContain(result.Notes, n => n.Dropped);
    }

    // Rule 16 with a paired interval that excludes zero ------------------------------------------

    /// <summary><see cref="OverlapSheet"/> with Model A's paired interval excluding zero and Model B's including it.</summary>
    private static BenchmarkReportFactSheet PairedSheet()
    {
        var sheet = OverlapSheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.A.pairedExcludesZero", Value = JsonValue.Create(true), Display = BenchmarkReportFacts.PairedExcludesZeroDisplay });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.B.pairedExcludesZero", Value = JsonValue.Create(false), Display = BenchmarkReportFacts.PairedIncludesZeroDisplay });
        return sheet;
    }

    [Theory]
    [InlineData("On the same questions {{peer:A}} scored higher than {{subject}} on average, and the paired interval excludes zero.")]
    [InlineData("{{subject}} scored lower than {{peer:A}}; {{peer.A.pairedExcludesZero}}, not adjusted for comparing several models.")]
    [InlineData("{{subject}} trails {{peer:A}} on the same questions, a paired interval excluding zero.")]
    public void Rule16_APairedHedge_PassesForAPeerWhosePairedIntervalExcludesZero(string text)
    {
        Assert.Empty(ValidateMeaning(text, PairedSheet()));
    }

    [Fact]
    public void Rule16_APairedHedge_DoesNotCoverAPeerWhosePairedIntervalIncludesZero()
    {
        var sheet = PairedSheet();
        sheet.Facts.RemoveAll(f => f.Key == "peer.B.intervalOverlap");
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.B.intervalOverlap", Value = JsonValue.Create(true), Display = "its 95 % interval overlaps the subject's" });

        var note = Assert.Single(ValidateMeaning("{{subject}} scored higher than {{peer:B}}, and the paired interval excludes zero.", sheet));

        Assert.Equal(BenchmarkReportPackValidator.OverlapHedgeRule, note.Rule);
        Assert.Contains("{{peer:B}}", note.Message);
        Assert.Contains("order between them is not established", note.Message);
    }

    [Fact]
    public void Rule16_AnUnhedgedRankingOfAPairedPeer_AsksForThePairedResult()
    {
        var note = Assert.Single(ValidateMeaning("{{subject}} scored lower than {{peer:A}} on item lore.", PairedSheet()));

        Assert.Equal(16, note.Rule);
        Assert.Contains("{{peer:A}}", note.Message);
        Assert.Contains("the paired interval excludes zero", note.Message);
        Assert.DoesNotContain("not established", note.Message);
    }

    [Fact]
    public void Rule16_TheOverlapHedgeStillPassesForAPairedPeer()
    {
        Assert.Empty(ValidateMeaning("{{subject}} scored lower than {{peer:A}}, and their intervals overlap.", PairedSheet()));
    }

    // Rule 18 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Use a rubric-coverage pass before answering.", "rubric")]
    [InlineData("Add GnollHack-specific retrieval prioritization.", "retrieval")]
    [InlineData("Add regression tests for branch layout and temple services.", "regression tests")]
    [InlineData("Ground answers in the corpus before stating a rule.", "corpus")]
    [InlineData("Follow the System Prompt more closely.", "system prompt")]
    [InlineData("Read the tool guides before calling a tool.", "tool guides")]
    [InlineData("Rebuild the search index for version-specific code.", "index")]
    public void Rule18_AModelDeveloperRecommendation_AboutTheOverseer_AsksForRepair(string text, string term)
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Text = text;

        var note = Assert.Single(Validate(Tr, output));

        Assert.Equal(BenchmarkReportPackValidator.ModelDeveloperScopeRule, note.Rule);
        Assert.Equal(18, note.Rule);
        Assert.Equal("recommendations[0]", note.Location);
        Assert.Contains("\"" + term + "\"", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(18));
    }

    [Fact]
    public void Rule18_KeepsTheText_AndRecordsItsNote()
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Text = "Use a rubric-coverage pass before answering.";

        var result = Drop(Tr, output);

        Assert.False(result.Fatal);
        Assert.Equal("Use a rubric-coverage pass before answering.", Assert.Single(result.Output.Recommendations).Text);
        var note = Assert.Single(result.Notes);
        Assert.Equal(18, note.Rule);
        Assert.False(note.Dropped);
    }

    [Fact]
    public void Rule18_DoesNotApplyToOtherTargets()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Recommendations[0].Text = "Add regression tests and a retrieval check to the system prompt's rubric coverage.";
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetBenchmark,
            Text = "Review the rubric of Q3.",
            Evidence = { "R3" }
        });

        Assert.Equal(BenchmarkReportSlots.TargetOverseerChat, output.Recommendations[0].For);
        Assert.Empty(Validate(Ib, output));
    }

    [Fact]
    public void Rule18_AppliesToModelDevelopersInTheInternalBrief_AndIgnoresTokens()
    {
        var output = ReportPackWriterTestData.ValidOutput(Ib);
        output.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetModelDevelopers,
            Text = "Calibrate confidence on prayer rules; {{quality.index}} reflects the misses.",
            Evidence = { "quality.index" }
        });
        Assert.Empty(Validate(Ib, output));

        output.Recommendations[1].Text = "Calibrate confidence against the rubric.";
        var note = Assert.Single(Validate(Ib, output));
        Assert.Equal(18, note.Rule);
        Assert.Equal("recommendations[1]", note.Location);
    }

    [Theory]
    [InlineData("Check the board state before answering questions about item status.")]
    [InlineData("Keep answers shorter where the question asks for a single fact.")]
    [InlineData("Call a lookup tool before stating an item's weight.")]
    public void Rule18_ModelLevelRecommendationsPass(string text)
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Text = text;

        Assert.Empty(Validate(Tr, output));
    }

    [Theory]
    [InlineData("Prompt the model to name conduct-safe fallbacks such as prayer.", "prompt the model")]
    [InlineData("Strengthen knowledge of which NPC provides which service in GnollHack towns.", "GnollHack")]
    [InlineData("Add a rule to the assistant's prompt about shop prices.", "the assistant's prompt")]
    [InlineData("Add a rule to the assistant’s prompt about shop prices.", "the assistant's prompt")]
    [InlineData("Cover every rubric point before answering.", "rubric")]
    [InlineData("Read the system prompt more closely.", "system prompt")]
    public void Rule18_AGameFactOrAPromptChange_ForModelDevelopers_AsksForRepair(string text, string term)
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Text = text;

        var note = Assert.Single(Validate(Tr, output));

        Assert.Equal(BenchmarkReportPackValidator.ModelDeveloperScopeRule, note.Rule);
        Assert.Contains("\"" + term + "\"", note.Message);
        Assert.Contains("general capability a model developer can train or tune", note.Message);
    }

    [Theory]
    [InlineData("State the decisive mechanic behind a verdict before the verdict itself.")]
    [InlineData("Commit to a conclusion the inputs already settle instead of hedging.")]
    public void Rule18_AGeneralCapability_Passes(string text)
    {
        var output = ReportPackWriterTestData.ValidOutput(Tr);
        output.Recommendations[0].Text = text;

        Assert.Empty(Validate(Tr, output));
    }

    // Rule 19 -----------------------------------------------------------------------------------

    /// <summary>The writer test sheet with a critical-error count of zero and a scored-answer count.</summary>
    private static BenchmarkReportFactSheet ZeroCountSheet(string criticalDisplay = "0 of 18 answers")
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "answers.scored", Value = JsonValue.Create(18), Display = "18" });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "errors.critical", Value = JsonValue.Create(0), Display = criticalDisplay });
        sheet.Facts = sheet.Facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        return sheet;
    }

    [Theory]
    [InlineData("It made no critical errors across {{errors.critical}}.", "no")]
    [InlineData("No critical errors across {{errors.critical}}.", "no")]
    [InlineData("It never erred in {{errors.critical}}.", "never")]
    [InlineData("It answered without critical errors, {{errors.critical}}.", "without")]
    [InlineData("There were zero critical errors in {{errors.critical}}.", "zero")]
    [InlineData("None of the answers, {{errors.critical}}, erred.", "none")]
    public void Rule19_ANegationBeforeATokenReadingZero_AsksForRepair(string text, string negation)
    {
        var note = Assert.Single(ValidateMeaning(text, ZeroCountSheet()));

        Assert.Equal(BenchmarkReportPackValidator.ZeroTokenNegationRule, note.Rule);
        Assert.Equal(19, note.Rule);
        Assert.Equal(MeaningP1, note.Location);
        Assert.Contains("\"" + negation + "\" before {{errors.critical}}, which reads \"0 of 18 answers\"", note.Message);
        Assert.Contains("no critical errors across all {{answers.scored}} answers", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(19));
    }

    [Theory]
    // Five words between the negation and the token.
    [InlineData("No answer from the model in {{errors.critical}} was flagged.")]
    // The recommended wording.
    [InlineData("It made no critical errors across all {{answers.scored}} answers.")]
    [InlineData("{{errors.critical}} had a critical error.")]
    // The negation ends an earlier sentence.
    [InlineData("It made no mistakes. {{errors.critical}} had a critical error.")]
    public void Rule19_PassesAFarNegation_TheRecommendedWording_AndAnotherSentence(string text)
    {
        Assert.Empty(ValidateMeaning(text, ZeroCountSheet()));
    }

    [Fact]
    public void Rule19_IgnoresATokenWhoseValueDoesNotStartWithZero()
    {
        Assert.Empty(ValidateMeaning("It made no critical errors beyond {{errors.critical}}.", ZeroCountSheet("1 of 18 answers")));
        Assert.Empty(ValidateMeaning("It made no critical errors across {{answers.scored}} answers.", ZeroCountSheet()));
    }

    [Fact]
    public void Rule19_KeepsTheText_AndTriggersTheRepairTurn()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.Meaning] = "It made no critical errors across {{errors.critical}}.";

        var result = BenchmarkReportPackValidator.DropInvalid(Es, output, ZeroCountSheet(), ReportPackWriterTestData.Content());

        Assert.False(result.Fatal);
        Assert.Equal("It made no critical errors across {{errors.critical}}.", result.Output.Sections[BenchmarkReportSlots.Meaning]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(19, note.Rule);
        Assert.False(note.Dropped);

        // Any note asks for the repair turn; the repair message carries it.
        string repair = BenchmarkReportPackPrompt.BuildRepairMessage(Validate(Es, output, ZeroCountSheet()));
        Assert.Contains("- rule 19 at sections.meaning[p1]: Puts \"no\" before {{errors.critical}}", repair);
    }

    [Fact]
    public void TheWarningRules_AreTwelveToNineteen()
    {
        for (int rule = 1; rule <= 19; rule++)
        {
            Assert.Equal(rule >= 12, BenchmarkReportPackValidator.IsWarningRule(rule));
        }
        Assert.False(BenchmarkReportPackValidator.IsWarningRule(20));
    }
}

public class BenchmarkReportPackParserTests
{
    [Fact]
    public void Parse_PlainJson()
    {
        var result = BenchmarkReportPackParser.Parse(
            "{\"headline\":\"H\",\"sections\":{\"meaning\":\"M\"},\"strengths\":[{\"text\":\"S\",\"questions\":[1],\"evidence\":[\"R1\"]}]}");

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal("H", result.Output!.Headline);
        Assert.Equal("M", result.Output.Sections["meaning"]);
        var strength = Assert.Single(result.Output.Strengths);
        Assert.Equal("S", strength.Text);
        Assert.Equal(new[] { 1 }, strength.Questions);
        Assert.Equal(new[] { "R1" }, strength.Evidence);
        Assert.Empty(result.Output.Weaknesses);
        Assert.Empty(result.Output.Leads);
    }

    [Fact]
    public void Parse_FencedJson()
    {
        var result = BenchmarkReportPackParser.Parse("Here it is:\n```json\n{\"headline\":\"Fenced\"}\n```\nThanks.");

        Assert.True(result.Success);
        Assert.Equal("Fenced", result.Output!.Headline);
    }

    [Fact]
    public void Parse_StripsThoughts()
    {
        var result = BenchmarkReportPackParser.Parse(
            "<div class=\"ai-thought\">Maybe {\"headline\":\"wrong\"}</div>{\"headline\":\"right\"}");

        Assert.True(result.Success);
        Assert.Equal("right", result.Output!.Headline);
    }

    [Fact]
    public void Parse_TrailingProse()
    {
        var result = BenchmarkReportPackParser.Parse("{\"headline\":\"H\",\"sections\":{}} I hope this helps {with braces}.");

        Assert.True(result.Success);
        Assert.Equal("H", result.Output!.Headline);
    }

    [Theory]
    [InlineData("{\"headline\": \"x\", \"sections\": {")]
    [InlineData("No JSON here at all.")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("<div class=\"ai-thought\">only thinking</div>")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_FailuresAreResultsNotExceptions(string? raw)
    {
        var result = BenchmarkReportPackParser.Parse(raw);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_IsTolerantOfShape()
    {
        const string json = """
            {
              "Headline": "H",
              "SECTIONS": { "WhyItScored": "w", "custom": "c", "meaning": ["first", "second"] },
              "Strengths": [ { "Text": "t", "Questions": ["Q7", "3", 5], "Evidence": "R1, Q2" }, "bare text" ],
              "weaknesses": null,
              "recommendations": [ { "for": "Model_Developers", "text": "r" } ],
              "leads": [ { "triage": " Suite ", "text": "l", "questions": "Q1, Q2" } ],
              "questionTopics": [ { "question": "Q4", "topic": "x" }, { "question": "four", "topic": "y" } ],
              "questionNotes": [ { "question": 2.0, "note": "n" } ],
              "extra": { "ignored": true },
            }
            """;

        var result = BenchmarkReportPackParser.Parse(json);

        Assert.True(result.Success);
        var output = result.Output!;
        Assert.Equal("H", output.Headline);
        Assert.Equal("w", output.Sections[BenchmarkReportSlots.WhyItScored]);
        Assert.Equal("c", output.Sections["custom"]);
        Assert.Equal("first\n\nsecond", output.Sections[BenchmarkReportSlots.Meaning]);
        Assert.Equal(2, output.Strengths.Count);
        Assert.Equal(new[] { 7, 3, 5 }, output.Strengths[0].Questions);
        Assert.Equal(new[] { "R1", "Q2" }, output.Strengths[0].Evidence);
        Assert.Equal("bare text", output.Strengths[1].Text);
        Assert.NotNull(output.Weaknesses);
        Assert.Empty(output.Weaknesses);
        Assert.Equal("model_developers", Assert.Single(output.Recommendations).For);
        var lead = Assert.Single(output.Leads);
        Assert.Equal("suite", lead.Triage);
        Assert.Equal(new[] { 1, 2 }, lead.Questions);
        Assert.Equal(new[] { 4, 0 }, output.QuestionTopics.Select(t => t.Question));
        Assert.Equal(2, Assert.Single(output.QuestionNotes).Question);
    }

    [Fact]
    public void Parse_NormalizesEverySlotId_ComparisonAndLimitationsIncluded()
    {
        var result = BenchmarkReportPackParser.Parse(
            "{\"headline\":\"H\",\"sections\":{\"Comparison\":\"c\",\"LIMITATIONS\":\"l\",\"ModelResult\":\"m\"}}");

        Assert.True(result.Success);
        var sections = result.Output!.Sections;
        Assert.Equal("c", sections[BenchmarkReportSlots.Comparison]);
        Assert.Equal("l", sections[BenchmarkReportSlots.Limitations]);
        Assert.Equal("m", sections[BenchmarkReportSlots.ModelResult]);
        Assert.Equal(
            new[] { BenchmarkReportSlots.Comparison, BenchmarkReportSlots.Limitations, BenchmarkReportSlots.ModelResult }.OrderBy(k => k, StringComparer.Ordinal),
            sections.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Parse_OutputOfAValidDocumentValidates()
    {
        var expected = ReportPackWriterTestData.ValidOutput(BenchmarkReportAudience.InternalBrief);
        string json = JsonSerializer.Serialize(expected);

        var result = BenchmarkReportPackParser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal(json, JsonSerializer.Serialize(result.Output));
        Assert.Empty(BenchmarkReportPackValidator.Validate(
            BenchmarkReportAudience.InternalBrief,
            result.Output!,
            ReportPackWriterTestData.Sheet(),
            ReportPackWriterTestData.Content()));
    }
}

/// <summary>
/// The validator on a comparison-scope sheet: <c>{{model:X}}</c> for the sheet's letters only, the
/// <c>model.&lt;L&gt;.*</c> and <c>pair.&lt;L&gt;.&lt;M&gt;.*</c> keys, the <c>models</c> list, the names of
/// every covered model, rule 16 across two models, and topics written once per job.
/// </summary>
public class BenchmarkReportPackComparisonValidatorTests
{
    public static TheoryData<BenchmarkReportAudience> Audiences => new()
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief,
    };

    private static (BenchmarkReportFactSheet Sheet, BenchmarkReportContentSnapshot Content) Data(bool subset = false)
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts(subset ? BenchmarkReportPackFixture.SubsetKeys : null);
        return (built.Sheet!, built.Content!);
    }

    private static IReadOnlyList<BenchmarkReportValidationNote> Validate(
        BenchmarkReportAudience audience, BenchmarkReportWriterOutput output, bool subset = false,
        IReadOnlyList<BenchmarkReportQuestionTopic>? sharedTopics = null)
    {
        var (sheet, content) = Data(subset);
        return BenchmarkReportPackValidator.Validate(audience, output, sheet, content, sharedTopics);
    }

    private static BenchmarkReportWriterOutput Valid(BenchmarkReportAudience audience, bool subset = false)
        => BenchmarkReportPackFixture.ComparisonWriter(audience, Data(subset).Sheet);

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheFixtureWriterOutput_IsValid(BenchmarkReportAudience audience)
    {
        Assert.Empty(Validate(audience, Valid(audience)));
        Assert.Empty(Validate(audience, Valid(audience, subset: true), subset: true));
    }

    [Fact]
    public void ModelTokens_AreValidForTheSheetsLetters_AndNoOther()
    {
        var output = Valid(BenchmarkReportAudience.ExecutiveSummary, subset: true);
        output.Sections[BenchmarkReportSlots.Reliability] = "{{model:A}} and {{model:C}} were graded alike, and {{subject}} and {{peer:A}} are not tokens here.";

        var notes = Validate(BenchmarkReportAudience.ExecutiveSummary, output, subset: true);

        var tokens = Assert.Single(notes, n => n.Rule == 2);
        Assert.Contains("{{model:C}}", tokens.Message);
        Assert.Contains("{{subject}}", tokens.Message);
        Assert.Contains("{{peer:A}}", tokens.Message);
        Assert.DoesNotContain("{{model:A}},", tokens.Message);
        Assert.Contains("{{model:X}} with a letter from MODELS", tokens.Message);
    }

    [Fact]
    public void ModelAndPairFactKeys_AreValidEvidence_AndUnknownOnesAreNot()
    {
        var output = Valid(BenchmarkReportAudience.TechnicalReport);
        output.Models![0].Points[0].Evidence = new List<string> { "model.A.dimension.accuracy", "pair.A.E.quality.difference", "Q3" };
        output.Models[1].Points[0].Evidence = new List<string> { "model.Q.quality.index" };

        var notes = Validate(BenchmarkReportAudience.TechnicalReport, output);

        var unknown = Assert.Single(notes);
        Assert.Equal(5, unknown.Rule);
        Assert.Equal("models[1].points[0]", unknown.Location);
        Assert.Contains("model.Q.quality.index", unknown.Message);
    }

    [Fact]
    public void TheModelsList_NamesEachSheetLetterOnce_AndEveryModelNeedsAnEntry()
    {
        var output = Valid(BenchmarkReportAudience.ExecutiveSummary);
        output.Models![1].Model = "Z";
        output.Models.RemoveAt(4);

        var notes = Validate(BenchmarkReportAudience.ExecutiveSummary, output);

        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "models[1]" && n.Message.Contains("\"Z\""));
        var coverage = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.ModelCoverageRule);
        Assert.Contains("{{model:B}}", coverage.Message);
        Assert.Contains("{{model:E}}", coverage.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(BenchmarkReportPackValidator.ModelCoverageRule));

        var (sheet, content) = Data();
        var cleaned = BenchmarkReportPackValidator.DropInvalid(BenchmarkReportAudience.ExecutiveSummary, output, sheet, content);
        Assert.False(cleaned.Fatal);
        Assert.Equal(new[] { "A", "C", "D" }, cleaned.Output.Models!.Select(m => m.Model));
        Assert.Contains(cleaned.Notes, n => n.Rule == 1 && n.Location == "models[1]" && n.Dropped);
    }

    [Fact]
    public void APointWithoutEvidence_OrOverItsCap_IsAnIssue()
    {
        var output = Valid(BenchmarkReportAudience.ExecutiveSummary);
        output.Models![0].Points[0].Evidence.Clear();
        output.Models[1].Points.Add(new BenchmarkReportWriterItem { Text = "{{model:B}} was quick.", Evidence = new List<string> { "model.B.speed.rank" } });
        output.Models[1].Points.Add(new BenchmarkReportWriterItem { Text = "{{model:B}} was cheap.", Evidence = new List<string> { "model.B.cost.rank" } });

        var notes = Validate(BenchmarkReportAudience.ExecutiveSummary, output);

        Assert.Contains(notes, n => n.Rule == 5 && n.Location == "models[0].points[0]");
        Assert.Contains(notes, n => n.Rule == 7 && n.Location == "models[1].points[2]");
    }

    [Fact]
    public void ACoveredModelsName_IsRule10()
    {
        var output = Valid(BenchmarkReportAudience.ExecutiveSummary);
        output.Sections[BenchmarkReportSlots.Overview] = "Orion Max answered most questions well.";

        var notes = Validate(BenchmarkReportAudience.ExecutiveSummary, output);

        var names = Assert.Single(notes, n => n.Rule == 10);
        Assert.Contains("Orion Max", names.Message);
        Assert.Contains("{{model:X}}", names.Message);
    }

    [Fact]
    public void RankingTwoModelsWithOverlappingIntervals_MustSaySo()
    {
        var output = Valid(BenchmarkReportAudience.ExecutiveSummary);
        output.Sections[BenchmarkReportSlots.Overview] = "{{model:A}} scored higher than {{model:B}} on the questions.";

        var notes = Validate(BenchmarkReportAudience.ExecutiveSummary, output);
        var hedge = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule);
        Assert.Contains("{{model:A}} against {{model:B}}", hedge.Message);

        // Saying so satisfies the rule, and a pair whose intervals do not overlap needs nothing.
        output.Sections[BenchmarkReportSlots.Overview] = "{{model:A}} scored higher than {{model:B}}, but their intervals overlap.";
        Assert.DoesNotContain(Validate(BenchmarkReportAudience.ExecutiveSummary, output), n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule);
        output.Sections[BenchmarkReportSlots.Overview] = "{{model:A}} scored higher than {{model:E}} on the questions.";
        Assert.DoesNotContain(Validate(BenchmarkReportAudience.ExecutiveSummary, output), n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule);
    }

    [Fact]
    public void TheWhichModelSlot_HoldsAHundredAndFiftyWords_AndIsCutPastThem()
    {
        const string slot = BenchmarkReportSlots.WhichModel;
        Assert.Equal(150, BenchmarkReportPackValidator.SlotMaxWords(BenchmarkReportAudience.ExecutiveSummary, slot, comparisonScope: true));

        var output = Valid(BenchmarkReportAudience.ExecutiveSummary);
        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("choice", 140));
        Assert.Empty(Validate(BenchmarkReportAudience.ExecutiveSummary, output));

        output.Sections[slot] = string.Join(" ", Enumerable.Repeat("choice", 160));
        var note = Assert.Single(Validate(BenchmarkReportAudience.ExecutiveSummary, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections." + slot, note.Location);
        Assert.Contains("150", note.Message);

        // Without a repair, the paragraphs past the cap are dropped.
        output.Sections[slot] = "A first paragraph about the choice.\n\n" + string.Join(" ", Enumerable.Repeat("choice", 150));
        var (sheet, content) = Data();
        var cleaned = BenchmarkReportPackValidator.DropInvalid(BenchmarkReportAudience.ExecutiveSummary, output, sheet, content);
        Assert.Equal("A first paragraph about the choice.", cleaned.Output.Sections[slot]);
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "sections." + slot + "[p2]");
    }

    [Fact]
    public void ListsTheComparisonDocumentsDoNotUse_AreRule1()
    {
        var output = Valid(BenchmarkReportAudience.InternalBrief);
        output.Strengths.Add(new BenchmarkReportWriterItem { Text = "Strong.", Evidence = new List<string> { "Q1" } });
        output.Models = new List<BenchmarkReportModelPoints> { new() { Model = "A", Points = { new() { Text = "Fine.", Evidence = { "Q1" } } } } };

        var notes = Validate(BenchmarkReportAudience.InternalBrief, output);

        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "strengths");
        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "models");
    }

    [Fact]
    public void AModelLead_IsATriageOfTheComparisonBrief()
    {
        var output = Valid(BenchmarkReportAudience.InternalBrief);
        output.Leads[0].Triage = BenchmarkReportSlots.LeadTriageModel;

        Assert.Empty(Validate(BenchmarkReportAudience.InternalBrief, output));
    }

    [Fact]
    public void TopicsWrittenOncePerJob_AreNotCheckedAgain_AndReplaceTheWritersOwn()
    {
        var output = Valid(BenchmarkReportAudience.InternalBrief);
        output.QuestionTopics = new List<BenchmarkReportQuestionTopic> { new() { Question = 99, Topic = "No such question" } };
        var shared = new List<BenchmarkReportQuestionTopic> { new() { Question = 1, Topic = "Throwing gems" } };

        Assert.Contains(Validate(BenchmarkReportAudience.InternalBrief, output), n => n.Location.StartsWith("questionTopics", StringComparison.Ordinal));
        Assert.Empty(Validate(BenchmarkReportAudience.InternalBrief, output, sharedTopics: shared));

        var (sheet, content) = Data();
        output.Leads[0].Evidence = new List<string> { "nope" };
        var cleaned = BenchmarkReportPackValidator.DropInvalid(BenchmarkReportAudience.InternalBrief, output, sheet, content, shared);
        Assert.Equal(new[] { (1, "Throwing gems") }, cleaned.Output.QuestionTopics.Select(t => (t.Question, t.Topic)));
    }

    [Fact]
    public void TheParser_ReadsTheModelsList_AndItsLetters()
    {
        string json = "{\"headline\":\"h\",\"sections\":{\"Overview\":\"o\"},\"models\":[{\"model\":\"Model b\",\"points\":[{\"text\":\"t\",\"evidence\":[\"Q1\"]}]},{\"model\":\"{{model:C}}\",\"points\":[\"plain\"]}]}";

        var parsed = BenchmarkReportPackParser.Parse(json);

        Assert.True(parsed.Success);
        Assert.Equal(new[] { "B", "C" }, parsed.Output!.Models!.Select(m => m.Model));
        Assert.Equal("plain", parsed.Output.Models![1].Points[0].Text);
        Assert.True(parsed.Output.Sections.ContainsKey(BenchmarkReportSlots.Overview));
        Assert.Null(BenchmarkReportPackParser.Parse("{\"headline\":\"h\"}").Output!.Models);
    }
}

/// <summary>
/// The chat consistency scope: its slots and word caps, the claim discipline of rules C1 to C7, the
/// readable-text rule C8 and the repetition rule C9, each with a passing and a failing case; a per-model
/// document is unaffected by them.
/// </summary>
public class BenchmarkReportPackChatConsistencyValidatorTests
{
    private const BenchmarkReportAudience Es = BenchmarkReportAudience.ExecutiveSummary;
    private const BenchmarkReportAudience Tr = BenchmarkReportAudience.TechnicalReport;
    private const BenchmarkReportAudience Pir = BenchmarkReportAudience.ProviderIssueReport;

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateModelScope(BenchmarkReportAudience audience, BenchmarkReportWriterOutput output)
        => BenchmarkReportPackValidator.Validate(audience, output, ReportPackWriterTestData.Sheet(), ReportPackWriterTestData.Content());

    private static BenchmarkReportCleanResult DropModelScope(BenchmarkReportAudience audience, BenchmarkReportWriterOutput output)
        => BenchmarkReportPackValidator.DropInvalid(audience, output, ReportPackWriterTestData.Sheet(), ReportPackWriterTestData.Content());

    public static TheoryData<BenchmarkReportAudience> ChatConsistencyAudiences => new()
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief,
        BenchmarkReportAudience.ProviderIssueReport,
    };

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateChat(
        BenchmarkReportAudience audience, BenchmarkReportWriterOutput output, bool timeOfDayAssessable = false)
        => BenchmarkReportPackValidator.Validate(
            audience, output, ChatConsistencyReportTestData.Sheet(audience, timeOfDayAssessable), ChatConsistencyReportTestData.Content());

    /// <summary>The notes of the valid output of <paramref name="audience"/> with <paramref name="slot"/> replaced by <paramref name="text"/>.</summary>
    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateChatSlot(
        BenchmarkReportAudience audience, string slot, string text, bool timeOfDayAssessable = false)
    {
        var output = ChatConsistencyReportTestData.ValidOutput(audience);
        output.Sections[slot] = text;
        return ValidateChat(audience, output, timeOfDayAssessable);
    }

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateAsGoodAsBefore(string text, bool timeOfDayAssessable = false)
        => ValidateChatSlot(Es, BenchmarkReportSlots.AsGoodAsBefore, "Within {{scope.hours}}. " + text, timeOfDayAssessable);

    private static BenchmarkReportValidationNote AssertChatRule(IReadOnlyList<BenchmarkReportValidationNote> notes, int rule, string id, string location, string phrase)
    {
        var note = Assert.Single(notes);
        Assert.Equal(rule, note.Rule);
        Assert.Equal(location, note.Location);
        Assert.StartsWith(id + ": ", note.Message, StringComparison.Ordinal);
        Assert.Contains(phrase, note.Message, StringComparison.Ordinal);
        return note;
    }

    private const string AsGoodAsBeforeP1 = "sections.asGoodAsBefore[p1]";

    [Theory]
    [MemberData(nameof(ChatConsistencyAudiences))]
    public void ChatConsistency_ValidOutput_HasNoIssues(BenchmarkReportAudience audience)
    {
        Assert.Empty(ValidateChat(audience, ChatConsistencyReportTestData.ValidOutput(audience)));

        var cleaned = BenchmarkReportPackValidator.DropInvalid(
            audience, ChatConsistencyReportTestData.ValidOutput(audience), ChatConsistencyReportTestData.Sheet(audience), ChatConsistencyReportTestData.Content());
        Assert.False(cleaned.Fatal);
        Assert.Empty(cleaned.Notes);
    }

    [Theory]
    [InlineData("Answer quality degraded in the comparison period.", "degraded")]
    [InlineData("Answers got slower for players.", "got slower")]
    [InlineData("The Overseer chat regressed after the tool guide edit.", "regressed")]
    public void C1_AChangeWordWithoutAResult_IsAnError(string text, string phrase)
    {
        AssertChatRule(ValidateAsGoodAsBefore(text), BenchmarkReportPackValidator.ChatChangeClaimRule, "C1", AsGoodAsBeforeP1, "\"" + phrase + "\"");
    }

    [Fact]
    public void C1_AChangeWordCitingOnlyAnInconclusiveEndpoint_IsAnError()
    {
        var notes = ValidateAsGoodAsBefore("Cost per answer rose by {{endpoint.P5.estimate}}, against {{endpoint.P5.mde}} detectable.");

        AssertChatRule(notes, BenchmarkReportPackValidator.ChatChangeClaimRule, "C1", AsGoodAsBeforeP1, "\"rose\"");
    }

    [Theory]
    [InlineData("Answer quality degraded by {{endpoint.P1.estimate}}.")]
    [InlineData("Answer quality degraded, as {{attribution.1.label}} records.")]
    [InlineData("The chat {{verdict.overall}} degraded overall.")]
    [InlineData("Answer quality degraded against the control, by {{did.1.estimate}}.")]
    public void C1_AChangeWordCitingADecisiveResult_Passes(string text)
    {
        Assert.Empty(ValidateAsGoodAsBefore(text));
    }

    [Theory]
    [InlineData("The provider deliberately changed the chat.", "deliberately")]
    [InlineData("Answers fell short after the provider throttled requests.", "throttled")]
    [InlineData("The provider moved to quantized weights, as {{annotation.2.text}} says.", "quantized")]
    [InlineData("New hardware may explain it.", "hardware")]
    public void C2_IntentOrAMechanismWithoutAProviderConfirmedCause_IsAnError(string text, string phrase)
    {
        AssertChatRule(ValidateAsGoodAsBefore(text), BenchmarkReportPackValidator.ChatIntentMechanismRule, "C2", AsGoodAsBeforeP1, "\"" + phrase + "\"");
    }

    [Fact]
    public void C2_AMechanismCitingAProviderConfirmedCause_Passes()
    {
        Assert.Empty(ValidateAsGoodAsBefore("The provider names quantization in {{annotation.1.text}}."));
    }

    [Theory]
    [InlineData("Quality degraded by {{endpoint.P1.estimate}} because the provider changed something.", "because")]
    [InlineData("The gap is due to our tool guides, by {{endpoint.P1.estimate}}.", "due to")]
    public void C3_ACausalConnectiveWithoutAnAttribution_IsAnError(string text, string phrase)
    {
        AssertChatRule(ValidateAsGoodAsBefore(text), BenchmarkReportPackValidator.ChatCausalClaimRule, "C3", AsGoodAsBeforeP1, "\"" + phrase + "\"");
    }

    [Fact]
    public void C3_ACausalConnectiveCitingAnAttribution_Passes()
    {
        Assert.Empty(ValidateAsGoodAsBefore("Quality degraded by {{endpoint.P1.estimate}}, driven by the provider's side as {{attribution.1.label}} records."));
    }

    [Theory]
    [InlineData("The quality loss of {{endpoint.P1.estimate}} is established.", "established")]
    [InlineData("We can state that quality fell short, at {{endpoint.P1.estimate}}.", "We can state")]
    [InlineData("The result is publishable.", "publishable")]
    public void C4_PublicClaimWordingWithoutAnEstablishedGrade_IsAnError(string text, string phrase)
    {
        AssertChatRule(ValidateAsGoodAsBefore(text), BenchmarkReportPackValidator.ChatPublicClaimRule, "C4", AsGoodAsBeforeP1, "\"" + phrase + "\"");
    }

    [Theory]
    [InlineData("Waiting time is established as {{endpoint.P2.verdict}}, graded {{endpoint.P2.grade}}.")]
    [InlineData("Our own part is not established, graded {{attribution.2.grade}}.")]
    [InlineData("A provider-confirmed cause is on record in {{annotation.1.text}}.")]
    [InlineData("The provider confirmed a cause in {{annotation.1.text}}.")]
    public void C4_PublicClaimWordingWithAnEstablishedGrade_OrNegated_Passes(string text)
    {
        Assert.Empty(ValidateAsGoodAsBefore(text));
    }

    [Fact]
    public void C5_AnInconclusiveEndpointWithoutItsMde_IsAWarning_AndKeepsItsText()
    {
        string text = "Cost per answer is {{endpoint.P5.verdict}} at {{endpoint.P5.estimate}}.";
        var notes = ValidateChatSlot(Es, BenchmarkReportSlots.PlayerImpact, text);

        var note = AssertChatRule(notes, BenchmarkReportPackValidator.ChatInconclusiveMdeRule, "C5", "sections", "{{endpoint.P5.mde}}");
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));

        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.PlayerImpact] = text;
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Es, output, ChatConsistencyReportTestData.Sheet(Es), ChatConsistencyReportTestData.Content());
        Assert.Equal(text, cleaned.Output.Sections[BenchmarkReportSlots.PlayerImpact]);
        Assert.False(Assert.Single(cleaned.Notes).Dropped);
    }

    [Fact]
    public void C5_IsSatisfied_ByTheMdeCitedOnceAnywhereInTheDocument()
    {
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.PlayerImpact] = "Cost per answer is {{endpoint.P5.verdict}} at {{endpoint.P5.estimate}}.";
        output.Sections[BenchmarkReportSlots.ConfidenceAndScope] = "These runs could detect only a change of {{endpoint.P5.mde}} in cost, and time of day is {{serving.timeOfDayAssessable}}.";

        var notes = BenchmarkReportPackValidator.Validate(Es, output, ChatConsistencyReportTestData.Sheet(Es), ChatConsistencyReportTestData.Content());

        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.ChatInconclusiveMdeRule);
    }

    [Fact]
    public void C6_DoesNotAskForTheHours_WhenThePeriodsShareNone()
    {
        var result = ChatConsistencyReportTestData.Result() with
        {
            Scope = new ChatConsistencyScope { Text = "no common time stratum" }
        };
        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, Es);
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        foreach (string slot in output.Sections.Keys.ToList())
        {
            output.Sections[slot] = output.Sections[slot].Replace(" within {{scope.hours}}", string.Empty, StringComparison.Ordinal);
        }
        output.Headline = "The Overseer chat with {{subject}} is {{verdict.overall}}.";

        var notes = BenchmarkReportPackValidator.Validate(Es, output, sheet, ChatConsistencyReportTestData.Content());

        Assert.False(Assert.Single(sheet.Facts, f => f.Key == "scope.hours").Available);
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.ChatHoursRule);
    }

    [Fact]
    public void C5_AnInconclusiveEndpointWithItsMdeElsewhereInTheSlot_Passes()
    {
        Assert.Empty(ValidateChatSlot(Es, BenchmarkReportSlots.PlayerImpact,
            "Cost per answer is {{endpoint.P5.verdict}} at {{endpoint.P5.estimate}}.\n\nThe runs could detect a change of {{endpoint.P5.mde}}."));
    }

    [Theory]
    [InlineData(false, "Waiting time held at all hours, as {{endpoint.P2.verdict}} shows.", "at all hours")]
    [InlineData(false, "Waiting time held around the clock: {{serving.timeOfDayAssessable}}.", "around the clock")]
    [InlineData(true, "Waiting time held regardless of load, as {{endpoint.P2.verdict}} shows.", "regardless of load")]
    public void C6_AllHoursWordingWithoutATrueTimeOfDayFact_IsAnError(bool assessable, string text, string phrase)
    {
        AssertChatRule(ValidateAsGoodAsBefore(text, assessable), BenchmarkReportPackValidator.ChatHoursRule, "C6", AsGoodAsBeforeP1, "\"" + phrase + "\"");
    }

    [Fact]
    public void C6_AllHoursWordingCitingATrueTimeOfDayFact_Passes()
    {
        Assert.Empty(ValidateAsGoodAsBefore("Waiting time held around the clock: {{serving.timeOfDayAssessable}}.", timeOfDayAssessable: true));
    }

    [Theory]
    [MemberData(nameof(ChatConsistencyAudiences))]
    public void C6_ADocumentThatNeverCitesTheHours_IsAnError(BenchmarkReportAudience audience)
    {
        var output = ChatConsistencyReportTestData.ValidOutput(audience);
        output.Headline = output.Headline.Replace("{{scope.hours}}", "{{n.answers}}", StringComparison.Ordinal);
        foreach (string slot in output.Sections.Keys.ToList())
        {
            output.Sections[slot] = output.Sections[slot].Replace("{{scope.hours}}", "{{n.answers}}", StringComparison.Ordinal);
        }

        AssertChatRule(ValidateChat(audience, output), BenchmarkReportPackValidator.ChatHoursRule, "C6", "sections", "{{scope.hours}}");

        var cleaned = BenchmarkReportPackValidator.DropInvalid(audience, output, ChatConsistencyReportTestData.Sheet(audience), ChatConsistencyReportTestData.Content());
        var note = Assert.Single(cleaned.Notes);
        Assert.Equal(BenchmarkReportPackValidator.ChatHoursRule, note.Rule);
        Assert.False(note.Dropped);
        Assert.False(cleaned.Fatal);
    }

    [Theory]
    [InlineData("The model got slower by {{endpoint.P1.estimate}}.", "The model")]
    [InlineData("Serving latency moved by {{endpoint.P1.estimate}}.", "latency")]
    [InlineData("A new snapshot may be live, at {{endpoint.P1.estimate}}.", "snapshot")]
    public void C7_AModelOrServingClaimWithoutAProviderSideAttribution_IsAnError(string text, string phrase)
    {
        var notes = ValidateChatSlot(Pir, BenchmarkReportSlots.Measurements, text);

        AssertChatRule(notes, BenchmarkReportPackValidator.ChatProviderReportRule, "C7", "sections.measurements[p1]", phrase);
    }

    [Fact]
    public void C7_AModelClaimCitingAProviderSideAttribution_Passes_AndTheIdentifyingSlotsAreExempt()
    {
        Assert.Empty(ValidateChatSlot(Pir, BenchmarkReportSlots.Measurements,
            "The model's answers degraded by {{endpoint.P1.estimate}}, which the analysis places on the provider's side as {{attribution.1.grade}}."));

        // C7 reads a Provider Issue Report only, and not its slots that only identify.
        Assert.Empty(ValidateChatSlot(Pir, BenchmarkReportSlots.AffectedModel, "The model is {{subject.label}}, with the served model {{identity.servedModels}}."));
        Assert.Empty(ValidateChatSlot(Es, BenchmarkReportSlots.ProviderChanges, "The served model is {{identity.servedModels}}."));
    }

    [Fact]
    public void C7_RuledOutMustCiteEveryOverseerEvent()
    {
        var notes = ValidateChatSlot(Pir, BenchmarkReportSlots.RuledOut, "We checked {{events.1.label}} on our side.");

        AssertChatRule(notes, BenchmarkReportPackValidator.ChatProviderReportRule, "C7", "sections.ruledOut", "events.2.*");

        // Another document's slots need no event list.
        Assert.Empty(ValidateChatSlot(Tr, BenchmarkReportSlots.OverseerEvents, "One Overseer event matters here: {{events.1.label}}."));
    }

    [Theory]
    [InlineData("The chat is monitored within {{scope.hours}}.", "monitored")]
    [InlineData("GnollBench keeps monitoring the chat.", "monitoring")]
    public void C2_MonitoringWording_IsAnError(string text, string phrase)
    {
        var note = AssertChatRule(ValidateAsGoodAsBefore(text), BenchmarkReportPackValidator.ChatIntentMechanismRule, "C2", AsGoodAsBeforeP1, "\"" + phrase + "\"");
        Assert.Contains("made by hand", note.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "monitor", "monitors", "monitored", "monitoring" }, BenchmarkReportPackValidator.ChatMonitoringWords);
    }

    [Theory]
    [InlineData("The wiki moved to revision a8fa85a4bb80dc4e.", "a8fa85a4bb80dc4e")]
    [InlineData("The options went from {\"hasGameSnapshot\":false to on.", "{\"")]
    [InlineData("The CandidateSystemPromptSha256 field moved.", "CandidateSystemPromptSha256")]
    [InlineData("The corpus moved, recorded as CorpusIndexFingerprintsJson.", "CorpusIndexFingerprintsJson")]
    [InlineData("The input was {{analysis.inputSha256}}.", "{{analysis.inputSha256}}")]
    public void C8_HashesJsonAndInternalFieldNames_AreErrors(string text, string token)
    {
        var notes = ValidateChatSlot(Tr, BenchmarkReportSlots.OverseerEvents, text + " Within {{scope.hours}}.")
            .Where(n => n.Rule == BenchmarkReportPackValidator.ChatReadableTextRule)
            .ToList();

        var note = Assert.Single(notes);
        Assert.Equal("sections.overseerEvents[p1]", note.Location);
        Assert.StartsWith("C8: ", note.Message, StringComparison.Ordinal);
        Assert.Contains(token, note.Message, StringComparison.Ordinal);
        Assert.False(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    [Fact]
    public void C8_ReadableEventTokens_Pass_AndAHashInTheHeadline_IsFatal()
    {
        Assert.Empty(ValidateChatSlot(Tr, BenchmarkReportSlots.OverseerEvents,
            "Two Overseer updates fall between the periods: {{eventGroups.1.changes}} and {{events.2.change}}."));

        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Headline = "The Overseer chat with {{subject}} is {{verdict.overall}} at revision a8fa85a4bb80dc4e within {{scope.hours}}.";
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Es, output, ChatConsistencyReportTestData.Sheet(Es), ChatConsistencyReportTestData.Content());
        Assert.True(cleaned.Fatal);
        Assert.Contains(cleaned.Notes, n => n.Rule == BenchmarkReportPackValidator.ChatReadableTextRule && n.Location == "headline");
    }

    [Fact]
    public void C8_APairStatedOnceForBothPeriods_IsKeptFromTheWriter()
    {
        // The served model ID is the same in both periods, so the pair is stated once and the two halves are hidden.
        var note = Assert.Single(ValidateChatSlot(Tr, BenchmarkReportSlots.OverseerEvents,
            "The served model was {{identity.baseline.servedModels}}, within {{scope.hours}}."));
        Assert.Equal(BenchmarkReportPackValidator.ChatReadableTextRule, note.Rule);
        Assert.Contains("{{identity.baseline.servedModels}}", note.Message, StringComparison.Ordinal);

        Assert.Empty(ValidateChatSlot(Tr, BenchmarkReportSlots.OverseerEvents, "The served model was {{identity.servedModels}}, within {{scope.hours}}."));
    }

    [Fact]
    public void C8_TheMeanAnswerScore_IsKeptFromTheWriter_InABatteryComparisonWithOverallIndexes()
    {
        var baseResult = ChatConsistencyReportTestData.Result();
        var battery = baseResult with
        {
            ComparisonSet = new ChatConsistencyComparedSet { Kind = ChatConsistencyComparisonSetKinds.Battery, Key = "battery:abc", Label = "Two suites (revision 1)" },
            UnitKind = ChatConsistencyComparisonSetKinds.BatteryRunUnit,
            Units = new[]
            {
                new ChatConsistencyUnitView { UnitId = 101, Kind = ChatConsistencyComparisonSetKinds.BatteryRunUnit, Period = "baseline", MemberRunIds = new long[] { 10, 11 } },
                new ChatConsistencyUnitView { UnitId = 102, Kind = ChatConsistencyComparisonSetKinds.BatteryRunUnit, Period = "comparison", MemberRunIds = new long[] { 20, 21 } }
            },
            PeriodLevels = baseResult.PeriodLevels!.Select(l => l with { OverallIndex = l.Period == "baseline" ? 82.0 : 81.7 }).ToList()
        };
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.PlayerImpact] = "Answers scored {{level.comparison.quality}} within {{scope.hours}}.";

        var notes = BenchmarkReportPackValidator.Validate(Es, output, BenchmarkChatConsistencyReportFacts.Build(battery, Es), ChatConsistencyReportTestData.Content());
        var note = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.ChatReadableTextRule);
        Assert.Contains("{{level.comparison.quality}}", note.Message, StringComparison.Ordinal);

        // A comparison of runs keeps the mean answer score for the writer.
        Assert.DoesNotContain(ValidateChat(Es, output), n => n.Rule == BenchmarkReportPackValidator.ChatReadableTextRule);
    }

    [Theory]
    [InlineData("Quality and cost per answer are {{endpoint.P5.verdict}} and {{endpoint.P5.verdict}}, respectively.", "inconclusive and inconclusive, respectively")]
    [InlineData("The two estimates were 3.1 and 3.1, respectively.", "3.1 and 3.1, respectively")]
    [InlineData("Quality and speed paired {{endpoint.P1.items}} and {{endpoint.P2.items}} respectively.", "20 items and 20 items, respectively")]
    [InlineData("The checks report {{limitation.1}} and more.", "sampled. and more")]
    public void C9_ARepeatedPairOrASplicedSentence_IsAWarning(string text, string excerpt)
    {
        var notes = ValidateChatSlot(Tr, BenchmarkReportSlots.EndpointResults, text + " Within {{scope.hours}}.")
            .Where(n => n.Rule == BenchmarkReportPackValidator.ChatRepetitionRule)
            .ToList();

        var note = Assert.Single(notes);
        Assert.Equal("sections.endpointResults[p1]", note.Location);
        Assert.StartsWith("C9: ", note.Message, StringComparison.Ordinal);
        Assert.Contains(excerpt, note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    [Fact]
    public void C9_KeepsItsParagraph_AfterTheRepairTurn()
    {
        var output = ChatConsistencyReportTestData.ValidOutput(Tr);
        output.Sections[BenchmarkReportSlots.EndpointResults] = "The checks report {{limitation.1}} and more. Within {{scope.hours}}.";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Tr, output, ChatConsistencyReportTestData.Sheet(Tr), ChatConsistencyReportTestData.Content());

        Assert.False(cleaned.Fatal);
        Assert.Equal(output.Sections[BenchmarkReportSlots.EndpointResults], cleaned.Output.Sections[BenchmarkReportSlots.EndpointResults]);
        var note = Assert.Single(cleaned.Notes);
        Assert.Equal(BenchmarkReportPackValidator.ChatRepetitionRule, note.Rule);
        Assert.False(note.Dropped);
    }

    [Theory]
    [InlineData("The two estimates were 3.1 and 3.2, respectively.")]
    [InlineData("Time to first answer text and cost per answer were {{endpoint.P2.verdict}} and {{endpoint.P5.verdict}}, respectively.")]
    [InlineData("Some updates, e.g. and most notably the wiki, fall between the periods.")]
    [InlineData("The analysis records a limitation. And it names the hours.")]
    public void C9_DistinctValues_AnAbbreviation_OrANewSentence_Pass(string text)
    {
        Assert.DoesNotContain(ValidateChatSlot(Tr, BenchmarkReportSlots.EndpointResults, text + " Within {{scope.hours}}."),
            n => n.Rule == BenchmarkReportPackValidator.ChatRepetitionRule);
    }

    [Fact]
    public void AChatConsistencyError_DropsItsParagraph_AndAHeadlineError_IsFatal()
    {
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.PlayerImpact] = "Answers got worse for players.\n\nCost per answer is inconclusive at {{endpoint.P5.estimate}}, with {{endpoint.P5.mde}} detectable.";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Es, output, ChatConsistencyReportTestData.Sheet(Es), ChatConsistencyReportTestData.Content());

        Assert.Equal("Cost per answer is inconclusive at {{endpoint.P5.estimate}}, with {{endpoint.P5.mde}} detectable.", cleaned.Output.Sections[BenchmarkReportSlots.PlayerImpact]);
        var dropped = Assert.Single(cleaned.Notes);
        Assert.Equal(BenchmarkReportPackValidator.ChatChangeClaimRule, dropped.Rule);
        Assert.True(dropped.Dropped);

        output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Headline = "The Overseer chat deliberately got worse within {{scope.hours}}.";
        Assert.True(BenchmarkReportPackValidator.DropInvalid(Es, output, ChatConsistencyReportTestData.Sheet(Es), ChatConsistencyReportTestData.Content()).Fatal);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
    public void AModelScopeDocument_IsUnaffectedByTheChatConsistencyRules(BenchmarkReportAudience audience)
    {
        const string claims = "Answers degraded because the provider deliberately throttled them at all hours, which is established and publishable.";
        var output = ReportPackWriterTestData.ValidOutput(audience);
        string slot = BenchmarkReportSlots.For(audience).RequiredSlots[^1];
        output.Sections[slot] = output.Sections[slot] + "\n\n" + claims;

        Assert.Empty(ValidateModelScope(audience, output));
        Assert.Equal(output.Sections[slot], DropModelScope(audience, output).Output.Sections[slot]);
    }

    [Fact]
    public void ChatConsistencySlotCaps_ApplyToTheirScopeOnly()
    {
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.AsGoodAsBefore] = "Within {{scope.hours}}. " + string.Join(" ", Enumerable.Repeat("word", 120));

        var note = Assert.Single(ValidateChat(Es, output));
        Assert.Equal(7, note.Rule);
        Assert.Equal("sections.asGoodAsBefore", note.Location);
        Assert.Contains("\"Is the Overseer chat with this model as good as before?\"", note.Message, StringComparison.Ordinal);

        Assert.Equal(150, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.Limitations, BenchmarkReportScope.ChatConsistency));
        Assert.Equal(BenchmarkReportPackValidator.LimitationsMaxWords, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.Limitations, BenchmarkReportScope.Model));
        Assert.Equal(BenchmarkReportPackValidator.LimitationsMaxWords, BenchmarkReportPackValidator.SlotMaxWords(Tr, BenchmarkReportSlots.Limitations, BenchmarkReportScope.Comparison));
        Assert.Null(BenchmarkReportPackValidator.SlotMaxWords(Es, BenchmarkReportSlots.AsGoodAsBefore, comparisonScope: false));
        Assert.Equal(60, BenchmarkReportPackValidator.SlotMaxWords(Pir, BenchmarkReportSlots.SampleRequestIds, BenchmarkReportScope.ChatConsistency));
        foreach (var audience in BenchmarkReportSlots.ChatConsistencyAudiences)
        {
            Assert.All(BenchmarkReportSlots.ForChatConsistency(audience).RequiredSlots,
                slot => Assert.NotNull(BenchmarkReportPackValidator.ChatConsistencySlotMaxWords(audience, slot)));
        }
    }

    [Fact]
    public void TheParser_NormalizesChatConsistencySlotNames()
    {
        var parsed = BenchmarkReportPackParser.Parse("{\"headline\":\"h\",\"sections\":{\"IssueSummary\":\"a\",\"ruledout\":\"b\",\"asGoodAsBefore\":\"c\"}}");

        Assert.True(parsed.Success);
        Assert.Equal(new[] { BenchmarkReportSlots.AsGoodAsBefore, BenchmarkReportSlots.IssueSummary, BenchmarkReportSlots.RuledOut },
            parsed.Output!.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
