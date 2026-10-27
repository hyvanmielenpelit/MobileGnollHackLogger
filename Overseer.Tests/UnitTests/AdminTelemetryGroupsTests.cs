using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Telemetry;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Per-group usage: the Users view's figures summed over each group's current members, a member of two groups counted in
/// both, the busiest hour summed across members, the configuration lines with the group's pooled counters, the own-key
/// line, the "No group" row, blocked members and refusals, key sources, and groups with no members or no usage.
/// </summary>
public class AdminTelemetryGroupsTests
{
    private static readonly DateTime D1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = D1.AddDays(1);
    private static readonly DateTime D3 = D1.AddDays(2);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static SystemAiUsageLog Usage(string userId, long configId, DateTime at, int roleContext = 1) => new()
    {
        SystemAiApiConfigurationId = configId,
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = "Anthropic",
        ModelId = "model-" + configId,
        ModelDisplayName = "Logged " + configId,
        RoleContext = roleContext,
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadInputTokens = 0,
        EstimatedCost = 0.01m,
        ThinkingLevel = string.Empty
    };

    private static UserKeyAiUsageLog KeyUsage(string userId, DateTime at) => new()
    {
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = "OpenAI",
        ModelId = "own-model",
        RoleContext = 1,
        ThinkingLevel = string.Empty,
        InputTokens = 50,
        OutputTokens = 5,
        EstimatedCost = 0.05m
    };

    private static SecurityEvent Event(string userId, SecurityEventKind kind, int count) => new()
    {
        AspNetUserId = userId,
        Kind = kind,
        Count = count,
        OccurredUtc = D1,
        LastOccurredUtc = D1
    };

