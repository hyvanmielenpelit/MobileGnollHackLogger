using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Tests.Helpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.IntegrationTests
{
    public class AdminSecurityTests : IClassFixture<OverseerWebApplicationFactory>
    {
        private readonly OverseerWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public AdminSecurityTests(OverseerWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task GetGroups_Unauthenticated_Returns401()
        {
            var response = await _client.GetAsync("/api/admin/groups", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetGroups_NonAdmin_Returns403()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/groups");
            request.Headers.Add("X-Test-User", "NormalUser");
            
            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetGroups_Admin_Returns200()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/groups");
            request.Headers.Add("X-Test-User", "TestAdmin");
            
            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CreateGroup_NonAdmin_Returns403()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/groups")
            {
                Content = JsonContent.Create(new CreateGroupRequest { DisplayName = "Test" })
            };
            request.Headers.Add("X-Test-User", "NormalUser");
            
            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CreateGroup_Admin_Returns200()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/groups")
            {
                Content = JsonContent.Create(new CreateGroupRequest { DisplayName = "Test Group" })
            };
            request.Headers.Add("X-Test-User", "TestAdmin");
            
            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public static TheoryData<string, string> DefaultApiKeyRoutes => new()
        {
            { "GET", "/api/admin/default-api-keys" },
            { "PUT", "/api/admin/default-api-keys/OpenAI" },
            { "POST", "/api/admin/default-api-keys/OpenAI/verify" },
            { "GET", "/api/admin/default-api-keys/OpenAI/deletion-check" },
            { "DELETE", "/api/admin/default-api-keys/OpenAI" }
        };

        private static HttpRequestMessage DefaultApiKeyRequest(string method, string url)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method == "PUT")
            {
                request.Content = JsonContent.Create(new SaveDefaultApiKeyRequest { ApiKey = "test-key-not-real-0001" });
            }
            return request;
        }

        [Theory]
        [MemberData(nameof(DefaultApiKeyRoutes))]
        public async Task DefaultApiKeys_Unauthenticated_Returns401(string method, string url)
        {
            var request = DefaultApiKeyRequest(method, url);

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(DefaultApiKeyRoutes))]
        public async Task DefaultApiKeys_NonAdmin_Returns403(string method, string url)
        {
            var request = DefaultApiKeyRequest(method, url);
            request.Headers.Add("X-Test-User", "NormalUser");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        public static TheoryData<string, string> AdminUserRoutes => new()
        {
            { "GET", "/api/admin/users" },
            { "GET", "/api/admin/users?statuses=LockedOut&emailConfirmed=no&skip=0&take=20" },
            { "GET", "/api/admin/users/facets?search=a" },
            { "GET", "/api/admin/users/user-1" },
            { "POST", "/api/admin/users/user-1/lockout" },
            { "DELETE", "/api/admin/users/user-1/lockout" },
            { "POST", "/api/admin/users/user-1/ban" },
            { "DELETE", "/api/admin/users/user-1/ban" },
            { "PUT", "/api/admin/users/user-1/game-restrictions" },
            { "POST", "/api/admin/users/user-1/sign-out-everywhere" },
            { "POST", "/api/admin/users/user-1/confirm-email" },
            { "POST", "/api/admin/users/user-1/reset-two-factor" },
            { "POST", "/api/admin/users/user-1/overseer-block" },
            { "DELETE", "/api/admin/users/user-1/overseer-block" },
            { "POST", "/api/admin/users/user-1/account-hold" },
            { "DELETE", "/api/admin/users/user-1/account-hold" },
            { "GET", "/api/admin/users/user-1/deletion-impact" },
            { "POST", "/api/admin/users/user-1/delete" },
            { "POST", "/api/admin/users/bulk/preview" },
            { "GET", "/api/admin/users/bulk" },
            { "GET", "/api/admin/users/bulk/1" },
            { "POST", "/api/admin/users/bulk/1/execute" },
            { "POST", "/api/admin/users/bulk/1/cancel" },
            { "GET", "/api/admin/users/bulk/1/report.csv" },
            { "GET", "/api/admin/users/log?kinds=Banned&skip=0&take=10" }
        };

        private static HttpRequestMessage AdminUserRequest(string method, string url)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method is "POST" or "PUT")
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        [Theory]
        [MemberData(nameof(AdminUserRoutes))]
        public async Task AdminUsers_Unauthenticated_Returns401(string method, string url)
        {
            var response = await _client.SendAsync(AdminUserRequest(method, url), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(AdminUserRoutes))]
        public async Task AdminUsers_NonAdmin_Returns403(string method, string url)
        {
            var request = AdminUserRequest(method, url);
            request.Headers.Add("X-Test-User", "NormalUser");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AdminUsers_Admin_ListReturns200()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?sort=name&take=5");
            request.Headers.Add("X-Test-User", "TestAdmin");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task<long> SeedSystemConfigAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = new SystemAiApiConfiguration
            {
                DisplayName = "Test",
                Provider = "Test",
                ModelId = "Test",
                TotalChatRequestsCount = 100
            };
            db.SystemAiApiConfigurations.Add(config);
            await db.SaveChangesAsync();
            return config.Id;
        }

        [Fact]
        public async Task ResetCounter_InvalidName_Returns400()
        {
            long id = await SeedSystemConfigAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/systemconfigs/{id}/reset")
            {
                Content = JsonContent.Create(new ResetCounterRequest { CounterName = "Id" })
            };
            request.Headers.Add("X-Test-User", "TestAdmin");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ResetCounter_ValidName_Returns200()
        {
            long id = await SeedSystemConfigAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/systemconfigs/{id}/reset")
            {
                Content = JsonContent.Create(new ResetCounterRequest { CounterName = "TotalChatRequestsCount" })
            };
            request.Headers.Add("X-Test-User", "TestAdmin");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
