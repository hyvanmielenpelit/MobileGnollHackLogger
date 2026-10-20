using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MobileGnollHackLogger.Data;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.IntegrationTests;

/// <summary>
/// An /api request that matches no route, or whose action throws, answers problem JSON rather
/// than the Angular index.html the SPA fallback serves to every other unmatched path.
/// </summary>
public class ApiFallbackTests : IClassFixture<OverseerWebApplicationFactory>
{
    private readonly OverseerWebApplicationFactory _factory;

    public ApiFallbackTests(OverseerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("/api/this-route-does-not-exist")]
    [InlineData("/API/This-Route-Does-Not-Exist")]
    public async Task UnmatchedApiPath_AnswersA404Problem(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FailingApiAction_InProduction_AnswersA500Problem()
    {
        /* The schema is never created on this connection, so the terms query throws "no such
           table". The factory's empty WikiPath leaves no document version to short-circuit it. */
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddSingleton(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(connection)
                    .Options);
            });
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/legal/terms/overseer-terms/version", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
    }
}
