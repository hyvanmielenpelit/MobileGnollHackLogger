namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The plain-language shape of what the root-cause engine writes: hypothesis labels that read inside a
/// sentence and never cut a model name, rounded next-run estimates, grade reasons that state the one condition
/// that applied, the rule-31 warning on a sentence-shaped fact cited after words of the writer's own sentence,
/// and one definition and wording of a model's panel disagreement for the notable findings and the profiles.
/// </summary>
public class RootCauseLanguageTests
{
    private const BenchmarkReportAudience Rca = BenchmarkReportAudience.RootCauseAnalysis;

    // --- Labels -------------------------------------------------------------------------------------------

    [Fact]
    public void APairLabel_ReadsAsThePossessiveTopicComparedWithTheOtherModel_AndKeepsLongNamesWhole()
    {
        const string first = "GPT-6 Astra Ultra Preview Experimental Long Context Edition (medium)";
        const string second = "Gemini 4 Argon Flash Lite Preview Research Build (high)";
        var names = new Dictionary<string, string> { ["A"] = first, ["B"] = second };
        var hypothesis = new RootCauseHypothesis { Id = "HC2", Kind = "pair", Models = { "A", "B" } };

        string label = RootCauseLabels.Of(hypothesis, t => names[t]);

        Assert.Equal(first + "'s higher price per token, compared with " + second, label);
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(label));
    }

    [Fact]
    public void ALabelWithLetteredModels_ReadsInsideASentence()
    {
        var pair = new RootCauseHypothesis { Id = "HQ2", Kind = "pair", Models = { "F", "E" } };
        var within = new RootCauseHypothesis { Id = "WS3", Kind = "within", Models = { "D" } };

        Assert.Equal("Model F's critical errors, compared with Model E", RootCauseLabels.Of(pair, t => "Model " + t));
        Assert.Equal("Model D's time spent on tool rounds", RootCauseLabels.Of(within, t => "Model " + t));
    }

    [Fact]
    public void OnlyTheTopicIsCut_NeverAModelName()
    {
        string longTopic = string.Join(" ", Enumerable.Range(1, 14).Select(i => "word" + i));
        var hypothesis = new RootCauseHypothesis { Id = "HX9", Kind = "pair", Models = { "A", "B" }, ShortStatement = longTopic + ": Model A" };

        string label = RootCauseLabels.Of(hypothesis, t => "Model " + t);

        Assert.StartsWith("Model A's word1 ", label, StringComparison.Ordinal);
        Assert.EndsWith(", compared with Model B", label, StringComparison.Ordinal);
        Assert.Contains("word" + RootCauseLabels.MaxWords, label, StringComparison.Ordinal);
        Assert.DoesNotContain("word" + (RootCauseLabels.MaxWords + 1), label, StringComparison.Ordinal);
    }

    // --- Next-run estimates -----------------------------------------------------------------------------

    [Theory]
    [InlineData(7.542, 14.0, "about $7.50 and 14 minutes")]
    [InlineData(238.0, 474.0, "about $240 and 7 h 50 min")]
    [InlineData(0.0456, 95.0, "about $0.046 and 1 h 35 min")]
    [InlineData(1234.5, 1199.0, "about $1,200 and 20 h")]
    [InlineData(9.96, 89.4, "about $10 and 89 minutes")]
    [InlineData(12.4, 0.4, "about $12 and less than a minute")]
    public void TheEstimate_HasTwoSignificantFigures_AndHoursFromNinetyMinutes(double cost, double minutes, string expected)
    {
        var run = new RootCauseRecommendedRun { EstimatedCostUsd = cost, EstimatedMinutes = minutes };

        Assert.Equal(expected, RootCauseNextRuns.EstimateText(run));
    }

    [Fact]
    public void AnEstimateWithoutACost_IsEmpty_AndOneWithoutMinutes_GivesTheCostAlone()
    {
        Assert.Equal(string.Empty, RootCauseNextRuns.EstimateText(new RootCauseRecommendedRun { EstimatedMinutes = 30 }));
        Assert.Equal("about $7.50", RootCauseNextRuns.EstimateText(new RootCauseRecommendedRun { EstimatedCostUsd = 7.5 }));
    }

    // --- Grade reasons ------------------------------------------------------------------------------------

    private static RootCauseRobustnessCheck Check(string name, string status, string detail)
        => new() { Name = name, Status = status, Detail = detail };

    [Fact]
    public void AFailedCheck_GivesThePlainClauseOfWhatItFound()
    {
        Assert.Equal("the result depends on one question (leave-one-out check)",
            RootCauseHypotheses.CheckReason(Check(RootCauseHypotheses.LeaveOneOut, RootCauseCheckStatuses.Failed, "Without one question another component is larger.")));
        Assert.Equal("both models ran only once per suite",
            RootCauseHypotheses.CheckReason(Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed, "Both models ran only once per suite.")));
        Assert.Equal("it is unknown whether both panel members agree (the model was not graded by a panel)",
            RootCauseHypotheses.CheckReason(Check(RootCauseHypotheses.PanelAgreement, RootCauseCheckStatuses.NotAssessable, "The model was not graded by a panel.")));
    }

    [Fact]
    public void TheGradeReasons_AreTheConditionsThatApplied_OneClauseEach()
    {
        var draft = new RootCauseDraft
        {
            Id = "HS1",
            Axis = RootCauseAxes.Speed,
            Kind = "pair",
            Group = "A|B",
            Models = { "A", "B" },
            Established = false,
            EstablishedText = RootCauseHypotheses.PairTestReason,
            PairNullStands = false
        };
        draft.Robustness.Add(Check(RootCauseHypotheses.Replicates, RootCauseCheckStatuses.Failed, "Both models ran only once per suite."));
        draft.Robustness.Add(Check(RootCauseHypotheses.LeaveOneOut, RootCauseCheckStatuses.Passed, "The deciding share stays the largest."));

        var graded = RootCauseHypotheses.Grade(draft, t => "Model " + t);

        Assert.Equal(RootCauseGrades.Low, graded.Grade);
        Assert.Equal(new[] { "both models ran only once per suite", RootCauseHypotheses.PairTestReason }, graded.GradeReasons);
        Assert.True(RootCauseHypotheses.IsRobustnessReason(graded, graded.GradeReasons[0]));
        Assert.False(RootCauseHypotheses.IsRobustnessReason(graded, graded.GradeReasons[1]));
    }

    [Fact]
    public void AStandingNull_TakesThePlaceOfThePairedTestReason()
    {
        var draft = new RootCauseDraft
        {
            Id = "HQ2",
            Axis = RootCauseAxes.Intelligence,
            Kind = "pair",
            Group = "A|B",
            Models = { "A", "B" },
            Established = false,
            EstablishedText = RootCauseHypotheses.PairTestReason,
            PairNullStands = true
        };

        var graded = RootCauseHypotheses.Grade(draft, t => "Model " + t);

        Assert.Equal(RootCauseGrades.Low, graded.Grade);
        Assert.Equal(new[] { RootCauseHypotheses.NullStandsReason }, graded.GradeReasons);
    }

    [Fact]
    public void TheControlLimit_GivesTheReasonThatApplies()
    {
        Assert.Equal("only one other model answered these questions", RootCauseControls.Limit(1, 1).Reason);
        Assert.Equal("every other model that answered these questions comes from the model's own provider", RootCauseControls.Limit(3, 0).Reason);
        Assert.Equal("no other model answered these questions", RootCauseControls.Limit(0, 0).Reason);
    }

    [Fact]
    public void NoGradeReasonOrAxisReasonOfAComputedAnalysis_PrintsARuleDisjunction()
    {
        var result = RootCauseReportFixture.ComparisonFacts().Sheet!.RootCause!;
        Assert.NotEmpty(result.Hypotheses);

        foreach (var hypothesis in result.Hypotheses)
        {
            foreach (string reason in hypothesis.GradeReasons)
            {
                Assert.DoesNotContain(", or ", reason, StringComparison.Ordinal);
                Assert.DoesNotContain(" failed: ", reason, StringComparison.Ordinal);
                Assert.DoesNotContain("could not be assessed", reason, StringComparison.Ordinal);
                Assert.DoesNotContain(". ", reason.TrimEnd('.'), StringComparison.Ordinal);
            }
        }
        foreach (var axis in result.Axes)
        {
            Assert.DoesNotContain(", or ", axis.Reason ?? string.Empty, StringComparison.Ordinal);
        }
    }

    // --- Rule 31: a sentence-shaped fact after words of the writer's own sentence ---------------------------

    private const string ConflictKey = "model.A.style.responseStyleConflict";
    private const string ReasonsKey = "rca.hypothesis.HQ1.1.gradeReasons";

    private static BenchmarkReportFact TextFact(string key, string value, string? display = null)
        => new() { Key = key, Value = System.Text.Json.Nodes.JsonValue.Create(value), Display = display ?? value };

    private static BenchmarkReportFactSheet ClauseSheet()
    {
        var facts = new List<BenchmarkReportFact>
        {
            TextFact("rca.axis.cost.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.axis.intelligence.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.axis.speed.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.hypothesis.HQ1.1.grade", RootCauseGrades.Low),
            TextFact("rca.hypothesis.HQ1.1.label", "Model B's answers without tools, compared with Model A"),
            TextFact(ReasonsKey, "both models ran only once per suite; and no paired test shows that the gap between the two models is real"),
            new BenchmarkReportFact
            {
                Key = ConflictKey,
                Value = System.Text.Json.Nodes.JsonValue.Create(true),
                Display = "Completeness is the lowest dimension, 13.5 points below Accuracy"
            },
        };

        return new BenchmarkReportFactSheet
        {
            Scope = BenchmarkReportFactSheet.ComparisonScopeValue,
            SubjectKey = "comparison:1",
            SubjectLabel = "Comparison #1",
            Peers = new[] { "A", "B" }
                .Select((l, i) => new BenchmarkReportPeer { Letter = l, EntryKey = "run:" + (50 + i), Label = "Orion " + l, Provider = "Northwind", State = "Comparable" })
                .ToList(),
            Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList(),
        };
    }

    private static List<BenchmarkReportValidationNote> ClauseNotes(string paragraph)
    {
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        output.Sections[BenchmarkReportSlots.HypothesesIntelligence] = paragraph;
        return BenchmarkReportPackValidator.Validate(Rca, output, ClauseSheet(), new BenchmarkReportContentSnapshot())
            .Where(n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule
                        && n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesIntelligence, StringComparison.Ordinal))
            .ToList();
    }

    [Theory]
    [InlineData("Completeness was graded under the concise style at {{" + ConflictKey + "}}.", ConflictKey)]
    [InlineData("The grade of {{model:B}} rests on {{" + ReasonsKey + "}}.", ReasonsKey)]
    public void ASentenceShapedFact_AfterWordsOfTheWritersSentence_IsARepairableWarning(string paragraph, string key)
    {
        var note = Assert.Single(ClauseNotes(paragraph));

        Assert.Contains("{{" + key + "}}", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.False(BenchmarkReportPackValidator.BlocksText(note));
    }

    [Theory]
    [InlineData("{{" + ConflictKey + "}}. That gap is the largest of any model.")]
    [InlineData("The grade of {{model:B}} is low for these reasons:\n- {{" + ReasonsKey + "}}")]
    [InlineData("Why the grade is low: {{" + ReasonsKey + "}}.")]
    [InlineData("The writer cites {{rca.hypothesis.HQ1.1.label}} inside a sentence.")]
    public void ASentenceShapedFact_StandingAlone_OrAfterAColon_IsLeftAlone(string paragraph)
    {
        Assert.Empty(ClauseNotes(paragraph));
    }

    [Fact]
    public void TheSentenceShapedFacts_AreTheClauseAndListValuedKeys()
    {
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.hypothesis.HQ1.1.statement"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.hypothesis.HQ1.1.gradeReasons"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.known.3"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.check.1"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.question.2.text"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped("rca.profile.A.flags"));
        Assert.True(RootCauseClaimSupport.IsSentenceShaped(ConflictKey, "Completeness is the lowest dimension, 13.5 points below Accuracy"));

        Assert.False(RootCauseClaimSupport.IsSentenceShaped(ConflictKey, "no response-style conflict"));
        Assert.False(RootCauseClaimSupport.IsSentenceShaped("rca.hypothesis.HQ1.1.label"));
        Assert.False(RootCauseClaimSupport.IsSentenceShaped("rca.profile.A.flag.1"));
        Assert.False(RootCauseClaimSupport.IsSentenceShaped("model.A.quality.index"));
    }

    // --- Panel disagreement: one definition and one wording ------------------------------------------------

    /// <summary>
    /// Model A disagrees on 7 of the 36 answers both members graded; B on 2 of 32, C on 3 of 40, D on 1 of 30.
    /// The other models' median for A is B's share.
    /// </summary>
    private static readonly (string Letter, int Disagreements, int Answers)[] Panel = { ("A", 7, 36), ("B", 2, 32), ("C", 3, 40), ("D", 1, 30) };

    private static RootCauseModelInput PanelModel(string letter, int disagreements, int answers) => new()
    {
        Token = letter,
        Letter = letter,
        Label = "Model label " + letter,
        Provider = "Provider " + letter,
        ModelId = "model-" + letter.ToLowerInvariant(),
        EntryKey = "run:" + letter,
        Evidence = new RootCauseModelEvidence { Panel = true, RunsPerSuite = 1, PanelDisagreements = disagreements, PanelAnswers = answers }
    };

    [Fact]
    public void PanelDisagreement_HasOneBasis_TheNotableFindingsWording_AndACompactProfilePhrase()
    {
        var input = new RootCauseInput
        {
            Scope = RootCauseEngine.ComparisonScope,
            Models = Panel.Select(p => PanelModel(p.Letter, p.Disagreements, p.Answers)).ToList()
        };
        var profiles = RootCauseProfiles.Build(new RootCauseContext(input));

        var notable = BenchmarkNotableFindings.Detect(new BenchmarkNotableFindingsInput
        {
            Models = Panel.Select(p => new BenchmarkNotableModel { Letter = p.Letter, PanelDisagreements = p.Disagreements, PanelAnswers = p.Answers }).ToList()
        }).Where(f => f.Kind == BenchmarkNotableFindings.GraderDisagreementConcentration).ToList();

        // The share of the answers both members graded, against the median of the other three models' shares.
        double rate = 7 / 36.0;
        double othersMedian = 2 / 32.0;
        string phrase = "panel disagreement on " + BenchmarkReportFormat.Percent(rate * 100) + " of the answers both members graded, "
                        + "about three times the other models' median of " + BenchmarkReportFormat.Percent(othersMedian * 100);
        Assert.Equal(phrase, BenchmarkNotableFindings.DisagreementPhrase(rate, othersMedian));

        // The profile states the same rate and median compactly; its note states the rule once.
        var flag = Assert.Single(profiles.Single(p => p.Token == "A").Flags, f => f.Kind == RootCauseProfiles.PanelDisagreement);
        Assert.Equal("panel disagreement 19% (others' median 6%)", flag.Text);
        Assert.Equal(BenchmarkNotableFindings.ProfileDisagreementPhrase(rate, othersMedian), flag.Text);
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(flag.Text));

        var finding = Assert.Single(notable);
        Assert.Equal(new[] { "A" }, finding.Models);
        Assert.Contains(phrase, finding.Summary, StringComparison.Ordinal);

        // B, C and D stand out in neither place.
        Assert.All(profiles.Where(p => p.Token != "A"), p => Assert.DoesNotContain(p.Flags, f => f.Kind == RootCauseProfiles.PanelDisagreement));
    }

    [Theory]
    [InlineData(1.4, "about the same as")]
    [InlineData(2.4, "about twice")]
    [InlineData(3.17, "about three times")]
    [InlineData(4.67, "about five times")]
    [InlineData(15.2, "about 15 times")]
    public void TheRatio_IsStatedToTheNearestWhole(double ratio, string expected)
    {
        Assert.Equal(expected, BenchmarkNotableFindings.AboutTimes(ratio));
    }
}
