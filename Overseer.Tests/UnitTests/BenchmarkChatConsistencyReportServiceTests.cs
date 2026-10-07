using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Chat consistency documents written end to end over the fake report writer: one document per
/// requested audience with its origin, scope, subject key, analysis id and fingerprint rows (the
/// control runs as peers), the Provider Issue Report's sample request ids, the refusals in their
/// order, a document-level C6 note marking the document, and the job's view and cancellation.
/// </summary>
public class BenchmarkChatConsistencyReportServiceTests
{
    private const BenchmarkReportAudience Es = BenchmarkReportAudience.ExecutiveSummary;
    private const BenchmarkReportAudience Pir = BenchmarkReportAudience.ProviderIssueReport;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WritingTwoDocuments_StoresOnePerAudience_AboutTheAnalysis_WithTheControlRunsAsPeers()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        h.Provider.Replies.Enqueue(ChatConsistencyReportHarness.Reply(ChatConsistencyReportTestData.ValidOutput(Es)));
        h.Provider.Replies.Enqueue(ChatConsistencyReportHarness.Reply(ChatConsistencyReportTestData.ValidOutput(Pir)));

        var start = await h.Service().WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Pir, Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        await h.Service().ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);

        Assert.Equal(StatusCodes.Status202Accepted, start.StatusCode);
        Assert.Equal(ChatConsistencyReportHarness.AnalysisId, start.Value!.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, start.Value.Status);
        Assert.Equal(new[] { Es, Pir }, start.Value.Audiences);

        var documents = await h.Db.BenchmarkReportDocuments.AsNoTracking().Include(d => d.Runs).OrderBy(d => d.Audience).ToListAsync(Ct);
        Assert.Equal(new[] { Es, Pir }, documents.Select(d => d.Audience));
        foreach (var document in documents)
        {
            Assert.Equal(BenchmarkReportDocumentOrigin.ChatConsistencyReport, document.Origin);
            Assert.Equal(BenchmarkReportScope.ChatConsistency, document.Scope);
            Assert.Equal(ChatConsistencyReportHarness.AnalysisId, document.ChatConsistencyAnalysisId);
            Assert.Equal("chat-consistency:7", document.SubjectKey);
            Assert.Equal("Test Model", document.SubjectLabel);
            Assert.Equal("Overseer Chat Consistency Report: Test Model", document.Title);
            Assert.Equal(BenchmarkReportDocumentStatus.Completed, document.Status);
            Assert.Equal(1, document.ReportFormatVersion);
            Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(document.Audience, BenchmarkReportScope.ChatConsistency), document.WriterPromptSha256);
            Assert.Equal(0, document.AnswerExcerptChars);
            Assert.Null(document.ComparisonId);
            Assert.Null(document.ComparisonKey);
            Assert.Equal(new long[] { 10, 11, 20, 21 }, BenchmarkReportJson.Deserialize<List<long>>(document.SubjectRunIdsJson));
            Assert.Equal(new long[] { 10, 11, 20, 21 }, document.Runs.Where(r => !r.IsPeer).Select(r => r.RunId).OrderBy(id => id));
            Assert.Equal(new long[] { 30, 31 }, document.Runs.Where(r => r.IsPeer).Select(r => r.RunId).OrderBy(id => id));
            Assert.All(document.Runs, r => Assert.Equal(16, r.SynthesisSha256.Length));
            Assert.Equal(h.Writer.Id, document.WriterConfigId);
            Assert.False(document.SameProviderAcknowledged);
        }

        var providerSheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(documents.Single(d => d.Audience == Pir).FactsJson);
        Assert.True(providerSheet.IsChatConsistency);
        Assert.Equal("req-alpha", FactValue(providerSheet, "requestIds.sample.1"));
        Assert.Equal("req-beta", FactValue(providerSheet, "requestIds.sample.2"));
        Assert.DoesNotContain(providerSheet.Facts, f => f.Key == "requestIds.sample.3");
        var executiveSheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(documents.Single(d => d.Audience == Es).FactsJson);
        Assert.DoesNotContain(executiveSheet.Facts, f => f.Key.StartsWith("requestIds.", StringComparison.Ordinal));
        Assert.Equal(new[] { "A" }, executiveSheet.Peers.Select(p => p.Letter));

        var usage = await h.Db.SystemAiUsageLogs.ToListAsync(Ct);
        Assert.Equal(2, usage.Count);
        Assert.All(usage, u => Assert.Equal(BenchmarkReportPackService.UsageRoleContext, u.RoleContext));

        var job = await h.Service().GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal(StatusCodes.Status200OK, job.StatusCode);
        Assert.Equal(ChatConsistencyReportHarness.AnalysisId, job.Value!.RunId);
        Assert.Equal("Finished", job.Value.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, job.Value.Status);
        Assert.Null(job.Value.Message);
        Assert.Equal(new[] { Es, Pir }, job.Value.Audiences);
        Assert.Equal(h.Writer.Id, job.Value.WriterConfigId);
        Assert.Equal("chat-consistency:7", job.Value.Job.SubjectKey);
        Assert.Equal(BenchmarkReportScope.ChatConsistency, job.Value.Job.Scope);
        Assert.All(job.Value.Job.Documents, d => Assert.Equal(nameof(BenchmarkReportPackDocumentStatus.Completed), d.Status));
    }

    [Fact]
    public async Task ADocumentThatNeverCitesTheHours_IsStoredWithWarnings_AfterARepairTurnWithTheChatReminders()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        var output = ChatConsistencyReportTestData.ValidOutput(Es);
        output.Headline = "The Overseer chat with {{subject}} is {{verdict.overall}}.";
        output.Sections[BenchmarkReportSlots.AsGoodAsBefore] = "Answer quality degraded by {{endpoint.P1.estimate}}. Waiting time is {{endpoint.P2.verdict}}.";
        string reply = ChatConsistencyReportHarness.Reply(output);
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var start = await h.Service().WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        await h.Service().ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);

        Assert.Equal(StatusCodes.Status202Accepted, start.StatusCode);
        Assert.Equal(2, h.Provider.Calls);
        string repair = h.Provider.Requests.ToArray()[1];
        Assert.Contains("Every document cites {{scope.hours}} at least once.", repair);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.ChatHoursRule && !n.Dropped);
        Assert.DoesNotContain(notes, n => n.Dropped);

        var job = await h.Service().GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, job.Value!.Status);
    }

    [Fact]
    public async Task TheProviderIssueReport_IsRefusedWithTheReason_WhileNoProviderSideFindingIsEstablishedOrIndicated()
    {
        var ours = ChatConsistencyReportTestData.Result().Attribution.Attributions.Where(a => a.Side != ChatConsistencyAttribution.SideProvider).ToList();
        var result = ChatConsistencyReportTestData.Result() with { Attribution = new ChatConsistencyAttributionOutcome { Attributions = ours } };
        await using var h = await ChatConsistencyReportHarness.CreateAsync(result);

        var refused = await h.Service().WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Es, Pir }, false, ChatConsistencyReportHarness.UserId, Ct);

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.ProviderIssueReportUnavailableReason, refused.Error);
        Assert.Equal(StatusCodes.Status204NoContent, (await h.Service().GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct)).StatusCode);

        var estimate = await h.Service().EstimateChatConsistencyDocumentsAsync(ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, null, Ct);
        Assert.Equal(StatusCodes.Status200OK, estimate.StatusCode);
        Assert.False(estimate.Value!.ProviderIssueReportAvailable);
        Assert.Equal(BenchmarkChatConsistencyReportFacts.ProviderIssueReportUnavailableReason, estimate.Value.ProviderIssueReportReason);
        Assert.Equal(new[] { Es, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief },
            estimate.Value.Estimates.Select(e => e.Audience));
    }

    [Fact]
    public async Task TheEstimate_PricesEachDocument_AndSaysTheProviderIssueReportIsAvailable()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();

        var estimate = await h.Service().EstimateChatConsistencyDocumentsAsync(ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Pir, Es }, Ct);

        Assert.Equal(StatusCodes.Status200OK, estimate.StatusCode);
        var dto = estimate.Value!;
        Assert.True(dto.ProviderIssueReportAvailable);
        Assert.Null(dto.ProviderIssueReportReason);
        Assert.Null(dto.Refusal);
        Assert.Null(dto.SameProviderWarning);
        Assert.Equal(new[] { Es, Pir }, dto.Estimates.Select(e => e.Audience));
        Assert.All(dto.Estimates, e =>
        {
            Assert.Equal("chat-consistency:7", e.SubjectKey);
            Assert.True(e.PromptChars > 0);
            Assert.Equal((e.PromptChars + 3) / 4, e.EstimatedInputTokens);
            Assert.True(e.EstimatedOutputTokens > 0);
        });
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task AWriterThatIsTheModelUnderReport_IsRefused_AndOneOfItsProviderNeedsAnAcknowledgment()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        var service = h.Service();

        var self = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.SubjectModel.Id, new[] { Es }, true, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status400BadRequest, self.StatusCode);
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, self.Error);

        var sameProvider = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.SameProvider.Id, new[] { Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status409Conflict, sameProvider.StatusCode);
        Assert.NotNull(sameProvider.SameProviderWarning);
        Assert.Equal("reportWriter", sameProvider.SameProviderWarning!.Role);
        Assert.Equal("TestProvider", sameProvider.SameProviderWarning.Provider);

        var estimate = await service.EstimateChatConsistencyDocumentsAsync(ChatConsistencyReportHarness.AnalysisId, h.SubjectModel.Id, new[] { Es }, Ct);
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, estimate.Value!.Refusal);

        Assert.Equal(StatusCodes.Status204NoContent, (await service.GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct)).StatusCode);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task TheStart_RefusesAnUnknownAnalysis_AnUnknownDocument_AndADocumentAlreadyWritten()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        var service = h.Service();

        Assert.Equal(StatusCodes.Status404NotFound,
            (await service.WriteChatConsistencyDocumentsAsync(99, h.Writer.Id, null, false, ChatConsistencyReportHarness.UserId, Ct)).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await service.EstimateChatConsistencyDocumentsAsync(99, h.Writer.Id, null, Ct)).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await service.GetChatConsistencyJobAsync(99, Ct)).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await service.CancelChatConsistencyJobAsync(99, Ct)).StatusCode);

        var unknown = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { (BenchmarkReportAudience)9 }, false, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status400BadRequest, unknown.StatusCode);
        Assert.Equal(BenchmarkReportPackService.ChatConsistencyInvalidAudienceMessage, unknown.Error);

        await h.StoreDocumentAsync(Es);
        var written = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status409Conflict, written.StatusCode);
        Assert.Equal("The Executive Summary is already written. Delete it first to write it again.", written.Error);

        var estimate = await service.EstimateChatConsistencyDocumentsAsync(ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, null, Ct);
        Assert.Equal(new[] { BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief, Pir },
            estimate.Value!.Estimates.Select(e => e.Audience));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task AQueuedJob_IsCanceledBeforeTheWritingBegins_AndNoJobAnswers204And409()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        var service = h.Service();
        Assert.Equal(StatusCodes.Status204NoContent, (await service.GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct)).StatusCode);
        var none = await service.CancelChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal(StatusCodes.Status409Conflict, none.StatusCode);
        Assert.Equal(BenchmarkReportPackService.ChatConsistencyNothingInProgressMessage, none.Error);

        var holder = h.HoldTheSlot();
        var start = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status202Accepted, start.StatusCode);

        var again = await service.WriteChatConsistencyDocumentsAsync(
            ChatConsistencyReportHarness.AnalysisId, h.Writer.Id, new[] { Es }, false, ChatConsistencyReportHarness.UserId, Ct);
        Assert.Equal(StatusCodes.Status409Conflict, again.StatusCode);
        Assert.Equal(BenchmarkReportPackService.ChatConsistencyAlreadyWritingMessage, again.Error);

        var queued = await service.GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal("Queued", queued.Value!.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, queued.Value.Status);
        Assert.Equal("Report Pack: Other", queued.Value.BlockingJobLabel);

        var canceled = await service.CancelChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal(StatusCodes.Status202Accepted, canceled.StatusCode);
        Assert.NotNull(canceled.Value!.CancelRequestedAtUtc);
        await service.ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);
        holder.SetStatus(BenchmarkReportPackJobStatus.Completed);

        var finished = await service.GetChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct);
        Assert.Equal("Finished", finished.Value!.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, finished.Value.Status);
        Assert.Equal(BenchmarkRunReportDocumentService.CanceledBeforeWritingMessage, finished.Value.Message);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.ToListAsync(Ct));
        Assert.Equal(0, h.Provider.Calls);
        Assert.Equal(StatusCodes.Status409Conflict, (await service.CancelChatConsistencyJobAsync(ChatConsistencyReportHarness.AnalysisId, Ct)).StatusCode);
    }

    [Fact]
    public void ARepairTurnOfAnotherScope_KeepsItsOwnReminders()
    {
        var issues = new[] { new BenchmarkReportValidationNote { Rule = 9, Location = "headline", Message = "An issue." } };

        Assert.Equal(BenchmarkReportPackPrompt.BuildRepairMessage(issues, comparisonScope: false),
            BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.Model));
        Assert.Equal(BenchmarkReportPackPrompt.BuildRepairMessage(issues, comparisonScope: true),
            BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.Comparison));
        Assert.Contains("Every document cites {{scope.hours}} at least once.",
            BenchmarkReportPackPrompt.BuildRepairMessage(issues, BenchmarkReportScope.ChatConsistency));
    }

    [Fact]
    public void ChatConsistencyFileNames_UseTheAnalysisStem_AndTheProviderIssueReportSlug()
    {
        var document = new BenchmarkReportDocument
        {
            Id = 41,
            Audience = Pir,
            Origin = BenchmarkReportDocumentOrigin.ChatConsistencyReport,
            Scope = BenchmarkReportScope.ChatConsistency,
            ChatConsistencyAnalysisId = 12,
            SubjectKey = "chat-consistency:12",
            SubjectLabel = "GPT-5.6 Luna",
            Title = "Overseer Chat Consistency Report: GPT-5.6 Luna"
        };
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Anonymized };

        Assert.Equal("chat-consistency-12_gpt-5.6-luna_provider-issue-report", Overseer.Services.Benchmarking.Pdf.BenchmarkPdfFileNames.ChatConsistencyStem(document));
        Assert.Equal("chat-consistency-12_gpt-5.6-luna_provider-issue-report_summary_anonymized.pdf",
            Overseer.Services.Benchmarking.Pdf.BenchmarkPdfFileNames.ForReportDocument(document, options));
        Assert.Equal("provider-issue-report", Overseer.Services.Benchmarking.Pdf.BenchmarkPdfFileNames.KindSlug(Pir));
        Assert.Equal("Provider Issue Report", BenchmarkReportRenderService.AudienceName(Pir));

        document.ChatConsistencyAnalysisId = null;
        document.Audience = Es;
        Assert.Equal("chat-consistency-12_gpt-5.6-luna_executive-summary_full_named_INTERNAL.docx",
            Overseer.Services.Benchmarking.Pdf.BenchmarkPdfFileNames.ForReportDocument(document,
                new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named }, "docx"));
    }

    private static string? FactValue(BenchmarkReportFactSheet sheet, string key)
    {
        var fact = Assert.Single(sheet.Facts, f => f.Key == key);
        return fact.Value?.GetValue<string>();
    }
}

