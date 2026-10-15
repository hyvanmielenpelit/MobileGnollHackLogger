namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Linq;

// The stored result of the root-cause engine: what code computed about why a model, or the compared
// models, did well or badly on the Intelligence Index, speed and cost. It is stored inside the
// document's FactsJson as BenchmarkReportFactSheet.RootCause and serialized with BenchmarkReportJson
// (camelCase, nulls left out), so its property names are part of the stored format: renaming one is
// a ReportFormatVersion change. Every collection is in an explicit, deterministic order.
//
// A model is named in this data by its token: its letter on a comparison-scope sheet, "subject" for the
// subject of a per-model sheet and a peer's letter for each peer.

/// <summary>The three axes a Root Cause Analysis explains.</summary>
public static class RootCauseAxes
{
    public const string Intelligence = "intelligence";
    public const string Speed = "speed";
    public const string Cost = "cost";

    /// <summary>In document order.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Intelligence, Speed, Cost };

    /// <summary><c>Intelligence</c>, <c>Speed</c> or <c>Cost</c>.</summary>
    public static string Name(string axis) => axis switch
    {
        Intelligence => "Intelligence",
        Speed => "Speed",
        Cost => "Cost",
        _ => axis
    };
}

/// <summary>The confidence grades of § A2, from the strongest.</summary>
public static class RootCauseGrades
{
    public const string High = "High";
    public const string Moderate = "Moderate";
    public const string Low = "Low";
    public const string NotSupported = "NotSupported";

    /// <summary>3 for High down to 0 for Not supported; -1 for an unknown grade.</summary>
    public static int Rank(string? grade) => grade switch
    {
        High => 3,
        Moderate => 2,
        Low => 1,
        NotSupported => 0,
        _ => -1
    };

    /// <summary>The grade as documents print it: <c>Not supported</c> for <see cref="NotSupported"/>.</summary>
    public static string Display(string? grade) => grade == NotSupported ? "Not supported" : grade ?? string.Empty;

    /// <summary>The weaker of <paramref name="grade"/> and <paramref name="cap"/>; Not supported is never raised.</summary>
    public static string Capped(string grade, string cap) => Rank(grade) <= Rank(cap) ? grade : cap;

    /// <summary>The grade in plain words: Established, Probable, Suggestive, Ruled out, or Unknown for none.</summary>
    public static string Word(string? grade) => grade switch
    {
        High => "Established",
        Moderate => "Probable",
        Low => "Suggestive",
        NotSupported => "Ruled out",
        _ => "Unknown"
    };

    /// <summary><c>Probable (Moderate)</c>: the plain word with the grade; <c>Unknown</c> for none.</summary>
    public static string WordWithGrade(string? grade)
        => Rank(grade) < 0 ? Word(grade) : Word(grade) + " (" + Display(grade) + ")";
}

/// <summary>The status of one axis (§ A2): the <c>rca.axis.&lt;axis&gt;.status</c> fact's value.</summary>
public static class RootCauseStatuses
{
    public const string Established = "RootCauseEstablished";
    public const string Probable = "ProbableCause";
    public const string NotEstablished = "NotEstablished";

    public static string Display(string? status) => status switch
    {
        Established => "Root cause established",
        Probable => "Probable cause",
        _ => "Not established"
    };
}

/// <summary>Where a hypothesis puts the cause: the triage categories plus model and provider.</summary>
public static class RootCauseLoci
{
    public const string Model = "model";
    public const string Chat = "chat";
    public const string Harness = "harness";
    public const string Suite = "suite";
    public const string Corpus = "corpus";
    public const string Provider = "provider";
}

/// <summary>The outcome of one robustness check.</summary>
public static class RootCauseCheckStatuses
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string NotAssessable = "NotAssessable";
}

/// <summary>Where a Root Cause Analysis was written from.</summary>
public static class RootCauseOrigins
{
    public const string Comparison = "Comparison";
    public const string ModelPack = "ModelPack";
    public const string RunCompletion = "RunCompletion";
    public const string BatteryCompletion = "BatteryCompletion";
}

/// <summary>The kinds of recommended run (§ A5).</summary>
public static class RootCauseRunKinds
{
    public const string Replicate = "replicate";
    public const string ThinkingPair = "thinkingPair";
    public const string StylePair = "stylePair";
    public const string PanelSwap = "panelSwap";
    public const string TimeStratum = "timeStratum";
    public const string Control = "control";
}

