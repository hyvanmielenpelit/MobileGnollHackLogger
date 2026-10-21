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
/// The charts' filter options: models from both logs and from the attempts ledger, key sources and roles, the users'
/// order and cap, and every group with its members' figures, plus "No group" when an ungrouped user has usage.
/// </summary>
public class AdminTelemetrySeriesOptionsTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static SystemAiUsageLog Usage(string userId, string modelId = "model-a", int roleContext = 1, decimal? cost = 0.01m,
        DateTime? at = null, string provider = "Anthropic") => new()
    {
        SystemAiApiConfigurationId = 1,
        AspNetUserId = userId,
        TimestampUtc = at ?? Day,
        Provider = provider,
        ModelId = modelId,
        RoleContext = roleContext,
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadInputTokens = 0,
        EstimatedCost = cost,
        ThinkingLevel = string.Empty
    };

    private static UserKeyAiUsageLog KeyUsage(string userId, string modelId = "model-c", decimal? cost = 0.02m, string provider = "OpenAI") => new()
    {
        AspNetUserId = userId,
        TimestampUtc = Day,
        Provider = provider,
        ModelId = modelId,
        RoleContext = 1,
        ThinkingLevel = string.Empty,
        InputTokens = 50,
        OutputTokens = 5,
        EstimatedCost = cost
    };

    private static ChatProviderAttempt Attempt(string provider, string modelId, AiKeySource keySource = AiKeySource.System,
        int roleContext = 1, string? userId = null) => new()
    {
        StartedAtUtc = Day,
        AgentRunId = Guid.NewGuid(),
        RoleContext = roleContext,
        KeySource = keySource,
        SystemAiApiConfigurationId = keySource == AiKeySource.System ? 1 : null,
        AspNetUserId = userId,
        Provider = provider,
        RequestedModelId = modelId,
        ThinkingLevel = string.Empty,
        DurationMs = 100,
        Outcome = ProviderAttemptOutcome.ProviderOverloaded
    };

    private static async Task<AiTelemetrySeriesOptionsDto> OptionsAsync(ApplicationDbContext db, string? keys = null, string[]? roles = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetSeriesOptionsAsync(Day, Day, keys, roles, TestContext.Current.CancellationToken);
    }

    // --- Models --------------------------------------------------------------------------------

    /// <summary>
    /// u1: Anthropic|model-a chat ×2 on a system configuration and ×1 on an own key; Anthropic|model-b benchmark ×1.
    /// u2: OpenAI|model-c chat ×1 on an own key.
    /// </summary>
    private static void SeedModels(ApplicationDbContext db)
    {
        db.SystemAiUsageLogs.AddRange(Usage("u1"), Usage("u1"), Usage("u1", "model-b", roleContext: 4, cost: 0.5m));
        db.UserKeyAiUsageLogs.AddRange(KeyUsage("u1", "model-a", provider: "Anthropic"), KeyUsage("u2"));
    }

    [Fact]
    public async Task GetSeriesOptions_ModelsFollowTheKeySources()
    {
        using var db = NewDb();
        SeedModels(db);

        var system = await OptionsAsync(db);
        var user = await OptionsAsync(db, keys: "user");
        var both = await OptionsAsync(db, keys: "both");

        Assert.Equal(new[] { "model-a", "model-b" }, system.Models.Select(m => m.ModelId));
        Assert.All(system.Models, m => Assert.Equal(new[] { "system" }, m.KeySources));
        Assert.Equal(new[] { "model-a", "model-c" }, user.Models.Select(m => m.ModelId).OrderBy(id => id));

        var a = Assert.Single(both.Models, m => m.ModelId == "model-a");
        Assert.Equal(new[] { "system", "user" }, a.KeySources);
        Assert.Equal(3, a.Requests);
        Assert.Equal(0.04m, a.EstimatedCost);
        Assert.Equal(3, a.PricedRequests);
        Assert.Equal(100 + 100 + 50 + 10 + 10 + 5, a.Tokens);
        Assert.Equal(Day, a.LastRequestUtc);
        Assert.Equal(DateTimeKind.Utc, a.LastRequestUtc!.Value.Kind);
        Assert.Equal("model-a", both.Models[0].ModelId);
    }

    [Fact]
    public async Task GetSeriesOptions_ModelsFollowTheRoles()
    {
        using var db = NewDb();
        SeedModels(db);

        var chat = await OptionsAsync(db, roles: new[] { "chat" });
        var benchmark = await OptionsAsync(db, roles: new[] { "benchmark" });

        Assert.Equal(new[] { "model-a" }, chat.Models.Select(m => m.ModelId));
        Assert.Equal(new[] { "model-b" }, benchmark.Models.Select(m => m.ModelId));
        Assert.Equal(new[] { "u1" }, benchmark.Users.Select(u => u.UserId));
        Assert.Equal(1, benchmark.Users[0].Requests);
    }

    [Fact]
    public async Task GetSeriesOptions_AModelSeenOnlyInTheAttemptsIsOfferedWithNoRequests()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage("u1"));
        db.ChatProviderAttempts.AddRange(
            Attempt("Anthropic", "model-a"),
            Attempt("Anthropic", "model-down"),
            Attempt("Anthropic", "model-down"),
            Attempt("OpenAI", "model-own-down", AiKeySource.UserKey, userId: "u1"),
            Attempt("Anthropic", "model-title-down", roleContext: 2));

        var system = await OptionsAsync(db);
        var chatOnly = await OptionsAsync(db, roles: new[] { "chat" });
        var user = await OptionsAsync(db, keys: "user");

        Assert.Equal(new[] { "model-a", "model-down", "model-title-down" }, system.Models.Select(m => m.ModelId));
        var down = Assert.Single(system.Models, m => m.ModelId == "model-down");
        Assert.Equal(0, down.Requests);
        Assert.Equal(0m, down.EstimatedCost);
        Assert.Null(down.LastRequestUtc);
        Assert.Equal(new[] { "system" }, down.KeySources);
        Assert.Equal(1, Assert.Single(system.Models, m => m.ModelId == "model-a").Requests);

        Assert.DoesNotContain(chatOnly.Models, m => m.ModelId == "model-title-down");
        var own = Assert.Single(user.Models);
        Assert.Equal(("OpenAI", "model-own-down"), (own.Provider, own.ModelId));
        Assert.Equal(new[] { "user" }, own.KeySources);
    }

    // --- Users ---------------------------------------------------------------------------------

    [Fact]
    public async Task GetSeriesOptions_UsersAreOrderedByRequestsThenNameThenId_AndAGoneAccountHasNoName()
    {
        using var db = NewDb();
        db.Users.AddRange(
            new ApplicationUser { Id = "u-bob", UserName = "bob" },
            new ApplicationUser { Id = "u-zed", UserName = "zed" },
            new ApplicationUser { Id = "u-amy", UserName = "Amy" });
        db.SystemAiUsageLogs.AddRange(Usage("u-bob"), Usage("u-bob"), Usage("u-bob"), Usage("u-zed"), Usage("u-amy"), Usage("gone"));
        db.UserKeyAiUsageLogs.Add(KeyUsage("u-zed"));

        var options = await OptionsAsync(db, keys: "both");

        // bob 3, zed 2 (one on each key source), then Amy and the gone account with 1 each: a missing name sorts last.
        Assert.Equal(new[] { "u-bob", "u-zed", "u-amy", "gone" }, options.Users.Select(u => u.UserId));
        var zed = options.Users[1];
        Assert.Equal(2, zed.Requests);
        Assert.Equal(1, zed.SystemRequests);
        Assert.Equal(1, zed.UserKeyRequests);
        Assert.Equal(0.03m, zed.EstimatedCost);
        Assert.Null(Assert.Single(options.Users, u => u.UserId == "gone").UserName);
        Assert.Equal(4, options.TotalUsers);
        Assert.False(options.UsersTruncated);
    }

    [Fact]
    public async Task GetSeriesOptions_TiesOnRequestsAreOrderedByName()
    {
        using var db = NewDb();
        db.Users.AddRange(
            new ApplicationUser { Id = "t-1", UserName = "zed" },
            new ApplicationUser { Id = "t-2", UserName = "Amy" },
            new ApplicationUser { Id = "t-3", UserName = "max" });
        db.SystemAiUsageLogs.AddRange(Usage("t-1"), Usage("t-2"), Usage("t-3"), Usage("gone"));

        var options = await OptionsAsync(db);

        Assert.Equal(new[] { "Amy", "max", "zed", null }, options.Users.Select(u => u.UserName));
    }

    [Fact]
    public async Task GetSeriesOptions_CapsTheUsers()
    {
        using var db = NewDb();
        int count = AiTelemetrySummaryService.MaxSeriesOptionUsers + 2;
        for (int i = 0; i < count; i++)
        {
            db.SystemAiUsageLogs.Add(Usage($"u{i:0000}"));
        }
        db.SystemAiUsageLogs.Add(Usage("u0500"));

        var options = await OptionsAsync(db);

        Assert.Equal(AiTelemetrySummaryService.MaxSeriesOptionUsers, options.Users.Count);
        Assert.Equal(count, options.TotalUsers);
        Assert.True(options.UsersTruncated);
        Assert.Equal("u0500", options.Users[0].UserId);
    }

    // --- Groups --------------------------------------------------------------------------------

    /// <summary>
    /// Groups "Red" (u1, u2), "Blue" (u2) and "Quiet" (u3, no usage) and "Empty" (no members). u1: 2 requests,
    /// u2: 1, u4 (no group): 1.
    /// </summary>
    private static void SeedGroups(ApplicationDbContext db)
    {
        foreach (var id in new[] { "u1", "u2", "u3", "u4", "u5" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }
        db.Groups.AddRange(
            new Group { Id = 1, DisplayName = "Red", CreatedAtUtc = Day },
            new Group { Id = 2, DisplayName = "Blue", CreatedAtUtc = Day },
            new Group { Id = 3, DisplayName = "Quiet", CreatedAtUtc = Day },
            new Group { Id = 4, DisplayName = "Empty", CreatedAtUtc = Day });
        db.UserGroups.AddRange(
            new UserGroup { AspNetUserId = "u1", GroupId = 1 },
            new UserGroup { AspNetUserId = "u2", GroupId = 1 },
            new UserGroup { AspNetUserId = "u2", GroupId = 2 },
            new UserGroup { AspNetUserId = "u3", GroupId = 3 });
        db.SystemAiUsageLogs.AddRange(Usage("u1"), Usage("u1", at: Day.AddHours(-1)), Usage("u2", at: Day.AddHours(-2)));
    }

    [Fact]
    public async Task GetSeriesOptions_ListsEveryGroup_WithItsMembersFigures()
    {
        using var db = NewDb();
        SeedGroups(db);

        var options = await OptionsAsync(db);

        Assert.Equal(new[] { "Red", "Blue", "Empty", "Quiet" }, options.Groups.Select(g => g.GroupName));
        var red = options.Groups[0];
        Assert.Equal(1, red.GroupId);
        Assert.Equal(2, red.MemberCount);
        Assert.Equal(3, red.Requests);
        Assert.Equal(0.03m, red.EstimatedCost);
        Assert.Equal(3, red.PricedRequests);
        Assert.Equal(Day, red.LastRequestUtc);

        var blue = options.Groups[1];
        Assert.Equal(1, blue.Requests);
        Assert.Equal(Day.AddHours(-2), blue.LastRequestUtc);

        var quiet = Assert.Single(options.Groups, g => g.GroupName == "Quiet");
        Assert.Equal(1, quiet.MemberCount);
        Assert.Equal(0, quiet.Requests);
        Assert.Equal(0m, quiet.EstimatedCost);
        Assert.Null(quiet.LastRequestUtc);
        Assert.Equal(0, Assert.Single(options.Groups, g => g.GroupName == "Empty").MemberCount);
        Assert.DoesNotContain(options.Groups, g => g.GroupId == null);
    }

    [Fact]
    public async Task GetSeriesOptions_OffersNoGroup_OnlyWhenAnUngroupedUserHasUsage()
    {
        using var db = NewDb();
        SeedGroups(db);
        db.SystemAiUsageLogs.Add(Usage("u4", at: Day.AddHours(-3)));

        var options = await OptionsAsync(db);
        var chatless = await OptionsAsync(db, roles: new[] { "benchmark" });

        var none = Assert.Single(options.Groups, g => g.GroupId == null);
        Assert.Equal(AiTelemetrySummaryService.NoGroupName, none.GroupName);
        // u4 and u5 belong to no group.
        Assert.Equal(2, none.MemberCount);
        Assert.Equal(1, none.Requests);
        Assert.Equal(Day.AddHours(-3), none.LastRequestUtc);
        // Blue and No group have one request each: No group comes last among equals.
        Assert.Equal(new[] { "Red", "Blue", AiTelemetrySummaryService.NoGroupName, "Empty", "Quiet" }, options.Groups.Select(g => g.GroupName));

        Assert.DoesNotContain(chatless.Groups, g => g.GroupId == null);
    }

    [Fact]
    public async Task GetSeriesOptions_AGoneAccountsUsageCountsUnderNoGroup()
    {
        using var db = NewDb();
        SeedGroups(db);
        db.SystemAiUsageLogs.Add(Usage("gone"));

        var options = await OptionsAsync(db);

        var none = Assert.Single(options.Groups, g => g.GroupId == null);
        Assert.Equal(1, none.Requests);
        Assert.Equal(2, none.MemberCount);
        Assert.Null(Assert.Single(options.Users, u => u.UserId == "gone").UserName);
    }

    [Fact]
    public async Task GetSeriesOptions_GroupsFollowTheKeySources()
    {
        using var db = NewDb();
        SeedGroups(db);
        db.UserKeyAiUsageLogs.AddRange(KeyUsage("u3"), KeyUsage("u3"));

        var system = await OptionsAsync(db);
        var user = await OptionsAsync(db, keys: "user");

        Assert.Equal(0, Assert.Single(system.Groups, g => g.GroupName == "Quiet").Requests);
        Assert.Equal(new[] { "Quiet", "Blue", "Empty", "Red" }, user.Groups.Select(g => g.GroupName));
        Assert.Equal(2, user.Groups[0].Requests);
        Assert.Equal(0.04m, user.Groups[0].EstimatedCost);
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
    public async Task GetAiTelemetrySeriesOptions_ParsesKeysAndRoles()
    {
        using var db = NewDb();
        SeedModels(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).GetAiTelemetrySeriesOptions(Day, Day, "both", " benchmark , ", Service(db));

        var dto = Assert.IsType<AiTelemetrySeriesOptionsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(new[] { "model-b" }, dto.Models.Select(m => m.ModelId));
    }
}
