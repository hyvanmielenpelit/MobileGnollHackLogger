using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.IntegrationTests;

/// <summary>The Security tab's snapshot guard endpoints: the state with the configured rule, and setting the mode.</summary>
public sealed class AdminSnapshotGuardEndpointTests : IClassFixture<OverseerWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdminSnapshotGuardEndpointTests(OverseerWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, object? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/admin/security/snapshot-guard");
        if (body != null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-Test-User", "TestAdmin");
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_ReturnsTheModeAndTheConfiguredRule()
    {
        using var response = await SendAsync(HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Contains(root.GetProperty("mode").GetString(), new[] { "Off", "Observe", "Enforce" });
        Assert.True(root.GetProperty("autoBlockEnabled").GetBoolean());
        Assert.Equal(3, root.GetProperty("autoBlockDistinctDays").GetInt32());
        Assert.Equal(30, root.GetProperty("autoBlockWindowDays").GetInt32());
        Assert.True(root.GetProperty("notificationEmailConfigured").GetBoolean());
    }

    [Fact]
    public async Task Post_AnUnknownMode_Is400()
    {
        using var response = await SendAsync(HttpMethod.Post, new { mode = "Loud" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_AKnownMode_SetsIt_AndReturnsTheState()
    {
        // Observe is the deployment's seed, so the host's other tests see the mode they would see anyway.
        using var response = await SendAsync(HttpMethod.Post, new { mode = "observe" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Observe", json.RootElement.GetProperty("mode").GetString());
        Assert.False(json.RootElement.GetProperty("seeded").GetBoolean());
        Assert.Equal("test-id-TestAdmin", json.RootElement.GetProperty("setByUserName").GetString());
    }
}
