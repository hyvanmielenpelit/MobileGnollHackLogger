using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Overseer.Services.Tools;

public sealed class ToolBatchItem
{
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required JsonElement Arguments { get; init; }
    public required bool IsClientTool { get; init; }
}

public sealed class ToolBatchOutcome
{
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required string Content { get; init; }
    public required bool Success { get; init; }
    public long QueueWaitMs { get; init; }
    public long ExecutionMs { get; init; }
    public List<MobileGnollHackLogger.Data.ChatMessageToolCall>? NestedToolCalls { get; init; }
    public string? TerminationStatus { get; init; }
}

public static class ToolBatchRunner
{
    /// <summary>
    /// Runs <paramref name="items"/> concurrently under dual throttles and writes a
    /// ChatEvent-shaped record for each completion to <paramref name="events"/>.
    /// Never throws and never faults: cancellation and handler exceptions are folded
    /// into a failed <see cref="ToolBatchOutcome"/>. Results preserve input order.
    /// </summary>
    public static async Task<IReadOnlyList<ToolBatchOutcome>> RunAsync(
        IReadOnlyList<ToolBatchItem> items,
        Func<ToolBatchItem, CancellationToken, Task<ToolResult>> executor,
        int maxParallelTools,
        int maxParallelClientTools,
        ChannelWriter<ToolBatchOutcome> events,
        CancellationToken cancellationToken)
    {
        try
        {
            int globalLimit = Math.Max(1, maxParallelTools);
            int clientLimit = Math.Clamp(maxParallelClientTools, 1, globalLimit);

            using var throttler = new SemaphoreSlim(globalLimit);
            using var clientThrottler = new SemaphoreSlim(clientLimit);

            var tasks = new List<Task<ToolBatchOutcome>>(items.Count);

            foreach (var item in items)
            {
                tasks.Add(Task.Run(async () =>
                {
                    bool clientSlot = false;
                    bool globalSlot = false;
                    var swQueue = Stopwatch.StartNew();
                    Stopwatch? swTool = null;
                    try
                    {
                        // Client slot first: a queued client tool must never hold a global
                        // slot, or it could starve server tools. Server tools take only the
                        // global semaphore, so no acquisition cycle is possible.
                        if (item.IsClientTool)
                        {
                            await clientThrottler.WaitAsync(cancellationToken);
                            clientSlot = true;
                        }

                        await throttler.WaitAsync(cancellationToken);
                        globalSlot = true;
                        swQueue.Stop();

                        swTool = Stopwatch.StartNew();
                        var res = await executor(item, cancellationToken);
                        swTool.Stop();

                        var content = res.Success
                            ? (!string.IsNullOrEmpty(res.Content) ? res.Content : "Success")
                            : (!string.IsNullOrWhiteSpace(res.ErrorMessage)
                                ? res.ErrorMessage
                                : (!string.IsNullOrWhiteSpace(res.Content) ? res.Content : "Unknown error"));

                        var outcome = new ToolBatchOutcome
                        {
                            ToolCallId = item.ToolCallId,
                            ToolName = item.ToolName,
                            Content = content,
                            Success = res.Success,
                            QueueWaitMs = swQueue.ElapsedMilliseconds + (res.QueueWaitMs ?? 0),
                            ExecutionMs = res.ExecutionMs ?? swTool.ElapsedMilliseconds,
                            NestedToolCalls = res.NestedToolCalls,
                            TerminationStatus = res.TerminationStatus ?? (res.Success ? "completed" : "error")
                        };
                        events.TryWrite(outcome);
                        return outcome;
                    }
                    catch (Exception ex)
                    {
                        swQueue.Stop();
                        swTool?.Stop();
                        var outcome = new ToolBatchOutcome
                        {
                            ToolCallId = item.ToolCallId,
                            ToolName = item.ToolName,
                            Content = ex is OperationCanceledException
                                ? "Tool execution was canceled (request stopped)."
                                : $"Orchestrator error: {ex.Message}",
                            Success = false,
                            QueueWaitMs = swQueue.ElapsedMilliseconds,
                            ExecutionMs = swTool?.ElapsedMilliseconds ?? 0,
                            TerminationStatus = ex is OperationCanceledException ? "canceled" : "error"
                        };
                        events.TryWrite(outcome);
                        return outcome;
                    }
                    finally
                    {
                        if (globalSlot) throttler.Release();
                        if (clientSlot) clientThrottler.Release();
                    }
                }, CancellationToken.None));
            }

            var all = Task.WhenAll(tasks);
            try { await all; } catch { /* unreachable: each task delegate catches all */ }

            return tasks.Select((t, i) => t.IsCompletedSuccessfully
                ? t.Result
                : SynthesizeFailure(items[i], t.Exception)).ToList();
        }
        finally
        {
            events.TryComplete();
        }
    }

    private static ToolBatchOutcome SynthesizeFailure(ToolBatchItem item, Exception? ex)
    {
        return new ToolBatchOutcome
        {
            ToolCallId = item.ToolCallId,
            ToolName = item.ToolName,
            Content = $"Orchestrator error: {ex?.Message ?? "Task failed"}",
            Success = false,
            QueueWaitMs = 0,
            ExecutionMs = 0,
            TerminationStatus = "error"
        };
    }
}
