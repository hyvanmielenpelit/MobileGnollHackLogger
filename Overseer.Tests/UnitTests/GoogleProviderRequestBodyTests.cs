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

    // ---------------------------------------------------------------------------------------
    // Prefix stability. Gemini's implicit cache keys on a request prefix, so everything that is
    // identical from turn to turn has to be serialized before the turn list. Run 22 read a
    // 21.1 % cache-read share against 90 % on Anthropic and OpenAI, with `contents` leading the
    // body and nothing stable in front of it.
    // ---------------------------------------------------------------------------------------

    private static ToolsForRequest TwoTools() => new()
    {
        FunctionDeclarations = new List<object>
        {
            new { name = "get_wiki_article", description = "Fetch an article.", parameters = new { type = "object" } },
            new { name = "search_wiki", description = "Search the wiki.", parameters = new { type = "object" } }
        }
    };

    private static string SerializeTurn(GoogleProvider provider, SegmentedPrompt? segmented, string lastUserTurn)
    {
        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "system", content = segmented?.FullPrompt ?? "system text" },
            new { role = "user", content = "first question" },
            new { role = "assistant", content = "first answer" },
            new { role = "user", content = lastUserTurn }
        });

        var body = provider.BuildChatRequestBody(
            modelId: "gemini-3.7-flash",
            messageHistory: prepared,
            maxOutputTokens: 4096,
            thinkingLevel: "high",
            requestTools: TwoTools(),
            segmentedPrompt: segmented,
            promptCacheKey: "session-abc");

        return JsonSerializer.Serialize(body);
    }

    private static string CommonPrefix(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return a.Substring(0, i);
    }

    [Fact]
    public void BuildChatRequestBody_TwoTurnsSharingAPrompt_ShareEverythingBeforeContents()
    {
        var provider = CreateProvider();
        var segmented = new SegmentedPrompt("FROZEN RULES. ", "SESSION CONTEXT. ", "VOLATILE SUFFIX.");

        string prefix = CommonPrefix(
            SerializeTurn(provider, segmented, "what is a gnoll"),
            SerializeTurn(provider, segmented, "what is a lich"));

        // The whole of systemInstruction and the whole of the tool declarations, not a fragment
        // of either: a prefix that stops mid-object caches nothing.
        Assert.Contains("\"systemInstruction\"", prefix);
        Assert.Contains("VOLATILE SUFFIX.", prefix);
        Assert.Contains("\"tools\"", prefix);
        Assert.Contains("get_wiki_article", prefix);
        Assert.Contains("search_wiki", prefix);
        Assert.Contains("\"toolConfig\"", prefix);

        // And it runs all the way to the turn list, which is where the two bodies first differ.
        Assert.EndsWith("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"first question\"}]},{\"role\":\"model\",\"parts\":[{\"text\":\"first answer\"}]},{\"role\":\"user\",\"parts\":[{\"text\":\"what is a ", prefix);
    }

    [Fact]
    public void BuildChatRequestBody_SegmentedPrompt_EmitsTheSegmentsInStabilityOrder()
    {
        var provider = CreateProvider();
        var segmented = new SegmentedPrompt("FROZEN RULES. ", "SESSION CONTEXT. ", "VOLATILE SUFFIX.");

        string json = SerializeTurn(provider, segmented, "q");

        Assert.Contains(
            "\"systemInstruction\":{\"parts\":[{\"text\":\"FROZEN RULES. \"},{\"text\":\"SESSION CONTEXT. \"},{\"text\":\"VOLATILE SUFFIX.\"}]}",
            json);

        // Same characters as the unsegmented prompt, split at the two stability boundaries.
        Assert.DoesNotContain(segmented.FullPrompt, json);
    }

    [Fact]
    public void BuildChatRequestBody_WithoutASegmentedPrompt_PassesSystemPartsThrough()
    {
        // The benchmark and sub-agent paths supply no segmented prompt, and must keep today's
        // single-part systemInstruction.
        var provider = CreateProvider();

        string json = SerializeTurn(provider, segmented: null, lastUserTurn: "q");

        Assert.Contains("\"systemInstruction\":{\"parts\":[{\"text\":\"system text\"}]}", json);
    }

    [Fact]
    public void BuildChatRequestBody_HoistedSystemMessages_SitBeforeTheVolatileSuffix()
    {
        // A second system message is context hoisted by the caller, not the prompt the segments
        // already carry. It has to survive, and it has to stay ahead of the volatile tail.
        var provider = CreateProvider();
        var segmented = new SegmentedPrompt("FROZEN. ", "SESSION. ", "VOLATILE.");

        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            new { role = "system", content = segmented.FullPrompt },
            new { role = "system", content = "HOISTED SNAPSHOT." },
            new { role = "user", content = "q" }
        });

        var body = provider.BuildChatRequestBody(
            modelId: "gemini-3.7-flash",
            messageHistory: prepared,
            maxOutputTokens: 4096,
            thinkingLevel: null,
            requestTools: new ToolsForRequest(),
            segmentedPrompt: segmented);

        string json = JsonSerializer.Serialize(body);

        Assert.Contains(
            "\"systemInstruction\":{\"parts\":[{\"text\":\"FROZEN. \"},{\"text\":\"SESSION. \"},{\"text\":\"HOISTED SNAPSHOT.\"},{\"text\":\"VOLATILE.\"}]}",
            json);

        // The segments replace the first system message; they must not be emitted twice.
        Assert.DoesNotContain(segmented.FullPrompt, json);
    }
}
