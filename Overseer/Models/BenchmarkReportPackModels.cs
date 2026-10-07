namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Verbatim text and answer excerpts for every question; no rubric or grader evidence.</summary>
    Detailed = 2,

    /// <summary>
    /// Everything: complete answers where they were captured, rubrics and grader evidence included.
    /// Internal only.
    /// </summary>
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

    /// <summary>
    /// The closing document ID, version, writer and provenance lines. The PDF and Word downloads
    /// leave them out, because their cover prints the same facts.
    /// </summary>
    public bool IncludeDocumentFooter { get; init; } = true;

    /// <summary>The fact sheet section of the Internal Improvement Brief. The PDF and Word downloads leave it out.</summary>
    public bool IncludeFactSheet { get; init; } = true;

    /// <summary>The chart figures placed in the document; empty for none.</summary>
    public IReadOnlyList<BenchmarkReportRenderChart> Charts { get; init; } = Array.Empty<BenchmarkReportRenderChart>();
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
    bool UsesLeads)
{
    /// <summary>Slots of <see cref="RequiredSlots"/> that a document with peers requires and a stand-alone one never has.</summary>
    public IReadOnlyList<string> PeerOnlySlots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// A comparison-scope document: every covered model is described as an equal, by its letter, and
    /// there is no subject.
    /// </summary>
    public bool ComparisonScope { get; init; }

    /// <summary>A chat consistency document: one model's chat measured over two periods, with no questions and no peers.</summary>
    public bool ChatConsistencyScope { get; init; }

    /// <summary>The strengths and weaknesses lists; a comparison-scope document has neither.</summary>
    public bool UsesStrengthsAndWeaknesses { get; init; } = true;

    /// <summary>The most points each model gets in the <c>models</c> list; 0 when the document has no such list.</summary>
    public int MaxModelPoints { get; init; }

    /// <summary>The leads the document holds where it uses them.</summary>
    public int MaxLeads { get; init; } = 6;

    /// <summary>The values a lead's <c>triage</c> may take.</summary>
    public IReadOnlyList<string> LeadTriages { get; init; } = new[] { "harness", "suite", "chat", "corpus" };

    /// <summary>The slots a document requires: every slot with peers, the slots outside <see cref="PeerOnlySlots"/> without.</summary>
    public IReadOnlyList<string> SlotsFor(bool hasPeers)
        => hasPeers ? RequiredSlots : RequiredSlots.Where(s => !PeerOnlySlots.Contains(s, StringComparer.Ordinal)).ToList();
}

public static class BenchmarkReportSlots
{
    // Executive Summary
    public const string Comparison = "comparison";   // How it compares, ≤ 70 words, peer mode only
    public const string Meaning = "meaning";         // What this means for use as a game assistant
    public const string Confidence = "confidence";   // How reliable this result is

    // Report for AI Researchers and Developers (the TechnicalReport audience)
    public const string Abstract = "abstract";       // ≤ 150 words
    public const string WhyItScored = "whyItScored"; // patterns behind the weaknesses, ≤ 300 words
    public const string WhatWorked = "whatWorked";   // patterns behind the strengths, ≤ 150 words
    public const string Limitations = "limitations"; // the last paragraph of Threats to validity, ≤ 120 words

    // Internal Improvement Brief, in the order of What the Benchmark Is For
    public const string OverseerChat = "overseerChat";       // ≤ 200 words
    public const string BenchmarkSystem = "benchmarkSystem"; // ≤ 150 words
    public const string ModelResult = "modelResult";         // ≤ 150 words

    // Comparison scope: Executive Summary
    public const string Overview = "overview";               // The comparison in one paragraph
    public const string WhichModel = "whichModel";           // Which model to use
    public const string TradeOffs = "tradeOffs";             // Trade-offs
    public const string Reliability = "reliability";         // How reliable this is

    // Comparison scope: Report for AI Researchers and Developers (abstract and limitations as above)
    public const string Results = "results";                     // after the results table and paired tests
    public const string DimensionProfiles = "dimensionProfiles";
    public const string Frontier = "frontier";                   // Speed and cost frontier
    public const string QuestionPatterns = "questionPatterns";   // Cross-model question patterns
    public const string GraderReliability = "graderReliability";

    // Comparison scope: Internal Improvement Brief (benchmarkSystem as above)
    public const string SharedGaps = "sharedGaps";
    public const string ModelGaps = "modelGaps";

    // Chat consistency scope: Executive Summary
    public const string AsGoodAsBefore = "asGoodAsBefore";     // Is the Overseer chat with this model as good as before?
    public const string PlayerImpact = "playerImpact";         // What changed for players
    public const string OurChanges = "ourChanges";             // Our changes and their effect
    public const string ProviderChanges = "providerChanges";   // Provider-side changes
    public const string ConfidenceAndScope = "confidenceAndScope"; // Confidence and scope
    public const string NextRuns = "nextRuns";                 // Next runs (also in the Internal Brief)

    // Chat consistency scope: Report for AI Researchers and Developers (limitations as above)
    public const string QuestionAndDesign = "questionAndDesign";   // Question and design
    public const string RunsAndCoverage = "runsAndCoverage";       // Runs, coverage and scope
    public const string OverseerEvents = "overseerEvents";         // Overseer events
    public const string EndpointResults = "endpointResults";       // Results by endpoint
    public const string Attribution = "attribution";               // Attribution
    public const string Robustness = "robustness";                 // Robustness
    public const string Reproducibility = "reproducibility";       // Reproducibility