/// <summary>What a recommended run serves, in the order runs are made: the chat, the benchmark, then model selection.</summary>
public static class RootCausePurposes
{
    public const string Chat = "C";
    public const string Benchmark = "B";
    public const string ModelSelection = "M";

    /// <summary>0 for C, 1 for B, 2 for M.</summary>
    public static int Order(string? purpose) => purpose switch
    {
        Chat => 0,
        Benchmark => 1,
        _ => 2
    };
}

/// <summary>How strongly a run is recommended.</summary>
public static class RootCauseMarks
{
    public const string Required = "Required";
    public const string Recommended = "Recommended";
    public const string Optional = "Optional";
}

/// <summary>The segment keys of the chart rows, in stacking order.</summary>
public static class RootCauseSegments
{
    // R1: points at risk
    public const string NoTool = "noTool";
    public const string BudgetOrDefect = "budgetOrDefect";
    public const string CriticalError = "criticalError";
    public const string OtherLow = "otherLow";

    // R2: time per question
    public const string Ttft = "ttft";
    public const string OutputGeneration = "outputGeneration";
    public const string ToolExecution = "toolExecution";
    public const string OwnWaits = "ownWaits";
    public const string Other = "other";

    // R3: cost per question, and the cost components of the decompositions
    public const string UncachedInput = "uncachedInput";
    public const string CacheRead = "cacheRead";
    public const string CacheWrite = "cacheWrite";
    public const string Output = "output";
    public const string Reasoning = "reasoning";

    public static readonly IReadOnlyList<string> AtRisk = new[] { NoTool, BudgetOrDefect, CriticalError, OtherLow };
    public static readonly IReadOnlyList<string> Time = new[] { Ttft, OutputGeneration, ToolExecution, OwnWaits, Other };
    public static readonly IReadOnlyList<string> CostComponents = new[] { UncachedInput, CacheRead, CacheWrite, Output, Reasoning };

    /// <summary>The segment's words, as a chart key and a table print them.</summary>
    public static string Label(string key) => key switch
    {
        NoTool => "answers without tools",
        BudgetOrDefect => "budget, truncation or output defects",
        CriticalError => "confirmed critical errors",
        OtherLow => "other low-scoring answers",
        Ttft => "time to first token",
        OutputGeneration => "output generation",
        ToolExecution => "tool execution",
        OwnWaits => "own waits and retries",
        Other => "other",
        UncachedInput => "uncached input",
        CacheRead => "cache reads",
        CacheWrite => "cache writes",
        Output => "visible output",
        Reasoning => "reasoning",
        _ => key
    };
}

/// <summary>
/// Everything the root-cause engine computed for one document: evidence, known facts, hypotheses with
/// their grades, the status of each axis, the next runs and checks, the controls used and the
/// chart-ready rows. Stored as <c>BenchmarkReportFactSheet.RootCause</c>; its <c>rca.*</c> facts are a
/// flattened copy for the writer and the validator.
/// </summary>
public sealed class RootCauseResult
{
    /// <summary>The engine version the result was computed with.</summary>
    public int EngineVersion { get; set; } = RootCauseEngine.Version;

    /// <summary><c>Comparison</c> or <c>Model</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCauseOrigins"/>.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>When the analysis was computed: the comparison's computation time, so the same inputs give the same result.</summary>
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>The pricing basis in words, for the cost captions.</summary>
    public string PricingBasis { get; set; } = string.Empty;

    /// <summary><c>catalog</c> (today's prices) or <c>snapshot</c> (each run's stored prices).</summary>
    public string PricingBasisKind { get; set; } = string.Empty;

    /// <summary>The token of the comparison's reference model; null in model scope.</summary>
    public string? ReferenceToken { get; set; }

    /// <summary>The models analyzed: the subject first in model scope, letter order in comparison scope.</summary>
    public List<RootCauseModel> Models { get; set; } = new();

    /// <summary>The controls a run- or battery-completion analysis used, most preferred first; empty otherwise.</summary>
    public List<RootCauseControl> Controls { get; set; } = new();

    /// <summary>
    /// The cap the controls put on model-versus-instrument intelligence hypotheses: null for none, or
    /// <see cref="RootCauseGrades.Moderate"/> or <see cref="RootCauseGrades.Low"/>.
    /// </summary>
    public string? ControlLimit { get; set; }

