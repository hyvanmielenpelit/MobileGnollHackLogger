using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The grading delivery probe, against the request bodies the three real providers build from the
/// grading requests the harness builds. Nothing here reaches a network.
/// </summary>
public class BenchmarkGradingRequestProbeTests
{
    private const string BoardText =
        "Dlvl:3  HP:14(14)  Pw:5(5)  AC:7  Xp:2/24  T:512\n"
        + "Inventory:\n"
        + "a - a blessed +1 quarterstaff (weapon in hands)\n"
        + "b - an uncursed hooded cloak (being worn)\n"
        + "c - 3 fortune cookies\n";

    private const string Rubric = "**BOARD FACTS**\n- \"c - 3 fortune cookies\"\n**REQUIRED**\n- Read the cookies.";

    private static IAiProvider ProviderNamed(string name, bool anthropicCacheControl = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "PromptCacheSettings:EnableAnthropicCacheControl", anthropicCacheControl ? "true" : "false" },
                { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
            })
            .Build();

        return name switch
        {
            "Google" => new GoogleProvider(config),
            "Anthropic" => new AnthropicProvider(config),
            _ => new OpenAiResponsesProvider(config)
        };
    }

    private static BenchmarkRun RunWith(bool withBoard)
    {
        var suite = new BenchmarkSuite { Id = 1, Name = "Snapshot Suite" };
        BenchmarkRunBoardSnapshot? board = withBoard
            ? new BenchmarkRunBoardSnapshot { Id = 7, Sha256 = new string('1', 64), SanitizedText = BoardText, CharCount = BoardText.Length }
            : null;

        return new BenchmarkRun
        {
            Id = 54,
            SuiteName = "Snapshot Suite",
            BenchmarkSuite = suite,
            GameSnapshotNameUsed = board != null ? "Tommi2" : null,
            GameSnapshotSha256Used = board != null ? new string('0', 64) : null,
            BoardSnapshotId = board?.Id,
            BoardSnapshot = board
        };
    }

    /// <summary>The assessor's request for one answer, built the way the grading paths build it.</summary>
    private static AgentRunRequest AssessorRequest(string providerName, BenchmarkRun run, string rubric = Rubric)
    {
        string? boardBlock = BenchmarkService.GradingBoardBlock(run);
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            4, "Which of my items is worth reading first?", BenchmarkDifficulty.Simple, rubric,
            "Read the fortune cookies first.", BenchmarkAnswerStatus.Ok, boardGivenAbove: boardBlock != null);
        var (prompt, seed) = BenchmarkService.BuildGradingPrompt(
            BenchmarkService.GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName),
            body,
            boardBlock);

        return new AgentRunRequest
        {
            ProviderName = providerName,
            ModelId = "probe",
            SystemPrompt = prompt.FullPrompt,
            SegmentedPrompt = prompt,
            PromptCacheKey = "benchmark:per_question:probe",
            CacheConversationTail = false,
            SeedHistory = seed
        };
    }

    private static void VerifyAssessor(string providerName, AgentRunRequest request, BenchmarkRun run, bool anthropicCacheControl = true)
        => BenchmarkGradingRequestProbe.Verify(
            ProviderNamed(providerName, anthropicCacheControl),
            request,
            "assessor",
            request.SegmentedPrompt!.FullPrompt,
            BenchmarkService.GradingBoardBlock(run),
            run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            BenchmarkAssessmentPrompt.QuestionBlockMarker,
            questionNumber: 4);

    [Theory]
    [InlineData("Anthropic", true, true)]
    [InlineData("Anthropic", true, false)]
    [InlineData("Anthropic", false, true)]
    [InlineData("Google", true, true)]
    [InlineData("Google", false, true)]
    [InlineData("OpenAI", true, true)]
    [InlineData("OpenAI", false, true)]
    public void Probe_PassesForTheGradingRequestTheHarnessBuilds(string providerName, bool withBoard, bool anthropicCacheControl)
    {
        var run = RunWith(withBoard);

        VerifyAssessor(providerName, AssessorRequest(providerName, run), run, anthropicCacheControl);
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    [InlineData("OpenAI")]
    public void Probe_PassesForTheRepairTurn(string providerName)
    {
        var run = RunWith(withBoard: true);
        var request = AssessorRequest(providerName, run);
        request.SeedHistory.Add(new { role = "assistant", content = "{\"accuracyLevel\":5}" });
        request.SeedHistory.Add(new { role = "user", content = BenchmarkService.WithdrawnRepairMessage });

        VerifyAssessor(providerName, request, run);
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    [InlineData("OpenAI")]
    public void Probe_ARubricQuotingTheWholeBoard_StillPasses(string providerName)
    {
        // BOARD FACTS repeat board lines inside the question by design; only the board's delimited
        // block has to occur once.
        var run = RunWith(withBoard: true);

        VerifyAssessor(providerName, AssessorRequest(providerName, run, Rubric + "\n- \"" + BoardText + "\""), run);
    }

    [Fact]
    public void Probe_OpenAi_ReadsInstructionsBeforeInput_ThoughTheBodySerialisesInputFirst()
    {
        var run = RunWith(withBoard: true);
        var request = AssessorRequest("OpenAI", run);

        string serialized = BenchmarkGradingRequestProbe.SerializeBody(ProviderNamed("OpenAI"), request);
        Assert.True(serialized.IndexOf("\"input\"", StringComparison.Ordinal) < serialized.IndexOf("\"instructions\"", StringComparison.Ordinal));

        string modelOrder = BenchmarkGradingRequestProbe.ModelOrderText(serialized);
        Assert.True(modelOrder.IndexOf("GAME CONTEXT BOARD", StringComparison.Ordinal)
            < modelOrder.IndexOf("QUESTION AND CANDIDATE ANSWER", StringComparison.Ordinal));

        VerifyAssessor("OpenAI", request, run);
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    [InlineData("OpenAI")]
    public void Probe_RejectsARequestWithoutTheBoard(string providerName)
    {
        var run = RunWith(withBoard: true);
        var request = AssessorRequest(providerName, run);
        request.SeedHistory.RemoveAt(1);

        var ex = Assert.Throws<InvalidOperationException>(() => VerifyAssessor(providerName, request, run));

        Assert.Equal(
            $"Harness delivery check failed: the {providerName} assessor request for question 4 did not contain the board.",
            ex.Message);
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    [InlineData("OpenAI")]
    public void Probe_RejectsTheBoardAfterTheQuestion(string providerName)
    {
        // The harness-31 shape: the board inside the question's body, after the question.
        var run = RunWith(withBoard: true);
        string body = BenchmarkAssessmentPrompt.QuestionBlockMarker + "\nQuestion #4\n"
            + BenchmarkService.GradingBoardBlock(run) + "\nRubric.";
        var (prompt, seed) = BenchmarkService.BuildGradingPrompt(
            BenchmarkService.GradingSystemPrompt, BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName), body);
        var request = new AgentRunRequest
        {
            ProviderName = providerName,
            SystemPrompt = prompt.FullPrompt,
            SegmentedPrompt = prompt,
            CacheConversationTail = false,
            SeedHistory = seed
        };

        var ex = Assert.Throws<InvalidOperationException>(() => VerifyAssessor(providerName, request, run));

        Assert.Equal(
            $"Harness delivery check failed: the {providerName} assessor request for question 4 did not carry the instructions, the board and the question in that order.",
            ex.Message);
    }

    [Fact]
    public void Probe_RejectsTheBoardSentTwice()
    {
        var run = RunWith(withBoard: true);
        var request = AssessorRequest("Google", run);
        request.SeedHistory.Insert(2, new { role = "system", content = BenchmarkService.GradingBoardBlock(run)! });

        var ex = Assert.Throws<InvalidOperationException>(() => VerifyAssessor("Google", request, run));

        Assert.Equal(
            "Harness delivery check failed: the Google assessor request for question 4 carried the board 2 times.",
            ex.Message);
    }

    [Fact]
    public void Probe_RejectsARequestWithoutTheInstructions()
    {
        // OpenAI builds `instructions` from the history's system messages; a history whose only
        // system message is the board sends the board as the instructions and the grading text not
        // at all.
        var run = RunWith(withBoard: true);
        var request = AssessorRequest("OpenAI", run);
        request.SeedHistory.RemoveAt(0);

        var ex = Assert.Throws<InvalidOperationException>(() => VerifyAssessor("OpenAI", request, run));

        Assert.Equal(
            "Harness delivery check failed: the OpenAI assessor request for question 4 did not contain the instructions.",
            ex.Message);
    }

    // --- The claim verifier -----------------------------------------------------------------

    private static AgentRunRequest VerifierRequest(string providerName, BenchmarkRun run)
    {
        var snapshot = run.BenchmarkSuite?.GameSnapshot;
        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            run.SuiteName, 4, "Which of my items is worth reading first?", Rubric,
            new List<string> { "The hero carries 3 fortune cookies." },
            new List<string> { "source_code_search" }, 15,
            boardName: snapshot?.Name, boardText: snapshot?.SanitizedText);

        return BenchmarkService.BuildClaimVerificationRequest(
            new SystemAiApiConfiguration { Id = 5, Provider = providerName, ModelId = "probe", DisplayName = "Verifier" },
            "api-key-test", AiEndpointDescriptor.Official, prompt, new List<string> { "source_code_search" },
            maxOutputTokens: 1024, toolIterations: 5, totalModelCalls: 10, toolCallBudget: 15,
            maxResultLength: 2000, runId: run.Id, orderIndex: 4, startedByUserId: "user-1");
    }

    [Theory]
    [InlineData("Anthropic", true)]
    [InlineData("Anthropic", false)]
    [InlineData("Google", true)]
    [InlineData("Google", false)]
    [InlineData("OpenAI", true)]
    [InlineData("OpenAI", false)]
    public void Probe_PassesForTheVerifierRequestTheHarnessBuilds(string providerName, bool withBoard)
    {
        var run = RunWith(withBoard);
        var snapshot = run.BenchmarkSuite?.GameSnapshot;
        var request = VerifierRequest(providerName, run);

        BenchmarkGradingRequestProbe.Verify(
            ProviderNamed(providerName),
            request,
            "claim verifier",
            request.SystemPrompt!,
            BenchmarkClaimVerificationPrompt.BuildBoardBlock(snapshot?.Name, snapshot?.SanitizedText),
            snapshot?.SanitizedText,
            BenchmarkClaimVerificationPrompt.QuestionBlockMarker,
            questionNumber: 4);
    }
}
