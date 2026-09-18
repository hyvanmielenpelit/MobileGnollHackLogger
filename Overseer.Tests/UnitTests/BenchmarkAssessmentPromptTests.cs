namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
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
    public void ScoringMethodVersion_IsEleven()
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
        // below 6 must name a wrong or imprecise statement. It moves every index.
        // Scores are not comparable across any of those boundaries on the answers they touch, and
        // the report prints the version so a mixed comparison is visible rather than silent.
        Assert.Equal(11, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void HarnessVersion_IsThirtyTwo()
    {
        Assert.Equal("32", BenchmarkAssessmentPrompt.HarnessVersion);
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
    public void Versions_HarnessIs32_ScoringMethodIs11()
    {
        Assert.Equal("32", BenchmarkAssessmentPrompt.HarnessVersion);

        // Harness 32 keeps scoring method 11 and changes the instrument around it: every grading
        // role reads the whole board ahead of the question — a second system message for the
        // assessor, second opinion, re-grade, calibration and trial, and directly after the
        // numbered instructions for the claim verifier, which gains instructions 3d and 3e — and a
        // probe checks each serialized request for that order before the call. The run records its
        // rubrics' BOARD FACTS quote check, accused sentences are also read from single quotes and
        // skipped where the evidence approves them, and the re-grade's schema carries `withdrawn`
        // with one repair turn when it is missing. A 32-stamped run differs from a 31-stamped one on
        // HarnessVersion, and on a snapshot suite the board's position makes the two not
        // grade-comparable.
        Assert.Equal(11, BenchmarkAssessmentPrompt.ScoringMethodVersion);
    }

    [Fact]
    public void PerQuestionPrompt_AnchorsAccuracyLevelsFourToSixOnWhatTheAnswerStates()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite", 1, "Question?", BenchmarkDifficulty.Simple, "Rubric.", "Answer.", BenchmarkAnswerStatus.Ok);

        Assert.Contains("- Level 4: Accurate in substance, with minor imprecisions that are not errors a player would act on: a loosely stated figure, an imprecise term, a rule stated without a condition that does not apply here.", prompt);
        Assert.Contains("- Level 5: Accurate, with a single trivial imprecision and nothing a player could act on wrongly.", prompt);
        Assert.Contains("- Level 6: No false or imprecise statement: every claim the answer makes that you can adjudicate is correct as stated.", prompt);
        Assert.Contains("- **ACCURACY grades only what the answer states.** Depth, length, source-level detail and how many mechanics are covered are not ACCURACY criteria. A two-sentence answer in which you find no false or imprecise statement is level 6; what it leaves out is graded under COMPLETENESS. Never withhold an ACCURACY level because the answer lacks precision, nuance, a formula, a figure or a source reference that it did not attempt to give. A claim you cannot adjudicate goes to `unverifiedClaims` (instruction 8): it does not lower the level, and level 6 does not certify it. Every level below 6 must name a statement the answer makes and say what is wrong or imprecise about it.", prompt);
        Assert.Contains("An accuracy deduction must name something the answer **states** that is wrong or imprecise.", prompt);

        // The v10 anchors rewarded source-level depth the concise prompt tells the candidate not to produce.
        Assert.DoesNotContain("Fully accurate; all factual claims align with GnollHack mechanics", prompt);
        Assert.DoesNotContain("demonstrates nuanced understanding of mechanics and interactions", prompt);
        Assert.DoesNotContain("matching C core source code implementation details exactly", prompt);
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
    public void PerQuestionPrompt_RequiresTheOutOfRubricMarkerOnADeductionTheRubricDoesNotCarry()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            "Suite",
            1,
            "How long is the prayer timeout?",
            BenchmarkDifficulty.Advanced,
            "Rubric point one.",
            "Answer.",
            BenchmarkAnswerStatus.Ok);

        // The marker is what routes the basis to the claim verifier, so the instruction names it
        // verbatim from the constant the parser matches on.
        Assert.Contains(
            $"- If a deduction does not come from the rubric, the evidence sentence MUST begin with `{BenchmarkAssessmentParser.OutOfRubricAccuracyMarker}` followed by the basis — the harness sends that basis to the claim verifier, and a deduction written without the marker is never checked.",
            prompt);
        Assert.Equal("Not in rubric:", BenchmarkAssessmentParser.OutOfRubricAccuracyMarker);
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