    // Chat consistency scope: Internal Brief (nextRuns as above)
    public const string ChatFindings = "chatFindings";             // Findings for the chat
    public const string ChangeEffects = "changeEffects";           // Our changes that helped or hurt
    public const string InfrastructureIssues = "infrastructureIssues"; // Infrastructure issues
    public const string Actions = "actions";                       // Actions

    // Chat consistency scope: Provider Issue Report
    public const string IssueSummary = "issueSummary";             // Summary
    public const string AffectedModel = "affectedModel";           // Affected model and configuration
    public const string Timeline = "timeline";                     // Timeline
    public const string Measurements = "measurements";             // Measurements with intervals
    public const string HoursObserved = "hoursObserved";           // Hours observed
    public const string RuledOut = "ruledOut";                     // What we ruled out (our changes, infrastructure)
    public const string SampleRequestIds = "sampleRequestIds";     // Sample request ids
    public const string ProviderRequest = "providerRequest";       // Request to the provider

    public const string TargetModelDevelopers = "model_developers";
    public const string TargetOverseerChat = "overseer_chat";
    public const string TargetBenchmark = "benchmark";

    /// <summary>A comparison-scope lead about one model rather than the system.</summary>
    public const string LeadTriageModel = "model";

    public static readonly BenchmarkReportAudienceSpec ExecutiveSummary = new(
        BenchmarkReportAudience.ExecutiveSummary,
        new[] { Comparison, Meaning, Confidence },
        MaxStrengths: 3,
        MaxWeaknesses: 3,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: false,
        UsesLeads: false)
    {
        PeerOnlySlots = new[] { Comparison }
    };

    public static readonly BenchmarkReportAudienceSpec TechnicalReport = new(
        BenchmarkReportAudience.TechnicalReport,
        new[] { Abstract, WhyItScored, WhatWorked, Limitations },
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

    public static readonly BenchmarkReportAudienceSpec ComparisonExecutiveSummary = new(
        BenchmarkReportAudience.ExecutiveSummary,
        new[] { Overview, WhichModel, TradeOffs, Reliability },
        MaxStrengths: 0,
        MaxWeaknesses: 0,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: false,
        UsesLeads: false)
    {
        ComparisonScope = true,
        UsesStrengthsAndWeaknesses = false,
        MaxModelPoints = 2
    };

    public static readonly BenchmarkReportAudienceSpec ComparisonTechnicalReport = new(
        BenchmarkReportAudience.TechnicalReport,
        new[] { Abstract, Results, DimensionProfiles, Frontier, QuestionPatterns, GraderReliability, Limitations },
        MaxStrengths: 0,
        MaxWeaknesses: 0,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: true,
        UsesLeads: false)
    {
        ComparisonScope = true,
        UsesStrengthsAndWeaknesses = false,
        MaxModelPoints = 4
    };

    public static readonly BenchmarkReportAudienceSpec ComparisonInternalBrief = new(
        BenchmarkReportAudience.InternalBrief,
        new[] { SharedGaps, ModelGaps, BenchmarkSystem },
        MaxStrengths: 0,
        MaxWeaknesses: 0,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: true,
        UsesLeads: true)
    {
        ComparisonScope = true,
        UsesStrengthsAndWeaknesses = false,
        MaxLeads = 8,
        LeadTriages = new[] { "chat", "harness", "suite", "corpus", LeadTriageModel }
    };

    public static readonly BenchmarkReportAudienceSpec ChatConsistencyExecutiveSummary = ChatConsistencySpec(
        BenchmarkReportAudience.ExecutiveSummary,
        AsGoodAsBefore, PlayerImpact, OurChanges, ProviderChanges, ConfidenceAndScope, NextRuns);

    public static readonly BenchmarkReportAudienceSpec ChatConsistencyTechnicalReport = ChatConsistencySpec(
        BenchmarkReportAudience.TechnicalReport,
        QuestionAndDesign, RunsAndCoverage, OverseerEvents, EndpointResults, Attribution, Robustness, Limitations, Reproducibility);

    public static readonly BenchmarkReportAudienceSpec ChatConsistencyInternalBrief = ChatConsistencySpec(
        BenchmarkReportAudience.InternalBrief,
        ChatFindings, ChangeEffects, InfrastructureIssues, NextRuns, Actions);

    public static readonly BenchmarkReportAudienceSpec ChatConsistencyProviderIssueReport = ChatConsistencySpec(
        BenchmarkReportAudience.ProviderIssueReport,
        IssueSummary, AffectedModel, Timeline, Measurements, HoursObserved, RuledOut, SampleRequestIds, ProviderRequest);

    /// <summary>The audiences of a chat consistency document, in document order.</summary>
    public static IReadOnlyList<BenchmarkReportAudience> ChatConsistencyAudiences { get; } = new[]
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief,
        BenchmarkReportAudience.ProviderIssueReport
    };

