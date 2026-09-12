namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// <see cref="BenchmarkToolCallRecorder"/> translates the live <see cref="ChatMessageToolCall"/>
/// list of a benchmark turn into the stored <see cref="BenchmarkRunAnswerToolCall"/> rows, and
/// classifies those rows into succeeded / failed / refused. Both halves are pure and static, so
/// every test here is a direct call with no database or provider involved.
/// </summary>
public class BenchmarkToolCallRecorderTests
{
    private static ChatMessageToolCall Call(
        int sortOrder = 0,
        int? batchIndex = null,
        string? name = "wiki_search",
        string? toolCallId = "call-1",
        string? status = "completed",
        string? argsText = null,
        string? result = null,
        string? error = null,
        int? queueWaitMs = null,
        int? executionMs = null,
        int depth = 0,
        string? agentName = null)
    {
        return new ChatMessageToolCall
        {
            SortOrder = sortOrder,
            BatchIndex = batchIndex,
            Name = name,
            ToolCallId = toolCallId,
            Status = status,
            ArgsText = argsText,
            Result = result,
            Error = error,
            QueueWaitMs = queueWaitMs,
            ExecutionMs = executionMs,
            Depth = depth,
            AgentName = agentName
        };
    }

    private static BenchmarkToolCallRecordLimits Limits(int maxArgs = 4000, int maxResult = 12000, int maxError = 2000)
        => new(maxArgs, maxResult, maxError);

    // --- Build: ordering -----------------------------------------------------------------

