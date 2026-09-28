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
        Assert.Contains("verifier refuted is the verifier's advisory finding", system);
        Assert.Contains("verifier supported is a fact of the game", system);
        Assert.Contains("response-style conflict", system);
        Assert.Contains("Never re-grade", system);
        Assert.Contains("never invent a cause", system);
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void SystemPrompt_StatesTheNoSignificanceNoQuotingAndFormatRules(BenchmarkReportAudience audience)
    {
        string system = Build(audience).SystemPrompt;

        Assert.Contains("runs no significance test", system);
        Assert.Contains("say they overlap", system);
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
        Assert.Contains("- Co-assessor: same provider as the subject", message);
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
        Assert.Contains("raised only by the Co-assessor, which shares the subject's provider", message);
        Assert.Contains("  member B: Reasonable prayer advice.", message);

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
    public void UserMessage_OmitsTheResponseStyleBlockWithoutTheFact()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        sheet.Facts.RemoveAll(f => f.Key.Contains("responseStyleConflict", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("RESPONSE STYLE", Build(BenchmarkReportAudience.TechnicalReport, sheet).UserMessage);
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