    /// <summary>Why <see cref="ControlLimit"/> applies; null when it does not.</summary>
    public string? ControlLimitReason { get; set; }

    /// <summary>The subject's mean deficit against the controls' mean on the shared questions, in quality points.</summary>
    public double? ControlDeficitMean { get; set; }

    public double? ControlDeficitLower { get; set; }
    public double? ControlDeficitUpper { get; set; }

    /// <summary>The item-level control table: each shared question, in question order.</summary>
    public List<RootCauseControlItem> ControlItems { get; set; } = new();

    /// <summary>Numbered from 1, as the <c>rca.known.&lt;n&gt;</c> facts.</summary>
    public List<RootCauseKnownFact> KnownFacts { get; set; } = new();

    /// <summary>Every hypothesis shown in the document, by axis, then nulls first, then by grade and share.</summary>
    public List<RootCauseHypothesis> Hypotheses { get; set; } = new();

    /// <summary>One per axis, in <see cref="RootCauseAxes.All"/> order.</summary>
    public List<RootCauseAxisStatus> Axes { get; set; } = new();

    /// <summary>Ordered C, then B, then M; numbered from 1.</summary>
    public List<RootCauseRecommendedRun> RecommendedRuns { get; set; } = new();

    /// <summary>Numbered from 1, as the <c>rca.check.&lt;n&gt;</c> facts.</summary>
    public List<RootCauseCheck> Checks { get; set; } = new();

    /// <summary>A harness or suite hypothesis is graded Moderate or higher, so model-selection runs wait for it.</summary>
    public bool SystemFirst { get; set; }

    /// <summary>R1: index points at risk per model, or per difficulty band or suite of a stand-alone subject.</summary>
    public List<RootCauseChartRow> PointsAtRisk { get; set; } = new();

    /// <summary>R2: seconds per question, split into time to first token, output generation, tools, waits and other.</summary>
    public List<RootCauseChartRow> SpeedRows { get; set; } = new();

    /// <summary>R3: cost per question, split into uncached input, cache reads and writes, visible output and reasoning.</summary>
    public List<RootCauseChartRow> CostRows { get; set; } = new();

    /// <summary>The LMDI speed decomposition of each analyzed pair.</summary>
    public List<RootCausePairDecomposition> SpeedPairs { get; set; } = new();

    /// <summary>The LMDI cost decomposition of each analyzed pair.</summary>
    public List<RootCausePairDecomposition> CostPairs { get; set; } = new();

    /// <summary>The Intelligence Index difference of each analyzed pair split by the scoring weights.</summary>
    public List<RootCauseQualitySplit> QualitySplits { get; set; } = new();

    /// <summary>Each model's own timing, token, cost and failure figures, in <see cref="Models"/> order.</summary>
    public List<RootCauseModelEvidenceSummary> Evidence { get; set; } = new();

    /// <summary>The research questions of the analysis, numbered 1 to 4; only those whose axis has two comparable models.</summary>
    public List<RootCauseQuestion> Questions { get; set; } = new();

    /// <summary>Each model's measured profile, in <see cref="Models"/> order.</summary>
    public List<RootCauseModelProfile> Profiles { get; set; } = new();

    /// <summary>The highest grade the run counts allow, in a sentence; null when every model has two or more runs per suite.</summary>
    public string? GradeCap { get; set; }

    /// <summary>What the computation could not do and why, in words.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// One research question: why a model sits at an extreme of an axis (the highest or lowest Intelligence
/// Index, the slowest, the most expensive), with the hypotheses about it and its measured signals.
/// </summary>
public sealed class RootCauseQuestion
{
    /// <summary>1 highest score, 2 lowest score, 3 slowest, 4 most expensive.</summary>
    public int Number { get; set; }

    /// <summary>One of <see cref="RootCauseAxes"/>.</summary>
    public string Axis { get; set; } = string.Empty;

    /// <summary>The model the question is about, by token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary><c>Why did Model A score highest?</c></summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The best grade among <see cref="HypothesisKeys"/> other than Not supported; null when none.</summary>
    public string? Grade { get; set; }

    /// <summary>The kept non-null hypotheses about the model on the axis, best grade first.</summary>
    public List<string> HypothesisKeys { get; set; } = new();

