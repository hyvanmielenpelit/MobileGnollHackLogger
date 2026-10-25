using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// <c>/BonesTransactions</c>. On the InMemory provider, because the view filters bones and transactions by
/// <c>ulong</c> version ranges. The factory configures pools 1 and 9000, which the page lists newest first:
/// index 0 is versions 9000 and up, index 1 is versions 1 to 8999.
/// </summary>
public class BonesTransactionsPageTests : IClassFixture<InMemoryWebsiteWebApplicationFactory>
{
    private readonly InMemoryWebsiteWebApplicationFactory _factory;

    public BonesTransactionsPageTests(InMemoryWebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_Default_Returns200AndShowsTheNewestPool()
    {
        var (oldPoolUser, newPoolUser) = await SeedTwoPoolsAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/BonesTransactions", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(">" + newPoolUser.UserName + "<", html);
        Assert.DoesNotContain(">" + oldPoolUser.UserName + "<", html);
    }

    [Fact]
    public async Task Get_Compatibility1_Returns200AndShowsTheOlderPool()
    {
        var (oldPoolUser, newPoolUser) = await SeedTwoPoolsAsync();
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/BonesTransactions?compatibility=1", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(">" + oldPoolUser.UserName + "<", html);
        Assert.DoesNotContain(">" + newPoolUser.UserName + "<", html);
    }

    [Fact]
    public async Task Get_CompatibilityOutOfRange_Returns500()
    {
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/BonesTransactions?compatibility=7", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Two users, each with a bones row and an upload, a download and a deletion transaction: the first at version 1,
    /// the second at version 9000.
    /// </summary>
    private Task<(ApplicationUser OldPoolUser, ApplicationUser NewPoolUser)> SeedTwoPoolsAsync()
    {
        return _factory.WithScopeAsync(async sp =>
        {
            var oldPoolUser = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("btPool1"));
            var newPoolUser = await WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName("btPool9000"));
            await AddBonesWithTransactionsAsync(sp, oldPoolUser, 1);
            await AddBonesWithTransactionsAsync(sp, newPoolUser, 9000);
            return (oldPoolUser, newPoolUser);
        });
    }

    private static async Task AddBonesWithTransactionsAsync(IServiceProvider services, ApplicationUser user, ulong version)
    {
        var bones = await WebsiteTestData.AddBonesAsync(services, user, 2, version, version);
        var db = services.GetRequiredService<ApplicationDbContext>();
        foreach (var type in new[] { TransactionType.Upload, TransactionType.Download, TransactionType.Deletion })
        {
            db.BonesTransactions.Add(new BonesTransaction
            {
                AspNetUserId = user.Id,
                Date = DateTime.UtcNow,
                Type = type,
                BonesId = bones.Id,
                DifficultyLevel = bones.DifficultyLevel,
                Platform = bones.Platform,
                PlatformVersion = bones.PlatformVersion,
                Port = bones.Port,
                PortVersion = bones.PortVersion,
                PortBuild = bones.PortBuild,
                VersionNumber = version,
                VersionCompatibilityNumber = version
            });
        }
        await db.SaveChangesAsync();
    }
}
