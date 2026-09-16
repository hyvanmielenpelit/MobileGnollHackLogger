namespace Overseer.Tests;

using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Covers the two prompt builders that read from a game snapshot:
/// <see cref="BenchmarkGenerationPrompt.BuildPrompt"/> with a <c>replacingQuestionText</c> and
/// <see cref="BenchmarkGenerationPrompt.BuildRubricOnlyPrompt"/>.
/// </summary>
public class BenchmarkGenerationPromptTests
{
    private static BenchmarkGameSnapshot SampleSnapshot() => new()
    {
        Name = "Test Snapshot",
        SanitizedText = "HP 12/60. A mind flayer is adjacent to the east.",
        Sha256 = "deadbeef",
        CaptureMethod = "Manual"
    };

    [Fact]
    public void BuildRubricOnlyPrompt_ContainsTheQuestionTextAndTheVerbatimInstruction()
    {
        string prompt = BenchmarkGenerationPrompt.BuildRubricOnlyPrompt(
            SampleSnapshot(),
            "Operator instructions",
            BenchmarkDifficulty.Simple,
            "What is the most urgent threat this turn?");

        Assert.Contains("What is the most urgent threat this turn?", prompt);
        Assert.Contains(
            "Write the grading rubric for exactly this question. Return the question text verbatim in `questionText`; do not rewrite it.",
            prompt);
    }

    [Fact]
    public void BuildRubricOnlyPrompt_ContainsTheBoardFactsRubricStructure()
    {
        string prompt = BenchmarkGenerationPrompt.BuildRubricOnlyPrompt(
            SampleSnapshot(), "Operator instructions", BenchmarkDifficulty.Advanced, "Question text");

        Assert.Contains("**BOARD FACTS**", prompt);
        Assert.Contains("**REQUIRED**", prompt);
        Assert.Contains("**CRITICAL ERROR**", prompt);
        Assert.Contains("**SCOPE**", prompt);
        Assert.Contains("**SOURCE**", prompt);
    }

    [Fact]
    public void BuildPrompt_WithReplacingQuestionText_ContainsTheReplacedTextAndRequestsExactlyOneQuestion()
    {
        string prompt = BenchmarkGenerationPrompt.BuildPrompt(
            SampleSnapshot(),
            "Operator instructions",
            BenchmarkDifficulty.Intermediate,
            1,
            existingQuestions: null,
            replacingQuestionText: "What potion should I drink right now?");

        Assert.Contains("--- QUESTION BEING REPLACED (WRITE A DIFFERENT ONE) ---", prompt);
        Assert.Contains("What potion should I drink right now?", prompt);
        Assert.Contains(
            "Write exactly one new question that probes a different decision from this one and from every existing question above.",
            prompt);
    }

    [Fact]
    public void BuildRubricOnlyPrompt_ContainsTheUntrustedDataPreamble()
    {
        string prompt = BenchmarkGenerationPrompt.BuildRubricOnlyPrompt(
            SampleSnapshot(), "Operator instructions", BenchmarkDifficulty.Simple, "Question text");

        Assert.Contains("UNTRUSTED REFERENCE DATA", prompt);
    }

    [Fact]
    public void BuildPrompt_WithReplacingQuestionText_ContainsTheUntrustedDataPreamble()
    {
        string prompt = BenchmarkGenerationPrompt.BuildPrompt(
            SampleSnapshot(),
            "Operator instructions",
            BenchmarkDifficulty.Intermediate,
            1,
            existingQuestions: null,
            replacingQuestionText: "What potion should I drink right now?");

        Assert.Contains("UNTRUSTED REFERENCE DATA", prompt);
    }
}
