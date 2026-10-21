namespace Overseer.Services.Feedback;

using System;
using System.Text;
using Overseer.Services.Privacy.Dlp;

/// <summary>
/// The data scrubber for chat feedback reports: replaces every credential, card or account number,
/// e-mail address and phone number in the free-text fields with <see cref="Marker"/> before storage.
/// </summary>
public static class FeedbackRedaction
{
    public const string Marker = "[redacted]";

    /// <summary><see cref="DlpPolicy.Defaults"/> plus the two contact-detail classes.</summary>
    internal static readonly DlpPolicy Policy = new() { Emails = true, PhoneNumbers = true };

    /// <summary>
    /// Scrubs <paramref name="args"/> in place and sets <see cref="ReportFeedbackArguments.Redacted"/>
    /// when anything was replaced. A field the marker lengthened past its maximum is cut again.
    /// </summary>
    public static void Apply(DlpScannerService scanner, ReportFeedbackArguments args)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(args);

        bool redacted = false;
        bool truncated = args.Truncated;

        args.Subject = Fit(Scrub(scanner, args.Subject, ref redacted), ReportFeedbackArguments.SubjectMax, ref truncated) ?? Marker;
        args.Summary = Fit(Scrub(scanner, args.Summary, ref redacted), ReportFeedbackArguments.SummaryMax, ref truncated) ?? Marker;
        args.Details = Fit(Scrub(scanner, args.Details, ref redacted), ReportFeedbackArguments.DetailsMax, ref truncated);
        args.SearchedFor = Fit(Scrub(scanner, args.SearchedFor, ref redacted), ReportFeedbackArguments.SearchedForMax, ref truncated);
        args.ExpectedLocation = Fit(Scrub(scanner, args.ExpectedLocation, ref redacted), ReportFeedbackArguments.ExpectedLocationMax, ref truncated);
        args.SuspectedCauseNote = Fit(Scrub(scanner, args.SuspectedCauseNote, ref redacted), ReportFeedbackArguments.SuspectedCauseNoteMax, ref truncated);

        args.Truncated = truncated;
        if (redacted) args.Redacted = true;
    }

    private static string? Scrub(DlpScannerService scanner, string? text, ref bool redacted)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var findings = scanner.Scan(text, Policy);
        if (findings.Count == 0) return text;

        // From the end backwards, so the earlier findings' offsets stay valid.
        var builder = new StringBuilder(text);
        for (int i = findings.Count - 1; i >= 0; i--)
        {
            builder.Remove(findings[i].Start, findings[i].Length).Insert(findings[i].Start, Marker);
        }
        redacted = true;
        return builder.ToString();
    }

    private static string? Fit(string? text, int max, ref bool truncated)
        => ReportFeedbackArguments.Cut(text, max, ref truncated);
}
