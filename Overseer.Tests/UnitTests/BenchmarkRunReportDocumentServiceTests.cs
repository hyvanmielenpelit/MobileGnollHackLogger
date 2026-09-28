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
using Xunit;

/// <summary>
/// The run-completion documents: when a finished run's two AI-written documents are written, that
/// they are written once, how a busy report-pack slot, the compliance guard, a missing writer and a
/// failed document settle the run's status, that a download never reaches the writer, the restart
/// settlement, and the write-now endpoint. The writer is a fake; nothing calls a provider.
/// </summary>
public class BenchmarkRunReportDocumentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task ACompletedRun_GetsBothDocuments_StoredAsRunCompletionDocuments()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.ScheduleIfDue(h.RunId);

        var documents = await h.DocumentsAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, d.Origin);
            Assert.Equal(BenchmarkRunReportDocumentService.SubjectKeyOf(h.RunId), d.SubjectKey);
        });
        Assert.Equal(
            new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport },
            documents.Select(d => d.Audience).OrderBy(a => a));

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, run.ReportDocumentsStatus);
        Assert.Null(run.ReportDocumentsMessage);
        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(2, h.Writer.Calls);
        Assert.Equal(h.WriterConfig.Id, h.Writer.LastWriterConfigId);
    }

    [Fact]
    public async Task ASecondSchedule_CallsTheWriterNoMore()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.ScheduleIfDue(h.RunId);
        await h.Service.ScheduleIfDue(h.RunId);

        Assert.Equal(1, h.Writer.JobCalls);
        Assert.Equal(2, h.Writer.Calls);
        Assert.Equal(2, (await h.DocumentsAsync()).Count);
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
    public async Task OneFailedDocument_FailsTheRun_KeepsTheOther_AndWriteNowWritesOnlyTheMissingOne()
    {
        await using var h = await Harness.CreateAsync();
        h.Writer.FailAudience = BenchmarkReportAudience.TechnicalReport;

        await h.Service.ScheduleIfDue(h.RunId);

        var run = await h.RunAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, run.ReportDocumentsStatus);
        Assert.StartsWith("Report for AI Researchers and Developers: ", run.ReportDocumentsMessage);
        var kept = Assert.Single(await h.DocumentsAsync());
        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, kept.Audience);

        // A failed run is not written again automatically.
        await h.Service.ScheduleIfDue(h.RunId);
        Assert.Equal(1, h.Writer.JobCalls);

        h.Writer.FailAudience = null;
        Assert.True(h.Service.TryStart(h.RunId, "user-1", out var completion));
        await completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(3, h.Writer.Calls);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport }, h.Writer.LastAudiences);
        Assert.Equal(2, (await h.DocumentsAsync()).Count);
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
        var render = new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance);
        foreach (var document in await h.DocumentsAsync())
        {
            var (markdown, notFound, refusal) = await render.RenderAsync(document.Id, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Full,
                PeerNaming = BenchmarkReportPeerNaming.Named
            }, Ct);

            Assert.False(notFound);
            Assert.Null(refusal);
            Assert.Contains("## Evaluation terms", markdown);
        }

        Assert.Equal(calls, h.Writer.Calls);
        Assert.Empty(await db.SystemAiUsageLogs.ToListAsync(Ct));

        var listed = await render.ListAsync(null, h.RunId, null, Ct);
        Assert.Equal(2, listed.Count);
        Assert.All(listed, d => Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, d.Origin));
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
                new BenchmarkReportPackDocumentProgress { Audience = BenchmarkReportAudience.TechnicalReport, Status = BenchmarkReportPackDocumentStatus.Canceled }
            }
        };

        var (status, message) = BenchmarkRunReportDocumentService.OutcomeOf(job);

        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, status);
        Assert.Equal("Report for AI Researchers and Developers: the writing was canceled.", message);
    }

    // --- Writer checks -------------------------------------------------------------------------------

    [Fact]
    public void WriterRefusal_RefusesAnUnusableWriter_TheModelUnderTest_AndItsProvider()
    {
        var guard = new BenchmarkComplianceGuard(new ConfigurationBuilder().Build(), null!);
        var candidate = new SystemAiApiConfiguration { Provider = "OpenAI", ModelId = "gpt-5.6-luna", DisplayName = "GPT Luna" };
        SystemAiApiConfiguration Config(string provider, string modelId, bool enabled = true) => new()
        {
            Provider = provider, ModelId = modelId, DisplayName = modelId, ModelRole = 4, IsEnabled = enabled, EncryptedApiKey = "dummy_encrypted"
        };

        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage, BenchmarkRunReportDocumentService.WriterRefusal(null, candidate, guard));
        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage,
            BenchmarkRunReportDocumentService.WriterRefusal(Config("Anthropic", "claude-opus-5-5", enabled: false), candidate, guard));
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage,
            BenchmarkRunReportDocumentService.WriterRefusal(Config("OpenAI", "gpt-5.6-luna"), candidate, guard));
        Assert.Equal(BenchmarkRunReportDocumentService.SameProviderMessage,
            BenchmarkRunReportDocumentService.WriterRefusal(Config("openai", "gpt-5.6-sol"), candidate, guard));
        Assert.Null(BenchmarkRunReportDocumentService.WriterRefusal(Config("Anthropic", "claude-opus-5-5"), candidate, guard));
    }

    // --- Write now -----------------------------------------------------------------------------------

    [Fact]
    public async Task WriteNow_SetsTheWriter_AnswersAccepted_AndWritesBothDocuments()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);

        var result = await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id }, Ct);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value);
        Assert.Equal(h.RunId, response.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, response.Status);

        await WaitUntilAsync(() => !h.Service.IsActive(h.RunId));
        var run = await h.RunAsync();
        Assert.Equal(h.WriterConfig.Id, run.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, run.ReportDocumentsStatus);
        Assert.Equal(2, (await h.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task WriteNow_AnswersConflict_WhenBothDocumentsExist_OrAJobIsPending()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.ScheduleIfDue(h.RunId);
        var request = new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.WriterConfig.Id };

        var both = Assert.IsType<ConflictObjectResult>(await h.Controller().WriteRunReportDocuments(h.RunId, request, Ct));
        Assert.Contains("already has both", JsonSerializer.Serialize(both.Value));

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
    public async Task WriteNow_RefusesAWriterFromTheCandidatesProvider_AndARunWithoutSynthesis()
    {
        await using var h = await Harness.CreateAsync(withWriter: false);
        long sameProviderId;
        await using (var db = new ApplicationDbContext(h.Options))
        {
            var sameProvider = Harness.Config("OpenAI", "gpt-5.6-sol", "GPT Sol");
            db.SystemAiApiConfigurations.Add(sameProvider);
            await db.SaveChangesAsync(Ct);
            sameProviderId = sameProvider.Id;
        }

        var refused = Assert.IsType<BadRequestObjectResult>(await h.Controller().WriteRunReportDocuments(
            h.RunId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = sameProviderId }, Ct));
        Assert.Contains(BenchmarkRunReportDocumentService.SameProviderMessage, JsonSerializer.Serialize(refused.Value));

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

    /// <summary>
    /// Stores a fixture run-completion document for each audience on the job, except
    /// <see cref="FailAudience"/>, which it fails as a writer's reply that could not be parsed.
    /// </summary>
    private sealed class FakeWriter : IBenchmarkRunReportWriter
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private int _calls;
        private int _jobCalls;

        public FakeWriter(DbContextOptions<ApplicationDbContext> options) => _options = options;

        public BenchmarkReportAudience? FailAudience { get; set; }
        public BenchmarkReportAudience? WarnAudience { get; set; }
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

            await using var db = new ApplicationDbContext(_options);
            int failed = 0;
            foreach (var progress in job.Documents.ToList())
            {
                Interlocked.Increment(ref _calls);
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
                document.Status = progress.Audience == WarnAudience
                    ? BenchmarkReportDocumentStatus.CompletedWithWarnings
                    : BenchmarkReportDocumentStatus.Completed;
                document.Runs = new List<BenchmarkReportDocumentRun>
                {
                    new() { RunId = runId, ScoringMethodVersion = 9, SynthesisSha256 = "0123456789abcdef" }
                };
                db.BenchmarkReportDocuments.Add(document);
                await db.SaveChangesAsync(ct);

                job.SetDocumentStatus(progress.Audience,
                    document.Status == BenchmarkReportDocumentStatus.Completed
                        ? BenchmarkReportPackDocumentStatus.Completed
                        : BenchmarkReportPackDocumentStatus.CompletedWithWarnings,
                    documentId: document.Id);
            }

            job.SetStatus(failed == 0 ? BenchmarkReportPackJobStatus.Completed : BenchmarkReportPackJobStatus.CompletedWithErrors);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public DbContextOptions<ApplicationDbContext> Options { get; private init; } = default!;
        public BenchmarkRunExamTests.SeededSuite Seeded { get; private init; } = default!;
        public IConfiguration Configuration { get; private init; } = default!;
        public FakeWriter Writer { get; private init; } = default!;
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
                NullLogger<BenchmarkRunReportDocumentService>.Instance);

            await using var db = new ApplicationDbContext(options);
            harness.WriterConfig = Config("Anthropic", "claude-opus-5-5", "Claude Opus 5.5");
            db.SystemAiApiConfigurations.Add(harness.WriterConfig);
            await db.SaveChangesAsync();

            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == seeded.RunIds[0]);
            run.AssessmentJson = "{\"finalScore\":80,\"findings\":[]}";
            run.ReportWriterModelConfigurationId = withWriter ? harness.WriterConfig.Id : null;
            await db.SaveChangesAsync();

            return harness;
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

        public async Task UpdateRunAsync(Action<BenchmarkRun> update)
        {
            await using var db = new ApplicationDbContext(Options);
            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == RunId);
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
