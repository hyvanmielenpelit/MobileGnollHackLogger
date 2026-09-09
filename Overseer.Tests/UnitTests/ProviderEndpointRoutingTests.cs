using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// URL and credential composition for each provider at each endpoint style.
/// </summary>
/// <remarks>
/// The three providers authenticate three different ways on their public APIs, and each of
/// those ways is wrong for at least one custom endpoint — which is why the descriptor carries
/// an auth style rather than only a URL.
/// </remarks>
public class ProviderEndpointRoutingTests
{
    private static IConfiguration EmptyConfig => new ConfigurationBuilder().Build();

    private static OpenAiResponsesProvider OpenAi => new(EmptyConfig);

    private static AnthropicProvider Anthropic => new(EmptyConfig);

    private static GoogleProvider Google => new(EmptyConfig);

    private static AiEndpointDescriptor Custom(
        string baseUrl,
        AiEndpointAuthStyle style = AiEndpointAuthStyle.BearerToken,
        string? apiVersion = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(baseUrl, apiVersion, headers, style);

    private static HttpRequestMessage Configure(IAiProvider provider, string apiKey, AiEndpointDescriptor endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://placeholder.test/");
        provider.ConfigureRequest(request, apiKey, endpoint);
        return request;
    }

    // ── The official endpoints are unchanged ────────────────────────────────────

    [Fact]
    public void OfficialEndpoints_AreExactlyWhatTheyWereBeforeCustomEndpointsExisted()
    {
        var official = AiEndpointDescriptor.Official;

        Assert.Equal("https://api.openai.com/v1/responses", OpenAi.GetChatStreamUrl("gpt-x", "k", official));
        Assert.Equal("https://api.openai.com/v1/responses", OpenAi.GetTitleUrl("gpt-x", "k", official));

        Assert.Equal("https://api.anthropic.com/v1/messages", Anthropic.GetChatStreamUrl("claude-x", "k", official));
        Assert.Equal("https://api.anthropic.com/v1/messages", Anthropic.GetTitleUrl("claude-x", "k", official));

        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-x:streamGenerateContent?alt=sse&key=k",
            Google.GetChatStreamUrl("gemini-x", "k", official));
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent?key=k",
            Google.GetTitleUrl("gemini-x", "k", official));
    }

    [Fact]
    public void OfficialAuth_IsUnchangedPerProvider()
    {
        var official = AiEndpointDescriptor.Official;

        var openAi = Configure(OpenAi, "sk-test", official);
        Assert.Equal("Bearer", openAi.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", openAi.Headers.Authorization.Parameter);

        var anthropic = Configure(Anthropic, "sk-ant-test", official);
        Assert.Equal("sk-ant-test", anthropic.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", anthropic.Headers.GetValues("anthropic-version").Single());
        Assert.Null(anthropic.Headers.Authorization);

        // Google's public API carries the key in the query string, so no header is set.
        var google = Configure(Google, "goog-test", official);
        Assert.Null(google.Headers.Authorization);
        Assert.Empty(google.Headers);
    }

    // ── Azure OpenAI ────────────────────────────────────────────────────────────

    [Fact]
    public void AzureOpenAi_UsesAnApiKeyHeaderAndNotABearerToken()
    {
        /* The first of the plan's two traps. A bearer token against Azure fails with a 401
           that names neither the cause nor the fix. */
        var azure = Custom("https://acme.openai.azure.com", AiEndpointAuthStyle.AzureApiKey, "2026-05-01");

        var request = Configure(OpenAi, "azure-key", azure);

        Assert.Null(request.Headers.Authorization);
        Assert.Equal("azure-key", request.Headers.GetValues("api-key").Single());
    }

    [Fact]
    public void AzureOpenAi_PathIsDeploymentScopedAndCarriesTheApiVersion()
    {
        var azure = Custom("https://acme.openai.azure.com", AiEndpointAuthStyle.AzureApiKey, "2026-05-01");

        Assert.Equal(
            "https://acme.openai.azure.com/openai/v1/responses?api-version=2026-05-01",
            OpenAi.GetChatStreamUrl("gpt-x", "k", azure));
        Assert.Equal(
            "https://acme.openai.azure.com/openai/v1/responses?api-version=2026-05-01",
            OpenAi.GetTitleUrl("gpt-x", "k", azure));
    }

    // ── Google, the query-string key ────────────────────────────────────────────

    [Fact]
    public void Google_NeverPutsTheKeyInTheQueryStringOfACustomEndpoint()
    {
        /* The second trap, and the more dangerous one: a Vertex or gateway endpoint rejects
           ?key=, and a credential in a URL lands in every proxy access log on the way. */
        var gateway = Custom("https://gemini-gateway.example.com");

        string streamUrl = Google.GetChatStreamUrl("gemini-x", "secret-key", gateway);
        string titleUrl = Google.GetTitleUrl("gemini-x", "secret-key", gateway);

        Assert.DoesNotContain("key=", streamUrl);
        Assert.DoesNotContain("secret-key", streamUrl);
        Assert.DoesNotContain("key=", titleUrl);
        Assert.DoesNotContain("secret-key", titleUrl);

        Assert.Equal(
            "https://gemini-gateway.example.com/v1beta/models/gemini-x:streamGenerateContent?alt=sse",
            streamUrl);
        Assert.Equal("https://gemini-gateway.example.com/v1beta/models/gemini-x:generateContent", titleUrl);
    }

    [Fact]
    public void Google_MovesTheCredentialToAHeaderOnACustomEndpoint()
    {
        var gateway = Custom("https://gemini-gateway.example.com");

        var request = Configure(Google, "secret-key", gateway);

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", request.Headers.Authorization.Parameter);
    }

    // ── Anthropic ───────────────────────────────────────────────────────────────

    [Fact]
    public void Anthropic_UsesABearerTokenOnAGatewayButKeepsTheVersionHeader()
    {
        var gateway = Custom("https://claude-gateway.example.com");

        var request = Configure(Anthropic, "gateway-key", gateway);

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.False(request.Headers.Contains("x-api-key"));

        /* anthropic-version selects the wire format of the body, not the credential, so a
           proxy speaking the Messages API needs it exactly as the public API does. */
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public void Anthropic_ComposesTheMessagesPathOnACustomBase()
    {
        var gateway = Custom("https://claude-gateway.example.com");

        Assert.Equal("https://claude-gateway.example.com/v1/messages", Anthropic.GetChatStreamUrl("c", "k", gateway));
        Assert.Equal("https://claude-gateway.example.com/v1/messages", Anthropic.GetTitleUrl("c", "k", gateway));
    }

    // ── Auth style None, for a local server with auth disabled ──────────────────

    [Fact]
    public void AuthStyleNone_SendsNoCredentialAtAll()
    {
        var local = Custom("https://ollama.internal", AiEndpointAuthStyle.None);

        Assert.Null(Configure(OpenAi, "unused", local).Headers.Authorization);
        Assert.False(Configure(OpenAi, "unused", local).Headers.Contains("api-key"));

        var anthropic = Configure(Anthropic, "unused", local);
        Assert.Null(anthropic.Headers.Authorization);
        Assert.False(anthropic.Headers.Contains("x-api-key"));

        Assert.Null(Configure(Google, "unused", local).Headers.Authorization);
    }

    // ── Custom headers ──────────────────────────────────────────────────────────

    [Fact]
    public void AllowlistedCustomHeaders_ReachTheRequestOnEveryProvider()
    {
        var headers = new Dictionary<string, string> { ["X-Gateway-Tenant"] = "acme" };
        var gateway = Custom("https://gateway.example.com", headers: headers);

        Assert.Equal("acme", Configure(OpenAi, "k", gateway).Headers.GetValues("X-Gateway-Tenant").Single());
        Assert.Equal("acme", Configure(Anthropic, "k", gateway).Headers.GetValues("X-Gateway-Tenant").Single());
        Assert.Equal("acme", Configure(Google, "k", gateway).Headers.GetValues("X-Gateway-Tenant").Single());
    }

    // ── Path composition ────────────────────────────────────────────────────────

    [Fact]
    public void ATrailingSlashOnTheBaseUrl_DoesNotDoubleUp()
    {
        var gateway = Custom("https://gateway.example.com/");

        Assert.Equal("https://gateway.example.com/v1/responses", OpenAi.GetChatStreamUrl("m", "k", gateway));
        Assert.Equal("https://gateway.example.com/v1/messages", Anthropic.GetChatStreamUrl("m", "k", gateway));
        Assert.StartsWith("https://gateway.example.com/v1beta/", Google.GetChatStreamUrl("m", "k", gateway));
    }

    [Fact]
    public void ABaseUrlWithAPathPrefix_IsPreserved()
    {
        // A gateway commonly mounts the provider API under a prefix.
        var gateway = Custom("https://gateway.example.com/openai-proxy");

        Assert.Equal("https://gateway.example.com/openai-proxy/v1/responses", OpenAi.GetChatStreamUrl("m", "k", gateway));
    }

    [Fact]
    public void ComposeUrl_ReturnsTheOfficialUrlWhenTheDescriptorIsNotCustom()
    {
        Assert.Equal(
            "https://official.test/path",
            AiEndpointDescriptor.Official.ComposeUrl("https://official.test/path", "/ignored"));
    }
}
