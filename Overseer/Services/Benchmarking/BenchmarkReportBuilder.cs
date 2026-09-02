namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;

public static class BenchmarkReportBuilder
{
    private static string Inv(IFormattable value, string? format = null)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
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
        sb.AppendLine($"- **Parallel Questions Setting:** {run.MaxParallelQuestionsUsed}" + (run.SpeedMeasurementDegraded ? " *(Speed metrics advisory due to concurrency)*" : " *(Sequential, strict timing)*"));
        sb.AppendLine();

        // Comparability block
        sb.AppendLine("### Comparability");
        sb.AppendLine($"- **Harness Version:** {run.HarnessVersion ?? "1 (unversioned legacy)"}");
        sb.AppendLine($"- **Scoring Method Version:** {run.ScoringMethodVersion}");
        sb.AppendLine($"- **Scoring Profile:** {run.ScoringProfile?.Name ?? "Default Intelligence Profile"}");
        sb.AppendLine($"- **Tool Call Budget per Question:** {(run.MaxToolCallsPerQuestionUsed.HasValue ? run.MaxToolCallsPerQuestionUsed.Value.ToString() : "unlimited (legacy)")}");
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
        sb.AppendLine($"- **Parallel Execution Mode:** {run.TestedModelParallelExecutionModeUsed}");
        sb.AppendLine();
        sb.AppendLine("### Assessment Model");
        sb.AppendLine($"- **Display Name:** {run.AssessorModelDisplayNameUsed}");
        sb.AppendLine($"- **Provider:** {run.AssessorModelProviderUsed}");
        sb.AppendLine($"- **Model ID:** {run.AssessorModelIdUsed}");
        sb.AppendLine($"- **Thinking Level:** {run.AssessorModelThinkingLevelUsed ?? "Default"}");
        sb.AppendLine($"- **Reasoning Mode:** {run.AssessorModelReasoningModeUsed ?? "Default"}");
        sb.AppendLine();

