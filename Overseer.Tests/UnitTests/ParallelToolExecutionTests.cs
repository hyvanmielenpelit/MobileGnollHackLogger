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

        var channel = Channel.CreateUnbounded<ToolBatchOutcome>();
        var sw = Stopwatch.StartNew();

        var outcomes = await ToolBatchRunner.RunAsync(
            items,
            async (name, args, ct) =>
            {
                await Task.Delay(150, ct);
                return new ToolResult { Success = true, Content = $"Result for {name}" };
            },
            maxParallelTools: 5,
            maxParallelClientTools: 1,
            channel.Writer,
            CancellationToken.None);

        sw.Stop();

        Assert.Equal(5, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Success));
        // Sequential would be 5 * 150ms = 750ms. Parallel with 5 slots should finish well under 650ms.
        Assert.True(sw.ElapsedMilliseconds < 650, $"Elapsed time was {sw.ElapsedMilliseconds}ms, expected < 650ms");
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
            async (name, args, ct) =>
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
            async (name, args, ct) =>
            {
                bool isClient = name.StartsWith("client");
                if (isClient)
                {
                    int c = Interlocked.Increment(ref currentClientConcurrent);
                    lock (items)
                    {
                        if (c > maxObservedClientConcurrent) maxObservedClientConcurrent = c;
                    }
                }
                await Task.Delay(60, ct);
                if (isClient)
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
            async (name, args, ct) =>
            {
                await Task.Yield();
                if (name == "tool_throw")
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
            async (name, args, ct) =>
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
            async (name, args, ct) =>
            {
                int delay = name switch
                {
                    "slow_tool" => 150,
                    "medium_tool" => 80,
                    "fast_tool" => 20,
                    _ => 10
                };
                await Task.Delay(delay, ct);
                return new ToolResult { Success = true, Content = $"Result of {name}" };
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
            async (name, args, ct) =>
            {
                await Task.Delay(20, ct);
                return new ToolResult { Success = true, Content = $"Done {name}" };
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
