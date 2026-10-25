using System.Net;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

public class JunetHackApiTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private readonly WebsiteWebApplicationFactory _factory;

    public JunetHackApiTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_UnknownUser_Returns404()
    {
        string userName = WebsiteTestData.UniqueUserName("jh_unknown");
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/junethack/" + userName, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserWithoutJunethackName_Returns200AndEmptyBody()
    {
        string userName = WebsiteTestData.UniqueUserName("jh_none");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/junethack/" + userName, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_UserWithJunethackName_ReturnsTheJunethackLine()
    {
        string userName = WebsiteTestData.UniqueUserName("jh_named");
        string junethackName = "jh" + userName;
        await _factory.WithScopeAsync(sp =>
            WebsiteTestData.CreateUserAsync(sp, userName, u => u.JunetHackUserName = junethackName));
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/junethack/" + userName, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("junethack " + junethackName, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
