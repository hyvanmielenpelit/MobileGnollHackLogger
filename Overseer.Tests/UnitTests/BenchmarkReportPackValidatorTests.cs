namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
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

    [Fact]
    public void TheWarningRules_AreTwelveToSeventeen()
    {
        for (int rule = 1; rule <= 17; rule++)
        {
            Assert.Equal(rule >= 12, BenchmarkReportPackValidator.IsWarningRule(rule));
        }
        Assert.False(BenchmarkReportPackValidator.IsWarningRule(18));
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
