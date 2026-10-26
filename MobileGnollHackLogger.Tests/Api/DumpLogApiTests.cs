using System.Net;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// The dumplog GETs. Names are always sent in their stored case: SQLite compares text case-sensitively where
/// SQL Server does not.
/// </summary>
public class DumpLogApiTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static long _startTimeUtc = 1_780_000_000;

    private readonly WebsiteWebApplicationFactory _factory;

    public DumpLogApiTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetById_ReturnsTheHtmlDumplog()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + game.Id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(game.Html, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetById_Plain_ReturnsThePlainTextDumplog()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + game.Id + "/plain", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(game.Plain, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetById_UnknownType_FallsBackToHtml()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + game.Id + "/pdf", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(game.Html, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByName_FindsTheGameByLocalStartTime()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var html = await client.GetAsync("/dumplog/byname/" + game.UserName + "/" + game.StartTime,
            TestContext.Current.CancellationToken);
        var plain = await client.GetAsync("/dumplog/byname/" + game.UserName + "/" + game.StartTime + "/plain",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("text/html", html.Content.Headers.ContentType?.MediaType);
        Assert.Equal(game.Html, await html.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal("text/plain", plain.Content.Headers.ContentType?.MediaType);
        Assert.Equal(game.Plain, await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByName_WithUtcStartTime_Returns404()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/byname/" + game.UserName + "/" + game.StartTimeUtc,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task GetByName_WithNonPositiveStartTime_Returns400(string startTime)
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/byname/" + game.UserName + "/" + startTime,
            TestContext.Current.CancellationToken);
        var typed = await client.GetAsync("/dumplog/byname/" + game.UserName + "/" + startTime + "/plain",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, typed.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + (game.Id + 1_000_000), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/html")]
    [InlineData("/plain")]
    public async Task GetById_SetsSandboxPolicyAndNoSniff(string typeSuffix)
    {
        var game = await SeedGameAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + game.Id + typeSuffix, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sandbox; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task GetById_HtmlWithScript_IsReturnedUnchangedUnderTheSandboxPolicy()
    {
        var game = await SeedGameAsync();
        string html = "<html><head><style>body { color: red; }</style></head><body><script>alert(document.cookie)</script>dumplog</body></html>";
        await File.WriteAllTextAsync(DumpLogFile(game.UserName, game.StartTimeUtc, "html"), html, TestContext.Current.CancellationToken);
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dumplog/" + game.Id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(html, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("sandbox; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task GetById_GameNamedDotDot_Returns404AndDoesNotServeTheFileAboveTheStore()
    {
        string userName = WebsiteTestData.UniqueUserName("dumplog_dots");
        long startUtc = Interlocked.Add(ref _startTimeUtc, 10_000);
        GameLog game = await _factory.WithScopeAsync(async sp =>
        {
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            return await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry("..", 100, startUtc));
        });
        Assert.Equal("..", game.Name);
        // Where DumpLogPath\..\gnollhack.<name>.<starttimeUTC>.<ext> resolves for the name "..".
        string baseName = Path.Combine(_factory.TempRoot, "gnollhack." + ".." + "." + startUtc);
        await File.WriteAllTextAsync(baseName + ".html", "<html>outside the store</html>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(baseName + ".txt", "outside the store", TestContext.Current.CancellationToken);
        using var client = _factory.CreateGameClient();

        var html = await client.GetAsync("/dumplog/" + game.Id, TestContext.Current.CancellationToken);
        var plain = await client.GetAsync("/dumplog/" + game.Id + "/plain", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, html.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plain.StatusCode);
    }

    [Fact]
    public async Task GetById_GameWithoutDumplogFile_Returns410()
    {
        var game = await SeedGameAsync(writeFiles: false);
        using var client = _factory.CreateGameClient();

        var html = await client.GetAsync("/dumplog/" + game.Id, TestContext.Current.CancellationToken);
        var plain = await client.GetAsync("/dumplog/" + game.Id + "/plain", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Gone, html.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, plain.StatusCode);
    }

    private sealed record SeededGame(long Id, string UserName, long StartTimeUtc, long StartTime, string Plain, string Html);

    /// <summary>
    /// A game of a new user and, when <paramref name="writeFiles"/>, its two dumplogs at the path
    /// <c>DumpLogController</c> reads: <c>DumpLogPath\&lt;name&gt;\gnollhack.&lt;name&gt;.&lt;starttimeUTC&gt;.&lt;ext&gt;</c>.
    /// </summary>
    private async Task<SeededGame> SeedGameAsync(bool writeFiles = true)
    {
        string userName = WebsiteTestData.UniqueUserName("dumplog");
        long startUtc = Interlocked.Add(ref _startTimeUtc, 10_000);
        long startLocal = startUtc + 3600;
        GameLog game = await _factory.WithScopeAsync(async sp =>
        {
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            return await WebsiteTestData.AddGameLogAsync(sp, user,
                WebsiteTestData.XLogEntry(userName, 100, startUtc, startTime: startLocal));
        });

        string plain = "plain dumplog of " + userName;
        string html = "<html><body>dumplog of " + userName + "</body></html>";
        if (writeFiles)
        {
            Directory.CreateDirectory(Path.Combine(_factory.DumpLogPath, userName));
            await File.WriteAllTextAsync(DumpLogFile(userName, startUtc, "txt"), plain, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(DumpLogFile(userName, startUtc, "html"), html, TestContext.Current.CancellationToken);
        }
        return new SeededGame(game.Id, userName, startUtc, startLocal, plain, html);
    }

    private string DumpLogFile(string userName, long startUtc, string extension) =>
        Path.Combine(_factory.DumpLogPath, userName, "gnollhack." + userName + "." + startUtc + "." + extension);
}
