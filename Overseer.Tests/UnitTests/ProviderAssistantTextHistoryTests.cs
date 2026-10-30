using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>The text-only assistant turn of the recovery call, in each provider's wire shape.</summary>
public class ProviderAssistantTextHistoryTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private static IEnumerable<IAiProvider> Providers() => new IAiProvider[]
    {
        new OpenAiResponsesProvider(Config()), new AnthropicProvider(Config()), new GoogleProvider(Config())
    };

    private static JsonElement Item(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void EmptyText_AppendsNothing_OnEveryProvider()
    {
        foreach (var provider in Providers())
        {
            var history = new List<object> { new { role = "user", content = "Hello" } };

            provider.AppendAssistantTextToHistory(history, "", null);
            provider.AppendAssistantTextToHistory(history, "   ", new List<JsonElement>());

            Assert.Single(history);
        }
    }

    [Fact]
    public void Anthropic_AppendsOneTextBlock_AndTheHistoryStillAlternatesAfterTheInstruction()
    {
        var provider = new AnthropicProvider(Config());
        var history = provider.PrepareMessageHistory(new List<object> { new { role = "user", content = "Hello" } });

        provider.AppendAssistantTextToHistory(history, "Still checking.", null);
        provider.AppendUserTextToHistory(history, "Answer now.");

        Assert.Equal(3, history.Count);
        Assert.Equal("assistant", ProviderHelper.GetProperty(history[1], "role")?.ToString());
        Assert.Equal("user", ProviderHelper.GetProperty(history[2], "role")?.ToString());
    }

    [Fact]
    public void Anthropic_WithEmptyText_MergesTheInstructionIntoTheTrailingUserTurn()
    {
        var provider = new AnthropicProvider(Config());
        var history = provider.PrepareMessageHistory(new List<object> { new { role = "user", content = "Hello" } });

        provider.AppendAssistantTextToHistory(history, "", null);
        provider.AppendUserTextToHistory(history, "Answer now.");

        Assert.Single(history);
        Assert.Equal("user", ProviderHelper.GetProperty(history[0], "role")?.ToString());
    }

    [Fact]
    public void Google_AppendsOneModelTextPart()
    {
        var provider = new GoogleProvider(Config());
        var history = new List<object>();

        provider.AppendAssistantTextToHistory(history, "Still checking.", null);

        Assert.Equal("model", ProviderHelper.GetProperty(Assert.Single(history), "role")?.ToString());
    }

    [Fact]
    public void OpenAi_ReplaysTheProviderItems_WhenAMessageIsAmongThem()
    {
        var provider = new OpenAiResponsesProvider(Config());
        var history = new List<object>();
        var items = new List<JsonElement>
        {
            Item("{\"type\":\"reasoning\",\"id\":\"rs_1\"}"),
            Item("{\"type\":\"message\",\"id\":\"msg_1\",\"phase\":\"commentary\"}")
        };

        provider.AppendAssistantTextToHistory(history, "Still checking.", items);

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void OpenAi_DoesNotReplayAReasoningItemAlone()
    {
        var provider = new OpenAiResponsesProvider(Config());
        var history = new List<object>();
        var items = new List<JsonElement> { Item("{\"type\":\"reasoning\",\"id\":\"rs_1\"}") };

        provider.AppendAssistantTextToHistory(history, "", items);

        Assert.Empty(history);
    }

    [Fact]
    public void OpenAi_WithoutItems_AppendsAMessageItem()
    {
        var provider = new OpenAiResponsesProvider(Config());
        var history = new List<object>();

        provider.AppendAssistantTextToHistory(history, "Still checking.", null);

        Assert.Equal("assistant", ProviderHelper.GetProperty(Assert.Single(history), "role")?.ToString());
    }
}
