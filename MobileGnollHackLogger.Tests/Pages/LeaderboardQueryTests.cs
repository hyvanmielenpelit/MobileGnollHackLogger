using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Pages;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// The <c>/Statistics</c> aggregate and the <c>/RecentGames</c> count and order, on SQLite, so the server-side
/// <c>GroupBy</c> and <c>Count(predicate)</c> translation runs. The tests share one factory, so each asserts the
/// difference from a baseline taken before it seeds.
/// </summary>
public class LeaderboardQueryTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private readonly WebsiteWebApplicationFactory _factory;

    public LeaderboardQueryTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Statistics_CountsPerRoleAndDifficulty_MatchThePagePredicate()
    {
        await _factory.WithScopeAsync(async sp =>
        {
            var allBefore = await MeasureStatisticsAsync(sp, null);
            var modernBefore = await MeasureStatisticsAsync(sp, "modern");

            var user = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("lbStats"));
            long t = 1_800_000_000;
            async Task AddAsync(string role, int difficulty, string mode = "normal", string scoring = "yes", int turns = 1234,
                bool ascended = false)
            {
                t += 1000;
                await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(user.UserName!, 100, t, mode, scoring,
                    turns, ascended ? "ascended" : "killed by a jackal", role: role, difficulty: difficulty));
            }

            // Counted.
            await AddAsync("Val", 0);
            await AddAsync("Val", 0);
            await AddAsync("Val", 0, ascended: true);
            await AddAsync("Val", 1, ascended: true);
            await AddAsync("Wiz", 0);
            await AddAsync("Wiz", 0, mode: "modern", ascended: true);
            await AddAsync("Xyz", 0);

            // Excluded by the predicate.
            await AddAsync("Val", 0, turns: 999);
            await AddAsync("Val", 0, mode: "debug", ascended: true);
            await AddAsync("Val", 0, mode: "explore");
            await AddAsync("Val", 0, scoring: "no", ascended: true);

            var all = Delta(allBefore, await MeasureStatisticsAsync(sp, null));
            Assert.Equal(3, all["Games(Val,0)"]);
            Assert.Equal(1, all["Ascensions(Val,0)"]);
            Assert.Equal(1, all["Games(Val,1)"]);
            Assert.Equal(1, all["Ascensions(Val,1)"]);
            Assert.Equal(2, all["Games(Wiz,0)"]);
            Assert.Equal(1, all["Ascensions(Wiz,0)"]);
            Assert.Equal(4, all["RoleGames(Val)"]);
            Assert.Equal(2, all["RoleAscensions(Val)"]);
            Assert.Equal(2, all["RoleGames(Wiz)"]);
            Assert.Equal(1, all["RoleGames(Xyz)"]);
            // The unknown role has no column but counts in its difficulty's row and in the total.
            Assert.Equal(6, all["DifficultyGames(0)"]);
            Assert.Equal(2, all["DifficultyAscensions(0)"]);
            Assert.Equal(1, all["DifficultyGames(1)"]);
            Assert.Equal(7, all["TotalGames"]);
            Assert.Equal(3, all["TotalAscensions"]);

            var modern = Delta(modernBefore, await MeasureStatisticsAsync(sp, "modern"));
            Assert.Equal(0, modern["Games(Val,0)"]);
            Assert.Equal(1, modern["Games(Wiz,0)"]);
            Assert.Equal(1, modern["Ascensions(Wiz,0)"]);
            Assert.Equal(1, modern["DifficultyGames(0)"]);
            Assert.Equal(1, modern["TotalGames"]);
            Assert.Equal(1, modern["TotalAscensions"]);
        });
    }

    [Fact]
    public async Task RecentGames_NewestFirst_AndSubTitleCountsAllMatchingGames()
    {
        await _factory.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var before = new RecentGamesModel(db);
            await before.OnGetAsync(null, "normal");
            int beforeCount = before.GameLogs!.Count;

            var user = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("lbRecent"));
            // Far in the future, so these are the newest games whatever else the database holds.
            long start = 4_000_000_000;
            var oldest = await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(user.UserName!, 10, start));
            var newest = await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(user.UserName!, 10, start + 2000));
            var middle = await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(user.UserName!, 10, start + 1000));
            await WebsiteTestData.AddGameLogAsync(sp, user, WebsiteTestData.XLogEntry(user.UserName!, 10, start + 3000, mode: "modern"));

            var after = new RecentGamesModel(db);
            await after.OnGetAsync(null, "normal");

            var games = after.GameLogs!;
            Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, games.Take(3).Select(g => g.Id));
            Assert.All(games.Zip(games.Skip(1)), pair => Assert.True(pair.First.EndTimeUTC >= pair.Second.EndTimeUTC));
            Assert.Equal(beforeCount + 3, games.Count);
            Assert.Equal($"Last {beforeCount + 3} Games", after.SubTitle);
        });
    }

    private static readonly string[] MeasuredRoles = { "Val", "Wiz", "Xyz" };
    private static readonly int[] MeasuredDifficulties = { 0, 1 };

    private static async Task<Dictionary<string, long>> MeasureStatisticsAsync(IServiceProvider sp, string? mode)
    {
        var model = new StatisticsModel(sp.GetRequiredService<ApplicationDbContext>());
        await model.OnGetAsync(mode);

        var values = new Dictionary<string, long>
        {
            ["TotalGames"] = model.TotalGames,
            ["TotalAscensions"] = model.TotalAscensions
        };
        foreach (var role in MeasuredRoles)
        {
            values[$"RoleGames({role})"] = model.RoleGames(role);
            values[$"RoleAscensions({role})"] = model.RoleAscensions(role);
            foreach (var difficulty in MeasuredDifficulties)
            {
                values[$"Games({role},{difficulty})"] = model.Games(role, difficulty);
                values[$"Ascensions({role},{difficulty})"] = model.Ascensions(role, difficulty);
            }
        }
        foreach (var difficulty in MeasuredDifficulties)
        {
            values[$"DifficultyGames({difficulty})"] = model.DifficultyGames(difficulty);
            values[$"DifficultyAscensions({difficulty})"] = model.DifficultyAscensions(difficulty);
        }
        return values;
    }

    private static Dictionary<string, long> Delta(Dictionary<string, long> before, Dictionary<string, long> after) =>
        after.ToDictionary(kv => kv.Key, kv => kv.Value - before[kv.Key]);
}
