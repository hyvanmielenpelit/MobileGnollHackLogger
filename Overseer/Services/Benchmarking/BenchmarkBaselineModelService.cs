namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.ChatConsistency;

/// <summary>
/// The GnollBench baseline model: reads and appends <see cref="BenchmarkBaselineModelSetting"/> rows,
/// and finds which comparison entry is the baseline model.
///
/// <para>An entry is the baseline model when its newest run's tested snapshot has the baseline snapshot's
/// model-axis key (<see cref="ChatConsistencyComparability.ModelAxisKey(SystemAiConfigurationSnapshot?)"/>):
/// the nine settings that make two runs different models to Model Comparison. The display name and prices
/// are not among them, so a rename or a price change still matches. The key is computed at read time from
/// the stored snapshot and never persisted.</para>
/// </summary>
public class BenchmarkBaselineModelService
{
    /// <summary>The Benchmark bit of <see cref="SystemAiApiConfiguration.ModelRole"/>.</summary>
    private const int BenchmarkRole = 4;

    private readonly ApplicationDbContext _db;

    public BenchmarkBaselineModelService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>The newest setting row, or null when the baseline model was never set.</summary>
    public Task<BenchmarkBaselineModelSetting?> CurrentSettingAsync(CancellationToken ct = default)
        => _db.BenchmarkBaselineModelSettings
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The baseline model's snapshot, or null when none is set.</summary>
    public async Task<SystemAiConfigurationSnapshot?> CurrentSnapshotAsync(CancellationToken ct = default)
        => (await CurrentSettingAsync(ct))?.ModelSnapshot;

    /// <summary>The baseline model, or null when none is set.</summary>
    public async Task<BenchmarkBaselineModelDto?> GetAsync(CancellationToken ct = default)
        => ToDto(await CurrentSettingAsync(ct));

    /// <summary>The baseline model's model-axis key, or null when none is set.</summary>
    public async Task<string?> AxisKeyAsync(CancellationToken ct = default)
    {
        var snapshot = await CurrentSnapshotAsync(ct);
        return snapshot == null ? null : ChatConsistencyComparability.ModelAxisKey(snapshot);
    }

    /// <summary>
    /// Appends a setting row: the snapshot of the live configuration's current settings, or a cleared row
    /// when <paramref name="configurationId"/> is null. Returns the new baseline model (null when cleared),
    /// or an error sentence for a missing configuration or one without the Benchmark role.
    /// </summary>
    public async Task<(BenchmarkBaselineModelDto? Result, string? Error)> SetAsync(
        long? configurationId, string? userId, CancellationToken ct = default)
    {
        SystemAiConfigurationSnapshot? snapshot = null;

        if (configurationId is long id)
        {
            var configuration = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (configuration == null)
                return (null, $"Model configuration {id} was not found.");
            if ((configuration.ModelRole & BenchmarkRole) != BenchmarkRole)
                return (null, $"{configuration.DisplayName} does not have the Benchmark role, so it cannot be the baseline model.");

            snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(_db, configuration, ct);
        }

        var setting = new BenchmarkBaselineModelSetting
        {
            ModelSnapshotId = snapshot?.Id,
            ModelSnapshot = snapshot,
            SourceConfigurationId = snapshot == null ? null : configurationId,
            SetAtUtc = DateTime.UtcNow,
            SetByUserId = string.IsNullOrEmpty(userId) ? null : userId,
        };
        _db.BenchmarkBaselineModelSettings.Add(setting);
        await _db.SaveChangesAsync(ct);

        return (ToDto(setting), null);
    }

    /// <summary>
    /// Fills the comparison's baseline-model fields. <paramref name="newestRunByEntryKey"/> holds, per entry
    /// key, the run its identity is read from: its newest run.
    /// </summary>
    public async Task ApplyAsync(
        BenchmarkModelComparisonDto result,
        IReadOnlyDictionary<string, BenchmarkRun?> newestRunByEntryKey,
        CancellationToken ct = default)
    {
        var setting = await CurrentSettingAsync(ct);
        Apply(result, setting, newestRunByEntryKey);
    }

    /// <summary>The pure part of <see cref="ApplyAsync"/>.</summary>
    public static void Apply(
        BenchmarkModelComparisonDto result,
        BenchmarkBaselineModelSetting? setting,
        IReadOnlyDictionary<string, BenchmarkRun?> newestRunByEntryKey)
    {
        result.BaselineModel = ToDto(setting);
        string? baselineAxisKey = setting?.ModelSnapshot == null
            ? null
            : ChatConsistencyComparability.ModelAxisKey(setting.ModelSnapshot);

        var entries = result.Entries
            .Where(e => newestRunByEntryKey.TryGetValue(e.Key, out var run) && run != null)
            .Select(e => (Entry: e, AxisKey: ChatConsistencyComparability.ModelAxisKey(newestRunByEntryKey[e.Key]!)))
            .ToList();

        var (entryKey, state) = Resolve(baselineAxisKey, entries);
        result.BaselineModelEntryKey = entryKey;
        result.BaselineModelState = state;
    }

    /// <summary>
    /// Which entry is the baseline model. Among the entries with <paramref name="baselineAxisKey"/>, a
    /// non-excluded one wins, then the newest <see cref="BenchmarkModelComparisonEntryDto.LastRunStartedAtUtc"/>,
    /// then the entry key in ordinal order.
    /// </summary>
    public static (string? EntryKey, string State) Resolve(
        string? baselineAxisKey,
        IReadOnlyList<(BenchmarkModelComparisonEntryDto Entry, string AxisKey)> entries)
    {
        if (baselineAxisKey == null)
            return (null, BenchmarkBaselineModelStates.NotSet);

        var chosen = entries
            .Where(e => string.Equals(e.AxisKey, baselineAxisKey, StringComparison.Ordinal))
            .Select(e => e.Entry)
            .OrderBy(e => e.Excluded)
            .ThenByDescending(e => e.LastRunStartedAtUtc)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (chosen == null)
            return (null, BenchmarkBaselineModelStates.NotInComparison);

        return (chosen.Key, chosen.Excluded ? BenchmarkBaselineModelStates.Excluded : BenchmarkBaselineModelStates.Included);
    }

    /// <summary>
    /// Whether runs launched with <paramref name="configuration"/>'s current settings would be the baseline
    /// model, given the baseline's model-axis key (null when none is set).
    /// </summary>
    public static bool IsBaselineModel(SystemAiApiConfiguration configuration, string? baselineAxisKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (baselineAxisKey == null) return false;

        var snapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(configuration);
        return string.Equals(ChatConsistencyComparability.ModelAxisKey(snapshot), baselineAxisKey, StringComparison.Ordinal);
    }

    /// <summary>The baseline model of a setting row; null for no row or a cleared one.</summary>
    public static BenchmarkBaselineModelDto? ToDto(BenchmarkBaselineModelSetting? setting)
    {
        var snapshot = setting?.ModelSnapshot;
        if (setting == null || snapshot == null) return null;

        return new BenchmarkBaselineModelDto
        {
            SnapshotId = snapshot.Id,
            DisplayName = snapshot.Label() ?? string.Empty,
            Provider = snapshot.Provider,
            ModelId = snapshot.ModelId,
            ThinkingLevel = snapshot.ThinkingLevel,
            ReasoningMode = snapshot.ReasoningMode,
            ServiceTier = snapshot.ServiceTier,
            MaxOutputTokens = snapshot.MaxOutputTokens,
            SourceConfigurationId = setting.SourceConfigurationId,
            SetAtUtc = setting.SetAtUtc,
        };
    }
}
