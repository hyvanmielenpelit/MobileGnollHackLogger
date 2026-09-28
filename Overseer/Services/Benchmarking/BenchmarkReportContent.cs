namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The verbatim material a report-pack document may print at Detailed and Full disclosure, captured
/// from the subject's answer rows: the question text as asked, the rubric as graded, an answer
/// excerpt, each grader's comment and evidence, and the claim verifier's rulings. Never read from the
/// live suite. Pure: no I/O and no clock.
/// </summary>
public static class BenchmarkReportContent
{
    public const string Ellipsis = "…";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// One block per run in run-id order, each with its answers in order-index order, numbered as
    /// <see cref="BenchmarkReportFacts.NumberQuestions"/> numbers them.
    /// </summary>
    public static BenchmarkReportContentSnapshot Build(IReadOnlyList<BenchmarkRun> subjectRuns, int answerExcerptChars)
    {
        var runs = (subjectRuns ?? Array.Empty<BenchmarkRun>()).OrderBy(r => r.Id).ToList();
        var numberByItem = BenchmarkReportFacts.NumberQuestions(runs)
            .ToDictionary(s => s.ItemKey, s => s.Number, StringComparer.Ordinal);

        var snapshot = new BenchmarkReportContentSnapshot { AnswerExcerptChars = Math.Max(0, answerExcerptChars) };

        foreach (var run in runs)
        {
            var block = new BenchmarkReportContentRun { RunId = run.Id };
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);

            foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
            {
                var (excerpt, cut) = Excerpt(answer.AnswerText, snapshot.AnswerExcerptChars);

                block.Questions.Add(new BenchmarkReportContentQuestion
                {
                    Number = numberByItem[BenchmarkReportFacts.ItemKeyOf(answer)],
                    QuestionKey = BenchmarkReportFacts.QuestionKeyOf(answer),
                    ItemRevisionUsed = answer.ItemRevisionUsed,
                    OrderIndex = answer.OrderIndex,
                    Band = BenchmarkReportFacts.BandNameOf(answer),
                    QuestionText = answer.QuestionText ?? string.Empty,
                    ExpectedPoints = answer.ExpectedPointsRecorded ? answer.ExpectedPointsUsed : null,
                    ExpectedPointsRecorded = answer.ExpectedPointsRecorded,
                    AnswerExcerpt = excerpt,
                    AnswerExcerptCut = cut,
                    Graders = GradersOf(run, answer, panel),
                    ClaimRulings = RulingsOf(answer)
                });
            }

            snapshot.Runs.Add(block);
        }

        return snapshot;
    }

    /// <summary>
    /// The first <paramref name="maxChars"/> characters of <paramref name="text"/>, cut back to the
    /// last whitespace at or before that position (a hard cut when there is none), trailing whitespace
    /// removed and an ellipsis appended; the whole text when it fits.
    /// </summary>
    public static (string Excerpt, bool Cut) Excerpt(string? text, int maxChars)
    {
        text ??= string.Empty;
        if (text.Length <= maxChars) return (text, false);
        if (maxChars <= 0) return (Ellipsis, true);

        int cut = maxChars;
        for (int i = maxChars; i > 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                cut = i;
                break;
            }
        }

        return (text[..cut].TrimEnd() + Ellipsis, true);
    }

    /// <summary>Each grader that recorded a verdict on the answer, in role order.</summary>
    private static List<BenchmarkReportContentGrader> GradersOf(BenchmarkRun run, BenchmarkRunAnswer answer, bool panel)
    {
        var graders = new List<BenchmarkReportContentGrader>();

        if (answer.AssessmentStatus == BenchmarkAssessmentStatus.Scored)
        {
            var evidence = EvidenceOf(
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "accuracy"),
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "completeness"),
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "readability"),
                answer.CriticalError ? answer.CriticalErrorQuote : null);

            graders.Add(new BenchmarkReportContentGrader
            {
                Role = panel ? BenchmarkReportFacts.PanelMemberARole : BenchmarkReportFacts.AssessorRole,
                Label = (answer.AssessedByModelSnapshot ?? run.AssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.QualityScore,
                Comment = NullIfBlank(answer.ReviewComment),
                Evidence = evidence
            });
        }

        if (panel && answer.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored)
        {
            var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson);
            graders.Add(new BenchmarkReportContentGrader
            {
                Role = BenchmarkReportFacts.PanelMemberBRole,
                Label = (answer.CoAssessedByModelSnapshot ?? run.CoAssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.CoAssessmentQualityScore ?? record?.QualityScore,
                Comment = NullIfBlank(record?.Comment),
                Evidence = EvidenceOf(
                    record?.AccuracyEvidence,
                    record?.CompletenessEvidence,
                    record?.ReadabilityEvidence,
                    record?.CriticalError == true ? record!.CriticalErrorQuote : null)
            });
        }

        if (!string.IsNullOrWhiteSpace(answer.SecondOpinionJson))
        {
            string json = answer.SecondOpinionJson;
            bool critical = ReadBool(json, "criticalError");
            graders.Add(new BenchmarkReportContentGrader
            {
                Role = panel ? BenchmarkReportFacts.ReferenceReaderRole : BenchmarkReportFacts.SecondReaderRole,
                Label = (answer.SecondOpinionByModelSnapshot ?? run.SecondOpinionAssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.SecondOpinionQualityScore,
                Comment = NullIfBlank(ReadString(json, "comment")),
                Evidence = EvidenceOf(
                    ReadString(json, "accuracyEvidence"),
                    ReadString(json, "completenessEvidence"),
                    ReadString(json, "readabilityEvidence"),
                    critical ? ReadString(json, "criticalErrorQuote") : null)
            });
        }

        return graders;
    }

    private static List<string> EvidenceOf(string? accuracy, string? completeness, string? readability, string? criticalQuote)
    {
        var evidence = new List<string>();
        if (!string.IsNullOrWhiteSpace(accuracy)) evidence.Add("Accuracy: " + accuracy.Trim());
        if (!string.IsNullOrWhiteSpace(completeness)) evidence.Add("Completeness: " + completeness.Trim());
        if (!string.IsNullOrWhiteSpace(readability)) evidence.Add("Readability: " + readability.Trim());
        if (!string.IsNullOrWhiteSpace(criticalQuote)) evidence.Add("Critical error: \"" + criticalQuote.Trim() + "\"");
        return evidence;
    }

    /// <summary>The verifier's rulings as stored, each with its effective verdict; empty when none is stored or it is unreadable.</summary>
    private static List<BenchmarkReportContentClaimRuling> RulingsOf(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.ClaimVerificationJson)) return new List<BenchmarkReportContentClaimRuling>();

        List<BenchmarkClaimVerification>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(answer.ClaimVerificationJson, ReadOptions);
        }
        catch (JsonException)
        {
            return new List<BenchmarkReportContentClaimRuling>();
        }

        return (items ?? new List<BenchmarkClaimVerification>())
            .Where(v => v != null)
            .Select(v => new BenchmarkReportContentClaimRuling
            {
                Claim = v.Claim ?? string.Empty,
                Verdict = v.EffectiveVerdict.ToString().ToLowerInvariant(),
                Rationale = RationaleOf(v)
            })
            .ToList();
    }

    private static string? RationaleOf(BenchmarkClaimVerification v)
    {
        string? basis = NullIfBlank(v.Basis)?.Trim();
        string? citation = NullIfBlank(v.Citation)?.Trim();
        if (basis != null && citation != null) return basis + " (" + citation + ")";
        return basis ?? citation;
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ReadBool(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
