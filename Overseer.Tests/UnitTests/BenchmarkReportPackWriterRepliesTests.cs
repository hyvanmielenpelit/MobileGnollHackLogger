namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The writer's replies as one Markdown file, built from the job held in memory: the header, a section per document
/// and per call, a reply that holds a code fence kept inside a longer one, the no-reply and cut lines, and LF only.
/// The endpoint that serves it is tested with the service, in <see cref="BenchmarkReportPackServiceTests"/>.
/// </summary>
public class BenchmarkReportPackWriterRepliesTests
{
    private static readonly DateTime Captured = new(2026, 10, 16, 11, 35, 42, DateTimeKind.Utc);
    private static readonly Guid PackId = new("6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5");

    private const string FencedReply = "Here it is:\n```json\n{ \"headline\": \"The models differ.\" }\n```";

    private static BenchmarkReportPackCallDto Call(string kind, int second) => new()
    {
        Kind = kind,
        StartedAtUtc = new DateTime(2026, 10, 16, 11, 28, second, DateTimeKind.Utc),
        DurationMs = 142_000,
        InputTokens = 191_795,
        CachedInputTokens = 0,
        OutputTokens = 5_666,
        CostUsd = 0.44,
        FinishReason = "STOP"
    };

    private static (BenchmarkReportPackJob Job, string LongReply) Job()
    {
        var job = new BenchmarkReportPackJob
        {
            Id = "job-1",
            PackId = PackId,
            SubjectKey = "comparison:4",
            SubjectLabel = "Comparison #4",
            Scope = BenchmarkReportScope.Comparison,
            WriterDisplayName = "Writer One",
            WriterProvider = "ReportWriterTest",
            WriterModelId = "writer-1",
            WriterThinkingLevel = "high",
            WriterPromptSha256 = new string('a', 64),
            Cts = new CancellationTokenSource(),
            Documents =
            {
                new BenchmarkReportPackDocumentProgress
                {
                    Audience = BenchmarkReportAudience.RootCauseAnalysis, SubjectLabel = "Comparison #4",
                    Status = BenchmarkReportPackDocumentStatus.CompletedWithWarnings, DocumentId = 65
                },
                new BenchmarkReportPackDocumentProgress
                {
                    Audience = BenchmarkReportAudience.ExecutiveSummary, SubjectLabel = "Comparison #4",
                    Status = BenchmarkReportPackDocumentStatus.Failed
                },
            }
        };

        string longReply = new('x', BenchmarkReportPackJob.MaxFailedReplyChars + 5);
        job.RecordCall(job.Documents[0], Call(BenchmarkReportPackCallKinds.Draft, 39), FencedReply);
        job.RecordCall(job.Documents[0], Call(BenchmarkReportPackCallKinds.Revision, 50), reply: null, error: "boom");
        job.RecordCall(job.Documents[1], Call(BenchmarkReportPackCallKinds.Draft, 55), longReply + "\r\n");
        return (job, longReply);
    }

    [Fact]
    public void TheHeader_NamesTheJobTheWriterAndTheVersions_AndMarksTheFileInternal()
    {
        var (job, _) = Job();

        string markdown = BenchmarkReportPackWriterReplies.BuildMarkdown(job, Captured);

        Assert.StartsWith("# Writer replies: Comparison #4 (comparison:4)\n\nINTERNAL — the writer's raw replies, before Overseer's checks, removals and additions.", markdown, StringComparison.Ordinal);
        Assert.Contains($"- Job: job-1 · Pack: {PackId} · Captured (UTC): 2026-10-16 11:35:42\n", markdown, StringComparison.Ordinal);
        Assert.Contains("- Writer: Writer One (ReportWriterTest, writer-1; high) · Writer prompt SHA-256: " + new string('a', 64) + "\n", markdown, StringComparison.Ordinal);
        Assert.Contains($"- Report format {BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.Comparison)} · Engine {RootCauseEngine.Version}\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EachDocumentAndCall_HasItsSection_InOrder()
    {
        var (job, _) = Job();

        string markdown = BenchmarkReportPackWriterReplies.BuildMarkdown(job, Captured);

        string first = $"## Document 1: {BenchmarkReportRenderService.AudienceName(BenchmarkReportAudience.RootCauseAnalysis)} — Comparison #4\n\nStatus: Completed with warnings · Document #65\n";
        string second = $"## Document 2: {BenchmarkReportRenderService.AudienceName(BenchmarkReportAudience.ExecutiveSummary)} — Comparison #4\n\nStatus: Failed\n";
        Assert.Contains(first, markdown, StringComparison.Ordinal);
        Assert.Contains(second, markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf(first, StringComparison.Ordinal) < markdown.IndexOf(second, StringComparison.Ordinal));
        Assert.Equal(2, markdown.Split("### Call 1: Draft\n").Length - 1);
        Assert.Contains("### Call 2: Revision\n", markdown, StringComparison.Ordinal);
        Assert.Contains("Started 2026-10-16 11:28:39 UTC · 2 min 22 s · 191,795 in / 0 cached / 5,666 out tokens · $0.44 · finish STOP · reply "
                        + FencedReply.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " characters\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplyHoldingACodeFence_StaysInsideALongerFence()
    {
        var (job, _) = Job();

        string markdown = BenchmarkReportPackWriterReplies.BuildMarkdown(job, Captured);

        Assert.Contains("````json\n" + FencedReply + "\n````\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallWithoutAReply_SaysSo_AndACutReply_EndsWithTheCutLine()
    {
        var (job, longReply) = Job();

        string markdown = BenchmarkReportPackWriterReplies.BuildMarkdown(job, Captured);

        Assert.Contains("### Call 2: Revision\n\nStarted 2026-10-16 11:28:50 UTC", markdown, StringComparison.Ordinal);
        Assert.Contains("\n\nNo reply (provider error: boom).\n", markdown, StringComparison.Ordinal);
        Assert.Contains(longReply[..BenchmarkReportPackJob.MaxFailedReplyChars] + "\n```\n\n(cut at 200,000 characters)\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(longReply, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFile_UsesLineFeedsOnly()
    {
        var (job, _) = Job();

        string markdown = BenchmarkReportPackWriterReplies.BuildMarkdown(job, Captured);

        Assert.DoesNotContain('\r', markdown);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJob_KeepsEachReplyCut_AndPutsOnlyItsLengthOnTheCall()
    {
        var (job, longReply) = Job();

        var replies = job.Documents[0].CallReplies;
        Assert.Equal(FencedReply, replies[0].Text);
        Assert.False(replies[0].Truncated);
        Assert.Null(replies[1].Text);
        Assert.Equal("boom", replies[1].Error);
        var cut = Assert.Single(job.Documents[1].CallReplies);
        Assert.True(cut.Truncated);
        Assert.Equal(BenchmarkReportPackJob.MaxFailedReplyChars, cut.Text!.Length);
        Assert.Equal(longReply.Length + 2, cut.Length);

        var calls = job.ToDto().Documents.SelectMany(d => d.Calls).ToList();
        Assert.Equal(new int?[] { FencedReply.Length, null, longReply.Length + 2 }, calls.Select(c => c.ReplyChars));
    }
}
