namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
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
    public void ScoringMethodVersion_IsSeven()
    {
        // v4 was the artifact scrubbing and speed recalibration. v5 changed what a critical
        // error is — an omission can no longer be one, and the claim must be quoted. v6 changed
        // what an *accuracy deduction* is: a claim the rubric neither states nor contradicts is
        // declared rather than deducted for. v7 enforces evidence discipline: docking a level
        // requires stating what was wrong, and "Matches rubric." may only accompany level 6.
        // Scores are not comparable across any of those boundaries on the answers they touch,
        // and the report prints the version so a mixed comparison is visible rather than silent.
        Assert.Equal(7, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void HarnessVersion_IsEleven()
    {
        Assert.Equal("11", BenchmarkAssessmentPrompt.HarnessVersion);
    }

    [Fact]
    public void PerQuestionPrompt_WithBoard_IncludesGroundTruthSection()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "What is the status of the player?",
            BenchmarkDifficulty.Simple,
            "**REQUIRED** - HP 15/15.\n**BOARD FACTS**\n- HP: 15/15",
            "Player is healthy.",
            BenchmarkAnswerStatus.Ok,
            boardName: "Test Board",
            boardText: "Dlvl:1 $:0 HP:15(15) Pw:10(10) AC:10");

        Assert.Contains("--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---", prompt);
        Assert.Contains("Test Board", prompt);
        Assert.Contains("HP:15(15)", prompt);
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
    }

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
}
