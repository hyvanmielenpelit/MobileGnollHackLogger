namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Critical-error resolution under scoring method 13: every row of the panel table, the quote
/// match, the gate, and what <see cref="BenchmarkCriticalErrorResolver.ApplyTo"/> writes.
/// </summary>
public class BenchmarkCriticalErrorResolverTests
{
    private const string QuoteA = "Praying on an unaligned altar at 1 HP is always safe.";
    private const string QuoteB = "Elbereth scares every minotaur.";
    private const int Ceiling = 25;

    private static BenchmarkClaimVerification Item(
        string claim,
        BenchmarkClaimVerdict verdict,
        string? raisedBy = null,
        int claimIndex = 0,
        string? citationNote = null)
        => new(claimIndex, claim, verdict, "src/pray.c:120", "The source says otherwise.")
        {
            RaisedBy = raisedBy == null ? null : new[] { raisedBy },
            CitationNote = citationNote
        };

    /// <summary>
    /// An answer both members scored: member A's verdict in the primary columns, member B's in
    /// <see cref="BenchmarkRunAnswer.CoAssessmentJson"/>.
    /// </summary>
    private static BenchmarkRunAnswer PanelAnswer(
        int scoreA,
        int scoreB,
        bool flagA = false,
        bool flagB = false,
        int? rawA = null,
        int? rawB = null,
        BenchmarkClaimVerification? item = null,
        IReadOnlyList<BenchmarkClaimVerification>? items = null)
    {
        var verifications = item != null ? new List<BenchmarkClaimVerification> { item } : items;
        return new BenchmarkRunAnswer
        {
            Id = 1,
            OrderIndex = 1,
            QuestionText = "When is prayer safe?",
            AnswerText = "Graded answer text.",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = 5,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = scoreA,
            RawQualityScore = rawA ?? scoreA,
            CriticalError = flagA,
            CriticalErrorQuote = flagA ? QuoteA : null,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = scoreB,
            CoAssessmentRawQualityScore = rawB ?? scoreB,
            CoAssessmentCriticalError = flagB,
            CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                CriticalError = flagB,
                CriticalErrorQuote = flagB ? QuoteB : null,
                QualityScore = scoreB,
                RawQualityScore = rawB ?? scoreB
            }.Serialize(),
            ClaimVerificationJson = verifications == null ? null : JsonSerializer.Serialize(verifications)
        };
    }

    private static BenchmarkRun Run(int method, bool panel, string? snapshot = null) => new()
    {
        Id = 50,
        ScoringMethodVersion = method,
        CoAssessorModelConfigurationId = panel ? 7 : null,
        ScoringProfileSnapshotJson = snapshot
    };

    private static void AssertResolved(
        (BenchmarkCriticalErrorResolution Resolution, int ScoreA, int ScoreB)? resolved,
        BenchmarkCriticalErrorResolution resolution,
        int scoreA,
        int scoreB)
    {
        Assert.NotNull(resolved);
        Assert.Equal(resolution, resolved.Value.Resolution);
        Assert.Equal(scoreA, resolved.Value.ScoreA);
        Assert.Equal(scoreB, resolved.Value.ScoreB);
    }

    // --- The panel table ---

    [Fact]
    public void NeitherMemberFlags_ResolvesNone_AndKeepsBothScores()
    {
        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(PanelAnswer(80, 70), Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.None, 80, 70);
    }

    [Fact]
    public void BothMembersFlag_ResolvesAgreed_AndKeepsBothCappedScores()
    {
        var answer = PanelAnswer(25, 20, flagA: true, flagB: true, rawA: 90, rawB: 85,
            item: Item(QuoteA, BenchmarkClaimVerdict.Supported, "A"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        // A verifier ruling does not reopen a flag both members raised.
        AssertResolved(resolved, BenchmarkCriticalErrorResolution.Agreed, 25, 20);
    }

    [Fact]
    public void MemberAFlags_AndTheVerifierRefutesItsQuote_UpholdsIt_AndCapsMemberB()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.UpheldByVerifier, 25, 25);
    }

    [Fact]
    public void MemberBFlags_AndTheVerifierRefutesItsQuote_UpholdsIt_AndCapsMemberA()
    {
        var answer = PanelAnswer(80, 20, flagB: true, rawB: 85,
            item: Item(QuoteB, BenchmarkClaimVerdict.Refuted, "B"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.UpheldByVerifier, 25, 20);
    }

    [Fact]
    public void AnUpheldFlag_LeavesAMemberAlreadyBelowTheCeilingAsGraded()
    {
        var answer = PanelAnswer(15, 18, flagA: true,
            item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.UpheldByVerifier, 15, 18);
    }

    [Fact]
    public void MemberAFlags_AndTheVerifierSupportsItsQuote_OverturnsIt_WithMemberAsPreCapScore()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Supported, "A"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.OverturnedByVerifier, 90, 70);
    }

    [Fact]
    public void MemberBFlags_AndTheVerifierSupportsItsQuote_OverturnsIt_WithMemberBsPreCapScore()
    {
        var answer = PanelAnswer(80, 20, flagB: true, rawB: 85,
            item: Item(QuoteB, BenchmarkClaimVerdict.Supported, "B"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.OverturnedByVerifier, 80, 85);
    }

    [Fact]
    public void AnIndeterminateVerdict_LeavesTheFlagUnresolved_AndBothScoresAsGraded()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Indeterminate, "A"));

        var resolved = BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling);

        AssertResolved(resolved, BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void NoMatchingItem_LeavesTheFlagUnresolved()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item("Some other claim entirely.", BenchmarkClaimVerdict.Refuted, "A"));

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void NoVerificationAtAll_LeavesTheFlagUnresolved()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90);

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void InvalidVerificationJson_ReadsAsNoItems()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90);
        answer.ClaimVerificationJson = "{not valid json";

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void TheQuoteIsReadOverTheItemsTheFlaggingMemberRaised()
    {
        // The same text, but only member B raised it, so it does not rule on member A's quote.
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "B"));

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void ACitationNoteDemotion_ReadsAsIndeterminate_AndLeavesTheFlagUnresolved()
    {
        // The verifier refuted the quote, but its citation has no live call site, so the effective
        // verdict is Indeterminate.
        var demoted = Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A", citationNote: "cited function pray_fake has no live call site");
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, demoted.EffectiveVerdict);

        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90, item: demoted);

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.Unresolved, 25, 70);
    }

    [Fact]
    public void AnOverturnWithoutARecordedPreCapScore_UsesTheGradedScore()
    {
        var answer = PanelAnswer(25, 70, flagA: true,
            item: Item(QuoteA, BenchmarkClaimVerdict.Supported, "A"));
        answer.RawQualityScore = null;

        AssertResolved(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling), BenchmarkCriticalErrorResolution.OverturnedByVerifier, 25, 70);
    }

    [Theory]
    [InlineData(BenchmarkAssessmentStatus.Pending)]
    [InlineData(BenchmarkAssessmentStatus.Assessing)]
    [InlineData(BenchmarkAssessmentStatus.Failed)]
    public void AMemberNotScored_GivesNoResolution(BenchmarkAssessmentStatus status)
    {
        var memberBNotScored = PanelAnswer(25, 70, flagA: true, item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A"));
        memberBNotScored.CoAssessmentStatus = status;
        Assert.Null(BenchmarkCriticalErrorResolver.ResolvePanel(memberBNotScored, Ceiling));

        var memberANotScored = PanelAnswer(80, 70);
        memberANotScored.AssessmentStatus = status;
        Assert.Null(BenchmarkCriticalErrorResolver.ResolvePanel(memberANotScored, Ceiling));
    }

    [Fact]
    public void AMissingQualityScore_GivesNoResolution()
    {
        var answer = PanelAnswer(80, 70);
        answer.CoAssessmentQualityScore = null;

        Assert.Null(BenchmarkCriticalErrorResolver.ResolvePanel(answer, Ceiling));
    }

    // --- The quote match ---

    [Fact]
    public void QuoteVerdict_IsNull_ForNoItemsOrABlankQuote()
    {
        var items = new List<BenchmarkClaimVerification> { Item(QuoteA, BenchmarkClaimVerdict.Refuted) };

        Assert.Null(BenchmarkCriticalErrorResolver.QuoteVerdict(null, QuoteA));
        Assert.Null(BenchmarkCriticalErrorResolver.QuoteVerdict(new List<BenchmarkClaimVerification>(), QuoteA));
        Assert.Null(BenchmarkCriticalErrorResolver.QuoteVerdict(items, (string?)null));
        Assert.Null(BenchmarkCriticalErrorResolver.QuoteVerdict(items, "   "));
    }

    [Fact]
    public void QuoteVerdict_PrefersTheExactItemAtIndexZero_ThenAnyExactItem_ThenTheSameItem()
    {
        var indexZero = new List<BenchmarkClaimVerification>
        {
            Item(QuoteA, BenchmarkClaimVerdict.Supported, claimIndex: 2),
            Item(QuoteA, BenchmarkClaimVerdict.Refuted, claimIndex: 0)
        };
        Assert.Equal(BenchmarkClaimVerdict.Refuted, BenchmarkCriticalErrorResolver.QuoteVerdict(indexZero, "  " + QuoteA + " "));

        var exactElsewhere = new List<BenchmarkClaimVerification>
        {
            Item("- " + QuoteA, BenchmarkClaimVerdict.Indeterminate, claimIndex: 0),
            Item(QuoteA, BenchmarkClaimVerdict.Supported, claimIndex: 3)
        };
        Assert.Equal(BenchmarkClaimVerdict.Supported, BenchmarkCriticalErrorResolver.QuoteVerdict(exactElsewhere, QuoteA));

        var sameItem = new List<BenchmarkClaimVerification>
        {
            Item("- " + QuoteA.ToUpperInvariant(), BenchmarkClaimVerdict.Refuted, claimIndex: 1)
        };
        Assert.Equal(BenchmarkClaimVerdict.Refuted, BenchmarkCriticalErrorResolver.QuoteVerdict(sameItem, QuoteA));
    }

    [Fact]
    public void QuoteVerdict_AgreesWithCriticalErrorQuoteWasSupported()
    {
        foreach (var verdict in new[] { BenchmarkClaimVerdict.Supported, BenchmarkClaimVerdict.Refuted, BenchmarkClaimVerdict.Indeterminate })
        {
            var items = new List<BenchmarkClaimVerification> { Item(QuoteA, verdict) };
            Assert.Equal(
                BenchmarkService.CriticalErrorQuoteWasSupported(items, QuoteA),
                BenchmarkCriticalErrorResolver.QuoteVerdict(items, QuoteA) == BenchmarkClaimVerdict.Supported);
        }
    }

    [Fact]
    public void QuoteVerdict_ForAView_ReadsTheItemsThatMemberRaised()
    {
        var answer = PanelAnswer(80, 20, flagB: true,
            items: new[]
            {
                Item(QuoteB, BenchmarkClaimVerdict.Supported, "A"),
                Item(QuoteB, BenchmarkClaimVerdict.Refuted, "B")
            });
        var items = BenchmarkReportContent.ReadVerifications(answer.ClaimVerificationJson);
        var memberB = BenchmarkVerdictView.FromCoAssessment(answer)!;

        Assert.Equal(BenchmarkClaimVerdict.Refuted, BenchmarkCriticalErrorResolver.QuoteVerdict(items, memberB));
    }

    // --- Gate, ceiling and confirmation ---

    [Theory]
    [InlineData(0, false)]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    public void Applies_FromScoringMethod13(int method, bool expected)
    {
        Assert.Equal(expected, BenchmarkCriticalErrorResolver.Applies(Run(method, panel: true)));
    }

    [Fact]
    public void CeilingOf_ReadsTheRunsProfileSnapshot_AndDefaultsTo25()
    {
        Assert.Equal(30, BenchmarkCriticalErrorResolver.CeilingOf(Run(13, panel: true, "{\"CriticalErrorCeiling\":30}")));
        Assert.Equal(25, BenchmarkCriticalErrorResolver.CeilingOf(Run(13, panel: true, "{\"SpeedTargetMs\":2000}")));
        Assert.Equal(25, BenchmarkCriticalErrorResolver.CeilingOf(Run(13, panel: true)));
    }

    [Theory]
    [InlineData(BenchmarkCriticalErrorResolution.Agreed, true)]
    [InlineData(BenchmarkCriticalErrorResolution.UpheldByVerifier, true)]
    [InlineData(BenchmarkCriticalErrorResolution.SingleAssessor, true)]
    [InlineData(BenchmarkCriticalErrorResolution.OverturnedByVerifier, false)]
    [InlineData(BenchmarkCriticalErrorResolution.Unresolved, false)]
    [InlineData(BenchmarkCriticalErrorResolution.None, false)]
    public void IsConfirmed_ForAgreedUpheldAndSingleAssessor(BenchmarkCriticalErrorResolution resolution, bool expected)
    {
        var answer = PanelAnswer(80, 70);
        answer.CriticalErrorResolution = resolution;

        Assert.Equal(expected, BenchmarkCriticalErrorResolver.IsConfirmed(answer));
    }

    [Fact]
    public void IsConfirmed_IsFalseWithoutAResolution()
    {
        Assert.False(BenchmarkCriticalErrorResolver.IsConfirmed(PanelAnswer(80, 70)));
    }

    // --- ApplyTo ---

    [Fact]
    public void ApplyTo_PanelRun_WritesTheResolvedPanelScore_AndTheMembersOwnDisagreement()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A"));

        BenchmarkCriticalErrorResolver.ApplyTo(answer, Run(13, panel: true), isPanelRun: true);

        Assert.Equal(BenchmarkCriticalErrorResolution.UpheldByVerifier, answer.CriticalErrorResolution);
        Assert.Equal(25.0, answer.PanelQualityScore);
        // The members' own scores, 25 and 70, and their split flag.
        Assert.True(answer.PanelDisagreed);

        // The member columns are untouched.
        Assert.Equal(25, answer.QualityScore);
        Assert.Equal(70, answer.CoAssessmentQualityScore);
    }

    [Fact]
    public void ApplyTo_PanelRun_CapsAtTheRunsRecordedCeiling()
    {
        var answer = PanelAnswer(30, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Refuted, "A"));

        BenchmarkCriticalErrorResolver.ApplyTo(answer, Run(13, panel: true, "{\"CriticalErrorCeiling\":30}"), isPanelRun: true);

        Assert.Equal(30.0, answer.PanelQualityScore);
    }

    [Fact]
    public void ApplyTo_PanelRun_Overturned_UsesThePreCapScore()
    {
        var answer = PanelAnswer(25, 70, flagA: true, rawA: 90,
            item: Item(QuoteA, BenchmarkClaimVerdict.Supported, "A"));

        BenchmarkCriticalErrorResolver.ApplyTo(answer, Run(13, panel: true), isPanelRun: true);

        Assert.Equal(BenchmarkCriticalErrorResolution.OverturnedByVerifier, answer.CriticalErrorResolution);
        Assert.Equal(80.0, answer.PanelQualityScore);
    }

    [Fact]
    public void ApplyTo_PanelRun_NoFlags_IsTheMeanAndAgreesWithinFifteenPoints()
    {
        var answer = PanelAnswer(80, 70);

        BenchmarkCriticalErrorResolver.ApplyTo(answer, Run(13, panel: true), isPanelRun: true);

        Assert.Equal(BenchmarkCriticalErrorResolution.None, answer.CriticalErrorResolution);
        Assert.Equal(75.0, answer.PanelQualityScore);
        Assert.False(answer.PanelDisagreed);
    }

    [Fact]
    public void ApplyTo_PanelRun_NullsAllThree_WhenAMemberHasNotScored()
    {
        var answer = PanelAnswer(80, 70);
        answer.PanelQualityScore = 75.0;
        answer.PanelDisagreed = false;
        answer.CriticalErrorResolution = BenchmarkCriticalErrorResolution.None;
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Failed;

        BenchmarkCriticalErrorResolver.ApplyTo(answer, Run(13, panel: true), isPanelRun: true);

        Assert.Null(answer.PanelQualityScore);
        Assert.Null(answer.PanelDisagreed);
        Assert.Null(answer.CriticalErrorResolution);
    }

    [Fact]
    public void ApplyTo_SingleAssessorRun_RecordsWhetherTheAssessorFlagged_AndLeavesTheScore()
    {
        var run = Run(13, panel: false);

        var flagged = PanelAnswer(25, 0, flagA: true, rawA: 90);
        flagged.CoAssessmentStatus = null;
        flagged.CoAssessmentQualityScore = null;
        BenchmarkCriticalErrorResolver.ApplyTo(flagged, run, isPanelRun: false);
        Assert.Equal(BenchmarkCriticalErrorResolution.SingleAssessor, flagged.CriticalErrorResolution);
        Assert.Equal(25, flagged.QualityScore);
        Assert.Null(flagged.PanelQualityScore);

        var clean = PanelAnswer(80, 0);
        clean.CoAssessmentStatus = null;
        BenchmarkCriticalErrorResolver.ApplyTo(clean, run, isPanelRun: false);
        Assert.Equal(BenchmarkCriticalErrorResolution.None, clean.CriticalErrorResolution);

        var unscored = PanelAnswer(80, 0, flagA: true);
        unscored.CoAssessmentStatus = null;
        unscored.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
        BenchmarkCriticalErrorResolver.ApplyTo(unscored, run, isPanelRun: false);
        Assert.Null(unscored.CriticalErrorResolution);
    }
}
