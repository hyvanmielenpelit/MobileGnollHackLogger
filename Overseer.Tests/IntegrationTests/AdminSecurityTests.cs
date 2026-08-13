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
