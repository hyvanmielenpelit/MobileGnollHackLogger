using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ProviderHistoryReplayTests
{
    private static IConfiguration CreateEmptyConfig()
    {
        return new ConfigurationBuilder().Build();
    }

    [Fact]
    public void OpenAI_ReplayPath_AppendsItemsVerbatim()
    {
        var provider = new OpenAiResponsesProvider();
        var history = new List<object>();

        var reasoningJson = "{\"type\":\"reasoning\",\"encrypted_content\":\"enc_123\"}";
        var callJson = "{\"type\":\"function_call\",\"call_id\":\"call_abc\",\"name\":\"source_code_view\",\"arguments\":\"{}\"}";

        var providerItems = new List<JsonElement>
        {
            JsonDocument.Parse(reasoningJson).RootElement,
            JsonDocument.Parse(callJson).RootElement
        };

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"call_abc\",\"name\":\"source_code_view\",\"arguments\":\"{}\"}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Some thinking text", toolCalls, providerItems);

        Assert.Equal(2, history.Count);
        var json = JsonSerializer.Serialize(history);
        Assert.Contains("enc_123", json);
        Assert.Contains("source_code_view", json);
        Assert.DoesNotContain("Some thinking text", json);
    }

    [Fact]
    public void OpenAI_FallbackPath_EmitsReconstruction()
    {
        var provider = new OpenAiResponsesProvider();
        var history = new List<object>();

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"call_abc\",\"name\":\"source_code_view\",\"arguments\":\"{}\"}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Fallback text", toolCalls, null);

        Assert.Equal(2, history.Count);
        var json = JsonSerializer.Serialize(history);
        Assert.Contains("Fallback text", json);
        Assert.Contains("output_text", json);
        Assert.Contains("function_call", json);
    }

    [Fact]
    public void Anthropic_ReplayPath_AppendsAssistantContentBlockEnvelope()
    {
        var provider = new AnthropicProvider(CreateEmptyConfig());
        var history = new List<object>();

        var thoughtJson = "{\"type\":\"thinking\",\"thinking\":\"Thought 1\",\"signature\":\"sig_xyz\"}";
        var toolJson = "{\"type\":\"tool_use\",\"id\":\"toolu_123\",\"name\":\"get_item_stats\",\"input\":{}}";

        var providerItems = new List<JsonElement>
        {
            JsonDocument.Parse(thoughtJson).RootElement,
            JsonDocument.Parse(toolJson).RootElement
        };

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"toolu_123\",\"name\":\"get_item_stats\",\"arguments\":\"{}\"}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Reconstructed prose", toolCalls, providerItems);

        Assert.Single(history);
        var msg = history[0];
        Assert.Equal("assistant", ProviderHelper.GetProperty(msg, "role")?.ToString());

        var json = JsonSerializer.Serialize(history);
        Assert.Contains("sig_xyz", json);
        Assert.Contains("get_item_stats", json);
        Assert.DoesNotContain("Reconstructed prose", json);
    }

    [Fact]
    public void Anthropic_FallbackPath_EmitsTextAndToolUse()
    {
        var provider = new AnthropicProvider(CreateEmptyConfig());
        var history = new List<object>();

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"toolu_123\",\"name\":\"get_item_stats\",\"arguments\":\"{}\"}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Fallback prose", toolCalls, null);

        Assert.Single(history);
        var json = JsonSerializer.Serialize(history);
        Assert.Contains("Fallback prose", json);
        Assert.Contains("tool_use", json);
    }

    [Fact]
    public void Google_ReplayPath_AppendsModelPartsEnvelope_NoDuplication()
    {
        var provider = new GoogleProvider(CreateEmptyConfig());
        var history = new List<object>();

        var thoughtJson = "{\"thought\":true,\"thoughtSignature\":\"tsig_123\",\"text\":\"Thought text\"}";
        var fcJson = "{\"functionCall\":{\"name\":\"item_lookup\",\"args\":{}}}";

        var providerItems = new List<JsonElement>
        {
            JsonDocument.Parse(thoughtJson).RootElement,
            JsonDocument.Parse(fcJson).RootElement
        };

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"1\",\"name\":\"item_lookup\",\"arguments\":\"{}\",\"raw_part\":" + fcJson + "}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Google text", toolCalls, providerItems);

        Assert.Single(history);
        var msg = history[0];
        Assert.Equal("model", ProviderHelper.GetProperty(msg, "role")?.ToString());

        var json = JsonSerializer.Serialize(history);
        Assert.Contains("tsig_123", json);
        Assert.Contains("item_lookup", json);
        Assert.DoesNotContain("Google text", json);

        // Ensure tool call is not duplicated
        int fcCount = 0;
        int idx = 0;
        while ((idx = json.IndexOf("item_lookup", idx, StringComparison.Ordinal)) != -1)
        {
            fcCount++;
            idx += "item_lookup".Length;
        }
        Assert.Equal(1, fcCount);
    }

    [Fact]
    public void Google_FallbackPath_EmitsReconstructedParts()
    {
        var provider = new GoogleProvider(CreateEmptyConfig());
        var history = new List<object>();

        var toolCalls = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\":\"1\",\"name\":\"item_lookup\",\"arguments\":\"{}\"}").RootElement
        };

        provider.AppendAssistantToolCallsToHistory(history, "Google fallback text", toolCalls, null);

        Assert.Single(history);
        var json = JsonSerializer.Serialize(history);
        Assert.Contains("Google fallback text", json);
        Assert.Contains("item_lookup", json);
    }

    [Fact]
    public void ProviderHelper_GetProperty_HandlesJsonElementCorrectly()
    {
        var doc = JsonDocument.Parse("{\"role\":\"assistant\",\"parts\":[1,2,3],\"count\":42,\"flag\":true,\"empty\":null}");
        var root = doc.RootElement;

        Assert.Equal("assistant", ProviderHelper.GetProperty(root, "role")?.ToString());
        Assert.Equal("42", ProviderHelper.GetProperty(root, "count")?.ToString());
        Assert.Equal(true, ProviderHelper.GetProperty(root, "flag"));
        Assert.Null(ProviderHelper.GetProperty(root, "empty"));
        Assert.Null(ProviderHelper.GetProperty(root, "non_existent"));

        var partsProp = ProviderHelper.GetProperty(root, "parts");
        Assert.NotNull(partsProp);
        Assert.True(partsProp is JsonElement);
    }
}
