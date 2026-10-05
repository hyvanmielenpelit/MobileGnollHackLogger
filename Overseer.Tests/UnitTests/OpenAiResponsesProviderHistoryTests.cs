using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class OpenAiResponsesProviderHistoryTests
{
    private static IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder().Build();
    }

    [Fact]
    public void AppendUserTextToHistory_AppendsAUserInputItemAfterTheToolOutputs()
    {
        var provider = new OpenAiResponsesProvider(CreateConfig());
        var history = provider.PrepareMessageHistory(new List<object>
        {
            provider.FormatMessage("user", "Investigate something", null)
        });

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"call_1\",\"name\":\"wiki_search\",\"arguments\":\"{}\"}").RootElement
        };
        provider.AppendAssistantToolCallsToHistory(history, "", toolCalls, null);
        provider.AppendToolResultsToHistory(history, new List<ProviderToolResult>
        {
            new ProviderToolResult { ToolCallId = "call_1", ToolName = "wiki_search", Content = "Found it.", Success = true }
        });
        int countBefore = history.Count;

        provider.AppendUserTextToHistory(history, "Answer now.");

        Assert.Equal(countBefore + 1, history.Count);

        var body = provider.BuildChatRequestBody("gpt-5.5", history, 1024, null, new ToolsForRequest());
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var input = doc.RootElement.GetProperty("input");
        int length = input.GetArrayLength();

        var toolOutput = input[length - 2];
        Assert.Equal("function_call_output", toolOutput.GetProperty("type").GetString());
        Assert.Equal("call_1", toolOutput.GetProperty("call_id").GetString());

        var last = input[length - 1];
        Assert.Equal("user", last.GetProperty("role").GetString());
        var content = last.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("Answer now.", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public void AppendUserTextToHistory_MatchesTheFormatMessageUserShape()
    {
        var provider = new OpenAiResponsesProvider(CreateConfig());
        var appended = new List<object>();

        provider.AppendUserTextToHistory(appended, "Answer now.");

        Assert.Equal(
            JsonSerializer.Serialize(provider.FormatMessage("user", "Answer now.", null)),
            JsonSerializer.Serialize(Assert.Single(appended)));
    }
}
