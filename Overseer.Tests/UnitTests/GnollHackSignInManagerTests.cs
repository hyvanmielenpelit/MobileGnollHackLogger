using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The sign-in manager both applications register: a banned account is refused on every password path without its
/// password being checked or counted, and a completed sign-in stamps LastSignInUtc at most once an hour
/// (<see cref="AccountActivity"/>). SQLite in memory, because the stamp is a set-based update the in-memory provider
/// cannot translate.
/// </summary>
public sealed class GnollHackSignInManagerTests : IDisposable
{
    private const string Password = "Correct-Horse-9";
    private const string WrongPassword = "Wrong-Horse-9";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    /// <summary>Accepts every sign-in without writing a cookie.</summary>
    private sealed class NoopAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    /// <summary>Keeps the warnings it is given.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    public GnollHackSignInManagerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.SignIn.RequireConfirmedEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 10;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<GnollHackSignInManager>();
        services.AddAuthenticationCore();
        services.AddSingleton<IAuthenticationService, NoopAuthenticationService>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _signInManager = _scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        // Set on the manager rather than the accessor, whose async-local value does not reach the test method.
        _signInManager.Context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task<ApplicationUser> CreateUserAsync(string userName, bool isBanned = false, bool emailConfirmed = true)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = userName + "@example.com",
            EmailConfirmed = emailConfirmed,
            IsBanned = isBanned
        };
        var created = await _userManager.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<ApplicationUser> ReloadAsync(string userId)
        => await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);

    private async Task SetLastSignInAsync(string userId, DateTime? value)
        => await _db.Users.OfType<ApplicationUser>().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSignInUtc, value), TestContext.Current.CancellationToken);

    [Fact]
    public void TheRegisteredSignInManagerIsTheGnollHackOne()
    {
        Assert.IsType<GnollHackSignInManager>(_signInManager);
    }

    [Fact]
    public async Task ABannedUserWithTheCorrectPasswordIsNotAllowed()
    {
        var user = await CreateUserAsync("banned_player", isBanned: true);

        var result = await _signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);

        Assert.True(result.IsNotAllowed);
        Assert.False(result.Succeeded);
        Assert.Null((await ReloadAsync(user.Id)).LastSignInUtc);
    }

    [Fact]
    public async Task ABannedUserIsRefusedBeforeThePasswordIsCheckedOrCounted()
    {
        var user = await CreateUserAsync("banned_guesser", isBanned: true);

        for (int i = 0; i < 12; i++)
        {
            var result = await _signInManager.CheckPasswordSignInAsync(user, WrongPassword, lockoutOnFailure: true);
            Assert.True(result.IsNotAllowed);
        }

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Null(stored.LockoutEnd);
    }

    [Fact]
    public async Task ABannedUserIsNotAllowedOnTheGamePasswordCheck()
    {
        var user = await CreateUserAsync("banned_gamer", isBanned: true);

        var result = await _signInManager.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: false);

        Assert.True(result.IsNotAllowed);
        Assert.True(await _userManager.CheckPasswordAsync(user, Password));
    }

    [Fact]
    public async Task TheBaseRulesStillApplyToAnUnbannedUser()
    {
        var user = await CreateUserAsync("unconfirmed_player", emailConfirmed: false);

        var result = await _signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: false);

        Assert.True(result.IsNotAllowed);
    }

    [Fact]
    public async Task IsBannedFalseOrUnsetSignsIn()
    {
        var unset = await CreateUserAsync("never_banned");
        var cleared = await CreateUserAsync("unbanned");
        cleared.IsBanned = false;
        await _userManager.UpdateAsync(cleared);

        Assert.True((await _signInManager.PasswordSignInAsync(unset, Password, false, false)).Succeeded);
        Assert.True((await _signInManager.PasswordSignInAsync(cleared, Password, false, false)).Succeeded);
    }

    [Fact]
    public async Task ASignInStampsLastSignInUtc()
    {
        var user = await CreateUserAsync("player_one");
        DateTime before = DateTime.UtcNow.AddSeconds(-1);

        var result = await _signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);

        Assert.True(result.Succeeded);
        DateTime? stamped = (await ReloadAsync(user.Id)).LastSignInUtc;
        Assert.NotNull(stamped);
        Assert.InRange(stamped!.Value, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task ASecondSignInWithinTheHourLeavesTheStampAlone()
    {
        var user = await CreateUserAsync("player_two");
        DateTime recent = DateTime.UtcNow.AddMinutes(-30);
        await SetLastSignInAsync(user.Id, recent);

        await _signInManager.SignInAsync(user, isPersistent: false);

        Assert.Equal(recent, (await ReloadAsync(user.Id)).LastSignInUtc);
    }

    [Fact]
    public async Task ASignInMoreThanAnHourLaterMovesTheStamp()
    {
        var user = await CreateUserAsync("player_three");
        DateTime old = DateTime.UtcNow.AddHours(-2);
        await SetLastSignInAsync(user.Id, old);

        await _signInManager.SignInAsync(user, isPersistent: false);

        Assert.True((await ReloadAsync(user.Id)).LastSignInUtc > old.AddHours(1));
    }

    [Fact]
    public async Task TheGamePasswordCheckDoesNotStamp()
    {
        // CheckPasswordSignInAsync issues no cookie; the game APIs and session create stamp for themselves.
        var user = await CreateUserAsync("player_four");

        Assert.True((await _signInManager.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: false)).Succeeded);

        Assert.Null((await ReloadAsync(user.Id)).LastSignInUtc);
    }

    // ── AccountActivity ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TouchSetsAnUnsetStamp()
    {
        var user = await CreateUserAsync("touch_unset");
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

        await AccountActivity.TouchSignInAsync(_db, user.Id, now, ct: TestContext.Current.CancellationToken);

        Assert.Equal(now, (await ReloadAsync(user.Id)).LastSignInUtc);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(59, false)]
    [InlineData(60, false)]
    [InlineData(61, true)]
    [InlineData(600, true)]
    public async Task TouchWritesAtMostOnceAnHour(int minutesLater, bool moves)
    {
        var user = await CreateUserAsync("touch_hourly");
        var first = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var later = first.AddMinutes(minutesLater);
        await AccountActivity.TouchSignInAsync(_db, user.Id, first, ct: TestContext.Current.CancellationToken);

        await AccountActivity.TouchSignInAsync(_db, user.Id, later, ct: TestContext.Current.CancellationToken);

        Assert.Equal(moves ? later : first, (await ReloadAsync(user.Id)).LastSignInUtc);
    }

    [Fact]
    public async Task TouchLeavesTheConcurrencyStampAndOtherUsersAlone()
    {
        var user = await CreateUserAsync("touch_stamp");
        var other = await CreateUserAsync("touch_other");
        string? stampBefore = (await ReloadAsync(user.Id)).ConcurrencyStamp;

        await AccountActivity.TouchSignInAsync(_db, user.Id, DateTime.UtcNow, ct: TestContext.Current.CancellationToken);

        var stored = await ReloadAsync(user.Id);
        Assert.NotNull(stored.LastSignInUtc);
        Assert.Equal(stampBefore, stored.ConcurrencyStamp);
        Assert.Null((await ReloadAsync(other.Id)).LastSignInUtc);
    }

    [Fact]
    public async Task TouchOfAnUnknownUserDoesNothing()
    {
        await AccountActivity.TouchSignInAsync(_db, "no-such-user", DateTime.UtcNow, ct: TestContext.Current.CancellationToken);
        await AccountActivity.TouchSignInAsync(_db, string.Empty, DateTime.UtcNow, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TouchNeverThrowsAndLogsTheFailure()
    {
        // The in-memory provider cannot run a set-based update, which stands in for a database failure.
        using var inMemory = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var logger = new ListLogger();

        await AccountActivity.TouchSignInAsync(inMemory, "some-user", DateTime.UtcNow, logger, TestContext.Current.CancellationToken);

        Assert.Single(logger.Warnings);
    }
}
