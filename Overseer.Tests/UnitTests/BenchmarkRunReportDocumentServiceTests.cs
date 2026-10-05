namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The run-completion documents: when a finished run's three AI-written documents are written, that
/// they are written once, how a busy report-pack slot, the compliance guard, a missing writer and a
/// failed document settle the run's status, that a download never reaches the writer, the restart
/// settlement, the write-now endpoint with its document choice and same-provider acknowledgment,
/// cancellation, the job view, the estimate and the run-scoped delete. The writer is a fake; nothing
/// calls a provider.
/// </summary>
public class BenchmarkRunReportDocumentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BenchmarkReportAudience[] AllAudiences =
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief
    };

    // --- Scheduling ----------------------------------------------------------------------------------

    [Fact]
    public async Task ARunWithoutAReportWriter_SchedulesNothing()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
        Assert.Empty(await h.DocumentsAsync());
    }

    [Fact]
    public async Task ARunThatIsNotCompleted_SchedulesNothing()
    {
        await using var h = await Harness.CreateAsync();
        await h.UpdateRunAsync(r => r.Status = BenchmarkRunStatus.CompletedWithErrors);

        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ACompletedRun_GetsEveryDocument_StoredAsRunCompletionDocuments()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.ScheduleIfDue(h.RunId);

        var documents = await h.DocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, d.Origin);
            Assert.Equal(BenchmarkRunReportDocumentService.SubjectKeyOf(h.RunId), d.SubjectKey);
            Assert.False(d.SameProviderAcknowledged);
        });
        Assert.Equal(AllAudiences, documents.Select(d => d.Audience).OrderBy(a => a));

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, run.ReportDocumentsStatus);
        Assert.Null(run.ReportDocumentsMessage);
        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(3, h.Writer.Calls);
        Assert.Equal(h.WriterConfig.Id, h.Writer.LastWriterConfigId);
    }

    [Fact]
    public async Task ASecondSchedule_CallsTheWriterNoMore()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.ScheduleIfDue(h.RunId);
        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(3, h.Writer.Calls);
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task ABusySlot_MakesTheJobWaitAsPending_ThenItRuns()
    {
        await using var h = await Harness.CreateAsync();
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));

        var job = h.Service.ScheduleIfDue(h.RunId);

        await WaitUntilAsync(() => h.Jobs.WaitingCount == 1);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, await h.StatusAsync());
        Assert.False(job.IsCompleted);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.True(h.Service.IsActive(h.RunId));

        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
        Assert.False(h.Service.IsActive(h.RunId));
    }

    [Fact]
    public async Task AComplianceRefusal_SkipsTheDocuments_WithTheGuardsReason()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);

        await h.Service.ScheduleIfDue(h.RunId);

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Skipped, run.ReportDocumentsStatus);
        Assert.Contains("Hourly benchmark run cap reached", run.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Empty(await h.DocumentsAsync());
    }

    [Fact]
    public async Task ADeletedWriter_FailsTheDocuments_WithAClearMessage()
    {
        await using var h = await Harness.CreateAsync();
        await using (var db = new ApplicationDbContext(h.Options))
        {
            db.SystemAiApiConfigurations.Remove(await db.SystemAiApiConfigurations.SingleAsync(c => c.Id == h.WriterConfig.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await h.Service.ScheduleIfDue(h.RunId);

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, run.ReportDocumentsStatus);
        Assert.Equal(BenchmarkRunReportDocumentService.WriterUnavailableMessage, run.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task OneFailedDocument_FailsTheRun_KeepsTheOthers_AndWriteNowWritesOnlyTheMissingOne()
    {
        await using var h = await Harness.CreateAsync();
        h.Writer.FailAudience = BenchmarkReportAudience.TechnicalReport;

        await h.Service.ScheduleIfDue(h.RunId);

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, run.ReportDocumentsStatus);
        Assert.StartsWith("Report for AI Researchers and Developers: ", run.ReportDocumentsMessage);
        Assert.Equal(
            new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.InternalBrief },
            (await h.DocumentsAsync()).Select(d => d.Audience));

        // A failed run is not written again automatically.
        await h.Service.ScheduleIfDue(h.RunId);
        Assert.Equal(1, h.Writer.JobCalls);

        h.Writer.FailAudience = null;
        Assert.True(h.Service.TryStart(h.RunId, "user-1", null, false, out var completion));
        await completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(4, h.Writer.Calls);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, h.Writer.LastAudiences);
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    [Fact]
    public async Task ADocumentWithWarnings_CompletesTheRunWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        h.Writer.WarnAudience = BenchmarkReportAudience.ExecutiveSummary;

        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, await h.StatusAsync());
    }

    [Fact]
    public async Task ADownload_RendersTheStoredRow_WithoutCallingTheWriterOrRecordingUsage()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.ScheduleIfDue(h.RunId);
        int calls = h.Writer.Calls;

        await using var db = new ApplicationDbContext(h.Options);
        var render = new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);
        foreach (var document in await h.DocumentsAsync())
        {
            var (markdown, notFound, refusal) = await render.RenderAsync(document.Id, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Full,
                PeerNaming = BenchmarkReportPeerNaming.Named
            }, Ct);

            Assert.False(notFound);
            Assert.Null(refusal);
            Assert.Contains(document.Audience == BenchmarkReportAudience.InternalBrief ? "## 6. Fact sheet" : "## Evaluation terms", markdown);
        }

        Assert.Equal(calls, h.Writer.Calls);
        Assert.Empty(await db.SystemAiUsageLogs.ToListAsync(Ct));

        var listed = await render.ListAsync(null, h.RunId, null, Ct);
        Assert.Equal(3, listed.Count);
        Assert.All(listed, d => Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, d.Origin));
    }

    [Fact]
    public async Task ScheduledJob_OfAnAcknowledgedLaunch_MarksItsDocumentsAcknowledged()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");
        await h.UpdateRunAsync(r => r.ReportWriterModelConfigurationId = sameProviderId);

        await h.Service.ScheduleIfDue(h.RunId);

        var documents = await h.DocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.True(d.SameProviderAcknowledged);
            Assert.Equal(sameProviderId, d.WriterConfigId);
        });
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    // --- Restart -------------------------------------------------------------------------------------

    [Fact]
    public async Task SettleInterrupted_FailsEveryPendingOrWritingRun_WithTheRestartMessage()
    {
        await using var h = await Harness.CreateAsync();
        await using (var db = new ApplicationDbContext(h.Options))
        {
            var runs = await db.BenchmarkRuns.OrderBy(r => r.Id).ToListAsync(Ct);
            runs[0].ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Pending;
            runs[1].ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing;
            runs[2].ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed;
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = new ApplicationDbContext(h.Options))
        {
            Assert.Equal(2, await BenchmarkRunReportDocumentService.SettleInterruptedAsync(db, Ct));
        }

        await using (var db = new ApplicationDbContext(h.Options))
        {
            var runs = await db.BenchmarkRuns.OrderBy(r => r.Id).ToListAsync(Ct);
            Assert.All(runs.Take(2), r =>
            {
                Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, r.ReportDocumentsStatus);
                Assert.Equal(BenchmarkRunReportDocumentService.RestartMessage, r.ReportDocumentsMessage);
            });
            Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, runs[2].ReportDocumentsStatus);
        }
    }

    // --- The report-pack slot ------------------------------------------------------------------------

    [Fact]
    public async Task AManualStart_IsRefusedWhileARunJobWaits_AndAcceptedOnceTheWaitIsCanceled()
    {
        var jobs = new BenchmarkReportPackJobManager(TimeSpan.FromHours(1));
        var running = new BenchmarkReportPackJob { SubjectLabel = "Running", Cts = new CancellationTokenSource() };
        Assert.True(jobs.TryStart(running, out _));

        var queued = new BenchmarkReportPackJob { SubjectLabel = "Queued", Cts = new CancellationTokenSource() };
        using var cts = new CancellationTokenSource();
        var wait = jobs.WaitForSlotAsync(queued, cts.Token);
        Assert.Equal(1, jobs.WaitingCount);

        // The running job ends; the queued one has not polled yet and still holds its place.
        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        var manual = new BenchmarkReportPackJob { SubjectLabel = "Manual", Cts = new CancellationTokenSource() };
        Assert.False(jobs.TryStart(manual, out var existing));
        Assert.Same(queued, existing);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(0, jobs.WaitingCount);
        Assert.True(jobs.TryStart(manual, out _));
    }

    [Fact]
    public async Task QueuedJobs_TakeTheSlotInArrivalOrder()
    {
        var jobs = new BenchmarkReportPackJobManager(TimeSpan.FromMilliseconds(10));
        var running = new BenchmarkReportPackJob { SubjectLabel = "Running", Cts = new CancellationTokenSource() };
        Assert.True(jobs.TryStart(running, out _));

        var first = new BenchmarkReportPackJob { SubjectLabel = "First", Cts = new CancellationTokenSource() };
        var second = new BenchmarkReportPackJob { SubjectLabel = "Second", Cts = new CancellationTokenSource() };
        var firstWait = jobs.WaitForSlotAsync(first, Ct);
        var secondWait = jobs.WaitForSlotAsync(second, Ct);

        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await firstWait.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Same(first, jobs.Current);
        await Task.Delay(50, Ct);
        Assert.False(secondWait.IsCompleted);

        first.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await secondWait.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Same(second, jobs.Current);
    }

    [Fact]
    public void OutcomeOf_NamesTheFirstFailedDocument()
    {
        var job = new BenchmarkReportPackJob
        {
            Documents =
            {
                new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.ExecutiveSummary, Status = BenchmarkReportPackDocumentStatus.CompletedWithWarnings },
                new BenchmarkReportPackDocumentProgress
                {
                    Audience = BenchmarkReportAudience.TechnicalReport,
                    Status = BenchmarkReportPackDocumentStatus.Failed,
                    ErrorMessage = "The writer's reply could not be parsed."
                }
            }
        };
        job.SetStatus(BenchmarkReportPackJobStatus.CompletedWithErrors);

        var (status, message) = BenchmarkRunReportDocumentService.OutcomeOf(job);

        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, status);
        Assert.Equal("Report for AI Researchers and Developers: The writer's reply could not be parsed.", message);
    }

    [Fact]
    public void OutcomeOf_ACanceledJob_IsCanceled_NotFailed()
    {
        BenchmarkReportPackJob Canceled(BenchmarkReportPackDocumentStatus executive, BenchmarkReportPackDocumentStatus researcher)
        {
            var job = new BenchmarkReportPackJob
            {
                Documents =
                {
                    new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.ExecutiveSummary, Status = executive },
                    new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.TechnicalReport, Status = researcher }
                }
            };
            job.SetStatus(BenchmarkReportPackJobStatus.Canceled);
            return job;
        }

        Assert.Equal(
            (BenchmarkRunReportDocumentsStatus.Canceled, "Canceled while writing. The Executive Summary was written and is kept."),
            BenchmarkRunReportDocumentService.OutcomeOf(Canceled(BenchmarkReportPackDocumentStatus.Completed, BenchmarkReportPackDocumentStatus.Canceled)));
        Assert.Equal(
            (BenchmarkRunReportDocumentsStatus.Canceled, "Canceled while writing. Nothing was written."),
            BenchmarkRunReportDocumentService.OutcomeOf(Canceled(BenchmarkReportPackDocumentStatus.Canceled, BenchmarkReportPackDocumentStatus.Canceled)));
        Assert.Equal(
            (BenchmarkRunReportDocumentsStatus.Canceled,
                "Canceled while writing. The Executive Summary and the Report for AI Researchers and Developers were written and are kept."),
            BenchmarkRunReportDocumentService.OutcomeOf(Canceled(BenchmarkReportPackDocumentStatus.CompletedWithWarnings, BenchmarkReportPackDocumentStatus.Completed)));

        var three = new BenchmarkReportPackJob
        {
            Documents =
            {
                new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.ExecutiveSummary, Status = BenchmarkReportPackDocumentStatus.Completed },
                new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.TechnicalReport, Status = BenchmarkReportPackDocumentStatus.Completed },
                new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.InternalBrief, Status = BenchmarkReportPackDocumentStatus.Completed }
            }
        };
        three.SetStatus(BenchmarkReportPackJobStatus.Canceled);
        Assert.Equal(
            (BenchmarkRunReportDocumentsStatus.Canceled,
                "Canceled while writing. The Executive Summary, the Report for AI Researchers and Developers and the Internal Improvement Brief were written and are kept."),
            BenchmarkRunReportDocumentService.OutcomeOf(three));
    }

    // --- Writer checks -------------------------------------------------------------------------------

    private static SystemAiApiConfiguration CheckConfig(string provider, string modelId, bool enabled = true) => new()
    {
        Provider = provider, ModelId = modelId, DisplayName = modelId, ModelRole = 4, IsEnabled = enabled, EncryptedApiKey = "dummy_encrypted"
    };

    private static readonly SystemAiApiConfiguration CheckCandidate = new() { Provider = "OpenAI", ModelId = "gpt-5.6-luna", DisplayName = "GPT Luna" };

    [Fact]
    public void WriterRefusal_RefusesAnUnusableWriter_AndTheModelUnderTest_ButNotItsProvider()
    {
        var guard = new BenchmarkComplianceGuard(new ConfigurationBuilder().Build(), null!);

        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage, BenchmarkRunReportDocumentService.WriterRefusal(null, CheckCandidate, guard));
        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage,
            BenchmarkRunReportDocumentService.WriterRefusal(CheckConfig("Anthropic", "claude-opus-5-5", enabled: false), CheckCandidate, guard));
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage,
            BenchmarkRunReportDocumentService.WriterRefusal(CheckConfig("OpenAI", "gpt-5.6-luna"), CheckCandidate, guard));
        Assert.Null(BenchmarkRunReportDocumentService.WriterRefusal(CheckConfig("openai", "gpt-5.6-sol"), CheckCandidate, guard));
        Assert.Null(BenchmarkRunReportDocumentService.WriterRefusal(CheckConfig("Anthropic", "claude-opus-5-5"), CheckCandidate, guard));
    }

    [Fact]
    public void WriterWarning_WarnsForTheCandidatesProvider_Only()
    {
        var guard = new BenchmarkComplianceGuard(new ConfigurationBuilder().Build(), null!);

        Assert.Equal(
            "gpt-5.6-sol is from OpenAI, the provider of the model under test. Its reports may describe that model more favorably than an independent writer would.",
            BenchmarkRunReportDocumentService.WriterWarning(CheckConfig("openai", "gpt-5.6-sol"), CheckCandidate, guard));
        Assert.Null(BenchmarkRunReportDocumentService.WriterWarning(CheckConfig("Anthropic", "claude-opus-5-5"), CheckCandidate, guard));
        Assert.Null(BenchmarkRunReportDocumentService.WriterWarning(null, CheckCandidate, guard));
    }

    // --- Write now -----------------------------------------------------------------------------------

    [Fact]
    public async Task WriteNow_SetsTheWriter_AnswersAccepted_AndWritesEveryDocument()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        var result = await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value);
        Assert.Equal(h.RunId, response.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, response.Status);
        Assert.Equal(AllAudiences, response.Audiences);

        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        var run = await h.RunAsync();
        Assert.Equal(h.WriterConfig.Id, run.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, run.ReportDocumentsStatus);
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task WriteNow_WritesOnlyTheRequestedDocument_AndLeavesTheOthersMissing()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().WriteRunReportDocuments(h.RunId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.WriterConfig.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.TechnicalReport }
        }, Ct));
        Assert.Equal(
            new[] { BenchmarkReportAudience.TechnicalReport },
            Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value).Audiences);

        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        var written = Assert.Single(await h.DocumentsAsync());
        Assert.Equal(BenchmarkReportAudience.TechnicalReport, written.Audience);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, h.Writer.LastAudiences);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());

        await using var db = new ApplicationDbContext(h.Options);
        Assert.Equal(
            new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.InternalBrief },
            await BenchmarkRunReportDocumentService.MissingAudiencesAsync(db, h.RunId, Ct));
    }

    [Fact]
    public async Task WriteNow_WritesTheInternalBriefOnItsOwn()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().WriteRunReportDocuments(h.RunId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.WriterConfig.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.InternalBrief }
        }, Ct));
        Assert.Equal(
            new[] { BenchmarkReportAudience.InternalBrief },
            Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value).Audiences);

        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        var written = Assert.Single(await h.DocumentsAsync());
        Assert.Equal(BenchmarkReportAudience.InternalBrief, written.Audience);
        Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, written.Origin);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    [Fact]
    public async Task WriteNow_RefusesARequestedDocumentThatIsAlreadyWritten_WithConflict()
    {
        await using var h = await Harness.CreateAsync();
        h.Writer.FailAudience = BenchmarkReportAudience.TechnicalReport;
        await h.Service.ScheduleIfDue(h.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, await h.StatusAsync());

        WriteRunReportDocumentsRequest Request(params BenchmarkReportAudience[] audiences) => new()
        {
            WriterModelConfigurationId = h.WriterConfig.Id,
            Audiences = audiences.ToList()
        };

        var executive = Assert.IsType<ConflictObjectResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, Request(BenchmarkReportAudience.ExecutiveSummary), Ct));
        Assert.Contains("The Executive Summary is already written. Delete it first to write it again.", JsonSerializer.Serialize(executive.Value));

        var all = Assert.IsType<ConflictObjectResult>(await h.Controller().WriteRunReportDocuments(h.RunId, Request(AllAudiences), Ct));
        Assert.Contains("The Executive Summary is already written.", JsonSerializer.Serialize(all.Value));
        Assert.Equal(1, h.Writer.JobCalls);

        h.Writer.FailAudience = null;
        Assert.IsType<AcceptedResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, Request(BenchmarkReportAudience.TechnicalReport), Ct));
        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    [Fact]
    public async Task WriteNow_RefusesAnAudienceThatIsNotARunCompletionDocument()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        foreach (var audiences in new[]
                 {
                     new List<BenchmarkReportAudience> { (BenchmarkReportAudience)99 },
                     new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary, (BenchmarkReportAudience)0 }
                 })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().WriteRunReportDocuments(h.RunId, new WriteRunReportDocumentsRequest
            {
                WriterModelConfigurationId = h.WriterConfig.Id,
                Audiences = audiences
            }, Ct));
            Assert.Contains(AdminBenchmarkReportPacksController.InvalidAudienceMessage, JsonSerializer.Serialize(bad.Value));
        }

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, run.ReportDocumentsStatus);
        Assert.Null(run.ReportWriterModelConfigurationId);
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task TwoRounds_WriteTheTwoDocumentsWithDifferentWriters()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);
        long secondWriterId = await h.AddConfigAsync("Google", "gemini-3.7-pro", "Gemini 3.7 Pro");

        async Task WriteAsync(long writerId, BenchmarkReportAudience audience)
        {
            Assert.IsType<AcceptedResult>(await h.Controller().WriteRunReportDocuments(h.RunId, new WriteRunReportDocumentsRequest
            {
                WriterModelConfigurationId = writerId,
                Audiences = new List<BenchmarkReportAudience> { audience }
            }, Ct));
            await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        }

        await WriteAsync(h.WriterConfig.Id, BenchmarkReportAudience.ExecutiveSummary);
        await WriteAsync(secondWriterId, BenchmarkReportAudience.TechnicalReport);

        var documents = await h.DocumentsAsync();
        Assert.Equal(2, documents.Count);
        var executive = documents.Single(d => d.Audience == BenchmarkReportAudience.ExecutiveSummary);
        var researcher = documents.Single(d => d.Audience == BenchmarkReportAudience.TechnicalReport);
        Assert.Equal(h.WriterConfig.Id, executive.WriterConfigId);
        Assert.Equal("Claude Opus 5.5", executive.WriterDisplayName);
        Assert.Equal(secondWriterId, researcher.WriterConfigId);
        Assert.Equal("Gemini 3.7 Pro", researcher.WriterDisplayName);
        Assert.Equal(2, h.Writer.JobCalls);

        var run = await h.RunAsync();
        Assert.Equal(secondWriterId, run.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, run.ReportDocumentsStatus);

        // The second round's job replaced the first in the job view.
        var view = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, view.Audiences);
        Assert.Equal(secondWriterId, view.WriterConfigId);
        Assert.Equal("Google", view.WriterProvider);
    }

    [Fact]
    public async Task WriteNow_AnswersConflict_WhenEveryDocumentExists_OrAJobIsPending()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.ScheduleIfDue(h.RunId);
        var request = new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id };

        var all = Assert.IsType<ConflictObjectResult>(await h.Controller().WriteRunReportDocuments(h.RunId, request, Ct));
        Assert.Contains(AdminBenchmarkReportPacksController.AllWrittenMessage, JsonSerializer.Serialize(all.Value));

        await h.UpdateRunAsync(r => r.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing);
        await using (var db = new ApplicationDbContext(h.Options))
        {
            db.BenchmarkReportDocuments.RemoveRange(await db.BenchmarkReportDocuments.ToListAsync(Ct));
            await db.SaveChangesAsync(Ct);
        }
        var pending = Assert.IsType<ConflictObjectResult>(await h.Controller().WriteRunReportDocuments(h.RunId, request, Ct));
        Assert.Contains("already being written", JsonSerializer.Serialize(pending.Value));
    }

    [Fact]
    public async Task WriteNow_AnswersConflictWithTheWarning_WhenNotAcknowledged_AndStartsWhenAcknowledged()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");

        var warned = Assert.IsType<ObjectResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = sameProviderId }, Ct));
        Assert.Equal(StatusCodes.Status409Conflict, warned.StatusCode);
        var warning = Assert.IsType<SameProviderWarningDto>(warned.Value);
        Assert.Equal("reportWriter", warning.Role);
        Assert.True(warning.SameProvider);
        Assert.Equal("OpenAI", warning.Provider);
        Assert.Equal("gpt-5.6-luna", warning.TestedModelDisplayName);
        Assert.Equal("GPT Sol", warning.AssessorModelDisplayName);
        Assert.Equal(
            "GPT Sol is from OpenAI, the provider of the model under test. Its reports may describe that model more favorably than an independent writer would.",
            warning.Message);

        var untouched = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, untouched.ReportDocumentsStatus);
        Assert.Null(untouched.ReportWriterModelConfigurationId);
        Assert.Equal(0, h.Writer.JobCalls);

        Assert.IsType<AcceptedResult>(await h.Controller().WriteRunReportDocuments(h.RunId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = sameProviderId,
            AcknowledgeSameProvider = true
        }, Ct));
        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));

        var documents = await h.DocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.True(d.SameProviderAcknowledged);
            Assert.Equal(sameProviderId, d.WriterConfigId);
        });
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    [Fact]
    public async Task WriteNow_RefusesARunWithoutSynthesis_AndAnUnknownRun()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        await h.UpdateRunAsync(r => r.AssessmentJson = null);
        var unfinished = Assert.IsType<BadRequestObjectResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct));
        Assert.Contains("final synthesis", JsonSerializer.Serialize(unfinished.Value));

        Assert.IsType<NotFoundResult>(await h.Controller().WriteRunReportDocuments(
            999999, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct));
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task WriteNow_AnswersTooManyRequests_AtTheSpendCap()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0, withWriter: false);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct));

        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    // --- Cancel --------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_WhileQueued_LeavesTheQueue_AndSettlesTheRunAsCanceled()
    {
        await using var h = await Harness.CreateAsync();
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));

        var job = h.Service.ScheduleIfDue(h.RunId);
        await WaitUntilAsync(() => h.Jobs.WaitingCount == 1);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().CancelRunReportJob(h.RunId, Ct));
        Assert.Equal(h.Clock.UtcNow, Assert.IsType<BenchmarkRunReportJobDto>(accepted.Value).CancelRequestedAtUtc);
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(0, h.Jobs.WaitingCount);
        Assert.Same(running, h.Jobs.Current);
        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, run.ReportDocumentsStatus);
        Assert.Equal(BenchmarkRunReportDocumentService.CanceledBeforeWritingMessage, run.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Empty(await h.DocumentsAsync());
        Assert.False(h.Service.IsActive(h.RunId));

        var view = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal("Finished", view.Phase);
        Assert.Null(view.SlotAcquiredAtUtc);
        Assert.NotNull(view.CancelRequestedAtUtc);
        Assert.Equal(nameof(BenchmarkReportPackJobStatus.Canceled), view.Job.Status);
        Assert.All(view.Job.Documents, d => Assert.Equal(nameof(BenchmarkReportPackDocumentStatus.Canceled), d.Status));
        Assert.Contains(view.Job.Log, l => l.Message == "Cancellation requested.");
    }

    [Fact]
    public async Task Cancel_WhileWriting_KeepsTheWrittenDocument_AndSettlesAsCanceled()
    {
        await using var h = await Harness.CreateAsync();
        h.Writer.BlockAfterFirst = true;

        var job = h.Service.ScheduleIfDue(h.RunId);
        await h.Writer.FirstStored.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("Writing", h.Service.TryGetJob(h.RunId)!.Phase);

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.Requested, h.Service.TryCancel(h.RunId));
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var kept = Assert.Single(await h.DocumentsAsync());
        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, kept.Audience);
        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, run.ReportDocumentsStatus);
        Assert.Equal("Canceled while writing. The Executive Summary was written and is kept.", run.ReportDocumentsMessage);

        var view = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal("Finished", view.Phase);
        Assert.Equal(nameof(BenchmarkReportPackJobStatus.Canceled), view.Job.Status);
        Assert.Equal(
            new[]
            {
                nameof(BenchmarkReportPackDocumentStatus.Completed),
                nameof(BenchmarkReportPackDocumentStatus.Canceled),
                nameof(BenchmarkReportPackDocumentStatus.Canceled)
            },
            view.Job.Documents.Select(d => d.Status));
    }

    [Fact]
    public async Task Cancel_WithNoJob_AnswersConflict()
    {
        await using var h = await Harness.CreateAsync();

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.NotFound, h.Service.TryCancel(h.RunId));
        var none = Assert.IsType<ConflictObjectResult>(await h.Controller().CancelRunReportJob(h.RunId, Ct));
        Assert.Contains("No report writing is in progress for this run.", JsonSerializer.Serialize(none.Value));
        Assert.IsType<NotFoundResult>(await h.Controller().CancelRunReportJob(999999, Ct));

        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.NotInProgress, h.Service.TryCancel(h.RunId));
        Assert.IsType<ConflictObjectResult>(await h.Controller().CancelRunReportJob(h.RunId, Ct));
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    // --- Job view ------------------------------------------------------------------------------------

    [Fact]
    public async Task JobView_ReportsPhaseQueuePositionTimesAndPerDocumentUsage()
    {
        await using var h = await Harness.CreateAsync();
        long secondRunId = h.Seeded.RunIds[1];
        await h.UpdateRunAsync(secondRunId, r =>
        {
            r.AssessmentJson = Harness.AssessmentJson;
            r.ReportWriterModelConfigurationId = h.WriterConfig.Id;
        });
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));

        var first = h.Service.ScheduleIfDue(h.RunId);
        await WaitUntilAsync(() => h.Jobs.WaitingCount == 1);
        var second = h.Service.ScheduleIfDue(secondRunId);
        await WaitUntilAsync(() => h.Jobs.WaitingCount == 2);

        var queued = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal("Queued", queued.Phase);
        Assert.Equal(0, queued.JobsAhead);
        Assert.Equal("Report Pack: Other", queued.BlockingJobLabel);
        Assert.Equal(h.Clock.UtcNow, queued.QueuedAtUtc);
        Assert.Null(queued.SlotAcquiredAtUtc);
        Assert.Null(queued.FinishedAtUtc);
        Assert.Null(queued.CancelRequestedAtUtc);
        Assert.Equal(AllAudiences, queued.Audiences);
        Assert.Equal(h.WriterConfig.Id, queued.WriterConfigId);
        Assert.Equal("Claude Opus 5.5", queued.WriterDisplayName);
        Assert.Equal("Anthropic", queued.WriterProvider);
        Assert.Equal("claude-opus-5-5", queued.WriterModelId);
        Assert.Equal(h.Clock.UtcNow, queued.ServerTimeUtc);
        Assert.Contains(queued.Job.Log, l => l.Message == "Queued for the report writer.");
        Assert.All(queued.Job.Documents, d =>
        {
            Assert.Null(d.StartedAtUtc);
            Assert.Equal(0, d.InputTokens);
        });
        Assert.Equal(1, h.Service.TryGetJob(secondRunId)!.JobsAhead);

        var ok = Assert.IsType<OkObjectResult>(await h.Controller().GetRunReportJob(h.RunId, Ct));
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, Assert.IsType<BenchmarkRunReportJobDto>(ok.Value).Status);

        // The first job writes while the second waits behind it.
        h.Writer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await WaitUntilAsync(() => h.Service.TryGetJob(h.RunId)!.Phase == "Writing");

        var writing = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal(h.Clock.UtcNow, writing.SlotAcquiredAtUtc);
        Assert.Null(writing.JobsAhead);
        Assert.Null(writing.BlockingJobLabel);
        var behind = h.Service.TryGetJob(secondRunId)!;
        Assert.Equal("Queued", behind.Phase);
        Assert.Equal(0, behind.JobsAhead);
        Assert.Equal($"Run #{h.RunId}: gpt-5.6-luna", behind.BlockingJobLabel);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Writer.Gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await second.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var finished = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal("Finished", finished.Phase);
        Assert.Equal(h.Clock.UtcNow, finished.FinishedAtUtc);
        Assert.Equal(nameof(BenchmarkReportPackJobStatus.Completed), finished.Job.Status);
        Assert.All(finished.Job.Documents, d =>
        {
            Assert.NotNull(d.StartedAtUtc);
            Assert.NotNull(d.CompletedAtUtc);
            Assert.Equal(1, d.ModelCalls);
            Assert.Equal(FakeWriter.InputTokensPerDocument, d.InputTokens);
            Assert.Equal(FakeWriter.OutputTokensPerDocument, d.OutputTokens);
            Assert.Equal((double)FakeWriter.CostPerDocument, d.CostUsd);
        });
        Assert.Equal(3 * FakeWriter.InputTokensPerDocument, finished.Job.InputTokens);
        Assert.Equal("Finished", h.Service.TryGetJob(secondRunId)!.Phase);
    }

    [Fact]
    public async Task JobView_IsKeptAfterTheJobFinishes_AndPrunedAfterTheRetention()
    {
        await using var h = await Harness.CreateAsync();
        Assert.IsType<NoContentResult>(await h.Controller().GetRunReportJob(h.RunId, Ct));

        await h.Service.ScheduleIfDue(h.RunId);
        Assert.Equal("Finished", h.Service.TryGetJob(h.RunId)!.Phase);

        h.Clock.Advance(BenchmarkRunReportDocumentService.FinishedJobRetention - TimeSpan.FromMinutes(1));
        var kept = Assert.IsType<BenchmarkRunReportJobDto>(Assert.IsType<OkObjectResult>(await h.Controller().GetRunReportJob(h.RunId, Ct)).Value);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, kept.Status);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(h.Service.TryGetJob(h.RunId));
        Assert.IsType<NoContentResult>(await h.Controller().GetRunReportJob(h.RunId, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().GetRunReportJob(999999, Ct));

        // The persisted status and the documents remain.
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task TryGetJob_RightAfterTryStart_IsNeverNull()
    {
        await using var h = await Harness.CreateAsync();
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));
        var requested = new[] { BenchmarkReportAudience.TechnicalReport };

        Assert.True(h.Service.TryStart(h.RunId, "user-1", requested, false, out var completion));
        var view = h.Service.TryGetJob(h.RunId);

        // Before or after the job registers, the busy slot keeps it Queued with the requested document.
        Assert.NotNull(view);
        Assert.Equal("Queued", view.Phase);
        Assert.Equal(requested, view.Audiences);
        Assert.Equal(h.Clock.UtcNow, view.QueuedAtUtc);
        Assert.Equal(h.Clock.UtcNow, view.ServerTimeUtc);
        Assert.NotNull(view.Job);

        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("Finished", h.Service.TryGetJob(h.RunId)!.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
    }

    [Fact]
    public async Task TryStart_AfterAFinishedJob_TheFirstViewIsTheNewJob()
    {
        await using var h = await Harness.CreateAsync();
        Assert.True(h.Service.TryStart(h.RunId, "user-1", new[] { BenchmarkReportAudience.ExecutiveSummary }, false, out var first));
        await first.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("Finished", h.Service.TryGetJob(h.RunId)!.Phase);

        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));
        Assert.True(h.Service.TryStart(h.RunId, "user-1", new[] { BenchmarkReportAudience.TechnicalReport }, false, out var second));
        var view = h.Service.TryGetJob(h.RunId);

        Assert.NotNull(view);
        Assert.NotEqual("Finished", view.Phase);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, view.Audiences);

        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await second.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var finished = h.Service.TryGetJob(h.RunId)!;
        Assert.Equal("Finished", finished.Phase);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, finished.Audiences);
        Assert.Equal(2, (await h.DocumentsAsync()).Count);
    }

    // --- Estimate ------------------------------------------------------------------------------------

    [Fact]
    public async Task Estimate_UsesThePreviewArithmetic_AndReportsTheRunWriterRefusalAndWarning()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");
        long sameModelId = await h.AddConfigAsync("OpenAI", "gpt-5.6-luna", "GPT Luna");

        async Task<BenchmarkRunReportEstimateDto> EstimateAsync(long writerId, List<BenchmarkReportAudience>? audiences = null)
            => Assert.IsType<BenchmarkRunReportEstimateDto>(Assert.IsType<OkObjectResult>(
                await h.Controller().EstimateRunReportDocuments(h.RunId, new BenchmarkRunReportEstimateRequest
                {
                    WriterModelConfigurationId = writerId,
                    Audiences = audiences
                }, Ct)).Value);

        var estimate = await EstimateAsync(h.WriterConfig.Id);
        Assert.Null(estimate.Refusal);
        Assert.Null(estimate.SameProviderWarning);
        Assert.Equal(AllAudiences, estimate.Estimates.Select(e => e.Audience));

        // The preview's arithmetic over the run's own prompts: input tokens are a quarter of the characters, rounded up.
        await using (var prepDb = new ApplicationDbContext(h.Options))
        {
            var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                prepDb, new BenchmarkModelComparisonService(prepDb),
                BenchmarkRunReportDocumentService.RunRequest(h.RunId, AllAudiences, h.WriterConfig.Id),
                BenchmarkReportPackPreparation.AnswerExcerptChars(h.Configuration), Ct, h.Configuration);
            Assert.True(prep != null, prepRefusal);
            foreach (var e in estimate.Estimates)
            {
                var prompt = BenchmarkReportPackPrompt.Build(e.Audience, prep!.Sheet, prep.Content);
                int chars = prompt.SystemPrompt.Length + prompt.UserMessage.Length;
                Assert.Equal(chars, e.PromptChars);
                Assert.Equal((chars + 3) / 4, e.EstimatedInputTokens);
            }
        }
        Assert.Equal(
            estimate.Estimates.All(e => e.EstimatedCostUsd != null) ? (double?)estimate.Estimates.Sum(e => e.EstimatedCostUsd!.Value) : null,
            estimate.EstimatedTotalCostUsd);

        // The Report Pack preview refuses the run on its own: a comparison report needs a peer.
        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(new BenchmarkReportPackRequest
            {
                RunIds = new List<long> { h.RunId },
                SubjectKey = BenchmarkRunReportDocumentService.SubjectKeyOf(h.RunId),
                Audiences = AllAudiences.ToList(),
                WriterModelConfigurationId = h.WriterConfig.Id
            }, Ct)).Value);
        Assert.Equal(BenchmarkReportPackPreparation.PeerlessReportRefusal, preview.Refusal);
        Assert.Empty(preview.Estimates);

        var one = await EstimateAsync(h.WriterConfig.Id, new List<BenchmarkReportAudience> { BenchmarkReportAudience.TechnicalReport });
        Assert.Equal(
            estimate.Estimates.Single(e => e.Audience == BenchmarkReportAudience.TechnicalReport).PromptChars,
            Assert.Single(one.Estimates).PromptChars);

        var sameProvider = await EstimateAsync(sameProviderId);
        Assert.Null(sameProvider.Refusal);
        Assert.Equal("reportWriter", sameProvider.SameProviderWarning?.Role);
        Assert.Equal("GPT Sol", sameProvider.SameProviderWarning?.AssessorModelDisplayName);
        Assert.Equal(3, sameProvider.Estimates.Count);

        var sameModel = await EstimateAsync(sameModelId);
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, sameModel.Refusal);
        Assert.Null(sameModel.SameProviderWarning);

        Assert.IsType<BadRequestObjectResult>(await h.Controller().EstimateRunReportDocuments(h.RunId, new BenchmarkRunReportEstimateRequest
        {
            WriterModelConfigurationId = h.WriterConfig.Id,
            Audiences = new List<BenchmarkReportAudience> { (BenchmarkReportAudience)99 }
        }, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().EstimateRunReportDocuments(
            999999, new BenchmarkRunReportEstimateRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct));

        Assert.Equal(0, h.Writer.JobCalls);
        await using var db = new ApplicationDbContext(h.Options);
        Assert.Empty(await db.SystemAiUsageLogs.ToListAsync(Ct));
    }

    [Fact]
    public async Task EstimateAndPreview_AbortedByTheClient_Answer499()
    {
        await using var h = await Harness.CreateAsync();
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        var estimate = Assert.IsType<StatusCodeResult>(await h.Controller().EstimateRunReportDocuments(
            h.RunId, new BenchmarkRunReportEstimateRequest { WriterModelConfigurationId = h.WriterConfig.Id }, aborted.Token));
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, estimate.StatusCode);

        var preview = Assert.IsType<StatusCodeResult>(await h.Controller().Preview(new BenchmarkReportPackRequest
        {
            RunIds = new List<long> { h.RunId },
            SubjectKey = BenchmarkRunReportDocumentService.SubjectKeyOf(h.RunId),
            Audiences = AllAudiences.ToList(),
            WriterModelConfigurationId = h.WriterConfig.Id
        }, aborted.Token));
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, preview.StatusCode);

        Assert.Equal(0, h.Writer.JobCalls);
    }

    // --- Run-scoped delete ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRunDocument_SettlesTheStatus_RefusesWhileWriting_AndRefusesAnotherRunsDocument()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.ScheduleIfDue(h.RunId);
        var executive = (await h.DocumentsAsync()).Single(d => d.Audience == BenchmarkReportAudience.ExecutiveSummary);

        long otherRunId = h.Seeded.RunIds[1];
        long otherDocumentId;
        await using (var db = new ApplicationDbContext(h.Options))
        {
            var other = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
            other.Id = 0;
            other.SubjectKey = BenchmarkRunReportDocumentService.SubjectKeyOf(otherRunId);
            other.Runs = new List<BenchmarkReportDocumentRun>();
            db.BenchmarkReportDocuments.Add(other);
            await db.SaveChangesAsync(Ct);
            otherDocumentId = other.Id;
        }

        Assert.IsType<NotFoundResult>(await h.Controller().DeleteRunReportDocument(h.RunId, otherDocumentId, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().DeleteRunReportDocument(999999, executive.Id, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().DeleteRunReportDocument(h.RunId, 999999, Ct));

        await h.UpdateRunAsync(r => r.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing);
        var busy = Assert.IsType<ConflictObjectResult>(await h.Controller().DeleteRunReportDocument(h.RunId, executive.Id, Ct));
        Assert.Contains("Wait for the writing to finish, or cancel it, before deleting a report.", JsonSerializer.Serialize(busy.Value));
        Assert.Equal(4, (await h.DocumentsAsync()).Count);

        await h.UpdateRunAsync(r => r.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed);
        Assert.IsType<NoContentResult>(await h.Controller().DeleteRunReportDocument(h.RunId, executive.Id, Ct));

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, run.ReportDocumentsStatus);
        Assert.Null(run.ReportDocumentsMessage);
        Assert.Equal(h.WriterConfig.Id, run.ReportWriterModelConfigurationId);

        var remaining = await h.DocumentsAsync();
        Assert.DoesNotContain(remaining, d => d.Id == executive.Id);
        Assert.Contains(remaining, d => d.Id == otherDocumentId);
        await using (var db = new ApplicationDbContext(h.Options))
        {
            Assert.Equal(
                new[] { BenchmarkReportAudience.ExecutiveSummary },
                await BenchmarkRunReportDocumentService.MissingAudiencesAsync(db, h.RunId, Ct));
        }
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not reached in time.");
            await Task.Delay(10, Ct);
        }
    }

    /// <summary>A clock the tests move by hand.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        public DateTime UtcNow => GetUtcNow().UtcDateTime;

        public void Advance(TimeSpan by)
        {
            lock (_lock)
            {
                _now += by;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _now;
            }
        }
    }

    /// <summary>
    /// Stores a fixture run-completion document for each audience on the job, except
    /// <see cref="FailAudience"/>, which it fails as a writer's reply that could not be parsed. Each
    /// document records one call's usage. <see cref="Gate"/> holds the job before its first document;
    /// <see cref="BlockAfterFirst"/> holds it after the first document is stored until it is canceled.
    /// A cancellation settles the job as the real writer does.
    /// </summary>
    private sealed class FakeWriter : IBenchmarkRunReportWriter
    {
        public const long InputTokensPerDocument = 100;
        public const long OutputTokensPerDocument = 20;
        public const decimal CostPerDocument = 0.01m;

        private readonly DbContextOptions<ApplicationDbContext> _options;
        private int _calls;
        private int _jobCalls;

        public FakeWriter(DbContextOptions<ApplicationDbContext> options) => _options = options;

        public BenchmarkReportAudience? FailAudience { get; set; }
        public BenchmarkReportAudience? WarnAudience { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public bool BlockAfterFirst { get; set; }
        public TaskCompletionSource FirstStored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => _calls;
        public int JobCalls => _jobCalls;
        public long? LastWriterConfigId { get; private set; }
        public List<BenchmarkReportAudience> LastAudiences { get; private set; } = new();

        public async Task WriteRunCompletionDocumentsAsync(BenchmarkReportPackJob job, CancellationToken ct)
        {
            Interlocked.Increment(ref _jobCalls);
            LastWriterConfigId = job.WriterConfigId;
            LastAudiences = job.Documents.Select(d => d.Audience).ToList();
            long runId = job.Request.RunIds.Single();

            try
            {
                if (Gate != null) await Gate.Task.WaitAsync(ct);

                await using var db = new ApplicationDbContext(_options);
                int failed = 0;
                foreach (var progress in job.Documents.ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _calls);
                    job.SetDocumentStatus(progress.Audience, BenchmarkReportPackDocumentStatus.Writing);
                    job.AddUsage(progress.Audience, InputTokensPerDocument, OutputTokensPerDocument, CostPerDocument);
                    if (progress.Audience == FailAudience)
                    {
                        job.SetDocumentStatus(progress.Audience, BenchmarkReportPackDocumentStatus.Failed, "The writer's reply could not be parsed.");
                        failed++;
                        continue;
                    }

                    var document = BenchmarkReportPackFixture.StandaloneDocument(progress.Audience);
                    document.Id = 0;
                    document.SubjectKey = job.SubjectKey;
                    document.SubjectRunIdsJson = "[" + runId + "]";
                    document.WriterConfigId = job.WriterConfigId;
                    document.WriterDisplayName = job.WriterDisplayName;
                    document.SameProviderAcknowledged = job.SameProviderAcknowledged;
                    document.Status = progress.Audience == WarnAudience
                        ? BenchmarkReportDocumentStatus.CompletedWithWarnings
                        : BenchmarkReportDocumentStatus.Completed;
                    document.Runs = new List<BenchmarkReportDocumentRun>
                    {
                        new() { RunId = runId, ScoringMethodVersion = 9, SynthesisSha256 = "0123456789abcdef" }
                    };
                    db.BenchmarkReportDocuments.Add(document);
                    await db.SaveChangesAsync(CancellationToken.None);

                    job.SetDocumentStatus(progress.Audience,
                        document.Status == BenchmarkReportDocumentStatus.Completed
                            ? BenchmarkReportPackDocumentStatus.Completed
                            : BenchmarkReportPackDocumentStatus.CompletedWithWarnings,
                        documentId: document.Id);

                    if (BlockAfterFirst && FirstStored.TrySetResult())
                    {
                        await Task.Delay(Timeout.Infinite, ct);
                    }
                }

                job.SetStatus(failed == 0 ? BenchmarkReportPackJobStatus.Completed : BenchmarkReportPackJobStatus.CompletedWithErrors);
            }
            catch (OperationCanceledException)
            {
                foreach (var d in job.Documents.ToList().Where(d => d.Status is BenchmarkReportPackDocumentStatus.Pending
                             or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing))
                {
                    job.SetDocumentStatus(d.Audience, BenchmarkReportPackDocumentStatus.Canceled);
                }
                job.SetStatus(BenchmarkReportPackJobStatus.Canceled);
            }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public const string AssessmentJson = "{\"finalScore\":80,\"findings\":[]}";

        public DbContextOptions<ApplicationDbContext> Options { get; private init; } = default!;
        public BenchmarkRunExamTests.SeededSuite Seeded { get; private init; } = default!;
        public IConfiguration Configuration { get; private init; } = default!;
        public FakeWriter Writer { get; private init; } = default!;
        public FakeClock Clock { get; } = new();
        public BenchmarkReportPackJobManager Jobs { get; } = new(TimeSpan.FromMilliseconds(10));
        public BenchmarkRunReportDocumentService Service { get; private set; } = default!;
        public SystemAiApiConfiguration WriterConfig { get; private set; } = default!;
        public long RunId => Seeded.RunIds[0];

        private ServiceProvider _provider = default!;

        public static SystemAiApiConfiguration Config(string provider, string modelId, string name) => new()
        {
            Provider = provider,
            ModelId = modelId,
            DisplayName = name,
            ModelRole = 4,
            IsEnabled = true,
            EncryptedApiKey = "dummy_encrypted",
            ApiKeyNonce = "nonce",
            ApiKeyTag = "tag"
        };

        public static async Task<Harness> CreateAsync(int maxRunsPerHour = 100, bool withWriter = true)
        {
            var options = BenchmarkRunExamTests.InMemoryOptions();
            var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:Compliance:MaxRunsPerHour"] = maxRunsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Benchmark:Compliance:MaxRunsPerDay"] = "100"
            }).Build();

            var harness = new Harness
            {
                Options = options,
                Seeded = seeded,
                Configuration = configuration,
                Writer = new FakeWriter(options)
            };

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddScoped(_ => new ApplicationDbContext(options));
            services.AddScoped<BenchmarkComplianceGuard>();
            services.AddSingleton<IBenchmarkRunReportWriter>(harness.Writer);
            harness._provider = services.BuildServiceProvider();
            harness.Service = new BenchmarkRunReportDocumentService(
                harness._provider.GetRequiredService<IServiceScopeFactory>(),
                harness.Jobs,
                NullLogger<BenchmarkRunReportDocumentService>.Instance,
                harness.Clock);

            await using var db = new ApplicationDbContext(options);
            harness.WriterConfig = Config("Anthropic", "claude-opus-5-5", "Claude Opus 5.5");
            db.SystemAiApiConfigurations.Add(harness.WriterConfig);
            await db.SaveChangesAsync();

            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == seeded.RunIds[0]);
            run.AssessmentJson = AssessmentJson;
            run.ReportWriterModelConfigurationId = withWriter ? harness.WriterConfig.Id : null;
            await db.SaveChangesAsync();

            return harness;
        }

        public async Task<long> AddConfigAsync(string provider, string modelId, string name)
        {
            await using var db = new ApplicationDbContext(Options);
            var config = Config(provider, modelId, name);
            db.SystemAiApiConfigurations.Add(config);
            await db.SaveChangesAsync();
            return config.Id;
        }

        public AdminBenchmarkReportPacksController Controller()
        {
            var db = new ApplicationDbContext(Options);
            return new AdminBenchmarkReportPacksController(
                db,
                Jobs,
                new BenchmarkComplianceGuard(Configuration, db),
                new BenchmarkModelComparisonService(db),
                new ModelPricingService(new ModelMetadataService(), db),
                new EndpointPolicy(Configuration),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Configuration,
                Service);
        }

        public async Task<BenchmarkRun> RunAsync()
        {
            await using var db = new ApplicationDbContext(Options);
            return await db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(r => r.Id == RunId);
        }

        public async Task<BenchmarkRunReportDocumentsStatus> StatusAsync() => (await RunAsync()).ReportDocumentsStatus;

        public Task UpdateRunAsync(Action<BenchmarkRun> update) => UpdateRunAsync(RunId, update);

        public async Task UpdateRunAsync(long runId, Action<BenchmarkRun> update)
        {
            await using var db = new ApplicationDbContext(Options);
            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == runId);
            update(run);
            await db.SaveChangesAsync();
        }

        public async Task<List<BenchmarkReportDocument>> DocumentsAsync()
        {
            await using var db = new ApplicationDbContext(Options);
            return await db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes().OrderBy(d => d.Id).ToListAsync();
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
