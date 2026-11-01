using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

namespace Overseer.Services;

/// <summary>
/// Brings stored model names into line with the model catalog after a rename: saved configurations
/// on one side, and the names recorded by past runs, chats, logs and reports on the other. A dry run
/// writes nothing, and a second real run changes nothing. Verbatim evidence (chat text, answers,
/// rubrics, <c>ContentJson</c>), stored hashes other than a snapshot's own and chart images are never touched.
/// </summary>
public class ModelDisplayNameSyncService
{
    /// <summary>The recorded name columns are unindexed and <c>ChatMessage</c> and the usage logs are large.</summary>
    private static readonly TimeSpan RecordedColumnTimeout = TimeSpan.FromMinutes(10);

    private const int TextBatchSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ModelMetadataService _metadata;
    private readonly SystemConfigUsageGuard _guard;
    private readonly BenchmarkReportDocumentFactsCache _factsCache;

    public ModelDisplayNameSyncService(
        ApplicationDbContext db,
        ModelMetadataService metadata,
        SystemConfigUsageGuard guard,
        BenchmarkReportDocumentFactsCache factsCache)
    {
        _db = db;
        _metadata = metadata;
        _guard = guard;
        _factsCache = factsCache;
    }

    /// <summary>The benchmark work that refuses a real run, because it holds snapshot rows and captures new ones.</summary>
    public Task<List<SystemConfigBlockerDto>> FindBlockersAsync(CancellationToken ct = default)
        => _guard.FindAnyActiveBenchmarkWorkAsync(ct);

    /// <summary>What each sync would change now. Scans the same columns as the sync.</summary>
    public async Task<ModelNameSyncStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var status = new ModelNameSyncStatusDto
        {
            ConfigurationCount = (await LoadConfigurationChangesAsync(ct)).Count,
            UserModelCount = (await LoadUserModelChangesAsync(ct)).Count,
            SnapshotCount = (await PlanSnapshotsAsync(ct)).QualifyingCount,
            Blockers = await FindBlockersAsync(ct),
            ComputedAtUtc = DateTime.UtcNow
        };

        await WithRecordedColumnTimeoutAsync(async () =>
        {
            foreach (var (_, count) in await RewriteRecordedColumnsAsync(isDryRun: true, ct))
                status.RecordedRowCount += count;
            status.UnmatchedNames = await FindUnmatchedNamesAsync(ct);
        });

        var rewriter = await BuildTextRewriterAsync(ct);
        if (rewriter != null)
        {
            foreach (var (_, count) in await RewriteTextColumnsAsync(rewriter, isDryRun: true, ct))
                status.TextRowCount += count;
        }