/// <summary>
/// A saved chat consistency analysis (<see cref="ChatConsistencyReportTestData.Result"/>) with its
/// target runs 10, 11, 20 and 21, its control runs 30 and 31, candidate call telemetry with provider
/// request ids, and three report writers: one of another provider, the model under report, and one of
/// its provider. The service writes through the real agent loop over
/// <see cref="BenchmarkReportPackServiceTests.WriterProvider"/>; nothing leaves the process.
/// </summary>
internal sealed class ChatConsistencyReportHarness : IAsyncDisposable
{
    public const string UserId = "user-1";
    public const int AnalysisId = 7;

    private static readonly DateTime Day1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    public DbContextOptions<ApplicationDbContext> Options { get; private init; } = default!;
    public ApplicationDbContext Db { get; private init; } = default!;
    public IConfiguration Configuration { get; private init; } = default!;
    public BenchmarkReportPackJobManager Jobs { get; } = new(TimeSpan.FromMilliseconds(20));
    public BenchmarkReportPackServiceTests.WriterProvider Provider { get; } = new();

    /// <summary>Another provider than the model under report.</summary>
    public SystemAiApiConfiguration Writer { get; private set; } = default!;

    /// <summary>The model under report itself.</summary>
    public SystemAiApiConfiguration SubjectModel { get; private set; } = default!;

