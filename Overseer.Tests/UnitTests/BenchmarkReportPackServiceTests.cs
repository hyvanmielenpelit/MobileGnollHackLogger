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

    /// <summary>
    /// A warning rule's issue still there after the repair turn: the text is kept, its note is kept
    /// undropped, and the document completes with warnings.
    /// </summary>
    [Theory]
    [InlineData("confidence", "The interval is narrow, so the result should be read with care.", BenchmarkReportPackValidator.IntervalWidthRule, null)]
    [InlineData("headline", "{{subject}} answered well, though the claim verifier refuted one of its answers.", BenchmarkReportPackValidator.VerifierInSummaryRule, "headline")]
    [InlineData("strength", "{{subject}} showed sound judgement on item lore.", BenchmarkReportPackValidator.UsSpellingRule, "strengths[1]")]
    public async Task AWarningStillThereAfterRepair_IsKept_AndTheDocumentCompletesWithWarnings(
        string part, string text, int rule, string? location)
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = part switch
        {
            "confidence" => ExecutiveReply(prep, confidence: text),
            "headline" => ExecutiveReply(prep, headline: text),
            _ => ExecutiveReply(prep, extraStrength: text)
        };
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);

        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        switch (part)
        {
            case "confidence":
                Assert.Equal(text, writer.Sections[BenchmarkReportSlots.Confidence]);
                break;
            case "headline":
                Assert.Equal(text, writer.Headline);
                break;
            default:
                Assert.Contains(writer.Strengths, s => s.Text == text);
                break;
        }

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson!);
        Assert.Contains(notes, n => n.Rule == rule && !n.Dropped && (location == null || n.Location == location));
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
            batteryRunId, new[] { BenchmarkReportAudience.ExecutiveSummary }, h.Writer.Id);

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

    // --- Battery comparisons: judge-family diagnostics and critical-error resolutions ----------------------

    /// <summary>The first member run of the second battery the comparison tests seed, clear of the first's.</summary>
    private const long SecondBatteryFirstRunId = 9101;

    /// <summary>A comparison-scope request over two battery results.</summary>
    private static BenchmarkReportPackRequest BatteryComparisonRequest(Harness h, params long[] batteryRunIds) => new()
    {
        BatteryRunIds = batteryRunIds.ToList(),
        PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun,
        Scope = BenchmarkReportScope.Comparison,
        Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary },
        WriterModelConfigurationId = h.Writer.Id
    };

    /// <summary>
    /// Two analysed panel batteries graded by one panel and reference reader: an OpenAI model and an Anthropic
    /// model scoring 10 points higher on every question. The reference reader leaves the Anthropic model's first
    /// suite A answer ungraded, so the run-level diagnostics withhold that model's reader index while the battery's
    /// reader composite skips the missing score.
    /// </summary>
    private static async Task<(long Luna, long Opus)> SeedPanelBatteriesAsync(Harness h)
    {
        long luna = await h.SeedBatteryAsync(panel: true);
        long opus = await h.SeedBatteryAsync(SecondBatteryFirstRunId, "claude-opus-5", "Anthropic", shift: 10, panel: true, adjust: run =>
        {
            if (run.BenchmarkSuiteId != BenchmarkBatteryTestData.SuiteA) return;
            var first = run.Answers.Single(a => a.OrderIndex == 1);
            first.SecondOpinionQualityScore = null;
            first.SecondOpinionTrigger = null;
        });
        return (luna, opus);
    }

    [Fact]
    public async Task ABatteryPanelComparison_CarriesTheGraderFamilyGaps_IntoTheRootCauseAnalysis()
    {
        await using var h = await Harness.CreateAsync();
        var (luna, opus) = await SeedPanelBatteriesAsync(h);

        var prep = await h.PrepareComparisonAsync(BatteryComparisonRequest(h, luna, opus));

        // The comparison itself carries no diagnostics; the report path computes them from the member runs.
        Assert.Equal(BenchmarkModelComparisonSubjectKinds.Batteries, prep.Comparison.SubjectKind);
        Assert.Null(prep.Comparison.PanelDiagnostics);

        var rca = prep.Sheet.RootCause;
        Assert.NotNull(rca);
        var gap = Assert.Single(rca!.FamilyGaps ?? new List<Overseer.Services.Benchmarking.RootCause.RootCauseFamilyGap>());
        Assert.Equal(("Anthropic", "OpenAI"), (gap.Provider1, gap.Provider2));
        Assert.False(gap.IsMemberProviderPair);

        // The two batteries' five questions pair on question id and item revision across both suites.
        Assert.Equal(5, gap.PairedQuestionCount);
        Assert.False(gap.InsufficientData);
        Assert.Equal(10.0, gap.GapA.Value!.Value, 9);
        Assert.Equal(10.0, gap.GapB.Value!.Value, 9);
        Assert.Equal(10.0, gap.GapPanel.Value!.Value, 9);
    }

    [Fact]
    public async Task ABatteryPanelComparison_RulesOutHQ4_OnTheReaderComposite_WhenBothMembersAndTheReaderOrderThePairAlike()
    {
        await using var h = await Harness.CreateAsync();
        var (luna, opus) = await SeedPanelBatteriesAsync(h);

        var prep = await h.PrepareComparisonAsync(BatteryComparisonRequest(h, luna, opus));

        // Both batteries carry their member-alone and reader composites on the Overall Index's scale.
        string opusLetter = prep.Sheet.Models!.Single(m => m.EntryKey == BenchmarkBatteryModelComparison.KeyOf(opus)).Letter;
        Assert.Contains(prep.Sheet.Facts, f => f.Key == $"model.{opusLetter}.panel.referenceReaderIndex" && f.Available);

        // Member B shares the Anthropic model's provider, so HQ4 fires; the members order the pair alike, so no
        // pair is judge-dependent; and the reader composite, which the run-level diagnostics would withhold for the
        // Anthropic model, orders it alike too.
        var rca = prep.Sheet.RootCause!;
        var graders = rca.Hypotheses.Where(x => x.Id == "HQ4").ToList();
        Assert.NotEmpty(graders);
        Assert.All(graders, x =>
        {
            Assert.Equal(Overseer.Services.Benchmarking.RootCause.RootCauseGrades.NotSupported, x.Grade);
            Assert.Contains("Both panel members and the reference reader order the two alike.", x.Against);
        });
        Assert.DoesNotContain(prep.Sheet.NotableFindings ?? new List<BenchmarkReportNotableFinding>(), f => f.Kind == BenchmarkNotableFindings.JudgeDependentOrder);
    }

    [Fact]
    public async Task ABatteryComparison_ListsEachCellsCriticalErrorResolutions_IncludingAnAnswerWithNoExcerpt()
    {
        await using var h = await Harness.CreateAsync();
        const long overturnedRunId = BatteryFirstRunId + 2;
        void ScoredUnderMethod13(BenchmarkRun run)
        {
            run.ScoringMethodVersion = BenchmarkCriticalErrorResolver.FirstScoringMethod;
            foreach (var answer in run.Answers) answer.CriticalErrorResolution = BenchmarkCriticalErrorResolution.None;
            if (run.Id == overturnedRunId)
            {
                run.Answers.Single(a => a.OrderIndex == 1).CriticalErrorResolution = BenchmarkCriticalErrorResolution.OverturnedByVerifier;
            }
        }

        // Two rounds each: a question's detail is its median round, the lower one at two rounds, so the overturned
        // answer of round 2 (4 points higher) is never excerpted.
        long luna = await h.SeedBatteryAsync(rounds: 2, adjust: ScoredUnderMethod13);
        long opus = await h.SeedBatteryAsync(SecondBatteryFirstRunId, "claude-opus-5", shift: 10, rounds: 2, adjust: ScoredUnderMethod13);

        var prep = await h.PrepareComparisonAsync(BatteryComparisonRequest(h, luna, opus));

        string lunaLetter = prep.Sheet.Models!.Single(m => m.EntryKey == BenchmarkBatteryModelComparison.KeyOf(luna)).Letter;
        string opusLetter = prep.Sheet.Models!.Single(m => m.EntryKey == BenchmarkBatteryModelComparison.KeyOf(opus)).Letter;
        var question = prep.Sheet.Questions.Single(q => q.Reference == "S1-Q1");

        var lunaCell = question.Models!.Single(c => c.Letter == lunaLetter);
        Assert.Equal(new[] { "None", "OverturnedByVerifier" }, lunaCell.CriticalErrorResolutions);
        var opusCell = question.Models!.Single(c => c.Letter == opusLetter);
        Assert.Equal(new[] { "None", "None" }, opusCell.CriticalErrorResolutions);

        var excerpts = prep.Content.Runs.Where(r => r.Questions.Any(q => q.Number == question.Number)).ToList();
        Assert.Contains(excerpts, r => r.Letter == lunaLetter);
        Assert.DoesNotContain(excerpts, r => r.RunId == overturnedRunId);
        Assert.DoesNotContain(excerpts.SelectMany(r => r.Questions), q => q.CriticalErrorResolution == "OverturnedByVerifier");

        // A question of the other suite records only its own answers' resolutions.
        var other = prep.Sheet.Questions.Single(q => q.Reference == "S2-Q1");
        Assert.Equal(new[] { "None", "None" }, other.Models!.Single(c => c.Letter == lunaLetter).CriticalErrorResolutions);
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
        var request = h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis });
        await h.StoreDocumentAsync(BenchmarkReportAudience.RootCauseAnalysis, request);

        var conflict = Assert.IsType<ConflictObjectResult>(await h.Controller().Start(request, CancellationToken.None));
        string? error = ErrorOf(conflict.Value);
        Assert.StartsWith("The Root Cause Analysis about ", error);
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
        var request = h.ComparisonRequest(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary });

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(request, CancellationToken.None)).Value);

        Assert.Null(preview.Refusal);
        Assert.Equal(BenchmarkReportScope.Comparison, preview.Scope);
        Assert.Null(preview.ComparisonId);
        Assert.Equal(3, preview.ComparisonEntryCount);
        Assert.True(preview.CoversAllEntries);
        Assert.Equal(new[] { "A", "B", "C" }, preview.CoveredModels.Select(m => m.Letter));
        // Every offered document is estimated, the unrequested Root Cause Analysis included.
        Assert.Equal(new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis }, preview.Estimates.Select(e => e.Audience));
        Assert.Equal(new[] { 3800, 14000 }, preview.Estimates.Select(e => e.EstimatedOutputTokens));
        Assert.All(preview.Estimates, e => Assert.Equal(preview.SubjectKey, e.SubjectKey));
        Assert.All(preview.Estimates, e => Assert.False(e.ExceedsContextWindow));
        Assert.Equal(3, preview.SubjectDocuments.Count);
        Assert.Empty(preview.OtherModelSets);
        Assert.Null(preview.WriterContextWindowTokens);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task AStartedJob_CarriesTheRequestedReference_IntoItsSheetAndItsDocument()
    {
        await using var h = await Harness.CreateAsync();
        var lettered = await h.PrepareComparisonAsync(h.ComparisonRequest());
        var last = lettered.Sheet.Peers.OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal).Last();
        Assert.Equal("A", lettered.Sheet.ReferenceLetter);
        Assert.NotEqual("A", last.Letter);

        var request = h.ComparisonRequest();
        request.ReferenceEntryKey = " " + last.EntryKey + " ";
        var job = AdminBenchmarkReportPacksController.NewJob(request, h.Writer, sameProvider: false, UserId, comparisonId: 1, snapshotId: 0);
        Assert.Equal(last.EntryKey, job.Request.ReferenceEntryKey);

        // The job writes from its own copy of the request, completed as a comparison-scope start completes it.
        var copied = job.Request;
        copied.Scope = BenchmarkReportScope.Comparison;
        copied.Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary };
        var prep = await h.PrepareComparisonAsync(copied);
        Assert.Equal(last.Letter, prep.Sheet.ReferenceLetter);
        Assert.Equal(last.EntryKey, prep.ReferenceEntryKey);
        if (prep.Sheet.PairedTests is { Count: > 0 } families)
        {
            Assert.Equal(last.Letter, Assert.Single(families, f => f.Mode == "Reference").ReferenceLetter);
        }

        string reply = Reply(BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, prep.Sheet));
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);
        await h.RunComparisonAsync(copied);

        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(last.EntryKey, document.ReferenceEntryKey);
        Assert.Equal(last.Letter, BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson).ReferenceLetter);
    }

    [Fact]
    public async Task AReferenceThatIsNotACoveredModel_IsA400_FromThePreviewTheLayoutPreviewAndTheStart()
    {
        await using var h = await Harness.CreateAsync();
        var controller = h.Controller();
        var covered = h.Seeded.RunIds.Take(2).Select(id => "run:" + id).ToList();
        string outside = "run:" + h.Seeded.RunIds[2];

        foreach (string reference in new[] { outside, "run:999999" })
        {
            var request = h.ComparisonRequest(covered: covered);
            request.ReferenceEntryKey = reference;

            var preview = Assert.IsType<BadRequestObjectResult>(await controller.Preview(request, CancellationToken.None));
            Assert.Equal(BenchmarkComparisonReportFacts.ReferenceRefusal, ErrorOf(preview.Value));

            var start = Assert.IsType<BadRequestObjectResult>(await controller.Start(request, CancellationToken.None));
            Assert.Equal(BenchmarkComparisonReportFacts.ReferenceRefusal, ErrorOf(start.Value));

            var layout = new BenchmarkReportLayoutPreviewRequest
            {
                RunIds = request.RunIds,
                PricingBasis = request.PricingBasis,
                Scope = BenchmarkReportScope.Comparison,
                CoveredEntryKeys = covered,
                ReferenceEntryKey = reference,
                Audience = BenchmarkReportAudience.ExecutiveSummary
            };
            string json = JsonSerializer.Serialize(layout, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var refused = Assert.IsType<BadRequestObjectResult>(await controller.LayoutPreview(json, null, CancellationToken.None));
            Assert.Equal(BenchmarkComparisonReportFacts.ReferenceRefusal, ErrorOf(refused.Value));
        }

        // A covered reference is previewed as usual.
        var ok = h.ComparisonRequest(covered: covered);
        ok.ReferenceEntryKey = covered[1];
        var previewed = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(await controller.Preview(ok, CancellationToken.None)).Value);
        Assert.Null(previewed.Refusal);

        Assert.Empty(await h.Db.BenchmarkComparisons.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public void AnElevenModelComparisonOfTwentyQuestions_StaysBelowSeventyPercentOfA200000TokenWriter()
    {
        const int models = 11;
        const int questions = 20;
        var entries = new List<BenchmarkModelComparisonEntryDto>();
        var runs = new Dictionary<long, BenchmarkRun>();
        for (int m = 0; m < models; m++)
        {
            long id = 500 + m;
            var specs = Enumerable.Range(0, questions)
                .Select(q => new BenchmarkReportPackFixture.AnswerSpec(700 + q, q + 1, 1, 35 + (q * 7 + m * 13) % 60, Difficulty: 20 + (q % 3) * 30))
                .ToArray();
            var run = BenchmarkReportPackFixture.Run(id, "Provider" + m, "model-" + m, specs);
            foreach (var answer in run.Answers)
            {
                answer.QuestionText = "How does the game treat item " + answer.OrderIndex + " when it is thrown, dipped, read, worn or broken during play? " + new string('x', 200);
                answer.ExpectedPointsUsed = string.Join("\n", Enumerable.Range(1, 6).Select(i => "- Rubric point " + i + " for question " + answer.OrderIndex + ": " + new string('y', 80)));
                answer.AnswerText = string.Join(" ", Enumerable.Repeat("The answer explains the rule for this item and its exceptions in detail.", 30));
                answer.ReviewComment = string.Join(" ", Enumerable.Repeat("The grader explains what the answer got wrong about the rule.", 8));
            }
            runs[id] = run;
            double quality = 90 - m * 3;
            entries.Add(BenchmarkReportPackFixture.Entry("run:" + id, new[] { id }, "Candidate " + m, "Provider" + m, quality, quality - 4, quality + 4,
                modelTimeP50Ms: 8000 + m * 1500, costPerQuestion: 0.01 + m * 0.004));
        }
        var comparison = BenchmarkReportPackFixture.Comparison(entries.ToArray());
        var keys = entries.Select(e => e.Key).ToList();
        var (reference, allPairs, unavailable) = BenchmarkComparisonReportFacts.PairedFromRuns(comparison, keys, runs);
        var built = BenchmarkComparisonReportFacts.BuildFromRuns(
            comparison, keys, runs, BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, reference, allPairs, unavailable, comparisonId: 12);
        Assert.True(built.Sheet != null, built.Refusal);
        Assert.Equal(models, built.Sheet!.Peers.Count);
        Assert.Equal(questions, built.Sheet.Questions.Count);

        const int window = 200_000;
        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet, built.Content!);
        int tokens = (prompt.SystemPrompt.Length + prompt.UserMessage.Length + 3) / 4;
        Assert.True(tokens < window * 0.7, $"{tokens} estimated tokens, {(double)tokens / window:P0} of the window.");
    }

    // --- Preview ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_NamesThePeers_EstimatesEachDocument_AndMakesNoModelCall()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.Request(audiences: new[] { BenchmarkReportAudience.ExecutiveSummary });

        var ok = Assert.IsType<OkObjectResult>(await h.Controller().Preview(request, CancellationToken.None));
        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(ok.Value);

        Assert.Null(preview.Refusal);
        Assert.Null(preview.SameProviderWarning);
        Assert.Equal($"run:{h.Seeded.RunIds[0]}", preview.SubjectKey);
        Assert.Equal("A", Assert.Single(preview.Peers).Letter);
        Assert.Equal(new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis }, preview.Estimates.Select(e => e.Audience));
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
        long older = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, createdAtUtc: new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        long newer = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, createdAtUtc: new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));
        long rootCause = await h.StoreDocumentAsync(BenchmarkReportAudience.RootCauseAnalysis, request, createdAtUtc: new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, comparisonKey: new string('f', 64));
        await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request, origin: BenchmarkReportDocumentOrigin.RunCompletion);

        var preview = Assert.IsType<BenchmarkReportPackPreviewDto>(Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(request, CancellationToken.None)).Value);

        Assert.Null(preview.Refusal);
        Assert.Equal(
            new[] { (BenchmarkReportAudience.ExecutiveSummary, newer), (BenchmarkReportAudience.RootCauseAnalysis, rootCause) },
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

    [Fact]
    public async Task Preview_FlagsEachDocumentAboveTheContextWindow_AndOnlyStartRefusesARequestedOne()
    {
        await using var h = await Harness.CreateAsync(maxRunsPerHour: 0);
        var both = new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis };
        var sized = (BenchmarkReportPackPreviewDto)Assert.IsType<OkObjectResult>(
            await h.Controller().Preview(h.Request(audiences: both), CancellationToken.None)).Value!;
        var ordered = sized.Estimates.OrderBy(e => e.EstimatedInputTokens).ToList();
        Assert.Equal(2, ordered.Count);
        var small = ordered[0];
        var large = ordered[1];
        Assert.True(large.EstimatedInputTokens > small.EstimatedInputTokens + 100, "The two prompts are too close in size for this test.");

        // A window between the two: the smaller prompt fits within the refusal share, the larger does not.
        int window = (int)Math.Ceiling(
            (small.EstimatedInputTokens + large.EstimatedInputTokens) / 2.0 / AdminBenchmarkReportPacksController.ContextWindowRefusalShare);
        var controller = h.Controller(new FixedWindowMetadata(window));

        // The preview estimates both documents, the unrequested one included, and refuses neither.
        var preview = (BenchmarkReportPackPreviewDto)Assert.IsType<OkObjectResult>(
            await controller.Preview(h.Request(audiences: new[] { small.Audience }), CancellationToken.None)).Value!;
        Assert.Null(preview.Refusal);
        Assert.Equal(window, preview.WriterContextWindowTokens);
        Assert.Equal(both, preview.Estimates.Select(e => e.Audience));
        Assert.False(Assert.Single(preview.Estimates, e => e.Audience == small.Audience).ExceedsContextWindow);
        Assert.True(Assert.Single(preview.Estimates, e => e.Audience == large.Audience).ExceedsContextWindow);

        // Start refuses over the requested documents only: the smaller one goes on to the spend cap.
        var fits = Assert.IsAssignableFrom<ObjectResult>(
            await controller.Start(h.Request(audiences: new[] { small.Audience }), CancellationToken.None));
        Assert.Equal(429, fits.StatusCode);
        var tooLarge = Assert.IsType<BadRequestObjectResult>(
            await controller.Start(h.Request(audiences: new[] { large.Audience }), CancellationToken.None));
        Assert.Contains("of the context window of Writer One", ErrorOf(tooLarge.Value));
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task Preview_ThatDiffersOnlyInTheWriter_PreparesOnce_AndARescoredRunIsPreparedAgain()
    {
        await using var h = await Harness.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var cache = new BenchmarkReportPackPreviewCache(memory);
        var both = new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis };

        async Task<BenchmarkReportPackPreviewDto> PreviewAsync(BenchmarkReportPackRequest request)
            => (BenchmarkReportPackPreviewDto)Assert.IsType<OkObjectResult>(
                await h.Controller(previewCache: cache).Preview(request, CancellationToken.None)).Value!;

        var first = await PreviewAsync(h.Request(writerId: h.Writer.Id));
        Assert.Null(first.Refusal);
        Assert.Equal(1, memory.Count);

        // The answers' text is outside the key, so a preview prepared again would show it.
        var answers = await h.Db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == h.Seeded.RunIds[0]).ToListAsync(ct);
        foreach (var answer in answers) answer.AnswerText += " " + new string('x', 300);
        await h.Db.SaveChangesAsync(ct);

        var second = await PreviewAsync(h.Request(writerId: h.SameProvider.Id, audiences: both));
        Assert.Equal(1, memory.Count);
        Assert.Equal(first.Estimates.Select(e => e.PromptChars), second.Estimates.Select(e => e.PromptChars));
        Assert.Equal(first.Peers.Select(p => p.EntryKey), second.Peers.Select(p => p.EntryKey));
        Assert.Contains("same provider", second.SameProviderWarning);

        // A rescored run is a new key.
        var run = await h.Db.BenchmarkRuns.SingleAsync(r => r.Id == h.Seeded.RunIds[0], ct);
        run.QualityIndex = (run.QualityIndex ?? 0) + 1;
        await h.Db.SaveChangesAsync(ct);
        var rescored = await PreviewAsync(h.Request());
        Assert.Null(rescored.Refusal);
        Assert.Equal(2, memory.Count);

        // The comparison scope keys the same way.
        var comparison = await PreviewAsync(h.ComparisonRequest(writerId: h.Writer.Id));
        Assert.Null(comparison.Refusal);
        Assert.Equal(3, memory.Count);
        var again = await PreviewAsync(h.ComparisonRequest(writerId: h.SameProvider.Id, audiences: both));
        Assert.Equal(3, memory.Count);
        Assert.Equal(comparison.CoveredModels.Select(m => m.Letter), again.CoveredModels.Select(m => m.Letter));
        Assert.Equal(comparison.Estimates.Select(e => e.PromptChars), again.Estimates.Select(e => e.PromptChars));
        Assert.Equal(0, h.Provider.Calls);
    }

    /// <summary>The model catalog with every model's context window set to one size.</summary>
    private sealed class FixedWindowMetadata(int window) : ModelMetadataService
    {
        public override ModelMetadata GetMetadata(string provider, string modelId)
        {
            var metadata = base.GetMetadata(provider, modelId);
            metadata.ContextWindowSize = window;
            return metadata;
        }
    }

    [Fact]
    public void ReleaseDatesOf_KeysEachKnownDate_LeavesOutBlankOnes_AndIsNullWithoutALookup()
    {
        var entries = new[]
        {
            new BenchmarkModelComparisonEntryDto { Key = "a", Provider = "OpenAI", ModelId = "gpt-6-luna" },
            new BenchmarkModelComparisonEntryDto { Key = "b", Provider = "OpenAI", ModelId = "gpt-5.6-luna" },
            new BenchmarkModelComparisonEntryDto { Key = "c", Provider = "Anthropic", ModelId = "unknown-model" }
        };
        string? Lookup(string provider, string modelId) => modelId switch
        {
            "gpt-6-luna" => "2026-08-01",
            "gpt-5.6-luna" => " 2026-03-15 ",
            _ => string.Empty
        };

        var dates = BenchmarkReportPackPreparation.ReleaseDatesOf(entries, Lookup);

        Assert.NotNull(dates);
        Assert.Equal(2, dates!.Count);
        Assert.Equal("2026-08-01", dates[Overseer.Services.Benchmarking.RootCause.RootCauseEvidence.ReleaseDateKey("OpenAI", "gpt-6-luna")]);
        Assert.Equal("2026-03-15", dates[Overseer.Services.Benchmarking.RootCause.RootCauseEvidence.ReleaseDateKey("OpenAI", "gpt-5.6-luna")]);
        Assert.Null(BenchmarkReportPackPreparation.ReleaseDatesOf(entries, null));
    }

    // --- Documents controller ---------------------------------------------------------------------------

    [Fact]
    public async Task Render_IsNotFoundForAnUnknownDocument_AndRefusesADisallowedCombination()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        // A Root Cause Analysis renders at Full alone; every request here is refused before rendering.
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.RootCauseAnalysis);
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
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
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
        // A Root Cause Analysis renders at Full alone; every request here is refused before rendering.
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.RootCauseAnalysis);
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
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
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
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_detailed_anonymized.pdf",
            provider.FileDownloadName);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(provider.FileContents, 0, 5));
        Assert.Equal(
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_full_named_INTERNAL.pdf",
            full.FileDownloadName);
    }

    [Fact]
    public async Task RenderPdf_Inline_SendsAnInlineDispositionWithTheSameName()
    {
        BenchmarkPdfTestSetup.Configure();
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
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
            "inline; filename*=UTF-8''run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_detailed_anonymized.pdf",
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
        run.ReportDocumentsMessage = "Root Cause Analysis: it could not be written.";

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
        var packDocument = Stored(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentOrigin.ReportPack);
        var executive = Stored(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentOrigin.RunCompletion);
        var rootCause = Stored(BenchmarkReportAudience.RootCauseAnalysis, BenchmarkReportDocumentOrigin.RunCompletion);
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
        Assert.IsType<NoContentResult>(await controller.Delete(rootCause.Id, ct));
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
    public async Task List_BackfillsAMissingSynthesisFingerprint_AndFlagsAReRunSynthesis()
    {
        await using var h = await Harness.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));
        await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, ct);
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var render = new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance,
            factsCache: new BenchmarkReportDocumentFactsCache(memory));
        long runId = h.Seeded.RunIds[0];
        long peerRunId = h.Seeded.RunIds[1];

        async Task<string?> StoredFingerprintAsync(long id)
        {
            await using var db = new ApplicationDbContext(h.Options);
            return await db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().Where(r => r.Id == id).Select(r => r.SynthesisFingerprint).SingleAsync(ct);
        }

        Assert.Null(await StoredFingerprintAsync(runId));
        Assert.Null(await StoredFingerprintAsync(peerRunId));

        var fresh = Assert.Single(await render.ListAsync(null, runId, null, ct));
        Assert.False(fresh.RunChangedSinceGeneration);
        Assert.False(fresh.PeersChangedSinceGeneration);
        Assert.Equal(1, fresh.PeerCount);
        Assert.Equal(BenchmarkReportRenderService.SynthesisSha256(null, null), await StoredFingerprintAsync(runId));
        Assert.Equal(BenchmarkReportRenderService.SynthesisSha256(null, null), await StoredFingerprintAsync(peerRunId));
        Assert.Equal(1, memory.Count);

        // A re-run final synthesis writes both columns, as the assessment does.
        var run = await h.Db.BenchmarkRuns.SingleAsync(r => r.Id == runId, ct);
        run.AssessmentJson = "{\"finalScore\":81,\"findings\":[]}";
        run.SynthesisFingerprint = BenchmarkReportRenderService.SynthesisSha256(run.AssessmentJson, run.CoAssessorSynthesisJson);
        await h.Db.SaveChangesAsync(ct);

        var rerun = Assert.Single(await render.ListAsync(null, runId, null, ct));
        Assert.True(rerun.RunChangedSinceGeneration);
        Assert.False(rerun.PeersChangedSinceGeneration);
        Assert.Equal(fresh.PeerLetters, rerun.PeerLetters);
        Assert.Equal(1, memory.Count);
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
        var battery8 = BatteryReportHarness.BatteryDocument(8, BenchmarkReportAudience.ExecutiveSummary);
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
    public void TheRenderService_TakesOnlyTheDbContextTheChartStoreALoggerTheChartPublisherAndTheFactsCache()
    {
        // The chart publisher draws with QuestPDF from the stored fact sheet, and the facts cache holds parsed
        // fact sheets; neither holds a provider.
        var constructor = Assert.Single(typeof(BenchmarkReportRenderService).GetConstructors());
        Assert.Equal(
            new[]
            {
                typeof(ApplicationDbContext), typeof(BenchmarkReportChartStore), typeof(ILogger<BenchmarkReportRenderService>),
                typeof(Overseer.Services.Benchmarking.Charts.RootCauseChartPublisher),
                typeof(BenchmarkReportDocumentFactsCache)
            },
            constructor.GetParameters().Select(p => p.ParameterType));
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

    // --- Headline rescue and diagnostics -----------------------------------------------------------------

    private static string Overlong(string headline) => headline.TrimEnd('.') + string.Concat(Enumerable.Repeat(" and still more words", 8)) + ".";

    [Fact]
    public async Task ARepairedHeadlineTooLong_TakesTheFirstDraftsValidHeadline_AndCompletesWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string valid = "{{subject}} gave accurate and readable answers across the benchmark.";
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: valid, extraStrength: "It handled 7 hard questions."));
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: Overlong(valid)));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
        Assert.Equal(valid, BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Headline);
        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        Assert.Contains(notes, n => n.Rule == 7 && n.Location == "headline" && n.Dropped && n.Message.Contains("first draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AComparisonWhoseHeadlinesAreBothTooLong_GetsAHeadlineOnlyTurn_AndStoresItsHeadline()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.ComparisonRequest();
        var prep = await h.PrepareComparisonAsync(request);
        var writer = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, prep.Sheet);
        string valid = writer.Headline;
        writer.Headline = Overlong(valid);
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(JsonSerializer.Serialize(new { headline = valid }));

        var job = await h.RunComparisonAsync(request);

        Assert.Equal(3, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(valid, BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Headline);
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
        Assert.Equal(3, (await h.Db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken)).Count);
        Assert.Contains(job.ToDto().Log, l => l.Severity == "warning" && l.Message.Contains("headline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AHeadlineOnlyTurnThatFailsToo_FailsTheDocument_AndKeepsItsReply()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.ComparisonRequest();
        var prep = await h.PrepareComparisonAsync(request);
        var writer = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, prep.Sheet);
        writer.Headline = Overlong(writer.Headline);
        string stillTooLong = JsonSerializer.Serialize(new { headline = writer.Headline });
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(stillTooLong);

        var job = await h.RunComparisonAsync(request);

        Assert.Equal(3, h.Provider.Calls);
        Assert.Empty(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        var progress = Assert.Single(job.ToDto().Documents);
        Assert.Equal(nameof(BenchmarkReportPackDocumentStatus.Failed), progress.Status);
        Assert.Equal(stillTooLong, progress.FailedReply);
        Assert.NotNull(progress.FirstTurnIssues);
        Assert.Contains(progress.FinalIssues!, i => i.Rule == 7 && i.Location == "headline");
    }

    [Fact]
    public async Task TheFirstTurnLogLine_CountsTheIssuesByRule()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, extraStrength: "It handled 7 hard questions."));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Contains(job.ToDto().Log, l => System.Text.RegularExpressions.Regex.IsMatch(l.Message,
            @"\d+ validation issue\(s\) \(rule \d+: \d+(, rule \d+: \d+)*\); sending one repair turn\."));
    }

    [Fact]
    public async Task AnUnparseableRepairReply_KeepsTheFirstReply_AndSaysSo()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, extraStrength: "It handled 7 hard questions."));
        h.Provider.Replies.Enqueue("this is not JSON at all");

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Contains(job.ToDto().Log, l => l.Severity == "warning" && l.Message.Contains("the repair reply could not be parsed", StringComparison.Ordinal)
                                              && l.Message.Contains("the first reply is used", StringComparison.Ordinal));
        Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnreadableFirstReply_GetsASecondRepairTurn_WhenTheRevisionHasIssues()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string valid = ValidExecutiveReply(prep);
        h.Provider.Replies.Enqueue(valid[..(valid.Length / 2)]);
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, extraStrength: "It handled 7 hard questions."));
        h.Provider.Replies.Enqueue(valid);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(3, h.Provider.Calls);
        var progress = Assert.Single(job.ToDto().Documents);
        Assert.Equal(new[] { BenchmarkReportPackCallKinds.Draft, BenchmarkReportPackCallKinds.Revision, BenchmarkReportPackCallKinds.Revision },
            progress.Calls.Select(c => c.Kind));
        Assert.Equal(1, Assert.Single(progress.FirstTurnIssues!).Rule);
        Assert.Empty(progress.RevisionIssues!);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.Completed, document.Status);
        Assert.Contains(job.ToDto().Log, l => l.Severity == "warning"
            && l.Message.Contains("the first reply could not be read, so the revised reply gets a repair turn of its own: ", StringComparison.Ordinal));

        // Each repair turn carries the previous reply and its repair message on the same conversation.
        Assert.Equal(new[] { h.Provider.MessageCounts[0], h.Provider.MessageCounts[0] + 2, h.Provider.MessageCounts[0] + 4 }, h.Provider.MessageCounts);
    }

    [Fact]
    public async Task AnUnreadableFirstReply_WithACleanRevision_TakesTwoCalls()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string valid = ValidExecutiveReply(prep);
        h.Provider.Replies.Enqueue(valid[..(valid.Length / 2)]);
        h.Provider.Replies.Enqueue(valid);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        Assert.DoesNotContain(job.ToDto().Log, l => l.Message.Contains("the first reply could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReadableFirstReply_StillGetsOneRepairTurn()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = ExecutiveReply(prep, extraStrength: "It handled 7 hard questions.");
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(2, h.Provider.Calls);
        var progress = Assert.Single(job.ToDto().Documents);
        Assert.Equal(new[] { BenchmarkReportPackCallKinds.Draft, BenchmarkReportPackCallKinds.Revision }, progress.Calls.Select(c => c.Kind));
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
    }

    [Fact]
    public async Task AReplyCutOffAtTheOutputLimit_SaysSoInItsRuleOneNote_AndTheLog()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string valid = ValidExecutiveReply(prep);
        h.Provider.FinishReasons.Enqueue("MAX_TOKENS");
        h.Provider.ReasoningTokens = 600;
        h.Provider.Replies.Enqueue(valid[..(valid.Length / 2)]);
        h.Provider.Replies.Enqueue(valid);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var dto = job.ToDto();
        var progress = Assert.Single(dto.Documents);
        var note = Assert.Single(progress.FirstTurnIssues!);
        Assert.Equal(1, note.Rule);
        Assert.StartsWith("The reply stopped at the output token limit (32,000 tokens, reasoning included), so its JSON is incomplete: ", note.Message);
        Assert.Contains(dto.Log, l => l.Severity == "warning"
            && l.Message.EndsWith("the reply stopped at the output token limit (32,000 tokens, 600 reasoning).", StringComparison.Ordinal));

        // The repair turn tells the writer why its reply was incomplete.
        Assert.Contains("The reply stopped at the output token limit", h.Provider.Requests.ToArray()[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachCall_RecordsItsReasoningTokens_AndALaterTurnItsRequest()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.ReasoningTokens = 600;
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: "{{subject}} answered 3 questions well."));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        var progress = Assert.Single(job.ToDto().Documents);
        Assert.Equal(2, progress.Calls.Count);
        Assert.All(progress.Calls, c => Assert.Equal(600L, c.ReasoningTokens));

        var (_, calls) = Assert.Single(job.WriterReplies());
        Assert.All(calls, c => Assert.Equal(600L, c.Call.ReasoningTokens));
        Assert.Null(calls[0].Reply.Request);
        Assert.False(string.IsNullOrWhiteSpace(calls[1].Reply.Request));

        // The request recorded is the user message the repair turn sent last.
        using var sent = JsonDocument.Parse(h.Provider.Requests.ToArray()[1]);
        var lastMessage = sent.RootElement.EnumerateArray().Last();
        Assert.Equal(lastMessage.GetProperty("content").GetString(), calls[1].Reply.Request);
    }

    [Fact]
    public async Task ACallWithoutReasoningTokens_RecordsNone()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(Assert.Single(job.ToDto().Documents).Calls).ReasoningTokens);
    }

    [Fact]
    public async Task TheWriterCall_IsSentTheConfiguredOutputLimit_AndTheJobRecordsIt()
    {
        await using var h = await Harness.CreateAsync(maxOutputTokens: 40000);
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        // The fake writer's model is not in the catalog, so nothing lowers the configured limit.
        Assert.Equal(40000, Assert.Single(h.Provider.MaxOutputTokensLog));
        Assert.Equal(40000, job.ToDto().WriterMaxOutputTokens);
    }

    [Theory]
    [InlineData(null, "Google", "gemini-4-argon", BenchmarkReportPackPreparation.DefaultMaxOutputTokens)]
    [InlineData(40000, "Google", "gemini-4-argon", 40000)]
    [InlineData(100000, "Google", "gemini-4-argon", 65536)]
    [InlineData(100000, WriterProvider.Name, "writer-1", 100000)]
    public void TheWriterOutputLimit_IsTheConfiguredOne_LoweredToTheCatalogLimit(int? configured, string provider, string modelId, int expected)
    {
        var settings = new Dictionary<string, string?>();
        if (configured is int value)
        {
            settings["Benchmark:ReportPack:MaxOutputTokens"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Equal(expected, BenchmarkReportPackPreparation.WriterMaxOutputTokens(configuration, provider, modelId));
    }

    [Fact]
    public async Task TheValidationDiagnostics_AreNullWhileTheJobRuns_AndFilledOnceItEnds()
    {
        var running = new BenchmarkReportPackJob
        {
            Request = new BenchmarkReportPackRequest(),
            Cts = new CancellationTokenSource(),
            Documents = new List<BenchmarkReportPackDocumentProgress> { new() { Audience = BenchmarkReportAudience.ExecutiveSummary } }
        };
        var note = new BenchmarkReportValidationNote { Rule = 7, Location = "headline", Message = "Too long.", Dropped = false };
        var digit = new BenchmarkReportValidationNote { Rule = 3, Location = "sections.meaning[p1]", Message = "Contains the digit form \"3\"." };
        var counts = new[]
        {
            new BenchmarkReportPackWordCountDto
            {
                Location = "sections.meaning", LocationLabel = "What this means for use as a game assistant", Cap = 90,
                Draft = 96, Revision = 94, Final = 88, FinalParagraphs = new List<int> { 40, 48 }
            }
        };
        running.SetDocumentDiagnostics(running.Documents[0], new[] { note, digit }, new[] { note, digit }, "raw reply", new[] { digit }, counts);

        var whileRunning = Assert.Single(running.ToDto().Documents);
        Assert.Null(whileRunning.FirstTurnIssues);
        Assert.Null(whileRunning.FinalIssues);
        Assert.Null(whileRunning.FailedReply);
        Assert.Null(whileRunning.RevisionIssues);
        Assert.Null(whileRunning.WordCounts);

        running.SetStatus(BenchmarkReportPackJobStatus.CompletedWithErrors);
        var after = Assert.Single(running.ToDto().Documents);
        Assert.Equal(new[] { 7, 3 }, after.FirstTurnIssues!.Select(i => i.Rule));
        Assert.Equal("headline", after.FinalIssues![0].Location);
        Assert.Equal("raw reply", after.FailedReply);

        // A draft's issue gives what its rule would do; the same note among the final ones was recorded against kept text.
        Assert.Equal(BenchmarkReportRuleCatalog.RemovesItemSeverity, after.FirstTurnIssues![1].Severity);
        Assert.Equal(BenchmarkReportRuleCatalog.RemovesItemSeverity, Assert.Single(after.RevisionIssues!).Severity);
        Assert.Equal(BenchmarkReportRuleCatalog.WarningTextKeptSeverity, after.FinalIssues![1].Severity);
        var count = Assert.Single(after.WordCounts!);
        Assert.Equal(96, count.Draft);
        Assert.Equal(94, count.Revision);
        Assert.Null(count.Shortened);
        Assert.Equal(88, count.Final);
        Assert.Equal(new[] { 40, 48 }, count.FinalParagraphs);

        await Task.CompletedTask;
    }

    // --- The Root Cause Analysis of a run completion ------------------------------------------------------

    private async Task<(long ExecutiveSummary, long RootCause)> StoreRunCompletionDocumentsAsync(Harness h)
    {
        var request = h.StandaloneRequest(new[] { BenchmarkReportAudience.ExecutiveSummary });
        long es = await h.StoreDocumentAsync(BenchmarkReportAudience.ExecutiveSummary, request,
            subjectKey: request.SubjectKey, origin: BenchmarkReportDocumentOrigin.RunCompletion);
        long rca = await h.StoreDocumentAsync(BenchmarkReportAudience.RootCauseAnalysis, request,
            subjectKey: request.SubjectKey, origin: BenchmarkReportDocumentOrigin.RunCompletion);
        return (es, rca);
    }

    [Fact]
    public async Task RewritingWithControls_ReplacesTheOldRootCauseAnalysis_AndLeavesTheExecutiveSummary()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync(standalone: true);
        Assert.NotNull(prep.Sheet.RootCause);
        var (es, oldRca) = await StoreRunCompletionDocumentsAsync(h);
        string reply = Reply(RootCauseReportFixture.Writer(prep.Sheet, comparison: false));
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunCompletionAsync(BenchmarkReportAudience.RootCauseAnalysis, replace: new[] { oldRca });

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        var documents = await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(documents, d => d.Id == es && d.Audience == BenchmarkReportAudience.ExecutiveSummary);
        Assert.DoesNotContain(documents, d => d.Id == oldRca);
        var written = Assert.Single(documents, d => d.Audience == BenchmarkReportAudience.RootCauseAnalysis);
        Assert.Equal(BenchmarkReportDocumentOrigin.RunCompletion, written.Origin);
        Assert.NotNull(BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(written.FactsJson).RootCause);
    }

    [Fact]
    public async Task AJobWithoutReplaceIds_ReplacesNothing()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync(standalone: true);
        var (es, oldRca) = await StoreRunCompletionDocumentsAsync(h);
        string reply = Reply(RootCauseReportFixture.Writer(prep.Sheet, comparison: false));
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        await h.RunCompletionAsync(BenchmarkReportAudience.RootCauseAnalysis);

        var documents = await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(documents, d => d.Id == es);
        Assert.Contains(documents, d => d.Id == oldRca);
        Assert.Equal(2, documents.Count(d => d.Audience == BenchmarkReportAudience.RootCauseAnalysis));
    }

    [Fact]
    public async Task AReplaceIdOfAnotherAudience_IsNotDeleted()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync(standalone: true);
        var (es, _) = await StoreRunCompletionDocumentsAsync(h);
        string reply = Reply(RootCauseReportFixture.Writer(prep.Sheet, comparison: false));
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        await h.RunCompletionAsync(BenchmarkReportAudience.RootCauseAnalysis, replace: new[] { es });

        Assert.Contains(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken), d => d.Id == es);
    }

    // --- Phases, calls and the repair loop's records ------------------------------------------------------

    [Fact]
    public async Task ADropThatUncitesAnAxis_AddsTheOverseerParagraphAtItsAxisPlace_AndRecordsItAsAddedByOverseer()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync(standalone: true);
        var writer = RootCauseReportFixture.Writer(prep.Sheet, comparison: false);
        const string removed = "The speed gap is due to the price. Speed: {{rca.axis.speed.status}}.";
        writer.Sections[BenchmarkReportSlots.Conclusions] =
            "Intelligence: {{rca.axis.intelligence.status}}.\n\n" + removed + "\n\nCost: {{rca.axis.cost.status}}.";
        string reply = Reply(writer);
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunCompletionAsync(BenchmarkReportAudience.RootCauseAnalysis);

        Assert.Equal(BenchmarkReportPackJobStatus.Completed, job.Status);
        Assert.Equal(2, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);

        // The paragraph Overseer wrote stands where the speed paragraph stood, between intelligence and cost.
        string conclusions = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Sections[BenchmarkReportSlots.Conclusions];
        Assert.DoesNotContain("due to the price", conclusions);
        var paragraphs = BenchmarkReportPackValidator.SplitParagraphs(conclusions);
        Assert.Equal(3, paragraphs.Count);
        Assert.StartsWith("Intelligence:", paragraphs[0]);
        Assert.StartsWith(BenchmarkReportPackService.AddedByOverseer + " Speed: {{rca.axis.speed.status}}.", paragraphs[1]);
        Assert.StartsWith("Cost:", paragraphs[2]);

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        var drop = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule && n.Dropped);
        Assert.Equal("sections.conclusions[p2]", drop.Location);
        Assert.Equal(removed, drop.Text);

        // The stale "Does not cite" note is gone; Overseer's paragraph is recorded as added.
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseAxisStatusRule && !n.AddedByOverseer);
        var added = Assert.Single(notes, n => n.AddedByOverseer);
        Assert.Equal(BenchmarkReportPackValidator.RootCauseAxisStatusRule, added.Rule);
        Assert.Equal("sections.conclusions", added.Location);
        Assert.False(added.Dropped);
        Assert.Equal("The writer's paragraph on speed was removed; Overseer wrote the axis's status in its place.", added.Message);
        Assert.Equal(paragraphs[1], added.Text);
        var issue = BenchmarkReportRuleCatalog.ToIssueDto(added);
        Assert.True(issue.AddedByOverseer);
        Assert.Equal(BenchmarkReportRuleCatalog.AddedByOverseerSeverity, issue.Severity);

        var log = job.ToDto().Log;
        Assert.Contains(log, l => l.Message.Contains("1 item(s) removed by validation: Conclusions, paragraph 2 ("
                                                     + BenchmarkReportRuleCatalog.Name(BenchmarkReportPackValidator.RootCauseCausalClaimRule) + ")", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Message.Contains("the conclusions' paragraph on speed was removed; Overseer wrote the axis's status in its place.", StringComparison.Ordinal));
    }

    // --- The final shortening turn ------------------------------------------------------------------------

    private const string ShortMeaning = "{{subject}} would serve players well as a game assistant.";

    /// <summary>A meaning section of ninety-six words, six over its cap: <see cref="ShortMeaning"/> and twenty-nine short sentences.</summary>
    private static string LongMeaning() => ShortMeaning + " " + string.Join(" ", Enumerable.Repeat("Answers stayed clear.", 29));

    [Fact]
    public async Task ASectionStillOverItsCapAfterTheRepair_GetsOneShorteningTurn_AndItsReplyIsMerged()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string longReply = ExecutiveReply(prep, meaning: LongMeaning());
        string shortReply = JsonSerializer.Serialize(new { sections = new Dictionary<string, string> { [BenchmarkReportSlots.Meaning] = ShortMeaning } });
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(shortReply);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(3, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ShortMeaning, BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Sections[BenchmarkReportSlots.Meaning]);
        // A shortening that brings every section within its cap leaves no warning.
        Assert.Equal(BenchmarkReportDocumentStatus.Completed, document.Status);
        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        Assert.DoesNotContain(notes, n => n.Rule == 7);

        var dto = job.ToDto();
        var progress = Assert.Single(dto.Documents);
        Assert.Equal(
            new[] { BenchmarkReportPackCallKinds.Draft, BenchmarkReportPackCallKinds.Revision, BenchmarkReportPackCallKinds.Shorten },
            progress.Calls.Select(c => c.Kind));
        Assert.Equal(new int?[] { longReply.Length, longReply.Length, shortReply.Length }, progress.Calls.Select(c => c.ReplyChars));
        Assert.Contains(dto.Log, l => l.Message.Contains("1 section(s) over their word caps after the repair (What this means for use as a game assistant, 96 of 90); sending one shortening turn.", StringComparison.Ordinal));
        var shortenedLine = Assert.Single(dto.Log, l => l.Message.Contains("the shortening turn brought it to", StringComparison.Ordinal));
        Assert.Equal("info", shortenedLine.Severity);
        Assert.EndsWith("What this means for use as a game assistant was 96 words, 6 over its cap of 90, after the repair; the shortening turn brought it to 9.",
            shortenedLine.Message, StringComparison.Ordinal);

        // The diagnostics: the revision's own issues, and the word counts turn by turn.
        Assert.Contains(progress.RevisionIssues!, i => i.Rule == 7 && i.Location == "sections." + BenchmarkReportSlots.Meaning);
        var meaning = Assert.Single(progress.WordCounts!, c => c.Location == "sections." + BenchmarkReportSlots.Meaning);
        Assert.Equal(BenchmarkReportPackValidator.MeaningMaxWords, meaning.Cap);
        Assert.Equal(96, meaning.Draft);
        Assert.Equal(96, meaning.Revision);
        Assert.Equal(9, meaning.Shortened);
        Assert.Equal(9, meaning.Final);
        Assert.Equal(new[] { 9 }, meaning.FinalParagraphs);
        Assert.DoesNotContain(progress.FinalIssues!, i => i.Rule == 7);

        // Each reply is held on the job, and none travels with the polled job object.
        Assert.Equal(new[] { longReply, longReply, shortReply }, job.Documents[0].CallReplies.Select(r => r.Text));
        string polled = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("Answers stayed clear. Answers stayed clear.", polled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShortenedSectionStillOverItsCap_IsTrimmed_AndTheDocumentCompletesWithWarnings()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string longReply = ExecutiveReply(prep, meaning: LongMeaning());
        // Ninety-three words: three over the cap of ninety.
        string stillLong = ShortMeaning + " " + string.Join(" ", Enumerable.Repeat("Answers stayed clear.", 28));
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(JsonSerializer.Serialize(new { sections = new Dictionary<string, string> { [BenchmarkReportSlots.Meaning] = stillLong } }));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(3, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkReportDocumentStatus.CompletedWithWarnings, document.Status);
        string meaning = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Sections[BenchmarkReportSlots.Meaning];
        Assert.Equal(BenchmarkReportPackValidator.MeaningMaxWords, BenchmarkReportPackValidator.WordCount(meaning));

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        var trimmed = Assert.Single(notes, n => n.Rule == 7);
        Assert.True(trimmed.Dropped);
        Assert.Contains(job.ToDto().Log, l => l.Severity == "info"
            && l.Message.EndsWith("the shortening turn brought it to 93, still over its cap.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AShorteningReplyThatAddsBlockingIssues_IsNotUsed_AndTheSectionLosesSentencesFromItsEnd()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string longReply = ExecutiveReply(prep, meaning: LongMeaning());
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(longReply);
        h.Provider.Replies.Enqueue(JsonSerializer.Serialize(new
        {
            sections = new Dictionary<string, string> { [BenchmarkReportSlots.Meaning] = "It answered 3 questions.\n\nIt made 2 errors." }
        }));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(3, h.Provider.Calls);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
        string meaning = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson).Sections[BenchmarkReportSlots.Meaning];
        Assert.StartsWith(ShortMeaning, meaning, StringComparison.Ordinal);
        Assert.Equal(BenchmarkReportPackValidator.MeaningMaxWords, BenchmarkReportPackValidator.WordCount(meaning));

        var notes = BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson);
        var trimmed = Assert.Single(notes, n => n.Rule == 7);
        Assert.True(trimmed.Dropped);
        Assert.Equal("sections.meaning[p1]", trimmed.Location);
        Assert.StartsWith("Removed its last 2 sentences", trimmed.Message, StringComparison.Ordinal);
        Assert.Contains(job.ToDto().Log, l => l.Message.Contains("the final turn's reply was not used", StringComparison.Ordinal));
        Assert.Equal(8, Assert.Single(job.ToDto().Documents).WordCounts!.Single(c => c.Location == "sections.meaning").Shortened);
    }

    [Fact]
    public async Task OtherBlockingIssuesAfterTheRepair_SendNoShorteningTurn()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = ExecutiveReply(prep, meaning: LongMeaning(), extraStrength: "It handled 7 hard questions.");
        h.Provider.Replies.Enqueue(reply);
        h.Provider.Replies.Enqueue(reply);

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(2, h.Provider.Calls);
        Assert.DoesNotContain(Assert.Single(job.ToDto().Documents).Calls, c => c.Kind == BenchmarkReportPackCallKinds.Shorten);
        Assert.Single(await h.Db.BenchmarkReportDocuments.ToListAsync(TestContext.Current.CancellationToken));
    }

    // --- The writer's replies, served on request ----------------------------------------------------------

    [Fact]
    public async Task TheWriterRepliesEndpoint_IsNotFoundForAnUnknownJob_AndServesMarkdownForAHeldOne()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        string reply = ValidExecutiveReply(prep);
        h.Provider.Replies.Enqueue(reply);
        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(await h.Controller().GetWriterReplies("no-such-job", TestContext.Current.CancellationToken));

        var content = Assert.IsType<ContentResult>(await h.Controller().GetWriterReplies(job.Id, TestContext.Current.CancellationToken));
        Assert.Equal("text/markdown; charset=utf-8", content.ContentType);
        Assert.StartsWith("# Writer replies: ", content.Content, StringComparison.Ordinal);
        Assert.Contains("### Call 1: Draft", content.Content, StringComparison.Ordinal);
        Assert.Contains(reply, content.Content, StringComparison.Ordinal);

        // The stored document resolves every token the reply cites.
        Assert.Contains("#### Tokens cited\n\n| Token | Prints |\n|---|---|\n", content.Content, StringComparison.Ordinal);
        Assert.Contains("| `{{subject}}` | ", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("not in the fact sheet", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePhases_RunWritingCheckingRevisingStoring()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ExecutiveReply(prep, headline: "{{subject}} answered 3 questions well."));
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                BenchmarkReportPackPhases.Preparing, BenchmarkReportPackPhases.Writing, BenchmarkReportPackPhases.Checking,
                BenchmarkReportPackPhases.Revising, BenchmarkReportPackPhases.CheckingRevision, BenchmarkReportPackPhases.Storing,
                BenchmarkReportPackPhases.Done
            },
            job.Documents[0].PhaseHistory);
        var progress = Assert.Single(job.ToDto().Documents);
        Assert.Equal(BenchmarkReportPackPhases.Done, progress.Phase);
        Assert.False(progress.FirstDraftClean);

        // The revision's detail counts the first draft's issues by rule, in plain words.
        var issues = new List<BenchmarkReportValidationNote>
        {
            new() { Rule = 16, Location = "sections.meaning[p1]" },
            new() { Rule = 16, Location = "sections.meaning[p2]" },
            new() { Rule = 3, Location = "headline" },
        };
        Assert.Equal(
            "The automatic check found 3 issues in the first draft: "
            + "ranking without the overlap caveat (2), " + BenchmarkReportRuleCatalog.Name(3).ToLowerInvariant() + " (1).",
            BenchmarkReportPackService.RevisionDetail(issues));
    }

    [Fact]
    public void FirstTurnIssues_AreVisibleWhileRunning()
    {
        var job = new BenchmarkReportPackJob
        {
            Request = new BenchmarkReportPackRequest(),
            Cts = new CancellationTokenSource(),
            Documents = new List<BenchmarkReportPackDocumentProgress> { new() { Audience = BenchmarkReportAudience.ExecutiveSummary } }
        };
        var document = job.Documents[0];
        var note = new BenchmarkReportValidationNote { Rule = 16, Location = "sections.meaning[p1]", Message = "Ranks {{subject}} against {{peer:A}}.", Text = "The paragraph." };
        job.SetFirstTurnIssues(document, new[] { note });

        // Before the revision starts, nothing travels on a running job's polls.
        Assert.Null(Assert.Single(job.ToDto().Documents).FirstTurnIssues);

        job.SetDocumentPhase(document, BenchmarkReportPackPhases.Revising, "The automatic check found one issue in the first draft.");
        var running = Assert.Single(job.ToDto().Documents);
        var issue = Assert.Single(running.FirstTurnIssues!);
        Assert.Equal(16, issue.Rule);
        Assert.Equal(BenchmarkReportRuleCatalog.Name(16), issue.RuleName);
        Assert.Equal(BenchmarkReportRuleCatalog.WarningSeverity, issue.Severity);
        Assert.Equal("What this means for use as a game assistant, paragraph 1", issue.LocationLabel);
        Assert.Equal("The paragraph.", issue.Text);
        Assert.Null(running.FinalIssues);
        Assert.Null(running.FailedReply);
        Assert.Equal(BenchmarkReportPackPhases.Revising, running.Phase);
        Assert.Equal("The automatic check found one issue in the first draft.", running.PhaseDetail);
    }

    [Fact]
    public async Task EachCall_IsRecordedWithItsKind()
    {
        await using var h = await Harness.CreateAsync();
        var request = h.ComparisonRequest();
        var prep = await h.PrepareComparisonAsync(request);
        var writer = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, prep.Sheet);
        string valid = writer.Headline;
        writer.Headline = Overlong(valid);
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(Reply(writer));
        h.Provider.Replies.Enqueue(JsonSerializer.Serialize(new { headline = valid }));

        var job = await h.RunComparisonAsync(request);

        var dto = job.ToDto();
        var calls = Assert.Single(dto.Documents).Calls;
        Assert.Equal(
            new[] { BenchmarkReportPackCallKinds.Draft, BenchmarkReportPackCallKinds.Revision, BenchmarkReportPackCallKinds.Headline },
            calls.Select(c => c.Kind));
        Assert.All(calls, c => Assert.Equal(WriterProvider.PromptTokens, c.InputTokens));
        Assert.All(calls, c => Assert.Equal(WriterProvider.OutputTokens, c.OutputTokens));
        Assert.Equal(new[] { 2, 4, 6 }, calls.Select(c => c.RequestMessages));
        Assert.True(calls[0].RequestCharacters > 0);
        Assert.True(calls[1].RequestCharacters > calls[0].RequestCharacters);
        Assert.True(calls[2].RequestCharacters > calls[1].RequestCharacters);
        Assert.True(calls[0].StartedAtUtc <= calls[1].StartedAtUtc && calls[1].StartedAtUtc <= calls[2].StartedAtUtc);

        // The sums stay as they were.
        Assert.Equal(3, dto.Documents[0].ModelCalls);
        Assert.Equal(calls.Sum(c => c.InputTokens), dto.Documents[0].InputTokens);

        // The job's context.
        Assert.Equal(BenchmarkReportPackRenderer.ComparisonReportFormatVersion, dto.ReportFormatVersion);
        Assert.Null(dto.EngineVersion);
        Assert.Equal(Overseer.Services.Benchmarking.Pdf.BenchmarkPdfRenderer.LayoutVersion, dto.PdfLayoutVersion);
        Assert.Equal(Overseer.Services.Benchmarking.Word.BenchmarkWordRenderer.LayoutVersion, dto.WordLayoutVersion);
        Assert.Equal(BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportScope.Comparison), dto.WriterPromptSha256);
        Assert.Equal(WriterProvider.Name, dto.WriterProvider);
        Assert.Equal(h.Writer.ModelId, dto.WriterModelId);
        Assert.Equal(prep.Comparison.Entries.Select(e => e.Key), dto.Entries);
        Assert.Equal(prep.Covered.Single(c => c.Key == prep.ReferenceEntryKey).Label, dto.ReferenceModel);
    }

    [Fact]
    public async Task ACleanFirstDraft_IsFlagged_AndNotRevised()
    {
        await using var h = await Harness.CreateAsync();
        var prep = await h.PrepareAsync();
        h.Provider.Replies.Enqueue(ValidExecutiveReply(prep));

        var job = await h.RunAsync(BenchmarkReportAudience.ExecutiveSummary, TestContext.Current.CancellationToken);

        Assert.Equal(1, h.Provider.Calls);
        var progress = Assert.Single(job.ToDto().Documents);
        Assert.True(progress.FirstDraftClean);
        Assert.Equal(BenchmarkReportPackCallKinds.Draft, Assert.Single(progress.Calls).Kind);
        Assert.DoesNotContain(BenchmarkReportPackPhases.Revising, job.Documents[0].PhaseHistory);
        Assert.Empty(progress.FirstTurnIssues!);
        Assert.DoesNotContain(job.ToDto().Log, l => l.Message.Contains("removed by validation", StringComparison.Ordinal));
    }

    // --- Replies -----------------------------------------------------------------------------------------

    private static string ValidExecutiveReply(BenchmarkReportPackPreparation prep) => ExecutiveReply(prep);

    /// <summary>An Executive Summary reply citing a fact the sheet really has; each argument overrides one part.</summary>
    private static string ExecutiveReply(
        BenchmarkReportPackPreparation prep, string? headline = null, string? extraStrength = null, string? confidence = null, string? meaning = null)
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
            [BenchmarkReportSlots.Meaning] = meaning ?? "{{subject}} would serve players well as a game assistant.",
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

        public static async Task<Harness> CreateAsync(int maxRunsPerHour = 100, int? maxOutputTokens = null)
        {
            var options = BenchmarkRunExamTests.InMemoryOptions();
            var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
            var settings = new Dictionary<string, string?>
            {
                ["AesEncryptionKey"] = Convert.ToBase64String(new byte[32]),
                ["Benchmark:Compliance:MaxRunsPerHour"] = maxRunsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Benchmark:Compliance:MaxRunsPerDay"] = "100"
            };
            if (maxOutputTokens is int limit)
            {
                settings["Benchmark:ReportPack:MaxOutputTokens"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

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
        public async Task<BenchmarkReportPackJob> RunCompletionAsync(BenchmarkReportAudience audience, IEnumerable<long>? replace = null)
        {
            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(Db, Writer, CancellationToken.None);
            var request = StandaloneRequest(new[] { audience });
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
                Documents = { new BenchmarkReportPackDocumentProgress { Audience = audience } }
            };
            await Jobs.WaitForSlotAsync(job, CancellationToken.None);

            await Service().WriteRunCompletionDocumentsAsync(job, CancellationToken.None);
            return job;
        }

        /// <summary>
        /// <paramref name="rounds"/> rounds of both suites of <see cref="BenchmarkBatteryTestData.Definition"/>, analysed;
        /// returns the battery run id. Round r is runs <paramref name="firstRunId"/> + 2(r − 1) (suite A) and the next
        /// (suite B) of <paramref name="modelId"/> by <paramref name="provider"/>, scoring <paramref name="shift"/> + 4(r − 1)
        /// above the suites' own scores. With <paramref name="panel"/> every run is a panel run with a reference reader
        /// (<see cref="AsRefereedPanelRun"/>); <paramref name="adjust"/> then changes each run before it is saved.
        /// </summary>
        public async Task<long> SeedBatteryAsync(
            long firstRunId = BatteryFirstRunId,
            string modelId = "gpt-5.6-luna",
            string provider = "OpenAI",
            int shift = 0,
            int rounds = 1,
            bool panel = false,
            Action<BenchmarkRun>? adjust = null)
        {
            var members = new List<(BenchmarkRun Run, int SuiteIndex, int Round)>();
            for (int round = 1; round <= rounds; round++)
            {
                long runId = firstRunId + 2 * (round - 1);
                int roundShift = shift + 4 * (round - 1);
                var suiteA = BenchmarkBatteryTestData.SuiteARun(runId, modelId, roundShift);
                var suiteB = BenchmarkBatteryTestData.SuiteBRun(runId + 1, modelId, roundShift);
                foreach (var run in new[] { suiteA, suiteB })
                {
                    if (!string.Equals(provider, "OpenAI", StringComparison.Ordinal))
                    {
                        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                            provider: provider, modelId: modelId, displayName: modelId, thinkingLevel: "high", maxOutputTokens: 32000);
                    }
                    if (panel) AsRefereedPanelRun(run);
                    adjust?.Invoke(run);
                }
                members.Add((suiteA, 0, round));
                members.Add((suiteB, 1, round));
            }

            long id = await BenchmarkBatteryTestData.SeedAsync(Db, BenchmarkBatteryTestData.Definition(), members.ToArray());
            var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(Db).AnalyseAsync(id, null, null, CancellationToken.None);
            Assert.True(analysis != null, error);
            return id;
        }

        /// <summary>
        /// A panel run (<see cref="BenchmarkBatteryTestData.AsPanelRun"/>: member A Google, member B Anthropic) whose
        /// reference reader, a second Google model, scores every answer as the panel does.
        /// </summary>
        public static BenchmarkRun AsRefereedPanelRun(BenchmarkRun run)
        {
            BenchmarkBatteryTestData.AsPanelRun(run);
            run.SecondOpinionAssessorModelConfigurationId = 11;
            run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-reader", displayName: "Gemini Reader");
            run.SecondOpinionEffectiveMaxOutputTokens = 16000;
            run.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.All;
            run.SecondOpinionBlindUsed = true;
            foreach (var answer in run.Answers)
            {
                answer.SecondOpinionQualityScore = answer.QualityScore;
                answer.SecondOpinionTrigger = "All";
            }
            return run;
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

        public AdminBenchmarkReportPacksController Controller(
            ModelMetadataService? metadata = null, BenchmarkReportPackPreviewCache? previewCache = null) => new(
            Db,
            Jobs,
            new BenchmarkComplianceGuard(Configuration, Db),
            new BenchmarkModelComparisonService(Db),
            new ModelPricingService(new ModelMetadataService(), Db),
            new EndpointPolicy(Configuration),
            scopeFactory: null!,
            Configuration,
            modelMetadata: metadata,
            previewCache: previewCache);

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
                TestKnowledgeBase.Empty,
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

        /// <summary>The finish reason of each call in turn; <c>stop</c> once it is empty.</summary>
        public ConcurrentQueue<string> FinishReasons { get; } = new();

        /// <summary>The reasoning tokens each call's usage report carries, inside its output tokens.</summary>
        public int ReasoningTokens { get; set; }

        /// <summary>The output token limit each request was sent.</summary>
        public ConcurrentQueue<int?> MaxOutputTokensLog { get; } = new();

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
            MaxOutputTokensLog.Enqueue(maxOutputTokens);
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
                    OutputTokens = OutputTokens,
                    ReasoningTokens = ReasoningTokens
                }
            };
            yield return new ChatEvent { Type = "finish_reason", Data = FinishReasons.TryDequeue(out var finish) ? finish : "stop" };
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
