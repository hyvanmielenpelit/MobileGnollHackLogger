namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

public enum BenchmarkToolFamily
{
    SourceCode,
    Wiki,
    StructuredLookup,
    KnowledgeBase,
    Other
}

public sealed record BenchmarkToolFamilyStats
{
    public BenchmarkToolFamily Family { get; init; }
    public int CallCount { get; init; }
    public double SharePercentage { get; init; }
}

public sealed record BenchmarkBandToolFamilyStats
{
    public BenchmarkDifficulty Difficulty { get; init; }
    public int TotalCalls { get; init; }
    public IReadOnlyList<BenchmarkToolFamilyStats> FamilyStats { get; init; } = Array.Empty<BenchmarkToolFamilyStats>();
}

public sealed record BenchmarkToolRoutingAnalysis
{
    public int TotalCalls { get; init; }
    public IReadOnlyDictionary<BenchmarkToolFamily, int> FamilyCalls { get; init; } = new Dictionary<BenchmarkToolFamily, int>();
    public IReadOnlyList<BenchmarkToolFamilyStats> RunWideStats { get; init; } = Array.Empty<BenchmarkToolFamilyStats>();
    public IReadOnlyList<BenchmarkBandToolFamilyStats> BandStats { get; init; } = Array.Empty<BenchmarkBandToolFamilyStats>();
    public int AnsweredQuestionCount { get; init; }
    public int ZeroKnowledgeBaseAnswerCount { get; init; }
    public double? SourceShareModelTimeCorrelation { get; init; }
    public double? SourceShareQualityScoreCorrelation { get; init; }
    public int CorrelationSampleSize { get; init; }
    public double SourceFamilySharePercentage { get; init; }
    public int AdvancedQuestionCount { get; init; }
}

/// <summary>
/// Pure static analyser over benchmark answers extracting tool routing and chat transfer signals.
/// Provides tool-family classification, usage aggregation, correlation analysis, and response-style
/// conflict detection.
/// </summary>
public static class BenchmarkChatTransfer
{
    public const double LevelStepThreshold = 13.0;

    public static BenchmarkToolFamily ClassifyTool(string toolName)
    {
        return (toolName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "source_code_search" or "source_code_view" or "search_definitions"
                or "get_function_definition" or "get_constants" or "list_indexed_files" => BenchmarkToolFamily.SourceCode,
            "wiki_search" or "wiki_view" or "nethack_wiki_search" or "nethack_wiki_view" => BenchmarkToolFamily.Wiki,
            "monster_lookup" or "item_lookup" or "get_monster_stats" or "get_item_stats" => BenchmarkToolFamily.StructuredLookup,
            "get_knowledge_article" => BenchmarkToolFamily.KnowledgeBase,
            _ => BenchmarkToolFamily.Other
        };
    }

