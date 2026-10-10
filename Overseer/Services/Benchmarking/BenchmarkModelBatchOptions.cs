namespace Overseer.Services.Benchmarking;

using Microsoft.Extensions.Configuration;

/// <summary>The <c>Benchmark:ModelBatch</c> settings, read live from configuration.</summary>
public sealed record BenchmarkModelBatchOptions
{
    public const string Section = "Benchmark:ModelBatch";

    /// <summary>The most models one batch may hold (MB-B02).</summary>
    public int MaxModels { get; init; } = 12;

    /// <summary>The projected cost above which a batch is large (MB-W09).</summary>
    public decimal WarnCostUsd { get; init; } = 40m;

    /// <summary>The projected wall time, in hours, above which a batch is large (MB-W09).</summary>
    public double WarnWallHours { get; init; } = 12;

    /// <summary>The projected wall time, in hours, above which models run hours apart (MB-T02).</summary>
    public double LongWallHours { get; init; } = 4;

    /// <summary>Minutes without progress before the progress dialog flags a stall (MB-R5).</summary>
    public int StallMinutes { get; init; } = 15;

    public static BenchmarkModelBatchOptions From(IConfiguration? configuration)
    {
        var defaults = new BenchmarkModelBatchOptions();
        if (configuration == null) return defaults;

        return new BenchmarkModelBatchOptions
        {
            MaxModels = configuration.GetValue($"{Section}:MaxModels", defaults.MaxModels),
            WarnCostUsd = configuration.GetValue($"{Section}:WarnCostUsd", defaults.WarnCostUsd),
            WarnWallHours = configuration.GetValue($"{Section}:WarnWallHours", defaults.WarnWallHours),
            LongWallHours = configuration.GetValue($"{Section}:LongWallHours", defaults.LongWallHours),
            StallMinutes = configuration.GetValue($"{Section}:StallMinutes", defaults.StallMinutes)
        };
    }
}
