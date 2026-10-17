namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The options of the shared comparison renderer blocks the Root Cause Analysis uses: the question details' order, cap
/// and compact grader format, the reproducibility appendix's shared hashes, and the grader findings table's differing
/// rows. Each option defaults to the output without it.
/// </summary>
public class BenchmarkReportComparisonOptionsTests
{
    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] Letters = { "A", "B", "C" };

    private static BenchmarkReportPeer Peer(string letter) => new()
    {
        Letter = letter,
        EntryKey = "run:" + letter,
        Label = "Model " + letter + " name",
        DisplayName = "Model " + letter + " name",
        Provider = "Provider" + letter,
        ModelId = "model-" + letter.ToLowerInvariant(),
        ThinkingLevel = "medium",
        State = "Comparable"
    };

    private static BenchmarkReportFact MakeFact(string key, string display, double? value = null) => new()
    {
        Key = key,
        Display = display,
        Value = value.HasValue ? JsonValue.Create(value.Value) : null
    };

    private static BenchmarkReportFactSheet Sheet(int models, IEnumerable<BenchmarkReportFact>? facts = null) => new()
    {
        Scope = BenchmarkReportFactSheet.ComparisonScopeValue,
        Peers = Letters.Take(models).Select(Peer).ToList(),
        Graders = new List<BenchmarkReportGrader>
        {
            new() { Role = BenchmarkReportFacts.PanelMemberARole, Label = "Grader A", Provider = "GraderCo", ModelId = "grader-a" },
            new() { Role = BenchmarkReportFacts.PanelMemberBRole, Label = "Grader B", Provider = "OtherCo", ModelId = "grader-b" },
            new() { Role = BenchmarkReportFacts.ReferenceReaderRole, Label = "Reader", Provider = "ThirdCo", ModelId = "reader" }
        },
        Facts = (facts ?? Enumerable.Empty<BenchmarkReportFact>()).OrderBy(f => f.Key, StringComparer.Ordinal).ToList()
    };

    private static BenchmarkReportPackRenderer.Context Context(BenchmarkReportFactSheet sheet, BenchmarkReportContentSnapshot? content = null) => new()
    {
        Document = new BenchmarkReportDocument
        {
            Audience = BenchmarkReportAudience.RootCauseAnalysis,
            Scope = BenchmarkReportScope.Comparison,
            ReportFormatVersion = BenchmarkReportPackRenderer.ComparisonReportFormatVersion
        },
        Options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named
        },
        Sheet = sheet,
        Content = content ?? new BenchmarkReportContentSnapshot(),
        Writer = new BenchmarkReportWriterOutput(),
        Notes = new List<BenchmarkReportValidationNote>()
    };

    private static BenchmarkReportContentGrader Grader(string role, int? score, string? comment = null, params string[] evidence) => new()
    {
        Role = role,
        Label = role + " model",
        Score = score,
        Comment = comment,
        Evidence = evidence.ToList()
    };

    /// <summary>Questions 1 to <paramref name="questions"/>, each answered by model A in run 11 with the given graders.</summary>
    private static BenchmarkReportContentSnapshot Content(int questions, Func<int, List<BenchmarkReportContentGrader>> graders)
    {
        var numbers = Enumerable.Range(1, questions).ToList();
        return new BenchmarkReportContentSnapshot
        {
            Questions = numbers.Select(n => new BenchmarkReportContentQuestion
            {
                Number = n,
                QuestionText = "Question text " + n,
                ExpectedPoints = "Rubric point " + n,
                ExpectedPointsRecorded = true
            }).ToList(),
            Runs = new List<BenchmarkReportContentRun>
            {
                new()
                {
                    RunId = 11,
                    Letter = "A",
                    EntryKey = "run:A",
                    Questions = numbers.Select(n => new BenchmarkReportContentQuestion
                    {
                        Number = n,
                        AnswerExcerpt = "Answer excerpt " + n,
                        Graders = graders(n)
                    }).ToList()
                }
            }
        };
    }

    private static List<BenchmarkReportContentGrader> Agreeing(int _) => new()
    {
        Grader(BenchmarkReportFacts.PanelMemberARole, 95, "Member A comment.", "Accuracy: Matches rubric.", "Completeness: Misses the altar."),
        Grader(BenchmarkReportFacts.PanelMemberBRole, 92, "Member B comment."),
        Grader(BenchmarkReportFacts.ReferenceReaderRole, 93, "Reader comment.")
    };

    private static (string Text, IReadOnlyList<int> LeftOut) Details(
        BenchmarkReportContentSnapshot content, bool compactGraders = false, IReadOnlyList<int>? order = null, int? maxQuestions = null,
        IReadOnlySet<int>? only = null)
    {
        var sb = new StringBuilder();
        var leftOut = BenchmarkReportPackRenderer.ComparisonQuestionDetails(
            sb, Context(Sheet(1), content), grading: true, level: 2, only: only,
            compactGraders: compactGraders, order: order, maxQuestions: maxQuestions);
        return (sb.ToString(), leftOut);
    }

    // ---------------------------------------------------------------------------------------------
    // Question details
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void QuestionDetails_ByDefault_ListEveryGraderInFull_ByNumber_AndLeaveNothingOut()
    {
        var (text, leftOut) = Details(Content(3, Agreeing));

        Assert.Empty(leftOut);
        Assert.Contains("- **Panel member A (Panel member A model):** Score 95. Member A comment.", text);
        Assert.Contains("  - Completeness: Misses the altar.", text);
        Assert.DoesNotContain("Accuracy: Matches rubric.", text);
        Assert.Contains("- **Reference reader (Reference reader model):** Score 93. Reader comment.", text);
        Assert.DoesNotContain("(panel disagreement)", text);
        Assert.DoesNotContain("panel member B 92", text);
        Assert.True(text.IndexOf("### Q1", StringComparison.Ordinal) < text.IndexOf("### Q2", StringComparison.Ordinal));
        Assert.True(text.IndexOf("### Q2", StringComparison.Ordinal) < text.IndexOf("### Q3", StringComparison.Ordinal));
    }

    [Fact]
    public void QuestionDetails_WithAnOrderAndACap_DetailTheFirstInThatOrder_AndReturnTheRest()
    {
        var (text, leftOut) = Details(Content(4, Agreeing), order: new[] { 3, 1, 4, 2 }, maxQuestions: 2);

        Assert.Contains("### Q3", text);
        Assert.Contains("### Q1", text);
        Assert.DoesNotContain("### Q4", text);
        Assert.DoesNotContain("### Q2", text);
        Assert.True(text.IndexOf("### Q3", StringComparison.Ordinal) < text.IndexOf("### Q1", StringComparison.Ordinal));
        Assert.Equal(new[] { 4, 2 }, leftOut.ToArray());
    }

    [Fact]
    public void QuestionDetails_AnOrderThatListsSomeQuestions_PutsTheOthersAfter_ByNumber()
    {
        var (text, leftOut) = Details(Content(3, Agreeing), order: new[] { 2 });

        Assert.Empty(leftOut);
        int q1 = text.IndexOf("### Q1", StringComparison.Ordinal);
        int q2 = text.IndexOf("### Q2", StringComparison.Ordinal);
        int q3 = text.IndexOf("### Q3", StringComparison.Ordinal);
        Assert.True(q2 >= 0 && q2 < q1 && q1 < q3);
    }

    [Fact]
    public void QuestionDetails_ACapAboveTheCount_LeavesNothingOut_AndTheOnlySetStillApplies()
    {
        var (text, leftOut) = Details(Content(3, Agreeing), maxQuestions: 10, only: new HashSet<int> { 1, 3 });

        Assert.Empty(leftOut);
        Assert.Contains("### Q1", text);
        Assert.Contains("### Q3", text);
        Assert.DoesNotContain("### Q2", text);
    }

    [Fact]
    public void QuestionDetails_ACapOfZero_PrintsNothing_AndReturnsEveryQuestion()
    {
        var (text, leftOut) = Details(Content(2, Agreeing), maxQuestions: 0);

        Assert.Equal(string.Empty, text);
        Assert.Equal(new[] { 1, 2 }, leftOut.ToArray());
    }

    [Fact]
    public void CompactGraders_WhenThePanelAgrees_PrintOneScoresLine_AndNoComment()
    {
        var (text, _) = Details(Content(1, Agreeing), compactGraders: true);

        Assert.Contains("**Graders:** Panel member A 95, panel member B 92, reference reader 93.", text);
        Assert.DoesNotContain("Member A comment.", text);
        Assert.DoesNotContain("Member B comment.", text);
        Assert.DoesNotContain("Reader comment.", text);
        Assert.DoesNotContain("Misses the altar.", text);
        Assert.DoesNotContain("(panel disagreement)", text);
        Assert.Contains("only for a panel disagreement", text);
        Assert.Contains("**Claim verifier:**", text);
    }

    [Fact]
    public void CompactGraders_WhenThePanelScoresAreMoreThanTheThresholdApart_MarkADisagreement_AndPrintTheMembersNotes()
    {
        int apart = BenchmarkService.SecondOpinionDisagreementPoints + 1;
        var (text, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 90, "Member A comment.", "Accuracy: Matches rubric.", "Completeness: Misses the altar."),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 90 - apart, "Member B comment."),
            Grader(BenchmarkReportFacts.ReferenceReaderRole, 85, "Reader comment.")
        }), compactGraders: true);

        Assert.Contains("#### Model A name, run 11 (panel disagreement)", text);
        Assert.Contains("- **Panel member A (Panel member A model):** Member A comment.", text);
        Assert.Contains("  - Completeness: Misses the altar.", text);
        Assert.DoesNotContain("Accuracy: Matches rubric.", text);
        Assert.Contains("- **Panel member B (Panel member B model):** Member B comment.", text);
        Assert.DoesNotContain("Reader comment.", text);
    }

    [Fact]
    public void CompactGraders_AtExactlyTheThreshold_IsNoDisagreement()
    {
        int threshold = BenchmarkService.SecondOpinionDisagreementPoints;
        var (text, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 90, "Member A comment."),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 90 - threshold, "Member B comment.")
        }), compactGraders: true);

        Assert.DoesNotContain("(panel disagreement)", text);
        Assert.DoesNotContain("Member A comment.", text);
    }

    [Fact]
    public void CompactGraders_ACriticalErrorFlaggedByOneMember_IsADisagreement_AndNamesTheGraderAndQuote()
    {
        var (text, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 40, "Member A comment.", "Critical error: \"Pray at any altar.\""),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 50, "Member B comment.")
        }), compactGraders: true);

        Assert.Contains("(panel disagreement)", text);
        Assert.Contains("- **Critical error (panel member A):** \"Pray at any altar.\"", text);
        Assert.Contains("Member A comment.", text);
        Assert.Equal(1, CountOf(text, "Pray at any altar."));
    }

    [Fact]
    public void CompactGraders_ACriticalErrorBothMembersFlagged_IsNoDisagreement_ButEachFlagIsPrinted()
    {
        var (text, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 40, "Member A comment.", "Critical error: \"Pray at any altar.\""),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 45, "Member B comment.", "Critical error: \"Pray at any altar.\"")
        }), compactGraders: true);

        Assert.DoesNotContain("(panel disagreement)", text);
        Assert.Contains("- **Critical error (panel member A):** \"Pray at any altar.\"", text);
        Assert.Contains("- **Critical error (panel member B):** \"Pray at any altar.\"", text);
        Assert.DoesNotContain("Member A comment.", text);
    }

    [Fact]
    public void CompactGraders_PrintTheReferenceReadersComment_OnlyWhenItIsFarFromThePanelMean()
    {
        int far = BenchmarkService.SecondOpinionDisagreementPoints + 1;
        var (farText, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 90, "Member A comment."),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 90, "Member B comment."),
            Grader(BenchmarkReportFacts.ReferenceReaderRole, 90 - far, "Reader comment.")
        }), compactGraders: true);
        var (nearText, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>
        {
            Grader(BenchmarkReportFacts.PanelMemberARole, 90, "Member A comment."),
            Grader(BenchmarkReportFacts.PanelMemberBRole, 90, "Member B comment."),
            Grader(BenchmarkReportFacts.ReferenceReaderRole, 90 - far + 1, "Reader comment.")
        }), compactGraders: true);

        Assert.Contains("- **Reference reader (Reference reader model):** Reader comment.", farText);
        Assert.DoesNotContain("Member A comment.", farText);
        Assert.DoesNotContain("(panel disagreement)", farText);
        Assert.DoesNotContain("Reader comment.", nearText);
    }

    [Fact]
    public void CompactGraders_WithoutAVerdict_SayNoneWasRecorded()
    {
        var (text, _) = Details(Content(1, _ => new List<BenchmarkReportContentGrader>()), compactGraders: true);

        Assert.Contains("**Graders:** No grader verdict was recorded.", text);
    }

    // ---------------------------------------------------------------------------------------------
    // Reproducibility appendix
    // ---------------------------------------------------------------------------------------------

    private static IEnumerable<BenchmarkReportFact> RunFacts(int models, Func<string, string> prompt, Func<string, string> guides)
        => Letters.Take(models).SelectMany(l => new[]
        {
            MakeFact(BenchmarkComparisonReportFacts.ModelPrefix(l) + "run.ids", "#1" + l),
            MakeFact(BenchmarkComparisonReportFacts.ModelPrefix(l) + "run.dates", "2026-10-0" + (Array.IndexOf(Letters, l) + 1)),
            MakeFact(BenchmarkComparisonReportFacts.ModelPrefix(l) + "run.promptSha256", prompt(l)),
            MakeFact(BenchmarkComparisonReportFacts.ModelPrefix(l) + "run.toolGuidesSha256", guides(l))
        });

    private static string Reproducibility(BenchmarkReportFactSheet sheet, bool? sharedOnce)
    {
        var sb = new StringBuilder();
        if (sharedOnce is bool shared) BenchmarkReportPackRenderer.ComparisonReproducibility(sb, Context(sheet), "## Appendix F", shared);
        else BenchmarkReportPackRenderer.ComparisonReproducibility(sb, Context(sheet), "## Appendix F");
        return sb.ToString();
    }

    [Fact]
    public void Reproducibility_ByDefault_GivesEveryModelsHashes()
    {
        string text = Reproducibility(Sheet(3, RunFacts(3, _ => "abc123", _ => "def456")), sharedOnce: null);

        Assert.DoesNotContain("All models", text);
        Assert.Equal(3, CountOf(text, "`abc123`"));
        Assert.Equal(3, CountOf(text, "`def456`"));
        Assert.Equal(text, Reproducibility(Sheet(3, RunFacts(3, _ => "abc123", _ => "def456")), sharedOnce: false));
    }

    [Fact]
    public void Reproducibility_SharedOnce_PrintsHashesEveryModelShares_OnOneLine()
    {
        string text = Reproducibility(Sheet(3, RunFacts(3, _ => "abc123", _ => "def456")), sharedOnce: true);

        Assert.Contains("- **All models:** System prompt SHA-256 prefix `abc123`; tool guides SHA-256 prefix `def456`\n", text);
        Assert.Equal(1, CountOf(text, "`abc123`"));
        Assert.Equal(1, CountOf(text, "`def456`"));
        Assert.Contains("- **Model A name** (ProviderA model-a, thinking level medium): Run IDs #1A; run dates 2026-10-01\n", text);
        Assert.True(text.IndexOf("All models", StringComparison.Ordinal) < text.IndexOf("**Model A name**", StringComparison.Ordinal));
    }

    [Fact]
    public void Reproducibility_SharedOnce_KeepsAHashThatDiffers_OnEveryModelsLine()
    {
        string text = Reproducibility(Sheet(3, RunFacts(3, l => l == "C" ? "999999" : "abc123", _ => "def456")), sharedOnce: true);

        Assert.Contains("- **All models:** Tool guides SHA-256 prefix `def456`\n", text);
        Assert.DoesNotContain("All models:** System prompt", text);
        Assert.Contains("run dates 2026-10-01; System prompt SHA-256 prefix `abc123`\n", text);
        Assert.Contains("run dates 2026-10-03; System prompt SHA-256 prefix `999999`\n", text);
        Assert.Equal(1, CountOf(text, "`def456`"));
    }

    [Fact]
    public void Reproducibility_SharedOnce_WithOneModel_IsTheDefault()
    {
        var facts = RunFacts(1, _ => "abc123", _ => "def456").ToList();

        Assert.Equal(Reproducibility(Sheet(1, facts), sharedOnce: null), Reproducibility(Sheet(1, facts), sharedOnce: true));
    }

    // ---------------------------------------------------------------------------------------------
    // Grader findings table
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] Categories = { "accuracy", "completeness", "conciseness", "readability", "critical_error", "tool_use", "other" };

    /// <summary>Each model's findings facts: 4 runs with findings, and the given accuracy and completeness counts.</summary>
    private static IEnumerable<BenchmarkReportFact> FindingsFacts(params (string Letter, int Accuracy, int Completeness)[] models)
        => models.SelectMany(m =>
        {
            string prefix = BenchmarkComparisonReportFacts.ModelPrefix(m.Letter) + BenchmarkComparisonModelProfile.KeyPrefix + "findings.";
            var facts = new List<BenchmarkReportFact> { MakeFact(prefix + "runsWithSynthesis", "4", 4) };
            foreach (string category in Categories)
            {
                int count = category == "accuracy" ? m.Accuracy : category == "completeness" ? m.Completeness : 0;
                facts.Add(MakeFact(prefix + category, count.ToString(System.Globalization.CultureInfo.InvariantCulture), count));
            }
            return facts;
        });

    private static string Findings(BenchmarkReportFactSheet sheet, bool? onlyDifferingRows)
    {
        var sb = new StringBuilder();
        if (onlyDifferingRows is bool only) BenchmarkReportPackRenderer.FindingsTable(sb, Context(sheet), only);
        else BenchmarkReportPackRenderer.FindingsTable(sb, Context(sheet));
        return sb.ToString();
    }

    [Fact]
    public void FindingsTable_ByDefault_HasARowPerModel()
    {
        var sheet = Sheet(3, FindingsFacts(("A", 2, 3), ("B", 2, 3), ("C", 1, 3)));
        string text = Findings(sheet, onlyDifferingRows: null);

        Assert.Contains("| Model A name |", text);
        Assert.Contains("| Model B name |", text);
        Assert.Contains("| Model C name |", text);
        Assert.DoesNotContain("Left out, with the most common count", text);
        Assert.Equal(text, Findings(sheet, onlyDifferingRows: false));
    }

    [Fact]
    public void FindingsTable_OnlyDifferingRows_LeavesOutTheModelsWithTheMostCommonCounts_AndNamesThem()
    {
        string text = Findings(Sheet(3, FindingsFacts(("A", 2, 3), ("B", 2, 3), ("C", 1, 3))), onlyDifferingRows: true);

        Assert.DoesNotContain("| Model A name |", text);
        Assert.DoesNotContain("| Model B name |", text);
        Assert.Contains("| Model C name |", text);
        Assert.Contains("Left out, with the most common count in every category: Model A name and Model B name.", text);
    }

    [Fact]
    public void FindingsTable_OnlyDifferingRows_WhenEveryModelHasTheCommonCounts_PrintsASentenceInsteadOfTheTable()
    {
        string text = Findings(Sheet(3, FindingsFacts(("A", 2, 3), ("B", 2, 3), ("C", 2, 3))), onlyDifferingRows: true);

        Assert.Equal("Every model had the most common count in every category.\n\n", text);
    }

    [Fact]
    public void FindingsTable_OnlyDifferingRows_WithoutAMostCommonCount_KeepsEveryRow()
    {
        var sheet = Sheet(2, FindingsFacts(("A", 2, 3), ("B", 1, 3)));

        Assert.Equal(Findings(sheet, onlyDifferingRows: null), Findings(sheet, onlyDifferingRows: true));
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
