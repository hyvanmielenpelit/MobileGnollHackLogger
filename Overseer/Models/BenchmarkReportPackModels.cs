namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

// Report packs: AI-written documents about one model of a model comparison, in the context of its
// peers. Figures come from code (the fact sheet); the writer supplies only prose between them, and
// the document is rendered from stored JSON at download, deterministically and without a model call.
//
// The shapes in the first half are stored as JSON on BenchmarkReportDocument and read back by the
// renderer, so their property names are part of the stored format: renaming one is a
// ReportFormatVersion change.

// ---------------------------------------------------------------------------------------------
// Render options
// ---------------------------------------------------------------------------------------------

/// <summary>How much verbatim benchmark content a rendered document prints.</summary>
public enum BenchmarkReportDisclosure
{
    /// <summary>Questions described by topic; no question text, rubric, answer or grader evidence.</summary>
    Summary = 1,

    /// <summary>Verbatim question text and answer excerpts for the questions the notes discuss.</summary>
    Detailed = 2,

    /// <summary>Everything, rubrics and grader evidence included. Internal only.</summary>
    Full = 3,
}

/// <summary>Whether peers are printed by name or as "Model A", "Model B"….</summary>
public enum BenchmarkReportPeerNaming
{
    Named = 1,
    Anonymized = 2,
}

public sealed class BenchmarkReportRenderOptions
{
    public BenchmarkReportDisclosure Disclosure { get; init; } = BenchmarkReportDisclosure.Summary;
    public BenchmarkReportPeerNaming PeerNaming { get; init; } = BenchmarkReportPeerNaming.Anonymized;

    /// <summary>
    /// The italic stamp and the Date, Suite, Questions, Runs and Peers list under the title. The PDF
    /// and Word downloads leave them out, because their cover prints the same facts.
    /// </summary>
    public bool IncludeFrontMatter { get; init; } = true;
}

// ---------------------------------------------------------------------------------------------
// Audience slots
// ---------------------------------------------------------------------------------------------

/// <summary>
/// What the writer must and may supply for one audience. The prompt asks for exactly these slots,
/// the validator enforces them and the renderer places them.
/// </summary>
public sealed record BenchmarkReportAudienceSpec(
    BenchmarkReportAudience Audience,
    IReadOnlyList<string> RequiredSlots,
    int MaxStrengths,
    int MaxWeaknesses,
    bool UsesRecommendations,
    IReadOnlyList<string> RecommendationTargets,
    bool UsesQuestionNotes,
    bool RequiresQuestionTopics,
    bool UsesLeads);

public static class BenchmarkReportSlots
{
    // Executive Summary
    public const string Meaning = "meaning";         // What this means for use as a game assistant
    public const string Confidence = "confidence";   // How reliable this result is

    // Report for AI Researchers and Developers (the TechnicalReport audience)
    public const string Abstract = "abstract";       // ≤ 150 words
    public const string WhyItScored = "whyItScored"; // patterns behind the weaknesses, ≤ 300 words
    public const string WhatWorked = "whatWorked";   // patterns behind the strengths, ≤ 150 words

    // Internal Improvement Brief, in the order of What the Benchmark Is For
    public const string OverseerChat = "overseerChat";
    public const string BenchmarkSystem = "benchmarkSystem";
    public const string ModelResult = "modelResult";

    public const string TargetModelDevelopers = "model_developers";
    public const string TargetOverseerChat = "overseer_chat";
    public const string TargetBenchmark = "benchmark";

    public static readonly BenchmarkReportAudienceSpec ExecutiveSummary = new(
        BenchmarkReportAudience.ExecutiveSummary,
        new[] { Meaning, Confidence },
        MaxStrengths: 3,
        MaxWeaknesses: 3,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: false,
        UsesLeads: false);

