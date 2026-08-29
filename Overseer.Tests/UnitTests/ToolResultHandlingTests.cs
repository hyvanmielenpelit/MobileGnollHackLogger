using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ToolResultHandlingTests
{
    private static string ExtractResultContent(ToolResult res)
    {
        return res.Success
            ? (!string.IsNullOrEmpty(res.Content) ? res.Content : "Success")
            : (!string.IsNullOrWhiteSpace(res.ErrorMessage) ? res.ErrorMessage : (!string.IsNullOrWhiteSpace(res.Content) ? res.Content : "Unknown error"));
    }

    [Fact]
    public void ToolResult_DefaultProperties_InitializedCorrectly()
    {
        var result = new ToolResult();
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Content);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExtractResultContent_WhenSuccessWithContent_ReturnsContent()
    {
        var result = new ToolResult { Success = true, Content = "Found 5 items" };
        var extracted = ExtractResultContent(result);
        Assert.Equal("Found 5 items", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenSuccessWithEmptyContent_ReturnsSuccess()
    {
        var result = new ToolResult { Success = true, Content = "" };
        var extracted = ExtractResultContent(result);
        Assert.Equal("Success", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithErrorMessage_ReturnsErrorMessage()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "", // As defaulted in ToolResult
            ErrorMessage = "NetHack Wiki is temporarily unavailable (HTTP 503 Service Unavailable)."
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("NetHack Wiki is temporarily unavailable (HTTP 503 Service Unavailable).", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithContentAndNoErrorMessage_ReturnsContent()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "Execution failed on step 2",
            ErrorMessage = null
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("Execution failed on step 2", extracted);
    }

    [Fact]
    public void ExtractResultContent_WhenFailedWithEmptyContentAndNoErrorMessage_ReturnsUnknownError()
    {
        var result = new ToolResult
        {
            Success = false,
            Content = "",
            ErrorMessage = null
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal("Unknown error", extracted);
    }

    [Theory]
    [InlineData(ToolGuardMessages.WikiIndexingInProgress)]
    [InlineData(ToolGuardMessages.NetHackWikiIndexingInProgress)]
    [InlineData(ToolGuardMessages.KnowledgeBaseIndexingInProgress)]
    [InlineData(ToolGuardMessages.SourceCodeIndexingInProgress)]
    [InlineData(ToolGuardMessages.NetHackSourceCodeIndexingInProgress)]
    public void ToolGuardMessages_AreDirectiveAndInstructive(string message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("initialization in progress", message);
        Assert.Contains("Do not retry this tool in this turn", message);
        Assert.Contains("warming up", message);
    }

    [Fact]
    public void ExtractResultContent_WhenUnindexedToolError_ExtractsDirectiveMessage()
    {
        var result = new ToolResult
        {
            Success = false,
            ErrorMessage = ToolGuardMessages.WikiIndexingInProgress
        };

        var extracted = ExtractResultContent(result);
        Assert.Equal(ToolGuardMessages.WikiIndexingInProgress, extracted);
    }

    private class TestOverrideToolHandler : IToolHandler
    {
        public string ToolName => "test_override_tool";
        public string Description { get; set; } = "Test override tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category { get; set; } = ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public int TimeoutSeconds { get; set; } = 15;
        public int? MaxResultLengthOverride { get; set; }
        public string ResultToReturn { get; set; } = string.Empty;

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = ResultToReturn });
        }
    }

    private class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, JsonElement parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "Client result" });
        }
    }

    private static IConfiguration CreateConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["ToolExecutionLimits:MaxProcessParallelToolCalls"] = "30",
            ["ToolExecutionLimits:MaxProcessExternalLookupCalls"] = "3",
            ["ToolExecutionLimits:MaxBatchResultLength"] = "40000"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxResultLengthOverride_ReturnsUntruncatedContentAboveContextMax()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new TestOverrideToolHandler
        {
            MaxResultLengthOverride = 60000,
            ResultToReturn = new string('A', 60000)
        };
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        var context = new ToolExecutionContext
        {
            SessionId = 1001,
            MaxResultLength = 10000,
            MaxCallsPerSession = 50
        };

        var result = await executor.ExecuteAsync("test_override_tool", JsonDocument.Parse("{}").RootElement, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(60000, result.Content.Length);
        Assert.DoesNotContain("[Result truncated for length]", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithContextMaxLargerThanOverride_HonorsContextMaxFloor()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new TestOverrideToolHandler
        {
            MaxResultLengthOverride = 60000,
            ResultToReturn = new string('B', 100000)
        };
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        var context = new ToolExecutionContext
        {
            SessionId = 1002,
            MaxResultLength = 100000,
            MaxCallsPerSession = 50
        };

        var result = await executor.ExecuteAsync("test_override_tool", JsonDocument.Parse("{}").RootElement, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(100000, result.Content.Length);
        Assert.DoesNotContain("[Result truncated for length]", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOverride_TruncatesAtContextMax()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new TestOverrideToolHandler
        {
            MaxResultLengthOverride = null,
            ResultToReturn = new string('C', 20000)
        };
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        var context = new ToolExecutionContext
        {
            SessionId = 1003,
            MaxResultLength = 10000,
            MaxCallsPerSession = 50
        };

        var result = await executor.ExecuteAsync("test_override_tool", JsonDocument.Parse("{}").RootElement, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.StartsWith(new string('C', 10000), result.Content);
        Assert.Contains("[Result truncated for length]", result.Content);
    }

    [Fact]
    public void GetEffectiveMaxResultLength_CalculatesCorrectCap()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new TestOverrideToolHandler
        {
            MaxResultLengthOverride = 60000
        };
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        int effectiveOverride = executor.GetEffectiveMaxResultLength("test_override_tool", 10000);
        int effectiveUnknown = executor.GetEffectiveMaxResultLength("no_such_tool", 10000);

        Assert.Equal(60000, effectiveOverride);
        Assert.Equal(10000, effectiveUnknown);
    }

    [Fact]
    public void RefreshSnapshotTool_CapLeavesRoomForTheClientTruncationMarker()
    {
        // The client caps sanitized snapshot text at DefaultMaxSnapshotChars (60000)
        // and then APPENDS this marker, so a truncated snapshot arrives longer than
        // the cap. If the server's cap does not cover both, ToolExecutor cuts off
        // exactly the marker that refresh_snapshot.md tells the model to look for.
        const int clientCap = 60000;
        const string marker = "\n\n[SNAPSHOT TRUNCATED at 60000 characters.]";

        var tool = new RefreshSnapshotTool();

        Assert.NotNull(tool.MaxResultLengthOverride);
        Assert.True(tool.MaxResultLengthOverride >= clientCap + marker.Length,
            $"refresh_snapshot cap {tool.MaxResultLengthOverride} must cover the client's "
            + $"{clientCap}-char cap plus its {marker.Length}-char truncation marker.");
    }
}