    /// <summary>The section title of each chat consistency slot.</summary>
    public static IReadOnlyDictionary<string, string> ChatConsistencySlotTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [AsGoodAsBefore] = "Is the Overseer chat with this model as good as before?",
        [PlayerImpact] = "What changed for players",
        [OurChanges] = "Our changes and their effect",
        [ProviderChanges] = "Provider-side changes",
        [ConfidenceAndScope] = "Confidence and scope",
        [NextRuns] = "Next runs",
        [QuestionAndDesign] = "Question and design",
        [RunsAndCoverage] = "Runs, coverage and scope",
        [OverseerEvents] = "Overseer events",
        [EndpointResults] = "Results by endpoint",
        [Attribution] = "Attribution",
        [Robustness] = "Robustness",
        [Limitations] = "Limitations",
        [Reproducibility] = "Reproducibility",
        [ChatFindings] = "Findings for the chat",
        [ChangeEffects] = "Our changes that helped or hurt",
        [InfrastructureIssues] = "Infrastructure issues",
        [Actions] = "Actions",
        [IssueSummary] = "Summary",
        [AffectedModel] = "Affected model and configuration",
        [Timeline] = "Timeline",
        [Measurements] = "Measurements with intervals",
        [HoursObserved] = "Hours observed",
        [RuledOut] = "What we ruled out (our changes, infrastructure)",
        [SampleRequestIds] = "Sample request ids",
        [ProviderRequest] = "Request to the provider",
    };

    /// <summary>A chat consistency audience's slots: prose sections only, with no lists, questions or peers.</summary>
    private static BenchmarkReportAudienceSpec ChatConsistencySpec(BenchmarkReportAudience audience, params string[] slots) => new(
        audience,
        slots,
        MaxStrengths: 0,
        MaxWeaknesses: 0,
        UsesRecommendations: false,
        RecommendationTargets: Array.Empty<string>(),
        UsesQuestionNotes: false,
        RequiresQuestionTopics: false,
        UsesLeads: false)
    {
        ChatConsistencyScope = true,
        UsesStrengthsAndWeaknesses = false
    };

    /// <summary>The audience's slots for a chat consistency document.</summary>
    public static BenchmarkReportAudienceSpec ForChatConsistency(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => ChatConsistencyExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport => ChatConsistencyTechnicalReport,
        BenchmarkReportAudience.InternalBrief => ChatConsistencyInternalBrief,
        BenchmarkReportAudience.ProviderIssueReport => ChatConsistencyProviderIssueReport,
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, null)
    };

    public static BenchmarkReportAudienceSpec For(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport => TechnicalReport,
        BenchmarkReportAudience.InternalBrief => InternalBrief,
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, null)
    };

    /// <summary>The audience's slots for a per-model, comparison-scope or chat consistency document.</summary>
    public static BenchmarkReportAudienceSpec For(BenchmarkReportAudience audience, BenchmarkReportScope scope)
        => scope == BenchmarkReportScope.ChatConsistency
            ? ForChatConsistency(audience)
            : scope != BenchmarkReportScope.Comparison
            ? For(audience)
            : audience switch
            {
                BenchmarkReportAudience.ExecutiveSummary => ComparisonExecutiveSummary,
                BenchmarkReportAudience.TechnicalReport => ComparisonTechnicalReport,
                BenchmarkReportAudience.InternalBrief => ComparisonInternalBrief,
                _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, null)
            };

    /// <summary>The audience's slots for the document the sheet describes.</summary>
    public static BenchmarkReportAudienceSpec For(BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return For(audience, sheet.IsChatConsistency ? BenchmarkReportScope.ChatConsistency
            : sheet.IsComparison ? BenchmarkReportScope.Comparison
            : BenchmarkReportScope.Model);
    }
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

/// <summary>One grading role of the subject's runs, with its provider relation to the subject.</summary>
public sealed class BenchmarkReportGrader
{
    /// <summary>e.g. <c>Panel member A</c>, <c>Panel member B</c>, <c>Assessor</c>, <c>Reference reader</c>, <c>Claim verifier</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }

    /// <summary>
    /// The grader's provider is the subject's. The name is the stored FactsJson property of every
    /// format version, so it keeps "family".
    /// </summary>
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

    /// <summary>The assessed difficulty band's name.</summary>
    public string Band { get; set; } = string.Empty;

    /// <summary>
    /// The difficulty band the question was authored with; null when no answer recorded one, and on a
    /// document stored before format version 9.
    /// </summary>
    public string? AuthoredBand { get; set; }

    /// <summary>The subject's published quality on this item; a mean over runs for a group.</summary>
    public double? Score { get; set; }

    /// <summary>The peers' mean quality on the same item and revision; null when no peer answered it.</summary>
    public double? PeerMean { get; set; }

    public double? Difference { get; set; }
    public int PeerCount { get; set; }

    /// <summary>The lowest peer quality on the same item and revision; null when no peer answered it.</summary>
    public double? PeerMin { get; set; }

    /// <summary>The highest peer quality on the same item and revision; null when no peer answered it.</summary>
    public double? PeerMax { get; set; }

    /// <summary>
    /// Peers scoring more than <see cref="Overseer.Services.Benchmarking.BenchmarkReportFacts.PeerAboveMarginPoints"/>
    /// points above the subject on the same item and revision; 0 when the subject has no score.
    /// </summary>
    public int PeersAbove { get; set; }

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

    /// <summary>
    /// A battery question's suite-qualified reference, <c>S2-Q7</c>: the suite's number in the battery
    /// and the question's number within that suite. Null on a run or group sheet, whose questions are
    /// <c>Q&lt;n&gt;</c>.
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>A battery question's suite number, from 1 in the battery's suite order; null on a run or group sheet.</summary>
    public int? Suite { get; set; }

    /// <summary>A battery question's rounds with a critical error, from the persisted item row; null on a run or group sheet.</summary>
    public int? CriticalErrorCount { get; set; }

