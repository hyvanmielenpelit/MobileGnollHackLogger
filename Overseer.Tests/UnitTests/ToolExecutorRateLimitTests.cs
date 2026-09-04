using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        public ToolCategory Category { get; set; } = ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public int TimeoutSeconds { get; set; } = 15;

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

    private static IConfiguration CreateConfiguration(int maxProcess = 30, int maxLookup = 3)
    {
        var dict = new Dictionary<string, string?>
        {
            ["ToolExecutionLimits:MaxProcessParallelToolCalls"] = maxProcess.ToString(),
            ["ToolExecutionLimits:MaxProcessExternalLookupCalls"] = maxLookup.ToString(),
            ["ToolExecutionLimits:MaxBatchResultLength"] = "40000"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
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
            cache,
            CreateConfiguration());

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

        // Count 0 to maxCalls - 1 = maxCalls succeed.
        Assert.Equal(maxCalls, successes);
        Assert.Equal(30 - maxCalls, rateLimited);
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
            cache,
            CreateConfiguration());

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

        Assert.Equal(maxCalls, results1.Count(r => r.Success));
        Assert.Equal(maxCalls, results2.Count(r => r.Success));
    }

    [Fact]
    public async Task ExecuteAsync_ProcessWideThrottling_CapsConcurrencyAcrossAllSessions()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler();
        const int maxProcessLimit = 3;
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration(maxProcess: maxProcessLimit));

        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var ctx = new ToolExecutionContext { SessionId = i, MaxCallsPerSession = 50 };
            return executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, ctx, CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecuteAsync_ExternalLookupCategoryThrottling_CapsLookupConcurrency()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler { Category = ToolCategory.ExternalLookup };
        const int maxLookupLimit = 2;
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration(maxLookup: maxLookupLimit));

        var tasks = Enumerable.Range(1, 6).Select(i =>
        {
            var ctx = new ToolExecutionContext { SessionId = i, MaxCallsPerSession = 50 };
            return executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, ctx, CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(6, results.Length);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecuteAsync_SubAgentCalls_DoNotDepleteCoordinatorRateLimit()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler();
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        const int maxCalls = 5;
        var coordinatorContext = new ToolExecutionContext
        {
            SessionId = 999,
            MaxCallsPerSession = maxCalls,
            AgentDepth = 0
        };

        var subAgentContext = new ToolExecutionContext
        {
            SessionId = 999,
            MaxCallsPerSession = maxCalls,
            AgentDepth = 1
        };

        // Fire 10 subagent calls (which have their own pool)
        var subTasks = Enumerable.Range(0, 10).Select(_ =>
            executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, subAgentContext, CancellationToken.None)
        ).ToList();
        var subResults = await Task.WhenAll(subTasks);

        // Subagents should succeed up to MinCallsPerSessionWithSubAgents (200)
        Assert.All(subResults, r => Assert.True(r.Success));

        // Now coordinator makes calls; coordinator's full maxCalls budget must still be available
        var coordTasks = Enumerable.Range(0, 8).Select(_ =>
            executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, coordinatorContext, CancellationToken.None)
        ).ToList();
        var coordResults = await Task.WhenAll(coordTasks);

        int coordSuccesses = coordResults.Count(r => r.Success);
        int coordRateLimited = coordResults.Count(r => !r.Success);

        Assert.Equal(maxCalls, coordSuccesses);
        Assert.Equal(3, coordRateLimited);
    }

    [Fact]
    public async Task ExecuteAsync_ToolBudgetScopeId_LimitsAndExhaustsScopedBudget()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler();
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration());

        const int maxCalls = 3;
        var contextQ1 = new ToolExecutionContext
        {
            SessionId = 500,
            ToolBudgetScopeId = "bench_run_1_q1",
            MaxCallsPerSession = maxCalls
        };
        var contextQ2 = new ToolExecutionContext
        {
            SessionId = 500,
            ToolBudgetScopeId = "bench_run_1_q2",
            MaxCallsPerSession = maxCalls
        };

        // Q1 makes 5 calls
        var q1Results = new List<ToolResult>();
        for (int i = 0; i < 5; i++)
        {
            q1Results.Add(await executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, contextQ1, CancellationToken.None));
        }

        // Q1: first 3 succeed, last 2 are rate limited with BudgetExhausted = true
        Assert.Equal(3, q1Results.Count(r => r.Success));
        Assert.Equal(2, q1Results.Count(r => !r.Success && r.BudgetExhausted));

        // RemainingBudget is decremented on each success
        Assert.Equal(2, q1Results[0].RemainingBudget);
        Assert.Equal(1, q1Results[1].RemainingBudget);
        Assert.Equal(0, q1Results[2].RemainingBudget);

        // Scope-aware refusal text
        Assert.Equal("Tool call budget for this question is exhausted (3 calls). No further tool calls will run — answer now with what you have.", q1Results[3].ErrorMessage);

        // Q2 is independent even though same SessionId
        var q2Results = new List<ToolResult>();
        for (int i = 0; i < 3; i++)
        {
            q2Results.Add(await executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, contextQ2, CancellationToken.None));
        }
        Assert.All(q2Results, r => Assert.True(r.Success));
    }
}

