using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The two model name sync actions against the real embedded catalogs, on SQLite, because the EF Core
/// in-memory provider does not support <c>ExecuteUpdateAsync</c>.
/// </summary>
public sealed class ModelDisplayNameSyncServiceTests : IDisposable
{
    private const string FormerName = "Claude 5.5 Opus";
    private const string CurrentName = "Claude Opus 5.5";
    private const string ModelId = "claude-opus-5-5";

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly BenchmarkDifficultyJobManager _difficulty = new();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly BenchmarkReportDocumentFactsCache _factsCache;
    private readonly ModelDisplayNameSyncService _service;

    public ModelDisplayNameSyncServiceTests()
    {
        _connection = SqliteSchemaTemplate.OpenCopy();
        _connection.CreateFunction("getutcdate", () => DateTime.UtcNow);
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _factsCache = new BenchmarkReportDocumentFactsCache(_memoryCache);
        _service = new ModelDisplayNameSyncService(
            _db,
            new ModelMetadataService(),
            new SystemConfigUsageGuard(_db, _difficulty, new BenchmarkGenerationJobManager(), new BenchmarkRubricCheckJobManager(),
                new BenchmarkRubricGapAuthorJobManager(), new BenchmarkReportPackJobManager()),
            _factsCache);
    }

