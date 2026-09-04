namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class BenchmarkScoringProfile
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public bool IsDefault { get; set; }

    public double WeightAccuracy { get; set; } = 0.55;

    public double WeightCompleteness { get; set; } = 0.25;

    public double WeightConciseness { get; set; } = 0.10;

    public double WeightReadability { get; set; } = 0.10;

    public string LevelScoresJson { get; set; } = "[1, 15, 35, 55, 72, 87, 100]";

    public int CriticalErrorCeiling { get; set; } = 25;

    // Quality score below which an answer is re-graded by the run's second-opinion assessor,
    // if one was selected. 0 disables the score trigger; a critical error triggers a re-grade
    // regardless. It lives on the profile rather than in configuration because it changes what
    // a run reports, and the profile is snapshotted into the run — so a report can always say
    // what threshold produced its second verdicts.
    public int SecondOpinionQualityThreshold { get; set; } = 50;

    // How the second-opinion assessor is used: see BenchmarkSecondOpinionMode. Defaults to
    // Flagged because that is the closest reproduction of existing behaviour for every profile
    // that already exists — changing a stored profile's behaviour during a migration would
    // silently alter what the next run does and what it costs. The *recommended* value for a new
    // profile is All, and the profile editor says so, because All is the only mode that measures
    // grader agreement rather than sampling it: under the trigger-based modes the disagreement
    // rate is conditioned on the first assessor's own uncertainty.
    public int SecondOpinionMode { get; set; } = (int)BenchmarkSecondOpinionMode.Flagged;

    /// <summary>
    /// Whether the second-opinion assessor should be blinded to the first assessor's score,
    /// critical-error flag, and comment. Defaults to true.
    /// </summary>
    public bool SecondOpinionBlind { get; set; } = true;

    // Quality points below the run's own median at which an answer is re-graded, in
    // FlaggedAndOutliers mode only. It exists because an absolute threshold cannot see an
    // outlier in an otherwise strong run: on the 2026-09-03 run the median was 96 and the two
    // worst answers scored 60 — the two the synthesis singled out — while the absolute threshold
    // of 50 selected neither.
    //
    // Meaningless outside FlaggedAndOutliers, and validated > 0 there: a zero delta in that mode
    // would select nothing, which contradicts the mode the operator chose.
    public int SecondOpinionOutlierDeltaPoints { get; set; } = 25;

    // Speed constants are calibrated so that the score floor is unreachable within
    // Benchmark:PerQuestionTimeoutSeconds at every difficulty, which is what keeps the metric
    // from saturating on agentic turns that legitimately make many tool calls.
    public int SpeedTargetMs { get; set; } = 15000;

    public double SpeedDecayK { get; set; } = 20.0;

    // Scales the speed target by assessed difficulty:
    //   effectiveTarget = SpeedTargetMs * (1 + SpeedDifficultyScaling * difficulty / 100)
    // Difficulty raises the expected time rather than the aggregate weight; weighting by
    // difficulty would penalise hard questions twice, once for being slow and once for
    // counting more.
    public double SpeedDifficultyScaling { get; set; } = 1.0;

    public int MaxParallelQuestions { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
