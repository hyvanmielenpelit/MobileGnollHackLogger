using System.Net;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

public class PublicPagesSmokeTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static WebsiteWebApplicationFactory? _seededFactory;

    private readonly WebsiteWebApplicationFactory _factory;

    public PublicPagesSmokeTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/?list=top")]
    [InlineData("/?list=recent")]
    [InlineData("/TopScores")]
    [InlineData("/TopScores?mode=normal&death=ascended")]
    [InlineData("/RecentGames")]
    [InlineData("/RecentGames?mode=modern")]
    [InlineData("/Statistics")]
    [InlineData("/Statistics?mode=modern")]
    [InlineData("/Privacy")]
    [InlineData("/Contact")]
    public async Task AnonymousGet_PublicPage_Returns200Html(string url)
    {
        await EnsureSeededAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Games in both display modes, an ascension, a game without <c>mode</c> and one in a mode the site does not
    /// know, once per factory.
    /// </summary>
    private async Task EnsureSeededAsync()
    {
        await SeedLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (ReferenceEquals(_seededFactory, _factory))
            {
                return;
            }

            await _factory.WithScopeAsync(async sp =>
            {
                var first = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("pages"));
                var second = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("pages"));
                long start = 1_760_000_000;

                await WebsiteTestData.AddGameLogAsync(sp, first,
                    WebsiteTestData.XLogEntry(first.UserName!, 1500, start, mode: "normal"));
                await WebsiteTestData.AddGameLogAsync(sp, first,
                    WebsiteTestData.XLogEntry(first.UserName!, 2500, start + 1000, mode: "modern"));
                await WebsiteTestData.AddGameLogAsync(sp, first,
                    WebsiteTestData.XLogEntry(first.UserName!, 900_000, start + 2000, mode: "normal", turns: 45_000, death: "ascended"));
                await WebsiteTestData.AddGameLogAsync(sp, second,
                    WebsiteTestData.XLogEntry(second.UserName!, 700_000, start + 3000, mode: "modern", turns: 40_000, death: "ascended"));
                await WebsiteTestData.AddGameLogAsync(sp, second,
                    WebsiteTestData.XLogEntry(second.UserName!, 3000, start + 4000, mode: null));
                await WebsiteTestData.AddGameLogAsync(sp, second,
                    WebsiteTestData.XLogEntry(second.UserName!, 3500, start + 5000, mode: "futuremode"));
                await WebsiteTestData.AddGameLogAsync(sp, second,
                    WebsiteTestData.XLogEntry(second.UserName!, 100, start + 6000, mode: "normal", scoring: "no"));
            });

            _seededFactory = _factory;
        }
        finally
        {
            SeedLock.Release();
        }
    }
}
