namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Text.Json;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// Validation of <c>report_feedback</c> arguments: required fields named in the error, silent cuts
/// recorded as <see cref="ReportFeedbackArguments.Truncated"/>, and the lenient optional fields.
/// </summary>
public class ReportFeedbackArgumentsTests
{
    private static readonly string[] KnownTools = { "wiki_search", "wiki_view", "source_code_search", "monster_lookup" };

    private static bool IsKnown(string name) => KnownTools.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static ReportFeedbackArguments Parse(object value)
    {
        Assert.True(ReportFeedbackArguments.TryParse(Json(value), IsKnown, out var args, out string? error), error);
        Assert.Null(error);
        return args!;
    }

    private static string Fail(string? json)
    {
        Assert.False(ReportFeedbackArguments.TryParse(json, IsKnown, out var args, out string? error));
        Assert.Null(args);
        return Assert.IsType<string>(error);
    }

    [Fact]
    public void AValidReport_ParsesEveryField_Trimmed()
    {
        var args = Parse(new
        {
            category = " own_mistake ",
            subject = "  Dilithium crystal ",
            summary = "I said it was worthless. ",
            details = " details ",
            searched_for = " wiki_search 'dilithium' ",
            expected_location = " wiki page Dilithium crystal ",
            related_tools = new[] { "wiki_search" },
            noticed_via = "user_correction",
            suspected_cause = "did_not_verify",
            suspected_cause_note = " note "
        });

        Assert.Equal(FeedbackCategories.OwnMistake, args.Category);
        Assert.Equal("Dilithium crystal", args.Subject);
        Assert.Equal("I said it was worthless.", args.Summary);
        Assert.Equal("details", args.Details);
        Assert.Equal("wiki_search 'dilithium'", args.SearchedFor);
        Assert.Equal("wiki page Dilithium crystal", args.ExpectedLocation);
        Assert.Equal(new[] { "wiki_search" }, args.RelatedTools);
        Assert.Equal("user_correction", args.NoticedVia);
        Assert.Equal("did_not_verify", args.SuspectedCause);
        Assert.Equal("note", args.SuspectedCauseNote);
        Assert.False(args.Truncated);
        Assert.False(args.Redacted);
    }

    [Fact]
    public void AMissingCategory_IsAnError_NamingTheField()
    {
        Assert.Contains("'category'", Fail(Json(new { subject = "Elbereth", summary = "Not found." })));
    }

