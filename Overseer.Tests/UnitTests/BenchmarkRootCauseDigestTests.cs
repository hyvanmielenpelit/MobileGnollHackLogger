namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.RootCause;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The Executive Summary written after the Root Cause Analysis, from a code-built digest of it: the digest's items and
/// hash, its facts on the summary's sheet, the prompt's ROOT CAUSE ANALYSIS block and rules, rules 47 to 49, What to look
/// at next, the summary's header row, the same-provider reading, the zero-count rule, and the Root Cause Analysis
/// headline's observation.
/// </summary>
public class BenchmarkRootCauseDigestTests
{
    private const BenchmarkReportAudience Es = BenchmarkReportAudience.ExecutiveSummary;

    /// <summary>The Root Cause Analysis document the test digest is built from.</summary>
    private const long RootCauseDocumentId = 42;

    /// <summary>
    /// A run's Root Cause Analysis shaped as run 123's: its only intelligence hypothesis besides a Low one was ruled out
    /// (WQ2.1), speed is a probable cause in the tool rounds (WS3.1, Moderate), cost has no hypothesis; one next run, one
    /// check, a control limit and a replicate limit, and one writer hypothesis.
    /// </summary>
    internal static (BenchmarkReportFactSheet Sheet, BenchmarkReportWriterOutput Writer) RootCauseAnalysis()
    {
        var sheet = new BenchmarkReportFactSheet
        {
            SubjectKey = "run:123",
            Facts = new List<BenchmarkReportFact>
            {
                new() { Key = "rca.axis.speed.status", Value = JsonValue.Create(RootCauseStatuses.Probable), Display = "Probable cause" },
                new() { Key = "rca.run.1.summary", Display = "Run 1, a replicate of the model's run" },
                new() { Key = "rca.run.1.purpose", Display = RootCausePurposes.Text(RootCausePurposes.Chat) },
                new() { Key = "rca.check.1.summary", Display = "Check 1, a review of the rubric of Q2" },
            },
            RootCause = new RootCauseResult
            {
                Origin = RootCauseOrigins.RunCompletion,
                ControlLimit = RootCauseGrades.Low,
                ControlLimitReason = "no other model answered these questions",
                GradeCap = "The grades are capped at Moderate: the model has one run per suite.",
                Axes =
                {
                    new RootCauseAxisStatus { Axis = RootCauseAxes.Intelligence, Status = RootCauseStatuses.NotEstablished, Reason = RootCauseAxisStatus.AllRuledOutReason },
                    new RootCauseAxisStatus { Axis = RootCauseAxes.Speed, Status = RootCauseStatuses.Probable, BestHypothesis = "WS3.1", Reason = "Probable (Moderate) because one run per suite." },
                    new RootCauseAxisStatus { Axis = RootCauseAxes.Cost, Status = RootCauseStatuses.NotEstablished, Reason = "No rule found a possible cause on this axis." },
                },
                Hypotheses =
                {
                    new RootCauseHypothesis
                    {
                        Id = "WQ2", Number = 1, Key = "WQ2.1", Axis = RootCauseAxes.Intelligence, Kind = "within", Grade = RootCauseGrades.NotSupported,
                        Statement = "The model lost its points on answers given without a tool call.", Locus = "model / chat"
                    },
                    new RootCauseHypothesis
                    {
                        Id = "WQ1", Number = 1, Key = "WQ1.1", Axis = RootCauseAxes.Intelligence, Kind = "within", Grade = RootCauseGrades.Low,
                        Statement = "The model lost points on the board questions.", Locus = "model"
                    },
                    new RootCauseHypothesis
                    {
                        Id = "WS3", Number = 1, Key = "WS3.1", Axis = RootCauseAxes.Speed, Kind = "within", Grade = RootCauseGrades.Moderate,
                        Statement = "Tool rounds multiply the model's time per question.", ShortStatement = "tool rounds multiply time", Locus = "chat"
                    },
                },
                RecommendedRuns = { new RootCauseRecommendedRun { Number = 1, Kind = RootCauseRunKinds.Replicate, Purpose = RootCausePurposes.Chat } },
                Checks = { new RootCauseCheck { Number = 1, Kind = "rubric", Text = "Check the rubric of Q2.", Summary = "Check 1, a review of the rubric of Q2" } },
            },
        };
        var writer = new BenchmarkReportWriterOutput
        {
            Headline = "{{subject}} lost points on Q2; speed: {{rca.axis.speed.status}}.",
            WriterHypotheses = new List<BenchmarkReportWriterHypothesis>
            {
                new() { Axis = RootCauseAxes.Intelligence, Statement = "{{subject}} may have misread the board on Q2.", Confidence = "Low" },
            },
        };
        return (sheet, writer);
    }

    internal static BenchmarkRootCauseDigest Digest()
    {
        var (sheet, writer) = RootCauseAnalysis();
        var digest = BenchmarkRootCauseDigest.From(RootCauseDocumentId, sheet, writer);
        Assert.NotNull(digest);
        return digest!;
    }

