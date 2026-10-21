namespace Overseer.Tests.UnitTests;

using Microsoft.Extensions.Configuration;
using Overseer.Services.Feedback;
using Overseer.Services.Privacy.Dlp;
using Xunit;

/// <summary>
/// The data scrubber of chat feedback reports: credentials and contact details become
/// <see cref="FeedbackRedaction.Marker"/>, and the rest of the text is left as it was.
/// </summary>
public class FeedbackRedactionTests
{
    private static readonly DlpScannerService Scanner = new(new ConfigurationBuilder().Build());

    private static ReportFeedbackArguments Args(string summary, string? details = null) => new()
    {
        Category = FeedbackCategories.MissingInformation,
        Subject = "Elbereth",
        Summary = summary,
        Details = details
    };

    [Fact]
    public void AnApiKeyShapedValue_IsRedacted()
    {
        var args = Args("The user pasted " + DlpScannerServiceTests.AwsKey + " into the chat.");

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal("The user pasted [redacted] into the chat.", args.Summary);
        Assert.DoesNotContain(DlpScannerServiceTests.AwsKey, args.Summary);
        Assert.True(args.Redacted);
    }

    [Fact]
    public void AnEmailAddress_IsRedacted()
    {
        var args = Args("Short summary.", details: "Contact was ada@example.com about it.");

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal("Contact was [redacted] about it.", args.Details);
        Assert.True(args.Redacted);
    }

    [Fact]
    public void APhoneNumber_IsRedacted()
    {
        var args = Args("The user said to call 555-123-4567 now.");

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal("The user said to call [redacted] now.", args.Summary);
        Assert.True(args.Redacted);
    }

    [Fact]
    public void CleanText_IsUnchanged_AndNotMarkedRedacted()
    {
        var args = Args("The wiki has no page for the Elbereth engraving rules.", details: "Searched wiki_search for 'Elbereth'.");
        args.SearchedFor = "wiki_search Elbereth";
        args.ExpectedLocation = "wiki page Elbereth";
        args.SuspectedCauseNote = "The page may not exist.";

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal("Elbereth", args.Subject);
        Assert.Equal("The wiki has no page for the Elbereth engraving rules.", args.Summary);
        Assert.Equal("Searched wiki_search for 'Elbereth'.", args.Details);
        Assert.Equal("wiki_search Elbereth", args.SearchedFor);
        Assert.Equal("wiki page Elbereth", args.ExpectedLocation);
        Assert.Equal("The page may not exist.", args.SuspectedCauseNote);
        Assert.False(args.Redacted);
        Assert.False(args.Truncated);
    }

    [Fact]
    public void TwoFindingsInOneField_AreBothReplaced_AndTheTextBetweenStaysIntact()
    {
        var args = Args("Write to ada@example.com or call 555-123-4567 today.");

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal("Write to [redacted] or call [redacted] today.", args.Summary);
        Assert.True(args.Redacted);
    }

    [Fact]
    public void EveryFreeTextField_IsScrubbed()
    {
        var args = Args("Summary with ada@example.com.");
        args.Subject = "ada@example.com";
        args.SearchedFor = "ada@example.com";
        args.ExpectedLocation = "ada@example.com";
        args.SuspectedCauseNote = "ada@example.com";
        args.Details = "ada@example.com";

        FeedbackRedaction.Apply(Scanner, args);

        Assert.Equal(FeedbackRedaction.Marker, args.Subject);
        Assert.Equal("Summary with [redacted].", args.Summary);
        Assert.Equal(FeedbackRedaction.Marker, args.Details);
        Assert.Equal(FeedbackRedaction.Marker, args.SearchedFor);
        Assert.Equal(FeedbackRedaction.Marker, args.ExpectedLocation);
        Assert.Equal(FeedbackRedaction.Marker, args.SuspectedCauseNote);
    }
}