    /// <summary>Another model of the provider of the model under report.</summary>
    public SystemAiApiConfiguration SameProvider { get; private set; } = default!;

    public static IConfiguration TestConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AesEncryptionKey"] = Convert.ToBase64String(new byte[32]),
        ["Benchmark:Compliance:MaxRunsPerHour"] = "100",
        ["Benchmark:Compliance:MaxRunsPerDay"] = "100"
    }).Build();

    public static async Task<ChatConsistencyReportHarness> CreateAsync(ChatConsistencyAnalysisResult? result = null)
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var harness = new ChatConsistencyReportHarness
        {
            Options = options,
            Db = new ApplicationDbContext(options),
            Configuration = TestConfiguration()
        };
        await harness.SeedAsync(result ?? ChatConsistencyReportTestData.Result());
        return harness;
    }

    /// <summary>The writer's reply: the output's headline and sections as one JSON object.</summary>
    public static string Reply(BenchmarkReportWriterOutput output)
        => JsonSerializer.Serialize(new { headline = output.Headline, sections = output.Sections });

    private async Task SeedAsync(ChatConsistencyAnalysisResult result)
    {
        Db.ChatConsistencyAnalyses.Add(new ChatConsistencyAnalysis
        {
            Id = AnalysisId,
            Name = result.Name,
            SubjectModelKey = result.Subject.Key,
            BaselineStartUtc = result.Baseline.StartUtc,
            BaselineEndUtc = result.Baseline.EndUtc,
            ComparisonStartUtc = result.Comparison.StartUtc,
            ComparisonEndUtc = result.Comparison.EndUtc,
            TargetRunIdsJson = "[10,11,20,21]",
            ControlRunIdsJson = "[30,31]",
            ProtocolVersion = "1",
            ProtocolJson = "{}",
            ResultJson = JsonSerializer.Serialize(result, ChatConsistencyJson.Options),
            InputSha256 = result.InputSha256,
            AnalysisCodeVersion = result.AnalysisCodeVersion,
            CreatedAtUtc = Day1.AddDays(25)
        });

        foreach (long id in new long[] { 10, 11, 20, 21, 30, 31 })
        {
            Db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                Id = id,
                SuiteName = "Core suite",
                StartedAtUtc = Day1.AddDays(id < 20 ? 1 : 15).AddHours(id % 10),
                Status = BenchmarkRunStatus.Completed,
                FinalScore = 80,
                QualityIndex = 80,
                ScoringMethodVersion = 14,
                AssessmentJson = "{\"findings\":[]}"
            }));
        }

        Db.ModelCallTelemetry.AddRange(
            Telemetry(20, "req-beta", Day1.AddDays(15).AddHours(9)),
            Telemetry(21, "req-alpha", Day1.AddDays(16).AddHours(10)),
            Telemetry(21, "  ", Day1.AddDays(16).AddHours(9)),
            Telemetry(21, "req-alpha", Day1.AddDays(16).AddHours(8)),
            Telemetry(10, "req-baseline", Day1.AddDays(17)),
            Telemetry(21, "req-grader", Day1.AddDays(18), ModelCallSource.BenchmarkGrader));

        var crypto = new CryptoService(Configuration);
        var (cipher, nonce, tag) = crypto.Encrypt("test-api-key", "SYSTEM_API_KEY");
        SystemAiApiConfiguration Config(string provider, string modelId, string name) => new()
        {
            Provider = provider,
            ModelId = modelId,
            DisplayName = name,
            ModelRole = 4,
            IsEnabled = true,
            EncryptedApiKey = cipher,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
        };
        Writer = Config(BenchmarkReportPackServiceTests.WriterProvider.Name, "writer-1", "Writer One");
        SubjectModel = Config("TestProvider", "test-model-1", "Test Model");
        SameProvider = Config("TestProvider", "test-model-2", "Test Model Two");
        Db.SystemAiApiConfigurations.AddRange(Writer, SubjectModel, SameProvider);
        await Db.SaveChangesAsync();
    }

    private static ModelCallTelemetry Telemetry(long runId, string requestId, DateTime at, ModelCallSource source = ModelCallSource.BenchmarkCandidate) => new()
    {
        Source = source,
        BenchmarkRunId = runId,
        Provider = "TestProvider",
        RequestedModelId = "test-model-1",
        RequestId = requestId,
        StartedAtUtc = at,
        AttemptCount = 1
    };

    /// <summary>Stores a chat consistency document of <paramref name="audience"/> written from the analysis.</summary>
    public async Task StoreDocumentAsync(BenchmarkReportAudience audience)
    {
        Db.BenchmarkReportDocuments.Add(new BenchmarkReportDocument
        {
            PackId = Guid.NewGuid(),
            Audience = audience,
            Origin = BenchmarkReportDocumentOrigin.ChatConsistencyReport,
            Scope = BenchmarkReportScope.ChatConsistency,
            ChatConsistencyAnalysisId = AnalysisId,
            SubjectKey = "chat-consistency:7",
            SubjectLabel = "Test Model",
            SubjectRunIdsJson = "[10,11,20,21]",
            ComparisonRequestJson = "{}",
            SuiteName = "Core suite",
            WriterDisplayName = "Writer One",
            WriterProvider = BenchmarkReportPackServiceTests.WriterProvider.Name,
            WriterModelId = "writer-1",
            WriterPromptSha256 = new string('a', 64),
            FactsJson = "{}",
            ContentJson = "{}",
            WriterOutputJson = "{}",
            ValidationNotesJson = "[]",
            Title = "Overseer Chat Consistency Report: Test Model",
            CreatedAtUtc = Day1.AddDays(26)
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>A running Report Pack job that holds the report-pack slot until its status is set.</summary>
    public BenchmarkReportPackJob HoldTheSlot()
    {
        var job = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Core suite", WriterConfigId = Writer.Id, Cts = new CancellationTokenSource() };
        Assert.True(Jobs.TryStart(job, out _));
        return job;
    }

    /// <summary>A report-pack service over this harness's database, slot and writer; its chat consistency jobs run on it.</summary>
    public BenchmarkReportPackService Service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ApplicationDbContext(Options));
        services.AddSingleton<IAiProvider>(Provider);
        return ServiceFor(Db, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Configuration, Jobs, Provider);
    }

    /// <summary>A report-pack service over <paramref name="db"/>, writing through the real agent loop over <paramref name="provider"/>.</summary>
    public static BenchmarkReportPackService ServiceFor(
        ApplicationDbContext db, IServiceScopeFactory agentScopes, IConfiguration configuration, BenchmarkReportPackJobManager jobs, IAiProvider provider)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handlers = new List<IToolHandler>();
        var bridge = new BenchmarkReportPackServiceTests.NoClientBridge();
        var runner = new AgentLoopRunner(
            new IAiProvider[] { provider },
            new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, configuration),
            new BenchmarkReportPackServiceTests.ReplyEchoHttpClientFactory(),
            configuration,
            agentScopes,
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, configuration),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance);

        return new BenchmarkReportPackService(
            db,
            runner,
            new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance),
            new CryptoService(configuration),
            new EndpointPolicy(configuration),
            new ModelPricingService(new ModelMetadataService(), db),
            new BenchmarkModelComparisonService(db),
            jobs,
            configuration,
            NullLogger<BenchmarkReportPackService>.Instance);
    }

    /// <summary>The chat consistency controller over this harness's database and report-pack service, as an administrator.</summary>
    public AdminChatConsistencyController Controller(IServiceScopeFactory regradeScopes)
    {
        var evidence = new ChatConsistencyEvidenceBuilder(Db);
        var regrade = new ChatConsistencyRegradeService(
            Db, new ChatConsistencyRegradeJobManager(), regradeScopes, new BenchmarkRunManager(),
            NullLogger<ChatConsistencyRegradeService>.Instance, calibrationRunner: (_, _, _, _) => Task.CompletedTask);
        return new AdminChatConsistencyController(
            new ChatConsistencyAnalysisService(Db, evidence, NullLogger<ChatConsistencyAnalysisService>.Instance), evidence, regrade, Service())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, "admin"),
                        new Claim(ClaimTypes.NameIdentifier, UserId)
                    }, "Test"))
                }
            }
        };
    }

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}