        // 3. Results Summary
        var answers = run.Answers.OrderBy(a => a.OrderIndex).ToList();
        var scoredAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue).ToList();

        var rawScorableItems = scoredAnswers
            .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();
        int? rawQualityIndex = BenchmarkScoring.QualityIndex(rawScorableItems);
        int cappedCount = scoredAnswers.Count(a => a.RawQualityScore.HasValue && a.QualityScore.HasValue && a.RawQualityScore.Value > a.QualityScore.Value);

        sb.AppendLine("## 2. Results Summary");
        sb.AppendLine();
        sb.AppendLine($"### **Intelligence Index: {(run.QualityIndex.HasValue ? $"{run.QualityIndex.Value} / 100" : "Not Scored")}**");
        if (rawQualityIndex.HasValue)
        {
            if (rawQualityIndex.Value == (run.QualityIndex ?? 0))
            {
                sb.AppendLine($"### **Raw Quality Index: {rawQualityIndex.Value} / 100**");
            }
            else
            {
                sb.AppendLine($"### **Raw Quality Index: {rawQualityIndex.Value} / 100 ({cappedCount} question(s) capped by critical error)**");
            }
        }
        sb.AppendLine($"### **Speed Index: {(run.SpeedIndex.HasValue ? $"{run.SpeedIndex.Value} / 100" : "Not Scored")}**" + (run.SpeedMeasurementDegraded ? " *(Advisory — measured under concurrency)*" : ""));
        sb.AppendLine();
        sb.AppendLine($"- **Holistic Assessor Score:** {(run.FinalScore.HasValue ? $"{run.FinalScore.Value} / 100" : "N/A")}");
        sb.AppendLine($"- **Total Model Answer Duration:** {FormatDuration(run.TotalAnswerDurationMs)} ({Inv(run.TotalAnswerDurationMs, "N0")} ms)");

        var durations = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).Select(a => a.DurationMs).OrderBy(d => d).ToList();
        if (durations.Count > 0)
        {
            long p50 = Percentile(durations, 0.50);
            long p90 = Percentile(durations, 0.90);
            long maxD = durations[^1];
            sb.AppendLine($"- **Turn Duration Percentiles:** Median (P50) = {Inv(p50, "N0")} ms, P90 = {Inv(p90, "N0")} ms, Max = {Inv(maxD, "N0")} ms");
        }

        sb.AppendLine($"- **Total Input Tokens:** {Inv(run.TotalInputTokens, "N0")}");
        sb.AppendLine($"- **Total Output Tokens:** {Inv(run.TotalOutputTokens, "N0")}");
        sb.AppendLine($"- **Total Cache Read Tokens:** {Inv(run.TotalCacheReadTokens, "N0")}");
        sb.AppendLine($"- **Total Cache Creation Tokens:** {Inv(run.TotalCacheCreationTokens, "N0")}");
        sb.AppendLine();

        // Run Integrity block
        int totalQuestions = run.TotalQuestionCount;
        int emptyCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.EmptyAnswer || ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Empty));
        int artifactCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        int truncatedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Truncated));
        int starvedCount = answers.Count(a => a.ToolBudgetExhausted);
        int providerErrorCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.ProviderError);
        int degradedCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.EmptyAnswer || a.AnswerFlags != 0 || a.ToolBudgetExhausted);
        int cleanCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok && a.AnswerFlags == 0 && !a.ToolBudgetExhausted);
        double cleanPct = totalQuestions > 0 ? (cleanCount * 100.0 / totalQuestions) : 0.0;

        sb.AppendLine("### Run Integrity");
        sb.AppendLine($"- **Clean Answers:** {cleanCount} of {totalQuestions} ({Inv(cleanPct, "F1")}%)");
        sb.AppendLine($"- **Degraded Answers:** {degradedCount} (empty: {emptyCount}, harness artifacts: {artifactCount}, truncated: {truncatedCount})");
        sb.AppendLine($"- **Tool-Starved Answers:** {starvedCount} of {totalQuestions}");
        sb.AppendLine($"- **Provider Errors:** {providerErrorCount}");
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

        // Difficulty breakdown bucketed by AssessedDifficulty
        var simpleAssessed = scoredAnswers.Where(a => (a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)) <= 33).ToList();
        var intermediateAssessed = scoredAnswers.Where(a => {
            int d = a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty);
            return d >= 34 && d <= 66;
        }).ToList();
        var advancedAssessed = scoredAnswers.Where(a => (a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)) >= 67).ToList();

        sb.AppendLine("### Difficulty Breakdown");
        sb.AppendLine($"- **Simple (1–33):** {(simpleAssessed.Count > 0 ? $"{Inv(simpleAssessed.Average(a => a.QualityScore!.Value), "F1")} / 100 ({simpleAssessed.Count} answered, avg diff: {Inv(simpleAssessed.Average(a => a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)), "F0")})" : "None")}");
        sb.AppendLine($"- **Intermediate (34–66):** {(intermediateAssessed.Count > 0 ? $"{Inv(intermediateAssessed.Average(a => a.QualityScore!.Value), "F1")} / 100 ({intermediateAssessed.Count} answered, avg diff: {Inv(intermediateAssessed.Average(a => a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)), "F0")})" : "None")}");
        sb.AppendLine($"- **Advanced (67–100):** {(advancedAssessed.Count > 0 ? $"{Inv(advancedAssessed.Average(a => a.QualityScore!.Value), "F1")} / 100 ({advancedAssessed.Count} answered, avg diff: {Inv(advancedAssessed.Average(a => a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)), "F0")})" : "None")}");
        sb.AppendLine();

        int authoredSimple = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Simple);
        int authoredIntermediate = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Intermediate);
        int authoredAdvanced = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Advanced);
        sb.AppendLine($"- **Authored Band Distribution:** {authoredSimple} Simple, {authoredIntermediate} Intermediate, {authoredAdvanced} Advanced");
        sb.AppendLine();

        if (simpleAssessed.Count > 0 && intermediateAssessed.Count > 0 && advancedAssessed.Count > 0)
        {
            double sAvg = simpleAssessed.Average(a => a.QualityScore!.Value);
            double iAvg = intermediateAssessed.Average(a => a.QualityScore!.Value);
            double aAvg = advancedAssessed.Average(a => a.QualityScore!.Value);
            if (sAvg < iAvg || iAvg < aAvg)
            {
                sb.AppendLine($"*Note:* Average quality does not decrease monotonically with assessed difficulty on this run (Simple: {Inv(sAvg, "F1")}, Intermediate: {Inv(iAvg, "F1")}, Advanced: {Inv(aAvg, "F1")}). This is common on small question sets or when the model has specific domain strengths.");
                sb.AppendLine();
            }
        }

        // 4. Questions and Replies
        sb.AppendLine("## 3. Questions and Replies");
        sb.AppendLine();

        foreach (var a in answers)
        {
            sb.AppendLine($"### Question {a.OrderIndex} [{a.Difficulty} | Assessed Diff: {a.AssessedDifficulty ?? 50}]");
            sb.AppendLine($"**Question:** {a.QuestionText}");
            sb.AppendLine();
            sb.AppendLine($"- **Status:** {a.Status}" + (a.HttpStatusCode.HasValue ? $" (HTTP {a.HttpStatusCode.Value})" : ""));
            sb.AppendLine($"- **Duration:** {a.DurationMs} ms (TTFT: {(a.TimeToFirstTokenMs.HasValue ? $"{a.TimeToFirstTokenMs.Value} ms" : "N/A")})");
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
                sb.AppendLine("- **Tool Budget:** Exhausted (hit per-question cap)");
            }
            if (a.AnswerFlags != 0)
            {
                var flags = (BenchmarkAnswerFlags)a.AnswerFlags;
                sb.AppendLine($"- **Integrity Flags:** {flags}");
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
                sb.AppendLine(a.AnswerText);
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

                sb.AppendLine($"> - **Speed Score:** {(a.SpeedScore.HasValue ? $"{a.SpeedScore.Value} / 100" : "N/A")} ({a.DurationMs} ms)");
                if (!string.IsNullOrWhiteSpace(a.ReviewComment))
                {
                    sb.AppendLine($"> - **Assessor Comment:** {a.ReviewComment}");
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
        sb.AppendLine("- **Speed Score:** $Speed = \\text{clamp}(100 - 25 \\cdot \\log_2(\\text{DurationMs} / 5000), 1, 100)$");
        sb.AppendLine("- **Intelligence Index:** $\\Sigma(\\text{Difficulty}(q) \\cdot \\text{Quality}(q)) / \\Sigma(\\text{Difficulty}(q))$ (answered questions only)");
        sb.AppendLine("- **Speed Index:** $\\Sigma(\\text{Difficulty}(q) \\cdot \\text{Speed}(q)) / \\Sigma(\\text{Difficulty}(q))$ (answered questions only)");
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
                var flagDescriptions = new List<string>();
                if (ia.Status == BenchmarkAnswerStatus.EmptyAnswer) flagDescriptions.Add("Empty answer");
                if (ia.Status == BenchmarkAnswerStatus.ProviderError) flagDescriptions.Add($"Provider error (HTTP {ia.HttpStatusCode}): {ia.ErrorMessage}");
                if (ia.Status == BenchmarkAnswerStatus.Failed) flagDescriptions.Add($"Failed: {ia.ErrorMessage}");
                if (((BenchmarkAnswerFlags)ia.AnswerFlags).HasFlag(BenchmarkAnswerFlags.HarnessArtifacts)) flagDescriptions.Add("Harness artifacts detected in answer");
                if (((BenchmarkAnswerFlags)ia.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Truncated)) flagDescriptions.Add("Answer truncated (output token limit)");
                if (ia.ToolBudgetExhausted) flagDescriptions.Add("Tool call budget exhausted");

                string desc = string.Join("; ", flagDescriptions);
                string note = (ia.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed or BenchmarkAnswerStatus.EmptyAnswer)
                    ? " *(Note: Excluded from scoring)*"
                    : string.Empty;
                sb.AppendLine($"- **Question {ia.OrderIndex}:** Status {ia.Status} — {desc}.{note}");
            }
        }
        sb.AppendLine();

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
        if (rawQualityIndex.HasValue)
        {
            sb.AppendLine($"### Raw Quality Index: {rawQualityIndex.Value} / 100");
        }
        sb.AppendLine($"### Speed Index: {run.SpeedIndex?.ToString() ?? "N/A"} / 100");
        sb.AppendLine($"### Holistic Assessor Score: {run.FinalScore?.ToString() ?? "N/A"} / 100");
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