    /// <summary>The validator test sheet as an Executive Summary written from <see cref="Digest"/>.</summary>
    private static BenchmarkReportFactSheet DigestSheet()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        Digest().ApplyTo(sheet, BenchmarkRootCauseSources.ThisJob);
        return sheet;
    }

    private static BenchmarkReportWriterOutput DigestOutput()
    {
        var output = ReportPackWriterTestData.ValidOutput(Es);
        output.Sections[BenchmarkReportSlots.NextSteps] = "Make this run first to settle the open question: {{rcaDigest.nextRun.1}}";
        return output;
    }

    private static IReadOnlyList<BenchmarkReportValidationNote> Validate(BenchmarkReportWriterOutput output, BenchmarkReportFactSheet sheet)
        => BenchmarkReportPackValidator.Validate(Es, output, sheet, ReportPackWriterTestData.Content());

    // --- The digest -------------------------------------------------------------------------------------

    [Fact]
    public void TheDigest_HoldsEachItemAsASentence_InDigestOrder()
    {
        var digest = Digest();
        var facts = digest.Facts.ToDictionary(f => f.Key, f => f.Display);

        Assert.Equal("The model under test lost points on Q2; speed: Probable cause.", facts["rcaDigest.keyFinding"]);
        Assert.Equal("Intelligence: not established; every hypothesis was ruled out.", facts["rcaDigest.axis.intelligence"]);
        Assert.Equal("Speed: probable cause — tool rounds multiply time (graded Moderate).", facts["rcaDigest.axis.speed"]);
        Assert.Equal("Cost: not established; no rule found a possible cause on this axis.", facts["rcaDigest.axis.cost"]);
        Assert.Equal("Intelligence: the model lost points on the board questions, graded Low.", facts["rcaDigest.hypothesis.1"]);
        Assert.Equal("Intelligence, ruled out: the model lost its points on answers given without a tool call, graded Not supported.",
            facts["rcaDigest.hypothesis.2"]);
        Assert.Equal("Speed: tool rounds multiply the model's time per question, graded Moderate.", facts["rcaDigest.hypothesis.3"]);
        Assert.DoesNotContain(digest.Facts, f => f.Display.Contains("locus", StringComparison.Ordinal));
        Assert.Equal("An untested lead (Low), from the graders' comments: the model under test may have misread the board on Q2.", facts["rcaDigest.lead.1"]);
        Assert.Equal("Run 1, a replicate of the model's run, for purpose C, the Overseer chat.", facts["rcaDigest.nextRun.1"]);
        Assert.Equal("Check 1, a review of the rubric of Q2.", facts["rcaDigest.check.1"]);
        Assert.Equal("Control limit: capped at Low; replicate limit: the grades are capped at Moderate: the model has one run per suite.", facts["rcaDigest.limits"]);

        Assert.Equal(new[]
        {
            "rcaDigest.keyFinding", "rcaDigest.axis.intelligence", "rcaDigest.axis.speed", "rcaDigest.axis.cost",
            "rcaDigest.hypothesis.1", "rcaDigest.hypothesis.2", "rcaDigest.hypothesis.3", "rcaDigest.lead.1",
            "rcaDigest.nextRun.1", "rcaDigest.check.1", "rcaDigest.limits"
        }, digest.Facts.Select(f => f.Key));

        // The raw values say what each item is: a lead or a Low grade asks for a hedge.
        Assert.Equal(BenchmarkRootCauseDigest.LeadValue, digest.Facts.Single(f => f.Key == "rcaDigest.lead.1").Value!.GetValue<string>());
        Assert.Equal(RootCauseGrades.Low, digest.Facts.Single(f => f.Key == "rcaDigest.hypothesis.1").Value!.GetValue<string>());
        Assert.Equal(RootCauseStatuses.Probable, digest.Facts.Single(f => f.Key == "rcaDigest.axis.speed").Value!.GetValue<string>());
    }

    [Fact]
    public void TheDigest_HashesItsOwnText_AndCarriesItsDocument()
    {
        var digest = Digest();

        Assert.Equal(RootCauseDocumentId, digest.DocumentId);
        Assert.Equal(string.Concat(digest.Facts.Select(f => f.Key + " = " + f.Display + "\n")), digest.Text);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(digest.Text))), digest.Sha256);
        Assert.Equal(digest.Sha256, Digest().Sha256);
        Assert.InRange(digest.Text.Length, 1, 10_000);
    }

    [Fact]
    public void TheKeyFinding_LeavesOutTheHypothesisReferences_AndTheParenthesesThatHeldOnlyThem()
    {
        var (sheet, writer) = RootCauseAnalysis();
        foreach (string key in new[] { "WS3.1", "WC3.1", "WQ1.1", "WQ1.3" })
        {
            sheet.Facts.Add(new BenchmarkReportFact { Key = "rca.hypothesis." + key + ".ref", Display = key });
        }
        string KeyFinding(string headline)
        {
            writer.Headline = headline;
            return BenchmarkRootCauseDigest.From(RootCauseDocumentId, sheet, writer)!.Facts.Single(f => f.Key == BenchmarkRootCauseDigest.KeyFindingKey).Display;
        }

        // Run 125's headline.
        Assert.Equal("On Q9 accuracy, both panel members flagged a critical error, while tool rounds multiply time and input cost.",
            KeyFinding("On Q9 accuracy, both panel members flagged a critical error, while tool rounds multiply time ({{rca.hypothesis.WS3.1.ref}}) "
                       + "and input cost ({{rca.hypothesis.WC3.1.ref}})."));

        // A range of one rule's references goes whole, and so does a share's reference.
        Assert.Equal("The board questions cost the most points.",
            KeyFinding("The board questions cost the most points ({{rca.hypothesis.WQ1.1.ref}}–{{rca.hypothesis.WQ1.3.ref}})."));
        Assert.Equal("Critical errors hold 62% of the points at risk.",
            KeyFinding("Critical errors hold 62% of the points at risk ({{rca.hypothesis.WQ1.1.ref}})."));

        // A parenthesis with words of its own stays; a reference outside parentheses leaves no id behind.
        Assert.Equal("Speed (the tool rounds) is slow.", KeyFinding("Speed (the tool rounds) is slow ({{rca.hypothesis.WS3.1.ref}})."));
        string bare = KeyFinding("{{rca.hypothesis.WS3.1.ref}} holds: tool rounds multiply time.");
        Assert.Equal("Holds: tool rounds multiply time.", bare);
        Assert.DoesNotContain("WS3.1", bare);
        Assert.DoesNotContain("\uE000", bare);
    }

    [Fact]
    public void AHypothesisStatement_LosesItsScopeGloss_InTheDigestOnly()
    {
        var (sheet, writer) = RootCauseAnalysis();
        sheet.SubjectDisplayName = "GPT-6 Luna";
        sheet.RootCause!.Hypotheses.Insert(0, new RootCauseHypothesis
        {
            Id = "WQ3", Number = 1, Key = "WQ3.1", Axis = RootCauseAxes.Intelligence, Kind = "within", Grade = RootCauseGrades.Low,
            Statement = "Confirmed critical errors cost GPT-6 Luna most of its points (where its points go, not a comparison).",
            ShortStatement = "Critical errors: GPT-6 Luna", Locus = RootCauseLoci.Model
        });

        var facts = BenchmarkRootCauseDigest.From(RootCauseDocumentId, sheet, writer)!.Facts.ToDictionary(f => f.Key, f => f.Display);

        Assert.Equal("Intelligence: confirmed critical errors cost GPT-6 Luna most of its points, graded Low.", facts["rcaDigest.hypothesis.1"]);
        Assert.DoesNotContain(facts.Values, d => d.Contains("not a comparison", StringComparison.Ordinal));

        // An axis whose best hypothesis has no short statement or label shows the statement, its gloss left out too.
        var best = new RootCauseHypothesis
        {
            Key = "WS3.1", Axis = RootCauseAxes.Speed, Grade = RootCauseGrades.Moderate,
            Statement = "Tool rounds multiply GPT-6 Luna's time (where its time goes, not a comparison)."
        };
        Assert.Equal("Speed: probable cause — tool rounds multiply GPT-6 Luna's time (graded Moderate).",
            BenchmarkRootCauseDigest.AxisSentence(new RootCauseAxisStatus { Axis = RootCauseAxes.Speed, Status = RootCauseStatuses.Probable, BestHypothesis = "WS3.1" }, best));

        // Each of the three glosses goes; the statement itself is unchanged.
        Assert.Equal(3, BenchmarkRootCauseDigest.ScopeGlosses.Count);
        foreach (string gloss in BenchmarkRootCauseDigest.ScopeGlosses)
        {
            Assert.Equal("Uncached input is the largest cost component.", BenchmarkRootCauseDigest.WithoutScopeGloss("Uncached input is the largest cost component" + gloss + "."));
        }
        Assert.EndsWith("(where its time goes, not a comparison).", best.Statement);
    }

    [Fact]
    public void AComparisonDigest_KeepsAtMostTwoHypothesesPerAxis()
    {
        var (sheet, writer) = RootCauseAnalysis();
        for (int n = 2; n <= 4; n++)
        {
            sheet.RootCause!.Hypotheses.Add(new RootCauseHypothesis
            {
                Id = "HS1", Number = n, Key = "HS1." + n, Axis = RootCauseAxes.Speed, Kind = "pair", Grade = RootCauseGrades.Low,
                Statement = "Model A makes more model calls than Model B.", Locus = "model"
            });
        }

        int run = BenchmarkRootCauseDigest.From(1, sheet, writer)!.Facts.Count(f => f.Display.StartsWith("Speed", StringComparison.Ordinal) && f.Key.StartsWith(BenchmarkRootCauseDigest.HypothesisPrefix, StringComparison.Ordinal));
        sheet.Scope = BenchmarkReportFactSheet.ComparisonScopeValue;
        int comparison = BenchmarkRootCauseDigest.From(1, sheet, writer)!.Facts.Count(f => f.Display.StartsWith("Speed", StringComparison.Ordinal) && f.Key.StartsWith(BenchmarkRootCauseDigest.HypothesisPrefix, StringComparison.Ordinal));

        Assert.Equal(BenchmarkRootCauseDigest.MaxHypothesesPerAxis, run);
        Assert.Equal(BenchmarkRootCauseDigest.MaxComparisonHypothesesPerAxis, comparison);
    }

    [Fact]
    public void ADocumentThatIsNoRootCauseAnalysis_HasNoDigest()
    {
        var document = BenchmarkReportPackFixture.Document(Es);
        Assert.Null(BenchmarkRootCauseDigest.From(document));

        var stored = RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion);
        var digest = BenchmarkRootCauseDigest.From(stored);
        Assert.NotNull(digest);
        Assert.Equal(stored.Id, digest!.DocumentId);
        Assert.Contains(digest.Facts, f => f.Key == BenchmarkRootCauseDigest.KeyFindingKey);
        Assert.Contains(digest.Facts, f => f.Key.StartsWith(BenchmarkRootCauseDigest.LeadPrefix, StringComparison.Ordinal));
        Assert.Equal(3, digest.Facts.Count(f => f.Key.StartsWith(BenchmarkRootCauseDigest.AxisPrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplyTo_AddsTheDigestFactsInKeyOrder_AndRecordsItsSource_AndTheFactsHashIgnoresThem()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        string before = BenchmarkRootCauseDigest.FactsSha256(sheet);

        Digest().ApplyTo(sheet, BenchmarkRootCauseSources.Stored);

        Assert.Equal(sheet.Facts.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal), sheet.Facts.Select(f => f.Key));
        Assert.Equal(11, sheet.Facts.Count(f => BenchmarkRootCauseDigest.IsDigestKey(f.Key)));
        Assert.True(sheet.HasRootCauseDigest);
        Assert.Equal(RootCauseDocumentId, sheet.RootCauseDigest!.DocumentId);
        Assert.Equal(Digest().Sha256, sheet.RootCauseDigest.Sha256);
        Assert.Equal(BenchmarkRootCauseSources.Stored, sheet.RootCauseDigest.Source);
        Assert.Equal(before, BenchmarkRootCauseDigest.FactsSha256(sheet));

        // Stored as rootCauseDigest: { documentId, sha256, source }.
        string json = BenchmarkReportJson.Serialize(sheet);
        Assert.Contains("\"rootCauseDigest\":{\"documentId\":42,\"sha256\":\"" + Digest().Sha256 + "\",\"source\":\"stored\"}", json);

        BenchmarkRootCauseDigest.ApplyNone(sheet, BenchmarkRootCauseSources.NoMatchNote);
        Assert.DoesNotContain(sheet.Facts, f => BenchmarkRootCauseDigest.IsDigestKey(f.Key));
        Assert.False(sheet.HasRootCauseDigest);
        Assert.Equal(BenchmarkRootCauseSources.NoMatchNote, sheet.RootCauseDigest!.Note);

        // Another fact moves the hash.
        sheet.Facts.Add(new BenchmarkReportFact { Key = "zz.other", Display = "1" });
        Assert.NotEqual(before, BenchmarkRootCauseDigest.FactsSha256(sheet));
    }

    [Theory]
    [InlineData("rcaDigest.keyFinding", "Root Cause Analysis: key finding")]
    [InlineData("rcaDigest.limits", "Root Cause Analysis: limits of the analysis")]
    [InlineData("rcaDigest.axis.speed", "Root Cause Analysis, Speed: status")]
    [InlineData("rcaDigest.hypothesis.2", "Root Cause Analysis: hypothesis 2")]
    [InlineData("rcaDigest.lead.1", "Root Cause Analysis: writer's lead 1")]
    [InlineData("rcaDigest.nextRun.1", "Root Cause Analysis: next run 1")]
    [InlineData("rcaDigest.check.2", "Root Cause Analysis: check 2")]
    [InlineData("panel.sameProviderDelta", "Same-provider panel member's reading")]
    public void EveryDigestFact_HasALabel(string key, string label)
    {
        Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string found));
        Assert.Equal(label, found);
    }

    [Theory]
    [InlineData("rcaDigest.keyFinding", true)]
    [InlineData("rcaDigest.limits", true)]
    [InlineData("rcaDigest.axis.cost", true)]
    [InlineData("rcaDigest.hypothesis.1", true)]
    [InlineData("rcaDigest.lead.1", true)]
    [InlineData("rcaDigest.nextRun.1", false)]
    [InlineData("rcaDigest.check.1", false)]
    public void TheSentenceValuedDigestFacts_FollowTheSentenceValuedConvention(string key, bool sentence)
        => Assert.Equal(sentence, RootCauseClaimSupport.IsSentenceShaped(key));

    // --- The prompt -------------------------------------------------------------------------------------

    [Fact]
    public void ThePrompt_WithADigest_ListsItAboveTheData_AndStatesItsRulesInTheSystemPrompt()
    {
        var prompt = BenchmarkReportPackPrompt.Build(Es, DigestSheet(), ReportPackWriterTestData.Content());

        Assert.Contains("ROOT CAUSE ANALYSIS:\n", prompt.SystemPrompt);
        Assert.Contains("State no cause, mechanism or next step that the digest does not contain.", prompt.SystemPrompt);
        Assert.Contains("- nextSteps: At most 60 words, printed under \"What to look at next\"", prompt.SystemPrompt);
        Assert.Contains("\"nextSteps\": \"Markdown paragraphs, only when ROOT CAUSE ANALYSIS lists a next run or a check\"", prompt.SystemPrompt);
        Assert.Contains("a lead is called a lead", prompt.SystemPrompt);

        string message = prompt.UserMessage;
        int block = message.IndexOf("ROOT CAUSE ANALYSIS (written first, from the same data", StringComparison.Ordinal);
        Assert.True(block >= 0);
        Assert.True(block < message.IndexOf("FACTS (write {{key}}", StringComparison.Ordinal));
        Assert.Contains("rcaDigest.axis.speed = Speed: probable cause — tool rounds multiply time (graded Moderate).\n", message);
        // Each digest item is listed once, in its own block, and never among the FACTS.
        Assert.Single(AllIndexesOf(message, "rcaDigest.keyFinding = "));
        Assert.DoesNotContain("rca.axis.speed.status", message);

        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(Es, BenchmarkReportScope.Model, rootCauseDigest: true), BenchmarkReportPackPrompt.PromptSha256(Es, DigestSheet()));
        Assert.NotEqual(BenchmarkReportPackPrompt.PromptSha256(Es, BenchmarkReportScope.Model), BenchmarkReportPackPrompt.PromptSha256(Es, DigestSheet()));
    }

    [Fact]
    public void ThePrompt_WithoutADigest_HasNeitherTheBlockNorTheRules_NorWhatToLookAtNext()
    {
        var sheet = ReportPackWriterTestData.Sheet();
        BenchmarkRootCauseDigest.ApplyNone(sheet, BenchmarkRootCauseSources.NoMatchNote);
        var prompt = BenchmarkReportPackPrompt.Build(Es, sheet, ReportPackWriterTestData.Content());

        Assert.DoesNotContain("ROOT CAUSE ANALYSIS", prompt.SystemPrompt);
        Assert.DoesNotContain("nextSteps", prompt.SystemPrompt);
        Assert.DoesNotContain("ROOT CAUSE ANALYSIS", prompt.UserMessage);
        Assert.Equal(BenchmarkReportPackPrompt.BuildSystemPrompt(Es), prompt.SystemPrompt);
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(Es, BenchmarkReportScope.Model), BenchmarkReportPackPrompt.PromptSha256(Es, sheet));
    }

    [Fact]
    public void TheComparisonPrompt_WithADigest_AddsTheSameRules_AndWhatToLookAtNext()
    {
        string with = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(Es, rootCauseDigest: true);
        string without = BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(Es);

        Assert.Contains("ROOT CAUSE ANALYSIS:\n", with);
        Assert.Contains("Where a point about a model's weakness is one a digest item explains, the point names that item", with);
        Assert.Contains("\"nextSteps\": \"Markdown paragraphs, only when ROOT CAUSE ANALYSIS lists a next run or a check\"", with);
        Assert.DoesNotContain("ROOT CAUSE ANALYSIS", without);
        Assert.NotEqual(BenchmarkReportPackPrompt.PromptSha256(Es, BenchmarkReportScope.Comparison),
            BenchmarkReportPackPrompt.PromptSha256(Es, BenchmarkReportScope.Comparison, rootCauseDigest: true));
    }

    [Fact]
    public void EveryExecutiveSummaryPrompt_CitesAZeroCountOnce_OnlyToRuleSomethingOut()
    {
        Assert.Contains("- " + BenchmarkReportPackPrompt.ZeroCountPromptRule + "\n", BenchmarkReportPackPrompt.BuildSystemPrompt(Es));
        Assert.Contains("- " + BenchmarkReportPackPrompt.ZeroCountPromptRule + "\n", BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(Es));
        Assert.Contains("cited at most once, and only to rule something out", BenchmarkReportPackPrompt.ZeroCountPromptRule);
        Assert.Contains("A count whose value is zero is cited at most once, and only to rule something out; never present it as a finding.",
            BenchmarkReportPackPrompt.BuildChatConsistencySystemPrompt(Es));
    }

    [Fact]
    public void TheEstimate_AddsTheDigestToTheExecutiveSummarysPrompt_BeforeTheAnalysisExists()
    {
        var (sheet, _) = RootCauseAnalysis();
        int chars = BenchmarkReportPackPrompt.RootCauseDigestPromptChars(sheet);

        Assert.True(chars > BenchmarkReportPackPrompt.DigestWriterTextAllowanceChars);
        Assert.Equal(0, BenchmarkReportPackPrompt.RootCauseDigestPromptChars(ReportPackWriterTestData.Sheet()));
        Assert.Equal(0, BenchmarkReportPackPrompt.RootCauseDigestPromptChars(DigestSheet()));
    }

    // --- The same-provider reading ----------------------------------------------------------------------

    [Theory]
    [InlineData("B", "A", -2.4, "member B, from the model's own provider, scored it 2 points below member A")]
    [InlineData("A", "B", 1.2, "member A, from the model's own provider, scored it 1 point above member B")]
    [InlineData("B", "A", 0.3, "member B, from the model's own provider, scored it within a point of member A")]
    public void TheSameProviderReading_SaysWhichMemberScoredItHowFarAboveOrBelow(string same, string other, double delta, string expected)
        => Assert.Equal(expected, BenchmarkReportFacts.SameProviderDeltaDisplay(same, other, delta));

    [Fact]
    public void TheSameProviderDelta_IsAFact_OnlyWithExactlyOneMemberOfTheModelsProvider()
    {
        BenchmarkRun Run(string a, string b) => new()
        {
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: a, modelId: a + "-grader"),
            CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: b, modelId: b + "-grader")
        };
        var alone = new List<(int? MemberA, int? MemberB)> { (80, 78) };

        var one = new BenchmarkReportFacts.FactList();
        BenchmarkReportFacts.AddSameProviderDelta(one, "Anthropic", new[] { Run("Google", "Anthropic") }, alone);
        var fact = Assert.Single(one.Sorted());
        Assert.Equal("panel.sameProviderDelta", fact.Key);
        Assert.Equal(-2.0, fact.Value!.GetValue<double>(), 9);
        Assert.Equal("member B, from the model's own provider, scored it 2 points below member A", fact.Display);

        var none = new BenchmarkReportFacts.FactList();
        BenchmarkReportFacts.AddSameProviderDelta(none, "OpenAI", new[] { Run("Google", "Anthropic") }, alone);
        Assert.Empty(none.Sorted());

        var both = new BenchmarkReportFacts.FactList();
        BenchmarkReportFacts.AddSameProviderDelta(both, "Anthropic", new[] { Run("Anthropic", "Anthropic") }, alone);
        Assert.Empty(both.Sorted());
    }

    [Fact]
    public void TheReliabilityParagraph_CitesTheSameProviderReading_InPlaceOfTheGenericSentence()
    {
        Assert.Contains("Where FACTS has {{panel.sameProviderDelta}}, cite it instead of saying that the grader may read the subject more favorably",
            BenchmarkReportPackPrompt.BuildSystemPrompt(Es));
        Assert.Contains("where that model's model.X.panel.sameProviderDelta fact exists, cite it instead",
            BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(Es));
    }

    // --- Rules 47 and 48 --------------------------------------------------------------------------------

    [Fact]
    public void ASummaryWrittenFromADigest_PassesWithWhatToLookAtNext_AndRequiresIt()
    {
        Assert.Empty(Validate(DigestOutput(), DigestSheet()));

        var missing = ReportPackWriterTestData.ValidOutput(Es);
        var note = Assert.Single(Validate(missing, DigestSheet()));
        Assert.Equal(1, note.Rule);
        Assert.Equal("sections.nextSteps", note.Location);

        // Without a digest the summary's rules are those of today: no such slot.
        Assert.Empty(Validate(ReportPackWriterTestData.ValidOutput(Es), ReportPackWriterTestData.Sheet()));
        Assert.Contains(Validate(DigestOutput(), ReportPackWriterTestData.Sheet()), n => n.Rule == 1 && n.Location == "sections.nextSteps");
    }

    [Fact]
    public void Rule47_ACauseInTheSummary_NeedsADigestItem_HedgedBesideALead()
    {
        var sheet = DigestSheet();

        // Run 123's weakness on Q8, rewritten with the lead's token: hedged, it passes.
        var hedged = DigestOutput();
        hedged.Weaknesses[0] = new BenchmarkReportWriterItem
        {
            Text = "Overlooked what the board showed on Q2, possibly because it misread it: {{rcaDigest.lead.1}}",
            Questions = { 2 },
            Evidence = { "R2", "Q2", "rcaDigest.lead.1" }
        };
        Assert.Empty(Validate(hedged, sheet));

        var unhedged = DigestOutput();
        unhedged.Weaknesses[0].Text = "Overlooked what the board showed on Q2 because it misread it: {{rcaDigest.lead.1}}";
        var lead = Assert.Single(Validate(unhedged, sheet));
        Assert.Equal(BenchmarkReportPackValidator.ExecutiveCausalClaimRule, lead.Rule);
        Assert.Equal("weaknesses[0]", lead.Location);
        Assert.Contains("on a lead or a hypothesis graded Low only", lead.Message);

        var bare = DigestOutput();
        bare.Weaknesses[0].Text = "Overlooked what the board showed on Q2 because it misread it.";
        var unsupported = Assert.Single(Validate(bare, sheet));
        Assert.Equal(BenchmarkReportPackValidator.ExecutiveCausalClaimRule, unsupported.Rule);
        Assert.Contains("without a Root Cause Analysis item in the same sentence", unsupported.Message);
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Es, bare, sheet, ReportPackWriterTestData.Content());
        Assert.Empty(cleaned.Output.Weaknesses);
        Assert.Contains(cleaned.Notes, n => n.Rule == BenchmarkReportPackValidator.ExecutiveCausalClaimRule && n.Dropped);

        // A probable cause needs no hedge.
        var probable = DigestOutput();
        probable.Sections[BenchmarkReportSlots.Meaning] = "Answers were slow because of the extra tool rounds: {{rcaDigest.axis.speed}}";
        Assert.Empty(Validate(probable, sheet));

        // Without a digest, a causal word is not checked.
        var plain = ReportPackWriterTestData.ValidOutput(Es);
        plain.Weaknesses[0].Text = "Overlooked what the board showed on Q2 because it misread it.";
        Assert.DoesNotContain(Validate(plain, ReportPackWriterTestData.Sheet()), n => n.Rule == BenchmarkReportPackValidator.ExecutiveCausalClaimRule);
    }

    [Fact]
    public void Rule47_DoesNotReadTheReliabilitySection_WhichSpeaksAboutTheMeasurement()
    {
        var sheet = DigestSheet();
        const string design = "Because this report covers a single run, the graders' agreement is the main check on the result.";

        var reliability = DigestOutput();
        reliability.Sections[BenchmarkReportSlots.Confidence] = design;
        Assert.DoesNotContain(Validate(reliability, sheet), n => n.Rule == BenchmarkReportPackValidator.ExecutiveCausalClaimRule);

        // The same sentence about the model's use still needs a Root Cause Analysis item.
        var meaning = DigestOutput();
        meaning.Sections[BenchmarkReportSlots.Meaning] = design;
        Assert.Contains(Validate(meaning, sheet), n => n.Rule == BenchmarkReportPackValidator.ExecutiveCausalClaimRule
                                                       && n.Location.StartsWith("sections." + BenchmarkReportSlots.Meaning, StringComparison.Ordinal));

        // The comparison's reliability section is exempt too, and the catalog and both prompts say so.
        Assert.Equal(new[] { BenchmarkReportSlots.Confidence, BenchmarkReportSlots.Reliability }.OrderBy(s => s, StringComparer.Ordinal),
            BenchmarkReportPackValidator.DigestClaimExemptSlots.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Contains("How reliable this result is", BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.ExecutiveCausalClaimRule).Meaning);
        Assert.Contains("- " + BenchmarkReportSlots.Confidence + " (\"How reliable this result is\") speaks about the measurement: a reason there that is a fact of the run's design",
            BenchmarkReportPackPrompt.BuildSystemPrompt(Es, rootCauseDigest: true));
        Assert.Contains("- " + BenchmarkReportSlots.Reliability + " (\"How reliable this is\") speaks about the measurement",
            BenchmarkReportPackPrompt.BuildComparisonSystemPrompt(Es, rootCauseDigest: true));
        Assert.DoesNotContain("speaks about the measurement", BenchmarkReportPackPrompt.BuildSystemPrompt(Es));
    }

    [Fact]
    public void TheDigestOfAModelScopeAnalysis_NamesItsSubject_AndItsNextRunHoldsOneParenthesis()
    {
        var (sheet, writer) = RootCauseAnalysis();
        sheet.SubjectLabel = "GPT-6 Luna (high)";
        sheet.SubjectDisplayName = "GPT-6 Luna";
        sheet.Facts.Single(f => f.Key == "rca.run.1.summary").Display = "Run 1, a replicate of GPT-6 Luna (about $5.20 and 27 minutes over 3 launches)";

        var facts = BenchmarkRootCauseDigest.From(RootCauseDocumentId, sheet, writer)!.Facts.ToDictionary(f => f.Key, f => f.Display);

        Assert.Equal("GPT-6 Luna lost points on Q2; speed: Probable cause.", facts["rcaDigest.keyFinding"]);
        Assert.Equal("An untested lead (Low), from the graders' comments: GPT-6 Luna may have misread the board on Q2.", facts["rcaDigest.lead.1"]);
        string next = facts["rcaDigest.nextRun.1"];
        Assert.Equal("Run 1, a replicate of GPT-6 Luna (about $5.20 and 27 minutes over 3 launches), for purpose C, the Overseer chat.", next);
        Assert.EndsWith(".", next);
        Assert.Equal(1, next.Count(c => c == '('));
        Assert.Equal(1, next.Count(c => c == ')'));
        Assert.EndsWith(".", facts["rcaDigest.check.1"]);

        // A comparison has no single subject.
        sheet.Scope = BenchmarkReportFactSheet.ComparisonScopeValue;
        Assert.StartsWith("The model under test lost points",
            BenchmarkRootCauseDigest.From(RootCauseDocumentId, sheet, writer)!.Facts.Single(f => f.Key == BenchmarkRootCauseDigest.KeyFindingKey).Display);
    }

    [Fact]
    public void Rule48_EachItemOfWhatToLookAtNext_CitesADigestRunOrCheck_AndThereAreAtMostThree()
    {
        var sheet = DigestSheet();

        var uncited = DigestOutput();
        uncited.Sections[BenchmarkReportSlots.NextSteps] = "Make this run first: {{rcaDigest.nextRun.1}}\n\nThen read the rubric again.";
        var note = Assert.Single(Validate(uncited, sheet));
        Assert.Equal(BenchmarkReportPackValidator.ExecutiveNextStepRule, note.Rule);
        Assert.Equal("sections.nextSteps[p2]", note.Location);
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Es, uncited, sheet, ReportPackWriterTestData.Content());
        Assert.Equal("Make this run first: {{rcaDigest.nextRun.1}}", cleaned.Output.Sections[BenchmarkReportSlots.NextSteps]);
        Assert.False(cleaned.Fatal);

        var four = DigestOutput();
        four.Sections[BenchmarkReportSlots.NextSteps] = string.Join("\n\n", new[]
        {
            "First: {{rcaDigest.nextRun.1}}", "Then: {{rcaDigest.check.1}}", "Again: {{rcaDigest.nextRun.1}}", "Last: {{rcaDigest.check.1}}"
        });
        var extra = Assert.Single(Validate(four, sheet));
        Assert.Equal(BenchmarkReportPackValidator.ExecutiveNextStepRule, extra.Rule);
        Assert.Equal("sections.nextSteps[p4]", extra.Location);

        Assert.Equal(BenchmarkReportSlots.NextStepsMaxWords, BenchmarkReportPackValidator.SlotMaxWords(Es, BenchmarkReportSlots.NextSteps, BenchmarkReportScope.Model));
        Assert.Equal(BenchmarkReportSlots.NextStepsMaxWords, BenchmarkReportPackValidator.SlotMaxWords(Es, BenchmarkReportSlots.NextSteps, BenchmarkReportScope.Comparison));
    }

    [Fact]
    public void ADigestWithoutARunOrCheck_DoesNotRequireWhatToLookAtNext()
    {
        var (rca, writer) = RootCauseAnalysis();
        rca.RootCause!.RecommendedRuns.Clear();
        rca.RootCause.Checks.Clear();
        var sheet = ReportPackWriterTestData.Sheet();
        BenchmarkRootCauseDigest.From(RootCauseDocumentId, rca, writer)!.ApplyTo(sheet, BenchmarkRootCauseSources.ThisJob);

        Assert.Empty(Validate(ReportPackWriterTestData.ValidOutput(Es), sheet));
    }

    [Fact]
    public void TheCatalog_NamesRules47To49()
    {
        var cause = BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.ExecutiveCausalClaimRule);
        Assert.Equal(47, cause.Rule);
        Assert.Equal("Cause without a Root Cause Analysis item", cause.Name);
        Assert.Equal(BenchmarkReportRuleSeverity.RemovesItem, cause.Severity);

        var next = BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.ExecutiveNextStepRule);
        Assert.Equal(48, next.Rule);
        Assert.Equal("Next step without a run or check", next.Name);
        Assert.Equal(BenchmarkReportRuleSeverity.RemovesItem, next.Severity);

        var without = BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.WrittenWithoutRootCauseRule);
        Assert.Equal(49, without.Rule);
        Assert.Equal("Written without the Root Cause Analysis", without.Name);
        Assert.Equal(BenchmarkReportRuleSeverity.WarningTextKept,
            BenchmarkReportRuleCatalog.SeverityFor(BenchmarkReportPackValidator.WrittenWithoutRootCauseRule, "headline", dropped: false));

        foreach (var rule in new[] { cause, next, without })
        {
            Assert.DoesNotContain(rule.Fix, char.IsDigit);
            Assert.False(BenchmarkReportPackValidator.IsWarningRule(rule.Rule));
        }
        Assert.Equal("What to look at next", BenchmarkReportRuleCatalog.LocationLabel("sections.nextSteps[p2]").Split(',')[0]);
    }

    // --- The rendered summary ---------------------------------------------------------------------------

    private static BenchmarkReportDocument DigestDocument(Action<BenchmarkReportFactSheet> source, int? formatVersion = null)
    {
        var document = BenchmarkReportPackFixture.Document(Es);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        source(sheet);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        if (sheet.HasRootCauseDigest)
        {
            writer.Sections[BenchmarkReportSlots.NextSteps] = "Make this run first to settle the open question: {{rcaDigest.nextRun.1}}";
        }
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        document.ReportFormatVersion = formatVersion ?? BenchmarkReportPackRenderer.ReportFormatVersion;
        return document;
    }

    private static readonly BenchmarkReportRenderOptions FullNamed = new()
    {
        Disclosure = BenchmarkReportDisclosure.Full,
        PeerNaming = BenchmarkReportPeerNaming.Named
    };

    [Fact]
    public void TheSummary_NamesItsRootCauseAnalysis_AndEndsInWhatToLookAtNext()
    {
        var document = DigestDocument(sheet => Digest().ApplyTo(sheet, BenchmarkRootCauseSources.ThisJob));

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);

        Assert.Contains("- **Root Cause Analysis:** Document 42, written first, from the same data\n", text);
        int reliable = text.IndexOf("## How reliable this result is", StringComparison.Ordinal);
        int next = text.IndexOf("## What to look at next", StringComparison.Ordinal);
        Assert.True(reliable >= 0 && next > reliable);
        Assert.Contains("Make this run first to settle the open question: Run 1, a replicate of the model's run, for purpose C, the Overseer chat", text);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, FullNamed, BenchmarkPdfPaper.A4);
        var labels = info.Facts.Select(f => f.Label).ToList();
        Assert.Equal("Document 42, written first, from the same data", info.Facts.Single(f => f.Label == "Root Cause Analysis").Value);
        Assert.Equal(labels.IndexOf("Writer") + 1, labels.IndexOf("Root Cause Analysis"));
        Assert.Equal(labels.IndexOf("Root Cause Analysis") + 1, labels.IndexOf("Provenance"));
    }

    [Fact]
    public void ASummaryWithoutADigest_SaysWhy_AndOneStoredBeforeFormat25_HasNoRow()
    {
        var none = DigestDocument(sheet => BenchmarkRootCauseDigest.ApplyNone(sheet, BenchmarkRootCauseSources.FailedNote));
        string text = BenchmarkReportPackRenderer.Render(none, FullNamed);
        Assert.Contains("- **Root Cause Analysis:** None: the Root Cause Analysis of this job failed\n", text);
        Assert.DoesNotContain("## What to look at next", text);
        Assert.Equal("None: the Root Cause Analysis of this job failed",
            BenchmarkPdfDocumentInfo.ForReportDocument(none, FullNamed, BenchmarkPdfPaper.A4).Facts.Single(f => f.Label == "Root Cause Analysis").Value);

        var old = DigestDocument(sheet => Digest().ApplyTo(sheet, BenchmarkRootCauseSources.ThisJob), formatVersion: 24);
        Assert.DoesNotContain("**Root Cause Analysis:**", BenchmarkReportPackRenderer.Render(old, FullNamed));
        Assert.DoesNotContain(BenchmarkPdfDocumentInfo.ForReportDocument(old, FullNamed, BenchmarkPdfPaper.A4).Facts, f => f.Label == "Root Cause Analysis");

        var plain = BenchmarkReportPackFixture.Document(Es);
        Assert.DoesNotContain("**Root Cause Analysis:**", BenchmarkReportPackRenderer.Render(plain, FullNamed));
    }

    [Fact]
    public void AnAxisWhoseEveryHypothesisWasRuledOut_PrintsNoBestHypothesisInTheConclusions()
    {
        var sheet = RootCauseReportFixture.RunSheet(controls: true);
        var intelligence = sheet.RootCause!.Axes.Single(a => a.Axis == RootCauseAxes.Intelligence);
        intelligence.Status = RootCauseStatuses.NotEstablished;
        intelligence.BestHypothesis = null;
        intelligence.Reason = RootCauseAxisStatus.AllRuledOutReason;
        var document = RootCauseReportFixture.ModelDocument(sheet, BenchmarkReportDocumentOrigin.RunCompletion);

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed);

        Assert.Contains("| Intelligence | Not established | None (all ruled out) |", text);
        Assert.Equal("none (all ruled out)", BenchmarkReportPackRenderer.RcaAllRuledOut);
    }

    // --- The Root Cause Analysis' headline --------------------------------------------------------------

    /// <summary><paramref name="content"/> with the question <paramref name="number"/> of its first run graded by both panel members, its critical error <paramref name="resolution"/>.</summary>
    private static BenchmarkReportContentSnapshot WithPanel(BenchmarkReportContentSnapshot content, int number, string? resolution, int scoreA = 20, int scoreB = 25)
    {
        var item = content.Runs[0].Questions.Single(q => q.Number == number);
        item.Graders = new List<BenchmarkReportContentGrader>
        {
            new() { Role = BenchmarkReportFacts.PanelMemberARole, Label = "Gemini 3.8 Flash", Score = scoreA, Comment = "Wrong." },
            new() { Role = BenchmarkReportFacts.PanelMemberBRole, Label = "Claude Sonnet 5.5", Score = scoreB, Comment = "Wrong too." },
        };
        item.CriticalErrorResolution = resolution;
        return content;
    }

    [Fact]
    public void TheHeadlineObservation_IsTheFirstAnswerBothGradersFlagged_OrBothGradedIncorrect()
    {
        var sheet = ReportPackWriterTestData.Sheet();

        Assert.Equal("Q3, accuracy: both panel members flagged a critical error",
            BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet, WithPanel(ReportPackWriterTestData.Content(), 3, nameof(BenchmarkCriticalErrorResolution.Agreed))));

        // One member's flag, overturned or upheld by the verifier in a panel, is no observation of both.
        Assert.Null(BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet,
            WithPanel(ReportPackWriterTestData.Content(), 3, nameof(BenchmarkCriticalErrorResolution.UpheldByVerifier), scoreA: 60, scoreB: 70)));

        // A single assessor's critical error the claim verifier upheld.
        var single = ReportPackWriterTestData.Content();
        single.Runs[0].Questions.Single(q => q.Number == 3).CriticalErrorResolution = nameof(BenchmarkCriticalErrorResolution.UpheldByVerifier);
        Assert.Equal("Q3, accuracy: the assessor flagged a critical error and the claim verifier upheld it",
            BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet, single));

        // Both members graded the answer incorrect.
        Assert.Equal("Q2, accuracy: both panel members graded the answer incorrect",
            BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet, WithPanel(ReportPackWriterTestData.Content(), 2, null)));

        Assert.Null(BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet, ReportPackWriterTestData.Content()));
        sheet.Scope = BenchmarkReportFactSheet.ComparisonScopeValue;
        Assert.Null(BenchmarkReportPackPrompt.RootCauseHeadlineObservation(sheet,
            WithPanel(ReportPackWriterTestData.Content(), 3, nameof(BenchmarkCriticalErrorResolution.Agreed))));
    }

    [Fact]
    public void TheRootCauseHeadline_OpensWithTheObservation_AndTheValidatorAcceptsIt()
    {
        var document = RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
        int number = content.Runs[0].Questions.Select(q => q.Number).Where(n => sheet.Questions.Any(s => s.Number == n)).Min();
        WithPanel(content, number, nameof(BenchmarkCriticalErrorResolution.Agreed));
        string reference = "Q" + number;

        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.RootCauseAnalysis, sheet, content);

        Assert.Contains("When the data has a HEADLINE OBSERVATION, open the headline with it in one clause", prompt.SystemPrompt);
        Assert.Contains("A speed or cost axis whose best hypothesis is about tool rounds (WS3 or WC3) comes after any intelligence observation.", prompt.SystemPrompt);
        int observation = prompt.UserMessage.IndexOf("HEADLINE OBSERVATION (", StringComparison.Ordinal);
        Assert.True(observation >= 0);
        Assert.Contains("\n" + reference + ", accuracy: both panel members flagged a critical error.\n", prompt.UserMessage);
        Assert.True(observation < prompt.UserMessage.IndexOf("FACTS (write {{key}}", StringComparison.Ordinal));

        var writer = RootCauseReportFixture.Writer(sheet, comparison: false);
        writer.Headline = "On " + reference + ", both graders flagged an accuracy error; intelligence: {{rca.axis.intelligence.status}}; "
            + "speed: {{rca.axis.speed.status}}; cost: {{rca.axis.cost.status}}.";
        var notes = BenchmarkReportPackValidator.Validate(BenchmarkReportAudience.RootCauseAnalysis, writer, sheet, content);
        Assert.DoesNotContain(notes, n => n.Location == "headline" && BenchmarkReportPackValidator.BlocksText(n));

        // Without an answer both graders flagged or graded incorrect, the data has no observation.
        var plain = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
        foreach (var item in plain.Runs.SelectMany(r => r.Questions))
        {
            item.CriticalErrorResolution = null;
            item.Graders = item.Graders.Where(g => g.Role != BenchmarkReportFacts.PanelMemberBRole).ToList();
        }
        Assert.DoesNotContain("HEADLINE OBSERVATION (", BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.RootCauseAnalysis, sheet, plain).UserMessage);
    }

    // --- The write order --------------------------------------------------------------------------------

    [Fact]
    public void TheWriteOrder_PutsTheRootCauseAnalysisFirst_AndKeepsAnyOtherAudienceAfter()
    {
        Assert.Equal(new[] { BenchmarkReportAudience.RootCauseAnalysis, Es }, BenchmarkReportSlots.WriteOrder);
        Assert.Equal(new[] { BenchmarkReportAudience.RootCauseAnalysis, Es },
            BenchmarkReportSlots.InWriteOrder(new[] { Es, BenchmarkReportAudience.RootCauseAnalysis, Es }));
        Assert.Equal(new[] { Es, BenchmarkReportAudience.ProviderIssueReport },
            BenchmarkReportSlots.InWriteOrder(new[] { BenchmarkReportAudience.ProviderIssueReport, Es }));

        // Display order is unchanged.
        Assert.Equal(new[] { Es, BenchmarkReportAudience.RootCauseAnalysis }, BenchmarkRunReportDocumentService.Audiences);
        Assert.Equal(new[] { Es, BenchmarkReportAudience.ProviderIssueReport }, BenchmarkReportSlots.ChatConsistencyAudiences);
    }

    [Fact]
    public void TheJobView_GivesAnExecutiveSummarysSource_OnlyOnceItHasStarted()
    {
        var job = new BenchmarkReportPackJob
        {
            Cts = new System.Threading.CancellationTokenSource(),
            Documents = BenchmarkReportSlots.WriteOrder.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
        };
        Assert.All(job.ToDto().Documents, d => Assert.Null(d.RootCauseSource));

        job.SetRootCauseSource(job.Documents[1], BenchmarkRootCauseSources.None, null, BenchmarkRootCauseSources.FailedNote);
        var dto = job.ToDto();
        Assert.Equal(new[] { BenchmarkReportAudience.RootCauseAnalysis, Es }, dto.Documents.Select(d => d.Audience));
        Assert.Equal("none", dto.Documents[1].RootCauseSource);
        Assert.Null(dto.Documents[1].RootCauseSourceDocumentId);
        Assert.Equal("the Root Cause Analysis of this job failed", dto.Documents[1].RootCauseSourceNote);

        string json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"rootCauseSource\":\"none\"", json);
        Assert.Contains("\"rootCauseSourceDocumentId\":null", json);
        Assert.Contains("\"rootCauseSourceNote\":\"the Root Cause Analysis of this job failed\"", json);
    }

    private static List<int> AllIndexesOf(string text, string value)
    {
        var found = new List<int>();
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            found.Add(i);
        }
        return found;
    }
}