    /// <summary>
    /// Red (1): alice, bob (blocked), dave (no usage). Blue (2): alice, carol. Empty (3): no members. erin is in no group.
    /// alice: configuration 1 ×2 at D1 10:00, configuration 3 ×1 at D1 11:00, one own-key request at D1 10:45.
    /// bob: configuration 1 ×3 at D1 10:00 and ×1 at D2 09:00. carol: configuration 2 ×1 at D2 09:00.
    /// erin: configuration 1 ×2 at D3. Every system request costs $0.01, the own-key one $0.05.
    /// Red grants configuration 1 (enabled, today's counters) and configuration 2 (disabled, yesterday's daily counter);
    /// Empty grants configuration 3.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        db.Users.AddRange(
            new ApplicationUser { Id = "alice", UserName = "alice" },
            new ApplicationUser { Id = "bob", UserName = "bob", OverseerBlockedUtc = D1 },
            new ApplicationUser { Id = "carol", UserName = "carol" },
            new ApplicationUser { Id = "dave", UserName = "dave" },
            new ApplicationUser { Id = "erin", UserName = "erin" });

        db.SystemAiApiConfigurations.AddRange(
            new SystemAiApiConfiguration { Id = 1, DisplayName = "Config One", Provider = "Anthropic", ModelId = "model-1" },
            new SystemAiApiConfiguration { Id = 2, DisplayName = "Config Two", Provider = "Anthropic", ModelId = "model-2" },
            new SystemAiApiConfiguration { Id = 3, DisplayName = "Config Three", Provider = "Anthropic", ModelId = "model-3" });

        db.Groups.AddRange(
            new Group { Id = 1, DisplayName = "Red", CreatedAtUtc = D1 },
            new Group { Id = 2, DisplayName = "Blue", CreatedAtUtc = D1 },
            new Group { Id = 3, DisplayName = "Empty", CreatedAtUtc = D1 });
        db.UserGroups.AddRange(
            new UserGroup { AspNetUserId = "alice", GroupId = 1 },
            new UserGroup { AspNetUserId = "bob", GroupId = 1 },
            new UserGroup { AspNetUserId = "dave", GroupId = 1 },
            new UserGroup { AspNetUserId = "alice", GroupId = 2 },
            new UserGroup { AspNetUserId = "carol", GroupId = 2 });

        db.GroupSystemAiApiConfigurations.AddRange(
            new GroupSystemAiApiConfiguration
            {
                Id = 31, GroupId = 1, SystemAiApiConfigurationId = 1, IsEnabled = true, OrderIndex = 0,
                MaxDailyChatRequests = 100, MaxMonthlyChatRequests = 1000, MaxDailyChatTokens = 50_000,
                DailyChatRequestsCount = 7, MonthlyChatRequestsCount = 20, DailyChatTokensCount = 900,
                LastDailyReset = now, LastMonthlyReset = now
            },
            new GroupSystemAiApiConfiguration
            {
                Id = 32, GroupId = 1, SystemAiApiConfigurationId = 2, IsEnabled = false, OrderIndex = 1,
                MaxDailyChatRequests = 10, DailyChatRequestsCount = 4, MonthlyChatRequestsCount = 6, DailyChatTokensCount = 400,
                LastDailyReset = now.AddDays(-1), LastMonthlyReset = now
            },
            new GroupSystemAiApiConfiguration { Id = 33, GroupId = 3, SystemAiApiConfigurationId = 3, IsEnabled = true });

        db.SystemAiUsageLogs.AddRange(
            Usage("alice", 1, D1.AddHours(10)), Usage("alice", 1, D1.AddHours(10).AddMinutes(20)),
            Usage("alice", 3, D1.AddHours(11)),
            Usage("bob", 1, D1.AddHours(10).AddMinutes(5)), Usage("bob", 1, D1.AddHours(10).AddMinutes(15)),
            Usage("bob", 1, D1.AddHours(10).AddMinutes(25)), Usage("bob", 1, D2.AddHours(9), roleContext: 4),
            Usage("carol", 2, D2.AddHours(9)),
            Usage("erin", 1, D3), Usage("erin", 1, D3.AddMinutes(1)));
        db.UserKeyAiUsageLogs.Add(KeyUsage("alice", D1.AddHours(10).AddMinutes(45)));

        db.SecurityEvents.AddRange(
            Event("alice", SecurityEventKind.QuotaRefused, 2),
            Event("bob", SecurityEventKind.ProviderRefusal, 1),
            Event("bob", SecurityEventKind.RateLimitRejected, 1));

        db.OverseerDailyActivities.AddRange(
            new OverseerDailyActivity { DateUtc = DateOnly.FromDateTime(D1), AspNetUserId = "alice", ChatsCreated = 1, UserMessages = 3 },
            new OverseerDailyActivity { DateUtc = DateOnly.FromDateTime(D1), AspNetUserId = "bob", ChatsCreated = 2, UserMessages = 5 },
            new OverseerDailyActivity { DateUtc = DateOnly.FromDateTime(D2), AspNetUserId = "carol", ChatsCreated = 4, UserMessages = 4 });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<AiTelemetryGroupsDto> GroupsAsync(ApplicationDbContext db, string? keys = null) =>
        await Service(db).GetGroupsAsync(null, null, keys, TestContext.Current.CancellationToken);

    private static AiTelemetryGroupRowDto Row(AiTelemetryGroupsDto dto, string name) => Assert.Single(dto.Groups, g => g.GroupName == name);

    [Fact]
    public async Task GetGroups_SumsTheMembersFigures()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupsAsync(db);

        Assert.Equal("system", dto.KeySource);
        Assert.True(dto.SecurityEventsAvailable);
        var red = Row(dto, "Red");
        Assert.Equal(1, red.GroupId);
        Assert.Equal(3, red.MemberCount);
        Assert.Equal(2, red.ActiveMembers);
        Assert.Equal(7, red.Requests);
        Assert.Equal(7, red.SystemRequests);
        Assert.Equal(0, red.UserKeyRequests);
        Assert.Equal(6, red.ChatRequests);
        Assert.Equal(1, red.BenchmarkRequests);
        Assert.Equal(700, red.PromptTokens);
        Assert.Equal(70, red.OutputTokens);
        Assert.Equal(0.07m, red.EstimatedCost);
        Assert.Equal(0.07m, red.SystemEstimatedCost);
        Assert.Equal(7, red.PricedRequests);
        Assert.Equal(D2.AddHours(9), red.LastRequestUtc);
        Assert.Equal(3, red.ChatsCreated);
        Assert.Equal(8, red.UserMessages);
        Assert.Equal(2, red.ActiveDays);
    }

    [Fact]
    public async Task GetGroups_AMemberOfTwoGroupsCountsInBoth()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupsAsync(db);

        // alice's 3 requests are in Red and in Blue; Blue adds carol's 1.
        Assert.Equal(4, Row(dto, "Blue").Requests);
        Assert.Equal(2, Row(dto, "Blue").ActiveMembers);
        Assert.Equal(7, Row(dto, "Red").Requests);
    }

    [Fact]
    public async Task GetGroups_TheBusiestHourSumsTheMembersRequests()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var red = Row(await GroupsAsync(db), "Red");

        // alice 2 + bob 3 in D1 10:00; neither alone reaches 5.
        Assert.Equal(5, red.PeakHourRequests);
        Assert.Equal(D1.AddHours(10), red.PeakHourStartUtc);
        Assert.Equal(DateTimeKind.Utc, red.PeakHourStartUtc!.Value.Kind);
        // Seven requests over three hours with any member's request: D1 10:00, D1 11:00, D2 09:00.
        Assert.Equal(2.33, red.RequestsPerActiveHour);
    }

    [Fact]
    public async Task GetGroups_GrantedLinesCarryThePooledCounters_AndOtherLinesNone()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var red = Row(await GroupsAsync(db), "Red");

        Assert.Equal(new long?[] { 1, 3, 2 }, red.Configs.Select(c => c.ConfigId));
        Assert.All(red.Configs, c => Assert.Equal("system", c.KeySource));

        var one = red.Configs[0];
        Assert.Equal("Config One", one.ConfigName);
        // alice 2 and bob 4.
        Assert.Equal(6, one.Requests);
        Assert.Equal(0.06m, one.EstimatedCost);
        Assert.True(one.GrantedByGroup);
        Assert.Equal(31, one.AssignmentId);
        Assert.True(one.AssignmentEnabled);
        Assert.Equal(7, one.DailyChatRequests);
        Assert.Equal(100, one.MaxDailyChatRequests);
        Assert.Equal(20, one.MonthlyChatRequests);
        Assert.Equal(1000, one.MaxMonthlyChatRequests);
        Assert.Equal(900, one.DailyChatTokens);
        Assert.Equal(50_000, one.MaxDailyChatTokens);

        var three = red.Configs[1];
        Assert.Equal("Config Three", three.ConfigName);
        Assert.Equal(1, three.Requests);
        Assert.False(three.GrantedByGroup);
        Assert.Null(three.AssignmentId);
        Assert.Null(three.AssignmentEnabled);
        Assert.Null(three.DailyChatRequests);
        Assert.Null(three.MaxDailyChatRequests);
        Assert.Null(three.MonthlyChatRequests);
        Assert.Null(three.DailyChatTokens);
    }

    [Fact]
    public async Task GetGroups_ADisabledAssignmentIsListedWithItsEffectiveCounters()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var two = Assert.Single(Row(await GroupsAsync(db), "Red").Configs, c => c.ConfigId == 2);

        Assert.Equal("Config Two", two.ConfigName);
        Assert.Equal(0, two.Requests);
        Assert.True(two.GrantedByGroup);
        Assert.Equal(32, two.AssignmentId);
        Assert.False(two.AssignmentEnabled);
        // Last daily reset yesterday: the daily counters read 0; the monthly one stands.
        Assert.Equal(0, two.DailyChatRequests);
        Assert.Equal(0, two.DailyChatTokens);
        Assert.Equal(6, two.MonthlyChatRequests);
        Assert.Equal(10, two.MaxDailyChatRequests);
    }

    [Fact]
    public async Task GetGroups_TheOwnKeyLineAppearsUnderUserKeys()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var system = Row(await GroupsAsync(db), "Red");
        var user = Row(await GroupsAsync(db, keys: "user"), "Red");
        var both = Row(await GroupsAsync(db, keys: "both"), "Red");

        Assert.DoesNotContain(system.Configs, c => c.KeySource == "user");

        var own = Assert.Single(user.Configs);
        Assert.Equal("user", own.KeySource);
        Assert.Null(own.ConfigId);
        Assert.Equal(AiTelemetrySummaryService.OwnKeysLineName, own.ConfigName);
        Assert.Equal(1, own.Requests);
        Assert.Equal(0.05m, own.EstimatedCost);
        Assert.False(own.GrantedByGroup);
        Assert.Null(own.DailyChatRequests);

        Assert.Equal(new[] { ("system", (long?)1), ("system", 3), ("user", null), ("system", 2) },
            both.Configs.Select(c => (c.KeySource, c.ConfigId)));
    }

    [Fact]
    public async Task GetGroups_SystemLinesCarryTheirConfigurationsBadgeSettings_AndTheOwnKeyLineNone()
    {
        using var db = NewDb();
        await SeedAsync(db);
        foreach (var config in await db.SystemAiApiConfigurations.ToListAsync(TestContext.Current.CancellationToken))
        {
            config.ThinkingLevel = "level-" + config.Id;
            config.ReasoningMode = "Summary";
            config.ServiceTier = "priority";
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var red = Row(await GroupsAsync(db, keys: "both"), "Red");

        // Configuration 1 granted and used, 2 granted only, 3 used only.
        foreach (long id in new long[] { 1, 2, 3 })
        {
            var line = Assert.Single(red.Configs, c => c.ConfigId == id);
            Assert.Equal("Anthropic", line.Provider);
            Assert.Equal("level-" + id, line.ThinkingLevel);
            Assert.Equal("Summary", line.ReasoningMode);
            Assert.Equal("priority", line.ServiceTier);
        }

        var own = Assert.Single(red.Configs, c => c.KeySource == "user");
        Assert.Null(own.Provider);
        Assert.Null(own.ThinkingLevel);
        Assert.Null(own.ReasoningMode);
        Assert.Null(own.ServiceTier);
    }

    [Fact]
    public async Task GetGroups_KeySourcesSelectTheLogs()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var user = Row(await GroupsAsync(db, keys: "user"), "Red");
        var both = Row(await GroupsAsync(db, keys: "both"), "Red");

        Assert.Equal(1, user.Requests);
        Assert.Equal(1, user.UserKeyRequests);
        Assert.Equal(0.05m, user.EstimatedCost);
        Assert.Equal(1, user.ActiveMembers);
        Assert.Equal(8, both.Requests);
        Assert.Equal(7, both.SystemRequests);
        Assert.Equal(1, both.UserKeyRequests);
        Assert.Equal(0.12m, both.EstimatedCost);
        Assert.Equal(0.05m, both.UserKeyEstimatedCost);
        // The own-key request at D1 10:45 joins the busiest hour.
        Assert.Equal(6, both.PeakHourRequests);
    }

    [Fact]
    public async Task GetGroups_SumsBlockedMembersAndRefusals()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupsAsync(db);
        var red = Row(dto, "Red");
        var blue = Row(dto, "Blue");

        Assert.Equal(1, red.BlockedMembers);
        // alice: QuotaRefused 2; bob: RateLimitRejected 1 and ProviderRefusal 1.
        Assert.Equal(3, red.LimitRefusals);
        Assert.Equal(1, red.ProviderRefusals);
        Assert.Equal(0, blue.BlockedMembers);
        Assert.Equal(2, blue.LimitRefusals);
        Assert.Equal(0, blue.ProviderRefusals);
    }

    [Fact]
    public async Task GetGroups_TheNoGroupRowHoldsTheUngroupedUsers()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var none = Assert.Single((await GroupsAsync(db)).Groups, g => g.GroupId == null);

        Assert.Equal(AiTelemetrySummaryService.NoGroupName, none.GroupName);
        Assert.Equal(1, none.MemberCount);
        Assert.Equal(1, none.ActiveMembers);
        Assert.Equal(2, none.Requests);
        Assert.Equal(0.02m, none.EstimatedCost);
        var line = Assert.Single(none.Configs);
        Assert.Equal(1, line.ConfigId);
        Assert.False(line.GrantedByGroup);
        Assert.Null(line.AssignmentId);
        Assert.Null(line.DailyChatRequests);
    }

    [Fact]
    public async Task GetGroups_ListsAGroupWithNoMembers_WithZerosAndItsGrants()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var empty = Row(await GroupsAsync(db), "Empty");

        Assert.Equal(0, empty.MemberCount);
        Assert.Equal(0, empty.ActiveMembers);
        Assert.Equal(0, empty.Requests);
        Assert.Equal(0m, empty.EstimatedCost);
        Assert.Equal(0, empty.PeakHourRequests);
        Assert.Null(empty.PeakHourStartUtc);
        Assert.Null(empty.LastRequestUtc);
        var grant = Assert.Single(empty.Configs);
        Assert.Equal(3, grant.ConfigId);
        Assert.True(grant.GrantedByGroup);
        Assert.Equal(0, grant.Requests);
    }

    [Fact]
    public async Task GetGroups_OrdersByCostThenName_AndListsEveryGroup()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupsAsync(db);

        // Red $0.07, Blue $0.04, No group $0.02, Empty $0.
        Assert.Equal(new[] { "Red", "Blue", AiTelemetrySummaryService.NoGroupName, "Empty" }, dto.Groups.Select(g => g.GroupName));
    }

    [Fact]
    public async Task GetGroups_WithNoGroupsAtAll_ListsOnlyNoGroup()
    {
        using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "solo", UserName = "solo" });
        db.SystemAiUsageLogs.Add(Usage("solo", 1, D1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = await GroupsAsync(db);

        var none = Assert.Single(dto.Groups);
        Assert.Null(none.GroupId);
        Assert.Equal(1, none.MemberCount);
        Assert.Equal(1, none.Requests);
    }

    // --- Endpoint ------------------------------------------------------------------------------

    private static AdminController CreateController(ApplicationDbContext db)
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
                { "AiRateLimitSettings:MaxConcurrentModelCalls", "2" },
                { "AiRateLimitSettings:MaxRetryAfterSeconds", "90" }
            })
            .Build();

        var usageGuard = new SystemConfigUsageGuard(
            db,
            new Overseer.Services.Benchmarking.BenchmarkDifficultyJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkGenerationJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricCheckJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricGapAuthorJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkReportPackJobManager());
        return new AdminController(db, config, null!, new CryptoService(config),
            new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance),
            new Overseer.Services.Privacy.EndpointPolicy(config), usageGuard,
            new ModelPricingService(new ModelMetadataService(), db));
    }

    [Fact]
    public async Task GetAiTelemetryGroups_PassesTheKeys()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var result = await CreateController(db).GetAiTelemetryGroups(null, null, "both", Service(db));

        var dto = Assert.IsType<AiTelemetryGroupsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("both", dto.KeySource);
        Assert.Equal(8, Row(dto, "Red").Requests);
    }
}
