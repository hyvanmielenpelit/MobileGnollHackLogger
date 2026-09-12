namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkToolCallLogBuilderTests
{
    private static BenchmarkRun SampleRun()
    {
        return new BenchmarkRun
        {
            Id = 30,
            SuiteName = "Tool Call Log Suite",
            TestedModelDisplayNameUsed = "Model X",
            HarnessVersion = "18",
            StartedAtUtc = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            CandidateSystemPromptSha256 = "abc123",
            ToolGuidesSha256 = null,
            KnowledgeBaseHeadSha = "def456",
            WikiHeadSha = null,
            SourceCodeHeadSha = "ghi789"
        };
    }

    [Fact]
    public void Build_RendersATableForAnAnsweredQuestion_AndANoRowsLineForOneWithNone()
    {
        var withRows = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "wiki_search",
                    Status = "completed",
                    ArgsText = "{}",
                    Result = "ok",
                    ResultLengthChars = 2
                }
            }
        };
        // Status defaults to 0 (not a named BenchmarkAnswerStatus member, and not ProviderError or
        // Failed), so this is the harness-17-and-later, non-terminal-failure wording.
        var withoutRows = new BenchmarkRunAnswer
        {
            OrderIndex = 2,
            QuestionText = "Q2",
            AnswerText = "A2"
            // ToolCalls stays at its default empty list: the turn made no calls.
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { withRows, withoutRows });

        Assert.Contains("## Question 1: Q1", markdown);
        Assert.Contains("| SortOrder | IterationIndex | Name |", markdown);
        Assert.Contains("`wiki_search`", markdown);

        Assert.Contains("## Question 2: Q2", markdown);
        Assert.Contains("No tool calls attempted on this answer.", markdown);

        // No table at all for the answer with no rows: everything from its heading onward is
        // the one-line explanation, never a header row.
        string fromQ2 = markdown.Substring(markdown.IndexOf("## Question 2", StringComparison.Ordinal));
        Assert.DoesNotContain("| SortOrder |", fromQ2);
    }

    [Fact]
    public void Build_RendersTheLegacyNoRowsSentence_OnARunBeforeHarness17()
    {
        var run = SampleRun();
        run.HarnessVersion = "16";
        var answer = new BenchmarkRunAnswer { OrderIndex = 1, QuestionText = "Q1", AnswerText = "A1" };

        string markdown = BenchmarkToolCallLogBuilder.Build(run, new[] { answer });

        Assert.Contains("No tool-call rows recorded for this answer — a run before harness 17 records none.", markdown);
    }

    [Fact]
    public void Build_RendersTheLegacyNoRowsSentence_WhenHarnessVersionIsUnrecorded()
    {
        var run = SampleRun();
        run.HarnessVersion = null;
        var answer = new BenchmarkRunAnswer { OrderIndex = 1, QuestionText = "Q1", AnswerText = "A1" };

        string markdown = BenchmarkToolCallLogBuilder.Build(run, new[] { answer });

        Assert.Contains("No tool-call rows recorded for this answer — a run before harness 17 records none.", markdown);
    }

    [Theory]
    [InlineData(BenchmarkAnswerStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.ProviderError)]
    public void Build_RendersTheTerminalFailureSentence_ForAZeroRowTerminalFailureAnswer_OnAHarness17PlusRun(
        BenchmarkAnswerStatus status)
    {
        var run = SampleRun();
        run.HarnessVersion = "18";
        var answer = new BenchmarkRunAnswer { OrderIndex = 1, QuestionText = "Q1", AnswerText = string.Empty, Status = status };

        string markdown = BenchmarkToolCallLogBuilder.Build(run, new[] { answer });

        Assert.Contains("No tool calls attempted — the answer failed before its first tool round.", markdown);
        Assert.DoesNotContain("No tool calls attempted on this answer.", markdown);
        Assert.DoesNotContain("a run before harness 17 records none", markdown);
    }

    [Fact]
    public void Build_PutsArgsWithAPipeInAFencedBlock_UnescapedAndUnmangled()
    {
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "source_code_search",
                    Status = "completed",
                    ArgsText = "{\"query\": \"a|b\"}",
                    Result = "some result",
                    ResultLengthChars = 11
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        // Unescaped and byte-identical — the pipe survives exactly because it never enters a
        // table cell, only a fenced code block.
        Assert.Contains("{\"query\": \"a|b\"}", markdown);
    }

    [Fact]
    public void Build_RendersPrunedByRetentionMarker_WhenArgsAndResultAreNulledButResultLengthSurvives()
    {
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "wiki_search",
                    Status = "completed",
                    ArgsText = null,
                    Result = null,
                    ResultLengthChars = 4096
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        Assert.Contains("(pruned by retention)", markdown);
    }

    [Fact]
    public void Build_CutsALongResultToA600CharacterHeadAnda240CharacterTail_WithAMarkerNamingTheTrueSize()
    {
        string longResult = new string('x', 5000);
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "source_code_search",
                    Status = "completed",
                    ArgsText = "{}",
                    Result = longResult,
                    ResultLengthChars = 5000
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        Assert.Contains(new string('x', 600), markdown);
        Assert.DoesNotContain(new string('x', 601), markdown);
        Assert.Contains("head of 600 chars; tail of 240 chars follows", markdown);
        Assert.Contains("first 600 and last 240 of 5,000 chars", markdown);
    }

    [Fact]
    public void Build_KeepsTheLastLineOfALongResult()
    {
        // The line a reader needs most is often the last one: it says what the result is a subset
        // of, and a head-only cut discards it.
        const string lastLine = "[Showing 5 of 271 matching articles — …]";
        string longResult = new string('x', 2000 - lastLine.Length) + lastLine;
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "wiki_search",
                    Status = "completed",
                    ArgsText = "{}",
                    Result = longResult,
                    ResultLengthChars = 2000
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        Assert.Contains(lastLine, markdown);
        Assert.Contains("first 600 and last 240 of 2,000 chars", markdown);
    }

    [Fact]
    public void Build_WritesAResultOfExactlyTheHeadPlusTailLengthWhole()
    {
        // At the boundary a cut would emit two fragments that abut, together no shorter than the
        // original, so the whole result is written under the plain label instead.
        string boundaryResult = new string('a', 600) + new string('b', 240);
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "wiki_search",
                    Status = "completed",
                    ArgsText = "{}",
                    Result = boundaryResult,
                    ResultLengthChars = 840
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        Assert.Contains("Result:\n" + boundaryResult, markdown);
        Assert.DoesNotContain("head of 600 chars", markdown);
    }

    [Fact]
    public void Build_NamesBothTheReturnedAndTheStoredSize_WhenTheHarnessCappedWhatItKept()
    {
        // ResultLengthChars is what the tool returned; Result is what survived the harness's own
        // storage cap. When they differ the label must say so, or the reader takes the stored
        // prefix for the whole result and reads the tail shown as the result's own tail.
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            ToolCalls = new List<BenchmarkRunAnswerToolCall>
            {
                new BenchmarkRunAnswerToolCall
                {
                    SortOrder = 0,
                    Name = "wiki_search",
                    Status = "completed",
                    ArgsText = "{}",
                    Result = new string('x', 12000),
                    ResultLengthChars = 12897
                }
            }
        };

        string markdown = BenchmarkToolCallLogBuilder.Build(SampleRun(), new[] { answer });

        Assert.Contains("Result (first 600 and last 240 of 12,897 chars, stored 12,000):", markdown);
    }
}
