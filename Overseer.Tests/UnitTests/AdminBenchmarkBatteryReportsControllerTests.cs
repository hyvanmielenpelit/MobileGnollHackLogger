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
        BenchmarkReportAudience.RootCauseAnalysis
    };

    /// <summary>
    /// The battery-completion documents an estimate prices: both, since the Executive Summary alone needs a usable stored
    /// Root Cause Analysis and is refused without one.
    /// </summary>
    private static readonly List<BenchmarkReportAudience> EstimableAudiences = AllAudiences.ToList();

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
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal(BenchmarkReportDocumentOrigin.BatteryCompletion, d.Origin));
        Assert.Equal(h.BatteryRunId, h.Writer.LastBatteryRunId);
    }

    [Fact]
    public async Task Write_WritesOnlyTheRequestedDocument_AndRefusesOneAlreadyWritten()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.RootCauseAnalysis), Ct));
        Assert.Equal(new[] { BenchmarkReportAudience.RootCauseAnalysis }, Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value).Audiences);
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(BenchmarkReportAudience.RootCauseAnalysis, Assert.Single(await h.DocumentsAsync()).Audience);

        var written = Assert.IsType<ConflictObjectResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.RootCauseAnalysis), Ct));
        Assert.Contains(
            "The Root Cause Analysis is already written. Delete it first to write it again.",
            JsonSerializer.Serialize(written.Value));

        // The missing Executive Summary is written from the stored Root Cause Analysis once that one is usable.
        await h.MakeRootCauseUsableAsync();
        Assert.IsType<AcceptedResult>(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(2, (await h.DocumentsAsync()).Count);

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
        Assert.Equal(2, documents.Count);
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

    // --- The Executive Summary needs its Root Cause Analysis -------------------------------------------

    /// <summary>A 409 body's <c>error</c>, <c>code</c> and <c>rootCauseDocumentId</c>, read from its JSON.</summary>
    private static (string? Error, string? Code, long? RootCauseDocumentId) RootCauseConflictOf(IActionResult result)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;
        var id = root.GetProperty("rootCauseDocumentId");
        return (root.GetProperty("error").GetString(), root.GetProperty("code").GetString(),
            id.ValueKind == JsonValueKind.Null ? null : id.GetInt64());
    }

    [Fact]
    public async Task Write_RefusesTheExecutiveSummaryAlone_UntilTheStoredRootCauseAnalysisIsUsable()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        var missing = RootCauseConflictOf(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.ExecutiveSummary), Ct));
        Assert.Equal(
            "The Executive Summary is written from the Root Cause Analysis, and this battery run has none. Write both: check Root Cause Analysis too.",
            missing.Error);
        Assert.Equal("rootCauseMissing", missing.Code);
        Assert.Null(missing.RootCauseDocumentId);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Null((await h.BatteryRunAsync()).ReportWriterModelConfigurationId);

        Assert.IsType<AcceptedResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.RootCauseAnalysis), Ct));
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        long rootCauseId = Assert.Single(await h.DocumentsAsync()).Id;

        // Every missing document, here the Executive Summary alone, meets the same refusal.
        var outdated = RootCauseConflictOf(await h.Controller().Write(h.BatteryRunId, Request(h.WriterConfig.Id), Ct));
        Assert.Equal(BenchmarkRootCauseReadiness.OutdatedMessage("battery run", rootCauseId), outdated.Error);
        Assert.Contains("than this battery run has now", outdated.Error);
        Assert.Equal("rootCauseOutdated", outdated.Code);
        Assert.Equal(rootCauseId, outdated.RootCauseDocumentId);
        Assert.Equal(1, h.Writer.JobCalls);

        await h.MakeRootCauseUsableAsync();
        Assert.IsType<AcceptedResult>(await h.Controller().Write(
            h.BatteryRunId, Request(h.WriterConfig.Id, BenchmarkReportAudience.ExecutiveSummary), Ct));
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(2, (await h.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task Write_AcceptsTheExecutiveSummaryWithItsRootCauseAnalysisReplaced()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        long rootCauseId = await h.AddDocumentAsync(h.BatteryRunId, BenchmarkReportAudience.RootCauseAnalysis);

        var request = Request(h.WriterConfig.Id, AllAudiences);
        request.ReplaceDocumentIds = new List<long> { rootCauseId };
        var accepted = Assert.IsType<AcceptedResult>(await h.Controller().Write(h.BatteryRunId, request, Ct));

        Assert.Equal(AllAudiences, Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value).Audiences);
        await BatteryReportHarness.WaitUntilAsync(() => !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(BenchmarkReportSlots.WriteOrder, h.Writer.LastAudiences);
    }

    [Fact]
    public async Task SummaryReadiness_IsMissingUsableOrOutdated_AndNotFoundForAnUnknownBatteryRun()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        async Task<BenchmarkRootCauseReadinessDto> ReadinessAsync()
            => Assert.IsType<BenchmarkRootCauseReadinessDto>(Assert.IsType<OkObjectResult>(
                await h.Controller().GetSummaryReadiness(h.BatteryRunId, Ct)).Value);

        Assert.IsType<NotFoundResult>(await h.Controller().GetSummaryReadiness(999999, Ct));

        var missing = await ReadinessAsync();
        Assert.Equal("missing", missing.State);
        Assert.Equal("rootCauseMissing", missing.Code);
        Assert.Equal(BenchmarkRootCauseReadiness.MissingMessage("battery run"), missing.Message);

        long rootCauseId = await h.AddDocumentAsync(h.BatteryRunId, BenchmarkReportAudience.RootCauseAnalysis);
        var outdated = await ReadinessAsync();
        Assert.Equal("outdated", outdated.State);
        Assert.Equal(rootCauseId, outdated.RootCauseDocumentId);
        Assert.Equal("rootCauseOutdated", outdated.Code);

        await h.MakeRootCauseUsableAsync();
        var usable = await ReadinessAsync();
        Assert.Equal("usable", usable.State);
        Assert.Equal(rootCauseId, usable.RootCauseDocumentId);
        Assert.Null(usable.Message);
        Assert.Null(usable.Code);

        // A battery run that cannot be written about is outdated with the refusal and no code.
        await h.UpdateBatteryRunAsync(b => b.Status = BenchmarkRunSeriesStatus.Running);
        var unprepared = await ReadinessAsync();
        Assert.Equal("outdated", unprepared.State);
        Assert.Equal(BenchmarkBatteryReportDocumentService.NotFinishedMessage, unprepared.Message);
        Assert.Null(unprepared.Code);
        Assert.Equal(0, h.Writer.JobCalls);
    }

    [Fact]
    public void TheSummaryReadinessEndpoint_IsAGetUnderTheReportDocumentsRoute()
    {
        var method = typeof(AdminBenchmarkBatteryReportsController).GetMethod(nameof(AdminBenchmarkBatteryReportsController.GetSummaryReadiness))!;
        Assert.Equal("summary-readiness", Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);

        var run = typeof(AdminBenchmarkReportPacksController).GetMethod(nameof(AdminBenchmarkReportPacksController.GetRunSummaryReadiness))!;
        Assert.Equal("runs/{runId:long}/report-documents/summary-readiness",
            Assert.Single(run.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
    }

    [Fact]
    public async Task Estimate_OfTheExecutiveSummaryAlone_CarriesTheRefusalTheWriteAnswersWith()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        async Task<BenchmarkRunReportEstimateDto> EstimateAsync(params BenchmarkReportAudience[] audiences)
            => Assert.IsType<BenchmarkRunReportEstimateDto>(Assert.IsType<OkObjectResult>(
                await h.Controller().Estimate(h.BatteryRunId, new BenchmarkRunReportEstimateRequest
                {
                    WriterModelConfigurationId = h.WriterConfig.Id,
                    Audiences = audiences.ToList()
                }, Ct)).Value);

        var alone = await EstimateAsync(BenchmarkReportAudience.ExecutiveSummary);
        Assert.Equal(BenchmarkRootCauseReadiness.MissingMessage("battery run"), alone.Refusal);
        Assert.Single(alone.Estimates);
        Assert.Null((await EstimateAsync(AllAudiences)).Refusal);
        Assert.Equal(0, h.Writer.JobCalls);
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
        // A battery job lists its documents in the write order: the Root Cause Analysis first.
        Assert.Equal(BenchmarkReportSlots.WriteOrder, view.Audiences);
        Assert.Equal(BenchmarkReportSlots.WriteOrder, view.Job.Documents.Select(d => d.Audience));
        Assert.Equal(h.WriterConfig.Id, view.WriterConfigId);
        Assert.Equal("Anthropic", view.WriterProvider);
        Assert.Equal(BenchmarkReportPackPreparation.BatteryJobLabel(h.BatteryRunId), view.Job.SubjectLabel);
    }

    [Fact]
    public async Task WriterReplies_ServeTheHeldJobsReplies_AndAreNotFoundWithoutOne_OrPastTheRetention()
    {
        var clock = new MovableClock();
        await using var h = await BatteryReportHarness.CreateAsync(clock: clock);
        Assert.Null(h.Service.TryGetHeldJob(h.BatteryRunId));
        Assert.IsType<NotFoundResult>(await h.Controller().GetWriterReplies(h.BatteryRunId, Ct));

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.NotNull(h.Service.TryGetHeldJob(h.BatteryRunId));
        var content = Assert.IsType<ContentResult>(await h.Controller().GetWriterReplies(h.BatteryRunId, Ct));
        Assert.Equal("text/markdown; charset=utf-8", content.ContentType);
        Assert.StartsWith("# Writer replies: ", content.Content, StringComparison.Ordinal);
        Assert.Contains(BatteryReportFakeWriter.ReplyOf(BenchmarkReportAudience.RootCauseAnalysis), content.Content, StringComparison.Ordinal);
        Assert.Contains(BatteryReportFakeWriter.ReplyOf(BenchmarkReportAudience.ExecutiveSummary), content.Content, StringComparison.Ordinal);
        Assert.IsType<NotFoundResult>(await h.Controller().GetWriterReplies(999999, Ct));

        // A finished job is held for the retention, and no longer.
        clock.Advance(BenchmarkRunReportDocumentService.FinishedJobRetention - TimeSpan.FromMinutes(1));
        Assert.IsType<ContentResult>(await h.Controller().GetWriterReplies(h.BatteryRunId, Ct));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(h.Service.TryGetHeldJob(h.BatteryRunId));
        Assert.IsType<NotFoundResult>(await h.Controller().GetWriterReplies(h.BatteryRunId, Ct));
    }

    [Fact]
    public void TheWriterRepliesEndpoint_IsAGetUnderTheJobRoute()
    {
        var method = typeof(AdminBenchmarkBatteryReportsController).GetMethod(nameof(AdminBenchmarkBatteryReportsController.GetWriterReplies))!;
        var get = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>());
        Assert.Equal("job/writer-replies", get.Template);

        var run = typeof(AdminBenchmarkReportPacksController).GetMethod(nameof(AdminBenchmarkReportPacksController.GetRunReportWriterReplies))!;
        var runGet = Assert.Single(run.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>());
        Assert.Equal("runs/{runId:long}/report-documents/job/writer-replies", runGet.Template);
    }

    /// <summary>A clock the tests move by hand, from a fixed start.</summary>
    private sealed class MovableClock : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

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

        var estimate = await EstimateAsync(h.WriterConfig.Id, EstimableAudiences);
        Assert.Null(estimate.Refusal);
        Assert.Null(estimate.SameProviderWarning);
        Assert.Equal(EstimableAudiences, estimate.Estimates.Select(e => e.Audience));
        Assert.All(estimate.Estimates, e => Assert.True(e.PromptChars > 0));

        var sameProvider = await EstimateAsync(sameProviderId, EstimableAudiences);
        Assert.Null(sameProvider.Refusal);
        Assert.Equal("reportWriter", sameProvider.SameProviderWarning?.Role);
        Assert.Equal(EstimableAudiences.Count, sameProvider.Estimates.Count);

        var sameModel = await EstimateAsync(sameModelId, EstimableAudiences);
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
        Assert.Equal(3, (await h.DocumentsAsync()).Count);

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
