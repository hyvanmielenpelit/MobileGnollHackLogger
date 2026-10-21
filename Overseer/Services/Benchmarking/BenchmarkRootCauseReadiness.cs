namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Whether an Executive Summary may be written from a subject's stored Root Cause Analysis: only from the newest one of
/// the subject, origin and scope, and only when its fact sheet's facts hash to the new sheet's
/// (<see cref="BenchmarkRootCauseDigest.FactsSha256"/>) and it yields a digest. An Executive Summary is written only
/// from a Root Cause Analysis: the one written earlier in its own job, or the stored one judged here. Chat consistency
/// documents are not read here.
/// </summary>
public static class BenchmarkRootCauseReadiness
{
    /// <summary>No Root Cause Analysis of the subject is stored.</summary>
    public const string Missing = "missing";

    /// <summary>The newest stored Root Cause Analysis was written from the same data, and its digest can be read.</summary>
    public const string Usable = "usable";

    /// <summary>The newest stored Root Cause Analysis was written from other data, cannot be read, or the subject cannot be prepared.</summary>
    public const string Outdated = "outdated";

    /// <summary>The code of a refusal for <see cref="Missing"/>.</summary>
    public const string MissingCode = "rootCauseMissing";

    /// <summary>The code of a refusal for <see cref="Outdated"/>.</summary>
    public const string OutdatedCode = "rootCauseOutdated";

    /// <summary>The label a comparison-scope message names its covered models by.</summary>
    public const string CoveredModelsLabel = "these models";

    /// <summary>
    /// <c>The Executive Summary is written from the Root Cause Analysis, and this run has none. …</c>, with
    /// <paramref name="subject"/> <c>run</c> or <c>battery run</c>.
    /// </summary>
    public static string MissingMessage(string subject)
        => $"The Executive Summary is written from the Root Cause Analysis, and this {subject} has none. Write both: check Root Cause Analysis too.";

    /// <summary><c>The stored Root Cause Analysis (#77) was written from different data than this run has now, …</c></summary>
    public static string OutdatedMessage(string subject, long documentId)
        => $"The stored Root Cause Analysis (#{Inv(documentId)}) was written from different data than this {subject} has now, "
           + "so the Executive Summary cannot be written from it. Rewrite the Root Cause Analysis with it: check Root Cause Analysis — rewrite.";

    /// <summary>The Report Pack's <see cref="MissingMessage"/>, naming the model, or <see cref="CoveredModelsLabel"/>.</summary>
    public static string ReportPackMissingMessage(string label)
        => $"The Executive Summary for {label} is written from the Root Cause Analysis, and there is none for {label} in this comparison. "
           + "Write both: check Write on the Root Cause Analysis too.";

    /// <summary>The Report Pack's <see cref="OutdatedMessage"/>, naming the model, or <see cref="CoveredModelsLabel"/>.</summary>
    public static string ReportPackOutdatedMessage(string label, long documentId)
        => $"The stored Root Cause Analysis (#{Inv(documentId)}) for {label} was written from different data than this comparison has now, "
           + "so the Executive Summary cannot be written from it. Rewrite the Root Cause Analysis with it: check Rewrite on the Root Cause Analysis.";

    /// <summary>
    /// The documents ask for an Executive Summary without its Root Cause Analysis, so the summary needs a usable stored one.
    /// A replaced Root Cause Analysis is among the documents written.
    /// </summary>
    public static bool AsksSummaryAlone(IEnumerable<BenchmarkReportAudience> audiences)
    {
        ArgumentNullException.ThrowIfNull(audiences);
        var list = audiences as ICollection<BenchmarkReportAudience> ?? audiences.ToList();
        return list.Contains(BenchmarkReportAudience.ExecutiveSummary) && !list.Contains(BenchmarkReportAudience.RootCauseAnalysis);
    }

    /// <summary>The noun a run's or a battery run's messages name their subject by.</summary>
    public static string SubjectNounOf(BenchmarkReportDocumentOrigin origin)
        => origin == BenchmarkReportDocumentOrigin.BatteryCompletion ? "battery run" : "run";

    /// <summary>
    /// The scope and subject key a preparation's Root Cause Analyses are stored under for <paramref name="origin"/>: a
    /// comparison-scope Report Pack's sheet subject key, else the subject's entry key at model scope.
    /// </summary>
    public static (BenchmarkReportScope Scope, string SubjectKey) SubjectOf(BenchmarkReportDocumentOrigin origin, BenchmarkReportPackPreparation prep)
    {
        ArgumentNullException.ThrowIfNull(prep);
        var scope = origin == BenchmarkReportDocumentOrigin.ReportPack ? prep.Scope : BenchmarkReportScope.Model;
        return (scope, scope == BenchmarkReportScope.Comparison ? prep.Sheet.SubjectKey : prep.Subject.Key);
    }

    /// <summary>
    /// The label a Report Pack message names its subject by: <see cref="CoveredModelsLabel"/> at comparison scope, the
    /// subject's label otherwise; null for a run or a battery run, whose messages name the subject by its noun.
    /// </summary>
    public static string? LabelOf(BenchmarkReportDocumentOrigin origin, BenchmarkReportPackPreparation prep)
    {
        ArgumentNullException.ThrowIfNull(prep);
        if (origin != BenchmarkReportDocumentOrigin.ReportPack) return null;
        return prep.Scope == BenchmarkReportScope.Comparison ? CoveredModelsLabel : prep.Subject.Label;
    }

