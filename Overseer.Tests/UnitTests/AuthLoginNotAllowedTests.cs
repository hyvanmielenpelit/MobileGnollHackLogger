using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Overseer's /api/auth/login for an account Identity refuses before the password (banned, unconfirmed): the state is
/// told only with the correct password, a wrong password counts toward lockout as on any other account, and a
/// locked-out account is refused before any password check. The real <see cref="GnollHackSignInManager"/> over SQLite.
/// </summary>
public sealed class AuthLoginNotAllowedTests : IDisposable
{
    private const string Password = "Correct-Horse-9";
    private const string WrongPassword = "Wrong-Horse-9";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthLoginNotAllowedTests()
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

    private Task<IActionResult> LoginAsync(string userName, string password)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var controller = new AuthController(_signInManager, _userManager, _db)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller.Login(new LoginRequest { UserName = userName, Password = password });
    }

    private static string? Property(IActionResult result, string name)
    {
        var value = Assert.IsType<UnauthorizedObjectResult>(result).Value!;
        return value.GetType().GetProperty(name)?.GetValue(value)?.ToString();
    }

    private async Task<ApplicationUser> ReloadAsync(string userName)
    {
        _db.ChangeTracker.Clear();
        return (await _userManager.FindByNameAsync(userName))!;
    }

    [Fact]
    public async Task ABannedAccountIsToldSoOnlyWithTheCorrectPassword()
    {
        await CreateUserAsync("banned", isBanned: true);

        Assert.Equal("Invalid credentials.", Property(await LoginAsync("banned", WrongPassword), "message"));
        Assert.Equal("True", Property(await LoginAsync("banned", Password), "isBanned"));
    }

    [Fact]
    public async Task AWrongPasswordOnABannedAccountCountsTowardLockout()
    {
        await CreateUserAsync("banned", isBanned: true);

        for (int i = 0; i < 3; i++)
        {
            await LoginAsync("banned", WrongPassword);
        }

        Assert.Equal(3, (await ReloadAsync("banned")).AccessFailedCount);
    }

    [Fact]
    public async Task ALockedOutBannedAccountIsRefusedBeforeThePasswordIsChecked()
    {
        await CreateUserAsync("banned", isBanned: true);

        for (int i = 0; i < 10; i++)
        {
            await LoginAsync("banned", WrongPassword);
        }
        var user = await ReloadAsync("banned");
        Assert.True(await _userManager.IsLockedOutAsync(user));

        var result = await LoginAsync("banned", Password);
        Assert.Equal("True", Property(result, "isLockedOut"));
        Assert.Null(Property(result, "isBanned"));
    }

    [Fact]
    public async Task AnUnconfirmedAccountIsToldSoOnlyWithTheCorrectPasswordAndCountsWrongOnes()
    {
        await CreateUserAsync("unconfirmed", emailConfirmed: false);

        Assert.Equal("Invalid credentials.", Property(await LoginAsync("unconfirmed", WrongPassword), "message"));
        Assert.Equal("True", Property(await LoginAsync("unconfirmed", Password), "isNotAllowed"));
        Assert.Equal(1, (await ReloadAsync("unconfirmed")).AccessFailedCount);
    }
}
