namespace Overseer.Services.Benchmarking.Charts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Models;

/// <summary>What one drawing of a document's server charts did: the charts drawn, and the failure, if any, in words.</summary>
public sealed record RootCauseChartPublishResult(int ChartCount, string? Error)
{
    public bool Succeeded => Error == null;
}

/// <summary>
/// Draws a Root Cause Analysis's server charts (<see cref="RootCauseChartRenderer"/>) from its fact sheet
/// and stores them in the document's chart set (<see cref="BenchmarkReportChartStore.SetServerChartsAsync"/>),
/// replacing the server-drawn charts stored before and keeping the uploaded ones. A document with peers,
/// or of comparison scope, gets both the named and the anonymized variant; a stand-alone document the
/// named one only. Used when a Root Cause Analysis is stored and by the redraw endpoint.
/// </summary>
public class RootCauseChartPublisher
{
    public const string NoRootCauseMessage = "The fact sheet holds no root-cause analysis to draw the charts from.";

    private readonly BenchmarkReportChartStore _charts;
    private readonly ILogger<RootCauseChartPublisher> _logger;

    public RootCauseChartPublisher(BenchmarkReportChartStore charts, ILogger<RootCauseChartPublisher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(charts);
        _charts = charts;
        _logger = logger ?? NullLogger<RootCauseChartPublisher>.Instance;
    }

    /// <summary>
    /// Draws and stores the document's server charts and returns how many charts were drawn (each figure
    /// counted once, whatever its variants), logging <c>Root Cause Analysis: N charts drawn</c>. A drawing
    /// or storage failure is logged as a warning and returns 0; only cancellation throws.
    /// </summary>
    public async Task<int> DrawAndStoreAsync(long documentId, BenchmarkReportFactSheet sheet, CancellationToken ct)
        => (await PublishAsync(documentId, sheet, ct)).ChartCount;

    /// <summary>
    /// As <see cref="DrawAndStoreAsync"/>, with the failure in words for a caller that reports it. With
    /// no row to draw in any chart, the server-drawn charts stored before are removed and the count is 0.
    /// </summary>
    public async Task<RootCauseChartPublishResult> PublishAsync(long documentId, BenchmarkReportFactSheet sheet, CancellationToken ct)
    {
        if (sheet?.RootCause is not { } result)
        {
            _logger.LogWarning("Root Cause Analysis charts of document {DocumentId} were not drawn: {Reason}", documentId, NoRootCauseMessage);
            return new RootCauseChartPublishResult(0, NoRootCauseMessage);
        }

        if (!_charts.IsConfigured)
        {
            _logger.LogWarning("Root Cause Analysis charts of document {DocumentId} were not drawn: chart storage is not configured.", documentId);
            return new RootCauseChartPublishResult(0, BenchmarkReportChartStore.NotConfiguredMessage);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            bool bothVariants = sheet.IsComparison || sheet.Peers.Count > 0;

            var charts = new List<ValidatedChart>();
            var named = RootCauseChartRenderer.Draw(result, sheet, anonymized: false);
            charts.AddRange(named.Select(c => Validate(c, BenchmarkReportChartStore.Named)));
            if (bothVariants)
            {
                var anonymized = RootCauseChartRenderer.Draw(result, sheet, anonymized: true);
                charts.AddRange(anonymized.Select(c => Validate(c, BenchmarkReportChartStore.Anonymized)));
            }

            ct.ThrowIfCancellationRequested();
            await _charts.SetServerChartsAsync(documentId, charts, ct);

            int count = charts.Select(c => c.FigureKey).Distinct(StringComparer.Ordinal).Count();
            _logger.LogInformation("Root Cause Analysis: {Count} charts drawn (document {DocumentId}).", count, documentId);
            return new RootCauseChartPublishResult(count, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Root Cause Analysis charts of document {DocumentId} could not be drawn or stored.", documentId);
            string error = ex is ChartStoreException ? ex.Message : "The charts could not be drawn: " + ex.Message;
            return new RootCauseChartPublishResult(0, error);
        }
    }

    private static ValidatedChart Validate(DrawnChart chart, string naming)
        => BenchmarkReportChartStore.ValidateServerChart(chart.Key, naming, chart.Title, chart.Caption, chart.AltText, chart.Png);
}
