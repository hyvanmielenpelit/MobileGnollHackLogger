using System.Globalization;
using System.Net;
using GnollHackServer.Data.Security;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary><c>POST api/replay</c>: sign-in and the bones ban are checked; the upload itself is not implemented.</summary>
public class ReplayApiTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static int _ipCounter;

    private readonly WebsiteWebApplicationFactory _factory;

    public ReplayApiTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_CorrectCredentials_Returns403Failed()
    {
        string userName = WebsiteTestData.UniqueUserName("replay_ok");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password));

        var failed = GameApiSignInResponses.For(GameApiSignInOutcome.Failed);
        Assert.Equal(403, failed.StatusCode);
        Assert.Equal(failed.StatusCode, (int)response.StatusCode);
        Assert.Equal(failed.Body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_BonesBannedUser_Returns423()
    {
        string userName = WebsiteTestData.UniqueUserName("replay_bban");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName, u => u.IsBonesBanned = true));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password));

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        Assert.Equal("User " + userName + " is not allowed to send bones.",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_WrongAntiForgeryToken_Returns401()
    {
        string userName = WebsiteTestData.UniqueUserName("replay_token");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password, "wrong-token"));

        var mismatch = GameApiSignInResponses.For(GameApiSignInOutcome.AntiForgeryMismatch);
        Assert.Equal(401, mismatch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(mismatch.Body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A documentation address (2001:db8::/32) no other test uses, in the form the server prints it.</summary>
    private static string NextIp() =>
        IPAddress.Parse("2001:db8:a4::" + Interlocked.Increment(ref _ipCounter).ToString("x", CultureInfo.InvariantCulture)).ToString();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/replay") { Content = content };
        request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, NextIp());
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
