namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

/// <summary>
/// The members of a two-family assessor panel. <see cref="A"/> is the run's assessor,
/// <see cref="B"/> its co-assessor.
/// </summary>
[Flags]
public enum BenchmarkPanelMember
{
    A = 1,
    B = 2,
    Both = 3
}

/// <summary>
/// One member's per-answer verdict, read from the columns that member writes: member A's from the
/// primary columns and <see cref="BenchmarkRunAnswer.AnswerFlags"/>, member B's from
/// <see cref="BenchmarkRunAnswer.CoAssessmentJson"/>. Read-only; nothing is written through it.
/// </summary>
public sealed record BenchmarkVerdictView(
    BenchmarkPanelMember Member,
    int AccuracyLevel,
    int CompletenessLevel,
    int ConcisenessLevel,
    int ReadabilityLevel,
    bool CriticalError,
    string? CriticalErrorQuote,
    int? QualityScore,
    string? Comment,
    string? AccuracyEvidence,
    string? CompletenessEvidence,
    IReadOnlyList<string> UnverifiedClaims,
    bool OutOfRubricAccuracy,
    bool ContestedVerdict)
{
    /// <summary>The label <see cref="BenchmarkClaimVerification.RaisedBy"/> records for this member: <c>"A"</c> or <c>"B"</c>.</summary>
    [JsonIgnore]
    public string MemberLabel => LabelOf(Member);

    /// <summary><c>"A"</c> for member A, <c>"B"</c> for member B.</summary>
    public static string LabelOf(BenchmarkPanelMember member) => member == BenchmarkPanelMember.B ? "B" : "A";

    /// <summary>
    /// Member A's verdict. Null unless the answer's assessment is <c>Scored</c> with all four levels
    /// recorded: an answer scored without a grader (a model-produced empty answer) has no verdict.
    /// </summary>
    public static BenchmarkVerdictView? FromPrimary(BenchmarkRunAnswer a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.AssessmentStatus != BenchmarkAssessmentStatus.Scored
            || a.AccuracyLevel is not int accuracy
            || a.CompletenessLevel is not int completeness
            || a.ConcisenessLevel is not int conciseness
            || a.ReadabilityLevel is not int readability)
        {
            return null;
        }

        var flags = (BenchmarkAnswerFlags)a.AnswerFlags;
        return new BenchmarkVerdictView(
            BenchmarkPanelMember.A,
            accuracy,
            completeness,
            conciseness,
            readability,
            a.CriticalError,
            a.CriticalErrorQuote,
            a.QualityScore,
            a.ReviewComment,
            BenchmarkAssessmentParser.ReadEvidenceField(a.AssessmentEvidenceJson, "accuracy"),
            BenchmarkAssessmentParser.ReadEvidenceField(a.AssessmentEvidenceJson, "completeness"),
            ReadClaims(a.UnverifiedClaimsJson),
            flags.HasFlag(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction),
            flags.HasFlag(BenchmarkAnswerFlags.ContestedVerdict));
    }

    /// <summary>
    /// Member B's verdict. Null unless <see cref="BenchmarkRunAnswer.CoAssessmentStatus"/> is
    /// <c>Scored</c> and <see cref="BenchmarkRunAnswer.CoAssessmentJson"/> records all four levels.
    /// </summary>
    public static BenchmarkVerdictView? FromCoAssessment(BenchmarkRunAnswer a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.CoAssessmentStatus != BenchmarkAssessmentStatus.Scored)
        {
            return null;
        }

        var record = BenchmarkCoAssessmentRecord.Parse(a.CoAssessmentJson);
        if (record == null
            || record.AccuracyLevel is not int accuracy
            || record.CompletenessLevel is not int completeness
            || record.ConcisenessLevel is not int conciseness
            || record.ReadabilityLevel is not int readability)
        {
            return null;
        }

        return new BenchmarkVerdictView(
            BenchmarkPanelMember.B,
            accuracy,
            completeness,
            conciseness,
            readability,
            record.CriticalError,
            record.CriticalErrorQuote,
            record.QualityScore ?? a.CoAssessmentQualityScore,
            record.Comment,
            record.AccuracyEvidence,
            record.CompletenessEvidence,
            record.UnverifiedClaims ?? new List<string>(),
            record.Flags?.OutOfRubricAccuracy ?? false,
            record.Flags?.ContestedVerdict ?? false);
    }

    /// <summary>The view of <paramref name="member"/>: <see cref="FromPrimary"/> for A, <see cref="FromCoAssessment"/> for B.</summary>
    public static BenchmarkVerdictView? For(BenchmarkRunAnswer a, BenchmarkPanelMember member)
        => member == BenchmarkPanelMember.B ? FromCoAssessment(a) : FromPrimary(a);

    private static IReadOnlyList<string> ReadClaims(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// Member B's full verdict as <see cref="BenchmarkRunAnswer.CoAssessmentJson"/> stores it. The levels
/// are null only on a record written without a grader (a model-produced empty answer, scored 0).
/// </summary>
public sealed class BenchmarkCoAssessmentRecord
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    [JsonPropertyName("accuracyLevel")]
    public int? AccuracyLevel { get; set; }

    [JsonPropertyName("completenessLevel")]
    public int? CompletenessLevel { get; set; }

    [JsonPropertyName("concisenessLevel")]
    public int? ConcisenessLevel { get; set; }

    [JsonPropertyName("readabilityLevel")]
    public int? ReadabilityLevel { get; set; }

    [JsonPropertyName("criticalError")]
    public bool CriticalError { get; set; }

    [JsonPropertyName("criticalErrorQuote")]
    public string? CriticalErrorQuote { get; set; }

    [JsonPropertyName("criticalErrorDemoted")]
    public bool CriticalErrorDemoted { get; set; }

    [JsonPropertyName("qualityScore")]
    public int? QualityScore { get; set; }

    [JsonPropertyName("rawQualityScore")]
    public int? RawQualityScore { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("accuracyEvidence")]
    public string? AccuracyEvidence { get; set; }

    [JsonPropertyName("completenessEvidence")]
    public string? CompletenessEvidence { get; set; }

    [JsonPropertyName("readabilityEvidence")]
    public string? ReadabilityEvidence { get; set; }

    /// <summary>Deduplicated as the primary column's are (<c>BenchmarkService.DeduplicateUnverifiedClaims</c>).</summary>
    [JsonPropertyName("unverifiedClaims")]
    public List<string>? UnverifiedClaims { get; set; }

    [JsonPropertyName("flags")]
    public BenchmarkCoAssessmentFlags? Flags { get; set; }

    /// <summary>The record, or null for a null, blank or malformed value.</summary>
    public static BenchmarkCoAssessmentRecord? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<BenchmarkCoAssessmentRecord>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);
}

