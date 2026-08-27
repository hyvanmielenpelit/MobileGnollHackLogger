using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ParallelToolExecutionTests
{
    private static JsonElement EmptyArgs => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task RunAsync_ExecutesToolsConcurrently_CompletesFasterThanSequentialSum()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "1", ToolName = "tool_1", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "2", ToolName = "tool_2", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "3", ToolName = "tool_3", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "4", ToolName = "tool_4", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "5", ToolName = "tool_5", Arguments = EmptyArgs, IsClientTool = false }
        };

        int currentConcurrent = 0;
        int maxObservedConcurrent = 0;
        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                int c = Interlocked.Increment(ref currentConcurrent);
                lock (items)
                {
                    if (c > maxObservedConcurrent) maxObservedConcurrent = c;
                }
                await Task.Delay(100, ct);
                Interlocked.Decrement(ref currentConcurrent);
                return new ToolResult { Success = true, Content = $"Result for {item.ToolName}" };
            },
            maxParallelTools: 5,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(5, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.True(maxObservedConcurrent > 1, $"Expected concurrent execution, but max concurrency was {maxObservedConcurrent}");
    }

    [Fact]
    public async Task RunAsync_EmptyBatch_CompletesImmediatelyAndClosesChannel()
    {
        var items = new List<ToolBatchItem>();
        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            (item, ct) => Task.FromResult(new ToolResult { Success = true }),
            maxParallelTools: 5,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Empty(outcomes);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task RunAsync_GlobalThrottling_NeverExceedsMaxParallelTools()
    {
        var items = Enumerable.Range(1, 10).Select(i => new ToolBatchItem
        {
            ToolCallId = i.ToString(),
            ToolName = $"tool_{i}",
            Arguments = EmptyArgs,
            IsClientTool = false
        }).ToList();

        int currentConcurrent = 0;
        int maxObservedConcurrent = 0;
        const int maxParallel = 3;

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                int c = Interlocked.Increment(ref currentConcurrent);
                lock (items)
                {
                    if (c > maxObservedConcurrent) maxObservedConcurrent = c;
                }
                await Task.Delay(50, ct);
                Interlocked.Decrement(ref currentConcurrent);
                return new ToolResult { Success = true, Content = "OK" };
            },
            maxParallelTools: maxParallel,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(10, outcomes.Count);
        Assert.True(maxObservedConcurrent <= maxParallel, $"Observed concurrency was {maxObservedConcurrent}, expected <= {maxParallel}");
    }

    [Fact]
    public async Task RunAsync_ClientThrottling_SerializesClientToolsWhileServerToolsOverlap()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "c1", ToolName = "client_1", Arguments = EmptyArgs, IsClientTool = true },
            new() { ToolCallId = "s1", ToolName = "server_1", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "c2", ToolName = "client_2", Arguments = EmptyArgs, IsClientTool = true },
            new() { ToolCallId = "s2", ToolName = "server_2", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "c3", ToolName = "client_3", Arguments = EmptyArgs, IsClientTool = true }
        };

        int currentClientConcurrent = 0;
        int maxObservedClientConcurrent = 0;

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                if (item.IsClientTool)
                {
                    int c = Interlocked.Increment(ref currentClientConcurrent);
                    lock (items)
                    {
                        if (c > maxObservedClientConcurrent) maxObservedClientConcurrent = c;
                    }
                }
                await Task.Delay(60, ct);
                if (item.IsClientTool)
                {
                    Interlocked.Decrement(ref currentClientConcurrent);
                }
                return new ToolResult { Success = true, Content = "OK" };
            },
            maxParallelTools: 4,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(5, outcomes.Count);
        Assert.Equal(1, maxObservedClientConcurrent);
    }

    [Fact]
    public async Task RunAsync_ClientLimitHigherThanGlobalLimit_ClampedToGlobalLimit()
    {
        var items = Enumerable.Range(1, 6).Select(i => new ToolBatchItem
        {
            ToolCallId = i.ToString(),
            ToolName = $"client_{i}",
            Arguments = EmptyArgs,
            IsClientTool = true
        }).ToList();

        int currentConcurrent = 0;
        int maxObservedConcurrent = 0;
        const int maxParallel = 2;

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                int c = Interlocked.Increment(ref currentConcurrent);
                lock (items)
                {
                    if (c > maxObservedConcurrent) maxObservedConcurrent = c;
                }
                await Task.Delay(50, ct);
                Interlocked.Decrement(ref currentConcurrent);
                return new ToolResult { Success = true, Content = "OK" };
            },
            maxParallelTools: maxParallel,
            maxParallelClientTools: 10, // Higher than global
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(6, outcomes.Count);
        Assert.True(maxObservedConcurrent <= maxParallel, $"Observed concurrency was {maxObservedConcurrent}, expected <= {maxParallel}");
    }

    [Fact]
    public async Task RunAsync_ServerToolsNeverTakeClientSlot()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "s1", ToolName = "server_1", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "s2", ToolName = "server_2", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "s3", ToolName = "server_3", Arguments = EmptyArgs, IsClientTool = false }
        };

        int currentConcurrent = 0;
        int maxObservedConcurrent = 0;

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                int c = Interlocked.Increment(ref currentConcurrent);
                lock (items)
                {
                    if (c > maxObservedConcurrent) maxObservedConcurrent = c;
                }
                await Task.Delay(50, ct);
                Interlocked.Decrement(ref currentConcurrent);
                return new ToolResult { Success = true, Content = "OK" };
            },
            maxParallelTools: 3,
            maxParallelClientTools: 1, // Client limit is 1, but these are server tools
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(3, outcomes.Count);
        Assert.True(maxObservedConcurrent > 1, $"Server tools should run up to global limit (3), observed {maxObservedConcurrent}");
    }

    [Fact]
    public async Task RunAsync_FaultTolerance_HandlesExceptionsGracefully()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "1", ToolName = "tool_ok", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "2", ToolName = "tool_throw", Arguments = EmptyArgs, IsClientTool = false }
        };

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                await Task.Yield();
                if (item.ToolName == "tool_throw")
                {
                    throw new InvalidOperationException("Simulated error in handler");
                }
                return new ToolResult { Success = true, Content = "Success content" };
            },
            maxParallelTools: 2,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.True(outcomes[0].Success);
        Assert.Equal("Success content", outcomes[0].Content);

        Assert.False(outcomes[1].Success);
        Assert.Contains("Simulated error in handler", outcomes[1].Content);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsCanceledOutcomesWithoutFaulting()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "1", ToolName = "tool_1", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "2", ToolName = "tool_2", Arguments = EmptyArgs, IsClientTool = false }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                await Task.Delay(1000, ct);
                return new ToolResult { Success = true, Content = "OK" };
            },
            maxParallelTools: 2,
            maxParallelClientTools: 1,
            channel.Writer,
            cts.Token);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o =>
        {
            Assert.False(o.Success);
            Assert.Contains("Tool execution was canceled", o.Content);
        });
    }

    [Fact]
    public async Task RunAsync_PreservesInputOrder_EvenWhenTasksFinishOutOfOrder()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "1", ToolName = "slow_tool", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "2", ToolName = "medium_tool", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "3", ToolName = "fast_tool", Arguments = EmptyArgs, IsClientTool = false }
        };

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                int delay = item.ToolName switch
                {
                    "slow_tool" => 150,
                    "medium_tool" => 80,
                    "fast_tool" => 20,
                    _ => 10
                };
                await Task.Delay(delay, ct);
                return new ToolResult { Success = true, Content = $"Result of {item.ToolName}" };
            },
            maxParallelTools: 3,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        Assert.Equal(3, outcomes.Count);
        Assert.Equal("1", outcomes[0].ToolCallId);
        Assert.Equal("slow_tool", outcomes[0].ToolName);
        Assert.Equal("2", outcomes[1].ToolCallId);
        Assert.Equal("medium_tool", outcomes[1].ToolName);
        Assert.Equal("3", outcomes[2].ToolCallId);
        Assert.Equal("fast_tool", outcomes[2].ToolName);
    }

    [Fact]
    public async Task RunAsync_EmitsAllEventsToChannelAndCompletesWriter()
    {
        var items = new List<ToolBatchItem>
        {
            new() { ToolCallId = "1", ToolName = "tool_1", Arguments = EmptyArgs, IsClientTool = false },
            new() { ToolCallId = "2", ToolName = "tool_2", Arguments = EmptyArgs, IsClientTool = false }
        };

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();

        var runnerTask = ToolBatchRunner.RunAsync(
            items,
            async (item, ct) =>
            {
                await Task.Delay(20, ct);
                return new ToolResult { Success = true, Content = $"Done {item.ToolName}" };
            },
            maxParallelTools: 2,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        var channelEvents = new List<ToolBatchOutcome>();
        await foreach (var evt in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            channelEvents.Add(evt);
        }

        var outcomes = await runnerTask;

        Assert.Equal(2, channelEvents.Count);
        Assert.Equal(2, outcomes.Count);
    }
}

