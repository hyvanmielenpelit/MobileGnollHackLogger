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

        // Quality
        ["quality.index"] = "Intelligence Index",
        ["quality.interval"] = "95 % interval",
        ["quality.intervalSpan"] = "Interval span",
        ["quality.intervalBasis"] = "Interval basis",
        ["quality.rank"] = "Intelligence rank",
        ["quality.intervalOverlap"] = "Interval overlap",
        ["quality.peerMedian"] = "Peer median Intelligence Index",
        ["quality.peerBest"] = "Best peer Intelligence Index",
        ["quality.rawIndex"] = "Intelligence Index before the critical-error cap",
        ["quality.unweightedMean"] = "Unweighted mean answer quality",
        ["quality.scoredItems"] = "Scored questions",

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
    /// <c>endpoint.P1.ci95</c> reads <c>Quality (P1): 95 % interval</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ChatSuffixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "name",
        ["estimate"] = "change",
        ["percent"] = "change in percent",
        ["baseline"] = "baseline",
        ["comparison"] = "comparison",
        ["ci95"] = "95 % interval",
        ["ci95Low"] = "lower bound of the 95 % interval",
        ["ci95High"] = "upper bound of the 95 % interval",
        ["ci90"] = "90 % interval",
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
        ["controlChangeCi95"] = "95 % interval of the control's own change",
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

    /// <summary>
    /// The labels of a peer's own facts (<c>peer.&lt;letter&gt;.&lt;suffix&gt;</c>) by suffix, <c>{0}</c> standing
    /// for <c>Model &lt;letter&gt;</c>. A label never names the peer; the renderer resolves the naming.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PeerLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["quality.index"] = "{0}'s Intelligence Index",
        ["quality.interval"] = "{0}'s 95 % interval",
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

        // dimension.<d>, dimension.<d>.peerMean, dimension.<d>.difference
        if (parts[0] == "dimension" && parts.Length is 2 or 3 && DimensionNames.TryGetValue(parts[1], out var dimension))
        {
            string? suffix = parts.Length == 2 ? " score" : Comparison(parts[2]);
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

        return TryChatConsistencyLabel(parts, out label);
    }

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

    private static string LowerFirst(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;
}
