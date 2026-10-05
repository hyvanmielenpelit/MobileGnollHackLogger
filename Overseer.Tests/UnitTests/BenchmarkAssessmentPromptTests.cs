namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkAssessmentPromptTests
{
    [Fact]
    public void BuildPerQuestionPrompt_FormatsRubricWithDelimiters()
    {
        var rubric = "**REQUIRED** (accuracy + completeness)\n- Fact 1\n- Fact 2\n\n**SOURCE** — src/role.c";
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 1,
            questionText: "What is the Gnoll race?",
            difficulty: BenchmarkDifficulty.Simple,
            expectedPoints: rubric,
            answerText: "The Gnoll race replaces the gnome in GnollHack.",
            status: BenchmarkAnswerStatus.Ok,
            durationMs: 2500);

        Assert.Contains("Assessment Rubric / Reference Points:\n--- BEGIN RUBRIC ---\n" + rubric + "\n--- END RUBRIC ---", prompt.Replace("\r\n", "\n"));
        Assert.DoesNotContain($"Assessment Rubric / Reference Points: {rubric}", prompt);
    }

    [Fact]
    public void BuildPerQuestionPrompt_OmitsRubricWhenNullOrWhitespace()
    {
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 1,
            questionText: "What is the Gnoll race?",
            difficulty: BenchmarkDifficulty.Simple,
            expectedPoints: null,
            answerText: "Some answer",
            status: BenchmarkAnswerStatus.Ok,
            durationMs: 2000);

        Assert.DoesNotContain("--- BEGIN RUBRIC ---", prompt);
        Assert.DoesNotContain("Assessment Rubric", prompt);
    }

    [Fact]
    public void BuildPerQuestionPrompt_DelimitsCandidateAnswerAndHidesModelNameAndThought()
    {
        var answerText = "Candidate answer about dragon armor.";
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 3,
            questionText: "What are the stats of silver dragon scale mail?",
            difficulty: BenchmarkDifficulty.Simple,
            expectedPoints: "Base AC 1",
            answerText: answerText,
            status: BenchmarkAnswerStatus.Ok,
            durationMs: 1500);

        Assert.Contains("=== START OF CANDIDATE ANSWER ===\n" + answerText + "\n=== END OF CANDIDATE ANSWER ===", prompt.Replace("\r\n", "\n"));
        Assert.DoesNotContain("gpt-4", prompt);
        Assert.DoesNotContain("claude-3-5", prompt);
    }

    [Fact]
    public void BuildPerQuestionPrompt_ProviderError_MarksExcludedAndOmitsCandidateAnswerBlock()
    {
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 2,
            questionText: "Explain weapon quality modifiers.",
            difficulty: BenchmarkDifficulty.Intermediate,
            expectedPoints: "Quality modifiers multiply damage dice.",
            answerText: "",
            status: BenchmarkAnswerStatus.ProviderError,
            durationMs: 500);

        Assert.Contains("Status: ProviderError", prompt);
        Assert.Contains("Excluded: Provider API error", prompt);
        Assert.DoesNotContain("=== START OF CANDIDATE ANSWER ===", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_FormatsRubricWithDelimiters()
    {
        var rubric = "**REQUIRED** — base AC 1 and reflection";
        var verdicts = new List<BenchmarkPerQuestionVerdictSummary>
        {
            new BenchmarkPerQuestionVerdictSummary
            {
                OrderIndex = 1,
                QuestionText = "Stats of silver dragon scale mail?",
                ExpectedPoints = rubric,
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                QualityScore = 87,
                SpeedScore = 90,
                DurationMs = 2500,
                AssessedDifficulty = 25,
                CriticalError = false,
                ReviewComment = "Accurate and thorough.",
                Status = BenchmarkAnswerStatus.Ok
            }
        };

        var prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Test Suite", verdicts);

        Assert.Contains("Rubric:\n--- BEGIN RUBRIC ---\n" + rubric + "\n--- END RUBRIC ---", prompt.Replace("\r\n", "\n"));
        Assert.DoesNotContain($"Rubric: {rubric}", prompt);
    }

    [Fact]
    public void BenchmarkDifficultyPrompt_BuildPrompt_FormatsRubricWithDelimiters()
    {
        var rubric = "**REQUIRED** — pray timeout formula";
        var items = new List<BenchmarkDifficultyQuestionItem>
        {
            new BenchmarkDifficultyQuestionItem
            {
                Id = 1,
                OrderIndex = 1,
                QuestionText = "How does prayer timeout work?",
                AuthorBand = BenchmarkDifficulty.Simple,
                ExpectedPoints = rubric
            }
        };

        var prompt = BenchmarkDifficultyPrompt.BuildPrompt("Test Suite", items);

        Assert.Contains("Reference Rubric:\n--- BEGIN RUBRIC ---\n" + rubric + "\n--- END RUBRIC ---", prompt.Replace("\r\n", "\n"));
        Assert.DoesNotContain($"Reference Rubric: {rubric}", prompt);
    }

    [Fact]
    public void Prompts_WithAtxHeadingsInRubric_SafelyDelimited()
    {
        var complexRubric = "### Question 99: Internal Rubric Heading\n- Item 1\n- Item 2";
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 1,
            questionText: "Explain multi-layer rendering.",
            difficulty: BenchmarkDifficulty.Advanced,
            expectedPoints: complexRubric,
            answerText: "Layer types enum",
            status: BenchmarkAnswerStatus.Ok,
            durationMs: 3000);

        Assert.Contains("--- BEGIN RUBRIC ---\n" + complexRubric + "\n--- END RUBRIC ---", prompt.Replace("\r\n", "\n"));
    }

    [Fact]
    public void BuildPerQuestionPrompt_OmitsCandidateTurnDuration_AndIncludesHarnessContextAndCriticalInstructions()
    {
        var prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suiteName: "Test Suite",
            orderIndex: 1,
            questionText: "What is the Gnoll race?",
            difficulty: BenchmarkDifficulty.Simple,
            expectedPoints: "Gnolls replaced gnomes.",
            answerText: "Gnolls are playable.",
            status: BenchmarkAnswerStatus.Ok,
            allowedTools: new List<string> { "source_code_search", "source_code_view" },
            toolCallsCompleted: 2,
            toolBudgetExhausted: true,
            scrubbedArtifactCount: 3,
            toolCallBudget: 40);

        // Turn duration MUST be omitted from candidate assessment prompt to avoid anchoring
        Assert.DoesNotContain("candidate turn duration", prompt, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("turn duration:", prompt, System.StringComparison.OrdinalIgnoreCase);

        // Harness context must be present
        Assert.Contains("Harness Context:", prompt);
        Assert.Contains("- Available tools: source_code_search, source_code_view", prompt);
        Assert.Contains("- Completed tool calls: 2", prompt);
        Assert.Contains("- Tool budget exhausted: Yes", prompt);
        Assert.Contains("- Tool call budget for this question: 40", prompt);
        Assert.Contains("- Transport artifacts removed by the harness before grading: 3 block(s)", prompt);

        // Critical instructions 6 and 7 must be present
        Assert.Contains("do not treat harness-imposed tool unavailability as a model failure", prompt);

        // Instruction 7 now states the artifacts are already gone. Asking the assessor to spot
        // and ignore them was an unverifiable judgement call, and it was applied
        // inconsistently: on the 2026-09-03 run four answers with the same defect scored 94-99
        // while one scored 70.
        Assert.Contains("already had provider transport artifacts", prompt);
        Assert.Contains("do not speculate about removed content or deduct for it", prompt);
        Assert.DoesNotContain("If the answer contains obvious harness artifacts", prompt);
    }

    [Fact]
    public void ScoringMethodVersion_IsFourteen()
    {
        // v4 was the artifact scrubbing and speed recalibration. v5 changed what a critical
        // error is — an omission can no longer be one, and the claim must be quoted. v6 changed
        // what an *accuracy deduction* is: a claim the rubric neither states nor contradicts is
        // declared rather than deducted for. v7 enforces evidence discipline: docking a level
        // requires stating what was wrong, and "Matches rubric." may only accompany level 6.
        // v8 widens the synthesis factual-error guardrail to any answer whose accuracy evidence
        // names a defect, and makes completeness scope a grading rule: the question defines the
        // scope, and a rubric point it did not ask for is recorded under OUT-OF-SCOPE: rather than
        // deducted for. v9 does the same for Readability — a rubric FORM suggestion is recorded
        // under FORM: rather than deducted for — and stops the synthesis accuracy-defect marker
        // firing on sentence-form denials. v10 scores a question the model failed to answer 0
        // instead of excluding it, so a candidate can no longer raise its index by not answering.
        // v11 re-anchors ACCURACY levels 4-6 on what the answer states rather than on source-level
        // depth, which the production concise prompt tells the candidate not to produce; a level
        // below 6 must name a wrong or imprecise statement. It moves every index. v12 grades
        // ACCURACY against the rubric and the board only: an own-knowledge suspicion is reported as a
        // "Suspected false: " unverified claim for the verifier instead of lowering the level. v13
        // confirms critical errors: a one-member panel split is settled by the claim verifier, a
        // critical error comes only from the rubric or the board, and an honest abstention marked
        // notAttempted is raised to the profile's not-attempted floor. v14 narrows the ACCURACY
        // anchors: a true statement is never an imprecision, and a lowered level names a statement
        // a player could act on wrongly.
        // Scores are not comparable across any of those boundaries on the answers they touch, and
        // the report prints the version so a mixed comparison is visible rather than silent.
        Assert.Equal(14, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void HarnessVersion_IsFiftyThree()
    {
        // Harness 53: a member whose charged sentence the verifier refuted is never
        // verification-cleared; the synthesis footer counts a contested deduction on either member
        // and the charges the verifier upheld; the synthesis data blocks state each assessor's
        // charged sentences by verdict; and an answer whose own claims were not checked prints its
        // charged sentences on the Claim Verification line.
        Assert.Equal("53", BenchmarkAssessmentPrompt.HarnessVersion);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_AsksForTheStructuredFindingsArray()
    {
        var summary = new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = 1,
            QuestionText = "Q?",
            AccuracyLevel = 5,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 90,
            Status = BenchmarkAnswerStatus.Ok
        };

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { summary }).Replace("\r\n", "\n");

        // The instruction names every kind and category the parser accepts, and the schema carries
        // one example entry with all four fields.
        Assert.Contains("List every strength and weakness you name in `findings` as well", prompt);
        Assert.Contains("`kind` is \"strength\" or \"weakness\"", prompt);
        Assert.Contains("`category` is one of " + string.Join(", ", BenchmarkAssessmentParser.SynthesisFindingCategories), prompt);
        Assert.Contains("\"findings\": [\n    { \"kind\": \"weakness\", \"category\": \"accuracy\", \"questions\": [3, 7], \"text\": ", prompt);

        int schema = prompt.IndexOf("--- OUTPUT JSON SCHEMA ---", StringComparison.Ordinal);
        Assert.True(schema >= 0);
        Assert.True(prompt.IndexOf("\"findings\":", StringComparison.Ordinal) > schema);
        Assert.True(prompt.IndexOf("\"overallComments\":", schema, StringComparison.Ordinal) < prompt.IndexOf("\"findings\":", schema, StringComparison.Ordinal));
    }

    [Fact]
    public void PerQuestionPrompt_SaysAnUnmentionedClaimIsNotTherebyInvented()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "Question?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer text.",
            BenchmarkAnswerStatus.Ok);

        // A rubric is an incomplete ground-truth list, so "absent from the rubric" is not evidence
        // of falsehood and cannot carry the critical-error cap on its own.
        Assert.Contains("A claim the rubric does not mention is not thereby invented", prompt);
        Assert.Contains("A claim the rubric merely omits belongs in `unverifiedClaims` too (section 7).", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_WithBoard_SendsTheGroundTruthSectionAsTheSecondSystemMessage()
    {
        const string boardText = "Dlvl:1 $:0 HP:15(15) Pw:10(10) AC:10";
        string? boardBlock = BenchmarkAssessmentPrompt.BuildGradingBoardBlock("Test Board", boardText);
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            1,
            "What is the status of the player?",
            BenchmarkDifficulty.Simple,
            "**REQUIRED** - HP 15/15.\n**BOARD FACTS**\n- HP: 15/15",
            "Player is healthy.",
            BenchmarkAnswerStatus.Ok,
            boardGivenAbove: true);

        var seed = BenchmarkService.BuildGradingPrompt(
            BenchmarkService.GradingSystemPrompt, BenchmarkAssessmentPrompt.BuildPerQuestionPreamble("Suite"), body, boardBlock).SeedHistory;

        Assert.Equal(3, seed.Count);
        Assert.Equal(new[] { "system", "system", "user" }, seed.Select(m => Field(m, "role")));
        string boardMessage = Field(seed[1], "content");
        Assert.StartsWith("--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---", boardMessage);
        Assert.Contains("Board Name: Test Board", boardMessage);
        Assert.Contains("HP:15(15)", boardMessage);
        Assert.Contains("--- END GAME CONTEXT BOARD ---", boardMessage);

        // The question's body points at the board and never repeats it; the rubric's BOARD FACTS stay.
        string userMessage = Field(seed[2], "content");
        Assert.DoesNotContain("--- GAME CONTEXT BOARD", userMessage);
        Assert.DoesNotContain("Dlvl:1 $:0", userMessage);
        Assert.Contains(BenchmarkAssessmentPrompt.BoardGivenAboveLine, userMessage);
        Assert.Contains("- HP: 15/15", userMessage);
        Assert.StartsWith(BenchmarkAssessmentPrompt.QuestionBlockMarker, userMessage);
    }

    [Fact]
    public void PerQuestionPrompt_WithBoard_ReadsInstructionsThenBoardThenQuestion()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "What is the status of the player?",
            BenchmarkDifficulty.Simple,
            "**REQUIRED** - HP 15/15.",
            "Player is healthy.",
            BenchmarkAnswerStatus.Ok,
            boardName: "Test Board",
            boardText: "Dlvl:1 $:0 HP:15(15) Pw:10(10) AC:10");

        int instructions = prompt.IndexOf("CRITICAL INSTRUCTIONS:", StringComparison.Ordinal);
        int board = prompt.IndexOf("--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---", StringComparison.Ordinal);
        int question = prompt.IndexOf(BenchmarkAssessmentPrompt.QuestionBlockMarker, StringComparison.Ordinal);

        Assert.True(instructions >= 0 && instructions < board && board < question);
        Assert.Equal(board, prompt.LastIndexOf("--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---", StringComparison.Ordinal));
    }

    [Fact]
    public void PerQuestionPrompt_WithoutBoard_OmitsGroundTruthSection()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "What is the status of the player?",
            BenchmarkDifficulty.Simple,
            "**REQUIRED** - keen smell.",
            "Player has keen smell.",
            BenchmarkAnswerStatus.Ok,
            boardName: null,
            boardText: null);

        Assert.DoesNotContain("--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---", prompt);
        Assert.DoesNotContain(BenchmarkAssessmentPrompt.BoardGivenAboveLine, prompt);
        Assert.Null(BenchmarkAssessmentPrompt.BuildGradingBoardBlock("Test Board", "  "));

        var seed = BenchmarkService.BuildGradingPrompt(BenchmarkService.GradingSystemPrompt, "Preamble", "Body").SeedHistory;
        Assert.Equal(new[] { "system", "user" }, seed.Select(m => Field(m, "role")));
    }

    private static string Field(object message, string name) =>
        message.GetType().GetProperty(name)?.GetValue(message) as string ?? string.Empty;

    [Fact]
    public void PerQuestionPrompt_DeclaresUnverifiedClaimsInsteadOfDeductingForThem()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "What racial traits does a Gnoll get?",
            BenchmarkDifficulty.Simple,
            "**REQUIRED** - keen smell; eats bones.",
            "Gnolls are immune to lycanthropy.",
            BenchmarkAnswerStatus.Ok);

        // The rule the 2026-09-03 run needed and did not have: Q1 lost 45 points of Accuracy —
        // the 55%-weight dimension — for a Yeenaghu trait the assessor called "unverified",
        // which penalised the candidate for knowing more than its rubric.
        Assert.Contains("is **not** an accuracy deduction", prompt);
        Assert.Contains("unverifiedClaims", prompt);
        Assert.Contains("Never write \"unverified\"", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_AccuracyLevelThree_NoLongerOffersHallucinationAsAnAnchor()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "Question?",
            BenchmarkDifficulty.Simple,
            null,
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        // Level 3 was the only anchor that fitted "I could not confirm this", which is how an
        // unverifiable claim came to cost the same as a verified falsehood. Fabrication stays
        // covered at levels 0-2 and by CRITICAL ERROR, both of which still say so.
        Assert.Contains("Level 3: Mostly correct; minor inaccuracies or subtle confusion of edge cases.", prompt);
        Assert.DoesNotContain("slight hallucinations", prompt);
        Assert.Contains("Level 0: Completely fabricated", prompt);
    }

    [Fact]
    public void SynthesisPrompt_OmitsTurnDuration()
    {
        var verdicts = new List<BenchmarkPerQuestionVerdictSummary>
        {
            new()
            {
                OrderIndex = 1,
                QuestionText = "Question?",
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                QualityScore = 87,
                SpeedScore = 60,
                DurationMs = 165068,
                AssessedDifficulty = 60,
                ReviewComment = "Good.",
                Status = BenchmarkAnswerStatus.Ok
            }
        };

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", verdicts);

        // Turn duration was removed from the per-question prompt in harness version 2 to stop the
        // assessor penalising deliberation. Leaving it in the prompt that produces the Holistic
        // Assessor Score reintroduced the same bias one level up.
        Assert.DoesNotContain("Duration:", prompt);
        Assert.DoesNotContain("165068", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_IncludesTheLevelDistributionAndTheAdvisoryInstructions()
    {
        // H5: the model used to have to count eighteen "Levels:" lines itself to state how many
        // answers sat at a given level; the harness now hands over the count pre-computed.
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        var q2 = Verdict(2, accuracyLevel: 4, accuracyEvidence: "Misstates the AC.");
        q2.CompletenessLevel = 6;

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1, q2 });

        Assert.Contains(
            "--- LEVEL DISTRIBUTION (counted by the harness; copy these figures rather than recounting the verdicts below) ---",
            prompt);
        Assert.Contains("Accuracy: 1 answer(s) at level 6, 1 answer(s) at level 4", prompt);
        Assert.Contains("Completeness: 1 answer(s) at level 6, 1 answer(s) at level 5", prompt);
        Assert.Contains("Conciseness: 2 answer(s) at level 5", prompt);
        Assert.Contains("Readability: 2 answer(s) at level 5", prompt);

        // Printed ahead of the per-question verdicts, so the model reads the pre-counted figures
        // before it ever sees a "Levels:" line to (mis)count itself.
        int distributionAt = prompt.IndexOf("--- LEVEL DISTRIBUTION", StringComparison.Ordinal);
        int verdictsAt = prompt.IndexOf("--- PER-QUESTION VERDICTS AND ASSESSMENTS ---", StringComparison.Ordinal);
        Assert.True(distributionAt >= 0 && distributionAt < verdictsAt);

        Assert.Contains(
            "A refuted claim, and a critical error the second reader or the claim verifier contested, are advisory findings, not confirmed defects",
            prompt);
        Assert.Contains(
            "is copied from the data blocks below, never recomputed by rereading or recounting the per-question verdicts. This synthesis feeds no score.",
            prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_CarriesHarnessCountedClaimVerificationTotals()
    {
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 2;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimsIndeterminateCount = 0;
        var q2 = Verdict(2, accuracyLevel: 5, accuracyEvidence: "Minor slip.");
        q2.UnverifiedClaimCount = 3;
        q2.ClaimsSupportedCount = 1;
        q2.ClaimsRefutedCount = 1;
        q2.ClaimsIndeterminateCount = 1;
        var q3 = Verdict(3, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1, q2, q3 });

        Assert.Contains(
            "--- CLAIM VERIFICATION TOTALS (counted by the harness; copy these figures rather than adding up the per-question lines) ---",
            prompt);
        Assert.Contains("Unverified claims: 5 across 2 answer(s); verified: 3 supported, 1 refuted, 1 indeterminate", prompt);

        int totalsAt = prompt.IndexOf("--- CLAIM VERIFICATION TOTALS", StringComparison.Ordinal);
        int verdictsAt = prompt.IndexOf("--- PER-QUESTION VERDICTS AND ASSESSMENTS ---", StringComparison.Ordinal);
        Assert.True(totalsAt >= 0 && totalsAt < verdictsAt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_WithoutVerificationCounts_HasNoClaimTotalsBlock()
    {
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        q1.UnverifiedClaimCount = 2;

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1 });

        Assert.DoesNotContain("CLAIM VERIFICATION TOTALS", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_PanelRun_PrintsTheMembersClaimsAndTheRunsUnion()
    {
        // Run 74: each panel synthesis was handed its own member's totals (52 and 77 claims) and
        // quoted them as the run's, while the report printed the union (87).
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 1;
        q1.ClaimsRefutedCount = 1;
        q1.ClaimsIndeterminateCount = 0;
        var totals = new BenchmarkRunClaimTotals(Claims: 5, Answers: 3, Supported: 3, Refuted: 1, Indeterminate: 1);

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1 }, panelRunClaimTotals: totals);

        Assert.Contains("Claims you raised: 2 across 1 answer(s); verified: 1 supported, 1 refuted, 0 indeterminate", prompt);
        Assert.Contains(
            "All claims checked in this run (both members, each counted once): 5 across 3 answer(s); verified: 3 supported, 1 refuted, 1 indeterminate",
            prompt);
        Assert.DoesNotContain("Unverified claims: ", prompt);
        Assert.Contains("When you state a run-wide claim total, copy the 'All claims checked' line.", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_SingleAssessorRun_HasNoRunWideClaimLine()
    {
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 2;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimsIndeterminateCount = 0;

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1 });

        Assert.Contains("Unverified claims: 2 across 1 answer(s); verified: 2 supported, 0 refuted, 0 indeterminate", prompt);
        Assert.DoesNotContain("Claims you raised", prompt);
        Assert.DoesNotContain("All claims checked", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_StatesWhatTheVerifierDidWithEachCharge()
    {
        // Runs 93 and 94: a synthesis wrote that the verifier refuted nothing while it had upheld a
        // charged sentence. One answer here carries an upheld charge and the other a cleared one.
        var q1 = Verdict(1, accuracyLevel: 4, accuracyEvidence: "States the wrong prayer timeout.");
        q1.ChargedSentencesUpheld = 1;
        var q2 = Verdict(2, accuracyLevel: 5, accuracyEvidence: "Misstates the altar rule.");
        q2.ChargedSentencesCleared = 1;

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1, q2 });

        Assert.Contains("Sentences you charged as false in this run: 2 — upheld 1, cleared 1, undecided 0.", prompt);
        Assert.Contains("Sentences you charged as false: 1 — upheld by the claim verifier (refuted) 1, cleared (supported) 0, undecided 0.", prompt);
        Assert.Contains("Sentences you charged as false: 1 — upheld by the claim verifier (refuted) 0, cleared (supported) 1, undecided 0.", prompt);
        Assert.Contains(
            "A charged sentence the verifier refuted is one where the verifier agreed with you: never write that nothing was refuted when one was.",
            prompt);
        // No claim counts were recorded, so the totals block carries the charges alone.
        Assert.DoesNotContain("Unverified claims: ", prompt);

        int totalsAt = prompt.IndexOf("Sentences you charged as false in this run:", StringComparison.Ordinal);
        int verdictsAt = prompt.IndexOf("--- PER-QUESTION VERDICTS AND ASSESSMENTS ---", StringComparison.Ordinal);
        Assert.True(totalsAt >= 0 && totalsAt < verdictsAt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_WithoutCharges_PrintsNoChargedSentenceLine()
    {
        var q1 = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q1 });

        Assert.DoesNotContain("Sentences you charged as false", prompt);
    }

    [Fact]
    public void BuildVerdictSummary_CountsEachMembersOwnChargesByVerdict()
    {
        static BenchmarkClaimVerification Charge(int index, string member, BenchmarkClaimVerdict verdict)
            => new(index, $"Sentence {index}.", verdict, "src/pray.c:120", null)
            {
                Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
                RaisedBy = new[] { member },
                AccusedBy = new[] { member }
            };

        var run = new BenchmarkRun { Id = 1, HarnessVersion = "53", ScoringMethodVersion = 14, CoAssessorModelConfigurationId = 9 };
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "Answer 1",
            ExpectedPointsRecorded = true,
            ExpectedPointsUsed = "Rubric.",
            Difficulty = BenchmarkDifficulty.Intermediate,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = 4,
            QualityScore = 70,
            ClaimVerificationJson = JsonSerializer.Serialize(new[]
            {
                Charge(0, "A", BenchmarkClaimVerdict.Refuted),
                Charge(1, "A", BenchmarkClaimVerdict.Supported),
                Charge(2, "B", BenchmarkClaimVerdict.Refuted),
                Charge(3, "B", BenchmarkClaimVerdict.Indeterminate)
            })
        };

        var memberA = BenchmarkService.BuildVerdictSummary(run, answer, BenchmarkPanelMember.A, includeAdvisory: false);
        var memberB = BenchmarkService.BuildVerdictSummary(run, answer, BenchmarkPanelMember.B, includeAdvisory: false);

        Assert.Equal((1, 1, 0), (memberA.ChargedSentencesUpheld, memberA.ChargedSentencesCleared, memberA.ChargedSentencesUndecided));
        Assert.Equal((1, 0, 1), (memberB.ChargedSentencesUpheld, memberB.ChargedSentencesCleared, memberB.ChargedSentencesUndecided));
    }

    [Fact]
    public void BenchmarkRunClaimTotals_CountsEachAnswersOwnClaimsOnce()
    {
        static string Json(params BenchmarkClaimVerification[] items) => JsonSerializer.Serialize(items);
        var claim = new[] { BenchmarkClaimRoles.UnverifiedClaim };
        var answers = new[]
        {
            new BenchmarkRunAnswer
            {
                OrderIndex = 1,
                ClaimVerificationJson = Json(
                    new BenchmarkClaimVerification(0, "Raised by both.", BenchmarkClaimVerdict.Supported, "src/a.c", null) { Roles = claim, RaisedBy = new[] { "A", "B" } },
                    new BenchmarkClaimVerification(1, "Raised by B.", BenchmarkClaimVerdict.Refuted, "src/b.c", null) { Roles = claim, RaisedBy = new[] { "B" } },
                    new BenchmarkClaimVerification(2, "An accused sentence.", BenchmarkClaimVerdict.Supported, "src/c.c", null) { Roles = new[] { BenchmarkClaimRoles.AccusedQuote }, RaisedBy = new[] { "A" } }),
            },
            new BenchmarkRunAnswer
            {
                OrderIndex = 2,
                ClaimVerificationJson = Json(
                    new BenchmarkClaimVerification(0, "Raised by A.", BenchmarkClaimVerdict.Indeterminate, null, null) { Roles = claim, RaisedBy = new[] { "A" } }),
            },
            new BenchmarkRunAnswer { OrderIndex = 3 },
        };

        var totals = BenchmarkRunClaimTotals.FromAnswers(answers);

        Assert.Equal(new BenchmarkRunClaimTotals(Claims: 3, Answers: 2, Supported: 1, Refuted: 1, Indeterminate: 1), totals);
    }

    [Fact]
    public void PerQuestionPrompt_ExcludesOmissionsFromCriticalError_AndRequiresAQuote()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "Question?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer text.",
            BenchmarkAnswerStatus.Ok);

        // The 2026-09-03 run capped a question at 25 because the answer "completely omits the
        // character level 3 requirement" — an omission, which the rubric's own negative example
        // already excluded.
        Assert.Contains("An omission is NEVER a critical error", prompt);
        Assert.Contains("criticalErrorQuote", prompt);
        Assert.Contains("accuracyEvidence", prompt);
        Assert.Contains("completenessEvidence", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_ExcludesOmissionsFromAccuracy()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "Question?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer text.",
            BenchmarkAnswerStatus.Ok);

        Assert.Contains("An omission is NEVER an ACCURACY deduction", prompt);
        Assert.Contains("Missing information is graded through COMPLETENESS", prompt);
        Assert.Contains("An accuracy deduction must name something the answer **states** that is wrong", prompt);
    }

    [Fact]
    public void SecondOpinionPrompt_BlindMode_OmitsFirstVerdictAndFramesIndependently()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildSecondOpinionPrompt(
            "Suite",
            17,
            "How do sacrifice gifts work?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer text.",
            BenchmarkAnswerStatus.Ok,
            firstQualityScore: 25,
            firstCriticalError: true,
            firstComment: "Misstates the gift formula.",
            blind: true,
            triggerLabel: "LowQualityScore");

        Assert.Contains("--- SECOND OPINION ---", prompt);
        Assert.Contains("Another assessor has already graded this answer independently", prompt);
        Assert.Contains("The harness selected this answer for a second reading because", prompt);
        Assert.DoesNotContain("Quality score: 25 / 100", prompt);
        Assert.DoesNotContain("Critical error: yes", prompt);
        Assert.DoesNotContain("Misstates the gift formula.", prompt);
        Assert.DoesNotContain("severe enough", prompt);
        Assert.Contains("Rubric point one.", prompt);
    }

    [Fact]
    public void SecondOpinionPrompt_AnchoredMode_CarriesTheFirstVerdictWithoutAskingForAgreement()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildSecondOpinionPrompt(
            "Suite",
            17,
            "How do sacrifice gifts work?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer text.",
            BenchmarkAnswerStatus.Ok,
            firstQualityScore: 25,
            firstCriticalError: true,
            firstComment: "Misstates the gift formula.",
            blind: false,
            triggerLabel: "CriticalError");

        Assert.Contains("SECOND OPINION", prompt);
        Assert.Contains("Quality score: 25 / 100", prompt);
        Assert.Contains("Critical error: yes", prompt);
        Assert.Contains("Do NOT defer to the first verdict", prompt);
        Assert.DoesNotContain("severe enough", prompt);
        Assert.Contains("Rubric point one.", prompt);
    }

    [Fact]
    public void SecondOpinionPrompt_WithVerifiedClaims_IncludesVerifiedClaimsContext()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildSecondOpinionPrompt(
            "Suite",
            1,
            "Question?",
            BenchmarkDifficulty.Simple,
            "Rubric.",
            "Answer.",
            BenchmarkAnswerStatus.Ok,
            firstQualityScore: 50,
            firstCriticalError: false,
            firstComment: "Comment.",
            claimVerifications: new[] { new BenchmarkClaimVerification(0, "Claim A", BenchmarkClaimVerdict.Supported, "source.c:10", null) });

        Assert.Contains("--- FACT-CHECK VERIFICATION CONTEXT ---", prompt);
        Assert.Contains("Claim: \"Claim A\"", prompt);
        Assert.Contains("source.c:10", prompt);
    }

    [Fact]
    public void BuildEvidenceInformedBody_PointsAtTheBoard_AndCarriesRubricFindingsAndInstruction_ButNotTheFirstVerdict()
    {
        const string quote = "Peacefuls are never displaced.";
        const string basis = "walking into a peaceful never displaces it";
        string body = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
            1,
            "Can you swap places with the peaceful dwarf?",
            BenchmarkDifficulty.Intermediate,
            "- displace_peaceful defaults to on",
            "Yes: walk into it and you swap places.",
            BenchmarkAnswerStatus.Ok,
            new[]
            {
                new BenchmarkClaimVerification(0, basis, BenchmarkClaimVerdict.Refuted, "src/options.c:139", "displace_peaceful defaults TRUE."),
                new BenchmarkClaimVerification(1, "A peaceful dwarf is displaced.", BenchmarkClaimVerdict.Supported, "src/hack.c:2319", "The swap is in domove.")
            },
            criticalErrorQuote: quote,
            outOfRubricBasis: basis,
            boardGivenAbove: true);

        // The board reaches the re-grade as the second system message, as for every grading role.
        Assert.DoesNotContain("--- GAME CONTEXT BOARD", body);
        Assert.Contains(BenchmarkAssessmentPrompt.BoardGivenAboveLine, body);
        Assert.Contains("--- BEGIN RUBRIC ---", body);
        Assert.Contains("- displace_peaceful defaults to on", body);
        Assert.Contains("--- VERIFIER FINDINGS ---", body);
        Assert.Contains($"Finding F0 (the statement your out-of-rubric Accuracy deduction rested on): \"{basis}\"", body);
        Assert.Contains("Finding F1: \"A peaceful dwarf is displaced.\"", body);
        Assert.Contains("Verdict: Refuted", body);
        Assert.Contains("Citation: src/options.c:139", body);
        Assert.Contains("Basis: displace_peaceful defaults TRUE.", body);
        Assert.Contains("Verdict: Supported", body);
        Assert.Contains("Basis: The swap is in domove.", body);
        Assert.Contains(BenchmarkAssessmentPrompt.EvidenceInformedInstruction, body);
        Assert.Contains("\"withdrawn\"", body);

        // Without levels and targets the first verdict is absent; the score and comment never appear.
        Assert.DoesNotContain("Quality score:", body);
        Assert.DoesNotContain("FIRST VERDICT", body);
        Assert.DoesNotContain("Critical error: yes", body);
    }

    [Fact]
    public void BuildEvidenceInformedBody_ListsTheFirstLevelsAndTheTargets_AndAsksForStructuredWithdrawals()
    {
        string body = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
            1,
            "Question?",
            BenchmarkDifficulty.Intermediate,
            "Rubric.",
            "Answer.",
            BenchmarkAnswerStatus.Ok,
            new[]
            {
                new BenchmarkClaimVerification(0, "Quoted sentence.", BenchmarkClaimVerdict.Supported, "src/a.c:1", "True.")
                    { Roles = new[] { BenchmarkClaimRoles.CriticalErrorQuote } },
                new BenchmarkClaimVerification(1, "Charged sentence.", BenchmarkClaimVerdict.Supported, "src/b.c:2", "True.")
                    { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } }
            },
            targets: new[]
            {
                new BenchmarkEvidenceInformedTarget("T1", BenchmarkEvidenceInformedTarget.CriticalErrorKind, BenchmarkClaimRoles.CriticalErrorQuote, "Quoted sentence.", new[] { "F0" }),
                new BenchmarkEvidenceInformedTarget("T2", BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.AccusedQuote, "Charged sentence.", new[] { "F1" })
            },
            originalLevels: (4, 5, 6, 5),
            originalCriticalError: true);

        Assert.Contains("Finding F0 (the sentence you quoted as a critical error): \"Quoted sentence.\"", body);
        Assert.Contains("Finding F1 (a sentence of the answer you charged as false or imprecise): \"Charged sentence.\"", body);
        Assert.Contains("Levels: Accuracy=4/6, Completeness=5/6, Conciseness=6/6, Readability=5/6", body);
        Assert.Contains("Critical error: yes", body);
        Assert.Contains("- T1 (Critical error): \"Quoted sentence.\" — findings that bear on it: F0", body);
        Assert.Contains("- T2 (Accuracy deduction): \"Charged sentence.\" — findings that bear on it: F1", body);
        Assert.Contains("\"withdrawn\": [ { \"targetId\": \"T1\", \"findingIds\": [\"F2\"], \"reason\": \"…\" } ]", body);
        Assert.Contains("Withdraw only a listed target, and only on the strength of the findings listed for it", BenchmarkAssessmentPrompt.EvidenceInformedInstruction);
        Assert.Contains("a true sentence beside bad advice does not clear it", BenchmarkAssessmentPrompt.EvidenceInformedInstruction);
        Assert.DoesNotContain("Quality score:", body);
    }

    [Fact]
    public void BuildEvidenceInformedBody_TheSchemaBlockCarriesWithdrawn_AndNoTrailingFieldProse()
    {
        string body = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
            1, "Question?", BenchmarkDifficulty.Intermediate, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok,
            new[] { new BenchmarkClaimVerification(0, "Charged sentence.", BenchmarkClaimVerdict.Supported, "src/b.c:2", "True.") });
        string nl = Environment.NewLine;

        int schema = body.IndexOf("--- OUTPUT JSON SCHEMA ---", StringComparison.Ordinal);
        int findings = body.IndexOf("--- VERIFIER FINDINGS ---", StringComparison.Ordinal);
        Assert.True(schema >= 0 && schema < findings);
        string schemaBlock = body.Substring(schema, findings - schema);
        Assert.Contains("  \"comment\": \"Brief 1-3 sentence evaluation explaining the ratings and noting any specific flaws.\"," + nl
            + BenchmarkAssessmentPrompt.WithdrawnSchemaField + nl
            + "}" + nl
            + BenchmarkAssessmentPrompt.WithdrawnRequiredSentence, schemaBlock);

        Assert.DoesNotContain("with one more field", body);
        Assert.EndsWith("Output the same JSON schema as above and nothing else." + nl, body);
    }

    [Fact]
    public void PerQuestionBody_TheWithdrawnSchemaChangesOnlyTheSchemaBlock()
    {
        string Body(bool withWithdrawn) => BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            3, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok,
            new[] { "wiki_search" }, 2, false, 0, 45, boardGivenAbove: true, withWithdrawnField: withWithdrawn);

        string standard = Body(false);
        string regrade = Body(true);

        Assert.DoesNotContain("withdrawn", standard);
        string head = standard.Substring(0, standard.LastIndexOf('}')).TrimEnd();
        Assert.StartsWith(head + ",", regrade);
        Assert.Contains(BenchmarkAssessmentPrompt.WithdrawnRequiredSentence, regrade);
    }

    [Fact]
    public void BuildEvidenceInformedBody_ListsEachFindingUnderTheTargetsItBearsOn_AndTheRestAsContextOnly()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "Shared sentence.", BenchmarkClaimVerdict.Supported, "src/a.c:1", "True.")
                { Roles = new[] { BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimRoles.AccusedQuote } },
            new BenchmarkClaimVerification(1, "Own claim of the answer.", BenchmarkClaimVerdict.Refuted, "src/c.c:3", "False.")
                { Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim } }
        };

        string body = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
            1, "Question?", BenchmarkDifficulty.Intermediate, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok,
            verifications,
            targets: new[]
            {
                new BenchmarkEvidenceInformedTarget("T1", BenchmarkEvidenceInformedTarget.CriticalErrorKind, BenchmarkClaimRoles.CriticalErrorQuote, "Shared sentence.", new[] { "F0" }),
                new BenchmarkEvidenceInformedTarget("T2", BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.AccusedQuote, "Shared sentence.", new[] { "F0" })
            },
            originalLevels: (4, 5, 6, 5),
            originalCriticalError: true);

        int deductions = body.IndexOf("--- DEDUCTIONS YOU MAY WITHDRAW ---", StringComparison.Ordinal);
        int t1 = body.IndexOf("- T1 (Critical error)", StringComparison.Ordinal);
        int t2 = body.IndexOf("- T2 (Accuracy deduction)", StringComparison.Ordinal);
        int end = body.IndexOf("--- END DEDUCTIONS YOU MAY WITHDRAW ---", StringComparison.Ordinal);
        Assert.True(deductions < t1 && t1 < t2 && t2 < end);

        // The shared finding sits under both targets, and nowhere else.
        string underT1 = body.Substring(t1, t2 - t1);
        string underT2 = body.Substring(t2, end - t2);
        Assert.Contains("  - Finding F0", underT1);
        Assert.Contains("  - Finding F0", underT2);
        Assert.Contains("Citation: src/a.c:1", underT1);
        Assert.Equal(2, CountOf(body, "- Finding F0"));

        // A finding no target lists is printed once, as context that may not be cited.
        int others = body.IndexOf("Other verifier findings (context only — never cite these in `withdrawn`):", StringComparison.Ordinal);
        int f1 = body.IndexOf("- Finding F1: \"Own claim of the answer.\"", StringComparison.Ordinal);
        Assert.True(others >= 0 && others < f1 && f1 < deductions);
        Assert.Equal(1, CountOf(body, "- Finding F1"));
    }

    [Fact]
    public void BuildEvidenceInformedBody_WithEveryFindingUnderATarget_PrintsNoContextOnlyList()
    {
        string body = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
            1, "Question?", BenchmarkDifficulty.Intermediate, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok,
            new[] { new BenchmarkClaimVerification(0, "Quoted sentence.", BenchmarkClaimVerdict.Supported, "src/a.c:1", "True.") },
            targets: new[]
            {
                new BenchmarkEvidenceInformedTarget("T1", BenchmarkEvidenceInformedTarget.CriticalErrorKind, BenchmarkClaimRoles.CriticalErrorQuote, "Quoted sentence.", new[] { "F0" })
            });

        Assert.DoesNotContain("Other verifier findings", body);
        Assert.Equal(1, CountOf(body, "- Finding F0"));
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [Fact]
    public void Versions_HarnessIs53_ScoringMethodIs14()
    {
        Assert.Equal("53", BenchmarkAssessmentPrompt.HarnessVersion);

        // Harness 53 keeps an upheld charge out of the verification-cleared figures, gives the
        // synthesis each assessor's charged sentences by verdict, and counts upheld charges and
        // either member's contested deductions in the report; the per-question grader prompt and
        // its ACCURACY anchors do not change, so scoring method 14 stays.
        Assert.Equal(14, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void OutputSchema_CarriesNotAttempted_DirectlyAfterTheCriticalErrorQuote()
    {
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);
        string nl = Environment.NewLine;

        Assert.Contains("  \"criticalErrorQuote\": null," + nl + "  \"notAttempted\": false," + nl, body);
        Assert.True(
            body.IndexOf("\"notAttempted\"", StringComparison.Ordinal) < body.IndexOf("\"comment\"", StringComparison.Ordinal));
    }

    [Fact]
    public void PerQuestionPreamble_EndsWithSectionEight_NotAttemptedUncertaintyAndAlternatives()
    {
        string preamble = BenchmarkAssessmentPrompt.BuildPerQuestionPreamble("Suite");
        string nl = Environment.NewLine;

        int section7 = preamble.IndexOf("### 7. UNVERIFIED CLAIMS", StringComparison.Ordinal);
        int section8 = preamble.IndexOf("### 8. NOT ATTEMPTED, UNCERTAINTY AND ALTERNATIVES", StringComparison.Ordinal);
        Assert.True(section7 >= 0 && section7 < section8);
        Assert.DoesNotContain("### 9.", preamble);

        Assert.Contains("- **`notAttempted`.** Set it to true when the answer does not give what the question asks for and says why: it states that it could not find, or could not verify, that information. It may add what it did find, where to look, or behavior it labels as NetHack's. Set it to false for every other answer: one that gives a value, an outcome or a recommendation, however tentatively; a refusal for any other reason; an answer that does not address the question. Grade the four levels exactly as you otherwise would; `notAttempted` changes nothing about them.", preamble);
        Assert.Contains("- **A statement the answer marks as uncertain** (\"I believe…\", \"I could not confirm…\", \"in NetHack this is X, but GnollHack may differ\") is graded for ACCURACY as the claim it actually makes. A true statement about NetHack, or a correct statement that something is uncertain, is not an error. A tentative claim that the rubric or the GAME BOARD contradicts lowers ACCURACY like any other claim. It is not confidently asserted, so it is not a critical error, unless it recommends an action that the rubric's CRITICAL ERROR section names: tentative advice to take a dangerous action is still a critical error.", preamble);
        Assert.EndsWith("- **Alternatives instead of an answer.** When the question asks for a value, an outcome or a decision and the answer offers two or more alternatives without committing to one, that point earns no COMPLETENESS credit, and each alternative counts as a claim for ACCURACY." + nl, preamble);
    }

    [Fact]
    public void PerQuestionPrompt_TakesACriticalErrorOnlyFromTheRubricOrTheBoard()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);

        Assert.Contains("Mark criticalError only for a claim the rubric's ground truth or the GAME BOARD **contradicts**. A claim you believe false from your own knowledge alone is not a critical error: report it in `unverifiedClaims`, quoted verbatim and prefixed `Suspected false: ` as section 1 describes, and the claim verifier checks it against the source. A claim the rubric merely omits belongs in `unverifiedClaims` too (section 7).", prompt);
        Assert.DoesNotContain("your own verified knowledge **contradicts**", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_MethodThirteen_StatesEachCriticalErrorResolution()
    {
        var agreed = Verdict(1, accuracyLevel: 2, accuracyEvidence: "Wrong.");
        agreed.CriticalError = true;
        agreed.CriticalErrorResolution = BenchmarkCriticalErrorResolution.Agreed;
        var upheld = Verdict(2, accuracyLevel: 2, accuracyEvidence: "Wrong.");
        upheld.CriticalErrorResolution = BenchmarkCriticalErrorResolution.UpheldByVerifier;
        var overturned = Verdict(3, accuracyLevel: 5, accuracyEvidence: "Level 5.");
        overturned.CriticalError = true;
        overturned.CriticalErrorResolution = BenchmarkCriticalErrorResolution.OverturnedByVerifier;
        var unresolved = Verdict(4, accuracyLevel: 4, accuracyEvidence: "Imprecise.");
        unresolved.CriticalErrorResolution = BenchmarkCriticalErrorResolution.Unresolved;
        var single = Verdict(5, accuracyLevel: 2, accuracyEvidence: "Wrong.");
        single.CriticalError = true;
        single.CriticalErrorResolution = BenchmarkCriticalErrorResolution.SingleAssessor;
        var none = Verdict(6, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        none.CriticalErrorResolution = BenchmarkCriticalErrorResolution.None;

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt(
            "Suite", new[] { agreed, upheld, overturned, unresolved, single, none });

        Assert.Equal("CRITICAL ERROR: YES (confirmed by both panel members)", CriticalErrorLine(prompt, 1));
        Assert.Equal("CRITICAL ERROR: YES (flagged by one panel member, upheld by the claim verifier)", CriticalErrorLine(prompt, 2));
        Assert.Equal("CRITICAL ERROR: NO (flagged by one panel member, overturned by the claim verifier, not applied)", CriticalErrorLine(prompt, 3));
        Assert.Equal("CRITICAL ERROR: SPLIT (flagged by one panel member, unresolved, averaged)", CriticalErrorLine(prompt, 4));
        Assert.Equal("CRITICAL ERROR: YES", CriticalErrorLine(prompt, 5));
        Assert.Null(CriticalErrorLine(prompt, 6));
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_MarksANotAttemptedAnswer()
    {
        var abstained = Verdict(1, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");
        abstained.NotAttempted = true;
        var answered = Verdict(2, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { abstained, answered });

        Assert.Contains("NOT ATTEMPTED: yes", QuestionBlock(prompt, 1));
        Assert.DoesNotContain("NOT ATTEMPTED", QuestionBlock(prompt, 2));
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_BeforeMethodThirteen_PrintsTheCriticalErrorFlagAlone()
    {
        // A summary of a method-12 run carries no resolution and no not-attempted outcome.
        var flagged = Verdict(1, accuracyLevel: 2, accuracyEvidence: "Wrong.");
        flagged.CriticalError = true;
        var clean = Verdict(2, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { flagged, clean });

        Assert.Equal("CRITICAL ERROR: YES", CriticalErrorLine(prompt, 1));
        Assert.Null(CriticalErrorLine(prompt, 2));
        Assert.DoesNotContain("NOT ATTEMPTED", prompt);
        Assert.DoesNotContain("panel member", QuestionBlock(prompt, 1));
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(13, true)]
    public void SynthesisCriticalErrorResolution_IsTheAnswersOwn_FromMethodThirteen(int method, bool expected)
    {
        var run = new BenchmarkRun { ScoringMethodVersion = method };
        var answer = new BenchmarkRunAnswer { CriticalErrorResolution = BenchmarkCriticalErrorResolution.UpheldByVerifier };

        BenchmarkCriticalErrorResolution? want = expected ? BenchmarkCriticalErrorResolution.UpheldByVerifier : null;

        Assert.Equal(want, BenchmarkAssessmentPrompt.SynthesisCriticalErrorResolution(run, answer));
    }

    [Theory]
    [InlineData(13, 5, true, null, true)]
    [InlineData(12, 5, true, null, false)]
    [InlineData(13, 5, false, null, false)]
    [InlineData(13, 2, true, null, false)]
    [InlineData(13, 5, true, BenchmarkCriticalErrorResolution.SingleAssessor, false)]
    [InlineData(13, 5, true, BenchmarkCriticalErrorResolution.None, true)]
    public void IsNotAttemptedOutcome_SingleAssessorRun(
        int method, int accuracy, bool notAttempted, BenchmarkCriticalErrorResolution? resolution, bool expected)
    {
        var run = new BenchmarkRun { ScoringMethodVersion = method };
        var answer = GradedAnswer(accuracy);
        answer.NotAttempted = notAttempted;
        answer.CriticalErrorResolution = resolution;

        Assert.Equal(expected, BenchmarkAssessmentPrompt.IsNotAttemptedOutcome(run, answer));
    }

    [Theory]
    [InlineData(5, 5, true, true, null, true)]
    [InlineData(5, 5, true, false, null, false)]
    [InlineData(4, 0, true, true, null, false)]
    [InlineData(3, 2, true, true, null, true)]
    [InlineData(5, 5, true, true, BenchmarkCriticalErrorResolution.Agreed, false)]
    [InlineData(5, 5, true, true, BenchmarkCriticalErrorResolution.UpheldByVerifier, false)]
    [InlineData(5, 5, true, true, BenchmarkCriticalErrorResolution.Unresolved, true)]
    public void IsNotAttemptedOutcome_PanelRun_NeedsBothMembers(
        int accuracyA, int accuracyB, bool notAttemptedA, bool notAttemptedB, BenchmarkCriticalErrorResolution? resolution, bool expected)
    {
        var run = new BenchmarkRun { ScoringMethodVersion = 13, CoAssessorModelConfigurationId = 2 };
        var answer = GradedAnswer(accuracyA);
        answer.NotAttempted = notAttemptedA;
        answer.CoAssessmentNotAttempted = notAttemptedB;
        answer.CriticalErrorResolution = resolution;
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentJson = new BenchmarkCoAssessmentRecord
        {
            AccuracyLevel = accuracyB,
            CompletenessLevel = 3,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5
        }.Serialize();

        Assert.Equal(expected, BenchmarkAssessmentPrompt.IsNotAttemptedOutcome(run, answer));
    }

    [Fact]
    public void IsNotAttemptedOutcome_IsFalseForAnUngradedAnswer()
    {
        var single = new BenchmarkRun { ScoringMethodVersion = 13 };
        var ungraded = GradedAnswer(5);
        ungraded.NotAttempted = true;
        ungraded.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
        Assert.False(BenchmarkAssessmentPrompt.IsNotAttemptedOutcome(single, ungraded));

        // A panel answer member B has not scored is not graded either.
        var panel = new BenchmarkRun { ScoringMethodVersion = 13, CoAssessorModelConfigurationId = 2 };
        var memberAOnly = GradedAnswer(5);
        memberAOnly.NotAttempted = true;
        memberAOnly.CoAssessmentNotAttempted = true;
        memberAOnly.CoAssessmentStatus = BenchmarkAssessmentStatus.Pending;
        Assert.False(BenchmarkAssessmentPrompt.IsNotAttemptedOutcome(panel, memberAOnly));
    }

    private static BenchmarkRunAnswer GradedAnswer(int accuracyLevel) => new()
    {
        OrderIndex = 1,
        Status = BenchmarkAnswerStatus.Ok,
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        AccuracyLevel = accuracyLevel,
        CompletenessLevel = 3,
        ConcisenessLevel = 5,
        ReadabilityLevel = 5
    };

    /// <summary>The per-question block of question <paramref name="orderIndex"/> in a synthesis prompt.</summary>
    private static string QuestionBlock(string prompt, int orderIndex)
    {
        int start = prompt.IndexOf($"### Question #{orderIndex} ", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int next = prompt.IndexOf("### Question #", start + 1, StringComparison.Ordinal);
        int end = next >= 0 ? next : prompt.IndexOf("--- OUTPUT JSON SCHEMA ---", start, StringComparison.Ordinal);
        return prompt.Substring(start, end - start);
    }

    /// <summary>The single "CRITICAL ERROR:" line of a question's block, or null when it has none.</summary>
    private static string? CriticalErrorLine(string prompt, int orderIndex)
    {
        var lines = QuestionBlock(prompt, orderIndex)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => l.StartsWith("CRITICAL ERROR:", StringComparison.Ordinal))
            .ToList();
        Assert.True(lines.Count <= 1);
        return lines.SingleOrDefault();
    }

    [Fact]
    public void PerQuestionPrompt_SaysTheSourceLineIsProvenanceNotTheAnswerKey()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);

        Assert.Contains("- **The rubric's SOURCE line records where the rubric's author found its facts; it is not the list of correct citations.** A source location the answer cites that the SOURCE line does not name — another function, another file or another line — is not wrong for that reason and never lowers ACCURACY by itself. When you cannot tell whether such a citation is right, copy its sentence to `unverifiedClaims`; the claim verifier checks it.", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_AnchorsAccuracyLevelsFourToSixOnWhatTheAnswerStates()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);

        Assert.Contains("- Level 4: Accurate in substance, with minor imprecisions a player could act on slightly wrongly: a figure stated loosely enough to mislead, a term applied to the wrong thing, or a rule stated without a condition that decides the outcome in the question's situation.", prompt);
        Assert.Contains("- Level 5: Accurate, with a single trivial imprecision: one statement worded loosely enough to be misread, though nothing a player could act on wrongly.", prompt);
        Assert.Contains("- Level 6: No false or imprecise statement: every claim the answer makes that you can adjudicate is correct as stated.", prompt);
        Assert.Contains("- **ACCURACY grades only what the answer states.** Depth, length, source-level detail and how many mechanics are covered are not ACCURACY criteria. A two-sentence answer in which you find no false or imprecise statement is level 6; what it leaves out is graded under COMPLETENESS. Never withhold an ACCURACY level because the answer lacks precision, nuance, a formula, a figure or a source reference that it did not attempt to give. A claim you cannot adjudicate goes to `unverifiedClaims` (instruction 8): it does not lower the level, and level 6 does not certify it. Every level below 6 must name a statement the answer makes and say what is wrong or imprecise about it.", prompt);
        Assert.Contains("An accuracy deduction must name something the answer **states** that is wrong or imprecise.", prompt);

        // The v10 anchors rewarded source-level depth the concise prompt tells the candidate not to produce.
        Assert.DoesNotContain("Fully accurate; all factual claims align with GnollHack mechanics", prompt);
        Assert.DoesNotContain("demonstrates nuanced understanding of mechanics and interactions", prompt);
        Assert.DoesNotContain("matching C core source code implementation details exactly", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_ATrueStatementIsNeverAnImprecision_DirectlyAfterTheGradesOnlyBullet()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);
        string nl = Environment.NewLine;

        const string bullet = "- **A true statement is never an imprecision.** A statement that is true but less specific than the rubric — \"some\" or \"often\" where the rubric gives a percentage, \"several\" where it lists the items — is graded under COMPLETENESS for what it leaves out, not under ACCURACY. A true condition, exception or qualifier that the rubric does not mention is not an imprecision either; if you believe it false, report it in `unverifiedClaims` (instruction 8). A rule stated without one of its conditions lowers ACCURACY only when that condition decides the outcome in the question's situation, so that a player following the sentence would act wrongly; otherwise the missing condition is a COMPLETENESS omission. Before you lower ACCURACY, quote the statement and say what in it is false or would mislead a player; \"less specific than the rubric\" is not such a reason.";
        Assert.Contains(bullet, prompt);
        Assert.Contains("say what is wrong or imprecise about it." + nl + bullet + nl, prompt);

        // The method-13 anchors excused a missing condition and a loose figure outright.
        Assert.DoesNotContain("a rule stated without a condition that does not apply here", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_CarriesTheCriticalErrorQuoteRuleAndTheMathRenderingRule()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);

        Assert.Contains("- Quote the sentence that commits the error the clause names. When the clause is about advice or an implication, quote the advice, not a true statement beside it.", prompt);
        Assert.Contains("- The answer is displayed in a client that renders Markdown and LaTeX math (`$…$`, `$$…$$`, `\\(…\\)`, `\\[…\\]`). Valid math markup is judged as the player sees it typeset and is not a readability defect. Invalid markup, an unreadable formula, or a formula where a sentence would do, still are.", prompt);
    }

    [Fact]
    public void SynthesisPrompt_NamesASupportedAccusation_ApartFromARefutedBasis()
    {
        var summary = new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = 9,
            QuestionText = "Question 9",
            AccuracyLevel = 4,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 70,
            Status = BenchmarkAnswerStatus.Ok,
            SupportedAccusations = new List<(string Claim, string? Citation)> { ("The Grail heals 1000 hit points.", "src/objects.c:2889") }
        };

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { summary });

        Assert.Contains("A sentence of Q9 the assessor charged as false, \"The Grail heals 1000 hit points.\", was checked by the claim verifier and supported (src/objects.c:2889)", prompt);
        Assert.DoesNotContain("own-knowledge statement", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_EmitsRefutedClaimsAndSecondOpinions()
    {
        var summary = new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = 1,
            QuestionText = "Question 1",
            ExpectedPoints = "Rubric",
            AccuracyLevel = 4,
            CompletenessLevel = 4,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 70,
            SpeedScore = 80,
            DurationMs = 2000,
            AssessedDifficulty = 30,
            CriticalError = false,
            ReviewComment = "Good answer.",
            Status = BenchmarkAnswerStatus.Ok,
            RefutedClaims = new List<(string Claim, string? Citation, string? Basis)>
            {
                ("Gnolls have infravision", "src/role.c:10", "Code shows gnolls do not have infravision")
            },
            SecondOpinionQualityScore = 40,
            SecondOpinionCriticalError = true
        };

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { summary });

        Assert.Contains("Refuted claim: \"Gnolls have infravision\" — refuted against src/role.c:10. Basis: Code shows gnolls do not have infravision", prompt);
        Assert.Contains("Second opinion (advisory, did not score): 40/100, critical error yes", prompt);
        Assert.Contains("must NOT be described as free of factual errors", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_MarksTheQuestionsWhoseAccuracyEvidenceNamesADefect()
    {
        // Q14's shape on the 2026-09-06 run: Accuracy 5/6, no refuted claim, no critical error —
        // so nothing in the scoring method v7 guardrail applied — with evidence naming a concrete
        // false assertion. The synthesis then reported the run as free of factual errors.
        var q14 = Verdict(14, accuracyLevel: 5,
            accuracyEvidence: "The answer lists Level as 40 and Hit dice as 25; in the monster definition LVL(25, 16, -10, 15, 10, -20), his level/HD is 25, while 40 is his monster difficulty.");
        var clean = Verdict(15, accuracyLevel: 6, accuracyEvidence: "Matches rubric.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { q14, clean });

        // CRITICAL INSTRUCTION 2 quotes the marker verbatim so the two halves refer to each other,
        // so the per-question search must start at the first question heading. Searching the whole
        // prompt would find the instruction's own quotation and report a marker on no question.
        string blocks = prompt.Substring(prompt.IndexOf("### Question #", StringComparison.Ordinal));

        int markerIndex = blocks.IndexOf("Accuracy defect recorded: yes", StringComparison.Ordinal);
        Assert.True(markerIndex > 0, "The affected question block must carry the marker.");

        // Exactly one block carries it, and it is Q14's: the marker must sit after Q14's heading
        // and before Q15's, or the constraint has been attached to the wrong question.
        Assert.Equal(markerIndex, blocks.LastIndexOf("Accuracy defect recorded: yes", StringComparison.Ordinal));
        Assert.InRange(
            markerIndex,
            blocks.IndexOf("### Question #14", StringComparison.Ordinal),
            blocks.IndexOf("### Question #15", StringComparison.Ordinal));

        // And the header constraint names the marker, so the two halves refer to each other.
        Assert.Contains("Accuracy defect recorded: yes` below", prompt);
        Assert.Contains("weaknesses", prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_DoesNotMarkALevelFiveVerdictWhoseEvidenceIsBoilerplate()
    {
        // The exclusion that keeps the marker from appearing on every answer of a strong run.
        var boilerplate = Verdict(3, accuracyLevel: 5, accuracyEvidence: "Matches rubric.");
        var aligns = Verdict(4, accuracyLevel: 5, accuracyEvidence: "Aligns with rubric.");
        var none = Verdict(5, accuracyLevel: 5, accuracyEvidence: null);

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt(
            "Suite", new[] { boilerplate, aligns, none });

        // Scoped past the header for the same reason as the test above: CRITICAL INSTRUCTION 2
        // always quotes the marker, whether or not any question carries it.
        string blocks = prompt.Substring(prompt.IndexOf("### Question #", StringComparison.Ordinal));

        Assert.DoesNotContain("Accuracy defect recorded: yes", blocks);
    }

    [Fact]
    public void PerQuestionPrompt_StatesTheCompletenessScopeRuleAndTheOutOfScopeMarker()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            12,
            "What are the Exceptional and Elite quality modifiers for body armor?",
            BenchmarkDifficulty.Intermediate,
            "Exceptional: -6 AC. Elite: -9 AC. Celestial/Primordial/Infernal: -12 AC/+4 MC.",
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        // Scoring method v8 states the precedence as a grading rule, not as advice: Q12 was docked
        // on the 2026-09-06 run for rubric points its own question did not ask for.
        Assert.Contains("The question defines the scope.", prompt);
        Assert.Contains("must **not** lower the COMPLETENESS level", prompt);
        Assert.Contains(BenchmarkAssessmentParser.OutOfScopeCompletenessMarker, prompt);
        Assert.Contains("Record it and do not deduct for it", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_StatesTheReadabilityFormRuleAndTheFormMarker()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "Which weapon should a Ranger carry at experience level 5?",
            BenchmarkDifficulty.Intermediate,
            "**FORM** Present the options as a comparison table.",
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        // Scoring method v9. The level anchors are the whole dimension; a rubric FORM criterion is
        // a presentation preference, and this suite grades a chat prompt that asks for prose.
        Assert.Contains("A rubric FORM or format suggestion is not a READABILITY criterion.", prompt);
        Assert.Contains("The level anchors above are the whole of this dimension.", prompt);
        Assert.Contains(BenchmarkAssessmentParser.FormOnlyReadabilityMarker, prompt);
        Assert.Contains("`readabilityEvidence`", prompt);
        Assert.Contains("\"readabilityEvidence\": null", prompt);
    }

    [Fact]
    public void PerQuestionPrompt_SaysAnOutOfRubricClaimDoesNotLowerTheAccuracyLevel()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "What does Yeenaghu's gaze do?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        Assert.Contains(
            "A claim outside the rubric does not lower the ACCURACY level either — do not withhold level 5 or 6 because the answer states something the rubric does not cover. Award an ACCURACY level below 6 only for a named statement in the answer that is wrong or imprecise.",
            prompt);
    }

    [Fact]
    public void PerQuestionPrompt_GradesAccuracyOnTheRubricAndBoardOnly_AndRoutesASuspicionToUnverifiedClaims()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "How long is the prayer timeout?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        // Scoring method 12: an own-knowledge suspicion does not lower the level; it is reported with
        // the prefix the harness strips before the claim verifier sees the sentence.
        Assert.Contains(
            "- **ACCURACY is graded against the rubric and the GAME BOARD only.** A statement you believe false from your own knowledge, which neither the rubric nor the board settles, **does not lower the level**. Report it instead as an entry of `unverifiedClaims`, quoted verbatim from the answer and prefixed `Suspected false: `, with your reason after an em dash (section 7).",
            prompt);
        Assert.Contains(
            "- A deduction that does not come from the rubric or the GAME BOARD is not made. A statement you believe false from your own knowledge goes to `unverifiedClaims` prefixed `Suspected false: ` — the harness sends the quoted sentence to the claim verifier — and leaves the ACCURACY level where the rubric and the board put it.",
            prompt);
        Assert.Contains(
            "A sentence you believe false from your own knowledge, which neither the rubric nor the board settles, is also an entry here, written `Suspected false: <the sentence, verbatim from the answer> — <your reason>`.",
            prompt);

        // The method-11 instruction to mark an own-knowledge deduction is gone; the constant stays so
        // stored method-11 evidence still parses and renders.
        Assert.DoesNotContain("the evidence sentence MUST begin with", prompt);
        Assert.DoesNotContain(BenchmarkAssessmentParser.OutOfRubricAccuracyMarker, prompt);
        Assert.Equal("Not in rubric:", BenchmarkAssessmentParser.OutOfRubricAccuracyMarker);
        Assert.Equal("Suspected false: ", BenchmarkSuspectedFalseClaim.Prefix);
    }

    [Fact]
    public void OutOfRubricAccuracyDeductionDescription_SaysTheInstructionWasNotFollowed_FromMethodTwelve()
    {
        string method11 = BenchmarkVerdictConsistency.OutOfRubricAccuracyDeductionDescription(11);
        string method12 = BenchmarkVerdictConsistency.OutOfRubricAccuracyDeductionDescription(12);

        Assert.StartsWith("Accuracy deductions whose basis is the assessor's own knowledge", method11);
        Assert.DoesNotContain("not followed", method11);
        Assert.Contains("The instruction was not followed.", method12);
        Assert.Contains("Suspected false:", method12);
    }

    [Fact]
    public void ASuspectedFalseEntry_SurvivesTheParserVerbatim_AndReachesTheVerifierWithoutPrefixOrReason()
    {
        const string answer = "Monks are vegan in GnollHack. Fortune cookies are vegan food — eat them freely.";
        const string entry = "Suspected false: Fortune cookies are vegan food — eat them freely. — fortune cookies are vegetarian, not vegan.";
        string raw = "{\"accuracyLevel\":6,\"completenessLevel\":5,\"concisenessLevel\":5,\"readabilityLevel\":5,"
            + "\"criticalError\":false,\"criticalErrorQuote\":null,"
            + "\"unverifiedClaims\":[" + JsonSerializer.Serialize(entry) + ",\"Suspected false: A sentence the answer never wrote at all. — invented\"],"
            + "\"accuracyEvidence\":\"Matches rubric.\",\"completenessEvidence\":\"Complete.\",\"readabilityEvidence\":null,\"comment\":\"Fine.\"}";

        var parsed = BenchmarkAssessmentParser.ParsePerQuestion(raw, answer);

        Assert.True(parsed.Success);
        var stored = Assert.Single(parsed.Result!.UnverifiedClaims);
        Assert.Equal(entry, stored);
        Assert.Equal(1, parsed.Result.UnverifiedClaimsDropped);

        // The persisted column round-trips the entry as written.
        string json = JsonSerializer.Serialize(parsed.Result.UnverifiedClaims);
        var roundTrip = JsonSerializer.Deserialize<List<string>>(json)!;

        var item = Assert.Single(BenchmarkService.BuildClaimManifest(roundTrip, null, null, null, null, answer));
        Assert.Equal("Fortune cookies are vegan food — eat them freely.", item.Text);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, item.Roles);
        Assert.True(item.SuspectedFalse);
        Assert.Equal("fortune cookies are vegetarian, not vegan.", item.Suspicion);
        Assert.Equal(entry, item.RecordedClaim);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_EmitsTheVerifierSupportedClaims()
    {
        var summary = Verdict(7, accuracyLevel: 5, accuracyEvidence: "Level 5.");
        summary.SupportedClaims = new[]
        {
            "Yeenaghu's gaze inflicts fear on a failed save.",
            "Gnolls have keen smell."
        };

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { summary });

        Assert.Contains(
            "Verifier-supported claims (checked against source/wiki; do not describe any of these as invented, fabricated, unsupported, uncorroborated or inflated): \"Yeenaghu's gaze inflicts fear on a failed save.\"; \"Gnolls have keen smell.\"",
            prompt);

        // And the instruction the line exists to make actionable.
        Assert.Contains(
            "A claim listed as verifier-supported is a fact of the game, whatever the rubric omitted; naming it as embellishment is a grading error, not a finding.",
            prompt);
    }

    [Fact]
    public void BuildFinalSynthesisPrompt_OmitsTheSupportedClaimsLine_WhenThereAreNone()
    {
        var summary = Verdict(7, accuracyLevel: 5, accuracyEvidence: "Level 5.");

        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { summary });

        // Scoped past the header: CRITICAL INSTRUCTION 2 names verifier-supported claims whether or
        // not any question carries one.
        string blocks = prompt.Substring(prompt.IndexOf("### Question #", StringComparison.Ordinal));

        Assert.DoesNotContain("Verifier-supported claims", blocks);
    }

    /// <summary>A verdict summary carrying only the fields these tests turn on.</summary>
    private static BenchmarkPerQuestionVerdictSummary Verdict(
        int orderIndex,
        int accuracyLevel,
        string? accuracyEvidence)
    {
        return new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = orderIndex,
            QuestionText = $"Question {orderIndex}",
            AccuracyLevel = accuracyLevel,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 95,
            SpeedScore = 80,
            AssessedDifficulty = 60,
            CriticalError = false,
            AccuracyEvidence = accuracyEvidence,
            ReviewComment = "Comment.",
            Status = BenchmarkAnswerStatus.Ok
        };
    }
}
