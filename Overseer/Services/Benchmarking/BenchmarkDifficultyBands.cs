namespace Overseer.Services.Benchmarking;

using MobileGnollHackLogger.Data;

/// <summary>
/// The single source of truth for assessed-difficulty band boundaries.
///
/// These were previously defined twice and disagreed: the difficulty assessor was told
/// 1-35 / 36-70 / 71-100, while the report bucketed 1-33 / 34-66 / 67-100. A question the
/// assessor rated 35 *as Simple* was therefore reported as Intermediate, and a 70 rated *as
/// Intermediate* was reported as Advanced.
///
/// The prompt's boundaries win, because those are the ones the rating was actually made against.
/// </summary>
public static class BenchmarkDifficultyBands
{
    public const int SimpleMax = 35;
    public const int IntermediateMax = 70;
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 100;

    public static int SimpleMin => MinDifficulty;
    public static int IntermediateMin => SimpleMax + 1;
    public static int AdvancedMin => IntermediateMax + 1;

    public static BenchmarkDifficulty BandOf(int difficulty)
    {
        if (difficulty <= SimpleMax) return BenchmarkDifficulty.Simple;
        if (difficulty <= IntermediateMax) return BenchmarkDifficulty.Intermediate;
        return BenchmarkDifficulty.Advanced;
    }

    public static bool IsSimple(int difficulty) => BandOf(difficulty) == BenchmarkDifficulty.Simple;

    public static bool IsIntermediate(int difficulty) => BandOf(difficulty) == BenchmarkDifficulty.Intermediate;

    public static bool IsAdvanced(int difficulty) => BandOf(difficulty) == BenchmarkDifficulty.Advanced;

    /// <summary>Inclusive range label for a band, e.g. "36–70".</summary>
    public static string RangeLabel(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => $"{SimpleMin}–{SimpleMax}",
        BenchmarkDifficulty.Intermediate => $"{IntermediateMin}–{IntermediateMax}",
        BenchmarkDifficulty.Advanced => $"{AdvancedMin}–{MaxDifficulty}",
        _ => $"{MinDifficulty}–{MaxDifficulty}"
    };
}
