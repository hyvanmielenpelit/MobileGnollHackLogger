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
}
