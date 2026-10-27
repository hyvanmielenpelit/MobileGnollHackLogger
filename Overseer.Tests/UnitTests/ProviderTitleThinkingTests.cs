using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>Title request bodies carry the title model's thinking level, and title parsers skip reasoning output.</summary>
public class ProviderTitleThinkingTests
{
    private const string Identifier = "user-hash-1";

    private static IConfiguration Config(Dictionary<string, string?>? settings = null)
        => new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

    private static JsonElement TitleBody(IAiProvider provider, string modelId, string? thinkingLevel, string? reasoningMode, string? safetyIdentifier = null)
    {
        var body = provider.BuildTitleRequestBody(modelId, "Write a title.", "What is a newt?", 2048, null, safetyIdentifier, thinkingLevel, reasoningMode);
        return JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement;
    }

    [Fact]
    public void OpenAi_WithLevel_SendsReasoningEffortWithoutEncryptedReasoning()
    {
        var root = TitleBody(new OpenAiResponsesProvider(Config()), "gpt-5", "low", null);

        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(root.GetProperty("reasoning").TryGetProperty("summary", out _));
        Assert.False(root.TryGetProperty("include", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    public void OpenAi_WithoutLevel_SendsNoReasoning(string? level)
    {
        var root = TitleBody(new OpenAiResponsesProvider(Config()), "gpt-5", level, null);

        Assert.False(root.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public void OpenAi_WithReasoningModeOnly_SendsTheModeAlone()
    {
        var reasoning = TitleBody(new OpenAiResponsesProvider(Config()), "gpt-5", null, "pro").GetProperty("reasoning");

        Assert.Equal("pro", reasoning.GetProperty("mode").GetString());
        Assert.False(reasoning.TryGetProperty("effort", out _));
    }

    [Fact]
    public void Anthropic_WithLevel_SendsAdaptiveThinkingAndEffort()
    {
        var root = TitleBody(new AnthropicProvider(Config()), "claude-opus-5", "low", null);

        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("low", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public void Anthropic_WithoutLevel_DoesNotApplyTheExplicitDefault()
    {
        var provider = new AnthropicProvider(Config(new Dictionary<string, string?> { ["AnthropicSettings:ExplicitDefaultEffort"] = "high" }));
        var root = TitleBody(provider, "claude-opus-5", null, null);

        Assert.False(root.TryGetProperty("thinking", out _));
        Assert.False(root.TryGetProperty("output_config", out _));
    }

    [Fact]
    public void Google_WithLevel_SendsThinkingConfigBesideMaxOutputTokens()
    {
        var generationConfig = TitleBody(new GoogleProvider(Config()), "gemini-3.7-flash", "low", null).GetProperty("generationConfig");

        Assert.Equal("LOW", generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.Equal(2048, generationConfig.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public void Google_WithoutLevel_SendsNoThinkingConfig()
    {
        var generationConfig = TitleBody(new GoogleProvider(Config()), "gemini-3.7-flash", null, null).GetProperty("generationConfig");

        Assert.False(generationConfig.TryGetProperty("thinkingConfig", out _));
        Assert.Equal(2048, generationConfig.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public void OpenAi_TheLevelOverload_StillCarriesTheSafetyIdentifier()
    {
        var root = TitleBody(new OpenAiResponsesProvider(Config()), "gpt-5", "low", null, Identifier);

        Assert.Equal(Identifier, root.GetProperty("safety_identifier").GetString());
    }

    [Fact]
    public void Anthropic_TheLevelOverload_StillCarriesTheSafetyIdentifier()
    {
        var root = TitleBody(new AnthropicProvider(Config()), "claude-opus-5", "low", null, Identifier);

        Assert.Equal(Identifier, root.GetProperty("metadata").GetProperty("user_id").GetString());
    }

    [Fact]
    public void Anthropic_ParseTitleResponse_SkipsALeadingThinkingBlock()
    {
        using var doc = JsonDocument.Parse("""
            {"content":[{"type":"thinking","thinking":"A newt is an amphibian.","signature":"abc"},{"type":"text","text":"\"Newts in GnollHack\""}]}
            """);

        Assert.Equal("Newts in GnollHack", new AnthropicProvider(Config()).ParseTitleResponse(doc.RootElement));
    }

    [Fact]
    public void Google_ParseTitleResponse_SkipsAThoughtPart()
    {
        using var doc = JsonDocument.Parse("""
            {"candidates":[{"content":{"parts":[{"thought":true,"text":"Thinking about newts."},{"text":"Title"}]}}]}
            """);

        Assert.Equal("Title", new GoogleProvider(Config()).ParseTitleResponse(doc.RootElement));
    }
}
