using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Tests.Helpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.IntegrationTests
{
    public class AuthSecurityTests : IClassFixture<OverseerWebApplicationFactory>
    {
        private readonly OverseerWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public AuthSecurityTests(OverseerWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Me_Unauthenticated_Returns204NoContentOrNull()
        {
            var response = await _client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.True(string.IsNullOrEmpty(content) || content == "null");
            }
        }

        [Fact]
        public async Task Me_Authenticated_ReturnsUserData()
        {
            var username = "AuthTestUser";
            var userId = "test-id-" + username;

            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var existing = await userManager.FindByIdAsync(userId);
                if (existing == null)
                {
                    var user = new ApplicationUser
                    {
                        Id = userId,
                        UserName = username,
                        Email = "authtest@example.com",
                        EmailConfirmed = true
                    };
                    await userManager.CreateAsync(user);
                }
            }

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Add("X-Test-User", username);

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal(username, json.GetProperty("userName").GetString());
            Assert.Equal("authtest@example.com", json.GetProperty("email").GetString());
            Assert.False(json.GetProperty("isAdmin").GetBoolean());
        }
    }
}
