namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// <see cref="BenchmarkSelfReports"/> turns a benchmark turn's <c>report_feedback</c> calls into
/// <see cref="BenchmarkRunAnswerSelfReport"/> rows. Pure and static: no database or provider.
/// </summary>
public class BenchmarkSelfReportsTests
{
    private static bool IsKnownTool(string name)
        => string.Equals(name, "monster_lookup", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "wiki_search", StringComparison.OrdinalIgnoreCase);

    private static ChatMessageToolCall Call(
        string? name,
        string? argsText = "{}",
        string? status = "completed",
        string? error = null,
        int? batchIndex = null)
        => new()
        {
            Name = name,
            ArgsText = argsText,
            Status = status,
            Error = error,
            BatchIndex = batchIndex
        };

    private static string Args(
        string category,
        string subject,
        string summary,
        string[]? relatedTools = null,
        string? expectedLocation = null,
        string? suspectedCause = null)
        => JsonSerializer.Serialize(new
        {
            category,
            subject,
            summary,
            related_tools = relatedTools ?? Array.Empty<string>(),
            expected_location = expectedLocation,
            suspected_cause = suspectedCause
        }, OmitNulls);

    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static string FingerprintOf(string argsJson)
    {
        Assert.True(ReportFeedbackArguments.TryParse(argsJson, IsKnownTool, out var args, out _));
        return FeedbackFingerprint.Compute(args);
    }

    [Theory]
    [InlineData("report_feedback", true)]
    [InlineData("Report_Feedback", true)]
    [InlineData("wiki_search", false)]
    [InlineData(null, false)]
    public void IsSelfReport_MatchesTheFeedbackToolOnly_CaseInsensitively(string? name, bool expected)
    {
        Assert.Equal(expected, BenchmarkSelfReports.IsSelfReport(name));
    }

    [Fact]
    public void Extract_TakesSelfReportsInEmissionOrder_WithDenseSortOrderAndFingerprints()
    {
        string first = Args("missing_information", "Dilithium crystal", "The wiki page has no price.",
            expectedLocation: "wiki page Dilithium crystal", suspectedCause: "corpus_or_tool_limit");
        string second = Args("tool_problem", "monster_lookup", "The lookup returned an empty object.",
            relatedTools: new[] { "monster_lookup", "not_a_tool", "wiki_search" });

        var calls = new[]
        {
            Call("wiki_search", batchIndex: 0),
            Call("report_feedback", first, batchIndex: 0),
            Call("monster_lookup", batchIndex: 1),
            Call("report_feedback", second, batchIndex: 2)
        };

        var rows = BenchmarkSelfReports.Extract(calls, IsKnownTool);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.SortOrder));
        Assert.Equal(new int?[] { 0, 2 }, rows.Select(r => r.IterationIndex));
        Assert.All(rows, r => Assert.True(r.Accepted));
        Assert.All(rows, r => Assert.Null(r.Error));

        Assert.Equal("missing_information", rows[0].Category);
        Assert.Equal("Dilithium crystal", rows[0].Subject);
        Assert.Equal("The wiki page has no price.", rows[0].Summary);
        Assert.Equal("wiki page Dilithium crystal", rows[0].ExpectedLocation);
        Assert.Equal("corpus_or_tool_limit", rows[0].SuspectedCause);
        Assert.Equal(FingerprintOf(first), rows[0].Fingerprint);
        Assert.Equal(64, rows[0].Fingerprint!.Length);

        // Unknown names are dropped; the rest keep their order, as a JSON array.
        Assert.Equal("tool_problem", rows[1].Category);
        Assert.Equal(FingerprintOf(second), rows[1].Fingerprint);
        Assert.Equal(new[] { "monster_lookup", "wiki_search" }, JsonSerializer.Deserialize<string[]>(rows[1].RelatedToolsJson!));
        Assert.NotEqual(rows[0].Fingerprint, rows[1].Fingerprint);
    }

    [Fact]
    public void Extract_AcceptedIsTheCompletedStatus_AndARefusalKeepsItsErrorCapped()
    {
        string args = Args("own_mistake", "Elbereth", "I said Elbereth scares @ humans.");
        string limit = "Feedback limit for this turn reached (3). Do not report again in this turn; continue with the user's request.";
        string longError = new string('x', 800);

        var rows = BenchmarkSelfReports.Extract(new[]
        {
            Call("report_feedback", args, status: "completed"),
            Call("report_feedback", args, status: "error", error: limit),
            Call("report_feedback", args, status: "error", error: longError)
        }, IsKnownTool);

        Assert.True(rows[0].Accepted);
        Assert.Null(rows[0].Error);

        Assert.False(rows[1].Accepted);
        Assert.Equal(limit, rows[1].Error);

        // A refused call whose arguments parsed still carries its fingerprint.
        Assert.Equal(rows[0].Fingerprint, rows[1].Fingerprint);
        Assert.Equal("own_mistake", rows[1].Category);

        Assert.False(rows[2].Accepted);
        Assert.Equal(500, rows[2].Error!.Length);
    }

    [Theory]
    [InlineData("{\"category\":\"not_a_category\",\"subject\":\"Elbereth\",\"summary\":\"Something.\"}")]
    [InlineData("{\"category\":\"missing_information\",\"subject\":\"  \",\"summary\":\"Something.\"}")]
    [InlineData("not json")]
    [InlineData(null)]
    public void Extract_ArgumentsThatDoNotParse_LeaveFingerprintAndCategoryNull(string? argsText)
    {
        var rows = BenchmarkSelfReports.Extract(new[]
        {
            Call("report_feedback", argsText, status: "error", error: "category is required."),
            Call("report_feedback", argsText, status: "error", error: null)
        }, IsKnownTool);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.False(r.Accepted);
            Assert.Null(r.Fingerprint);
            Assert.Null(r.Category);
            Assert.Null(r.Subject);
            Assert.Null(r.Summary);
            Assert.Null(r.RelatedToolsJson);
            Assert.False(r.Truncated);
        });

        // The call's own error is kept; without one, the parse error stands in.
        Assert.Equal("category is required.", rows[0].Error);
        Assert.False(string.IsNullOrWhiteSpace(rows[1].Error));
    }

    [Fact]
    public void Extract_RecordsTruncation_AndStoresTheCutText()
    {
        string longSubject = string.Concat(Enumerable.Repeat("cockatrice corpse ", 20));
        string args = Args("missing_information", longSubject, "No weight given.");

        var row = Assert.Single(BenchmarkSelfReports.Extract(new[] { Call("report_feedback", args) }, IsKnownTool));

        Assert.True(row.Truncated);
        Assert.NotNull(row.Subject);
        Assert.True(row.Subject!.Length <= ReportFeedbackArguments.SubjectMax);
        Assert.NotNull(row.Fingerprint);
    }

    [Fact]
    public void Extract_WithNoSelfReports_IsEmpty()
    {
        Assert.Empty(BenchmarkSelfReports.Extract(new[] { Call("wiki_search"), Call("monster_lookup") }, IsKnownTool));
        Assert.Empty(BenchmarkSelfReports.Extract(null!, IsKnownTool));
    }
}
