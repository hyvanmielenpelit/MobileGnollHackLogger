using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Telemetry;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The chat and title provider-attempt ledger and the user-key usage log: how attempts map to rows, how the two
/// recorders save in a scope of their own and swallow failures, and how both tables cascade with the user.
/// </summary>
public class ChatProviderAttemptLedgerTests
{
    private const string UserId = "ledger-user";
    private static readonly DateTime Started = new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

    private static ProviderAttemptRecord AttemptRecord(int index, ProviderAttemptOutcome outcome = ProviderAttemptOutcome.Ok,
        int? httpStatus = 200, string? errorCode = null, bool retried = false) => new()
    {
        AttemptIndex = index,
        StartedAtUtc = Started.AddSeconds(index),
        DurationMs = 100 + index,
        Outcome = outcome,
        HttpStatus = httpStatus,
        ProviderErrorCode = errorCode,
        EndpointKind = BenchmarkEndpointKind.Official,
        Retried = retried,
        RetryAfterMs = retried ? 2000 : null
    };

    private static ChatAttemptLinks Links(AiKeySource keySource = AiKeySource.System, string? userId = UserId,
        string? thinkingLevel = "medium") =>
        new(Guid.Parse("11111111-2222-3333-4444-555555555555"), RoleContext: 1, AgentDepth: 0, keySource,
            SystemAiApiConfigurationId: keySource == AiKeySource.System ? 31 : null, userId, thinkingLevel);

    /// <summary>Call 0 with one attempt; call 1 with an overloaded attempt and its successful retry.</summary>
    private static AgentRunResult TwoCallsThreeAttempts(string provider = "Anthropic", string model = "claude-model") => new()
    {
        ModelCalls = new List<ModelCallRecord>
        {
            new()
            {
                CallIndex = 0, Provider = provider, RequestedModelId = model,
                Attempts = new List<ProviderAttemptRecord> { AttemptRecord(0) }
            },
            new()
            {
                CallIndex = 1, Provider = provider, RequestedModelId = model,
                Attempts = new List<ProviderAttemptRecord>
                {
                    AttemptRecord(0, ProviderAttemptOutcome.ProviderOverloaded, 529, "overloaded_error", retried: true),
                    AttemptRecord(1)
                }
            }
        }
    };

    // --- Mapping -------------------------------------------------------------------------------

