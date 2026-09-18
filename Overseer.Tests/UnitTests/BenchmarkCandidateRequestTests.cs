using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What a candidate's request body actually carries, per provider. The seed history is built the
/// way <see cref="BenchmarkService"/> builds it and serialised through the provider that would
/// send it; nothing here reaches a network.
/// </summary>
public class BenchmarkCandidateRequestTests
{
    private const string SystemPrompt =
        "You are Overseer, an assistant for the roguelike GnollHack. Answer from the game's own source "
        + "code and wiki, name the file you read a fact from, and never invent an item the hero does not "
        + "carry. Keep the answer to the question that was asked.";

    private const string BoardText =
        "Dlvl:3  HP:14(14)  Pw:5(5)  AC:7  Xp:2/24  T:512\n"
        + "Inventory:\n"
        + "a - a blessed +1 quarterstaff (weapon in hands)\n"
        + "b - an uncursed hooded cloak (being worn)\n"
        + "c - 3 fortune cookies\n"
        + "Discoveries:\n"
        + "  orange potion called booze\n";

    private const string QuestionText = "Which of my items is worth reading first, and why?";

    private static IConfiguration Config(bool anthropicCacheControl = true)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "PromptCacheSettings:EnableAnthropicCacheControl", anthropicCacheControl ? "true" : "false" },
                { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
            })
            .Build();
    }

    /// <summary>
    /// The production shape: the bulk of the prompt in the frozen prefix and the rest after it.
    /// The three concatenate to the flat prompt, which is what the run hashes.
    /// </summary>
    private static SegmentedPrompt Segments() =>
        new(SystemPrompt[..160], SystemPrompt[160..], string.Empty);

    /// <summary>A split whose first segment is shorter than the probe's needle.</summary>
    private static SegmentedPrompt ShortFirstSegment() =>
        new(SystemPrompt[..40], SystemPrompt[40..200], SystemPrompt[200..]);

    private static BenchmarkRun RunWith(bool withBoard)
    {
        var suite = new BenchmarkSuite { Id = 1, Name = "Snapshot Suite" };
        if (withBoard)
        {
            suite.GameSnapshot = new BenchmarkGameSnapshot
            {
                Id = 7,
                Name = "Tommi2",
                SanitizedText = BoardText,
                Sha256 = new string('0', 64),
                CaptureMethod = "YamlImport"
            };
        }

        return new BenchmarkRun { Id = 50, BenchmarkSuite = suite };
    }

    private static string Serialize(IAiProvider provider, List<object> seed, SegmentedPrompt? segments)
    {
        var history = provider.PrepareMessageHistory(new List<object>(seed));
        return JsonSerializer.Serialize(provider.BuildChatRequestBody(
            modelId: "probe",
            messageHistory: history,
            maxOutputTokens: null,
            thinkingLevel: null,
            requestTools: new ToolsForRequest(),
            segmentedPrompt: segments));
    }

    /// <summary>A needle escaped by the same serializer that produced the body.</summary>
    private static string Escaped(string text)
    {
        string json = JsonSerializer.Serialize(text);
        return json[1..^1];
    }

    private static int OccurrenceCount(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static IAiProvider ProviderNamed(string name, bool anthropicCacheControl = true) => name switch
    {
        "Google" => new GoogleProvider(Config(anthropicCacheControl)),
        "Anthropic" => new AnthropicProvider(Config(anthropicCacheControl)),
        _ => new OpenAiResponsesProvider(Config(anthropicCacheControl))
    };

    [Theory]
    [InlineData("Google", true, true, true)]
    [InlineData("Google", true, false, true)]
    [InlineData("Google", false, true, true)]
    [InlineData("Google", false, false, true)]
    [InlineData("Anthropic", true, true, true)]
    [InlineData("Anthropic", true, false, true)]
    [InlineData("Anthropic", false, true, true)]
    [InlineData("Anthropic", false, false, true)]
    [InlineData("Anthropic", true, true, false)]
    [InlineData("Anthropic", true, false, false)]
    [InlineData("OpenAI", true, true, true)]
    [InlineData("OpenAI", true, false, true)]
    [InlineData("OpenAI", false, true, true)]
    [InlineData("OpenAI", false, false, true)]
    public void CandidateRequest_CarriesPromptBoardAndQuestion(
        string providerName, bool withBoard, bool segmented, bool anthropicCacheControl)
    {
        var run = RunWith(withBoard);
        var provider = ProviderNamed(providerName, anthropicCacheControl);
        var segments = segmented ? Segments() : null;

        string body = Serialize(
            provider,
            BenchmarkService.BuildCandidateSeedHistory(run, SystemPrompt, QuestionText),
            segments);

        Assert.Contains(Escaped(SystemPrompt[..120]), body, StringComparison.Ordinal);
        Assert.Contains(Escaped(QuestionText), body, StringComparison.Ordinal);

        if (withBoard)
        {
            Assert.Contains(Escaped(BoardText[..120]), body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Google", true)]
    [InlineData("Google", false)]
    [InlineData("Anthropic", true)]
    [InlineData("Anthropic", false)]
    [InlineData("OpenAI", true)]
    [InlineData("OpenAI", false)]
    public void CandidateRequest_CarriesThePromptExactlyOnce(string providerName, bool segmented)
    {
        var run = RunWith(withBoard: true);
        var provider = ProviderNamed(providerName);

        string body = Serialize(
            provider,
            BenchmarkService.BuildCandidateSeedHistory(run, SystemPrompt, QuestionText),
            segmented ? Segments() : null);

        // The prompt's own opening, not a segment boundary: a provider that emitted both the seed's
        // system message and the segments would double the whole prompt and the model would read it
        // twice.
        Assert.Equal(1, OccurrenceCount(body, Escaped(SystemPrompt[..60])));
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    public void Probe_PassesForTheSeedTheHarnessBuilds(string providerName)
    {
        var run = RunWith(withBoard: true);

        BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed(providerName),
            BenchmarkService.BuildCandidateSeedHistory(run, SystemPrompt, QuestionText),
            Segments(),
            SystemPrompt,
            BoardText);
    }

    /// <summary>
    /// The seed shape runs 50 and 51 were executed with: the board as the first system message and
    /// no prompt message at all.
    /// </summary>
    private static List<object> OldSeedHistory() => new()
    {
        new { role = "system", content = ChatService.GameSnapshotPrefix + "\n" + BoardText },
        new { role = "user", content = QuestionText }
    };

    [Theory]
    [InlineData("Google", "board")]
    [InlineData("Anthropic", "board")]
    public void Probe_RejectsTheOldSeedShape_BecauseTheBoardIsDropped(string providerName, string missingPart)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed(providerName), OldSeedHistory(), Segments(), SystemPrompt, BoardText, questionNumber: 4));

        Assert.Equal(
            $"Harness delivery check failed: the {providerName} request for question 4 did not contain the {missingPart}.",
            ex.Message);
    }

    [Fact]
    public void Probe_RejectsTheOldSeedShapeOnOpenAi_BecauseThePromptIsAbsent()
    {
        // This provider reads `instructions` from the history, so the old seed sent the board as
        // the system text and the prompt not at all. The segmented fallback added with the probe
        // only fires for a history with no system message, which the old seed is not.
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed("OpenAI"), OldSeedHistory(), Segments(), SystemPrompt, BoardText, questionNumber: 4));

        Assert.Equal(
            "Harness delivery check failed: the OpenAI request for question 4 did not contain the system prompt.",
            ex.Message);
    }

    [Fact]
    public void Probe_NamesTheRunWhenNoQuestionIsGiven()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed("Google"), OldSeedHistory(), Segments(), SystemPrompt, BoardText));

        Assert.Equal(
            "Harness delivery check failed: the Google request did not contain the board.",
            ex.Message);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    public void Probe_FindsEverySegmentWhenTheFirstIsShorterThanItsNeedle(string providerName)
    {
        // The segments reach Gemini and Anthropic as separate strings, so a needle taken from the
        // flat prompt would span a boundary and be found nowhere.
        var run = RunWith(withBoard: true);

        BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed(providerName),
            BenchmarkService.BuildCandidateSeedHistory(run, SystemPrompt, QuestionText),
            ShortFirstSegment(),
            SystemPrompt,
            BoardText);
    }

    [Fact]
    public void Probe_IgnoresTheBoardForASuiteThatHasNone()
    {
        var run = RunWith(withBoard: false);

        BenchmarkCandidateRequestProbe.Verify(
            ProviderNamed("Google"),
            BenchmarkService.BuildCandidateSeedHistory(run, SystemPrompt, QuestionText),
            Segments(),
            SystemPrompt,
            boardText: null);
    }

    [Fact]
    public void SeedHistory_PutsThePromptFirstAndTheBoardSecond()
    {
        var seed = BenchmarkService.BuildCandidateSeedHistory(RunWith(withBoard: true), SystemPrompt, QuestionText);

        Assert.Equal(3, seed.Count);
        Assert.Equal("system", Role(seed[0]));
        Assert.Equal(SystemPrompt, Content(seed[0]));
        Assert.Equal("system", Role(seed[1]));
        Assert.StartsWith(ChatService.GameSnapshotPrefix, Content(seed[1]));
        Assert.Equal("user", Role(seed[2]));
        Assert.StartsWith(QuestionText, Content(seed[2]));
    }

    [Fact]
    public void SeedHistory_WithoutABoard_IsPromptThenQuestion()
    {
        var seed = BenchmarkService.BuildCandidateSeedHistory(RunWith(withBoard: false), SystemPrompt, QuestionText);

        Assert.Equal(2, seed.Count);
        Assert.Equal(SystemPrompt, Content(seed[0]));
        Assert.Equal("user", Role(seed[1]));
    }

    // --- Grading requests on OpenAI ---------------------------------------------------------

    /// <summary>The seed history the assessor paths build for a run of this file's suite.</summary>
    private static (SegmentedPrompt Prompt, List<object> SeedHistory) GradingSeed(BenchmarkRun run)
    {
        string? boardBlock = BenchmarkService.GradingBoardBlock(run);
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            2, QuestionText, BenchmarkDifficulty.Simple, "**BOARD FACTS**\n- \"c - 3 fortune cookies\"",
            "Read the cookies first.", BenchmarkAnswerStatus.Ok, boardGivenAbove: boardBlock != null);
        return BenchmarkService.BuildGradingPrompt(
            BenchmarkService.GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble("Snapshot Suite"),
            body,
            boardBlock);
    }

    [Fact]
    public void OpenAiGradingRequest_JoinsInstructionsThenBoardIntoInstructions_AndKeepsTheQuestionInInput()
    {
        var (segments, seed) = GradingSeed(RunWith(withBoard: true));
        var provider = ProviderNamed("OpenAI");

        var body = provider.BuildChatRequestBody(
            modelId: "probe",
            messageHistory: provider.PrepareMessageHistory(new List<object>(seed)),
            maxOutputTokens: null,
            thinkingLevel: null,
            requestTools: new ToolsForRequest(),
            segmentedPrompt: segments,
            promptCacheKey: "benchmark:per_question:probe");

        string instructions = Assert.IsType<string>(body["instructions"]);
        Assert.StartsWith(BenchmarkService.GradingSystemPrompt, instructions);
        int board = instructions.IndexOf(BenchmarkAssessmentPrompt.GradingBoardHeading, StringComparison.Ordinal);
        Assert.True(board > 0, "The board must follow the grading instructions inside `instructions`.");
        Assert.Equal(segments.FullPrompt + "\n\n" + BenchmarkAssessmentPrompt.BuildGradingBoardBlock("Tommi2", BoardText), instructions);
        Assert.Contains("c - 3 fortune cookies", instructions);

        string input = JsonSerializer.Serialize(body["input"]);
        Assert.Contains(Escaped(BenchmarkAssessmentPrompt.QuestionBlockMarker), input, StringComparison.Ordinal);
        Assert.DoesNotContain(Escaped(BenchmarkAssessmentPrompt.GradingBoardHeading), input, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenAiGradingRequest_PassesTheGradingProbe_WithAndWithoutABoard(bool withBoard)
    {
        var run = RunWith(withBoard);
        var (segments, seed) = GradingSeed(run);
        var request = new Overseer.Services.Agents.AgentRunRequest
        {
            ProviderName = "OpenAI",
            SystemPrompt = segments.FullPrompt,
            SegmentedPrompt = segments,
            PromptCacheKey = "benchmark:per_question:probe",
            CacheConversationTail = false,
            SeedHistory = seed
        };

        BenchmarkGradingRequestProbe.Verify(
            ProviderNamed("OpenAI"), request, "assessor", segments.FullPrompt,
            BenchmarkService.GradingBoardBlock(run), withBoard ? BoardText : null,
            BenchmarkAssessmentPrompt.QuestionBlockMarker, questionNumber: 2);
    }

    private static string? Role(object message) =>
        message.GetType().GetProperty("role")?.GetValue(message) as string;

    private static string? Content(object message) =>
        message.GetType().GetProperty("content")?.GetValue(message) as string;
}