    public static SortedDictionary<string, int> ParseToolCallCounts(string? toolCallSummary)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(toolCallSummary)) return counts;

        foreach (var entry in toolCallSummary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int sep = entry.IndexOf('×');
            if (sep <= 0) continue;

            string name = entry.Substring(0, sep).Trim();
            string countPart = new string(entry.Substring(sep + 1).TakeWhile(char.IsDigit).ToArray());
            if (name.Length == 0 || name.StartsWith('(') || !int.TryParse(countPart, out int n)) continue;

            counts[name] = counts.TryGetValue(name, out int prev) ? prev + n : n;
        }
        return counts;
    }

    /// <summary>
    /// The per-tool-name call counts of one answer, from the best source that answer has: its own
    /// <see cref="BenchmarkRunAnswer.ToolCalls"/> rows when it has any, and
    /// <see cref="ParseToolCallCounts"/> over <see cref="BenchmarkRunAnswer.ToolCallSummary"/>
    /// otherwise. Rows exist only for runs from harness 17 onward and no backfill is possible, so
    /// for every earlier run the summary is not merely the fallback — it is the only record.
    ///
    /// The row path counts <b>succeeded rows only</b>, grouped by
    /// <see cref="BenchmarkRunAnswerToolCall.Name"/>. That is not a simplification: it is what makes
    /// the two paths mean the same thing. <c>ToolCallSummary</c> lists, per tool name, only the calls
    /// whose status was <c>completed</c> with no error, so folding the failed and refused rows in
    /// here — the obvious "use the richer data" move — would inflate a harness-17 run's tool-name
    /// counts above a harness-16 run's for identical model behaviour, and every family share and
    /// correlation derived from them would be incomparable across that boundary while looking fine.
    /// The failed and refused calls are reported on their own, from
    /// <see cref="BenchmarkToolCallRecorder.Outcomes"/>.
    ///
    /// This is the single place the rows-else-summary decision is made; every reader in this class
    /// goes through it, so run-wide totals and per-answer shares cannot be drawn from two different
    /// sources.
    /// </summary>
    public static SortedDictionary<string, int> ToolCallCountsFor(BenchmarkRunAnswer answer)
    {
        if (answer == null) return new SortedDictionary<string, int>(StringComparer.Ordinal);

        if (answer.ToolCalls == null || answer.ToolCalls.Count == 0)
        {
            return ParseToolCallCounts(answer.ToolCallSummary);
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in answer.ToolCalls)
        {
            if (row == null) continue;

            string name = (row.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            // Classified by the recorder rather than by a status test written out here, so this
            // path and the report's outcome split cannot come to disagree about what "succeeded"
            // means — including the budget-refusal wording, which is scope-dependent.
            var (succeeded, _, _) = BenchmarkToolCallRecorder.Outcomes(new[] { row });
            if (succeeded == 0) continue;

            counts[name] = counts.TryGetValue(name, out int prev) ? prev + 1 : 1;
        }

        return counts;
    }

    public static SortedDictionary<string, int> AggregateToolCounts(IEnumerable<BenchmarkRunAnswer> answers)
    {
        var total = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var a in answers)
        {
            var counts = ToolCallCountsFor(a);
            foreach (var (k, v) in counts)
            {
                total[k] = total.TryGetValue(k, out int prev) ? prev + v : v;
            }
        }
        return total;
    }

    public static BenchmarkToolRoutingAnalysis AnalyzeToolRouting(
        IReadOnlyList<BenchmarkRunAnswer> answers)
    {
        // The gradeable-answer population, not Status == Ok: every figure this report shows is
        // over the set the quality index is computed on, and one line running a narrower
        // population than the rest is a report that disagrees with itself about how many
        // questions the run answered. A model-produced-empty answer is gradeable — scoring
        // method 10 scores it 0 — and made whatever tool calls it made.
        var answered = answers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex).ToList();
        var toolCounts = AggregateToolCounts(answered);
        int totalCalls = toolCounts.Values.Sum();

        var families = new[]
        {
            BenchmarkToolFamily.SourceCode,
            BenchmarkToolFamily.Wiki,
            BenchmarkToolFamily.StructuredLookup,
            BenchmarkToolFamily.KnowledgeBase,
            BenchmarkToolFamily.Other
        };

        var runWideFamilyCounts = new Dictionary<BenchmarkToolFamily, int>();
        foreach (var f in families) runWideFamilyCounts[f] = 0;

        foreach (var (tool, count) in toolCounts)
        {
            var family = ClassifyTool(tool);
            runWideFamilyCounts[family] += count;
        }

        var runWideStats = families.Select(f => new BenchmarkToolFamilyStats
        {
            Family = f,
            CallCount = runWideFamilyCounts[f],
            SharePercentage = totalCalls > 0 ? (runWideFamilyCounts[f] * 100.0 / totalCalls) : 0.0
        }).ToList();

        // By difficulty band
        var bandStatsList = new List<BenchmarkBandToolFamilyStats>();
        foreach (var diff in new[] { BenchmarkDifficulty.Simple, BenchmarkDifficulty.Intermediate, BenchmarkDifficulty.Advanced })
        {
            var bandAnswers = answered.Where(a =>
            {
                int d = a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty);
                return diff switch
                {
                    BenchmarkDifficulty.Simple => BenchmarkDifficultyBands.IsSimple(d),
                    BenchmarkDifficulty.Intermediate => BenchmarkDifficultyBands.IsIntermediate(d),
                    BenchmarkDifficulty.Advanced => BenchmarkDifficultyBands.IsAdvanced(d),
                    _ => false
                };
            }).ToList();

            var bandToolCounts = AggregateToolCounts(bandAnswers);
            int bandTotal = bandToolCounts.Values.Sum();

            var bandFamilyCounts = new Dictionary<BenchmarkToolFamily, int>();
            foreach (var f in families) bandFamilyCounts[f] = 0;

            foreach (var (tool, count) in bandToolCounts)
            {
                var family = ClassifyTool(tool);
                bandFamilyCounts[family] += count;
            }

            bandStatsList.Add(new BenchmarkBandToolFamilyStats
            {
                Difficulty = diff,
                TotalCalls = bandTotal,
                FamilyStats = families.Select(f => new BenchmarkToolFamilyStats
                {
                    Family = f,
                    CallCount = bandFamilyCounts[f],
                    SharePercentage = bandTotal > 0 ? (bandFamilyCounts[f] * 100.0 / bandTotal) : 0.0
                }).ToList()
            });
        }

        // Knowledge base under-use: answers with zero get_knowledge_article calls
        int zeroKbCount = 0;
        foreach (var a in answered)
        {
            var counts = ToolCallCountsFor(a);
            if (!counts.TryGetValue("get_knowledge_article", out int kbCalls) || kbCalls == 0)
            {
                zeroKbCount++;
            }
        }

        // Correlations
        var sourceShares = new List<double>();
        var modelTimes = new List<double>();
        var qualityScores = new List<double>();

        foreach (var a in answered)
        {
            if (!a.QualityScore.HasValue) continue;

            var counts = ToolCallCountsFor(a);
            int ansTotal = counts.Values.Sum();
            int sourceCalls = counts.Where(kvp => ClassifyTool(kvp.Key) == BenchmarkToolFamily.SourceCode).Sum(kvp => kvp.Value);
            double share = ansTotal > 0 ? ((double)sourceCalls / ansTotal) : 0.0;

            sourceShares.Add(share);
            modelTimes.Add(a.ModelTimeMs);
            qualityScores.Add(a.QualityScore.Value);
        }

        double? rTime = PearsonCorrelation(sourceShares, modelTimes);
        double? rQuality = PearsonCorrelation(sourceShares, qualityScores);

        int advancedCount = answered.Count(a =>
        {
            int d = a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty);
            return BenchmarkDifficultyBands.IsAdvanced(d);
        });

        double sourceSharePct = totalCalls > 0
            ? (runWideFamilyCounts[BenchmarkToolFamily.SourceCode] * 100.0 / totalCalls)
            : 0.0;

        return new BenchmarkToolRoutingAnalysis
        {
            TotalCalls = totalCalls,
            FamilyCalls = runWideFamilyCounts,
            RunWideStats = runWideStats,
            BandStats = bandStatsList,
            AnsweredQuestionCount = answered.Count,
            ZeroKnowledgeBaseAnswerCount = zeroKbCount,
            SourceShareModelTimeCorrelation = rTime,
            SourceShareQualityScoreCorrelation = rQuality,
            CorrelationSampleSize = sourceShares.Count,
            SourceFamilySharePercentage = sourceSharePct,
            AdvancedQuestionCount = advancedCount
        };
    }

    public static double? ComputePearsonCorrelation(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        return PearsonCorrelation(xs, ys);
    }

    public static double? PearsonCorrelation(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (xs.Count != ys.Count || xs.Count < 2) return null;
        double avgX = xs.Average();
        double avgY = ys.Average();
        double sumX2 = 0;
        double sumY2 = 0;
        double sumXY = 0;

        for (int i = 0; i < xs.Count; i++)
        {
            double dx = xs[i] - avgX;
            double dy = ys[i] - avgY;
            sumX2 += dx * dx;
            sumY2 += dy * dy;
            sumXY += dx * dy;
        }

        if (sumX2 == 0 || sumY2 == 0) return null;
        return sumXY / Math.Sqrt(sumX2 * sumY2);
    }

    /// <summary>
    /// Evaluates whether a response-style conflict exists from dimension averages.
    /// Returns true when VerboseMode is false, Completeness is the lowest of all 4 dimensions,
    /// and the gap between Accuracy and Completeness is at least <see cref="LevelStepThreshold"/> points (13.0).
    /// </summary>
    public static bool HasResponseStyleConflict(
        bool verboseMode,
        double? accuracyAverage,
        double? completenessAverage,
        double? concisenessAverage,
        double? readabilityAverage)
    {
        if (verboseMode || !accuracyAverage.HasValue || !completenessAverage.HasValue ||
            !concisenessAverage.HasValue || !readabilityAverage.HasValue)
        {
            return false;
        }

        double acc = accuracyAverage.Value;
        double comp = completenessAverage.Value;
        double conc = concisenessAverage.Value;
        double read = readabilityAverage.Value;

        if (comp < acc && comp < conc && comp < read)
        {
            double gap = acc - comp;
            return gap >= LevelStepThreshold;
        }

        return false;
    }

    /// <summary>
    /// Evaluates whether a response-style conflict exists for the given run and answers.
    /// Returns true when VerboseMode is false, Completeness is the lowest of all 4 dimensions,
    /// and the gap between Accuracy and Completeness exceeds <see cref="LevelStepThreshold"/> points (13.0).
    /// </summary>
    public static bool HasResponseStyleConflict(
        BenchmarkRun run,
        IReadOnlyList<BenchmarkRunAnswer> answers,
        out double gap)
    {
        gap = 0.0;
        var promptOptions = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson);
        if (promptOptions.VerboseMode)
        {
            return false;
        }

        var scored = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue).ToList();
        if (scored.Count == 0) return false;

        double accAvg = scored.Average(a => a.AccuracyScore ?? 0);
        double compAvg = scored.Average(a => a.CompletenessScore ?? 0);
        double concAvg = scored.Average(a => a.ConcisenessScore ?? 0);
        double readAvg = scored.Average(a => a.ReadabilityScore ?? 0);

        if (HasResponseStyleConflict(promptOptions.VerboseMode, accAvg, compAvg, concAvg, readAvg))
        {
            gap = accAvg - compAvg;
            return true;
        }

        return false;
    }
}
