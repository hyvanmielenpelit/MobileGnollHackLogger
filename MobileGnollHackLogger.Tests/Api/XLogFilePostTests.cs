using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Areas.API;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

public class XLogFilePostTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static long _startTimeUtc = 1_760_000_000;

    private readonly WebsiteWebApplicationFactory _factory;

    public XLogFilePostTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_ValidEntryWithBothDumplogs_StoresTheGameAndWritesBothFiles()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_valid");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        byte[] plain = Encoding.ASCII.GetBytes("plain dumplog of " + userName);
        byte[] html = Encoding.ASCII.GetBytes("<html><body>dumplog of " + userName + "</body></html>");
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, startUtc), plain, html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var info = await ReadResponseInfoAsync(response);
        Assert.True(info.DatabaseRowId > 0);
        Assert.Equal("https://localhost/TopScores?mode=normal", info.TopScorePageUrl);

        var row = await FindGameAsync(info.DatabaseRowId);
        Assert.NotNull(row);
        Assert.Equal(user.Id, row.AspNetUserId);
        Assert.Equal(userName, row.Name);
        Assert.Equal(startUtc, row.StartTimeUTC);
        Assert.Equal(plain, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "txt"), TestContext.Current.CancellationToken));
        Assert.Equal(html, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "html"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_EntryClaimingAnotherPlayersName_IsStoredUnderTheSendersAccountOnly()
    {
        string senderName = WebsiteTestData.UniqueUserName("xpost_sender");
        string otherName = WebsiteTestData.UniqueUserName("xpost_other");
        var sender = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, senderName));
        var other = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, otherName));
        long startUtc = NextStartTimeUtc();
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, senderName, WebsiteTestData.XLogEntry(otherName, 100, startUtc),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await ReadResponseInfoAsync(response);
        var row = await FindGameAsync(info.DatabaseRowId);
        Assert.NotNull(row);
        Assert.Equal(senderName, row.Name);
        Assert.Equal(sender.Id, row.AspNetUserId);
        Assert.Empty(await GamesOfAsync(other.Id));
        Assert.True(File.Exists(DumpLogFile(senderName, startUtc, "txt")));
        Assert.True(File.Exists(DumpLogFile(senderName, startUtc, "html")));
        Assert.False(Directory.Exists(Path.Combine(_factory.DumpLogPath, otherName)));
    }

    [Fact]
    public async Task Post_UserNameInUpperCase_StoresTheAccountsNameAndWritesFilesUnderIt()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_case");
        string upperName = userName.ToUpperInvariant();
        Assert.NotEqual(userName, upperName);
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, upperName, WebsiteTestData.XLogEntry(upperName, 100, startUtc),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await ReadResponseInfoAsync(response);
        var game = Assert.Single(await GamesOfAsync(user.Id));
        Assert.Equal(info.DatabaseRowId, game.Id);
        Assert.Equal(userName, game.Name);

        // The file system may ignore case, so the names are compared as they were created.
        string dir = Assert.Single(Directory.GetDirectories(_factory.DumpLogPath, userName));
        Assert.Equal(userName, Path.GetFileName(dir));
        string[] fileNames = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "gnollhack." + userName + "." + startUtc + ".html", "gnollhack." + userName + "." + startUtc + ".txt" },
            fileNames);
    }

    [Fact]
    public async Task Post_UserNameDotDot_Returns400FromModelValidationAndWritesNothing()
    {
        string[] entriesBefore = EntriesUnder(_factory.TempRoot);
        long failuresBefore = await UserNameModelStateFailureCountAsync();
        long startUtc = NextStartTimeUtc();
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, "..", WebsiteTestData.XLogEntry("..", 100, startUtc),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(failuresBefore + 1, await UserNameModelStateFailureCountAsync());
        Assert.Equal(entriesBefore, EntriesUnder(_factory.TempRoot));
    }

    [Fact]
    public async Task Post_TopScorePosition_CountsOnlyOtherScoringGamesInTheSameMode()
    {
        // A host of its own, so the games the other tests of this class post do not move the position.
        await using var factory = new WebsiteWebApplicationFactory();
        await factory.WithScopeAsync(async sp =>
        {
            foreach (var (points, mode, scoring) in new[]
            {
                (500L, "normal", "yes"),
                (300L, "normal", "yes"),
                (300L, "normal", "yes"),
                (400L, "normal", "no"),
                (400L, "casual", "yes"),
            })
            {
                string name = WebsiteTestData.UniqueUserName("xpost_rank");
                var owner = await WebsiteTestData.CreateUserAsync(sp, name);
                await WebsiteTestData.AddGameLogAsync(sp, owner,
                    WebsiteTestData.XLogEntry(name, points, NextStartTimeUtc(), mode: mode, scoring: scoring));
            }
        });
        string userName = WebsiteTestData.UniqueUserName("xpost_ranked");
        await factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 300, NextStartTimeUtc()),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await ReadResponseInfoAsync(response);
        Assert.Equal(2, info.TopScoreDisplayIndex);
        Assert.InRange(info.TopScoreIndex, 2, 4);
    }

    [Fact]
    public async Task Post_WithoutHtmlDumpLog_Returns400AndLogsPartialDataError()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_partial");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, NextStartTimeUtc()),
            Encoding.ASCII.GetBytes("plain"), html: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var logs = await RequestLogsOfAsync(userName);
        Assert.Contains(logs, l => l.SubType == RequestLogSubType.PartialDataError && l.ResponseCode == 400);
        Assert.Empty(await GamesOfAsync(user.Id));
    }

    [Fact]
    public async Task Post_GameLogBannedAccount_Returns423AndStoresNothing()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_glban");
        var user = await _factory.WithScopeAsync(sp =>
            WebsiteTestData.CreateUserAsync(sp, userName, u => u.IsGameLogBanned = true));
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, NextStartTimeUtc()),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        Assert.Equal("User " + userName + " is not allowed to make GameLog entries.",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await GamesOfAsync(user.Id));
        Assert.False(Directory.Exists(Path.Combine(_factory.DumpLogPath, userName)));
    }

    [Fact]
    public async Task Post_WithoutMode_StoresTheGameWithZeroTopScoreIndexes()
    {
        await AssertStoredWithoutTopScoreAsync(mode: null);
    }

    [Fact]
    public async Task Post_WithUnknownMode_StoresTheGameWithZeroTopScoreIndexes()
    {
        await AssertStoredWithoutTopScoreAsync(mode: "futuremode");
    }

    [Fact]
    public async Task Post_IdenticalGameTwice_ReturnsTheStoredRowAndKeepsTheFiles()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_repeat");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        string entry = WebsiteTestData.XLogEntry(userName, 150, startUtc);
        byte[] firstPlain = Encoding.ASCII.GetBytes("first plain dumplog");
        byte[] firstHtml = Encoding.ASCII.GetBytes("<html>first</html>");
        using var client = _factory.CreateGameClient();

        var first = await PostGameAsync(client, userName, entry, firstPlain, firstHtml);
        var second = await PostGameAsync(client, userName, entry,
            Encoding.ASCII.GetBytes("second plain dumplog, longer"), Encoding.ASCII.GetBytes("<html>second</html>"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstInfo = await ReadResponseInfoAsync(first);
        var secondInfo = await ReadResponseInfoAsync(second);
        Assert.Equal(firstInfo.DatabaseRowId, secondInfo.DatabaseRowId);
        Assert.Single(await GamesOfAsync(user.Id));
        Assert.Equal(firstPlain, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "txt"), TestContext.Current.CancellationToken));
        Assert.Equal(firstHtml, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "html"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_SameStartTimeWithDifferentPoints_Returns409AndKeepsTheStoredGame()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_conflict");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        byte[] firstPlain = Encoding.ASCII.GetBytes("first plain dumplog");
        byte[] firstHtml = Encoding.ASCII.GetBytes("<html>first</html>");
        using var client = _factory.CreateGameClient();

        var first = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, startUtc), firstPlain, firstHtml);
        var second = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 200, startUtc),
            Encoding.ASCII.GetBytes("second plain dumplog"), Encoding.ASCII.GetBytes("<html>second</html>"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var games = await GamesOfAsync(user.Id);
        var game = Assert.Single(games);
        Assert.Equal(100, game.Points);
        Assert.Equal(firstPlain, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "txt"), TestContext.Current.CancellationToken));
        Assert.Equal(firstHtml, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "html"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_WithLeftoverDumplogFilesAndNoRow_StoresTheGameAndReplacesTheFiles()
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_leftover");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        Directory.CreateDirectory(Path.Combine(_factory.DumpLogPath, userName));
        byte[] leftover = Encoding.ASCII.GetBytes(new string('x', 2000));
        await File.WriteAllBytesAsync(DumpLogFile(userName, startUtc, "txt"), leftover, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(DumpLogFile(userName, startUtc, "html"), leftover, TestContext.Current.CancellationToken);
        byte[] plain = Encoding.ASCII.GetBytes("new plain");
        byte[] html = Encoding.ASCII.GetBytes("<html>new</html>");
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, startUtc), plain, html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await ReadResponseInfoAsync(response);
        var game = Assert.Single(await GamesOfAsync(user.Id));
        Assert.Equal(info.DatabaseRowId, game.Id);
        Assert.Equal(plain, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "txt"), TestContext.Current.CancellationToken));
        Assert.Equal(html, await File.ReadAllBytesAsync(DumpLogFile(userName, startUtc, "html"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_FailingInsert_Returns500WithTheMessageAndDeletesBothDumplogs()
    {
        await using var factory = new FailingGameLogInsertFactory();
        string userName = WebsiteTestData.UniqueUserName("xpost_insertfail");
        var user = await factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        long startUtc = NextStartTimeUtc();
        using var client = factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, startUtc),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ThrowOnGameLogInsertInterceptor.FailureMessage,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var games = await factory.WithScopeAsync(sp => sp.GetRequiredService<ApplicationDbContext>().GameLog
            .AsNoTracking().Where(gl => gl.AspNetUserId == user.Id).ToListAsync());
        Assert.Empty(games);
        Assert.False(File.Exists(DumpLogFile(factory.DumpLogPath, userName, startUtc, "txt")));
        Assert.False(File.Exists(DumpLogFile(factory.DumpLogPath, userName, startUtc, "html")));

        // The request log of the failure is saved only when the failed GameLog no longer tracks as Added.
        var logs = await factory.WithScopeAsync(sp => sp.GetRequiredService<ApplicationDbContext>().RequestLogs
            .AsNoTracking().Where(r => r.RequestUserName == userName).ToListAsync());
        Assert.Contains(logs, l => l.ResponseCode == 500
            && l.Message != null && l.Message.StartsWith("GameLog insertion to database failed.", StringComparison.Ordinal));
    }

    /// <summary>The website host whose database refuses every insert of a <see cref="GameLog"/>.</summary>
    public class FailingGameLogInsertFactory : WebsiteWebApplicationFactory
    {
        protected override void ConfigureDbContextOptions(DbContextOptionsBuilder<ApplicationDbContext> options)
        {
            options.AddInterceptors(new ThrowOnGameLogInsertInterceptor());
        }
    }

    /// <summary>Throws a <see cref="DbUpdateException"/> from a save while an added <see cref="GameLog"/> is tracked.</summary>
    public sealed class ThrowOnGameLogInsertInterceptor : SaveChangesInterceptor
    {
        public const string FailureMessage = "Test interceptor refused the GameLog insert.";

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfGameLogAdded(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowIfGameLogAdded(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void ThrowIfGameLogAdded(DbContextEventData eventData)
        {
            if (eventData.Context != null
                && eventData.Context.ChangeTracker.Entries<GameLog>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateException(FailureMessage);
            }
        }
    }

    private async Task AssertStoredWithoutTopScoreAsync(string? mode)
    {
        string userName = WebsiteTestData.UniqueUserName("xpost_nomode");
        var user = await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var client = _factory.CreateGameClient();

        var response = await PostGameAsync(client, userName, WebsiteTestData.XLogEntry(userName, 100, NextStartTimeUtc(), mode: mode),
            Encoding.ASCII.GetBytes("plain"), Encoding.ASCII.GetBytes("<html></html>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await ReadResponseInfoAsync(response);
        var game = Assert.Single(await GamesOfAsync(user.Id));
        Assert.Equal(info.DatabaseRowId, game.Id);
        Assert.Equal(mode, game.Mode);
        Assert.Equal(0, info.TopScoreIndex);
        Assert.Equal(0, info.TopScoreDisplayIndex);
        Assert.Equal("https://localhost/TopScores", info.TopScorePageUrl);
    }

    private static long NextStartTimeUtc() => Interlocked.Add(ref _startTimeUtc, 10_000);

    private string DumpLogFile(string userName, long startUtc, string extension) =>
        DumpLogFile(_factory.DumpLogPath, userName, startUtc, extension);

    private static string DumpLogFile(string dumpLogPath, string userName, long startUtc, string extension) =>
        Path.Combine(dumpLogPath, userName, "gnollhack." + userName + "." + startUtc + "." + extension);

    private static async Task<HttpResponseMessage> PostGameAsync(HttpClient client, string userName, string entry,
        byte[]? plainText, byte[]? html)
    {
        var files = new List<(string Field, string FileName, byte[] Content)>();
        if (plainText != null)
        {
            files.Add(("PlainTextDumpLog", "dumplog.txt", plainText));
        }
        if (html != null)
        {
            files.Add(("HtmlDumpLog", "dumplog.html", html));
        }
        using var form = WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password,
            fields: new Dictionary<string, string> { ["XLogEntry"] = entry }, files: files.ToArray());
        return await client.PostAsync("/xlogfile", form, TestContext.Current.CancellationToken);
    }

    private static async Task<LogPostResponseInfo> ReadResponseInfoAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var info = JsonSerializer.Deserialize<LogPostResponseInfo>(body);
        Assert.NotNull(info);
        return info;
    }

    private Task<GameLog?> FindGameAsync(long id) => _factory.WithScopeAsync(sp =>
        sp.GetRequiredService<ApplicationDbContext>().GameLog.AsNoTracking().FirstOrDefaultAsync(gl => gl.Id == id));

    private Task<List<GameLog>> GamesOfAsync(string userId) => _factory.WithScopeAsync(sp =>
        sp.GetRequiredService<ApplicationDbContext>().GameLog.AsNoTracking().Where(gl => gl.AspNetUserId == userId).ToListAsync());

    private Task<List<RequestInfo>> RequestLogsOfAsync(string userName) => _factory.WithScopeAsync(sp =>
        sp.GetRequiredService<ApplicationDbContext>().RequestLogs.AsNoTracking().Where(r => r.RequestUserName == userName).ToListAsync());

    /// <summary>Every file and directory under <paramref name="directory"/>, sorted.</summary>
    private static string[] EntriesUnder(string directory) =>
        Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// How many <c>UserName</c> model-validation failures were logged for <c>/xlogfile</c>. The request log merges
    /// identical entries into one row with a count, so the total of the counts is compared.
    /// </summary>
    private async Task<long> UserNameModelStateFailureCountAsync()
    {
        var rows = await _factory.WithScopeAsync(sp => sp.GetRequiredService<ApplicationDbContext>().RequestLogs
            .AsNoTracking()
            .Where(r => r.Type == LogType.GameLog && r.SubType == RequestLogSubType.ModelStateFailed)
            .ToListAsync());
        return rows
            .Where(r => r.RequestPath != null && r.RequestPath.EndsWith("/xlogfile", StringComparison.OrdinalIgnoreCase)
                && r.Message != null && r.Message.Contains("UserName", StringComparison.Ordinal))
            .Sum(r => r.Count);
    }
}