    [Fact]
    public void MapChatAttempts_MapsEveryAttemptAtItsCallAndAttemptIndex()
    {
        var rows = ModelCallTelemetryWriter.MapChatAttempts(TwoCallsThreeAttempts(), Links());

        Assert.Equal(new[] { (0, (byte)0), (1, (byte)0), (1, (byte)1) }, rows.Select(r => (r.CallIndex, r.AttemptIndex)));
        Assert.All(rows, r =>
        {
            Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), r.AgentRunId);
            Assert.Equal(1, r.RoleContext);
            Assert.Equal((byte)0, r.AgentDepth);
            Assert.Equal(AiKeySource.System, r.KeySource);
            Assert.Equal(31, r.SystemAiApiConfigurationId);
            Assert.Equal("Anthropic", r.Provider);
            Assert.Equal("claude-model", r.RequestedModelId);
            Assert.Equal("medium", r.ThinkingLevel);
            Assert.Equal(BenchmarkEndpointKind.Official, r.EndpointKind);
        });

        var overloaded = rows[1];
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, overloaded.Outcome);
        Assert.Equal((short)529, overloaded.HttpStatus);
        Assert.Equal("overloaded_error", overloaded.ProviderErrorCode);
        Assert.True(overloaded.Retried);
        Assert.Equal(2000, overloaded.RetryAfterMs);
        Assert.Equal(Started, overloaded.StartedAtUtc);
        Assert.Equal(100, overloaded.DurationMs);
        Assert.Equal(ProviderAttemptOutcome.Ok, rows[2].Outcome);
        Assert.False(rows[2].Retried);
    }

    [Fact]
    public void MapChatAttempts_CutsStringsToTheirColumns()
    {
        string provider = new('p', 40);
        string model = new('m', 200);
        string thinking = new('t', 40);

        var row = ModelCallTelemetryWriter.MapChatAttempts(TwoCallsThreeAttempts(provider, model), Links(thinkingLevel: thinking))[0];

        Assert.Equal(new string('p', 32), row.Provider);
        Assert.Equal(new string('m', 128), row.RequestedModelId);
        Assert.Equal(new string('t', 32), row.ThinkingLevel);
    }

    [Fact]
    public void MapChatAttempts_ANullThinkingLevelIsStoredEmpty()
    {
        var rows = ModelCallTelemetryWriter.MapChatAttempts(TwoCallsThreeAttempts(), Links(thinkingLevel: null));

        Assert.All(rows, r => Assert.Equal(string.Empty, r.ThinkingLevel));
    }

    [Fact]
    public void MapChatAttempts_SanitizesTheErrorCode_AndNarrowsTheStatus()
    {
        var result = new AgentRunResult
        {
            ModelCalls = new List<ModelCallRecord>
            {
                new()
                {
                    CallIndex = 0, Provider = "OpenAI", RequestedModelId = "gpt-model",
                    Attempts = new List<ProviderAttemptRecord>
                    {
                        AttemptRecord(0, ProviderAttemptOutcome.ProviderServerError, 503, "server error/<b>v1.2-x_y</b>!", retried: true),
                        AttemptRecord(1, ProviderAttemptOutcome.TransportUnattributed, 70_000, "!!!", retried: true),
                        AttemptRecord(2, ProviderAttemptOutcome.TransportUnattributed, null, null)
                    }
                }
            }
        };

        var rows = ModelCallTelemetryWriter.MapChatAttempts(result, Links());

        Assert.Equal("servererrorbv1.2-x_yb", rows[0].ProviderErrorCode);
        Assert.Equal((short)503, rows[0].HttpStatus);
        Assert.Null(rows[1].ProviderErrorCode);
        Assert.Null(rows[1].HttpStatus);
        Assert.Null(rows[2].ProviderErrorCode);
        Assert.Null(rows[2].HttpStatus);
    }

    [Fact]
    public void MapChatAttempts_WritesTheUserIdOnUserKeyRowsOnly()
    {
        var userKeyRows = ModelCallTelemetryWriter.MapChatAttempts(TwoCallsThreeAttempts(), Links(AiKeySource.UserKey, UserId));
        var systemRows = ModelCallTelemetryWriter.MapChatAttempts(TwoCallsThreeAttempts(), Links(AiKeySource.System, UserId));

        Assert.All(userKeyRows, r => Assert.Equal(UserId, r.AspNetUserId));
        Assert.All(userKeyRows, r => Assert.Equal(AiKeySource.UserKey, r.KeySource));
        Assert.All(systemRows, r => Assert.Null(r.AspNetUserId));
    }

    [Fact]
    public void MapTitleAttempt_IsCallZero()
    {
        var links = new ChatAttemptLinks(Guid.NewGuid(), RoleContext: 2, AgentDepth: 0, AiKeySource.UserKey, null, UserId, null);

        var row = ModelCallTelemetryWriter.MapTitleAttempt("Google", "gemini-model", AttemptRecord(1), links);

        Assert.Equal(0, row.CallIndex);
        Assert.Equal((byte)1, row.AttemptIndex);
        Assert.Equal(2, row.RoleContext);
        Assert.Equal("Google", row.Provider);
        Assert.Equal("gemini-model", row.RequestedModelId);
        Assert.Equal(string.Empty, row.ThinkingLevel);
        Assert.Equal(UserId, row.AspNetUserId);
    }

    // --- Recorders -----------------------------------------------------------------------------

    /// <summary>Fails every save.</summary>
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("rejected");
    }

    private static ServiceProvider Container(string databaseName, bool failSaves = false)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
        {
            o.UseInMemoryDatabase(databaseName);
            if (failSaves) o.AddInterceptors(new FailingSaveInterceptor());
        });
        return services.BuildServiceProvider();
    }

    private static async Task<List<T>> ReadAsync<T>(ServiceProvider container, Func<ApplicationDbContext, IQueryable<T>> query)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await query(db).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ChatProviderAttemptRecorder_SavesTheRowsInItsOwnScope()
    {
        using var container = Container(Guid.NewGuid().ToString());

        await ChatProviderAttemptRecorder.TryRecordAsync(container.GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance, TwoCallsThreeAttempts(), Links());

        var rows = await ReadAsync(container, db => db.ChatProviderAttempts.AsNoTracking());
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("claude-model", r.RequestedModelId));
    }

    [Fact]
    public async Task ChatProviderAttemptRecorder_SwallowsAFailingContext()
    {
        using var container = Container(Guid.NewGuid().ToString(), failSaves: true);

        var exception = await Record.ExceptionAsync(() => ChatProviderAttemptRecorder.TryRecordAsync(
            container.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, TwoCallsThreeAttempts(), Links()));

        Assert.Null(exception);
        Assert.Empty(await ReadAsync(container, db => db.ChatProviderAttempts.AsNoTracking()));
    }

    [Fact]
    public async Task ChatProviderAttemptRecorder_WithNoRowsDoesNothing()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        // No context is registered, so reaching for one would be caught and logged; an empty list never gets that far.
        var exception = await Record.ExceptionAsync(() => ChatProviderAttemptRecorder.TryRecordAsync(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, new List<ChatProviderAttempt>()));

        Assert.Null(exception);
    }

    private static UserKeyUsageEntry Entry(string userId = UserId, decimal? cost = 0.042m) => new(
        UserId: userId,
        UserAiModelId: 17,
        Provider: new string('p', 80),
        ModelId: new string('m', 200),
        ModelDisplayName: new string('d', 300),
        ThinkingLevel: new string('t', 40),
        InputTokens: 1200,
        OutputTokens: 300,
        CacheReadInputTokens: 800,
        CacheCreationInputTokens: 50,
        TotalDurationMs: 4321,
        RoleContext: 1,
        EstimatedCost: cost,
        PricingSource: "catalog-with-a-long-suffix",
        TimestampUtc: Started);

    [Fact]
    public async Task UserKeyUsageRecorder_SavesOneRowWithItsStringsCut()
    {
        using var container = Container(Guid.NewGuid().ToString());

        await UserKeyUsageRecorder.TryRecordAsync(container.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, Entry());

        var row = Assert.Single(await ReadAsync(container, db => db.UserKeyAiUsageLogs.AsNoTracking()));
        Assert.Equal(UserId, row.AspNetUserId);
        Assert.Equal(17, row.UserAiModelId);
        Assert.Equal(Started, row.TimestampUtc);
        Assert.Equal(new string('p', 64), row.Provider);
        Assert.Equal(new string('m', 128), row.ModelId);
        Assert.Equal(new string('d', 256), row.ModelDisplayName);
        Assert.Equal(new string('t', 32), row.ThinkingLevel);
        Assert.Equal(1200, row.InputTokens);
        Assert.Equal(300, row.OutputTokens);
        Assert.Equal(800, row.CacheReadInputTokens);
        Assert.Equal(50, row.CacheCreationInputTokens);
        Assert.Equal(4321, row.TotalDurationMs);
        Assert.Equal(1, row.RoleContext);
        Assert.Equal(0.042m, row.EstimatedCost);
        Assert.Equal("catalog-with-a-l", row.PricingSource);
    }

    [Fact]
    public async Task UserKeyUsageRecorder_WithoutACost_StoresNoSource()
    {
        using var container = Container(Guid.NewGuid().ToString());

        await UserKeyUsageRecorder.TryRecordAsync(container.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, Entry(cost: null));

        var row = Assert.Single(await ReadAsync(container, db => db.UserKeyAiUsageLogs.AsNoTracking()));
        Assert.Null(row.EstimatedCost);
        Assert.Null(row.PricingSource);
    }

    [Fact]
    public async Task UserKeyUsageRecorder_WithoutAUserRecordsNothing()
    {
        using var container = Container(Guid.NewGuid().ToString());

        await UserKeyUsageRecorder.TryRecordAsync(container.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, Entry(userId: ""));

        Assert.Empty(await ReadAsync(container, db => db.UserKeyAiUsageLogs.AsNoTracking()));
    }

    [Fact]
    public async Task UserKeyUsageRecorder_SwallowsAFailingContext()
    {
        using var container = Container(Guid.NewGuid().ToString(), failSaves: true);

        var exception = await Record.ExceptionAsync(() => UserKeyUsageRecorder.TryRecordAsync(
            container.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, Entry()));

        Assert.Null(exception);
        Assert.Empty(await ReadAsync(container, db => db.UserKeyAiUsageLogs.AsNoTracking()));
    }

    // --- Cascade -------------------------------------------------------------------------------

    [Fact]
    public async Task DeletingAUserRemovesTheirUserKeyRows_AndKeepsSystemAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = SqliteSchemaTemplate.OpenCopy();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);

        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "ledger-user" });
        db.UserKeyAiUsageLogs.Add(new UserKeyAiUsageLog
        {
            AspNetUserId = UserId, TimestampUtc = Started, Provider = "OpenAI", ModelId = "gpt-model", RoleContext = 1
        });
        var userKeyLinks = Links(AiKeySource.UserKey, UserId);
        var systemLinks = Links(AiKeySource.System, UserId);
        db.ChatProviderAttempts.Add(ModelCallTelemetryWriter.MapTitleAttempt("OpenAI", "gpt-model", AttemptRecord(0), userKeyLinks));
        db.ChatProviderAttempts.Add(ModelCallTelemetryWriter.MapTitleAttempt("Anthropic", "claude-model", AttemptRecord(0), systemLinks));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // The dependents are not loaded, so the database's own cascade removes them.
        var user = await db.Users.SingleAsync(u => u.Id == UserId, ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        Assert.Empty(await db.UserKeyAiUsageLogs.ToListAsync(ct));
        var remaining = Assert.Single(await db.ChatProviderAttempts.ToListAsync(ct));
        Assert.Equal(AiKeySource.System, remaining.KeySource);
        Assert.Null(remaining.AspNetUserId);
    }
}
