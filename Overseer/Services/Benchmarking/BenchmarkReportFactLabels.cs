namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// The human label of every fact key <see cref="BenchmarkReportFacts"/> defines, so a rendered
/// document never prints a raw key and one figure never has two names. Explicit entries cover the
/// fixed keys; rules cover the patterned dimension, band, authored-band and peer keys. A key neither covers gets a
/// readable fallback: its dotted parts split at camel case and joined as words.
/// </summary>
public static class BenchmarkReportFactLabels
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Identity and configuration
        ["subject.label"] = "Model under test",
        ["subject.provider"] = "Provider of the model under test",
        ["subject.modelId"] = "Model ID of the model under test",
        ["subject.thinkingLevel"] = "Thinking level",
        ["subject.runs"] = "Runs",
        ["subject.state"] = "Comparability state",
        ["suite.name"] = "Suite",
        ["suite.questions"] = "Questions",
        ["config.chat"] = "Chat configuration under test",
        ["comparison.models"] = "Models compared",
        ["comparison.peerRuns"] = "Runs per peer",
        ["comparison.pricingBasis"] = "Pricing basis",
        ["comparison.pricingBasisKind"] = "Pricing basis kind",
        ["comparison.pricedOn"] = "Catalog price date",
        ["comparison.signature"] = "Comparability signature",

        // Comparison scope: the fixed keys of BenchmarkComparisonReportFacts
        ["comparison.reference"] = "Reference model",
        ["comparison.runsPerSuite"] = "Runs per suite",
        ["comparison.suiteCount"] = "Suites",
        ["comparison.memberRuns"] = "Member runs per model",
        ["comparison.configDifferences"] = "Configuration differences across the models",
        ["frontier.qualityCost"] = "Intelligence against cost frontier",
        ["frontier.qualitySpeed"] = "Intelligence against speed frontier",
        ["frontier.speedCost"] = "Speed against cost frontier",
        ["spread.quality"] = "Intelligence Index spread",
        ["spread.speed"] = "Median answer time spread",
        ["spread.cost"] = "Cost per question spread",
        ["questions.anyCriticalError"] = "Questions with a critical error",
        ["questions.wideSpread"] = "Questions with a wide spread between models",
        ["questions.sharedLow"] = "Questions every model scored low",
        ["notable.count"] = "Notable findings",

        // Quality
        ["quality.index"] = "Intelligence Index",
        ["quality.interval"] = "95% interval",
        ["quality.intervalSpan"] = "Interval span",
        ["quality.intervalBasis"] = "Interval basis",
        ["quality.rank"] = "Intelligence rank",
        ["quality.intervalOverlap"] = "Interval overlap",
        ["quality.peerMedian"] = "Peer median Intelligence Index",
        ["quality.peerBest"] = "Best peer Intelligence Index",
        ["quality.rawIndex"] = "Intelligence Index before the critical-error cap",
        ["quality.unweightedMean"] = "Unweighted mean answer quality",
        ["quality.scoredItems"] = "Scored questions",
        ["quality.withheldRuns"] = "Runs with a withheld index",

        // Speed
        ["speed.modelTimeP50"] = "Median answer time",
        ["speed.modelTimeMean"] = "Mean answer time",
        ["speed.modelTimeP90"] = "90th-percentile answer time",
        ["speed.ttftP50"] = "Median time to first token",
        ["speed.rank"] = "Speed rank",

        // Cost and tokens
        ["cost.perQuestion"] = "Cost per question",
        ["cost.perRun"] = "Cost per run",
        ["cost.rank"] = "Cost rank",
        ["cost.basis"] = "Cost basis",
        ["cost.pricingAsOf"] = "Price card date",
        ["cost.totalRunPerRun"] = "Total cost per run (every grading and synthesis role; report writer excluded)",
        ["tokens.inputPerQuestion"] = "Input tokens per question",
        ["tokens.outputPerQuestion"] = "Output tokens per question",

        // Root-cause evidence (RootCauseEvidence)
        ["speed.ttftP90"] = "90th-percentile time to first token",
        ["speed.toolTimeP50"] = "Median tool time per answer",
        ["speed.modelCallsPerAnswer"] = "Model calls per answer",
        ["speed.ttftPerCallP50"] = "Median time to first token per model call",
        ["speed.decodeRate"] = "Median visible decode rate",
        ["speed.ownWaitShare"] = "Own waits and retries, share of model time",
        ["speed.retriesPerAnswer"] = "Retried attempts per answer",
        ["speed.servedTierMismatch"] = "Answers served at another tier than requested",
        ["tokens.reasoningPerQuestion"] = "Reasoning tokens per question",
        ["tokens.reasoningShare"] = "Reasoning share of output tokens",
        ["tokens.cacheReadShare"] = "Cache-read share of input tokens",
        ["price.cardsDiffer"] = "Price cards differ between the runs",
        ["servedTier"] = "Served service tier",
        ["score.withTools"] = "Mean score of answers with a tool call",
        ["score.withoutTools"] = "Mean score of answers without a tool call",
        ["outcome.confirmedCriticalRate"] = "Confirmed critical-error rate",
        ["config.serviceTier"] = "Requested service tier",
        ["config.reasoningMode"] = "Reasoning mode",
        ["config.parallelMode"] = "Parallel execution mode",
        ["config.maxOutputTokens"] = "Output token cap",

        // Root cause (RootCauseFacts)
        ["rca.controls.count"] = "Controls",
        ["rca.controls.providers"] = "Providers of the controls",
        ["rca.controls.limit"] = "Control limit",
        ["rca.control.deficit.mean"] = "Mean difference from the controls",
        ["rca.control.deficit.interval"] = "95% interval of the mean difference from the controls",
        ["rca.control.instrumentSuspect"] = "Questions every control also missed",
        ["rca.control.modelSpecific"] = "Questions only the model under test missed",
        ["rca.systemFirst"] = "System hypotheses first",
        ["rca.pricingBasis"] = "Pricing basis of the cost decomposition",
        ["rca.anomaly.count"] = "Unexpected results",
        ["rca.anomaly.priorsChecked"] = "Unexpected results: priors checked",
        ["rca.anomaly.untestedPairs"] = "Unexpected results: pairs without a paired test",

        // Battery (BenchmarkBatteryReportFacts)
        ["battery.memberRuns"] = "Member runs",
        ["battery.excludedMembers"] = "Member runs left out of the analysis",

        // Errors and claims
        ["answers.scored"] = "Scored answers",
        ["errors.critical"] = "Critical errors",
        ["claims.supported"] = "Claims the verifier supported",
        ["claims.refuted"] = "Claims the verifier refuted (the answers' own claims)",
        ["claims.indeterminate"] = "Claims the verifier could not decide",
        ["claims.refutedAnswerSentences"] = "Refuted answer sentences, accused sentences included",

        // Tools
        ["tools.callsPerQuestion"] = "Tool calls per question",
        ["tools.callsPerQuestion.peerMean"] = "Peer mean tool calls per question",
        ["tools.share.sourceCode"] = "Source code share",
        ["tools.share.wiki"] = "Wiki share",
        ["tools.share.structuredLookup"] = "Structured lookup share",
        ["tools.share.knowledgeBase"] = "Knowledge base share",
        ["tools.share.other"] = "Other tools share",
        ["tools.zeroKnowledgeBaseAnswers"] = "Answers without a knowledge-base article",
        ["tools.failed"] = "Failed tool calls",
        ["tools.refusedByBudget"] = "Calls refused by the tool budget",

        // Panel
        ["panel.meanAbsDelta"] = "Panel mean absolute difference",
        ["panel.icc"] = "Intraclass correlation, ICC(A,1)",
        ["panel.disagreements"] = "Panel disagreements",
        ["panel.memberAAlone"] = "Panel member A alone",
        ["panel.memberBAlone"] = "Panel member B alone",
        ["panel.referenceReaderIndex"] = "Reference reader (advisory, third provider)",
        ["panel.referenceReaderOffset"] = "Reference reader's mean offset from the panel",
        ["panel.judgeDependentPairs"] = "Judge-dependent pairs",
        ["panel.sameProviderDelta"] = "Same-provider panel member's reading",
        ["rcaDigest.keyFinding"] = "Root Cause Analysis: key finding",
        ["rcaDigest.limits"] = "Root Cause Analysis: limits of the analysis",

        // Style
        ["style.responseStyleConflict"] = "Response-style conflict",

        // Scoring and provenance
        ["scoring.weights"] = "Dimension weights",
        ["scoring.levels"] = "Level scores",
        ["scoring.criticalErrorCap"] = "Critical-error cap",
        ["scoring.methodVersion"] = "Scoring method version",
        ["run.harnessVersion"] = "Harness version",
        ["run.ids"] = "Run IDs",
        ["run.dates"] = "Run dates",
        ["run.promptSha256"] = "System prompt SHA-256 prefix",
        ["run.toolGuidesSha256"] = "Tool guides SHA-256 prefix",

        // Provider availability
        ["availability.attempts"] = "Valid attempts",
        ["availability.providerErrors"] = "Provider errors",
        ["availability.providerErrorRate"] = "Provider error rate",
        ["availability.providerErrorRateCi95Low"] = "Lower bound of the provider error rate's 95% interval",
        ["availability.providerErrorRateCi95High"] = "Upper bound of the provider error rate's 95% interval",
        ["availability.unattributedErrors"] = "Unattributed errors",
        ["availability.inclusiveRate"] = "Error rate including unattributed errors (upper bound)",
        ["availability.inclusiveRateCi95Low"] = "Lower bound of the 95% interval of the error rate including unattributed errors",
        ["availability.inclusiveRateCi95High"] = "Upper bound of the 95% interval of the error rate including unattributed errors",
        ["availability.overloaded"] = "Provider overloaded (HTTP 503 and 529)",
        ["availability.serverErrors"] = "Provider server errors (HTTP 5xx)",
        ["availability.timeouts"] = "Provider timeouts",
        ["availability.streamFailures"] = "Provider stream failures",
        ["availability.rateLimitedOurs"] = "Rate limited by our own limits (HTTP 429, excluded)",
        ["availability.rateLimitedUnattributed"] = "Rate limited, unattributed (HTTP 429)",
        ["availability.transportUnattributed"] = "Transport failures, unattributed",
        ["availability.ourSideFailures"] = "Our-side failures (excluded)",
        ["availability.questionsLost"] = "Questions lost to provider errors",
        ["availability.reruns"] = "Re-runs needed",
        ["availability.providerReruns"] = "Re-runs needed for provider errors",
        ["availability.replacedRuns"] = "Replaced runs",
        ["availability.oneIn"] = "Provider errors as a natural frequency",

        // Chat consistency: the fixed keys of BenchmarkChatConsistencyReportFacts
        ["subject.serviceTier"] = "Service tier",
        ["analysis.id"] = "Analysis ID",
        ["analysis.name"] = "Analysis name",
        ["analysis.inputSha256"] = "Analysis input SHA-256 prefix",
        ["analysis.codeVersion"] = "Analysis code version",
        ["analysis.relaxedPooling"] = "Relaxed pooling across a measurement change",
        ["analysis.compared"] = "Compared",
        ["analysis.writtenOutOfDate"] = "Written from an out-of-date analysis",
        ["analysis.savedAt"] = "Analysis saved",
        ["verdict.overall"] = "Overall verdict on the chat",
        ["verdict.short"] = "Outcome in short",
        ["sample.minimumUnits"] = "Minimum units per period",
        ["sample.minimumDays"] = "Minimum days per period",
        ["sample.minimumPairedItems"] = "Minimum paired items",
        ["sample.met"] = "Minimum sample",
        ["sample.shortfall"] = "Shortfall from the minimum sample",
        ["eventGroups.count"] = "Overseer updates",
        ["verdict.quality"] = "Verdict on quality",
        ["verdict.headline"] = "Analysis headline",
        ["verdict.reliabilityIncreases"] = "Established reliability increases",
        ["scope.hours"] = "Hours the comparison holds for",
        ["scope.excludedShare"] = "Timed answers outside the common hours",
        ["scope.oneTimeStratum"] = "Single common time stratum",
        ["coverage.strata.count"] = "Common time strata",
        ["coverage.usBusinessHours"] = "US business hours covered",
        ["coverage.outsideBusinessHours"] = "Hours outside US business hours covered",
        ["protocol.version"] = "Protocol version",
        ["protocol.label"] = "Protocol",
        ["protocol.alpha"] = "Significance level (alpha)",
        ["protocol.overridden"] = "Protocol overrides",
        ["n.targetRuns"] = "Runs of the model under test",
        ["n.controlRuns"] = "Control runs",
        ["n.answers"] = "Answers analyzed",
        ["quality.commonGrader"] = "Common grader",
        ["grader.drift.count"] = "Grader drift checks",
        ["robustness.count"] = "Robustness checks",
        ["robustness.failed"] = "Failed robustness checks",
        ["identity.changed"] = "Served model changed",
        ["identity.servedModels"] = "Served model IDs, both periods",
        ["serving.speeds"] = "Served speeds, both periods",
        ["ownWaits.share"] = "Own-wait share of model time, both periods",
        ["ownWaits.retries"] = "Retry attempts, both periods",
        ["serving.configurationDiffers"] = "Served configuration differs from the request",
        ["serving.timeOfDayAssessable"] = "Time of day assessable",
        ["pricing.source"] = "Price card source",
        ["pricing.asOf"] = "Price card date",
        ["pricing.card"] = "Price card",
        ["events.count"] = "Overseer events",
        ["controls.count"] = "Control models",
        ["controls.missing.count"] = "Missing controls",
        ["did.count"] = "Difference-in-differences estimates",
        ["annotation.count"] = "Annotations",
        ["annotation.providerConfirmed"] = "Provider-confirmed causes",
        ["attribution.count"] = "Attributions",
        ["limitation.count"] = "Limitations",
        ["limitation.dataQuality.count"] = "Data-quality notes",
        ["nextRuns.count"] = "Suggested next runs",
        ["requestIds.sample.count"] = "Sample request IDs",
    };

    /// <summary>The primary endpoints of a chat consistency analysis by id.</summary>
    private static readonly IReadOnlyDictionary<string, string> EndpointNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["P1"] = "Quality",
        ["P2"] = "Time to first answer text",
        ["P3"] = "Answer streaming rate",
        ["P4"] = "Work per turn",
        ["P5"] = "Cost per question",
        ["P6"] = "Provider error rate",
    };

    /// <summary>The periods of a chat consistency analysis.</summary>
    private static readonly IReadOnlyDictionary<string, string> PeriodNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["baseline"] = "Baseline",
        ["comparison"] = "Comparison",
    };

    /// <summary>The reliability rates of a chat consistency analysis by id; another id reads as its words.</summary>
    private static readonly IReadOnlyDictionary<string, string> ReliabilityNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["terminalFailures"] = "Terminal failures",
        ["timeouts"] = "Timeouts",
        ["emptyAnswers"] = "Empty answers",
        ["refusals"] = "Refusals",
        ["toolBudgetExhausted"] = "Tool-budget exhaustion",
        ["http429"] = "429 responses",
        ["http5xx"] = "5xx responses",
    };

    /// <summary>The secondary families of a chat consistency analysis by id; another id reads as its words.</summary>
    private static readonly IReadOnlyDictionary<string, string> FamilyNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["reasoningTokens"] = "Reasoning tokens",
        ["answerLength"] = "Answer length",
        ["netModelTime"] = "Net model time",
        ["shiftFunction"] = "Shift function",
        ["timeOfDay"] = "Time-of-day contrast",
        ["generationRate"] = "Implied generation rate",
    };

    /// <summary>
    /// The last part of a patterned chat consistency key, as the words after the colon of its label:
    /// <c>endpoint.P1.ci95</c> reads <c>Quality (P1): 95% interval</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ChatSuffixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "name",
        ["estimate"] = "change",
        ["percent"] = "change in percent",
        ["baseline"] = "baseline",
        ["comparison"] = "comparison",
        ["ci95"] = "95% interval",
        ["ci95Low"] = "lower bound of the 95% interval",
        ["ci95High"] = "upper bound of the 95% interval",
        ["ci90"] = "90% interval",
        ["p"] = "p-value",
        ["adjustedP"] = "adjusted p-value",
        ["rejected"] = "rejected at the false discovery rate",
        ["items"] = "paired items",
        ["runs"] = "runs",
        ["verdict"] = "verdict",
        ["grade"] = "evidence grade",
        ["mde"] = "minimum detectable effect",
        ["mdeNote"] = "note on the minimum detectable effect",
        ["runsForMargin"] = "runs per period to reach the margin",
        ["legacyProxy"] = "legacy proxy",
        ["commonGrader"] = "common grader",
        ["minimumSampleMet"] = "minimum sample",
        ["note"] = "note",
        ["at"] = "time",
        ["kind"] = "kind",
        ["label"] = "description",
        ["from"] = "from",
        ["to"] = "to",
        ["change"] = "what changed",
        ["changes"] = "what changed",
        ["run"] = "run",
        ["previousRun"] = "previous run",
        ["series"] = "series",
        ["model"] = "model",
        ["sameProvider"] = "provider relation",
        ["periods"] = "periods matched",
        ["endpoint"] = "endpoint",
        ["controlChange"] = "control's own change",
        ["controlChangeCi95"] = "95% interval of the control's own change",
        ["includesZero"] = "interval relative to zero",
        ["separatesTarget"] = "separates the model under test",
        ["movedSameWay"] = "control moved the same way",
        ["status"] = "status",
        ["detail"] = "detail",
        ["servedModels"] = "served model IDs",
        ["calls"] = "candidate calls",
        ["tierMismatchCalls"] = "calls served at another tier",
        ["fallbackCalls"] = "calls served by a fallback model",
        ["speeds"] = "served speeds",
        ["share"] = "own-wait share of model time",
        ["permitWait"] = "permit wait",
        ["backoffWait"] = "backoff wait",
        ["retries"] = "retry attempts",
        ["answersWithTelemetry"] = "answers with call telemetry",
        ["text"] = "text",
        ["provider"] = "provider",
        ["source"] = "source",
        ["side"] = "side",
        ["rule"] = "decision-table rule",
        ["endpoints"] = "endpoints",
        ["evidence"] = "evidence",
        ["period"] = "period",
        ["suite"] = "suite",
        ["suggestion"] = "suggestion",
        ["targetRun"] = "run of the model under test",
        ["reason"] = "reason",
        ["repeatRun"] = "run whose setup to repeat",
        ["start"] = "start",
        ["end"] = "end",
        ["window"] = "window",
        ["days"] = "days",
        ["units"] = "units compared",
        ["unitNoun"] = "unit",
        ["memberRuns"] = "member runs",
        ["answers"] = "answers",
        ["legacyRuns"] = "runs without call telemetry",
        ["suites"] = "suites",
        ["increased"] = "increased",
        ["establishedIncrease"] = "established increase",
        ["anchorRun"] = "anchor run",
        ["grader"] = "grader",
        ["earliest"] = "earliest re-grade",
        ["latest"] = "latest re-grade",
        ["drift"] = "drift",
        ["withinMargin"] = "within the margin",
        ["tag"] = "tag",
        ["lastAt"] = "last time",
        ["hours"] = "hours",
        ["notComputedKind"] = "why not computed",
        ["batteryRun"] = "battery run",
        ["buildReplaced"] = "build replaced",
        ["summary"] = "summary",
    };

    /// <summary>The <c>level.&lt;period&gt;.&lt;measure&gt;</c> measures of a chat consistency sheet.</summary>
    private static readonly IReadOnlyDictionary<string, string> LevelMeasureNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["answers"] = "answers",
        ["quality"] = "mean answer score",
        ["overallIndex"] = "battery Overall Index",
        ["timeToFirstAnswerText"] = "median time to first answer text",
        ["streamingRate"] = "median answer streaming rate",
        ["outputTokens"] = "mean output tokens per answer",
        ["costPerQuestion"] = "mean cost per question",
        ["failedAnswers"] = "failed answers",
    };

    /// <summary>The <c>availability.&lt;period&gt;.&lt;measure&gt;</c> measures of a chat consistency sheet.</summary>
    private static readonly IReadOnlyDictionary<string, string> AvailabilityPeriodMeasureNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attempts"] = "valid attempts",
        ["providerErrors"] = "provider errors",
        ["providerErrorRate"] = "provider error rate",
        ["providerErrorRateCi95Low"] = "provider error rate, 95% interval low",
        ["providerErrorRateCi95High"] = "provider error rate, 95% interval high",
        ["unattributedErrors"] = "unattributed errors",
        ["ourSideFailures"] = "our-side failures (excluded)",
        ["reruns"] = "re-runs needed",
        ["providerReruns"] = "re-runs for provider errors",
        ["recordedRuns"] = "runs that recorded attempts",
        ["availabilityOnlyRuns"] = "availability-only runs",
    };

    /// <summary>
    /// The labels of a peer's own facts (<c>peer.&lt;letter&gt;.&lt;suffix&gt;</c>) by suffix, <c>{0}</c> standing
    /// for <c>Model &lt;letter&gt;</c>. A label never names the peer; the renderer resolves the naming.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PeerLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["quality.index"] = "{0}'s Intelligence Index",
        ["quality.interval"] = "{0}'s 95% interval",
        ["quality.rank"] = "{0}'s intelligence rank",
        ["speed.medianSeconds"] = "{0}'s median answer time",
        ["cost.perQuestion"] = "{0}'s cost per question",
        ["runs"] = "{0}'s runs",
        ["intervalOverlap"] = "Interval overlap with {0}",
        ["pairedDifference"] = "Paired difference from {0}",
        ["pairedInterval"] = "Paired-bootstrap interval against {0}",
        ["pairedExcludesZero"] = "Paired interval against {0} relative to zero",
        ["sharedQuestions"] = "Questions shared with {0}",
    };

    /// <summary>
    /// The labels of the comparison-scope keys a covered model has beyond its per-model sheet's
    /// (<c>model.&lt;letter&gt;.&lt;suffix&gt;</c>), by suffix; any other suffix is labeled as its per-model key.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ModelLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runs"] = "Runs",
        ["state"] = "Comparability state",
        ["thinkingLevel"] = "Thinking level",
        ["frontier"] = "Frontier",
        ["profile.answers"] = "Scored answers in the failure profile",
        ["profile.mayMislead"] = "Answers that may mislead",
        ["profile.mayMisleadPer20"] = "Answers that may mislead, per 20 answers",
        ["profile.outcome.correct"] = "Correct answers",
        ["profile.outcome.partial"] = "Partially correct answers",
        ["profile.outcome.incorrect"] = "Incorrect answers",
        ["profile.outcome.notAttempted"] = "Answers not attempted",
        ["profile.outcome.noAnswer"] = "Empty answers",
        ["profile.outcome.wrongInsteadOfAbstaining"] = "Wrong instead of abstaining",
        ["profile.truncated"] = "Truncated answers",
        ["profile.outputDefects"] = "Answers with output defects",
        ["profile.noToolAnswers"] = "Answers without a tool call",
        ["profile.toolLimitHits"] = "Tool-limit hits",
        ["profile.answerChars.median"] = "Median answer length",
        ["profile.isolatedMisses"] = "Isolated misses",
        ["profile.sharedMisses"] = "Shared misses",
        ["profile.lowestScorer"] = "Questions scored lowest of all models",
        ["profile.findings.runsWithSynthesis"] = "Runs with structured grader findings",
        ["vsReference.shared"] = "Questions shared with the reference",
        ["vsReference.better"] = "Questions scored more than 15 points above the reference",
        ["vsReference.similar"] = "Questions scored within 15 points of the reference",
        ["vsReference.worse"] = "Questions scored more than 15 points below the reference",
    };

    /// <summary>The synthesis finding categories as a weakness's words: <c>Runs with an accuracy weakness</c>.</summary>
    private static readonly IReadOnlyDictionary<string, string> FindingCategoryWeaknesses = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accuracy"] = "an accuracy weakness",
        ["completeness"] = "a completeness weakness",
        ["conciseness"] = "a conciseness weakness",
        ["readability"] = "a readability weakness",
        ["critical_error"] = "a critical-error weakness",
        ["tool_use"] = "a tool-use weakness",
        ["other"] = "a weakness of another kind",
    };

    /// <summary>A synthesis finding category as a weakness's words, <c>an accuracy weakness</c>; the category itself when unknown.</summary>
    public static string FindingCategoryWeakness(string category)
        => FindingCategoryWeaknesses.TryGetValue(category, out var weakness) ? weakness : "a " + category + " weakness";

    /// <summary>The labels of a pair's facts (<c>pair.&lt;L&gt;.&lt;M&gt;.&lt;suffix&gt;</c>) by suffix.</summary>
    private static readonly IReadOnlyDictionary<string, string> PairLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["intervalOverlap"] = "Interval overlap",
        ["sharedQuestions"] = "Questions both answered",
        ["quality.difference"] = "Paired difference",
        ["quality.interval"] = "Paired interval",
        ["speed.ratio"] = "Time ratio",
        ["cost.ratio"] = "Spend ratio",
        ["configDifferences"] = "Configuration differences",
    };

    /// <summary>The axes of a pair's paired-test results, with their names.</summary>
    private static readonly IReadOnlyDictionary<string, string> PairAxisNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["quality"] = "Intelligence",
        ["speed"] = "Speed",
        ["cost"] = "Cost",
    };

    /// <summary>The parts of a notable finding's facts (<c>notable.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> NotableParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["kind"] = "kind",
        ["models"] = "models",
        ["questions"] = "questions",
        ["summary"] = "summary",
    };

    private static readonly IReadOnlyDictionary<string, string> DimensionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accuracy"] = "Accuracy",
        ["completeness"] = "Completeness",
        ["conciseness"] = "Conciseness",
        ["readability"] = "Readability",
    };

    private static readonly IReadOnlyDictionary<string, string> BandNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["simple"] = "Simple",
        ["intermediate"] = "Intermediate",
        ["advanced"] = "Advanced",
    };

    /// <summary>The dimension keys in report order, with their names.</summary>
    public static IReadOnlyList<(string Key, string Name)> Dimensions { get; } =
        new[] { "accuracy", "completeness", "conciseness", "readability" }.Select(d => (d, DimensionNames[d])).ToList();

    /// <summary>The difficulty band keys in report order, with their names.</summary>
    public static IReadOnlyList<(string Key, string Name)> Bands { get; } =
        new[] { "simple", "intermediate", "advanced" }.Select(b => (b, BandNames[b])).ToList();

    /// <summary>The label of a fact key; a readable fallback for a key no entry or rule covers.</summary>
    public static string Label(string key)
        => TryLabel(key, out string label) ? label : Fallback(key);

    /// <summary>The label from an explicit entry or a patterned-key rule; false for a key neither covers.</summary>
    public static bool TryLabel(string? key, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.Trim();

        if (Labels.TryGetValue(key, out var explicitLabel))
        {
            label = explicitLabel;
            return true;
        }

        string[] parts = key.Split('.');

        // dimension.<d>, dimension.<d>.peerMean, dimension.<d>.difference, dimension.<d>.topLevel
        if (parts[0] == "dimension" && parts.Length is 2 or 3 && DimensionNames.TryGetValue(parts[1], out var dimension))
        {
            string? suffix = parts.Length == 2 ? " score" : parts[2] == "topLevel" ? ": answers at level 6" : Comparison(parts[2]);
            if (suffix == null) return false;
            label = dimension + suffix;
            return true;
        }

        // band.<b>.questions, band.<b>.score, band.<b>.peerMean, band.<b>.difference
        if (parts[0] == "band" && parts.Length == 3 && BandNames.TryGetValue(parts[1], out var band))
        {
            string? suffix = parts[2] switch
            {
                "questions" => " band questions",
                "score" => " band score",
                _ => Comparison(parts[2]) is string s ? " band" + s : null
            };
            if (suffix == null) return false;
            label = band + suffix;
            return true;
        }

        // bands.authored.<b>
        if (parts[0] == "bands" && parts.Length == 3 && parts[1] == "authored" && BandNames.TryGetValue(parts[2], out var authoredBand))
        {
            label = "Questions authored as " + authoredBand;
            return true;
        }

        // profile.*: a per-model sheet's failure profile
        if (parts[0] == "profile" && TryProfileLabel(key, out label)) return true;

        // question.Q<n>.memberA, question.Q<n>.memberB: a question's score under one panel member alone
        if (parts[0] == "question" && parts.Length == 3 && parts[1].Length > 1 && parts[1][0] == 'Q' && IsIndex(parts[1][1..])
            && parts[2] is "memberA" or "memberB")
        {
            label = parts[1] + ": panel member " + parts[2][^1] + "'s score";
            return true;
        }

        // cost.component.<c>.perQuestion, price.<c>.perMillion
        if (parts.Length == 4 && parts[0] == "cost" && parts[1] == "component" && parts[3] == "perQuestion" && CostComponentNames.TryGetValue(parts[2], out var costComponent))
        {
            label = "Cost per question: " + costComponent;
            return true;
        }
        if (parts.Length == 3 && parts[0] == "price" && parts[2] == "perMillion" && CostComponentNames.TryGetValue(parts[1], out var priceComponent))
        {
            label = "Price per million tokens: " + priceComponent;
            return true;
        }

        if (parts[0] == "rca" && TryRootCauseLabel(parts, out label)) return true;

        // rcaDigest.axis.<axis>, rcaDigest.hypothesis.<n>, rcaDigest.lead.<n>, rcaDigest.nextRun.<n>, rcaDigest.check.<n>
        if (parts[0] == "rcaDigest" && parts.Length == 3)
        {
            if (parts[1] == "axis" && RootCauseAxisNames.TryGetValue(parts[2], out var digestAxis))
            {
                label = "Root Cause Analysis, " + digestAxis + ": status";
                return true;
            }
            if (IsIndex(parts[2]))
            {
                string? item = parts[1] switch
                {
                    "hypothesis" => "hypothesis",
                    "lead" => "writer's lead",
                    "nextRun" => "next run",
                    "check" => "check",
                    _ => null
                };
                if (item != null)
                {
                    label = "Root Cause Analysis: " + item + " " + parts[2];
                    return true;
                }
            }
        }

        // peer.<letter>.<key>: another model's own figure
        if (parts[0] == "peer" && parts.Length >= 3 && parts[1].Length > 0 && parts[1].All(char.IsAsciiLetterUpper))
        {
            string suffix = string.Join('.', parts.Skip(2));
            if (PeerLabels.TryGetValue(suffix, out var peerLabel))
            {
                label = string.Format(CultureInfo.InvariantCulture, peerLabel, "Model " + parts[1]);
                return true;
            }

            if (TryLabel(suffix, out var inner))
            {
                label = "Model " + parts[1] + ": " + LowerFirst(inner);
                return true;
            }
        }

        if (TryComparisonLabel(parts, out label)) return true;

        return TryChatConsistencyLabel(parts, out label);
    }

    /// <summary>
    /// The label of a comparison-scope model key's suffix, <c>model.&lt;letter&gt;.&lt;suffix&gt;</c>: its own
    /// entry, else its per-model key's label, else the readable fallback.
    /// </summary>
    public static string ModelSuffixLabel(string suffix)
        => TryModelSuffixLabel(suffix, out string label) ? label : Fallback(suffix);

    private static bool TryModelSuffixLabel(string suffix, out string label)
        => TryProfileLabel(suffix, out label) || TryLabel(suffix, out label);

    /// <summary>
    /// The label of a comparison-scope model suffix of <see cref="ModelLabels"/>, or of a failure-profile key
    /// (<c>profile.*</c>), which a per-model sheet carries bare; false for any other key.
    /// </summary>
    private static bool TryProfileLabel(string suffix, out string label)
    {
        if (ModelLabels.TryGetValue(suffix, out var own))
        {
            label = own;
            return true;
        }

        label = string.Empty;
        string[] parts = suffix.Split('.');

        // profile.deficit.<d>
        if (parts.Length == 3 && parts[0] == "profile" && parts[1] == "deficit" && DimensionNames.TryGetValue(parts[2], out var dimension))
        {
            label = "Answers at level 2 or below on " + dimension.ToLowerInvariant();
            return true;
        }

        // profile.top.<d>
        if (parts.Length == 3 && parts[0] == "profile" && parts[1] == "top" && DimensionNames.TryGetValue(parts[2], out var topDimension))
        {
            label = "Answers at level 6 on " + topDimension.ToLowerInvariant();
            return true;
        }

        // profile.findings.<category>
        if (parts.Length == 3 && parts[0] == "profile" && parts[1] == "findings" && FindingCategoryWeaknesses.TryGetValue(parts[2], out var weakness))
        {
            label = "Runs with " + weakness + " in the grader findings";
            return true;
        }

        return false;
    }

    /// <summary>The cost components of the root-cause evidence, as a label names them.</summary>
    private static readonly IReadOnlyDictionary<string, string> CostComponentNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uncachedInput"] = "uncached input",
        ["cacheRead"] = "cache reads",
        ["cacheWrite"] = "cache writes",
        ["output"] = "visible output",
        ["reasoning"] = "reasoning",
        ["outputWithThinking"] = "output, thinking included",
    };

    /// <summary>The parts of a root-cause hypothesis's facts (<c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> HypothesisParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["statement"] = "statement",
        ["label"] = "label",
        ["ref"] = "reference",
        ["axis"] = "axis",
        ["models"] = "models",
        ["locus"] = "locus",
        ["grade"] = "confidence grade",
        ["gradeReasons"] = "reasons for the grade",
        ["for"] = "evidence for",
        ["against"] = "evidence against",
        ["refutedIf"] = "refuted if",
        ["nextRun"] = "next run",
        ["check"] = "check",
        ["share"] = "share of the difference",
        ["shareOf"] = "what the share is of",
        ["interval"] = "95% interval of the share",
        ["priceCard"] = "price effect, known from the price cards",
    };

    /// <summary>The parts of a research question's facts (<c>rca.question.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> QuestionParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["text"] = "question",
        ["model"] = "model",
        ["neighbor"] = "model the place is measured against",
        ["status"] = "answer strength",
        ["hypotheses"] = "hypotheses about the model",
        ["signals"] = "measured signals",
        ["withinOnly"] = "answered only by the model's own breakdown",
        ["orderEstablished"] = "order against the neighbor established",
    };

    /// <summary>The unindexed parts of a model profile's facts (<c>rca.profile.&lt;T&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> ProfileParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["gainedPointsOn"] = "gained points on",
        ["lostPointsOn"] = "lost points on",
        ["weakestDimension"] = "weakest dimension",
        ["lowestRank"] = "lowest rank",
        ["flags"] = "flags",
    };

    /// <summary>The parts of an unexpected result's facts (<c>rca.anomaly.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> AnomalyParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ref"] = "reference",
        ["summary"] = "summary",
        ["prior"] = "prior it goes against",
        ["confounds"] = "configuration differences",
        ["graders"] = "scores under each grader alone",
        ["settledBy"] = "settled by",
        ["tested"] = "paired test made",
    };

    /// <summary>The parts of a control's facts (<c>rca.controls.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> ControlParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["model"] = "model",
        ["runId"] = "run",
        ["batteryRunId"] = "battery run",
        ["date"] = "date",
        ["provider"] = "provider",
        ["sharedQuestions"] = "shared questions",
    };

    /// <summary>The parts of a grader-family gap's facts (<c>rca.graders.familyGap.&lt;n&gt;.&lt;part&gt;</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> FamilyGapParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["providers"] = "provider pair, first minus second",
        ["underA"] = "quality gap under panel member A alone",
        ["underB"] = "quality gap under panel member B alone",
        ["contrast"] = "interaction contrast, member A's gap minus member B's (a panel score is the mean of two members, so a contrast of d moves a cross-provider pair by about d ÷ 2)",
        ["notable"] = "interaction contrast's 95% interval excludes zero",
    };

    /// <summary>The axes of the root cause, as a label names them.</summary>
    private static readonly IReadOnlyDictionary<string, string> RootCauseAxisNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["intelligence"] = "Intelligence",
        ["speed"] = "Speed",
        ["cost"] = "Cost",
    };

    /// <summary>The components of a speed decomposition, as a label names them.</summary>
    private static readonly IReadOnlyDictionary<string, string> SpeedComponentNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["callCount"] = "number of model calls",
        ["perCallLatency"] = "latency per call",
        ["outputTokens"] = "output tokens",
        ["decodeRate"] = "decode rate",
        ["gap"] = "time outside the calls",
    };

    /// <summary>The segments of the points at risk and of the time per question, as a label names them.</summary>
    private static readonly IReadOnlyDictionary<string, string> SegmentNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["noTool"] = "answers without tools",
        ["budgetOrDefect"] = "budget, truncation or output defects",
        ["criticalError"] = "confirmed critical errors",
        ["otherLow"] = "other low-scoring answers",
        ["ttft"] = "time to first token",
        ["outputGeneration"] = "output generation",
        ["toolExecution"] = "tool execution",
        ["ownWaits"] = "own waits and retries",
        ["other"] = "other time",
    };

    /// <summary>A model token as a label names it: <c>Model C</c>, or the model under test for <c>subject</c>; null for anything else.</summary>
    private static string? TokenName(string token)
        => token == "subject" ? "the model under test" : IsLetter(token) ? "Model " + token : null;

    /// <summary>The patterned <c>rca.*</c> keys of <see cref="Overseer.Services.Benchmarking.RootCause.RootCauseFacts"/>.</summary>
    private static bool TryRootCauseLabel(string[] parts, out string label)
    {
        label = string.Empty;
        if (parts.Length < 2) return false;

        switch (parts[1])
        {
            // rca.axis.<axis>.status, rca.axis.<axis>.best
            case "axis" when parts.Length == 4 && RootCauseAxisNames.TryGetValue(parts[2], out var axis):
                label = parts[3] switch
                {
                    "status" => "Root cause, " + axis + ": status",
                    "best" => "Root cause, " + axis + ": best hypothesis",
                    _ => string.Empty
                };
                return label.Length > 0;

            // rca.known.<n>
            case "known" when parts.Length == 3 && IsIndex(parts[2]):
                label = "Known fact " + parts[2];
                return true;

            // rca.hypothesis.<id>.<n>.<part>
            case "hypothesis" when parts.Length == 5 && IsIndex(parts[3]) && HypothesisParts.TryGetValue(parts[4], out var part):
                label = "Hypothesis " + parts[2] + " (" + parts[3] + "): " + part;
                return true;

            // rca.graders.familyGap.count, rca.graders.familyGap.<n>.<part>
            case "graders" when parts.Length >= 4 && parts[2] == "familyGap":
                if (parts.Length == 4 && parts[3] == "count")
                {
                    label = "Grader-family gaps: provider pairs";
                    return true;
                }
                if (parts.Length == 5 && IsIndex(parts[3]) && FamilyGapParts.TryGetValue(parts[4], out var familyPart))
                {
                    label = "Grader-family gap " + parts[3] + ": " + familyPart;
                    return true;
                }
                return false;

            // rca.question.<n>.<part>
            case "question" when parts.Length == 4 && IsIndex(parts[2]) && QuestionParts.TryGetValue(parts[3], out var questionPart):
                label = "Research question " + parts[2] + ": " + questionPart;
                return true;

            // rca.profile.<T>.<part>, rca.profile.<T>.strength.<n>, rca.profile.<T>.flag.<n>: Model C's profile, which a named copy names
            case "profile" when parts.Length >= 4 && TokenName(parts[2]) is string profiled:
                string profile = char.ToUpperInvariant(profiled[0]) + profiled[1..] + "'s profile: ";
                if (parts.Length == 4 && ProfileParts.TryGetValue(parts[3], out var profilePart))
                {
                    label = profile + profilePart;
                    return true;
                }
                if (parts.Length == 5 && parts[3] is "strength" or "flag" && IsIndex(parts[4]))
                {
                    label = profile + parts[3] + " " + parts[4];
                    return true;
                }
                return false;

            // rca.speed.<F>.<S>.total, .decomposed, rca.speed.<F>.<S>.<component>.share or .interval
            case "speed" when parts.Length >= 5 && TokenName(parts[2]) is string first && TokenName(parts[3]) is string second:
                if (parts.Length == 5 && parts[4] == "total")
                {
                    label = "Model time difference per question, " + first + " against " + second;
                    return true;
                }
                if (parts.Length == 5 && parts[4] == "decomposed")
                {
                    label = "Model time difference per question, " + first + " against " + second + ": split into parts";
                    return true;
                }
                if (parts.Length == 6 && SpeedComponentNames.TryGetValue(parts[4], out var speedComponent) && parts[5] is "share" or "interval")
                {
                    label = "Speed decomposition, " + first + " against " + second + ": " + speedComponent + (parts[5] == "share" ? " share" : " share, 95% interval");
                    return true;
                }
                return false;

            // rca.cost.<F>.<S>.total, .decomposed, .grouped, .volumeRatio,
            // rca.cost.<F>.<S>.<component>.<volume|price>.share or .interval (component: a cost component, input or output
            // of a grouped pair, or all), rca.cost.<F>.<S>.<input|all>.caching.share or .interval
            case "cost" when parts.Length >= 5 && TokenName(parts[2]) is string first && TokenName(parts[3]) is string second:
                if (parts.Length == 5 && parts[4] == "total")
                {
                    label = "Cost difference per question, " + first + " against " + second;
                    return true;
                }
                if (parts.Length == 5 && parts[4] == "decomposed")
                {
                    label = "Cost difference per question, " + first + " against " + second + ": split into parts";
                    return true;
                }
                if (parts.Length == 5 && parts[4] == "grouped")
                {
                    label = "Cost difference per question, " + first + " against " + second + ": split into input and output";
                    return true;
                }
                if (parts.Length == 5 && parts[4] == "volumeRatio")
                {
                    label = "Cost difference per question, " + first + " against " + second + ": cost ratio at equal prices";
                    return true;
                }
                if (parts.Length == 7 && parts[6] is "share" or "interval"
                    && ((parts[5] is "volume" or "price" && (parts[4] is "all" or "input" || CostComponentNames.ContainsKey(parts[4])))
                        || (parts[5] == "caching" && parts[4] is "all" or "input")))
                {
                    string component = parts[4] switch
                    {
                        "all" => "every component",
                        "input" => "input (uncached input, cache reads and cache writes)",
                        "output" => "output (visible output, or output with reasoning in an input-and-output split)",
                        _ => CostComponentNames[parts[4]]
                    };
                    string effect = parts[5] switch
                    {
                        "volume" => "token volume",
                        "caching" => "caching",
                        _ => "price per token"
                    };
                    label = "Cost decomposition, " + first + " against " + second + ": " + component + ", " + effect
                            + (parts[6] == "share" ? " share" : " share, 95% interval");
                    return true;
                }
                return false;

            // rca.quality.<F>.<S>.total, rca.quality.<F>.<S>.dimension.<d>.points, rca.quality.<F>.<S>.other.points
            case "quality" when parts.Length >= 5 && TokenName(parts[2]) is string first && TokenName(parts[3]) is string second:
                if (parts.Length == 5 && parts[4] == "total")
                {
                    label = "Intelligence Index difference, " + first + " against " + second;
                    return true;
                }
                if (parts.Length == 7 && parts[4] == "dimension" && DimensionNames.TryGetValue(parts[5], out var dimension) && parts[6] == "points")
                {
                    label = "Intelligence Index difference, " + first + " against " + second + ": " + dimension + " contribution";
                    return true;
                }
                if (parts.Length == 6 && parts[4] == "other" && parts[5] == "points")
                {
                    label = "Intelligence Index difference, " + first + " against " + second + ": critical-error cap, weighting and spread";
                    return true;
                }
                return false;

            // rca.atRisk.<T>.<segment>, rca.atRisk.<T>.total: an upper bound, never points lost
            case "atRisk" when parts.Length == 4 && TokenName(parts[2]) is string model:
                if (parts[3] == "total")
                {
                    label = "Index points at risk, " + model + " (upper bound)";
                    return true;
                }
                if (SegmentNames.TryGetValue(parts[3], out var segment))
                {
                    label = "Index points at risk, " + model + ": " + segment + " (upper bound)";
                    return true;
                }
                return false;

            // rca.within.<T>.time.total, .time.<segment>.seconds or .share, .cost.total, .cost.<component>.share,
            // .speed.burstAnswers; the time to first token segment is the wait before output summed over a question's calls
            case "within" when parts.Length >= 5 && TokenName(parts[2]) is string own:
                if (parts.Length == 5 && parts[4] == "total" && parts[3] is "time" or "cost")
                {
                    label = (parts[3] == "time" ? "Time per question, " : "Cost per question, ") + own;
                    return true;
                }
                if (parts.Length == 5 && parts[3] == "speed" && parts[4] == "burstAnswers")
                {
                    label = "Answers whose visible text arrived in one burst, " + own;
                    return true;
                }
                if (parts.Length == 6 && parts[3] == "time" && parts[4] == "ttft" && parts[5] is "seconds" or "share")
                {
                    label = "Wait before output, summed over the model calls of a question, " + own + (parts[5] == "share" ? ": share of the time per question" : string.Empty);
                    return true;
                }
                if (parts.Length == 6 && parts[3] == "time" && SegmentNames.TryGetValue(parts[4], out var timeSegment) && parts[5] is "seconds" or "share")
                {
                    label = "Time per question, " + own + ": " + timeSegment + (parts[5] == "share" ? " share" : string.Empty);
                    return true;
                }
                if (parts.Length == 6 && parts[3] == "cost" && CostComponentNames.TryGetValue(parts[4], out var costComponent) && parts[5] == "share")
                {
                    label = "Cost per question, " + own + ": " + costComponent + " share";
                    return true;
                }
                return false;

            // rca.rule.<id>.shareSpan, rca.rule.<id>.count
            case "rule" when parts.Length == 4 && parts[2].Length > 0 && parts[3] is "shareSpan" or "count":
                label = "Hypotheses of rule " + parts[2] + (parts[3] == "shareSpan" ? ": span of their shares" : ": how many");
                return true;

            // rca.anomaly.<n>.<part>
            case "anomaly" when parts.Length == 4 && IsIndex(parts[2]) && AnomalyParts.TryGetValue(parts[3], out var anomalyPart):
                label = "Unexpected result " + parts[2] + ": " + anomalyPart;
                return true;

            // rca.controls.<n>.<part>
            case "controls" when parts.Length == 4 && IsIndex(parts[2]) && ControlParts.TryGetValue(parts[3], out var controlPart):
                label = "Control " + parts[2] + ": " + controlPart;
                return true;

            // rca.run.<n>.summary, rca.run.<n>.purpose, rca.check.<n>, rca.check.<n>.summary
            case "run" when parts.Length == 4 && IsIndex(parts[2]) && parts[3] == "summary":
                label = "Recommended run " + parts[2];
                return true;
            case "run" when parts.Length == 4 && IsIndex(parts[2]) && parts[3] == "purpose":
                label = "Recommended run " + parts[2] + ", purpose";
                return true;
            case "check" when parts.Length == 3 && IsIndex(parts[2]):
                label = "Check " + parts[2];
                return true;
            case "check" when parts.Length == 4 && IsIndex(parts[2]) && parts[3] == "summary":
                label = "Check " + parts[2] + ", in short";
                return true;
        }

        return false;
    }

    /// <summary>
    /// The patterned keys of a comparison-scope sheet: a covered model's figure (<c>model.&lt;L&gt;.*</c>), a
    /// pair's (<c>pair.&lt;L&gt;.&lt;M&gt;.*</c>), a dimension's spread and a notable finding's parts.
    /// </summary>
    private static bool TryComparisonLabel(string[] parts, out string label)
    {
        label = string.Empty;

        // model.<letter>.<suffix>
        if (parts[0] == "model" && parts.Length >= 3 && IsLetter(parts[1]))
        {
            if (!TryModelSuffixLabel(string.Join('.', parts.Skip(2)), out var inner)) return false;
            label = "Model " + parts[1] + ": " + LowerFirst(inner);
            return true;
        }

        // pair.<L>.<M>.<suffix>
        if (parts[0] == "pair" && parts.Length >= 4 && IsLetter(parts[1]) && IsLetter(parts[2]))
        {
            string suffix = string.Join('.', parts.Skip(3));
            string? pair = PairLabels.TryGetValue(suffix, out var fixedLabel)
                ? fixedLabel
                : parts.Length == 5 && PairAxisNames.TryGetValue(parts[3], out var axis)
                    ? FamilyResult(axis, parts[4])
                    : parts.Length == 6 && parts[3] == "dimension" && DimensionNames.TryGetValue(parts[4], out var pairDimension)
                        ? FamilyResult(pairDimension, parts[5])
                        : null;
            if (pair == null) return false;
            label = "Model " + parts[1] + " against Model " + parts[2] + ": " + LowerFirst(pair);
            return true;
        }

        // spread.dimension.<d>
        if (parts[0] == "spread" && parts.Length == 3 && parts[1] == "dimension" && DimensionNames.TryGetValue(parts[2], out var dimension))
        {
            label = dimension + " spread";
            return true;
        }

        // notable.<n>.<part>; the summary is the finding itself
        if (parts[0] == "notable" && parts.Length == 3 && IsIndex(parts[1]) && NotableParts.TryGetValue(parts[2], out var part))
        {
            label = parts[2] == "summary" ? "Notable finding " + parts[1] : "Notable finding " + parts[1] + ": " + part;
            return true;
        }

        return false;
    }

    /// <summary>A paired-test result's label in one family: <c>Speed result against the reference</c>; null for an unknown family.</summary>
    private static string? FamilyResult(string name, string family) => family switch
    {
        BenchmarkComparisonReportFacts.ReferenceFamilyName => name + " result against the reference",
        BenchmarkComparisonReportFacts.AllPairsFamilyName => name + " result among all pairs",
        "short" => name + " result in short",
        _ => null
    };

    /// <summary>A model letter: <c>A</c> to <c>Z</c>, <c>AA</c>, ….</summary>
    private static bool IsLetter(string part) => part.Length > 0 && part.All(char.IsAsciiLetterUpper);

    /// <summary>
    /// The patterned keys of a chat consistency sheet: <c>&lt;prefix&gt;.&lt;suffix&gt;</c>, the prefix
    /// naming the figure and the suffix one of <see cref="ChatSuffixes"/>; indexed prefixes count from 1.
    /// </summary>
    private static bool TryChatConsistencyLabel(string[] parts, out string label)
    {
        label = string.Empty;
        string? head = null;
        int suffixAt = parts.Length - 1;

        switch (parts[0])
        {
            // endpoint.<P>.<suffix>
            case "endpoint" when parts.Length == 3 && EndpointNames.TryGetValue(parts[1], out var endpoint):
                head = endpoint + " (" + parts[1] + ")";
                break;

            // protocol.margin.<P>
            case "protocol" when parts.Length == 3 && parts[1] == "margin" && EndpointNames.TryGetValue(parts[2], out var margined):
                label = "Equivalence margin of " + LowerFirst(margined) + " (" + parts[2] + ")";
                return true;

            // period.<baseline|comparison>.<suffix>, identity.…, serving.…, ownWaits.…
            case "period" or "identity" or "serving" or "ownWaits" when parts.Length == 3 && PeriodNames.TryGetValue(parts[1], out var period):
                head = period + " period" + parts[0] switch
                {
                    "identity" => ", served model",
                    "serving" => ", served configuration",
                    "ownWaits" => ", Overseer's own waits",
                    _ => string.Empty
                };
                break;

            // level.<baseline|comparison>.<measure>
            case "level" when parts.Length == 3 && PeriodNames.TryGetValue(parts[1], out var levelPeriod) && LevelMeasureNames.TryGetValue(parts[2], out var measure):
                label = levelPeriod + " period level: " + measure;
                return true;

            // availability.<baseline|comparison>.<measure>; a run sheet's availability.<x> keys are two-part and labeled explicitly
            case "availability" when parts.Length == 3 && PeriodNames.TryGetValue(parts[1], out var availabilityPeriod)
                                     && AvailabilityPeriodMeasureNames.TryGetValue(parts[2], out var availabilityMeasure):
                label = availabilityPeriod + " period: " + availabilityMeasure;
                return true;

            // robustness.<check>.summary
            case "robustness" when parts.Length == 3 && parts[2] == "summary" && parts[1].Length > 0 && !IsIndex(parts[1]):
                label = "Robustness check " + LowerFirst(Fallback(parts[1])) + ": summary";
                return true;

            // coverage.strata.<n>
            case "coverage" when parts.Length == 3 && parts[1] == "strata" && IsIndex(parts[2]):
                label = "Common time stratum " + parts[2];
                return true;

            // quality.dimensions.<d>.<suffix>, quality.criticalErrors.<suffix>
            case "quality" when parts.Length == 4 && parts[1] == "dimensions" && DimensionNames.TryGetValue(parts[2], out var level):
                head = level + " level";
                break;
            case "quality" when parts.Length == 3 && parts[1] == "criticalErrors":
                head = "Critical-error rate";
                break;

            // flip.<suffix>
            case "flip" when parts.Length == 2:
                head = "Per-item flips against the null flip rate";
                break;

            // grader.drift.<n>.<suffix>
            case "grader" when parts.Length == 4 && parts[1] == "drift" && IsIndex(parts[2]):
                head = "Grader drift check " + parts[2];
                break;

            // reliability.<id>.<suffix>
            case "reliability" when parts.Length == 3 && parts[1].Length > 0:
                head = ReliabilityNames.TryGetValue(parts[1], out var rate) ? rate : Fallback(parts[1]);
                break;

            // tools.<id>.<suffix>
            case "tools" when parts.Length == 3 && parts[1].Length > 0:
                head = "Tool use, " + LowerFirst(Fallback(parts[1]));
                break;

            // secondary.<family>.note, secondary.<family>.<id>.<suffix>
            case "secondary" when parts.Length == 3 && parts[2] == "note" && parts[1].Length > 0:
                head = FamilyName(parts[1]);
                break;
            case "secondary" when parts.Length == 4 && parts[1].Length > 0 && parts[2].Length > 0:
                head = FamilyName(parts[1]) + ", " + LowerFirst(Fallback(parts[2]));
                break;

            // events.<n>.<suffix>, did.<n>.<suffix>, robustness.<n>.<suffix>, annotation.<n>.<suffix>,
            // attribution.<n>.<suffix>, nextRuns.<n>.<suffix>
            case "events" or "eventGroups" or "did" or "robustness" or "annotation" or "attribution" or "nextRuns" when parts.Length == 3 && IsIndex(parts[1]):
                head = parts[0] switch
                {
                    "events" => "Overseer event ",
                    "eventGroups" => "Overseer update ",
                    "did" => "Difference in differences ",
                    "robustness" => "Robustness check ",
                    "annotation" => "Annotation ",
                    "attribution" => "Attribution ",
                    _ => "Suggested next run "
                } + parts[1];
                break;

            // controls.<n>.<suffix>, controls.missing.<n>.<suffix>
            case "controls" when parts.Length == 3 && IsIndex(parts[1]):
                head = "Control model " + parts[1];
                break;
            case "controls" when parts.Length == 4 && parts[1] == "missing" && IsIndex(parts[2]):
                head = "Missing control " + parts[2];
                break;

            // limitation.<n>, limitation.dataQuality.<n>, requestIds.sample.<n>
            case "limitation" when parts.Length == 2 && IsIndex(parts[1]):
                label = "Limitation " + parts[1];
                return true;
            case "limitation" when parts.Length == 3 && parts[1] == "dataQuality" && IsIndex(parts[2]):
                label = "Data-quality note " + parts[2];
                return true;
            case "requestIds" when parts.Length == 3 && parts[1] == "sample" && IsIndex(parts[2]):
                label = "Sample request ID " + parts[2];
                return true;
        }

        if (head == null || !ChatSuffixes.TryGetValue(parts[suffixAt], out var suffix)) return false;
        label = head + ": " + suffix;
        return true;
    }

    private static string FamilyName(string family)
        => FamilyNames.TryGetValue(family, out var name) ? name : Fallback(family);

    /// <summary>A 1-based index: digits, no leading zero.</summary>
    private static bool IsIndex(string part)
        => part.Length > 0 && part[0] != '0' && part.All(char.IsAsciiDigit);

    private static string? Comparison(string suffix) => suffix switch
    {
        "peerMean" => " peer mean",
        "difference" => " difference from the peer mean",
        _ => null
    };

    /// <summary>"foo.barBaz" as "Foo bar baz".</summary>
    private static string Fallback(string? key)
    {
        var words = new List<string>();
        foreach (string part in (key ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var word = new StringBuilder();
            for (int i = 0; i < part.Length; i++)
            {
                char c = part[i];
                bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(part[i - 1]) || char.IsDigit(part[i - 1]));
                if (boundary)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
                word.Append(c);
            }
            if (word.Length > 0) words.Add(word.ToString());
        }

        if (words.Count == 0) return "Figure";

        string text = string.Join(" ", words.Select(w => w.All(char.IsUpper) && w.Length > 1 ? w : w.ToLowerInvariant()));
        return char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
    }

    /// <summary>Terms <see cref="LowerFirst"/> leaves capitalized at the start of a label.</summary>
    private static readonly string[] ProperTerms = { "Intelligence Index" };

    private static string LowerFirst(string text)
        => ProperTerms.Any(t => text.StartsWith(t, StringComparison.Ordinal))
            ? text
            : text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
                ? char.ToLowerInvariant(text[0]) + text[1..]
                : text;
}
