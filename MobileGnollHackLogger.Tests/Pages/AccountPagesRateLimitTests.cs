using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// The <c>AccountPages</c> rate limit on the account pages, per remote address. It runs before the page, so a page that
/// fails without its prerequisites is still limited.
/// </summary>
public class AccountPagesRateLimitTests : IClassFixture<AccountPagesRateLimitTests.AccountPagesFactory>
{
    private const byte IpBlock = 0x23;

    private readonly AccountPagesFactory _factory;

    public AccountPagesRateLimitTests(AccountPagesFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The website with two account page requests per address a minute, and the <c>LogFile</c> setting the Register
    /// page model requires.
    /// </summary>
    public class AccountPagesFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:AccountPagePermitsPerMinute"] = "2";
                settings["LogFile"] = Path.Combine(TempRoot, "website.log");
                return settings;
            }
        }
    }

    /// <summary>
    /// Each limited page and what its GET answers within the limit. ResetPassword answers 400 without a code; the two
    /// two-factor pages redirect to Login without the two-factor cookie.
    /// </summary>
    public static TheoryData<string, HttpStatusCode> Pages => new()
    {
        { "/Identity/Account/Login", HttpStatusCode.OK },
        { "/Identity/Account/Register", HttpStatusCode.OK },
        { "/Identity/Account/ForgotPassword", HttpStatusCode.OK },
        { "/Identity/Account/ResendEmailConfirmation", HttpStatusCode.OK },
        { "/Identity/Account/ResetPassword?code=YQ", HttpStatusCode.OK },
        { "/Identity/Account/LoginWith2fa", HttpStatusCode.Redirect },
        { "/Identity/Account/LoginWithRecoveryCode", HttpStatusCode.Redirect },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Get_ThirdFromOneIpWithinMinute_Returns429AndOtherIpIsNotLimited(string path, HttpStatusCode allowedStatus)
    {
        using var session = new BrowserSession(_factory, BrowserSession.NextIp(IpBlock));
        using var otherSession = new BrowserSession(_factory, BrowserSession.NextIp(IpBlock));

        for (int i = 0; i < 2; i++)
        {
            var allowed = await session.GetAsync(path);
            Assert.Equal(allowedStatus, allowed.StatusCode);
        }

        var rejected = await session.GetAsync(path);

        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", rejected.Content.Headers.ContentType?.ToString());
        Assert.Equal("Too many requests. Please wait a minute and try again.",
            await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter), "The response has no Retry-After header.");
        Assert.True(int.TryParse(Assert.Single(retryAfter), NumberStyles.Integer, CultureInfo.InvariantCulture, out _));

        string address = IPAddress.Parse(session.RemoteIp).ToString();
        var events = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<ApplicationDbContext>().SecurityEvents.AsNoTracking()
                .Where(e => e.IpAddress == address)
                .ToListAsync(TestContext.Current.CancellationToken));
        var securityEvent = Assert.Single(events, e => e.Kind == SecurityEventKind.RateLimitRejected);
        Assert.Equal("AccountPages", securityEvent.Code);

        var fromOtherIp = await otherSession.GetAsync(path);

        Assert.Equal(allowedStatus, fromOtherIp.StatusCode);
    }
}
