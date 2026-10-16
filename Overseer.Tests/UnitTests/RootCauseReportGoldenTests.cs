namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The Root Cause Analysis documents and prompts against their golden files: comparison scope named and
/// anonymized, a per-model pack, a run completion with and without controls, and a battery completion;
/// then the structure every one of them must hold whatever the golden says.
/// </summary>
public class RootCauseReportGoldenTests
{
    private const string PromptSeparator = "\n===== USER MESSAGE =====\n";

    private static readonly BenchmarkReportRenderOptions FullNamed = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
    private static readonly BenchmarkReportRenderOptions FullAnonymized = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized };

    private static void AssertGolden(string file, string text)
    {
        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, text);
            return;
        }
        Assert.Equal(BenchmarkReportPackFixture.ReadGolden(file), text);
    }

    private static BenchmarkReportDocument Document(string name) => name switch
    {
        "comparison" => RootCauseReportFixture.ComparisonDocument(),
        "pack" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack),
        "runWithControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion),
        "runNoControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: false), BenchmarkReportDocumentOrigin.RunCompletion),
        "battery" => RootCauseReportFixture.BatteryDocument(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("comparison", false, "comparison_rca_full_named.md")]
    [InlineData("comparison", true, "comparison_rca_full_anonymized.md")]
    [InlineData("pack", false, "rca_full_named.md")]
    [InlineData("runWithControls", false, "rca_run_with_controls.md")]
    [InlineData("runNoControls", false, "rca_run_no_controls.md")]
    [InlineData("battery", false, "rca_battery_standalone.md")]
    public void TheRootCauseAnalysis_MatchesItsGoldenFile(string name, bool anonymized, string file)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(name), anonymized ? FullAnonymized : FullNamed);

        AssertGolden(file, text);
    }

    [Theory]
    [InlineData("comparison")]
    [InlineData("pack")]
    [InlineData("runWithControls")]
    [InlineData("runNoControls")]
    [InlineData("battery")]
    public void EveryRootCauseAnalysis_HasItsSectionsInOrder_ThenItsAppendices(string name)
    {
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            Charts = BenchmarkReportChartPlacement.ServerFigureKeys.Select(key => new BenchmarkReportRenderChart
            {
                FigureKey = key,
                Title = "Chart " + key,
                Caption = "Caption of " + key,
                AltText = "Alt text of " + key,
                Png = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                WidthPx = 1600,
                HeightPx = 900,
                Sha256 = new string('0', 64)
            }).ToList()
        };
        var document = Document(name);
        string text = BenchmarkReportPackRenderer.Render(document, options);

        // Appendix E is there only when the analysis lists a count or a configuration fact; without it the letters close up.
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        bool knownFacts = sheet.RootCause!.KnownFacts.Any(k => k.Kind is RootCauseKnownFacts.KindCount or RootCauseKnownFacts.KindConfiguration);
        var headings = new List<string>
        {
            "Summary", "Why each model scored as it did", "Scope and design", "Evidence: intelligence", "Evidence: speed", "Evidence: cost",
            "Hypotheses", "Conclusions", "What to run next", "Limitations and threats to validity", "Appendices",
            "Appendix A: Intelligence detail", "Appendix B: Speed and cost decompositions", "Appendix C: Hypothesis details",
            "Appendix D: Run settings"
        };
        headings.AddRange(knownFacts
            ? new[] { "Appendix E: Known facts", "Appendix F: Reproducibility", "Appendix G: Question details", "Appendix H: Glossary" }
            : new[] { "Appendix E: Reproducibility", "Appendix F: Question details", "Appendix G: Glossary" });
        headings.AddRange(new[] { "Fact sheet", "Evaluation terms" });
        if (!knownFacts)
        {
            Assert.DoesNotContain("## Appendix E: Known facts", text);
            Assert.DoesNotContain("- **E. Known facts:**", text);
        }
        int last = -1;
        foreach (string heading in headings)
        {
            int at = text.IndexOf("\n## " + heading + "\n", StringComparison.Ordinal);
            Assert.True(at > last, heading + " is missing or out of order");
            last = at;
        }
        foreach (string retired in new[] { "## The root causes in one sentence", "## Known facts", "## Threats to validity", "## Next runs and checks", "## Reproducibility appendix" })
        {
            Assert.DoesNotContain(retired + "\n", text);
        }

        // Every figure is in the body, before the appendices.
        int appendices = text.IndexOf("\n## Appendices\n", StringComparison.Ordinal);
        foreach (string key in BenchmarkReportChartPlacement.ServerFigureKeys)
        {
            int at = text.IndexOf("[[figure:" + key + "]]", StringComparison.Ordinal);
            Assert.True(at > 0 && at < appendices, key + " is missing or not in the body");
        }
        Assert.Contains("INTERNAL", text);
    }

    [Fact]
    public void TheQuestionDetails_ListWhatWasDisputed_AndCountTheSupportedSentences()
    {
        var document = Document("comparison");
        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
        foreach (var item in content.Runs.SelectMany(r => r.Questions))
        {
            item.Graders = new List<BenchmarkReportContentGrader>
            {
                new()
                {
                    Role = "Panel member A", Label = "Gemini 3.8 Flash", Score = 80, Comment = "Mostly right.",
                    Evidence = new List<string> { "Accuracy: Matches rubric.", " completeness: matches RUBRIC. ", "Completeness: Omits the Luck adjustment." }
                }
            };
            item.ClaimRulings = new List<BenchmarkReportContentClaimRuling>
            {
                new() { Claim = "First supported sentence.", Verdict = "supported", Role = BenchmarkReportContent.ClaimRole },
                new() { Claim = "Second supported sentence.", Verdict = "supported", Role = null },
                new() { Claim = "A refuted sentence.", Verdict = "refuted", Role = BenchmarkReportContent.ClaimRole, Rationale = "The source says otherwise." },
                new() { Claim = "An accused but true sentence.", Verdict = "supported", Role = BenchmarkReportContent.AccusedSentenceRole }
            };
        }
        document.ContentJson = BenchmarkReportJson.Serialize(content);

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);
        string appendix = Section(text, "## Appendix G: Question details");

        // Only the questions with an answer given in detail, each answer as the excerpt the writer saw.
        Assert.Contains("The excerpts were given in rounds, one per question in each round and at most three per question", appendix);
        Assert.DoesNotContain("No answer to this question was given as an excerpt.", appendix);
        Assert.True(appendix.Contains("**Answer excerpt:**\n", StringComparison.Ordinal) || appendix.Contains("**Answer:**\n", StringComparison.Ordinal));
        foreach (var item in content.Runs.SelectMany(r => r.Questions).Where(q => q.AnswerText != null))
        {
            Assert.DoesNotContain(item.AnswerText!, appendix);
        }

        Assert.Contains("**Claim verifier:**", text);
        Assert.DoesNotContain("  - Accuracy: Matches rubric.", text);
        Assert.DoesNotContain("  - completeness: matches RUBRIC.", text);
        Assert.Contains("  - Completeness: Omits the Luck adjustment.", text);
        Assert.Contains("Mostly right.", text);
        Assert.DoesNotContain("First supported sentence.", text);
        Assert.DoesNotContain("Second supported sentence.", text);
        Assert.Contains("\"A refuted sentence.\" — The source says otherwise.", text);
        Assert.Contains("\"An accused but true sentence.\"", text);
        Assert.Contains("- 2 further answer sentences supported.", text);
    }

    [Fact]
    public void TheSummary_PutsTheQuestionsInCallouts()
    {
        var document = Document("comparison");
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);
        string summary = Section(text, "## Summary");

        Assert.Contains("### What we found\n\n> [!IMPORTANT]\n> **Key finding.** ", summary);
        Assert.Contains("### How sure are we?\n\n> [!WARNING]\n> **Caveat.** How sure each answer is. **Established (High)**: ", summary);
        Assert.Contains("> Each model ran only once per suite, so this document cannot tell whether its results would repeat. "
            + "No cause can be graded above Probable (Moderate) until a second run of each suite is made, apart from a price effect, "
            + "which follows from the price cards.\n", summary);
        Assert.Contains(sheet.RootCause!.Hypotheses, h => h.Id == "HC2" && h.Grade != RootCauseGrades.NotSupported);
        Assert.Contains("| Model | Intelligence | Median time | Cost per question | Where it lost most points | Flags |\n", summary);

        var questions = sheet.RootCause!.Questions;
        Assert.Equal(new[] { 1, 2, 3, 4 }, questions.Select(q => q.Number));
        Assert.Equal(questions.Count, CountOf(summary, "> [!NOTE]\n> **Question.** **Why "));
        foreach (var question in questions)
        {
            // Question 1 without a comparative cause states its measured lead; question 4 from the price cards says so.
            string strength = question.Number == 1 && question.ComparativeKeys != null && question.Grade == null
                              && (question.WithinKeys ?? new List<string>()).Count == 0 && question.OrderEstablished.HasValue
                ? "Measured lead over "
                : question.PriceCard
                    ? RootCauseGrades.PriceCardWord + " — see HC2."
                    : question.HypothesisKeys.Count > 0
                        ? RootCauseGrades.WordWithGrade(question.Grade)
                        : "No hypothesis about this model fired — see its profile.";
            int at = summary.IndexOf("> **Question.** **" + NamedQuestion(sheet, question.Text) + "**\n>\n> *Answer strength: "
                + strength, StringComparison.Ordinal);
            Assert.True(at >= 0, question.Text);
            string callout = summary[at..summary.IndexOf("\n\n", at, StringComparison.Ordinal)];
            Assert.Contains(">\n> For ", callout);
            Assert.Contains(" the measured signals are ", callout);
            Assert.All(callout.Split('\n'), line => Assert.StartsWith(">", line));
        }

        // One model on its own has no question to answer.
        string alone = BenchmarkReportPackRenderer.Render(Document("runNoControls"), FullNamed);
        Assert.DoesNotContain("### The questions", alone);
        Assert.Contains("> [!IMPORTANT]\n> **Key finding.** ", alone);
    }

    [Fact]
    public void AnOldRootCauseAnalysis_SaysToRegenerate_InItsAnswerCallouts()
    {
        var document = Document("comparison");
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        foreach (string slot in BenchmarkReportSlots.RootCauseAnswerSlots) writer.Sections.Remove(slot);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        document.ReportFormatVersion = 15;

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);
        string summary = Section(text, "## Summary");

        Assert.Equal(4, CountOf(summary, "> *Written before format 16; regenerate to get the answers.*\n"));
        Assert.DoesNotContain("*Not written.*", summary);

        // A current document missing one answer says it was not written.
        var current = Document("comparison");
        var partial = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(current.WriterOutputJson);
        partial.Sections.Remove(BenchmarkReportSlots.AnswerCost);
        current.WriterOutputJson = BenchmarkReportJson.Serialize(partial);
        string missing = Section(BenchmarkReportPackRenderer.Render(current, FullNamed), "## Summary");
        Assert.Equal(1, CountOf(missing, "> *Not written.*\n"));
        Assert.DoesNotContain("Written before format 16", missing);
    }

    [Theory]
    [InlineData("comparison", false)]
    [InlineData("comparison", true)]
    [InlineData("pack", false)]
    [InlineData("runWithControls", false)]
    [InlineData("runNoControls", false)]
    [InlineData("battery", false)]
    public void TheProfileSection_HoldsNoCausalWord(string name, bool anonymized)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(name), anonymized ? FullAnonymized : FullNamed);
        string profiles = Section(text, "## Why each model scored as it did");

        Assert.Contains("| Model | Index | Strengths | Lost points on | Flags |\n", profiles);
        Assert.Empty(Overseer.Services.Benchmarking.RootCause.RootCauseClaimSupport.CausalPhrases(profiles));
    }

    [Fact]
    public void TheAvailabilityThreats_AreGroupedByReason()
    {
        var document = Document("comparison");
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        foreach (var peer in sheet.Peers)
        {
            sheet.Facts.Add(new BenchmarkReportFact
            {
                Key = BenchmarkComparisonReportFacts.ModelPrefix(peer.Letter) + "availability.attempts",
                Display = BenchmarkReportFacts.NotAvailable,
                Available = false,
                UnavailableReason = BenchmarkReportFacts.AvailabilityNotRecordedReason
            });
        }
        sheet.Facts = sheet.Facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);
        string threats = Section(text, "## Limitations and threats to validity");

        Assert.Contains("- Provider availability is not recorded for any of the 5 models (runs before harness 56), so provider errors cannot be "
            + "separated from failures on Overseer's side.\n", threats);
        Assert.DoesNotContain("Provider availability of ", threats);
        Assert.Equal(1, CountOf(threats, "Provider availability"));

        // The setup says it once too, without a table of rows that all say "not recorded".
        string scope = Section(text, "## Scope and design");
        Assert.Contains("Provider availability is not recorded for any of the 5 models (runs before harness 56).\n", scope);
        Assert.DoesNotContain("### Provider availability", scope);
    }

    [Fact]
    public void RemovedContent_PrintsTheRemovedText_AtFullDisclosure()
    {
        var document = Document("comparison");
        document.Status = BenchmarkReportDocumentStatus.CompletedWithWarnings;
        document.ValidationNotesJson = BenchmarkReportJson.Serialize(new List<BenchmarkReportValidationNote>
        {
            new()
            {
                Rule = 3,
                Location = "sections.conclusions[p1]",
                Message = "Holds the digit 7.",
                Dropped = true,
                Text = "{{model:B}} answered 7 questions well, as Vega Pro did before."
            }
        });

        string named = Section(BenchmarkReportPackRenderer.Render(document, FullNamed), "## Removed content");
        Assert.Contains("- **Conclusions, paragraph 1** (rule 3, Digit in prose): Holds the digit 7.\n", named);
        Assert.Contains("  - *Removed text:* Vega Pro answered 7 questions well, as Vega Pro did before.\n", named);

        string anonymized = Section(BenchmarkReportPackRenderer.Render(document, FullAnonymized), "## Removed content");
        Assert.Contains("  - *Removed text:* Model B answered 7 questions well, as Model B did before.\n", anonymized);
        foreach (var m in BenchmarkReportPackFixture.ComparisonModels)
        {
            Assert.DoesNotContain(m.Label, anonymized);
            Assert.DoesNotContain(m.Provider, anonymized);
        }
    }

    /// <summary>The text from <paramref name="heading"/> to the next level-two heading.</summary>
    private static string Section(string text, string heading)
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, heading + " is missing");
        int end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..(end + 1)];
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>A question's text as a named copy prints it: each <c>Model X</c> by its label.</summary>
    private static string NamedQuestion(BenchmarkReportFactSheet sheet, string text)
    {
        foreach (var peer in sheet.Peers.OrderByDescending(p => p.Letter.Length))
        {
            text = text.Replace("Model " + peer.Letter + " ", peer.Label + " ", StringComparison.Ordinal);
        }
        return text;
    }

    [Fact]
    public void TheComparisonFailureProfile_IsTwoTables_OfAtMostNineColumnsEach()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullNamed);

        int start = text.IndexOf("Failure profile", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string section = text[start..];
        section = section[..section.IndexOf("\n#", 1, StringComparison.Ordinal)];
        Assert.Contains("**Outcomes**", section);
        Assert.Contains("**Signals**", section);

        // A header row is a table row followed by its delimiter row.
        var lines = section.Split('\n');
        var headerRows = lines
            .Where((line, i) => line.StartsWith("|", StringComparison.Ordinal)
                                && i + 1 < lines.Length
                                && System.Text.RegularExpressions.Regex.IsMatch(lines[i + 1], @"^\|\s*:?-"))
            .ToList();
        Assert.Equal(2, headerRows.Count);
        Assert.All(headerRows, row => Assert.True(row.Count(c => c == '|') - 1 <= 9, row));
    }

    [Fact]
    public void TheAnonymizedComparison_NamesNoModel()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullAnonymized);

        foreach (var m in BenchmarkReportPackFixture.ComparisonModels)
        {
            Assert.DoesNotContain(m.Label, text);
            Assert.DoesNotContain(m.Provider, text);
        }
    }

    [Fact]
    public void ARunWithoutControls_SaysNoOtherModelAnsweredTheQuestions_AndProposesAControlRun()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("runNoControls"), FullNamed);

        Assert.Contains("no other model answered these questions", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Control run", text);
    }

    [Fact]
    public void ARunWithControls_NamesThem()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("runWithControls"), FullNamed);

        Assert.Contains("Vega Pro (high)", text);
        Assert.Contains("Zeta Prime (high)", text);
    }

    [Fact]
    public void TheControlsTable_NamesEachControlsThinkingLevelOnce()
    {
        var document = Document("runWithControls");
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var controls = sheet.RootCause!.Controls.OrderBy(c => c.Number).ToList();
        Assert.Equal("Vega Pro (high)", controls[0].ModelLabel);
        controls[1].ModelLabel = "Zeta Prime";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);

        Assert.Contains("| Vega Pro (high) | Southstar |", text);
        Assert.Contains("| Zeta Prime (high) | Midland |", text);
        Assert.DoesNotContain("(high) (high)", text);
    }

    [Fact]
    public void TheRootCauseAnalysis_RendersOnlyAtFullDisclosure()
    {
        Assert.Equal(new[] { BenchmarkReportDisclosure.Full }, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.RootCauseAnalysis));
    }

    [Theory]
    [InlineData(true, "prompt_rca_comparison.txt")]
    [InlineData(false, "prompt_rca_standalone_run.txt")]
    public void TheRootCausePrompt_IsPinned(bool comparison, string file)
    {
        var sheet = comparison ? RootCauseReportFixture.ComparisonFacts().Sheet! : RootCauseReportFixture.RunSheet(controls: false);
        var content = comparison
            ? RootCauseReportFixture.ComparisonFacts().Content!
            : BenchmarkReportContent.Build(new[] { RootCauseReportFixture.Runs()[RootCauseReportFixture.SubjectRunId] }, 120);
        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.RootCauseAnalysis, sheet, content);
        string text = prompt.SystemPrompt + PromptSeparator + prompt.UserMessage;

        AssertGolden(file, text);
        if (BenchmarkReportPackFixture.UpdateGoldens) return;

        var scope = comparison ? BenchmarkReportScope.Comparison : BenchmarkReportScope.Model;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt.SystemPrompt))),
            BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.RootCauseAnalysis, scope));
        Assert.Contains("rca.axis.speed.status", prompt.UserMessage);
        Assert.Contains("CLAIM SUPPORT", prompt.UserMessage);
    }

    [Fact]
    public void TheExecutiveSummaryPrompt_SeesNoRootCauseFact()
    {
        var built = RootCauseReportFixture.ComparisonFacts();
        Assert.Contains(built.Sheet!.Facts, f => f.Key.StartsWith("rca.", StringComparison.Ordinal));

        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet!, built.Content!);

        Assert.DoesNotContain("rca.", prompt.UserMessage);
        Assert.DoesNotContain(".speed.ttftP90", prompt.UserMessage);
    }
}