    public void Dispose()
    {
        _db.Dispose();
        _memoryCache.Dispose();
        _connection.Dispose();
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MaintenanceResultDto Result(bool isDryRun) => new() { IsDryRun = isDryRun };

    private async Task<SystemAiApiConfiguration> AddConfigurationAsync(
        string displayName, string? mode, string provider = "Anthropic", string modelId = ModelId)
    {
        var config = new SystemAiApiConfiguration
        {
            DisplayName = displayName,
            DisplayNameMode = mode,
            Provider = provider,
            ModelId = modelId,
            EncryptedApiKey = "x",
            ApiKeyNonce = "x",
            ApiKeyTag = "x"
        };
        _db.SystemAiApiConfigurations.Add(config);
        await _db.SaveChangesAsync(Ct);
        return config;
    }

    private async Task<string?> ConfigurationNameAsync(long id)
        => await _db.SystemAiApiConfigurations.AsNoTracking().Where(c => c.Id == id).Select(c => c.DisplayName).SingleAsync(Ct);

    private async Task<SystemAiConfigurationSnapshot> AddSnapshotAsync(
        string? displayName, string provider = "Anthropic", string modelId = ModelId, string? thinkingLevel = null)
    {
        var snapshot = BenchmarkModelSnapshots.Model(provider, modelId, displayName, thinkingLevel);
        _db.SystemAiConfigurationSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(Ct);
        return snapshot;
    }

    private async Task<SystemAiConfigurationSnapshot> SnapshotAsync(long id)
        => await _db.SystemAiConfigurationSnapshots.AsNoTracking().SingleAsync(s => s.Id == id, Ct);

    private async Task<BenchmarkReportDocument> AddDocumentAsync(
        string subjectLabel = "Subject", string title = "Title", string facts = "{}", string content = "{}", string writerOutput = "{}", string writer = "Writer")
    {
        var document = new BenchmarkReportDocument
        {
            PackId = Guid.NewGuid(),
            Audience = BenchmarkReportAudience.ExecutiveSummary,
            SubjectKey = "run:1",
            SubjectLabel = subjectLabel,
            SubjectRunIdsJson = "[1]",
            ComparisonRequestJson = "{}",
            SuiteName = "Suite",
            WriterDisplayName = writer,
            WriterProvider = "Anthropic",
            WriterModelId = ModelId,
            WriterPromptSha256 = new string('a', 64),
            FactsJson = facts,
            ContentJson = content,
            WriterOutputJson = writerOutput,
            ValidationNotesJson = "[]",
            Title = title,
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.BenchmarkReportDocuments.Add(document);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return document;
    }

    private async Task<BenchmarkReportDocument> DocumentAsync(long id)
        => await _db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes().SingleAsync(d => d.Id == id, Ct);

    private async Task<long> AddErrorLogAsync(string? name)
    {
        var log = new SystemAiErrorLog { SystemAiApiConfigurationId = 1, ModelDisplayName = name, TimestampUtc = DateTime.UtcNow };
        _db.SystemAiErrorLogs.Add(log);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return log.Id;
    }

    private async Task<string?> ErrorLogNameAsync(long id)
        => await _db.SystemAiErrorLogs.AsNoTracking().Where(l => l.Id == id).Select(l => l.ModelDisplayName).SingleAsync(Ct);

    // --- Configurations ---

    [Theory]
    [InlineData(FormerName, DisplayNameModes.ModelName, CurrentName)]
    [InlineData("Anything else", DisplayNameModes.ModelName, CurrentName)]
    [InlineData(CurrentName, DisplayNameModes.ModelName, CurrentName)]
    [InlineData(FormerName, null, CurrentName)]
    [InlineData("My own label", null, "My own label")]
    [InlineData(FormerName, "not-a-mode", CurrentName)]
    [InlineData(FormerName, DisplayNameModes.ModelId, FormerName)]
    [InlineData(FormerName, DisplayNameModes.Custom, FormerName)]
    public async Task SyncConfigurations_FollowsTheModeTable(string stored, string? mode, string expected)
    {
        var config = await AddConfigurationAsync(stored, mode);

        await _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal(expected, await ConfigurationNameAsync(config.Id));
    }

    [Fact]
    public async Task SyncConfigurations_LeavesARowTheCatalogDoesNotDescribe()
    {
        var config = await AddConfigurationAsync(FormerName, DisplayNameModes.ModelName, modelId: "claude-unknown");

        await _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal(FormerName, await ConfigurationNameAsync(config.Id));
    }

    [Fact]
    public async Task SyncConfigurations_UpdatesUserModels_AndChangesNoOtherField()
    {
        var user = new ApplicationUser { UserName = "u", NormalizedUserName = "U" };
        _db.Users.Add(user);
        var model = new UserAiModel
        {
            AspNetUserId = user.Id, Provider = "Anthropic", ModelId = ModelId, DisplayName = FormerName,
            DisplayNameMode = DisplayNameModes.ModelName, ThinkingLevel = "high"
        };
        _db.UserAiModels.Add(model);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        var result = Result(false);
        await _service.SyncConfigurationsAsync(isDryRun: false, result, Ct);

        var stored = await _db.UserAiModels.AsNoTracking().SingleAsync(m => m.Id == model.Id, Ct);
        Assert.Equal(CurrentName, stored.DisplayName);
        Assert.Equal("high", stored.ThinkingLevel);
        Assert.Equal(1, result.UpdatedRowCount);
        Assert.Contains(result.Logs, l => l.Contains($"{FormerName} → {CurrentName}"));
    }

    [Fact]
    public async Task SyncConfigurations_LogsACustomRowHoldingAFormerName()
    {
        var config = await AddConfigurationAsync(FormerName, DisplayNameModes.Custom);

        var result = Result(false);
        await _service.SyncConfigurationsAsync(isDryRun: false, result, Ct);

        Assert.Contains(result.Logs, l => l.Contains($"#{config.Id}") && l.Contains("Left alone"));
        Assert.Equal(0, result.UpdatedRowCount);
    }

    [Fact]
    public async Task ADryRun_WritesNothing_AndASecondRealRunChangesNothing()
    {
        var config = await AddConfigurationAsync(FormerName, DisplayNameModes.ModelName);
        var snapshot = await AddSnapshotAsync(FormerName);
        var log = await AddErrorLogAsync(FormerName);
        var document = await AddDocumentAsync(title: $"About {FormerName}");
        _db.ChangeTracker.Clear();

        var dryConfig = Result(true);
        var dryRecords = Result(true);
        await _service.SyncConfigurationsAsync(isDryRun: true, dryConfig, Ct);
        await _service.SyncRecordsAsync(isDryRun: true, dryRecords, Ct);

        Assert.Equal(1, dryConfig.UpdatedRowCount);
        Assert.Equal(3, dryRecords.UpdatedRowCount);
        Assert.Equal(FormerName, await ConfigurationNameAsync(config.Id));
        Assert.Equal(FormerName, (await SnapshotAsync(snapshot.Id)).DisplayName);
        Assert.Equal(FormerName, await ErrorLogNameAsync(log));
        Assert.Equal($"About {FormerName}", (await DocumentAsync(document.Id)).Title);

        await _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct);
        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        var secondConfig = Result(false);
        var secondRecords = Result(false);
        await _service.SyncConfigurationsAsync(isDryRun: false, secondConfig, Ct);
        await _service.SyncRecordsAsync(isDryRun: false, secondRecords, Ct);

        Assert.Equal(0, secondConfig.UpdatedRowCount);
        Assert.Equal(0, secondRecords.UpdatedRowCount);
        Assert.Equal(0, secondRecords.MergedSnapshotCount);
    }

    // --- Status ---

    [Fact]
    public async Task GetStatus_CountsWhatADryRunCounts_AndZeroAfterARealRun()
    {
        await AddConfigurationAsync(FormerName, DisplayNameModes.ModelName);
        await AddConfigurationAsync(FormerName, null);
        await AddSnapshotAsync(FormerName);
        await AddErrorLogAsync(FormerName);
        await AddErrorLogAsync("Claude 5 Sonnet");
        await AddDocumentAsync(title: $"About {FormerName}", facts: $"{{\"label\":\"{FormerName}\"}}");
        _db.ChangeTracker.Clear();

        var status = await _service.GetStatusAsync(Ct);
        var dryConfig = Result(true);
        var dryRecords = Result(true);
        await _service.SyncConfigurationsAsync(isDryRun: true, dryConfig, Ct);
        await _service.SyncRecordsAsync(isDryRun: true, dryRecords, Ct);

        Assert.Equal(2, status.ConfigurationCount);
        Assert.Equal(0, status.UserModelCount);
        Assert.Equal(1, status.SnapshotCount);
        Assert.Equal(2, status.RecordedRowCount);
        Assert.Equal(1, status.TextRowCount);
        Assert.Equal(dryConfig.UpdatedRowCount, status.ConfigurationCount + status.UserModelCount);
        Assert.Equal(dryRecords.UpdatedRowCount, status.SnapshotCount + status.RecordedRowCount + status.TextRowCount);

        await _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct);
        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        var after = await _service.GetStatusAsync(Ct);
        Assert.Equal(0, after.ConfigurationCount + after.UserModelCount + after.SnapshotCount + after.RecordedRowCount + after.TextRowCount);
    }

