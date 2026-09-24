namespace Overseer.Services.Benchmarking;

using System;
using MobileGnollHackLogger.Data;

public static class BenchmarkQuestionAssessment
{
    /// <summary>Records which model assessed this question, and with which settings.</summary>
    public static void ApplySnapshot(
        BenchmarkQuestion question, int difficulty, long configId, SystemAiConfigurationSnapshot snapshot, DateTime nowUtc)
    {
        question.AssessedDifficulty = difficulty;
        question.AssessedDifficultyAtUtc = nowUtc;
        question.AssessedDifficultyModelConfigurationId = configId;
        question.AssessedDifficultyModelSnapshot = snapshot;
    }

    /// <summary>
    /// Drops the assessment because the question's content changed, and bumps
    /// <see cref="BenchmarkQuestion.ItemRevision"/> for the same reason: an edited question is a
    /// different item, and its statistics must not straddle the rewrite. The two travel together
    /// because they express one fact.
    /// </summary>
    public static void Clear(BenchmarkQuestion question)
    {
        question.ItemRevision = question.ItemRevision <= 0 ? 2 : question.ItemRevision + 1;
        question.AssessedDifficulty = null;
        question.AssessedDifficultyAtUtc = null;
        question.AssessedDifficultyModelConfigurationId = null;
        question.AssessedDifficultyModelSnapshot = null;
        question.AssessedDifficultyModelSnapshotId = null;
    }

    /// <summary>True when the question counts toward suite completion.</summary>
    public static bool IsAssessed(BenchmarkQuestion question) => question.AssessedDifficulty.HasValue;
}