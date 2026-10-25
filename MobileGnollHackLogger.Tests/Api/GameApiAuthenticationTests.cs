using System.Globalization;
using System.Net;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// The sign-in shared by the four game API POST endpoints (<c>GameApiAuthenticator</c>): antiforgery check, throttle,
/// password check and the refusals, with the request-log rows and security events they leave.
/// </summary>
public class GameApiAuthenticationTests : IClassFixture<WebsiteWebApplicationFactory>,
    IClassFixture<GameApiAuthenticationTests.ThrottleFactory>
{
    private const string SaveFileTrackingCreatePath = "/api/savefiletracking/create";

    private static int _ipCounter;

    private readonly WebsiteWebApplicationFactory _factory;
    private readonly ThrottleFactory _throttleFactory;

    public GameApiAuthenticationTests(WebsiteWebApplicationFactory factory, ThrottleFactory throttleFactory)
    {
        _factory = factory;
        _throttleFactory = throttleFactory;
    }

    /// <summary>The website with a pair failure limit of 3, so the sign-in throttle is reached in three requests.</summary>
    public class ThrottleFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:PairFailureLimit"] = "3";
                return settings;
            }
        }
    }

    /// <summary>Each endpoint's path and the route name its security events carry after "game-api:".</summary>
    public static TheoryData<string, string> Endpoints => new()
    {
        { "/xlogfile", "xlogfile" },
        { "/bones", "bones" },
        { SaveFileTrackingCreatePath, "savefiletracking" },
        { "/api/replay", "replay" },
    };

    private static readonly string[] Paths = ["/xlogfile", "/bones", SaveFileTrackingCreatePath, "/api/replay"];

    public static TheoryData<string> EndpointPaths => new(Paths);

    [Fact]
    public async Task XLogFile_TestConnectionWithCorrectCredentials_Returns200AndLogsSuccess()
    {
        string userName = WebsiteTestData.UniqueUserName("auth_ok");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, "/xlogfile", Form("/xlogfile", userName, WebsiteTestData.Password), NextIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await RequestLogsOfAsync(userName), r => r.SubType == RequestLogSubType.TestConnection);
        Assert.Equal(LogType.GameLog, row.Type);
        Assert.Equal(200, row.ResponseCode);
        Assert.True(row.LoginSucceeded);
        Assert.Equal("match", row.RequestAntiForgeryToken);
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_WrongAntiForgeryToken_ReturnsMismatchAndLogsMismatch(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_token");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password, "wrong-token"), NextIp());

        await AssertResponseAsync(response, GameApiSignInResponses.For(GameApiSignInOutcome.AntiForgeryMismatch));
        var rows = await RequestLogsOfAsync(userName);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("mismatch", r.RequestAntiForgeryToken));
    }

    [Fact]
    public async Task RequestLogsAndSecurityEvents_AfterMixedRequests_NeverHoldPasswordOrAntiForgerySecret()
    {
        const string probePassword = "Leak-Probe-Passw0rd-9q7z";
        using var client = _factory.CreateGameClient();

        foreach (string path in Paths)
        {
            string userName = WebsiteTestData.UniqueUserName("auth_leak");
            await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
            string ip = NextIp();

            await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password), ip);
            await PostAsync(client, path, Form(path, userName, probePassword), ip);
            await PostAsync(client, path, Form(path, userName, probePassword, "wrong-token"), ip);
            await PostAsync(client, path, Form(path, WebsiteTestData.UniqueUserName("auth_leak_none"), probePassword), ip);
        }

        // A request that fails model validation, and one with partial game data.
        string partialUser = WebsiteTestData.UniqueUserName("auth_leak_partial");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, partialUser));
        await PostAsync(client, "/xlogfile", new MultipartFormDataContent
        {
            { new StringContent(partialUser), "UserName" },
            { new StringContent(probePassword), "Password" }
        }, NextIp());
        await PostAsync(client, "/xlogfile", WebsiteTestData.GameApiForm(partialUser, WebsiteTestData.Password,
            fields: new Dictionary<string, string> { ["XLogEntry"] = WebsiteTestData.XLogEntry(partialUser, 100, 1_700_000_000) }), NextIp());

        var (requestLogs, securityEvents) = await _factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var logs = await db.RequestLogs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            var events = await db.SecurityEvents.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            return (logs, events);
        });

        Assert.NotEmpty(requestLogs);
        Assert.NotEmpty(securityEvents);
        string[] secrets = [probePassword, WebsiteTestData.Password, WebsiteWebApplicationFactory.AntiForgeryToken];
        var leaks = StringColumns("RequestLogs", requestLogs).Concat(StringColumns("SecurityEvents", securityEvents))
            .Where(c => secrets.Any(s => c.Value.Contains(s, StringComparison.Ordinal)))
            .Select(c => c.Column + ": " + c.Value)
            .ToList();
        Assert.Empty(leaks);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Post_WrongPassword_Returns403FailedAndRecordsLoginFailed(string path, string route)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_wrongpw");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        var response = await PostAsync(client, path, Form(path, userName, "Wrong-Passw0rd"), ip);

        var failed = GameApiSignInResponses.For(GameApiSignInOutcome.Failed);
        Assert.Equal(403, failed.StatusCode);
        await AssertResponseAsync(response, failed);
        var securityEvent = Assert.Single(await SecurityEventsFromAsync(_factory, ip), e => e.Kind == SecurityEventKind.LoginFailed);
        Assert.Equal("game-api:" + route, securityEvent.Code);
        Assert.Equal(user.Id, securityEvent.AspNetUserId);
        Assert.Null(securityEvent.Detail);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Post_UnknownUser_RespondsAsWrongPasswordAndRecordsUnknownUser(string path, string route)
    {
        string existingUser = WebsiteTestData.UniqueUserName("auth_known");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, existingUser));
        string unknownUser = WebsiteTestData.UniqueUserName("auth_unknown");
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        var wrongPassword = await PostAsync(client, path, Form(path, existingUser, "Wrong-Passw0rd"), ip);
        var unknown = await PostAsync(client, path, Form(path, unknownUser, WebsiteTestData.Password), ip);

        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await AssertResponseAsync(unknown, GameApiSignInResponses.For(GameApiSignInOutcome.Failed));
        var securityEvent = Assert.Single(await SecurityEventsFromAsync(_factory, ip),
            e => e.Kind == SecurityEventKind.LoginFailed && e.AspNetUserId == null);
        Assert.Equal("game-api:" + route, securityEvent.Code);
        Assert.Equal("unknown-user", securityEvent.Detail);
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_UnconfirmedEmail_IsNamedOnlyAfterCorrectPassword(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_unconf");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName, u => u.EmailConfirmed = false));
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        var correct = await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password), ip);
        var wrong = await PostAsync(client, path, Form(path, userName, "Wrong-Passw0rd"), ip);

        await AssertResponseAsync(correct, GameApiSignInResponses.For(GameApiSignInOutcome.EmailNotConfirmed));
        await AssertResponseAsync(wrong, GameApiSignInResponses.For(GameApiSignInOutcome.Failed));
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_BannedUser_IsNamedOnlyAfterCorrectPassword(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_banned");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName, u => u.IsBanned = true));
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        var correct = await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password), ip);
        var wrong = await PostAsync(client, path, Form(path, userName, "Wrong-Passw0rd"), ip);

        await AssertResponseAsync(correct, GameApiSignInResponses.For(GameApiSignInOutcome.Banned));
        await AssertResponseAsync(wrong, GameApiSignInResponses.For(GameApiSignInOutcome.Failed));
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_LockedOutUser_Returns403LockedOutWithRetryAfter(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_locked");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName,
            u => u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10)));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password), NextIp());

        int retryAfter = RetryAfterSeconds(response);
        Assert.True(retryAfter > 0);
        var expected = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut, TimeSpan.FromSeconds(retryAfter));
        Assert.Equal(403, expected.StatusCode);
        await AssertResponseAsync(response, expected);
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_TwoFactorEnabled_Returns412RequiresTwoFactor(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_2fa");
        await _factory.WithScopeAsync(async sp =>
        {
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await userManager.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        });
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, path, Form(path, userName, WebsiteTestData.Password), NextIp());

        var expected = GameApiSignInResponses.For(GameApiSignInOutcome.RequiresTwoFactor);
        Assert.Equal(412, expected.StatusCode);
        await AssertResponseAsync(response, expected);
    }

    [Theory]
    [MemberData(nameof(EndpointPaths))]
    public async Task Post_WrongPasswords_NeverTouchIdentityLockout(string path)
    {
        string userName = WebsiteTestData.UniqueUserName("auth_nolock");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        for (int i = 0; i < 3; i++)
        {
            var response = await PostAsync(client, path, Form(path, userName, "Wrong-Passw0rd"), ip);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var stored = await FindUserAsync(_factory, userName);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Null(stored.LockoutEnd);
    }

    [Fact]
    public async Task XLogFile_SuccessfulSignIn_SetsLastSignInUtc()
    {
        string userName = WebsiteTestData.UniqueUserName("auth_lastsign");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        Assert.Null((await FindUserAsync(_factory, userName)).LastSignInUtc);
        using var client = _factory.CreateGameClient();
        DateTime before = DateTime.UtcNow;

        var response = await PostAsync(client, "/xlogfile", Form("/xlogfile", userName, WebsiteTestData.Password), NextIp());

        DateTime after = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DateTime? lastSignIn = (await FindUserAsync(_factory, userName)).LastSignInUtc;
        Assert.NotNull(lastSignIn);
        Assert.InRange(lastSignIn.Value, before.AddSeconds(-5), after.AddSeconds(5));
    }

    [Fact]
    public async Task XLogFile_PairReachesFailureLimit_ThrottlesCorrectPasswordFromThatIpOnly()
    {
        string userName = WebsiteTestData.UniqueUserName("auth_throttle");
        await _throttleFactory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _throttleFactory.CreateGameClient();
        string throttledIp = NextIp();
        string otherIp = NextIp();

        for (int i = 0; i < 3; i++)
        {
            var failed = await PostAsync(client, "/xlogfile", Form("/xlogfile", userName, "Wrong-Passw0rd"), throttledIp);
            await AssertResponseAsync(failed, GameApiSignInResponses.For(GameApiSignInOutcome.Failed));
        }

        var throttled = await PostAsync(client, "/xlogfile", Form("/xlogfile", userName, WebsiteTestData.Password), throttledIp);

        int retryAfter = RetryAfterSeconds(throttled);
        Assert.True(retryAfter > 0);
        var expected = GameApiSignInResponses.For(GameApiSignInOutcome.Throttled, TimeSpan.FromSeconds(retryAfter));
        Assert.Equal(403, expected.StatusCode);
        await AssertResponseAsync(throttled, expected);
        var securityEvent = Assert.Single(await SecurityEventsFromAsync(_throttleFactory, throttledIp),
            e => e.Kind == SecurityEventKind.RateLimitRejected);
        Assert.Equal("game-api-signin", securityEvent.Code);
        Assert.Equal("pair", securityEvent.Detail);

        var fromOtherIp = await PostAsync(client, "/xlogfile", Form("/xlogfile", userName, WebsiteTestData.Password), otherIp);

        Assert.Equal(HttpStatusCode.OK, fromOtherIp.StatusCode);
    }

    /// <summary>A game API form; <c>api/savefiletracking/create</c> also gets the fields its model validation requires.</summary>
    private static MultipartFormDataContent Form(string path, string userName, string password,
        string token = WebsiteWebApplicationFactory.AntiForgeryToken) =>
        WebsiteTestData.GameApiForm(userName, password, token,
            path == SaveFileTrackingCreatePath ? WebsiteTestData.SaveFileTrackingFields() : null);

    /// <summary>A documentation address (2001:db8::/32) no other test uses, in the form the server prints it.</summary>
    private static string NextIp() =>
        IPAddress.Parse("2001:db8:a1::" + Interlocked.Increment(ref _ipCounter).ToString("x", CultureInfo.InvariantCulture)).ToString();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, HttpContent content, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, ip);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertResponseAsync(HttpResponseMessage response, GameApiResponse expected)
    {
        Assert.Equal(expected.StatusCode, (int)response.StatusCode);
        Assert.Equal(expected.Body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static int RetryAfterSeconds(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values), "The response has no Retry-After header.");
        return int.Parse(Assert.Single(values), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private Task<List<RequestInfo>> RequestLogsOfAsync(string userName) => _factory.WithScopeAsync(sp =>
        sp.GetRequiredService<ApplicationDbContext>().RequestLogs.AsNoTracking()
            .Where(r => r.RequestUserName == userName)
            .ToListAsync(TestContext.Current.CancellationToken));

    private static Task<List<SecurityEvent>> SecurityEventsFromAsync(WebsiteWebApplicationFactory factory, string ip) =>
        factory.WithScopeAsync(sp =>
            sp.GetRequiredService<ApplicationDbContext>().SecurityEvents.AsNoTracking()
                .Where(e => e.IpAddress == ip)
                .ToListAsync(TestContext.Current.CancellationToken));

    /// <summary>The user as stored, read through a fresh context.</summary>
    private static async Task<ApplicationUser> FindUserAsync(WebsiteWebApplicationFactory factory, string userName)
    {
        var user = await factory.WithScopeAsync(sp =>
            sp.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName));
        Assert.NotNull(user);
        return user;
    }

    /// <summary>Every non-null string property of <paramref name="rows"/>, named "Table.Column".</summary>
    private static IEnumerable<(string Column, string Value)> StringColumns<T>(string table, IEnumerable<T> rows)
    {
        var properties = typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string)).ToList();
        foreach (var row in rows)
        {
            foreach (var property in properties)
            {
                if (property.GetValue(row) is string value)
                {
                    yield return (table + "." + property.Name, value);
                }
            }
        }
    }
}