        return status;
    }

    /// <summary>Updates saved system configurations and user models whose name follows the catalog.</summary>
    public async Task SyncConfigurationsAsync(bool isDryRun, MaintenanceResultDto result, CancellationToken ct = default)
    {
        await CheckBlockersAsync(isDryRun, result, ct);

        string prefix = DryRunPrefix(isDryRun);
        var configurations = await LoadConfigurationChangesAsync(ct);
        foreach (var change in configurations)
        {
            result.Logs.Add($"{prefix}System configuration #{change.Id}: {change.OldName} → {change.NewName}");
            if (!isDryRun)
            {
                await _db.SystemAiApiConfigurations
                    .IgnoreAutoIncludes()
                    .Where(c => c.Id == change.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DisplayName, change.NewName), ct);
            }
        }

        var userModels = await LoadUserModelChangesAsync(ct);
        foreach (var change in userModels)
        {
            result.Logs.Add($"{prefix}User model #{change.Id}: {change.OldName} → {change.NewName}");
            if (!isDryRun)
            {
                await _db.UserAiModels
                    .IgnoreAutoIncludes()
                    .Where(m => m.Id == change.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.DisplayName, change.NewName), ct);
            }
        }

        foreach (string line in await DescribeCustomFormerNamesAsync(ct))
            result.Logs.Add(line);

        result.UpdatedRowCount += configurations.Count + userModels.Count;
        result.Logs.Add($"{prefix}{(isDryRun ? "Would update" : "Updated")} {configurations.Count} system configurations and {userModels.Count} user models.");
    }

    /// <summary>
    /// Replaces former catalog names in configuration snapshots, then in the columns that hold a whole
    /// name, then in the text that contains one.
    /// </summary>
    public async Task SyncRecordsAsync(bool isDryRun, MaintenanceResultDto result, CancellationToken ct = default)
    {
        await CheckBlockersAsync(isDryRun, result, ct);

        string prefix = DryRunPrefix(isDryRun);
        string verb = isDryRun ? "Would update" : "Updated";

        await SyncSnapshotsAsync(isDryRun, result, ct);

        await WithRecordedColumnTimeoutAsync(async () =>
        {
            foreach (var (table, count) in await RewriteRecordedColumnsAsync(isDryRun, ct))
            {
                result.UpdatedRowCount += count;
                result.Logs.Add($"{prefix}{verb} {count} rows of {table}.");
            }
        });

        var rewriter = await BuildTextRewriterAsync(ct);
        if (rewriter != null)
        {
            foreach (string label in rewriter.ProtectedLabels)
                result.Logs.Add($"Left alone, because an owner chose it: {label}");

            foreach (var (table, count) in await RewriteTextColumnsAsync(rewriter, isDryRun, ct))
            {
                result.UpdatedRowCount += count;
                result.Logs.Add($"{prefix}{verb} {count} rows of {table}.");
            }
        }

        List<string> unmatched = [];
        await WithRecordedColumnTimeoutAsync(async () => unmatched = await FindUnmatchedNamesAsync(ct));
        if (unmatched.Count > 0)
            result.Logs.Add($"Recorded names that match no catalog entry, left alone: {string.Join(", ", unmatched)}");

        result.Logs.Add($"{prefix}{verb} {result.UpdatedRowCount} rows and {(isDryRun ? "would merge" : "merged")} {result.MergedSnapshotCount} snapshot rows.");
    }

    private static string DryRunPrefix(bool isDryRun) => isDryRun ? "[DRY RUN] " : "";

    /// <summary>Refuses a real run while benchmark work is running; a dry run only reports it.</summary>
    private async Task CheckBlockersAsync(bool isDryRun, MaintenanceResultDto result, CancellationToken ct)
    {
        var blockers = await FindBlockersAsync(ct);
        if (blockers.Count == 0)
            return;

        string labels = string.Join("; ", blockers.Select(b => b.Label));
        if (!isDryRun)
            throw new InvalidOperationException($"Model names cannot be synced while benchmark work is running: {labels}");

        result.Logs.Add($"A real run would be refused while this work is running: {labels}");
    }

    // --- Configurations ---

    private sealed record NameChange(long Id, string? OldName, string NewName);

    /// <summary>The catalog name a saved row should take, or null when the row keeps its name.</summary>
    private string? ConfigurationTarget(string provider, string modelId, string? storedName, string? mode)
    {
        var meta = _metadata.GetMetadata(provider, modelId);
        if (string.IsNullOrEmpty(meta.DisplayName))
            return null;

        return DisplayNameModes.Normalize(mode) switch
        {
            DisplayNameModes.ModelName => storedName != meta.DisplayName ? meta.DisplayName : null,
            null => storedName != null && meta.FormerDisplayNames.Contains(storedName) ? meta.DisplayName : null,
            _ => null
        };
    }

    private async Task<List<NameChange>> LoadConfigurationChangesAsync(CancellationToken ct)
    {
        var rows = await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Select(c => new { c.Id, c.Provider, c.ModelId, c.DisplayName, c.DisplayNameMode })
            .ToListAsync(ct);

        return rows
            .Select(r => (r, Target: ConfigurationTarget(r.Provider, r.ModelId, r.DisplayName, r.DisplayNameMode)))
            .Where(x => x.Target != null)
            .Select(x => new NameChange(x.r.Id, x.r.DisplayName, x.Target!))
            .ToList();
    }

    private async Task<List<NameChange>> LoadUserModelChangesAsync(CancellationToken ct)
    {
        var rows = await _db.UserAiModels
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Select(m => new { m.Id, m.Provider, m.ModelId, m.DisplayName, m.DisplayNameMode })
            .ToListAsync(ct);

        return rows
            .Select(r => (r, Target: ConfigurationTarget(r.Provider, r.ModelId, r.DisplayName, r.DisplayNameMode)))
            .Where(x => x.Target != null)
            .Select(x => new NameChange(x.r.Id, x.r.DisplayName, x.Target!))
            .ToList();
    }

    /// <summary>A log line for each custom-mode row that still holds a former catalog name, which is never changed.</summary>
    private async Task<List<string>> DescribeCustomFormerNamesAsync(CancellationToken ct)
    {
        var configurations = await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(c => c.DisplayNameMode == DisplayNameModes.Custom)
            .Select(c => new { c.Id, c.Provider, c.ModelId, c.DisplayName })
            .ToListAsync(ct);
        var userModels = await _db.UserAiModels
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(m => m.DisplayNameMode == DisplayNameModes.Custom)
            .Select(m => new { m.Id, m.Provider, m.ModelId, m.DisplayName })
            .ToListAsync(ct);

        bool HoldsFormerName(string provider, string modelId, string? name)
            => name != null && _metadata.GetMetadata(provider, modelId).FormerDisplayNames.Contains(name);

        return configurations
            .Where(c => HoldsFormerName(c.Provider, c.ModelId, c.DisplayName))
            .Select(c => $"System configuration #{c.Id} has the custom name {c.DisplayName}, a former catalog name. Left alone.")
            .Concat(userModels
                .Where(m => HoldsFormerName(m.Provider, m.ModelId, m.DisplayName))
                .Select(m => $"User model #{m.Id} has the custom name {m.DisplayName}, a former catalog name. Left alone."))
            .ToList();
    }

    // --- Part A: configuration snapshots ---

    private sealed record SnapshotGroup(long KeptId, string Name, string Sha256, bool KeptNeedsRename, List<long> RemovedIds);

    private sealed record SnapshotPlan(int QualifyingCount, List<SnapshotGroup> Groups, List<string> Descriptions);

    /// <summary>
    /// Finds the snapshots holding a former name of their own model, and groups them with any stored row
    /// that already holds the hash they will take. The lowest id of a group stays, because it is the one
    /// older JSON is most likely to name.
    /// </summary>
    private async Task<SnapshotPlan> PlanSnapshotsAsync(CancellationToken ct)
    {
        var rows = await _db.SystemAiConfigurationSnapshots.AsNoTracking().ToListAsync(ct);
        var idByHash = rows.ToDictionary(r => r.Sha256, r => r.Id, StringComparer.Ordinal);

        var qualifying = new List<(long Id, string? OldName, string NewName, string NewHash)>();
        foreach (var row in rows)
        {
            if (row.DisplayName == null)
                continue;

            var meta = _metadata.GetMetadata(row.Provider, row.ModelId);
            if (!meta.FormerDisplayNames.Contains(row.DisplayName))
                continue;

            string oldName = row.DisplayName;
            row.DisplayName = meta.DisplayName;
            qualifying.Add((row.Id, oldName, meta.DisplayName, SystemAiConfigurationSnapshotStore.ComputeSha256(row)));
        }

        var groups = new List<SnapshotGroup>();
        var descriptions = new List<string>();
        foreach (var group in qualifying.GroupBy(q => q.NewHash, StringComparer.Ordinal))
        {
            var ids = group.Select(q => q.Id).ToList();
            bool targetStored = idByHash.TryGetValue(group.Key, out long storedId);
            if (targetStored)
                ids.Add(storedId);

            long keptId = ids.Min();
            var removed = ids.Where(id => id != keptId).OrderBy(id => id).ToList();
            bool keptNeedsRename = group.Any(q => q.Id == keptId);
            string newName = group.First().NewName;
            groups.Add(new SnapshotGroup(keptId, newName, group.Key, keptNeedsRename, removed));

            if (keptNeedsRename)
                descriptions.Add($"Snapshot #{keptId}: {group.First(q => q.Id == keptId).OldName} → {newName}");
            foreach (long removedId in removed)
                descriptions.Add($"Snapshot #{removedId} merges into #{keptId} ({newName})");
        }

        return new SnapshotPlan(qualifying.Count, groups, descriptions);
    }

    private async Task SyncSnapshotsAsync(bool isDryRun, MaintenanceResultDto result, CancellationToken ct)
    {
        var plan = await PlanSnapshotsAsync(ct);
        string prefix = DryRunPrefix(isDryRun);

        foreach (string description in plan.Descriptions)
            result.Logs.Add(prefix + description);

        int renamed = plan.Groups.Count(g => g.KeptNeedsRename);
        int merged = plan.Groups.Sum(g => g.RemovedIds.Count);

        if (!isDryRun && plan.Groups.Count > 0)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            // The unique Sha256 index must never see two rows with one hash, so deletes come first.
            foreach (var group in plan.Groups)
            {
                foreach (long removedId in group.RemovedIds)
                {
                    await RepointSnapshotReferencesAsync(removedId, group.KeptId, ct);
                    await _db.SystemAiConfigurationSnapshots
                        .Where(s => s.Id == removedId)
                        .ExecuteDeleteAsync(ct);
                }
            }

            foreach (var group in plan.Groups.Where(g => g.KeptNeedsRename))
            {
                await _db.SystemAiConfigurationSnapshots
                    .Where(s => s.Id == group.KeptId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.DisplayName, group.Name)
                        .SetProperty(x => x.Sha256, group.Sha256), ct);
            }

            await transaction.CommitAsync(ct);
        }

        result.UpdatedRowCount += renamed;
        result.MergedSnapshotCount += merged;
        result.Logs.Add($"{prefix}{(isDryRun ? "Would rename" : "Renamed")} {renamed} and {(isDryRun ? "would merge" : "merged")} {merged} configuration snapshot rows.");
    }

    /// <summary>
    /// Moves every reference from one snapshot row to another: each foreign key in the model, so a key
    /// added later cannot be missed, and <c>ChatConsistencyAnalysis.CommonGraderSnapshotId</c>, a plain id.
    /// </summary>
    private async Task RepointSnapshotReferencesAsync(long removedId, long keptId, CancellationToken ct)
    {
        var foreignKeys = _db.Model.FindEntityType(typeof(SystemAiConfigurationSnapshot))!.GetReferencingForeignKeys();
        foreach (var foreignKey in foreignKeys)
        {
            var entity = foreignKey.DeclaringEntityType;
            string table = entity.GetTableName()!;
            string? schema = entity.GetSchema();
            string column = foreignKey.Properties[0].GetColumnName(StoreObjectIdentifier.Table(table, schema))!;
            string qualifiedTable = schema == null ? $"[{table}]" : $"[{schema}].[{table}]";

            // The table and column names come from the EF model; only the ids are values.
            string sql = $"UPDATE {qualifiedTable} SET [{column}] = {{0}} WHERE [{column}] = {{1}}";
            await _db.Database.ExecuteSqlRawAsync(sql, new object[] { keptId, removedId }, ct);
        }

        await _db.ChatConsistencyAnalyses
            .Where(a => a.CommonGraderSnapshotId == removedId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CommonGraderSnapshotId, keptId), ct);
    }

    // --- Part B: columns that hold a whole name ---

    private async Task WithRecordedColumnTimeoutAsync(Func<Task> action)
    {
        var previous = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(RecordedColumnTimeout);
        try
        {
            await action();
        }
        finally
        {
            _db.Database.SetCommandTimeout(previous);
        }
    }

    private async Task<List<(string Table, int Count)>> RewriteRecordedColumnsAsync(bool isDryRun, CancellationToken ct)
    {
        var renames = _metadata.GetDisplayNameRenames();
        if (renames.Count == 0)
            return new List<(string, int)>();

        return new List<(string, int)>
        {
            ("ChatMessage.ModelDisplayNameUsed", await RewriteRecordedColumnAsync<ChatMessage>(nameof(ChatMessage.ModelDisplayNameUsed), renames, isDryRun, ct)),
            ("SystemAiUsageLog.ModelDisplayName", await RewriteRecordedColumnAsync<SystemAiUsageLog>(nameof(SystemAiUsageLog.ModelDisplayName), renames, isDryRun, ct)),
            ("UserKeyAiUsageLog.ModelDisplayName", await RewriteRecordedColumnAsync<UserKeyAiUsageLog>(nameof(UserKeyAiUsageLog.ModelDisplayName), renames, isDryRun, ct)),
            ("SystemAiErrorLog.ModelDisplayName", await RewriteRecordedColumnAsync<SystemAiErrorLog>(nameof(SystemAiErrorLog.ModelDisplayName), renames, isDryRun, ct)),
            ("ChatTurnLimitEvent.ModelDisplayName", await RewriteRecordedColumnAsync<ChatTurnLimitEvent>(nameof(ChatTurnLimitEvent.ModelDisplayName), renames, isDryRun, ct)),
            ("BenchmarkReportDocument.WriterDisplayName", await RewriteRecordedColumnAsync<BenchmarkReportDocument>(nameof(BenchmarkReportDocument.WriterDisplayName), renames, isDryRun, ct))
        };
    }

    /// <summary>One statement per column with a CASE over the rename pairs, so a large table is read once.</summary>
    private async Task<int> RewriteRecordedColumnAsync<TEntity>(
        string column, IReadOnlyList<(string Former, string Current)> renames, bool isDryRun, CancellationToken ct)
        where TEntity : class
    {
        var formerNames = renames.Select(r => r.Former).ToList();
        var holding = _db.Set<TEntity>()
            .IgnoreAutoIncludes()
            .Where(e => formerNames.Contains(EF.Property<string>(e, column)));

        if (isDryRun)
            return await holding.CountAsync(ct);

        var entity = Expression.Parameter(typeof(TEntity), "e");
        var stored = Expression.Property(entity, column);
        Expression value = stored;
        foreach (var (former, current) in renames)
            value = Expression.Condition(Expression.Equal(stored, Expression.Constant(former)), Expression.Constant(current), value);

        var propertyLambda = Expression.Lambda<Func<TEntity, string>>(stored, entity);
        var valueLambda = Expression.Lambda<Func<TEntity, string>>(value, entity);
        return await holding.ExecuteUpdateAsync(s => s.SetProperty(propertyLambda, valueLambda), ct);
    }

    // --- Part C: text that contains a name ---

    /// <summary>Replaces former names in stored text, and leaves a protected label that contains one whole.</summary>
    private sealed class TextRewriter
    {
        private readonly Regex _pattern;
        private readonly Dictionary<string, string> _renames;

        public IReadOnlyList<string> ProtectedLabels { get; }

        /// <summary>The SQL LIKE patterns a text holding a former name has to match.</summary>
        public IReadOnlyList<string> LikePatterns { get; }

        public TextRewriter(IReadOnlyList<(string Former, string Current)> renames, IReadOnlyList<string> protectedLabels)
        {
            _renames = renames.ToDictionary(r => r.Former, r => r.Current, StringComparer.Ordinal);
            ProtectedLabels = protectedLabels;

            LikePatterns = renames
                .Select(r => r.Former.Split(' ')[0])
                .Distinct(StringComparer.Ordinal)
                .Select(word => $"%{word} %")
                .ToList();

            // A protected label also appears JSON-encoded in the JSON columns; the names themselves hold nothing JSON escapes.
            var alternatives = _renames.Keys
                .Concat(protectedLabels)
                .Concat(protectedLabels.Select(label => JsonEncodedText.Encode(label).ToString()))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(a => a.Length)
                .Select(Regex.Escape);

            // In the JSON columns a name can follow an escape such as \n or ", whose last character is a word character.
            _pattern = new Regex(
                @"(?:(?<![\w.])|(?<=\\[nrtbf])|(?<=\\u[0-9a-fA-F]{4}))(?:" + string.Join("|", alternatives) + @")(?!\w|\.\d)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public string? Rewrite(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return _pattern.Replace(text, m => _renames.TryGetValue(m.Value, out var current) ? current : m.Value);
        }
    }

    /// <summary>Null when the catalogs hold no former name.</summary>
    private async Task<TextRewriter?> BuildTextRewriterAsync(CancellationToken ct)
    {
        var renames = _metadata.GetDisplayNameRenames();
        if (renames.Count == 0)
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith(await _db.SystemAiConfigurationSnapshots.AsNoTracking().Where(s => s.DisplayName != null).Select(s => s.DisplayName!).Distinct().ToListAsync(ct));
        names.UnionWith(await _db.SystemAiApiConfigurations.AsNoTracking().IgnoreAutoIncludes().Where(c => c.DisplayName != null).Select(c => c.DisplayName!).Distinct().ToListAsync(ct));
        names.UnionWith(await _db.UserAiModels.AsNoTracking().IgnoreAutoIncludes().Where(m => m.DisplayName != null).Select(m => m.DisplayName!).Distinct().ToListAsync(ct));

        var formerNames = renames.Select(r => r.Former).ToList();
        var protectedLabels = names
            .Where(name => !formerNames.Contains(name) && formerNames.Any(former => name.Contains(former, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new TextRewriter(renames, protectedLabels);
    }

    private async Task<List<(string Table, int Count)>> RewriteTextColumnsAsync(TextRewriter rewriter, bool isDryRun, CancellationToken ct)
    {
        // A reader of a stored document's facts holds them in the cache, so a rewritten document leaves it.
        var documents = await RewriteTextAsync<BenchmarkReportDocument>(rewriter, isDryRun, ct, _factsCache.Remove,
            nameof(BenchmarkReportDocument.SubjectLabel), nameof(BenchmarkReportDocument.Title),
            nameof(BenchmarkReportDocument.FactsJson), nameof(BenchmarkReportDocument.WriterOutputJson));

        return new List<(string, int)>
        {
            ("BenchmarkReportDocument", documents),
            ("BenchmarkGroupAnalysis", await RewriteTextAsync<BenchmarkGroupAnalysis>(rewriter, isDryRun, ct, null,
                nameof(BenchmarkGroupAnalysis.ResultJson), nameof(BenchmarkGroupAnalysis.ComparisonJson))),
            ("BenchmarkBatteryAnalysis", await RewriteTextAsync<BenchmarkBatteryAnalysis>(rewriter, isDryRun, ct, null,
                nameof(BenchmarkBatteryAnalysis.ResultJson), nameof(BenchmarkBatteryAnalysis.ComparisonJson))),
            ("ChatConsistencyAnalysis", await RewriteTextAsync<ChatConsistencyAnalysis>(rewriter, isDryRun, ct, null,
                nameof(ChatConsistencyAnalysis.Name), nameof(ChatConsistencyAnalysis.ResultJson))),
            ("BenchmarkModelBatchMember", await RewriteTextAsync<BenchmarkModelBatchMember>(rewriter, isDryRun, ct, null,
                nameof(BenchmarkModelBatchMember.TestedModelSnapshotJson))),
            ("BenchmarkRunGroup", await RewriteTextAsync<BenchmarkRunGroup>(rewriter, isDryRun, ct, null,
                nameof(BenchmarkRunGroup.Name))),
            ("BenchmarkComparison", await RewriteTextAsync<BenchmarkComparison>(rewriter, isDryRun, ct, null,
                nameof(BenchmarkComparison.DefaultName), nameof(BenchmarkComparison.Name)))
        };
    }

    private sealed record TextRow(long Id, string?[] Values);

    /// <summary>
    /// Rewrites the columns of one table in batches by ascending id, reading only those columns. Returns
    /// the rows whose text changes or, on a dry run, would change.
    /// </summary>
    private async Task<int> RewriteTextAsync<TEntity>(
        TextRewriter rewriter, bool isDryRun, CancellationToken ct, Action<long>? onChanged, params string[] columns)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var idProperty = Expression.Property(entity, "Id");
        var idAsLong = Expression.Convert(idProperty, typeof(long));
        var columnProperties = columns.Select(c => Expression.Property(entity, c)).ToList();

        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like), new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;
        var mayHoldName = columnProperties
            .SelectMany(property => rewriter.LikePatterns.Select(pattern =>
                (Expression)Expression.Call(likeMethod, Expression.Constant(EF.Functions), property, Expression.Constant(pattern))))
            .Aggregate(Expression.OrElse);

        var orderById = Expression.Lambda<Func<TEntity, long>>(idAsLong, entity);
        var project = Expression.Lambda<Func<TEntity, TextRow>>(
            Expression.New(
                typeof(TextRow).GetConstructor(new[] { typeof(long), typeof(string?[]) })!,
                idAsLong,
                Expression.NewArrayInit(typeof(string), columnProperties)),
            entity);

        int changed = 0;
        long lastId = 0;
        while (true)
        {
            var afterLast = Expression.GreaterThan(idProperty, IdConstant(lastId, idProperty.Type));
            var filter = Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(afterLast, mayHoldName), entity);

            var batch = await _db.Set<TEntity>()
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(filter)
                .OrderBy(orderById)
                .Take(TextBatchSize)
                .Select(project)
                .ToListAsync(ct);

            if (batch.Count == 0)
                return changed;

            foreach (var row in batch)
            {
                lastId = row.Id;

                var updates = new List<(string Column, string? Value)>();
                for (int i = 0; i < columns.Length; i++)
                {
                    string? rewritten = rewriter.Rewrite(row.Values[i]);
                    if (!string.Equals(rewritten, row.Values[i], StringComparison.Ordinal))
                        updates.Add((columns[i], rewritten));
                }

                if (updates.Count == 0)
                    continue;

                changed++;
                if (isDryRun)
                    continue;

                var isRow = Expression.Lambda<Func<TEntity, bool>>(
                    Expression.Equal(idProperty, IdConstant(row.Id, idProperty.Type)), entity);
                await _db.Set<TEntity>()
                    .IgnoreAutoIncludes()
                    .Where(isRow)
                    .ExecuteUpdateAsync(s =>
                    {
                        foreach (var (column, value) in updates)
                            s.SetProperty(e => EF.Property<string?>(e, column), value);
                    }, ct);
                onChanged?.Invoke(row.Id);
            }
        }
    }

    private static ConstantExpression IdConstant(long id, Type idType)
        => Expression.Constant(Convert.ChangeType(id, idType, CultureInfo.InvariantCulture), idType);

    // --- Reporting ---

    /// <summary>
    /// The distinct recorded names that carry the family word of a catalog name, such as Claude, but are
    /// neither a current nor a former catalog name: a model that left the catalog, or an owner's own label.
    /// </summary>
    private async Task<List<string>> FindUnmatchedNamesAsync(CancellationToken ct)
    {
        var known = new HashSet<string>(_metadata.GetCatalogDisplayNames(), StringComparer.Ordinal);
        known.UnionWith(_metadata.GetDisplayNameRenames().Select(r => r.Former));

        var families = known
            .Select(name => Regex.Match(name, "^[A-Za-z]+").Value)
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var recorded = new HashSet<string>(StringComparer.Ordinal);
        recorded.UnionWith(await _db.SystemAiConfigurationSnapshots.AsNoTracking().Where(s => s.DisplayName != null).Select(s => s.DisplayName!).Distinct().ToListAsync(ct));
        recorded.UnionWith(await DistinctValuesAsync<ChatMessage>(nameof(ChatMessage.ModelDisplayNameUsed), ct));
        recorded.UnionWith(await DistinctValuesAsync<SystemAiUsageLog>(nameof(SystemAiUsageLog.ModelDisplayName), ct));
        recorded.UnionWith(await DistinctValuesAsync<UserKeyAiUsageLog>(nameof(UserKeyAiUsageLog.ModelDisplayName), ct));
        recorded.UnionWith(await DistinctValuesAsync<SystemAiErrorLog>(nameof(SystemAiErrorLog.ModelDisplayName), ct));
        recorded.UnionWith(await DistinctValuesAsync<ChatTurnLimitEvent>(nameof(ChatTurnLimitEvent.ModelDisplayName), ct));
        recorded.UnionWith(await DistinctValuesAsync<BenchmarkReportDocument>(nameof(BenchmarkReportDocument.WriterDisplayName), ct));

        return recorded
            .Where(name => !known.Contains(name) && families.Any(family => name.Contains(family, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private Task<List<string>> DistinctValuesAsync<TEntity>(string column, CancellationToken ct) where TEntity : class
        => _db.Set<TEntity>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(e => EF.Property<string?>(e, column) != null)
            .Select(e => EF.Property<string>(e, column))
            .Distinct()
            .ToListAsync(ct);
}