    /// <summary>
    /// The newest stored Root Cause Analysis of the subject, <paramref name="origin"/> and <paramref name="scope"/>, by id;
    /// with <paramref name="comparisonKey"/>, the newest of that comparison only. Null when none is stored.
    /// </summary>
    public static Task<BenchmarkReportDocument?> NewestAsync(
        ApplicationDbContext db, BenchmarkReportDocumentOrigin origin, BenchmarkReportScope scope, string subjectKey, CancellationToken ct,
        string? comparisonKey = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.SubjectKey == subjectKey && d.Audience == BenchmarkReportAudience.RootCauseAnalysis
                        && d.Origin == origin && d.Scope == scope);
        if (comparisonKey != null) query = query.Where(d => d.ComparisonKey == comparisonKey);
        return query.OrderByDescending(d => d.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The readiness of the subject of <paramref name="prep"/> for an Executive Summary of <paramref name="origin"/>:
    /// <see cref="Missing"/> with nothing stored, <see cref="Usable"/> or <see cref="Outdated"/> by the newest stored Root
    /// Cause Analysis (<see cref="Evaluate"/>). <paramref name="comparisonKey"/> limits a Report Pack subject to its comparison.
    /// </summary>
    public static async Task<BenchmarkRootCauseReadinessDto> ForSubjectAsync(
        ApplicationDbContext db, BenchmarkReportDocumentOrigin origin, BenchmarkReportScope scope, string subjectKey,
        BenchmarkReportPackPreparation prep, CancellationToken ct, string? comparisonKey = null)
        => (await EvaluateAsync(db, origin, scope, subjectKey, prep, ct, comparisonKey)).Readiness;

    /// <summary>As <see cref="ForSubjectAsync"/>, with the digest of a usable Root Cause Analysis.</summary>
    public static async Task<(BenchmarkRootCauseReadinessDto Readiness, BenchmarkRootCauseDigest? Digest)> EvaluateAsync(
        ApplicationDbContext db, BenchmarkReportDocumentOrigin origin, BenchmarkReportScope scope, string subjectKey,
        BenchmarkReportPackPreparation prep, CancellationToken ct, string? comparisonKey = null)
    {
        ArgumentNullException.ThrowIfNull(prep);
        var newest = await NewestAsync(db, origin, scope, subjectKey, ct, comparisonKey);
        return Evaluate(newest, BenchmarkRootCauseDigest.FactsSha256(prep.Sheet), origin, LabelOf(origin, prep));
    }

    /// <summary>
    /// The readiness the newest stored Root Cause Analysis <paramref name="newest"/> gives a sheet whose facts hash to
    /// <paramref name="factsSha256"/>: <see cref="Missing"/> when it is null; <see cref="Usable"/>, with its digest, when
    /// its sheet's facts hash the same and it yields a digest; <see cref="Outdated"/> otherwise, an unreadable sheet or
    /// writer output included. The messages name a run or a battery run by its noun, a Report Pack subject by
    /// <paramref name="label"/>.
    /// </summary>
    public static (BenchmarkRootCauseReadinessDto Readiness, BenchmarkRootCauseDigest? Digest) Evaluate(
        BenchmarkReportDocument? newest, string factsSha256, BenchmarkReportDocumentOrigin origin, string? label)
    {
        if (newest == null) return (MissingFor(origin, label), null);

        var digest = UsableDigest(newest, factsSha256);
        if (digest != null)
        {
            return (new BenchmarkRootCauseReadinessDto { State = Usable, RootCauseDocumentId = newest.Id }, digest);
        }

        return (new BenchmarkRootCauseReadinessDto
        {
            State = Outdated,
            RootCauseDocumentId = newest.Id,
            Message = label != null ? ReportPackOutdatedMessage(label, newest.Id) : OutdatedMessage(SubjectNounOf(origin), newest.Id),
            Code = OutdatedCode
        }, null);
    }

    /// <summary><see cref="Missing"/>, its message naming a run or a battery run by its noun, a Report Pack subject by <paramref name="label"/>.</summary>
    public static BenchmarkRootCauseReadinessDto MissingFor(BenchmarkReportDocumentOrigin origin, string? label) => new()
    {
        State = Missing,
        Message = label != null ? ReportPackMissingMessage(label) : MissingMessage(SubjectNounOf(origin)),
        Code = MissingCode
    };

    /// <summary>
    /// The readiness of a subject that cannot be prepared: <see cref="Outdated"/> with the preparation's refusal and no
    /// code, naming the newest stored Root Cause Analysis when there is one.
    /// </summary>
    public static BenchmarkRootCauseReadinessDto Unprepared(long? newestDocumentId, string refusal) => new()
    {
        State = Outdated,
        RootCauseDocumentId = newestDocumentId,
        Message = refusal,
        Code = null
    };

    /// <summary>The digest of <paramref name="document"/> when its sheet's facts hash to <paramref name="factsSha256"/>; null otherwise.</summary>
    private static BenchmarkRootCauseDigest? UsableDigest(BenchmarkReportDocument document, string factsSha256)
    {
        if (document.Audience != BenchmarkReportAudience.RootCauseAnalysis || string.IsNullOrWhiteSpace(document.FactsJson)) return null;
        try
        {
            var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
            if (!string.Equals(BenchmarkRootCauseDigest.FactsSha256(sheet), factsSha256, StringComparison.Ordinal)) return null;

            var output = string.IsNullOrWhiteSpace(document.WriterOutputJson)
                ? new BenchmarkReportWriterOutput()
                : BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
            return BenchmarkRootCauseDigest.From(document.Id, sheet, output);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
