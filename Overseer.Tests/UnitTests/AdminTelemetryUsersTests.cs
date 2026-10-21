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
/// Per-user usage of system AI configurations and own keys: totals and their split by key source, per-configuration
/// rows with effective counters and the limit source, the peak hour, refusal counts, flags, search, filter, sorting
/// and paging.
/// </summary>
public class AdminTelemetryUsersTests
{
    private const string Alice = "u-alice";
    private const string Bob = "u-bob";

    private static readonly DateTime D1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = D1.AddDays(1);
    private static readonly DateTime D3 = D1.AddDays(2);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static SystemAiUsageLog Usage(string userId, long configId, DateTime at, int roleContext = 1, int input = 100,
        int output = 10, decimal? cost = 0.01m) => new()
    {
        SystemAiApiConfigurationId = configId,
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = "Anthropic",
        ModelId = "model-" + configId,
        ModelDisplayName = "Logged " + configId,
        RoleContext = roleContext,
        InputTokens = input,
        OutputTokens = output,
        CacheReadInputTokens = 0,
        EstimatedCost = cost,
        ThinkingLevel = string.Empty
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
    /// Alice: 4 requests in one hour on configuration 1 (D1 10:00), 3 spread over three hours on configuration 2 (D2),
    /// $0.07, 770 tokens, last D2 16:00, 7 refusals. Configuration 1 by her own assignment (yesterday's daily
    /// counter), configuration 2 through her group.
    /// Bob: 2 benchmark requests in one hour (D3 09:00) on configurations 3 (system-wide) and 4 (unassigned), $1.00,
    /// 10,200 tokens, no refusals, blocked.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        db.Users.AddRange(
            new ApplicationUser { Id = Alice, UserName = "alice", Email = "Alice@Example.com" },
            new ApplicationUser { Id = Bob, UserName = "bob", Email = "bob@sample.org", OverseerBlockedUtc = D1 },
            new ApplicationUser { Id = "u-idle", UserName = "idle", Email = "idle@example.com" });

