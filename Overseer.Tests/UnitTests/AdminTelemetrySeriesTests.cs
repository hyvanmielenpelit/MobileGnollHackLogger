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
/// The AI Telemetry series: bucket choice and folding, line selection, the user split, the user, model and group
/// filters and their echoes, activity per bucket, and the endpoint's guards.
/// </summary>
public class AdminTelemetrySeriesTests
{
    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static DateTime Utc(int year, int month, int day, int hour = 12) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static SystemAiUsageLog Usage(DateTime at, string modelId = "model-x", string userId = "u1", int roleContext = 1,
        string provider = "Anthropic") => new()
    {
        SystemAiApiConfigurationId = 1,
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = provider,
        ModelId = modelId,
        RoleContext = roleContext,
        InputTokens = 100,
        OutputTokens = 10,
        CacheReadInputTokens = 0,
        ThinkingLevel = string.Empty
    };

    private static UserKeyAiUsageLog KeyUsage(DateTime at, string userId, string modelId = "user-model", string provider = "OpenAI") => new()
    {
        AspNetUserId = userId,
        TimestampUtc = at,
        Provider = provider,
        ModelId = modelId,
        RoleContext = 1,
        InputTokens = 50,
        OutputTokens = 5
    };

    private static ChatProviderAttempt Attempt(DateTime at, string provider, string modelId, ProviderAttemptOutcome outcome,
        AiKeySource keySource = AiKeySource.System, string? userId = null) => new()
    {
        StartedAtUtc = at,
        AgentRunId = Guid.NewGuid(),
        RoleContext = 1,
        AgentDepth = 0,
        KeySource = keySource,
        SystemAiApiConfigurationId = keySource == AiKeySource.System ? 1 : null,
        AspNetUserId = userId,
        Provider = provider,
        RequestedModelId = modelId,
        ThinkingLevel = string.Empty,
        CallIndex = 0,
        AttemptIndex = 0,
        DurationMs = 100,
        Outcome = outcome,
        Retried = false
    };

    private static OverseerDailyActivity Activity(DateOnly day, string? userId, int chats) => new()
    {
        DateUtc = day,
        AspNetUserId = userId,
        ChatsCreated = userId == null ? 0 : chats,
        IncognitoChatsCreated = userId == null ? chats : 0
    };

    private static async Task<AiTelemetrySeriesDto> SeriesAsync(ApplicationDbContext db, DateTime start, DateTime end,
        string? keys = null, string? bucket = null, string? splitBy = null, string[]? userIds = null,
        AiTelemetryModelRef[]? models = null, long?[]? groupIds = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetSeriesAsync(start, end, keys, null, bucket, splitBy, userIds, models, groupIds,
            TestContext.Current.CancellationToken);
    }

