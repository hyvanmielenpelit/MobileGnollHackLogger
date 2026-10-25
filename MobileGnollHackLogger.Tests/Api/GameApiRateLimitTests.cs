using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// The <c>GameApiFlood</c> rate limit on the game API POST actions, per remote address. Every request carries its own
/// address: requests without one share the "unknown" partition, whose permits the first test would spend.
/// </summary>
public class GameApiRateLimitTests : IClassFixture<GameApiRateLimitTests.FloodFactory>
{
    private static int _ipCounter;

    private readonly FloodFactory _factory;

    public GameApiRateLimitTests(FloodFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The website with two game API POSTs per address a minute.</summary>
    public class FloodFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:FloodRequestsPerMinute"] = "2";
                return settings;
            }
        }
    }

    [Fact]
    public async Task XLogFilePost_ThirdFromOneIpWithinMinute_Returns429AndRecordsRejection()
    {
        string userName = WebsiteTestData.UniqueUserName("rl_flood");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        for (int i = 0; i < 2; i++)
        {
            var allowed = await PostTestConnectionAsync(client, userName, ip);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var rejected = await PostTestConnectionAsync(client, userName, ip);

        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", rejected.Content.Headers.ContentType?.ToString());
        Assert.Equal("Too many requests. Please wait a minute and try again.",
            await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter), "The response has no Retry-After header.");
        Assert.True(int.TryParse(Assert.Single(retryAfter), NumberStyles.Integer, CultureInfo.InvariantCulture, out _));

        var events = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<ApplicationDbContext>().SecurityEvents.AsNoTracking()
                .Where(e => e.IpAddress == ip)
                .ToListAsync(TestContext.Current.CancellationToken));
        var securityEvent = Assert.Single(events, e => e.Kind == SecurityEventKind.RateLimitRejected);
        Assert.Equal("GameApiFlood", securityEvent.Code);
    }

    [Fact]
    public async Task XLogFilePost_FromSecondIp_IsNotLimitedByFirstIpsRequests()
    {
        string userName = WebsiteTestData.UniqueUserName("rl_secondip");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();
        string firstIp = NextIp();
        string secondIp = NextIp();

        for (int i = 0; i < 2; i++)
        {
            await PostTestConnectionAsync(client, userName, firstIp);
        }
        var firstIpRejected = await PostTestConnectionAsync(client, userName, firstIp);
        Assert.Equal((HttpStatusCode)429, firstIpRejected.StatusCode);

        var fromSecondIp = await PostTestConnectionAsync(client, userName, secondIp);

        Assert.Equal(HttpStatusCode.OK, fromSecondIp.StatusCode);
    }

    [Fact]
    public async Task XLogFileGet_CarriesNoPolicy_FiveFromOneIpAllReturn200()
    {
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/xlogfile");
            request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, ip);

            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>A documentation address (2001:db8::/32) no other test uses, in the form the server prints it.</summary>
    private static string NextIp() =>
        IPAddress.Parse("2001:db8:a3::" + Interlocked.Increment(ref _ipCounter).ToString("x", CultureInfo.InvariantCulture)).ToString();

    /// <summary>An xlogfile connection test: credentials only.</summary>
    private static async Task<HttpResponseMessage> PostTestConnectionAsync(HttpClient client, string userName, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/xlogfile")
        {
            Content = WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password)
        };
        request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, ip);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