    /// <summary>The model's top measured signals on the axis, each a descriptive phrase.</summary>
    public List<string> Signals { get; set; } = new();
}

/// <summary>
/// One model's measured profile: what it does best among the models, where it lost points, and what was
/// flagged. Every entry is a count or a measurement with its questions, never a cause.
/// </summary>
public sealed class RootCauseModelProfile
{
    public string Token { get; set; } = string.Empty;

    /// <summary>The model's letter; null for the subject of a per-model sheet.</summary>
    public string? Letter { get; set; }

    public double? QualityIndex { get; set; }

    /// <summary>The mean dimension scores, accuracy to readability; null where missing.</summary>
    public List<double?> Dimensions { get; set; } = new();

    /// <summary>At most two: a dimension the model ranks first or second on.</summary>
    public List<RootCauseProfileEntry> Strengths { get; set; } = new();

    /// <summary>At most three: where it lost most points, its weakest dimension, a dimension it ranks last on.</summary>
    public List<RootCauseProfileEntry> Weaknesses { get; set; } = new();

    /// <summary>Critical errors, answers without tools, panel disagreement, isolated misses and output defects, where they stand out.</summary>
    public List<RootCauseProfileEntry> Flags { get; set; } = new();
}

/// <summary>One measured entry of a model's profile.</summary>
public sealed class RootCauseProfileEntry
{
    /// <summary>
    /// <c>strength</c>, <c>lostPointsOn</c>, <c>weakestDimension</c>, <c>lowestRank</c>, <c>criticalErrors</c>,
    /// <c>noTool</c>, <c>panelDisagreement</c>, <c>isolatedMisses</c> or <c>outputDefects</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>The entry in a few words, for a table cell: <c>2 critical errors</c>.</summary>
    public string Short { get; set; } = string.Empty;

    /// <summary>The questions it rests on, as the sheet refers to them.</summary>
    public List<string> Questions { get; set; } = new();
}

/// <summary>One analyzed model.</summary>
public sealed class RootCauseModel
{
    /// <summary>The model's letter, or <c>subject</c>.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The model's letter; null for the subject of a per-model sheet.</summary>
    public string? Letter { get; set; }

    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string EntryKey { get; set; } = string.Empty;
    public bool IsSubject { get; set; }
    public bool IsReference { get; set; }
    public List<long> RunIds { get; set; } = new();
}

/// <summary>
/// One control of a run- or battery-completion analysis (§ A9): an earlier run of another model on the
/// same exam under the same instrument. Also what the <c>root-cause-controls</c> endpoints return.
/// </summary>
public sealed class RootCauseControl
{
    /// <summary>1-based, the <c>n</c> of <c>rca.controls.&lt;n&gt;.*</c>.</summary>
    public int Number { get; set; }

    public string ModelLabel { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }

    /// <summary>The control run of a run subject; null for a battery control.</summary>
    public long? RunId { get; set; }

    /// <summary>The control battery run of a battery subject; null for a run control.</summary>
    public long? BatteryRunId { get; set; }

    /// <summary>When the control run or battery run started, UTC.</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>Scored questions the control shares with the subject on the same item revision.</summary>
    public int SharedQuestions { get; set; }
}

/// <summary>One shared question of the item-level control table.</summary>
public sealed class RootCauseControlItem
{
    /// <summary>The subject sheet's question number.</summary>
    public int Number { get; set; }

    /// <summary><c>Q7</c>, or a battery's <c>S2-Q7</c>.</summary>
    public string Reference { get; set; } = string.Empty;

    public double SubjectScore { get; set; }
    public double ControlsMean { get; set; }

    /// <summary>Subject minus the controls' mean, in quality points.</summary>
    public double Difference { get; set; }

    /// <summary>The controls that scored the question.</summary>
    public int ControlCount { get; set; }

    /// <summary>The subject scored below 50 and so did every control.</summary>
    public bool InstrumentSuspect { get; set; }

    /// <summary>The subject scored below 50 while every control scored 75 or above.</summary>
    public bool ModelSpecific { get; set; }
}

/// <summary>A numbered sentence that states only what a test, a count, a decomposition or the configuration proves.</summary>
public sealed class RootCauseKnownFact
{
    public int Number { get; set; }

