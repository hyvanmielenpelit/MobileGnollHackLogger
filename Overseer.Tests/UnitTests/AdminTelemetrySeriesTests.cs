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

/// <summary>The AI Telemetry series: bucket choice and folding, line selection, the user split and filter, and activity per bucket.</summary>
public class AdminTelemetrySeriesTests
{
    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static DateTime Utc(int year, int month, int day, int hour = 12) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static SystemAiUsageLog Usage(DateTime at, string modelId = "model-x", string userId = "u1", int roleContext = 1) => new()
    {
        SystemAiApiConfigurationId = 1,
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = "Anthropic",
        ModelId = modelId,
        RoleContext = roleContext,
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadInputTokens = 0,
        ThinkingLevel = string.Empty
    };

    private static UserKeyAiUsageLog KeyUsage(DateTime at, string userId, string modelId = "user-model") => new()
    {
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = "OpenAI",
        ModelId = modelId,
        RoleContext = 1,
        InputTokens = 50,
        OutputTokens = 5
    };

    private static async Task<AiTelemetrySeriesDto> SeriesAsync(ApplicationDbContext db, DateTime start, DateTime end,
        string? keys = null, string? bucket = null, string? splitBy = null, string? userId = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetSeriesAsync(start, end, keys, null, bucket, splitBy, userId, TestContext.Current.CancellationToken);
    }

    // --- Buckets -------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "day")]
    [InlineData(45, "day")]
    [InlineData(46, "week")]
    [InlineData(180, "week")]
    [InlineData(181, "month")]
    public void ResolveBucket_Auto_ChoosesByTheNumberOfDays(int days, string expected)
    {
        var start = new DateTime(2026, 1, 1);

        Assert.Equal(expected, AiTelemetrySummaryService.ResolveBucket(start, start.AddDays(days - 1), "auto"));
        Assert.Equal(expected, AiTelemetrySummaryService.ResolveBucket(start, start.AddDays(days - 1), null));
    }

    [Fact]
    public void ResolveBucket_AnExplicitBucketWins()
    {
        var start = new DateTime(2026, 1, 1);

        Assert.Equal("month", AiTelemetrySummaryService.ResolveBucket(start, start.AddDays(3), "month"));
        Assert.Equal("day", AiTelemetrySummaryService.ResolveBucket(start, start.AddDays(400), "day"));
    }

    [Fact]
    public async Task GetSeries_AutoBucketForSixtyDaysIsWeek()
    {
        using var db = NewDb();

        var series = await SeriesAsync(db, Utc(2026, 7, 1), Utc(2026, 7, 1).AddDays(59));

        Assert.Equal("week", series.Bucket);
    }

