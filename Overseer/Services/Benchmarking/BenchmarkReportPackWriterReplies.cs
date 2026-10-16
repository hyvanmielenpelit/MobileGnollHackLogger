namespace Overseer.Services.Benchmarking;

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The writer's raw replies of a report-pack job as one Markdown file, built on request from the job Overseer holds in
/// memory: a header naming the job, the writer and the versions, then one section per document and, under it, one per
/// writer call with its timing, tokens, cost, finish reason and reply, each reply in a fence longer than any backtick
/// run it holds. Lines end with LF. The file is internal: the replies come before Overseer's checks, removals and
/// additions.
/// </summary>
public static class BenchmarkReportPackWriterReplies
{
    /// <summary>The Markdown of every writer reply the job holds, captured at <paramref name="capturedUtc"/>.</summary>
    public static string BuildMarkdown(BenchmarkReportPackJob job, DateTime capturedUtc)
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
        Line(sb, $"- Writer: {WriterText(job)} · Writer prompt SHA-256: {job.WriterPromptSha256 ?? "not recorded"}");

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
                Line(sb, $"### Call {Number(c + 1)}: {call.Kind}");
                Line(sb);
                Line(sb, CallLine(call, reply));
                Line(sb);
                if (reply.Text == null)
                {
                    Line(sb, reply.Error == null ? "No reply." : $"No reply (provider error: {reply.Error}).");
                    continue;
                }

                string fence = new('`', Math.Max(3, LongestBacktickRun(reply.Text) + 1));
                Line(sb, fence + "json");
                Line(sb, reply.Text.Replace("\r\n", "\n").Replace('\r', '\n'));
                Line(sb, fence);
                if (reply.Truncated)
                {
                    Line(sb);
                    Line(sb, $"(cut at {BenchmarkReportPackJob.MaxFailedReplyChars.ToString("N0", CultureInfo.InvariantCulture)} characters)");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>Started 2026-10-16 11:28:39 UTC · 2 min 22 s · 191,795 in / 0 cached / 5,666 out tokens · $0.44 · finish STOP ·
    /// reply 22,113 characters</c>; the parts the call did not record are left out.
    /// </summary>
    private static string CallLine(BenchmarkReportPackCallDto call, BenchmarkReportPackCallReply reply)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            "Started " + Time(call.StartedAtUtc) + " UTC",
            Duration(call.DurationMs),
            $"{Count(call.InputTokens)} in / {Count(call.CachedInputTokens)} cached / {Count(call.OutputTokens)} out tokens",
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

    /// <summary><c>42 s</c> under a minute, else <c>2 min 05 s</c>.</summary>
    private static string Duration(long ms)
    {
        long seconds = (long)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero);
        return seconds < 60
            ? seconds.ToString(CultureInfo.InvariantCulture) + " s"
            : (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min " + (seconds % 60).ToString("00", CultureInfo.InvariantCulture) + " s";
    }

    /// <summary><c>$0.44</c>; below a cent, four decimals (<c>$0.0029</c>).</summary>
    private static string Cost(double usd)
        => "$" + usd.ToString(usd >= 0.01 || usd == 0 ? "0.00" : "0.0000", CultureInfo.InvariantCulture);

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb, string text = "") => sb.Append(text).Append('\n');
}
