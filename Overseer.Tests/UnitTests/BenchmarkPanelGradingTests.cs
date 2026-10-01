namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Panel member B's verdict and the panel score: how a co-assessment is written beside member A's
/// verdict, how the two combine into the published score, what a re-run clears and a rescore
/// recomputes, and which grader overrides a panel run refuses. The helpers are driven directly; one
/// case grades both members through the real agent loop behind a fake provider.
/// </summary>
public class BenchmarkPanelGradingTests
{
    private const string AnswerText = "Silver dragon scale mail gives AC 9 and reflection. It weighs 40.";

    private static BenchmarkScoringConstants Constants => BenchmarkScoringConstants.Default;

    private static string Verdict(
        int accuracy,
        int completeness,
        int conciseness,
        int readability,
        bool criticalError = false,
        string? quote = null,
        string accuracyEvidence = "Matches rubric.",
        string comment = "Graded.",
        bool notAttempted = false)
        => JsonSerializer.Serialize(new
        {
            accuracyLevel = accuracy,
            completenessLevel = completeness,
            concisenessLevel = conciseness,
            readabilityLevel = readability,
            criticalError,
            criticalErrorQuote = quote,
            notAttempted,
            unverifiedClaims = Array.Empty<string>(),
            accuracyEvidence,
            completenessEvidence = "Matches rubric.",
            comment
        });

    private static PerQuestionAssessmentParseResult Parse(string verdictJson)
        => BenchmarkAssessmentParser.ParsePerQuestion(verdictJson, AnswerText);

    /// <summary>An answer member A has scored, with every column of its verdict set.</summary>
    private static BenchmarkRunAnswer AnswerScoredByA(int quality = 87, bool criticalError = false) => new()
    {
        OrderIndex = 1,
        QuestionText = "What does silver dragon scale mail give?",
        AnswerText = AnswerText,
        Status = BenchmarkAnswerStatus.Ok,
        Difficulty = BenchmarkDifficulty.Simple,
        AssessedDifficulty = 25,
        DurationMs = 2000,
        ExpectedPointsUsed = "- AC 9 and reflection",
        ExpectedPointsRecorded = true,
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        AccuracyLevel = 5,
        CompletenessLevel = 5,
        ConcisenessLevel = 5,
        ReadabilityLevel = 5,
        AccuracyScore = 87,
        CompletenessScore = 87,
        ConcisenessScore = 87,
        ReadabilityScore = 87,
        QualityScore = quality,
        RawQualityScore = quality,
        Score = quality,
        CriticalError = criticalError,
        CriticalErrorQuote = criticalError ? "Silver dragon scale mail gives AC 9" : null,
        ReviewComment = "Member A's comment.",
        AssessmentRawText = "Member A's raw verdict.",
        AssessmentEvidenceJson = "{\"accuracy\":\"Matches rubric.\"}",
        UnverifiedClaimsJson = "[\"It weighs 40.\"]",
        UnverifiedClaimCount = 1,
        AnswerFlags = (int)BenchmarkAnswerFlags.OmissionAsAccuracy,
        AssessorBoardChars = 120,
        AssessmentInputTokens = 1000,
        AssessmentOutputTokens = 200
    };

