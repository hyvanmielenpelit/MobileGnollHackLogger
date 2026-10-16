namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The writer's raw replies of a report-pack job as one Markdown file, built on request from the job Overseer holds in
/// memory: a header naming the job, the writer, its output limit and the versions, then one section per document and,
/// under it, one per writer call with its timing, tokens, cost, finish reason, the user message a later turn added and
/// the reply, each request and reply in a fence longer than any backtick run it holds, and after the calls a table of
/// every token the replies cite with what the stored document prints for it. Lines end with LF. The file is internal: the replies come before Overseer's checks, removals and
/// additions.
/// </summary>
public static class BenchmarkReportPackWriterReplies
{
    private static readonly Regex TokenRegex = new(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The Markdown of every writer reply the job holds, captured at <paramref name="capturedUtc"/>; <paramref name="stored"/>
    /// holds the stored documents by id, for each document's table of the tokens its replies cite.
    /// </summary>
    public static string BuildMarkdown(BenchmarkReportPackJob job, DateTime capturedUtc, IReadOnlyDictionary<long, BenchmarkReportDocument>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(job);

        var sb = new StringBuilder();
        string subject = string.IsNullOrWhiteSpace(job.SubjectKey) ? job.SubjectLabel : $"{job.SubjectLabel} ({job.SubjectKey})";
        Line(sb, "# Writer replies: " + subject);
        Line(sb);
        Line(sb, "INTERNAL — the writer's raw replies, before Overseer's checks, removals and additions. Do not share outside the "
                 + "Overseer team. The stored document is what Overseer kept of them.");
        Line(sb);
        Line(sb, $"- Job: {job.Id} · Pack: {job.PackId} · Captured (UTC): {Time(capturedUtc)}");
        Line(sb, $"- Writer: {WriterText(job)}"
                 + (job.WriterMaxOutputTokens is int limit ? $" · Output limit {Count(limit)} tokens (reasoning included)" : string.Empty)
                 + $" · Writer prompt SHA-256: {job.WriterPromptSha256 ?? "not recorded"}");

        var documents = job.WriterReplies();
        bool rootCause = documents.Any(d => d.Document.Audience == BenchmarkReportAudience.RootCauseAnalysis);
        Line(sb, $"- Report format {Number(BenchmarkReportPackRenderer.CurrentFormatVersion(job.Scope))}"
                 + (rootCause ? $" · Engine {Number(RootCause.RootCauseEngine.Version)}" : string.Empty));

        for (int d = 0; d < documents.Count; d++)
        {
            var (document, calls) = documents[d];
            string label = string.IsNullOrWhiteSpace(document.SubjectLabel) ? job.SubjectLabel : document.SubjectLabel;
            Line(sb);
            Line(sb, $"## Document {Number(d + 1)}: {BenchmarkReportRenderService.AudienceName(document.Audience)} — {label}");
            Line(sb);
            Line(sb, "Status: " + StatusText(document.Status)
                     + (document.DocumentId is long id ? " · Document #" + id.ToString(CultureInfo.InvariantCulture) : string.Empty));

            if (calls.Count == 0)
            {
                Line(sb);
                Line(sb, "No writer call was made.");
                continue;
            }

            for (int c = 0; c < calls.Count; c++)
            {
                var (call, reply) = calls[c];
                Line(sb);
                Line(sb, $"### Call {Number(c + 1)}: {BenchmarkReportPackCallKinds.Label(call.Kind)}");
                Line(sb);
                Line(sb, CallLine(call, reply));
                Line(sb);
                if (reply.Request != null)
                {
                    Line(sb, "Request (the message this turn added):");
                    Line(sb);
                    Fenced(sb, reply.Request, "text", reply.RequestTruncated);
                    Line(sb);
                }
                if (reply.Text == null)
                {
                    Line(sb, reply.Error == null ? "No reply." : $"No reply (provider error: {reply.Error}).");
                    continue;
                }

                Fenced(sb, reply.Text, "json", reply.Truncated);
            }

            TokensCited(sb, calls.Select(c => c.Item2.Text), document.DocumentId is long stores && stored != null && stored.TryGetValue(stores, out var row) ? row : null);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The <c>#### Tokens cited</c> table of one document: every distinct token of its replies, in the order first cited,
    /// with what <paramref name="document"/> prints for it (<see cref="BenchmarkReportPackRenderer.TokenDisplays"/>), or
    /// <c>not in the fact sheet</c>; one line instead when no stored document is at hand.
    /// </summary>
    private static void TokensCited(StringBuilder sb, IEnumerable<string?> replies, BenchmarkReportDocument? document)
    {
        var tokens = replies
            .Where(r => r != null)
            .SelectMany(r => TokenRegex.Matches(r!).Select(m => m.Groups[1].Value.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tokens.Count == 0) return;

        Line(sb);
        Line(sb, "#### Tokens cited");
        Line(sb);
        if (document == null)
        {
            Line(sb, "The table is unavailable: Overseer stored no document for these replies.");
            return;
        }

        Line(sb, "| Token | Prints |");
        Line(sb, "|---|---|");
        foreach (var (token, display) in BenchmarkReportPackRenderer.TokenDisplays(document, tokens))
        {
            Line(sb, "| `{{" + token + "}}` | " + Cell(display ?? "not in the fact sheet") + " |");
        }
    }

    /// <summary>A table cell: one line, with its pipes escaped.</summary>
    private static string Cell(string text)
        => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|");

    /// <summary>
    /// <paramref name="text"/> in a fence longer than any backtick run it holds, followed by a line saying so when it
    /// was cut.
    /// </summary>
    private static void Fenced(StringBuilder sb, string text, string language, bool truncated)
    {
        string fence = new('`', Math.Max(3, LongestBacktickRun(text) + 1));
        Line(sb, fence + language);
        Line(sb, text.Replace("\r\n", "\n").Replace('\r', '\n'));
        Line(sb, fence);
        if (truncated)
        {
            Line(sb);
            Line(sb, $"(cut at {BenchmarkReportPackJob.MaxFailedReplyChars.ToString("N0", CultureInfo.InvariantCulture)} characters)");
        }
    }

    /// <summary>
    /// <c>Started 2026-10-16 11:28:39 UTC · 2 min 22 s · 191,795 in / 0 cached / 5,666 out tokens (4,120 reasoning) · $0.44 ·
    /// finish STOP · reply 22,113 characters</c>; the parts the call did not record are left out.
    /// </summary>
    private static string CallLine(BenchmarkReportPackCallDto call, BenchmarkReportPackCallReply reply)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            "Started " + Time(call.StartedAtUtc) + " UTC",
            Duration(call.DurationMs),
            $"{Count(call.InputTokens)} in / {Count(call.CachedInputTokens)} cached / {Count(call.OutputTokens)} out tokens"
                + (call.ReasoningTokens is long reasoning and > 0 ? $" ({Count(reasoning)} reasoning)" : string.Empty),
            call.CostUsd is double cost ? Cost(cost) : "cost unknown"
        };
        if (!string.IsNullOrWhiteSpace(call.FinishReason)) parts.Add("finish " + call.FinishReason);
        if (reply.Text != null) parts.Add("reply " + Count(reply.Length) + " characters");
        return string.Join(" · ", parts);
    }

    /// <summary><c>Name (Provider, model-id; high)</c>, each part only when the job recorded it.</summary>
    private static string WriterText(BenchmarkReportPackJob job)
    {
        string name = string.IsNullOrWhiteSpace(job.WriterDisplayName) ? job.WriterModelId ?? "not recorded" : job.WriterDisplayName;
        var identity = new[] { job.WriterProvider, job.WriterModelId }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (identity.Count == 0) return name;
        return $"{name} ({string.Join(", ", identity)}"
               + (string.IsNullOrWhiteSpace(job.WriterThinkingLevel) ? string.Empty : "; " + job.WriterThinkingLevel)
               + ")";
    }

    /// <summary>A progress status in words: <c>CompletedWithWarnings</c> becomes "Completed with warnings".</summary>
    private static string StatusText(string status)
    {
        if (string.IsNullOrEmpty(status)) return "Not recorded";
        var sb = new StringBuilder();
        for (int i = 0; i < status.Length; i++)
        {
            char ch = status[i];
            if (i > 0 && char.IsUpper(ch))
            {
                sb.Append(' ').Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>The longest run of backticks in <paramref name="text"/>.</summary>
    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int run = 0;
        foreach (char ch in text)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary><c>42 s</c>, <c>2 min 05 s</c> or <c>1 h 02 min</c>, cut to whole seconds as the diagnostics cut them.</summary>
    private static string Duration(long ms)
    {
        long seconds = Math.Max(0, ms / 1000);
        if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + " s";
        long minutes = seconds / 60;
        return minutes < 60
            ? minutes.ToString(CultureInfo.InvariantCulture) + " min " + (seconds % 60).ToString("00", CultureInfo.InvariantCulture) + " s"
            : (minutes / 60).ToString(CultureInfo.InvariantCulture) + " h " + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + " min";
    }

    /// <summary><c>$0.44</c>; below a cent, four decimals (<c>$0.0029</c>).</summary>
    private static string Cost(double usd)
        => "$" + usd.ToString(usd >= 0.01 || usd == 0 ? "0.00" : "0.0000", CultureInfo.InvariantCulture);

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb, string text = "") => sb.Append(text).Append('\n');
}