    public static readonly BenchmarkReportAudienceSpec TechnicalReport = new(
        BenchmarkReportAudience.TechnicalReport,
        new[] { Abstract, WhyItScored, WhatWorked },
        MaxStrengths: 8,
        MaxWeaknesses: 8,
        UsesRecommendations: true,
        RecommendationTargets: new[] { TargetModelDevelopers },
        UsesQuestionNotes: true,
        RequiresQuestionTopics: true,
        UsesLeads: false);

    public static readonly BenchmarkReportAudienceSpec InternalBrief = new(
        BenchmarkReportAudience.InternalBrief,
        new[] { OverseerChat, BenchmarkSystem, ModelResult },
        MaxStrengths: 8,
        MaxWeaknesses: 8,
        UsesRecommendations: true,
        RecommendationTargets: new[] { TargetOverseerChat, TargetBenchmark, TargetModelDevelopers },
        UsesQuestionNotes: true,
        RequiresQuestionTopics: true,
        UsesLeads: true);

    public static BenchmarkReportAudienceSpec For(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport => TechnicalReport,
        BenchmarkReportAudience.InternalBrief => InternalBrief,
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, null)
    };
}

// ---------------------------------------------------------------------------------------------
// The fact sheet (stored as FactsJson)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// One computed figure. The writer cites it as <c>{{Key}}</c> and the renderer prints
/// <see cref="Display"/>. An unavailable fact (a degraded axis, a missing measure) keeps its key so
/// the writer and the renderer can say why it is missing.
/// </summary>
public sealed class BenchmarkReportFact
{
    public string Key { get; set; } = string.Empty;

    /// <summary>The raw value: a number, string, boolean or null.</summary>
    public JsonNode? Value { get; set; }

    /// <summary>Culture-invariant display string, e.g. <c>80 / 100</c>.</summary>
    public string Display { get; set; } = string.Empty;

    public bool Available { get; set; } = true;

    public string? UnavailableReason { get; set; }
}

/// <summary>A model of the comparison other than the subject, with its stored letter.</summary>
public sealed class BenchmarkReportPeer
{
    /// <summary><c>A</c>, <c>B</c>, … in quality-rank order, ties by entry key (ordinal).</summary>
    public string Letter { get; set; } = string.Empty;

    public string EntryKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public List<long> RunIds { get; set; } = new();

    /// <summary><c>Comparable</c> or <c>Degraded</c>; Excluded entries are never peers.</summary>
    public string State { get; set; } = string.Empty;

