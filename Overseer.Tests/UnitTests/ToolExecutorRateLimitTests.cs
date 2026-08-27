using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ToolExecutorRateLimitTests
{
    private class TestServerToolHandler : IToolHandler
    {
        public string ToolName => "test_tool";
        public string Description { get; set; } = "Test server tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(5, cancellationToken);
            return new ToolResult { Success = true, Content = "Test result" };
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

    [Fact]
    public async Task ExecuteAsync_RateLimit_AtomicAcrossConcurrentCalls()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler();
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache);

        const int maxCalls = 10;
        var context = new ToolExecutionContext
        {
            SessionId = 42,
            MaxCallsPerSession = maxCalls
        };

        // Fire 30 concurrent calls
        var tasks = Enumerable.Range(0, 30).Select(_ =>
            executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, context, CancellationToken.None)
        ).ToList();

        var results = await Task.WhenAll(tasks);

        int successes = results.Count(r => r.Success);
        int rateLimited = results.Count(r => !r.Success && r.ErrorMessage == "Maximum tool calls per session exceeded.");

        // Count starts at 0. When count <= maxCalls (0 through 10 = 11 calls), call proceeds.
        Assert.Equal(maxCalls + 1, successes);
        Assert.Equal(30 - (maxCalls + 1), rateLimited);
    }

    [Fact]
    public async Task ExecuteAsync_SessionIsolation_CountsAreIsolated()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler();
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache);

        const int maxCalls = 5;
        var context1 = new ToolExecutionContext { SessionId = 101, MaxCallsPerSession = maxCalls };
        var context2 = new ToolExecutionContext { SessionId = 202, MaxCallsPerSession = maxCalls };

        var tasks1 = Enumerable.Range(0, 15).Select(_ =>
            executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, context1, CancellationToken.None)
        );
        var tasks2 = Enumerable.Range(0, 15).Select(_ =>
            executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, context2, CancellationToken.None)
        );

        var allResults = await Task.WhenAll(tasks1.Concat(tasks2));
        var results1 = allResults.Take(15).ToList();
        var results2 = allResults.Skip(15).ToList();

        Assert.Equal(maxCalls + 1, results1.Count(r => r.Success));
        Assert.Equal(maxCalls + 1, results2.Count(r => r.Success));
    }
}
