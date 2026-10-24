using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
    public bool BudgetExhausted { get; init; }
    public int? RemainingBudget { get; init; }
    public List<Agents.DelegatedRunCost>? DelegatedRunCosts { get; init; }

    /// <summary><see cref="ToolResult.SnapshotRefusalCode"/>: the snapshot guard withheld this client result.</summary>
    public string? SnapshotRefusalCode { get; init; }
}

public static class ToolBatchRunner
{
    /// <summary>
    /// Runs <paramref name="items"/> concurrently under dual throttles and writes a
    /// ChatEvent-shaped record for each completion to <paramref name="events"/>.
    /// Never throws and never faults: cancellation and handler exceptions are folded
    /// into a failed <see cref="ToolBatchOutcome"/>, and a handler exception other than
    /// cancellation is logged to <paramref name="logger"/>. Results preserve input order.
    /// </summary>
    public static async Task<IReadOnlyList<ToolBatchOutcome>> RunAsync(
        IReadOnlyList<ToolBatchItem> items,
        Func<ToolBatchItem, CancellationToken, Task<ToolResult>> executor,
        int maxParallelTools,
        int maxParallelClientTools,
        ChannelWriter<ToolBatchOutcome> events,
        CancellationToken cancellationToken,
        ILogger? logger = null)
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

                        var content = ResultContent(res);

                        var outcome = new ToolBatchOutcome
                        {
                            ToolCallId = item.ToolCallId,
                            ToolName = item.ToolName,
                            Content = content,
                            Success = res.Success,
                            QueueWaitMs = swQueue.ElapsedMilliseconds + (res.QueueWaitMs ?? 0),
                            ExecutionMs = res.ExecutionMs ?? swTool.ElapsedMilliseconds,
                            NestedToolCalls = res.NestedToolCalls,
                            TerminationStatus = res.TerminationStatus ?? (res.Success ? "completed" : "error"),
                            BudgetExhausted = res.BudgetExhausted,
                            RemainingBudget = res.RemainingBudget,
                            DelegatedRunCosts = res.DelegatedRunCosts,
                            SnapshotRefusalCode = res.SnapshotRefusalCode
                        };
                        events.TryWrite(outcome);
                        return outcome;
                    }
                    catch (Exception ex)
                    {
                        swQueue.Stop();
                        swTool?.Stop();
                        if (ex is not OperationCanceledException)
                        {
                            logger?.LogError(ex, "Tool {ToolName} ({ToolCallId}) failed in the batch runner.", item.ToolName, item.ToolCallId);
                        }
                        var outcome = new ToolBatchOutcome
                        {
                            ToolCallId = item.ToolCallId,
                            ToolName = item.ToolName,
                            Content = ex is OperationCanceledException
                                ? "Tool execution was canceled (request stopped)."
                                : $"Orchestrator error: {ToolErrorText.ForModel(ex)}",
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
                : SynthesizeFailure(items[i], t.Exception, logger)).ToList();
        }
        finally
        {
            events.TryComplete();
        }
    }

    /// <summary>
    /// The text a tool call contributes to the conversation: the content on success
    /// ("Success" when empty); on failure the error message, else the content, else "Unknown error".
    /// </summary>
    internal static string ResultContent(ToolResult res)
    {
        return res.Success
            ? (!string.IsNullOrEmpty(res.Content) ? res.Content : "Success")
            : (!string.IsNullOrWhiteSpace(res.ErrorMessage)
                ? res.ErrorMessage
                : (!string.IsNullOrWhiteSpace(res.Content) ? res.Content : "Unknown error"));
    }

    private static ToolBatchOutcome SynthesizeFailure(ToolBatchItem item, Exception? ex, ILogger? logger)
    {
        var cause = ex is AggregateException { InnerExceptions.Count: 1 } single ? single.InnerExceptions[0] : ex;
        if (cause != null)
        {
            logger?.LogError(cause, "Tool {ToolName} ({ToolCallId}) task faulted in the batch runner.", item.ToolName, item.ToolCallId);
        }
        return new ToolBatchOutcome
        {
            ToolCallId = item.ToolCallId,
            ToolName = item.ToolName,
            Content = $"Orchestrator error: {(cause == null ? "Task failed" : ToolErrorText.ForModel(cause))}",
            Success = false,
            QueueWaitMs = 0,
            ExecutionMs = 0,
            TerminationStatus = "error"
        };
    }
}
