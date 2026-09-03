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
        if (string.IsNullOrWhiteSpace(run.ScoringProfileSnapshotJson))
        {
            return BenchmarkScoringConstants.Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(run.ScoringProfileSnapshotJson);
            var root = doc.RootElement;
            var defaults = BenchmarkScoringConstants.Default;

            int target = root.TryGetProperty("SpeedTargetMs", out var t) && t.TryGetInt32(out int tv)
                ? tv
                : defaults.SpeedTargetMs;
            double scaling = root.TryGetProperty("SpeedDifficultyScaling", out var s) && s.TryGetDouble(out double sv)
                ? sv
                : defaults.SpeedDifficultyScaling;
            double decay = root.TryGetProperty("SpeedDecayK", out var k) && k.TryGetDouble(out double kv)
                ? kv
                : defaults.SpeedDecayK;
            int secondOpinion = root.TryGetProperty("SecondOpinionQualityThreshold", out var o) && o.TryGetInt32(out int ov)
                ? ov
                : defaults.SecondOpinionQualityThreshold;

            return defaults with
            {
                SpeedTargetMs = target,
                SpeedDifficultyScaling = scaling,
                SpeedDecayK = decay,
                SecondOpinionQualityThreshold = secondOpinion
            };
        }
        catch (JsonException)
        {
            // A malformed snapshot must not stop a report from rendering; the figures it
            // annotates are already stored on the answers.
            return BenchmarkScoringConstants.Default;
        }
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
        int blocked = 0;

        if (!string.IsNullOrWhiteSpace(answer.ToolCallSummary))
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

    private static long Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    public static string BuildMarkdownReport(BenchmarkRun run, string? overseerVersion = null)
    {
        var sb = new StringBuilder();
        // Read back from the run's own profile snapshot, so the report describes the run in
        // front of it rather than whatever the default profile says today.
        var scoringConstants = ScoringConstantsOf(run);

        // 1. Introduction
        sb.AppendLine("# GnollHack Overseer AI Intelligence Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"This report contains the automated domain knowledge, reasoning, and efficiency benchmark results for suite **{run.SuiteName}**, evaluated against model **{run.TestedModelDisplayNameUsed}** ({run.TestedModelProviderUsed} / {run.TestedModelIdUsed}).");
        sb.AppendLine($"Run conducted on {run.StartedAtUtc:yyyy-MM-dd HH:mm:ss} UTC" + (!string.IsNullOrEmpty(run.StartedByUser?.UserName) ? $" by {run.StartedByUser.UserName}." : "."));
        sb.AppendLine();
        sb.AppendLine("> *Note:* This benchmark evaluates domain-specific roguelike intelligence, codebase comprehension, and tool usage within the GnollHack Overseer harness. Scoring uses Behaviorally Anchored Rating Scales (BARS), weighted geometric aggregation, and logarithmic speed decay.");
        sb.AppendLine();

        // 2. Run Manifest
        sb.AppendLine("## 1. Run Manifest");
        sb.AppendLine();
        sb.AppendLine($"- **Overseer Version:** {overseerVersion ?? "1.0.0"}");
        sb.AppendLine($"- **Suite Name:** {run.SuiteName}");
        sb.AppendLine($"- **Total Questions:** {run.TotalQuestionCount}");
        sb.AppendLine($"- **Answered Questions:** {run.AnsweredQuestionCount} of {run.TotalQuestionCount}");
        sb.AppendLine($"- **Run Status:** {run.Status}");
        sb.AppendLine($"- **Start Time (UTC):** {run.StartedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **End Time (UTC):** {(run.CompletedAtUtc.HasValue ? run.CompletedAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "In Progress / Interrupted")}");
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
        sb.AppendLine($"- **Parallel Tool Calls:** {run.TestedModelParallelExecutionModeUsed} *(provider-side tool batching, unrelated to Question Parallelism)*");
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
            sb.AppendLine("- **Trigger:** a critical error, or a quality score below the scoring profile's second-opinion threshold. Advisory: the first verdict is what scored.");
        }
        else
        {
            sb.AppendLine("- **None selected.** No answer in this run was re-graded by a second assessor.");

            // What was forgone, stated in the run's own numbers. The 2026-09-03 run produced
            // two critical errors with no second opinion selected — precisely the trigger the
            // feature exists for — and nothing in the report connected the two facts.
            int wouldCritical = answers.Count(a => a.CriticalError);
            int threshold = scoringConstants.SecondOpinionQualityThreshold;
            int wouldThreshold = threshold > 0
                ? answers.Count(a => !a.CriticalError && a.QualityScore.HasValue && a.QualityScore.Value < threshold)
                : 0;
            int wouldTotal = wouldCritical + wouldThreshold;
            if (wouldTotal > 0)
            {
                string thresholdPart = threshold > 0
                    ? $"below the profile's threshold of {threshold}: {wouldThreshold}"
                    : "score trigger disabled";
                sb.AppendLine($"- **{wouldTotal} answer(s) would have been re-graded** had a second opinion assessor been selected (critical error: {wouldCritical}; {thresholdPart}).");
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
        sb.AppendLine($"### **Intelligence Index: {(run.QualityIndex.HasValue ? $"{run.QualityIndex.Value} / 100" : "Not Scored")}**");
        // Only shown when a critical-error cap actually moved it. Printing the identical number
        // under a second heading told the reader nothing.
        if (rawQualityIndex.HasValue && rawQualityIndex.Value != (run.QualityIndex ?? 0))
        {
            sb.AppendLine($"### **Raw Quality Index: {rawQualityIndex.Value} / 100 ({cappedCount} question(s) capped by critical error)**");
        }
        sb.AppendLine($"### **Speed Index: {(run.SpeedIndex.HasValue ? $"{run.SpeedIndex.Value} / 100" : "Not Scored")}**" + (run.SpeedMeasurementDegraded ? " *(Advisory — measured under concurrency)*" : ""));
        // A critical error caps Quality at 25 (see BenchmarkScoring), which the Raw/Intelligence
        // Index pair above already shows as a point delta — but that delta is diluted by every
        // *other* answer's difficulty weight, so a single hallucinated answer can move the index
        // by as little as one point (see the "How to read these" note under Final Indices). The
        // headline below gives the reader the actual count instead of asking them to infer it
        // from a small index shift.
        var criticalErrorAnswers = answers.Where(a => a.CriticalError).OrderBy(a => a.OrderIndex).ToList();
        if (criticalErrorAnswers.Count > 0)
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
        if (run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0 || run.TotalAssessmentDurationMs > 0)
        {
            sb.AppendLine("### Harness Cost");
            sb.AppendLine($"- **Candidate Tokens:** {Inv(run.TotalInputTokens, "N0")} in / {Inv(run.TotalOutputTokens, "N0")} out");
            sb.AppendLine($"- **Assessor Tokens:** {Inv(run.TotalAssessmentInputTokens, "N0")} in / {Inv(run.TotalAssessmentOutputTokens, "N0")} out");
            sb.AppendLine($"- **Total Tokens:** {Inv(run.TotalInputTokens + run.TotalAssessmentInputTokens, "N0")} in / {Inv(run.TotalOutputTokens + run.TotalAssessmentOutputTokens, "N0")} out");
            sb.AppendLine($"- **Assessment Time:** {FormatDuration(run.TotalAssessmentDurationMs)} ({Inv(run.TotalAssessmentDurationMs, "N0")} ms)");
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
            ? "— *advisory only; these overlap the categories above, do not affect the run status, and the text they describe was removed before grading.*"
            : $"— *advisory only; these overlap the categories above and do not affect the run status. Removed before grading in {bleedRemoved} of {bleedCount}; in the remainder the text was detected but remained in the graded answer.*";
        if (bleedUnrecorded > 0)
        {
            advisoryNote += $" *Removal was not recorded for {bleedUnrecorded} of these — the run predates harness version {BenchmarkAssessmentPrompt.HarnessVersion}, which added the counter; that figure is inferred, not measured.*";
        }
        sb.AppendLine($"- **Advisory Flags:** {advisoryCount} (reasoning bleed: {bleedCount}, repeated fragments: {repeatCount}) {advisoryNote}");
        sb.AppendLine($"- **Answers Scrubbed:** {scrubbedAnyCount} of {totalQuestions} (transport payloads: {scrubbedTransportCount}, reasoning narration: {bleedRemoved})");
        sb.AppendLine();

        if (scoredAnswers.Count > 0)
        {
            sb.AppendLine("### Dimensional Score Averages");
            sb.AppendLine($"- **Accuracy (Weight 55%):** {Inv(scoredAnswers.Average(a => a.AccuracyScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.AccuracyLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Completeness (Weight 25%):** {Inv(scoredAnswers.Average(a => a.CompletenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.CompletenessLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Conciseness (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ConcisenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ConcisenessLevel ?? 0), "F1")} / 6)");
            sb.AppendLine($"- **Readability (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ReadabilityScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ReadabilityLevel ?? 0), "F1")} / 6)");
            sb.AppendLine();
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
            return $"- **{name} ({range}):** {Inv(bucket.Average(a => a.QualityScore!.Value), "F1")} / 100 " +
                   $"({bucket.Count} answered, avg diff: {Inv(bucket.Average(a => (double)AssessedOf(a)), "F0")})";
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

                string monotonicityCause = cappedBand.Length > 0
                    ? $"All {cappedAnswers.Count} critical-error cap(s) on this run fell in the **{cappedBand}** band (question(s) {string.Join(", ", cappedAnswers.OrderBy(a => a.OrderIndex).Select(a => a.OrderIndex.ToString()))}), which is what depressed that band's average — read the inversion as a critical-error effect, not a difficulty effect."
                    : "This is common on small question sets or when the model has specific domain strengths.";

                sb.AppendLine($"*Note:* Average quality does not decrease monotonically with assessed difficulty on this run (Simple: {Inv(sAvg, "F1")}, Intermediate: {Inv(iAvg, "F1")}, Advanced: {Inv(aAvg, "F1")}). {monotonicityCause}");
                sb.AppendLine();
            }
        }

        // Tool Usage Profile. The old report said a cap had been hit but never which tools
        // consumed the budget, leaving the operator no basis for tuning it: Q11 of the
        // 2026-09-03 run spent all 25 calls on wiki search churn and nothing said so.
        var toolCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var a in answers)
        {
            if (string.IsNullOrWhiteSpace(a.ToolCallSummary)) continue;

            foreach (var entry in a.ToolCallSummary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // Entries look like "wiki_search×11"; a trailing "(n blocked by budget)" note is
                // parenthesised and carries no tool name.
                int sep = entry.IndexOf('×');
                if (sep <= 0) continue;

                string name = entry.Substring(0, sep).Trim();
                string countPart = new string(entry.Substring(sep + 1).TakeWhile(char.IsDigit).ToArray());
                if (name.Length == 0 || name.StartsWith('(') || !int.TryParse(countPart, out int n)) continue;

                toolCounts[name] = toolCounts.TryGetValue(name, out int prev) ? prev + n : n;
            }
        }

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
            .Where(a => !a.ToolBudgetExhausted
                        && a.ToolCallBudgetUsed.HasValue && a.ToolCallBudgetUsed.Value > 0
                        && a.ToolCallCount.HasValue
                        && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value * BudgetPressureFraction)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (pressured.Count > 0)
        {
            sb.AppendLine($"- **Budget Pressure:** {pressured.Count} question(s) used at least {Inv(BudgetPressureFraction * 100, "F0")}% of the tool call budget without exhausting it — " +
                string.Join(", ", pressured.Select(a =>
                    $"Q{a.OrderIndex} {a.ToolCallCount!.Value}/{a.ToolCallBudgetUsed!.Value} ({a.ToolCallBudgetUsed.Value - a.ToolCallCount.Value} left)")) +
                ". *An answer this close to its cap may have stopped investigating because of the cap rather than because it was finished.*");
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

        var starved = answers.Where(a => a.ToolBudgetExhausted).OrderBy(a => a.OrderIndex).ToList();
        if (starved.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Questions that reached their tool call budget:**");
            foreach (var a in starved)
            {
                sb.AppendLine($"- **Question {a.OrderIndex}** ({a.Difficulty}, assessed {AssessedOf(a)}): {FormatToolBudgetLine(a)}");
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
                if (criticalErrorDemoted)
                {
                    sb.AppendLine("> - **Critical Error:** claimed by the assessor but not applied — the quoted claim could not be found in the graded answer, and an omission is not a critical error.");
                }
                if (a.SecondOpinionQualityScore.HasValue)
                {
                    string agreement = a.SecondOpinionDisagreed ? "**disagrees**" : "agrees";
                    string secondCritical = a.SecondOpinionCriticalError == true ? "yes" : "no";
                    sb.AppendLine($"> - **Second Opinion ({a.SecondOpinionByModelDisplayNameUsed}):** {a.SecondOpinionQualityScore.Value} / 100, critical error {secondCritical} — {agreement} with the first verdict. Advisory; the first verdict is what scored.");
                }
                if (a.AssessedByModelConfigurationId.HasValue &&
                    a.AssessedByModelConfigurationId != run.AssessorModelConfigurationId)
                {
                    sb.AppendLine($"> - **Assessed by:** {a.AssessedByModelDisplayNameUsed} ({a.AssessedByModelProviderUsed}, {a.AssessedByModelIdUsed}) — differs from this run's assessor");
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
                if (ia.ToolBudgetExhausted)
                {
                    flagDescriptions.Add($"Tool call budget reached ({FormatToolBudgetLine(ia)}) — configured harness limit, not an error");
                }

                string desc = string.Join("; ", flagDescriptions);
                string note = (ia.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed or BenchmarkAnswerStatus.EmptyAnswer)
                    ? " *(Note: Excluded from scoring)*"
                    : string.Empty;
                sb.AppendLine($"- **Question {ia.OrderIndex}:** Status {ia.Status} — {desc}.{note}");
            }
        }
        sb.AppendLine();

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
                sb.AppendLine($"- **Question {d.OrderIndex}:** first {d.QualityScore ?? 0} / 100 (critical error {(d.CriticalError ? "yes" : "no")}, {d.AssessedByModelDisplayNameUsed}) vs second {d.SecondOpinionQualityScore!.Value} / 100 (critical error {(d.SecondOpinionCriticalError == true ? "yes" : "no")}, {d.SecondOpinionByModelDisplayNameUsed})");
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

        // 8. Final Score
        sb.AppendLine("## 7. Final Indices");
        sb.AppendLine();
        sb.AppendLine($"# **Intelligence Index: {run.QualityIndex?.ToString() ?? "N/A"} / 100**");
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
}
