using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// The Login page through a browser session: lockout, the refusals and the messages they show, the security events
/// they leave, and the return URL.
/// </summary>
public partial class LoginPageTests : IClassFixture<LoginPageTests.LockoutFactory>
{
    private const string LoginPath = BrowserSession.LoginPath;
    private const string ManagePath = "/Identity/Account/Manage";
    private const string LockoutPath = "/Identity/Account/Lockout";
    private const string InvalidLoginMessage = "Invalid login attempt.";
    private const string WrongPassword = "Wrong-Passw0rd";
    private const byte IpBlock = 0x20;

    public static TheoryData<string> OffSiteReturnUrls => new()
    {
        "https://evil.example.test/",
        "//evil.example.test/",
        "/\\evil.example.test/",
    };

    private readonly LockoutFactory _factory;

    public LoginPageTests(LockoutFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The website with a lockout after three failed sign-ins, and room for many account page requests per address.</summary>
    public class LockoutFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["IdentityLockout:MaxFailedAccessAttempts"] = "3";
                settings["GameApiSecurity:AccountPagePermitsPerMinute"] = "1000";
                return settings;
            }
        }
    }

    [Fact]
    public async Task Post_CorrectPassword_RedirectsToReturnUrlAndSignsIn()
    {
        string userName = WebsiteTestData.UniqueUserName("login_ok");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = NewSession();

        var response = await PostLoginAsync(session, userName, WebsiteTestData.Password, "/TopScores");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/TopScores", response.Headers.Location?.OriginalString);
        var manage = await session.GetAsync(ManagePath);
        Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
    }

    [Fact]
    public async Task Post_WrongPasswords_LockOutOnThirdAndRefuseCorrectPassword()
    {
        string userName = WebsiteTestData.UniqueUserName("login_lock");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = NewSession();

        for (int i = 0; i < 2; i++)
        {
            var failed = await PostLoginAsync(session, userName, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }
        Assert.Equal(2, (await FindUserAsync(userName)).AccessFailedCount);

        var third = await PostLoginAsync(session, userName, WrongPassword);

        Assert.Equal(HttpStatusCode.Redirect, third.StatusCode);
        Assert.Equal(LockoutPath, third.Headers.Location?.OriginalString);
        var stored = await FindUserAsync(userName);
        Assert.NotNull(stored.LockoutEnd);
        Assert.True(stored.LockoutEnd > DateTimeOffset.UtcNow);
        // Identity resets the count when it locks the account out.
        Assert.Equal(0, stored.AccessFailedCount);

        var correct = await PostLoginAsync(session, userName, WebsiteTestData.Password);

        Assert.Equal(HttpStatusCode.Redirect, correct.StatusCode);
        Assert.Equal(LockoutPath, correct.Headers.Location?.OriginalString);
        var events = await SecurityEventsFromAsync(session.RemoteIp);
        var lockedOut = Assert.Single(events, e => e.Kind == SecurityEventKind.LoginLockedOut);
        Assert.Equal("website-login", lockedOut.Code);
    }

    [Fact]
    public async Task Post_UnknownUserAndWrongPassword_ShowTheSameSingleMessage()
    {
        string existingUser = WebsiteTestData.UniqueUserName("login_known");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, existingUser));
        string unknownUser = WebsiteTestData.UniqueUserName("login_unknown");
        using var session = NewSession();

        var wrongPassword = await PostLoginAsync(session, existingUser, WrongPassword);
        var unknown = await PostLoginAsync(session, unknownUser, WebsiteTestData.Password);

        // The bodies differ in the antiforgery token and the echoed user name, so only their messages are compared.
        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(InvalidLoginMessage, SingleMessage(await ReadBodyAsync(wrongPassword)));
        Assert.Equal(InvalidLoginMessage, SingleMessage(await ReadBodyAsync(unknown)));
        var events = await SecurityEventsFromAsync(session.RemoteIp);
        var unknownEvent = Assert.Single(events, e => e.Kind == SecurityEventKind.LoginFailed && e.Detail == "unknown-user");
        Assert.Null(unknownEvent.AspNetUserId);
        Assert.Equal("website-login", unknownEvent.Code);
    }

    [Fact]
    public async Task Post_BannedUser_IsNamedOnlyAfterCorrectPassword()
    {
        string userName = WebsiteTestData.UniqueUserName("login_banned");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName, u => u.IsBanned = true));
        using var session = NewSession();

        var correct = await PostLoginAsync(session, userName, WebsiteTestData.Password);
        var wrong = await PostLoginAsync(session, userName, WrongPassword);

        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.StartsWith("This account has been banned.", SingleMessage(await ReadBodyAsync(correct)));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Equal(InvalidLoginMessage, SingleMessage(await ReadBodyAsync(wrong)));
        Assert.Equal(1, (await FindUserAsync(userName)).AccessFailedCount);
    }

    [Fact]
    public async Task Post_UnconfirmedUserWithCorrectPassword_ShowsInvalidAttemptAndIssuesNoCookie()
    {
        string userName = WebsiteTestData.UniqueUserName("login_unconf");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName, u => u.EmailConfirmed = false));
        using var session = NewSession();

        var response = await PostLoginAsync(session, userName, WebsiteTestData.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InvalidLoginMessage, SingleMessage(await ReadBodyAsync(response)));
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : Enumerable.Empty<string>();
        Assert.DoesNotContain(setCookies, c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OffSiteReturnUrls))]
    public async Task Post_OffSiteReturnUrl_IsNeverRedirectedTo(string returnUrl)
    {
        string userName = WebsiteTestData.UniqueUserName("login_ret");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = NewSession();

        var response = await PostLoginAsync(session, userName, WebsiteTestData.Password, returnUrl);

        // LocalRedirect throws on a non-local URL, so the response is the error page.
        AssertNotRedirectedOffSite(response);
    }

    [Theory]
    [MemberData(nameof(OffSiteReturnUrls))]
    public async Task Logout_OffSiteReturnUrl_IsNeverRedirectedTo(string returnUrl)
    {
        string userName = WebsiteTestData.UniqueUserName("logout_ret");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = NewSession();
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var response = await session.PostFormAsync("/Identity/Account/Logout?returnUrl=" + Uri.EscapeDataString(returnUrl),
            new Dictionary<string, string>(), tokenFromPath: "/Identity/Account/Manage/Email");

        AssertNotRedirectedOffSite(response);
    }

    [Fact]
    public async Task FailedLogin_NeverStoresOrLogsProbePasswordOrUnknownUserName()
    {
        const string probePassword = "Leak-Probe-Login-Passw0rd-4m8x";
        string probeUser = WebsiteTestData.UniqueUserName("login_leak");
        using var session = NewSession();

        var response = await PostLoginAsync(session, probeUser, probePassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (requestLogs, securityEvents) = await _factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var logs = await db.RequestLogs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            var events = await db.SecurityEvents.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            return (logs, events);
        });

        Assert.NotEmpty(securityEvents);
        var passwordLeaks = StringColumns("RequestLogs", requestLogs).Concat(StringColumns("SecurityEvents", securityEvents))
            .Where(c => c.Value.Contains(probePassword, StringComparison.Ordinal))
            .Select(c => c.Column + ": " + c.Value)
            .ToList();
        Assert.Empty(passwordLeaks);
        var userNameLeaks = StringColumns("SecurityEvents", securityEvents)
            .Where(c => c.Value.Contains(probeUser, StringComparison.Ordinal))
            .Select(c => c.Column + ": " + c.Value)
            .ToList();
        Assert.Empty(userNameLeaks);
        Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(probePassword, StringComparison.Ordinal));
    }

    private BrowserSession NewSession() => new(_factory, BrowserSession.NextIp(IpBlock));

    /// <summary>Posts the Login form; <paramref name="returnUrl"/> goes in the query string, URL-encoded.</summary>
    private static Task<HttpResponseMessage> PostLoginAsync(BrowserSession session, string userName, string password,
        string? returnUrl = null)
    {
        string path = returnUrl == null ? LoginPath : LoginPath + "?returnUrl=" + Uri.EscapeDataString(returnUrl);
        return session.PostFormAsync(path, new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Password"] = password
        });
    }

    /// <summary>Not a redirect, or a redirect whose target stays on this site.</summary>
    private static void AssertNotRedirectedOffSite(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        if (status < 300 || status >= 400)
        {
            return;
        }
        string location = response.Headers.Location?.OriginalString ?? "";
        Assert.False(location.StartsWith("//", StringComparison.Ordinal) || location.StartsWith("/\\", StringComparison.Ordinal),
            "Redirected to " + location);
        var target = new Uri(new Uri("https://localhost"), location);
        Assert.Equal("localhost", target.Host);
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    /// <summary>The only validation message <paramref name="html"/> shows; fails when there is none or more than one.</summary>
    private static string SingleMessage(string html) => Assert.Single(ValidationMessages(html));

    /// <summary>The items of the rendered validation summary, then the text of every field validation error.</summary>
    private static List<string> ValidationMessages(string html)
    {
        var messages = new List<string>();
        var summary = ValidationSummaryRegex().Match(html);
        if (summary.Success)
        {
            foreach (Match item in ListItemRegex().Matches(summary.Groups[1].Value))
            {
                messages.Add(WebUtility.HtmlDecode(item.Groups[1].Value).Trim());
            }
        }
        foreach (Match field in FieldErrorRegex().Matches(html))
        {
            messages.Add(WebUtility.HtmlDecode(field.Groups[1].Value).Trim());
        }
        return messages;
    }

    /// <summary>The user as stored, read through a fresh context.</summary>
    private async Task<ApplicationUser> FindUserAsync(string userName)
    {
        var user = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName));
        Assert.NotNull(user);
        return user;
    }

    /// <summary>The events recorded for <paramref name="ip"/>, matched in the form the server prints the address.</summary>
    private Task<List<SecurityEvent>> SecurityEventsFromAsync(string ip)
    {
        string address = IPAddress.Parse(ip).ToString();
        return _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<ApplicationDbContext>().SecurityEvents.AsNoTracking()
                .Where(e => e.IpAddress == address)
                .ToListAsync(TestContext.Current.CancellationToken));
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

    [GeneratedRegex(@"<div\b[^>]*\bvalidation-summary-errors\b[^>]*>\s*<ul>(.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ValidationSummaryRegex();

    [GeneratedRegex(@"<li\b[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<span\b[^>]*\bfield-validation-error\b[^>]*>(.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FieldErrorRegex();
}
