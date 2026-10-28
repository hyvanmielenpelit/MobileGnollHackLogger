namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Globalization;
using Overseer.Models;

/// <summary>
/// The example files the Feedback tab's formats dialog shows and downloads. The issues example is a fixed synthetic
/// document rendered by <see cref="FeedbackYamlExport"/>, so it cannot drift from the format; the resolutions example
/// is a hand-written version-2 file that the tests validate with <see cref="FeedbackResolutionService.TryValidate"/>.
/// Every id, fingerprint, time and text is invented, and every text starts with <c>Example:</c>.
/// </summary>
public static class FeedbackFormatExamples
{
    public const string FirstFingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string SecondFingerprint = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private static readonly DateTime ExportedAt = new(2026, 10, 28, 9, 15, 0, DateTimeKind.Utc);

    /// <summary>Both examples of <paramref name="source"/>, a <see cref="FeedbackTransferSources"/> value.</summary>
    public static AdminFeedbackFormatExamplesDto For(string source)
    {
        var document = Issues(source);
        return new AdminFeedbackFormatExamplesDto
        {
            Issues = new AdminFeedbackFormatExampleDto
            {
                FileName = "example-" + FeedbackYamlExport.FileName(source, document.ExportedAtUtc),
                Content = FeedbackYamlExport.Build(document),
                MediaType = FeedbackYamlExport.MediaType
            },
            Resolutions = new AdminFeedbackFormatExampleDto
            {
                FileName = $"example-agent-feedback-resolutions-{source}_v1.yaml",
                Content = Resolutions(source),
                MediaType = FeedbackYamlExport.MediaType
            }
        };
    }

    /// <summary>A document with one issue and two occurrences.</summary>
    public static FeedbackExportDocument Issues(string source)
    {
        bool gnollBench = source == FeedbackTransferSources.GnollBench;
        var newest = new DateTime(2026, 10, 27, 18, 42, 5, DateTimeKind.Utc);
        var older = new DateTime(2026, 10, 25, 11, 3, 40, DateTimeKind.Utc);
        const string subject = "Example: The dilithium crystal's base price is missing";
        const string summary = "Example: The wiki page on dilithium crystals gives no base price, so the price could not be answered.";

        FeedbackExportOccurrence Occurrence(DateTime at, bool own, long run) => new()
        {
            OccurredUtc = at,
            Provider = "anthropic",
            Model = gnollBench ? "Example Model (example-model-1)" : "example-model-1",
            ModelSource = gnollBench ? null : FeedbackModelSources.System,
            Configuration = gnollBench ? null : "Example configuration",
            Version = gnollBench ? null : "1.4.3",
            RunId = gnollBench ? run : null,
            Suite = gnollBench ? "Example suite" : null,
            QuestionId = gnollBench ? 7 : null,
            Condition = gnollBench ? "production" : null,
            Variant = gnollBench ? GnollBenchFeedbackFilter.NoVariant : null,
            Corpus = gnollBench ? GnollBenchFeedbackFilter.LiveCorpus : null,
            Harness = gnollBench ? "60" : null,
            NoticedVia = "tool_result",
            SuspectedCause = "missing_data",
            RelatedTools = new[] { "wiki_search", "wiki_view" },
            Subject = own ? "Example: No base price for dilithium crystals" : subject,
            Summary = summary,
            Details = "Example: wiki_view returned the page, but its Properties section lists weight and material only.\nThe price-identification table does not mention it either.",
            SearchedFor = "Example: dilithium crystal price",
            ExpectedLocation = "Example: Items/Dilithium crystal.md, the Properties section",
            SuspectedCauseNote = "Example: The page may predate the price table.",
            QuestionText = gnollBench ? "Example: What does a dilithium crystal sell for?" : null
        };

        var issue = new FeedbackExportIssue
        {
            Id = gnollBench ? null : 1,
            Fingerprint = FirstFingerprint,
            Category = "missing_information",
            Status = "acknowledged",
            FirstSeenUtc = older,
            LastSeenUtc = newest,
            OccurrenceCount = 2,
            RetainedCount = gnollBench ? null : 2,
            DistinctUserCount = gnollBench ? null : 2,
            RegressionCount = gnollBench ? null : 0,
            TriagedUtc = new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc),
            Models = new[] { gnollBench ? "Example Model (example-model-1)" : "example-model-1" },
            Providers = new[] { "anthropic" },
            ModelSources = gnollBench ? Array.Empty<string>() : new[] { "system 2" },
            Configurations = gnollBench ? Array.Empty<string>() : new[] { "Example configuration" },
            Versions = gnollBench ? Array.Empty<string>() : new[] { "1.4.3" },
            RelatedTools = new[] { "wiki_search", "wiki_view" },
            SuspectedCauses = new[] { "missing_data" },
            RunCount = gnollBench ? 2 : null,
            RunIds = gnollBench ? new long[] { 102, 101 } : Array.Empty<long>(),
            Suites = gnollBench ? new[] { "Example suite" } : Array.Empty<string>(),
            Conditions = gnollBench ? new[] { "production" } : Array.Empty<string>(),
            Variants = gnollBench ? new[] { GnollBenchFeedbackFilter.NoVariant } : Array.Empty<string>(),
            Corpora = gnollBench ? new[] { GnollBenchFeedbackFilter.LiveCorpus } : Array.Empty<string>(),
            Harnesses = gnollBench ? new[] { "60" } : Array.Empty<string>(),
            Subject = subject,
            Summary = summary,
            TriageNote = "Example: Checking the wiki page against src/objects.c.",
            Occurrences = new[] { Occurrence(newest, true, 102), Occurrence(older, false, 101) }
        };