    /// <summary><c>test</c>, <c>count</c>, <c>decomposition</c> or <c>configuration</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>The fact keys it rests on.</summary>
    public List<string> FactKeys { get; set; } = new();
}

/// <summary>One hypothesis, fired by a rule of § A4 or a null of § A1, with its evidence and grade.</summary>
public sealed class RootCauseHypothesis
{
    /// <summary>The rule id: <c>HQ1</c>, <c>WS3</c>, <c>H0-Q</c>, <c>H0-W</c>, ….</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>1-based among the hypotheses of the same rule.</summary>
    public int Number { get; set; }

    /// <summary><c>&lt;Id&gt;.&lt;Number&gt;</c>, the middle of its <c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.*</c> facts.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCauseAxes"/>.</summary>
    public string Axis { get; set; } = string.Empty;

    /// <summary><c>pair</c>, <c>within</c> or <c>null</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The models it names, by token, the worse model first.</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>The models in words: <c>Model C and Model F</c>.</summary>
    public string ModelsText { get; set; } = string.Empty;

    public string Statement { get; set; } = string.Empty;

    /// <summary>The statement in at most a few words, for the hypothesis chart's row label.</summary>
    public string ShortStatement { get; set; } = string.Empty;

    /// <summary>A noun phrase of at most ten words naming the rule and its models: <c>critical errors of Model F against Model E</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The loci it may point to, in the rule table's order.</summary>
    public List<string> Loci { get; set; } = new();

    /// <summary>The loci in one string: <c>model / chat</c>.</summary>
    public string Locus { get; set; } = string.Empty;

    public List<string> For { get; set; } = new();
    public List<string> Against { get; set; } = new();
    public string RefutedIf { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCauseGrades"/>.</summary>
    public string Grade { get; set; } = RootCauseGrades.Low;

    public List<string> GradeReasons { get; set; } = new();

    /// <summary>The deciding share of the difference (0 to 1), where the hypothesis has one.</summary>
    public double? Share { get; set; }

    public double? ShareLower { get; set; }
    public double? ShareUpper { get; set; }

    /// <summary>
    /// The amount of the axis' own measure the hypothesis accounts for (index points, milliseconds or USD per
    /// question): its share, at most one, of the difference or total it is a share of. Hypotheses rank by it.
    /// </summary>
    public double? Explains { get; set; }

    public List<RootCauseRobustnessCheck> Robustness { get; set; } = new();

    /// <summary>The configuration keys that differ between the models it compares, other than the one it is about.</summary>
    public List<string> Confounds { get; set; } = new();

    /// <summary>The number of the recommended run that would settle it; null when none does.</summary>
    public int? NextRunNumber { get; set; }

    /// <summary>The number of the check that would settle it; null when none does.</summary>
    public int? CheckNumber { get; set; }

    /// <summary>The fact keys it rests on.</summary>
    public List<string> FactKeys { get; set; } = new();
}

/// <summary>One robustness check of a hypothesis.</summary>
public sealed class RootCauseRobustnessCheck
{
    /// <summary><c>Leave one question out</c>, <c>Across suites</c>, <c>Replicates</c>, <c>Panel agreement</c> or <c>Configuration</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCauseCheckStatuses"/>.</summary>
    public string Status { get; set; } = RootCauseCheckStatuses.NotAssessable;

    public string Detail { get; set; } = string.Empty;
}

/// <summary>The status of one axis and the hypothesis it rests on.</summary>
public sealed class RootCauseAxisStatus
{
    public string Axis { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCauseStatuses"/>.</summary>
    public string Status { get; set; } = RootCauseStatuses.NotEstablished;

    /// <summary>The key of the best non-null hypothesis; null when none fired.</summary>
    public string? BestHypothesis { get; set; }

    public string Reason { get; set; } = string.Empty;
}

/// <summary>A check a run cannot settle: a rubric, a corpus or a configuration to look at.</summary>
public sealed class RootCauseCheck
{
    public int Number { get; set; }

    /// <summary><c>rubric</c>, <c>corpus</c>, <c>configuration</c> or <c>graders</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>The hypothesis keys it would settle.</summary>
    public List<string> HypothesisIds { get; set; } = new();
}

/// <summary>
/// A run the analysis recommends, with every launcher setting. The setting names follow
/// <c>StartBenchmarkRunRequest</c> and <c>StartBenchmarkModelBatchRequest</c>, so a launcher prefill maps
/// them one to one. A null setting is one the runs did not share or that no longer resolves; its note
/// says which.
/// </summary>
public sealed class RootCauseRecommendedRun
{
    public int Number { get; set; }

