namespace Overseer.Tests.UnitTests;

using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The report pack's verbatim content snapshot: taken from the answer rows as asked and graded,
/// never from the live suite, with answers excerpted on a word boundary.
/// </summary>
public class BenchmarkReportContentTests
{
    [Fact]
    public void QuestionTextAndRubric_ComeFromTheAnswerRows_NotTheLiveQuestion()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80));
        var answer = run.Answers[0];
        answer.QuestionText = "What does Excalibur do? (as asked)";
        answer.ExpectedPointsUsed = "- Rubric as graded.";
        answer.BenchmarkQuestion = new BenchmarkQuestion
        {
            Id = 11,
            QuestionText = "Edited later: what does Excalibur do now?",
            ExpectedPoints = "- Rubric edited later."
        };

        var snapshot = BenchmarkReportContent.Build(new[] { run }, 200);

        var question = Assert.Single(Assert.Single(snapshot.Runs).Questions);
        Assert.Equal("What does Excalibur do? (as asked)", question.QuestionText);
        Assert.Equal("- Rubric as graded.", question.ExpectedPoints);
        Assert.True(question.ExpectedPointsRecorded);
        Assert.Equal("11", question.QuestionKey);
        Assert.Equal(1, question.ItemRevisionUsed);
        Assert.Equal(1, question.Number);
    }

    [Fact]
    public void AnUnrecordedRubric_IsStoredAsNotRecorded_NeverAsTheLiveRubric()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80));
        run.Answers[0].ExpectedPointsRecorded = false;
        run.Answers[0].ExpectedPointsUsed = "- stale value";

        var question = BenchmarkReportContent.Build(new[] { run }, 200).Runs[0].Questions[0];

        Assert.False(question.ExpectedPointsRecorded);
        Assert.Null(question.ExpectedPoints);
    }

    [Theory]
    [InlineData("alpha beta gamma", 8, "alpha…", true)]
    [InlineData("alpha beta gamma", 10, "alpha beta…", true)]
    [InlineData("alpha beta gamma", 16, "alpha beta gamma", false)]
    [InlineData("alpha beta gamma", 100, "alpha beta gamma", false)]
    [InlineData("alphabetagamma", 5, "alpha…", true)]
    public void AnswerExcerpts_AreCutOnWhitespace_WithAnEllipsis(string text, int max, string expected, bool cut)
    {
        var (excerpt, wasCut) = BenchmarkReportContent.Excerpt(text, max);

        Assert.Equal(expected, excerpt);
        Assert.Equal(cut, wasCut);
    }

    [Fact]
    public void TheSnapshot_CarriesTheExcerptLength_AndTheCutFlag()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80));
        run.Answers[0].AnswerText = "The unicorn catches the gem and your Luck rises.";

        var snapshot = BenchmarkReportContent.Build(new[] { run }, 20);

        Assert.Equal(20, snapshot.AnswerExcerptChars);
        var question = snapshot.Runs[0].Questions[0];
        Assert.Equal("The unicorn catches…", question.AnswerExcerpt);
        Assert.True(question.AnswerExcerptCut);
    }

    [Fact]
    public void AGroupSubject_HasOneBlockPerRun_InRunIdOrder_NumberedLikeTheFactSheet()
    {
        var later = Run(7, "OpenAI", "subject", new AnswerSpec(12, 2, 1, 60), new AnswerSpec(11, 1, 1, 80), new AnswerSpec(13, 3, 1, 70));
        var earlier = Run(3, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 90), new AnswerSpec(12, 2, 1, 50));

        var snapshot = BenchmarkReportContent.Build(new[] { later, earlier }, 200);

        Assert.Equal(new long[] { 3, 7 }, snapshot.Runs.Select(r => r.RunId));
        Assert.Equal(new[] { 1, 2 }, snapshot.Runs[0].Questions.Select(q => q.OrderIndex));
        Assert.Equal(new[] { 1, 2, 3 }, snapshot.Runs[1].Questions.Select(q => q.OrderIndex));

        // Question 13 was asked only by the later run, so it is numbered after the earlier run's items.
        Assert.Equal(new[] { 1, 2, 3 }, snapshot.Runs[1].Questions.Select(q => q.Number));

        var slots = BenchmarkReportFacts.NumberQuestions(new[] { later, earlier });
        foreach (var question in snapshot.Runs.SelectMany(r => r.Questions))
        {
            Assert.Equal(question.Number, slots.Single(s => s.QuestionKey == question.QuestionKey).Number);
        }
    }

    [Fact]
    public void GraderEvidence_AndClaimRulings_AreCapturedPerMember()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 25));
        run.CoAssessorModelConfigurationId = 2;
        run.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-haiku-5", displayName: "Claude Haiku 5");

        var answer = run.Answers[0];
        answer.ReviewComment = "Critical error.";
        answer.CriticalError = true;
        answer.CriticalErrorQuote = "A thrown gem always shatters";
        answer.AssessmentEvidenceJson = "{\"accuracy\":\"The gem does not always shatter.\"}";
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = 30;
        answer.CoAssessmentJson = "{\"comment\":\"Fabricated breakage.\",\"completenessEvidence\":\"Omits the catch.\"}";
        answer.ClaimVerificationJson = "[{\"claimIndex\":0,\"claim\":\"A thrown gem always shatters.\",\"verdict\":\"Refuted\",\"citation\":\"dothrow.c\",\"basis\":\"Gems are caught.\"}]";

        var question = BenchmarkReportContent.Build(new[] { run }, 200).Runs[0].Questions[0];

        Assert.Equal(2, question.Graders.Count);
        var memberA = question.Graders[0];
        Assert.Equal(BenchmarkReportFacts.PanelMemberARole, memberA.Role);
        Assert.Equal("Gemini 3.8 Flash", memberA.Label);
        Assert.Equal(25, memberA.Score);
        Assert.Equal("Critical error.", memberA.Comment);
        Assert.Equal(new[] { "Accuracy: The gem does not always shatter.", "Critical error: \"A thrown gem always shatters\"" }, memberA.Evidence);

        var memberB = question.Graders[1];
        Assert.Equal(BenchmarkReportFacts.PanelMemberBRole, memberB.Role);
        Assert.Equal("Claude Haiku 5", memberB.Label);
        Assert.Equal(30, memberB.Score);
        Assert.Equal("Fabricated breakage.", memberB.Comment);
        Assert.Equal(new[] { "Completeness: Omits the catch." }, memberB.Evidence);

        var ruling = Assert.Single(question.ClaimRulings);
        Assert.Equal("refuted", ruling.Verdict);
        Assert.Equal("A thrown gem always shatters.", ruling.Claim);
        Assert.Equal("Gems are caught. (dothrow.c)", ruling.Rationale);
    }
}
