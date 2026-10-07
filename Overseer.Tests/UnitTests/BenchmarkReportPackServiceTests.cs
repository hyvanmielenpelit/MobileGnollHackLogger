using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
using Overseer.Tests.Helpers;
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

    /// <summary>The first member run of <see cref="Harness.SeedBatteryAsync"/>, clear of the seeded suite's runs.</summary>
    private const long BatteryFirstRunId = 9001;

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

        var child = Assert.Single(document.Runs, r => !r.IsPeer);
        Assert.Equal(h.Seeded.RunIds[0], child.RunId);
        Assert.Equal(16, child.SynthesisSha256.Length);
        var peer = Assert.Single(document.Runs, r => r.IsPeer);
        Assert.Equal(h.Seeded.RunIds[1], peer.RunId);
        Assert.Equal(16, peer.SynthesisSha256.Length);
        Assert.Equal(BenchmarkReportComparisonKey.From(new[] { h.Seeded.RunIds[0], h.Seeded.RunIds[1] }, Array.Empty<long>()), document.ComparisonKey);

        Assert.Equal(1, h.Provider.Calls);
        var usage = Assert.Single(await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportPackService.UsageRoleContext, usage.RoleContext);
        Assert.Equal(8, usage.RoleContext);
        Assert.Equal(h.Writer.Id, usage.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task TheJobDto_CarriesTheServerTime()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        var before = DateTime.UtcNow;
        var dto = job.ToDto();
        var after = DateTime.UtcNow;

        Assert.InRange(dto.ServerTimeUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, dto.ServerTimeUtc.Kind);
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
        Assert.Equal(BenchmarkReportPackRenderer.ReportFormatVersion, document.ReportFormatVersion);
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
    public async Task AHeadlineNamingTheClaimVerifier_GetsTheRepairTurn_AndIsKeptWithItsNote()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        const string headline = "{{subject}} answered well, though the claim verifier refuted one of its answers.";
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: headline));
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: headline));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        Assert.Equal(headline, writer.Headline);
        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson!);
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.VerifierInSummaryRule && !n.Dropped && n.Location == "headline");
        Assert.DoesNotContain(notes, n => n.Dropped);
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
        var only = Assert.Single(document.Runs);
        Assert.Equal(runId, only.RunId);
        Assert.False(only.IsPeer);
        Assert.Equal(BenchmarkReportComparisonKey.From(new[] { runId }, Array.Empty<long>()), document.ComparisonKey);

        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        Assert.Empty(sheet.Peers);
        Assert.Equal(BenchmarkReportFacts.StandaloneReason, sheet.Facts.Single(f => f.Key == "quality.rank").UnavailableReason);

        var usage = Assert.Single(await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportPackService.UsageRoleContext, usage.RoleContext);
    }

    [Fact]
    public async Task ABatteryCompletionJob_WritesAStandaloneBatteryDocument_StoredWithItsOrigin()
    {
        await using var h = await Harness.CreateAsync();
        long batteryRunId = await h.SeedBatteryAsync();
        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            h.Db, new BenchmarkModelComparisonService(h.Db),
            BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, new[] { BenchmarkReportAudience.ExecutiveSummary }, h.Writer.Id),
            BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, CancellationToken.None);
        Assert.True(prep != null, refusal);
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep!));

        var job = await h.RunBatteryCompletionAsync(batteryRunId, BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal($"battery:{batteryRunId}", job.Request.SubjectKey);
        Assert.Equal(new[] { batteryRunId }, job.Request.BatteryRunIds);
        Assert.Empty(job.Request.RunIds);
        Assert.Equal(BenchmarkReportPackPreparation.BatteryJobLabel(batteryRunId), job.SubjectLabel);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.Include(d => d.Runs).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentOrigin.BatteryCompletion, document.Origin);
        Assert.Equal($"battery:{batteryRunId}", document.SubjectKey);
        Assert.Equal(BenchmarkReportComparisonKey.From(Array.Empty<long>(), Array.Empty<long>(), new[] { batteryRunId }), document.ComparisonKey);
        Assert.Equal(new long[] { BatteryFirstRunId, BatteryFirstRunId + 1 }, document.Runs.Where(r => !r.IsPeer).Select(r => r.RunId).OrderBy(id => id));
        Assert.DoesNotContain(document.Runs, r => r.IsPeer);
        Assert.Equal("Core knowledge", document.SuiteName);
        Assert.Null(document.SuiteId);

        var request = BenchmarkReportJson.Deserialize<BenchmarkModelComparisonRequest>(document.ComparisonRequestJson);
        Assert.Equal(new[] { batteryRunId }, request.BatteryRunIds);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        Assert.Equal(BenchmarkBatteryReportFacts.SubjectKind, sheet.SubjectKind);
        Assert.NotNull(sheet.Battery);
        Assert.Empty(sheet.Peers);
        Assert.Equal(new[] { "S1-Q1", "S1-Q2", "S1-Q3", "S2-Q1", "S2-Q2" }, sheet.Questions.Select(q => q.Reference));
    }

    [Fact]
    public async Task StartAndPreview_RefuseBatteryResultsMixedWithRuns()
    {
        await using var h = await Harness.CreateAsync();
        long batteryRunId = await h.SeedBatteryAsync();
        var request = h.Request();
        request.BatteryRunIds.Add(batteryRunId);

        var start = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Contains(BenchmarkBatteryModelComparison.MixedSourcesError, JsonSerializer.Serialize(start.Value));

        var preview = Assert.IsType<BadRequestObjectResult>(await h.Controller().Preview(request, CancellationToken.None));
        Assert.Contains(BenchmarkBatteryModelComparison.MixedSourcesError, JsonSerializer.Serialize(preview.Value));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task PreviewAndStart_OfABatterySubjectWithNoPeer_AreRefused()
    {
        await using var h = await Harness.CreateAsync();
        long batteryRunId = await h.SeedBatteryAsync();
        var request = BenchmarkReportPackPreparation.BatteryRequest(
            batteryRunId, new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport }, h.Writer.Id);

        var ok = Assert.IsType<OkObjectResult>(await h.Controller().Preview(request, CancellationToken.None));
        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(ok.Value);

        Assert.Equal(BenchmarkReportPackPreparation.PeerlessReportRefusal, preview.Refusal);
        Assert.Equal($"battery:{batteryRunId}", preview.SubjectKey);
        Assert.Equal("Core knowledge", preview.SuiteName);
        Assert.Empty(preview.Peers);
        Assert.Empty(preview.Estimates);
        Assert.Empty(preview.WrittenDocuments);

        var start = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(BenchmarkReportPackPreparation.PeerlessReportRefusal, ErrorOf(start.Value));
        Assert.Empty(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, h.Provider.Calls);
    }

    /// <summary>The <c>error</c> of an anonymous <c>{ error }</c> body, unescaped.</summary>
    private static string? ErrorOf(object? body) => body?.GetType().GetProperty("error")?.GetValue(body) as string;

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
    public async Task Start_RefusesASubjectWithNoPeer_BeforeAnUnusableWriter()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.StandaloneRequest(Array.Empty<BenchmarkReportAudience>());
        request.WriterModelConfigurationId = h.Disabled.Id;

        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(BenchmarkReportPackPreparation.PeerlessReportRefusal, ErrorOf(bad.Value));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Start_RefusesADocumentAlreadyWrittenForTheComparisonAndSubject_With409_BeforeTheSpendCap()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var request = h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport });
        await h.StoreDocumentAsync(BenchmarkReportAudience.TechnicalReport, request);

        var conflict = Assert.IsType<ConflictObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        string? error = ErrorOf(conflict.Value);
        Assert.StartsWith("The Report for AI Researchers and Developers about ", error);
        Assert.EndsWith(" is already written for this comparison. Delete it in step 4 to write it again.", error);

        // Another audience of the same comparison goes on to the spend cap.
        var other = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(
            h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary }), CancellationToken.None));
        Assert.Equal(429, other.StatusCode);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Start_IgnoresDocumentsOfAnotherComparison_AnotherSubjectOrAnotherOrigin()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var request = h.Request();
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, comparisonKey: new string('f', 64));
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, subjectKey: $"run:{h.Seeded.RunIds[1]}");
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, origin: BenchmarkReportDocumentOrigin.RunCompletion);

        var result = Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Equal(429, result.StatusCode);
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

    // --- Comparison scope, several subjects and replacement ------------------------------------------

    private static string Reply(BenchmarkReportWriterOutput output) => JsonSerializer.Serialize(output);

    [Fact]
    public async Task AComparisonJob_StoresOneComparisonScopeDocument_OfItsNumberedComparisonAndCoveredSet()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.ComparisonRequest();
        var prep = await h.PrepareComparisonAsync(request);
        string reply = Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, prep.Sheet));
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunComparisonAsync(request);

        var comparison = Assert.Single(await h.Db.BenchmarkComparisons.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportComparisonKey.From(h.Seeded.RunIds, Array.Empty<long>()), comparison.ComparisonKey);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.Include(d => d.Runs).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportScope.Comparison, document.Scope);
        Assert.Equal(comparison.Id, document.ComparisonId);
        Assert.Equal(comparison.Id, job.ComparisonId);
        Assert.Equal("comparison:" + comparison.Id, document.SubjectKey);
        Assert.Equal(job.Documents[0].SubjectKey, document.SubjectKey);

        var keys = h.Seeded.RunIds.Select(id => "run:" + id).ToList();
        Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(keys), document.CoveredSetKey);
        Assert.Equal(keys, BenchmarkReportRenderService.CoveredEntryKeys(document.CoveredEntryKeysJson));
        Assert.Equal(BenchmarkReportPackRenderer.ComparisonReportFormatVersion, document.ReportFormatVersion);
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportScope.Comparison), document.WriterPromptSha256);
        Assert.StartsWith("Comparison #" + comparison.Id + " — ", document.Title);
        Assert.All(document.Runs, r => Assert.False(r.IsPeer));
        Assert.Equal(h.Seeded.RunIds.OrderBy(id => id), document.Runs.Select(r => r.RunId).OrderBy(id => id));

        var facts = BenchmarkReportRenderService.ReadFacts(document.FactsJson);
        Assert.True(facts.CoversAllEntries);
        Assert.Equal(3, facts.ComparisonEntryCount);
        Assert.Equal(3, facts.PeerLetters.Count);
    }

    [Fact]
    public async Task AComparisonJob_WritesItsTopicsOnce_AndGivesThemToItsLaterDocuments()
    {
        await using var h = await Harness.CreateAsync();
        var audiences = new[] { BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief };
        var request = h.ComparisonRequest(audiences: audiences);
        var prep = await h.PrepareComparisonAsync(request);
        var technical = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.TechnicalReport, prep.Sheet);
        var brief = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.InternalBrief, prep.Sheet);
        foreach (var topic in brief.QuestionTopics) topic.Topic = "A different wording";
        h.Provider.Replies.Enqueue(Reply(technical));
        h.Provider.Replies.Enqueue(Reply(brief));

        var job = await h.RunComparisonAsync(request);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var documents = await h.Db.BenchmarkReportDocuments.OrderBy(d => d.Audience).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, documents.Count);
        var topics = documents
            .Select(d => BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(d.WriterOutputJson).QuestionTopics.Select(t => (t.Question, t.Topic)).ToList())
            .ToList();
        Assert.Equal(technical.QuestionTopics.Select(t => (t.Question, t.Topic)), topics[0]);
        Assert.Equal(topics[0], topics[1]);
        Assert.Equal(2, h.Provider.MessageCounts.Length);
    }

    [Fact]
    public async Task ASubsetJob_IsKeyedByItsCoveredSet_AndIsUniqueApartFromTheWholeComparison()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var whole = h.ComparisonRequest();
        var wholePrep = await h.PrepareComparisonAsync(whole);
        h.Provider.Replies.Enqueue(Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, wholePrep.Sheet)));
        h.Provider.Replies.Enqueue(Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, wholePrep.Sheet)));
        await h.RunComparisonAsync(whole);

        var covered = h.Seeded.RunIds.Take(2).Select(id => "run:" + id).ToList();
        var subset = h.ComparisonRequest(covered: covered);

        // The whole comparison's Executive Summary is written; the subset's is not.
        var conflict = Assert.IsType<ConflictObjectResult>(await h.Controller().Start(h.ComparisonRequest(), CancellationToken.None));
        Assert.Contains("already written", ErrorOf(conflict.Value));
        Assert.Equal(429, Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(subset, CancellationToken.None)).StatusCode);

        var subsetPrep = await h.PrepareComparisonAsync(subset);
        h.Provider.Replies.Enqueue(Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, subsetPrep.Sheet)));
        h.Provider.Replies.Enqueue(Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, subsetPrep.Sheet)));
        await h.RunComparisonAsync(subset);

        var document = await h.Db.BenchmarkReportDocuments.OrderByDescending(d => d.Id).FirstAsync(TestContext.Current.CancellationToken);
        string coveredSetKey = BenchmarkReportComparisonKey.ForCoveredSet(covered);
        Assert.Equal(coveredSetKey, document.CoveredSetKey);
        Assert.Equal("comparison:" + document.ComparisonId + "/" + coveredSetKey[..16], document.SubjectKey);
        Assert.False(BenchmarkReportRenderService.ReadFacts(document.FactsJson).CoversAllEntries);

        Assert.IsType<ConflictObjectResult>(await h.Controller().Start(subset, CancellationToken.None));

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(h.ComparisonRequest(), CancellationToken.None)).Value);
        Assert.Equal(document.ComparisonId, preview.ComparisonId);
        Assert.True(preview.CoversAllEntries);
        Assert.Single(preview.WrittenDocuments);
        var other = Assert.Single(preview.OtherModelSets);
        Assert.Equal(coveredSetKey, other.CoveredSetKey);
        Assert.Equal(covered, other.CoveredModels.Select(m => m.EntryKey));
        Assert.Equal(document.Id, Assert.Single(other.Documents).DocumentId);
    }

    [Fact]
    public async Task AModelScopeJobOfSeveralSubjects_WritesEachInTurn_InOneNumberedComparison()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        var subjects = new[] { $"run:{h.Seeded.RunIds[0]}", $"run:{h.Seeded.RunIds[1]}" };

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken, subjects);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var documents = await h.Db.BenchmarkReportDocuments.OrderBy(d => d.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(subjects, documents.Select(d => d.SubjectKey));
        var comparison = Assert.Single(await h.Db.BenchmarkComparisons.ToListAsync(TestContext.Current.CancellationToken));
        Assert.All(documents, d =>
        {
            Assert.Equal(BenchmarkReportScope.Model, d.Scope);
            Assert.Equal(comparison.Id, d.ComparisonId);
            Assert.Equal(new[] { d.SubjectKey }, BenchmarkReportRenderService.CoveredEntryKeys(d.CoveredEntryKeysJson));
            Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(new[] { d.SubjectKey }), d.CoveredSetKey);
        });
        Assert.Equal(subjects, job.ToDto().Documents.Select(d => d.SubjectKey));
    }

    [Fact]
    public async Task ACompletionDocument_KeepsModelScope_WithNoComparison()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync(standalone: true);
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        await h.RunCompletionAsync(BenchmarkReportAudience.ExecutiveSummary);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportScope.Model, document.Scope);
        Assert.Null(document.ComparisonId);
        Assert.Null(document.CoveredSetKey);
        Assert.Null(document.CoveredEntryKeysJson);
        Assert.Empty(await h.Db.BenchmarkComparisons.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReplacingDocument_IsStoredFirst_ThenTheReplacedRowAndItsChartsGo()
    {
        using var charts = TestChartStores.InTempFolder();
        await using var h = await Harness.CreateAsync();
        long old = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, h.Request());
        await AttachChartAsync(h, charts.Store, old);
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken, replace: new[] { old }, charts: charts.Store);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(old, document.Id);
        Assert.Equal(job.Documents[0].DocumentId, document.Id);
        Assert.False(Directory.Exists(Path.Combine(charts.Root, old.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task AFailedWrite_ReplacesNothing()
    {
        using var charts = TestChartStores.InTempFolder();
        await using var h = await Harness.CreateAsync();
        long old = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, h.Request());
        await AttachChartAsync(h, charts.Store, old);
        h.Provider.Replies.Enqueue(WriterProvider.ProviderError);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken, replace: new[] { old }, charts: charts.Store);

        Assert.Equal(BenchmarkReportPackJobStatus.Failed, job.Status);
        Assert.Equal(old, Assert.Single(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken)).Id);
        Assert.True(Directory.Exists(Path.Combine(charts.Root, old.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static async Task AttachChartAsync(Harness h, BenchmarkReportChartStore store, long documentId)
    {
        var render = new BenchmarkReportRenderService(h.Db, store, NullLogger<BenchmarkReportRenderService>.Instance);
        var (summary, _, refusal) = await render.SetChartsAsync(documentId, new PutReportDocumentChartsRequest
        {
            Charts = new List<ReportDocumentChartUpload>
            {
                new()
                {
                    FigureKey = BenchmarkReportChartPlacement.QualityKey,
                    Naming = BenchmarkReportChartStore.Named,
                    Title = "Intelligence",
                    Caption = "Caption.",
                    AltText = "Alt text.",
                    SettingsHash = new string('a', 64),
                    PngBase64 = TestPngs.MakeBase64(640, 360, 0)
                }
            }
        }, CancellationToken.None);
        Assert.True(summary != null, refusal);
    }

    [Fact]
    public async Task Start_RefusesAWrittenDocument_UnlessTheRequestReplacesIt()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var request = h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary });
        long old = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request);

        Assert.IsType<ConflictObjectResult>(await h.Controller().Start(request, CancellationToken.None));

        request.ReplaceDocumentIds = new List<long> { old };
        Assert.Equal(429, Assert.IsAssignableFrom<ObjectResult>(await h.Controller().Start(request, CancellationToken.None)).StatusCode);

        request.ReplaceDocumentIds = new List<long> { old + 1000 };
        var bad = Assert.IsType<BadRequestObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        Assert.Contains("does not exist", ErrorOf(bad.Value));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task StartComparison_RefusesInOrder_TheCoveredSetAndTheWriter()
    {
        await using var h = await Harness.CreateAsync();
        var controller = h.Controller();

        var unknown = Assert.IsType<BadRequestObjectResult>(await controller.Start(h.ComparisonRequest(covered: new[] { "run:999999", $"run:{h.Seeded.RunIds[0]}" }), CancellationToken.None));
        Assert.Contains("is not an entry of this comparison", ErrorOf(unknown.Value));

        var one = Assert.IsType<ConflictObjectResult>(await controller.Start(h.ComparisonRequest(covered: new[] { $"run:{h.Seeded.RunIds[0]}" }), CancellationToken.None));
        Assert.Equal(BenchmarkComparisonReportFacts.TooFewRefusal, ErrorOf(one.Value));

        var itself = Assert.IsType<BadRequestObjectResult>(await controller.Start(h.ComparisonRequest(writerId: h.SameConfiguration.Id), CancellationToken.None));
        Assert.Contains("a model this document covers", ErrorOf(itself.Value));

        // The same model at another thinking level only shares the provider.
        var warning = Assert.IsAssignableFrom<ObjectResult>(await controller.Start(h.ComparisonRequest(writerId: h.SameModel.Id), CancellationToken.None));
        Assert.Equal(409, warning.StatusCode);
        var dto = Assert.IsType<SameProviderWarningDto>(warning.Value);
        Assert.Equal("OpenAI", dto.Provider);
        Assert.Contains("models this document covers", dto.Message);

        Assert.Empty(await h.Db.BenchmarkComparisons.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task PreviewComparison_LettersTheCoveredModels_AndEstimatesWithTheComparisonOutputSizes()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.ComparisonRequest(audiences: new[]
        {
            BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief
        });

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(request, CancellationToken.None)).Value);

        Assert.Null(preview.Refusal);
        Assert.Equal(BenchmarkReportScope.Comparison, preview.Scope);
        Assert.Null(preview.ComparisonId);
        Assert.Equal(3, preview.ComparisonEntryCount);
        Assert.True(preview.CoversAllEntries);
        Assert.Equal(new[] { "A", "B", "C" }, preview.CoveredModels.Select(m => m.Letter));
        Assert.Equal(new[] { 3000, 9500, 9000 }, preview.Estimates.Select(e => e.EstimatedOutputTokens));
        Assert.All(preview.Estimates, e => Assert.Equal(preview.SubjectKey, e.SubjectKey));
        Assert.Equal(3, preview.SubjectDocuments.Count);
        Assert.Empty(preview.OtherModelSets);
        Assert.Null(preview.WriterContextWindowTokens);
        Assert.Equal(0, h.Provider.Calls);
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
        Assert.Empty(preview.WrittenDocuments);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Preview_RefusesASubjectWithNoPeer_WithNoEstimate()
    {
        await using var h = await Harness.CreateAsync();

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(h.StandaloneRequest(), CancellationToken.None)).Value);

        Assert.Equal(BenchmarkReportPackPreparation.PeerlessReportRefusal, preview.Refusal);
        Assert.Equal($"run:{h.Seeded.RunIds[0]}", preview.SubjectKey);
        Assert.Empty(preview.Peers);
        Assert.Empty(preview.Estimates);
        Assert.Null(preview.EstimatedTotalCostUsd);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Preview_ListsTheDocumentsWrittenForTheComparisonAndSubject_TheNewestPerAudience()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request();
        long older = await h.StoreDocumentAsync(BenchmarkReportAudience.TechnicalReport, request, createdAtUtc: new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        long newer = await h.StoreDocumentAsync(BenchmarkReportAudience.TechnicalReport, request, createdAtUtc: new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));
        long brief = await h.StoreDocumentAsync(BenchmarkReportAudience.InternalBrief, request, createdAtUtc: new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, comparisonKey: new string('f', 64));
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, origin: BenchmarkReportDocumentOrigin.RunCompletion);

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(request, CancellationToken.None)).Value);

        Assert.Null(preview.Refusal);
        Assert.Equal(
            new[] { (BenchmarkReportAudience.TechnicalReport, newer), (BenchmarkReportAudience.InternalBrief, brief) },
            preview.WrittenDocuments.Select(d => (d.Audience, d.DocumentId)));
        Assert.NotEqual(older, newer);
        Assert.Equal(new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc), preview.WrittenDocuments[0].CreatedAtUtc);
        Assert.Equal("Claude Opus 5.5", preview.WrittenDocuments[0].WriterDisplayName);
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
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));

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
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));

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
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));

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
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));

        var provider = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "detailed", "anonymized", "letter", CancellationToken.None));
        var full = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "Full", "Named", null, CancellationToken.None));

        Assert.Equal("application/pdf", provider.ContentType);
        Assert.Equal(
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
            provider.FileDownloadName);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(provider.FileContents, 0, 5));
        Assert.Equal(
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_full_named_INTERNAL.pdf",
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
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var pdf = Assert.IsType<FileContentResult>(
            await controller.RenderPdf(document.Id, "detailed", "anonymized", null, CancellationToken.None, inline: true));

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.True(string.IsNullOrEmpty(pdf.FileDownloadName));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5));
        Assert.Equal(
            "inline; filename*=UTF-8''run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
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
            new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));

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
        var render = new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);
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

    [Fact]
    public async Task List_ByRun_MatchesTheSubjectsRunsOnly_NeverAPeersRun()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);
        var render = new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);

        Assert.Single(await render.ListAsync(null, h.Seeded.RunIds[0], null, CancellationToken.None));
        Assert.Empty(await render.ListAsync(null, h.Seeded.RunIds[1], null, CancellationToken.None));
    }

    [Fact]
    public async Task List_FlagsADocumentWhosePeerWasRescoredOrDeleted_AndDescribesItsComparison()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);
        var render = new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);
        long subjectRunId = h.Seeded.RunIds[0];
        long peerRunId = h.Seeded.RunIds[1];

        var fresh = Assert.Single(await render.ListAsync(null, subjectRunId, null, CancellationToken.None));
        Assert.False(fresh.RunChangedSinceGeneration);
        Assert.False(fresh.PeersChangedSinceGeneration);
        Assert.Empty(fresh.MissingRunIds);
        Assert.Equal(BenchmarkReportComparisonKey.From(new[] { subjectRunId, peerRunId }, Array.Empty<long>()), fresh.ComparisonKey);
        Assert.Equal(2, fresh.ComparisonEntryCount);
        Assert.Equal(1, fresh.PeerCount);
        Assert.Equal("AsRun", fresh.PricingBasis);

        var peer = await h.Db.BenchmarkRuns.SingleAsync(r => r.Id == peerRunId, TestContext.Current.CancellationToken);
        peer.QualityIndex = (peer.QualityIndex ?? 0) + 1;
        await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var rescored = Assert.Single(await render.ListAsync(null, subjectRunId, null, CancellationToken.None));
        Assert.True(rescored.PeersChangedSinceGeneration);
        Assert.False(rescored.RunChangedSinceGeneration);

        h.Db.BenchmarkRuns.Remove(peer);
        await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var orphan = Assert.Single(await render.ListAsync(null, subjectRunId, null, CancellationToken.None));
        Assert.True(orphan.PeersChangedSinceGeneration);
        Assert.False(orphan.RunChangedSinceGeneration);
        Assert.Empty(orphan.MissingRunIds);

        var detail = await render.GetAsync(orphan.Id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.True(detail!.PeersChangedSinceGeneration);
        Assert.False(detail.RunChangedSinceGeneration);
        Assert.Equal(1, detail.PeerCount);
    }

    [Fact]
    public async Task ListController_FiltersByComparisonEntryKeysAndOrigin_AndRefusesMalformedValues()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);
        h.Provider.Replies.Enqueue(ValidExecutiveReply(await h.PrepareAsync(standalone: true)));
        await h.RunCompletionAsync(BenchmarkReportAudience.ExecutiveSummary);

        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));
        async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(string? comparison = null, string? origin = null)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.List(null, null, null, CancellationToken.None, comparison, origin));
            return Assert.IsType<List<BenchmarkReportDocumentListItemDto>>(ok.Value);
        }

        long a = h.Seeded.RunIds[0];
        long b = h.Seeded.RunIds[1];
        Assert.Equal(2, (await ListAsync()).Count);

        // Order and spacing of the entry keys do not matter; the pricing basis is not part of the identity.
        var pack = Assert.Single(await ListAsync(comparison: $"run:{b}, run:{a}"));
        Assert.Equal(BenchmarkReportDocumentOrigin.ReportPack, pack.Origin);
        var single = Assert.Single(await ListAsync(comparison: $"run:{a}"));
        Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, single.Origin);
        Assert.Empty(await ListAsync(comparison: $"run:{a},group:{b}"));

        Assert.Equal(BenchmarkReportDocumentOrigin.ReportPack, Assert.Single(await ListAsync(origin: "reportPack")).Origin);
        Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, Assert.Single(await ListAsync(origin: "runCompletion")).Origin);
        Assert.Empty(await ListAsync(comparison: $"run:{a}", origin: "reportPack"));

        foreach (var malformed in new[] { "", "run:", "run:x", $"run:{a},suite:1", "run:-1" })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(await controller.List(null, null, null, CancellationToken.None, malformed, null));
            Assert.Contains("comma-separated list of run:", JsonSerializer.Serialize(bad.Value));
        }
        Assert.IsType<BadRequestObjectResult>(await controller.List(null, null, null, CancellationToken.None, null, "both"));
    }

    [Fact]
    public async Task ListController_FiltersBySubjectKey_AndByTheBatteryCompletionOrigin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());

        var runDocument = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
        runDocument.Id = 0;
        runDocument.Runs = new List<BenchmarkReportDocumentRun>();
        var groupDocument = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        groupDocument.Id = 0;
        groupDocument.SubjectKey = "group:4";
        groupDocument.Runs = new List<BenchmarkReportDocumentRun>();
        var battery7 = BatteryReportHarness.BatteryDocument(7, BenchmarkReportAudience.ExecutiveSummary);
        battery7.Runs = new List<BenchmarkReportDocumentRun>();
        var battery8 = BatteryReportHarness.BatteryDocument(8, BenchmarkReportAudience.TechnicalReport);
        battery8.Runs = new List<BenchmarkReportDocumentRun>();
        db.BenchmarkReportDocuments.AddRange(runDocument, groupDocument, battery7, battery8);
        await db.SaveChangesAsync(ct);

        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));
        async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(string? comparison = null, string? origin = null, string? subject = null)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.List(null, null, null, CancellationToken.None, comparison, origin, subject));
            return Assert.IsType<List<BenchmarkReportDocumentListItemDto>>(ok.Value);
        }

        Assert.Equal(4, (await ListAsync()).Count);
        Assert.Equal(runDocument.Id, Assert.Single(await ListAsync(subject: "run:12")).Id);
        Assert.Equal(groupDocument.Id, Assert.Single(await ListAsync(subject: "group:4")).Id);
        Assert.Equal(battery7.Id, Assert.Single(await ListAsync(subject: "battery:7")).Id);
        Assert.Empty(await ListAsync(subject: "battery:9"));

        var batteryDocuments = await ListAsync(origin: "batteryCompletion");
        Assert.Equal(new[] { battery7.Id, battery8.Id }, batteryDocuments.Select(d => d.Id).OrderBy(id => id));
        Assert.All(batteryDocuments, d => Assert.Equal(BenchmarkReportDocumentOrigin.BatteryCompletion, d.Origin));
        Assert.Equal(battery8.Id, Assert.Single(await ListAsync(origin: "BatteryCompletion", subject: "battery:8")).Id);
        Assert.Empty(await ListAsync(origin: "runCompletion", subject: "battery:8"));

        var byComparison = Assert.Single(await ListAsync(comparison: "battery:7"));
        Assert.Equal(battery7.Id, byComparison.Id);
        Assert.Equal(1, byComparison.ComparisonEntryCount);
        Assert.Empty(await ListAsync(comparison: "battery:7,battery:8"));

        foreach (var malformed in new[] { "", "battery:", "battery:x", "battery:-1", "battery:0", " battery:7", "run:1,run:2", "suite:1" })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(await controller.List(null, null, null, CancellationToken.None, null, null, malformed));
            Assert.Contains("subject must be one run:", JsonSerializer.Serialize(bad.Value));
        }
        var badOrigin = Assert.IsType<BadRequestObjectResult>(await controller.List(null, null, null, CancellationToken.None, null, "battery"));
        Assert.Contains("batteryCompletion", JsonSerializer.Serialize(badOrigin.Value));
    }

    [Fact]
    public async Task Backfill_KeysEveryReadableRow_LeavesAnUnreadableOneEmpty_AndIsIdempotent()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var ct = TestContext.Current.CancellationToken;

        BenchmarkReportDocument Stored(string comparisonRequestJson)
        {
            var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
            document.Id = 0;
            document.ComparisonKey = null;
            document.ComparisonRequestJson = comparisonRequestJson;
            document.Runs = new List<BenchmarkReportDocumentRun>();
            db.BenchmarkReportDocuments.Add(document);
            return document;
        }
        var groups = Stored("{\"runIds\":[7,3],\"groupIds\":[4],\"pricingBasis\":1}");
        var runs = Stored("{\"runIds\":[3,7,3],\"groupIds\":[],\"pricingBasis\":0}");
        var unreadable = Stored("not json");
        var keyed = Stored("{\"runIds\":[1],\"groupIds\":[]}");
        keyed.ComparisonKey = "already-set";
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await BenchmarkReportDocumentBackfill.BackfillComparisonKeysAsync(db, NullLogger.Instance, ct));
        Assert.Equal(0, await BenchmarkReportDocumentBackfill.BackfillComparisonKeysAsync(db, NullLogger.Instance, ct));

        await using var verify = new ApplicationDbContext(options);
        var byId = await verify.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes().ToDictionaryAsync(d => d.Id, ct);
        Assert.Equal(BenchmarkReportComparisonKey.From(new long[] { 3, 7 }, new long[] { 4 }), byId[groups.Id].ComparisonKey);
        Assert.Equal(BenchmarkReportComparisonKey.From(new long[] { 3, 7 }, Array.Empty<long>()), byId[runs.Id].ComparisonKey);
        Assert.Null(byId[unreadable.Id].ComparisonKey);
        Assert.Equal("already-set", byId[keyed.Id].ComparisonKey);
    }

    // --- The download path cannot reach a model (D13) -------------------------------------------------

    [Fact]
    public void TheRenderService_TakesOnlyTheDbContextTheChartStoreAndALogger()
    {
        var constructor = Assert.Single(typeof(BenchmarkReportRenderService).GetConstructors());
        Assert.Equal(
            new[] { typeof(ApplicationDbContext), typeof(BenchmarkReportChartStore), typeof(ILogger<BenchmarkReportRenderService>) },
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

        var sections = new Dictionary<string, string>
        {
            [BenchmarkReportSlots.Meaning] = "{{subject}} would serve players well as a game assistant.",
            [BenchmarkReportSlots.Confidence] = confidence ?? "The result rests on a small set of questions, so it should be read with care."
        };
        if (prep.Sheet.Peers.Count > 0)
        {
            sections[BenchmarkReportSlots.Comparison] = "{{subject}} sits among its peers on quality, and the order between them is not established.";
        }

        return JsonSerializer.Serialize(new
        {
            headline = headline ?? "{{subject}} gave accurate and readable answers across the benchmark.",
            sections,
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

        /// <summary>The seeded runs' own configuration: provider, model id and thinking level.</summary>
        public SystemAiApiConfiguration SameConfiguration { get; private set; } = default!;

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
            SameConfiguration = Config("OpenAI", "gpt-5.6-luna", "GPT Luna High");
            SameConfiguration.ThinkingLevel = "high";
            Db.SystemAiApiConfigurations.AddRange(Writer, SameProvider, SameModel, Disabled, SameConfiguration);
            await Db.SaveChangesAsync();
        }

        /// <summary>A comparison-scope request over the three seeded runs, covering <paramref name="covered"/> or every run.</summary>
        public BenchmarkReportPackRequest ComparisonRequest(
            IEnumerable<string>? covered = null,
            IEnumerable<BenchmarkReportAudience>? audiences = null,
            long? writerId = null,
            bool acknowledgeSameProvider = false,
            IEnumerable<long>? replace = null) => new()
        {
            RunIds = Seeded.RunIds.ToList(),
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun,
            Scope = BenchmarkReportScope.Comparison,
            CoveredEntryKeys = covered?.ToList(),
            Audiences = (audiences ?? new[] { BenchmarkReportAudience.ExecutiveSummary }).ToList(),
            WriterModelConfigurationId = writerId ?? Writer.Id,
            AcknowledgeSameProvider = acknowledgeSameProvider,
            ReplaceDocumentIds = replace?.ToList()
        };

        public async Task<BenchmarkReportPackPreparation> PrepareComparisonAsync(BenchmarkReportPackRequest request)
        {
            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareComparisonAsync(
                Db, new BenchmarkModelComparisonService(Db), request, BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, null, CancellationToken.None);
            Assert.True(prep != null, refusal);
            return prep!;
        }

        /// <summary>Runs one comparison-scope job for the request through the service, over the fake writer.</summary>
        public async Task<BenchmarkReportPackJob> RunComparisonAsync(BenchmarkReportPackRequest request, BenchmarkReportChartStore? charts = null)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var job = new BenchmarkReportPackJob
            {
                Scope = BenchmarkReportScope.Comparison,
                SubjectLabel = "Comparison",
                SuiteName = "Isolation Suite",
                WriterConfigId = Writer.Id,
                WriterDisplayName = Writer.DisplayName,
                WriterSnapshotId = snapshot.Id,
                Request = request,
                StartedByUserId = UserId,
                Cts = new CancellationTokenSource(),
                Documents = request.Audiences.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
            };
            Assert.True(Jobs.TryStart(job, out _));

            await Service(charts).RunAsync(job.Id, CancellationToken.None);
            return job;
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

        /// <summary>One round of both suites of <see cref="BenchmarkBatteryTestData.Definition"/>, analysed; returns the battery run id.</summary>
        public async Task<long> SeedBatteryAsync()
        {
            long id = await BenchmarkBatteryTestData.SeedAsync(
                Db,
                BenchmarkBatteryTestData.Definition(),
                (BenchmarkBatteryTestData.SuiteARun(BatteryFirstRunId), 0, 1),
                (BenchmarkBatteryTestData.SuiteBRun(BatteryFirstRunId + 1), 1, 1));
            var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(Db).AnalyseAsync(id, null, null, CancellationToken.None);
            Assert.True(analysis != null, error);
            return id;
        }

        /// <summary>Runs one battery-completion job for the battery run through the service, over the fake writer.</summary>
        public async Task<BenchmarkReportPackJob> RunBatteryCompletionAsync(long batteryRunId, BenchmarkReportAudience audience)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var job = new BenchmarkReportPackJob
            {
                WriterConfigId = Writer.Id,
                WriterDisplayName = Writer.DisplayName,
                WriterSnapshotId = snapshot.Id,
                StartedByUserId = UserId,
                Cts = new CancellationTokenSource(),
                Documents = { new BenchmarkReportPackDocumentProgress { Audience = audience } }
            };
            await Jobs.WaitForSlotAsync(job, CancellationToken.None);

            await Service().WriteBatteryCompletionDocumentsAsync(batteryRunId, job, CancellationToken.None);
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

        /// <summary>
        /// Stores a fixture document of <paramref name="audience"/> about the request's subject, keyed by
        /// the request's comparison, a Report Pack document unless another origin is given; returns its id.
        /// </summary>
        public async Task<long> StoreDocumentAsync(
            BenchmarkReportAudience audience,
            BenchmarkReportPackRequest request,
            string? comparisonKey = null,
            string? subjectKey = null,
            BenchmarkReportDocumentOrigin origin = BenchmarkReportDocumentOrigin.ReportPack,
            DateTime? createdAtUtc = null)
        {
            var document = BenchmarkReportPackFixture.Document(audience);
            document.Id = 0;
            document.Origin = origin;
            document.SubjectKey = subjectKey ?? request.SubjectKey;
            document.ComparisonKey = comparisonKey ?? BenchmarkReportComparisonKey.From(request.RunIds, request.GroupIds, request.BatteryRunIds);
            document.CreatedAtUtc = createdAtUtc ?? BenchmarkReportPackFixture.CreatedAt;
            document.Runs = new List<BenchmarkReportDocumentRun>();
            Db.BenchmarkReportDocuments.Add(document);
            await Db.SaveChangesAsync();
            return document.Id;
        }

        public BenchmarkReportPackJob StartRunningJob()
        {
            var job = new BenchmarkReportPackJob { SubjectLabel = "Other", SuiteName = "Isolation Suite", WriterConfigId = Writer.Id, Cts = new CancellationTokenSource() };
            Assert.True(Jobs.TryStart(job, out _));
            return job;
        }

        /// <summary>
        /// Runs one job for the given audience through the service, over the fake writer: about the first
        /// seeded run, or each of <paramref name="subjects"/> in turn, replacing <paramref name="replace"/>.
        /// </summary>
        public async Task<BenchmarkReportPackJob> RunAsync(
            BenchmarkReportAudience audience,
            CancellationToken ct = default,
            IReadOnlyList<string>? subjects = null,
            IEnumerable<long>? replace = null,
            BenchmarkReportChartStore? charts = null)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var request = Request(audiences: new[] { audience });
            request.SubjectKeys = subjects?.ToList();
            request.ReplaceDocumentIds = replace?.ToList();
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
                Documents = subjects == null
                    ? new List<BenchmarkReportPackDocumentProgress> { new() { Audience = audience } }
                    : subjects.Select(s => new BenchmarkReportPackDocumentProgress { Audience = audience, SubjectKey = s, SubjectLabel = s }).ToList()
            };
            Assert.True(Jobs.TryStart(job, out _));

            await Service(charts).RunAsync(job.Id, ct);
            return job;
        }

        private BenchmarkReportPackService Service(BenchmarkReportChartStore? charts = null)
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
                NullLogger<BenchmarkReportPackService>.Instance,
                charts);
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    /// <summary>
    /// A provider that answers each request with the next queued reply. The reply travels in the
    /// request body and <see cref="ReplyEchoHandler"/> sends it back; <see cref="ProviderError"/> makes
    /// the handler answer 400, which the agent loop fails at once instead of retrying with backoff.
    /// Each request's message history is kept as JSON in <see cref="Requests"/>.
    /// </summary>
    internal sealed class WriterProvider : IAiProvider
    {
        public const string Name = "ReportWriterTest";
        public const string ProviderError = "__provider_error__";
        public const int PromptTokens = 5000;
        public const int OutputTokens = 800;

        public ConcurrentQueue<string> Replies { get; } = new();
        public int Calls;
        public ConcurrentQueue<int> MessageCountLog { get; } = new();
        public int[] MessageCounts => MessageCountLog.ToArray();
        public ConcurrentQueue<string> Requests { get; } = new();

        public string ProviderName => Name;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public Dictionary<string, object> BuildChatRequestBody(
            string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel,
            ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null,
            string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null,
            string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            Interlocked.Increment(ref Calls);
            MessageCountLog.Enqueue(messageHistory.Count);
            try
            {
                Requests.Enqueue(JsonSerializer.Serialize(messageHistory));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or JsonException)
            {
                Requests.Enqueue(string.Empty);
            }
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
    internal sealed class ReplyEchoHandler : HttpMessageHandler
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

    internal sealed class ReplyEchoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ReplyEchoHandler());
    }

    internal sealed class NoClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }
}