    /// <summary>
    /// A battery question whose text, rubric, answer excerpt and grader comments the writer was given;
    /// null on a run or group sheet, where every question is given in full. On a comparison-scope
    /// sheet, a question whose text and rubric the writer was given.
    /// </summary>
    public bool? Detailed { get; set; }

    /// <summary>
    /// A comparison-scope sheet's per-model cells of this question, by letter; null on a per-model
    /// sheet. There the question's own figures are over the covered models: <see cref="Score"/> their
    /// mean, <see cref="PeerMin"/> and <see cref="PeerMax"/> their lowest and highest score and
    /// <see cref="PeerCount"/> the models that scored it.
    /// </summary>
    public List<BenchmarkReportQuestionModelScore>? Models { get; set; }

    /// <summary>The letters whose answer excerpts the writer was given (comparison scope); null on a per-model sheet.</summary>
    public List<string>? ExcerptLetters { get; set; }
}

/// <summary>One covered model's figures on one question of a comparison-scope sheet.</summary>
public sealed class BenchmarkReportQuestionModelScore
{
    public string Letter { get; set; } = string.Empty;

    /// <summary>The model's published quality on the item; a mean over its runs; null when unscored.</summary>
    public double? Score { get; set; }

    public bool CriticalError { get; set; }

    /// <summary>Rounds with a critical error, for a battery result; null otherwise.</summary>
    public int? CriticalErrorCount { get; set; }

    public int RefutedClaims { get; set; }
    public int? RefutedAnswerSentences { get; set; }
    public double ToolCalls { get; set; }
    public double? ModelTimeMs { get; set; }
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

    /// <summary>
    /// The questions panel member A's findings in this row name, sorted; null for a single-assessor
    /// row and on a document stored before format version 9.
    /// </summary>
    public List<int>? QuestionsA { get; set; }

    /// <summary>The questions panel member B's findings in this row name, sorted; null as for <see cref="QuestionsA"/>.</summary>
    public List<int>? QuestionsB { get; set; }

    /// <summary>
    /// The questions both members' findings in this row name, sorted; null as for <see cref="QuestionsA"/>.
    /// A Convergent row's <c>Both graders</c> label holds for an item only on these questions.
    /// </summary>
    public List<int>? SharedQuestions { get; set; }

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

    /// <summary><c>Run</c>, <c>Group</c> or <c>Battery</c>.</summary>
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
    /// The "no pairwise significance test" statement for the comparison's entries, in the documents' own
    /// fixed wording (<see cref="Overseer.Services.Benchmarking.BenchmarkReportFacts.NoSignificanceStatement(int)"/>);
    /// shown to the writer, and its presence makes the renderer print its own sentence.
    /// </summary>
    public string NoSignificanceSummary { get; set; } = string.Empty;

    /// <summary>
    /// Where the comparison view tells its operator to look instead. Kept in the stored format; an
    /// instruction for the Overseer's own view, so neither documents nor the writer see it.
    /// </summary>
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

    /// <summary>
    /// The subject's paired difference against each peer, by letter; empty on a stand-alone sheet
    /// and on a document stored before format version 7.
    /// </summary>
    public List<BenchmarkReportPairedDifference> PairedDifferences { get; set; } = new();

    /// <summary>The battery run a battery subject's sheet describes; null on a run or group sheet.</summary>
    public BenchmarkReportBatterySubject? Battery { get; set; }

    /// <summary>The value of <see cref="Scope"/> on a comparison-scope sheet.</summary>
    public const string ComparisonScopeValue = "Comparison";

    /// <summary>
    /// <see cref="ComparisonScopeValue"/> on a comparison-scope sheet, which describes every covered
    /// model as an equal: <see cref="Peers"/> then holds every covered model with its letter and there
    /// is no subject. Null on a per-model sheet.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>The sheet is a comparison-scope sheet.</summary>
    [JsonIgnore]
    public bool IsComparison => string.Equals(Scope, ComparisonScopeValue, StringComparison.Ordinal);

    /// <summary>The value of <see cref="Scope"/> on a chat consistency sheet.</summary>
    public const string ChatConsistencyScopeValue = "ChatConsistency";

    /// <summary>The sheet is a chat consistency sheet.</summary>
    [JsonIgnore]
    public bool IsChatConsistency => string.Equals(Scope, ChatConsistencyScopeValue, StringComparison.Ordinal);

    /// <summary>The analysis a chat consistency sheet describes; null on every other sheet.</summary>
    public BenchmarkReportChatConsistencySubject? ChatConsistency { get; set; }

    /// <summary>
    /// A comparison-scope sheet written over every entry of the comparison that was not Excluded at
    /// the time; null on a per-model sheet.
    /// </summary>
    public bool? CoversAllEntries { get; set; }

    /// <summary>The comparison's entries that were not Excluded when a comparison-scope sheet was written; null on a per-model sheet.</summary>
    public int? ComparisonEntryCount { get; set; }

    /// <summary>The covered models of a comparison-scope sheet, in letter order; null on a per-model sheet.</summary>
    public List<BenchmarkReportComparisonModel>? Models { get; set; }

    /// <summary>
    /// A comparison-scope sheet's paired-test families over its covered models: against the reference
    /// (the model lettered A), and over all pairs when there are few enough models; null on a per-model sheet.
    /// </summary>
    public List<BenchmarkReportPairedFamily>? PairedTests { get; set; }

