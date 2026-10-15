namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkReportPackPromptTests
{
    public static TheoryData<BenchmarkReportAudience> Audiences => new()
    {
        BenchmarkReportAudience.ExecutiveSummary,
    };

    private static BenchmarkReportWriterPrompt Build(BenchmarkReportAudience audience, BenchmarkReportFactSheet? sheet = null)
        => BenchmarkReportPackPrompt.Build(audience, sheet ?? ReportPackWriterTestData.Sheet(), ReportPackWriterTestData.Content());

    // System prompt -----------------------------------------------------------------------------

    [Fact]
    public void ExecutiveSummary_AsksOnlyForItsSlotsAndLists()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;

        Assert.Contains("\"meaning\"", system);
        Assert.Contains("\"confidence\"", system);
        Assert.Contains("\"strengths\"", system);
        Assert.Contains("\"weaknesses\"", system);
        Assert.Contains("\"questionTopics\"", system);
        Assert.Contains("at most three items", system);
        Assert.Contains("non-specialist", system);

        Assert.DoesNotContain("\"abstract\"", system);
        Assert.DoesNotContain("whyItScored", system);
        Assert.DoesNotContain("whatWorked", system);
        Assert.DoesNotContain("overseerChat", system);
        Assert.DoesNotContain("benchmarkSystem", system);
        Assert.DoesNotContain("modelResult", system);
        Assert.DoesNotContain("\"recommendations\"", system);
        Assert.DoesNotContain("questionNotes", system);
        Assert.DoesNotContain("\"leads\"", system);
        Assert.DoesNotContain("triage", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheTokenAndEvidenceRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("{{key}}", system);
        Assert.Contains("{{subject}}", system);
        Assert.Contains("{{peer:X}}", system);
        Assert.Contains("no digits", system);
        Assert.Contains("Number words", system);
        Assert.Contains("Q7", system);
        Assert.Contains("Never name any model, provider or product", system);
        Assert.Contains("fact key from FACTS", system);
        Assert.Contains("finding row id such as R2", system);
        Assert.Contains("never cites a weakness row", system);
        Assert.Contains("the graders disagree", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheWeighingRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("Convergent row", system);
        Assert.Contains("weakest evidence", system);
        Assert.Contains("Never put it in the headline", system);
        Assert.Contains("Conflicting row means the graders disagree", system);
        Assert.Contains("A claim-verifier ruling is an advisory judgment by an AI model that is sometimes wrong. "
            + "Attribute it ('the claim verifier judged …'), never state it as a fact about the game, and never list refuted "
            + "claims in the abstract or the one-sentence result.", system);
        Assert.Contains("Never describe a claim the claim verifier supported as a mistake", system);
        Assert.DoesNotContain("is a fact of the game", system);
        Assert.Contains("response-style conflict", system);
        Assert.Contains("The response-style note is Overseer's own observation. Never attribute it to a grader.", system);
        Assert.Contains("Never re-grade", system);
        Assert.Contains("never invent a cause", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheNoSignificanceNoQuotingAndFormatRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("runs no significance test", system);
        Assert.Contains("say that the intervals overlap and that the order between them is not established", system);
        Assert.Contains("significant, significantly or statistically", system);
        Assert.Contains("reliably better, reliably worse or clearly outperforms", system);
        Assert.Contains("eight consecutive words", system);
        Assert.Contains("Describe a question by its topic", system);
        Assert.Contains("No headings, no tables, no HTML", system);
        Assert.Contains("Answer with the JSON object only", system);
        Assert.Contains("UNTRUSTED REFERENCE DATA", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_CarriesNoData(BenchmarkReportAudience audience)
    {
        var prompt = Build(audience);

        Assert.DoesNotContain("quality.index", prompt.SystemPrompt);
        Assert.DoesNotContain("80 ± 3 / 100", prompt.SystemPrompt);
        Assert.DoesNotContain("long sword", prompt.SystemPrompt);
        Assert.DoesNotContain("\r", prompt.SystemPrompt);
        Assert.DoesNotContain("\r", prompt.UserMessage);
    }

    // SHA-256 -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Audiences))]
    public void PromptSha256_IsStableLowerCaseHexOfTheSystemPrompt(BenchmarkReportAudience audience)
    {
        string sha = BenchmarkReportPackPrompt.PromptSha256(audience);

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), sha);
        Assert.Equal(sha, BenchmarkReportPackPrompt.PromptSha256(audience));

        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Build(audience).SystemPrompt)));
        Assert.Equal(expected, sha);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void PromptSha256_IsIndependentOfTheData(BenchmarkReportAudience audience)
    {
        var other = ReportPackWriterTestData.Sheet();
        other.Facts.RemoveAt(0);
        other.Peers.RemoveAt(1);
        other.Questions.RemoveAt(3);

        var first = Build(audience);
        var second = BenchmarkReportPackPrompt.Build(audience, other, new BenchmarkReportContentSnapshot());

        Assert.NotEqual(first.UserMessage, second.UserMessage);
        Assert.Equal(first.SystemPrompt, second.SystemPrompt);
    }

    // User message ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Audiences))]
    public void UserMessage_IsDeterministicAndSorted(BenchmarkReportAudience audience)
    {
        string first = Build(audience).UserMessage;
        Assert.Equal(first, Build(audience).UserMessage);

        var shuffled = ReportPackWriterTestData.Sheet();
        shuffled.Facts.Reverse();
        shuffled.Peers.Reverse();
        shuffled.Questions.Reverse();
        shuffled.Rows.Reverse();

        Assert.Equal(first, Build(audience, shuffled).UserMessage);
    }

    [Fact]
    public void UserMessage_ListsFactsSortedByKeyWithUnavailableReasons()
    {
        string message = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        string[] lines =
        {
            "quality.dim.accuracy = 4.1 / 5",
            "quality.index = 80 ± 3 / 100",
            "speed.p50Ms = unavailable: the speed axis is degraded",
            "style.responseStyleConflict = yes",
            "tools.notFound = 2",
        };
        var positions = lines.Select(l => message.IndexOf(l, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Contains("{{style.responseStyleConflict}} is true", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheResponseStyle_IsNamedAsTheProductionDefault_NeverAsTheBenchmarksInstruction(BenchmarkReportAudience audience)
    {
        var prompt = Build(audience);

        foreach (string text in new[] { prompt.SystemPrompt, prompt.UserMessage })
        {
            Assert.DoesNotContain("benchmark's concise", text);
            Assert.DoesNotContain("concise-answer instruction", text);
            Assert.DoesNotContain("the instruction's effect", text);
        }
        Assert.Contains("{{style.responseStyleConflict}} is true: completeness is the lowest dimension, well below accuracy, under the production chat's concise response style. This states a condition, not a cause.", prompt.UserMessage);
    }

    [Fact]
    public void TheWeighingRules_CallTheConciseStyleTheProductionDefault()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;

        Assert.Contains("- When the response-style conflict fact is true, {{subject}}'s completeness is its lowest dimension, well below its accuracy, and it answered under the production chat's concise response style — the default every Overseer user receives, which the benchmark grades as it is. Where completeness is discussed, say that it was graded under that style. Never say that the style caused the gap or a part of it: the run does not compare response styles. Never present the gap as the model's failing alone either. Never call the style the benchmark's instruction.", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheResponseStyle_IsAConditionNeverACause_InBothPrompts(BenchmarkReportAudience audience)
    {
        var prompt = Build(audience);
        var comparison = ComparisonPrompt(audience);

        foreach (string text in new[] { prompt.SystemPrompt, prompt.UserMessage, comparison.SystemPrompt, comparison.UserMessage })
        {
            Assert.DoesNotContain("partly the", text);
            Assert.DoesNotContain("style's effect", text);
        }

        Assert.Contains("- When a model's response-style conflict fact is true, its completeness is its lowest dimension, well below its accuracy, and it answered under the production chat's concise response style — the default every Overseer user receives, which the benchmark grades as it is. "
            + "Where that model's completeness is discussed, say that it was graded under that style. "
            + "Never say that the style caused the gap or a part of it: no run of this comparison compares response styles. "
            + "Never present the gap as that model's failing alone either. "
            + "Where the fact is true for several models, say it once for all of them, and never use it to explain a difference in completeness between models. "
            + "Never call the style the benchmark's instruction or attribute it to a grader.", comparison.SystemPrompt);
    }

    [Fact]
    public void TheComparisonResponseStyleLines_StateAConditionNotACause()
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts();
        var sheet = built.Sheet!;
        sheet.Facts.RemoveAll(f => f.Key.Contains("responseStyleConflict", StringComparison.OrdinalIgnoreCase));
        sheet.Facts.Add(new BenchmarkReportFact { Key = "model.A.style.responseStyleConflict", Value = System.Text.Json.Nodes.JsonValue.Create(true), Display = "yes" });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "model.B.style.responseStyleConflict", Value = System.Text.Json.Nodes.JsonValue.Create(false), Display = "no" });

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, built.Content!).UserMessage;

        Assert.Contains("RESPONSE STYLE\n"
            + "{{model.A.style.responseStyleConflict}} is true: that model's completeness is its lowest dimension, well below its accuracy, under the production chat's concise response style. This states a condition, not a cause.\n"
            + "{{model.B.style.responseStyleConflict}} is not true: that model's completeness is not its lowest dimension by that margin.\n", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void BothPrompts_LimitWhatACitedQuestionSupports_AndHowQuestionsAndSuitesAreCompared(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;
        string comparison = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience);

        Assert.Contains("- Cite a question only for what the data shows about it. A question given with its excerpts and grader comments supports a claim about what an answer said or left out; "
            + "a question shown only by its scores supports only a claim about its score, critical error, tool calls or time.", system);
        Assert.Contains("- Cite a question only for what the data shows about it. A question listed under QUESTIONS, with its excerpts and grader comments, supports a claim about what an answer said or left out. "
            + "A question you see only as a QUESTION MATRIX row supports only a claim about its scores, critical errors, tool calls or time.", comparison);

        const string suites = "- A difference between suites, or between difficulty bands, compares different questions. Never explain it by what a suite or a question contains, "
            + "for example that it uses the game snapshot; say only where the model scored lower.";
        Assert.Contains(suites, system);
        Assert.Contains(suites, comparison);
        Assert.Contains("- When you name the subject's lowest or highest scoring questions, take them from its QUESTIONS in order, without skipping one in between.", system);
        Assert.Contains("- When you name a model's lowest or highest scoring questions, take them from its QUESTION MATRIX cells in order, without skipping one in between.", comparison);
        Assert.Contains("- A statement that several models did, or left out, the same thing must hold for each of them in their excerpts and grader comments. "
            + "Where only one grader charged it, attribute it to that grader.", comparison);
        Assert.DoesNotContain("A statement that several models did", system);

        // Each rule sits in its own block.
        int evidence = comparison.IndexOf("\nEVIDENCE:\n", StringComparison.Ordinal);
        int weighing = comparison.IndexOf("\nWEIGHING THE EVIDENCE:\n", StringComparison.Ordinal);
        Assert.True(evidence >= 0 && weighing > evidence);
        Assert.InRange(comparison.IndexOf("- Cite a question only for", StringComparison.Ordinal), evidence, weighing);
        Assert.True(comparison.IndexOf(suites, StringComparison.Ordinal) > weighing);
        Assert.True(system.IndexOf(suites, StringComparison.Ordinal) > system.IndexOf("\nWEIGHING THE EVIDENCE:\n", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheComparisonPrompt_SaysAPairedSpeedOrCostResultMayRestOnOneRunASide(BenchmarkReportAudience audience)
    {
        string comparison = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience);

        Assert.Contains("- A speed or cost result that a paired test establishes holds on these questions after the adjustment. "
            + "Where the paired tests carry a single-run caveat, say that the result rests on one run a side.", comparison);
    }

    [Fact]
    public void TheComparisonWhichModelSlot_EndsWithADefaultChoice()
    {
        string system = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(BenchmarkReportAudience.ExecutiveSummary);
        string slot = system.Split('\n').Single(l => l.StartsWith("- " + BenchmarkReportSlots.WhichModel + ":", StringComparison.Ordinal));

        Assert.Contains("At most 150 words: which model to use for the best answers, for speed and for cost, each conditional on what the paired tests and the intervals establish. "
            + "Where the order of the leading models is not established, say so and name what would decide between them. "
            + "When the reference is the baseline model, say whether any model is established as better than it, and on which measure. "
            + "End with one sentence naming a default choice for a typical Overseer player — who asks during play and waits for each answer — and the case in which another model is the better choice. "
            + "Base it only on established results and the frontier facts; where nothing separates the models on any measure, say that the choice is open.", slot);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheReadabilityLine_NamesOverclaimingWords_InBothPrompts(BenchmarkReportAudience audience)
    {
        foreach (string system in new[] { Build(audience).SystemPrompt, BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience) })
        {
            string line = system.Split('\n').Single(l => l.StartsWith("- No hype", StringComparison.Ordinal));
            Assert.Equal("- No hype, filler or overclaiming words: " + string.Join(", ", BenchmarkReportPackValidator.HypeWords) + ".", line);
            Assert.Contains("settled", line);
            Assert.Contains("conclusively", line);
        }
    }

    [Fact]
    public void UserMessage_ShowsPeersOnlyByLetterAndGradersOnlyByRole()
    {
        string message = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        Assert.Contains("- {{peer:A}}: Comparable", message);
        Assert.Contains("- {{peer:B}}: Degraded; speed figures degraded", message);
        Assert.Contains("- Panel member B: same provider as the subject", message);
        Assert.DoesNotContain("GPT-5.2", message);
        Assert.DoesNotContain("OpenAI", message);
        Assert.DoesNotContain("Gemini", message);
        Assert.DoesNotContain("Google", message);
        Assert.DoesNotContain("Claude", message);
    }

    [Fact]
    public void UserMessage_CarriesRowsAndPerQuestionMaterial()
    {
        string message = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        Assert.Contains("R3 | kind: weakness | category: accuracy | questions: Q3 | status: Conflicting | support: Graders disagree | in 1 of 1 runs", message);
        Assert.Contains("raised only by panel member B, which shares the subject's provider", message);
        Assert.Contains("  panel member A: Unsafe prayer advice.", message);
        Assert.Contains("  panel member B: Reasonable prayer advice.", message);

        Assert.Contains("[Q2] band: Intermediate | score: 50 | peer mean: 72 | difference: -22 | critical error: no", message);
        Assert.Contains("[Q1] band: Simple | score: 90 | peer mean: 85 | difference: +5 | critical error: no", message);
        Assert.Contains("[Q4] band: Advanced | score: n/a | peer mean: n/a | difference: n/a", message);
        Assert.Contains("Which items in the inventory shown on the board are cursed and how can you tell?", message);
        Assert.Contains("The bag of holding and the ring are cursed; the altar test reveals it.", message);
        Assert.Contains("Drop items on an altar to see a black flash.", message);
        Assert.Contains("Misses that the ring on the board is already known to be cursed.", message);
        Assert.Contains("- supported: Dropping items on an altar shows a black flash for cursed items", message);
        Assert.True(message.IndexOf("[Q1]", StringComparison.Ordinal) < message.IndexOf("[Q2]", StringComparison.Ordinal));
    }

    [Fact]
    public void UserMessage_OfTheExecutiveSummary_AsksForNoQuestionNotesOrTopics()
    {
        string executive = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        Assert.DoesNotContain("QUESTIONS NEEDING A NOTE", executive);
        Assert.DoesNotContain("QUESTIONS NEEDING A TOPIC", executive);
    }

    [Fact]
    public void UserMessage_ASingleMemberRowOfAPanelRun_SaysWhichMemberRaisedIt()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Rows.Add(new BenchmarkReportFindingRow
        {
            Id = "R5", Kind = "weakness", Category = "tool_use", Questions = new List<int> { 2 },
            Status = "MemberAOnly", SupportLabel = BenchmarkReportFacts.SupportOneGraderDifferentProvider, MemberAText = "Few lookups."
        });

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.Contains("raised only by panel member B, which shares the subject's provider", message);
        Assert.Contains("raised only by panel member A, which does not share the subject's provider", message);
    }

    [Fact]
    public void UserMessage_LegacyAssessorRoleNames_StillNameTheSingleMember()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Graders[0].Role = "Assessor";
        sheet.Graders[1].Role = "Co-assessor";

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.Contains("raised only by the co-assessor, which shares the subject's provider", message);
    }

    [Fact]
    public void AStandaloneSheet_SaysThereAreNoPeers()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Peers.Clear();
        foreach (var q in sheet.Questions)
        {
            q.PeerMean = null;
            q.Difference = null;
        }

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.Contains("(no peers: this is a stand-alone run report", message);
        Assert.DoesNotContain("{{peer:A}}", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_AsksForUsEnglish_AndStatesTheStandaloneForm(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("Write in US English: color, behavior, analyze, center, gray, labeled, canceled.", system);
        Assert.Contains("stand-alone run report", system);
    }

    [Fact]
    public void ExecutiveSummary_StatesItsWordCaps()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;

        Assert.Contains("At most 90 words: what this means for use as a game assistant", system);
        Assert.Contains("At most 60 words: how reliable this result is", system);
        Assert.Contains("when a grader shares the subject's provider, say so in plain words and that it may read the subject more favorably", system);
        Assert.DoesNotContain("how confident are we", system);
        Assert.Contains("each at most 30 words", system);
    }

    [Fact]
    public void UserMessage_OmitsTheResponseStyleBlockWithoutTheFact()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Facts.RemoveAll(f => f.Key.Contains("responseStyleConflict", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("RESPONSE STYLE", Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_NamesTheGradersInOneVocabulary(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("Call the graders by the role names listed in GRADERS, in lower case: panel member A, panel member B, "
            + "the reference reader and the claim verifier (in a single-assessor run, the assessor and the second reader).", system);
        Assert.Contains("In the Executive Summary say 'one grader' or 'both graders' instead.", system);
        Assert.DoesNotContain("co-assessor", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("member A is the assessor", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A Conflicting row whose two member texts are about different things is not a disagreement about one finding; leave it out.", system);
    }

    [Fact]
    public void UserMessage_NamesTheGradersInTheSameVocabulary()
    {
        string message = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        Assert.Contains("GRADERS (by role; write each role name in lower case)", message);
        Assert.DoesNotContain("the co-assessor)", message);
        Assert.DoesNotContain("\n  member A:", message);
        Assert.DoesNotContain("\n  member B:", message);
    }

    [Fact]
    public void UserMessage_LeavesOutTheRubricsSourceParagraphs()
    {
        var content = ReportPackWriterTestData.Content();
        content.Runs[0].Questions[0].ExpectedPoints =
            "- A lawful character may receive Excalibur.\n\nSOURCE — fountain.c:212 dipfountain() rolls the chance.\nsee also artifact.c:88\n\n- The chance is one in six per dip.";

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, ReportPackWriterTestData.Sheet(), content).UserMessage;

        Assert.Contains("    - A lawful character may receive Excalibur.\n    \n    - The chance is one in six per dip.\n", message);
        Assert.DoesNotContain("SOURCE", message);
        Assert.DoesNotContain("fountain.c:212", message);
        Assert.DoesNotContain("artifact.c:88", message);

        Assert.Equal("Keep this.", BenchmarkReportPackPrompt.WithoutSourceParagraphs("SOURCE: a.c:1\nb.c:2\n\nKeep this."));
        Assert.Equal("Keep this.", BenchmarkReportPackPrompt.WithoutSourceParagraphs("Keep this.\n\nSOURCE — a.c:1"));
        Assert.Equal("No citation here.", BenchmarkReportPackPrompt.WithoutSourceParagraphs("No citation here."));
    }

    [Fact]
    public void UserMessage_GivesEachRulingItsRole_AndCountsRefutedAnswerSentences()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Questions[2].RefutedAnswerSentences = 1;
        var content = ReportPackWriterTestData.Content();
        content.Runs[0].Questions[2].ClaimRulings = new List<BenchmarkReportContentClaimRuling>
        {
            new() { Claim = "Praying now is safe.", Verdict = "refuted", Role = BenchmarkReportContent.ClaimRole },
            new() { Claim = "The prayer timeout is always one thousand turns.", Verdict = "refuted", Role = BenchmarkReportContent.AssessorStatementRole }
        };

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, content).UserMessage;

        Assert.Contains("| critical error: yes | refuted answer sentences: 1 |", message);
        Assert.Contains("| critical error: no | refuted claims: 0 |", message);
        Assert.Contains("    - Answer sentence — refuted: Praying now is safe.", message);
        Assert.Contains("    - Grader's statement — refuted (the verifier sided with the answer): The prayer timeout is always one thousand turns.", message);
    }

    [Fact]
    public void TheConfidenceSlot_LeavesTheIntervalToTheAppendedSentence_AndNeverCallsItsWidth()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;
        string confidence = system.Split('\n').Single(l => l.StartsWith("- confidence:", StringComparison.Ordinal));

        Assert.Contains("Code appends one sentence right after this paragraph that states the quality interval, its span and how many "
            + "questions the result rests on; do not restate any of them.", confidence);
        Assert.Contains("without calling the interval narrow, wide, tight or broad", confidence);
        Assert.DoesNotContain("{{quality.interval}}", confidence);
        Assert.DoesNotContain("{{quality.intervalSpan}}", confidence);
        Assert.DoesNotContain("width", confidence);
    }

    [Fact]
    public void UserMessage_QuotesTheExcerpt_NeverTheCompleteAnswer()
    {
        const string sentinel = "SENTINEL-AFTER-THE-CUT";
        var content = ReportPackWriterTestData.Content();
        var question = content.Runs[0].Questions[0];
        question.AnswerExcerptCut = true;
        question.AnswerText = question.AnswerExcerpt + " " + sentinel;

        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, ReportPackWriterTestData.Sheet(), content);

        Assert.Contains(question.AnswerExcerpt, prompt.UserMessage);
        Assert.DoesNotContain(sentinel, prompt.UserMessage);
        Assert.DoesNotContain(sentinel, prompt.SystemPrompt);
    }

    // Format version 7: the comparison and limitations slots, caps, readability, the peer block ----

    [Fact]
    public void ExecutiveSummary_AsksForTheComparisonSlot_ForPeersOnly()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;
        string comparison = system.Split('\n').Single(l => l.StartsWith("- comparison:", StringComparison.Ordinal));

        Assert.Contains("For peers only: write it only when PEERS lists peers, and leave the key out otherwise.", comparison);
        Assert.Contains("At most 70 words in one paragraph", comparison);
        Assert.Contains("whether that position is established", comparison);
        Assert.Contains("say so and that the order between them is not established", comparison);
        Assert.Contains("the paired-difference facts included", comparison);
        Assert.Contains("\"comparison\": \"Markdown paragraph, only when PEERS lists peers\"", system);
        Assert.Contains("a slot marked as for peers only is left out of a stand-alone run report", system);

        Assert.Equal(new[] { BenchmarkReportSlots.Comparison }, BenchmarkReportSlots.ExecutiveSummary.PeerOnlySlots);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheReadabilityRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("READABILITY:", system);
        Assert.Contains("- One idea per sentence, in sentences of at most about twenty-five words.", system);
        Assert.Contains("- Use the active voice.", system);
        Assert.Contains("- Prefer a count from the facts to vague words such as many or several.", system);
        Assert.Contains("rather than writing \"issues across many topics\"", system);
        foreach (string word in BenchmarkReportPackValidator.HypeWords)
        {
            Assert.Contains(word, system);
        }

        Assert.DoesNotContain("Lead with the action", system);

        if (audience == BenchmarkReportAudience.ExecutiveSummary)
        {
            Assert.Contains("Tone: plain US English in short sentences, with no jargon.", system);
        }
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheOverlapAndPairedDifferenceRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("must also say in the same sentence that the intervals overlap or that the order is not established", system);
        foreach (string word in new[] { "higher", "lower", "better", "worse", "ahead", "behind", "outperforms", "beats", "leads", "trails" })
        {
            Assert.Contains(word, system);
        }
        Assert.Contains("an estimate from question sampling only, not adjusted for comparing several models and not a significance test", system);
    }

    /// <summary>The writer test sheet with the subject's and each peer's explanation and per-peer facts.</summary>
    private static BenchmarkReportFactSheet PeerFactSheet()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.SubjectExplanation = "Comparable: every key outside the model axis matches the baseline.";
        sheet.Peers[1].Explanation = "Plotted with a degraded axis: quality is sound, and speed mixes conditions across the set.";
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.B.quality.index", Value = System.Text.Json.Nodes.JsonValue.Create(71.0), Display = "71 / 100" });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.A.quality.index", Value = System.Text.Json.Nodes.JsonValue.Create(83.0), Display = "83 / 100" });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "peer.A.intervalOverlap", Value = System.Text.Json.Nodes.JsonValue.Create(true), Display = "its 95 % interval overlaps the subject's" });
        sheet.Facts.Add(new BenchmarkReportFact
        {
            Key = "peer.A.pairedDifference", Display = BenchmarkReportFacts.NotAvailable, Available = false,
            UnavailableReason = "Fewer than five questions were scored for both this model and the subject on the same item revision, too few for a paired difference."
        });
        return sheet;
    }

    [Fact]
    public void UserMessage_ListsEachPeersExplanationAndFactKeys_TheSubjectsExplanation_AndTheNoSignificanceStatement()
    {
        string message = Build(BenchmarkReportAudience.ExecutiveSummary, PeerFactSheet()).UserMessage;

        Assert.Contains("State: Comparable\nExplanation: Comparable: every key outside the model axis matches the baseline.\n", message);
        Assert.Contains("- {{peer:A}}: Comparable\n  its facts (values under FACTS): peer.A.intervalOverlap, peer.A.pairedDifference, peer.A.quality.index\n", message);
        Assert.Contains("- {{peer:B}}: Degraded; speed figures degraded\n"
            + "  explanation: Plotted with a degraded axis: quality is sound, and speed mixes conditions across the set.\n"
            + "  its facts (values under FACTS): peer.B.quality.index\n", message);
        Assert.Contains("peer.A.quality.index = 83 / 100", message);
        Assert.Contains("peer.A.pairedDifference = unavailable: Fewer than five questions", message);
        Assert.Contains("NO SIGNIFICANCE TEST (the comparison's own statement; code prints it in the document)\nNo pairwise significance test is run.\n", message);
        Assert.True(message.IndexOf("PEERS", StringComparison.Ordinal) < message.IndexOf("NO SIGNIFICANCE TEST", StringComparison.Ordinal));
        Assert.True(message.IndexOf("NO SIGNIFICANCE TEST", StringComparison.Ordinal) < message.IndexOf("FACTS (", StringComparison.Ordinal));
    }

    [Fact]
    public void UserMessage_OfAStandaloneSheet_HasNoPeerBlockLinesAndNoSignificanceStatement()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Peers.Clear();

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.DoesNotContain("its facts (values under FACTS)", message);
        Assert.DoesNotContain("NO SIGNIFICANCE TEST", message);
        Assert.DoesNotContain("Explanation:", message);
    }

    [Fact]
    public void RepairMessage_RemindsOfTheOverlapAndHypeRules()
    {
        string message = BenchmarkReportPackPrompt.BuildRepairMessage(new List<BenchmarkReportValidationNote>());

        Assert.Contains("says that the intervals overlap or that the order is not established; where that peer's pairedExcludesZero fact is true, "
            + "it says instead that on the same questions the higher-scoring model scored higher on average and that the paired interval excludes zero. "
            + "No hype or filler words.", message);
        Assert.Contains("say only whether intervals overlap, or whether a paired interval excludes zero.", message);
        Assert.Contains("- Every question number, Q reference and evidence id must exist in the data. Every strength and weakness cites at least one evidence id.\n", message);
        Assert.DoesNotContain("recommendation", message);
        Assert.DoesNotContain("QUESTIONS NEEDING A NOTE", message);
        Assert.DoesNotContain("every lead", message);
    }

    // Report quality round: operator text, triage, paired result, unavailable and repeated facts ----

    [Theory]
    [MemberData(nameof(Audiences))]
    public void UserMessage_PrintsTheNoSignificanceSummaryOnly_NeverTheOperatorInstruction(BenchmarkReportAudience audience)
    {
        var prompt = Build(audience);

        Assert.Contains("No pairwise significance test is run.", prompt.UserMessage);
        Assert.DoesNotContain("Compare the intervals.", prompt.UserMessage);
        Assert.DoesNotContain("Compare the intervals.", prompt.SystemPrompt);
    }

    [Fact]
    public void UserMessage_GivesEachQuestionThePeerSpread_OnlyWhereThePeersAnsweredIt()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Questions[1].PeerMin = 64;
        sheet.Questions[1].PeerMax = 80;
        sheet.Questions[1].PeersAbove = 2;
        sheet.Questions[0].PeerMin = 80;
        sheet.Questions[0].PeerMax = 90;
        sheet.Questions[0].PeersAbove = 0;

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.Contains("[Q2] band: Intermediate | score: 50 | peer mean: 72 | difference: -22 | peers: min 64, max 80, 2 of 2 scored clearly higher | critical error: no", message);
        Assert.Contains("[Q1] band: Simple | score: 90 | peer mean: 85 | difference: +5 | peers: min 80, max 90, 0 of 2 scored clearly higher | critical error: no", message);
        Assert.Contains("[Q4] band: Advanced | score: n/a | peer mean: n/a | difference: n/a | critical error: no", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesThePeerTriageRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("\"peers: min …, max …, N of M scored clearly higher\"", system);
        Assert.Contains("scored more than five points above {{subject}}", system);
        Assert.Contains("Where most peers answered a question well and {{subject}} missed it, that is evidence about the subject model, "
            + "not about the chat, its tools, the corpus or the rubric. Where every model missed it, suspect the chat, its tools, the corpus or the rubric first.", system);
        Assert.Contains("strengths and weaknesses prefer points where {{subject}} differs from its peers", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheSharedWritingRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("- Never recommend training, fine-tuning or using outputs as training targets; recommend a lever the facts name — "
            + "a tool, a tool guide, the knowledge base, a wiki page, a rubric, the grading, or the model and its settings.\n", system);
        Assert.Contains("- Write 'disagreed' or 'disagreement' only for an answer the facts mark as a panel disagreement; "
            + "for any other gap, give both members' scores.\n", system);
        Assert.Equal(2, BenchmarkReportPackPrompt.SharedWritingRules.Count);
        Assert.All(BenchmarkReportPackPrompt.SharedWritingRules, rule => Assert.Contains("- " + rule + "\n", system));
        Assert.DoesNotContain("A recommendation for model developers names a general capability", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_TreatsAnNOfMToken_AsANounPhrase(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("- A token whose value reads 'N of M' is a noun phrase: '{{errors.critical}} had a critical error'. Never put it after 'no' "
            + "or make it the object of 'made'; to say none occurred, write 'no critical errors across all {{answers.scored}} answers'.\n", system);
    }

    [Fact]
    public void RepairMessage_RemindsOfTheNounPhraseRule()
    {
        string message = BenchmarkReportPackPrompt.BuildRepairMessage(new List<BenchmarkReportValidationNote>());

        Assert.Contains("A token whose value reads 'N of M' is a noun phrase; never put it after 'no' or make it the object of 'made'.", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void UserMessage_SaysTheBandsAreAssessed_AndWhereTheAuthoredOnesAre(BenchmarkReportAudience audience)
    {
        string message = Build(audience).UserMessage;

        Assert.Contains("FACTS (write {{key}} to place a figure; key = value as printed)\n"
            + "Difficulty bands are assessed difficulty; the authored bands are bands.authored.*.\n", message);
    }

    [Fact]
    public void FindingRows_ListTheSharedAndEachMembersOwnQuestions()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Rows.Add(new BenchmarkReportFindingRow
        {
            Id = "R5", Kind = "weakness", Category = "tool_use", Questions = new List<int> { 1, 2, 3 },
            Status = "Convergent", SupportLabel = BenchmarkReportFacts.SupportBothGraders,
            QuestionsA = new List<int> { 1, 2 }, QuestionsB = new List<int> { 1, 3 }, SharedQuestions = new List<int> { 1 },
            MemberAText = "Few lookups.", MemberBText = "Too few lookups."
        });
        sheet.Rows.Add(new BenchmarkReportFindingRow
        {
            Id = "R6", Kind = "weakness", Category = "calibration", Questions = new List<int>(),
            Status = "Convergent", SupportLabel = BenchmarkReportFacts.SupportBothGraders,
            QuestionsA = new List<int>(), QuestionsB = new List<int>(), SharedQuestions = new List<int>()
        });

        string message = Build(BenchmarkReportAudience.ExecutiveSummary, sheet).UserMessage;

        Assert.Contains("R5 | kind: weakness | category: tool_use | questions: Q1, Q2, Q3 | status: Convergent | support: Both graders | in 1 of 1 runs\n"
            + "  shared: Q1 | A only: Q2 | B only: Q3\n", message);
        // A run-wide row and a row stored without its members' questions list none.
        Assert.Contains("R6 | kind: weakness | category: calibration | questions: run-wide | status: Convergent | support: Both graders | in 1 of 1 runs\n\n", message);
        Assert.Contains("R1 | kind: strength | category: accuracy | questions: Q1 | status: Convergent | support: Both graders | in 1 of 1 runs\n"
            + "  panel member A: Correct item lore.", message);
        Assert.Null(BenchmarkReportPackPrompt.MemberQuestions(sheet.Rows[0]));
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_SaysAConvergentRowIsBothGradersOnlyOnItsSharedQuestions(BenchmarkReportAudience audience)
    {
        Assert.Contains("Both members raised a Convergent row only on its shared questions. An item resting only on its A only or B only "
            + "questions was raised by one grader, and its support label says so.", Build(audience).SystemPrompt);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesThePairedResultRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("Where the subject's and a peer's quality intervals overlap and that peer's pairedExcludesZero fact is not true, "
            + "say that the intervals overlap and that the order between them is not established", system);
        Assert.Contains("Where a peer's pairedExcludesZero fact is true, say that on the same questions the higher-scoring model scored higher on average "
            + "and that the paired interval excludes zero, not adjusted for comparing several models. "
            + "Never say for that pair that the order is not established, even where the intervals overlap.", system);
        Assert.Contains("- Never use the words significant, significantly or statistically", system);
        Assert.Contains("where that peer's pairedExcludesZero fact is true, it says instead that the paired interval excludes zero.", system);
    }

    [Fact]
    public void TheComparisonSlot_DefersToThePairedResult()
    {
        string system = Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt;
        string comparison = system.Split('\n').Single(l => l.StartsWith("- comparison:", StringComparison.Ordinal));

        Assert.Contains("unless that peer's pairedExcludesZero fact is true; then state the paired result as WEIGHING THE EVIDENCE describes.", comparison);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheUnavailableAndRepeatedValueRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("Mention an unavailable figure only where leaving it out would mislead the reader", system);
        Assert.DoesNotContain("If a fact is unavailable, say the figure is unavailable and why", system);
        Assert.Contains("In the prose, never write a fact key outside its {{key}} token, and never describe the facts list, the fact sheet or how the data was given to you.", system);
        Assert.Contains("State a value that several peers share once, for all of them; never list equal values one by one.", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_NeverAsksForGraderAgreementPerItem(BenchmarkReportAudience audience)
    {
        Assert.DoesNotContain("both graders agreed", Build(audience).SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // The stand-alone run prompt, pinned ---------------------------------------------------------

    private const string PromptSeparator = "\n===== USER MESSAGE =====\n";

    /// <summary>
    /// The run-completion documents' writer prompt, system prompt and user message, against a golden
    /// file of the stand-alone fixture; with <c>OVERSEER_UPDATE_GOLDENS=1</c> the file is written
    /// instead. The battery subject must leave it byte for byte as it is, and the stored prompt hash
    /// must be the hash of the pinned system prompt.
    /// </summary>
    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, "prompt_exec_standalone_run.txt")]
    public void TheStandaloneRunPrompt_IsPinned(BenchmarkReportAudience audience, string file)
    {
        var prompt = BenchmarkReportPackPrompt.Build(audience, BenchmarkReportPackFixture.StandaloneSheet(), BenchmarkReportPackFixture.Content());
        string text = prompt.SystemPrompt + PromptSeparator + prompt.UserMessage;

        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, text);
            return;
        }

        string golden = BenchmarkReportPackFixture.ReadGolden(file);
        Assert.Equal(golden, text);

        string system = golden[..golden.IndexOf(PromptSeparator, StringComparison.Ordinal)];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(system))), BenchmarkReportPackPrompt.PromptSha256(audience));
    }

    // A battery subject -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Audiences))]
    public void ABatterySubject_KeepsTheAudiencesSystemPrompt(BenchmarkReportAudience audience)
    {
        var battery = BenchmarkReportPackPrompt.Build(audience, BatteryReportFixture.Sheet(), BatteryReportFixture.Content());
        var run = BenchmarkReportPackPrompt.Build(audience, BenchmarkReportPackFixture.StandaloneSheet(), BenchmarkReportPackFixture.Content());

        Assert.Equal(run.SystemPrompt, battery.SystemPrompt);
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(audience),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(battery.SystemPrompt))));
    }

    [Fact]
    public void ABatteryUserMessage_ExplainsTheComposite_AndHowToReferToAQuestion()
    {
        string message = BenchmarkReportPackPrompt.Build(
            BenchmarkReportAudience.ExecutiveSummary, BatteryReportFixture.Sheet(), BatteryReportFixture.Content()).UserMessage;

        Assert.Contains("Kind: Battery\nRuns: 4\n", message);
        Assert.Contains("\nBATTERY\nName: Core knowledge (revision 2)\nSuites: 2; runs per suite: 2; member runs: 4\nWeighting scheme: Questions and difficulty\n", message);
        Assert.Contains("Never compare it with a single suite's Intelligence Index", message);
        Assert.Contains("- S1: Item lore, 2 questions\n- S2: Hazards, 2 questions\n", message);
        Assert.Contains("refer to a question as S<suite>-Q<n>, for example S2-Q7", message);
        Assert.Contains("give the question's number shown after \"number\" in its QUESTIONS row", message);
        Assert.Contains("(no peers: this is a stand-alone battery report", message);
        Assert.True(message.IndexOf("BATTERY\n", StringComparison.Ordinal) < message.IndexOf("GRADERS", StringComparison.Ordinal));
    }

    [Fact]
    public void ABatteryUserMessage_GivesEveryQuestionARow_AndTheDetailOnlyWhereItWasKept()
    {
        string message = BenchmarkReportPackPrompt.Build(
            BenchmarkReportAudience.ExecutiveSummary, BatteryReportFixture.Sheet(), BatteryReportFixture.Content()).UserMessage;

        Assert.Contains("[S1-Q1] number 1 | band: Simple | mean score: 90 | scored in 2 runs | critical errors: 0 | refuted answer sentences: 0 | tool calls: 2 | in detail\n"
            + "  Question as asked:\n    What happens if I throw a gem at a co-aligned unicorn?\n", message);
        Assert.Contains("[S1-Q2] number 2 | band: Intermediate | mean score: 72 | scored in 2 runs | critical errors: 0 | refuted answer sentences: 0 | tool calls: 3\n"
            + "[S2-Q1] number 3 | band: Intermediate | mean score: 25 | scored in 2 runs | critical errors: 1 | refuted answer sentences: 1 | tool calls: 5 | in detail\n", message);
        Assert.Contains("  Answer excerpt (the median-scoring round, run 21, cut):\n", message);
        Assert.Contains("  Grader comments (run 12):\n", message);
        Assert.DoesNotContain("How long is the prayer timeout", message);
        Assert.DoesNotContain("[Q1]", message);
        Assert.Equal(new[] { 1, 3 }, BenchmarkReportPackPrompt.QuestionsNeedingTopic(BatteryReportFixture.Sheet()));
        Assert.Contains("FINDING ROWS (cite as evidence by id)\n(none)\n", message);
    }

    [Fact]
    public void ABatteryQuestionsDetailLength_IsWhatItAddsToThePrompt()
    {
        var sheet = BatteryReportFixture.Sheet();
        var content = BatteryReportFixture.Content();
        string with = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, content).UserMessage;
        int length = BenchmarkReportPackPrompt.DetailBlockLength(sheet, content, 1);

        sheet.Questions.Single(q => q.Number == 1).Detailed = false;
        string without = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, content).UserMessage;

        Assert.True(length > 0);
        Assert.Equal(with.Length - without.Length, length);
        Assert.Equal(0, BenchmarkReportPackPrompt.DetailBlockLength(sheet, content, 2));
    }

    // Repair ------------------------------------------------------------------------------------

    [Fact]
    public void RepairMessage_QuotesEveryIssue()
    {
        var issues = new List<BenchmarkReportValidationNote>
        {
            new() { Rule = 3, Location = "sections.meaning[p2]", Message = "Contains the digit form \"80\"." },
            new() { Rule = 9, Location = "strengths[1]", Message = "Shares an eight-word run with the rubric of Q1." },
        };

        string message = BenchmarkReportPackPrompt.BuildRepairMessage(issues);

        Assert.Contains("- rule 3 at sections.meaning[p2]: Contains the digit form \"80\".", message);
        Assert.Contains("- rule 9 at strengths[1]: Shares an eight-word run with the rubric of Q1.", message);
        Assert.Contains("complete, corrected JSON object only", message);
        Assert.Contains("{{subject}}", message);
        Assert.Contains("{{peer:X}}", message);
    }

    // Comparison scope --------------------------------------------------------------------------

    public static TheoryData<BenchmarkReportAudience, string> ComparisonPromptFiles => new()
    {
        { BenchmarkReportAudience.ExecutiveSummary, "prompt_exec_comparison.txt" },
    };

    private static BenchmarkReportWriterPrompt ComparisonPrompt(BenchmarkReportAudience audience, bool subset = false)
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts(subset ? BenchmarkReportPackFixture.SubsetKeys : null);
        return BenchmarkReportPackPrompt.Build(audience, built.Sheet!, built.Content!);
    }

    /// <summary>
    /// The comparison-scope writer prompt of the five-model fixture, system prompt and user message,
    /// against a golden file; with <c>OVERSEER_UPDATE_GOLDENS=1</c> the file is written instead. The
    /// stored prompt hash is the hash of the pinned system prompt.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComparisonPromptFiles))]
    public void TheComparisonPrompt_IsPinned(BenchmarkReportAudience audience, string file)
    {
        var prompt = ComparisonPrompt(audience);
        string text = prompt.SystemPrompt + PromptSeparator + prompt.UserMessage;

        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, text);
            return;
        }

        string golden = BenchmarkReportPackFixture.ReadGolden(file);
        Assert.Equal(golden, text);

        string system = golden[..golden.IndexOf(PromptSeparator, StringComparison.Ordinal)];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(system))),
            BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.Comparison));
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheComparisonSystemPrompt_IsItsOwn_AndItsHashIsTheScopes(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience);

        Assert.Equal(system, ComparisonPrompt(audience).SystemPrompt);
        Assert.NotEqual(BenchmarkReportPackPrompt.PromptSha256(audience), BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.Comparison));
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(audience), BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.Model));
        Assert.Contains("{{model:X}}", system);
        Assert.Contains("There is no {{subject}} and no {{peer:X}} token.", system);
        foreach (string slot in BenchmarkReportSlots.For(audience, BenchmarkReportScope.Comparison).RequiredSlots)
        {
            Assert.Contains("\"" + slot + "\": \"Markdown paragraphs\"", system);
        }
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void BothWriterPrompts_AskForProviderErrorsAsObserved_WithTheirDenominator_NeverACause(BenchmarkReportAudience audience)
    {
        string run = BenchmarkReportPackPrompt.BuildSystemPrompt(audience);
        string comparison = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience);

        Assert.Contains("- Report provider errors (the availability facts) only as provider errors observed in GnollBench runs, always with "
            + "their denominator through {{availability.providerErrors}}", run);
        Assert.Contains("- Report provider errors (a model's availability facts) only as provider errors observed in GnollBench runs, always with "
            + "their denominator through that model's model.X.availability.providerErrors fact", comparison);
        foreach (string system in new[] { run, comparison })
        {
            Assert.Contains("never with a cause or an intent", system);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(system, "observed in GnollBench runs"));
        }
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void AComparisonPrompt_NamesNoModel_WholeOrSubset(BenchmarkReportAudience audience)
    {
        foreach (bool subset in new[] { false, true })
        {
            var prompt = ComparisonPrompt(audience, subset);
            string text = prompt.SystemPrompt + "\n" + prompt.UserMessage;
            foreach (var model in BenchmarkReportPackFixture.ComparisonModels)
            {
                Assert.DoesNotContain(model.Label, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(BenchmarkReportPackFixture.ModelIdOf(model.Label), text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(model.Provider, text, StringComparison.OrdinalIgnoreCase);
            }
            Assert.DoesNotContain("Gemini", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AComparisonPrompt_LettersAModelNamedInAnExcerptOrNote_AndWithholdsItsProvider()
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts();
        var run = built.Content!.Runs.First(r => r.Letter == "C");
        var item = run.Questions[0];
        item.AnswerExcerpt = "As orion max, made by Northwind, I compared myself with Vega Pro.";

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet!, built.Content!).UserMessage;

        Assert.Contains("As Model A, made by " + BenchmarkReportPackPrompt.ProviderWithheld + ", I compared myself with Model B.", message);

        // The sheet's own reasons name the models by letter: run 31 is Model A's.
        Assert.Contains("run #31 of Model A", message);
    }

    [Fact]
    public void AComparisonUserMessage_GivesItsBlocksInOrder_WithThePairedTestsInPlaceOfNoSignificance()
    {
        string message = ComparisonPrompt(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        var blocks = new[]
        {
            "\nCOMPARISON\n", "\nMODELS (", "\nGRADERS (", "\nPAIRED TESTS (", "\nFACTS (", "\nQUESTION MATRIX (", "\nQUESTIONS ("
        };
        var positions = blocks.Select(b => message.IndexOf(b, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);

        Assert.DoesNotContain("NO SIGNIFICANCE TEST", message);
        Assert.DoesNotContain("{{subject}}", message);
        Assert.DoesNotContain("{{peer:", message);
        Assert.Contains("- {{model:A}}: Comparable; 1 run; thinking level not set\n", message);
        Assert.Contains("Family \"reference\": {{model:A}} against each other model.", message);
        Assert.Contains("Family \"allPairs\": every pair of models.", message);
        Assert.Contains("- {{model:A}} and {{model:E}}: pair.A.E.*\n", message);
        Assert.Contains("[Q3] band: ", message);
        Assert.Matches(@"C: \d+, CE, ", message);
        Assert.Contains("Answer excerpt of {{model:C}} (run 33", message);
        Assert.Contains("model.A.quality.index = 85 / 100\n", message);
    }

    [Fact]
    public void ASubsetPrompt_LettersOnlyItsOwnModels()
    {
        string message = ComparisonPrompt(BenchmarkReportAudience.ExecutiveSummary, subset: true).UserMessage;

        Assert.Contains("Models: 2\n", message);
        Assert.Contains("{{model:B}}", message);
        Assert.DoesNotContain("{{model:C}}", message);
        Assert.DoesNotContain("model.C.", message);
        Assert.DoesNotContain("Family \"allPairs\"", message);
    }

    [Fact]
    public void TheComparisonUserMessage_NamesItsReference_TheHighestIndexOrTheBaselineModel()
    {
        string highest = ComparisonPrompt(BenchmarkReportAudience.ExecutiveSummary).UserMessage;
        Assert.Contains("Reference: {{model:A}}, the highest Intelligence Index. Every other model is set against it.\n", highest);
        Assert.DoesNotContain("Baseline model:", highest);

        var baseline = BenchmarkReportPackFixture.ComparisonFacts(referenceKey: BenchmarkReportPackFixture.BaselineKey, baselineKey: BenchmarkReportPackFixture.BaselineKey);
        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, baseline.Sheet!, baseline.Content!).UserMessage;
        Assert.Contains("Reference: {{model:C}}, the Overseer team's baseline model. Every other model is set against it.\n", message);
        Assert.Contains("Family \"reference\": {{model:C}} against each other model.", message);

        var other = BenchmarkReportPackFixture.ComparisonFacts(referenceKey: "run:32", baselineKey: BenchmarkReportPackFixture.BaselineKey);
        string chosen = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, other.Sheet!, other.Content!).UserMessage;
        Assert.Contains("Reference: {{model:B}}, chosen for this document.", chosen);
        Assert.Contains("Baseline model: {{model:C}}, the Overseer team's baseline model, is not the reference of this document.\n", chosen);

        // A sheet stored before the reference was kept reads as its first letter.
        var stored = BenchmarkReportPackFixture.ComparisonFacts().Sheet!;
        stored.ReferenceLetter = null;
        stored.ReferenceIsBaselineModel = null;
        Assert.Equal("Reference: {{model:A}}, the highest Intelligence Index. Every other model is set against it.",
            BenchmarkReportPackPrompt.ReferenceLine(stored, BenchmarkReportPackPrompt.OrderedPeers(stored.Peers)));
    }

    [Fact]
    public void TheComparisonUserMessage_GivesTheNotableFindings_AndTheWeakestAnswersByModel()
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts();
        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet!, built.Content!).UserMessage;

        Assert.Contains("\nNOTABLE FINDINGS (detected by code from the facts; cite one as {{notable.N.summary}}, explain it, and never restate it)\n", message);
        var first = built.Sheet!.NotableFindings![0];
        Assert.Contains("- {{notable.1.summary}} (" + BenchmarkNotableFindings.KindText(first.Kind) + "): " + first.Summary, message);
        Assert.Contains("\nWEAKEST ANSWERS BY MODEL (", message);
        Assert.Contains("{{model:C}}:\n  [Q3] " + BenchmarkReportFacts.AssessorRole
            + ", flagged a critical error: Says the thrown gem always breaks, which contradicts the rubric.\n", message);
        Assert.Contains("{{model:E}}:\n  [Q1] " + BenchmarkReportFacts.AssessorRole + ": Misses the alignment condition.\n", message);

        var blocks = new[] { "\nFACTS (", "\nNOTABLE FINDINGS (", "\nQUESTION MATRIX (", "\nWEAKEST ANSWERS BY MODEL (", "\nQUESTIONS (" };
        var positions = blocks.Select(b => message.IndexOf(b, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);

        // With nothing notable, the writer is told that none were detected.
        var quiet = BenchmarkReportPackFixture.ComparisonFacts(new[] { "run:32", "run:33" });
        string none = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, quiet.Sheet!, quiet.Content!).UserMessage;
        Assert.Contains("NOTABLE FINDINGS (detected by code from the facts; cite one as {{notable.N.summary}}, explain it, and never restate it)\n(none detected by the rules)\n", none);
    }

    [Fact]
    public void AModelNamedInAWeakestAnswersComment_IsLettered()
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts();
        built.Content!.ModelNotes![0].Comment = "Worse than Vega Pro from Southstar.";

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet!, built.Content).UserMessage;

        Assert.Contains("Worse than Model B from " + BenchmarkReportPackPrompt.ProviderWithheld + ".", message);
        Assert.DoesNotContain("Vega Pro", message);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void TheComparisonSystemPrompt_StatesTheFailureProfileRules_AndTheReferenceWithoutAssumingModelA(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(audience);

        Assert.Contains("\nFAILURE PROFILES, THE REFERENCE AND NOTABLE FINDINGS:\n", system);
        Assert.Contains("They describe what went wrong, not why.", system);
        Assert.Contains("Never explain a result by a model's training, size or design.", system);
        Assert.Contains("- Compare models with different numbers of answers through the per-20 and \"of\" facts, never raw counts.", system);
        Assert.Contains("Where every model shares it, suspect the chat, its tools, the corpus or the question first.", system);
        Assert.Contains("- Better, similar and worse question counts (model.X.vsReference.*) are not tests.", system);
        Assert.Contains("- Notable findings were detected by code from the facts. Do not restate them; explain them, or say the data does not.", system);
        Assert.Contains("the reference family tests the reference model named under COMPARISON against each other model", system);
        Assert.DoesNotContain("the reference family tests {{model:A}}", system);
    }

    [Fact]
    public void TheComparisonExecutiveSummary_AsksForTheAgainstReferenceSlot()
    {
        string executive = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(BenchmarkReportAudience.ExecutiveSummary);

        Assert.Contains("- againstReference: At most 100 words, ", executive);
        Assert.DoesNotContain("- notableFindings:", executive);
        Assert.DoesNotContain("- whyModelsDiffer:", executive);
    }

    [Fact]
    public void TheComparisonRepairMessage_RemindsOfTheModelToken()
    {
        var issues = new List<BenchmarkReportValidationNote> { new() { Rule = 2, Location = "headline", Message = "Unknown token {{subject}}." } };

        string message = BenchmarkReportPackPrompt.BuildRepairMessage(issues, comparisonScope: true);

        Assert.Contains("- rule 2 at headline: Unknown token {{subject}}.", message);
        Assert.Contains("{{model:X}}", message);
        Assert.DoesNotContain("{{peer:X}}", message);
        Assert.Equal(BenchmarkReportPackPrompt.BuildRepairMessage(issues), BenchmarkReportPackPrompt.BuildRepairMessage(issues, comparisonScope: false));
    }

    // Chat consistency scope --------------------------------------------------------------------

    public static TheoryData<BenchmarkReportAudience, string> ChatConsistencyPromptFiles => new()
    {
        { BenchmarkReportAudience.ExecutiveSummary, "prompt_chatconsistency_exec.txt" },
        { BenchmarkReportAudience.ProviderIssueReport, "prompt_chatconsistency_provider.txt" },
    };

    public static TheoryData<BenchmarkReportAudience> ChatConsistencyAudiences => new()
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.ProviderIssueReport,
    };

    private static BenchmarkReportWriterPrompt ChatConsistencyPrompt(BenchmarkReportAudience audience)
        => BenchmarkReportPackPrompt.Build(audience, ChatConsistencyReportTestData.Sheet(audience), ChatConsistencyReportTestData.Content());

    /// <summary>
    /// The chat consistency writer prompt of the claim-discipline fixture, system prompt and user
    /// message, against a golden file; with <c>OVERSEER_UPDATE_GOLDENS=1</c> the file is written
    /// instead. The stored prompt hash is the hash of the pinned system prompt.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChatConsistencyPromptFiles))]
    public void TheChatConsistencyPrompt_IsPinned(BenchmarkReportAudience audience, string file)
    {
        var prompt = ChatConsistencyPrompt(audience);
        string text = prompt.SystemPrompt + PromptSeparator + prompt.UserMessage;

        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, text);
            return;
        }

        string golden = BenchmarkReportPackFixture.ReadGolden(file);
        Assert.Equal(golden, text);

        string system = golden[..golden.IndexOf(PromptSeparator, StringComparison.Ordinal)];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(system))),
            BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency));
    }

    [Theory]
    [MemberData(nameof(ChatConsistencyAudiences))]
    public void TheChatConsistencySystemPrompt_LimitsRepetition_AndAsksForTheHoursOnlyWhereThePeriodsShareAny(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.BuildChatConsistencySystemPrompt(audience);

        Assert.Contains("REPETITION AND LENGTH:", system);
        Assert.Contains("appears at most once in the whole document, and never inside parentheses", system);
        Assert.Contains("Endpoints that share a status or a value get one sentence together", system);
        Assert.Contains("State an inconclusive endpoint's minimum detectable effect once in the document", system);
        Assert.Contains("prefer robustness.<check>.summary", system);
        Assert.Contains("When {{verdict.short}} reads Not enough evidence yet, write at most two sentences per slot.", system);
        Assert.Contains("Where {{scope.hours}} is available, every document cites it at least once.", system);
        Assert.Contains("never write that a result holds within no stratum", system);
        Assert.DoesNotContain("Every document cites {{scope.hours}} at least once.", system);
        Assert.Contains("Never write \"X and X, respectively\".", system);
        Assert.Contains("cite that fact instead of the two", system);
        Assert.Contains("A fact whose value is a sentence is written as a sentence of its own, or quoted inside yours without its final full stop; never continue a sentence after it with and.", system);
        Assert.Contains("Cite an interval (a ci95, ci90 or controlChangeCi95 fact) only in a sentence that also cites its estimate and names its measure.", system);
        Assert.Contains("never by a field name", system);
        Assert.Equal(audience == BenchmarkReportAudience.ExecutiveSummary,
            system.Contains("Quality is the battery Overall Index when one is given; a mean answer score is never called quality.", StringComparison.Ordinal));
        Assert.Equal(audience == BenchmarkReportAudience.ProviderIssueReport, system.Contains("{{period.baseline.window}}", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChatConsistencyRepairMessage_RemindsOfTheRepetitionAndSpliceRules()
    {
        string message = BenchmarkReportPackPrompt.BuildRepairMessage(new List<BenchmarkReportValidationNote>(), BenchmarkReportScope.ChatConsistency);

        Assert.Contains("never continue a sentence after it with and.", message);
        Assert.Contains("Never write \"X and X, respectively\"", message);
    }

    [Fact]
    public void WithoutCommonHours_TheClaimSupportAsksForEachPeriodsHours_AndAShortDocumentWhenEvidenceIsLacking()
    {
        var baseResult = ChatConsistencyReportTestData.Result();
        var result = baseResult with
        {
            Scope = new Overseer.Services.ChatConsistency.ChatConsistencyScope { Text = "no common time stratum" },
            Endpoints = baseResult.Endpoints.Where(e => e.Id is "P3" or "P5").ToList()
        };
        var sheet = BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary);

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, ChatConsistencyReportTestData.Content()).UserMessage;

        Assert.Contains("- Hours: scope.hours is unavailable because the periods ran at different hours; say so once in plain words, citing {{period.baseline.hours}} and {{period.comparison.hours}}.", message);
        Assert.Contains("- Length: verdict.short reads Not enough evidence yet, so write at most two sentences per slot.", message);
        Assert.DoesNotContain("every document cites {{scope.hours}}", message);
    }

    [Theory]
    [MemberData(nameof(ChatConsistencyAudiences))]
    public void TheChatConsistencySystemPrompt_IsItsOwn_AndItsHashIsTheScopes(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.BuildChatConsistencySystemPrompt(audience);

        Assert.Equal(system, ChatConsistencyPrompt(audience).SystemPrompt);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(system))),
            BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency));
        if (audience != BenchmarkReportAudience.ProviderIssueReport)
        {
            Assert.NotEqual(BenchmarkReportPackPrompt.PromptSha256(audience), BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency));
            Assert.NotEqual(BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.Comparison), BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency));
            Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(audience), BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.Model));
        }

        Assert.Contains("the Overseer chat with {{subject}}", system);
        Assert.Contains("{{scope.hours}}", system);
        foreach (string slot in BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency).RequiredSlots)
        {
            Assert.Contains("\"" + slot + "\": \"Markdown paragraphs\"", system);
            Assert.Contains("(\"" + BenchmarkReportSlots.ChatConsistencySlotTitles[slot] + "\")", system);
        }
        Assert.DoesNotContain("\"strengths\": [", system);
        Assert.Equal(audience == BenchmarkReportAudience.ProviderIssueReport, system.Contains("Under ruledOut, list every Overseer event", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ChatConsistencyAudiences))]
    public void TheChatConsistencySystemPrompt_NamesTheCanonicalTerms_AndTheBlocksEachSlotMustNotRestate(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.BuildChatConsistencySystemPrompt(audience);

        Assert.Contains("P1 Quality, P2 Time to first answer text, P3 Answer streaming rate, P4 Work per turn, P5 Cost per question and, under protocol V2, P6 Provider error rate", system);
        Assert.Contains("count in battery runs", system);
        Assert.Contains("GnollBench runs are made by hand", system);
        Assert.Contains("Printed before every slot: the overall verdict", system);
        Assert.Contains("interpret them, never restate them", system);
        Assert.DoesNotContain(" watch it", system);
        Assert.Equal(audience == BenchmarkReportAudience.ExecutiveSummary, system.Contains("This Executive Summary never cites a run id, a rule id or a hash", StringComparison.Ordinal));

        switch (audience)
        {
            case BenchmarkReportAudience.ExecutiveSummary:
                Assert.Contains("Lead with {{verdict.short}}", system);
                Assert.Contains("state the shortfall with {{sample.shortfall}}", system);
                Assert.Contains("- ourChanges (\"Our changes and their effect\"): ", system);
                Assert.Contains("Printed above it: one sentence listing the Overseer updates, followed by the table of them when there are more than three; interpret them, never restate them.", system);
                break;
        }
    }

    [Fact]
    public void AChatConsistencyUserMessage_KeepsTheInputHashFromTheWriter()
    {
        foreach (var audience in BenchmarkReportSlots.ChatConsistencyAudiences)
        {
            string message = ChatConsistencyPrompt(audience).UserMessage;

            Assert.DoesNotContain("analysis.inputSha256", message, StringComparison.Ordinal);
            Assert.DoesNotContain("0123456789abcdef", message, StringComparison.Ordinal);
            Assert.DoesNotContain("instrument", message, StringComparison.Ordinal);
            Assert.DoesNotContain("{\"", message, StringComparison.Ordinal);
            Assert.Contains("events.1.change = game snapshot: off → on\n", message);
            Assert.Contains("eventGroups.count = 2 Overseer updates\n", message);
            Assert.Contains("verdict.short = The chat changed\n", message);
        }
    }

    [Fact]
    public void ChatConsistencyPromptSha256_DiffersPerAudience()
    {
        var hashes = BenchmarkReportSlots.ChatConsistencyAudiences
            .Select(a => BenchmarkReportPackPrompt.PromptSha256(a, BenchmarkReportScope.ChatConsistency))
            .ToList();

        Assert.Equal(hashes.Count, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(hashes, h => Assert.Matches("^[0-9a-f]{64}$", h));
    }

    [Fact]
    public void AChatConsistencyUserMessage_ListsTheClaimSupport_AndNamesNoControlModel()
    {
        string message = ChatConsistencyPrompt(BenchmarkReportAudience.ProviderIssueReport).UserMessage;

        var blocks = new[] { "\nSUBJECT\n", "\nPEERS (", "\nFACTS (", "\nCLAIM SUPPORT (" };
        var positions = blocks.Select(b => message.IndexOf(b, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);

        Assert.Contains("- {{peer:A}}: its facts (values under FACTS): controls.1.model", message);
        Assert.Contains("- Results that show a change, for change words: verdict.overall (degraded), verdict.quality (degraded), endpoint.P1.* (degraded), endpoint.P2.* (equivalent), attribution.1.*, did.1.*", message);
        Assert.Contains("- Inconclusive endpoints, each needing its minimum detectable effect in the same section: endpoint.P5.* with endpoint.P5.mde\n", message);
        Assert.Contains("- Established grades, for public-claim words: endpoint.P2.grade\n", message);
        Assert.Contains("- Provider-confirmed causes, for mechanism words: annotation.1.*\n", message);
        Assert.Contains("- Provider-side attributions, for sentences about the model or its serving: attribution.1.*\n", message);
        Assert.Contains("- Overseer events to list under ruledOut: events.1.*, events.2.*\n", message);
        Assert.Contains("- Sample request ids for sampleRequestIds: requestIds.sample.1, requestIds.sample.2\n", message);
        Assert.Contains("Provider Issue Report: available", message);
        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlName, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlProvider, message, StringComparison.OrdinalIgnoreCase);

        string executive = ChatConsistencyPrompt(BenchmarkReportAudience.ExecutiveSummary).UserMessage;
        Assert.DoesNotContain("Overseer events to list under ruledOut", executive);
        Assert.DoesNotContain("requestIds.sample", executive);
    }

    [Fact]
    public void TheChatConsistencyRepairMessage_RemindsOfTheClaimRules_AndTheOtherScopesKeepTheirs()
    {
        var issues = new List<BenchmarkReportValidationNote>
        {
            new() { Rule = BenchmarkReportPackValidator.ChatChangeClaimRule, Location = "sections.asGoodAsBefore[p1]", Message = "C1: Uses \"degraded\" without a result that shows a change." }
        };

        string message = BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.ChatConsistency);

        Assert.Contains("- rule 22 at sections.asGoodAsBefore[p1]: C1: Uses \"degraded\" without a result that shows a change.", message);
        Assert.Contains("{{scope.hours}}", message);
        Assert.Contains("{{peer:X}}", message);
        Assert.Equal(BenchmarkReportPackPrompt.BuildRepairMessage(issues), BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.Model));
        Assert.Equal(BenchmarkReportPackPrompt.BuildRepairMessage(issues, comparisonScope: true), BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.Comparison));
    }
}