    [Fact]
    public void Build_AssignsSortOrderDenselyInEnumerationOrder_NotBySourceSortOrder()
    {
        // Nested sub-agent calls are appended after their parent completes and carry no
        // meaningful value in ChatMessageToolCall.SortOrder, so the source values here are
        // absent, zero, and out of order on purpose — sorting on them would scramble the turn.
        var calls = new[]
        {
            Call(sortOrder: 5, batchIndex: 0, toolCallId: "first"),
            Call(sortOrder: 0, batchIndex: 0, toolCallId: "second"),
            Call(sortOrder: 0, batchIndex: 1, toolCallId: "third")
        };

        var rows = BenchmarkToolCallRecorder.Build(calls, Limits());

        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => r.SortOrder));
        Assert.Equal(new[] { "first", "second", "third" }, rows.Select(r => r.ToolCallId));
    }

    // --- Build: direct field mapping -------------------------------------------------------

    [Fact]
    public void Build_MapsIterationIndexAndPassthroughFieldsDirectly()
    {
        var call = Call(
            batchIndex: 3,
            name: "source_code_search",
            toolCallId: "call-42",
            status: "completed",
            queueWaitMs: 120,
            executionMs: 480,
            depth: 2,
            agentName: "sub-agent-1");

        var row = BenchmarkToolCallRecorder.Build(new[] { call }, Limits()).Single();

        Assert.Equal(3, row.IterationIndex);
        Assert.Equal("source_code_search", row.Name);
        Assert.Equal("call-42", row.ToolCallId);
        Assert.Equal("completed", row.Status);
        Assert.Equal(120, row.QueueWaitMs);
        Assert.Equal(480, row.ExecutionMs);
        Assert.Equal(2, row.Depth);
        Assert.Equal("sub-agent-1", row.AgentName);
    }

    // --- Build: ResultLengthChars is the true pre-cap length -------------------------------

    [Fact]
    public void Build_ResultLengthChars_IsTruePreCapLength_EvenWhenTheResultWasCapped()
    {
        string result = new string('r', 500);
        var call = Call(result: result);

        var row = BenchmarkToolCallRecorder.Build(new[] { call }, Limits(maxResult: 50)).Single();

        // The stored payload was capped to 50, but the recorded length is the tool's true
        // output size — the distinction the whole feature exists to preserve.
        Assert.Equal(500, row.ResultLengthChars);
        Assert.True(row.ResultTruncated);
        Assert.True(row.Result!.Length <= 50);
    }

    [Fact]
    public void Build_ResultLengthChars_IsZero_WhenTheCallProducedNoResult()
    {
        var call = Call(result: null);

        var row = BenchmarkToolCallRecorder.Build(new[] { call }, Limits()).Single();

        Assert.Equal(0, row.ResultLengthChars);
        Assert.False(row.ResultTruncated);
        Assert.Null(row.Result);
    }

    // --- Build: args/result caps ------------------------------------------------------------

    [Fact]
    public void Build_ArgsCap_FiresOnlyPastTheLimit_AndStoredPayloadNeverExceedsIt()
    {
        string atLimit = new string('a', 100);
        string overLimit = new string('a', 150);

        var atLimitRow = BenchmarkToolCallRecorder.Build(
            new[] { Call(argsText: atLimit) }, Limits(maxArgs: 100)).Single();
        var overLimitRow = BenchmarkToolCallRecorder.Build(
            new[] { Call(argsText: overLimit) }, Limits(maxArgs: 100)).Single();

        Assert.False(atLimitRow.ArgsTruncated);
        Assert.Equal(100, atLimitRow.ArgsText!.Length);

        Assert.True(overLimitRow.ArgsTruncated);
        Assert.True(overLimitRow.ArgsText!.Length <= 100);
    }

    [Fact]
    public void Build_ResultCap_FiresOnlyPastTheLimit_AndStoredPayloadNeverExceedsIt()
    {
        string atLimit = new string('b', 100);
        string overLimit = new string('b', 150);

        var atLimitRow = BenchmarkToolCallRecorder.Build(
            new[] { Call(result: atLimit) }, Limits(maxResult: 100)).Single();
        var overLimitRow = BenchmarkToolCallRecorder.Build(
            new[] { Call(result: overLimit) }, Limits(maxResult: 100)).Single();

        Assert.False(atLimitRow.ResultTruncated);
        Assert.Equal(100, atLimitRow.Result!.Length);

        Assert.True(overLimitRow.ResultTruncated);
        Assert.True(overLimitRow.Result!.Length <= 100);
    }

    [Fact]
    public void Build_ErrorIsCapped_ButNoTruncationFlagIsRecordedForIt()
    {
        // BenchmarkRunAnswerToolCall carries ArgsTruncated and ResultTruncated but deliberately
        // no ErrorTruncated: a cut error still carries its diagnostic first line.
        string overLimit = new string('e', 3000);

        var row = BenchmarkToolCallRecorder.Build(
            new[] { Call(error: overLimit) }, Limits(maxError: 100)).Single();

        Assert.True(row.Error!.Length <= 100);
    }

    // --- Build: column-length clipping -------------------------------------------------------

    [Fact]
    public void Build_ClipsOverLongShortFieldsToTheirColumnLengths_InsteadOfThrowing()
    {
        var call = Call(
            name: new string('n', 300),
            toolCallId: new string('i', 200),
            status: new string('s', 50),
            agentName: new string('g', 200));

        var row = BenchmarkToolCallRecorder.Build(new[] { call }, Limits()).Single();

        Assert.Equal(256, row.Name!.Length);
        Assert.Equal(128, row.ToolCallId!.Length);
        Assert.Equal(32, row.Status!.Length);
        Assert.Equal(128, row.AgentName!.Length);
    }

    // --- Build: no-cap escape hatch -----------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveLimit_MeansNoCap(int nonPositiveLimit)
    {
        string longArgs = new string('a', 10_000);

        var row = BenchmarkToolCallRecorder.Build(
            new[] { Call(argsText: longArgs) },
            new BenchmarkToolCallRecordLimits(nonPositiveLimit, nonPositiveLimit, nonPositiveLimit)).Single();

        Assert.False(row.ArgsTruncated);
        Assert.Equal(10_000, row.ArgsText!.Length);
    }

    // --- Build: empty input ---------------------------------------------------------------

    [Fact]
    public void Build_EmptyInput_YieldsEmptyList()
    {
        Assert.Empty(BenchmarkToolCallRecorder.Build(System.Array.Empty<ChatMessageToolCall>(), Limits()));
        Assert.Empty(BenchmarkToolCallRecorder.Build(null!, Limits()));
    }

    // --- BenchmarkToolCallRecordLimits.Resolve ---------------------------------------------

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Theory]
    [InlineData(1000, 3000)]
    [InlineData(5000, 7000)]
    public void Resolve_MaxResultChars_DefaultsToMaxResultLengthPlusHeadroom_NotALiteral(
        int maxResultLength, int expectedMaxResultChars)
    {
        var limits = BenchmarkToolCallRecordLimits.Resolve(EmptyConfig(), maxResultLength);

        Assert.Equal(expectedMaxResultChars, limits.MaxResultChars);
        Assert.Equal(maxResultLength + BenchmarkToolCallRecordLimits.ResultHeadroomChars, limits.MaxResultChars);
    }

    [Fact]
    public void Resolve_ExplicitMaxResultChars_OverridesTheDerivation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:ToolCallRecord:MaxResultChars", "9999" }
            })
            .Build();

        var limits = BenchmarkToolCallRecordLimits.Resolve(configuration, maxResultLength: 1000);

        Assert.Equal(9999, limits.MaxResultChars);
    }

    [Fact]
    public void Resolve_NoOverride_DerivesCapFromMaxResultLengthAlone()
    {
        // Benchmark:MaxResultLength defaults to 10,000; with no handler in the allowed set
        // declaring MaxResultLengthOverride, the cap is just the agent figure plus headroom.
        var limits = BenchmarkToolCallRecordLimits.Resolve(EmptyConfig(), maxResultLength: 10_000);

        Assert.Equal(12_000, limits.MaxResultChars);
    }

    [Fact]
    public void Resolve_OverrideLargerThanMaxResultLength_DerivesCapFromTheOverride()
    {
        // nethack_wiki_search declares MaxResultLengthOverride 16,140, above the 10,000
        // Benchmark:MaxResultLength default -- the cap must track the larger of the two, or a
        // full-yield result reaches the model whole while the record still cuts it.
        var limits = BenchmarkToolCallRecordLimits.Resolve(
            EmptyConfig(), maxResultLength: 10_000, largestAllowedOverride: 16_140);

        Assert.Equal(18_140, limits.MaxResultChars);
    }

    [Fact]
    public void Build_ResultAtOverriddenCap_IsStoredWhole_NotTruncated()
    {
        // A 12,221-character result -- above the 10,000 + 2,000 cap this same run would get
        // without the override, but under the 16,140 + 2,000 cap the override derives.
        string result = new string('w', 12_221);
        var limits = BenchmarkToolCallRecordLimits.Resolve(
            EmptyConfig(), maxResultLength: 10_000, largestAllowedOverride: 16_140);

        var row = BenchmarkToolCallRecorder.Build(new[] { Call(result: result) }, limits).Single();

        Assert.False(row.ResultTruncated);
        Assert.Equal(12_221, row.Result!.Length);
    }

    [Fact]
    public void Build_ResultOverCap_MarkerNamesStoredAndTotalCharacterCounts()
    {
        // The marker must be self-describing enough that an export reader can never mistake it
        // for ToolExecutor's own "... [Result truncated for length]" or
        // "... [Truncated: showing ...]" markers on the pre-storage payload the model saw.
        string result = new string('x', 500);

        var row = BenchmarkToolCallRecorder.Build(
            new[] { Call(result: result) }, Limits(maxResult: 100)).Single();

        Assert.True(row.ResultTruncated);
        Assert.True(row.Result!.Length <= 100);
        Assert.DoesNotContain("Result truncated for length", row.Result);
        Assert.Contains("Record truncated: stored", row.Result);
        Assert.Contains("of 500 characters", row.Result);

        // {stored} counts the kept prefix excluding the marker itself.
        int markerStart = row.Result.IndexOf("... [Record truncated:", System.StringComparison.Ordinal);
        Assert.True(markerStart >= 0);
        Assert.Equal(new string('x', markerStart), row.Result.Substring(0, markerStart));
        Assert.Contains($"stored {markerStart} of 500 characters", row.Result);
    }

    [Fact]
    public void Resolve_MaxArgsChars_DefaultsTo4000_AndHonoursItsConfigurationKey()
    {
        var defaults = BenchmarkToolCallRecordLimits.Resolve(EmptyConfig(), maxResultLength: 1000);
        Assert.Equal(4000, defaults.MaxArgsChars);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:ToolCallRecord:MaxArgsChars", "1234" }
            })
            .Build();
        var overridden = BenchmarkToolCallRecordLimits.Resolve(configuration, maxResultLength: 1000);
        Assert.Equal(1234, overridden.MaxArgsChars);
    }

    [Fact]
    public void Resolve_MaxErrorChars_DefaultsTo2000_AndHonoursItsConfigurationKey()
    {
        var defaults = BenchmarkToolCallRecordLimits.Resolve(EmptyConfig(), maxResultLength: 1000);
        Assert.Equal(2000, defaults.MaxErrorChars);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:ToolCallRecord:MaxErrorChars", "4321" }
            })
            .Build();
        var overridden = BenchmarkToolCallRecordLimits.Resolve(configuration, maxResultLength: 1000);
        Assert.Equal(4321, overridden.MaxErrorChars);
    }

    // --- Outcomes ---------------------------------------------------------------------------

    private static BenchmarkRunAnswerToolCall Row(string? status, string? error = null)
        => new() { Status = status, Error = error };

    [Fact]
    public void Outcomes_ClassifiesSucceededFailedAndRefused_AndTheThreeBucketsSumToTheRowCount()
    {
        var rows = new[]
        {
            Row("completed"),
            Row("completed"),
            Row("error"),
            Row("error", error: BenchmarkToolCallRecorder.BudgetRefusalMarker),
            Row("error", error: BenchmarkToolCallRecorder.PerQuestionBudgetRefusalMarker)
        };

        var (succeeded, failed, refused) = BenchmarkToolCallRecorder.Outcomes(rows);

        Assert.Equal(2, succeeded);
        Assert.Equal(1, failed);
        Assert.Equal(2, refused);
        Assert.Equal(rows.Length, succeeded + failed + refused);
    }

    [Fact]
    public void Outcomes_BothBudgetRefusalWordings_CountAsRefused_PerQuestionWordingIsNotMisclassifiedAsFailed()
    {
        var sessionScoped = Row("error", error: $"blah {BenchmarkToolCallRecorder.BudgetRefusalMarker} blah");
        var perQuestionScoped = Row("error", error: $"blah {BenchmarkToolCallRecorder.PerQuestionBudgetRefusalMarker} blah");

        var (_, failedSession, refusedSession) = BenchmarkToolCallRecorder.Outcomes(new[] { sessionScoped });
        var (_, failedPerQuestion, refusedPerQuestion) = BenchmarkToolCallRecorder.Outcomes(new[] { perQuestionScoped });

        Assert.Equal(0, failedSession);
        Assert.Equal(1, refusedSession);

        // A benchmark run is always per-question scoped, so this is the wording every real
        // refusal carries — the exact misclassification the outcome split exists to prevent.
        Assert.Equal(0, failedPerQuestion);
        Assert.Equal(1, refusedPerQuestion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    public void Outcomes_AnyNonCompletedStatus_CountsAsFailed(string? status)
    {
        var (succeeded, failed, refused) = BenchmarkToolCallRecorder.Outcomes(new[] { Row(status) });

        Assert.Equal(0, succeeded);
        Assert.Equal(1, failed);
        Assert.Equal(0, refused);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("COMPLETED")]
    [InlineData("Completed")]
    public void Outcomes_CompletedStatus_MatchesCaseInsensitively(string status)
    {
        var (succeeded, failed, refused) = BenchmarkToolCallRecorder.Outcomes(new[] { Row(status) });

        Assert.Equal(1, succeeded);
        Assert.Equal(0, failed);
        Assert.Equal(0, refused);
    }

    [Fact]
    public void Outcomes_ResultTooLargeConversion_CountsAsFailed_NotRefused()
    {
        // ToolExecutor converts a JSON result over the result cap into this exact error and a
        // non-completed status; it is the tool actually running and choking on its own output,
        // not a refusal, and this is the concrete defect the outcome split was written to fix.
        var row = Row("error", error: "Result too large (48213 chars). Please use a narrower search query to get fewer results.");

        var (succeeded, failed, refused) = BenchmarkToolCallRecorder.Outcomes(new[] { row });

        Assert.Equal(0, succeeded);
        Assert.Equal(1, failed);
        Assert.Equal(0, refused);
    }

    [Fact]
    public void Outcomes_EmptyInput_YieldsAllZeroes()
    {
        var (succeeded, failed, refused) = BenchmarkToolCallRecorder.Outcomes(System.Array.Empty<BenchmarkRunAnswerToolCall>());

        Assert.Equal((0, 0, 0), (succeeded, failed, refused));
    }

    // --- ToolRegistry.LargestResultLengthOverride ------------------------------------------

    [Fact]
    public void ToolRegistry_LargestResultLengthOverride_PicksTheMaxAmongTheNamedTools()
    {
        var registry = new ToolRegistry(
            new IToolHandler[]
            {
                new FakeToolHandler("wiki_search", 13_000),
                new FakeToolHandler("nethack_wiki_search", 16_140),
                new FakeToolHandler("source_code_search", null)
            },
            new FakeClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);

        var largest = registry.LargestResultLengthOverride(
            new[] { "wiki_search", "nethack_wiki_search", "source_code_search" });

        Assert.Equal(16_140, largest);
    }

    [Fact]
    public void ToolRegistry_LargestResultLengthOverride_IgnoresToolsOutsideTheNamedSet()
    {
        var registry = new ToolRegistry(
            new IToolHandler[]
            {
                new FakeToolHandler("wiki_search", 13_000),
                new FakeToolHandler("nethack_wiki_search", 16_140)
            },
            new FakeClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);

        // Only wiki_search is in the allowed set, so nethack_wiki_search's larger override must
        // not leak in.
        var largest = registry.LargestResultLengthOverride(new[] { "wiki_search" });

        Assert.Equal(13_000, largest);
    }

    [Fact]
    public void ToolRegistry_LargestResultLengthOverride_NullWhenNoNamedToolDeclaresOne()
    {
        var registry = new ToolRegistry(
            new IToolHandler[] { new FakeToolHandler("source_code_search", null) },
            new FakeClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);

        Assert.Null(registry.LargestResultLengthOverride(new[] { "source_code_search" }));
    }

    private sealed class FakeToolHandler : IToolHandler
    {
        public FakeToolHandler(string toolName, int? maxResultLengthOverride)
        {
            ToolName = toolName;
            MaxResultLengthOverride = maxResultLengthOverride;
        }

        public string ToolName { get; }
        public string Description { get; set; } = string.Empty;
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public int? MaxResultLengthOverride { get; }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true });
    }

    private sealed class FakeClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(
            Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = true });
    }
}
