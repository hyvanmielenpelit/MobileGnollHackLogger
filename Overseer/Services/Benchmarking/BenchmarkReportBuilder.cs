namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public static class BenchmarkReportBuilder
{
    private static string Inv(IFormattable value, string? format = null)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A UTC timestamp in the report's own fixed shape. Needed because ":" in a custom format
    /// string is the culture's *time separator* rather than a literal, so an interpolated
    /// "{d:yyyy-MM-dd HH:mm:ss}" renders "08.30.00" under fi-FI — the culture these machines
    /// actually run. A report is an artifact that gets compared across machines; its timestamps
    /// have to look the same on all of them.
    /// </summary>
    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static readonly Regex BlockedCallsRegex =
        new(@"\((\d+)\s+blocked by budget\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fraction of a question's tool call budget above which the question is reported as having
    // run under budget pressure, short of actually exhausting it.
    private const double BudgetPressureFraction = 0.90;

    // Below this speed target, a scoring profile is an interactive-latency profile, and a
    // high/max thinking candidate scores against it in a way that says more about the profile
    // than about the model.
    private const int InteractiveSpeedTargetMaxMs = 30000;

    /// <summary>
    /// The speed constants this run was actually scored with, read back from the run's own
    /// profile snapshot so a report always describes the run in front of it rather than
    /// today's default profile. Falls back to the defaults when a run carries no snapshot.
    /// </summary>
    private static BenchmarkScoringConstants ScoringConstantsOf(BenchmarkRun run)
    {
        return BenchmarkScoring.ConstantsFromSnapshot(run.ScoringProfileSnapshotJson);
    }

    // Same fence-splitting pattern as BenchmarkAnswerSanitizer.CodeBlockRegex: a "#" inside a
    // fenced block is a comment in someone's example, not a heading, and must not be rewritten.
    private static readonly Regex CodeFenceRegex = new(@"(```[\s\S]*?```)", RegexOptions.Compiled);

    // An ATX heading line: 1-6 "#" markers followed by whitespace or end of line. Group 1 is the
    // marker run, group 2 is everything after it (including the leading space, if any) so the
    // replacement can rebuild the line with a different marker length and identical content.
    private static readonly Regex AtxHeadingLineRegex =
        new(@"^(#{1,6})(?=\s|$)(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Demotes every ATX heading in a model's answer so it nests strictly under the report's own
    /// question heading. Model answers routinely emit their own top-level headings — on the
    /// 2026-09-03 run one answer opened with <c>## GnollHack's spell schools</c>, a sibling of
    /// the report's own <c>## 3. Questions and Replies</c>, and another opened with
    /// <c>### Available roles</c>, a sibling of the answer's own <c>### Question N</c> heading —
    /// which corrupts every outline view of the exported Markdown.
    ///
    /// Only fenced code blocks are protected; everything else outside a fence is a candidate.
    /// The shallowest heading level present is found first, and if it is already at or below
    /// <paramref name="minLevel"/> (i.e. deeper or equal), the text is returned byte-identical —
    /// this function only ever pushes headings deeper, never shallower.
    ///
    /// Setext headings (a line of text followed by a line of <c>===</c> or <c>---</c>) are
    /// deliberately out of scope: none appear in the corpus, and telling a setext underline apart
    /// from a Markdown table separator or a horizontal rule is a larger change with more ways to
    /// be wrong than this fix justifies.
    /// </summary>
    internal static string DemoteAnswerHeadings(string answerText, int minLevel)
    {
        if (string.IsNullOrEmpty(answerText))
        {
            return answerText;
        }

        var parts = CodeFenceRegex.Split(answerText);

        int shallowest = int.MaxValue;
        for (int i = 0; i < parts.Length; i += 2) // Even indices are outside fences.
        {
            foreach (Match m in AtxHeadingLineRegex.Matches(parts[i]))
            {
                int level = m.Groups[1].Value.Length;
                if (level < shallowest) shallowest = level;
            }
        }

        if (shallowest == int.MaxValue || shallowest >= minLevel)
        {
            // No heading found, or the shallowest one is already at or beyond minLevel.
            return answerText;
        }

        int shift = minLevel - shallowest;
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1) // Inside a fenced code block: never rewritten.
            {
                sb.Append(parts[i]);
                continue;
            }

            sb.Append(AtxHeadingLineRegex.Replace(parts[i], m =>
            {
                int newLevel = Math.Min(6, m.Groups[1].Value.Length + shift);
                return new string('#', newLevel) + m.Groups[2].Value;
            }));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits an answer's tool calls into the ones that executed and the ones the budget refused.
    /// <c>ToolCallCount</c> counts attempts, so printing it against the budget produced lines like
    /// "27 of 25 calls used" — a sentence that cannot be true. The blocked figure is recorded in
    /// the tool summary the executor writes.
    /// </summary>
    private static (int Executed, int Blocked) ToolCallSplit(BenchmarkRunAnswer answer)
    {
        int attempted = answer.ToolCallCount ?? 0;
        int blocked = answer.ToolCallsBlocked ?? 0;

        if (blocked == 0 && !string.IsNullOrWhiteSpace(answer.ToolCallSummary))
        {
            var match = BlockedCallsRegex.Match(answer.ToolCallSummary);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
            {
                blocked = parsed;
            }
        }

        blocked = Math.Clamp(blocked, 0, attempted);
        return (attempted - blocked, blocked);
    }

    /// <summary>
    /// Reads the assessor's stored evidence. Returns nulls for a run graded before evidence was
    /// collected, which is every run up to harness version 3.
    /// </summary>
    private static (string? Accuracy, string? Completeness, bool CriticalErrorDemoted) ReadEvidence(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.AssessmentEvidenceJson))
        {
            return (null, null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(answer.AssessmentEvidenceJson);
            var root = doc.RootElement;

            string? accuracy = root.TryGetProperty("accuracy", out var acc) && acc.ValueKind == JsonValueKind.String
                ? acc.GetString() : null;
            string? completeness = root.TryGetProperty("completeness", out var comp) && comp.ValueKind == JsonValueKind.String
                ? comp.GetString() : null;
            bool demoted = root.TryGetProperty("criticalErrorDemoted", out var dem) && dem.ValueKind == JsonValueKind.True;

            return (accuracy, completeness, demoted);
        }
        catch (JsonException)
        {
            // Evidence is commentary, never a score input: a malformed blob costs a line of the
            // report and nothing else.
            return (null, null, false);
        }
    }

    /// <summary>"25 executed, 2 blocked, budget 25" — three numbers that mean three things.</summary>
    private static string FormatToolBudgetLine(BenchmarkRunAnswer answer)
    {
        var (executed, blocked) = ToolCallSplit(answer);
        string budget = answer.ToolCallBudgetUsed.HasValue
            ? answer.ToolCallBudgetUsed.Value.ToString(CultureInfo.InvariantCulture)
            : "not recorded";

        return blocked > 0
            ? $"{executed} executed, {blocked} blocked, budget {budget}"
            : $"{executed} executed, budget {budget}";
    }

    /// <summary>
    /// The second-opinion mode this run was actually graded under, read from the run's own
    /// stamped value. An out-of-range value falls back to <c>Flagged</c> rather than to
    /// <c>Off</c>: <c>Off</c> would claim no second verdict was configured, which is the one
    /// reading a run carrying second verdicts cannot support.
    /// </summary>
    private static BenchmarkSecondOpinionMode ModeOf(BenchmarkRun run)
    {
        return Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), run.SecondOpinionModeUsed)
            ? (BenchmarkSecondOpinionMode)run.SecondOpinionModeUsed
            : BenchmarkSecondOpinionMode.Flagged;
    }

    /// <summary>The clause that turns a mode name into a statement about coverage.</summary>
    private static string ModeGloss(BenchmarkSecondOpinionMode mode) => mode switch
    {
        BenchmarkSecondOpinionMode.All => " — every answer graded twice.",
        BenchmarkSecondOpinionMode.FlaggedAndOutliers => " — flagged answers, plus a post-scoring sweep for outliers.",
        BenchmarkSecondOpinionMode.Flagged => " — flagged answers only.",
        _ => " — no second verdict was configured; anything recorded came from a manual re-grade."
    };

    /// <summary>Trigger names as stored, in the words the report uses for them.</summary>
    private static string TriggerLabel(string trigger) => trigger switch
    {
        "CriticalError" => "critical error",
        "ContestedVerdict" => "contested verdict",
        "UnverifiedClaims" => "unverifiable claims",
        "BelowThreshold" => "score below the profile threshold",
        "Outlier" => "outlier below the run median",
        "All" => "double grading, every answer",
        "Manual" => "manual re-grade",
        _ => trigger
    };

    /// <summary>
    /// The assessor's unverifiable claims. Empty for a run graded before the field existed, and
    /// empty rather than throwing on a malformed blob: these are commentary, never a score input.
    /// </summary>
    private static IReadOnlyList<string> ReadUnverifiedClaims(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.UnverifiedClaimsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var claims = JsonSerializer.Deserialize<List<string>>(answer.UnverifiedClaimsJson);
            return claims?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static long Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return (sorted.Count % 2 != 0)
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public static string BuildMarkdownReport(BenchmarkRun run, string? overseerVersion = null, BenchmarkRunPricing? runPricing = null)
    {
        var sb = new StringBuilder();
        // Read back from the run's own profile snapshot, so the report describes the run in
        // front of it rather than whatever the default profile says today.
        var scoringConstants = ScoringConstantsOf(run);

        // 1. Introduction
        sb.AppendLine("# GnollHack Overseer AI Intelligence Benchmark Report");
        sb.AppendLine();
        string suiteDisplay = !string.IsNullOrEmpty(run.GameSnapshotNameUsed)
            ? $"suite **{run.SuiteName}** (Board: **{run.GameSnapshotNameUsed}**)"
            : $"suite **{run.SuiteName}**";
        sb.AppendLine($"This report contains the automated domain knowledge, reasoning, and efficiency benchmark results for {suiteDisplay}, evaluated against model **{run.TestedModelDisplayNameUsed}** ({run.TestedModelProviderUsed} / {run.TestedModelIdUsed}).");
        sb.AppendLine($"Run conducted on {Stamp(run.StartedAtUtc)} UTC" + (!string.IsNullOrEmpty(run.StartedByUser?.UserName) ? $" by {run.StartedByUser.UserName}." : "."));
        sb.AppendLine();
        sb.AppendLine("> *Note:* This benchmark evaluates domain-specific roguelike intelligence, codebase comprehension, and tool usage within the GnollHack Overseer harness. Scoring uses Behaviorally Anchored Rating Scales (BARS), weighted geometric aggregation, and logarithmic speed decay.");
        sb.AppendLine();

        if (!run.SuiteQuestionsReviewed)
        {
            sb.AppendLine("> ⚠️ **Unreviewed questions**: This run contains AI-generated questions that were not verified before the run. Scores may reflect rubric defects.");
            sb.AppendLine();
        }

        // 2. Run Manifest
        sb.AppendLine("## 1. Run Manifest");
        sb.AppendLine();
        sb.AppendLine($"- **Overseer Version:** {overseerVersion ?? "1.0.0"}");
        sb.AppendLine($"- **Suite Name:** {run.SuiteName}");
        if (!string.IsNullOrEmpty(run.GameSnapshotNameUsed))
        {
            string shaPrefix = run.GameSnapshotSha256Used?.Length >= 12
                ? run.GameSnapshotSha256Used[..12]
                : (run.GameSnapshotSha256Used ?? "n/a");
            sb.AppendLine($"- **Game Snapshot:** {run.GameSnapshotNameUsed} ({run.GameSnapshotCaptureMethodUsed}, {run.GameSnapshotCharCountUsed} chars, SHA-256 {shaPrefix})");
        }
        sb.AppendLine($"- **Total Questions:** {run.TotalQuestionCount}");
        sb.AppendLine($"- **Answered Questions:** {run.AnsweredQuestionCount} of {run.TotalQuestionCount}");
        sb.AppendLine($"- **Run Status:** {run.Status}");
        sb.AppendLine($"- **Start Time (UTC):** {Stamp(run.StartedAtUtc)}");
        sb.AppendLine($"- **End Time (UTC):** {(run.CompletedAtUtc.HasValue ? Stamp(run.CompletedAtUtc.Value) : "In Progress / Interrupted")}");
        sb.AppendLine($"- **Total Elapsed Wall Time:** {FormatDuration(run.TotalDurationMs)}");
        sb.AppendLine($"- **Total Candidate Answer Time:** {FormatDuration(run.TotalAnswerDurationMs)}");
        // "Question Parallelism", not "Parallel …": the Model Under Test block reports the
        // provider's parallel *tool calls* under a similar name, and one report using the same
        // word for two mechanisms is how a reader concludes a sequential run ran concurrently.
        sb.AppendLine($"- **Question Parallelism:** {run.MaxParallelQuestionsUsed} question(s) at a time" + (run.SpeedMeasurementDegraded ? " *(Speed metrics advisory due to concurrency)*" : " *(Sequential, strict timing)*"));
        sb.AppendLine();

        // Declared before the comparability block, which reports the per-question
        // tool call budgets that were actually applied.
        var answers = run.Answers.OrderBy(a => a.OrderIndex).ToList();

        // Comparability block
        sb.AppendLine("### Comparability");
        sb.AppendLine($"- **Harness Version:** {run.HarnessVersion ?? "1 (unversioned legacy)"}");
        sb.AppendLine($"- **Scoring Method Version:** {run.ScoringMethodVersion}");
        sb.AppendLine($"- **Scoring Profile:** {run.ScoringProfile?.Name ?? "Default Intelligence Profile"}");

        // A heavy-thinking candidate graded against an interactive-latency profile produces a
        // Speed Index that describes the profile more than the model: the 2026-09-03 run scored
        // a max-thinking model 65 on speed beside 91 on intelligence. Say so where the reader
        // meets the number, rather than leaving it to be inferred from the thinking level.
        string candidateThinking = run.TestedModelThinkingLevelUsed ?? string.Empty;
        bool deliberatingCandidate =
            candidateThinking.Equals("high", StringComparison.OrdinalIgnoreCase) ||
            candidateThinking.Equals("max", StringComparison.OrdinalIgnoreCase);
        if (deliberatingCandidate && scoringConstants.SpeedTargetMs < InteractiveSpeedTargetMaxMs)
        {
            sb.AppendLine($"- **Profile Fit:** this profile targets interactive latency ({Inv(scoringConstants.SpeedTargetMs, "N0")} ms) while the candidate ran at thinking level **{candidateThinking}**. The Speed Index is advisory for this run; compare it only against runs sharing both the profile and the thinking level.");
        }

        var budgetsUsed = answers
            .Where(a => a.ToolCallBudgetUsed.HasValue)
            .Select(a => a.ToolCallBudgetUsed!.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        string budgetText = budgetsUsed.Count switch
        {
            0 => run.MaxToolCallsPerQuestionUsed.HasValue
                    ? $"{run.MaxToolCallsPerQuestionUsed.Value} (flat)"
                    : "unlimited (legacy)",
            1 => budgetsUsed[0].ToString(),
            // Budgets are resolved per difficulty band, so a run has no single figure.
            _ => string.Join(" / ", budgetsUsed.Select(v => v.ToString())) + " (per difficulty band)"
        };
        sb.AppendLine($"- **Tool Call Budget per Question:** {budgetText}");
        sb.AppendLine($"- **Timing Mode:** {(run.SpeedMeasurementDegraded ? "Concurrent (advisory speed)" : "Sequential (comparable speed)")}");

        // Who graded whom. Three distinct providers is the arrangement with the fewest shared
        // priors; an assessor and a second opinion drawn from one provider is the arrangement in
        // which the "independent reader" is least independent, and a reader of the agreement
        // figures below needs to know which of the two produced them.
        var pairingProviders = new List<string?> { run.TestedModelProviderUsed, run.AssessorModelProviderUsed };
        if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            pairingProviders.Add(run.SecondOpinionAssessorModelProviderUsed);
        }
        int distinctProviders = pairingProviders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        sb.AppendLine(run.SecondOpinionAssessorModelConfigurationId.HasValue
            ? $"- **Assessor Pairing:** candidate {run.TestedModelProviderUsed}, assessor {run.AssessorModelProviderUsed}, second opinion {run.SecondOpinionAssessorModelProviderUsed} — {distinctProviders} distinct provider(s)"
            : $"- **Assessor Pairing:** candidate {run.TestedModelProviderUsed}, assessor {run.AssessorModelProviderUsed} — {distinctProviders} distinct provider(s), no second opinion");
        if (run.SecondOpinionAssessorModelConfigurationId.HasValue &&
            string.Equals(run.AssessorModelProviderUsed, run.SecondOpinionAssessorModelProviderUsed, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  - *The assessor and the second opinion come from the same provider, so the second verdict is a weaker check than a cross-provider one: two models from one family share training data and failure modes, and can agree for reasons that have nothing to do with the answer.*");
        }
        sb.AppendLine($"- **Candidate System Prompt SHA-256:** {run.CandidateSystemPromptSha256 ?? "not recorded"}");
        sb.AppendLine($"- **Candidate System Prompt Text:** {(string.IsNullOrEmpty(run.CandidateSystemPromptText) ? "not stored" : $"stored ({run.CandidateSystemPromptText.Length:N0} characters)")}");
        sb.AppendLine($"- **ToolGuides SHA-256:** {run.ToolGuidesSha256 ?? "not recorded"}");
        sb.AppendLine($"- **Knowledge Base HEAD SHA:** {run.KnowledgeBaseHeadSha ?? "not recorded"}");
        sb.AppendLine("  - *Two runs are a reproduction only when `CandidateSystemPromptSha256` matches.*");
        sb.AppendLine();

        sb.AppendLine("### Model Under Test");
        sb.AppendLine($"- **Display Name:** {run.TestedModelDisplayNameUsed}");
        sb.AppendLine($"- **Provider:** {run.TestedModelProviderUsed}");
        sb.AppendLine($"- **Model ID:** {run.TestedModelIdUsed}");
        sb.AppendLine($"- **Thinking Level:** {run.TestedModelThinkingLevelUsed ?? "Default"}");
        sb.AppendLine($"- **Reasoning Mode:** {run.TestedModelReasoningModeUsed ?? "Default"}");
        sb.AppendLine($"- **Reasoning Summary:** {run.TestedModelReasoningSummaryUsed ?? "Default"}");
        sb.AppendLine($"- **Requested Service Tier:** {run.TestedModelServiceTierUsed ?? "Default"}");
        sb.AppendLine($"- **Max Output Tokens:** {(run.TestedModelMaxOutputTokensUsed.HasValue ? run.TestedModelMaxOutputTokensUsed.Value.ToString() : "Default")}");
        sb.AppendLine($"- **Parallel Tool Calls:** {run.TestedModelParallelExecutionModeUsed} *(provider-side tool batching)*");
        sb.AppendLine();

        sb.AppendLine("### Chat Prompt Under Test");
        string policyFile = run.TestedModelParallelExecutionModeUsed == MobileGnollHackLogger.Data.ParallelExecutionMode.Disabled
            ? "_policy_parallel_disabled.md"
            : "_policy_parallel_on_request.md";

        if (string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson))
        {
            sb.AppendLine("The candidate is graded under the **production Overseer chat system prompt** (`ChatService.BuildSystemPrompt`), not a benchmark-specific prompt. Every quality verdict below is a verdict on the prompt real users receive.");
            sb.AppendLine();
            sb.AppendLine($"- **Tool batching policy:** {run.TestedModelParallelExecutionModeUsed} — selects Overseer/ToolGuides/{policyFile}, so this is part of the prompt text.");
            sb.AppendLine("- *Configuration not recorded for this run.*");
        }
        else
        {
            var promptOpts = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson);
            string modeStr = promptOpts.OverseerMode == 0 ? "Gameplay Help" : $"Mode {promptOpts.OverseerMode}";
            string styleStr = promptOpts.VerboseMode ? "detailed (`verboseMode: true`)" : "concise (`verboseMode: false`)";
            string toolsStr = promptOpts.EnableToolUse ? "enabled" : "disabled";
            string webStr = promptOpts.EnableWebSearch ? "enabled" : "disabled";
            string subagentsStr = promptOpts.EnableSubAgents ? "enabled" : "disabled";
            string srcStr = promptOpts.AllowSourceCodeReferences ? "allowed" : "disallowed";
            string spoilerStr = promptOpts.SpoilerFreeMode ? "on" : "off";
            string activeGameStr = promptOpts.IsGameOn ? "yes" : "no";
            string historyStr = promptOpts.HasMessageHistory ? "yes" : "no";

            sb.AppendLine("The candidate is graded under the **production Overseer chat system prompt** (`ChatService.BuildSystemPrompt`), not a benchmark-specific prompt. Every quality verdict below is a verdict on the prompt real users receive.");
            sb.AppendLine();
            sb.AppendLine($"- **Mode:** {modeStr} · **Response style:** {styleStr}");
            sb.AppendLine($"- **Tools:** {toolsStr} · **Web search:** {webStr} · **Subagents:** {subagentsStr} · **Source code references:** {srcStr}");
            sb.AppendLine($"- **Spoiler-free mode:** {spoilerStr} · **Active game:** {activeGameStr} · **Message history:** {historyStr}");
            sb.AppendLine($"- **Tool batching policy:** {run.TestedModelParallelExecutionModeUsed} — selects Overseer/ToolGuides/{policyFile}, so this is part of the prompt text.");
            sb.AppendLine("- **Pre-injected wiki context:** none — live chat pre-injects relevant articles, so this run is a strictly harder configuration than production and its tool counts are an upper bound on chat's.");
            sb.AppendLine();
            sb.AppendLine("*Configurations differ in what they measure. Two runs are comparable on Completeness, Conciseness and Readability only if this block matches.*");
        }
        sb.AppendLine();

        sb.AppendLine("### Assessment Model");
        sb.AppendLine($"- **Display Name:** {run.AssessorModelDisplayNameUsed}");
        sb.AppendLine($"- **Provider:** {run.AssessorModelProviderUsed}");
        sb.AppendLine($"- **Model ID:** {run.AssessorModelIdUsed}");
        sb.AppendLine($"- **Thinking Level:** {run.AssessorModelThinkingLevelUsed ?? "Default"}");
        sb.AppendLine($"- **Reasoning Mode:** {run.AssessorModelReasoningModeUsed ?? "Default"}");
        sb.AppendLine();

        // Named whether or not one was used: "no second opinion" is itself a fact about how the
        // run was graded, and a reader comparing two runs needs to know which had one.
        sb.AppendLine("### Second Opinion Assessor");
        if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            sb.AppendLine($"- **Display Name:** {run.SecondOpinionAssessorModelDisplayNameUsed}");
            sb.AppendLine($"- **Provider:** {run.SecondOpinionAssessorModelProviderUsed}");
            sb.AppendLine($"- **Model ID:** {run.SecondOpinionAssessorModelIdUsed}");
            sb.AppendLine($"- **Thinking Level:** {run.SecondOpinionAssessorModelThinkingLevelUsed ?? "Default"}");
            sb.AppendLine($"- **Reasoning Mode:** {run.SecondOpinionAssessorModelReasoningModeUsed ?? "Default"}");
            var configuredMode = ModeOf(run);
            sb.AppendLine($"- **Mode:** {configuredMode}{ModeGloss(configuredMode)} Advisory throughout: the first verdict is what scored.");
            if (configuredMode is BenchmarkSecondOpinionMode.Flagged or BenchmarkSecondOpinionMode.FlaggedAndOutliers)
            {
                sb.AppendLine($"- **Triggers:** a critical error; a refuted claim; a contested verdict; an unevidenced deduction (a level docked to {BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel} or below whose stated evidence names no defect, or rests only on unverifiability); an omission docked as an accuracy defect; unverifiable claims alongside an accuracy level of {BenchmarkService.UnverifiedClaimsAccuracyMaxLevel} or below; a quality score below the profile's threshold of {scoringConstants.SecondOpinionQualityThreshold}" +
                    (configuredMode == BenchmarkSecondOpinionMode.FlaggedAndOutliers
                        ? $"; and, after scoring, any answer more than {scoringConstants.SecondOpinionOutlierDeltaPoints} points below the run's median."
                        : "."));
            }
        }
        else
        {
            sb.AppendLine("- **None selected.** No answer in this run was re-graded by a second assessor.");

            // What was forgone, stated in the run's own numbers. The 2026-09-03 run produced
            // two critical errors with no second opinion selected — precisely the trigger the
            // feature exists for — and nothing in the report connected the two facts. It was
            // also silent about the larger number: double grading would have covered every
            // answer, and that is the only setting that measures grader agreement instead of
            // sampling it.
            int threshold = scoringConstants.SecondOpinionQualityThreshold;

            // Mirrors the first-match cascade in BenchmarkService.ResolveSecondOpinionTrigger,
            // so the counts partition the answers rather than double-counting one that satisfies
            // two triggers.
            string? WouldTrigger(BenchmarkRunAnswer a)
            {
                if (a.CriticalError) return "critical error";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0) return "refuted claim";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0) return "contested verdict";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0) return "unevidenced deduction";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0) return "omission docked as accuracy";
                if ((a.UnverifiedClaimCount ?? 0) > 0 && (a.AccuracyLevel ?? 6) <= BenchmarkService.UnverifiedClaimsAccuracyMaxLevel
                    && ((a.ClaimsRefutedCount ?? 0) > 0 || (a.ClaimsIndeterminateCount ?? 0) > 0 || (a.ClaimVerificationJson == null && a.ClaimVerificationError == null))) return "unverifiable claims";
                if (threshold > 0 && a.QualityScore.HasValue && a.QualityScore.Value < threshold) return $"below the profile's threshold of {threshold}";
                return null;
            }

            var wouldByTrigger = answers
                .Select(WouldTrigger)
                .Where(t => t != null)
                .GroupBy(t => t!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
            int wouldTotal = wouldByTrigger.Sum(g => g.Count());
            if (wouldTotal > 0)
            {
                sb.AppendLine($"- **{wouldTotal} answer(s) would have been re-graded** under `Flagged` had a second opinion assessor been selected ({string.Join("; ", wouldByTrigger.Select(g => $"{g.Key}: {g.Count()}"))}).");
            }

            int answeredForForgone = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            if (answeredForForgone > 0)
            {
                sb.AppendLine($"- **{answeredForForgone} answer(s) would have been graded twice** under `All` (double grading) — the only mode that measures grader agreement rather than sampling it. `FlaggedAndOutliers` would have added a post-scoring sweep for answers more than {scoringConstants.SecondOpinionOutlierDeltaPoints} points below the run's median.");
            }
        }
        sb.AppendLine();

        sb.AppendLine("### Claim Verifier");
        if (run.ClaimVerifierModelConfigurationId.HasValue)
        {
            sb.AppendLine($"- **Display Name:** {run.ClaimVerifierDisplayNameUsed}");
            sb.AppendLine($"- **Provider:** {run.ClaimVerifierProviderUsed}");
            sb.AppendLine($"- **Model ID:** {run.ClaimVerifierModelIdUsed}");
            sb.AppendLine($"- **Thinking Level:** {run.ClaimVerifierThinkingLevelUsed ?? "Default"}");
            sb.AppendLine($"- **Reasoning Mode:** {run.ClaimVerifierReasoningModeUsed ?? "Default"}");
            sb.AppendLine("- **Role:** Verifies unverified factual claims against source code and wiki using read-only tools. Advisory throughout: nothing here is read by any scoring path.");
            if (!string.IsNullOrWhiteSpace(run.TestedModelProviderUsed) &&
                string.Equals(run.TestedModelProviderUsed, run.ClaimVerifierProviderUsed, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  - *The verifier and the candidate come from the same provider: the tools supply the evidence rather than the model's memory, so this is not worthless — but it is the weakest available pairing.*");
            }
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue &&
                (run.SecondOpinionAssessorModelConfigurationId == run.ClaimVerifierModelConfigurationId ||
                 (!string.IsNullOrWhiteSpace(run.SecondOpinionAssessorModelIdUsed) &&
                  string.Equals(run.SecondOpinionAssessorModelIdUsed, run.ClaimVerifierModelIdUsed, StringComparison.OrdinalIgnoreCase))))
            {
                sb.AppendLine("  - *Same model as the second-opinion assessor. Under blind mode the second reader is given the verification findings, so a refuted claim and a harsh second opinion on the same answer are one finding, not two independent ones.*");
            }
        }
        else
        {
            int unverifiedTotalClaims = answers.Where(a => (a.UnverifiedClaimCount ?? 0) > 0).Sum(a => a.UnverifiedClaimCount!.Value);
            int unverifiedAnswerCount = answers.Count(a => (a.UnverifiedClaimCount ?? 0) > 0);
            if (unverifiedTotalClaims > 0)
            {
                sb.AppendLine($"- **None selected.** No answer had its unverified claims checked against the source or wiki ({unverifiedTotalClaims} claim(s) across {unverifiedAnswerCount} answer(s) went unchecked).");
            }
            else
            {
                sb.AppendLine("- **None selected.** No answer in this run had its unverified claims checked against the source or wiki.");
            }
        }
        sb.AppendLine();

        // 3. Results Summary
        var scoredAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue).ToList();

        var rawScorableItems = scoredAnswers
            .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();
        int? rawQualityIndex = BenchmarkScoring.QualityIndex(rawScorableItems);
        int cappedCount = scoredAnswers.Count(a => a.RawQualityScore.HasValue && a.QualityScore.HasValue && a.RawQualityScore.Value > a.QualityScore.Value);

        sb.AppendLine("## 2. Results Summary");
        sb.AppendLine();

        double? se = run.QualityIndexStandardError;
        if (!se.HasValue && scoredAnswers.Count >= 3)
        {
            se = BenchmarkScoring.QualityIndexStandardError(
                scoredAnswers.Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty))));
        }

        string seText = string.Empty;
        int ciLower = 0, ciUpper = 100;
        if (se.HasValue && run.QualityIndex.HasValue)
        {
            double halfWidth = 1.96 * se.Value;
            ciLower = (int)Math.Max(0, Math.Round(run.QualityIndex.Value - halfWidth));
            ciUpper = (int)Math.Min(100, Math.Round(run.QualityIndex.Value + halfWidth));
            seText = $" ± {halfWidth:F0} (95% CI over {scoredAnswers.Count} items)";
        }

        sb.AppendLine($"### **Intelligence Index: {(run.QualityIndex.HasValue ? $"{run.QualityIndex.Value}{seText} / 100" : "Not Scored")}**");
        if (se.HasValue && run.QualityIndex.HasValue)
        {
            sb.AppendLine("*This reflects finite item-sampling uncertainty — how much the index would move under a different draw of questions of the same difficulty profile. Two runs whose intervals overlap are statistically indistinguishable on this suite.*");
            sb.AppendLine();
        }
        // Only shown when a critical-error cap actually moved it. Printing the identical number
        // under a second heading told the reader nothing.
        if (rawQualityIndex.HasValue && rawQualityIndex.Value != (run.QualityIndex ?? 0))
        {
            sb.AppendLine($"### **Raw Quality Index: {rawQualityIndex.Value} / 100 ({cappedCount} question(s) capped by critical error)**");
        }

        // The gap between the difficulty-weighted index and the plain mean of the same scores is
        // how much the weighting moved the headline, and it is invisible from either number
        // alone. On the 2026-09-03 run it moved the index *up* by two points, because the
        // model's two weakest answers were also two of its easiest questions. Computed here for
        // runs that predate the stored column, so the line works on the whole archive.
        int? unweightedMean = run.UnweightedQualityIndex
            ?? BenchmarkScoring.UnweightedQualityMean(scoredAnswers.Select(a => a.QualityScore));
        if (unweightedMean.HasValue && run.QualityIndex.HasValue &&
            Math.Abs(run.QualityIndex.Value - unweightedMean.Value) >= 1)
        {
            int weightingDelta = run.QualityIndex.Value - unweightedMean.Value;
            sb.AppendLine();
            sb.AppendLine($"- **Unweighted Quality Mean:** {unweightedMean.Value} / 100 — difficulty weighting moved the index by **{(weightingDelta > 0 ? "+" : string.Empty)}{Inv(weightingDelta)}** points. The Intelligence Index weights each answer by its assessed difficulty, so a run whose weak answers are its easy ones reads higher than its plain average.");
            sb.AppendLine();
        }

        sb.AppendLine($"### **Speed Index: {(run.SpeedIndex.HasValue ? $"{run.SpeedIndex.Value} / 100" : "Not Scored")}**" + (run.SpeedMeasurementDegraded ? " *(Advisory — measured under concurrency)*" : ""));
        // A critical error caps Quality at 25 (see BenchmarkScoring), which the Raw/Intelligence
        // Index pair above already shows as a point delta — but that delta is diluted by every
        // *other* answer's difficulty weight, so a single hallucinated answer can move the index
        // by as little as one point (see the "How to read these" note under Final Indices). The
        // headline below gives the reader the actual count instead of asking them to infer it
        // from a small index shift.
        var contestedCriticalAnswers = answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok &&
                        ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0 ||
                         (a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError)))
            .OrderBy(a => a.OrderIndex)
            .ToList();

        var criticalErrorAnswers = answers.Where(a => a.CriticalError).OrderBy(a => a.OrderIndex).ToList();
        var confirmedAnswers = criticalErrorAnswers.Where(a => !contestedCriticalAnswers.Any(ca => ca.OrderIndex == a.OrderIndex)).ToList();

        if (contestedCriticalAnswers.Count > 0)
        {
            var affectedAnswers = confirmedAnswers.Concat(contestedCriticalAnswers).OrderBy(a => a.OrderIndex).DistinctBy(a => a.OrderIndex).ToList();
            string questionNumbers = string.Join(", ", affectedAnswers.Select(a => a.OrderIndex));
            sb.AppendLine($"- **Critical Errors:** {confirmedAnswers.Count} confirmed, {contestedCriticalAnswers.Count} contested (question(s) {questionNumbers})");

            var sensitivityScorableItems = scoredAnswers
                .Select(a =>
                {
                    int? score = a.QualityScore;
                    if (contestedCriticalAnswers.Any(ca => ca.OrderIndex == a.OrderIndex) && a.SecondOpinionQualityScore.HasValue)
                    {
                        score = a.SecondOpinionQualityScore.Value;
                    }
                    return (score, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty));
                })
                .ToList();
            int? sensitivityIndex = BenchmarkScoring.QualityIndex(sensitivityScorableItems);
            if (sensitivityIndex.HasValue)
            {
                sb.AppendLine($"- **Contested-Verdict Sensitivity:** {sensitivityIndex.Value} / 100 — Intelligence Index recomputed with each contested verdict upheld at the second reader's score.");
            }
        }
        else if (criticalErrorAnswers.Count > 0)
        {
            int answeredCountForCritical = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            string criticalQuestionNumbers = string.Join(", ", criticalErrorAnswers.Select(a => a.OrderIndex));
            sb.AppendLine($"- **Critical Errors:** {criticalErrorAnswers.Count} of {answeredCountForCritical} answered (question(s) {criticalQuestionNumbers})");
        }
        sb.AppendLine();
        sb.AppendLine($"- **Holistic Assessor Score:** {(run.FinalScore.HasValue ? $"{run.FinalScore.Value} / 100" : "N/A")}");
        sb.AppendLine($"- **Total Model Answer Duration:** {FormatDuration(run.TotalAnswerDurationMs)} ({Inv(run.TotalAnswerDurationMs, "N0")} ms)");

        var okAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();
        var durations = okAnswers.Select(a => a.DurationMs).OrderBy(d => d).ToList();
        if (durations.Count > 0)
        {
            long p50 = Percentile(durations, 0.50);
            long p90 = Percentile(durations, 0.90);
            long maxD = durations[^1];
            sb.AppendLine($"- **Turn Duration Percentiles:** Median (P50) = {Inv(p50, "N0")} ms, P90 = {Inv(p90, "N0")} ms, Max = {Inv(maxD, "N0")} ms");
        }

        // Split the turn into model time and harness tool I/O, so a low speed score can be
        // attributed to the model rather than to the harness. Speed is scored on model time.
        if (okAnswers.Any(a => a.ToolTimeMs.HasValue))
        {
            long toolOverhead = okAnswers.Sum(a => a.ToolTimeMs ?? 0L);
            long modelTotal = okAnswers.Sum(a => a.ModelTimeMs);
            sb.AppendLine($"- **Total Tool Overhead:** {FormatDuration(toolOverhead)} ({Inv(toolOverhead, "N0")} ms)");
            sb.AppendLine($"- **Total Model-Attributable Time:** {FormatDuration(modelTotal)} ({Inv(modelTotal, "N0")} ms)");

            var modelTimes = okAnswers.Select(a => a.ModelTimeMs).OrderBy(d => d).ToList();
            sb.AppendLine($"- **Model Time Percentiles:** Median (P50) = {Inv(Percentile(modelTimes, 0.50), "N0")} ms, P90 = {Inv(Percentile(modelTimes, 0.90), "N0")} ms, Max = {Inv(modelTimes[^1], "N0")} ms");
        }
        else
        {
            sb.AppendLine("- **Tool Overhead:** Not recorded (run predates harness version 3); speed was scored on total turn duration.");
        }

        // Time to first token: the only latency figure a thinking=max configuration does not
        // dominate, and therefore the one that describes how the model would feel in the chat.
        var ttfts = okAnswers.Where(a => a.TimeToFirstTokenMs.HasValue)
            .Select(a => a.TimeToFirstTokenMs!.Value)
            .OrderBy(d => d)
            .ToList();
        if (ttfts.Count > 0)
        {
            sb.AppendLine($"- **Time to First Token:** Median (P50) = {Inv(Percentile(ttfts, 0.50), "N0")} ms, P90 = {Inv(Percentile(ttfts, 0.90), "N0")} ms, Max = {Inv(ttfts[^1], "N0")} ms");
        }

        sb.AppendLine($"- **Total Input Tokens:** {Inv(run.TotalInputTokens, "N0")}");
        sb.AppendLine($"- **Total Output Tokens:** {Inv(run.TotalOutputTokens, "N0")}");
        sb.AppendLine($"- **Total Cache Read Tokens:** {Inv(run.TotalCacheReadTokens, "N0")}");
        // A real zero and "this provider does not report the counter" are different facts, and
        // printing 0 beside four million cache reads reads as a cache that never warmed. OpenAI
        // reports cache reads only; the 2026-09-03 run showed exactly that shape.
        bool cacheCreationUnreported =
            run.TotalCacheCreationTokens == 0 &&
            run.TotalCacheReadTokens > 0 &&
            string.Equals(run.TestedModelProviderUsed, "OpenAI", StringComparison.OrdinalIgnoreCase);
        sb.AppendLine(cacheCreationUnreported
            ? "- **Total Cache Creation Tokens:** n/a *(not reported by this provider)*"
            : $"- **Total Cache Creation Tokens:** {Inv(run.TotalCacheCreationTokens, "N0")}");
        sb.AppendLine();

        // Harness cost. The token totals above are the candidate's alone; grading an 18-question
        // suite question by question is not a rounding error, and until this block existed the
        // run's actual consumption was recorded nowhere.
        if (run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0 || run.TotalAssessmentDurationMs > 0 ||
            run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0 || run.TotalClaimVerificationDurationMs > 0)
        {
            sb.AppendLine("### Harness Cost");
            sb.AppendLine($"- **Candidate Tokens:** {Inv(run.TotalInputTokens, "N0")} in / {Inv(run.TotalOutputTokens, "N0")} out");
            sb.AppendLine($"- **Assessor Tokens:** {Inv(run.TotalAssessmentInputTokens, "N0")} in / {Inv(run.TotalAssessmentOutputTokens, "N0")} out");
            if (run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0 || run.ClaimVerifierModelConfigurationId.HasValue)
            {
                bool allVerificationAttemptsFailed = answers.Any(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError)) &&
                    !answers.Any(a => string.IsNullOrWhiteSpace(a.ClaimVerificationError) && (a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue));
                string failedCaveat = allVerificationAttemptsFailed ? " — *every attempt failed; see Issues*" : string.Empty;
                sb.AppendLine($"- **Claim Verifier Tokens:** {Inv(run.TotalClaimVerificationInputTokens, "N0")} in / {Inv(run.TotalClaimVerificationOutputTokens, "N0")} out{failedCaveat}");
            }
            long totalInput = run.TotalInputTokens + run.TotalAssessmentInputTokens + run.TotalClaimVerificationInputTokens;
            long totalOutput = run.TotalOutputTokens + run.TotalAssessmentOutputTokens + run.TotalClaimVerificationOutputTokens;
            sb.AppendLine($"- **Total Tokens:** {Inv(totalInput, "N0")} in / {Inv(totalOutput, "N0")} out");
            sb.AppendLine($"- **Assessment Time:** {FormatDuration(run.TotalAssessmentDurationMs)} ({Inv(run.TotalAssessmentDurationMs, "N0")} ms)");
            if (run.TotalClaimVerificationDurationMs > 0)
            {
                sb.AppendLine($"- **Claim Verification Time:** {FormatDuration(run.TotalClaimVerificationDurationMs)} ({Inv(run.TotalClaimVerificationDurationMs, "N0")} ms)");
            }

            // Estimated Cost block (H7)
            bool hasAssessor = run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0;
            bool hasVerifier = run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0;

            var candidatePricing = runPricing?.Candidate;
            var assessorPricing = hasAssessor ? runPricing?.Assessor : null;
            var verifierPricing = hasVerifier ? runPricing?.ClaimVerifier : null;

            bool canEstimateCost = runPricing != null &&
                candidatePricing != null &&
                (!hasAssessor || assessorPricing != null) &&
                (!hasVerifier || verifierPricing != null);

            if (canEstimateCost)
            {
                long cachedInTokens = run.TotalCacheReadTokens;
                long uncachedInTokens = Math.Max(0, run.TotalInputTokens - cachedInTokens);

                decimal candidateInCost = (uncachedInTokens / 1_000_000m * candidatePricing!.InputPerMillion) +
                                          (cachedInTokens / 1_000_000m * (candidatePricing.CachedInputPerMillion ?? candidatePricing.InputPerMillion));
                decimal candidateOutCost = run.TotalOutputTokens / 1_000_000m * candidatePricing.OutputPerMillion;
                decimal candidateCacheWriteCost = (run.TotalCacheCreationTokens > 0 && candidatePricing.CacheWritePerMillion.HasValue)
                    ? (run.TotalCacheCreationTokens / 1_000_000m * candidatePricing.CacheWritePerMillion.Value)
                    : 0m;
                decimal candidateTotalCost = ModelPricingService.ComputeCost(candidatePricing, uncachedInTokens, run.TotalOutputTokens, cachedInTokens, run.TotalCacheCreationTokens);

                decimal assessorTotalCost = 0m;
                decimal assessorInCost = 0m;
                decimal assessorOutCost = 0m;
                if (hasAssessor && assessorPricing != null)
                {
                    assessorInCost = run.TotalAssessmentInputTokens / 1_000_000m * assessorPricing.InputPerMillion;
                    assessorOutCost = run.TotalAssessmentOutputTokens / 1_000_000m * assessorPricing.OutputPerMillion;
                    assessorTotalCost = ModelPricingService.ComputeCost(assessorPricing, run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens);
                }

                decimal verifierTotalCost = 0m;
                decimal verifierInCost = 0m;
                decimal verifierOutCost = 0m;
                if (hasVerifier && verifierPricing != null)
                {
                    verifierInCost = run.TotalClaimVerificationInputTokens / 1_000_000m * verifierPricing.InputPerMillion;
                    verifierOutCost = run.TotalClaimVerificationOutputTokens / 1_000_000m * verifierPricing.OutputPerMillion;
                    verifierTotalCost = ModelPricingService.ComputeCost(verifierPricing, run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens);
                }

                decimal totalCost = candidateTotalCost + assessorTotalCost + verifierTotalCost;
                sb.AppendLine($"- **Estimated Cost:** ${Inv(totalCost, "F2")} total");

                if (candidatePricing.CachedInputPerMillion.HasValue && cachedInTokens > 0)
                {
                    decimal uncachedCost = uncachedInTokens / 1_000_000m * candidatePricing.InputPerMillion;
                    decimal cachedCost = cachedInTokens / 1_000_000m * (candidatePricing.CachedInputPerMillion ?? candidatePricing.InputPerMillion);
                    if (candidateCacheWriteCost > 0)
                    {
                        sb.AppendLine($"  - Candidate ({run.TestedModelIdUsed}): ${Inv(candidateTotalCost, "F2")} (uncached in: ${Inv(uncachedCost, "F2")}, cached in: ${Inv(cachedCost, "F2")}, cache write: ${Inv(candidateCacheWriteCost, "F2")}, out: ${Inv(candidateOutCost, "F2")})");
                    }
                    else
                    {
                        sb.AppendLine($"  - Candidate ({run.TestedModelIdUsed}): ${Inv(candidateTotalCost, "F2")} (uncached in: ${Inv(uncachedCost, "F2")}, cached in: ${Inv(cachedCost, "F2")}, out: ${Inv(candidateOutCost, "F2")})");
                    }
                }
                else
                {
                    if (candidateCacheWriteCost > 0)
                    {
                        sb.AppendLine($"  - Candidate ({run.TestedModelIdUsed}): ${Inv(candidateTotalCost, "F2")} (in: ${Inv(candidateInCost, "F2")}, cache write: ${Inv(candidateCacheWriteCost, "F2")}, out: ${Inv(candidateOutCost, "F2")})");
                    }
                    else
                    {
                        sb.AppendLine($"  - Candidate ({run.TestedModelIdUsed}): ${Inv(candidateTotalCost, "F2")} (in: ${Inv(candidateInCost, "F2")}, out: ${Inv(candidateOutCost, "F2")})");
                    }
                }

                if (hasAssessor && assessorPricing != null)
                {
                    sb.AppendLine($"  - Assessor ({run.AssessorModelIdUsed}): ${Inv(assessorTotalCost, "F2")} (in: ${Inv(assessorInCost, "F2")}, out: ${Inv(assessorOutCost, "F2")})");
                }

                if (hasVerifier && verifierPricing != null)
                {
                    sb.AppendLine($"  - Claim Verifier ({run.ClaimVerifierModelIdUsed}): ${Inv(verifierTotalCost, "F2")} (in: ${Inv(verifierInCost, "F2")}, out: ${Inv(verifierOutCost, "F2")})");
                }

                var provenanceParts = new List<string>();
                string FormatProv(string roleTitle, ModelPricing p)
                {
                    if (p.Source == ModelPricingSource.Custom)
                        return $"{roleTitle} custom";
                    return $"{roleTitle} catalog" + (!string.IsNullOrEmpty(p.AsOf) ? $" (as of {p.AsOf})" : "");
                }
                provenanceParts.Add(FormatProv("candidate", candidatePricing));
                if (hasAssessor && assessorPricing != null)
                {
                    provenanceParts.Add(FormatProv("assessor", assessorPricing));
                }
                if (hasVerifier && verifierPricing != null)
                {
                    provenanceParts.Add(FormatProv("verifier", verifierPricing));
                }

                string provenanceLine = $"- *Prices: {string.Join("; ", provenanceParts)}.*";
                if (runPricing != null && !runPricing.IsSnapshot)
                {
                    provenanceLine += " *(priced at report generation time; this run predates price snapshotting)*";
                }
                sb.AppendLine(provenanceLine);
            }
            else
            {
                var missingRoles = new List<string>();
                if (candidatePricing == null) missingRoles.Add("candidate");
                if (hasAssessor && assessorPricing == null) missingRoles.Add("assessor");
                if (hasVerifier && verifierPricing == null) missingRoles.Add("claim verifier");
                if (missingRoles.Count == 0) missingRoles.Add("participating models");

                sb.AppendLine($"- **Estimated Cost:** not available — no price is known for {string.Join(", ", missingRoles)}. Set a price in Admin → System AI Configs (Custom), or add `pricing` to the model's catalog entry.");
            }


            sb.AppendLine("*Assessment runs pipelined behind each answer, so assessment time overlaps the candidate's and the two do not sum to the wall time.*");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("*Assessor token and time accounting was not recorded for this run (it predates harness version 4).*");
            sb.AppendLine();
        }

        // Run Integrity block.
        //
        // Every answer falls into exactly one of Clean / TransportDefect / HarnessLimit, so the
        // three always sum to the question count. The previous block printed a "Degraded" total
        // that included tool-budget exhaustion beside a breakdown that omitted it, so the
        // figures did not add up (7 = "empty: 0, harness artifacts: 4, truncated: 0"), and it
        // conflated a transport defect with a configured cap working as designed.
        int totalQuestions = run.TotalQuestionCount;
        int emptyCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.EmptyAnswer || ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Empty));
        int artifactCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        int truncatedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Truncated));
        int bleedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
        int repeatCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RepeatedFragments));
        int contestedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedVerdict));
        int unevidencedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction));
        int omissionCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.OmissionAsAccuracy));
        int refutedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RefutedClaim));
        int providerErrorCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.ProviderError);

        int transportDefectCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.TransportDefect);
        int recoveredCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Recovered);
        int harnessLimitCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.HarnessLimit);
        int advisoryCount = answers.Count(BenchmarkRunFinalizer.HasAdvisoryFlag);
        // NarrationBlockCount is the honest figure: how many narration blocks the scrubber
        // actually removed from this answer. Runs before harness version 6 did not record it,
        // and there null means "not recorded" — never zero. For those the old proxy stands, a
        // non-empty ScrubbedArtifactText, which is weaker because it is also true when only a
        // leaked payload was removed. That weakness is why the 2026-09-03 run's report claimed
        // narration had been removed from two answers that still carried it.
        static bool NarrationRemoved(BenchmarkRunAnswer a) =>
            a.NarrationBlockCount.HasValue
                ? a.NarrationBlockCount.Value > 0
                : !string.IsNullOrWhiteSpace(a.ScrubbedArtifactText);

        static bool BleedFlagged(BenchmarkRunAnswer a) =>
            ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ReasoningBleed);

        int bleedRemoved = answers.Count(a => BleedFlagged(a) && NarrationRemoved(a));
        int bleedUnrecorded = answers.Count(a => BleedFlagged(a) && !a.NarrationBlockCount.HasValue);
        int scrubbedTransportCount = answers.Count(a => a.ScrubbedArtifactCount > 0);
        int scrubbedAnyCount = answers.Count(a =>
            a.ScrubbedArtifactCount > 0 || (BleedFlagged(a) && NarrationRemoved(a)));
        int cleanCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Clean);
        double cleanPct = totalQuestions > 0 ? (cleanCount * 100.0 / totalQuestions) : 0.0;

        sb.AppendLine("### Run Integrity");
        sb.AppendLine($"- **Clean Answers:** {cleanCount} of {totalQuestions} ({Inv(cleanPct, "F1")}%)");
        sb.AppendLine($"- **Transport Defects:** {transportDefectCount} (empty: {emptyCount}, truncated: {truncatedCount}) — *unrecoverable; excluded or invalid*");
        sb.AppendLine($"- **Recovered:** {recoveredCount} (leaked transport artifacts in: {artifactCount}) — *the harness removed the leaked payloads and graded the answer beneath them; a provider-path defect, not a damaged result*");
        sb.AppendLine($"- **Harness Limits:** {harnessLimitCount} (tool budget exhausted: {answers.Count(a => a.ToolBudgetExhausted)})");
        sb.AppendLine($"- **Provider Errors:** {providerErrorCount}");
        sb.AppendLine($"*Clean + transport defects + recovered + harness limits = {cleanCount + transportDefectCount + recoveredCount + harnessLimitCount} of {totalQuestions}.*");
        sb.AppendLine();
        // On the 2026-09-03 run the report claimed the removal was unconditional; the streaming
        // writer's bug (fixed alongside this) meant five graded answers still carried their own
        // narration. The sentence now says so when it happens instead of asserting it away.
        string advisoryNote = bleedRemoved == bleedCount
            ? "— *advisory only; these overlap the categories above, do not affect the run status, and the text they describe was removed before grading. An answer may carry more than one, so the breakdown can exceed the count.*"
            : $"— *advisory only; these overlap the categories above and do not affect the run status. Removed before grading in {bleedRemoved} of {bleedCount}; in the remainder the text was detected but remained in the graded answer. An answer may carry more than one, so the breakdown can exceed the count.*";
        if (bleedUnrecorded > 0)
        {
            advisoryNote += $" *Removal was not recorded for {bleedUnrecorded} of these — the run predates harness version {BenchmarkAssessmentPrompt.HarnessVersion}, which added the counter; that figure is inferred, not measured.*";
        }
        sb.AppendLine($"- **Advisory Flags:** {advisoryCount} (reasoning bleed: {bleedCount}, repeated fragments: {repeatCount}, contested verdicts: {contestedCount}, unevidenced deductions: {unevidencedCount}, omissions as accuracy: {omissionCount}, refuted claims: {refutedCount}) {advisoryNote}");
        sb.AppendLine($"- **Answers Scrubbed:** {scrubbedAnyCount} of {totalQuestions} (transport payloads: {scrubbedTransportCount}, reasoning narration: {bleedRemoved})");
        sb.AppendLine();

        // Assessor Findings — advisory signals about the *grading* rather than the answers:
        // what the assessor could not verify, where its own prose contradicted its
        // critical-error flag, and where an omission was docked under accuracy.
        var withClaims = answers
            .Where(a => (a.UnverifiedClaimCount ?? 0) > 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        int unverifiedTotal = withClaims.Sum(a => a.UnverifiedClaimCount!.Value);
        bool claimsRecorded = answers.Any(a => a.UnverifiedClaimCount.HasValue);
        var contestedAnswers = answers
            .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var omissionAnswers = answers
            .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var refutedAnswers = answers
            .Where(a => (a.ClaimsRefutedCount ?? 0) > 0 && !string.IsNullOrWhiteSpace(a.ClaimVerificationJson))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var verificationFailedAnswers = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError))
            .OrderBy(a => a.OrderIndex)
            .ToList();

        if (!claimsRecorded || unverifiedTotal > 0 || contestedAnswers.Count > 0 || omissionAnswers.Count > 0 || refutedAnswers.Count > 0 || verificationFailedAnswers.Count > 0)
        {
            sb.AppendLine("### Assessor Findings");
            if (!claimsRecorded)
            {
                sb.AppendLine($"- **Unverified Claims:** not recorded — this run predates harness version {BenchmarkAssessmentPrompt.HarnessVersion}, which added the field.");
            }
            else if (unverifiedTotal > 0)
            {
                int totalSupported = run.ClaimsSupportedCount > 0 ? run.ClaimsSupportedCount : answers.Sum(a => a.ClaimsSupportedCount ?? 0);
                int totalRefuted = run.ClaimsRefutedCount > 0 ? run.ClaimsRefutedCount : answers.Sum(a => a.ClaimsRefutedCount ?? 0);
                int totalIndeterminate = run.ClaimsIndeterminateCount > 0 ? run.ClaimsIndeterminateCount : answers.Sum(a => a.ClaimsIndeterminateCount ?? 0);
                bool hasVerification = (run.ClaimVerifiedAnswerCount > 0 || answers.Any(a => a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue))
                    && (totalSupported > 0 || totalRefuted > 0 || totalIndeterminate > 0);

                string outcome = hasVerification
                    ? $" — verified: {totalSupported} supported, {totalRefuted} refuted, {totalIndeterminate} indeterminate."
                    : string.Empty;

                sb.AppendLine($"- **Unverified Claims:** {unverifiedTotal} across {withClaims.Count} answer(s) ({string.Join(", ", withClaims.Select(a => $"Q{a.OrderIndex}"))}){outcome} — *claims the assessor could neither confirm nor refute against the rubric. Advisory: from harness version 7 these do not reduce Accuracy.*");

                if (verificationFailedAnswers.Count > 0)
                {
                    string firstError = BenchmarkAssessmentFailure.Truncate(verificationFailedAnswers[0].ClaimVerificationError, 200) ?? string.Empty;
                    sb.AppendLine($"- **Claim Verification Failed:** {verificationFailedAnswers.Count} of {withClaims.Count} answer(s) with unverified claims ({string.Join(", ", verificationFailedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the verifier was configured but returned no verdict, so the unverified claims above were never checked. First error: `{firstError}`.");
                }
            }
            else if (verificationFailedAnswers.Count > 0)
            {
                string firstError = BenchmarkAssessmentFailure.Truncate(verificationFailedAnswers[0].ClaimVerificationError, 200) ?? string.Empty;
                sb.AppendLine($"- **Claim Verification Failed:** {verificationFailedAnswers.Count} answer(s) ({string.Join(", ", verificationFailedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the verifier was configured but returned no verdict. First error: `{firstError}`.");
            }
            if (contestedAnswers.Count > 0)
            {
                sb.AppendLine($"- **Contested Verdicts:** {contestedAnswers.Count} ({string.Join(", ", contestedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *the assessor's own comment describes a fabrication while its critical-error flag is false. Advisory; no scoring effect.*");
            }
            if (omissionAnswers.Count > 0)
            {
                sb.AppendLine($"- **Omission Docked as Accuracy:** {omissionAnswers.Count} ({string.Join(", ", omissionAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *the assessor docked Accuracy citing an omission, which scoring rules reserve for Completeness. An omission is not a defect of truthfulness.*");
            }
            if (refutedAnswers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("#### Refuted Claims");
                sb.AppendLine("*(Advisory; recorded after scoring and folded into no index)*");
                sb.AppendLine();
                foreach (var ans in refutedAnswers)
                {
                    try
                    {
                        var vers = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(ans.ClaimVerificationJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (vers != null)
                        {
                            foreach (var v in vers.Where(x => x.Verdict == BenchmarkClaimVerdict.Refuted))
                            {
                                sb.AppendLine($"- **Q{ans.OrderIndex}:** \"{v.Claim}\"");
                                if (!string.IsNullOrWhiteSpace(v.Citation))
                                {
                                    sb.AppendLine($"  - **Citation:** {v.Citation}");
                                }
                                if (!string.IsNullOrWhiteSpace(v.Basis))
                                {
                                    sb.AppendLine($"  - **Basis:** {v.Basis}");
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in report
                    }
                }
            }
            sb.AppendLine();
        }

        // Assessor Agreement. The coverage fraction travels with the figure everywhere it is
        // printed, because the two are not separable: a mean delta over trigger-selected answers
        // is conditioned on the first assessor's own uncertainty and says nothing about the
        // instrument, while the same number over every answer is an inter-rater agreement rate.
        if (run.SecondOpinionGradedAnswerCount > 0)
        {
            var agreementMode = ModeOf(run);
            var graded = answers
                .Where(a => a.SecondOpinionQualityScore.HasValue && a.QualityScore.HasValue
                            && !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal))
                .OrderBy(a => a.OrderIndex)
                .ToList();
            var disagreedAnswers = graded.Where(a => a.SecondOpinionDisagreed).ToList();
            int answeredForAgreement = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            double? meanAbsDelta = run.SecondOpinionMeanAbsDelta
                ?? (graded.Count > 0
                    ? graded.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value))
                    : null);
            double? meanSignedDelta = run.SecondOpinionMeanSignedDelta
                ?? (graded.Count > 0
                    ? graded.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value))
                    : null);
            var criticalErrorSplits = graded
                .Where(a => a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError)
                .ToList();

            sb.AppendLine("### Assessor Agreement");
            sb.AppendLine($"- **Mode:** {agreementMode}{ModeGloss(agreementMode)}");
            sb.AppendLine($"- **Coverage:** {run.SecondOpinionGradedAnswerCount} of {answeredForAgreement} answered questions.");
            sb.AppendLine($"- **Prompt Protocol:** {(run.SecondOpinionBlindUsed ? "Blind — the second assessor received the candidate's answer without seeing the first assessor's scores, comments, or critical error flag." : "Anchored — the second assessor saw the first assessor's verdict and comment.")}");
            if (run.SecondOpinionBlindUsed)
            {
                sb.AppendLine("  - *Note: Assessor agreement is reported for a **blind** second reader. Blind and anchored agreement figures are not comparable.*");
            }
            else
            {
                sb.AppendLine("  - *Note: Anchored second opinions exhibit anchoring bias toward the first assessor's verdict and cannot be compared directly with blind second opinions.*");
            }
            sb.AppendLine(meanAbsDelta.HasValue
                ? $"- **Mean absolute difference:** {Inv(meanAbsDelta.Value, "F1")} points."
                : "- **Mean absolute difference:** not recorded.");
            if (meanSignedDelta.HasValue)
            {
                string directionSuffix = string.Empty;
                var deltas = graded.Select(a => a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value).ToList();
                if (deltas.Count >= 3 && deltas.All(d => d < 0))
                {
                    directionSuffix = " — the second reader graded **lower** on every re-graded answer. A one-directional gap of this size is a statement about the grader whose verdict scores, not about noise between two readers.";
                }
                else if (deltas.Count >= 3 && deltas.All(d => d > 0))
                {
                    directionSuffix = " — the second reader graded **higher** on every re-graded answer. A one-directional gap of this size is a statement about the grader whose verdict scores, not about noise between two readers.";
                }
                sb.AppendLine($"- **Mean signed difference:** {Inv(meanSignedDelta.Value, "F1")} points (over {deltas.Count} of {answeredForAgreement} answered){directionSuffix}");
            }
            else
            {
                sb.AppendLine("- **Mean signed difference:** not recorded.");
            }
            int splitCount = run.SecondOpinionCriticalErrorSplitCount > 0 ? run.SecondOpinionCriticalErrorSplitCount : criticalErrorSplits.Count;
            if (splitCount > 0 && graded.Count > 0)
            {
                string splitNamed = criticalErrorSplits.Count > 0
                    ? " — " + string.Join(", ", criticalErrorSplits.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Critical-error splits:** {splitCount} of {graded.Count}{splitNamed}. The two readers disagree on whether the answer contains a fabrication, which is the most severe disagreement this harness records.");
            }
            if (graded.Count > 0)
            {
                double disagreementPct = disagreedAnswers.Count * 100.0 / graded.Count;
                string named = disagreedAnswers.Count > 0
                    ? " — " + string.Join(", ", disagreedAnswers.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Disagreements:** {disagreedAnswers.Count} of {graded.Count} ({Inv(disagreementPct, "F1")}%){named}. *A disagreement is a gap above {BenchmarkService.SecondOpinionDisagreementPoints} quality points — roughly one BARS level on the dominant dimension — or a split on criticalError.*");
            }
            if (agreementMode != BenchmarkSecondOpinionMode.All)
            {
                sb.AppendLine($"- *Coverage: {run.SecondOpinionGradedAnswerCount} of {answeredForAgreement}, selected by trigger. The disagreement rate is conditioned on the first assessor's own uncertainty and is not an unbiased estimate of grader agreement; `SecondOpinionMode = All` measures that.*");
            }
            sb.AppendLine();
        }
        else if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            var agreementMode = ModeOf(run);
            int answeredForAgreement = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            string assessorName = run.SecondOpinionAssessorModelDisplayNameUsed ?? "configured assessor";

            sb.AppendLine("### Assessor Agreement");
            sb.AppendLine($"- **Mode:** {agreementMode}{ModeGloss(agreementMode)}");

            var secondOpinionFailedAnswers = answers
                .Where(a => !string.IsNullOrWhiteSpace(a.SecondOpinionError))
                .OrderBy(a => a.OrderIndex)
                .ToList();

            if (secondOpinionFailedAnswers.Count > 0)
            {
                string firstError = secondOpinionFailedAnswers[0].SecondOpinionError!.Trim();
                if (firstError.Length > 200)
                {
                    firstError = firstError.Substring(0, 197) + "...";
                }
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **{secondOpinionFailedAnswers.Count} answer(s) met a trigger but the second-opinion call failed, so grader agreement is not measured for this run.** First error: `{firstError}`.");
            }
            else
            {
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **No answer met a trigger, so no answer was graded twice and grader agreement is not measured for this run.** The second-opinion assessor ({assessorName}) made no calls and appears in the Harness Cost figures only as zero.");
            }

            var scoredOkAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue).OrderBy(a => a.QualityScore!.Value).ToList();
            if (scoredOkAnswers.Count > 0)
            {
                var lowest = scoredOkAnswers[0];
                int threshold = scoringConstants.SecondOpinionQualityThreshold;
                if (threshold > 0)
                {
                    int margin = lowest.QualityScore!.Value - threshold;
                    if (margin >= 0)
                    {
                        sb.AppendLine($"- **Nearest miss:** lowest quality score {lowest.QualityScore.Value} (Q{lowest.OrderIndex}), {margin} points above the profile's threshold of {threshold}.");
                    }
                    else
                    {
                        sb.AppendLine($"- **Nearest miss:** lowest quality score {lowest.QualityScore.Value} (Q{lowest.OrderIndex}), below threshold {threshold}.");
                    }
                }
                else
                {
                    sb.AppendLine($"- **Nearest miss:** lowest quality score {lowest.QualityScore!.Value} (Q{lowest.OrderIndex}); no quality threshold configured.");
                }

                double median = Median(scoredOkAnswers.Select(a => (double)a.QualityScore!.Value));
                int roundedMedian = (int)Math.Round(median, MidpointRounding.AwayFromZero);
                int delta = scoringConstants.SecondOpinionOutlierDeltaPoints;
                int outlierCandidateCount = scoredOkAnswers.Count(a => median - a.QualityScore!.Value > delta);

                string outlierReason = outlierCandidateCount == 0
                    ? $"no answer is more than {delta} points below this run's median of {roundedMedian}"
                    : $"{outlierCandidateCount} answer(s) are more than {delta} points below this run's median of {roundedMedian}";
                sb.AppendLine($"- `FlaggedAndOutliers` would have re-graded {outlierCandidateCount} further answer(s) — {outlierReason}. `All` would have graded all {answeredForAgreement} twice; it is the only mode that measures grader agreement rather than sampling it.");
            }
            sb.AppendLine();
        }

        if (scoredAnswers.Count > 0)
        {
            sb.AppendLine("### Dimensional Score Averages");
            sb.AppendLine($"- **Accuracy (Weight 55%):** {Inv(scoredAnswers.Average(a => a.AccuracyScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.AccuracyLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Completeness (Weight 25%):** {Inv(scoredAnswers.Average(a => a.CompletenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.CompletenessLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Conciseness (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ConcisenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ConcisenessLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Readability (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ReadabilityScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ReadabilityLevel ?? 0), "F1")} / 6)");
            sb.AppendLine();
            if (BenchmarkChatTransfer.HasResponseStyleConflict(run, scoredAnswers, out double gap))
            {
                sb.AppendLine($"> **Response-style conflict.** This run graded a candidate instructed to *\"Default to 2–5 sentences per response\"* (`verboseMode: false`) against a Completeness dimension worth 25%, and Completeness is the weakest dimension by {Inv(gap, "F1")} points. Some of that gap may be the prompt rather than the model. Running the same suite at `verboseMode: true` separates the two; that run is not comparable with this one on Completeness, Conciseness or Readability.");
                sb.AppendLine();
            }
        }

        // Difficulty breakdown bucketed by AssessedDifficulty, using the shared band boundaries.
        // These used to be hardcoded here as 33/66 while the difficulty assessor was told
        // 35/70, so a question rated 35 *as Simple* was reported as Intermediate.
        int AssessedOf(BenchmarkRunAnswer a) =>
            a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty);

        var simpleAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsSimple(AssessedOf(a))).ToList();
        var intermediateAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsIntermediate(AssessedOf(a))).ToList();
        var advancedAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a))).ToList();

        string BandLine(string name, BenchmarkDifficulty band, List<BenchmarkRunAnswer> bucket)
        {
            string range = BenchmarkDifficultyBands.RangeLabel(band);
            if (bucket.Count == 0) return $"- **{name} ({range}):** None";

            // A band average alone cannot separate "uniformly mediocre" from "one bad answer":
            // the 2026-09-03 run's Simple band averaged 88.2 out of 60, 95, 97, 97 and 92. The
            // spread and the weakest question number say which of the two it was.
            var weakestInBand = bucket.OrderBy(a => a.QualityScore!.Value).ThenBy(a => a.OrderIndex).First();
            string dispersion = bucket.Count > 1
                ? $", range {bucket.Min(a => a.QualityScore!.Value)}–{bucket.Max(a => a.QualityScore!.Value)}, lowest Q{weakestInBand.OrderIndex}"
                : string.Empty;
            return $"- **{name} ({range}):** {Inv(bucket.Average(a => a.QualityScore!.Value), "F1")} / 100 " +
                   $"({bucket.Count} answered, avg diff: {Inv(bucket.Average(a => (double)AssessedOf(a)), "F0")}{dispersion})";
        }

        sb.AppendLine("### Difficulty Breakdown");
        sb.AppendLine(BandLine("Simple", BenchmarkDifficulty.Simple, simpleAssessed));
        sb.AppendLine(BandLine("Intermediate", BenchmarkDifficulty.Intermediate, intermediateAssessed));
        sb.AppendLine(BandLine("Advanced", BenchmarkDifficulty.Advanced, advancedAssessed));
        sb.AppendLine();

        int authoredSimple = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Simple);
        int authoredIntermediate = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Intermediate);
        int authoredAdvanced = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Advanced);
        sb.AppendLine($"- **Authored Band Distribution:** {authoredSimple} Simple, {authoredIntermediate} Intermediate, {authoredAdvanced} Advanced");
        sb.AppendLine();

        // Band Agreement. Without this, a reader sees an authored distribution of 6/6/6 next to
        // an answered breakdown of 5/7/6 and has no way to tell which question moved or why.
        var bandDisagreements = answers
            .Where(a => a.AssessedDifficulty.HasValue &&
                        BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value) != a.Difficulty)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        sb.AppendLine("### Band Agreement");
        if (bandDisagreements.Count == 0)
        {
            sb.AppendLine("All questions were assessed within their authored difficulty band.");
        }
        else
        {
            sb.AppendLine($"{bandDisagreements.Count} of {totalQuestions} question(s) were assessed outside their authored band. The Difficulty Breakdown above buckets by **assessed** difficulty, which is why its counts can differ from the authored distribution.");
            sb.AppendLine();
            foreach (var a in bandDisagreements)
            {
                sb.AppendLine($"- **Question {a.OrderIndex}:** authored {a.Difficulty} → assessed {BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty!.Value)} ({a.AssessedDifficulty.Value})");
            }
        }
        sb.AppendLine();

        if (simpleAssessed.Count > 0 && intermediateAssessed.Count > 0 && advancedAssessed.Count > 0)
        {
            double sAvg = simpleAssessed.Average(a => a.QualityScore!.Value);
            double iAvg = intermediateAssessed.Average(a => a.QualityScore!.Value);
            double aAvg = advancedAssessed.Average(a => a.QualityScore!.Value);
            if (sAvg < iAvg || iAvg < aAvg)
            {
                // When every critical-error cap landed in one band, that band's average is
                // depressed by the cap rather than by difficulty, and the generic explanation
                // is not the one that applies. On the 2026-09-03 run both caps fell on Simple
                // questions, which alone accounted for the inversion.
                var cappedAnswers = scoredAnswers.Where(a => a.CriticalError).ToList();
                string cappedBand = string.Empty;
                if (cappedAnswers.Count > 0)
                {
                    bool allSimple = cappedAnswers.All(a => BenchmarkDifficultyBands.IsSimple(AssessedOf(a)));
                    bool allIntermediate = cappedAnswers.All(a => BenchmarkDifficultyBands.IsIntermediate(AssessedOf(a)));
                    bool allAdvanced = cappedAnswers.All(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a)));
                    if (allSimple) cappedBand = "Simple";
                    else if (allIntermediate) cappedBand = "Intermediate";
                    else if (allAdvanced) cappedBand = "Advanced";
                }

                // Second branch: no cap explains it, so test whether the inversion rests on a
                // single answer. Removing the depressed band's weakest and re-checking the
                // ordering is the cheapest available discriminator between "this band is hard
                // for the model" and "one answer went wrong". On the 2026-09-03 run dropping Q1
                // lifts Simple from 88.2 to 95.3, above Intermediate's 90.0.
                string? SingleAnswerExplanation()
                {
                    var depressed = new List<(string Name, List<BenchmarkRunAnswer> Bucket)>();
                    if (sAvg < iAvg) depressed.Add(("Simple", simpleAssessed));
                    if (iAvg < aAvg) depressed.Add(("Intermediate", intermediateAssessed));

                    foreach (var (bandName, bucket) in depressed)
                    {
                        if (bucket.Count < 2) continue;

                        var weakest = bucket.OrderBy(x => x.QualityScore!.Value).ThenBy(x => x.OrderIndex).First();
                        double lifted = bucket.Where(x => !ReferenceEquals(x, weakest)).Average(x => x.QualityScore!.Value);

                        double s2 = ReferenceEquals(bucket, simpleAssessed) ? lifted : sAvg;
                        double i2 = ReferenceEquals(bucket, intermediateAssessed) ? lifted : iAvg;
                        if (s2 >= i2 && i2 >= aAvg)
                        {
                            return $"Removing the **{bandName}** band's single weakest answer (question {weakest.OrderIndex}, {weakest.QualityScore!.Value} / 100) lifts that band to {Inv(lifted, "F1")} and restores the ordering — read the inversion as one outlier, not a difficulty effect.";
                        }
                    }

                    return null;
                }

                string monotonicityCause = cappedBand.Length > 0
                    ? $"All {cappedAnswers.Count} critical-error cap(s) on this run fell in the **{cappedBand}** band (question(s) {string.Join(", ", cappedAnswers.OrderBy(a => a.OrderIndex).Select(a => a.OrderIndex.ToString()))}), which is what depressed that band's average — read the inversion as a critical-error effect, not a difficulty effect."
                    : SingleAnswerExplanation()
                      ?? "This is common on small question sets or when the model has specific domain strengths.";

                sb.AppendLine($"*Note:* Average quality does not decrease monotonically with assessed difficulty on this run (Simple: {Inv(sAvg, "F1")}, Intermediate: {Inv(iAvg, "F1")}, Advanced: {Inv(aAvg, "F1")}). {monotonicityCause}");
                sb.AppendLine();
            }
        }

        // Tool Usage Profile. The old report said a cap had been hit but never which tools
        // consumed the budget, leaving the operator no basis for tuning it: Q11 of the
        // 2026-09-03 run spent all 25 calls on wiki search churn and nothing said so.
        var toolCounts = BenchmarkChatTransfer.AggregateToolCounts(answers);

        sb.AppendLine("### Tool Usage Profile");
        int totalToolCalls = answers.Sum(a => a.ToolCallCount ?? 0);
        sb.AppendLine($"- **Total Tool Calls:** {totalToolCalls}");
        if (answers.Count > 0)
        {
            sb.AppendLine($"- **Mean Calls per Question:** {Inv(totalToolCalls / (double)answers.Count, "F1")}");
        }

        // Budget pressure. A question that stopped one call short of its budget is not
        // "exhausted" and is not flagged anywhere, yet it may have been cut off mid-
        // investigation — an outcome indistinguishable from a model choosing to stop. On the
        // 2026-09-03 run Q7 spent 34 of 35 and Q2 23 of 25, and nothing said so.
        var pressured = answers
            .Where(a => !a.ToolBudgetExhausted && (a.ToolCallsBlocked ?? 0) == 0
                        && a.ToolCallBudgetUsed.HasValue && a.ToolCallBudgetUsed.Value > 0
                        && a.ToolCallCount.HasValue
                        && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value * BudgetPressureFraction
                        && a.ToolCallCount.Value < a.ToolCallBudgetUsed.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        var saturated = answers
            .Where(a => !a.ToolBudgetExhausted && (a.ToolCallsBlocked ?? 0) == 0
                        && a.ToolCallBudgetUsed.HasValue && a.ToolCallBudgetUsed.Value > 0
                        && a.ToolCallCount.HasValue
                        && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        // Whether the cap cost anything is a different question from whether it was reached, and
        // the report used to answer only the second. On the 2026-09-03 run Q10 stopped one call
        // short of its budget and scored 60 — the worst answer in its band — and Q11 exhausted
        // its budget and scored 84, both against a run mean of 92. Marking the ones that scored
        // below the run's own mean is what turns "a cap was reached" into a testable hypothesis.
        string BelowMeanMarker(BenchmarkRunAnswer a)
        {
            if (!unweightedMean.HasValue || !a.QualityScore.HasValue) return string.Empty;
            return a.QualityScore.Value < unweightedMean.Value
                ? $" **— scored {a.QualityScore.Value}, below the run mean of {unweightedMean.Value}**"
                : string.Empty;
        }

        if (pressured.Count > 0)
        {
            sb.AppendLine($"- **Budget Pressure:** {pressured.Count} question(s) used at least {Inv(BudgetPressureFraction * 100, "F0")}% of the tool call budget without exhausting it — " +
                string.Join(", ", pressured.Select(a =>
                    $"Q{a.OrderIndex} {a.ToolCallCount!.Value}/{a.ToolCallBudgetUsed!.Value} ({a.ToolCallBudgetUsed.Value - a.ToolCallCount.Value} left){BelowMeanMarker(a)}")) +
                ". *An answer this close to its cap may have stopped investigating because of the cap rather than because it was finished.*");
        }

        if (saturated.Count > 0)
        {
            sb.AppendLine($"- **Budget Saturated:** {saturated.Count} question(s) reached the configured tool call budget without any calls blocked — " +
                string.Join(", ", saturated.Select(a =>
                    $"Q{a.OrderIndex} {a.ToolCallCount!.Value}/{a.ToolCallBudgetUsed!.Value}{BelowMeanMarker(a)}")) +
                ". *These questions used every allocated tool call but were not cut off mid-call; they may benefit from a larger budget.*");
        }

        var exhausted = answers
            .Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        if (exhausted.Count > 0)
        {
            sb.AppendLine($"- **Budget Exhausted:** {exhausted.Count} question(s) exhausted their tool call budget — " +
                string.Join(", ", exhausted.Select(a =>
                {
                    var (_, blocked) = ToolCallSplit(a);
                    string blockedClause = blocked > 0
                        ? $" ({blocked} calls refused by budget)"
                        : string.Empty;
                    return $"Q{a.OrderIndex} {a.ToolCallCount?.ToString() ?? "N/A"}/{a.ToolCallBudgetUsed?.ToString() ?? "N/A"}{blockedClause}{BelowMeanMarker(a)}";
                })) +
                ". *Further tool calls were refused; these questions were cut off mid-investigation.*");
        }

        var budgetConstrainedBelowMean = answers
            .Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0 || saturated.Contains(a) || pressured.Contains(a))
            .Where(a => unweightedMean.HasValue && a.QualityScore.HasValue && a.QualityScore.Value < unweightedMean.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (budgetConstrainedBelowMean.Count > 0)
        {
            sb.AppendLine($"- **Budget/Quality Correlation:** {budgetConstrainedBelowMean.Count} budget-constrained question(s) scored below the run's unweighted mean of {unweightedMean!.Value} — " +
                string.Join(", ", budgetConstrainedBelowMean.Select(a =>
                    $"Q{a.OrderIndex} ({a.QualityScore!.Value}, {(a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0 ? "budget exhausted" : saturated.Contains(a) ? "budget saturated" : "budget pressured")})")) +
                ". *The cap is a candidate explanation, not a demonstrated one — raise `Benchmark:ToolCallBudget:{Band}` for the affected band and re-run to test it.*");
        }

        // Grounding. An Advanced question answered from memory is not necessarily wrong, but it
        // is no longer testing source retrieval, which is what the Advanced band exists for.
        // Q14 and Q17 of the 2026-09-03 run each executed a single tool call at assessed 78 and
        // 79. This is a signal about the suite, not about the model.
        var ungroundedAdvanced = answers
            .Where(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a)) && (a.ToolCallCount ?? 0) <= 1)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (ungroundedAdvanced.Count > 0)
        {
            sb.AppendLine($"- **Grounding:** {ungroundedAdvanced.Count} Advanced-band question(s) answered with one tool call or fewer — " +
                string.Join(", ", ungroundedAdvanced.Select(a => $"Q{a.OrderIndex} ({a.ToolCallCount ?? 0})")) +
                ". *Worth reviewing as suite maintenance: these may no longer test source retrieval.*");
        }

        if (toolCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("| Tool | Successful Calls |");
            sb.AppendLine("|------|-----------------:|");
            foreach (var kv in toolCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"| `{kv.Key}` | {kv.Value} |");
            }
        }

        var routing = BenchmarkChatTransfer.AnalyzeToolRouting(answers);
        if (routing.TotalCalls > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Tool Routing");
            sb.AppendLine("Distribution of tool calls across functional tool families:");
            sb.AppendLine();
            sb.AppendLine("| Tool Family | Calls | Share |");
            sb.AppendLine("|-------------|------:|------:|");
            foreach (var fs in routing.RunWideStats.Where(s => s.CallCount > 0))
            {
                string famName = fs.Family switch
                {
                    BenchmarkToolFamily.SourceCode => "Source Code",
                    BenchmarkToolFamily.Wiki => "Wiki",
                    BenchmarkToolFamily.StructuredLookup => "Structured Lookup",
                    BenchmarkToolFamily.KnowledgeBase => "Knowledge Base",
                    _ => "Other"
                };
                sb.AppendLine($"| {famName} | {fs.CallCount} | {Inv(fs.SharePercentage, "F1")}% |");
            }
            if (HasKnowledgeBaseRoutingQuestion(answers))
            {
                sb.AppendLine($"- **Knowledge base under-use:** {routing.ZeroKnowledgeBaseAnswerCount} of {routing.AnsweredQuestionCount} answered question(s) made zero `get_knowledge_article` calls.");
            }
            else
            {
                sb.AppendLine($"- *Prompt observation:* {routing.ZeroKnowledgeBaseAnswerCount} of {routing.AnsweredQuestionCount} answered question(s) made zero `get_knowledge_article` calls. Per `Overseer/Services/ChatService.cs` § \"Information Routing\" and `Overseer/ToolGuides/get_knowledge_article.md`, the knowledge base is scoped to app navigation, settings, troubleshooting and platform documentation; for game mechanics, monsters, items, spells, or other topics not listed there, the prompt instructs the model to skip the knowledge base entirely.");
            }
            if (routing.CorrelationSampleSize >= 2)
            {
                string rTimeStr = routing.SourceShareModelTimeCorrelation.HasValue
                    ? Inv(routing.SourceShareModelTimeCorrelation.Value, "F2")
                    : "N/A";
                string rQualStr = routing.SourceShareQualityScoreCorrelation.HasValue
                    ? Inv(routing.SourceShareQualityScoreCorrelation.Value, "F2")
                    : "N/A";
                sb.AppendLine($"- **Source-family correlations (n = {routing.CorrelationSampleSize}):** r = {rTimeStr} with model time; r = {rQualStr} with quality score.");
            }
            if (routing.SourceFamilySharePercentage > 50.0)
            {
                // Do not quote the tool policy here. This line previously carried a hardcoded
                // paraphrase introduced as something "the production chat system prompt states",
                // and the prompt had never contained it — the policy is conditional, not a blanket
                // wiki-over-source preference. A hardcoded copy of a file that is edited
                // independently drifts silently, and a report that misquotes the prompt produces
                // findings about a rule that does not exist. Point at the policy file instead.
                sb.AppendLine($"- *Prompt observation:* Source code tools accounted for {Inv(routing.SourceFamilySharePercentage, "F1")}% of all tool calls. The tool preference hierarchy the candidate was given is in `Overseer/ToolGuides/_policy.md`: it routes strategy and general \"what is X\" questions to wiki tools first, routes specific mechanics questions (exact AC, damage dice, MR, resistances, speed, material, artifact flags) to the structured stats tools, and places source code tools at rung 4 for questions that require reading game logic. {routing.AdvancedQuestionCount} question(s) were assessed in the Advanced band, where source-level inspection is expected. Read this share against the suite's question mix and against the policy text itself — it is an observation for operator review, not a rule violation.");
            }
            sb.AppendLine();
            sb.AppendLine("*Note on tool ordering:* `ToolCallSummary` records aggregate call counts, not an ordered execution log (ordering is not recorded). Whether wiki tools were attempted before source code tools on any given question is not derivable from this data.");
        }

        var starved = answers.Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0).OrderBy(a => a.OrderIndex).ToList();
        if (starved.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Questions that exhausted their tool call budget:**");
            foreach (var a in starved)
            {
                sb.AppendLine($"- **Question {a.OrderIndex}** ({a.Difficulty}, assessed {AssessedOf(a)}): {FormatToolBudgetLine(a)}{BelowMeanMarker(a)}");
            }
        }
        sb.AppendLine();

        // 4. Questions and Replies
        sb.AppendLine("## 3. Questions and Replies");
        sb.AppendLine();

        foreach (var a in answers)
        {
            // Label with the same fallback the band buckets use, so an unassessed question is
            // never labelled 50 while being bucketed somewhere else.
            int shownDifficulty = AssessedOf(a);
            string assessedBandNote = a.AssessedDifficulty.HasValue &&
                                      BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value) != a.Difficulty
                ? $" → {BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value)}"
                : string.Empty;

            sb.AppendLine($"### Question {a.OrderIndex} [Authored: {a.Difficulty} | Assessed Diff: {shownDifficulty}{assessedBandNote}]");
            sb.AppendLine($"**Question:** {a.QuestionText}");
            sb.AppendLine();
            sb.AppendLine($"- **Status:** {a.Status}" + (a.HttpStatusCode.HasValue ? $" (HTTP {a.HttpStatusCode.Value})" : ""));
            string timingSuffix = a.ToolTimeMs.HasValue
                ? $", model {a.ModelTimeMs} ms, tools {a.ToolTimeMs.Value} ms"
                : string.Empty;
            sb.AppendLine($"- **Duration:** {a.DurationMs} ms (TTFT: {(a.TimeToFirstTokenMs.HasValue ? $"{a.TimeToFirstTokenMs.Value} ms" : "N/A")}{timingSuffix})");
            sb.AppendLine($"- **Tokens:** In={a.InputTokens ?? 0}, Out={a.OutputTokens ?? 0}, CacheRead={a.CacheReadInputTokens ?? 0}");
            if (!string.IsNullOrWhiteSpace(a.ActualServiceTierUsed))
            {
                sb.AppendLine($"- **Served Service Tier:** {a.ActualServiceTierUsed}");
            }
            if (!string.IsNullOrWhiteSpace(a.ToolCallSummary))
            {
                sb.AppendLine($"- **Tools Called:** `{a.ToolCallSummary}`");
            }
            if (a.ToolBudgetExhausted)
            {
                sb.AppendLine($"- **Tool Budget:** Exhausted — {FormatToolBudgetLine(a)} (configured limit, not an error)");
            }
            else if (a.ToolCallBudgetUsed.HasValue)
            {
                sb.AppendLine($"- **Tool Budget:** {FormatToolBudgetLine(a)}");
            }
            if (a.AnswerFlags != 0)
            {
                var flags = (BenchmarkAnswerFlags)a.AnswerFlags;
                sb.AppendLine($"- **Integrity Flags:** {flags}");
            }
            if (a.ScrubbedArtifactCount > 0)
            {
                sb.AppendLine($"- **Transport Artifacts Removed:** {a.ScrubbedArtifactCount} block(s) removed before grading");
            }
            sb.AppendLine();

            if (a.Status == BenchmarkAnswerStatus.ProviderError)
            {
                sb.AppendLine($"**Provider Error:** {a.ErrorMessage}");
            }
            else if (a.Status == BenchmarkAnswerStatus.EmptyAnswer)
            {
                sb.AppendLine("**Reply:** *(Empty answer produced)*");
            }
            else
            {
                sb.AppendLine("**Reply:**");
                sb.AppendLine();
                // Question headings are "###"; demote anything shallower so a model's own "##"
                // or "###" heading never lands at or above the report's own outline level.
                sb.AppendLine(DemoteAnswerHeadings(a.AnswerText, minLevel: 4));
            }
            sb.AppendLine();

            if (a.QualityScore.HasValue || a.AccuracyLevel.HasValue || !string.IsNullOrWhiteSpace(a.ReviewComment) ||
                (a.AssessedByModelConfigurationId.HasValue && a.AssessedByModelConfigurationId != run.AssessorModelConfigurationId))
            {
                sb.AppendLine("> **Evaluation:**");
                if (a.AccuracyLevel.HasValue)
                {
                    sb.AppendLine($"> - **Levels (0–6):** Accuracy={a.AccuracyLevel}/6 ({a.AccuracyScore} pts), Completeness={a.CompletenessLevel}/6 ({a.CompletenessScore} pts), Conciseness={a.ConcisenessLevel}/6 ({a.ConcisenessScore} pts), Readability={a.ReadabilityLevel}/6 ({a.ReadabilityScore} pts)");
                }

                if (a.QualityScore.HasValue)
                {
                    string rawPart = (a.RawQualityScore.HasValue && a.RawQualityScore.Value != a.QualityScore.Value)
                        ? $" (raw: {a.RawQualityScore.Value})"
                        : string.Empty;
                    sb.AppendLine($"> - **Quality Score:** {a.QualityScore.Value} / 100{rawPart}" + (a.CriticalError ? " *(CRITICAL ERROR CAP APPLIED)*" : ""));
                }
                else
                {
                    sb.AppendLine("> - **Quality Score:** N/A");
                }

                // Model time and the effective target, not the raw turn duration: those are the
                // two numbers the score is actually computed from, so printing DurationMs here
                // left a reader unable to check the arithmetic — and misleading by exactly the
                // tool time on a tool-heavy question.
                if (a.SpeedScore.HasValue)
                {
                    double speedTarget = BenchmarkScoring.EffectiveSpeedTargetMs(
                        a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty),
                        scoringConstants);
                    sb.AppendLine($"> - **Speed Score:** {a.SpeedScore.Value} / 100 (model {a.ModelTimeMs} ms vs target {Inv(Math.Round(speedTarget), "N0")} ms)");
                }
                else
                {
                    sb.AppendLine("> - **Speed Score:** N/A");
                }
                if (!string.IsNullOrWhiteSpace(a.ReviewComment))
                {
                    sb.AppendLine($"> - **Assessor Comment:** {a.ReviewComment}");
                }

                // What the deductions rest on. A score argued from the authored rubric and one
                // argued from the grader's own recall are different claims, and only the record
                // can tell them apart afterwards.
                var (accuracyEvidence, completenessEvidence, criticalErrorDemoted) = ReadEvidence(a);
                if (!string.IsNullOrWhiteSpace(accuracyEvidence))
                {
                    sb.AppendLine($"> - **Accuracy Evidence:** {accuracyEvidence}");
                }
                if (!string.IsNullOrWhiteSpace(completenessEvidence))
                {
                    sb.AppendLine($"> - **Completeness Evidence:** {completenessEvidence}");
                }
                if (a.CriticalError && !string.IsNullOrWhiteSpace(a.CriticalErrorQuote))
                {
                    sb.AppendLine($"> - **Critical Error Quote:** \"{a.CriticalErrorQuote}\"");
                }
                // The claims themselves, not just the count: the run-level Assessor Findings
                // block says how many there were, and this is the only place a reader can see
                // what they were and judge whether the rubric should have covered them.
                var unverifiedClaims = ReadUnverifiedClaims(a);
                if (unverifiedClaims.Count > 0)
                {
                    sb.AppendLine($"> - **Unverified Claims ({unverifiedClaims.Count}):** " +
                        string.Join("; ", unverifiedClaims.Select(c => $"\"{c}\"")) +
                        " — *neither confirmed nor refuted against the rubric; not an Accuracy deduction from harness version 7.*");
                }
                if (criticalErrorDemoted)
                {
                    sb.AppendLine("> - **Critical Error:** claimed by the assessor but not applied — the quoted claim could not be found in the graded answer, and an omission is not a critical error.");
                }
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0)
                {
                    var flaggedDims = new List<string>();
                    if (a.AccuracyLevel.HasValue && a.AccuracyLevel.Value <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel)
                    {
                        if (BenchmarkVerdictConsistency.IsNoFaultEvidence(accuracyEvidence))
                        {
                            string evDisplay = !string.IsNullOrWhiteSpace(accuracyEvidence) ? $"\"{accuracyEvidence.Trim()}\"" : "(none)";
                            flaggedDims.Add($"Accuracy to {a.AccuracyLevel.Value}/6 while its stated evidence ({evDisplay}) names no defect");
                        }
                        else if (BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(a.AccuracyLevel.Value, accuracyEvidence, a.UnverifiedClaimCount ?? unverifiedClaims.Count))
                        {
                            flaggedDims.Add($"Accuracy to {a.AccuracyLevel.Value}/6 while its stated evidence rests only on claims it could not verify, which scoring method v7 does not permit as an accuracy deduction");
                        }
                    }
                    if (a.CompletenessLevel.HasValue && a.CompletenessLevel.Value <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel && BenchmarkVerdictConsistency.IsNoFaultEvidence(completenessEvidence))
                    {
                        string evDisplay = !string.IsNullOrWhiteSpace(completenessEvidence) ? $"\"{completenessEvidence.Trim()}\"" : "(none)";
                        flaggedDims.Add($"Completeness to {a.CompletenessLevel.Value}/6 while its stated evidence ({evDisplay}) names no defect");
                    }

                    if (flaggedDims.Count > 0)
                    {
                        sb.AppendLine($"> - **Harness note:** the assessor docked {string.Join(" and ", flaggedDims)}. Advisory: the verdict stands and this did not change the score.");
                    }
                    else
                    {
                        sb.AppendLine("> - **Harness note:** the assessor docked a level while its stated evidence names no defect, or rests only on unverifiability. Advisory: the verdict stands and this did not change the score.");
                    }
                }
                if (a.SecondOpinionQualityScore.HasValue)
                {
                    string agreement = a.SecondOpinionDisagreed ? "**disagrees**" : "agrees";
                    string secondCritical = a.SecondOpinionCriticalError == true ? "yes" : "no";
                    // Why this answer was graded twice. "Every answer" and "this one looked
                    // wrong" are different facts about the same second verdict, and only the
                    // trigger separates them.
                    string triggerPart = string.IsNullOrWhiteSpace(a.SecondOpinionTrigger)
                        ? string.Empty
                        : $" (trigger: {TriggerLabel(a.SecondOpinionTrigger)})";
                    sb.AppendLine($"> - **Second Opinion ({a.SecondOpinionByModelDisplayNameUsed}):** {a.SecondOpinionQualityScore.Value} / 100, critical error {secondCritical} — {agreement} with the first verdict{triggerPart}. Advisory; the first verdict is what scored.");
                }
                if (!string.IsNullOrWhiteSpace(a.ClaimVerificationError))
                {
                    string verifierName = a.ClaimVerificationByModelDisplayNameUsed ?? run.ClaimVerifierDisplayNameUsed ?? "claim verifier";
                    string err = BenchmarkAssessmentFailure.Truncate(a.ClaimVerificationError, 200) ?? a.ClaimVerificationError;
                    sb.AppendLine($"> - **Claim Verification ({verifierName}):** failed — {err}. The unverified claims above were not checked.");
                }
                if (!string.IsNullOrWhiteSpace(a.ClaimVerificationJson) || a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue)
                {
                    string verifierName = a.ClaimVerificationByModelDisplayNameUsed ?? run.ClaimVerifierDisplayNameUsed ?? "claim verifier";
                    int sCount = a.ClaimsSupportedCount ?? 0;
                    int rCount = a.ClaimsRefutedCount ?? 0;
                    int iCount = a.ClaimsIndeterminateCount ?? 0;
                    sb.AppendLine($"> - **Claim Verification ({verifierName}):** {sCount} supported, {rCount} refuted, {iCount} indeterminate — *checked against source/wiki; advisory, not reflected in the score.*");
                }
                if (a.AssessedByModelConfigurationId.HasValue &&
                    a.AssessedByModelConfigurationId != run.AssessorModelConfigurationId)
                {
                    sb.AppendLine($"> - **Assessed by:** {a.AssessedByModelDisplayNameUsed} ({a.AssessedByModelProviderUsed}, {a.AssessedByModelIdUsed}) — differs from this run's assessor");
                }
                // A published index can move after publication. Where it did, the report says so
                // on the answer that moved, with the score it replaced.
                if (a.ReassessmentCount > 0)
                {
                    string previous = a.PreviousQualityScore.HasValue
                        ? $"{a.PreviousQualityScore.Value} / 100"
                        : "not recorded";
                    sb.AppendLine($"> - **Re-assessed:** {a.ReassessmentCount} time(s), most recently {(a.ReassessedAtUtc.HasValue ? Stamp(a.ReassessedAtUtc.Value) + " UTC" : "at an unrecorded time")} by {a.ReassessedByModelDisplayNameUsed ?? "an unrecorded model"} — the first verdict scored {previous} and this one replaced it.");
                }
                sb.AppendLine();
            }
        }

        // 5. Scoring Method & Configuration
        sb.AppendLine("## 4. Scoring Method & Configuration");
        sb.AppendLine();
        sb.AppendLine($"- **Scoring Method Version:** {run.ScoringMethodVersion}");
        sb.AppendLine($"- **Scoring Profile:** {run.ScoringProfile?.Name ?? "Default Intelligence Profile"}");
        sb.AppendLine($"- **Difficulty Fallback Applied:** {(run.DifficultyFallbackUsed ? "Yes (authored bands Simple=25, Intermediate=55, Advanced=85)" : "No (independently assessed)")}");
        sb.AppendLine();
        sb.AppendLine("### Compliance & Evaluation Terms");
        sb.AppendLine($"- **Purpose Statement:** {run.PurposeStatementUsed ?? "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack. Benchmark outputs are third-party generated content used solely for automated capability evaluation and scoring, and are not used for training, fine-tuning, distilling, or developing competing AI models."}");
        sb.AppendLine($"- **Third-Party Model Content:** Outputs generated by **{run.TestedModelDisplayNameUsed}** ({run.TestedModelProviderUsed}) and evaluated by **{run.AssessorModelDisplayNameUsed}** ({run.AssessorModelProviderUsed}) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.");
        sb.AppendLine("- **Distillation / Training Prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.");
        bool isSameProvider = string.Equals(run.TestedModelProviderUsed, run.AssessorModelProviderUsed, StringComparison.OrdinalIgnoreCase);
        if (isSameProvider)
        {
            sb.AppendLine($"- **Same-Provider Evaluation Notice:** Both candidate model ({run.TestedModelDisplayNameUsed}) and assessor model ({run.AssessorModelDisplayNameUsed}) belong to the same provider ({run.TestedModelProviderUsed}). Same-provider evaluation acknowledged: **{(run.SameProviderAcknowledged ? "Yes" : "No")}**.");
        }
        sb.AppendLine();
        sb.AppendLine("### Aggregation Formulas");
        sb.AppendLine("- **Quality Score:** $Quality = A^{0.55} \\cdot C^{0.25} \\cdot Cn^{0.10} \\cdot R^{0.10}$ (capped at 25 if criticalError is true)");
        sb.AppendLine("- **Model Time:** $ModelTime = \\max(0, \\text{DurationMs} - \\text{ToolTimeMs})$ — the turn duration with harness tool I/O removed");
        sb.AppendLine("- **Speed Target:** $Target(q) = T \\cdot (1 + s \\cdot \\text{Difficulty}(q) / 100)$, where $T$ is SpeedTargetMs and $s$ is SpeedDifficultyScaling");
        sb.AppendLine("- **Speed Score:** $Speed = \\text{clamp}(100 - k \\cdot \\log_2(\\text{ModelTime} / Target(q)), 1, 100)$, where $k$ is SpeedDecayK");
        sb.AppendLine("- **Intelligence Index:** $\\Sigma(\\text{Difficulty}(q) \\cdot \\text{Quality}(q)) / \\Sigma(\\text{Difficulty}(q))$ (answered questions only). Quality only: the Speed Index is reported separately by design and is not folded in.");
        sb.AppendLine("- **Speed Index:** equal-weight mean of $Speed(q)$ over answered questions. Difficulty enters through $Target(q)$, not through the weight; weighting here as well would count difficulty twice and pull the index toward the floor.");
        sb.AppendLine();
        sb.AppendLine($"> **Comparing Speed Indices:** thinking level dominates model time, so a Speed Index is comparable between runs at the same thinking level and misleading across levels. This run used thinking level **{run.TestedModelThinkingLevelUsed ?? "Default"}**.");
        sb.AppendLine();
        if (run.SpeedMeasurementDegraded)
        {
            sb.AppendLine("> ⚠️ **Concurrency Timing Notice:** This run was executed with concurrency (MaxParallelQuestions > 1). Model turn durations include resource queueing against simultaneous requests. Speed Index is advisory.");
            sb.AppendLine();
        }

        // 6. Issues
        sb.AppendLine("## 5. Issues");
        sb.AppendLine();
        var issueAnswers = answers.Where(a =>
            a.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed or BenchmarkAnswerStatus.EmptyAnswer
            || a.ToolBudgetExhausted
            || a.AnswerFlags != 0).ToList();

        if (issueAnswers.Count == 0 && string.IsNullOrEmpty(run.ErrorMessage))
        {
            sb.AppendLine("None. All questions completed cleanly without provider outages, degradation, or unexpected failures.");
        }
        else
        {
            if (!string.IsNullOrEmpty(run.ErrorMessage))
            {
                sb.AppendLine($"- **Run Level Error:** {run.ErrorMessage}");
            }
            foreach (var ia in issueAnswers)
            {
                var iaFlags = (BenchmarkAnswerFlags)ia.AnswerFlags;
                var flagDescriptions = new List<string>();
                if (ia.Status == BenchmarkAnswerStatus.EmptyAnswer) flagDescriptions.Add("Empty answer");
                if (ia.Status == BenchmarkAnswerStatus.ProviderError) flagDescriptions.Add($"Provider error (HTTP {ia.HttpStatusCode}): {ia.ErrorMessage}");
                if (ia.Status == BenchmarkAnswerStatus.Failed) flagDescriptions.Add($"Failed: {ia.ErrorMessage}");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts))
                {
                    flagDescriptions.Add($"Transport artifacts removed before grading ({ia.ScrubbedArtifactCount} block(s)) — recovered, and graded normally; a provider-path defect, not a damaged answer");
                }
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.Truncated)) flagDescriptions.Add("Answer truncated (output token limit)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed))
                {
                    // Same signal as the run-level advisory sentence: NarrationBlockCount where
                    // the run recorded it, and the weaker ScrubbedArtifactText proxy for runs
                    // that predate it. A run before harness version 6 cannot distinguish
                    // "removed" from "detected and left in place", and says so rather than
                    // asserting the flattering reading.
                    if (!ia.NarrationBlockCount.HasValue)
                    {
                        flagDescriptions.Add(!string.IsNullOrWhiteSpace(ia.ScrubbedArtifactText)
                            ? "Reasoning narration detected; removal not recorded for this run (advisory)"
                            : "Reasoning narration present in the graded answer (advisory)");
                    }
                    else
                    {
                        flagDescriptions.Add(ia.NarrationBlockCount.Value > 0
                            ? $"Reasoning narration removed before grading — {ia.NarrationBlockCount.Value} block(s) (advisory)"
                            : "Reasoning narration present in the graded answer (advisory)");
                    }
                }
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.RepeatedFragments)) flagDescriptions.Add("Repeated reasoning fragments in the removed narration (advisory)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ContestedVerdict)) flagDescriptions.Add("Contested verdict (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction)) flagDescriptions.Add("Unevidenced deduction (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.RefutedClaim)) flagDescriptions.Add("Refuted claim (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.OmissionAsAccuracy)) flagDescriptions.Add("Omission docked as accuracy (advisory, changed no score)");
                if (ia.ToolBudgetExhausted)
                {
                    flagDescriptions.Add($"Tool call budget reached ({FormatToolBudgetLine(ia)}) — configured harness limit, not an error");
                }

                if (flagDescriptions.Count == 0 && ia.Status == BenchmarkAnswerStatus.Ok)
                {
                    continue;
                }

                string desc = string.Join("; ", flagDescriptions);
                string note = (ia.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed or BenchmarkAnswerStatus.EmptyAnswer)
                    ? " *(Note: Excluded from scoring)*"
                    : string.Empty;
                sb.AppendLine($"- **Question {ia.OrderIndex}:** Status {ia.Status} — {desc}.{note}");
            }
        }
        sb.AppendLine();

        var stageFailedAnswers = answers.Where(a =>
            !string.IsNullOrWhiteSpace(a.AssessmentError)
            || !string.IsNullOrWhiteSpace(a.ClaimVerificationError)
            || !string.IsNullOrWhiteSpace(a.SecondOpinionError))
            .OrderBy(a => a.OrderIndex)
            .ToList();

        if (stageFailedAnswers.Count > 0)
        {
            sb.AppendLine("### Harness Stage Failures");
            sb.AppendLine("*(Advisory infrastructure failures; candidate output was not damaged)*");
            sb.AppendLine();
            foreach (var sfa in stageFailedAnswers)
            {
                var stageErrors = new List<string>();
                if (!string.IsNullOrWhiteSpace(sfa.AssessmentError))
                {
                    stageErrors.Add($"Assessment failed: {sfa.AssessmentError.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(sfa.ClaimVerificationError))
                {
                    stageErrors.Add($"Claim verification failed: {sfa.ClaimVerificationError.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(sfa.SecondOpinionError))
                {
                    stageErrors.Add($"Second opinion failed: {sfa.SecondOpinionError.Trim()}");
                }
                sb.AppendLine($"- **Question {sfa.OrderIndex}:** {string.Join("; ", stageErrors)}");
            }
            sb.AppendLine();
        }

        // Synthesis divergence. The run-level synthesis reads every verdict at once and can name
        // a hallucination the per-question grader declined to flag — which is what happened on
        // the 2026-09-03 run, where the synthesis made a fabrication its headline finding while
        // that question's own verdict returned criticalError: false. The per-question verdict is
        // what scored, so this is advisory, but the contradiction belongs in the record.
        var synthesisNamed = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            run.AssessmentText,
            answers.Select(a => a.OrderIndex));
        var synthesisDivergent = synthesisNamed
            .Select(index => answers.FirstOrDefault(a => a.OrderIndex == index))
            .Where(a => a != null && !a.CriticalError)
            .Select(a => a!)
            .ToList();
        if (synthesisDivergent.Count > 0)
        {
            sb.AppendLine("### Synthesis Divergence");
            sb.AppendLine();
            foreach (var d in synthesisDivergent)
            {
                sb.AppendLine($"- **Question {d.OrderIndex}:** the run synthesis reports a hallucination that the per-question verdict did not flag as a critical error. Advisory — the per-question verdict is what scored.");
            }
            sb.AppendLine();
        }

        // Disputed assessments. A single grader deciding a low score is the least reproducible
        // part of this benchmark, so where a second one was asked and disagreed, the report says
        // so rather than presenting one verdict as settled fact.
        var disputed = answers.Where(a => a.SecondOpinionDisagreed && a.SecondOpinionQualityScore.HasValue)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (disputed.Count > 0)
        {
            sb.AppendLine("### Disputed Assessments");
            sb.AppendLine();
            sb.AppendLine($"{disputed.Count} answer(s) were re-graded by a second assessor, which reached a materially different verdict. The first verdict is what scored; these are flagged for a human to settle, and re-assessing from the run detail is the way to do it.");
            sb.AppendLine();
            foreach (var d in disputed)
            {
                string claimNote = string.Empty;
                if (d.ClaimsSupportedCount.HasValue || d.ClaimsRefutedCount.HasValue || d.ClaimsIndeterminateCount.HasValue)
                {
                    claimNote = $" [Claims: {d.ClaimsSupportedCount ?? 0} supported, {d.ClaimsRefutedCount ?? 0} refuted, {d.ClaimsIndeterminateCount ?? 0} indeterminate]";
                }
                sb.AppendLine($"- **Question {d.OrderIndex}:** first {d.QualityScore ?? 0} / 100 (critical error {(d.CriticalError ? "yes" : "no")}, {d.AssessedByModelDisplayNameUsed}) vs second {d.SecondOpinionQualityScore!.Value} / 100 (critical error {(d.SecondOpinionCriticalError == true ? "yes" : "no")}, {d.SecondOpinionByModelDisplayNameUsed}){claimNote}");
            }
            sb.AppendLine();
        }

        // 7. Assessment
        sb.AppendLine("## 6. Synthesis Assessment");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(run.AssessmentText))
        {
            sb.AppendLine(run.AssessmentText);
        }
        else if (run.AssessmentParseFailed)
        {
            sb.AppendLine("*Note: The assessment output could not be parsed into the structured schema. Raw output:*");
            sb.AppendLine();
            sb.AppendLine(run.AssessmentJson);
        }
        else
        {
            sb.AppendLine("No synthesis assessment generated.");
        }
        sb.AppendLine();

        int totalRefutedClaims = run.ClaimsRefutedCount > 0 ? run.ClaimsRefutedCount : answers.Sum(a => a.ClaimsRefutedCount ?? 0);
        int disputedVerdicts = answers.Count(a => a.SecondOpinionDisagreed && a.SecondOpinionQualityScore.HasValue);
        if (totalRefutedClaims > 0 || disputedVerdicts > 0)
        {
            sb.AppendLine($"*The synthesis above is the primary assessor's own narrative. This run recorded {totalRefutedClaims} refuted claim(s) and {disputedVerdicts} disputed verdict(s) — see Run Integrity and Disputed Assessments.*");
            sb.AppendLine();
        }

        // 8. Final Score
        sb.AppendLine("## 7. Final Indices");
        sb.AppendLine();
        sb.AppendLine($"# **Intelligence Index: {run.QualityIndex?.ToString() ?? "N/A"}{seText} / 100**");
        // As in the summary, only printed when a critical-error cap actually moved it.
        if (rawQualityIndex.HasValue && rawQualityIndex.Value != (run.QualityIndex ?? 0))
        {
            sb.AppendLine($"### Raw Quality Index: {rawQualityIndex.Value} / 100");
        }
        sb.AppendLine($"### Speed Index: {run.SpeedIndex?.ToString() ?? "N/A"} / 100");
        sb.AppendLine($"### Holistic Assessor Score: {run.FinalScore?.ToString() ?? "N/A"} / 100");
        sb.AppendLine();
        sb.AppendLine("> **How to read these:** the Intelligence Index is the canonical, reproducible metric and is **quality only** — Speed Index is not folded into it, by design, so a slow model and an inaccurate one are never confused for each other. The Holistic Assessor Score is the assessor's own narrative judgement and is reported for contrast, not used in any aggregate.");
        sb.AppendLine();
        // The Intelligence Index weights each question by its assessed difficulty, so a critical
        // error caps that one question's Quality at 25 but moves the overall index least on the
        // easiest questions — on the 2026-09-03 run a fully hallucinated answer at assessed
        // difficulty 25 moved the index by one point. The Critical Errors count under Results
        // Summary above is the figure to read for this failure mode, not the index delta.
        sb.AppendLine("> The Intelligence Index weights each question by its assessed difficulty, so a critical error on an easy question moves the index the least of all — a fully hallucinated answer at assessed difficulty 25 can move it by as little as one point. The **Critical Errors** count under Results Summary is the number to read for this failure mode.");
        sb.AppendLine();
        if (run.FinalScore.HasValue && run.QualityIndex.HasValue)
        {
            int diff = Math.Abs(run.FinalScore.Value - run.QualityIndex.Value);
            if (diff > 10)
            {
                sb.AppendLine($"> ℹ️ **Index Divergence Notice:** The assessor's holistic synthesis score ({run.FinalScore.Value}) differs by {diff} points from the difficulty-weighted Intelligence Index ({run.QualityIndex.Value}). The Intelligence Index is the reproducible canonical metric.");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string FormatDuration(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
        return $"{ts.Seconds}.{ts.Milliseconds / 100}s";
    }

    private static readonly string[] KnowledgeBaseTopicKeywords = new[]
    {
        "navigation", "settings", "options", "troubleshooting", "crash",
        "account", "controls", "replay", "save management", "import", "export",
        "system requirements", "developer tools", "vault", "get_knowledge_article"
    };

    private static bool HasKnowledgeBaseRoutingQuestion(IEnumerable<BenchmarkRunAnswer> answers)
    {
        foreach (var a in answers)
        {
            if (IsKnowledgeBaseTopicText(a.QuestionText) || IsKnowledgeBaseTopicText(a.BenchmarkQuestion?.ExpectedPoints))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsKnowledgeBaseTopicText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var kw in KnowledgeBaseTopicKeywords)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(kw)}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
