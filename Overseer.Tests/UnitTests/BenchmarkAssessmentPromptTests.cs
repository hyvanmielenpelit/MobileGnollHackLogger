namespace Overseer.Tests.UnitTests;

using System;
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
    public void ScoringMethodVersion_IsTen()
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
        // Scores are not comparable across any of those boundaries on the answers they touch, and
        // the report prints the version so a mixed comparison is visible rather than silent.
        Assert.Equal(10, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void HarnessVersion_IsTwentyTwo()
    {
        Assert.Equal("22", BenchmarkAssessmentPrompt.HarnessVersion);
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
        Assert.Contains("a claim the rubric merely omits belongs in `unverifiedClaims`", prompt);
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

    [Fact]
    public void Versions_HarnessIs22_ScoringMethodIs10()
    {
        Assert.Equal("22", BenchmarkAssessmentPrompt.HarnessVersion);

        // Harness 21 withholds the quality and speed indexes from a run in which any answer
        // failed at the provider, leaves such an answer ungraded, and carries the provider's own
        // error code out of an OpenAI in-stream failure. No tool guide and no candidate system
        // prompt moves with it, so a 21-stamped run differs from a 20-stamped one on the single
        // instrument key HarnessVersion. The scoring method does not move: the formula is
        // unchanged, only whether a partial run publishes one.
        Assert.Equal(10, BenchmarkAssessmentPrompt.ScoringMethodVersion);
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
