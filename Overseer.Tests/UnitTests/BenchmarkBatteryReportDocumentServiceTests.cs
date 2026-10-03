namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// The battery-completion documents: when a finished battery run's two AI-written documents are
/// written (a finished battery, a complete and current analysis, a writer, no document yet), how the
/// battery run's status moves, the compliance guard, cancellation, the restart settlement, and the
/// cleanup when a battery run is deleted. The writer is a fake; nothing calls a provider.
/// </summary>
public class BenchmarkBatteryReportDocumentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BenchmarkReportAudience[] BothAudiences =
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport
    };

    // --- Scheduling ----------------------------------------------------------------------------------

    [Fact]
    public async Task ABatteryRunWithoutAReportWriter_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
        Assert.Empty(await h.DocumentsAsync());
    }

    [Fact]
    public async Task ABatteryRunThatHasNotFinished_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.UpdateBatteryRunAsync(b => b.Status = BenchmarkRunSeriesStatus.Running);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ABatteryRunWithoutAnAnalysis_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync(analyse: false);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ABatteryRunWithAnIncompleteAnalysis_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync(complete: false);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ABatteryRunWithAStaleAnalysis_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.AddMemberAfterAnalysisAsync();

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ABatteryRunThatAlreadyHasADocument_SchedulesNothing()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.AddDocumentAsync(h.BatteryRunId, BenchmarkReportAudience.ExecutiveSummary);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, await h.StatusAsync());
    }

    [Fact]
    public async Task ADueBatteryRun_GetsBothDocuments_StoredAsBatteryCompletionDocuments()
    {
        await using var h = await BatteryReportHarness.CreateAsync();

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        var documents = await h.DocumentsAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.Equal(BenchmarkReportDocumentOrigin.BatteryCompletion, d.Origin);
            Assert.Equal(BenchmarkBatteryReportDocumentService.SubjectKeyOf(h.BatteryRunId), d.SubjectKey);
            Assert.False(d.SameProviderAcknowledged);
        });
        Assert.Equal(BothAudiences, documents.Select(d => d.Audience).OrderBy(a => a));

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, batteryRun.ReportDocumentsStatus);
        Assert.Null(batteryRun.ReportDocumentsMessage);
        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(h.BatteryRunId, h.Writer.LastBatteryRunId);
        Assert.Equal(h.WriterConfig.Id, h.Writer.LastWriterConfigId);

        var view = h.Service.TryGetJob(h.BatteryRunId)!;
        Assert.Equal(h.BatteryRunId, view.RunId);
        Assert.Equal("Finished", view.Phase);
        Assert.Equal(BenchmarkReportPackPreparation.BatteryJobLabel(h.BatteryRunId), view.Job.SubjectLabel);
        Assert.Equal($"battery:{h.BatteryRunId}", view.Job.SubjectKey);
        Assert.Equal("Core knowledge", view.Job.SuiteName);

        // A second schedule finds the documents and calls the writer no more.
        await h.Service.ScheduleIfDue(h.BatteryRunId);
        Assert.Equal(1, h.Writer.JobCalls);
    }

    [Fact]
    public async Task TheStatus_MovesFromPendingThroughWritingToCompleted()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));

        var job = h.Service.ScheduleIfDue(h.BatteryRunId);

        await BatteryReportHarness.WaitUntilAsync(() => h.Jobs.WaitingCount == 1);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, await h.StatusAsync());
        Assert.True(h.Service.IsActive(h.BatteryRunId));
        var queued = h.Service.TryGetJob(h.BatteryRunId)!;
        Assert.Equal("Queued", queued.Phase);
        Assert.Equal("Report Pack: Other", queued.BlockingJobLabel);

        h.Writer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        running.SetStatus(BenchmarkReportPackJobStatus.Completed);
        await BatteryReportHarness.WaitUntilAsync(() => h.Service.TryGetJob(h.BatteryRunId)!.Phase == "Writing");
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Writing, await h.StatusAsync());

        h.Writer.Gate.SetResult();
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
        Assert.False(h.Service.IsActive(h.BatteryRunId));
    }

    [Fact]
    public async Task OneFailedDocument_FailsTheBatteryRun_AndADocumentWithWarnings_CompletesWithWarnings()
    {
        await using (var h = await BatteryReportHarness.CreateAsync())
        {
            h.Writer.FailAudience = BenchmarkReportAudience.TechnicalReport;

            await h.Service.ScheduleIfDue(h.BatteryRunId);

            var batteryRun = await h.BatteryRunAsync();
            Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, batteryRun.ReportDocumentsStatus);
            Assert.StartsWith("Report for AI Researchers and Developers: ", batteryRun.ReportDocumentsMessage);
            Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, Assert.Single(await h.DocumentsAsync()).Audience);
        }

        await using (var h = await BatteryReportHarness.CreateAsync())
        {
            h.Writer.WarnAudience = BenchmarkReportAudience.ExecutiveSummary;

            await h.Service.ScheduleIfDue(h.BatteryRunId);

            Assert.Equal(BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, await h.StatusAsync());
        }
    }

    [Fact]
    public async Task AComplianceRefusal_SkipsTheDocuments_WithTheGuardsReason()
    {
        await using var h = await BatteryReportHarness.CreateAsync(maxRunsPerHour: 0);

        await h.Service.ScheduleIfDue(h.BatteryRunId);

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Skipped, batteryRun.ReportDocumentsStatus);
        Assert.Contains("Hourly benchmark run cap reached", batteryRun.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Empty(await h.DocumentsAsync());
    }

    [Fact]
    public async Task TryStart_WithAnUnacknowledgedWriterOfTheCandidatesProvider_FailsWithTheWarning()
    {
        await using var h = await BatteryReportHarness.CreateAsync(withWriter: false);
        long sameProviderId = await h.AddConfigAsync("OpenAI", "gpt-5.6-sol", "GPT Sol");
        await h.UpdateBatteryRunAsync(b => b.ReportWriterModelConfigurationId = sameProviderId);

        Assert.True(h.Service.TryStart(h.BatteryRunId, "user-1", null, false, out var completion));
        await completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, batteryRun.ReportDocumentsStatus);
        Assert.StartsWith("GPT Sol is from OpenAI, the provider of the model under test.", batteryRun.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
    }

    // --- Cancel --------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_WhileQueued_LeavesTheQueue_AndSettlesTheBatteryRunAsCanceled()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        var running = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", Cts = new CancellationTokenSource() };
        Assert.True(h.Jobs.TryStart(running, out _));

        var job = h.Service.ScheduleIfDue(h.BatteryRunId);
        await BatteryReportHarness.WaitUntilAsync(() => h.Jobs.WaitingCount == 1);

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.Requested, h.Service.TryCancel(h.BatteryRunId));
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(0, h.Jobs.WaitingCount);
        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, batteryRun.ReportDocumentsStatus);
        Assert.Equal(BenchmarkRunReportDocumentService.CanceledBeforeWritingMessage, batteryRun.ReportDocumentsMessage);
        Assert.Equal(0, h.Writer.JobCalls);
        Assert.Empty(await h.DocumentsAsync());
        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.NotInProgress, h.Service.TryCancel(h.BatteryRunId));
    }

    [Fact]
    public async Task Cancel_WhileWriting_KeepsTheWrittenDocument_AndSettlesAsCanceled()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        h.Writer.BlockAfterFirst = true;

        var job = h.Service.ScheduleIfDue(h.BatteryRunId);
        await h.Writer.FirstStored.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.Requested, h.Service.TryCancel(h.BatteryRunId));
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, Assert.Single(await h.DocumentsAsync()).Audience);
        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, batteryRun.ReportDocumentsStatus);
        Assert.Equal("Canceled while writing. The Executive Summary was written and is kept.", batteryRun.ReportDocumentsMessage);
    }

    [Fact]
    public async Task Cancel_WithNoJob_IsNotFound()
    {
        await using var h = await BatteryReportHarness.CreateAsync();

        Assert.Equal(BenchmarkRunReportDocumentService.CancelOutcome.NotFound, h.Service.TryCancel(h.BatteryRunId));
        Assert.Null(h.Service.TryGetJob(h.BatteryRunId));
    }

    // --- Restart -------------------------------------------------------------------------------------

    [Fact]
    public async Task SettleInterrupted_FailsEveryPendingOrWritingBatteryRun_WithTheRestartMessage()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        long second = await h.SeedAnotherBatteryRunAsync();
        long third = await h.SeedAnotherBatteryRunAsync();
        await h.UpdateBatteryRunAsync(h.BatteryRunId, b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Pending);
        await h.UpdateBatteryRunAsync(second, b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing);
        await h.UpdateBatteryRunAsync(third, b => b.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed);

        await using (var db = new ApplicationDbContext(h.Options))
        {
            Assert.Equal(2, await BenchmarkBatteryReportDocumentService.SettleInterruptedAsync(db, Ct));
        }

        foreach (long id in new[] { h.BatteryRunId, second })
        {
            var batteryRun = await h.BatteryRunAsync(id);
            Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, batteryRun.ReportDocumentsStatus);
            Assert.Equal(BenchmarkBatteryReportDocumentService.RestartMessage, batteryRun.ReportDocumentsMessage);
        }
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, (await h.BatteryRunAsync(third)).ReportDocumentsStatus);
    }

    // --- Deletion ------------------------------------------------------------------------------------

    [Fact]
    public async Task SettleAfterDelete_RemovesTheBatteryRunsDocuments_AndForgetsTheJob()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.Service.ScheduleIfDue(h.BatteryRunId);
        long otherBatteryRunId = h.BatteryRunId + 1000;
        long other = await h.AddDocumentAsync(otherBatteryRunId, BenchmarkReportAudience.ExecutiveSummary);
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
        Assert.NotNull(h.Service.TryGetJob(h.BatteryRunId));

        Assert.Equal(2, await h.Service.SettleAfterDeleteAsync(h.BatteryRunId, Ct));

        Assert.Equal(other, Assert.Single(await h.DocumentsAsync()).Id);
        Assert.Null(h.Service.TryGetJob(h.BatteryRunId));
        Assert.Equal(0, await h.Service.SettleAfterDeleteAsync(h.BatteryRunId, Ct));
    }

    [Fact]
    public async Task SettleAfterDelete_WhileWriting_CancelsTheJobFirst_AndLeavesNoDocument()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        h.Writer.BlockAfterFirst = true;

        var job = h.Service.ScheduleIfDue(h.BatteryRunId);
        await h.Writer.FirstStored.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(1, await h.Service.SettleAfterDeleteAsync(h.BatteryRunId, Ct));
        await job.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Empty(await h.DocumentsAsync());
        Assert.False(h.Service.IsActive(h.BatteryRunId));
        Assert.Null(h.Service.TryGetJob(h.BatteryRunId));
    }

    [Fact]
    public async Task DeletingABatteryDocument_ReturnsTheBatteryRunToNotRequested()
    {
        await using var h = await BatteryReportHarness.CreateAsync();
        await h.Service.ScheduleIfDue(h.BatteryRunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());

        await using (var db = new ApplicationDbContext(h.Options))
        {
            var render = new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);
            Assert.True(await render.DeleteAsync((await h.DocumentsAsync())[0].Id, Ct));
        }

        var batteryRun = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, batteryRun.ReportDocumentsStatus);
        Assert.Null(batteryRun.ReportDocumentsMessage);
        Assert.Equal(h.WriterConfig.Id, batteryRun.ReportWriterModelConfigurationId);
        await using (var db = new ApplicationDbContext(h.Options))
        {
            Assert.Single(await BenchmarkBatteryReportDocumentService.MissingAudiencesAsync(db, h.BatteryRunId, Ct));
        }
    }

    [Fact]
    public void SubjectKeys_RoundTrip_AndOtherKeysAreRefused()
    {
        Assert.Equal("battery:7", BenchmarkBatteryReportDocumentService.SubjectKeyOf(7));
        Assert.True(BenchmarkBatteryReportDocumentService.TryParseSubjectKey("battery:7", out long id));
        Assert.Equal(7, id);
        foreach (var key in new[] { null, "", "run:7", "battery:", "battery:-7", "battery: 7", "group:7" })
        {
            Assert.False(BenchmarkBatteryReportDocumentService.TryParseSubjectKey(key, out _));
        }
    }
}

