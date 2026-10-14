using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Security;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// /api/session/create, which GnollHack calls with its stored credentials and retries: its failures count toward the
/// game-style throttle, never toward Identity lockout; the pair limit leaves the player's own address alone; a banned
/// or unconfirmed account is told so only with the correct password and is not counted; every refusal is a 401 with a
/// code. The real <see cref="GnollHackSignInManager"/> over SQLite in memory.
/// </summary>
public sealed class SessionCreateLockoutTests : IDisposable
{
    private const string Secret = "test-antiforgery-token";
    private const string Password = "Correct-Horse-9";
    private const string WrongPassword = "Wrong-Horse-9";
    private const string Ip = "203.0.113.7";
    private const string OtherIp = "198.51.100.1";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        public List<SecurityEventEntry> Entries { get; } = new();

        public void Record(SecurityEventEntry entry)
        {
            lock (Entries) Entries.Add(entry);
        }
    }

    public SessionCreateLockoutTests()
    {
        _connection = SqliteSchemaTemplate.OpenCopy();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1);
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.SignIn.RequireConfirmedEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 10;
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<GnollHackSignInManager>();
        services.AddAuthenticationCore();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _signInManager = _scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AntiForgeryToken", Secret },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();
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

    private SessionController CreateController(string ip)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        return new SessionController(
            _signInManager, _db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), _config,
            null!, new OngoingChatManager(_config), new ChatRetentionService(_db, _config, NullLogger<ChatRetentionService>.Instance),
            new ConfidentialPolicyResolver(_config), new ContentProtectionService(new ConfigurationContentKeyRing(_config)),
            NullLogger<SessionController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static CreateSessionRequest Request(string userName, string password) => new()
    {
        UserName = userName,
        Password = password,
        AntiForgeryToken = Secret,
        SnapshotText = "Dlvl 3",
        IsGnollHackSession = true
    };

    private async Task<IActionResult> CreateAsync(string userName, string password, string ip, GameApiSignInThrottle? throttle,
        RecordingSecurityEvents? events = null)
        => await CreateController(ip).Create(Request(userName, password), securityEvents: events, signInThrottle: throttle);

    private static string? Code(IActionResult result)
    {
        var refused = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
        return refused.Value!.GetType().GetProperty("code")!.GetValue(refused.Value) as string;
    }

    private async Task<ApplicationUser> ReloadAsync(string userId)
        => await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TwentyWrongPasswordsNeverTouchIdentityLockout()
    {
        var user = await CreateUserAsync("retrying_player");

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal("invalid_credentials", Code(await CreateAsync(user.UserName!, WrongPassword, Ip, throttle: null)));
        }

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Null(stored.LockoutEnd);

        Assert.IsType<OkObjectResult>(await CreateAsync(user.UserName!, Password, Ip, throttle: null));
    }

    [Fact]
    public async Task TwentyWrongPasswordsThroughTheThrottleNeverTouchIdentityLockoutEither()
    {
        var user = await CreateUserAsync("throttled_player");
        var throttle = new GameApiSignInThrottle();

        for (int i = 0; i < 20; i++)
        {
            await CreateAsync(user.UserName!, WrongPassword, Ip, throttle);
        }

        var stored = await ReloadAsync(user.Id);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Null(stored.LockoutEnd);
    }

    [Fact]
    public async Task TheThrottleRefusesOneAddressAfterTenWhileAnotherAddressWithTheRightPasswordSucceeds()
    {
        var user = await CreateUserAsync("targeted_player");
        var throttle = new GameApiSignInThrottle();
        var events = new RecordingSecurityEvents();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("invalid_credentials", Code(await CreateAsync(user.UserName!, WrongPassword, Ip, throttle)));
        }

        // The eleventh is refused before the password is looked at, even when it is correct.
        Assert.Equal("throttled", Code(await CreateAsync(user.UserName!, Password, Ip, throttle, events)));
        Assert.Contains(events.Entries, e => e.Kind == SecurityEventKind.RateLimitRejected && e.Code == "session-create");

        Assert.IsType<OkObjectResult>(await CreateAsync(user.UserName!, Password, OtherIp, throttle));
        Assert.Single(await _db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownNamesCountTowardTheThrottle()
    {
        var throttle = new GameApiSignInThrottle();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("invalid_credentials", Code(await CreateAsync("nobody_here", WrongPassword, Ip, throttle)));
        }

        Assert.Equal("throttled", Code(await CreateAsync("nobody_here", WrongPassword, Ip, throttle)));
    }

    [Fact]
    public async Task ABannedUserWithTheRightPasswordIsToldBannedAndIsNotCounted()
    {
        var user = await CreateUserAsync("banned_player", isBanned: true);
        var throttle = new GameApiSignInThrottle();
        var events = new RecordingSecurityEvents();

        for (int i = 0; i < 15; i++)
        {
            var result = await CreateAsync(user.UserName!, Password, Ip, throttle, events);
            Assert.Equal("banned", Code(result));
            var value = ((UnauthorizedObjectResult)result).Value!;
            Assert.Equal("This account has been banned.", value.GetType().GetProperty("message")!.GetValue(value));
        }

        Assert.True(throttle.Check(user.UserName!, Ip).Allowed);
        Assert.DoesNotContain(events.Entries, e => e.Kind == SecurityEventKind.LoginFailed);
        Assert.Equal(0, (await ReloadAsync(user.Id)).AccessFailedCount);
        Assert.Empty(await _db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABannedUserWithAWrongPasswordIsToldNothingAndIsCounted()
    {
        var user = await CreateUserAsync("banned_target", isBanned: true);
        var throttle = new GameApiSignInThrottle();
        var events = new RecordingSecurityEvents();

        Assert.Equal("invalid_credentials", Code(await CreateAsync(user.UserName!, WrongPassword, Ip, throttle, events)));

        Assert.Contains(events.Entries, e => e.Kind == SecurityEventKind.LoginFailed && e.UserId == user.Id);
        for (int i = 0; i < 9; i++)
        {
            await CreateAsync(user.UserName!, WrongPassword, Ip, throttle);
        }
        Assert.False(throttle.Check(user.UserName!, Ip).Allowed);
    }

    [Fact]
    public async Task AnUnconfirmedUserWithTheRightPasswordIsToldToConfirm()
    {
        var user = await CreateUserAsync("unconfirmed_player", emailConfirmed: false);
        var throttle = new GameApiSignInThrottle();

        Assert.Equal("email_not_confirmed", Code(await CreateAsync(user.UserName!, Password, Ip, throttle)));
        Assert.Equal("invalid_credentials", Code(await CreateAsync(user.UserName!, WrongPassword, Ip, throttle)));
    }

    [Fact]
    public async Task ALockedOutUserIsStillRefused()
    {
        var user = await CreateUserAsync("locked_player");
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        var events = new RecordingSecurityEvents();

        Assert.Equal("locked_out", Code(await CreateAsync(user.UserName!, Password, Ip, new GameApiSignInThrottle(), events)));

        Assert.Contains(events.Entries, e => e.Kind == SecurityEventKind.LoginLockedOut && e.UserId == user.Id);
        Assert.Empty(await _db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASuccessStampsLastSignInAndClearsThePairsFailures()
    {
        var user = await CreateUserAsync("returning_player");
        var throttle = new GameApiSignInThrottle();

        for (int i = 0; i < 9; i++)
        {
            await CreateAsync(user.UserName!, WrongPassword, Ip, throttle);
        }
        Assert.IsType<OkObjectResult>(await CreateAsync(user.UserName!, Password, Ip, throttle));
        Assert.NotNull((await ReloadAsync(user.Id)).LastSignInUtc);

        for (int i = 0; i < 9; i++)
        {
            await CreateAsync(user.UserName!, WrongPassword, Ip, throttle);
        }
        Assert.True(throttle.Check(user.UserName!, Ip).Allowed);
    }
}