    [Fact]
    public async Task GetStatus_ListsRecordedNamesThatMatchNoCatalogEntry()
    {
        await AddErrorLogAsync("Claude 3 Opus");
        await AddErrorLogAsync(FormerName);
        await AddErrorLogAsync(CurrentName);
        await AddErrorLogAsync("A local model");

        var status = await _service.GetStatusAsync(Ct);

        Assert.Equal(new[] { "Claude 3 Opus" }, status.UnmatchedNames);
    }

    // --- Part A ---

    [Fact]
    public async Task SyncRecords_RenamesASnapshot_AndALaterCaptureResolvesToTheSameRow()
    {
        var config = await AddConfigurationAsync(FormerName, DisplayNameModes.ModelName);
        var snapshot = await AddSnapshotAsync(FormerName);
        // The fixture's parallel mode is what a capture of this configuration records.
        await _db.SystemAiApiConfigurations.Where(c => c.Id == config.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParallelExecutionMode, snapshot.ParallelExecutionMode!.Value), Ct);
        _db.ChangeTracker.Clear();
        var createdAt = (await SnapshotAsync(snapshot.Id)).CreatedAtUtc;

        await _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct);
        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        var renamed = await SnapshotAsync(snapshot.Id);
        Assert.Equal(CurrentName, renamed.DisplayName);
        Assert.Equal(SystemAiConfigurationSnapshotStore.ComputeSha256(renamed), renamed.Sha256);
        Assert.Equal(createdAt, renamed.CreatedAtUtc);

        var live = await _db.SystemAiApiConfigurations.AsNoTracking().SingleAsync(c => c.Id == config.Id, Ct);
        var captured = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(_db, live, Ct);

        Assert.Equal(snapshot.Id, captured.Id);
        Assert.Equal(1, await _db.SystemAiConfigurationSnapshots.CountAsync(Ct));
    }

    [Fact]
    public async Task SyncRecords_MergesANewerRowIntoTheOlder_AndRepointsEveryReference()
    {
        var older = await AddSnapshotAsync(FormerName);
        var newer = await AddSnapshotAsync(CurrentName);

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            SuiteName = "Suite",
            Status = BenchmarkRunStatus.Completed,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            TestedModelSnapshot = newer,
            AssessorModelSnapshot = newer,
            CoAssessorModelSnapshot = newer,
            SecondOpinionAssessorModelSnapshot = newer,
            ClaimVerifierModelSnapshot = newer
        });
        _db.BenchmarkRuns.Add(run);
        _db.ChatConsistencyAnalyses.Add(new ChatConsistencyAnalysis
        {
            Name = "A",
            SubjectModelKey = "k",
            TargetRunIdsJson = "[]",
            ControlRunIdsJson = "[]",
            ProtocolVersion = "1",
            ProtocolJson = "{}",
            ResultJson = "{}",
            InputSha256 = new string('b', 64),
            CommonGraderSnapshotId = newer.Id
        });
        await _db.SaveChangesAsync(Ct);
        long runId = run.Id;
        long olderId = older.Id, newerId = newer.Id;
        _db.ChangeTracker.Clear();

        var result = Result(false);
        await _service.SyncRecordsAsync(isDryRun: false, result, Ct);

        Assert.Equal(1, result.MergedSnapshotCount);
        Assert.Null(await _db.SystemAiConfigurationSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == newerId, Ct));
        Assert.Equal(CurrentName, (await SnapshotAsync(olderId)).DisplayName);

        var stored = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(r => r.Id == runId, Ct);
        Assert.Equal(olderId, stored.TestedModelSnapshotId);
        Assert.Equal(olderId, stored.AssessorModelSnapshotId);
        Assert.Equal(olderId, stored.CoAssessorModelSnapshotId);
        Assert.Equal(olderId, stored.SecondOpinionAssessorModelSnapshotId);
        Assert.Equal(olderId, stored.ClaimVerifierModelSnapshotId);
        Assert.Equal(olderId, (await _db.ChatConsistencyAnalyses.AsNoTracking().SingleAsync(Ct)).CommonGraderSnapshotId);
    }

    [Fact]
    public async Task SyncRecords_KeepsAnOlderRowAlreadyHoldingTheTargetHash_AndRemovesTheFormerNameRow()
    {
        var older = await AddSnapshotAsync(CurrentName);
        var newer = await AddSnapshotAsync(FormerName);
        long olderId = older.Id, newerId = newer.Id;
        _db.ChangeTracker.Clear();

        var result = Result(false);
        await _service.SyncRecordsAsync(isDryRun: false, result, Ct);

        Assert.Equal(1, result.MergedSnapshotCount);
        Assert.Equal(CurrentName, (await SnapshotAsync(olderId)).DisplayName);
        Assert.False(await _db.SystemAiConfigurationSnapshots.AnyAsync(s => s.Id == newerId, Ct));
    }

    [Fact]
    public async Task SyncRecords_LeavesNoForeignKeyPointingAtAMergedRow()
    {
        var older = await AddSnapshotAsync(FormerName);
        var newer = await AddSnapshotAsync(CurrentName);
        long olderId = older.Id, newerId = newer.Id;

        _db.BenchmarkBaselineModelSettings.Add(new BenchmarkBaselineModelSetting { ModelSnapshotId = newerId, SetAtUtc = DateTime.UtcNow });
        var document = await AddDocumentAsync();
        await _db.BenchmarkReportDocuments.Where(d => d.Id == document.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.WriterModelSnapshotId, newerId), Ct);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal(olderId, (await _db.BenchmarkBaselineModelSettings.AsNoTracking().IgnoreAutoIncludes().SingleAsync(Ct)).ModelSnapshotId);
        Assert.Equal(olderId, (await DocumentAsync(document.Id)).WriterModelSnapshotId);

        Assert.False(await _db.SystemAiConfigurationSnapshots.AnyAsync(s => s.Id == newerId, Ct));

        var keys = _db.Model.FindEntityType(typeof(SystemAiConfigurationSnapshot))!.GetReferencingForeignKeys().ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            string table = key.DeclaringEntityType.GetTableName()!;
            string column = Assert.Single(key.Properties).GetColumnName(StoreObjectIdentifier.Table(table, key.DeclaringEntityType.GetSchema()))!;
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE [{column}] = {newerId}";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(Ct))!);
        }
    }

    [Fact]
    public async Task SyncRecords_LeavesASnapshotHoldingTheFormerNameOfAnotherModel()
    {
        var other = await AddSnapshotAsync("Claude 5 Sonnet", modelId: ModelId);
        var unknown = await AddSnapshotAsync(FormerName, provider: "OpenAI", modelId: "gpt-5.5");
        long otherId = other.Id, unknownId = unknown.Id;
        string otherHash = other.Sha256;
        _db.ChangeTracker.Clear();

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal("Claude 5 Sonnet", (await SnapshotAsync(otherId)).DisplayName);
        Assert.Equal(otherHash, (await SnapshotAsync(otherId)).Sha256);
        Assert.Equal(FormerName, (await SnapshotAsync(unknownId)).DisplayName);
    }

    // --- Part B ---

    [Fact]
    public async Task SyncRecords_ChangesEveryFormerNameInAColumnInOnePass_AndLeavesOtherValues()
    {
        var opus = await AddErrorLogAsync(FormerName);
        var sonnet = await AddErrorLogAsync("Claude 5 Sonnet");
        var label = await AddErrorLogAsync($"{FormerName} Extended Thinking");
        var none = await AddErrorLogAsync(null);
        var gemini = await AddErrorLogAsync("Gemini 3.5 Flash");

        var result = Result(false);
        await _service.SyncRecordsAsync(isDryRun: false, result, Ct);

        Assert.Equal(CurrentName, await ErrorLogNameAsync(opus));
        Assert.Equal("Claude Sonnet 5", await ErrorLogNameAsync(sonnet));
        Assert.Equal($"{FormerName} Extended Thinking", await ErrorLogNameAsync(label));
        Assert.Null(await ErrorLogNameAsync(none));
        Assert.Equal("Gemini 3.5 Flash", await ErrorLogNameAsync(gemini));
        Assert.Equal(2, result.UpdatedRowCount);
    }

    [Fact]
    public async Task SyncRecords_RewritesTheWriterDisplayName()
    {
        var document = await AddDocumentAsync(writer: FormerName);

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal(CurrentName, (await DocumentAsync(document.Id)).WriterDisplayName);
    }

    // --- Part C ---

    [Theory]
    [InlineData("Claude 5 Opus won.", "Claude Opus 5 won.")]
    [InlineData("Claude 5 Opus's answer", "Claude Opus 5's answer")]
    [InlineData("It was Claude 5 Opus.", "It was Claude Opus 5.")]
    [InlineData("(Claude 5 Opus)", "(Claude Opus 5)")]
    [InlineData("Claude 5.5 Opus and Claude 5 Opus", "Claude Opus 5.5 and Claude Opus 5")]
    [InlineData("Claude 5 Opus2", "Claude 5 Opus2")]
    [InlineData("xClaude 5 Opus", "xClaude 5 Opus")]
    [InlineData("Claude 5 Opus.5", "Claude 5 Opus.5")]
    [InlineData("Claude Opus 5", "Claude Opus 5")]
    [InlineData("{\"a\":\"line one\\nClaude 5 Opus did well\"}", "{\"a\":\"line one\\nClaude Opus 5 did well\"}")]
    [InlineData("{\"a\":\"\\u0022Claude 5 Opus\\u0022\"}", "{\"a\":\"\\u0022Claude Opus 5\\u0022\"}")]
    public async Task SyncRecords_ReplacesNamesAtWordBoundaries(string stored, string expected)
    {
        var document = await AddDocumentAsync(writerOutput: stored);

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        Assert.Equal(expected, (await DocumentAsync(document.Id)).WriterOutputJson);
    }

    [Fact]
    public async Task SyncRecords_LeavesAProtectedLabelWhole()
    {
        await AddConfigurationAsync("Claude 5.5 Opus Extended Thinking", DisplayNameModes.Custom);
        var document = await AddDocumentAsync(title: "Claude 5.5 Opus Extended Thinking against Claude 5.5 Opus");

        var result = Result(false);
        await _service.SyncRecordsAsync(isDryRun: false, result, Ct);

        Assert.Equal("Claude 5.5 Opus Extended Thinking against Claude Opus 5.5", (await DocumentAsync(document.Id)).Title);
        Assert.Contains(result.Logs, l => l.Contains("Left alone") && l.Contains("Claude 5.5 Opus Extended Thinking"));
    }

    [Fact]
    public async Task SyncRecords_RewritesEveryColumnOfADocument_AndLeavesContentJson_AndKeepsHashes()
    {
        var document = await AddDocumentAsync(
            subjectLabel: FormerName,
            title: $"About {FormerName}",
            facts: $"{{\"label\":\"{FormerName}\"}}",
            content: $"{{\"answer\":\"{FormerName}\"}}",
            writerOutput: $"{{\"text\":\"{FormerName}\"}}");

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        var stored = await DocumentAsync(document.Id);
        Assert.Equal(CurrentName, stored.SubjectLabel);
        Assert.Equal($"About {CurrentName}", stored.Title);
        Assert.Equal($"{{\"label\":\"{CurrentName}\"}}", stored.FactsJson);
        Assert.Equal($"{{\"text\":\"{CurrentName}\"}}", stored.WriterOutputJson);
        Assert.Equal($"{{\"answer\":\"{FormerName}\"}}", stored.ContentJson);
        Assert.Equal(new string('a', 64), stored.WriterPromptSha256);
    }

    [Fact]
    public async Task SyncRecords_EvictsTheFactsOfAChangedDocumentOnly()
    {
        var changed = await AddDocumentAsync(facts: $"{{\"label\":\"{FormerName}\"}}");
        var unchanged = await AddDocumentAsync();
        _factsCache.Set(changed.Id, BenchmarkReportRenderService.StoredFacts.Empty);
        _factsCache.Set(unchanged.Id, BenchmarkReportRenderService.StoredFacts.Empty);

        await _service.SyncRecordsAsync(isDryRun: true, Result(true), Ct);
        Assert.True(_factsCache.TryGet(changed.Id, out _));

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        Assert.False(_factsCache.TryGet(changed.Id, out _));
        Assert.True(_factsCache.TryGet(unchanged.Id, out _));
    }

    [Fact]
    public async Task SyncRecords_RewritesGroupAndComparisonNames_AndTheIntKeyedTables()
    {
        var group = new BenchmarkRunGroup { Name = $"Suite · {FormerName} · 2026-10-01 · R=3" };
        var comparison = new BenchmarkComparison
        {
            ComparisonKey = new string('c', 64),
            EntryKeysJson = "[]",
            SubjectKind = BenchmarkComparisonSubjectKind.Runs,
            DefaultName = $"{FormerName} vs Gemini 3.5 Flash",
            Name = $"My {FormerName} comparison",
            CreatedAtUtc = DateTime.UtcNow
        };
        var analysis = new ChatConsistencyAnalysis
        {
            Name = $"{FormerName} over time",
            SubjectModelKey = "k",
            TargetRunIdsJson = "[]",
            ControlRunIdsJson = "[]",
            ProtocolVersion = "1",
            ProtocolJson = "{}",
            ResultJson = $"{{\"model\":\"{FormerName}\"}}",
            InputSha256 = new string('d', 64)
        };
        _db.BenchmarkRunGroups.Add(group);
        _db.BenchmarkComparisons.Add(comparison);
        _db.ChatConsistencyAnalyses.Add(analysis);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        await _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct);

        var storedGroup = await _db.BenchmarkRunGroups.AsNoTracking().IgnoreAutoIncludes().SingleAsync(g => g.Id == group.Id, Ct);
        Assert.True(BenchmarkRunGroupNames.IsDefault(storedGroup.Name, "Suite", CurrentName, 3));

        var storedComparison = await _db.BenchmarkComparisons.AsNoTracking().SingleAsync(c => c.Id == comparison.Id, Ct);
        Assert.Equal($"{CurrentName} vs Gemini 3.5 Flash", storedComparison.DefaultName);
        Assert.Equal($"My {CurrentName} comparison", storedComparison.Name);

        var storedAnalysis = await _db.ChatConsistencyAnalyses.AsNoTracking().SingleAsync(a => a.Id == analysis.Id, Ct);
        Assert.Equal($"{CurrentName} over time", storedAnalysis.Name);
        Assert.Equal($"{{\"model\":\"{CurrentName}\"}}", storedAnalysis.ResultJson);
        Assert.Equal(new string('d', 64), storedAnalysis.InputSha256);
    }

    [Fact]
    public async Task SyncRecords_ReachesRowsBeyondTheFirstBatch()
    {
        for (int i = 0; i < 205; i++)
            _db.BenchmarkRunGroups.Add(new BenchmarkRunGroup { Name = $"{FormerName} {i}" });
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        var result = Result(false);
        await _service.SyncRecordsAsync(isDryRun: false, result, Ct);

        Assert.Equal(205, result.UpdatedRowCount);
        Assert.Equal(205, await _db.BenchmarkRunGroups.CountAsync(g => g.Name.StartsWith(CurrentName), Ct));
    }

    // --- Guard ---

    [Fact]
    public async Task ActiveWork_RefusesARealRun_AndIsReportedByADryRunAndTheStatus()
    {
        var config = await AddConfigurationAsync(FormerName, DisplayNameModes.ModelName);
        var job = new BenchmarkDifficultyJob { SuiteName = "Sokoban basics", AssessorConfigId = 99 };
        Assert.True(_difficulty.TryStart(job, out _));

        var refusedConfig = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SyncConfigurationsAsync(isDryRun: false, Result(false), Ct));
        Assert.Contains("Sokoban basics", refusedConfig.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SyncRecordsAsync(isDryRun: false, Result(false), Ct));
        Assert.Equal(FormerName, await ConfigurationNameAsync(config.Id));

        var dry = Result(true);
        await _service.SyncConfigurationsAsync(isDryRun: true, dry, Ct);
        Assert.Contains(dry.Logs, l => l.Contains("Sokoban basics") && l.Contains("refused"));

        var status = await _service.GetStatusAsync(Ct);
        Assert.Equal("difficultyJob", Assert.Single(status.Blockers).Kind);
    }
}