/// <summary>
/// Stores a fixture battery-completion document for each audience on the job, except
/// <see cref="FailAudience"/>, which it fails as a writer's reply that could not be parsed. Like the
/// real writer, it replaces the job's request with the battery run's. <see cref="Gate"/> holds the
/// job before its first document; <see cref="BlockAfterFirst"/> holds it after the first document is
/// stored until it is canceled. A cancellation settles the job as the real writer does.
/// </summary>
internal sealed class BatteryReportFakeWriter : IBenchmarkRunReportWriter
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private int _jobCalls;

    public BatteryReportFakeWriter(DbContextOptions<ApplicationDbContext> options) => _options = options;

    public BenchmarkReportAudience? FailAudience { get; set; }
    public BenchmarkReportAudience? WarnAudience { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public bool BlockAfterFirst { get; set; }
    public TaskCompletionSource FirstStored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int JobCalls => _jobCalls;
    public long? LastBatteryRunId { get; private set; }
    public long? LastWriterConfigId { get; private set; }
    public List<BenchmarkReportAudience> LastAudiences { get; private set; } = new();

    public Task WriteRunCompletionDocumentsAsync(BenchmarkReportPackJob job, CancellationToken ct)
        => throw new NotSupportedException("The battery tests write battery-completion documents only.");

    public async Task WriteBatteryCompletionDocumentsAsync(long batteryRunId, BenchmarkReportPackJob job, CancellationToken ct)
    {
        Interlocked.Increment(ref _jobCalls);
        LastBatteryRunId = batteryRunId;
        LastWriterConfigId = job.WriterConfigId;
        LastAudiences = job.Documents.Select(d => d.Audience).ToList();
        job.Request = BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, LastAudiences, job.WriterConfigId);
        job.SubjectKey = job.Request.SubjectKey;

        try
        {
            if (Gate != null) await Gate.Task.WaitAsync(ct);

            await using var db = new ApplicationDbContext(_options);
            int failed = 0;
            foreach (var progress in job.Documents.ToList())
            {
                ct.ThrowIfCancellationRequested();
                job.SetDocumentStatus(progress.Audience, BenchmarkReportPackDocumentStatus.Writing);
                if (progress.Audience == FailAudience)
                {
                    job.SetDocumentStatus(progress.Audience, BenchmarkReportPackDocumentStatus.Failed, "The writer's reply could not be parsed.");
                    failed++;
                    continue;
                }

                var document = BatteryReportHarness.BatteryDocument(batteryRunId, progress.Audience);
                document.WriterConfigId = job.WriterConfigId;
                document.WriterDisplayName = job.WriterDisplayName;
                document.SameProviderAcknowledged = job.SameProviderAcknowledged;
                document.Status = progress.Audience == WarnAudience
                    ? BenchmarkReportDocumentStatus.CompletedWithWarnings
                    : BenchmarkReportDocumentStatus.Completed;
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

/// <summary>
/// One analysed battery run of <see cref="BenchmarkBatteryTestData.Definition"/> (one round of both
/// suites, model <c>gpt-5.6-luna</c> of OpenAI) with an Anthropic report writer, the battery document
/// service over an in-memory database, and the battery report-documents controller.
/// </summary>
internal sealed class BatteryReportHarness : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DbContextOptions<ApplicationDbContext> Options { get; private init; } = default!;
    public IConfiguration Configuration { get; private init; } = default!;
    public BatteryReportFakeWriter Writer { get; private init; } = default!;
    public BenchmarkReportPackJobManager Jobs { get; } = new(TimeSpan.FromMilliseconds(10));
    public BenchmarkBatteryReportDocumentService Service { get; private set; } = default!;
    public SystemAiApiConfiguration WriterConfig { get; private set; } = default!;
    public long BatteryRunId { get; private set; }

    private ServiceProvider _provider = default!;
    private long _nextRunId = 100;

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

    /// <summary>
    /// The battery run, analysed unless <paramref name="analyse"/> is false; with
    /// <paramref name="complete"/> false it holds suite A only, so its analysis is incomplete.
    /// </summary>
    public static async Task<BatteryReportHarness> CreateAsync(
        int maxRunsPerHour = 100, bool withWriter = true, bool analyse = true, bool complete = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Benchmark:Compliance:MaxRunsPerHour"] = maxRunsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Benchmark:Compliance:MaxRunsPerDay"] = "1000"
        }).Build();

        var harness = new BatteryReportHarness
        {
            Options = options,
            Configuration = configuration,
            Writer = new BatteryReportFakeWriter(options)
        };

        var charts = TestChartStores.Unconfigured();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped(_ => new ApplicationDbContext(options));
        services.AddScoped<BenchmarkComplianceGuard>();
        services.AddScoped(sp => new BenchmarkReportRenderService(
            sp.GetRequiredService<ApplicationDbContext>(), charts, NullLogger<BenchmarkReportRenderService>.Instance));
        services.AddSingleton<IBenchmarkRunReportWriter>(harness.Writer);
        harness._provider = services.BuildServiceProvider();
        harness.Service = new BenchmarkBatteryReportDocumentService(
            harness._provider.GetRequiredService<IServiceScopeFactory>(),
            harness.Jobs,
            NullLogger<BenchmarkBatteryReportDocumentService>.Instance);

        await using var db = new ApplicationDbContext(options);
        harness.WriterConfig = Config("Anthropic", "claude-opus-5-5", "Claude Opus 5.5");
        db.SystemAiApiConfigurations.Add(harness.WriterConfig);
        await db.SaveChangesAsync(Ct);

        var members = new List<(BenchmarkRun Run, int SuiteIndex, int Round)> { (BenchmarkBatteryTestData.SuiteARun(1), 0, 1) };
        if (complete) members.Add((BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        harness.BatteryRunId = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(), members.ToArray());
        if (analyse)
        {
            var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(db).AnalyseAsync(harness.BatteryRunId, null, null, Ct);
            Assert.True(analysis != null, error);
        }

        var batteryRun = await db.BenchmarkBatteryRuns.SingleAsync(b => b.Id == harness.BatteryRunId, Ct);
        batteryRun.ReportWriterModelConfigurationId = withWriter ? harness.WriterConfig.Id : null;
        await db.SaveChangesAsync(Ct);

        return harness;
    }

    /// <summary>A stored battery-completion document of <paramref name="batteryRunId"/>, not yet saved.</summary>
    public static BenchmarkReportDocument BatteryDocument(long batteryRunId, BenchmarkReportAudience audience)
    {
        var document = BenchmarkReportPackFixture.StandaloneDocument(audience);
        document.Id = 0;
        document.Origin = BenchmarkReportDocumentOrigin.BatteryCompletion;
        document.SubjectKey = BenchmarkBatteryReportDocumentService.SubjectKeyOf(batteryRunId);
        document.SubjectRunIdsJson = "[1,2]";
        document.ComparisonKey = BenchmarkReportComparisonKey.From(Array.Empty<long>(), Array.Empty<long>(), new[] { batteryRunId });
        document.ComparisonRequestJson = "{\"runIds\":[],\"groupIds\":[],\"batteryRunIds\":[" + batteryRunId + "],\"pricingBasis\":1}";
        document.SuiteId = null;
        document.SuiteName = "Core knowledge";
        document.Runs = new List<BenchmarkReportDocumentRun>
        {
            new() { RunId = 1, ScoringMethodVersion = 9, SynthesisSha256 = "0123456789abcdef" }
        };
        return document;
    }

    public async Task<long> AddDocumentAsync(long batteryRunId, BenchmarkReportAudience audience)
    {
        await using var db = new ApplicationDbContext(Options);
        var document = BatteryDocument(batteryRunId, audience);
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(Ct);
        return document.Id;
    }

    public async Task<long> AddConfigAsync(string provider, string modelId, string name)
    {
        await using var db = new ApplicationDbContext(Options);
        var config = Config(provider, modelId, name);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(Ct);
        return config.Id;
    }

    /// <summary>A second round of suite A, attached after the analysis, which leaves the analysis stale.</summary>
    public async Task AddMemberAfterAnalysisAsync()
    {
        await using var db = new ApplicationDbContext(Options);
        long runId = _nextRunId++;
        db.BenchmarkRuns.Add(BenchmarkBatteryTestData.SuiteARun(runId));
        db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = BatteryRunId,
            BenchmarkRunId = runId,
            SuiteIndex = 0,
            Round = 2
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Another battery run, unanalysed, over two fresh member runs.</summary>
    public async Task<long> SeedAnotherBatteryRunAsync()
    {
        await using var db = new ApplicationDbContext(Options);
        long a = _nextRunId++;
        long b = _nextRunId++;
        return await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(a), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(b), 1, 1));
    }

    public AdminBenchmarkBatteryReportsController Controller()
    {
        var db = new ApplicationDbContext(Options);
        return new AdminBenchmarkBatteryReportsController(
            db,
            new BenchmarkComplianceGuard(Configuration, db),
            new BenchmarkModelComparisonService(db),
            new ModelPricingService(new ModelMetadataService(), db),
            new EndpointPolicy(Configuration),
            Configuration,
            Service);
    }

    public Task<BenchmarkBatteryRun> BatteryRunAsync() => BatteryRunAsync(BatteryRunId);

    public async Task<BenchmarkBatteryRun> BatteryRunAsync(long batteryRunId)
    {
        await using var db = new ApplicationDbContext(Options);
        return await db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(b => b.Id == batteryRunId, Ct);
    }

    public async Task<BenchmarkRunReportDocumentsStatus> StatusAsync() => (await BatteryRunAsync()).ReportDocumentsStatus;

    public Task UpdateBatteryRunAsync(Action<BenchmarkBatteryRun> update) => UpdateBatteryRunAsync(BatteryRunId, update);

    public async Task UpdateBatteryRunAsync(long batteryRunId, Action<BenchmarkBatteryRun> update)
    {
        await using var db = new ApplicationDbContext(Options);
        var batteryRun = await db.BenchmarkBatteryRuns.IgnoreAutoIncludes().SingleAsync(b => b.Id == batteryRunId, Ct);
        update(batteryRun);
        await db.SaveChangesAsync(Ct);
    }

    public async Task<List<BenchmarkReportDocument>> DocumentsAsync()
    {
        await using var db = new ApplicationDbContext(Options);
        return await db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes().OrderBy(d => d.Id).ToListAsync(Ct);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not reached in time.");
            await Task.Delay(10, Ct);
        }
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}
