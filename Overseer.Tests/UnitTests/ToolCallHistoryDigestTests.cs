using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ToolCallHistoryDigestTests
{
    [Fact]
    public void Build_WithMultipleCallsSameBatch_RendersIssuedTogether()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "get_monster_stats", ArgsText = "{\"name\":\"dog\"}", Status = "completed" },
            new() { SortOrder = 1, BatchIndex = 0, Name = "nethack_wiki_search", ArgsText = "{\"query\":\"dog\"}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (2 calls, issued together): get_monster_stats({\"name\":\"dog\"}), nethack_wiki_search({\"query\":\"dog\"})", result);
        Assert.StartsWith("[tool-call record - system-generated, not part of the assistant's reply]", result);
        Assert.EndsWith("[end tool-call record]", result);
    }

    [Fact]
    public void Build_WithSingleCallBatch_RendersSingleCall()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 1, Name = "wiki_view", ArgsText = "{\"title\":\"Dog\"}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 2 (1 call): wiki_view({\"title\":\"Dog\"})", result);
    }

    [Fact]
    public void Build_WithNullBatchIndex_RendersGroupingNotRecorded()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = null, Name = "get_monster_stats", ArgsText = "{\"name\":\"cat\"}", Status = "completed" },
            new() { SortOrder = 1, BatchIndex = null, Name = "wiki_search", ArgsText = "{\"query\":\"cat\"}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("grouping not recorded: get_monster_stats({\"name\":\"cat\"}), wiki_search({\"query\":\"cat\"})", result);
    }

    [Fact]
    public void Build_BatchesRenderInSortOrder()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 2, BatchIndex = 2, Name = "tool_c", ArgsText = "{}", Status = "completed" },
            new() { SortOrder = 0, BatchIndex = 0, Name = "tool_a", ArgsText = "{}", Status = "completed" },
            new() { SortOrder = 1, BatchIndex = 1, Name = "tool_b", ArgsText = "{}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        int idxA = result.IndexOf("batch 1 (1 call): tool_a()");
        int idxB = result.IndexOf("batch 2 (1 call): tool_b()");
        int idxC = result.IndexOf("batch 3 (1 call): tool_c()");

        Assert.True(idxA >= 0);
        Assert.True(idxB > idxA);
        Assert.True(idxC > idxB);
    }

    [Fact]
    public void Build_WithSubAgentNestedCalls_AnnotatesCountAndExcludesFromRootGrouping()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { ToolCallId = "parent-1", SortOrder = 0, BatchIndex = 0, Name = "delegate_to_subagent", ArgsText = "{\"agent\":\"wiki\"}", Status = "completed" },
            new() { ToolCallId = "child-1", ParentToolCallId = "parent-1", Depth = 1, SortOrder = 1, BatchIndex = 0, Name = "wiki_search", ArgsText = "{\"query\":\"sub\"}", Status = "completed" },
            new() { ToolCallId = "child-2", ParentToolCallId = "parent-1", Depth = 1, SortOrder = 2, BatchIndex = 1, Name = "wiki_view", ArgsText = "{\"title\":\"sub\"}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (1 call): delegate_to_subagent({\"agent\":\"wiki\"}) [+2 nested calls]", result);
        Assert.DoesNotContain("wiki_search", result);
        Assert.DoesNotContain("wiki_view", result);
    }

    [Fact]
    public void Build_WithLegacyRow_DepthZeroAndParentId_IsTreatedAsNestedOnly()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { ToolCallId = "parent-1", SortOrder = 0, BatchIndex = 0, Name = "delegate_to_subagent", ArgsText = "{}", Status = "completed" },
            // Legacy row where Depth was 0 before subagent columns were added
            new() { ToolCallId = "child-1", ParentToolCallId = "parent-1", Depth = 0, SortOrder = 1, BatchIndex = 0, Name = "nested_legacy_call", ArgsText = "{}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (1 call): delegate_to_subagent() [+1 nested call]", result);
        Assert.DoesNotContain("nested_legacy_call", result);
    }

    [Fact]
    public void Build_WithNullArgsText_DegradesToNameOnly()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "pruned_tool", ArgsText = null, Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (1 call): pruned_tool", result);
        Assert.DoesNotContain("pruned_tool(", result);
    }

    [Fact]
    public void Build_WithEmptyOrBlankArgs_RendersEmptyParens()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "tool_empty", ArgsText = "{}", Status = "completed" },
            new() { SortOrder = 1, BatchIndex = 0, Name = "tool_blank", ArgsText = "   ", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (2 calls, issued together): tool_empty(), tool_blank()", result);
    }

    [Fact]
    public void Build_WithLongArgs_TruncatesToEightyChars()
    {
        string longArg = "{\"long\":\"" + new string('x', 100) + "\"}";
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "tool_long", ArgsText = longArg, Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("tool_long(" + longArg.Substring(0, 77) + "...)", result);
    }

    [Fact]
    public void Build_WithNonSuccessStatus_AppendsStatusTag()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "tool_ok", ArgsText = "{}", Status = "completed" },
            new() { SortOrder = 1, BatchIndex = 0, Name = "tool_err", ArgsText = "{}", Status = "error" },
            new() { SortOrder = 2, BatchIndex = 0, Name = "tool_timeout", ArgsText = "{}", Status = "Timeout" },
            new() { SortOrder = 3, BatchIndex = 0, Name = "tool_running", ArgsText = "{}", Status = "running" },
            new() { SortOrder = 4, BatchIndex = 0, Name = "tool_success", ArgsText = "{}", Status = "success" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("tool_ok()", result);
        Assert.Contains("tool_err() [error]", result);
        Assert.Contains("tool_timeout() [timeout]", result);
        Assert.Contains("tool_running()", result);
        Assert.Contains("tool_success()", result);
    }

    [Fact]
    public void Build_WhenExceedingMaxChars_TruncatesWithOverflowNoteAndStaysUnderCap()
    {
        var calls = new List<ChatMessageToolCall>();
        for (int i = 0; i < 20; i++)
        {
            calls.Add(new ChatMessageToolCall
            {
                SortOrder = i,
                BatchIndex = i,
                Name = $"very_long_named_tool_operation_{i}",
                ArgsText = $"{{\"key\":\"arbitrary_value_for_batch_index_{i}\"}}",
                Status = "completed"
            });
        }

        int maxChars = 300;
        var result = ToolCallHistoryDigest.Build(calls, maxChars: maxChars);

        Assert.NotNull(result);
        Assert.True(result.Length <= maxChars, $"Expected length <= {maxChars}, but got {result.Length}");
        Assert.Contains("... (+", result);
        Assert.Contains("more calls)", result);
        Assert.EndsWith("[end tool-call record]", result);
    }

    [Fact]
    public void Build_WhenFirstGroupAloneExceedsMaxChars_StillEmitsIt()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "huge_call_name", ArgsText = "{\"large\":\"payload_that_exceeds_very_small_cap\"}", Status = "completed" }
        };

        int tinyMaxChars = 30; // smaller than header + footer + group
        var result = ToolCallHistoryDigest.Build(calls, maxChars: tinyMaxChars);

        Assert.NotNull(result);
        Assert.Contains("batch 1 (1 call): huge_call_name", result);
        Assert.EndsWith("[end tool-call record]", result);
    }

    [Fact]
    public void Build_UsesLineFeedOnly_NoCarriageReturns()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new() { SortOrder = 0, BatchIndex = 0, Name = "tool_lf", ArgsText = "{\"multi\":\"line\r\nvalue\"}", Status = "completed" }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.DoesNotContain("\r", result);
    }

    [Fact]
    public void Build_WithNoCallsOrOnlyNonRootCalls_ReturnsNull()
    {
        Assert.Null(ToolCallHistoryDigest.Build(null));
        Assert.Null(ToolCallHistoryDigest.Build(new List<ChatMessageToolCall>()));

        var onlyChildCalls = new List<ChatMessageToolCall>
        {
            new() { ToolCallId = "c1", ParentToolCallId = "p1", Depth = 1, Name = "child", ArgsText = "{}" }
        };
        Assert.Null(ToolCallHistoryDigest.Build(onlyChildCalls));
    }

    [Fact]
    public void Build_MultiLineArgs_SanitizesToSingleLine()
    {
        var calls = new List<ChatMessageToolCall>
        {
            new()
            {
                SortOrder = 0,
                BatchIndex = 0,
                Name = "tool_json",
                ArgsText = "{\n  \"query\": \"dog\",\n  \"limit\": 5\n}",
                Status = "completed"
            }
        };

        var result = ToolCallHistoryDigest.Build(calls);

        Assert.NotNull(result);
        Assert.Contains("tool_json({   \"query\": \"dog\",   \"limit\": 5 })", result);
    }
}
