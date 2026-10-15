namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// A covered model's failure profile: what the graders recorded over its scored answers, each count's
/// fact, the outcome classes from scoring method 13 only, a panel run's mean levels, the matrix misses
/// from three models, the grader findings by category, and the weakest answers' grader comments.
/// </summary>
public class BenchmarkComparisonModelProfileTests
{
    /// <summary>A single-assessor run of four answers, each scored 80 at level 5 on every dimension.</summary>
    private static BenchmarkRun FourAnswerRun(long id = 41)
        => Run(id, "Northwind", "orion-max",
            new AnswerSpec(301, 1, 1, 80), new AnswerSpec(302, 2, 1, 80), new AnswerSpec(303, 3, 1, 80), new AnswerSpec(304, 4, 1, 80));

    private static BenchmarkRunAnswer AnswerAt(BenchmarkRun run, int order) => run.Answers.Single(a => a.OrderIndex == order);

    private static List<BenchmarkReportFact> FactsOf(BenchmarkComparisonModelProfile profile)
    {
        var facts = new BenchmarkReportFacts.FactList();
        profile.AddFacts(facts, "model.A.");
        return facts.Sorted();
    }

    private static BenchmarkReportFact Fact(IEnumerable<BenchmarkReportFact> facts, string key) => Assert.Single(facts, f => f.Key == key);

    [Fact]
    public void TheScoredAnswers_AreTheDenominator_AndMayMisleadCountsLowAccuracyOrACriticalError()
    {
        var run = FourAnswerRun();
        AnswerAt(run, 2).AccuracyLevel = 3;
        AnswerAt(run, 3).CriticalError = true;
        AnswerAt(run, 4).AccuracyLevel = 4;

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.Equal(4, profile.Answers);
        Assert.Equal(2, profile.MayMislead);
        var facts = FactsOf(profile);
        Assert.Equal("4 answers", Fact(facts, "model.A.profile.answers").Display);
        Assert.Equal("2 of 4 answers", Fact(facts, "model.A.profile.mayMislead").Display);
        Assert.Equal("about 10 in 20 answers", Fact(facts, "model.A.profile.mayMisleadPer20").Display);
    }

    [Fact]
    public void AnAnswerOutsideTheIndex_IsNotCounted()
    {
        var run = FourAnswerRun();
        AnswerAt(run, 4).Status = BenchmarkAnswerStatus.ProviderError;

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.Equal(3, profile.Answers);
    }

    [Fact]
    public void Outcomes_AreUnavailableBeforeScoringMethod13()
    {
        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { FourAnswerRun() });