/// <summary>
/// Member B's advisory flags: the counterparts of the <see cref="BenchmarkAnswerFlags"/> bits member A
/// sets on the answer. The two <c>contested*</c> flags are written by claim verification.
/// </summary>
public sealed class BenchmarkCoAssessmentFlags
{
    [JsonPropertyName("contestedVerdict")]
    public bool ContestedVerdict { get; set; }

    [JsonPropertyName("unevidencedDeduction")]
    public bool UnevidencedDeduction { get; set; }

    [JsonPropertyName("omissionAsAccuracy")]
    public bool OmissionAsAccuracy { get; set; }

    [JsonPropertyName("outOfRubricAccuracy")]
    public bool OutOfRubricAccuracy { get; set; }

    [JsonPropertyName("dimensionOutlier")]
    public bool DimensionOutlier { get; set; }

    [JsonPropertyName("completenessOutOfScope")]
    public bool CompletenessOutOfScope { get; set; }

    [JsonPropertyName("readabilityFormOnly")]
    public bool ReadabilityFormOnly { get; set; }

    [JsonPropertyName("contestedCriticalError")]
    public bool ContestedCriticalError { get; set; }

    [JsonPropertyName("contestedAccuracyDeduction")]
    public bool ContestedAccuracyDeduction { get; set; }
}