    public bool SpeedDegraded { get; set; }
    public bool CostDegraded { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>One grading role of the subject's runs, with its family relation to the subject.</summary>
public sealed class BenchmarkReportGrader
{
    /// <summary>e.g. <c>Panel member A</c>, <c>Panel member B</c>, <c>Assessor</c>, <c>Reference reader</c>, <c>Claim verifier</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }

    /// <summary>The grader's provider is the subject's.</summary>
    public bool SameFamilyAsSubject { get; set; }
}

/// <summary>Per-question figures of the subject, numbered as the report numbers them (<c>Q&lt;n&gt;</c>).</summary>
public sealed class BenchmarkReportQuestion
{
    /// <summary>1-based position in the subject's exam, by order index.</summary>
    public int Number { get; set; }

    public string QuestionKey { get; set; } = string.Empty;
    public int? ItemRevisionUsed { get; set; }
    public int OrderIndex { get; set; }

    /// <summary>The difficulty band's name.</summary>
    public string Band { get; set; } = string.Empty;

    /// <summary>The subject's published quality on this item; a mean over runs for a group.</summary>
    public double? Score { get; set; }

    /// <summary>The peers' mean quality on the same item and revision; null when no peer answered it.</summary>
    public double? PeerMean { get; set; }

    public double? Difference { get; set; }
    public int PeerCount { get; set; }

    public bool CriticalError { get; set; }

    /// <summary>The verifier's refuted ordinary claims of the answers (the harness's <c>ClaimsRefutedCount</c>).</summary>
    public int RefutedClaims { get; set; }

    /// <summary>
    /// Refuted verifications of the answers' own text: ordinary claims, sentences a grader accused and
    /// critical-error quotes, never a grader's statement. Null when a verification carries no roles
    /// (recorded before harness 31) and on a document stored before format version 3.
    /// </summary>
    public int? RefutedAnswerSentences { get; set; }

    public double ToolCalls { get; set; }
    public double? ModelTimeMs { get; set; }

    /// <summary>For a group: how many of its runs scored this item.</summary>
    public int RunCount { get; set; }
}

/// <summary>
/// One synthesis finding row the writer may cite as <c>R&lt;n&gt;</c>: a convergence row in a panel
/// run, a synthesis finding otherwise, merged across runs for a group.
/// </summary>
public sealed class BenchmarkReportFindingRow
{
    /// <summary><c>R1</c>, <c>R2</c>, … in the stored order.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>strength</c> or <c>weakness</c> (or another synthesis kind).</summary>
    public string Kind { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public List<int> Questions { get; set; } = new();

    /// <summary><c>Convergent</c>, <c>MemberAOnly</c>, <c>MemberBOnly</c>, <c>Conflicting</c> or <c>Single</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The computed support label, e.g. <c>Both graders</c>.</summary>
    public string SupportLabel { get; set; } = string.Empty;

    public string? MemberAText { get; set; }
    public string? MemberBText { get; set; }

    /// <summary>Runs of the subject in which this finding occurred (1 for a single run).</summary>
    public int Recurrence { get; set; } = 1;
}

/// <summary>
/// Everything computed about the subject and its peers. The writer sees it as data; the renderer
/// prints it; the validator checks citations against it.
/// </summary>
public sealed class BenchmarkReportFactSheet
{
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary><c>Run</c> or <c>Group</c>.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    public string SubjectLabel { get; set; } = string.Empty;
    public string SubjectDisplayName { get; set; } = string.Empty;
    public string SubjectProvider { get; set; } = string.Empty;
    public string SubjectModelId { get; set; } = string.Empty;
    public string? SubjectThinkingLevel { get; set; }
    public List<long> SubjectRunIds { get; set; } = new();

    /// <summary><c>Comparable</c> or <c>Degraded</c>.</summary>
    public string SubjectState { get; set; } = string.Empty;

    /// <summary>The comparison's sentence for the subject's entry: its state and what moved.</summary>
    public string SubjectExplanation { get; set; } = string.Empty;

    public long? SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;

    public List<BenchmarkReportPeer> Peers { get; set; } = new();
    public List<BenchmarkReportGrader> Graders { get; set; } = new();

    /// <summary>Sorted by key, ordinal.</summary>
    public List<BenchmarkReportFact> Facts { get; set; } = new();

    /// <summary>Ordered by <see cref="BenchmarkReportQuestion.Number"/>.</summary>
    public List<BenchmarkReportQuestion> Questions { get; set; } = new();

    /// <summary>Ordered by R number.</summary>
    public List<BenchmarkReportFindingRow> Rows { get; set; } = new();

    /// <summary>
    /// Names the digit rule masks before it looks for bare numbers: model labels, display names,
    /// model ids, providers, the suite name and grader labels. Ordered, ordinal.
    /// </summary>
    public List<string> KnownNames { get; set; } = new();

    /// <summary>
    /// The comparison's own "no pairwise significance test" statement and where to look instead,
    /// printed verbatim by the renderer.
    /// </summary>
    public string NoSignificanceSummary { get; set; } = string.Empty;

    public string NoSignificanceInstead { get; set; } = string.Empty;

    /// <summary>
    /// The subject runs' purpose statements as recorded at launch, distinct, in run-id order. The
    /// renderer prints them under Evaluation terms; empty on a document written before they were kept.
    /// </summary>
    public List<string> PurposeStatements { get; set; } = new();

    /// <summary>
    /// Figures of every non-excluded entry (subject first, then peers by letter), for the
    /// code-rendered comparison tables. Keyed by entry key in the row itself, never by dictionary order.
    /// </summary>
    public List<BenchmarkReportEntryFigures> Entries { get; set; } = new();
}

/// <summary>One entry's figures for the code-rendered comparison tables.</summary>
public sealed class BenchmarkReportEntryFigures
{
    public string EntryKey { get; set; } = string.Empty;

    /// <summary>Null for the subject.</summary>
    public string? PeerLetter { get; set; }

    public bool IsSubject { get; set; }

    public double? QualityIndex { get; set; }
    public double? QualityLower { get; set; }
    public double? QualityUpper { get; set; }
    public int? QualityRank { get; set; }

    public double? ModelTimeP50Ms { get; set; }
    public int? SpeedRank { get; set; }
    public bool SpeedDegraded { get; set; }

    public double? CostPerQuestionUsd { get; set; }
    public int? CostRank { get; set; }
    public bool CostDegraded { get; set; }

    public int RunCount { get; set; }

    /// <summary>Additional per-entry figures (dimensions, bands, tools), sorted by key.</summary>
    public List<BenchmarkReportFact> Extra { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// The content snapshot (stored as ContentJson)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Verbatim material captured from the subject's answer rows at generation — the text as asked,
/// the rubric as graded — never from the live suite. The renderer prints it at Detailed and Full;
/// the validator's no-disclosure rule compares the prose against it.
/// </summary>
public sealed class BenchmarkReportContentSnapshot
{
    public int AnswerExcerptChars { get; set; }

    /// <summary>One block per subject run, in run-id order.</summary>
    public List<BenchmarkReportContentRun> Runs { get; set; } = new();
}

public sealed class BenchmarkReportContentRun
{
    public long RunId { get; set; }

    /// <summary>In order-index order.</summary>
    public List<BenchmarkReportContentQuestion> Questions { get; set; } = new();
}

public sealed class BenchmarkReportContentQuestion
{
    /// <summary>The report's question number, matching <see cref="BenchmarkReportQuestion.Number"/>.</summary>
    public int Number { get; set; }

    public string QuestionKey { get; set; } = string.Empty;
    public int? ItemRevisionUsed { get; set; }
    public int OrderIndex { get; set; }
    public string Band { get; set; } = string.Empty;

    public string QuestionText { get; set; } = string.Empty;

    /// <summary>The rubric as graded (<c>ExpectedPointsUsed</c>); null when not recorded.</summary>
    public string? ExpectedPoints { get; set; }

    public bool ExpectedPointsRecorded { get; set; }

    /// <summary>
    /// At most <see cref="BenchmarkReportContentSnapshot.AnswerExcerptChars"/> characters, cut at a
    /// sentence end, a line break or whitespace and never inside a table, with an ellipsis when cut.
    /// </summary>
    public string AnswerExcerpt { get; set; } = string.Empty;

    public bool AnswerExcerptCut { get; set; }

    /// <summary>Each grader's comment and evidence, in role order.</summary>
    public List<BenchmarkReportContentGrader> Graders { get; set; } = new();

    public List<BenchmarkReportContentClaimRuling> ClaimRulings { get; set; } = new();
}

public sealed class BenchmarkReportContentGrader
{
    public string Role { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Comment { get; set; }
    public List<string> Evidence { get; set; } = new();
}

public sealed class BenchmarkReportContentClaimRuling
{
    public string Claim { get; set; } = string.Empty;

    /// <summary><c>supported</c>, <c>refuted</c> or <c>indeterminate</c>.</summary>
    public string Verdict { get; set; } = string.Empty;

    public string? Rationale { get; set; }

    /// <summary>
    /// Why the item was verified (<see cref="Overseer.Services.Benchmarking.BenchmarkReportContent"/>'s
    /// role constants): <c>claim</c>, <c>accusedSentence</c>, <c>criticalErrorQuote</c>,
    /// <c>assessorStatement</c> or <c>outOfRubricBasis</c>. Null when the verification recorded no
    /// roles, and on a document stored before format version 3.
    /// </summary>
    public string? Role { get; set; }
}

// ---------------------------------------------------------------------------------------------
// The writer's output (stored as WriterOutputJson, after validation)
// ---------------------------------------------------------------------------------------------

public sealed class BenchmarkReportWriterOutput
{
    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    /// <summary>Slot id → Markdown paragraphs. Rendered in the audience's slot order, never dictionary order.</summary>
    [JsonPropertyName("sections")]
    public Dictionary<string, string> Sections { get; set; } = new();

    [JsonPropertyName("strengths")]
    public List<BenchmarkReportWriterItem> Strengths { get; set; } = new();

    [JsonPropertyName("weaknesses")]
    public List<BenchmarkReportWriterItem> Weaknesses { get; set; } = new();

    [JsonPropertyName("recommendations")]
    public List<BenchmarkReportWriterRecommendation> Recommendations { get; set; } = new();

    [JsonPropertyName("questionTopics")]
    public List<BenchmarkReportQuestionTopic> QuestionTopics { get; set; } = new();

    [JsonPropertyName("questionNotes")]
    public List<BenchmarkReportQuestionNote> QuestionNotes { get; set; } = new();

    [JsonPropertyName("leads")]
    public List<BenchmarkReportLead> Leads { get; set; } = new();
}

public class BenchmarkReportWriterItem
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("questions")]
    public List<int> Questions { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = new();
}

public sealed class BenchmarkReportWriterRecommendation : BenchmarkReportWriterItem
{
    /// <summary><c>model_developers</c>, <c>overseer_chat</c> or <c>benchmark</c>.</summary>
    [JsonPropertyName("for")]
    public string For { get; set; } = string.Empty;
}

public sealed class BenchmarkReportLead : BenchmarkReportWriterItem
{
    /// <summary><c>harness</c>, <c>suite</c>, <c>chat</c> or <c>corpus</c>.</summary>
    [JsonPropertyName("triage")]
    public string Triage { get; set; } = string.Empty;
}

public sealed class BenchmarkReportQuestionTopic
{
    [JsonPropertyName("question")]
    public int Question { get; set; }

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;
}

public sealed class BenchmarkReportQuestionNote
{
    [JsonPropertyName("question")]
    public int Question { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>One validation problem, and whether the offending item was dropped (stored as ValidationNotesJson).</summary>
public sealed class BenchmarkReportValidationNote
{
    /// <summary>The D7 rule number, 1–12.</summary>
    public int Rule { get; set; }

    /// <summary>Where: <c>headline</c>, <c>sections.abstract</c>, <c>weaknesses[1]</c>, ….</summary>
    public string Location { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The item or paragraph was removed from the stored output.</summary>
    public bool Dropped { get; set; }
}

// ---------------------------------------------------------------------------------------------
// API DTOs
// ---------------------------------------------------------------------------------------------

/// <summary>Preview and start request. The first three fields mirror <see cref="BenchmarkModelComparisonRequest"/>.</summary>
public class BenchmarkReportPackRequest
{
    public List<long> RunIds { get; set; } = new();
    public List<long> GroupIds { get; set; } = new();
    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; } = BenchmarkModelComparisonPricingBasis.Current;

    /// <summary>The comparison entry key of the subject, <c>run:&lt;id&gt;</c> or <c>group:&lt;id&gt;</c>.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    public List<BenchmarkReportAudience> Audiences { get; set; } = new();

    public long WriterModelConfigurationId { get; set; }

    /// <summary>The operator acknowledged that the writer shares the subject's provider.</summary>
    public bool AcknowledgeSameProvider { get; set; }
}

public class BenchmarkReportPackPeerDto
{
    public string Letter { get; set; } = string.Empty;
    public string EntryKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public class BenchmarkReportPackAudienceEstimateDto
{
    public BenchmarkReportAudience Audience { get; set; }
    public int PromptChars { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }

    /// <summary>Null when the writer has no resolvable price.</summary>
    public double? EstimatedCostUsd { get; set; }
}

public class BenchmarkReportPackPreviewDto
{
    public string SubjectKey { get; set; } = string.Empty;
    public string SubjectLabel { get; set; } = string.Empty;
    public string SubjectState { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public List<BenchmarkReportPackPeerDto> Peers { get; set; } = new();
    public List<BenchmarkReportPackAudienceEstimateDto> Estimates { get; set; } = new();

    /// <summary>Sum over <see cref="Estimates"/> of the first call; a repair turn can roughly double a document.</summary>
    public double? EstimatedTotalCostUsd { get; set; }

    public string? WriterDisplayName { get; set; }

    /// <summary>The same-provider warning, or null. Starting requires acknowledging it.</summary>
    public string? SameProviderWarning { get; set; }

    /// <summary>Why the pack cannot be generated as requested, or null when it can.</summary>
    public string? Refusal { get; set; }
}

public class BenchmarkReportPackStartResponse
{
    public string JobId { get; set; } = string.Empty;
}

public class BenchmarkReportPackDocumentProgressDto
{
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary><c>Pending</c>, <c>Writing</c>, <c>Repairing</c>, <c>Completed</c>, <c>CompletedWithWarnings</c>, <c>Failed</c>, <c>Canceled</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public long? DocumentId { get; set; }
    public string? ErrorMessage { get; set; }
    public int ModelCalls { get; set; }

    /// <summary>When the document first became <c>Writing</c>; null while it waits.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When the document reached a terminal status; null until then.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    /// <summary>Null when no call of the document had a resolvable price.</summary>
    public double? CostUsd { get; set; }
}

public class BenchmarkReportPackJobLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
}

public class BenchmarkReportPackJobDto
{
    public string Id { get; set; } = string.Empty;
    public Guid PackId { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public string SubjectLabel { get; set; } = string.Empty;
    public long? SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long WriterConfigId { get; set; }
    public string WriterDisplayName { get; set; } = string.Empty;
    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary><c>Running</c>, <c>Completed</c>, <c>CompletedWithErrors</c>, <c>Canceled</c> or <c>Failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public int TotalModelCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public double? CostUsd { get; set; }
    public List<BenchmarkReportPackDocumentProgressDto> Documents { get; set; } = new();
    public List<BenchmarkReportPackJobLogEntryDto> Log { get; set; } = new();
}

/// <summary>A stored document in a list; never carries rendered text.</summary>
public class BenchmarkReportDocumentListItemDto
{
    public long Id { get; set; }
    public Guid PackId { get; set; }
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary>A report pack's document (1), or one written after its run completed (2).</summary>
    public BenchmarkReportDocumentOrigin Origin { get; set; }

    public string Title { get; set; } = string.Empty;
    public string SubjectKey { get; set; } = string.Empty;
    public string SubjectLabel { get; set; } = string.Empty;
    public List<long> SubjectRunIds { get; set; } = new();
    public long? SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public string WriterDisplayName { get; set; } = string.Empty;
    public string WriterProvider { get; set; } = string.Empty;
    public string WriterModelId { get; set; } = string.Empty;
    public string? WriterThinkingLevel { get; set; }
    public bool SameProviderAcknowledged { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ReportFormatVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long DurationMs { get; set; }
    public decimal? CostUsd { get; set; }

    /// <summary>A subject run was re-scored, re-run or deleted since the document was written.</summary>
    public bool RunChangedSinceGeneration { get; set; }

    /// <summary>Subject runs that no longer exist.</summary>
    public List<long> MissingRunIds { get; set; } = new();

    /// <summary>The disclosure levels this document renders at.</summary>
    public List<BenchmarkReportDisclosure> AllowedDisclosures { get; set; } = new();
}

public class BenchmarkReportDocumentDetailDto : BenchmarkReportDocumentListItemDto
{
    public string? PricingSource { get; set; }
    public int AnswerExcerptChars { get; set; }
    public string WriterPromptSha256 { get; set; } = string.Empty;
    public List<BenchmarkReportValidationNote> ValidationNotes { get; set; } = new();
    public string FactsJson { get; set; } = string.Empty;
}

/// <summary>Writes a finished run's missing run-completion documents now, with the given writer.</summary>
public class WriteRunReportDocumentsRequest
{
    public long WriterModelConfigurationId { get; set; }

    /// <summary>The documents to write; null or empty writes every missing one.</summary>
    public List<BenchmarkReportAudience>? Audiences { get; set; }

    /// <summary>The operator acknowledged that the writer shares the candidate's provider.</summary>
    public bool AcknowledgeSameProvider { get; set; }
}

public class WriteRunReportDocumentsResponse
{
    public long RunId { get; set; }

    /// <summary>The run's documents status once the job is queued: <see cref="BenchmarkRunReportDocumentsStatus.Pending"/>.</summary>
    public BenchmarkRunReportDocumentsStatus Status { get; set; }

    /// <summary>The documents the job will write.</summary>
    public List<BenchmarkReportAudience> Audiences { get; set; } = new();
}

/// <summary>
/// A run's run-completion job as this process knows it: while it runs, and for a few hours after it
/// finishes or until the run's next job. After a restart only the persisted status and the documents remain.
/// </summary>
public class BenchmarkRunReportJobDto
{
    public long RunId { get; set; }

    /// <summary>The run's persisted documents status.</summary>
    public BenchmarkRunReportDocumentsStatus Status { get; set; }

    /// <summary>The run's persisted documents message.</summary>
    public string? Message { get; set; }

    /// <summary><c>Queued</c>, <c>Preparing</c>, <c>Writing</c> or <c>Finished</c>.</summary>
    public string Phase { get; set; } = string.Empty;

    public DateTime QueuedAtUtc { get; set; }
    public DateTime? SlotAcquiredAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? CancelRequestedAtUtc { get; set; }

    /// <summary>Jobs waiting ahead of this one; set only while it is queued.</summary>
    public int? JobsAhead { get; set; }

    /// <summary>The running job this one waits for, <c>Report Pack: �</c> or <c>Run #N: �</c>; set only while queued.</summary>
    public string? BlockingJobLabel { get; set; }

    public List<BenchmarkReportAudience> Audiences { get; set; } = new();
    public long WriterConfigId { get; set; }
    public string WriterDisplayName { get; set; } = string.Empty;
    public string WriterProvider { get; set; } = string.Empty;
    public string WriterModelId { get; set; } = string.Empty;
    public string? WriterThinkingLevel { get; set; }

    /// <summary>Per-document progress, the log and the running totals.</summary>
    public BenchmarkReportPackJobDto Job { get; set; } = new();

    /// <summary>The server's clock when the view was built, for elapsed times the client shows.</summary>
    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>Estimates writing a run's documents; makes no model call.</summary>
public class BenchmarkRunReportEstimateRequest
{
    public long WriterModelConfigurationId { get; set; }

    /// <summary>The documents to estimate; null or empty estimates every missing one.</summary>
    public List<BenchmarkReportAudience>? Audiences { get; set; }
}

public class BenchmarkRunReportEstimateDto
{
    public List<BenchmarkReportPackAudienceEstimateDto> Estimates { get; set; } = new();

    /// <summary>Sum over <see cref="Estimates"/> of the first call; null when the writer has no resolvable price.</summary>
    public double? EstimatedTotalCostUsd { get; set; }

    /// <summary>Why the documents cannot be written with this writer, or null when they can.</summary>
    public string? Refusal { get; set; }

    /// <summary>Set when the writer shares the candidate's provider; writing then needs an acknowledgment.</summary>
    public SameProviderWarningDto? SameProviderWarning { get; set; }
}
