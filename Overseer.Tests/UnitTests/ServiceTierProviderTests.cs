using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ServiceTierProviderTests
{
    private readonly IConfiguration _configuration;

    public ServiceTierProviderTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"DefaultMaxOutputTokens:Anthropic", "8192"}
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public void OpenAiResponsesProvider_SupportedServiceTiers_And_RequestBody()
    {
        var provider = new OpenAiResponsesProvider(_configuration);
        Assert.Equal(new[] { "auto", "default", "flex", "priority", "fast" }, provider.SupportedServiceTiers);

        var requestTools = new ToolsForRequest();
        var history = new List<object> { new { role = "user", content = "hello" } };

        // Test with serviceTier = "priority"
        var body = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", requestTools, "medium", "auto", "priority");
        Assert.True(body.ContainsKey("service_tier"));
        Assert.Equal("priority", body["service_tier"]);

        // Test with serviceTier = "none" (should omit)
        var bodyNone = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", requestTools, "medium", "auto", "none");
        Assert.False(bodyNone.ContainsKey("service_tier"));

        // Test with serviceTier = null (should omit)
        var bodyNull = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", requestTools, "medium", "auto", null);
        Assert.False(bodyNull.ContainsKey("service_tier"));

        // Title request body
        var titleBody = provider.BuildTitleRequestBody("gpt-5", "system", "user", 100, "flex");
        Assert.True(titleBody.ContainsKey("service_tier"));
        Assert.Equal("flex", titleBody["service_tier"]);
    }

    [Fact]
    public void AnthropicProvider_SupportedServiceTiers_And_RequestBody()
    {
        var provider = new AnthropicProvider(_configuration);
        Assert.Equal(new[] { "auto", "standard_only" }, provider.SupportedServiceTiers);

        var requestTools = new ToolsForRequest();
        var history = new List<object> { new { role = "user", content = "hello" } };

        // Test with serviceTier = "auto"
        var body = provider.BuildChatRequestBody(
            "claude-3-7-sonnet", history, 1000, "high", requestTools, null, null, "auto");
        Assert.True(body.ContainsKey("service_tier"));
        Assert.Equal("auto", body["service_tier"]);

        // Test with serviceTier = "none"
        var bodyNone = provider.BuildChatRequestBody(
            "claude-3-7-sonnet", history, 1000, "high", requestTools, null, null, "none");
        Assert.False(bodyNone.ContainsKey("service_tier"));

        // Title request body
        var titleBody = provider.BuildTitleRequestBody("claude-3-7-sonnet", "system", "user", 100, "standard_only");
        Assert.True(titleBody.ContainsKey("service_tier"));
        Assert.Equal("standard_only", titleBody["service_tier"]);
    }

    [Fact]
    public void GoogleProvider_SupportedServiceTiers_And_RequestBody()
    {
        var provider = new GoogleProvider(_configuration);
        Assert.Equal(new[] { "priority", "flex", "standard" }, provider.SupportedServiceTiers);

        var requestTools = new ToolsForRequest();
        var history = new List<object> { new { role = "user", content = "hello" } };

        // Test with serviceTier = "priority"
        var body = provider.BuildChatRequestBody(
            "gemini-2.5-pro", history, 1000, "high", requestTools, null, null, "priority");
        Assert.True(body.ContainsKey("service_tier"));
        Assert.Equal("priority", body["service_tier"]);

        // Test with serviceTier = null
        var bodyNull = provider.BuildChatRequestBody(
            "gemini-2.5-pro", history, 1000, "high", requestTools, null, null, null);
        Assert.False(bodyNull.ContainsKey("service_tier"));

        // Title request body
        var titleBody = provider.BuildTitleRequestBody("gemini-2.5-pro", "system", "user", 100, "standard");
        Assert.True(titleBody.ContainsKey("service_tier"));
        Assert.Equal("standard", titleBody["service_tier"]);
    }

    [Fact]
    public void OpenAiResponsesProvider_ParallelToolCalls_EmittedOnlyWhenToolsPresent()
    {
        var provider = new OpenAiResponsesProvider(_configuration);
        var history = new List<object> { new { role = "user", content = "hello" } };

        // 1. With tools present
        var toolsWithFunctions = new ToolsForRequest
        {
            FunctionDeclarations = new List<object>
            {
                provider.BuildFunctionDeclaration("test_tool", "description", new { type = "object" })
            }
        };

        // parallelToolCalls = true
        var bodyTrue = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", toolsWithFunctions, null, null, null, parallelToolCalls: true);
        Assert.True(bodyTrue.ContainsKey("parallel_tool_calls"));
        Assert.Equal(true, bodyTrue["parallel_tool_calls"]);

        // parallelToolCalls = false
        var bodyFalse = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", toolsWithFunctions, null, null, null, parallelToolCalls: false);
        Assert.True(bodyFalse.ContainsKey("parallel_tool_calls"));
        Assert.Equal(false, bodyFalse["parallel_tool_calls"]);

        // parallelToolCalls = null
        var bodyNull = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", toolsWithFunctions, null, null, null, parallelToolCalls: null);
        Assert.False(bodyNull.ContainsKey("parallel_tool_calls"));

        // 2. Without tools (no function declarations or provider tools)
        var emptyTools = new ToolsForRequest();
        var bodyNoTools = provider.BuildChatRequestBody(
            "gpt-5", history, 1000, "high", emptyTools, null, null, null, parallelToolCalls: true);
        Assert.False(bodyNoTools.ContainsKey("parallel_tool_calls"));
        Assert.False(bodyNoTools.ContainsKey("tools"));
    }

    [Fact]
    public void GoogleProvider_ExtractServiceTierFromHeaders_WithHeader_ReturnsTier()
    {
        var provider = new GoogleProvider(_configuration);
        using var response = new HttpResponseMessage();
        response.Headers.Add("x-gemini-service-tier", "priority");

        var tier = provider.ExtractServiceTierFromHeaders(response);
        Assert.Equal("priority", tier);

        using var responseStandard = new HttpResponseMessage();
        responseStandard.Headers.Add("x-gemini-service-tier", "standard");

        var standardTier = provider.ExtractServiceTierFromHeaders(responseStandard);
        Assert.Equal("standard", standardTier);
    }

    [Fact]
    public void GoogleProvider_ExtractServiceTierFromHeaders_WithoutHeader_ReturnsNull()
    {
        var provider = new GoogleProvider(_configuration);
        using var response = new HttpResponseMessage();

        var tier = provider.ExtractServiceTierFromHeaders(response);
        Assert.Null(tier);
    }

    [Fact]
    public void GoogleProvider_ExtractServiceTierFromBody_MeasuredPayloads()
    {
        var provider = new GoogleProvider(_configuration);

        var priorityJson = "{\"usageMetadata\":{\"promptTokenCount\":8,\"candidatesTokenCount\":1,\"totalTokenCount\":9,\"serviceTier\":\"priority\"}}";
        using var docPriority = JsonDocument.Parse(priorityJson);
        Assert.Equal("priority", provider.ExtractServiceTierFromBody(docPriority.RootElement));

        var standardJson = "{\"usageMetadata\":{\"promptTokenCount\":8,\"candidatesTokenCount\":1,\"totalTokenCount\":9,\"serviceTier\":\"standard\"}}";
        using var docStandard = JsonDocument.Parse(standardJson);
        Assert.Equal("standard", provider.ExtractServiceTierFromBody(docStandard.RootElement));

        var absentJson = "{\"usageMetadata\":{\"promptTokenCount\":8,\"candidatesTokenCount\":1,\"totalTokenCount\":9}}";
        using var docAbsent = JsonDocument.Parse(absentJson);
        Assert.Null(provider.ExtractServiceTierFromBody(docAbsent.RootElement));
    }

    [Fact]
    public void OpenAiResponsesProvider_ExtractServiceTierFromBody_MeasuredPayloads()
    {
        var provider = new OpenAiResponsesProvider(_configuration);

        var defaultJson = "{\"id\":\"resp_1\",\"service_tier\":\"default\",\"usage\":{}}";
        using var docDefault = JsonDocument.Parse(defaultJson);
        Assert.Equal("default", provider.ExtractServiceTierFromBody(docDefault.RootElement));

        var nullJson = "{\"id\":\"resp_2\",\"service_tier\":null,\"usage\":{}}";
        using var docNull = JsonDocument.Parse(nullJson);
        Assert.Null(provider.ExtractServiceTierFromBody(docNull.RootElement));
    }

    [Fact]
    public void AnthropicProvider_ExtractServiceTierFromBody_MeasuredPayloads()
    {
        var provider = new AnthropicProvider(_configuration);

        var priorityJson = "{\"usage\":{\"input_tokens\":10,\"service_tier\":\"priority\"}}";
        using var docPriority = JsonDocument.Parse(priorityJson);
        Assert.Equal("priority", provider.ExtractServiceTierFromBody(docPriority.RootElement));

        var sseMessageStartJson = "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":10,\"service_tier\":\"priority\"}}}";
        using var docSse = JsonDocument.Parse(sseMessageStartJson);
        Assert.Equal("priority", provider.ExtractServiceTierFromBody(docSse.RootElement));
    }

    [Theory]
    [InlineData("priority", "priority")]
    [InlineData("standard", "standard")]
    [InlineData("SERVICE_TIER_PRIORITY", "priority")]
    [InlineData("Priority", "priority")]
    [InlineData("SERVICE_TIER_UNSPECIFIED", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("some_future_tier", "some_future_tier")]
    public void ProviderHelper_NormalizeServiceTier_NormalizesCorrectly(string? input, string? expected)
    {
        var actual = ProviderHelper.NormalizeServiceTier(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IAiProvider_DefaultExtractServiceTierFromHeaders_ReturnsNull()
    {
        IAiProvider provider = new AnthropicProvider(_configuration);
        using var response = new HttpResponseMessage();
        response.Headers.Add("x-gemini-service-tier", "priority");
        response.Headers.Add("openai-service-tier", "priority");

        var tier = provider.ExtractServiceTierFromHeaders(response);
        Assert.Null(tier);
    }
}