    [Fact]
    public void AnUnknownCategory_IsAnError_NamingTheField()
    {
        Assert.Contains("'category'", Fail(Json(new { category = "bug_report", subject = "Elbereth", summary = "Not found." })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSubject_IsAnError_NamingTheField(string? subject)
    {
        Assert.Contains("'subject'", Fail(Json(new { category = "missing_information", subject, summary = "Not found." })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ABlankSummary_IsAnError_NamingTheField(string? summary)
    {
        Assert.Contains("'summary'", Fail(Json(new { category = "missing_information", subject = "Elbereth", summary })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void MissingOrMalformedArguments_AreAnError(string? json)
    {
        Assert.False(string.IsNullOrWhiteSpace(Fail(json)));
    }

    [Fact]
    public void LongFields_AreCut_WithoutAnError_AndMarkTruncated()
    {
        var args = Parse(new
        {
            category = "missing_information",
            subject = new string('s', 200),
            summary = new string('m', 400),
            details = new string('d', 2500),
            searched_for = new string('q', 600),
            expected_location = new string('l', 400),
            suspected_cause_note = new string('n', 600)
        });

        Assert.Equal(ReportFeedbackArguments.SubjectMax, args.Subject.Length);
        Assert.Equal(ReportFeedbackArguments.SummaryMax, args.Summary.Length);
        Assert.Equal(ReportFeedbackArguments.DetailsMax, args.Details!.Length);
        Assert.Equal(ReportFeedbackArguments.SearchedForMax, args.SearchedFor!.Length);
        Assert.Equal(ReportFeedbackArguments.ExpectedLocationMax, args.ExpectedLocation!.Length);
        Assert.Equal(ReportFeedbackArguments.SuspectedCauseNoteMax, args.SuspectedCauseNote!.Length);
        Assert.True(args.Truncated);
    }

    [Fact]
    public void OneLongField_IsEnoughToMarkTruncated()
    {
        var args = Parse(new { category = "missing_information", subject = "Elbereth", summary = "Short.", details = new string('d', 2001) });

        Assert.True(args.Truncated);
        Assert.Equal("Elbereth", args.Subject);
    }

    [Fact]
    public void RelatedTools_KeepOnlyKnownNames_DeduplicatedInOrder_AtMostEight()
    {
        var args = Parse(new
        {
            category = "tool_problem",
            subject = "source_code_search",
            summary = "It returned malformed output.",
            related_tools = new[] { "source_code_search", "no_such_tool", "WIKI_SEARCH", "wiki_search", "monster_lookup", "" }
        });

        Assert.Equal(new[] { "source_code_search", "WIKI_SEARCH", "monster_lookup" }, args.RelatedTools);

        var many = Parse(new
        {
            category = "tool_problem",
            subject = "x",
            summary = "y",
            related_tools = Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? "wiki_search" : "wiki_view").ToArray()
        });
        Assert.Equal(new[] { "wiki_search", "wiki_view" }, many.RelatedTools);
    }

    [Fact]
    public void RelatedTools_AreCappedAtEight()
    {
        string[] names = Enumerable.Range(1, 10).Select(i => "tool_" + i).ToArray();
        Assert.True(ReportFeedbackArguments.TryParse(
            Json(new { category = "tool_problem", subject = "x", summary = "y", related_tools = names }),
            _ => true, out var args, out _));

        Assert.Equal(names.Take(ReportFeedbackArguments.RelatedToolsMax), args!.RelatedTools);
    }

    [Theory]
    [InlineData("made_up_cause")]
    [InlineData("OTHER")]
    public void AnUnknownSuspectedCause_BecomesOther(string cause)
    {
        var args = Parse(new { category = "missing_information", subject = "Elbereth", summary = "Not found.", suspected_cause = cause });

        Assert.Equal(ReportFeedbackArguments.OtherCause, args.SuspectedCause);
    }

    [Fact]
    public void ABlankSuspectedCause_StaysNull()
    {
        var args = Parse(new { category = "missing_information", subject = "Elbereth", summary = "Not found.", suspected_cause = " " });

        Assert.Null(args.SuspectedCause);
    }

    [Theory]
    [InlineData("missing_information")]
    [InlineData("tool_problem")]
    public void NoticedVia_IsIgnored_OutsideOwnMistake(string category)
    {
        var args = Parse(new { category, subject = "Elbereth", summary = "Not found.", noticed_via = "self_review" });

        Assert.Null(args.NoticedVia);
    }

    [Fact]
    public void AnUnknownNoticedVia_IsDropped()
    {
        var args = Parse(new { category = "own_mistake", subject = "Elbereth", summary = "Wrong.", noticed_via = "intuition" });

        Assert.Null(args.NoticedVia);
    }

    [Fact]
    public void TheJsonElementOverload_MatchesTheStringOverload()
    {
        string json = Json(new { category = "missing_information", subject = "Elbereth", summary = "Not found." });
        using var document = JsonDocument.Parse(json);

        Assert.True(ReportFeedbackArguments.TryParse(document.RootElement, IsKnown, out var args, out _));
        Assert.Equal("Elbereth", args!.Subject);
    }

    [Fact]
    public void FeedbackCategories_KnowTheThreeWireValues()
    {
        Assert.Equal(new[] { "missing_information", "tool_problem", "own_mistake" }, FeedbackCategories.All);
        Assert.True(FeedbackCategories.IsKnown("tool_problem"));
        Assert.False(FeedbackCategories.IsKnown("Tool_Problem"));
        Assert.False(FeedbackCategories.IsKnown(null));
    }
}
