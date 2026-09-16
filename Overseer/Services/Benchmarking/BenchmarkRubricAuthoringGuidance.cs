namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The rubric format, grading semantics, worked example and difficulty band descriptions. Read by
/// <see cref="BenchmarkGenerationPrompt"/> and served to the admin client, which assembles the
/// YAML import instructions for an AI from it.
/// </summary>
/// <remarks>
/// The multi-line constants are verbatim strings, so their line breaks follow the checkout's line
/// endings, as <see cref="System.Text.StringBuilder.AppendLine()"/> does.
/// </remarks>
public static class BenchmarkRubricAuthoringGuidance
{
    public const string FormLabel = "**FORM** (not graded — presentation note only)";

    public const string SectionRules =
@"1. **BOARD FACTS**: List every factual game state claim the rubric relies on. EVERY board fact must be directly verifiable and quotable from the snapshot. Do not hallucinate or assume items, HP, positions, or stats not present on the board.
2. **REQUIRED**: Specific correct decisions, tactical advice, or warnings the candidate must provide.
3. **CRITICAL ERROR**: Severe blunders or lethal mistakes (e.g. meleeing a mind flayer with low HP, praying while on timeout) that fail the answer.
4. **SCOPE**: Boundaries of the question (e.g. immediate turn vs long-term).
5. " + FormLabel + @": Expected answer structure. Label this section exactly """ + FormLabel + @""". It is a presentation note the assessor does not score: a FORM suggestion is never a Readability criterion and the section must not assert a grading consequence.
6. **SOURCE**: Must specify ""**SOURCE** — board"" for facts grounded in this snapshot, and relevant C source files or wiki citations for game mechanics.";

    public const string GradingSemantics =
@"Only REQUIRED and CRITICAL ERROR points are ever charged. SCOPE and FORM are notes the assessor records and never deducts for.
A CRITICAL ERROR is a false claim the answer would have to make, quotable verbatim; an omission is never a critical error.
State player-visible values, or say which internal unit is meant.
Every BOARD FACT must be quotable from the snapshot; every mechanics point names its source file; mark an inference as an inference.
Never derive a rubric point from a candidate answer.
A snapshot-suite question must be unanswerable without the snapshot and should ask for a decision, not a transcription.";

    public const string WorkedExample =
@"**BOARD FACTS**
- HP is 12 out of 60 (20% remaining).
- An adjacent hostile master mind flayer is to the east.
- Inventory contains a wand of teleportation (0:3) and an uncursed potion of extra healing.
- Prayer timeout is 0 (prayer is safe).

**REQUIRED**
- Identify the lethal immediate threat of mind flayer brain-eating attacks.
- Recommend an immediate survival action: zap wand of teleportation at self or the flayer, or pray.
- Advise against engaging in melee combat this turn.

**CRITICAL ERROR**
- Recommending attacking in melee or drinking a standard potion while adjacent without defense.

**SCOPE**
- The turn 120 tactical emergency. Do not require long-term ascension advice.

" + FormLabel + @"
- Direct tactical assessment with immediate recommended action first.

**SOURCE** — board; C source: src/mhit.c (mind flayer attack), include/you.c (prayer safety)";

    /// <summary>What questions in a band test; follows the band name and range in the generation prompt.</summary>
    public static string BandDescription(BenchmarkDifficulty difficulty) => difficulty switch
    {
        BenchmarkDifficulty.Simple => "Questions focused on immediate tactical survival, direct monster threats, obvious escape item identification, standard inventory assessment, and urgent turn-1 decisions directly visible on the board.",
        BenchmarkDifficulty.Intermediate => "Questions requiring multi-turn tactical planning, risk/reward assessment, non-trivial resource combinations, companion handling, prayer safety calculations, route/branch choices, or identification risk tradeoffs.",
        BenchmarkDifficulty.Advanced => "Questions testing obscure engine interactions, complex damage or survival probability calculations, subtle GnollHack vs NetHack divergences (e.g. runewords), deep inventory and spell synergy, or edge-case escape sequences under severe constraints.",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null)
    };

    public static IReadOnlyList<BenchmarkDifficulty> Bands { get; } =
        new[] { BenchmarkDifficulty.Simple, BenchmarkDifficulty.Intermediate, BenchmarkDifficulty.Advanced };

    public static BenchmarkRubricAuthoringGuidanceDto ToDto()
    {
        var bands = new List<BenchmarkRubricAuthoringBandDto>();
        foreach (var band in Bands)
        {
            bands.Add(new BenchmarkRubricAuthoringBandDto
            {
                Name = band.ToString(),
                Range = BenchmarkDifficultyBands.RangeLabel(band),
                Description = BandDescription(band)
            });
        }

        return new BenchmarkRubricAuthoringGuidanceDto
        {
            SectionRules = SectionRules,
            GradingSemantics = GradingSemantics,
            WorkedExample = WorkedExample,
            FormLabel = FormLabel,
            Bands = bands
        };
    }
}
