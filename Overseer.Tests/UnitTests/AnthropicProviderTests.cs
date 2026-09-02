using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AnthropicProviderTests
{
    private static IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder().Build();
    }

    [Fact]
    public void PrepareMessageHistory_PreservesSystemMessages_AndBuildChatRequestBodyHoistsThem()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var rawMessages = new List<object>
        {
            provider.FormatMessage("system", "You are a specialized subagent.", null),
            provider.FormatMessage("user", "Hello, do this task.", null)
        };

        var prepared = provider.PrepareMessageHistory(rawMessages);
        Assert.Equal(2, prepared.Count);

        var requestBody = provider.BuildChatRequestBody("claude-3-7-sonnet-20250219", prepared, 1024, null, new ToolsForRequest());

        Assert.True(requestBody.ContainsKey("system"));
        Assert.Equal("You are a specialized subagent.", requestBody["system"]);

        var messages = requestBody["messages"] as List<object>;
        Assert.NotNull(messages);
        Assert.Single(messages);
        Assert.Equal("user", ProviderHelper.GetProperty(messages[0], "role")?.ToString());
        var content = ProviderHelper.GetProperty(messages[0], "content");
        if (content is IEnumerable<object> blocks)
        {
            var firstBlock = System.Linq.Enumerable.First(blocks);
            Assert.Equal("Hello, do this task.", ProviderHelper.GetProperty(firstBlock, "text")?.ToString());
        }
        else
        {
            Assert.Equal("Hello, do this task.", content?.ToString());
        }
    }

    [Fact]
    public void PrepareMessageHistory_PreservesAlternation_WhenMultipleUserMessagesExist()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var rawMessages = new List<object>
        {
            provider.FormatMessage("system", "System prompt", null),
            provider.FormatMessage("user", "First user prompt", null),
            provider.FormatMessage("user", "Second user prompt", null)
        };

        var prepared = provider.PrepareMessageHistory(rawMessages);
        Assert.Equal(4, prepared.Count); // system, user, inserted assistant filler, user

        var requestBody = provider.BuildChatRequestBody("claude-3-7-sonnet-20250219", prepared, 1024, null, new ToolsForRequest());
        Assert.Equal("System prompt", requestBody["system"]);

        var messages = requestBody["messages"] as List<object>;
        Assert.NotNull(messages);
        Assert.Equal(3, messages.Count);
        Assert.Equal("user", ProviderHelper.GetProperty(messages[0], "role")?.ToString());
        Assert.Equal("assistant", ProviderHelper.GetProperty(messages[1], "role")?.ToString());
        Assert.Equal("user", ProviderHelper.GetProperty(messages[2], "role")?.ToString());
    }

    [Fact]
    public void AppendToolResultsToHistory_RetainsSystemPromptAcrossToolRounds()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var history = new List<object>
        {
            provider.FormatMessage("system", "Subagent instructions", null),
            provider.FormatMessage("user", "Investigate something", null)
        };

        var prepared = provider.PrepareMessageHistory(history);

        // Assistant makes a tool call
        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"tc_123\",\"name\":\"wiki_search\",\"arguments\":\"{\\\"query\\\":\\\"gnoll\\\"}\"}").RootElement
        };
        provider.AppendAssistantToolCallsToHistory(prepared, "Searching wiki...", toolCalls, null);

        // Tool returns results
        var toolResults = new List<ProviderToolResult>
        {
            new ProviderToolResult { ToolCallId = "tc_123", ToolName = "wiki_search", Content = "Gnolls are hyena-like creatures.", Success = true }
        };
        provider.AppendToolResultsToHistory(prepared, toolResults);

        // Verify system prompt survived across the entire tool cycle
        var requestBody = provider.BuildChatRequestBody("claude-3-7-sonnet-20250219", prepared, 1024, null, new ToolsForRequest());
        Assert.True(requestBody.ContainsKey("system"));
        Assert.Equal("Subagent instructions", requestBody["system"]);
    }

    [Fact]
    public async Task ParseStreamAsync_WithMessageStartServiceTier_EmitsServiceTierEvent()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var sse = "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":10,\"service_tier\":\"priority\"}}}\n\n";
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
        };

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }

        var tierEvents = events.Where(e => e.Type == "service_tier").ToList();
        Assert.Single(tierEvents);
        Assert.Equal("priority", tierEvents[0].Data);
    }

    [Fact]
    public void BuildChatRequestBody_WithThinkingLevel_SendsAdaptiveThinkingAndEffort()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var rawMessages = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };
        var prepared = provider.PrepareMessageHistory(rawMessages);

        var requestBody = provider.BuildChatRequestBody("claude-fable-5-1", prepared, 1024, "xhigh", new ToolsForRequest());

        var json = JsonSerializer.Serialize(requestBody);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("thinking", out var thinkingProp));
        Assert.Equal("adaptive", thinkingProp.GetProperty("type").GetString());
        Assert.False(thinkingProp.TryGetProperty("display", out _));

        Assert.True(root.TryGetProperty("output_config", out var outputConfigProp));
        Assert.Equal("xhigh", outputConfigProp.GetProperty("effort").GetString());
    }

    [Fact]
    public void BuildChatRequestBody_WithoutThinkingLevel_SendsConfiguredDefaultEffort()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var rawMessages = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };
        var prepared = provider.PrepareMessageHistory(rawMessages);

        var requestBody = provider.BuildChatRequestBody("claude-opus-4-6", prepared, 1024, null, new ToolsForRequest());

        var json = JsonSerializer.Serialize(requestBody);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("thinking", out var thinkingProp));
        Assert.Equal("adaptive", thinkingProp.GetProperty("type").GetString());
        Assert.False(thinkingProp.TryGetProperty("display", out _));

        Assert.True(root.TryGetProperty("output_config", out var outputConfigProp));
        Assert.Equal("high", outputConfigProp.GetProperty("effort").GetString());
    }

    [Fact]
    public void BuildChatRequestBody_WithExplicitDefaultEffortNone_OmitsThinking()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AnthropicSettings:ExplicitDefaultEffort"] = "none"
            })
            .Build();

        var provider = new AnthropicProvider(config);
        var rawMessages = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };
        var prepared = provider.PrepareMessageHistory(rawMessages);

        var requestBody = provider.BuildChatRequestBody("claude-opus-4-6", prepared, 1024, null, new ToolsForRequest());

        var json = JsonSerializer.Serialize(requestBody);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("thinking", out _));
        Assert.False(root.TryGetProperty("output_config", out _));
    }

    [Fact]
    public void BuildChatRequestBody_WithReasoningSummary_SetsDisplay()
    {
        var provider = new AnthropicProvider(CreateConfig());
        var rawMessages = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };
        var prepared = provider.PrepareMessageHistory(rawMessages);

        // With explicit reasoning summary
        var requestBodyWithDisplay = provider.BuildChatRequestBody("claude-fable-5-1", prepared, 1024, "high", new ToolsForRequest(), reasoningSummary: "summarized");
        var jsonWithDisplay = JsonSerializer.Serialize(requestBodyWithDisplay);
        using var docWithDisplay = JsonDocument.Parse(jsonWithDisplay);
        Assert.True(docWithDisplay.RootElement.TryGetProperty("thinking", out var thinkingProp1));
        Assert.Equal("summarized", thinkingProp1.GetProperty("display").GetString());

        // With "default" reasoning summary
        var requestBodyDefault = provider.BuildChatRequestBody("claude-fable-5-1", prepared, 1024, "high", new ToolsForRequest(), reasoningSummary: "default");
        var jsonDefault = JsonSerializer.Serialize(requestBodyDefault);
        using var docDefault = JsonDocument.Parse(jsonDefault);
        Assert.True(docDefault.RootElement.TryGetProperty("thinking", out var thinkingProp2));
        Assert.False(thinkingProp2.TryGetProperty("display", out _));

        // With null reasoning summary
        var requestBodyNull = provider.BuildChatRequestBody("claude-fable-5-1", prepared, 1024, "high", new ToolsForRequest(), reasoningSummary: null);
        var jsonNull = JsonSerializer.Serialize(requestBodyNull);
        using var docNull = JsonDocument.Parse(jsonNull);
        Assert.True(docNull.RootElement.TryGetProperty("thinking", out var thinkingProp3));
        Assert.False(thinkingProp3.TryGetProperty("display", out _));
    }
}
