using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// The xlogfile GETs that NetHack Scoreboard and Junethack fetch incrementally by byte offset. The tests share one
/// database and every GET returns all of its games, so each test seeds its own games and computes what it expects
/// from the full body or the rows as they are after that seeding.
/// </summary>
public class XLogFileGetTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static long _startTimeUtc = 1_770_000_000;

    private readonly WebsiteWebApplicationFactory _factory;

    public XLogFileGetTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WithoutRange_ReturnsEveryGameAndFillsContiguousByteOffsets()
    {
        await SeedGamesAsync(3);
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/xlogfile", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(bytes, b => b >= 0x80);
        string body = Encoding.ASCII.GetString(bytes);
        Assert.DoesNotContain("\r", body, StringComparison.Ordinal);
        Assert.EndsWith("\n", body, StringComparison.Ordinal);

        var rows = await AllGamesAsync();
        string[] lines = body.Substring(0, body.Length - 1).Split('\n');
        Assert.Equal(rows.Select(r => r.ToXLogString()), lines);

        long expectedStart = 0;
        foreach (var row in rows)
        {
            Assert.Equal(expectedStart, row.ByteStart);
            Assert.NotNull(row.ByteEnd);
            Assert.NotNull(row.ByteLength);
            Assert.Equal(row.ByteLength, row.ByteEnd - row.ByteStart + 1);
            Assert.Equal(row.ToXLogString().Length + 1, row.ByteLength);
            expectedStart = row.ByteEnd!.Value + 1;
        }
        Assert.Equal(bytes.Length, expectedStart);
    }

    [Theory]
    [InlineData("from-zero")]
    [InlineData("from-start-of-line-2")]
    [InlineData("from-middle-of-line-2")]
    [InlineData("fixed-10-50")]
    [InlineData("middle-of-line-1-to-middle-of-line-3")]
    [InlineData("last-byte-only")]
    public async Task Get_WithRange_ReturnsTheMatchingSliceOfTheFullBody(string rangeCase)
    {
        await SeedGamesAsync(3);
        using var client = _factory.CreateGameClient();
        string full = await GetFullBodyAsync(client);
        var lineStarts = LineStarts(full);
        long LineStart(int line) => lineStarts[line - 1];
        long LineLength(int line) => (line < lineStarts.Count ? lineStarts[line] : full.Length) - lineStarts[line - 1];

        (long Start, long? End) range = rangeCase switch
        {
            "from-zero" => (0L, (long?)null),
            "from-start-of-line-2" => (LineStart(2), null),
            "from-middle-of-line-2" => (LineStart(2) + LineLength(2) / 2, null),
            "fixed-10-50" => (10, 50),
            "middle-of-line-1-to-middle-of-line-3" => (LineLength(1) / 2, LineStart(3) + LineLength(3) / 2),
            "last-byte-only" => (full.Length - 1, full.Length - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(rangeCase), rangeCase, null)
        };
        string expected = range.End == null
            ? full.Substring((int)range.Start)
            : full.Substring((int)range.Start, (int)(range.End.Value - range.Start + 1));

        var response = await GetWithRangeAsync(client, "bytes=" + range.Start + "-" + range.End);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_WithEmptyRangeStart_ReturnsBytesFromZeroToTheEndValueInclusive()
    {
        await SeedGamesAsync(1);
        using var client = _factory.CreateGameClient();
        string full = await GetFullBodyAsync(client);

        var response = await GetWithRangeAsync(client, "bytes=-20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(full.Substring(0, 21), await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_WithRangeStartingBeyondTheEnd_Returns200AndEmptyBody()
    {
        await SeedGamesAsync(1);
        using var client = _factory.CreateGameClient();
        string full = await GetFullBodyAsync(client);

        var response = await GetWithRangeAsync(client, "bytes=" + (full.Length + 100) + "-");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bytes=abc-10")]
    [InlineData("bytes=10-abc")]
    [InlineData("bytes=10")]
    [InlineData("bytes=1=2")]
    public async Task Get_WithMalformedRange_Returns400(string range)
    {
        using var client = _factory.CreateGameClient();

        var response = await GetWithRangeAsync(client, range);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithRangeEndBeforeStart_Returns416()
    {
        using var client = _factory.CreateGameClient();

        var response = await GetWithRangeAsync(client, "bytes=50-10");

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("minmax")]
    public async Task Get_ById_ReturnsExactlyTheMatchingRows(string route)
    {
        var seeded = await SeedGamesAsync(3);
        long first = seeded[0].Id;
        long second = seeded[1].Id;
        var rows = await AllGamesAsync();
        (string Path, Func<GameLog, bool> Matches) target = route switch
        {
            "single" => ("/xlogfile/" + second, (Func<GameLog, bool>)(r => r.Id == second)),
            "min" => ("/xlogfile/min/" + second, r => r.Id >= second),
            "max" => ("/xlogfile/max/" + second, r => r.Id <= second),
            "minmax" => ("/xlogfile/minmax/" + first + "/" + second, r => r.Id >= first && r.Id <= second),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };
        string expected = string.Concat(rows.Where(target.Matches).Select(r => r.ToXLogString() + "\n"));
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync(target.Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_ByUnknownId_ReturnsEmptyBody()
    {
        var seeded = await SeedGamesAsync(1);
        long unknownId = seeded[0].Id + 1_000_000;
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/xlogfile/" + unknownId, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<List<GameLog>> SeedGamesAsync(int count)
    {
        return await _factory.WithScopeAsync(async sp =>
        {
            string userName = WebsiteTestData.UniqueUserName("xget");
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            var games = new List<GameLog>();
            for (int i = 0; i < count; i++)
            {
                long startUtc = Interlocked.Add(ref _startTimeUtc, 10_000);
                games.Add(await WebsiteTestData.AddGameLogAsync(sp, user,
                    WebsiteTestData.XLogEntry(userName, 100 + i, startUtc, turns: 1000 + i)));
            }
            return games;
        });
    }

    private Task<List<GameLog>> AllGamesAsync() => _factory.WithScopeAsync(sp =>
        sp.GetRequiredService<ApplicationDbContext>().GameLog.AsNoTracking().OrderBy(gl => gl.Id).ToListAsync());

    /// <summary>The unranged GET, which also stores the byte offsets of every game that has none.</summary>
    private static async Task<string> GetFullBodyAsync(HttpClient client)
    {
        var response = await client.GetAsync("/xlogfile", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Sends <paramref name="range"/> unvalidated, because <see cref="HttpClient"/> rejects malformed ranges itself.</summary>
    private static async Task<HttpResponseMessage> GetWithRangeAsync(HttpClient client, string range)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/xlogfile");
        Assert.True(request.Headers.TryAddWithoutValidation("Range", range));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The offset of the first byte of every line of <paramref name="body"/>.</summary>
    private static List<long> LineStarts(string body)
    {
        var starts = new List<long>();
        for (int i = 0; i < body.Length; i++)
        {
            if (i == 0 || body[i - 1] == '\n')
            {
                starts.Add(i);
            }
        }
        return starts;
    }
}
