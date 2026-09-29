using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Report-pack generation end to end over a fake writer (valid, repaired, still invalid, provider
/// error, canceled), the start refusals in their order, and the structure that keeps every model
/// call out of the download path.
/// </summary>
public class BenchmarkReportPackServiceTests
{
    private const string UserId = "user-1";

    // --- Generation through the real agent loop -----------------------------------------------------

    [Fact]
    public async Task AValidReply_IsStoredCompleted_WithOneCallRecordedUnderRoleContext8()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.Include(d => d.Runs).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.Completed, document.Status);
        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, document.Audience);
        Assert.Equal(BenchmarkReportDocumentOrigin.ReportPack, document.Origin);
        Assert.Equal(job.PackId, document.PackId);
        Assert.Equal($"run:{h.Seeded.RunIds[0]}", document.SubjectKey);
        Assert.Equal(BenchmarkReportPackRenderer.ReportFormatVersion, document.ReportFormatVersion);
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.ExecutiveSummary), document.WriterPromptSha256);
        Assert.Equal(BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, document.AnswerExcerptChars);
        Assert.Equal(WriterProvider.PromptTokens, document.InputTokens);
        Assert.Equal(WriterProvider.OutputTokens, document.OutputTokens);

        var child = Assert.Single(document.Runs);
        Assert.Equal(h.Seeded.RunIds[0], child.RunId);
        Assert.Equal(16, child.SynthesisSha256.Length);

        Assert.Equal(1, h.Provider.Calls);
        var usage = Assert.Single(await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportPackService.UsageRoleContext, usage.RoleContext);
        Assert.Equal(8, usage.RoleContext);
        Assert.Equal(h.Writer.Id, usage.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task ACutAnswer_IsStoredWhole_InTheContentJson()
    {
        await using var h = await Harness.CreateAsync();
        string longAnswer = string.Join(" ", Enumerable.Repeat("The unicorn catches the gem and your Luck rises.", 20)) + " FINAL-SENTENCE.";
        var answer = await h.Db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == h.Seeded.RunIds[0])
            .OrderBy(a => a.OrderIndex)
            .FirstAsync(TestContext.Current.CancellationToken);
        answer.AnswerText = longAnswer;
        await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
        var question = content.Runs.SelectMany(r => r.Questions).Single(q => q.AnswerExcerptCut);
        Assert.Equal(longAnswer, question.AnswerText);
        Assert.DoesNotContain("FINAL-SENTENCE", question.AnswerExcerpt);
        Assert.Contains("\"answerText\":", document.ContentJson);
        Assert.Equal(5, document.ReportFormatVersion);
    }

    [Fact]
    public async Task AnUnrepairedIntervalAdjective_IsKept_AndMarksTheDocumentWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        const string confidence = "The interval is narrow, so the result should be read with care.";
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, confidence: confidence));
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, confidence: confidence));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        Assert.Equal(confidence, writer.Sections[BenchmarkReportSlots.Confidence]);
        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson!);
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.IntervalWidthRule && !n.Dropped);
    }

    [Fact]
    public async Task AnInvalidFirstReply_IsRepairedOnce_AndBothCallsAreRecorded()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: "{{subject}} answered 3 questions well."));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.Completed, document.Status);
        Assert.Equal(2 * WriterProvider.PromptTokens, document.InputTokens);
        Assert.Equal(2 * WriterProvider.OutputTokens, document.OutputTokens);

        Assert.Equal(2, h.Provider.Calls);
        var usage = await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, usage.Count);
        Assert.All(usage, u => Assert.Equal(BenchmarkReportPackService.UsageRoleContext, u.RoleContext));

        // The repair turn carries the first reply and the repair message on the same conversation.
        Assert.Equal(h.Provider.MessageCounts[0] + 2, h.Provider.MessageCounts[1]);
    }

    [Fact]
    public async Task EachDocument_RecordsItsStartAndCompletionTimes_TokensAndCost()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: "{{subject}} answered 3 questions well."));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        var before = DateTime.UtcNow;

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        var dto = job.ToDto();
        var progress = Assert.Single(dto.Documents);
        Assert.Equal(nameof(BenchmarkReportPackDocumentStatus.Completed), progress.Status);
        Assert.NotNull(progress.StartedAtUtc);
        Assert.NotNull(progress.CompletedAtUtc);
        Assert.True(progress.StartedAtUtc >= before);
        Assert.True(progress.CompletedAtUtc >= progress.StartedAtUtc);
        Assert.Equal(2, progress.ModelCalls);
        Assert.Equal(2L * WriterProvider.PromptTokens, progress.InputTokens);
        Assert.Equal(2L * WriterProvider.OutputTokens, progress.OutputTokens);
        Assert.Equal(dto.InputTokens, progress.InputTokens);
        Assert.Equal(dto.OutputTokens, progress.OutputTokens);
        Assert.Equal(dto.CostUsd, progress.CostUsd);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(document.InputTokens, progress.InputTokens);
        Assert.Equal(document.OutputTokens, progress.OutputTokens);
    }

    [Fact]
    public async Task AnItemStillInvalidAfterRepair_IsDropped_AndTheDocumentCompletesWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = ExecutiveReply(prep, extraStrength: "It handled 7 hard questions.");
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        Assert.Contains(notes, n => n.Dropped && n.Location.StartsWith("strengths", StringComparison.Ordinal));
        var output = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        Assert.DoesNotContain(output.Strengths, s => s.Text.Contains('7'));
        Assert.Equal(2, h.Provider.Calls);
    }

    [Fact]
    public async Task ABritishSpellingStillThereAfterRepair_IsKept_AndTheDocumentCompletesWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = ExecutiveReply(prep, extraStrength: "{{subject}} showed sound judgement on item lore.");
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.UsSpellingRule && !n.Dropped && n.Location == "strengths[1]");
        Assert.DoesNotContain(notes, n => n.Dropped);
        var output = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        Assert.Contains(output.Strengths, s => s.Text.Contains("judgement", StringComparison.Ordinal));
        Assert.Equal(2, h.Provider.Calls);
    }

    [Fact]
    public async Task ARunCompletionJob_WritesAStandaloneDocument_StoredWithItsOrigin()
    {
        await using var h = await Harness.CreateAsync();
        long runId = h.Seeded.RunIds[0];
        var prep = await h.PrepareAsync(standalone: true);
        Assert.Empty(prep.Sheet.Peers);
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunCompletionAsync(BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.Include(d => d.Runs).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, document.Origin);
        Assert.Equal($"run:{runId}", document.SubjectKey);
        Assert.Equal(runId, Assert.Single(document.Runs).RunId);

        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        Assert.Empty(sheet.Peers);
        Assert.Equal(BenchmarkReportFacts.StandaloneReason, sheet.Facts.Single(f => f.Key == "quality.rank").UnavailableReason);

        var usage = Assert.Single(await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportPackService.UsageRoleContext, usage.RoleContext);
    }

    [Fact]
    public async Task AProviderError_StoresNothing_AndFailsTheDocument()
    {
        await using var h = await Harness.CreateAsync();
        await h.PrepareAsync();
        h.Provider.Replies.Enqueue(WriterProvider.ProviderError);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Failed, job.Status);
        Assert.Equal(BenchmarkReportPackDocumentStatus.Failed, Assert.Single(job.Documents).Status);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACanceledJob_StoresNothing_AndIsMarkedCanceled()
    {
        await using var h = await Harness.CreateAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, cts.Token);

        Assert.Equal(BenchmarkReportPackJobStatus.Canceled, job.Status);
        Assert.Equal(BenchmarkReportPackDocumentStatus.Canceled, Assert.Single(job.Documents).Status);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, h.Provider.Calls);
    }

    // --- Start refusals, in order ---------------------------------------------------------------------

    [Fact]
    public async Task Start_RefusesAnUnknownSubjectFirst()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request(subjectKey: "run:999999", writerId: h.Disabled.Id, audiences: Array.Empty<BenchmarkReportAudience>());

        var result = await h.Controller().Start(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("not an entry", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Start_RefusesAnUnusableWriter_BeforeTheMissingDocuments()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request(writerId: h.Disabled.Id, audiences: Array.Empty<BenchmarkReportAudience>());

        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Contains("invalid, disabled", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Start_RefusesTheSubjectsOwnModelAsWriter_BeforeTheMissingDocuments()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request(writerId: h.SameModel.Id, audiences: Array.Empty<BenchmarkReportAudience>());

        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Contains("cannot write its own report", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Start_RefusesNoDocuments_BeforeTheSpendCap()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var request = h.Request(audiences: Array.Empty<BenchmarkReportAudience>());

        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Contains("at least one document", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Start_RefusesAtTheSpendCap_BeforeTheSameProviderWarning()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var request = h.Request(writerId: h.SameProvider.Id);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(429, result.StatusCode);
    }

    [Fact]
    public async Task Start_AnswersAnUnacknowledgedSameProviderWriterWith409AndTheWarning_BeforeARunningJob()
    {
        await using var h = await Harness.CreateAsync();
        h.StartRunningJob();
        var request = h.Request(writerId: h.SameProvider.Id);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
        var warning = Assert.IsType<SameProviderWarningDto>(result.Value);
        Assert.Equal("OpenAI", warning.Provider);
    }

    [Fact]
    public async Task Start_AnswersARunningJobWith409AndItsState()
    {
        await using var h = await Harness.CreateAsync();
        var running = h.StartRunningJob();
        var request = h.Request(writerId: h.SameProvider.Id, acknowledgeSameProvider: true);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(running.Id, Assert.IsType<BenchmarkReportPackJobDto>(result.Value).Id);
    }

    // --- Preview ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_NamesThePeers_EstimatesEachDocument_AndMakesNoModelCall()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport });

        var ok = Assert.IsType<OkObjectResult>(await h.Controller().Preview(request, CancellationToken.None));
        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(ok.Value);

        Assert.Null(preview.Refusal);
        Assert.Null(preview.SameProviderWarning);
        Assert.Equal($"run:{h.Seeded.RunIds[0]}", preview.SubjectKey);
        Assert.Equal("A", Assert.Single(preview.Peers).Letter);
        Assert.Equal(2, preview.Estimates.Count);
        Assert.All(preview.Estimates, e => Assert.True(e.EstimatedInputTokens > 0));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Preview_WarnsAboutASameProviderWriter_AndRefusesTheSubjectsOwnModel()
    {
        await using var h = await Harness.CreateAsync();

        var sameProvider = (BenchmarkReportPackPreviewDto)Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(h.Request(writerId: h.SameProvider.Id), CancellationToken.None)).Value!;
        Assert.Null(sameProvider.Refusal);
        Assert.Contains("same provider", sameProvider.SameProviderWarning);

        var sameModel = (BenchmarkReportPackPreviewDto)Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(h.Request(writerId: h.SameModel.Id), CancellationToken.None)).Value!;
        Assert.Contains("cannot write its own report", sameModel.Refusal);
    }

    // --- Documents controller ---------------------------------------------------------------------------

    [Fact]
    public async Task Render_IsNotFoundForAnUnknownDocument_AndRefusesADisallowedCombination()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.InternalBrief);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        Assert.IsType<NotFoundResult>(await controller.Render(document.Id + 1000, "full", "named", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Render(document.Id, "summary", "named", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Render(document.Id, "everything", "named", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Render(document.Id, "3", "named", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Render(document.Id, "full", "pseudonymous", CancellationToken.None));
    }

    [Fact]
    public async Task Render_ReturnsTheRenderersMarkdown_AndTheSameBytesEveryTime()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        var first = Assert.IsType<ContentResult>(await controller.Render(document.Id, "detailed", "anonymized", CancellationToken.None));
        var second = Assert.IsType<ContentResult>(await controller.Render(document.Id, "Detailed", "Anonymized", CancellationToken.None));

        Assert.Equal("text/markdown; charset=utf-8", first.ContentType);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(
            BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Detailed,
                PeerNaming = BenchmarkReportPeerNaming.Anonymized
            }),
            first.Content);
    }

    [Fact]
    public async Task RenderPdf_AnswersAsRenderDoes_AndRefusesAnUnknownPaper()
    {
        BenchmarkPdfTestSetup.Configure();
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.InternalBrief);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        Assert.IsType<NotFoundResult>(await controller.RenderPdf(document.Id + 1000, "full", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderPdf(document.Id, "summary", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderPdf(document.Id, "everything", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderPdf(document.Id, "3", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderPdf(document.Id, "full", "pseudonymous", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderPdf(document.Id, "full", "named", "a3", CancellationToken.None));
    }

    [Fact]
    public async Task RenderPdf_ReturnsAPdf_NamedAsTheDownloadCenterNamesTheMarkdown()
    {
        BenchmarkPdfTestSetup.Configure();
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        var provider = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "detailed", "anonymized", "letter", CancellationToken.None));
        var full = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "Full", "Named", null, CancellationToken.None));

        Assert.Equal("application/pdf", provider.ContentType);
        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
            provider.FileDownloadName);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(provider.FileContents, 0, 5));
        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_full_named_INTERNAL.pdf",
            full.FileDownloadName);
    }

    [Fact]
    public async Task RenderPdf_Inline_SendsAnInlineDispositionWithTheSameName()
    {
        BenchmarkPdfTestSetup.Configure();
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var pdf = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "detailed", "anonymized", null, CancellationToken.None, inline: true));

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.True(string.IsNullOrEmpty(pdf.FileDownloadName));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5));
        Assert.Equal(
            "inline; filename*=UTF-8''run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
            controller.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task DeleteDocument_OfARunCompletionDocument_SettlesTheRunsStatus()
    {
        await using var h = await Harness.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        long runId = h.Seeded.RunIds[0];
        var run = await h.Db.BenchmarkRuns.SingleAsync(r => r.Id == runId, ct);
        run.ReportWriterModelConfigurationId = h.Writer.Id;
        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Failed;
        run.ReportDocumentsMessage = "Report for AI Researchers and Developers: it could not be written.";

        BenchmarkReportDocument Stored(BenchmarkReportAudience audience, BenchmarkReportDocumentOrigin origin)
        {
            var document = BenchmarkReportPackFixture.StandaloneDocument(audience);
            document.Id = 0;
            document.Origin = origin;
            document.SubjectKey = $"run:{runId}";
            document.Runs = new List<BenchmarkReportDocumentRun>();
            h.Db.BenchmarkReportDocuments.Add(document);
            return document;
        }
        var packDocument = Stored(BenchmarkReportAudience.InternalBrief, BenchmarkReportDocumentOrigin.ReportPack);
        var executive = Stored(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentOrigin.RunCompletion);
        var researcher = Stored(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDocumentOrigin.RunCompletion);
        await h.Db.SaveChangesAsync(ct);

        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(h.Db, NullLogger<BenchmarkReportRenderService>.Instance));

        async Task<BenchmarkRun> RunNowAsync()
        {
            await using var db = new ApplicationDbContext(h.Options);
            return await db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(r => r.Id == runId, ct);
        }

        // A report pack's document about the same run leaves the run's status alone.
        Assert.IsType<NoContentResult>(await controller.Delete(packDocument.Id, ct));
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, (await RunNowAsync()).ReportDocumentsStatus);

        // While the documents are being written, a delete leaves the status to the job.
        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Writing;
        await h.Db.SaveChangesAsync(ct);
        Assert.IsType<NoContentResult>(await controller.Delete(researcher.Id, ct));
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Writing, (await RunNowAsync()).ReportDocumentsStatus);

        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed;
        await h.Db.SaveChangesAsync(ct);
        Assert.IsType<NoContentResult>(await controller.Delete(executive.Id, ct));

        var settled = await RunNowAsync();
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, settled.ReportDocumentsStatus);
        Assert.Null(settled.ReportDocumentsMessage);
        Assert.Equal(h.Writer.Id, settled.ReportWriterModelConfigurationId);
        Assert.IsType<NotFoundResult>(await controller.Delete(executive.Id, ct));
    }

    [Fact]
    public async Task List_FlagsADocumentWhoseRunWasRescoredOrDeleted()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);
        var render = new BenchmarkReportRenderService(h.Db, NullLogger<BenchmarkReportRenderService>.Instance);
        long runId = h.Seeded.RunIds[0];

        var fresh = Assert.Single(await render.ListAsync(null, runId, null, CancellationToken.None));
        Assert.False(fresh.RunChangedSinceGeneration);
        Assert.Equal(new[] { runId }, fresh.SubjectRunIds);

        var run = await h.Db.BenchmarkRuns.SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
        run.QualityIndex = (run.QualityIndex ?? 0) + 1;
        await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(await render.ListAsync(null, runId, null, CancellationToken.None)).RunChangedSinceGeneration);

        h.Db.BenchmarkRuns.Remove(run);
        await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var orphan = Assert.Single(await render.ListAsync(h.Seeded.SuiteId, null, null, CancellationToken.None));
        Assert.True(orphan.RunChangedSinceGeneration);
        Assert.Equal(new[] { runId }, orphan.MissingRunIds);
    }

    // --- The download path cannot reach a model (D13) -------------------------------------------------

    [Fact]
    public void TheRenderService_TakesOnlyTheDbContextAndALogger()
    {
        var constructor = Assert.Single(typeof(BenchmarkReportRenderService).GetConstructors());
        Assert.Equal(
            new[] { typeof(ApplicationDbContext), typeof(ILogger<BenchmarkReportRenderService>) },
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void TheRenderer_IsAStaticClassWithoutFields()
    {
        var type = typeof(BenchmarkReportPackRenderer);
        Assert.True(type.IsAbstract && type.IsSealed, "BenchmarkReportPackRenderer must be static.");
        var fields = type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.All(fields, f => Assert.True(f.IsLiteral, $"{f.Name} is a field, not a constant."));
    }

    [Fact]
    public void TheDocumentsController_TakesOnlyTheRenderService_AndNoActionInjectsAService()
    {
        var type = typeof(AdminBenchmarkReportDocumentsController);
        var constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(new[] { typeof(BenchmarkReportRenderService) }, constructor.GetParameters().Select(p => p.ParameterType));

        var actions = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        Assert.All(actions.SelectMany(a => a.GetParameters()), p =>
            Assert.Null(p.GetCustomAttribute<FromServicesAttribute>()));
    }

    // --- Replies -----------------------------------------------------------------------------------------

    private static string ValidExecutiveReply(BenchmarkReportPackPreparation prep) => ExecutiveReply(prep);

    /// <summary>An Executive Summary reply citing a fact the sheet really has; each argument overrides one part.</summary>
    private static string ExecutiveReply(
        BenchmarkReportPackPreparation prep, string? headline = null, string? extraStrength = null, string? confidence = null)
    {
        string factKey = prep.Sheet.Facts.First(f => f.Available).Key;
        var strengths = new List<object>
        {
            new { text = "{{subject}} gave clear and well organized answers.", questions = Array.Empty<int>(), evidence = new[] { factKey } }
        };
        if (extraStrength != null)
        {
            strengths.Add(new { text = extraStrength, questions = Array.Empty<int>(), evidence = new[] { factKey } });
        }

        return JsonSerializer.Serialize(new
        {
            headline = headline ?? "{{subject}} gave accurate and readable answers across the benchmark.",
            sections = new Dictionary<string, string>
            {
                [BenchmarkReportSlots.Meaning] = "{{subject}} would serve players well as a game assistant.",
                [BenchmarkReportSlots.Confidence] = confidence ?? "The result rests on a small set of questions, so it should be read with care."
            },
            strengths,
            weaknesses = new[]
            {
                new { text = "{{subject}} sometimes left out details the graders expected.", questions = Array.Empty<int>(), evidence = new[] { factKey } }
            }
        });
    }

    // --- Harness -----------------------------------------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        public DbContextOptions<ApplicationDbContext> Options { get; private init; } = default!;
        public ApplicationDbContext Db { get; private init; } = default!;
        public BenchmarkRunExamTests.SeededSuite Seeded { get; private init; } = default!;
        public IConfiguration Configuration { get; private init; } = default!;
        public CryptoService Crypto { get; private init; } = default!;
        public BenchmarkReportPackJobManager Jobs { get; } = new();
        public WriterProvider Provider { get; } = new();

        /// <summary>Another family than the subject's (OpenAI): the recommended kind of writer.</summary>
        public SystemAiApiConfiguration Writer { get; private set; } = default!;

        public SystemAiApiConfiguration SameProvider { get; private set; } = default!;
        public SystemAiApiConfiguration SameModel { get; private set; } = default!;
        public SystemAiApiConfiguration Disabled { get; private set; } = default!;

        public static async Task<Harness> CreateAsync(int maxRunsPerHour = 100)
        {
            var options = BenchmarkRunExamTests.InMemoryOptions();
            var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AesEncryptionKey"] = Convert.ToBase64String(new byte[32]),
                ["Benchmark:Compliance:MaxRunsPerHour"] = maxRunsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Benchmark:Compliance:MaxRunsPerDay"] = "100"
            }).Build();

            var harness = new Harness
            {
                Options = options,
                Db = new ApplicationDbContext(options),
                Seeded = seeded,
                Configuration = configuration,
                Crypto = new CryptoService(configuration)
            };
            await harness.AddWritersAsync();
            return harness;
        }

        private async Task AddWritersAsync()
        {
            var (cipher, nonce, tag) = Crypto.Encrypt("test-api-key", "SYSTEM_API_KEY");
            SystemAiApiConfiguration Config(string provider, string modelId, string name, bool enabled = true) => new()
            {
                Provider = provider,
                ModelId = modelId,
                DisplayName = name,
                ModelRole = 4,
                IsEnabled = enabled,
                EncryptedApiKey = cipher,
                ApiKeyNonce = nonce,
                ApiKeyTag = tag
            };

            Writer = Config(WriterProvider.Name, "writer-1", "Writer One");
            SameProvider = Config("OpenAI", "gpt-5.6-sol", "GPT Sol");
            SameModel = Config("OpenAI", "gpt-5.6-luna", "GPT Luna");
            Disabled = Config("Anthropic", "claude-disabled", "Disabled Writer", enabled: false);
            Db.SystemAiApiConfigurations.AddRange(Writer, SameProvider, SameModel, Disabled);
            await Db.SaveChangesAsync();
        }

        public BenchmarkReportPackRequest Request(
            string? subjectKey = null,
            long? writerId = null,
            IEnumerable<BenchmarkReportAudience>? audiences = null,
            bool acknowledgeSameProvider = false) => new()
        {
            RunIds = new List<long> { Seeded.RunIds[0], Seeded.RunIds[1] },
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun,
            SubjectKey = subjectKey ?? $"run:{Seeded.RunIds[0]}",
            Audiences = (audiences ?? new[] { BenchmarkReportAudience.ExecutiveSummary }).ToList(),
            WriterModelConfigurationId = writerId ?? Writer.Id,
            AcknowledgeSameProvider = acknowledgeSameProvider
        };

        public Task<BenchmarkReportPackPreparation> PrepareAsync(bool standalone = false)
            => PrepareCoreAsync(standalone);

        private async Task<BenchmarkReportPackPreparation> PrepareCoreAsync(bool standalone)
        {
            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                Db, new BenchmarkModelComparisonService(Db), standalone ? StandaloneRequest() : Request(),
                BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, CancellationToken.None);
            Assert.True(prep != null, refusal);
            return prep!;
        }

        /// <summary>The first seeded run alone, as a run-completion job asks for it.</summary>
        public BenchmarkReportPackRequest StandaloneRequest(IEnumerable<BenchmarkReportAudience>? audiences = null) => new()
        {
            RunIds = new List<long> { Seeded.RunIds[0] },
            SubjectKey = $"run:{Seeded.RunIds[0]}",
            Audiences = (audiences ?? new[] { BenchmarkReportAudience.ExecutiveSummary }).ToList(),
            WriterModelConfigurationId = Writer.Id
        };

        /// <summary>Runs one run-completion job for the first seeded run through the service, over the fake writer.</summary>
        public async Task<BenchmarkReportPackJob> RunCompletionAsync(BenchmarkReportAudience audience)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var request = StandaloneRequest(new[] { audience });
            var job = new BenchmarkReportPackJob
            {
                SubjectKey = request.SubjectKey,
                SubjectLabel = "gpt-5.6-luna",
                SuiteName = "Isolation Suite",
                WriterConfigId = Writer.Id,
                WriterDisplayName = Writer.DisplayName,
                WriterSnapshotId = snapshot.Id,
                Request = request,
                StartedByUserId = UserId,
                Cts = new CancellationTokenSource(),
                Documents = { new BenchmarkReportPackDocumentProgress { Audience = audience } }
            };
            await Jobs.WaitForSlotAsync(job, CancellationToken.None);

            await Service().WriteRunCompletionDocumentsAsync(job, CancellationToken.None);
            return job;
        }

        public AdminBenchmarkReportPacksController Controller() => new(
            Db,
            Jobs,
            new BenchmarkComplianceGuard(Configuration, Db),
            new BenchmarkModelComparisonService(Db),
            new ModelPricingService(new ModelMetadataService(), Db),
            new EndpointPolicy(Configuration),
            scopeFactory: null!,
            Configuration);

        public BenchmarkReportPackJob StartRunningJob()
        {
            var job = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", WriterConfigId = Writer.Id, Cts = new CancellationTokenSource() };
            Assert.True(Jobs.TryStart(job, out _));
            return job;
        }

        /// <summary>Runs one job for the given audiences through the service, over the fake writer.</summary>
        public async Task<BenchmarkReportPackJob> RunAsync(BenchmarkReportAudience audience, CancellationToken ct = default)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var request = Request(audiences: new[] { audience });
            var job = new BenchmarkReportPackJob
            {
                SubjectKey = request.SubjectKey,
                SubjectLabel = "gpt-5.6-luna",
                SuiteName = "Isolation Suite",
                WriterConfigId = Writer.Id,
                WriterDisplayName = Writer.DisplayName,
                WriterSnapshotId = snapshot.Id,
                Request = request,
                StartedByUserId = UserId,
                Cts = new CancellationTokenSource(),
                Documents = { new BenchmarkReportPackDocumentProgress { Audience = audience } }
            };
            Assert.True(Jobs.TryStart(job, out _));

            await Service().RunAsync(job.Id, ct);
            return job;
        }

        private BenchmarkReportPackService Service()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => new ApplicationDbContext(Options));
            services.AddSingleton<IAiProvider>(Provider);
            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var handlers = new List<IToolHandler>();
            var bridge = new NoClientBridge();
            var runner = new AgentLoopRunner(
                new IAiProvider[] { Provider },
                new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
                new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, Configuration),
                new ReplyEchoHttpClientFactory(),
                Configuration,
                scopeFactory,
                new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, Configuration),
                new ModelMetadataService(),
                NullLogger<AgentLoopRunner>.Instance);

            return new BenchmarkReportPackService(
                Db,
                runner,
                new SystemAiConfigService(Db, NullLogger<SystemAiConfigService>.Instance),
                Crypto,
                new EndpointPolicy(Configuration),
                new ModelPricingService(new ModelMetadataService(), Db),
                new BenchmarkModelComparisonService(Db),
                Jobs,
                Configuration,
                NullLogger<BenchmarkReportPackService>.Instance);
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    /// <summary>
    /// A provider that answers each request with the next queued reply. The reply travels in the
    /// request body and <see cref="ReplyEchoHandler"/> sends it back; <see cref="ProviderError"/> makes
    /// the handler answer 400, which the agent loop fails at once instead of retrying with backoff.
    /// </summary>
    private sealed class WriterProvider : IAiProvider
    {
        public const string Name = "ReportWriterTest";
        public const string ProviderError = "__provider_error__";
        public const int PromptTokens = 5000;
        public const int OutputTokens = 800;

        public ConcurrentQueue<string> Replies { get; } = new();
        public int Calls;
        public ConcurrentQueue<int> MessageCountLog { get; } = new();
        public int[] MessageCounts => MessageCountLog.ToArray();

        public string ProviderName => Name;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public Dictionary<string, object> BuildChatRequestBody(
            string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel,
            ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null,
            string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null,
            string? promptCacheKey = null, bool cacheConversationTail = true)
        {
            Interlocked.Increment(ref Calls);
            MessageCountLog.Enqueue(messageHistory.Count);
            return new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["messages"] = messageHistory,
                ["reply"] = Replies.TryDequeue(out var reply) ? reply : string.Empty
            };
        }

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(
            HttpResponseMessage response, bool showDebugLog,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string reply = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new ChatEvent { Type = "chunk", Data = reply };
            yield return new ChatEvent
            {
                Type = "usage",
                UsageReport = new TokenUsageReport
                {
                    TotalPromptTokens = PromptTokens,
                    UncachedInputTokens = PromptTokens,
                    OutputTokens = OutputTokens
                }
            };
            yield return new ChatEvent { Type = "finish_reason", Data = "stop" };
        }

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null) { }
        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results) { }
        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://report-writer.test/stream";
        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://report-writer.test/title";
        public string? ParseTitleResponse(JsonElement root) => null;
    }

    /// <summary>Answers every request with the <c>reply</c> its body carries. Nothing leaves the process.</summary>
    private sealed class ReplyEchoHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            string reply = doc.RootElement.TryGetProperty("reply", out var value) ? value.GetString() ?? string.Empty : string.Empty;
            return reply == WriterProvider.ProviderError
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"boom\"}"), RequestMessage = request }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply), RequestMessage = request };
        }
    }

    private sealed class ReplyEchoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ReplyEchoHandler());
    }

    private sealed class NoClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }
}
