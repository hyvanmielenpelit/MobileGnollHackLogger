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
        public int DelayMilliseconds { get; set; } = 5;

        private int _active;
        private int _peakConcurrency;

        /// <summary>The most calls that were inside <see cref="ExecuteAsync"/> at the same time.</summary>
        public int PeakConcurrency => Volatile.Read(ref _peakConcurrency);

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _active);
            int peak;
            while (active > (peak = Volatile.Read(ref _peakConcurrency))
                && Interlocked.CompareExchange(ref _peakConcurrency, active, peak) != peak)
            {
            }

            try
            {
                await Task.Delay(DelayMilliseconds, cancellationToken);
                return new ToolResult { Success = true, Content = "Test result" };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
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
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(42),
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
        var context1 = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(101), MaxCallsPerSession = maxCalls };
        var context2 = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(202), MaxCallsPerSession = maxCalls };

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
        var handler = new TestServerToolHandler { DelayMilliseconds = 100 };
        const int maxProcessLimit = 3;
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration(maxProcess: maxProcessLimit));

        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var ctx = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(i), MaxCallsPerSession = 50 };
            return executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, ctx, CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.True(r.Success));

        // Each call enters the handler synchronously while a slot is free, so the first calls fill
        // every slot before any finishes: the peak reaches the cap and never passes it.
        Assert.True(handler.PeakConcurrency <= maxProcessLimit, $"Peak concurrency {handler.PeakConcurrency} exceeded the cap {maxProcessLimit}.");
        Assert.Equal(maxProcessLimit, handler.PeakConcurrency);
    }

    [Fact]
    public async Task ExecuteAsync_ExternalLookupCategoryThrottling_CapsLookupConcurrency()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var handler = new TestServerToolHandler { Category = ToolCategory.ExternalLookup, DelayMilliseconds = 100 };
        const int maxLookupLimit = 2;
        var executor = new ToolExecutor(
            new[] { handler },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            CreateConfiguration(maxLookup: maxLookupLimit));

        var tasks = Enumerable.Range(1, 6).Select(i =>
        {
            var ctx = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(i), MaxCallsPerSession = 50 };
            return executor.ExecuteAsync("test_tool", JsonDocument.Parse("{}").RootElement, ctx, CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(6, results.Length);
        Assert.All(results, r => Assert.True(r.Success));

        // The process cap (30) leaves room for all six; the lookup cap is what holds them to two.
        Assert.True(handler.PeakConcurrency <= maxLookupLimit, $"Peak concurrency {handler.PeakConcurrency} exceeded the cap {maxLookupLimit}.");
        Assert.Equal(maxLookupLimit, handler.PeakConcurrency);
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
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(999),
            MaxCallsPerSession = maxCalls,
            AgentDepth = 0
        };

        var subAgentContext = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(999),
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
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(500),
            ToolBudgetScopeId = "bench_run_1_q1",
            MaxCallsPerSession = maxCalls
        };
        var contextQ2 = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(500),
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

    /// <summary>A handler outside the session quota, like <c>report_feedback</c>.</summary>
    private sealed class ExemptFeedbackHandler : IToolHandler
    {
        private int _executions;

        public string ToolName => "exempt_feedback_tool";
        public string Description { get; set; } = "Exempt feedback tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.Feedback;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public bool CountsTowardCallBudget => false;

        public int Executions => Volatile.Read(ref _executions);

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executions);
            return Task.FromResult(new ToolResult { Success = true, Content = "Recorded" });
        }
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return _messages.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }

    private static ToolExecutor CreateExecutor(MemoryCache cache, IToolHandler[] handlers, Microsoft.Extensions.Logging.ILogger<ToolExecutor>? logger = null)
        => new(handlers, new NullClientBridge(), logger ?? NullLogger<ToolExecutor>.Instance, cache, CreateConfiguration());

    [Fact]
    public async Task ExecuteAsync_ExemptHandler_RunsAtFullQuota_AndDoesNotMoveIt()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var counted = new TestServerToolHandler();
        var exempt = new ExemptFeedbackHandler();
        var executor = CreateExecutor(cache, new IToolHandler[] { counted, exempt });
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(77), MaxCallsPerSession = 2 };
        var args = JsonDocument.Parse("{}").RootElement;

        Assert.True((await executor.ExecuteAsync("test_tool", args, context, CancellationToken.None)).Success);

        // The exempt calls between the counted ones leave the quota where it was.
        for (int i = 0; i < 5; i++)
        {
            Assert.True((await executor.ExecuteAsync("exempt_feedback_tool", args, context, CancellationToken.None)).Success);
        }

        var second = await executor.ExecuteAsync("test_tool", args, context, CancellationToken.None);
        Assert.True(second.Success);
        Assert.Equal(0, second.RemainingBudget);

        var refused = await executor.ExecuteAsync("test_tool", args, context, CancellationToken.None);
        Assert.False(refused.Success);
        Assert.True(refused.BudgetExhausted);

        // At full quota the exempt handler still runs.
        var atFullQuota = await executor.ExecuteAsync("exempt_feedback_tool", args, context, CancellationToken.None);
        Assert.True(atFullQuota.Success);
        Assert.False(atFullQuota.BudgetExhausted);
        Assert.Equal(6, exempt.Executions);
    }

    [Fact]
    public async Task ExecuteAsync_ExemptHandler_LeavesRemainingBudgetNull()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var executor = CreateExecutor(cache, new IToolHandler[] { new ExemptFeedbackHandler() });
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(78), MaxCallsPerSession = 5 };

        var result = await executor.ExecuteAsync("exempt_feedback_tool", JsonDocument.Parse("{}").RootElement, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.RemainingBudget);
    }

    [Fact]
    public async Task ExecuteAsync_FeedbackHandler_WritesNoParametersAuditLine()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var logger = new RecordingLogger<ToolExecutor>();
        var executor = CreateExecutor(cache, new IToolHandler[] { new ExemptFeedbackHandler(), new TestServerToolHandler() }, logger);
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(79), MaxCallsPerSession = 5 };

        await executor.ExecuteAsync("exempt_feedback_tool", JsonDocument.Parse("{\"summary\":\"private feedback text\"}").RootElement, context, CancellationToken.None);

        Assert.Contains(logger.Messages, m => m.Contains("Executing tool exempt_feedback_tool"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Parameters:"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("private feedback text"));

        // Any other tool still writes it.
        await executor.ExecuteAsync("test_tool", JsonDocument.Parse("{\"q\":\"x\"}").RootElement, context, CancellationToken.None);
        Assert.Contains(logger.Messages, m => m.Contains("Parameters:"));
    }
}