    /// <summary>Why a comparison-scope sheet has no paired tests; null when it has them, and on a per-model sheet.</summary>
    public string? PairedTestsUnavailableReason { get; set; }
}

/// <summary>
/// The saved chat consistency analysis behind a chat consistency sheet. The sheet's subject is the
/// analysis's model; its <see cref="BenchmarkReportFactSheet.Peers"/> are the control models, lettered.
/// </summary>
public sealed class BenchmarkReportChatConsistencySubject
{
    /// <summary>The stored analysis; null for an analysis not yet saved.</summary>
    public int? AnalysisId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string ProtocolLabel { get; set; } = string.Empty;
    public string InputSha256 { get; set; } = string.Empty;
    public int AnalysisCodeVersion { get; set; }

    /// <summary>Ascending.</summary>
    public List<long> BaselineRunIds { get; set; } = new();

    /// <summary>Ascending.</summary>
    public List<long> ComparisonRunIds { get; set; } = new();

    /// <summary>The control runs the analysis read, ascending; never a run of the subject.</summary>
    public List<long> ControlRunIds { get; set; } = new();

    /// <summary>At least one provider-side attribution is graded Established or Indicated.</summary>
    public bool ProviderIssueReportAvailable { get; set; }
}

/// <summary>One covered model of a comparison-scope sheet.</summary>
public sealed class BenchmarkReportComparisonModel
{
    /// <summary><c>A</c> for the highest Intelligence Index of the covered models, ties by entry key (ordinal).</summary>
    public string Letter { get; set; } = string.Empty;

    public string EntryKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}

/// <summary>One family of paired tests over a comparison-scope sheet's covered models.</summary>
public sealed class BenchmarkReportPairedFamily
{
    /// <summary><c>Reference</c> (the model lettered A against each other model) or <c>AllPairs</c>.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>The reference model's letter in <c>Reference</c> mode.</summary>
    public string? ReferenceLetter { get; set; }

    /// <summary>Set when a model has a single run (for a battery result, one run per suite).</summary>
    public string? SingleRunCaveat { get; set; }

    /// <summary>Intelligence, then Speed and Cost, each its own family for the Holm adjustment.</summary>
    public List<BenchmarkReportPairedMeasure> Measures { get; set; } = new();
}

/// <summary>One measure's tests within a paired family.</summary>
public sealed class BenchmarkReportPairedMeasure
{
    /// <summary><c>Intelligence</c>, <c>Speed</c> or <c>Cost</c>.</summary>
    public string Measure { get; set; } = string.Empty;

    /// <summary>The tests made: pairs with a p-value.</summary>
    public int FamilySize { get; set; }

    /// <summary><c>None</c> or <c>Holm</c>.</summary>
    public string Adjustment { get; set; } = string.Empty;

    public string AdjustmentNote { get; set; } = string.Empty;

    public string? NotTestedReason { get; set; }

    /// <summary>In letter order of the first model, then of the second.</summary>
    public List<BenchmarkReportPairedTest> Pairs { get; set; } = new();
}

/// <summary>
/// One paired test, oriented by letter: the first model is the one with the earlier letter. A
/// difference is first minus second; a ratio is first divided by second.
/// </summary>
public sealed class BenchmarkReportPairedTest
{
    public string FirstLetter { get; set; } = string.Empty;
    public string SecondLetter { get; set; } = string.Empty;

    /// <summary>Questions both models answered on the same item revision.</summary>
    public int PairedItems { get; set; }

    /// <summary><c>Difference</c> or <c>Ratio</c>.</summary>
    public string EffectKind { get; set; } = string.Empty;

    public double? Effect { get; set; }
    public double? Lower { get; set; }
    public double? Upper { get; set; }
    public double? PValue { get; set; }

    /// <summary>Holm-adjusted within the measure's family; equal to <see cref="PValue"/> in a family of one.</summary>
    public double? AdjustedPValue { get; set; }

    /// <summary>The adjusted p-value is below 0.05.</summary>
    public bool Established { get; set; }

    /// <summary><c>First</c>, <c>Second</c> or <c>None</c>: which model the established result favors.</summary>
    public string Favors { get; set; } = string.Empty;

    public string? NotTestedReason { get; set; }
}

/// <summary>The battery run behind a battery subject: its definition as run, and its suites in order.</summary>
public sealed class BenchmarkReportBatterySubject
{
    public long BatteryRunId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The definition's revision; null when the definition snapshot cannot be read.</summary>
    public int? Revision { get; set; }

    /// <summary>The weighting scheme in words, e.g. <c>Questions and difficulty</c>.</summary>
    public string Scheme { get; set; } = string.Empty;

    public int SuiteCount { get; set; }
    public int RunsPerSuite { get; set; }

    /// <summary>The usable member runs behind the result.</summary>
    public int MemberRunCount { get; set; }

    /// <summary>In suite order.</summary>
    public List<BenchmarkReportBatterySuite> Suites { get; set; } = new();
}

/// <summary>One suite of a battery subject.</summary>
public sealed class BenchmarkReportBatterySuite
{
    /// <summary>The suite's number in the battery, from 1: the <c>S</c> of a question reference and the <c>n</c> of <c>suite.&lt;n&gt;.*</c>.</summary>
    public int Number { get; set; }

    public long? SuiteId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The suite's questions on the sheet.</summary>
    public int QuestionCount { get; set; }
}

