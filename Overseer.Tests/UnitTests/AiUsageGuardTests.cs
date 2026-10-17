using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The per-user daily ceiling on operator-funded AI use: today's chat, title and sub-agent rows count toward
/// the cost ceiling, chat and sub-agent rows toward the run cap, GnollBench and yesterday's rows do not,
/// administrators are exempt while configured so, 0 turns a control off, and system configurations reached
/// through a group or system-wide access are refused while one through an enabled user assignment is not.
/// </summary>
public class AiUsageGuardTests
{
    private const string UserId = "guard-user";
    private const string UserName = "guard-player";
    private const string OtherUserId = "guard-other";
    private const string AdminUserId = "guard-admin";
    private const string AdminUserName = "guard-boss";

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        private readonly ConcurrentQueue<SecurityEventEntry> _entries = new();

        public IReadOnlyList<SecurityEventEntry> Entries => _entries.ToArray();

        public void Record(SecurityEventEntry entry) => _entries.Enqueue(entry);
    }

    private static ApplicationDbContext NewInMemoryDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserName, Email = "guard-user@example.com" });
        db.Users.Add(new ApplicationUser { Id = OtherUserId, UserName = "guard-other", Email = "guard-other@example.com" });
        db.Users.Add(new ApplicationUser { Id = AdminUserId, UserName = AdminUserName, Email = "guard-admin@example.com" });
        db.SaveChanges();
        return db;
    }

    private static IConfiguration AdminConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { { "Admins", "someone-else, " + AdminUserName } })
        .Build();

    private static AiUsageGuardOptions GuardOptions(decimal maxCost = 5m, int maxRuns = 0, bool exemptAdmins = true) => new()
    {
        MaxUserDailyCostUsd = maxCost,
        MaxUserDailyRuns = maxRuns,
        ExemptAdmins = exemptAdmins
    };

    private static AiUsageGuard NewGuard(ApplicationDbContext db, AiUsageGuardOptions options, ISecurityEventRecorder? events = null)
        => new(db, Microsoft.Extensions.Options.Options.Create(options), AdminConfiguration(), events);

    /// <summary>Today's rows are stamped now; with the guard's day starting at 00:00 UTC they are always inside it.</summary>
    private static void AddUsage(ApplicationDbContext db, string userId, int role, decimal? cost, DateTime? at = null, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            db.SystemAiUsageLogs.Add(new SystemAiUsageLog
            {
                SystemAiApiConfigurationId = 1,
                AspNetUserId = userId,
                TimestampUtc = at ?? DateTime.UtcNow,
                Provider = "OpenAI",
                ModelId = "gpt-guard",
                RoleContext = role,
                EstimatedCost = cost
            });
        }
        db.SaveChanges();
    }

    private static DateTime Yesterday => DateTime.UtcNow.Date.AddHours(-1);

    // -- The guard itself ---------------------------------------------------------------------------

    [Fact]
    public async Task UnderTheCostCeiling_IsAllowed()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: 2.50m);
        AddUsage(db, UserId, role: 3, cost: 2.49m);
        AddUsage(db, UserId, role: 1, cost: null);
        AddUsage(db, OtherUserId, role: 1, cost: 100m);
        var events = new RecordingSecurityEvents();

        var result = await NewGuard(db, GuardOptions(maxCost: 5m), events).CheckAsync(UserId, 7, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(events.Entries);
    }

    [Fact]
    public async Task AtTheCostCeiling_IsRefused_AndRecordedAsGuardCost()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: 3m);
        AddUsage(db, UserId, role: 2, cost: 1m);
        AddUsage(db, UserId, role: 3, cost: 1m);
        var events = new RecordingSecurityEvents();

        var result = await NewGuard(db, GuardOptions(maxCost: 5m), events).CheckAsync(UserId, 7, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Equal(AiUsageGuard.RefusalMessage, result);
        Assert.Equal("You have reached today's limit for AI use in the Overseer. It resets at 00:00 UTC.", result);
        var entry = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.QuotaRefused, entry.Kind);
        Assert.Equal("guard-cost", entry.Code);
        Assert.Equal(UserId, entry.UserId);
        Assert.Equal(7, entry.SystemAiApiConfigurationId);
        Assert.Equal("OpenAI", entry.Provider);
        Assert.Equal("gpt-guard", entry.ModelId);
    }

    [Fact]
    public async Task AtTheRunCap_IsRefused_AndRecordedAsGuardRuns_WhileTitleRowsAreNotRuns()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: null);
        AddUsage(db, UserId, role: 2, cost: null, count: 5);
        var events = new RecordingSecurityEvents();
        var guard = NewGuard(db, GuardOptions(maxCost: 0m, maxRuns: 2), events);

        Assert.Null(await guard.CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken));

        AddUsage(db, UserId, role: 3, cost: null);
        var result = await guard.CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Equal(AiUsageGuard.RefusalMessage, result);
        var entry = Assert.Single(events.Entries);
        Assert.Equal("guard-runs", entry.Code);
        Assert.Null(entry.SystemAiApiConfigurationId);
    }

    [Fact]
    public async Task GnollBenchRows_AreNotCounted()
    {
        using var db = NewInMemoryDb();
        foreach (int role in new[] { 4, 5, 6, 7, 8 })
        {
            AddUsage(db, UserId, role, cost: 100m, count: 3);
        }

        var result = await NewGuard(db, GuardOptions(maxCost: 5m, maxRuns: 2)).CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task YesterdaysRows_AreNotCounted()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: 100m, at: Yesterday, count: 3);

        var result = await NewGuard(db, GuardOptions(maxCost: 5m, maxRuns: 2)).CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnAdministrator_IsExempt()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, AdminUserId, role: 1, cost: 100m, count: 3);
        var events = new RecordingSecurityEvents();

        var result = await NewGuard(db, GuardOptions(maxCost: 5m, maxRuns: 2), events).CheckAsync(AdminUserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(events.Entries);
    }

    [Fact]
    public async Task AnAdministrator_IsNotExempt_WhenExemptAdminsIsOff()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, AdminUserId, role: 1, cost: 100m);

        var result = await NewGuard(db, GuardOptions(maxCost: 5m, exemptAdmins: false)).CheckAsync(AdminUserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Equal(AiUsageGuard.RefusalMessage, result);
    }

    [Fact]
    public async Task ZeroTurnsTheCostCeilingOff()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: 1000m);

        var result = await NewGuard(db, GuardOptions(maxCost: 0m, maxRuns: 0)).CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ZeroTurnsTheRunCapOff()
    {
        using var db = NewInMemoryDb();
        AddUsage(db, UserId, role: 1, cost: null, count: 50);

        var result = await NewGuard(db, GuardOptions(maxCost: 5m, maxRuns: 0)).CheckAsync(UserId, null, "OpenAI", "gpt-guard", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // -- Through SystemAiConfigService --------------------------------------------------------------

    private static SystemAiConfigService NewConfigService(ApplicationDbContext db, AiUsageGuard guard)
        => new(db, NullLogger<SystemAiConfigService>.Instance, usageGuard: guard);

    private static async Task<SystemAiApiConfiguration> AddConfigAsync(ApplicationDbContext db, bool systemWide)
    {
        var config = new SystemAiApiConfiguration
        {
            DisplayName = "Guarded",
            Provider = "OpenAI",
            ModelId = "gpt-guarded",
            IsEnabled = true,
            IsSystemWide = systemWide,
            ModelRole = 3,
            LastDailyReset = DateTime.UtcNow,
            LastMonthlyReset = DateTime.UtcNow
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config;
    }

    [Fact]
    public async Task SystemWideAccess_IsRefused_WhenOverTheCeiling()
    {
        using var db = NewInMemoryDb();
        var config = await AddConfigAsync(db, systemWide: true);
        AddUsage(db, UserId, role: 1, cost: 10m);
        var events = new RecordingSecurityEvents();
        var service = NewConfigService(db, NewGuard(db, GuardOptions(maxCost: 5m), events));

        var (allowed, message) = await service.GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal(AiUsageGuard.RefusalMessage, message);
        var entry = Assert.Single(events.Entries);
        Assert.Equal("guard-cost", entry.Code);
        Assert.Equal(config.Id, entry.SystemAiApiConfigurationId);
        Assert.Equal("gpt-guarded", entry.ModelId);
    }

    [Fact]
    public async Task SystemWideAccess_IsAllowed_WhenUnderTheCeiling()
    {
        using var db = NewInMemoryDb();
        var config = await AddConfigAsync(db, systemWide: true);
        AddUsage(db, UserId, role: 1, cost: 1m);
        var service = NewConfigService(db, NewGuard(db, GuardOptions(maxCost: 5m)));

        var (allowed, message) = await service.GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.NotNull(allowed);
        Assert.Null(message);
    }

    [Fact]
    public async Task AnEnabledUserAssignment_IsNotRefused()
    {
        using var db = NewInMemoryDb();
        var config = await AddConfigAsync(db, systemWide: true);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            AspNetUserId = UserId, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        AddUsage(db, UserId, role: 1, cost: 10m);
        var service = NewConfigService(db, NewGuard(db, GuardOptions(maxCost: 5m)));

        var (allowed, message) = await service.GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.NotNull(allowed);
        Assert.Null(message);
    }

    [Fact]
    public async Task GroupOnlyAccess_IsRefused_WhenOverTheCeiling()
    {
        using var db = NewInMemoryDb();
        var config = await AddConfigAsync(db, systemWide: false);
        var group = new Group { DisplayName = "Guarded testers", CreatedAtUtc = DateTime.UtcNow };
        db.Groups.Add(group);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.UserGroups.Add(new UserGroup { AspNetUserId = UserId, GroupId = group.Id });
        db.GroupSystemAiApiConfigurations.Add(new GroupSystemAiApiConfiguration
        {
            GroupId = group.Id, SystemAiApiConfigurationId = config.Id, IsEnabled = true, ModelRole = 3,
            LastDailyReset = DateTime.UtcNow, LastMonthlyReset = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        AddUsage(db, UserId, role: 1, cost: 10m);
        var service = NewConfigService(db, NewGuard(db, GuardOptions(maxCost: 5m)));

        var (allowed, message) = await service.GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 1);

        Assert.Null(allowed);
        Assert.Equal(AiUsageGuard.RefusalMessage, message);
    }

    [Fact]
    public async Task ATitleCheck_IsNotRefused()
    {
        using var db = NewInMemoryDb();
        var config = await AddConfigAsync(db, systemWide: true);
        AddUsage(db, UserId, role: 1, cost: 10m);
        var events = new RecordingSecurityEvents();
        var service = NewConfigService(db, NewGuard(db, GuardOptions(maxCost: 5m), events));

        var (allowed, message) = await service.GetAndCheckSystemConfigAsync(config.Id, UserId, requiredRoleFilter: 2);

        Assert.NotNull(allowed);
        Assert.Null(message);
        Assert.Empty(events.Entries);
    }

    [Fact]
    public async Task AServerKeyUsageRow_IsWrittenAsRole3WithConfigurationIdZero_AndCountedByTheGuard()
    {
        using var db = NewInMemoryDb();
        var service = new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance);

        await service.RecordServerKeyUsageAsync(UserId, "OpenAI", "gpt-server", 1200, 300, 6m, "catalog", TestContext.Current.CancellationToken);

        var row = Assert.Single(await db.SystemAiUsageLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, row.SystemAiApiConfigurationId);
        Assert.Equal(3, row.RoleContext);
        Assert.Equal(UserId, row.AspNetUserId);
        Assert.Equal("OpenAI", row.Provider);
        Assert.Equal("gpt-server", row.ModelId);
        Assert.Equal("Server key: gpt-server", row.ModelDisplayName);
        Assert.Equal(1200, row.InputTokens);
        Assert.Equal(300, row.OutputTokens);
        Assert.Equal(6m, row.EstimatedCost);
        Assert.Equal("catalog", row.PricingSource);

        Assert.Equal(AiUsageGuard.RefusalMessage,
            await NewGuard(db, GuardOptions(maxCost: 5m)).CheckAsync(UserId, null, "OpenAI", "gpt-server", TestContext.Current.CancellationToken));
        Assert.Equal(AiUsageGuard.RefusalMessage,
            await NewGuard(db, GuardOptions(maxCost: 0m, maxRuns: 1)).CheckAsync(UserId, null, "OpenAI", "gpt-server", TestContext.Current.CancellationToken));
    }
}
