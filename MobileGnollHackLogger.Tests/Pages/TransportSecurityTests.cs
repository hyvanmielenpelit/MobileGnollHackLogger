using System.Net;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// HTTPS redirection, HSTS, host filtering against the production <c>AllowedHosts</c> of appsettings.json, the host
/// a password reset link names, and the security headers on every response.
/// </summary>
public class TransportSecurityTests : IClassFixture<WebsiteWebApplicationFactory>,
    IClassFixture<TransportSecurityTests.HttpsPortFactory>
{
    private const string ProductionHost = "https://account.gnollhack.com";
    private const string EvilHost = "https://evil.example.test";
    private const string ForgotPasswordPath = "/Identity/Account/ForgotPassword";
    private const byte IpBlock = 0x24;

    /// <summary>The value in Overseer's <c>SecurityHeadersMiddleware</c>.</summary>
    private const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    private static long _startTimeUtc = 1_790_000_000;

    private readonly WebsiteWebApplicationFactory _factory;
    private readonly HttpsPortFactory _httpsPortFactory;

    public TransportSecurityTests(WebsiteWebApplicationFactory factory, HttpsPortFactory httpsPortFactory)
    {
        _factory = factory;
        _httpsPortFactory = httpsPortFactory;
    }

    /// <summary>The website with the HTTPS port <c>HttpsRedirectionMiddleware</c> reads, which the test server does not report.</summary>
    public class HttpsPortFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["https_port"] = "443";
                return settings;
            }
        }
    }

    [Fact]
    public async Task HttpGet_RedirectsToHttps()
    {
        using var session = new BrowserSession(_httpsPortFactory, NextIp(), "http://localhost");

        var response = await session.GetAsync("/");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task HttpsGet_OnProductionHost_CarriesHsts()
    {
        // HstsMiddleware skips localhost.
        using var session = new BrowserSession(_factory, NextIp(), ProductionHost);

        var response = await session.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(HeaderValues(response, "Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("account.gnollhack.com")]
    [InlineData("test-account.gnollhack.com")]
    [InlineData("localhost")]
    public async Task Get_AllowedHost_Returns200(string host)
    {
        using var session = new BrowserSession(_factory, NextIp(), "https://" + host);

        var response = await session.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownHost_Returns400()
    {
        using var session = new BrowserSession(_factory, NextIp(), EvilHost);

        var response = await session.GetAsync("/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_OnUnknownHost_Returns400AndSendsNoEmail()
    {
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("host_evil")));
        using var session = new BrowserSession(_factory, NextIp(), EvilHost);

        // The unknown host has no page to read a token from; host filtering answers before antiforgery runs.
        var response = await session.PostFormAsync(ForgotPasswordPath, new Dictionary<string, string> { ["Input.Email"] = user.Email! },
            includeToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(_factory.Emails.Sent, e => e.Email == user.Email);
    }

    [Fact]
    public async Task ForgotPassword_OnProductionHost_LinkNamesThatHost()
    {
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("host_prod")));
        using var session = new BrowserSession(_factory, NextIp(), ProductionHost);

        var response = await session.PostFormAsync(ForgotPasswordPath, new Dictionary<string, string> { ["Input.Email"] = user.Email! });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sent = Assert.Single(_factory.Emails.Sent, e => e.Email == user.Email);
        string link = BrowserSession.LinkIn(sent.HtmlMessage, "/Identity/Account/ResetPassword");
        Assert.StartsWith(ProductionHost + "/Identity/Account/ResetPassword", link);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Manage")]
    [InlineData("/xlogfile")]
    public async Task Get_CarriesSecurityHeaders(string path)
    {
        using var session = new BrowserSession(_factory, NextIp());

        var response = await session.GetAsync(path);

        Assert.Equal("nosniff", SingleHeader(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", SingleHeader(response, "X-Frame-Options"));
        Assert.Equal("frame-ancestors 'none'", SingleHeader(response, "Content-Security-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", SingleHeader(response, "Referrer-Policy"));
        Assert.Equal(PermissionsPolicy, SingleHeader(response, "Permissions-Policy"));
    }

    [Fact]
    public async Task Get_Dumplog_KeepsItsOwnSandboxPolicy()
    {
        long id = await SeedDumplogAsync();
        using var session = new BrowserSession(_factory, NextIp());

        var response = await session.GetAsync("/dumplog/" + id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sandbox; frame-ancestors 'none'", SingleHeader(response, "Content-Security-Policy"));
    }

    private static string NextIp() => BrowserSession.NextIp(IpBlock);

    /// <summary>The only value of <paramref name="name"/> on the response; fails when it is missing or repeated.</summary>
    private static string SingleHeader(HttpResponseMessage response, string name)
    {
        var values = HeaderValues(response, name);
        Assert.True(values.Count == 1, name + " has " + values.Count + " values: " + string.Join(" | ", values));
        return values[0];
    }

    /// <summary>Every value of <paramref name="name"/> on the response, from its headers and its content's headers.</summary>
    private static List<string> HeaderValues(HttpResponseMessage response, string name)
    {
        var values = new List<string>();
        if (response.Headers.TryGetValues(name, out var headerValues))
        {
            values.AddRange(headerValues);
        }
        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            values.AddRange(contentValues);
        }
        return values;
    }

    /// <summary>
    /// A game of a new user and its HTML dumplog at the path <c>DumpLogController</c> reads:
    /// <c>DumpLogPath\&lt;name&gt;\gnollhack.&lt;name&gt;.&lt;starttimeUTC&gt;.html</c>.
    /// </summary>
    private async Task<long> SeedDumplogAsync()
    {
        string userName = WebsiteTestData.UniqueUserName("headers_dumplog");
        long startUtc = Interlocked.Add(ref _startTimeUtc, 10_000);
        GameLog game = await _factory.WithScopeAsync(async sp =>
        {
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            return await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(userName, 100, startUtc));
        });

        string dir = Path.Combine(_factory.DumpLogPath, userName);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "gnollhack." + userName + "." + startUtc + ".html"),
            "<html><body>dumplog of " + userName + "</body></html>", TestContext.Current.CancellationToken);
        return game.Id;
    }
}