/// <summary>
/// The subject's mean per-question difference from one peer over the questions both scored on the
/// same item revision, with a 95 % paired-bootstrap interval. An estimate from question sampling
/// only: not adjusted for comparing several models, and not a significance test.
/// </summary>
public sealed class BenchmarkReportPairedDifference
{
    public string PeerLetter { get; set; } = string.Empty;

    /// <summary>Questions both sides scored on the same item revision.</summary>
    public int SharedQuestions { get; set; }

    /// <summary>Subject minus peer, in quality points; null below the minimum of shared questions.</summary>
    public double? MeanDifference { get; set; }

    public double? Lower { get; set; }
    public double? Upper { get; set; }
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

    /// <summary>
    /// The entry's runs' harness version, or <c>mixed</c> when they differ; null on a document stored
    /// before format version 7.
    /// </summary>
    public string? HarnessVersion { get; set; }

    /// <summary>The earliest start time of the entry's runs, UTC.</summary>
    public DateTime? FirstRunUtc { get; set; }

    /// <summary>The latest completion time of the entry's runs (the start time of a run that recorded none), UTC.</summary>
    public DateTime? LastRunUtc { get; set; }

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

    /// <summary>
    /// One block per subject run, in run-id order. On a comparison-scope document, one block per
    /// covered model's run whose answers the writer was given as excerpts, in letter and run-id order,
    /// holding only those answers.
    /// </summary>
    public List<BenchmarkReportContentRun> Runs { get; set; } = new();

    /// <summary>
    /// A comparison-scope document's questions as asked, with their rubrics and no answer, by
    /// number; null on a per-model document, whose runs carry them.
    /// </summary>
    public List<BenchmarkReportContentQuestion>? Questions { get; set; }
}

public sealed class BenchmarkReportContentRun
{
    public long RunId { get; set; }

    /// <summary>The covered model the run belongs to on a comparison-scope document; null on a per-model document.</summary>
    public string? EntryKey { get; set; }

    /// <summary>That model's letter on a comparison-scope document; null on a per-model document.</summary>
    public string? Letter { get; set; }

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

    /// <summary>
    /// The complete answer, stored only when <see cref="AnswerExcerptCut"/> is true; otherwise the
    /// excerpt is the whole answer. Null on documents stored before format version 5. Printed at
    /// Full only; the writer never sees it.
    /// </summary>
    public string? AnswerText { get; set; }

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

    /// <summary>A comparison-scope document's points about each covered model, by letter; null on a per-model document.</summary>
    [JsonPropertyName("models")]
    public List<BenchmarkReportModelPoints>? Models { get; set; }
}

/// <summary>The writer's points about one covered model of a comparison-scope document.</summary>
public sealed class BenchmarkReportModelPoints
{
    /// <summary>The model's letter.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<BenchmarkReportWriterItem> Points { get; set; } = new();
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
    /// <summary>
    /// The D7 rule number, 1–19 and 21, or 20 for a battery prompt that left out question detail to
    /// stay within its budget.
    /// </summary>
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

/// <summary>Preview and start request. The first four fields mirror <see cref="BenchmarkModelComparisonRequest"/>.</summary>
public class BenchmarkReportPackRequest
{
    public List<long> RunIds { get; set; } = new();
    public List<long> GroupIds { get; set; } = new();

    /// <summary>Battery runs, each one comparison entry; a request naming any of them names no run or group.</summary>
    public List<long> BatteryRunIds { get; set; } = new();

    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; } = BenchmarkModelComparisonPricingBasis.Current;

    /// <summary>
    /// Model scope: the comparison entry key of the subject, <c>run:&lt;id&gt;</c>, <c>group:&lt;id&gt;</c>
    /// or <c>battery:&lt;id&gt;</c>; read when <see cref="SubjectKeys"/> is empty. Unused for comparison scope.
    /// </summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>
    /// A per-model document set (<see cref="BenchmarkReportScope.Model"/>, the default, sent as 1) or a
    /// comparison-scope document set over the covered models (<see cref="BenchmarkReportScope.Comparison"/>, 2).
    /// </summary>
    public BenchmarkReportScope Scope { get; set; } = BenchmarkReportScope.Model;

    /// <summary>
    /// Model scope: the subjects, each written in turn with the requested documents; empty or absent
    /// means <see cref="SubjectKey"/> alone.
    /// </summary>
    public List<string>? SubjectKeys { get; set; }

    /// <summary>
    /// Comparison scope: the covered entry keys; empty or absent means every entry of the comparison
    /// that is not Excluded.
    /// </summary>
    public List<string>? CoveredEntryKeys { get; set; }

    public List<BenchmarkReportAudience> Audiences { get; set; } = new();

    public long WriterModelConfigurationId { get; set; }

    /// <summary>The operator acknowledged that the writer shares the provider of the subject or of a covered model.</summary>
    public bool AcknowledgeSameProvider { get; set; }

    /// <summary>
    /// Report Pack documents this job replaces: each is deleted, with its charts, only once the new
    /// document of its subject or covered set and audience is stored. A requested document already
    /// written and not named here is refused with 409.
    /// </summary>
    public List<long>? ReplaceDocumentIds { get; set; }

    /// <summary>The subjects of a model-scope request: <see cref="SubjectKeys"/>, else <see cref="SubjectKey"/>; distinct, in order.</summary>
    public List<string> ModelSubjectKeys()
    {
        var keys = (SubjectKeys ?? new List<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0 && !string.IsNullOrWhiteSpace(SubjectKey)) keys.Add(SubjectKey.Trim());
        return keys;
    }
}

