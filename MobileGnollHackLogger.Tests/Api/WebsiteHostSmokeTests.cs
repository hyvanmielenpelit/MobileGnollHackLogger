using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

public class WebsiteHostSmokeTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private readonly WebsiteWebApplicationFactory _factory;

    public WebsiteHostSmokeTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetXLogFile_OnEmptyDatabase_Returns200AndEmptyBody()
    {
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/xlogfile", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Environment_IsTesting()
    {
        var environment = _factory.Services.GetRequiredService<IHostEnvironment>();
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("Testing", environment.EnvironmentName);
        Assert.True(string.IsNullOrEmpty(configuration["SentryDSN"]));
    }

    [Fact]
    public async Task Database_IsSqlite()
    {
        string? provider = await _factory.WithScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ApplicationDbContext>().Database.ProviderName));

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", provider);
    }

    [Fact]
    public async Task InMemoryFactory_Database_IsInMemory()
    {
        await using var inMemory = new InMemoryWebsiteWebApplicationFactory();

        string? provider = await inMemory.WithScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ApplicationDbContext>().Database.ProviderName));

        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", provider);
    }
}
