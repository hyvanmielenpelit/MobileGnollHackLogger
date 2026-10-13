using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>Each provider's reading of a non-streaming title response's usage, on captured-shape bodies.</summary>
public class ProviderTitleUsageTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private static TokenUsageReport? Parse(IAiProvider provider, string json)
    {
        using var doc = JsonDocument.Parse(json);
        return provider.ParseTitleUsage(doc.RootElement);
    }

    [Fact]
    public void Anthropic_CountsCacheReadsAndWritesIntoTheWholePrompt()
    {
        const string json = """
            {
              "id": "msg_01",
              "type": "message",
              "role": "assistant",
              "content": [ { "type": "text", "text": "Sokoban Strategy" } ],
              "stop_reason": "end_turn",
              "usage": {
                "input_tokens": 120,
                "cache_creation_input_tokens": 30,
                "cache_read_input_tokens": 400,
                "output_tokens": 9
              }
            }
            """;

        var usage = Parse(new AnthropicProvider(Config()), json);

        Assert.NotNull(usage);
        Assert.Equal(550, usage!.TotalPromptTokens);
        Assert.Equal(150, usage.UncachedInputTokens);
        Assert.Equal(400, usage.CacheReadTokens);
        Assert.Equal(30, usage.CacheCreationTokens);
        Assert.Equal(9, usage.OutputTokens);
        Assert.Equal(0, usage.ReasoningTokens);
        Assert.Equal(120, usage.BillableUncachedInputTokens);
    }

    [Fact]
    public void Anthropic_WithoutAUsageObject_ReturnsNull()
    {
        Assert.Null(Parse(new AnthropicProvider(Config()), """{ "content": [ { "type": "text", "text": "x" } ] }"""));
    }

    [Fact]
    public void OpenAiResponses_ReadsCachedAndReasoningTokenDetails()
    {
        const string json = """
            {
              "id": "resp_01",
              "object": "response",
              "status": "completed",
              "output": [ { "type": "message", "content": [ { "type": "output_text", "text": "Sokoban Strategy" } ] } ],
              "usage": {
                "input_tokens": 900,
                "input_tokens_details": { "cached_tokens": 512 },
                "output_tokens": 70,
                "output_tokens_details": { "reasoning_tokens": 64 },
                "total_tokens": 970
              }
            }
            """;

        var usage = Parse(new OpenAiResponsesProvider(Config()), json);

        Assert.NotNull(usage);
        Assert.Equal(900, usage!.TotalPromptTokens);
        Assert.Equal(512, usage.CacheReadTokens);
        Assert.Equal(0, usage.CacheCreationTokens);
        Assert.Equal(388, usage.UncachedInputTokens);
        Assert.Equal(70, usage.OutputTokens);
        Assert.Equal(64, usage.ReasoningTokens);
    }

    [Fact]
    public void OpenAiResponses_WithoutDetails_ReadsZeroCachedAndReasoningTokens()
    {
        var usage = Parse(new OpenAiResponsesProvider(Config()), """{ "usage": { "input_tokens": 40, "output_tokens": 5 } }""");

        Assert.NotNull(usage);
        Assert.Equal(40, usage!.TotalPromptTokens);
        Assert.Equal(40, usage.UncachedInputTokens);
        Assert.Equal(0, usage.CacheReadTokens);
        Assert.Equal(0, usage.ReasoningTokens);
    }

    [Fact]
    public void OpenAiResponses_WithoutAUsageObject_ReturnsNull()
    {
        Assert.Null(Parse(new OpenAiResponsesProvider(Config()), """{ "id": "resp_01", "output": [] }"""));
    }

    [Fact]
    public void Google_CountsToolUsePromptAsPrompt_AndThoughtsAsOutput()
    {
        const string json = """
            {
              "candidates": [ { "content": { "parts": [ { "text": "Sokoban Strategy" } ], "role": "model" }, "finishReason": "STOP" } ],
              "usageMetadata": {
                "promptTokenCount": 300,
                "toolUsePromptTokenCount": 20,
                "candidatesTokenCount": 8,
                "thoughtsTokenCount": 40,
                "cachedContentTokenCount": 128,
                "totalTokenCount": 368
              },
              "modelVersion": "gemini-model"
            }
            """;

        var usage = Parse(new GoogleProvider(Config()), json);

        Assert.NotNull(usage);
        Assert.Equal(320, usage!.TotalPromptTokens);
        Assert.Equal(128, usage.CacheReadTokens);
        Assert.Equal(0, usage.CacheCreationTokens);
        Assert.Equal(192, usage.UncachedInputTokens);
        Assert.Equal(48, usage.OutputTokens);
        Assert.Equal(40, usage.ReasoningTokens);
    }

    [Fact]
    public void Google_WithoutAUsageObject_ReturnsNull()
    {
        Assert.Null(Parse(new GoogleProvider(Config()), """{ "candidates": [] }"""));
    }
}