/// <summary>
/// The <c>request</c> field of <c>POST report-packs/layout-preview</c>: a preview request, the one
/// document to lay out, its paper and naming, the figures' layout and each chart's text. The chart
/// images travel as the multipart files <c>&lt;figureKey&gt;.png</c>.
/// </summary>
public class BenchmarkReportLayoutPreviewRequest : BenchmarkReportPackRequest
{
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary><c>a4</c> or <c>letter</c>; empty means A4.</summary>
    public string? Paper { get; set; }

    /// <summary><c>named</c> or <c>anonymized</c>: the copy the charts were drawn for; empty means named.</summary>
    public string? Naming { get; set; }

    public BenchmarkReportChartLayout? Layout { get; set; }

    public List<BenchmarkReportLayoutPreviewChart> Charts { get; set; } = new();
}

/// <summary>One chart of a layout preview, its image the multipart file named <c>&lt;figureKey&gt;.png</c>.</summary>
public class BenchmarkReportLayoutPreviewChart
{
    public string FigureKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
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

    /// <summary>The subject the estimate is for: a model's entry key, or the comparison-scope subject key.</summary>
    public string? SubjectKey { get; set; }

    public int PromptChars { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }

    /// <summary>Null when the writer has no resolvable price.</summary>
    public double? EstimatedCostUsd { get; set; }

    /// <summary>
    /// The estimated input tokens as a share of the writer's context window, 0 to 1 and above; null
    /// when the window is unknown. Above <c>0.9</c> the start is refused.
    /// </summary>
    public double? ContextWindowShare { get; set; }
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

    /// <summary>
    /// The Report Pack documents already stored for this comparison and its subject (model scope, the
    /// first subject) or its covered set (comparison scope), the newest per audience, in audience
    /// order. Start refuses to write one of these audiences again unless the request names the
    /// document under <c>replaceDocumentIds</c>.
    /// </summary>
    public List<BenchmarkReportPackWrittenDocumentDto> WrittenDocuments { get; set; } = new();

    /// <summary>The scope previewed, as requested.</summary>
    public BenchmarkReportScope Scope { get; set; } = BenchmarkReportScope.Model;

    /// <summary>The numbered comparison, when it has been identified; null before.</summary>
    public int? ComparisonId { get; set; }

    /// <summary>The comparison's display name now; null without a numbered comparison.</summary>
    public string? ComparisonName { get; set; }

    /// <summary>The comparison's entries that are not Excluded: the M of "2 of 5 models".</summary>
    public int ComparisonEntryCount { get; set; }

    /// <summary>Comparison scope: the covered set is every entry that is not Excluded; false for model scope.</summary>
    public bool CoversAllEntries { get; set; }

    /// <summary>Comparison scope: the covered set's key (<see cref="BenchmarkReportDocument.CoveredSetKey"/>); null for model scope.</summary>
    public string? CoveredSetKey { get; set; }

    /// <summary>
    /// Comparison scope: the covered models in letter order, each with its letter. Model scope: the
    /// subjects in request order, without letters.
    /// </summary>
    public List<BenchmarkReportCoveredModelDto> CoveredModels { get; set; } = new();

    /// <summary>
    /// Comparison scope: the comparison-scope documents of every other covered set of this
    /// comparison, one item per set, the newest document per audience; empty for model scope.
    /// </summary>
    public List<BenchmarkReportPackCoveredSetDto> OtherModelSets { get; set; } = new();

    /// <summary>
    /// The per-model Report Pack documents of this comparison: for each subject (model scope) or each
    /// covered model (comparison scope), the newest per audience.
    /// </summary>
    public List<BenchmarkReportPackSubjectDocumentsDto> SubjectDocuments { get; set; } = new();

    /// <summary>The writer configuration's context window in tokens; null when it is unknown.</summary>
    public int? WriterContextWindowTokens { get; set; }
}

/// <summary>A Report Pack document already stored for the previewed comparison.</summary>
public class BenchmarkReportPackWrittenDocumentDto
{
    public BenchmarkReportAudience Audience { get; set; }
    public long DocumentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? WriterDisplayName { get; set; }

    /// <summary>The document's subject key: an entry key, <c>comparison:&lt;id&gt;</c> or <c>comparison:&lt;id&gt;/&lt;16 hex&gt;</c>.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary><c>Completed</c> or <c>CompletedWithWarnings</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string? WriterProvider { get; set; }
    public string? WriterModelId { get; set; }
    public string? WriterThinkingLevel { get; set; }
    public long DurationMs { get; set; }
    public decimal? CostUsd { get; set; }
}

/// <summary>The comparison-scope documents of one covered set of a comparison.</summary>
public class BenchmarkReportPackCoveredSetDto
{
    public string CoveredSetKey { get; set; } = string.Empty;
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>The set covered every entry that was not Excluded when its documents were written.</summary>
    public bool CoversAllEntries { get; set; }

    /// <summary>The covered models, labeled from the newest document's fact sheet.</summary>
    public List<BenchmarkReportCoveredModelDto> CoveredModels { get; set; } = new();

    /// <summary>The newest document per audience, in audience order.</summary>
    public List<BenchmarkReportPackWrittenDocumentDto> Documents { get; set; } = new();
}

/// <summary>The per-model documents of one subject of a comparison.</summary>
public class BenchmarkReportPackSubjectDocumentsDto
{
    public string SubjectKey { get; set; } = string.Empty;
    public string SubjectLabel { get; set; } = string.Empty;

