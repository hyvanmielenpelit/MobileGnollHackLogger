namespace Overseer.Services.Benchmarking;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

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
