using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

public static class ToolCallHistoryDigest
{
    private const string Header = "[tool-call record - system-generated, not part of the assistant's reply]";
    private const string Footer = "[end tool-call record]";

    public static string? Build(IEnumerable<ChatMessageToolCall>? calls, int maxChars = 1500)
    {
        if (calls == null) return null;
        var callList = calls.ToList();
        if (callList.Count == 0) return null;

        // Root and nested are exact complements: a call is nested iff it has a parent id.
        var rootCalls = callList
            .Where(c => string.IsNullOrEmpty(c.ParentToolCallId))
            .OrderBy(c => c.SortOrder)
            .ToList();

        if (rootCalls.Count == 0) return null;

        // Counted per *immediate* parent, so a delegate call reports its direct children only.
        var nestedCounts = callList
            .Where(c => !string.IsNullOrEmpty(c.ParentToolCallId))
            .GroupBy(c => c.ParentToolCallId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // GroupBy preserves first-occurrence order, so batches stay in execution order.
        var groups = rootCalls
            .GroupBy(c => c.BatchIndex)
            .Select(g =>
            {
                var groupCalls = g.ToList();
                string formatted = string.Join(", ", groupCalls.Select(c => FormatCall(c, nestedCounts)));
                string line;
                if (g.Key.HasValue)
                {
                    // toolIterations is 0-based; render 1-based batch numbering for readability.
                    int batchNum = g.Key.Value + 1;
                    string countDescriptor = groupCalls.Count > 1
                        ? $"{groupCalls.Count} calls, issued together"
                        : "1 call";
                    line = $"batch {batchNum} ({countDescriptor}): {formatted}";
                }
                else
                {
                    line = $"grouping not recorded: {formatted}";
                }
                return (Line: line, Count: groupCalls.Count);
            })
            .ToList();

        int totalRootCalls = rootCalls.Count;
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');

        int emittedCalls = 0;
        foreach (var group in groups)
        {
            int remainingAfter = totalRootCalls - emittedCalls - group.Count;
            int reserve = remainingAfter > 0 ? OverflowNote(remainingAfter).Length + 1 : 0;
            int needed = group.Line.Length + 1 + reserve + Footer.Length;

            // Always emit at least one group, even if it alone exceeds maxChars.
            if (emittedCalls > 0 && sb.Length + needed > maxChars) break;

            sb.Append(group.Line).Append('\n');
            emittedCalls += group.Count;
        }

        if (emittedCalls < totalRootCalls)
        {
            sb.Append(OverflowNote(totalRootCalls - emittedCalls)).Append('\n');
        }

        sb.Append(Footer);
        return sb.ToString();
    }

    private static string OverflowNote(int remaining) => $"... (+{remaining} more calls)";

    private static string FormatCall(ChatMessageToolCall call, Dictionary<string, int> nestedCounts)
    {
        string name = call.Name ?? "";
        string formattedArgs;

        if (call.ArgsText == null)
        {
            // Null ArgsText means retention pruned the payload; degrade to name-only.
            formattedArgs = "";
        }
        else
        {
            var trimmed = call.ArgsText.Replace("\r", "").Replace("\n", " ").Trim();
            if (trimmed == "{}" || string.IsNullOrWhiteSpace(trimmed))
            {
                formattedArgs = "()";
            }
            else
            {
                if (trimmed.Length > 80)
                {
                    trimmed = trimmed.Substring(0, 77) + "...";
                }
                formattedArgs = $"({trimmed})";
            }
        }

        string callStr = string.IsNullOrEmpty(formattedArgs) ? name : $"{name}{formattedArgs}";

        if (!string.IsNullOrEmpty(call.Status) &&
            !string.Equals(call.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(call.Status, "running", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(call.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            callStr += $" [{call.Status.ToLowerInvariant()}]";
        }

        if (!string.IsNullOrEmpty(call.ToolCallId) &&
            nestedCounts.TryGetValue(call.ToolCallId, out var nestedCount) &&
            nestedCount > 0)
        {
            callStr += nestedCount == 1 ? " [+1 nested call]" : $" [+{nestedCount} nested calls]";
        }

        return callStr;
    }
}
