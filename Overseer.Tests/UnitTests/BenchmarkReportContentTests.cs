namespace Overseer.Tests.UnitTests;

using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The report pack's verbatim content snapshot: taken from the answer rows as asked and graded,
/// never from the live suite, with answers excerpted at a sentence end, a line break or a word
/// boundary and never inside a table, and claim rulings carried with their roles.
/// </summary>
public class BenchmarkReportContentTests
{
    private static string Verifications(params BenchmarkClaimVerification[] items) => JsonSerializer.Serialize(items);

    private static BenchmarkClaimVerification Verification(string claim, BenchmarkClaimVerdict verdict, params string[] roles)
        => new(0, claim, verdict, null, null) { Roles = roles.Length == 0 ? null : roles };
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
    public void TheCompleteAnswer_IsStoredOnlyWhenTheExcerptWasCut()
    {
        const string longAnswer = "The unicorn catches the gem and your Luck rises.";
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80), new AnswerSpec(12, 2, 1, 80));
        run.Answers[0].AnswerText = longAnswer;
        run.Answers[1].AnswerText = "Short.";

        var snapshot = BenchmarkReportContent.Build(new[] { run }, 20);

        var cut = snapshot.Runs[0].Questions[0];
        Assert.True(cut.AnswerExcerptCut);
        Assert.Equal(longAnswer, cut.AnswerText);

        var whole = snapshot.Runs[0].Questions[1];
        Assert.False(whole.AnswerExcerptCut);
        Assert.Equal("Short.", whole.AnswerExcerpt);
        Assert.Null(whole.AnswerText);
        Assert.DoesNotContain("answerText", BenchmarkReportJson.Serialize(whole));
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
        Assert.Null(ruling.Role);
    }

    [Fact]
    public void ClaimRulings_CarryTheirRoles_FromTheStoredVerification()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 40));
        run.Answers[0].ClaimVerificationJson = Verifications(
            Verification("Ordinary claim.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim),
            Verification("Accused sentence.", BenchmarkClaimVerdict.Supported, BenchmarkClaimRoles.AccusedQuote),
            Verification("Critical quote.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimRoles.UnverifiedClaim),
            Verification("Grader statement.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement),
            Verification("Out-of-rubric basis.", BenchmarkClaimVerdict.Supported, BenchmarkClaimRoles.OutOfRubricBasis),
            Verification("Record without roles.", BenchmarkClaimVerdict.Indeterminate));

        var rulings = BenchmarkReportContent.Build(new[] { run }, 200).Runs[0].Questions[0].ClaimRulings;

        Assert.Equal(
            new[]
            {
                BenchmarkReportContent.ClaimRole, BenchmarkReportContent.AccusedSentenceRole, BenchmarkReportContent.CriticalErrorQuoteRole,
                BenchmarkReportContent.AssessorStatementRole, BenchmarkReportContent.OutOfRubricBasisRole, BenchmarkReportContent.ClaimRole
            },
            rulings.Select(r => r.Role));
        Assert.True(BenchmarkReportContent.IsAnswerSentenceRole(BenchmarkReportContent.AccusedSentenceRole));
        Assert.True(BenchmarkReportContent.IsAnswerSentenceRole(BenchmarkReportContent.CriticalErrorQuoteRole));
        Assert.False(BenchmarkReportContent.IsAnswerSentenceRole(BenchmarkReportContent.AssessorStatementRole));
        Assert.False(BenchmarkReportContent.IsAnswerSentenceRole(BenchmarkReportContent.OutOfRubricBasisRole));
        Assert.False(BenchmarkReportContent.IsAnswerSentenceRole(null));
    }

    [Theory]
    [InlineData(null, "refuted", "refuted")]
    [InlineData(BenchmarkReportContent.ClaimRole, "refuted", "Answer sentence — refuted")]
    [InlineData(BenchmarkReportContent.AccusedSentenceRole, "supported", "Answer sentence accused by a grader — supported (the verifier sided with the answer)")]
    [InlineData(BenchmarkReportContent.AccusedSentenceRole, "refuted", "Answer sentence accused by a grader — refuted (the verifier sided with the grader)")]
    [InlineData(BenchmarkReportContent.CriticalErrorQuoteRole, "refuted", "Answer sentence a grader flagged as a critical error — refuted (the verifier sided with the grader)")]
    [InlineData(BenchmarkReportContent.CriticalErrorQuoteRole, "indeterminate", "Answer sentence a grader flagged as a critical error — indeterminate")]
    [InlineData(BenchmarkReportContent.AssessorStatementRole, "refuted", "Grader's statement — refuted (the verifier sided with the answer)")]
    [InlineData(BenchmarkReportContent.AssessorStatementRole, "supported", "Grader's statement — supported (the verifier sided with the grader)")]
    [InlineData(BenchmarkReportContent.OutOfRubricBasisRole, "refuted", "Grader's basis for an out-of-rubric deduction — refuted (the verifier sided with the answer)")]
    [InlineData(BenchmarkReportContent.OutOfRubricBasisRole, "supported", "Grader's basis for an out-of-rubric deduction — supported (the verifier sided with the grader)")]
    public void RulingLabel_NamesTheRole_AndWhichSideTheVerifierTook(string? role, string verdict, string expected)
    {
        Assert.Equal(expected, BenchmarkReportContent.RulingLabel(role, verdict));
        Assert.DoesNotContain("was right", BenchmarkReportContent.RulingLabel(role, verdict));
        Assert.DoesNotContain("was wrong", BenchmarkReportContent.RulingLabel(role, verdict));
    }

    [Theory]
    [InlineData("Gems are caught.", "dothrow.c", "Gems are caught. (dothrow.c)")]
    [InlineData("dothrow.c:412", "dothrow.c:412", "dothrow.c:412")]
    [InlineData("  dothrow.c:412 ", "dothrow.c:412", "dothrow.c:412")]
    [InlineData(null, "dothrow.c", "dothrow.c")]
    [InlineData("Gems are caught.", null, "Gems are caught.")]
    public void TheRationale_PrintsACitationEqualToTheBasisOnce(string? basis, string? citation, string expected)
    {
        var verification = new BenchmarkClaimVerification(0, "A claim.", BenchmarkClaimVerdict.Refuted, citation, basis);

        Assert.Equal(expected, BenchmarkReportContent.RationaleOf(verification));
    }

    [Fact]
    public void AnExcerpt_EndsAtTheLastSentenceEndBeforeTheLimit()
    {
        const string text = "First sentence here. Second sentence is longer than the limit allows.";

        var (excerpt, cut) = BenchmarkReportContent.Excerpt(text, 30);

        Assert.Equal("First sentence here. …", excerpt);
        Assert.True(cut);
    }

    [Fact]
    public void AnExcerpt_EndsAtALineBreak_WhenNoSentenceEndsLater()
    {
        const string text = "Line one is here\nLine two goes on and on and on.";

        Assert.Equal("Line one is here …", BenchmarkReportContent.Excerpt(text, 20).Excerpt);
    }

    [Fact]
    public void AnExcerpt_FallsBackToWhitespace_WhenTheLastSentenceEndIsTooEarly()
    {
        const string text = "Hi. This is a long run of words without any stop at all";

        Assert.Equal("Hi. This is a long run of…", BenchmarkReportContent.Excerpt(text, 30).Excerpt);
    }

    [Fact]
    public void AnExcerpt_EndsBeforeATable_WhenTheLimitFallsInsideIt()
    {
        const string text = "Intro text.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\nAfter.";

        var (excerpt, cut) = BenchmarkReportContent.Excerpt(text, 35);

        Assert.Equal("Intro text.\n\n" + BenchmarkReportContent.TableFollows, excerpt);
        Assert.True(cut);
        Assert.Equal(BenchmarkReportContent.TableFollows, BenchmarkReportContent.Excerpt("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |", 25).Excerpt);
    }

    [Fact]
    public void AnExcerpt_CutJustAfterATable_PutsTheEllipsisInItsOwnParagraph()
    {
        const string text = "| A | B |\n|---|---|\n| 1 | 2 |\nThen a long closing sentence that runs past the limit.";

        Assert.Equal("| A | B |\n|---|---|\n| 1 | 2 |\n\n…", BenchmarkReportContent.Excerpt(text, 40).Excerpt);
    }
}
