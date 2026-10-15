namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

/// <summary>
/// Startup step that deletes every stored document of a retired audience, 2 (Report for AI
/// Researchers and Developers) and 3 (Internal Improvement Brief), with its run fingerprint rows and
/// chart folder, and re-settles the documents status of each run and battery run that lost one.
///
/// <para>Safe to run on every start: neither audience can be written any more, so a second call finds
/// nothing. A document whose run or battery run is Pending or Writing is left for the next start.</para>
/// </summary>
public static class BenchmarkReportAudienceRetirement
{
    /// <summary>The retired <see cref="BenchmarkReportAudience"/> values, as stored.</summary>
    public static readonly IReadOnlyList<int> RetiredAudienceValues = new[] { 2, 3 };

    private const int BatchSize = 200;

    /// <summary>What one call removed.</summary>
    public sealed record Result(int TechnicalReports, int InternalBriefs, int ChartFoldersRemoved, int RunsResettled, int BatteryRunsResettled, int Skipped)
    {
        public int Deleted => TechnicalReports + InternalBriefs;
    }

    public static async Task<Result> RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var renderService = scope.ServiceProvider.GetService<BenchmarkReportRenderService>();
        var chartStore = scope.ServiceProvider.GetService<BenchmarkReportChartStore>();

        int technical = 0, internalBriefs = 0, charts = 0, skipped = 0;
        var runIds = new HashSet<long>();
        var batteryRunIds = new HashSet<long>();
        var seen = new HashSet<long>();
        var retired = RetiredAudienceValues.ToList();

        while (true)
        {
            var batch = await db.BenchmarkReportDocuments
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(d => retired.Contains((int)d.Audience) && !seen.Contains(d.Id))
                .OrderBy(d => d.Id)
                .Take(BatchSize)
                .Select(d => new { d.Id, d.Audience, d.Origin, d.SubjectKey })
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var d in batch)
            {
                seen.Add(d.Id);

                long? runId = d.Origin == BenchmarkReportDocumentOrigin.RunCompletion
                    && BenchmarkRunReportDocumentService.TryParseSubjectKey(d.SubjectKey, out long r) ? r : null;
                long? batteryRunId = d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion
                    && BenchmarkBatteryReportDocumentService.TryParseSubjectKey(d.SubjectKey, out long b) ? b : null;

                if (await IsWritingAsync(db, runId, batteryRunId, ct))
                {
                    skipped++;
                    logger.LogInformation(
                        "Retired-audience document {DocumentId} was kept for now: its {Subject} is being written.",
                        d.Id, d.SubjectKey);
                    continue;
                }

                bool chartsExisted = HasChartFolder(chartStore, d.Id);
                bool removed = renderService != null
                    ? await renderService.DeleteAsync(d.Id, ct)
                    : await BenchmarkReportRenderService.DeleteDocumentAsync(db, d.Id, ct);
                if (!removed) continue;

                if (chartsExisted && !HasChartFolder(chartStore, d.Id)) charts++;
                if ((int)d.Audience == 2) technical++; else internalBriefs++;
                if (runId != null) runIds.Add(runId.Value);
                if (batteryRunId != null) batteryRunIds.Add(batteryRunId.Value);
            }
        }

        foreach (long runId in runIds)
        {
            await BenchmarkRunReportDocumentService.ResettleAsync(db, runId, ct);
        }
        foreach (long batteryRunId in batteryRunIds)
        {
            await BenchmarkBatteryReportDocumentService.ResettleAsync(db, batteryRunId, ct);
        }

        var result = new Result(technical, internalBriefs, charts, runIds.Count, batteryRunIds.Count, skipped);
        if (result.Deleted > 0 || skipped > 0)
        {
            logger.LogInformation(
                "Retired {Deleted} documents (TechnicalReport {Technical}, InternalBrief {Internal}); charts removed {Charts}; {Runs} runs and {BatteryRuns} battery runs re-settled.",
                result.Deleted, technical, internalBriefs, charts, runIds.Count, batteryRunIds.Count);
        }
        return result;
    }

    private static bool HasChartFolder(BenchmarkReportChartStore? chartStore, long documentId)
        => chartStore is { IsConfigured: true } && Directory.Exists(chartStore.GetDocumentFolder(documentId));

    private static async Task<bool> IsWritingAsync(ApplicationDbContext db, long? runId, long? batteryRunId, CancellationToken ct)
    {
        if (runId is long r)
        {
            var status = await db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes()
                .Where(x => x.Id == r).Select(x => (BenchmarkRunReportDocumentsStatus?)x.ReportDocumentsStatus)
                .FirstOrDefaultAsync(ct);
            if (status is { } s && BenchmarkRunReportDocumentService.IsInProgress(s)) return true;
        }
        if (batteryRunId is long b)
        {
            var status = await db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes()
                .Where(x => x.Id == b).Select(x => (BenchmarkRunReportDocumentsStatus?)x.ReportDocumentsStatus)
                .FirstOrDefaultAsync(ct);
            if (status is { } s && BenchmarkRunReportDocumentService.IsInProgress(s)) return true;
        }
        return false;
    }
}