        return new FeedbackExportDocument
        {
            Source = source,
            ExportedAtUtc = ExportedAt,
            OverseerVersion = "1.4.3",
            OccurrencesPerIssue = FeedbackExportService.DefaultOccurrencesPerIssue,
            IncludeQuestionText = true,
            Issues = new[] { issue }
        };
    }

    /// <summary>A version-2 resolution file: one resolved item with its resolution message, one acknowledged item with a note.</summary>
    public static string Resolutions(string source)
    {
        bool overseer = source == FeedbackTransferSources.Overseer;
        string exportedAt = FeedbackMarkdownExport.FormatTimestamp(ExportedAt);
        string issueName = overseer ? "Feedback #1" : "GnollBench feedback " + FirstFingerprint[..FeedbackMarkdownExport.FingerprintHeadingLength];
        var lines = new List<string>
        {
            "# Example resolution file. Every id, fingerprint and text in it is invented.",
            $"format: {FeedbackMarkdownExport.ResolutionFormatName}",
            $"version: {FeedbackMarkdownExport.ResolutionFormatVersion.ToString(CultureInfo.InvariantCulture)}",
            $"source: {source}",
            $"exportedAtUtc: \"{exportedAt}\"",
            "issues:"
        };
        lines.Add(overseer ? "  - id: 1" : $"  - fingerprint: \"{FirstFingerprint}\"");
        if (overseer) lines.Add($"    fingerprint: \"{FirstFingerprint}\"");
        lines.AddRange(new[]
        {
            "    status: resolved",
            "    resolvedVia: [wiki]",
            "    resolution: |-",
            $"      {issueName}: Example: Add the dilithium crystal's base price to the GnollHack wiki",
            "",
            "      Resolved via: GnollHack wiki",
            "      Changed: Example: Items/Dilithium crystal.md, the base price, validated against src/objects.c.",
            "      Verified: Example: the wiki validation session passed; wiki_search finds the price.",
            "      Live: about ten minutes after the wiki clone on the server is pulled."
        });
        lines.Add(overseer ? "  - id: 2" : $"  - fingerprint: \"{SecondFingerprint}\"");
        if (overseer) lines.Add($"    fingerprint: \"{SecondFingerprint}\"");
        lines.AddRange(new[]
        {
            "    status: acknowledged",
            "    note: \"Example: Reproduced; the fix needs a GnollHack release.\""
        });
        return string.Join("\n", lines) + "\n";
    }
}
