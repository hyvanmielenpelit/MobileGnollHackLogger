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
}
