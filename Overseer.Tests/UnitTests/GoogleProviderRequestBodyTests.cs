using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Regression tests for the Gemini request body.
/// <para>
/// The benchmark difficulty assessor failed against every Gemini model with
/// <c>400 INVALID_ARGUMENT: Unknown name "content" at 'contents[0]': Cannot find field</c>,
/// because a provider-neutral seed history — <c>{ role = "user", content = "..." }</c> —
/// reached <see cref="GoogleProvider.BuildChatRequestBody"/> without passing through
/// <see cref="GoogleProvider.PrepareMessageHistory"/> first. A Gemini
/// <c>Content</c> carries <c>parts</c>, not <c>content</c>, and names the assistant role
/// <c>model</c>. These tests pin the wire shape that Google actually accepts.
/// </para>
/// </summary>
public class GoogleProviderRequestBodyTests
{
    private static GoogleProvider CreateProvider()
    {
        var config = new ConfigurationBuilder().Build();
        return new GoogleProvider(config);
    }

    private static string SerializeBody(GoogleProvider provider, List<object> messageHistory)
    {
        var body = provider.BuildChatRequestBody(
            modelId: "gemini-3.7-flash",
            messageHistory: messageHistory,
            maxOutputTokens: 4096,
            thinkingLevel: null,
            requestTools: new ToolsForRequest());

        return JsonSerializer.Serialize(body);
    }

    [Fact]
    public void PrepareMessageHistory_ThenBuildBody_EmitsPartsAndNeverContent()
    {
        var provider = CreateProvider();

        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "user", content = "hi" }
        });

        string json = SerializeBody(provider, prepared);

        Assert.Contains("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"hi\"}]}]", json);

        // The exact field Google rejected. It must not appear anywhere in the body.
        Assert.DoesNotContain("\"content\"", json);
    }

    [Fact]
    public void PrepareMessageHistory_MapsAssistantRoleToModel()
    {
        var provider = CreateProvider();

        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "user", content = "question" },
            new { role = "assistant", content = "answer" }
        });

        string json = SerializeBody(provider, prepared);

        Assert.Contains("\"role\":\"model\",\"parts\":[{\"text\":\"answer\"}]", json);
        Assert.DoesNotContain("\"assistant\"", json);
    }

    [Fact]
    public void PrepareMessageHistory_MovesSystemMessageToSystemInstruction()
    {
        var provider = CreateProvider();

        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "system", content = "You are an objective game mechanics expert." },
            new { role = "user", content = "rate this" }
        });

        string json = SerializeBody(provider, prepared);

        Assert.Contains("\"systemInstruction\":{\"parts\":[{\"text\":\"You are an objective game mechanics expert.\"}]}", json);

        // The system prompt belongs in systemInstruction, not in the turn list.
        Assert.Contains("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"rate this\"}]}]", json);
        Assert.DoesNotContain("\"role\":\"system\"", json);
    }

    [Fact]
    public void PrepareMessageHistory_IsIdempotent()
    {
        // AgentLoopRunner normalizes every seed history, and ChatService and
        // DelegateToSubAgentTool already normalize theirs before handing it over. A second
        // pass therefore has to be a no-op or those two callers would be corrupted.
        var provider = CreateProvider();

        var once = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "system", content = "sys" },
            new { role = "user", content = "hi" },
            new { role = "assistant", content = "there" }
        });

        var twice = provider.PrepareMessageHistory(once);

        Assert.Equal(SerializeBody(provider, once), SerializeBody(provider, twice));
    }

    [Fact]
    public void BuildChatRequestBody_WithRawContentHistory_ProducesTheBodyGoogleRejects()
    {
        // Documents the defect itself, so that a future refactor which removes the
        // normalization step fails a test instead of failing in production.
        var provider = CreateProvider();

        string json = SerializeBody(provider, new List<object>
        {
            new { role = "user", content = "hi" }
        });

        Assert.Contains("\"content\":\"hi\"", json);
        Assert.DoesNotContain("\"parts\"", json);
    }
}