    private static void AssertMemberAUnchanged(BenchmarkRunAnswer answer, int quality = 87, bool criticalError = false)
    {
        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.AssessmentStatus);
        Assert.Equal(5, answer.AccuracyLevel);
        Assert.Equal(5, answer.CompletenessLevel);
        Assert.Equal(5, answer.ConcisenessLevel);
        Assert.Equal(5, answer.ReadabilityLevel);
        Assert.Equal(quality, answer.QualityScore);
        Assert.Equal(quality, answer.RawQualityScore);
        Assert.Equal(quality, answer.Score);
        Assert.Equal(criticalError, answer.CriticalError);
        Assert.Equal("Member A's comment.", answer.ReviewComment);
        Assert.Equal("Member A's raw verdict.", answer.AssessmentRawText);
        Assert.Equal("{\"accuracy\":\"Matches rubric.\"}", answer.AssessmentEvidenceJson);
        Assert.Equal("[\"It weighs 40.\"]", answer.UnverifiedClaimsJson);
        Assert.Equal((int)BenchmarkAnswerFlags.OmissionAsAccuracy, answer.AnswerFlags);
        Assert.Equal(120, answer.AssessorBoardChars);
        Assert.Null(answer.AssessmentError);
    }

    private static BenchmarkRun PanelRun(long assessorId = 1, long coAssessorId = 2) => BenchmarkModelSnapshots.Attach(new BenchmarkRun
    {
        SuiteName = "Panel Suite",
        AssessorModelConfigurationId = assessorId,
        CoAssessorModelConfigurationId = coAssessorId,
        ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
        Status = BenchmarkRunStatus.Running,
        TotalQuestionCount = 1,
        StartedAtUtc = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc)
    });

    // --- Applying member B's verdict --------------------------------------------------------

    [Fact]
    public void ApplyCoAssessment_WritesOnlyTheCoColumns()
    {
        var answer = AnswerScoredByA();
        var parse = Parse(Verdict(4, 5, 6, 6, accuracyEvidence: "The answer states AC 9 without the reflection detail the rubric asks for.", comment: "Member B's comment."));
        Assert.True(parse.Success);

        BenchmarkService.ApplyCoAssessment(answer, parse, terminalError: null, Constants, boardChars: 140, finalText: null);

        var (quality, rawQuality, _) = BenchmarkScoring.Quality(4, 5, 6, 6, false, Constants);
        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.CoAssessmentStatus);
        Assert.Null(answer.CoAssessmentError);
        Assert.Equal(quality, answer.CoAssessmentQualityScore);
        Assert.Equal(rawQuality, answer.CoAssessmentRawQualityScore);
        Assert.False(answer.CoAssessmentCriticalError);
        Assert.Equal(140, answer.CoAssessorBoardChars);
        Assert.NotNull(answer.CoAssessmentRawText);

        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson);
        Assert.NotNull(record);
        Assert.Equal(4, record!.AccuracyLevel);
        Assert.Equal(5, record.CompletenessLevel);
        Assert.Equal(6, record.ConcisenessLevel);
        Assert.Equal(6, record.ReadabilityLevel);
        Assert.Equal(quality, record.QualityScore);
        Assert.Equal(rawQuality, record.RawQualityScore);
        Assert.Equal("Member B's comment.", record.Comment);
        Assert.NotNull(record.Flags);

        var view = BenchmarkVerdictView.FromCoAssessment(answer);
        Assert.NotNull(view);
        Assert.Equal(BenchmarkPanelMember.B, view!.Member);
        Assert.Equal(quality, view.QualityScore);

        // Member A's verdict, its flags and the panel columns are untouched: the panel score is the
        // caller's to compute once both members are in.
        AssertMemberAUnchanged(answer);
        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);
    }

    [Fact]
    public void ApplyCoAssessment_CriticalErrorWithAQuote_IsCappedAndRecorded()
    {
        var answer = AnswerScoredByA();
        var parse = Parse(Verdict(5, 5, 6, 6, criticalError: true, quote: "Silver dragon scale mail gives AC 9", accuracyEvidence: "The answer says silver dragon scale mail gives AC 9, which is false."));

        BenchmarkService.ApplyCoAssessment(answer, parse, null, Constants, null, null);

        var (quality, _, capApplied) = BenchmarkScoring.Quality(5, 5, 6, 6, true, Constants);
        Assert.True(capApplied);
        Assert.Equal(quality, answer.CoAssessmentQualityScore);
        Assert.True(answer.CoAssessmentCriticalError);
        Assert.Equal("Silver dragon scale mail gives AC 9", BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.CriticalErrorQuote);
        Assert.False(answer.CriticalError);
        Assert.Null(answer.CriticalErrorQuote);
    }

    [Fact]
    public void ApplyCoAssessment_NotAttempted_IsFlooredAndRecordedOnTheRecordAndTheColumn()
    {
        var answer = AnswerScoredByA();
        var parse = Parse(Verdict(5, 0, 0, 0, notAttempted: true, comment: "The answer says it could not verify the value."));
        Assert.True(parse.Success);

        BenchmarkService.ApplyCoAssessment(answer, parse, null, Constants, null, null);

        var (quality, rawQuality, _) = BenchmarkScoring.Quality(5, 0, 0, 0, false, Constants, notAttempted: true);
        Assert.Equal(Constants.NotAttemptedScore, quality);
        Assert.Equal(quality, answer.CoAssessmentQualityScore);
        Assert.Equal(rawQuality, answer.CoAssessmentRawQualityScore);
        Assert.True(answer.CoAssessmentNotAttempted);
        Assert.True(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.NotAttempted);
        Assert.True(BenchmarkVerdictView.FromCoAssessment(answer)!.NotAttempted);

        // Member A's verdict says nothing of the kind.
        Assert.Null(answer.NotAttempted);
        Assert.False(BenchmarkVerdictView.FromPrimary(answer)!.NotAttempted);
    }

    [Fact]
    public void ApplyCoAssessment_AFailedParse_FailsMemberBOnly()
    {
        var answer = AnswerScoredByA();
        var parse = Parse("This is not a verdict.");
        Assert.False(parse.Success);

        BenchmarkService.ApplyCoAssessment(answer, parse, null, Constants, 140, "This is not a verdict.");

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
        Assert.False(string.IsNullOrWhiteSpace(answer.CoAssessmentError));
        Assert.Equal("This is not a verdict.", answer.CoAssessmentRawText);
        Assert.Null(answer.CoAssessmentQualityScore);
        Assert.Null(answer.CoAssessmentJson);
        AssertMemberAUnchanged(answer);
    }

    [Fact]
    public void ApplyCoAssessment_ATerminalError_FailsMemberBOnly()
    {
        var answer = AnswerScoredByA();
        var parse = new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = "provider timeout" };

        BenchmarkService.ApplyCoAssessment(answer, parse, "The request timed out.", Constants, null, null);

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
        Assert.False(string.IsNullOrWhiteSpace(answer.CoAssessmentError));
        Assert.Null(answer.CoAssessmentRawText);
        AssertMemberAUnchanged(answer);
    }

    // --- The panel score --------------------------------------------------------------------

    [Fact]
    public void ComputePanelScore_IsTheMeanOfBothMembers()
    {
        var answer = AnswerScoredByA(quality: 80);
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = 87;
        answer.CoAssessmentCriticalError = false;

        BenchmarkService.ComputePanelScore(answer);

        Assert.Equal(83.5, answer.PanelQualityScore);
        Assert.False(answer.PanelDisagreed);
        Assert.Equal(83.5, BenchmarkScoring.IndexQuality(answer, isPanelRun: true));
        Assert.Equal(80.0, BenchmarkScoring.IndexQuality(answer, isPanelRun: false));
    }

    public static TheoryData<BenchmarkAssessmentStatus, BenchmarkAssessmentStatus?> OneMemberWithoutAScore => new()
    {
        { BenchmarkAssessmentStatus.Failed, BenchmarkAssessmentStatus.Scored },
        { BenchmarkAssessmentStatus.Scored, BenchmarkAssessmentStatus.Failed },
        { BenchmarkAssessmentStatus.Scored, BenchmarkAssessmentStatus.Pending },
        { BenchmarkAssessmentStatus.Scored, BenchmarkAssessmentStatus.Assessing },
        { BenchmarkAssessmentStatus.Scored, null },
        { BenchmarkAssessmentStatus.Pending, BenchmarkAssessmentStatus.Scored }
    };

    [Theory]
    [MemberData(nameof(OneMemberWithoutAScore))]
    public void ComputePanelScore_IsNull_UnlessBothMembersScored(BenchmarkAssessmentStatus statusA, BenchmarkAssessmentStatus? statusB)
    {
        var answer = AnswerScoredByA(quality: 80);
        answer.AssessmentStatus = statusA;
        answer.CoAssessmentStatus = statusB;
        answer.CoAssessmentQualityScore = 60;
        answer.CoAssessmentCriticalError = false;
        // A stale value from an earlier grading must not survive a member's failure.
        answer.PanelQualityScore = 70;
        answer.PanelDisagreed = true;

        BenchmarkService.ComputePanelScore(answer);

        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);
        Assert.Null(BenchmarkScoring.IndexQuality(answer, isPanelRun: true));
    }

    [Theory]
    [InlineData(50, 65, false)]
    [InlineData(50, 66, true)]
    [InlineData(66, 50, true)]
    [InlineData(90, 90, false)]
    public void ComputePanelScore_DisagreesOnADeltaAboveFifteen(int scoreA, int scoreB, bool expected)
    {
        var answer = AnswerScoredByA(quality: scoreA);
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = scoreB;
        answer.CoAssessmentCriticalError = false;

        BenchmarkService.ComputePanelScore(answer);

        Assert.Equal((scoreA + scoreB) / 2.0, answer.PanelQualityScore);
        Assert.Equal(expected, answer.PanelDisagreed);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ComputePanelScore_DisagreesOnACriticalErrorSplit_WhateverTheScores(bool criticalA, bool criticalB)
    {
        var answer = AnswerScoredByA(quality: 25, criticalError: criticalA);
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = 25;
        answer.CoAssessmentCriticalError = criticalB;

        BenchmarkService.ComputePanelScore(answer);

        Assert.Equal(25.0, answer.PanelQualityScore);
        Assert.True(answer.PanelDisagreed);
    }

    // --- Critical-error resolution (scoring method 13) ----------------------------------------

    private const string FlaggedQuote = "Silver dragon scale mail gives AC 9";

    /// <summary>
    /// A panel answer both members graded 5/5/5/5 (87), with a critical error on
    /// <see cref="FlaggedQuote"/> raised by the members named; a flag caps its member at 25.
    /// </summary>
    private static BenchmarkRunAnswer PanelAnswerFlaggedBy(bool flagA, bool flagB)
    {
        var answer = AnswerScoredByA(quality: flagA ? 25 : 87, criticalError: flagA);
        answer.RawQualityScore = 87;
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(5, 5, 5, 5,
            criticalError: flagB,
            quote: flagB ? FlaggedQuote : null,
            accuracyEvidence: flagB ? "The answer says silver dragon scale mail gives AC 9, which is false." : "Matches rubric.")),
            null, Constants, null, null);
        Assert.Equal(flagB ? 25 : 87, answer.CoAssessmentQualityScore);
        Assert.Equal(87, answer.CoAssessmentRawQualityScore);
        return answer;
    }

    /// <summary>A union verification with one item: the flagged quote, raised by <paramref name="raisedBy"/>.</summary>
    private static string QuoteVerification(BenchmarkClaimVerdict verdict, string raisedBy)
        => "[{\"claimIndex\":0,\"claim\":\"" + FlaggedQuote + "\",\"verdict\":\"" + verdict
           + "\",\"citation\":\"src/objects.c:1\",\"basis\":\"b\",\"raisedBy\":[\"" + raisedBy + "\"]}]";

    [Fact]
    public void ComputePanelScore_Method13_BothMembersFlag_IsAgreed()
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: true);

        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Equal(BenchmarkCriticalErrorResolution.Agreed, answer.CriticalErrorResolution);
        Assert.Equal(25.0, answer.PanelQualityScore);
        Assert.False(answer.PanelDisagreed);
    }

    [Theory]
    [InlineData(true, BenchmarkClaimVerdict.Refuted, BenchmarkCriticalErrorResolution.UpheldByVerifier, 25.0)]
    [InlineData(false, BenchmarkClaimVerdict.Refuted, BenchmarkCriticalErrorResolution.UpheldByVerifier, 25.0)]
    [InlineData(true, BenchmarkClaimVerdict.Supported, BenchmarkCriticalErrorResolution.OverturnedByVerifier, 87.0)]
    [InlineData(false, BenchmarkClaimVerdict.Supported, BenchmarkCriticalErrorResolution.OverturnedByVerifier, 87.0)]
    [InlineData(true, BenchmarkClaimVerdict.Indeterminate, BenchmarkCriticalErrorResolution.Unresolved, 56.0)]
    [InlineData(false, BenchmarkClaimVerdict.Indeterminate, BenchmarkCriticalErrorResolution.Unresolved, 56.0)]
    public void ComputePanelScore_Method13_ASplitFlag_IsSettledByTheVerifierOnTheFlaggingMembersQuote(
        bool memberAFlags, BenchmarkClaimVerdict verdict, BenchmarkCriticalErrorResolution expectedResolution, double expectedPanel)
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: memberAFlags, flagB: !memberAFlags);
        answer.ClaimVerificationJson = QuoteVerification(verdict, memberAFlags ? "A" : "B");

        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Equal(expectedResolution, answer.CriticalErrorResolution);
        Assert.Equal(expectedPanel, answer.PanelQualityScore);
        // A split flag is a disagreement whichever way it resolves.
        Assert.True(answer.PanelDisagreed);
        // The members' own columns keep their verdicts.
        Assert.Equal(memberAFlags ? 25 : 87, answer.QualityScore);
        Assert.Equal(memberAFlags ? 87 : 25, answer.CoAssessmentQualityScore);
    }

    [Fact]
    public void ComputePanelScore_Method13_ASplitFlagWithNoVerification_IsUnresolved()
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: false);

        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Equal(BenchmarkCriticalErrorResolution.Unresolved, answer.CriticalErrorResolution);
        Assert.Equal((25 + 87) / 2.0, answer.PanelQualityScore);
    }

    [Fact]
    public void ComputePanelScore_Method13_AVerdictOnTheOtherMembersItem_DoesNotSettleTheFlag()
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: false);
        answer.ClaimVerificationJson = QuoteVerification(BenchmarkClaimVerdict.Supported, "B");

        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Equal(BenchmarkCriticalErrorResolution.Unresolved, answer.CriticalErrorResolution);
        Assert.Equal((25 + 87) / 2.0, answer.PanelQualityScore);
    }

    [Fact]
    public void ComputePanelScore_Method13_WithAMemberUnscored_ClearsThePanelColumns()
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: false);
        BenchmarkService.ComputePanelScore(answer, run);
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Failed;

        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);
        Assert.Null(answer.CriticalErrorResolution);
    }

    [Fact]
    public void AVerificationSupportingTheFlaggingMembersQuote_RaisesThePanelScore_OnceRecomputed()
    {
        var run = PanelRun();
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: false);
        BenchmarkService.ComputePanelScore(answer, run);
        Assert.Equal(BenchmarkCriticalErrorResolution.Unresolved, answer.CriticalErrorResolution);
        Assert.Equal((25 + 87) / 2.0, answer.PanelQualityScore);

        var verifications = BenchmarkReportContent.ReadVerifications(QuoteVerification(BenchmarkClaimVerdict.Supported, "A"))!;
        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));
        BenchmarkService.ComputePanelScore(answer, run);

        Assert.Equal(BenchmarkCriticalErrorResolution.OverturnedByVerifier, answer.CriticalErrorResolution);
        Assert.Equal(87.0, answer.PanelQualityScore);
        Assert.True(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedCriticalError));
        // Member A's verdict keeps its flag and its capped score.
        Assert.True(answer.CriticalError);
        Assert.Equal(25, answer.QualityScore);
    }

    [Fact]
    public void ComputePanelScore_BeforeMethod13_IgnoresTheVerifier_AndRecordsNoResolution()
    {
        var run = PanelRun();
        run.ScoringMethodVersion = BenchmarkCriticalErrorResolver.FirstScoringMethod - 1;
        var answer = PanelAnswerFlaggedBy(flagA: true, flagB: false);
        answer.ClaimVerificationJson = QuoteVerification(BenchmarkClaimVerdict.Supported, "A");
        var legacy = PanelAnswerFlaggedBy(flagA: true, flagB: false);
        legacy.ClaimVerificationJson = answer.ClaimVerificationJson;

        BenchmarkService.ComputePanelScore(answer, run);
        BenchmarkService.ComputePanelScore(legacy);

        Assert.Equal((25 + 87) / 2.0, answer.PanelQualityScore);
        Assert.Equal(legacy.PanelQualityScore, answer.PanelQualityScore);
        Assert.Equal(legacy.PanelDisagreed, answer.PanelDisagreed);
        Assert.Null(answer.CriticalErrorResolution);
    }

    // --- The empty-answer and board-guard branches ----------------------------------------

    [Fact]
    public async Task AModelProducedEmptyAnswer_ScoresBothMembersZero_AndThePanelZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, db) = CreateHelperService();
        var run = PanelRun();
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = string.Empty,
            Status = BenchmarkAnswerStatus.EmptyAnswer,
            ProviderFinishReason = "stop",
            ExpectedPointsRecorded = true,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Pending
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, null,
            DummyConfig("assessor-model"), "unused", Constants, ct, DummyConfig("co-assessor-model"), "unused");

        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.AssessmentStatus);
        Assert.Equal(0, answer.QualityScore);
        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.CoAssessmentStatus);
        Assert.Equal(0, answer.CoAssessmentQualityScore);
        Assert.Equal(0, answer.CoAssessmentRawQualityScore);
        Assert.False(answer.CoAssessmentCriticalError);
        Assert.Equal(0.0, answer.PanelQualityScore);
        Assert.False(answer.PanelDisagreed);

        // A record written without a grader carries no levels, so it is no verdict to verify.
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson);
        Assert.NotNull(record);
        Assert.Null(record!.AccuracyLevel);
        Assert.Equal(0, record.QualityScore);
        Assert.Null(BenchmarkVerdictView.FromCoAssessment(answer));
    }

    [Fact]
    public async Task AnEmptyAnswerTheProviderFailed_FailsBothMembers_AndLeavesNoPanelScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, db) = CreateHelperService();
        var run = PanelRun();
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = string.Empty,
            Status = BenchmarkAnswerStatus.ProviderError,
            ExpectedPointsRecorded = true,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Pending
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, null,
            DummyConfig("assessor-model"), "unused", Constants, ct, DummyConfig("co-assessor-model"), "unused");

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.AssessmentStatus);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
        Assert.False(string.IsNullOrWhiteSpace(answer.CoAssessmentError));
        Assert.Equal(answer.AssessmentError, answer.CoAssessmentError);
        Assert.Null(answer.QualityScore);
        Assert.Null(answer.CoAssessmentQualityScore);
        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);
    }

    [Fact]
    public async Task ABoardTheLoadDidNotInclude_FailsBothMembers_AndLeavesNoPanelScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, db) = CreateHelperService();
        var run = PanelRun();
        run.GameSnapshotSha256Used = "board-sha";
        run.GameSnapshotNameUsed = "Board";
        var answer = AnswerScoredByA();
        answer.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Pending;
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, null,
            DummyConfig("assessor-model"), "unused", Constants, ct, DummyConfig("co-assessor-model"), "unused");

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.AssessmentStatus);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
        Assert.False(string.IsNullOrWhiteSpace(answer.CoAssessmentError));
        Assert.Equal(answer.AssessmentError, answer.CoAssessmentError);
        Assert.Null(answer.PanelQualityScore);
    }

    // --- Re-grading one member ---------------------------------------------------------------

    [Fact]
    public async Task ReGradingMemberAOnly_KeepsMemberBsVerdict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, db) = CreateHelperService();
        var run = PanelRun();
        // The board guard fails the member being re-graded without reaching a model.
        run.GameSnapshotSha256Used = "board-sha";
        run.GameSnapshotNameUsed = "Board";
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(6, 6, 6, 6)), null, Constants, 140, null);
        BenchmarkService.ComputePanelScore(answer);
        string recordBefore = answer.CoAssessmentJson!;
        int? scoreB = answer.CoAssessmentQualityScore;
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, null,
            DummyConfig("assessor-model"), "unused", Constants, ct, DummyConfig("co-assessor-model"), "unused",
            BenchmarkPanelMember.A);

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.AssessmentStatus);
        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.CoAssessmentStatus);
        Assert.Null(answer.CoAssessmentError);
        Assert.Equal(scoreB, answer.CoAssessmentQualityScore);
        Assert.Equal(recordBefore, answer.CoAssessmentJson);
        // The panel score follows the members: one of them is now unscored.
        Assert.Null(answer.PanelQualityScore);
    }

    [Fact]
    public async Task ReGradingMemberBOnly_WithoutItsConfiguration_FailsBAndKeepsMemberAsVerdict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, db) = CreateHelperService();
        var run = PanelRun();
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(6, 6, 6, 6)), null, Constants, 140, null);
        BenchmarkService.ComputePanelScore(answer);
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        // No co-assessor configuration: member B fails re-runnably, and member A is not graded at all,
        // so the agent loop (null here) is never reached.
        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, null,
            DummyConfig("assessor-model"), "unused", Constants, ct, coAssessorConfig: null, coAssessorApiKey: null,
            members: BenchmarkPanelMember.B);

        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
        Assert.Equal("Not assessed: the co-assessor configuration was unavailable.", answer.CoAssessmentError);
        AssertMemberAUnchanged(answer);
        Assert.Null(answer.PanelQualityScore);
    }

    [Fact]
    public void ANewMemberBVerdict_DropsTheUnionVerificationThatDescribedTheOldOne_AndKeepsMemberAsVerdict()
    {
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(5, 5, 6, 6)), null, Constants, null, null);
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        record.Flags!.ContestedAccuracyDeduction = true;
        answer.CoAssessmentJson = record.Serialize();
        answer.AnswerFlags |= (int)(BenchmarkAnswerFlags.ContestedCriticalError | BenchmarkAnswerFlags.RefutedClaim);
        answer.ClaimVerificationJson = "[{\"claimIndex\":0,\"claim\":\"It weighs 40.\",\"verdict\":\"Refuted\",\"citation\":\"src/objects.c:1\",\"basis\":\"b\",\"raisedBy\":[\"A\",\"B\"]}]";
        answer.ClaimsRefutedCount = 1;

        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(4, 5, 6, 6, accuracyEvidence: "The answer states AC 9 imprecisely.")), null, Constants, null, null);

        // The verification read both members' old verdicts, so it goes with every flag it set.
        Assert.Null(answer.ClaimVerificationJson);
        Assert.Null(answer.ClaimsRefutedCount);
        var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
        Assert.False(flags.HasFlag(BenchmarkAnswerFlags.ContestedCriticalError));
        Assert.False(flags.HasFlag(BenchmarkAnswerFlags.RefutedClaim));
        var newRecord = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.False(newRecord.Flags!.ContestedAccuracyDeduction);
        Assert.Equal(4, newRecord.AccuracyLevel);

        // Member A's verdict and its own advisory flag stay.
        AssertMemberAUnchanged(answer);
    }

    // --- Re-running an answer ------------------------------------------------------------------

    private static BenchmarkRunAnswer FullyGradedPanelAnswer()
    {
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(4, 5, 6, 6, accuracyEvidence: "The answer states AC 9 imprecisely.")), null, Constants, 140, null);
        BenchmarkService.ComputePanelScore(answer);
        answer.CoAssessedAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        answer.CoAssessedByModelSnapshot = BenchmarkModelSnapshots.Model("OpenAI", "co-assessor-model");
        answer.CoAssessmentInputTokens = 900;
        answer.CoAssessmentOutputTokens = 150;
        answer.SecondOpinionJson = "{\"qualityScore\":60}";
        answer.SecondOpinionQualityScore = 60;
        answer.SecondOpinionCriticalError = false;
        answer.SecondOpinionDisagreed = true;
        answer.SecondOpinionTrigger = "All";
        answer.SecondOpinionError = "earlier error";
        answer.SecondOpinionByModelSnapshot = BenchmarkModelSnapshots.Model("Google", "reader-model");
        answer.SecondOpinionInputTokens = 800;
        return answer;
    }

    [Fact]
    public void ClearForRerun_InAPanelRun_ClearsBothMembers_TheReferenceReader_AndThePanel()
    {
        var answer = FullyGradedPanelAnswer();
        answer.NotAttempted = false;
        answer.CoAssessmentNotAttempted = true;
        answer.CriticalErrorResolution = BenchmarkCriticalErrorResolution.None;
        Assert.NotNull(answer.PanelQualityScore);

        BenchmarkService.ClearForRerun(answer, isPanelRun: true);

        Assert.Equal(BenchmarkAssessmentStatus.Pending, answer.AssessmentStatus);
        Assert.Null(answer.QualityScore);
        Assert.Null(answer.AccuracyLevel);
        Assert.Null(answer.NotAttempted);
        Assert.Null(answer.CoAssessmentNotAttempted);
        Assert.Null(answer.CriticalErrorResolution);

        Assert.Equal(BenchmarkAssessmentStatus.Pending, answer.CoAssessmentStatus);
        Assert.Null(answer.CoAssessmentError);
        Assert.Null(answer.CoAssessmentQualityScore);
        Assert.Null(answer.CoAssessmentRawQualityScore);
        Assert.Null(answer.CoAssessmentCriticalError);
        Assert.Null(answer.CoAssessmentJson);
        Assert.Null(answer.CoAssessmentRawText);
        Assert.Null(answer.CoAssessedByModelSnapshot);
        Assert.Null(answer.CoAssessedByModelSnapshotId);
        Assert.Null(answer.CoAssessedAtUtc);
        Assert.Null(answer.CoAssessorBoardChars);
        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);

        Assert.Null(answer.SecondOpinionJson);
        Assert.Null(answer.SecondOpinionQualityScore);
        Assert.Null(answer.SecondOpinionCriticalError);
        Assert.False(answer.SecondOpinionDisagreed);
        Assert.Null(answer.SecondOpinionTrigger);
        Assert.Null(answer.SecondOpinionError);
        Assert.Null(answer.SecondOpinionByModelSnapshot);
        Assert.Null(answer.SecondOpinionByModelSnapshotId);

        // What the earlier calls consumed stays recorded.
        Assert.Equal(900, answer.CoAssessmentInputTokens);
        Assert.Equal(150, answer.CoAssessmentOutputTokens);
        Assert.Equal(800, answer.SecondOpinionInputTokens);
        Assert.Equal(1000, answer.AssessmentInputTokens);
    }

    [Fact]
    public void ClearForRerun_OutsideAPanelRun_LeavesTheCoAssessmentStatusNull()
    {
        var answer = AnswerScoredByA();
        answer.SecondOpinionQualityScore = 60;
        answer.SecondOpinionJson = "{\"qualityScore\":60}";

        BenchmarkService.ClearForRerun(answer, isPanelRun: false);

        Assert.Equal(BenchmarkAssessmentStatus.Pending, answer.AssessmentStatus);
        Assert.Null(answer.CoAssessmentStatus);
        Assert.Null(answer.SecondOpinionQualityScore);
        Assert.Null(answer.SecondOpinionJson);
        Assert.Null(answer.PanelQualityScore);
    }

    // --- Rescoring -------------------------------------------------------------------------------

    [Fact]
    public void RecomputeCoAssessmentScores_RescoresMemberBFromItsRecordedLevels()
    {
        var answer = FullyGradedPanelAnswer();
        var profile = new BenchmarkScoringConstants
        {
            WeightAccuracy = 0.25,
            WeightCompleteness = 0.25,
            WeightConciseness = 0.25,
            WeightReadability = 0.25
        };
        var (before, _, _) = BenchmarkScoring.Quality(4, 5, 6, 6, false, Constants);
        var (expected, expectedRaw, _) = BenchmarkScoring.Quality(4, 5, 6, 6, false, profile);
        Assert.NotEqual(before, expected);
        Assert.Equal(before, answer.CoAssessmentQualityScore);

        BenchmarkService.RecomputeCoAssessmentScores(answer, profile);
        BenchmarkService.ComputePanelScore(answer);

        Assert.Equal(expected, answer.CoAssessmentQualityScore);
        Assert.Equal(expectedRaw, answer.CoAssessmentRawQualityScore);
        Assert.False(answer.CoAssessmentCriticalError);
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.Equal(expected, record.QualityScore);
        Assert.Equal(expectedRaw, record.RawQualityScore);
        Assert.Equal(4, record.AccuracyLevel);

        // Member A's columns are the primary rescore's to rewrite, not this one's.
        Assert.Equal(87, answer.QualityScore);
        Assert.Equal((87 + expected) / 2.0, answer.PanelQualityScore);
    }

    [Fact]
    public void RecomputeCoAssessmentScores_AppliesTheCriticalErrorCeiling()
    {
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(6, 6, 6, 6, criticalError: true, quote: "Silver dragon scale mail gives AC 9", accuracyEvidence: "The answer says silver dragon scale mail gives AC 9, which is false.")), null, Constants, null, null);
        var profile = new BenchmarkScoringConstants { CriticalErrorCeiling = 10 };

        BenchmarkService.RecomputeCoAssessmentScores(answer, profile);

        var (expected, _, capApplied) = BenchmarkScoring.Quality(6, 6, 6, 6, true, profile);
        Assert.True(capApplied);
        Assert.Equal(expected, answer.CoAssessmentQualityScore);
        Assert.True(answer.CoAssessmentCriticalError);
    }

    [Fact]
    public void RecomputeCoAssessmentScores_LeavesARecordWithoutLevelsAsItIs()
    {
        var answer = AnswerScoredByA();
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = 0;
        answer.CoAssessmentRawQualityScore = 0;
        answer.CoAssessmentJson = new BenchmarkCoAssessmentRecord { QualityScore = 0, RawQualityScore = 0 }.Serialize();
        string before = answer.CoAssessmentJson;

        BenchmarkService.RecomputeCoAssessmentScores(answer, Constants);

        Assert.Equal(0, answer.CoAssessmentQualityScore);
        Assert.Equal(before, answer.CoAssessmentJson);
    }

    [Fact]
    public void RecomputeCoAssessmentScores_KeepsTheNotAttemptedFloor()
    {
        var answer = AnswerScoredByA();
        BenchmarkService.ApplyCoAssessment(answer, Parse(Verdict(5, 0, 0, 0, notAttempted: true)), null, Constants, null, null);
        var profile = Constants with { NotAttemptedScore = 40 };

        BenchmarkService.RecomputeCoAssessmentScores(answer, profile);

        Assert.Equal(40, answer.CoAssessmentQualityScore);
        Assert.Equal(40, answer.CoAssessmentRawQualityScore);
        Assert.True(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.NotAttempted);
    }

    private static BenchmarkRun RescorableRun(BenchmarkRun run, int questionCount, long? profileId)
    {
        run.Status = BenchmarkRunStatus.Completed;
        run.CompletedAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        run.TotalQuestionCount = questionCount;
        run.ScoringProfileId = profileId;
        return run;
    }

    [Fact]
    public async Task Rescore_OfAMethod13PanelRun_KeepsTheNotAttemptedFloor_AndTheCriticalErrorResolution()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (service, _) = CreateServiceOver(dbOptions);
        var profile = new BenchmarkScoringProfile { Name = "Rescore profile", NotAttemptedScore = 50 };

        long runId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            seedDb.BenchmarkScoringProfiles.Add(profile);
            await seedDb.SaveChangesAsync(ct);

            var run = RescorableRun(PanelRun(), questionCount: 2, profileId: profile.Id);

            // Question 1: both members marked it not attempted at Accuracy 5; the stored scores are stale.
            var abstained = AnswerScoredByA(quality: 1);
            abstained.CompletenessLevel = 0;
            abstained.ConcisenessLevel = 0;
            abstained.ReadabilityLevel = 0;
            abstained.NotAttempted = true;
            abstained.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
            abstained.CoAssessmentQualityScore = 1;
            abstained.CoAssessmentRawQualityScore = 1;
            abstained.CoAssessmentCriticalError = false;
            abstained.CoAssessmentNotAttempted = true;
            abstained.CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = 5,
                CompletenessLevel = 0,
                ConcisenessLevel = 0,
                ReadabilityLevel = 0,
                NotAttempted = true,
                QualityScore = 1,
                RawQualityScore = 1
            }.Serialize();

            // Question 2: member A alone flagged, and the verifier supported its quote.
            var split = PanelAnswerFlaggedBy(flagA: true, flagB: false);
            split.OrderIndex = 2;
            split.ClaimVerificationJson = QuoteVerification(BenchmarkClaimVerdict.Supported, "A");
            BenchmarkService.ComputePanelScore(split, run);
            Assert.Equal(87.0, split.PanelQualityScore);

            run.Answers.Add(abstained);
            run.Answers.Add(split);
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
        }

        var (success, error) = await service.RescoreRunAsync(runId);
        Assert.True(success, error);

        await using var readback = new ApplicationDbContext(dbOptions);
        var rescored = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
        Assert.Equal(BenchmarkAssessmentPrompt.ScoringMethodVersion, rescored.ScoringMethodVersion);

        var constants = new BenchmarkScoringProfileService(null!, NullLogger<BenchmarkScoringProfileService>.Instance).ToConstants(profile);
        Assert.True(BenchmarkScoring.Quality(5, 0, 0, 0, false, constants).Score < 50);

        var rescoredAbstained = rescored.Answers.Single(a => a.OrderIndex == 1);
        Assert.Equal(50, rescoredAbstained.QualityScore);
        Assert.Equal(50, rescoredAbstained.RawQualityScore);
        Assert.Equal(50, rescoredAbstained.CoAssessmentQualityScore);
        Assert.Equal(50.0, rescoredAbstained.PanelQualityScore);
        Assert.Equal(BenchmarkCriticalErrorResolution.None, rescoredAbstained.CriticalErrorResolution);

        var rescoredSplit = rescored.Answers.Single(a => a.OrderIndex == 2);
        Assert.Equal(25, rescoredSplit.QualityScore);
        Assert.Equal(87, rescoredSplit.RawQualityScore);
        Assert.Equal(87, rescoredSplit.CoAssessmentQualityScore);
        Assert.Equal(BenchmarkCriticalErrorResolution.OverturnedByVerifier, rescoredSplit.CriticalErrorResolution);
        Assert.Equal(87.0, rescoredSplit.PanelQualityScore);
    }

    [Fact]
    public async Task Rescore_OfAMethod13SingleAssessorRun_RecordsEachAnswersResolution()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (service, _) = CreateServiceOver(dbOptions);

        long runId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var run = RescorableRun(PanelRun(), questionCount: 2, profileId: null);
            run.CoAssessorModelConfigurationId = null;
            var flagged = AnswerScoredByA(quality: 25, criticalError: true);
            var clean = AnswerScoredByA();
            clean.OrderIndex = 2;
            run.Answers.Add(flagged);
            run.Answers.Add(clean);
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
        }

        var (success, error) = await service.RescoreRunAsync(runId);
        Assert.True(success, error);

        await using var readback = new ApplicationDbContext(dbOptions);
        var rescored = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
        var rescoredFlagged = rescored.Answers.Single(a => a.OrderIndex == 1);
        Assert.Equal(BenchmarkCriticalErrorResolution.SingleAssessor, rescoredFlagged.CriticalErrorResolution);
        Assert.Equal(25, rescoredFlagged.QualityScore);
        Assert.Null(rescoredFlagged.PanelQualityScore);
        Assert.Equal(BenchmarkCriticalErrorResolution.None, rescored.Answers.Single(a => a.OrderIndex == 2).CriticalErrorResolution);
    }

    // --- Grader overrides are refused in panel runs ------------------------------------------

    private static async Task<(BenchmarkRun Run, BenchmarkRunAnswer Answer, SystemAiApiConfiguration Substitute)> SeedPanelRunAsync(ApplicationDbContext db)
    {
        var (suite, modelA, modelB, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var openAiMember = new SystemAiApiConfiguration
        {
            DisplayName = "GPT 4.1",
            Provider = "OpenAI",
            ModelId = "gpt-4.1",
            EncryptedApiKey = "dummy_encrypted",
            ApiKeyNonce = "nonce",
            ApiKeyTag = "tag",
            ModelRole = 4,
            IsEnabled = true
        };
        db.SystemAiApiConfigurations.Add(openAiMember);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Status = BenchmarkRunStatus.CompletedWithErrors,
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            CoAssessorModelConfigurationId = openAiMember.Id,
            CandidatePromptOptionsJson = "{}",
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            TotalQuestionCount = 1,
            StartedAtUtc = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        var answer = AnswerScoredByA();
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Failed;
        answer.CoAssessmentError = "timeout";
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (run, answer, modelB);
    }

    [Fact]
    public async Task Controller_RefusesEveryGraderOverride_OnAPanelRun_As400()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (run, answer, substitute) = await SeedPanelRunAsync(db);
        var @override = new BenchmarkRetryRequest { AssessorModelConfigurationId = substitute.Id };

        var results = new List<IActionResult>
        {
            await controller.ReassessAnswer(run.Id, answer.Id, new ReassessAnswerRequest { AssessorModelConfigurationId = substitute.Id }),
            await controller.RerunAnswer(run.Id, answer.Id, @override),
            await controller.RerunSynthesis(run.Id, @override),
            await controller.RetryFailedAssessments(run.Id, @override)
        };

        foreach (var result in results)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(BenchmarkService.PanelOverrideRefusedMessage, badRequest.Value);
        }

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        AssertMemberAUnchanged(answer);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, answer.CoAssessmentStatus);
    }

    [Fact]
    public async Task Service_RefusesAReassessmentBySubstituteModel_OnAPanelRun_AndChangesNoVerdict()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (service, runManager) = CreateServiceOver(dbOptions);

        long runId;
        long answerId;
        long substituteId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var (run, answer, substitute) = await SeedPanelRunAsync(seedDb);
            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
            answerId = answer.Id;
            substituteId = substitute.Id;
        }

        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.ReassessSingleQuestionAsync(
            runId, answerId, substituteId, trial: false, BenchmarkRunStatus.CompletedWithErrors,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        await using var readback = new ApplicationDbContext(dbOptions);
        var reloadedRun = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
        var reloaded = reloadedRun.Answers.Single();
        Assert.Equal(BenchmarkService.PanelOverrideRefusedMessage, reloadedRun.ErrorMessage);
        Assert.NotEqual(BenchmarkRunStatus.Running, reloadedRun.Status);
        AssertMemberAUnchanged(reloaded);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, reloaded.CoAssessmentStatus);
        Assert.Null(runManager.CurrentRunId);
    }

    // --- Both members through the real agent loop -----------------------------------------------

    [Fact]
    public async Task BothMembers_AreGradedThroughTheAgentLoop_ThePanelScoreIsSet_AndUsageIsRecordedTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        string verdictA = Verdict(6, 6, 6, 6, comment: "Member A.");
        string verdictB = Verdict(3, 5, 6, 6, accuracyEvidence: "The answer states it weighs 40; the rubric gives 50.", comment: "Member B.");
        var provider = new PanelGraderProvider(new Dictionary<string, string>
        {
            ["judge-a"] = verdictA,
            ["judge-b"] = verdictB
        });

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddSingleton<IAiProvider>(provider);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handlers = new List<IToolHandler>();
        var bridge = new NoClientBridge();
        var runner = new AgentLoopRunner(
            new IAiProvider[] { provider },
            new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, configuration),
            new ReplyEchoHttpClientFactory(),
            configuration,
            scopeFactory,
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, configuration),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance);

        var service = new BenchmarkService(
            scopeFactory,
            chatService: null!,
            agentLoopRunner: runner,
            cryptoService: null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new EndpointPolicy(configuration),
            configuration,
            NullLogger<BenchmarkService>.Instance);

        await using var db = new ApplicationDbContext(dbOptions);
        var configA = GraderConfig("judge-a", "Judge A");
        var configB = GraderConfig("judge-b", "Judge B");
        db.SystemAiApiConfigurations.AddRange(configA, configB);
        await db.SaveChangesAsync(ct);

        var run = PanelRun(configA.Id, configB.Id);
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "What does silver dragon scale mail give?",
            AnswerText = AnswerText,
            Status = BenchmarkAnswerStatus.Ok,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = 25,
            DurationMs = 2000,
            ExpectedPointsUsed = "- AC 9 and reflection",
            ExpectedPointsRecorded = true,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Pending
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await service.ExecutePerQuestionAssessmentAsync(
            db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance), run, answer, answer.ExpectedPointsUsed,
            configA, "key-a", Constants, ct, configB, "key-b");

        var (qualityA, _, _) = BenchmarkScoring.Quality(6, 6, 6, 6, false, Constants);
        var (qualityB, _, _) = BenchmarkScoring.Quality(3, 5, 6, 6, false, Constants);

        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.AssessmentStatus);
        Assert.Equal(6, answer.AccuracyLevel);
        Assert.Equal(qualityA, answer.QualityScore);
        Assert.Equal("Member A.", answer.ReviewComment);
        Assert.Equal(configA.Id, answer.AssessedByModelConfigurationId);
        Assert.Equal("judge-a", answer.AssessedByModelSnapshot?.ModelId);

        Assert.Equal(BenchmarkAssessmentStatus.Scored, answer.CoAssessmentStatus);
        Assert.Equal(qualityB, answer.CoAssessmentQualityScore);
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.Equal(3, record.AccuracyLevel);
        Assert.Equal("Member B.", record.Comment);
        Assert.Equal("judge-b", answer.CoAssessedByModelSnapshot?.ModelId);
        Assert.NotNull(answer.CoAssessedAtUtc);

        Assert.Equal((qualityA + qualityB) / 2.0, answer.PanelQualityScore);
        Assert.Equal(Math.Abs(qualityA - qualityB) > 15, answer.PanelDisagreed);

        // Each member's call is counted on its own columns.
        Assert.Equal(PanelGraderProvider.PromptTokens, answer.AssessmentInputTokens);
        Assert.Equal(PanelGraderProvider.OutputTokens, answer.AssessmentOutputTokens);
        Assert.Equal(PanelGraderProvider.PromptTokens, answer.CoAssessmentInputTokens);
        Assert.Equal(PanelGraderProvider.OutputTokens, answer.CoAssessmentOutputTokens);

        // One model call per member, and usage recorded once against each member's configuration.
        Assert.Equal(new[] { "judge-a", "judge-b" }, provider.ModelsCalled.OrderBy(m => m, StringComparer.Ordinal));
        Assert.Equal(1, configA.TotalChatRequestsCount);
        Assert.Equal(1, configB.TotalChatRequestsCount);
        Assert.Equal(PanelGraderProvider.PromptTokens + PanelGraderProvider.OutputTokens, configA.TotalChatTokensCount);
        Assert.Equal(PanelGraderProvider.PromptTokens + PanelGraderProvider.OutputTokens, configB.TotalChatTokensCount);
    }

    // --- Fixtures ----------------------------------------------------------------------------------

    private static SystemAiApiConfiguration DummyConfig(string modelId) => new()
    {
        Provider = "Unused",
        ModelId = modelId,
        DisplayName = modelId
    };

    private static SystemAiApiConfiguration GraderConfig(string modelId, string displayName) => new()
    {
        Provider = PanelGraderProvider.Name,
        ModelId = modelId,
        DisplayName = displayName,
        EncryptedApiKey = "dummy_encrypted",
        ApiKeyNonce = "nonce",
        ApiKeyTag = "tag",
        ModelRole = 4,
        IsEnabled = true
    };

    /// <summary>A service over its own in-memory database whose agent loop is null: no path under test reaches a model.</summary>
    private static (BenchmarkService Service, ApplicationDbContext Db) CreateHelperService()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (service, _) = CreateServiceOver(dbOptions);
        return (service, new ApplicationDbContext(dbOptions));
    }

    private static (BenchmarkService Service, BenchmarkRunManager RunManager) CreateServiceOver(DbContextOptions<ApplicationDbContext> dbOptions)
    {
        var runManager = new BenchmarkRunManager();
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        services.AddSingleton<ILogger<SystemAiConfigService>>(NullLogger<SystemAiConfigService>.Instance);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var configuration = BenchmarkComplianceGuardTests.CreateConfig(maxRunsPerHour: 10);
        var service = new BenchmarkService(
            scopeFactory, null!, null!, null!, runManager, new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new EndpointPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build()),
            configuration,
            NullLogger<BenchmarkService>.Instance);
        return (service, runManager);
    }

    /// <summary>
    /// A provider whose reply depends on the model a request names. The reply travels in the request
    /// body and <see cref="ReplyEchoHandler"/> sends it back, so the two members' concurrent calls
    /// share no state. The delivery probe's body (model <c>probe</c>) carries none.
    /// </summary>
    private sealed class PanelGraderProvider : IAiProvider
    {
        public const string Name = "PanelGraderTest";
        public const int PromptTokens = 1200;
        public const int OutputTokens = 300;

        private readonly IReadOnlyDictionary<string, string> _replyByModel;

        public PanelGraderProvider(IReadOnlyDictionary<string, string> replyByModel) => _replyByModel = replyByModel;

        /// <summary>The model of every request sent to the model, not the probe's.</summary>
        public ConcurrentQueue<string> ModelsCalled { get; } = new();

        public string ProviderName => Name;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public Dictionary<string, object> BuildChatRequestBody(
            string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel,
            ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null,
            string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null,
            string? promptCacheKey = null, bool cacheConversationTail = true)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["messages"] = messageHistory
            };
            if (_replyByModel.TryGetValue(modelId, out string? reply))
            {
                ModelsCalled.Enqueue(modelId);
                body["reply"] = reply;
            }
            return body;
        }

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(
            HttpResponseMessage response, bool showDebugLog,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string reply = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new ChatEvent { Type = "chunk", Data = reply };
            yield return new ChatEvent
            {
                Type = "usage",
                UsageReport = new TokenUsageReport
                {
                    TotalPromptTokens = PromptTokens,
                    UncachedInputTokens = PromptTokens,
                    OutputTokens = OutputTokens
                }
            };
            yield return new ChatEvent { Type = "finish_reason", Data = "stop" };
        }

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null) { }
        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results) { }
        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://panel-grader.test/stream";
        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://panel-grader.test/title";
        public string? ParseTitleResponse(JsonElement root) => null;
    }

    /// <summary>Answers every request with the <c>reply</c> its body carries. Nothing leaves the process.</summary>
    private sealed class ReplyEchoHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            string reply = doc.RootElement.TryGetProperty("reply", out var value) ? value.GetString() ?? string.Empty : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply), RequestMessage = request };
        }
    }

    private sealed class ReplyEchoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ReplyEchoHandler());
    }

    private sealed class NoClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }
}