        db.SystemAiApiConfigurations.AddRange(
            new SystemAiApiConfiguration { Id = 1, DisplayName = "Config One", Provider = "Anthropic", ModelId = "model-1" },
            new SystemAiApiConfiguration { Id = 2, DisplayName = "Config Two", Provider = "Anthropic", ModelId = "model-2" },
            new SystemAiApiConfiguration { Id = 3, DisplayName = "Config Three", Provider = "Anthropic", ModelId = "model-3", IsSystemWide = true },
            new SystemAiApiConfiguration { Id = 4, DisplayName = "Config Four", Provider = "Anthropic", ModelId = "model-4" });

        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            Id = 11,
            AspNetUserId = Alice,
            SystemAiApiConfigurationId = 1,
            IsEnabled = true,
            MaxDailyChatRequests = 50,
            MaxMonthlyChatRequests = 500,
            MaxDailyChatTokens = 100_000,
            DailyChatRequestsCount = 5,
            DailyChatTokensCount = 500,
            MonthlyChatRequestsCount = 9,
            LastDailyReset = now.AddDays(-1),
            LastMonthlyReset = now
        });

        db.Groups.Add(new Group { Id = 7, DisplayName = "Testers", CreatedAtUtc = D1 });
        db.UserGroups.Add(new UserGroup { AspNetUserId = Alice, GroupId = 7 });
        db.GroupSystemAiApiConfigurations.Add(new GroupSystemAiApiConfiguration { Id = 21, GroupId = 7, SystemAiApiConfigurationId = 2, IsEnabled = true });

        for (int i = 0; i < 4; i++)
        {
            db.SystemAiUsageLogs.Add(Usage(Alice, 1, D1.AddHours(10).AddMinutes(i * 10)));
        }
        foreach (int hour in new[] { 8, 12, 16 })
        {
            db.SystemAiUsageLogs.Add(Usage(Alice, 2, D2.AddHours(hour)));
        }

        db.SystemAiUsageLogs.Add(Usage(Bob, 3, D3.AddHours(9), roleContext: 4, input: 5000, output: 100, cost: 0.50m));
        db.SystemAiUsageLogs.Add(Usage(Bob, 4, D3.AddHours(9).AddMinutes(30), roleContext: 4, input: 5000, output: 100, cost: 0.50m));

        db.SecurityEvents.AddRange(
            Event(Alice, SecurityEventKind.QuotaRefused, 2),
            Event(Alice, SecurityEventKind.RateLimitRejected, 1),
            Event(Alice, SecurityEventKind.ProviderRefusal, 1),
            Event(Alice, SecurityEventKind.ProviderSafetyBlock, 2),
            Event(Alice, SecurityEventKind.ProviderPolicyRejection, 1),
            Event(Alice, SecurityEventKind.LoginFailed, 9));

        db.OverseerDailyActivities.Add(new OverseerDailyActivity
        {
            DateUtc = DateOnly.FromDateTime(D1), AspNetUserId = Alice, ChatsCreated = 2, UserMessages = 6
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<AiTelemetryUsersPageDto> UsersAsync(ApplicationDbContext db, string? search = null, string? userId = null,
        string? sort = null, int skip = 0, int take = 20, string? keys = null, string? direction = null) =>
        await Service(db).GetUsersAsync(null, null, search, userId, sort, skip, take, keys, direction,
            ct: TestContext.Current.CancellationToken);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

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

        var cryptoService = new CryptoService(config);
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var metadataService = new ModelMetadataService();
        var pricingService = new ModelPricingService(metadataService, db);
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);
        var usageGuard = new SystemConfigUsageGuard(
            db,
            new Overseer.Services.Benchmarking.BenchmarkDifficultyJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkGenerationJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricCheckJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricGapAuthorJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkReportPackJobManager());
        return new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, usageGuard, pricingService);
    }

    [Fact]
    public async Task GetUsers_ReportsTotalsAndPerConfigurationRows()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db);

        Assert.Equal(2, page.TotalUsers);
        Assert.True(page.SecurityEventsAvailable);
        Assert.DoesNotContain(page.Users, u => u.UserId == "u-idle");

        var alice = Assert.Single(page.Users, u => u.UserId == Alice);
        Assert.Equal("alice", alice.UserName);
        Assert.Equal(7, alice.Requests);
        Assert.Equal(7, alice.ChatRequests);
        Assert.Equal(0, alice.BenchmarkRequests);
        Assert.Equal(700, alice.PromptTokens);
        Assert.Equal(70, alice.OutputTokens);
        Assert.Equal(0.07m, alice.EstimatedCost);
        Assert.Equal(7, alice.PricedRequests);
        Assert.Equal(D2.AddHours(16), alice.LastRequestUtc);
        Assert.Equal(2, alice.ChatsCreated);
        Assert.Equal(6, alice.UserMessages);

        Assert.Equal(new long[] { 1, 2 }, alice.Configs.Select(c => c.ConfigId));
        var one = alice.Configs[0];
        Assert.Equal("Config One", one.ConfigName);
        Assert.Equal(4, one.Requests);
        Assert.Equal(0.04m, one.EstimatedCost);
        Assert.Equal("Config Two", alice.Configs[1].ConfigName);
        Assert.Equal(3, alice.Configs[1].Requests);

        var bob = Assert.Single(page.Users, u => u.UserId == Bob);
        Assert.Equal(2, bob.Requests);
        Assert.Equal(2, bob.BenchmarkRequests);
        Assert.Equal(10_000, bob.PromptTokens);
        Assert.Equal(1.00m, bob.EstimatedCost);
        Assert.Equal(new long[] { 3, 4 }, bob.Configs.Select(c => c.ConfigId).OrderBy(id => id));
    }

    [Fact]
    public async Task GetUsers_PeakHourBeatsSpreadOutRequests()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var alice = Assert.Single((await UsersAsync(db)).Users, u => u.UserId == Alice);

        Assert.Equal(4, alice.PeakHourRequests);
        Assert.Equal(D1.AddHours(10), alice.PeakHourStartUtc);
        Assert.Equal(DateTimeKind.Utc, alice.PeakHourStartUtc!.Value.Kind);
        // Seven requests over four active hours.
        Assert.Equal(1.75, alice.RequestsPerActiveHour);
        Assert.Equal(2, alice.ActiveDays);
    }

    [Fact]
    public async Task GetUsers_ReportsEffectiveCountersAndTheLimitSource()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db);
        var alice = Assert.Single(page.Users, u => u.UserId == Alice);

        var own = Assert.Single(alice.Configs, c => c.ConfigId == 1);
        Assert.Equal("user", own.LimitSource);
        Assert.Equal(11, own.AssignmentId);
        Assert.True(own.AssignmentEnabled);
        // Last reset yesterday: the next request would see a daily counter of 0.
        Assert.Equal(0, own.DailyChatRequests);
        Assert.Equal(0, own.DailyChatTokens);
        Assert.Equal(9, own.MonthlyChatRequests);
        Assert.Equal(50, own.MaxDailyChatRequests);
        Assert.Equal(500, own.MaxMonthlyChatRequests);
        Assert.Equal(100_000, own.MaxDailyChatTokens);

        var group = Assert.Single(alice.Configs, c => c.ConfigId == 2);
        Assert.Equal("group", group.LimitSource);
        Assert.Null(group.AssignmentId);
        Assert.Null(group.DailyChatRequests);

        var bob = Assert.Single(page.Users, u => u.UserId == Bob);
        Assert.Equal("systemWide", Assert.Single(bob.Configs, c => c.ConfigId == 3).LimitSource);
        Assert.Equal("none", Assert.Single(bob.Configs, c => c.ConfigId == 4).LimitSource);
    }

    [Fact]
    public async Task GetUsers_ADisabledAssignmentIsNotTheLimitSource()
    {
        using var db = NewDb();
        await SeedAsync(db);
        db.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
        {
            Id = 12, AspNetUserId = Bob, SystemAiApiConfigurationId = 3, IsEnabled = false
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var bob = Assert.Single((await UsersAsync(db)).Users, u => u.UserId == Bob);
        var three = Assert.Single(bob.Configs, c => c.ConfigId == 3);

        Assert.Equal("systemWide", three.LimitSource);
        Assert.Equal(12, three.AssignmentId);
        Assert.False(three.AssignmentEnabled);
    }

    [Fact]
    public async Task GetUsers_CountsRefusalsByKind()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db);
        var alice = Assert.Single(page.Users, u => u.UserId == Alice);
        var bob = Assert.Single(page.Users, u => u.UserId == Bob);

        // QuotaRefused 2 + RateLimitRejected 1; ProviderRefusal 1 + ProviderSafetyBlock 2 + ProviderPolicyRejection 1.
        Assert.Equal(3, alice.LimitRefusals);
        Assert.Equal(4, alice.ProviderRefusals);
        Assert.Equal(0, bob.LimitRefusals);
        Assert.Equal(0, bob.ProviderRefusals);
    }

    [Fact]
    public async Task GetUsers_FlagsABlockedUser()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db);

        Assert.True(Assert.Single(page.Users, u => u.UserId == Bob).IsOverseerBlocked);
        Assert.False(Assert.Single(page.Users, u => u.UserId == Alice).IsOverseerBlocked);
    }

    [Fact]
    public async Task GetUsers_FlagsAnActiveHoldOrAPendingDeletionRequest()
    {
        using var db = NewDb();
        foreach (var id in new[] { "h-hold", "h-pending", "h-none" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
            db.SystemAiUsageLogs.Add(Usage(id, 1, D1));
        }

        db.AccountHolds.AddRange(
            new AccountHold { AspNetUserId = "h-hold", Reason = "review", PlacedByUserId = "admin", PlacedUtc = D1 },
            new AccountHold { AspNetUserId = "h-none", Reason = "old", PlacedByUserId = "admin", PlacedUtc = D1, ReleasedUtc = D2, ReleasedByUserId = "admin" });
        db.AccountDeletionRequests.AddRange(
            new AccountDeletionRequest { AspNetUserId = "h-pending", RequestedUtc = D1, Status = AccountDeletionRequestStatus.Pending, HoldReasons = "held" },
            new AccountDeletionRequest { AspNetUserId = "h-none", RequestedUtc = D1, Status = AccountDeletionRequestStatus.Declined, HoldReasons = "held" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await UsersAsync(db);

        Assert.True(Assert.Single(page.Users, u => u.UserId == "h-hold").HasAccountHold);
        Assert.True(Assert.Single(page.Users, u => u.UserId == "h-pending").HasAccountHold);
        Assert.False(Assert.Single(page.Users, u => u.UserId == "h-none").HasAccountHold);
    }

    [Fact]
    public async Task GetUsers_SearchMatchesTheEmailCaseInsensitively()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db, search: "EXAMPLE.COM");

        Assert.Equal(1, page.TotalUsers);
        Assert.Equal(Alice, Assert.Single(page.Users).UserId);
    }

    [Fact]
    public async Task GetUsers_ASearchThatMatchesNobodyReturnsAnEmptyPage()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db, search: "nobody-at-all");

        Assert.Equal(0, page.TotalUsers);
        Assert.Empty(page.Users);
        Assert.True(page.SecurityEventsAvailable);
    }

    [Fact]
    public async Task GetUsers_TheUserIdFilterReturnsOneUser()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db, userId: Bob);

        Assert.Equal(1, page.TotalUsers);
        Assert.Equal(Bob, Assert.Single(page.Users).UserId);
    }

    [Theory]
    [InlineData(null, Bob, Alice)]
    [InlineData("cost", Bob, Alice)]
    [InlineData("requests", Alice, Bob)]
    [InlineData("tokens", Bob, Alice)]
    [InlineData("peakHour", Alice, Bob)]
    [InlineData("refusals", Alice, Bob)]
    [InlineData("lastActive", Bob, Alice)]
    public async Task GetUsers_SortsDescendingByTheChosenAggregate(string? sort, string first, string second)
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db, sort: sort);

        Assert.Equal(new[] { first, second }, page.Users.Select(u => u.UserId));
    }

    [Theory]
    [InlineData("cost")]
    [InlineData("requests")]
    [InlineData("tokens")]
    [InlineData("peakHour")]
    [InlineData("refusals")]
    [InlineData("lastActive")]
    public async Task GetUsers_TiesAreOrderedByName(string sort)
    {
        using var db = NewDb();
        db.Users.AddRange(
            new ApplicationUser { Id = "t-zed", UserName = "zed" },
            new ApplicationUser { Id = "t-amy", UserName = "Amy" },
            new ApplicationUser { Id = "t-max", UserName = "max" });
        foreach (var id in new[] { "t-zed", "t-amy", "t-max" })
        {
            db.SystemAiUsageLogs.Add(Usage(id, 1, D1));
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await UsersAsync(db, sort: sort);

        Assert.Equal(new[] { "Amy", "max", "zed" }, page.Users.Select(u => u.UserName));
    }

    [Fact]
    public async Task GetUsers_PagesWithSkipAndTake()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var first = await UsersAsync(db, sort: "cost", skip: 0, take: 1);
        var second = await UsersAsync(db, sort: "cost", skip: 1, take: 1);
        var beyond = await UsersAsync(db, sort: "cost", skip: 2, take: 1);

        Assert.Equal(2, first.TotalUsers);
        Assert.Equal(Bob, Assert.Single(first.Users).UserId);
        Assert.Equal(2, second.TotalUsers);
        Assert.Equal(Alice, Assert.Single(second.Users).UserId);
        Assert.Empty(beyond.Users);
    }

    // --- Key sources -----------------------------------------------------------------------------

    private const string Carol = "u-carol";

    private static UserKeyAiUsageLog UserKeyUsage(string userId, DateTime at, long? userAiModelId, int roleContext = 1,
        decimal? cost = 0.02m, string modelId = "own-model", string? displayName = "Logged own") => new()
    {
        AspNetUserId = userId,
        UserAiModelId = userAiModelId,
        TimestampUtc = at,
        Provider = "Anthropic",
        ModelId = modelId,
        ModelDisplayName = displayName,
        RoleContext = roleContext,
        ThinkingLevel = string.Empty,
        InputTokens = 50,
        OutputTokens = 5,
        CacheReadInputTokens = 0,
        EstimatedCost = cost
    };

    /// <summary>
    /// <see cref="SeedAsync"/> plus own-key usage. Alice: 2 requests on her UserAiModel 1 ("My Claude", the same id as
    /// her system configuration 1) in D1 10:00, the hour of her 4 system requests, $0.04. Carol, own keys only: 3
    /// requests on UserAiModel 5 ("Carol Model", D2 09:00, D2 09:10, D3 09:00, $0.30) and 1 with no model id (D3 10:00,
    /// $0.05, no logged display name).
    /// </summary>
    private static async Task SeedWithOwnKeysAsync(ApplicationDbContext db)
    {
        await SeedAsync(db);
        db.Users.Add(new ApplicationUser { Id = Carol, UserName = "carol", Email = "carol@sample.org" });
        db.UserAiModels.AddRange(
            new UserAiModel { Id = 1, AspNetUserId = Alice, Provider = "Anthropic", ModelId = "own-model", DisplayName = "My Claude" },
            new UserAiModel { Id = 5, AspNetUserId = Carol, Provider = "Anthropic", ModelId = "own-model", DisplayName = "Carol Model" });
        db.UserKeyAiUsageLogs.AddRange(
            UserKeyUsage(Alice, D1.AddHours(10).AddMinutes(20), 1),
            UserKeyUsage(Alice, D1.AddHours(10).AddMinutes(40), 1),
            UserKeyUsage(Carol, D2.AddHours(9), 5, cost: 0.10m),
            UserKeyUsage(Carol, D2.AddHours(9).AddMinutes(10), 5, cost: 0.10m),
            UserKeyUsage(Carol, D3.AddHours(9), 5, cost: 0.10m),
            UserKeyUsage(Carol, D3.AddHours(10), null, cost: 0.05m, modelId: "loose-model", displayName: null));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetUsers_NoKeySourceIsTheSystemView()
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);

        foreach (var page in new[] { await UsersAsync(db), await UsersAsync(db, keys: "system") })
        {
            Assert.Equal("system", page.KeySource);
            Assert.Equal(2, page.TotalUsers);
            Assert.Equal(new[] { Bob, Alice }, page.Users.Select(u => u.UserId));

            var alice = Assert.Single(page.Users, u => u.UserId == Alice);
            Assert.Equal(7, alice.Requests);
            Assert.Equal(7, alice.SystemRequests);
            Assert.Equal(0, alice.UserKeyRequests);
            Assert.Equal(0.07m, alice.EstimatedCost);
            Assert.Equal(0.07m, alice.SystemEstimatedCost);
            Assert.Equal(0m, alice.UserKeyEstimatedCost);
            Assert.Equal(4, alice.PeakHourRequests);
            Assert.Equal(1.75, alice.RequestsPerActiveHour);
            Assert.Equal(new long[] { 1, 2 }, alice.Configs.Select(c => c.ConfigId));
            Assert.All(alice.Configs, c => Assert.Equal("system", c.KeySource));
            Assert.Equal("Config One", alice.Configs[0].ConfigName);
            Assert.Equal("user", alice.Configs[0].LimitSource);
        }
    }

    [Fact]
    public async Task GetUsers_UserKeysShowOnlyOwnKeyUsage()
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);

        var page = await UsersAsync(db, keys: "user");

        Assert.Equal("user", page.KeySource);
        Assert.Equal(2, page.TotalUsers);
        Assert.DoesNotContain(page.Users, u => u.UserId == Bob);

        var alice = Assert.Single(page.Users, u => u.UserId == Alice);
        Assert.Equal(2, alice.Requests);
        Assert.Equal(0, alice.SystemRequests);
        Assert.Equal(2, alice.UserKeyRequests);
        Assert.Equal(0.04m, alice.EstimatedCost);
        Assert.Equal(0.04m, alice.UserKeyEstimatedCost);
        Assert.Equal(100, alice.PromptTokens);
        Assert.Equal(2, alice.PeakHourRequests);
        Assert.Equal(1, alice.ActiveDays);

        var line = Assert.Single(alice.Configs);
        Assert.Equal("user", line.KeySource);
        Assert.Equal(1, line.ConfigId);
        Assert.Equal("My Claude", line.ConfigName);
        Assert.Equal("ownKey", line.LimitSource);
        Assert.Equal(2, line.Requests);
        Assert.Equal(0.04m, line.EstimatedCost);
        Assert.Null(line.AssignmentId);
        Assert.Null(line.AssignmentEnabled);
        Assert.Null(line.DailyChatRequests);
        Assert.Null(line.MonthlyChatRequests);
        Assert.Null(line.DailyChatTokens);

        var carol = Assert.Single(page.Users, u => u.UserId == Carol);
        Assert.Equal(new long[] { 5, 0 }, carol.Configs.Select(c => c.ConfigId));
        Assert.Equal("Carol Model", carol.Configs[0].ConfigName);
        Assert.Equal(3, carol.Configs[0].Requests);
        // No model id and no logged display name: the model id names the line.
        Assert.Equal("loose-model", carol.Configs[1].ConfigName);
        Assert.All(carol.Configs, c => Assert.Equal("ownKey", c.LimitSource));
    }

    [Fact]
    public async Task GetUsers_AnOwnKeyLineFallsBackToTheLoggedNameOnceTheModelIsDeleted()
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);
        db.UserAiModels.Remove(await db.UserAiModels.SingleAsync(m => m.Id == 1, TestContext.Current.CancellationToken));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var alice = Assert.Single((await UsersAsync(db, keys: "user")).Users, u => u.UserId == Alice);

        Assert.Equal("Logged own", Assert.Single(alice.Configs).ConfigName);
    }

    [Fact]
    public async Task GetUsers_BothSumsTheSourcesAndSplitsThem()
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);

        var page = await UsersAsync(db, keys: "both");

        Assert.Equal("both", page.KeySource);
        Assert.Equal(3, page.TotalUsers);

        var alice = Assert.Single(page.Users, u => u.UserId == Alice);
        Assert.Equal(9, alice.Requests);
        Assert.Equal(7, alice.SystemRequests);
        Assert.Equal(2, alice.UserKeyRequests);
        Assert.Equal(0.11m, alice.EstimatedCost);
        Assert.Equal(0.07m, alice.SystemEstimatedCost);
        Assert.Equal(0.04m, alice.UserKeyEstimatedCost);
        Assert.Equal(9, alice.PricedRequests);
        Assert.Equal(9, alice.ChatRequests);
        Assert.Equal(800, alice.PromptTokens);

        // 4 system and 2 own-key requests in D1 10:00 make one hour of 6.
        Assert.Equal(6, alice.PeakHourRequests);
        Assert.Equal(D1.AddHours(10), alice.PeakHourStartUtc);
        // Nine requests over four active hours.
        Assert.Equal(2.25, alice.RequestsPerActiveHour);
        Assert.Equal(2, alice.ActiveDays);

        // System configuration 1 and UserAiModel 1 are two lines.
        Assert.Equal(new[] { ("system", 1L), ("system", 2L), ("user", 1L) }, alice.Configs.Select(c => (c.KeySource, c.ConfigId)));
        Assert.Equal("Config One", alice.Configs[0].ConfigName);
        Assert.Equal("My Claude", alice.Configs[2].ConfigName);

        var carol = Assert.Single(page.Users, u => u.UserId == Carol);
        Assert.Equal(4, carol.Requests);
        Assert.Equal(0, carol.SystemRequests);
        Assert.Equal(0.35m, carol.EstimatedCost);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("system", false)]
    [InlineData("user", true)]
    [InlineData("both", true)]
    public async Task GetUsers_AnOwnKeyOnlyUserAppearsOnlyWhenOwnKeysAreIncluded(string? keys, bool listed)
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);

        var page = await UsersAsync(db, keys: keys);

        Assert.Equal(listed, page.Users.Any(u => u.UserId == Carol));
        Assert.Equal(listed, (await UsersAsync(db, keys: keys, userId: Carol)).TotalUsers == 1);
    }

    [Theory]
    [InlineData("asc", new[] { Alice, Bob, Carol })]
    [InlineData("desc", new[] { Carol, Bob, Alice })]
    [InlineData(null, new[] { Carol, Bob, Alice })]
    public async Task GetUsers_SortsByName(string? direction, string[] expected)
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);

        var page = await UsersAsync(db, sort: "name", keys: "both", direction: direction);

        Assert.Equal(expected, page.Users.Select(u => u.UserId));
    }

    [Theory]
    [InlineData("cost", "asc", Alice, Bob)]
    [InlineData("cost", "ASC", Alice, Bob)]
    [InlineData("cost", "sideways", Bob, Alice)]
    [InlineData("requests", "asc", Bob, Alice)]
    [InlineData("lastActive", "asc", Alice, Bob)]
    [InlineData("lastActive", "desc", Bob, Alice)]
    public async Task GetUsers_SortsInTheChosenDirection(string sort, string direction, string first, string second)
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await UsersAsync(db, sort: sort, direction: direction);

        Assert.Equal(new[] { first, second }, page.Users.Select(u => u.UserId));
    }

    [Fact]
    public async Task GetUsers_TiesOnNameAreOrderedByUserId()
    {
        using var db = NewDb();
        db.Users.AddRange(new ApplicationUser { Id = "t-b", UserName = "same" }, new ApplicationUser { Id = "t-a", UserName = "Same" });
        db.SystemAiUsageLogs.AddRange(Usage("t-b", 1, D1), Usage("t-a", 1, D1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        foreach (var direction in new[] { "asc", "desc" })
        {
            var page = await UsersAsync(db, sort: "name", direction: direction);
            Assert.Equal(new[] { "t-a", "t-b" }, page.Users.Select(u => u.UserId));
        }
    }

    [Fact]
    public async Task GetAiTelemetryUsers_PassesKeysAndDirection()
    {
        using var db = NewDb();
        await SeedWithOwnKeysAsync(db);
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetryUsers(null, null, null, null, "name", 0, 20, "both", "asc", Service(db));

        var page = Assert.IsType<AiTelemetryUsersPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("both", page.KeySource);
        Assert.Equal(new[] { Alice, Bob, Carol }, page.Users.Select(u => u.UserId));
    }
}