        Assert.False(profile.OutcomesRecorded);
        var facts = FactsOf(profile);
        foreach (string key in new[] { "correct", "partial", "incorrect", "notAttempted", "noAnswer", "wrongInsteadOfAbstaining" })
        {
            var fact = Fact(facts, "model.A.profile.outcome." + key);
            Assert.False(fact.Available);
            Assert.Equal(BenchmarkComparisonModelProfile.OutcomesNotRecordedReason, fact.UnavailableReason);
        }
    }

    [Fact]
    public void OneRunBeforeScoringMethod13_MakesTheOutcomesUnavailable()
    {
        var current = FourAnswerRun(41);
        current.ScoringMethodVersion = 13;
        var old = FourAnswerRun(42);

        Assert.False(BenchmarkComparisonModelProfile.FromRuns(new[] { current, old }).OutcomesRecorded);
        Assert.True(BenchmarkComparisonModelProfile.FromRuns(new[] { current }).OutcomesRecorded);
    }

    [Fact]
    public void Outcomes_FromScoringMethod13_ClassifyEachAnswer_AndCountWrongInsteadOfAbstaining()
    {
        var run = FourAnswerRun();
        run.ScoringMethodVersion = 13;
        AnswerAt(run, 2).AccuracyLevel = 4;
        AnswerAt(run, 3).AccuracyLevel = 2;
        AnswerAt(run, 4).AccuracyLevel = 3;
        AnswerAt(run, 4).NotAttempted = true;

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.True(profile.OutcomesRecorded);
        Assert.Equal((1, 1, 1, 1, 0), (profile.Correct, profile.Partial, profile.Incorrect, profile.NotAttempted, profile.NoAnswer));
        var facts = FactsOf(profile);
        Assert.Equal("1 of 4 answers", Fact(facts, "model.A.profile.outcome.correct").Display);
        var wrong = Fact(facts, "model.A.profile.outcome.wrongInsteadOfAbstaining");
        Assert.False(wrong.Available);
        Assert.Equal("Fewer than 3 answers were incorrect or not attempted (2), too few for a rate.", wrong.UnavailableReason);
    }

    [Fact]
    public void WrongInsteadOfAbstaining_IsARate_FromThreeIncorrectOrNotAttemptedAnswers()
    {
        var run = FourAnswerRun();
        run.ScoringMethodVersion = 13;
        AnswerAt(run, 2).AccuracyLevel = 2;
        AnswerAt(run, 3).AccuracyLevel = 2;
        AnswerAt(run, 4).AccuracyLevel = 3;
        AnswerAt(run, 4).NotAttempted = true;

        var wrong = Fact(FactsOf(BenchmarkComparisonModelProfile.FromRuns(new[] { run })), "model.A.profile.outcome.wrongInsteadOfAbstaining");

        Assert.True(wrong.Available);
        Assert.Equal("67% (2 of the 3 answers that were incorrect or not attempted)", wrong.Display);
    }

    [Fact]
    public void WrongInsteadOfAbstaining_IsUnavailable_WithNothingIncorrectOrNotAttempted()
    {
        var run = FourAnswerRun();
        run.ScoringMethodVersion = 13;

        var fact = Fact(FactsOf(BenchmarkComparisonModelProfile.FromRuns(new[] { run })), "model.A.profile.outcome.wrongInsteadOfAbstaining");

        Assert.False(fact.Available);
        Assert.Equal("No answer was incorrect or not attempted.", fact.UnavailableReason);
    }

    [Fact]
    public void Deficits_CountAnswersAtLevelTwoOrBelow_PerDimension()
    {
        var run = FourAnswerRun();
        AnswerAt(run, 1).CompletenessLevel = 2;
        AnswerAt(run, 2).CompletenessLevel = 1;
        AnswerAt(run, 3).ReadabilityLevel = 3;

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.Equal(0, profile.Deficits["accuracy"]);
        Assert.Equal(2, profile.Deficits["completeness"]);
        Assert.Equal(0, profile.Deficits["readability"]);
        Assert.Equal("2 of 4 answers", Fact(FactsOf(profile), "model.A.profile.deficit.completeness").Display);
    }

    [Fact]
    public void APanelRun_AveragesBothMembersLevels()
    {
        // Member A graded every dimension at level 5; member B accuracy 2, completeness 1.
        var run = AsPanelRun(FourAnswerRun(), accuracyLevel: 2, completenessLevel: 1, concisenessLevel: 5, readabilityLevel: 5);
        AnswerAt(run, 1).PanelDisagreed = true;
        foreach (int order in new[] { 2, 3, 4 }) AnswerAt(run, order).PanelDisagreed = false;

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        // Accuracy (5 + 2) / 2 = 3.5 is below 4; completeness (5 + 1) / 2 = 3 is above 2.
        Assert.Equal(4, profile.MayMislead);
        Assert.Equal(0, profile.Deficits["completeness"]);
        Assert.Equal(4, profile.PanelAnswers);
        Assert.Equal(1, profile.PanelDisagreements);

        // A single-assessor run reads the assessor's own levels.
        var single = FourAnswerRun(42);
        foreach (var answer in single.Answers) answer.CompletenessLevel = 1;
        var alone = BenchmarkComparisonModelProfile.FromRuns(new[] { single });
        Assert.Equal(4, alone.Deficits["completeness"]);
        Assert.Null(alone.PanelAnswers);
    }

    [Fact]
    public void OutputAndToolSignals_AreCountedPerAnswer()
    {
        var run = FourAnswerRun();
        AnswerAt(run, 1).AnswerFlags = (int)BenchmarkAnswerFlags.Truncated;
        AnswerAt(run, 2).AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed;
        AnswerAt(run, 2).ToolCallCount = 3;
        AnswerAt(run, 3).ToolBudgetExhausted = true;
        AnswerAt(run, 3).ToolCallCount = 1;
        AnswerAt(run, 4).TerminationReason = "iteration_limit";

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.Equal(1, profile.Truncated);
        Assert.Equal(2, profile.OutputDefects);
        Assert.Equal(2, profile.NoToolAnswers);
        Assert.Equal(2, profile.ToolLimitHits);
        var facts = FactsOf(profile);
        Assert.Equal("1 of 4 answers", Fact(facts, "model.A.profile.truncated").Display);
        Assert.Equal("2 of 4 answers", Fact(facts, "model.A.profile.toolLimitHits").Display);
    }

    [Fact]
    public void TheMedianAnswerLength_IsTheMiddleOfTheScoredAnswers()
    {
        var run = FourAnswerRun();
        AnswerAt(run, 1).AnswerText = new string('a', 10);
        AnswerAt(run, 2).AnswerText = new string('a', 20);
        AnswerAt(run, 3).AnswerText = new string('a', 30);
        AnswerAt(run, 4).AnswerText = new string('a', 2000);

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { run });

        Assert.Equal(25, profile.MedianAnswerChars);
        Assert.Equal("25 characters", Fact(FactsOf(profile), "model.A.profile.answerChars.median").Display);
        Assert.Equal(2.0, BenchmarkComparisonModelProfile.Median(new[] { 3.0, 1.0, 2.0 }));
        Assert.Null(BenchmarkComparisonModelProfile.Median(Array.Empty<double>()));
    }

    private static BenchmarkReportQuestion Row(int number, params (string Letter, double? Score)[] cells) => new()
    {
        Number = number,
        Models = cells.Select(c => new BenchmarkReportQuestionModelScore { Letter = c.Letter, Score = c.Score }).ToList()
    };

    [Fact]
    public void TheMatrixCounts_SetAModelAgainstTheOthers_FromThreeModels()
    {
        var questions = new[]
        {
            Row(1, ("A", 50), ("B", 80), ("C", 75)),   // isolated: at most 50 where the others' median is 77.5
            Row(2, ("A", 30), ("B", 40), ("C", 90)),   // shared: below 50, and so is one of the two others
            Row(3, ("A", 51), ("B", 80), ("C", 90)),   // just above the isolated-miss score
            Row(4, ("A", 45), ("B", 74), ("C", 74)),   // the others' median just below 75, and neither below 50
            Row(5, ("A", 90), ("B", 90), ("C", null))  // a tie for the lowest counts
        };

        var counts = BenchmarkComparisonModelProfile.MatrixCounts(questions, "A", coveredModels: 3);

        Assert.NotNull(counts);
        Assert.Equal((1, 1, 5, 5), counts.Value);
        Assert.Null(BenchmarkComparisonModelProfile.MatrixCounts(questions, "A", coveredModels: 2));
    }

    [Fact]
    public void TheMatrixFacts_AreUnavailable_WithFewerThanThreeModels()
    {
        var facts = new BenchmarkReportFacts.FactList();
        BenchmarkComparisonModelProfile.AddMatrixFacts(facts, "model.A.", new[] { Row(1, ("A", 10), ("B", 90)) }, "A", coveredModels: 2);

        var sorted = facts.Sorted();
        foreach (string key in new[] { "isolatedMisses", "sharedMisses", "lowestScorer" })
        {
            var fact = Fact(sorted, "model.A.profile." + key);
            Assert.False(fact.Available);
            Assert.Equal(BenchmarkComparisonModelProfile.TooFewModelsReason, fact.UnavailableReason);
        }
    }

    [Fact]
    public void GraderFindings_CountTheRunsThatRaisedAWeaknessOfEachCategory()
    {
        var first = FourAnswerRun(41);
        first.AssessmentJson = Synthesis(("weakness", "accuracy", new[] { 1 }, "Wrong rule."), ("strength", "readability", new[] { 2 }, "Clear."));
        var second = AsPanelRun(FourAnswerRun(42), 5, 5, 5, 5);
        second.AssessmentJson = Synthesis(("strength", "accuracy", new[] { 1 }, "Right."));
        second.CoAssessorSynthesisJson = Synthesis(("weakness", "completeness", new[] { 3 }, "Leaves out a step."), ("weakness", "accuracy", new[] { 2 }, "Wrong."));
        var third = FourAnswerRun(43);

        var profile = BenchmarkComparisonModelProfile.FromRuns(new[] { first, second, third });

        Assert.Equal(3, profile.Runs);
        Assert.Equal(2, profile.RunsWithSynthesis);
        Assert.Equal(2, profile.WeaknessRunsByCategory["accuracy"]);
        Assert.Equal(1, profile.WeaknessRunsByCategory["completeness"]);
        Assert.Equal(0, profile.WeaknessRunsByCategory["readability"]);
        var facts = FactsOf(profile);
        Assert.Equal("2 of 3 runs", Fact(facts, "model.A.profile.findings.runsWithSynthesis").Display);
        Assert.Equal("2 of 2 runs", Fact(facts, "model.A.profile.findings.accuracy").Display);
        Assert.Equal("0 of 2 runs", Fact(facts, "model.A.profile.findings.critical_error").Display);
    }

    [Fact]
    public void GraderFindings_AreUnavailable_WithoutStructuredFindings()
    {
        var facts = FactsOf(BenchmarkComparisonModelProfile.FromRuns(new[] { FourAnswerRun() }));

        Assert.Equal("0 of 1 run", Fact(facts, "model.A.profile.findings.runsWithSynthesis").Display);
        foreach (string category in BenchmarkComparisonModelProfile.FindingCategories)
        {
            var fact = Fact(facts, "model.A.profile.findings." + category);
            Assert.False(fact.Available);
            Assert.Equal(BenchmarkComparisonModelProfile.NoStructuredFindingsReason, fact.UnavailableReason);
        }
    }

    [Fact]
    public void EachItem_KeepsItsLowestScoringRun_AndThatRunsGraderComments()
    {
        var higher = Run(41, "Northwind", "orion-max", new AnswerSpec(301, 1, 1, 60));
        higher.Answers[0].ReviewComment = "Mostly right.";
        var lower = AsPanelRun(Run(42, "Northwind", "orion-max", new AnswerSpec(301, 1, 1, 40)), 2, 3, 5, 5);
        lower.Answers[0].ReviewComment = "Wrong about the rule.";
        lower.Answers[0].CriticalError = true;
        var record = BenchmarkCoAssessmentRecord.Parse(lower.Answers[0].CoAssessmentJson)!;
        record.Comment = "Misses the exception.";
        lower.Answers[0].CoAssessmentJson = record.Serialize();

        var item = Assert.Single(BenchmarkComparisonModelProfile.FromRuns(new[] { higher, lower }).Items);

        Assert.Equal("301", item.QuestionKey);
        Assert.Equal(1, item.ItemRevisionUsed);
        Assert.Equal(40.0, item.LowestScore);
        Assert.Equal(42L, item.RunId);
        Assert.Equal(
            new[]
            {
                new BenchmarkComparisonModelGraderNote(BenchmarkReportFacts.PanelMemberARole, true, "Wrong about the rule."),
                new BenchmarkComparisonModelGraderNote(BenchmarkReportFacts.PanelMemberBRole, false, "Misses the exception.")
            },
            item.Notes);
    }

    [Fact]
    public void ACommentIsCut_AtAWordBoundary_WithinItsLimit()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 100));

        string cut = BenchmarkComparisonModelProfile.Cut(text, BenchmarkComparisonModelProfile.CommentChars);

        Assert.True(cut.Length <= BenchmarkComparisonModelProfile.CommentChars);
        Assert.EndsWith("word" + BenchmarkReportContent.Ellipsis, cut);
        Assert.Equal("Short\ncomment.".Replace('\n', ' '), BenchmarkComparisonModelProfile.Cut("Short\ncomment.", 300));
    }

    [Fact]
    public void TheComparisonSheet_CarriesEveryProfileKey_ForEachModel()
    {
        var sheet = ComparisonFacts().Sheet!;

        foreach (var model in sheet.Peers)
        {
            string prefix = "model." + model.Letter + ".profile.";
            Assert.Contains(sheet.Facts, f => f.Key == prefix + "answers" && f.Display == "6 answers");
            Assert.Contains(sheet.Facts, f => f.Key == prefix + "isolatedMisses" && f.Available);
            Assert.Contains(sheet.Facts, f => f.Key == prefix + "findings.runsWithSynthesis");
        }

        // The fixture's runs are scored under method 12.
        Assert.False(Assert.Single(sheet.Facts, f => f.Key == "model.A.profile.outcome.correct").Available);
    }
}
