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
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief,
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

    [Fact]
    public void TechnicalReport_AsksOnlyForItsSlotsAndLists()
    {
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;

        Assert.Contains("\"abstract\"", system);
        Assert.Contains("\"whyItScored\"", system);
        Assert.Contains("\"whatWorked\"", system);
        Assert.Contains("\"recommendations\"", system);
        Assert.Contains("\"questionNotes\"", system);
        Assert.Contains("\"questionTopics\"", system);
        Assert.Contains(BenchmarkReportSlots.TargetModelDevelopers, system);
        Assert.Contains("every question of the exam", system);
        Assert.Contains("domain knowledge", system);
        Assert.Contains("calibration", system);

        Assert.DoesNotContain("\"meaning\"", system);
        Assert.DoesNotContain("overseerChat", system);
        Assert.DoesNotContain(BenchmarkReportSlots.TargetOverseerChat, system);
        Assert.DoesNotContain("\"leads\"", system);
        Assert.DoesNotContain("triage", system);
    }

    [Fact]
    public void InternalBrief_AsksOnlyForItsSlotsAndLists()
    {
        string system = Build(BenchmarkReportAudience.InternalBrief).SystemPrompt;

        Assert.Contains("\"overseerChat\"", system);
        Assert.Contains("\"benchmarkSystem\"", system);
        Assert.Contains("\"modelResult\"", system);
        Assert.Contains("\"recommendations\"", system);
        Assert.Contains("\"questionNotes\"", system);
        Assert.Contains("\"leads\"", system);
        Assert.Contains(BenchmarkReportSlots.TargetOverseerChat, system);
        Assert.Contains("\"benchmark\"", system);
        Assert.Contains(BenchmarkReportSlots.TargetModelDevelopers, system);
        Assert.Contains("harness | suite | chat | corpus", system);
        Assert.Contains("never findings", system);

        Assert.DoesNotContain("\"abstract\"", system);
        Assert.DoesNotContain("\"meaning\"", system);
        Assert.DoesNotContain("whyItScored", system);
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

    [Fact]
    public void PromptSha256_DiffersPerAudience()
    {
        var hashes = new[]
        {
            BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.ExecutiveSummary),
            BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.TechnicalReport),
            BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.InternalBrief),
        };

        Assert.Equal(3, hashes.Distinct(StringComparer.Ordinal).Count());
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
        string message = Build(BenchmarkReportAudience.TechnicalReport).UserMessage;

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
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;

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

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.TechnicalReport, sheet, built.Content!, null).UserMessage;

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
            + "End with one sentence naming a default choice for a typical Overseer player — who asks during play and waits for each answer — and the case in which another model is the better choice. "
            + "Base it only on established results and the frontier facts; where nothing separates the models on any measure, say that the choice is open.", slot);
    }

    [Fact]
    public void LeadsInBothPrompts_NameTheMostSpecificTargetTheDataShows()
    {
        const string target = "Name the most specific target the data shows: the question and its topic, and what to look at there — the rubric point a grader charged, "
            + "the knowledge source an answer excerpt relied on, the grading role that disagreed, or the kind of tool call the matrix shows. "
            + "Never name a file, setting or tool the data does not show.";

        string comparison = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(BenchmarkReportAudience.InternalBrief);
        string comparisonLeads = comparison.Split('\n').Single(l => l.StartsWith("- leads:", StringComparison.Ordinal));
        Assert.Contains("Lead with the action and its target, then the evidence. A lead is provisional and un-triaged, never a finding: "
            + "phrase it as something to check, and name the most specific target the data shows: the question and its topic, and what to look at there — the rubric point a grader charged, "
            + "the knowledge source an answer excerpt relied on, the grading role that disagreed, or the kind of tool call the matrix shows. "
            + "Never name a file, setting or tool the data does not show.", comparisonLeads);
        Assert.DoesNotContain("phrase each as something to check.", comparisonLeads);

        string system = Build(BenchmarkReportAudience.InternalBrief).SystemPrompt;
        string leads = system.Split('\n').Single(l => l.StartsWith("- leads:", StringComparison.Ordinal));
        Assert.Contains("phrase each as something to check, not as a conclusion. " + target + " Where the subject has peers,", leads);
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
        string message = Build(BenchmarkReportAudience.InternalBrief).UserMessage;

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
        string message = Build(BenchmarkReportAudience.TechnicalReport).UserMessage;

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
    public void UserMessage_NamesTheQuestionsNeedingNotesAndTopics()
    {
        string technical = Build(BenchmarkReportAudience.TechnicalReport).UserMessage;
        string executive = Build(BenchmarkReportAudience.ExecutiveSummary).UserMessage;

        Assert.Contains("QUESTIONS NEEDING A NOTE: Q2, Q3", technical);
        Assert.Contains("QUESTIONS NEEDING A TOPIC: Q1, Q2, Q3, Q4", technical);
        Assert.DoesNotContain("QUESTIONS NEEDING A NOTE", executive);
        Assert.DoesNotContain("QUESTIONS NEEDING A TOPIC", executive);
        Assert.Equal(new[] { 2, 3 }, BenchmarkReportPackPrompt.QuestionsNeedingNote(ReportPackWriterTestData.Sheet()));
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

        string message = Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage;

        Assert.Contains("raised only by panel member B, which shares the subject's provider", message);
        Assert.Contains("raised only by panel member A, which does not share the subject's provider", message);
    }

    [Fact]
    public void UserMessage_LegacyAssessorRoleNames_StillNameTheSingleMember()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Graders[0].Role = "Assessor";
        sheet.Graders[1].Role = "Co-assessor";

        string message = Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage;

        Assert.Contains("raised only by the co-assessor, which shares the subject's provider", message);
    }

    [Fact]
    public void AStandaloneSheet_SaysThereAreNoPeers_AndNotesWeakQuestionsByScore()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Peers.Clear();
        foreach (var q in sheet.Questions)
        {
            q.PeerMean = null;
            q.Difference = null;
        }

        string message = Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage;

        Assert.Contains("(no peers: this is a stand-alone run report", message);
        Assert.DoesNotContain("{{peer:A}}", message);
        // Q2 scored 50, which is not below 50; Q3 carries a critical error.
        Assert.Equal(new[] { 3 }, BenchmarkReportPackPrompt.QuestionsNeedingNote(sheet));

        sheet.Questions[1].Score = 49;
        Assert.Equal(new[] { 2, 3 }, BenchmarkReportPackPrompt.QuestionsNeedingNote(sheet));
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
    public void ResearcherReport_AsksForEvidencedRecommendations_AndUsesItsNewName()
    {
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;

        Assert.Contains("DOCUMENT: Report for AI Researchers and Developers.", system);
        Assert.Contains("recommendations: at most six items", system);
        Assert.Contains("Name the change proposed and, in a few words, the weakness it answers; do not restate the weakness.", system);
        Assert.Contains("Every recommendation cites at least one evidence id", system);
        Assert.DoesNotContain("A recommendation may cite evidence as well.", system);

        Assert.Contains("A recommendation may cite evidence as well.", Build(BenchmarkReportAudience.InternalBrief).SystemPrompt);
    }

    [Fact]
    public void UserMessage_OmitsTheResponseStyleBlockWithoutTheFact()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Facts.RemoveAll(f => f.Key.Contains("responseStyleConflict", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("RESPONSE STYLE", Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage);
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
        string message = Build(BenchmarkReportAudience.TechnicalReport).UserMessage;

        Assert.Contains("GRADERS (by role; write each role name in lower case)", message);
        Assert.DoesNotContain("the co-assessor)", message);
        Assert.DoesNotContain("\n  member A:", message);
        Assert.DoesNotContain("\n  member B:", message);
    }

    [Fact]
    public void ResearcherReport_SlotsExplainPatterns_AndLeaveTheListsToThemselves()
    {
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;

        Assert.Contains("- whyItScored: Explain the patterns and causes across the weaknesses, grouped by category", system);
        Assert.Contains("in at most 300 words.", system);
        Assert.Contains("The weaknesses list is printed right after this text; do not restate its items.", system);
        Assert.Contains("- whatWorked: Explain the patterns and causes across the strengths, grouped by category, in at most 150 words.", system);
        Assert.Contains("The strengths list is printed right after this text; do not restate its items.", system);
        Assert.Contains("Do not list claims the claim verifier refuted here", system);
    }

    [Fact]
    public void UserMessage_LeavesOutTheRubricsSourceParagraphs()
    {
        var content = ReportPackWriterTestData.Content();
        content.Runs[0].Questions[0].ExpectedPoints =
            "- A lawful character may receive Excalibur.\n\nSOURCE — fountain.c:212 dipfountain() rolls the chance.\nsee also artifact.c:88\n\n- The chance is one in six per dip.";

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.TechnicalReport, ReportPackWriterTestData.Sheet(), content).UserMessage;

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

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.TechnicalReport, sheet, content).UserMessage;

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
    public void TheAbstract_NeverListsTheClaimVerifiersRefutations()
    {
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;
        string abstractSlot = system.Split('\n').Single(l => l.StartsWith("- abstract:", StringComparison.Ordinal));

        Assert.Contains("Do not list claims the claim verifier refuted here; they belong in the weaknesses, attributed to the claim verifier.", abstractSlot);
        Assert.DoesNotContain("Name every error", system);
    }

    [Fact]
    public void UserMessage_QuotesTheExcerpt_NeverTheCompleteAnswer()
    {
        const string sentinel = "SENTINEL-AFTER-THE-CUT";
        var content = ReportPackWriterTestData.Content();
        var question = content.Runs[0].Questions[0];
        question.AnswerExcerptCut = true;
        question.AnswerText = question.AnswerExcerpt + " " + sentinel;

        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.InternalBrief, ReportPackWriterTestData.Sheet(), content);

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

        Assert.DoesNotContain("\"comparison\"", Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt);
        Assert.DoesNotContain("\"comparison\"", Build(BenchmarkReportAudience.InternalBrief).SystemPrompt);
        Assert.Equal(new[] { BenchmarkReportSlots.Comparison }, BenchmarkReportSlots.ExecutiveSummary.PeerOnlySlots);
        Assert.Empty(BenchmarkReportSlots.TechnicalReport.PeerOnlySlots);
        Assert.Empty(BenchmarkReportSlots.InternalBrief.PeerOnlySlots);
    }

    [Fact]
    public void TechnicalReport_AsksForTheLimitationsSlot_AndListsTheCodeLinesItMustNotRestate()
    {
        string system = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;
        string limitations = system.Split('\n').Single(l => l.StartsWith("- limitations:", StringComparison.Ordinal));

        Assert.Contains("At most 120 words, printed as the last paragraph of Threats to validity", limitations);
        Assert.Contains("a degraded peer, a subject or peer with a single run, heavy grader disagreement on particular questions, or a difficulty band with few questions", limitations);
        Assert.Contains("single-turn questions under one chat configuration", limitations);
        Assert.Contains("which sources of variation the interval covers", limitations);
        Assert.Contains("the graders are AI models", limitations);
        Assert.Contains("do not restate any of them", limitations);
        Assert.DoesNotContain("For peers only", limitations);
        Assert.Contains("\"limitations\": \"Markdown paragraphs\"", system);

        Assert.DoesNotContain("\"limitations\"", Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt);
    }

    [Fact]
    public void InternalBrief_StatesItsWordAndItemCaps()
    {
        string system = Build(BenchmarkReportAudience.InternalBrief).SystemPrompt;

        Assert.Contains("- overseerChat: At most 200 words. The brief's first part", system);
        Assert.Contains("- benchmarkSystem: At most 150 words. The brief's second part", system);
        Assert.Contains("- modelResult: At most 150 words. The brief's third part", system);
        Assert.Contains("- recommendations: at most eight concrete next steps.", system);
        Assert.Contains("- leads: at most six things worth checking", system);
        Assert.Contains("- strengths: at most eight items", system);
        Assert.Contains("- weaknesses: at most eight items", system);
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

        if (audience == BenchmarkReportAudience.InternalBrief)
        {
            Assert.Contains("- Lead with the action, then the evidence.", system);
        }
        else
        {
            Assert.DoesNotContain("Lead with the action", system);
        }

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

    [Fact]
    public void PromptSha256_CoversTheNewInstructions()
    {
        // The hash is of the whole system prompt, so each audience's new slot and rules are inside it.
        foreach (var (audience, marker) in new[]
        {
            (BenchmarkReportAudience.ExecutiveSummary, "- comparison:"),
            (BenchmarkReportAudience.TechnicalReport, "- limitations:"),
            (BenchmarkReportAudience.InternalBrief, "- Lead with the action, then the evidence.")
        })
        {
            string system = BenchmarkReportPackPrompt.BuildSystemPrompt(audience);
            Assert.Contains(marker, system);
            Assert.Contains("READABILITY:", system);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(system))), BenchmarkReportPackPrompt.PromptSha256(audience));

            string withoutReadability = system[..system.IndexOf("READABILITY:", StringComparison.Ordinal)];
            Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(withoutReadability))), BenchmarkReportPackPrompt.PromptSha256(audience));
        }
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
    public void RepairMessage_RemindsOfTheOverlapHypeAndNoteRules()
    {
        string message = BenchmarkReportPackPrompt.BuildRepairMessage(new List<BenchmarkReportValidationNote>());

        Assert.Contains("says that the intervals overlap or that the order is not established; where that peer's pairedExcludesZero fact is true, "
            + "it says instead that on the same questions the higher-scoring model scored higher on average and that the paired interval excludes zero. "
            + "No hype or filler words.", message);
        Assert.Contains("say only whether intervals overlap, or whether a paired interval excludes zero.", message);
        Assert.Contains("A recommendation for model_developers concerns only what a model developer can change in the model", message);
        Assert.Contains("Every question under QUESTIONS NEEDING A NOTE gets a note", message);
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

        string message = Build(BenchmarkReportAudience.InternalBrief, sheet).UserMessage;

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

    [Fact]
    public void TheSlotsThatTriage_ApplyThePeerRule()
    {
        string brief = Build(BenchmarkReportAudience.InternalBrief).SystemPrompt;
        string Slot(string system, string name) => system.Split('\n').Single(l => l.StartsWith("- " + name + ":", StringComparison.Ordinal));

        Assert.Contains("a question most peers answered well is evidence about the model, not the chat", Slot(brief, "overseerChat"));
        Assert.Contains("suspect a question or its rubric first when every model missed it", Slot(brief, "benchmarkSystem"));
        Assert.Contains("a \"chat\", \"corpus\" or \"suite\" lead rests on questions the peers missed as well", Slot(brief, "leads"));

        string researcher = Build(BenchmarkReportAudience.TechnicalReport).SystemPrompt;
        Assert.Contains("tell a miss of the model from one every model shared", Slot(researcher, "whyItScored"));
    }

    [Fact]
    public void TheModelResultSlot_LeavesTheIntervalToTheAppendedSentence()
    {
        string system = Build(BenchmarkReportAudience.InternalBrief).SystemPrompt;
        string modelResult = system.Split('\n').Single(l => l.StartsWith("- modelResult:", StringComparison.Ordinal));

        Assert.Contains("Code appends one sentence right after this paragraph that states the quality interval, its span and what it rests on; do not restate it.", modelResult);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
    public void ModelDeveloperRecommendations_ConcernOnlyTheModel(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("- A recommendation for model developers names a general capability a model developer can train or tune — for example "
            + "stating the decisive mechanic behind a verdict, or committing to a conclusion the inputs already settle. Never a GnollHack fact, "
            + "a change to the assistant's prompt or tools, or a rubric point; game-specific gaps are leads of the Internal Brief (`corpus` or `chat`).\n", system);
        Assert.DoesNotContain("its knowledge, calibration", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheSharedWritingRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("- Outside a recommendation for model developers, never recommend training, fine-tuning or using outputs as training targets; recommend a lever the facts name — "
            + "a tool, a tool guide, the knowledge base, a wiki page, a rubric, the grading, or the model and its settings.\n", system);
        Assert.Contains("- Write 'disagreed' or 'disagreement' only for an answer the facts mark as a panel disagreement; "
            + "for any other gap, give both members' scores.\n", system);
        Assert.Contains("- When an answer repeats a tool result the facts show to be wrong, write the lead about the source of that result "
            + "(tag `corpus`), not about the model's knowledge.\n", system);
        Assert.Equal(3, BenchmarkReportPackPrompt.SharedWritingRules.Count);
        Assert.All(BenchmarkReportPackPrompt.SharedWritingRules, rule => Assert.Contains("- " + rule + "\n", system));
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void ABatterySubject_StatesTheSharedWritingRules(BenchmarkReportAudience audience)
    {
        string system = BenchmarkReportPackPrompt.Build(audience, BatteryReportFixture.Sheet(), BatteryReportFixture.Content()).SystemPrompt;

        Assert.All(BenchmarkReportPackPrompt.SharedWritingRules, rule => Assert.Contains("- " + rule + "\n", system));
    }

    [Fact]
    public void ExecutiveSummary_HasNoRecommendationRule()
    {
        Assert.DoesNotContain("A recommendation for model developers names a general capability", Build(BenchmarkReportAudience.ExecutiveSummary).SystemPrompt);
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
    public void RepairMessage_RemindsOfTheNounPhraseAndGameFactRules()
    {
        string message = BenchmarkReportPackPrompt.BuildRepairMessage(new List<BenchmarkReportValidationNote>());

        Assert.Contains("never a GnollHack fact or the Overseer's prompts", message);
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

        string message = Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage;

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
    [InlineData(BenchmarkReportAudience.TechnicalReport, "prompt_technical_standalone_run.txt")]
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
            BenchmarkReportAudience.TechnicalReport, BatteryReportFixture.Sheet(), BatteryReportFixture.Content()).UserMessage;

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
            BenchmarkReportAudience.TechnicalReport, BatteryReportFixture.Sheet(), BatteryReportFixture.Content()).UserMessage;

        Assert.Contains("[S1-Q1] number 1 | band: Simple | mean score: 90 | scored in 2 runs | critical errors: 0 | refuted answer sentences: 0 | tool calls: 2 | in detail\n"
            + "  Question as asked:\n    What happens if I throw a gem at a co-aligned unicorn?\n", message);
        Assert.Contains("[S1-Q2] number 2 | band: Intermediate | mean score: 72 | scored in 2 runs | critical errors: 0 | refuted answer sentences: 0 | tool calls: 3\n"
            + "[S2-Q1] number 3 | band: Intermediate | mean score: 25 | scored in 2 runs | critical errors: 1 | refuted answer sentences: 1 | tool calls: 5 | in detail\n", message);
        Assert.Contains("  Answer excerpt (the median-scoring round, run 21, cut):\n", message);
        Assert.Contains("  Grader comments (run 12):\n", message);
        Assert.DoesNotContain("How long is the prayer timeout", message);
        Assert.DoesNotContain("[Q1]", message);
        Assert.Contains("QUESTIONS NEEDING A NOTE: S2-Q1\n", message);
        Assert.Contains("QUESTIONS NEEDING A TOPIC: S1-Q1, S2-Q1\n", message);
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
        { BenchmarkReportAudience.TechnicalReport, "prompt_technical_comparison.txt" },
        { BenchmarkReportAudience.InternalBrief, "prompt_internal_comparison.txt" },
    };

    private static BenchmarkReportWriterPrompt ComparisonPrompt(BenchmarkReportAudience audience, bool subset = false,
        IReadOnlyList<BenchmarkReportQuestionTopic>? sharedTopics = null)
    {
        var built = BenchmarkReportPackFixture.ComparisonFacts(subset ? BenchmarkReportPackFixture.SubsetKeys : null);
        return BenchmarkReportPackPrompt.Build(audience, built.Sheet!, built.Content!, sharedTopics);
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

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.InternalBrief, built.Sheet!, built.Content!, null).UserMessage;

        Assert.Contains("As Model A, made by " + BenchmarkReportPackPrompt.ProviderWithheld + ", I compared myself with Model B.", message);

        // The sheet's own reasons name the models by letter: run 31 is Model A's.
        Assert.Contains("run #31 of Model A", message);
    }

    [Fact]
    public void AComparisonUserMessage_GivesItsBlocksInOrder_WithThePairedTestsInPlaceOfNoSignificance()
    {
        string message = ComparisonPrompt(BenchmarkReportAudience.TechnicalReport).UserMessage;

        var blocks = new[]
        {
            "\nCOMPARISON\n", "\nMODELS (", "\nGRADERS (", "\nPAIRED TESTS (", "\nFACTS (", "\nQUESTION MATRIX (", "\nQUESTIONS (", "\nQUESTIONS NEEDING A TOPIC: "
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
    public void TopicsAlreadyWritten_AreGiven_InPlaceOfTheTopicRequest()
    {
        var shared = new List<BenchmarkReportQuestionTopic> { new() { Question = 1, Topic = "Throwing gems" } };

        string message = ComparisonPrompt(BenchmarkReportAudience.InternalBrief, sharedTopics: shared).UserMessage;

        Assert.Contains("QUESTION TOPICS (already written for this comparison; use them, and leave \"questionTopics\" empty)\n- Q1: Throwing gems\n", message);
        Assert.DoesNotContain("QUESTIONS NEEDING A TOPIC", message);
        Assert.Contains("QUESTIONS NEEDING A TOPIC: Q1, Q2, Q3, Q4, Q5, Q6", ComparisonPrompt(BenchmarkReportAudience.InternalBrief).UserMessage);
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
        { BenchmarkReportAudience.TechnicalReport, "prompt_chatconsistency_technical.txt" },
        { BenchmarkReportAudience.InternalBrief, "prompt_chatconsistency_internal.txt" },
        { BenchmarkReportAudience.ProviderIssueReport, "prompt_chatconsistency_provider.txt" },
    };

    public static TheoryData<BenchmarkReportAudience> ChatConsistencyAudiences => new()
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief,
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

        Assert.Contains("P1 Quality, P2 Time to first answer text, P3 Answer streaming rate, P4 Work per turn and P5 Cost per question", system);
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
                Assert.Contains("Printed above it: one sentence listing the Overseer updates; interpret them, never restate them.", system);
                break;
            case BenchmarkReportAudience.TechnicalReport:
                Assert.Contains("Printed above it: the table of Overseer updates", system);
                Assert.Contains("Printed above it: the robustness table", system);
                Assert.Contains("Never cite a hash.", system);
                break;
            case BenchmarkReportAudience.InternalBrief:
                Assert.Contains("Never recommend acting on a secondary difference whose 95 % interval includes zero.", system);
                Assert.Contains("the first action is to re-grade every compared run with one common grader", system);
                Assert.Contains("state the shortfall with {{sample.shortfall}}", system);
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
