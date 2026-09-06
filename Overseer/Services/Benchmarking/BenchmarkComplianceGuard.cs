namespace Overseer.Services.Benchmarking;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

/// <summary>
/// A snapshot of the run caps and the rolling-window counts they are measured against.
/// <paramref name="RemainingDailyHeadroom"/> never goes negative.
/// </summary>
public sealed record BenchmarkRunLimits(
    int MaxRunsPerHour,
    int MaxRunsPerDay,
    int RunsInLastHour,
    int RunsInLast24Hours,
    int RemainingDailyHeadroom);

public class BenchmarkComplianceGuard
{
    private const string DefaultPurposeStatement =
        "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack. " +
        "Benchmark outputs are third-party generated content used solely for automated capability evaluation and scoring, " +
        "and are not used for training, fine-tuning, distilling, or developing competing AI models.";

    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;

    public BenchmarkComplianceGuard(IConfiguration configuration, ApplicationDbContext dbContext)
    {
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public int MaxQuestionsPerSuite =>
        _configuration.GetValue<int>("Benchmark:Compliance:MaxQuestionsPerSuite", 50);

    public int MaxRunsPerDay =>
        _configuration.GetValue<int>("Benchmark:Compliance:MaxRunsPerDay", 20);

    public int MaxRunsPerHour =>
        _configuration.GetValue<int>("Benchmark:Compliance:MaxRunsPerHour", 5);

    public string GetPurposeStatement()
    {
        var configured = _configuration["Benchmark:Compliance:PurposeStatement"];
        return !string.IsNullOrWhiteSpace(configured) ? configured : DefaultPurposeStatement;
    }

    /// <summary>
    /// The caps and the live rolling-window counts behind them, as one snapshot.
    ///
    /// <para>This exists so the run-count field and the series orchestrator have something to bound
    /// themselves by without re-deriving the window arithmetic. Both windows are <b>rolling</b> —
    /// the last 60 minutes and the last 24 hours, counted from now — exactly as
    /// <see cref="CanSpendAsync"/> counts them, and not calendar hours or calendar days. A caller
    /// that reimplemented "today" as midnight-to-now would disagree with the guard that actually
    /// refuses the run, which is the failure this method is here to prevent.</para>
    ///
    /// <para><c>RemainingDailyHeadroom</c> is clamped at zero: the daily count can exceed the cap
    /// after the setting is lowered, and a negative headroom would render as a negative maximum on
    /// the run-count field.</para>
    /// </summary>
    public async Task<BenchmarkRunLimits> GetLimitsAsync(ApplicationDbContext? db = null, CancellationToken ct = default)
    {
        var dbContext = db ?? _dbContext;
        var now = DateTime.UtcNow;

        var hourCutoff = now.AddHours(-1);
        int hourlyCount = await dbContext.BenchmarkRuns
            .CountAsync(r => r.StartedAtUtc >= hourCutoff, ct);

        var dayCutoff = now.AddHours(-24);
        int dailyCount = await dbContext.BenchmarkRuns
            .CountAsync(r => r.StartedAtUtc >= dayCutoff, ct);

        return new BenchmarkRunLimits(
            MaxRunsPerHour,
            MaxRunsPerDay,
            hourlyCount,
            dailyCount,
            Math.Max(0, MaxRunsPerDay - dailyCount));
    }

    public async Task<(bool Allowed, string? DenialReason)> CanSpendAsync(ApplicationDbContext? db = null, CancellationToken ct = default)
    {
        var dbContext = db ?? _dbContext;
        var now = DateTime.UtcNow;

        var hourCutoff = now.AddHours(-1);
        int hourlyCount = await dbContext.BenchmarkRuns
            .CountAsync(r => r.StartedAtUtc >= hourCutoff, ct);

        if (hourlyCount >= MaxRunsPerHour)
        {
            return (false, $"Hourly benchmark run cap reached ({MaxRunsPerHour} runs/hour). Please try again later or adjust the cap in configuration.");
        }

        var dayCutoff = now.AddHours(-24);
        int dailyCount = await dbContext.BenchmarkRuns
            .CountAsync(r => r.StartedAtUtc >= dayCutoff, ct);

        if (dailyCount >= MaxRunsPerDay)
        {
            return (false, $"Daily benchmark run cap reached ({MaxRunsPerDay} runs/day). Please try again later or adjust the cap in configuration.");
        }

        return (true, null);
    }

    public async Task<(bool Allowed, string? DenialReason)> CanAddQuestionsAsync(long suiteId, int countToAdd = 1, ApplicationDbContext? db = null, CancellationToken ct = default)
    {
        var dbContext = db ?? _dbContext;
        int currentCount = await dbContext.BenchmarkQuestions
            .CountAsync(q => q.BenchmarkSuiteId == suiteId, ct);

        return CanAddQuestions(currentCount, countToAdd);
    }

    public (bool Allowed, string? DenialReason) CanAddQuestions(int currentCount, int countToAdd = 1)
    {
        if (currentCount + countToAdd > MaxQuestionsPerSuite)
        {
            return (false, $"Suite question limit reached ({MaxQuestionsPerSuite} questions maximum).");
        }

        return (true, null);
    }

    public bool IsSameProvider(string? testedProvider, string? assessorProvider)
    {
        if (string.IsNullOrWhiteSpace(testedProvider) || string.IsNullOrWhiteSpace(assessorProvider))
        {
            return false;
        }

        return string.Equals(testedProvider.Trim(), assessorProvider.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSameProvider(SystemAiApiConfiguration? testedConfig, SystemAiApiConfiguration? assessorConfig)
    {
        if (testedConfig == null || assessorConfig == null)
        {
            return false;
        }

        return IsSameProvider(testedConfig.Provider, assessorConfig.Provider);
    }
}