    /// <summary>One of <see cref="RootCauseRunKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>One of <see cref="RootCausePurposes"/>.</summary>
    public string Purpose { get; set; } = RootCausePurposes.ModelSelection;

    /// <summary>One of <see cref="RootCauseMarks"/>.</summary>
    public string Mark { get; set; } = RootCauseMarks.Optional;

    /// <summary>The hypothesis keys it settles.</summary>
    public List<string> HypothesisIds { get; set; } = new();

    /// <summary>"Confirms … if …; refutes it if …", stated before the run.</summary>
    public string Decides { get; set; } = string.Empty;

    /// <summary>The comparability the run is predicted to have against the analyzed runs, from the keys it changes.</summary>
    public string PredictedTier { get; set; } = string.Empty;

    public double? EstimatedCostUsd { get; set; }
    public double? EstimatedMinutes { get; set; }

    /// <summary>A model-selection run marked to wait until the system hypotheses are settled.</summary>
    public bool AfterSystemHypotheses { get; set; }

    /// <summary>When to make the run's passes, in words (<c>start each run at a different time of day</c>); null for any time.</summary>
    public string? When { get; set; }

    /// <summary><c>Suite</c> or <c>Battery</c>, as <c>BenchmarkModelBatchTargetKind</c> names them.</summary>
    public string TargetKind { get; set; } = "Suite";

    public long? SuiteId { get; set; }
    public string? SuiteName { get; set; }
    public long? BatteryId { get; set; }
    public string? BatteryName { get; set; }

    /// <summary><c>Single</c> (one model) or <c>Batch</c> (a model batch).</summary>
    public string RunMode { get; set; } = "Single";

    /// <summary>The models under test, by configuration id, in order.</summary>
    public List<long> TestedModelConfigurationIds { get; set; } = new();

    /// <summary>The models' names, aligned with <see cref="TestedModelConfigurationIds"/>.</summary>
    public List<string> ModelNames { get; set; } = new();

    /// <summary><c>Randomized</c> or <c>AsListed</c>, as <c>BenchmarkModelBatchOrder</c> names them.</summary>
    public string Order { get; set; } = "Randomized";

    /// <summary>Runs per model on a suite, or runs per suite on a battery.</summary>
    public int RunsPerModel { get; set; } = 1;

    /// <summary>The Number of Runs of a single-model run.</summary>
    public int RunCount { get; set; } = 1;

    public long? ScoringProfileId { get; set; }
    public bool? VerboseMode { get; set; }
    public bool? AllowSourceCodeReferences { get; set; }
    public long? AssessorModelConfigurationId { get; set; }
    public long? CoAssessorModelConfigurationId { get; set; }
    public long? SecondOpinionAssessorModelConfigurationId { get; set; }

    /// <summary>The second-opinion coverage mode, as <c>StartBenchmarkRunRequest.SecondOpinionMode</c> takes it.</summary>
    public int? SecondOpinionMode { get; set; }

    public long? ClaimVerifierModelConfigurationId { get; set; }

    /// <summary>Always null: a recommended run never writes documents of its own.</summary>
    public long? ReportWriterModelConfigurationId { get; set; }

    public List<string> Notes { get; set; } = new();
}

/// <summary>One bar of a server-drawn chart: a model, or a difficulty band or suite of a stand-alone subject.</summary>
public sealed class RootCauseChartRow
{
    /// <summary>The model's name, or the band's or suite's.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The model's letter where peers exist; null for the subject and for a band or suite row.</summary>
    public string? Letter { get; set; }

    /// <summary>The model's token; null for a band or suite row.</summary>
    public string? Token { get; set; }

    /// <summary><c>model</c>, <c>band</c> or <c>suite</c>.</summary>
    public string Kind { get; set; } = "model";

    /// <summary><c>points</c>, <c>seconds</c> or <c>usd</c>.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>In stacking order; empty when the row is unavailable.</summary>
    public List<RootCauseSegment> Segments { get; set; } = new();

    /// <summary>The sum of the segments; null when the row is unavailable.</summary>
    public double? Total { get; set; }

    /// <summary>Why the row has no bar; null when it has one.</summary>
    public string? UnavailableReason { get; set; }
}

/// <summary>One segment of a chart row.</summary>
public sealed class RootCauseSegment
{
    /// <summary>One of <see cref="RootCauseSegments"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
}

/// <summary>An exact decomposition of one axis' difference between two models, first minus second.</summary>
public sealed class RootCausePairDecomposition
{
    /// <summary><see cref="RootCauseAxes.Speed"/> or <see cref="RootCauseAxes.Cost"/>.</summary>
    public string Axis { get; set; } = string.Empty;

