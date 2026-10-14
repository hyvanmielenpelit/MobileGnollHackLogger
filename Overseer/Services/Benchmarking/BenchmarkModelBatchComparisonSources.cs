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
/// The sources a model batch's Open in Model Comparison starts from: each finished member's battery
/// run, analysis group or runs, at most <see cref="MaxSources"/>, and the baseline model's latest
/// comparable result when the batch does not hold the baseline model. Read-only.
/// </summary>
public class BenchmarkModelBatchComparisonSources
{
    /// <summary>The most sources Model Comparison takes.</summary>
    public const int MaxSources = 24;

    public const string NoBaselineModelReason = "No baseline model is set.";
    public const string AlreadyInBatchReason = "The baseline model is already in the batch.";
    public const string NoneComparableReason = "No earlier result of the baseline model is comparable with this batch.";
    public const string ComparisonFullReason = "The comparison already holds the most sources it can take, so the baseline model's result was not added.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkBaselineModelService _baselineModel;
    private readonly BenchmarkBaselineModelSourceFinder _finder;

    public BenchmarkModelBatchComparisonSources(
        ApplicationDbContext db,
        BenchmarkBaselineModelService? baselineModel = null,
        BenchmarkBaselineModelSourceFinder? finder = null)
    {
        _db = db;
        _baselineModel = baselineModel ?? new BenchmarkBaselineModelService(db);
        _finder = finder ?? new BenchmarkBaselineModelSourceFinder(db);
    }

    /// <summary>A member whose result a comparison can use.</summary>
    public static bool IsComparable(BenchmarkModelBatchMember member)
        => member.Status is BenchmarkModelBatchMemberStatus.Completed or BenchmarkModelBatchMemberStatus.CompletedWithErrors;

    /// <summary>The batch's comparison sources; null for an unknown batch.</summary>
    public async Task<BenchmarkModelBatchComparisonSourcesDto?> BuildAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await _db.BenchmarkModelBatchRuns
            .AsNoTracking()
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch == null) return null;

        var members = batch.Members.OrderBy(m => m.OrderIndex).Where(IsComparable).ToList();
        bool battery = batch.TargetKind == BenchmarkModelBatchTargetKind.Battery;
        var result = new BenchmarkModelBatchComparisonSourcesDto();

        if (battery)
        {
            result.BatteryRunIds = members
                .Where(m => m.BenchmarkBatteryRunId.HasValue)
                .Select(m => m.BenchmarkBatteryRunId!.Value)
                .Take(MaxSources)
                .ToList();
        }
        else
        {
            var seriesIds = members
                .Where(m => m.BenchmarkRunSeriesId.HasValue)
                .Select(m => m.BenchmarkRunSeriesId!.Value)
                .Distinct()
                .ToList();
            var groupBySeries = seriesIds.Count == 0
                ? new Dictionary<long, long?>()
                : await _db.BenchmarkRunSeries
                    .AsNoTracking()
                    .Where(s => seriesIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.AutoCreatedGroupId })
                    .ToDictionaryAsync(s => s.Id, s => s.AutoCreatedGroupId, ct);

            var runIds = new List<long>();
            var groupIds = new List<long>();
            foreach (var member in members)
            {
                long? groupId = member.BenchmarkRunSeriesId is long seriesId && groupBySeries.TryGetValue(seriesId, out var g) ? g : null;
                if (groupId.HasValue)
                {
                    groupIds.Add(groupId.Value);
                }
                else if (member.BenchmarkRunId.HasValue)
                {
                    runIds.Add(member.BenchmarkRunId.Value);
                }
                else
                {
                    // A series without its group: its runs, so the comparison still sees the model.
                    var runs = await BenchmarkModelBatchOrchestrator.MemberRunsAsync(_db, member, completedOnly: false, ct);
                    runIds.AddRange(runs.Select(r => r.Id));
                }
            }

            result.RunIds = runIds.Take(MaxSources).ToList();
            result.GroupIds = groupIds.Take(Math.Max(0, MaxSources - result.RunIds.Count)).ToList();
        }

        var setting = await _baselineModel.CurrentSettingAsync(ct);
        if (setting?.ModelSnapshot == null)
        {
            result.BaselineModelReason = NoBaselineModelReason;
            return result;
        }

        string baselineAxisKey = ChatConsistencyComparability.ModelAxisKey(setting.ModelSnapshot);
        var memberRuns = new List<IReadOnlyList<BenchmarkRun>>();
        foreach (var member in members)
        {
            memberRuns.Add(await BenchmarkModelBatchOrchestrator.MemberRunsAsync(_db, member, completedOnly: true, ct));
        }

        if (memberRuns.SelectMany(runs => runs).Any(r =>
                string.Equals(ChatConsistencyComparability.ModelAxisKey(r), baselineAxisKey, StringComparison.Ordinal)))
        {
            result.BaselineModelReason = AlreadyInBatchReason;
            return result;
        }

        if (result.RunIds.Count + result.GroupIds.Count + result.BatteryRunIds.Count >= MaxSources)
        {
            result.BaselineModelReason = ComparisonFullReason;
            return result;
        }

        BenchmarkBaselineModelSourceDto? source;
        if (battery)
        {
            var memberBatteryRunIds = members
                .Where(m => m.BenchmarkBatteryRunId.HasValue)
                .Select(m => m.BenchmarkBatteryRunId!.Value)
                .ToList();
            source = await _finder.FindBatteryRunAsync(baselineAxisKey, memberBatteryRunIds, ct);
        }
        else if (batch.BenchmarkSuiteId is long suiteId)
        {
            var ownRunIds = new List<long>();
            foreach (var member in batch.Members)
            {
                ownRunIds.AddRange((await BenchmarkModelBatchOrchestrator.MemberRunsAsync(_db, member, completedOnly: false, ct)).Select(r => r.Id));
            }
            source = await _finder.FindSuiteRunAsync(baselineAxisKey, suiteId, memberRuns, ownRunIds, ct);
        }
        else
        {
            source = null;
        }

        if (source == null)
        {
            result.BaselineModelReason = NoneComparableReason;
            return result;
        }

        if (source.BatteryRunId is long batteryRunId) result.BatteryRunIds.Add(batteryRunId);
        if (source.RunId is long runId) result.RunIds.Add(runId);
        result.BaselineModelSource = source;
        return result;
    }
}
