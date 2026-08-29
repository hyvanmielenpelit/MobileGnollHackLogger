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
}