    private static long Requests(AiTelemetrySeriesDto series) => series.Series.Sum(l => l.Points.Sum(p => p.Requests));

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
    public async Task GetSeries_SplitByUserWithOneUserChosen_FallsBackToNone_AndWithTwoSplits()
    {
        using var db = NewDb();
        db.UserKeyAiUsageLogs.AddRange(
            KeyUsage(Utc(2026, 9, 2), "u1"), KeyUsage(Utc(2026, 9, 2), "u2"), KeyUsage(Utc(2026, 9, 2), "u3"));

        var one = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "user", splitBy: "user", userIds: new[] { "u1" });
        var two = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "user", splitBy: "user", userIds: new[] { "u1", "u2" });

        Assert.Equal("none", one.SplitBy);
        Assert.Equal(1, Requests(one));
        Assert.Equal("user", two.SplitBy);
        Assert.Equal(new[] { "user:u1", "user:u2" }, two.Series.Select(l => l.Key).OrderBy(k => k));
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

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), splitBy: "model", userIds: new[] { "u1" });

        var user = Assert.Single(series.Users);
        Assert.Equal("u1", user.UserId);
        Assert.Equal("alice", user.UserName);
        Assert.Empty(series.Models);
        Assert.Empty(series.Groups);
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
    public async Task GetSeries_SeveralUsers_CombineWithOr()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(Utc(2026, 9, 2), userId: "u1"), Usage(Utc(2026, 9, 2), userId: "u2"), Usage(Utc(2026, 9, 2), userId: "u2"),
            Usage(Utc(2026, 9, 2), userId: "u3"));
        db.OverseerDailyActivities.AddRange(
            Activity(new DateOnly(2026, 9, 2), "u1", 1), Activity(new DateOnly(2026, 9, 2), "u2", 2),
            Activity(new DateOnly(2026, 9, 2), "u3", 4), Activity(new DateOnly(2026, 9, 2), null, 8));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), userIds: new[] { "u1", "u2" });

        Assert.Equal(3, Requests(series));
        var point = Assert.Single(series.Activity!);
        Assert.Equal(3, point.ChatsCreated);
        Assert.Equal(0, point.IncognitoChatsCreated);
        Assert.Equal(2, point.ActiveUsers);
        Assert.Equal(new[] { "u1", "u2" }, series.Users.Select(u => u.UserId));
    }

    [Fact]
    public async Task GetSeries_NoFilters_EchoEmptyLists()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(Utc(2026, 9, 2)));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), userIds: Array.Empty<string>(),
            models: Array.Empty<AiTelemetryModelRef>(), groupIds: Array.Empty<long?>());

        Assert.Empty(series.Users);
        Assert.Empty(series.Models);
        Assert.Empty(series.Groups);
        Assert.Equal(1, Requests(series));
    }

    // --- Model filter --------------------------------------------------------------------------

    [Fact]
    public async Task GetSeries_TheModelFilterReadsBothLogs_AndMatchesTheProviderToo()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(Utc(2026, 9, 2), modelId: "model-a"),
            Usage(Utc(2026, 9, 2), modelId: "model-a"),
            Usage(Utc(2026, 9, 2), modelId: "model-b"));
        db.UserKeyAiUsageLogs.AddRange(
            KeyUsage(Utc(2026, 9, 2), "u1", modelId: "model-a"),          // OpenAI|model-a: the same id, another provider
            KeyUsage(Utc(2026, 9, 2), "u1", modelId: "user-model"));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "both", splitBy: "model",
            models: new[] { new AiTelemetryModelRef("Anthropic", "model-a"), new AiTelemetryModelRef("OpenAI", "user-model") });

        Assert.Equal(new[] { "Anthropic|model-a", "OpenAI|user-model" }, series.Series.Select(l => l.Key));
        Assert.Equal(2, series.Series[0].Points[0].Requests);
        Assert.Equal(1, series.Series[1].Points[0].Requests);
        Assert.Equal(new[] { ("Anthropic", "model-a"), ("OpenAI", "user-model") }, series.Models.Select(m => (m.Provider, m.ModelId)));
    }

    [Fact]
    public async Task GetSeries_TheModelFilterAppliesToTheAttempts_OnTheProviderToo()
    {
        using var db = NewDb();
        db.ChatProviderAttempts.AddRange(
            Attempt(Utc(2026, 9, 2), "Anthropic", "model-a", ProviderAttemptOutcome.ProviderOverloaded),
            Attempt(Utc(2026, 9, 2), "Anthropic", "model-a", ProviderAttemptOutcome.Ok),
            Attempt(Utc(2026, 9, 2), "Anthropic", "model-b", ProviderAttemptOutcome.ProviderServerError),
            Attempt(Utc(2026, 9, 2), "OpenAI", "model-a", ProviderAttemptOutcome.ProviderServerError, AiKeySource.UserKey, "u1"));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "both",
            models: new[] { new AiTelemetryModelRef("Anthropic", "model-a") });

        var point = Assert.Single(Assert.Single(series.Series).Points);
        Assert.Equal(1, point.ProviderErrors);
        Assert.Equal(2, point.ValidAttempts);
        Assert.Equal(0, point.Requests);
    }

    [Fact]
    public async Task GetSeries_TheActivityIgnoresTheModelFilter()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(Utc(2026, 9, 2), modelId: "model-a"));
        db.OverseerDailyActivities.AddRange(
            Activity(new DateOnly(2026, 9, 2), "u1", 2), Activity(new DateOnly(2026, 9, 2), "u2", 3),
            Activity(new DateOnly(2026, 9, 2), null, 1));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2),
            models: new[] { new AiTelemetryModelRef("Anthropic", "model-zzz") });

        Assert.Equal(0, Requests(series));
        var point = Assert.Single(series.Activity!);
        Assert.Equal(5, point.ChatsCreated);
        Assert.Equal(1, point.IncognitoChatsCreated);
        Assert.Equal(2, point.ActiveUsers);
    }

    // --- Group filter --------------------------------------------------------------------------

    /// <summary>Group 1 "Alpha" has u1, group 2 "Beta" has u2, u3 is in no group; each user made 1, 2 and 4 requests and chats.</summary>
    private static void SeedGroups(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "u1", UserName = "alice" },
            new ApplicationUser { Id = "u2", UserName = "bob" },
            new ApplicationUser { Id = "u3", UserName = "carol" });
        db.Groups.AddRange(
            new Group { Id = 1, DisplayName = "Alpha", CreatedAtUtc = Utc(2026, 1, 1) },
            new Group { Id = 2, DisplayName = "Beta", CreatedAtUtc = Utc(2026, 1, 1) });
        db.UserGroups.AddRange(new UserGroup { AspNetUserId = "u1", GroupId = 1 }, new UserGroup { AspNetUserId = "u2", GroupId = 2 });
        db.SystemAiUsageLogs.Add(Usage(Utc(2026, 9, 2), userId: "u1"));
        db.SystemAiUsageLogs.AddRange(Enumerable.Range(0, 2).Select(_ => Usage(Utc(2026, 9, 2), userId: "u2")));
        db.SystemAiUsageLogs.AddRange(Enumerable.Range(0, 4).Select(_ => Usage(Utc(2026, 9, 2), userId: "u3")));
        db.OverseerDailyActivities.AddRange(
            Activity(new DateOnly(2026, 9, 2), "u1", 1), Activity(new DateOnly(2026, 9, 2), "u2", 2),
            Activity(new DateOnly(2026, 9, 2), "u3", 4), Activity(new DateOnly(2026, 9, 2), null, 8));
    }

    [Theory]
    [InlineData(new long[] { 1 }, false, 1)]
    [InlineData(new long[] { 2 }, false, 2)]
    [InlineData(new long[] { 1, 2 }, false, 3)]
    [InlineData(new long[0], true, 4)]
    [InlineData(new long[] { 1 }, true, 5)]
    public async Task GetSeries_TheGroupFilterTakesTheCurrentMembers_AndNoGroupTheUngrouped(long[] groups, bool noGroup, long expected)
    {
        using var db = NewDb();
        SeedGroups(db);
        var groupIds = groups.Select(g => (long?)g).Concat(noGroup ? new long?[] { null } : Array.Empty<long?>()).ToArray();

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), groupIds: groupIds);

        Assert.Equal(expected, Requests(series));
        var point = Assert.Single(series.Activity!);
        Assert.Equal((int)expected, point.ChatsCreated);
        Assert.Equal(0, point.IncognitoChatsCreated);
    }

    [Fact]
    public async Task GetSeries_UsersAndGroupsCombineWithAnd()
    {
        using var db = NewDb();
        SeedGroups(db);

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), userIds: new[] { "u1", "u2" }, groupIds: new long?[] { 1 });
        var none = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), userIds: new[] { "u3" }, groupIds: new long?[] { 1 });

        Assert.Equal(1, Requests(series));
        Assert.Equal(1, Assert.Single(series.Activity!).ActiveUsers);
        Assert.Equal(0, Requests(none));
    }

    [Fact]
    public async Task GetSeries_AGroupFilterDropsTheSystemAttempts()
    {
        using var db = NewDb();
        SeedGroups(db);
        db.ChatProviderAttempts.AddRange(
            Attempt(Utc(2026, 9, 2), "Anthropic", "model-x", ProviderAttemptOutcome.ProviderOverloaded),
            Attempt(Utc(2026, 9, 2), "OpenAI", "user-model", ProviderAttemptOutcome.ProviderOverloaded, AiKeySource.UserKey, "u1"),
            Attempt(Utc(2026, 9, 2), "OpenAI", "user-model", ProviderAttemptOutcome.ProviderOverloaded, AiKeySource.UserKey, "u2"));

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2), keys: "both", groupIds: new long?[] { 1 });

        Assert.Equal(1, series.Series.Sum(l => l.Points.Sum(p => p.ProviderErrors)));
    }

    // --- Echoes --------------------------------------------------------------------------------

    [Fact]
    public async Task GetSeries_EchoesTheFiltersWithNames_InRequestOrder()
    {
        using var db = NewDb();
        SeedGroups(db);

        var series = await SeriesAsync(db, Utc(2026, 9, 2), Utc(2026, 9, 2),
            userIds: new[] { "u2", "gone", "u1" },
            models: new[] { new AiTelemetryModelRef("Anthropic", "model-x"), new AiTelemetryModelRef("OpenAI", "user-model") },
            groupIds: new long?[] { 2, null, 99 });

        Assert.Equal(new[] { ("u2", (string?)"bob"), ("gone", null), ("u1", "alice") }, series.Users.Select(u => (u.UserId, u.UserName)));
        Assert.Equal(new[] { ("Anthropic", "model-x"), ("OpenAI", "user-model") }, series.Models.Select(m => (m.Provider, m.ModelId)));
        Assert.All(series.Models, m => Assert.Null(m.ModelName));
        Assert.Equal(new[] { ((long?)2, "Beta"), (null, AiTelemetrySummaryService.NoGroupName), (99, AiTelemetrySummaryService.UnknownGroupName) },
            series.Groups.Select(g => (g.GroupId, g.GroupName)));
    }

    [Theory]
    [InlineData("Ollama|llama3:8b", "Ollama", "llama3:8b")]
    [InlineData("OpenAI|gpt-x", "OpenAI", "gpt-x")]
    [InlineData("a|b|c", "a", "b|c")]
    public void ModelRef_SplitsAtTheFirstBar(string value, string provider, string modelId)
    {
        Assert.True(AiTelemetryModelRef.TryParse(value, out var model));
        Assert.Equal(new AiTelemetryModelRef(provider, modelId), model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpt-x")]
    [InlineData("|gpt-x")]
    [InlineData("OpenAI|")]
    [InlineData("  | ")]
    public void ModelRef_RefusesAMissingPart(string? value)
    {
        Assert.False(AiTelemetryModelRef.TryParse(value, out _));
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

    private static string? Message(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        return bad.Value!.GetType().GetProperty("message")!.GetValue(bad.Value) as string;
    }

    private static Task<IActionResult> SeriesEndpointAsync(ApplicationDbContext db, string? userId = null, string[]? userIds = null,
        string[]? models = null, string[]? groupIds = null) =>
        CreateController(db).GetAiTelemetrySeries(new DateTime(2026, 9, 2), new DateTime(2026, 9, 2), null, null, null, null,
            userId, userIds, models, groupIds, Service(db));

    [Fact]
    public async Task GetAiTelemetrySeries_RefusesMoreThan1100DayBuckets()
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetrySeries(new DateTime(2020, 1, 1), new DateTime(2026, 1, 1),
            null, null, "day", null, null, null, null, null, Service(db));

        Assert.Equal("The range has too many buckets; choose a coarser bucket.", Message(result));
    }

    [Fact]
    public async Task GetAiTelemetrySeries_AcceptsTheSameRangeByMonth()
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetrySeries(new DateTime(2020, 1, 1), new DateTime(2026, 1, 1),
            null, null, "month", null, null, null, null, null, Service(db));

        var dto = Assert.IsType<AiTelemetrySeriesDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("month", dto.Bucket);
        Assert.Equal(73, dto.BucketStartsUtc.Count);
    }

    [Fact]
    public async Task GetAiTelemetrySeries_RefusesMoreThanFiftyChoicesInACategory()
    {
        using var db = NewDb();
        var fiftyOne = Enumerable.Range(1, 51).ToArray();

        Assert.Equal("At most 50 users can be charted at once.",
            Message(await SeriesEndpointAsync(db, userIds: fiftyOne.Select(i => $"u{i}").ToArray())));
        Assert.Equal("At most 50 models can be charted at once.",
            Message(await SeriesEndpointAsync(db, models: fiftyOne.Select(i => $"OpenAI|m{i}").ToArray())));
        Assert.Equal("At most 50 groups can be charted at once.",
            Message(await SeriesEndpointAsync(db, groupIds: fiftyOne.Select(i => i.ToString()).ToArray())));
    }

    [Fact]
    public async Task GetAiTelemetrySeries_CountsTheCapAfterRemovingDuplicates()
    {
        using var db = NewDb();
        var fifty = Enumerable.Range(1, 50).Select(i => $"u{i}").ToArray();

        var result = await SeriesEndpointAsync(db, userId: "u1", userIds: fifty.Concat(new[] { "u2", "u50" }).ToArray());

        var dto = Assert.IsType<AiTelemetrySeriesDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(50, dto.Users.Count);
    }

    [Theory]
    [InlineData("gpt-x")]
    [InlineData("|gpt-x")]
    [InlineData("OpenAI|")]
    public async Task GetAiTelemetrySeries_RefusesAMalformedModel(string model)
    {
        using var db = NewDb();

        Assert.Equal("Each model is provider|modelId.", Message(await SeriesEndpointAsync(db, models: new[] { "OpenAI|gpt-y", model })));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("1.5")]
    public async Task GetAiTelemetrySeries_RefusesAMalformedGroup(string group)
    {
        using var db = NewDb();

        Assert.Equal("Each group is a group id or none.", Message(await SeriesEndpointAsync(db, groupIds: new[] { "3", group })));
    }

    [Fact]
    public async Task GetAiTelemetrySeries_MergesUserIdIntoUserIds_AndRemovesDuplicatesInOrder()
    {
        using var db = NewDb();
        db.Groups.Add(new Group { Id = 3, DisplayName = "Gamma", CreatedAtUtc = Utc(2026, 1, 1) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SeriesEndpointAsync(db, userId: "u1", userIds: new[] { "u2", "u1", " ", "u3" },
            models: new[] { "Ollama|llama3:8b", "Ollama|llama3:8b", "OpenAI|gpt-x" },
            groupIds: new[] { "none", "3", "NONE", "3" });

        var dto = Assert.IsType<AiTelemetrySeriesDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(new[] { "u1", "u2", "u3" }, dto.Users.Select(u => u.UserId));
        Assert.Equal(new[] { "Ollama|llama3:8b", "OpenAI|gpt-x" }, dto.Models.Select(m => m.Provider + "|" + m.ModelId));
        Assert.Equal(new long?[] { null, 3 }, dto.Groups.Select(g => g.GroupId));
        Assert.Equal("Gamma", dto.Groups[1].GroupName);
    }
}