    public string First { get; set; } = string.Empty;
    public string Second { get; set; } = string.Empty;

    /// <summary>Questions both models have the figures for.</summary>
    public int SharedQuestions { get; set; }

    /// <summary>First minus second, per question: milliseconds of model time, or USD.</summary>
    public double? Difference { get; set; }

    public double? DifferenceLower { get; set; }
    public double? DifferenceUpper { get; set; }

    /// <summary>The first model's and the second's per-question totals over the shared questions.</summary>
    public double? FirstTotal { get; set; }

    public double? SecondTotal { get; set; }

    /// <summary>The components; their effects sum exactly to <see cref="Difference"/>.</summary>
    public List<RootCauseComponent> Components { get; set; } = new();

    /// <summary>Why the decomposition is missing; null when it is present.</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>
    /// The difference is not established, so it is not decomposed: the difference and its interval are
    /// stated, the components are not.
    /// </summary>
    public bool NotEstablished { get; set; }
}

/// <summary>One component of a decomposition.</summary>
public sealed class RootCauseComponent
{
    /// <summary>
    /// Speed: <c>callCount</c>, <c>perCallLatency</c>, <c>outputTokens</c>, <c>decodeRate</c>, <c>gap</c>;
    /// cost: a cost component; intelligence: a dimension or <c>other</c>.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Cost only: <c>volume</c> or <c>price</c>; null otherwise.</summary>
    public string? Effect { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>The component's part of the difference, in the decomposition's unit.</summary>
    public double Amount { get; set; }

    /// <summary>Its share of the difference; null when the difference is zero.</summary>
    public double? Share { get; set; }

    /// <summary>The 95 % bootstrap percentile interval of the share over questions.</summary>
    public double? ShareLower { get; set; }

    public double? ShareUpper { get; set; }
}

/// <summary>The Intelligence Index difference of a pair split by the scoring weights into dimension contributions.</summary>
public sealed class RootCauseQualitySplit
{
    public string First { get; set; } = string.Empty;
    public string Second { get; set; } = string.Empty;

    /// <summary>First minus second, in index points.</summary>
    public double IndexDifference { get; set; }

    /// <summary>One per dimension, then <c>other</c> (the critical-error cap and difficulty weighting); they sum exactly to the difference.</summary>
    public List<RootCauseComponent> Components { get; set; } = new();
}

/// <summary>One model's own figures, for the evidence tables.</summary>
public sealed class RootCauseModelEvidenceSummary
{
    public string Token { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public int ScoredAnswers { get; set; }
    public int NoToolAnswers { get; set; }
    public int BudgetOrDefectAnswers { get; set; }
    public int CriticalAnswers { get; set; }

    /// <summary>The model's median answer quality, the reference of the points at risk.</summary>
    public double? MedianQuality { get; set; }

    /// <summary>Index points at risk by segment (<see cref="RootCauseSegments.AtRisk"/>), each answer counted once.</summary>
    public List<RootCauseSegment> AtRisk { get; set; } = new();

    /// <summary>Answers with candidate call telemetry.</summary>
    public int TelemetryAnswers { get; set; }

    /// <summary>Mean seconds per question by segment (<see cref="RootCauseSegments.Time"/>).</summary>
    public List<RootCauseSegment> Time { get; set; } = new();

    public double? ModelTimeMs { get; set; }
    public double? CallsPerAnswer { get; set; }
    public double? TtftPerCallP50Ms { get; set; }
    public double? DecodeTokensPerSecond { get; set; }
    public double? ReasoningShare { get; set; }
    public double? OutputTokens { get; set; }
    public double? ReasoningTokens { get; set; }

    /// <summary>Mean USD per question by component (<see cref="RootCauseSegments.CostComponents"/>).</summary>
    public List<RootCauseSegment> Cost { get; set; } = new();

    /// <summary>Mean tokens per question by component.</summary>
    public List<RootCauseSegment> Tokens { get; set; } = new();

    public double? CacheReadShare { get; set; }

    public string? TimeUnavailableReason { get; set; }
    public string? CostUnavailableReason { get; set; }
}