    /// <summary>The newest document per audience, in audience order.</summary>
    public List<BenchmarkReportPackWrittenDocumentDto> Documents { get; set; } = new();
}

public class BenchmarkReportPackStartResponse
{
    public string JobId { get; set; } = string.Empty;
}

public class BenchmarkReportPackDocumentProgressDto
{
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary>The document's subject: a model's entry key, or the comparison-scope subject key; empty for a completion job's document.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>The subject as the progress list names it: the model's label, or <c>Comparison #12</c>.</summary>
    public string SubjectLabel { get; set; } = string.Empty;

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

    /// <summary>The first subject's key; every document's own subject is on its progress row.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    public string SubjectLabel { get; set; } = string.Empty;

    /// <summary>Per-model documents (1) or comparison-scope documents (2).</summary>
    public BenchmarkReportScope Scope { get; set; } = BenchmarkReportScope.Model;

    /// <summary>The numbered comparison the job writes for; null for a completion job.</summary>
    public int? ComparisonId { get; set; }

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

    /// <summary>The server's clock when the view was built, for elapsed times the client shows.</summary>
    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>A stored document in a list; never carries rendered text.</summary>
public class BenchmarkReportDocumentListItemDto
{
    public long Id { get; set; }
    public Guid PackId { get; set; }
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary>A report pack's document (1), one written after its run completed (2), or one written after its battery run finished (3).</summary>
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

    /// <summary>The comparison's entry-set key (<see cref="BenchmarkReportDocument.ComparisonKey"/>); null when it could not be derived.</summary>
    public string? ComparisonKey { get; set; }

    /// <summary>The comparison's entries, the subject included; an analysis group counts once.</summary>
    public int ComparisonEntryCount { get; set; }

    /// <summary>The fact sheet's peers.</summary>
    public int PeerCount { get; set; }

    /// <summary><c>AsRun</c> or <c>Current</c>, from the stored comparison request.</summary>
    public string PricingBasis { get; set; } = string.Empty;

    /// <summary>
    /// A peer run was re-scored, re-run or deleted since the document was written. Always false for a
    /// document stored without peer rows.
    /// </summary>
    public bool PeersChangedSinceGeneration { get; set; }

    /// <summary>Images in the document's chart manifest, both namings counted; <see cref="ChartFigureKeys"/> lists its figures.</summary>
    public int ChartCount { get; set; }

    /// <summary>The figure keys of the document's charts, in manifest order.</summary>
    public List<string> ChartFigureKeys { get; set; } = new();

    /// <summary>The chart manifest's settings hash; null when the document has no charts.</summary>
    public string? ChartSettingsHash { get; set; }

    /// <summary>
    /// Peer entry key → the peer's letter, from the fact sheet. For a comparison-scope document, every
    /// covered model's entry key → its letter.
    /// </summary>
    public Dictionary<string, string> PeerLetters { get; set; } = new();

    /// <summary>The numbered comparison (<c>Comparison #Id</c>); null for a document without one.</summary>
    public int? ComparisonId { get; set; }

    /// <summary>The chat consistency analysis a Chat Consistency Report was written from; null on every other document.</summary>
    public int? ChatConsistencyAnalysisId { get; set; }

    /// <summary>The comparison's display name, its rename or else its default name; null without a comparison.</summary>
    public string? ComparisonName { get; set; }

    /// <summary>A per-model document (1) or a comparison-scope document (2).</summary>
    public BenchmarkReportScope Scope { get; set; } = BenchmarkReportScope.Model;

    /// <summary>A comparison-scope document written over every non-excluded entry; false for model scope.</summary>
    public bool CoversAllEntries { get; set; }

    /// <summary>The covered entry set's key (<see cref="BenchmarkReportDocument.CoveredSetKey"/>).</summary>
    public string? CoveredSetKey { get; set; }

    /// <summary>
    /// Comparison scope: the comparison's entries that were not Excluded when the document was written,
    /// the M of "2 of 5 models"; null for model scope.
    /// </summary>
    public int? ComparisonModelCount { get; set; }

    /// <summary>The models the document covers: the subject for model scope, every covered entry for comparison scope.</summary>
    public List<BenchmarkReportCoveredModelDto> CoveredModels { get; set; } = new();
}

/// <summary>One model (comparison entry) a document covers.</summary>
public class BenchmarkReportCoveredModelDto
{
    /// <summary>A <c>run:</c>, <c>group:</c> or <c>battery:</c> entry key.</summary>
    public string EntryKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string? Provider { get; set; }

    /// <summary>The model's letter on a comparison-scope document or preview; null for a per-model subject.</summary>
    public string? Letter { get; set; }
}

/// <summary>What <c>GET report-documents</c> filters on; every field is optional.</summary>
public sealed class BenchmarkReportDocumentListFilter
{
    public long? SuiteId { get; init; }

    /// <summary>A run the subject includes; a peer's run never matches.</summary>
    public long? RunId { get; init; }

    /// <summary>A <see cref="BenchmarkReportDocument.ComparisonKey"/>.</summary>
    public string? ComparisonKey { get; init; }

    /// <summary>A <see cref="BenchmarkReportDocument.ComparisonId"/>.</summary>
    public int? ComparisonId { get; init; }

    public BenchmarkReportDocumentOrigin? Origin { get; init; }

    public int? Take { get; init; }
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
