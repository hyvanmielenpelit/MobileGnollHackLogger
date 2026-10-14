using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Telemetry;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="ActivityCounter"/> on SQLite, because it adds with ExecuteUpdate, which the in-memory provider rejects. A
/// named shared-cache in-memory database lets every scope open a connection of its own to the same data, as
/// concurrent requests would; one connection held for the test's lifetime keeps the database alive.
/// </summary>
public sealed class ActivityCounterTests : IDisposable
{
    private const string UserId = "activity-user";
    private static readonly DateTime Today = new(2026, 9, 10, 15, 30, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _keeper;
    private readonly ServiceProvider _services;

    public ActivityCounterTests()
    {
        string connectionString = $"Data Source=activity-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connectionString);
        _keeper.Open();
        SqliteSchemaTemplate.CopyInto(_keeper);

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connectionString));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "activity-user" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _keeper.Dispose();
    }

    private ActivityCounter Counter() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ActivityCounter>.Instance);

    private async Task<OverseerDailyActivity[]> RowsAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OverseerDailyActivities.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheFirstEventInsertsTheDaysRow_AndTheSecondAddsToIt()
    {
        var counter = Counter();

        await counter.TryAddAsync(UserId, new ActivityDelta(Chats: 1, ConfidentialChats: 1), Today);
        var afterFirst = Assert.Single(await RowsAsync());
        Assert.Equal(DateOnly.FromDateTime(Today), afterFirst.DateUtc);
        Assert.Equal(UserId, afterFirst.AspNetUserId);
        Assert.Equal(1, afterFirst.ChatsCreated);
        Assert.Equal(1, afterFirst.ConfidentialChatsCreated);

        await counter.TryAddAsync(UserId, new ActivityDelta(Chats: 1, GnollHackChats: 1, UserMessages: 2, AssistantMessages: 3), Today.AddHours(1));
        var afterSecond = Assert.Single(await RowsAsync());
        Assert.Equal(afterFirst.Id, afterSecond.Id);
        Assert.Equal(2, afterSecond.ChatsCreated);
        Assert.Equal(1, afterSecond.ConfidentialChatsCreated);
        Assert.Equal(1, afterSecond.GnollHackChatsCreated);
        Assert.Equal(2, afterSecond.UserMessages);
        Assert.Equal(3, afterSecond.AssistantMessages);
    }

    [Fact]
    public async Task ANewDayGetsARowOfItsOwn()
    {
        var counter = Counter();

        await counter.TryAddAsync(UserId, new ActivityDelta(UserMessages: 1), Today);
        await counter.TryAddAsync(UserId, new ActivityDelta(UserMessages: 1), Today.AddDays(1));

        var rows = (await RowsAsync()).OrderBy(r => r.DateUtc).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(DateOnly.FromDateTime(Today), rows[0].DateUtc);
        Assert.Equal(DateOnly.FromDateTime(Today).AddDays(1), rows[1].DateUtc);
        Assert.All(rows, r => Assert.Equal(1, r.UserMessages));
    }

    [Fact]
    public async Task TwoConcurrentFirstEventsOfADayLeaveOneRowWithBothIncrements()
    {
        var counter = Counter();

        await Task.WhenAll(
            Task.Run(() => counter.TryAddAsync(UserId, new ActivityDelta(Chats: 1), Today), TestContext.Current.CancellationToken),
            Task.Run(() => counter.TryAddAsync(UserId, new ActivityDelta(UserMessages: 1), Today), TestContext.Current.CancellationToken));

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(UserId, row.AspNetUserId);
        Assert.Equal(1, row.ChatsCreated);
        Assert.Equal(1, row.UserMessages);
    }

    [Fact]
    public async Task ANullUserCountsOnTheDaysAnonymousRow()
    {
        var counter = Counter();

        await counter.TryAddAsync(null, new ActivityDelta(IncognitoChats: 1), Today);
        await counter.TryAddAsync(null, new ActivityDelta(IncognitoUserMessages: 2), Today);
        await counter.TryAddAsync(UserId, new ActivityDelta(UserMessages: 1), Today);

        var rows = await RowsAsync();
        Assert.Equal(2, rows.Length);
        var anonymous = Assert.Single(rows, r => r.AspNetUserId == null);
        Assert.Equal(1, anonymous.IncognitoChatsCreated);
        Assert.Equal(2, anonymous.IncognitoUserMessages);
        Assert.Equal(0, anonymous.UserMessages);
        var user = Assert.Single(rows, r => r.AspNetUserId == UserId);
        Assert.Equal(0, user.IncognitoChatsCreated);
        Assert.Equal(1, user.UserMessages);
    }

    [Fact]
    public async Task AnEmptyDeltaWritesNothing()
    {
        await Counter().TryAddAsync(UserId, default, Today);

        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task AFailingContextIsSwallowed()
    {
        // A database without the schema: the UPDATE fails with "no such table".
        string connectionString = $"Data Source=activity-broken-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keeper = new SqliteConnection(connectionString);
        keeper.Open();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connectionString));
        using var provider = services.BuildServiceProvider();
        var counter = new ActivityCounter(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ActivityCounter>.Instance);

        var exception = await Record.ExceptionAsync(() => counter.TryAddAsync(UserId, new ActivityDelta(Chats: 1), Today));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AMissingContextRegistrationIsSwallowed()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var counter = new ActivityCounter(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ActivityCounter>.Instance);

        var exception = await Record.ExceptionAsync(() => counter.TryAddAsync(UserId, new ActivityDelta(Chats: 1), Today));

        Assert.Null(exception);
    }
}