    [Fact]
    public async Task GetSeries_WeeksStartOnMonday_AndFoldTheirDays()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(Utc(2026, 9, 1)),   // Tuesday: the week of Monday 31 August
            Usage(Utc(2026, 9, 8)),   // Tuesday: the week of Monday 7 September
            Usage(Utc(2026, 9, 13)),  // Sunday: still the week of Monday 7 September
            Usage(Utc(2026, 9, 14))); // Monday: the week of 14 September

        var series = await SeriesAsync(db, Utc(2026, 9, 1), Utc(2026, 9, 20), bucket: "week");

        Assert.Equal("week", series.Bucket);
        Assert.Equal(new[] { Utc(2026, 8, 31, 0), Utc(2026, 9, 7, 0), Utc(2026, 9, 14, 0) }, series.BucketStartsUtc);
        Assert.All(series.BucketStartsUtc, b => Assert.Equal(DayOfWeek.Monday, b.DayOfWeek));
        var line = Assert.Single(series.Series);
        Assert.Equal(new long[] { 1, 2, 1 }, line.Points.Select(p => p.Requests));
    }

    [Fact]
    public void BucketStart_FoldsToMondayAndToTheFirstOfTheMonth()
    {
        Assert.Equal(new DateTime(2026, 9, 7), AiTelemetrySummaryService.BucketStart(Utc(2026, 9, 13, 23), "week"));
        Assert.Equal(new DateTime(2026, 9, 7), AiTelemetrySummaryService.BucketStart(Utc(2026, 9, 7, 0), "week"));
        Assert.Equal(new DateTime(2026, 9, 1), AiTelemetrySummaryService.BucketStart(Utc(2026, 9, 30), "month"));
        Assert.Equal(new DateTime(2026, 9, 13), AiTelemetrySummaryService.BucketStart(Utc(2026, 9, 13, 23), "day"));
    }

    [Fact]
    public async Task GetSeries_ZeroFillsEmptyBuckets_AlignedWithTheBucketStarts()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(Utc(2026, 9, 2)), Usage(Utc(2026, 9, 2, 13)), Usage(Utc(2026, 9, 4)));

        var series = await SeriesAsync(db, Utc(2026, 9, 1), Utc(2026, 9, 5));

        Assert.Equal("day", series.Bucket);
        Assert.Equal(5, series.BucketStartsUtc.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(Utc(2026, 9, 1, 0).AddDays(i), series.BucketStartsUtc[i]);
            Assert.Equal(DateTimeKind.Utc, series.BucketStartsUtc[i].Kind);
        }

        var line = Assert.Single(series.Series);
        Assert.Equal("All", line.Label);
        Assert.Equal(5, line.Points.Count);
        Assert.Equal(new long[] { 0, 2, 0, 1, 0 }, line.Points.Select(p => p.Requests));
        Assert.Equal(new long[] { 0, 200, 0, 100, 0 }, line.Points.Select(p => p.PromptTokens));
        Assert.Equal(5, series.Activity!.Count);
    }

    [Theory]
    [InlineData("2026-09-01", "2026-09-30", "day", 30)]
    [InlineData("2026-09-01", "2026-09-20", "week", 3)]
    [InlineData("2026-01-15", "2026-03-02", "month", 3)]
    [InlineData("2026-01-01", "2026-02-14", "auto", 45)]
    [InlineData("2026-01-01", "2026-12-31", "auto", 12)]
    [InlineData("2026-09-30", "2026-09-01", "day", 30)]
    public void BucketCount_CountsTheBucketsOfTheRange(string start, string end, string bucket, int expected)
    {
        Assert.Equal(expected, AiTelemetrySummaryService.BucketCount(DateTime.Parse(start), DateTime.Parse(end), bucket));
    }

    // --- Lines ---------------------------------------------------------------------------------

    [Fact]
    public async Task GetSeries_KeepsTheTopEightLines_AndSumsTheRestIntoOther()
    {
        using var db = NewDb();
        for (int m = 1; m <= 10; m++)
        {
            // m01 has 10 requests, m10 has 1.
            for (int r = 0; r < 11 - m; r++)
            {
                db.SystemAiUsageLogs.Add(Usage(Utc(2026, 9, 2), modelId: $"m{m:00}"));
            }
        }

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), splitBy: "model");

        Assert.Equal("model", series.SplitBy);
        Assert.Equal(AiTelemetrySummaryService.MaxSeriesLines + 1, series.Series.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(m => $"m{m:00}"), series.Series.Take(8).Select(l => l.Label));
        Assert.All(series.Series.Take(8), l => Assert.False(l.IsOther));

        var other = series.Series.Last();
        Assert.True(other.IsOther);
        Assert.Equal(AiTelemetrySummaryService.OtherLineKey, other.Key);
        Assert.Equal("Other", other.Label);
        Assert.Equal(3, Assert.Single(other.Points).Requests);
        Assert.Equal(300, other.Points[0].PromptTokens);
    }

    [Fact]
    public async Task GetSeries_SplitByUserUnderBothKeys_AddsTheSystemConfigurationsLine()
    {
        using var db = NewDb();
        db.Users.AddRange(new ApplicationUser { Id = "u1", UserName = "alice" }, new ApplicationUser { Id = "u2", UserName = "bob" });
        db.SystemAiUsageLogs.AddRange(Usage(Utc(2026, 9, 2), userId: "u1"), Usage(Utc(2026, 9, 2), userId: "u2"));
        db.UserKeyAiUsageLogs.AddRange(
            KeyUsage(Utc(2026, 9, 2), "u1"), KeyUsage(Utc(2026, 9, 2), "u1"), KeyUsage(Utc(2026, 9, 2), "u2"));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "both", splitBy: "user");

        Assert.Equal("user", series.SplitBy);
        Assert.Equal(3, series.Series.Count);
        var system = Assert.Single(series.Series, l => l.Key == "system");
        Assert.Equal("System AI configurations", system.Label);
        Assert.Equal(2, system.Points[0].Requests);
        Assert.Equal("alice", Assert.Single(series.Series, l => l.Key == "user:u1").Label);
        Assert.Equal(2, Assert.Single(series.Series, l => l.Key == "user:u1").Points[0].Requests);
        Assert.Equal("bob", Assert.Single(series.Series, l => l.Key == "user:u2").Label);
    }

    [Fact]
    public async Task GetSeries_SplitByUserWithoutUserKeys_FallsBackToNone()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(Utc(2026, 9, 2)));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "system", splitBy: "user");

        Assert.Equal("none", series.SplitBy);
        Assert.Equal("All", Assert.Single(series.Series).Label);
    }

    [Fact]
    public async Task GetSeries_AUserFilter_ReturnsOnlyThatUsersLinesAndActivity()
    {
        using var db = NewDb();
        db.Users.AddRange(new ApplicationUser { Id = "u1", UserName = "alice" }, new ApplicationUser { Id = "u2", UserName = "bob" });
        db.SystemAiUsageLogs.AddRange(
            Usage(Utc(2026, 9, 2), modelId: "model-a", userId: "u1"),
            Usage(Utc(2026, 9, 2), modelId: "model-b", userId: "u2"));
        db.OverseerDailyActivities.AddRange(
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 2), AspNetUserId = "u1", ChatsCreated = 1, UserMessages = 2 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 2), AspNetUserId = "u2", ChatsCreated = 5, UserMessages = 7 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 2), AspNetUserId = null, IncognitoChatsCreated = 3, IncognitoUserMessages = 4 });

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), splitBy: "model", userId: "u1");

        Assert.Equal("u1", series.UserId);
        Assert.Equal("alice", series.UserName);
        var line = Assert.Single(series.Series);
        Assert.Equal("Anthropic|model-a", line.Key);

        var point = Assert.Single(series.Activity!);
        Assert.Equal(1, point.ChatsCreated);
        Assert.Equal(2, point.UserMessages);
        Assert.Equal(0, point.IncognitoChatsCreated);
        Assert.Equal(0, point.IncognitoUserMessages);
        Assert.Equal(1, point.ActiveUsers);
    }

    [Fact]
    public async Task GetSeries_WeeklyActiveUsersCountsAUserOnce()
    {
        using var db = NewDb();
        db.OverseerDailyActivities.AddRange(
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 7), AspNetUserId = "u1", ChatsCreated = 1 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 8), AspNetUserId = "u1", ChatsCreated = 1 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 9), AspNetUserId = "u1", UserMessages = 1 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 9), AspNetUserId = null, IncognitoChatsCreated = 1 });

        var series = await SeriesAsync(db, Utc(2026, 9, 7), Utc(2026, 9, 20), bucket: "week");

        Assert.Equal(2, series.Activity!.Count);
        Assert.Equal(1, series.Activity[0].ActiveUsers);
        Assert.Equal(1, series.Activity[0].NewUsers);
        Assert.Equal(2, series.Activity[0].ChatsCreated);
        Assert.Equal(1, series.Activity[0].UserMessages);
        Assert.Equal(1, series.Activity[0].IncognitoChatsCreated);
        Assert.Equal(0, series.Activity[1].ActiveUsers);
        Assert.Equal(new DateOnly(2026, 9, 7), series.ActivityCountedSinceUtc);
    }

    // --- Controller guard ----------------------------------------------------------------------

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
    public async Task GetAiTelemetrySeries_RefusesMoreThan1100DayBuckets()
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetrySeries(new DateTime(2020, 1, 1), new DateTime(2026, 1, 1),
            null, null, "day", null, null, Service(db));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var message = bad.Value!.GetType().GetProperty("message")!.GetValue(bad.Value) as string;
        Assert.Equal("The range has too many buckets; choose a coarser bucket.", message);
    }

    [Fact]
    public async Task GetAiTelemetrySeries_AcceptsTheSameRangeByMonth()
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetrySeries(new DateTime(2020, 1, 1), new DateTime(2026, 1, 1),
            null, null, "month", null, null, Service(db));

        var dto = Assert.IsType<AiTelemetrySeriesDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("month", dto.Bucket);
        Assert.Equal(73, dto.BucketStartsUtc.Count);
    }
}
