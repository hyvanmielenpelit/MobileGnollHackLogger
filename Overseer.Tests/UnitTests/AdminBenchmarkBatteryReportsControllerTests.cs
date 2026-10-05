namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The battery run's report-documents endpoints: write now with its refusals in the run endpoint's
/// order and shapes, the job view, cancel, the estimate and the battery-scoped delete. The writer is
/// a fake; nothing calls a provider.
/// </summary>
public class AdminBenchmarkBatteryReportsControllerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BenchmarkReportAudience[] AllAudiences =
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief
    };

    private static WriteRunReportDocumentsRequest Request(long writerId, params BenchmarkReportAudience[] audiences) => new()
    {
        WriterModelConfigurationId = writerId,
        Audiences = audiences.Length == 0 ? null : audiences.ToList()
    };

    [Fact]
    public void TheController_IsAdminOnly_AtTheBatteryRunsReportDocumentsRoute()
    {
        var type = typeof(AdminBenchmarkBatteryReportsController);
        var route = Assert.Single(type.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>());
        Assert.Equal("api/admin/benchmark/batteries/runs/{batteryRunId:long}/report-documents", route.Template);
        var authorize = Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal("AdminOnly", authorize.Policy);
    }

    // --- Write now -----------------------------------------------------------------------------------

    [Fact]
    public async Task Write_SetsTheWriter_AnswersAccepted_AndWritesEveryDocument()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        var response = Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value);
        Assert.Equal(h.BatteryRunId, response.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, response.Status);
        Assert.Equal(AllAudiences, response.Audiences);

        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(h.WriterConfig.Id, batteryRun.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, batteryRun.ReportDocumentsStatus);
        var documents = await h.DocumentsAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d => Assert.Equal(BenchmarkReportDocumentOrigin.BatteryCompletion, d.Origin));
        Assert.Equal(h.BatteryRunId, h.Writer.LastBatteryRunId);
    }

    [Fact]
    public async Task Write_WritesOnlyTheRequestedDocument_AndRefusesOneAlreadyWritten()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.TechnicalReport), Ct));
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value).Audiences);
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(BenchmarkReportAudience.TechnicalReport, Assert.Single(await h.DocumentsAsync()).Audience);

        var written = Assert.IsType<ConflictObjectResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.TechnicalReport), Ct));
        Assert.Contains(
            "The Report for AI Researchers and Developers is already written. Delete it first to write it again.",
            JsonSerializer.Serialize(written.Value));

        Assert.IsType<AcceptedResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(3, (await h.DocumentsAsync()).Count);

        var all = Assert.IsType<ConflictObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Contains(AdminBenchmarkBatteryReportsController.AllWrittenMessage, JsonSerializer.Serialize(all.Value));
        Assert.Equal(2, h.Writer.JobCalls);
    }

    [Fact]
    public async Task Write_RefusesNoBody_AnUnknownBatteryRun_AnUnfinishedOne_AndOneWithoutACurrentAnalysis()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, null!, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().Write(999999, Request(h.WriterConfig.Id), Ct));

        await h.UpdateBatteryRunAsync(b => b.Status = BenchmarkRunSeriesStatus.Running);
        var running = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Contains(BenchmarkBatteryReportDocumentService.NotFinishedMessage, JsonSerializer.Serialize(running.Value));

        await h.UpdateBatteryRunAsync(b => b.Status = BenchmarkRunSeriesStatus.Completed);
        await h.AddMemberAfterAnalysisAsync();
        var stale = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Contains(BenchmarkBatteryReportDocumentService.NoCurrentAnalysisMessage, JsonSerializer.Serialize(stale.Value));

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, batteryRun.ReportDocumentsStatus);
        Assert.Null(batteryRun.ReportWriterModelConfigurationId);
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task Write_RefusesAnIncompleteAnalysis()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false, complete: false);

        var incomplete = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Contains(BenchmarkBatteryReportDocumentService.NoCurrentAnalysisMessage, JsonSerializer.Serialize(incomplete.Value));
    }

    [Fact]
    public async Task Write_AnswersConflict_WhileAJobIsPendingOrWriting()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        await h.UpdateBatteryRunAsync(b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing);

        var busy = Assert.IsType<ConflictObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Contains(AdminBenchmarkBatteryReportsController.AlreadyWritingMessage, JsonSerializer.Serialize(busy.Value));
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task Write_RefusesAnAudienceThatIsNotABatteryCompletionDocument()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, (BenchmarkReportAudience)99), Ct));
        Assert.Contains(AdminBenchmarkBatteryReportsController.InvalidAudienceMessage, JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Write_RefusesAnUnusableWriter_AndTheModelUnderTest()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        long sameModelId = await h.AddConfigAsync("OpenAI", "gpt-5.6-luna", "GPT Luna");

        var unknown = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(999999), Ct));
        Assert.Contains(BenchmarkRunReportDocumentService.InvalidWriterMessage, JsonSerializer.Serialize(unknown.Value));

        var sameModel = Assert.IsType<BadRequestObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(sameModelId), Ct));
        Assert.Contains(BenchmarkRunReportDocumentService.ModelUnderTestMessage, JsonSerializer.Serialize(sameModel.Value));
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public async Task Write_AnswersConflictWithTheWarning_WhenNotAcknowledged_AndStartsWhenAcknowledged()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");

        var warned = Assert.IsType<ObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(sameProviderId), Ct));
        Assert.Equal(StatusCodes.Status409Conflict, warned.StatusCode);
        var warning = Assert.IsType<SameProviderWarningDto>(warned.Value);
        Assert.Equal("reportWriter", warning.Role);
        Assert.True(warning.SameProvider);
        Assert.Equal("OpenAI", warning.Provider);
        Assert.Equal("gpt-5.6-luna", warning.TestedModelDisplayName);
        Assert.Equal("GPT Sol", warning.AssessorModelDisplayName);
        Assert.Null((await h.BatteryRunAsync()).ReportWriterModelConfigurationId);

        var acknowledged = Request(sameProviderId);
        acknowledged.AcknowledgeSameProvider = true;
        Assert.IsType<AcceptedResult>(await h.Controller().Write(h.BatteryRunId, acknowledged, Ct));
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));

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
    public async Task Write_AnswersTooManyRequests_AtTheSpendCap()
    {
        await using var h = await BatteryReportHarness.CreateAsync(maxRunsPerHour: 0, withWriter: false);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));

        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.IsType<string>(result.Value);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    // --- Job and cancel ------------------------------------------------------------------------------

    [Fact]
    public async Task Job_IsNoContentBeforeAnyJob_TheViewAfterwards_AndNotFoundForAnUnknownBatteryRun()
    {
        await using var h = await BatteryReportHarness.CreateAsync();

        Assert.IsType<NoContentResult>(await h.Controller().GetJob(h.BatteryRunId, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().GetJob(999999, Ct));

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        var view = Assert.IsType<BenchmarkRunReportJobDto>(Assert.IsType<OkObjectResult>(await h.Controller().GetJob(h.BatteryRunId, Ct)).Value);
        Assert.Equal(h.BatteryRunId, view.RunId);
        Assert.Equal("Finished", view.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, view.Status);
        Assert.Equal(AllAudiences, view.Audiences);
        Assert.Equal(h.WriterConfig.Id, view.WriterConfigId);
        Assert.Equal("Anthropic", view.WriterProvider);
        Assert.Equal(BenchmarkReportPackPreparation.BatteryJobLabel(h.BatteryRunId), view.Job.SubjectLabel);
    }

    [Fact]
    public async Task Cancel_AnswersAcceptedWhileQueued_ConflictWithNoJob_AndNotFoundForAnUnknownBatteryRun()
    {
        await using var h = await BatteryReportHarness.CreateAsync();

        var none = Assert.IsType<ConflictObjectResult>(await h.Controller().Cancel(h.BatteryRunId, Ct));
        Assert.Contains(AdminBenchmarkBatteryReportsController.NothingInProgressMessage, JsonSerializer.Serialize(none.Value));
        Assert.IsType<NotFoundResult>(await h.Controller().Cancel(999999, Ct));

        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));
        var job = h.Service.ScheduleIfDue(h.BatteryRunId);
        await BatteryReportHarness.WaitUntilAsync(() => h.Jobs.WaitingCount == 1);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().Cancel(h.BatteryRunId, Ct));
        Assert.NotNull(Assert.IsType<BenchmarkRunReportJobDto>(accepted.Value).CancelRequestedAtUtc);
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, await h.StatusAsync());
        Assert.IsType<ConflictObjectResult>(await h.Controller().Cancel(h.BatteryRunId, Ct));
        Assert.Equal(0, h.Writer.JobCalls);
    }

    // --- Estimate ------------------------------------------------------------------------------------

    [Fact]
    public async Task Estimate_PricesTheBatteryPrompt_AndReportsTheWriterRefusalAndWarning_WithoutAModelCall()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");
        long sameModelId = await h.AddConfigAsync("OpenAI", "gpt-5.6-luna", "GPT Luna");

        async Task<BenchmarkRunReportEstimateDto> EstimateAsync(long writerId, List<BenchmarkReportAudience>? audiences = null)
            => Assert.IsType<BenchmarkRunReportEstimateDto>(Assert.IsType<OkObjectResult>(
                await h.Controller().Estimate(h.BatteryRunId, new BenchmarkRunReportEstimateRequest
                {
                    WriterModelConfigurationId = writerId,
                    Audiences = audiences
                }, Ct)).Value);

        var estimate = await EstimateAsync(h.WriterConfig.Id);
        Assert.Null(estimate.Refusal);
        Assert.Null(estimate.SameProviderWarning);
        Assert.Equal(AllAudiences, estimate.Estimates.Select(e => e.Audience));
        Assert.All(estimate.Estimates, e => Assert.True(e.PromptChars > 0));

        var one = await EstimateAsync(h.WriterConfig.Id, new List<BenchmarkReportAudience> { BenchmarkReportAudience.TechnicalReport });
        Assert.Equal(
            estimate.Estimates.Single(e => e.Audience == BenchmarkReportAudience.TechnicalReport).PromptChars,
            Assert.Single(one.Estimates).PromptChars);

        var sameProvider = await EstimateAsync(sameProviderId);
        Assert.Null(sameProvider.Refusal);
        Assert.Equal("reportWriter", sameProvider.SameProviderWarning?.Role);
        Assert.Equal(3, sameProvider.Estimates.Count);

        var sameModel = await EstimateAsync(sameModelId);
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, sameModel.Refusal);
        Assert.Null(sameModel.SameProviderWarning);

        Assert.IsType<BadRequestObjectResult>(await h.Controller().Estimate(h.BatteryRunId, new BenchmarkRunReportEstimateRequest
        {
            WriterModelConfigurationId = h.WriterConfig.Id,
            Audiences = new List<BenchmarkReportAudience> { (BenchmarkReportAudience)99 }
        }, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().Estimate(
            999999, new BenchmarkRunReportEstimateRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct));

        await h.UpdateBatteryRunAsync(b => b.Status = BenchmarkRunSeriesStatus.Running);
        var running = await EstimateAsync(h.WriterConfig.Id);
        Assert.Equal(BenchmarkBatteryReportDocumentService.NotFinishedMessage, running.Refusal);
        Assert.Empty(running.Estimates);

        Assert.Equal(0, h.Writer.JobCalls);
        await using var db = new ApplicationDbContext(h.Options);
        Assert.Empty(await db.SystemAiUsageLogs.ToListAsync(Ct));
    }

    // --- Battery-scoped delete -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteDocument_SettlesTheStatus_RefusesWhileWriting_AndRefusesAnotherBatteryRunsDocument()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.Service.ScheduleIfDue(h.BatteryRunId);
        var executive = (await h.DocumentsAsync()).Single(d => d.Audience == BenchmarkReportAudience.ExecutiveSummary);
        long otherDocumentId = await h.AddDocumentAsync(h.BatteryRunId + 1000, BenchmarkReportAudience.ExecutiveSummary);

        Assert.IsType<NotFoundResult>(await h.Controller().DeleteDocument(h.BatteryRunId, otherDocumentId, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().DeleteDocument(999999, executive.Id, Ct));
        Assert.IsType<NotFoundResult>(await h.Controller().DeleteDocument(h.BatteryRunId, 999999, Ct));

        await h.UpdateBatteryRunAsync(b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing);
        var busy = Assert.IsType<ConflictObjectResult>(await h.Controller().DeleteDocument(h.BatteryRunId, executive.Id, Ct));
        Assert.Contains(AdminBenchmarkBatteryReportsController.DeleteWhileWritingMessage, JsonSerializer.Serialize(busy.Value));
        Assert.Equal(4, (await h.DocumentsAsync()).Count);

        await h.UpdateBatteryRunAsync(b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed);
        Assert.IsType<NoContentResult>(await h.Controller().DeleteDocument(h.BatteryRunId, executive.Id, Ct));

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, batteryRun.ReportDocumentsStatus);
        Assert.Null(batteryRun.ReportDocumentsMessage);
        Assert.Equal(h.WriterConfig.Id, batteryRun.ReportWriterModelConfigurationId);

        var remaining = await h.DocumentsAsync();
        Assert.DoesNotContain(remaining, d => d.Id == executive.Id);
        Assert.Contains(remaining, d => d.Id == otherDocumentId);
        await using var db = new ApplicationDbContext(h.Options);
        Assert.Equal(
            new[] { BenchmarkReportAudience.ExecutiveSummary },
            await BenchmarkBatteryReportDocumentService.MissingAudiencesAsync(db, h.BatteryRunId, Ct));
    }
}
