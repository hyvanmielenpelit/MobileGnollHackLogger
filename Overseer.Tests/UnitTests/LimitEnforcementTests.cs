using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Security;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Access and limit enforcement for system AI configurations: ordering never grants access, a disabled or
/// exhausted assignment never falls through to another grant, GnollBench roles consume no quota, refusals
/// name the limit and its reset and are recorded as security events, and the chat and sub-agent fallbacks
/// move past a refused configuration.
/// </summary>
public class LimitEnforcementTests : IDisposable
{
    private const string UserId = "limit-user";
    private const string OtherUserId = "limit-other-user";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-limits-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    // -- Fixtures ----------------------------------------------------------------------------------

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        private readonly ConcurrentQueue<SecurityEventEntry> _entries = new();

        public IReadOnlyList<SecurityEventEntry> Entries => _entries.ToArray();

        public void Record(SecurityEventEntry entry) => _entries.Enqueue(entry);
    }

    private static ApplicationDbContext NewInMemoryDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>SQLite in memory, for the set-based ExecuteDeleteAsync and ExecuteUpdateAsync calls.</summary>
    private static (ApplicationDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) NewRelationalDb()
    {
        var connection = SqliteSchemaTemplate.OpenCopy();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        return (db, connection);
    }

    private static IConfiguration CryptoConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
            })
            .Build();

    private static SettingsService NewSettingsService(ApplicationDbContext db)
    {
        var config = CryptoConfiguration();
        return new SettingsService(
            db, new CryptoService(config), new ConfidentialityPostureService(config),
            new ModelAvailabilityService(new ModelMetadataService()));
    }

    private static SystemAiConfigService NewConfigService(ApplicationDbContext db, ISecurityEventRecorder? events = null)
        => new(db, NullLogger<SystemAiConfigService>.Instance, events);

    /// <summary>An enabled chat-and-title configuration whose counters were reset today.</summary>
    private static SystemAiApiConfiguration NewConfig(string name, bool systemWide, int orderIndex = 0) => new()
    {
        DisplayName = name,
        Provider = "OpenAI",
        ModelId = "gpt-" + name.ToLowerInvariant(),
        IsEnabled = true,
        IsSystemWide = systemWide,
        ModelRole = 3,
        OrderIndex = orderIndex,
        LastDailyReset = DateTime.UtcNow,
        LastMonthlyReset = DateTime.UtcNow
    };

    private static void AddUsers(ApplicationDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "limit-user", Email = "limit-user@example.com" });
        db.Users.Add(new ApplicationUser { Id = OtherUserId, UserName = "limit-other", Email = "limit-other@example.com" });
    }

    /// <summary>A group with the user as its only member, saved.</summary>
    private static async Task<Group> AddGroupWithUserAsync(ApplicationDbContext db)
    {
        var group = new Group { DisplayName = "Testers", CreatedAtUtc = DateTime.UtcNow };
        db.Groups.Add(group);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserGroups.Add(new UserGroup { AspNetUserId = UserId, GroupId = group.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return group;
    }

    // -- Ordering ----------------------------------------------------------------------------------

    [Fact]
    public async Task Reorder_WithAnInvisibleConfiguration_CreatesNoAssignmentAndNoOrderRowForIt()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var hidden = NewConfig("Hidden", systemWide: false, orderIndex: 0);
        var visible = NewConfig("Visible", systemWide: true, orderIndex: 1);
        db.SystemAiApiConfigurations.AddRange(hidden, visible);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var settings = NewSettingsService(db);

        await settings.ReorderUserSystemModelsAsync(UserId, new[] { hidden.Id, visible.Id });

        Assert.Empty(await db.UserSystemAiApiConfigurations.ToListAsync(TestContext.Current.CancellationToken));
        var row = Assert.Single(await db.UserSystemModelOrders.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(visible.Id, row.SystemAiApiConfigurationId);
        Assert.Equal(UserId, row.AspNetUserId);
        Assert.Equal(0, row.OrderIndex);

        var resolved = await settings.GetResolvedSystemModelsAsync(UserId);
        Assert.Equal(new[] { visible.Id }, resolved.Select(r => r.Config.Id));
    }

    [Fact]
    public async Task Reorder_OfVisibleConfigurations_WritesOrderRowsOnly_AndTheResolvedListFollowsThem()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var first = NewConfig("First", systemWide: true, orderIndex: 0);
        var second = NewConfig("Second", systemWide: true, orderIndex: 1);
        var assigned = NewConfig("Assigned", systemWide: false, orderIndex: 2);
        db.SystemAiApiConfigurations.AddRange(first, second, assigned);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = assigned.Id, IsEnabled = true, ModelRole = 3, OrderIndex = 7
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var settings = NewSettingsService(db);

        await settings.ReorderUserSystemModelsAsync(UserId, new[] { assigned.Id, second.Id, first.Id });

        var rows = await db.UserSystemModelOrders.OrderBy(o => o.OrderIndex).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { assigned.Id, second.Id, first.Id }, rows.Select(o => o.SystemAiApiConfigurationId));
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(o => o.OrderIndex));
        Assert.All(rows, o => Assert.Equal(UserId, o.AspNetUserId));

        // The one existing assignment is untouched, and no assignment was created.
        var assignment = Assert.Single(await db.UserSystemAiApiConfigurations.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(assigned.Id, assignment.SystemAiApiConfigurationId);
        Assert.Equal(7, assignment.OrderIndex);

        var resolved = await settings.GetResolvedSystemModelsAsync(UserId);
        Assert.Equal(new[] { assigned.Id, second.Id, first.Id }, resolved.Select(r => r.Config.Id));
    }

    [Fact]
    public async Task ResetSystemModelsOrder_DeletesTheUsersOrderRows_AndClearsTheirLegacyAssignmentOrder()
    {
        var (db, connection) = NewRelationalDb();
        using var _ = db;
        using var __ = connection;
        var ct = TestContext.Current.CancellationToken;

        AddUsers(db);
        var first = NewConfig("First", systemWide: true, orderIndex: 0);
        var second = NewConfig("Second", systemWide: true, orderIndex: 1);
        db.SystemAiApiConfigurations.AddRange(first, second);
        await db.SaveChangesAsync(ct);

        db.UserSystemAiApiConfigurations.AddRange(
            new UserSystemAiApiConfiguration { AspNetUserId = UserId, SystemAiApiConfigurationId = second.Id, IsEnabled = true, OrderIndex = 0 },
            new UserSystemAiApiConfiguration { AspNetUserId = OtherUserId, SystemAiApiConfigurationId = second.Id, IsEnabled = true, OrderIndex = 4 });
        db.UserSystemModelOrders.AddRange(
            new UserSystemModelOrder { AspNetUserId = UserId, SystemAiApiConfigurationId = second.Id, OrderIndex = 0 },
            new UserSystemModelOrder { AspNetUserId = UserId, SystemAiApiConfigurationId = first.Id, OrderIndex = 1 },
            new UserSystemModelOrder { AspNetUserId = OtherUserId, SystemAiApiConfigurationId = first.Id, OrderIndex = 0 });
        await db.SaveChangesAsync(ct);

        await NewSettingsService(db).ResetSystemModelsOrderAsync(UserId);

        // The set-based calls bypass the change tracker, so read back from the database.
        db.ChangeTracker.Clear();
        Assert.Empty(await db.UserSystemModelOrders.Where(o => o.AspNetUserId == UserId).ToListAsync(ct));
        var otherOrder = Assert.Single(await db.UserSystemModelOrders.Where(o => o.AspNetUserId == OtherUserId).ToListAsync(ct));
        Assert.Equal(first.Id, otherOrder.SystemAiApiConfigurationId);

        var userAssignment = await db.UserSystemAiApiConfigurations.SingleAsync(u => u.AspNetUserId == UserId, ct);
        Assert.Null(userAssignment.OrderIndex);
        Assert.True(userAssignment.IsEnabled);
        var otherAssignment = await db.UserSystemAiApiConfigurations.SingleAsync(u => u.AspNetUserId == OtherUserId, ct);
        Assert.Equal(4, otherAssignment.OrderIndex);
    }

    // -- Access checks -----------------------------------------------------------------------------

    [Fact]
    public async Task AnUnlimitedUserAssignment_IsRefused_WhenItsGroupAssignmentIsExhausted()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        // System-wide too, so a fall-through to the system-wide grant would let the request in.
        var config = NewConfig("Grouped", systemWide: true);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var group = await AddGroupWithUserAsync(db);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3
        });
        db.GroupSystemAiApiConfigurations.Add(new GroupSystemAiApiConfiguration
        {
            GroupId = group.Id, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
            MaxDailyChatRequests = 50, DailyChatRequestsCount = 50,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal("Your group has reached its daily limit for this model (50 requests). It resets at 00:00 UTC.", error);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.QuotaRefused, entry.Kind);
        Assert.Equal("group", entry.Code);
        Assert.Equal(config.Id, entry.SystemAiApiConfigurationId);
        Assert.Equal(UserId, entry.UserId);
    }

    [Fact]
    public async Task AnUnlimitedUserAssignment_IsAllowed_WhenOneOfItsGroupAssignmentsIsUnderItsLimit()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Grouped", systemWide: false);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var exhausted = await AddGroupWithUserAsync(db);
        var roomy = new Group { DisplayName = "Roomy", CreatedAtUtc = DateTime.UtcNow };
        db.Groups.Add(roomy);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserGroups.Add(new UserGroup { AspNetUserId = UserId, GroupId = roomy.Id });
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3
        });
        db.GroupSystemAiApiConfigurations.AddRange(
            new GroupSystemAiApiConfiguration
            {
                GroupId = exhausted.Id, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
                MaxDailyChatRequests = 50, DailyChatRequestsCount = 50,
                LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
            },
            new GroupSystemAiApiConfiguration
            {
                GroupId = roomy.Id, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
                MaxDailyChatRequests = 50, DailyChatRequestsCount = 3,
                LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.NotNull(allowed);
        Assert.Null(error);
        Assert.Empty(events.Entries);
    }

    [Fact]
    public async Task ADisabledUserAssignment_OnASystemWideConfiguration_IsRefused_AndHiddenFromTheResolvedList()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var disabled = NewConfig("Disabled", systemWide: true, orderIndex: 0);
        var open = NewConfig("Open", systemWide: true, orderIndex: 1);
        db.SystemAiApiConfigurations.AddRange(disabled, open);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // An enabled group grant as well: a disabled user assignment denies outright regardless.
        var group = await AddGroupWithUserAsync(db);
        db.GroupSystemAiApiConfigurations.Add(new GroupSystemAiApiConfiguration
        {
            GroupId = group.Id, SystemAiApiConfigurationId = disabled.Id, IsEnabled = true, ModelRole = 3,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        });
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = disabled.Id, IsEnabled = false, ModelRole = 3
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(disabled.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal(SystemAiConfigService.DisabledAssignmentMessage, error);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.AccessDenied, entry.Kind);
        Assert.Equal("disabled", entry.Code);
        Assert.Equal(disabled.Id, entry.SystemAiApiConfigurationId);
        Assert.Equal(UserId, entry.UserId);

        var resolved = await NewSettingsService(db).GetResolvedSystemModelsAsync(UserId);
        Assert.Equal(new[] { open.Id }, resolved.Select(r => r.Config.Id));
    }

    [Fact]
    public async Task AnUnassignedConfiguration_IsRefusedAsNotAssigned()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Private", systemWide: false);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal(SystemAiConfigService.NotAssignedMessage, error);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.AccessDenied, entry.Kind);
        Assert.Equal("not-assigned", entry.Code);
        Assert.Equal(config.Id, entry.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task AnExhaustedUserAssignment_IsRefusedWithTheUserMessage_AndDoesNotFallThroughToTheSystemWideGrant()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Personal", systemWide: true);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
            MaxDailyChatRequests = 50, DailyChatRequestsCount = 50,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal("You have reached your daily limit for this model (50 requests). It resets at 00:00 UTC.", error);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.QuotaRefused, entry.Kind);
        Assert.Equal("user", entry.Code);
        Assert.Equal(config.Id, entry.SystemAiApiConfigurationId);
        Assert.Equal(UserId, entry.UserId);
    }

    [Fact]
    public async Task AnExhaustedConfigurationLimit_IsRefusedWithTheSystemMessage()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Shared", systemWide: true);
        config.MaxMonthlyChatTokens = 1_000_000;
        config.MonthlyChatTokensCount = 1_000_000;
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var events = new RecordingSecurityEvents();

        var (allowed, error) = await NewConfigService(db, events).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal("This model has reached its monthly limit for all users (1,000,000 tokens). It resets on the 1st of the month (UTC).", error);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.QuotaRefused, entry.Kind);
        Assert.Equal("system", entry.Code);
        Assert.Equal(config.Id, entry.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task ATitleRoleCheck_NamesTheTitleGenerationLimit()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Titles", systemWide: true);
        config.MaxDailyTitleRequests = 5;
        config.DailyTitleRequestsCount = 5;
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (allowed, error) = await NewConfigService(db).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 2);

        Assert.Null(allowed);
        Assert.Equal("This model has reached its daily title-generation limit for all users (5 requests). It resets at 00:00 UTC.", error);

        // The chat role is not limited by the title counters.
        var (chatAllowed, chatError) = await NewConfigService(db).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);
        Assert.NotNull(chatAllowed);
        Assert.Null(chatError);
    }

    [Fact]
    public async Task ALimitOfZero_MeansNoLimit()
    {
        using var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Unlimited", systemWide: true);
        config.MaxDailyChatRequests = 0;
        config.DailyChatRequestsCount = 500;
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
            MaxTotalChatTokens = 0, TotalChatTokensCount = 9_999_999,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (allowed, error) = await NewConfigService(db).GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.NotNull(allowed);
        Assert.Null(error);
    }

    // -- Refusal wording ---------------------------------------------------------------------------

    [Theory]
    [InlineData("User", "Daily", false, 50L, 1, "You have reached your daily limit for this model (50 requests). It resets at 00:00 UTC.")]
    [InlineData("Group", "Monthly", false, 1L, 1, "Your group has reached its monthly limit for this model (1 request). It resets on the 1st of the month (UTC).")]
    [InlineData("System", "Total", false, 1000L, 1, "This model has reached its lifetime limit for all users (1,000 requests). It does not reset.")]
    [InlineData("User", "Daily", true, 250000L, 1, "You have reached your daily limit for this model (250,000 tokens). It resets at 00:00 UTC.")]
    [InlineData("Group", "Monthly", true, 1000L, 1, "Your group has reached its monthly limit for this model (1,000 tokens). It resets on the 1st of the month (UTC).")]
    [InlineData("System", "Total", true, 5000000L, 1, "This model has reached its lifetime limit for all users (5,000,000 tokens). It does not reset.")]
    [InlineData("User", "Total", false, 10L, 2, "You have reached your lifetime title-generation limit for this model (10 requests). It does not reset.")]
    [InlineData("Group", "Daily", true, 20000L, 2, "Your group has reached its daily title-generation limit for this model (20,000 tokens). It resets at 00:00 UTC.")]
    public void LimitMessage_NamesTheLimitAndWhenItResets(string scope, string period, bool tokens, long max, int role, string expected)
    {
        var limit = new SystemAiConfigService.TrippedLimitInfo(
            Enum.Parse<SystemAiConfigService.LimitPeriod>(period), tokens, max, role);

        string message = SystemAiConfigService.LimitMessage(Enum.Parse<SystemAiConfigService.LimitScope>(scope), limit);

        Assert.Equal(expected, message);
    }

    // -- Usage counters ----------------------------------------------------------------------------

    /// <summary>Chat requests D/M/T, title requests D/M/T, chat tokens D/M/T, title tokens D/M/T.</summary>
    private static long[] Counters(IRateLimitedEntity e) => new long[]
    {
        e.DailyChatRequestsCount, e.MonthlyChatRequestsCount, e.TotalChatRequestsCount,
        e.DailyTitleRequestsCount, e.MonthlyTitleRequestsCount, e.TotalTitleRequestsCount,
        e.DailyChatTokensCount, e.MonthlyChatTokensCount, e.TotalChatTokensCount,
        e.DailyTitleTokensCount, e.MonthlyTitleTokensCount, e.TotalTitleTokensCount
    };

    private static async Task<(ApplicationDbContext Db, SystemAiApiConfiguration Config, UserSystemAiApiConfiguration User, GroupSystemAiApiConfiguration Group)> SeedUsageAsync()
    {
        var db = NewInMemoryDb();
        AddUsers(db);
        var config = NewConfig("Counted", systemWide: true);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var group = await AddGroupWithUserAsync(db);
        var user = new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 7,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        };
        var groupAssignment = new GroupSystemAiApiConfiguration
        {
            GroupId = group.Id, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 7,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        };
        db.UserSystemAiApiConfigurations.Add(user);
        db.GroupSystemAiApiConfigurations.Add(groupAssignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (db, config, user, groupAssignment);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task RecordUsage_ForAGnollBenchRole_LeavesEveryCounterUnchanged(int role)
    {
        var (db, config, user, group) = await SeedUsageAsync();
        using var _ = db;

        await NewConfigService(db).RecordUsageAsync(config.Id, UserId, inputTokens: 100, outputTokens: 50, roleContext: role);

        Assert.All(Counters(config), c => Assert.Equal(0L, c));
        Assert.All(Counters(user), c => Assert.Equal(0L, c));
        Assert.All(Counters(group), c => Assert.Equal(0L, c));

        // The call is still logged.
        var log = Assert.Single(await db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(role, log.RoleContext);
        Assert.Equal(100, log.InputTokens);
        Assert.Equal(50, log.OutputTokens);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RecordUsage_ForAChatRole_IncrementsTheChatCountersOfConfigurationUserAndGroup(int role)
    {
        var (db, config, user, group) = await SeedUsageAsync();
        using var _ = db;

        await NewConfigService(db).RecordUsageAsync(config.Id, UserId, inputTokens: 100, outputTokens: 50, roleContext: role);

        var expected = new long[] { 1, 1, 1, 0, 0, 0, 150, 150, 150, 0, 0, 0 };
        Assert.Equal(expected, Counters(config));
        Assert.Equal(expected, Counters(user));
        Assert.Equal(expected, Counters(group));
    }

    [Fact]
    public async Task RecordUsage_ForTheTitleRole_IncrementsTheTitleCountersOfConfigurationUserAndGroup()
    {
        var (db, config, user, group) = await SeedUsageAsync();
        using var _ = db;

        await NewConfigService(db).RecordUsageAsync(config.Id, UserId, inputTokens: 100, outputTokens: 50, roleContext: 2);

        var expected = new long[] { 0, 0, 0, 1, 1, 1, 0, 0, 0, 150, 150, 150 };
        Assert.Equal(expected, Counters(config));
        Assert.Equal(expected, Counters(user));
        Assert.Equal(expected, Counters(group));
    }

    // -- Chat fallback -----------------------------------------------------------------------------

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("data: {}\n\n")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class StubClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, ErrorMessage = "no client" });
    }

    /// <summary>A provider that answers every stream with one chunk and remembers the model and key it was given.</summary>
    private sealed class CapturingProvider : IAiProvider
    {
        public string ProviderName { get; set; } = "OpenAI";
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };
        public string? LastModelId { get; private set; }
        public string? LastApiKey { get; private set; }

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            LastModelId = modelId;
            return new Dictionary<string, object>();
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name, description, parameterSchema };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) => LastApiKey = apiKey;
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.stream.test";

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = "stub answer" };
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => messages;
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.stream.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Stub Title";
    }

    /// <summary>The container EphemeralTurnTests drives a turn with, plus SettingsService and the recorder.</summary>
    private ServiceProvider BuildChatContainer(string databaseName, RecordingSecurityEvents events)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                { "PrivacySettings:ActiveKeyVersion", null }
            })
            .Build();

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        services.AddLogging();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, StubClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddSingleton<IAiProvider>(new CapturingProvider { ProviderName = "StubProvider" });
        services.AddScoped<AgentLoopRunner>();
        services.AddSingleton<ParallelExecutionResolver>();
        services.AddSingleton<AttachmentValidator>();
        services.AddSingleton<IAntiMalwareScanner, NullAntiMalwareScanner>();
        services.AddSingleton<EndpointPolicy>();
        services.AddSingleton<ConfidentialityPostureService>();
        services.AddSingleton<IContentKeyRing, ConfigurationContentKeyRing>();
        services.AddSingleton<ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), logger: null, startSweeper: false));
        services.AddScoped<ModelPricingService>();
        services.AddScoped<SettingsService>();
        services.AddSingleton<ISecurityEventRecorder>(events);
        services.AddScoped<SystemAiConfigService>();
        services.AddScoped<ChatService>();
        return services.BuildServiceProvider();
    }

    /// <summary>The user, with no model of their own, and the given system configurations.</summary>
    private static async Task SeedChatUserAsync(ServiceProvider container, params SystemAiApiConfiguration[] configs)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "limit-user" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = UserId, EnableToolUse = false });
        db.SystemAiApiConfigurations.AddRange(configs);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            SessionRef.Persistent(1), "hello", null, UserId, false, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }

    [Fact]
    public async Task ChatFallback_UsesTheNextResolvedModel_WhenTheFirstIsRefused()
    {
        var events = new RecordingSecurityEvents();
        using var container = BuildChatContainer(Guid.NewGuid().ToString(), events);
        var refused = NewConfig("Refused", systemWide: true, orderIndex: 0);
        refused.Provider = "StubProvider";
        refused.MaxDailyChatRequests = 1;
        refused.DailyChatRequestsCount = 1;
        // The next model has no key, so the turn stops at the key check and names the provider it chose.
        var next = NewConfig("Next", systemWide: true, orderIndex: 1);
        next.Provider = "SecondProvider";
        await SeedChatUserAsync(container, refused, next);

        var turn = await RunTurnAsync(container);

        var error = Assert.Single(turn, e => e.Type == "error");
        Assert.StartsWith("Error: API Key not configured for SecondProvider.", error.Data);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.QuotaRefused, entry.Kind);
        Assert.Equal("system", entry.Code);
        Assert.Equal(refused.Id, entry.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task ChatFallback_WhenEveryResolvedModelIsRefused_ReportsTheLastRefusal()
    {
        var events = new RecordingSecurityEvents();
        using var container = BuildChatContainer(Guid.NewGuid().ToString(), events);
        var first = NewConfig("First", systemWide: true, orderIndex: 0);
        first.Provider = "StubProvider";
        first.MaxDailyChatRequests = 1;
        first.DailyChatRequestsCount = 1;
        var second = NewConfig("Second", systemWide: true, orderIndex: 1);
        second.Provider = "StubProvider";
        second.MaxDailyChatRequests = 2;
        second.DailyChatRequestsCount = 2;
        await SeedChatUserAsync(container, first, second);

        var turn = await RunTurnAsync(container);

        var error = Assert.Single(turn, e => e.Type == "error");
        Assert.Equal("Error: This model has reached its daily limit for all users (2 requests). It resets at 00:00 UTC.", error.Data);
        Assert.Equal(new[] { first.Id, second.Id }, events.Entries.Select(e => e.SystemAiApiConfigurationId!.Value));
        Assert.All(events.Entries, e => Assert.Equal(SecurityEventKind.QuotaRefused, e.Kind));
    }

    // -- Sub-agent model resolution ----------------------------------------------------------------

    private sealed class SingleAgentCatalog : SubAgentCatalogService
    {
        private readonly SubAgentDefinition _agent;

        public SingleAgentCatalog(IConfiguration config, SubAgentDefinition agent)
            : base(config, NullLogger<SubAgentCatalogService>.Instance)
        {
            _agent = agent;
        }

        public override SubAgentDefinition? GetSubAgent(string name)
            => string.Equals(name, _agent.Name, StringComparison.OrdinalIgnoreCase) ? _agent : base.GetSubAgent(name);

        public override IReadOnlyList<SubAgentDefinition> GetEnabledSubAgents() => new List<SubAgentDefinition> { _agent };
    }

    private const string SubAgentModelId = "gpt-5.6-system-custom";

    private static IConfiguration SubAgentConfiguration(Dictionary<string, string?>? overrides)
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var values = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
            { "AI:Provider", "OpenAI" },
            { "AI:Model", "gpt-5.6-luna" },
            { "AI:APIKey", "test-key" },
            { "DefaultMaxOutputTokens:OpenAI", "128000" }
        };
        if (overrides != null)
        {
            foreach (var kvp in overrides) values[kvp.Key] = kvp.Value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>The service set DelegateToSubAgentToolTests resolves a sub-agent with, plus the recorder.</summary>
    private static (DelegateToSubAgentTool Tool, ApplicationDbContext Db, CapturingProvider Provider, RecordingSecurityEvents Events, CryptoService Crypto) NewSubAgentSetup(
        IConfiguration config, SubAgentCatalogService? catalog = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var db = NewInMemoryDb();
        var crypto = new CryptoService(config);
        var provider = new CapturingProvider { ProviderName = "OpenAI" };
        var events = new RecordingSecurityEvents();
        var catalogService = catalog ?? new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();

        var clientBridge = new StubClientToolBridge();
        var handlers = new List<IToolHandler>();
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        services.AddSingleton(db);
        services.AddSingleton(crypto);
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISecurityEventRecorder>(events);
        services.AddSingleton<SystemAiConfigService>();
        services.AddSingleton<IAiProvider>(provider);
        services.AddSingleton(metadata);
        services.AddSingleton(toolRegistry);
        services.AddSingleton(toolExecutor);
        services.AddSingleton(TestKnowledgeBase.Empty);
        services.AddSingleton(catalogService);
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory());
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<EndpointPolicy>();
        services.AddSingleton<ConfidentialityPostureService>();
        services.AddSingleton<AgentLoopRunner>();
        services.AddScoped<ModelPricingService>();

        var sp = services.BuildServiceProvider();
        var tool = new DelegateToSubAgentTool(
            catalogService, manager, sp.GetRequiredService<IServiceScopeFactory>(), config, metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        return (tool, db, provider, events, crypto);
    }

    /// <summary>A system-wide chat configuration with an encrypted key; <paramref name="exhausted"/> trips its daily request limit.</summary>
    private static SystemAiApiConfiguration SubAgentConfig(CryptoService crypto, long id, string apiKey, int orderIndex, bool exhausted)
    {
        var (ciphertext, nonce, tag) = crypto.Encrypt(apiKey, "SYSTEM_API_KEY");
        return new SystemAiApiConfiguration
        {
            Id = id,
            DisplayName = "System " + id,
            Provider = "OpenAI",
            ModelId = SubAgentModelId,
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 3,
            OrderIndex = orderIndex,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag,
            MaxDailyChatRequests = exhausted ? 1 : null,
            DailyChatRequestsCount = exhausted ? 1 : 0,
            LastDailyReset = DateTime.UtcNow,
            LastMonthlyReset = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task SubAgent_AConfigurationRefusedAsTheActiveModel_IsNotRetriedInTheAssignedModelBranch()
    {
        var (tool, db, provider, events, crypto) = NewSubAgentSetup(SubAgentConfiguration(null));
        using var _ = db;
        db.SystemAiApiConfigurations.Add(SubAgentConfig(crypto, 401, "key-refused", orderIndex: 0, exhausted: true));
        db.SystemAiApiConfigurations.Add(SubAgentConfig(crypto, 402, "key-next", orderIndex: 1, exhausted: false));
        db.ChatSession.Add(new ChatSession { Id = 401, AspNetUserId = "user401", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext
        {
            SessionId = SessionRef.Persistent(401),
            ActiveSystemModelId = 401,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true
        };
        var parameters = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(parameters, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("key-next", provider.LastApiKey);
        // Checked once, in branch 2A; a second check in 2D would record a second refusal.
        var refusal = Assert.Single(events.Entries, e => e.Kind == SecurityEventKind.QuotaRefused);
        Assert.Equal(401, refusal.SystemAiApiConfigurationId);
        Assert.Equal("system", refusal.Code);
        Assert.Equal("user401", refusal.UserId);
    }

    [Fact]
    public async Task SubAgent_ARefusedPreferredConfiguration_IsSkipped_AndNotRetriedInTheAssignedModelBranch()
    {
        // No AI:APIKey, so branch 1.2 has no server key to use and resolution reaches branch 2D.
        var config = SubAgentConfiguration(new Dictionary<string, string?> { { "AI:APIKey", null } });
        var agent = new SubAgentDefinition
        {
            Name = "preferring_agent",
            DisplayName = "Preferring Agent",
            Instructions = "Answer briefly.",
            AllowedTools = new List<string> { "wiki_search" },
            IsEnabled = true,
            ModelPreference = new SubAgentModelPreference { Provider = "OpenAI", ModelId = SubAgentModelId }
        };
        var (tool, db, provider, events, crypto) = NewSubAgentSetup(config, new SingleAgentCatalog(config, agent));
        using var _ = db;
        db.SystemAiApiConfigurations.Add(SubAgentConfig(crypto, 411, "key-preferred", orderIndex: 0, exhausted: true));
        db.SystemAiApiConfigurations.Add(SubAgentConfig(crypto, 412, "key-next", orderIndex: 1, exhausted: false));
        db.ChatSession.Add(new ChatSession { Id = 411, AspNetUserId = "user411", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext
        {
            SessionId = SessionRef.Persistent(411),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true
        };
        var parameters = JsonDocument.Parse("{\"agent_name\":\"preferring_agent\",\"task\":\"do something\"}").RootElement;

        var result = await tool.ExecuteAsync(parameters, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("key-next", provider.LastApiKey);
        // Checked once, in branch 1.1.
        var refusal = Assert.Single(events.Entries, e => e.Kind == SecurityEventKind.QuotaRefused);
        Assert.Equal(411, refusal.SystemAiApiConfigurationId);
        Assert.Equal("system", refusal.Code);
    }
}
