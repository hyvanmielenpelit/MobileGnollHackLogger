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
/// The usage of one group's current members, or of the users in no group, by model: members only, "No group", key
/// sources, the member count, and the endpoint's required and known group id.
/// </summary>
public class AdminTelemetryGroupModelsTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static SystemAiUsageLog Usage(string userId, string modelId, decimal cost = 0.01m) => new()
    {
        SystemAiApiConfigurationId = 1,
        AspNetUserId = userId,
        TimestampUtc = Day,
        Provider = "Anthropic",
        ModelId = modelId,
        RoleContext = 1,
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadInputTokens = 0,
        EstimatedCost = cost,
        PricingSource = "catalog",
        ThinkingLevel = string.Empty
    };

    private static UserKeyAiUsageLog KeyUsage(string userId, string modelId) => new()
    {
        AspNetUserId = userId,
        TimestampUtc = Day,
        Provider = "OpenAI",
        ModelId = modelId,
        RoleContext = 1,
        ThinkingLevel = string.Empty,
        InputTokens = 50,
        OutputTokens = 5,
        EstimatedCost = 0.02m,
        PricingSource = "catalog"
    };

    /// <summary>
    /// Group 1 "Red": u1 (model-a ×2, own-key model-k ×1) and u2 (model-a ×1, model-b ×1). Group 2 "Blue": u3 (model-c).
    /// u4 is in no group (model-d ×1, own-key model-k ×1); u5 is in no group and has no usage.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        foreach (var id in new[] { "u1", "u2", "u3", "u4", "u5" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }
        db.Groups.AddRange(
            new Group { Id = 1, DisplayName = "Red", CreatedAtUtc = Day },
            new Group { Id = 2, DisplayName = "Blue", CreatedAtUtc = Day });
        db.UserGroups.AddRange(
            new UserGroup { AspNetUserId = "u1", GroupId = 1 },
            new UserGroup { AspNetUserId = "u2", GroupId = 1 },
            new UserGroup { AspNetUserId = "u3", GroupId = 2 });
        db.SystemAiUsageLogs.AddRange(
            Usage("u1", "model-a"), Usage("u1", "model-a"), Usage("u2", "model-a"), Usage("u2", "model-b", 0.5m),
            Usage("u3", "model-c"), Usage("u4", "model-d"));
        db.UserKeyAiUsageLogs.AddRange(KeyUsage("u1", "model-k"), KeyUsage("u4", "model-k"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<AiTelemetryGroupModelsDto?> GroupModelsAsync(ApplicationDbContext db, long? groupId, string? keys = null) =>
        await Service(db).GetGroupModelsAsync(null, null, keys, groupId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GetGroupModels_SumsTheMembersOnly()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupModelsAsync(db, 1);

        Assert.NotNull(dto);
        Assert.Equal("system", dto!.KeySource);
        Assert.Equal(1, dto.GroupId);
        Assert.Equal("Red", dto.GroupName);
        Assert.Equal(2, dto.MemberCount);
        Assert.Equal(new[] { "model-b", "model-a" }, dto.Rows.Select(r => r.ModelId));
        var a = dto.Rows[1];
        Assert.Equal(3, a.Requests);
        Assert.Equal(0.03m, a.EstimatedCost);
        Assert.Equal(300, a.PromptTokens);
        Assert.Equal("system", a.KeySource);
        Assert.Equal(Day, dto.CostRecordedSinceUtc);
    }

    [Fact]
    public async Task GetGroupModels_NoGroupTakesTheUngroupedUsers()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var dto = await GroupModelsAsync(db, null, keys: "both");

        Assert.NotNull(dto);
        Assert.Null(dto!.GroupId);
        Assert.Equal(AiTelemetrySummaryService.NoGroupName, dto.GroupName);
        // u4 and u5.
        Assert.Equal(2, dto.MemberCount);
        Assert.Equal(new[] { ("user", "model-k"), ("system", "model-d") }, dto.Rows.Select(r => (r.KeySource, r.ModelId)));
        Assert.All(dto.Rows, r => Assert.Equal(1, r.Requests));
    }

    [Fact]
    public async Task GetGroupModels_FollowsTheKeySources()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var user = await GroupModelsAsync(db, 1, keys: "user");
        var both = await GroupModelsAsync(db, 1, keys: "both");

        var own = Assert.Single(user!.Rows);
        Assert.Equal(("user", "OpenAI", "model-k", 1L), (own.KeySource, own.Provider, own.ModelId, own.Requests));
        Assert.Equal("user", user.KeySource);
        Assert.Equal(new[] { "model-b", "model-a", "model-k" }, both!.Rows.Select(r => r.ModelId));
    }

    [Fact]
    public async Task GetGroupModels_AGroupWithoutUsageHasNoRows()
    {
        using var db = NewDb();
        await SeedAsync(db);
        db.Groups.Add(new Group { Id = 3, DisplayName = "Quiet", CreatedAtUtc = Day });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = await GroupModelsAsync(db, 3);

        Assert.NotNull(dto);
        Assert.Equal(0, dto!.MemberCount);
        Assert.Empty(dto.Rows);
    }

    [Fact]
    public async Task GetGroupModels_AnUnknownGroupIsNull()
    {
        using var db = NewDb();
        await SeedAsync(db);

        Assert.Null(await GroupModelsAsync(db, 99));
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

    private static string? Message(object? value) => value!.GetType().GetProperty("message")!.GetValue(value) as string;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetAiTelemetryGroupModels_RequiresAGroupId(string? groupId)
    {
        using var db = NewDb();

        var result = await CreateController(db).GetAiTelemetryGroupModels(null, null, null, groupId, Service(db));

        Assert.Equal("groupId is required.", Message(Assert.IsType<BadRequestObjectResult>(result).Value));
    }

    [Fact]
    public async Task GetAiTelemetryGroupModels_RefusesAMalformedGroupId()
    {
        using var db = NewDb();

        var result = await CreateController(db).GetAiTelemetryGroupModels(null, null, null, "red", Service(db));

        Assert.Equal("groupId is a group id or none.", Message(Assert.IsType<BadRequestObjectResult>(result).Value));
    }

    [Fact]
    public async Task GetAiTelemetryGroupModels_AnUnknownGroupIsNotFound()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var result = await CreateController(db).GetAiTelemetryGroupModels(null, null, null, "99", Service(db));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData("1", 1L, "Red")]
    [InlineData("none", null, AiTelemetrySummaryService.NoGroupName)]
    public async Task GetAiTelemetryGroupModels_ReturnsTheGroup(string groupId, long? expectedId, string expectedName)
    {
        using var db = NewDb();
        await SeedAsync(db);

        var result = await CreateController(db).GetAiTelemetryGroupModels(null, null, "both", groupId, Service(db));

        var dto = Assert.IsType<AiTelemetryGroupModelsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(expectedId, dto.GroupId);
        Assert.Equal(expectedName, dto.GroupName);
        Assert.Equal("both", dto.KeySource);
    }
}
