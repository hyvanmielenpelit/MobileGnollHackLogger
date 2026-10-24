using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.IntegrationTests;

/// <summary>
/// The global <see cref="AutoValidateAntiforgeryTokenAttribute"/> refuses an unsafe administrator request that carries
/// no antiforgery token, and lets a safe one through. <see cref="OverseerWebApplicationFactory"/> removes the filter for
/// the other API tests; this class runs on a factory that keeps it.
/// </summary>
public sealed class AntiforgeryEnforcementTests : IClassFixture<AntiforgeryEnforcementTests.AntiforgeryKeptFactory>
{
    /// <summary>The test host with the CSRF filter <c>Program.cs</c> registers left in place.</summary>
    public sealed class AntiforgeryKeptFactory : OverseerWebApplicationFactory
    {
        protected override bool KeepAntiforgeryFilter => true;
    }

    private readonly AntiforgeryKeptFactory _factory;
    private readonly HttpClient _client;

    public AntiforgeryEnforcementTests(AntiforgeryKeptFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void TheHost_CarriesTheProductionFilter()
    {
        var filters = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MvcOptions>>().Value.Filters;

        Assert.True(filters.Any(f => f is AutoValidateAntiforgeryTokenAttribute),
            "Filters: " + string.Join(", ", filters.Select(f => f.GetType().Name)));
    }

    /// <remarks>
    /// Each body is one the action accepts, so without the filter the request would not be a bare 400: the group is
    /// created, the export finds no issue (404), and the apply's own refusal carries a message and errors.
    /// </remarks>
    [Theory]
    [InlineData("api/admin/feedback/resolutions/apply", "{\"format\":\"overseer-feedback-resolutions\",\"version\":1,\"source\":\"overseer\",\"items\":[{\"id\":987654,\"fingerprint\":\"" + Fingerprint + "\",\"status\":\"resolved\"}]}")]
    [InlineData("api/admin/feedback/export", "{\"source\":\"overseer\",\"issueIds\":[987654]}")]
    [InlineData("api/admin/groups", "{\"displayName\":\"CSRF probe\"}")]
    public async Task UnsafeAdminRequests_WithoutAnAntiforgeryToken_AreRefused(string route, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/" + route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-User", "TestAdmin");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // The filter's refusal is a bare 400, which the problem-details mapping gives a type and a title only.
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        Assert.DoesNotContain("\"message\"", text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("\"errors\"", text, System.StringComparison.Ordinal);
    }

    private const string Fingerprint = "0000000000000000000000000000000000000000000000000000000000000001";

    [Fact]
    public async Task SafeAdminRequests_DoNotNeedAToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/groups");
        request.Headers.Add("X-Test-User", "TestAdmin");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
