using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Xunit;

// ====================================================================================
// IMPORTANT: This test file connects to external AI APIs and consumes quota.
// 
// To run the test suite while SKIPPING this file (to save AI API quota), use:
// dotnet test --filter "Category!=UsesExternalApi"
// ====================================================================================

namespace Overseer.Tests
{
    public class ChatServiceTests
    {
        private readonly ITestOutputHelper _output;

        public ChatServiceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "UsesExternalApi")]
        public async Task StreamMessageAsync_Returns_Valid_Response()
        {
            // 1. Build Configuration from User Secrets using the local project secrets
            var config = new ConfigurationBuilder()
                .AddUserSecrets<ChatServiceTests>()
                .Build();

            // Read AI config
            var testProvider = config["AI:Provider"] ?? "";
            var testApiKey = config["AI:APIKey"] ?? "";
            var testModel = config["AI:Model"] ?? "";
            var testThinkingLevel = config["AI:ThinkingLevel"];

            Assert.False(string.IsNullOrEmpty(testProvider), "AI:Provider is not configured in User Secrets.");
            Assert.False(string.IsNullOrEmpty(testApiKey), "AI:APIKey is not configured in User Secrets.");

            // Also need AesEncryptionKey for CryptoService
            var aesKey = config["AesEncryptionKey"];
            Assert.False(string.IsNullOrEmpty(aesKey), "AesEncryptionKey is not configured in User Secrets.");

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: "ChatServiceTestDb"));

            services.AddSingleton<IConfiguration>(config);
            services.AddHttpClient();
            services.AddSignalR(); // Adds IHubContext<ChatHub>
            services.AddMemoryCache();
            services.AddSingleton<Overseer.Services.Tools.IClientToolBridge, DummyClientToolBridge>();
            services.AddScoped<Overseer.Services.Tools.ToolRegistry>();
            services.AddScoped<Overseer.Services.Tools.ToolExecutor>();
            services.AddScoped<CryptoService>();
            services.AddScoped<WikiService>();
            services.AddScoped<ModelMetadataService>();
            services.AddScoped<KnowledgeBaseService>();
            services.AddScoped<OngoingChatManager>();
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiResponsesProvider>();
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.AnthropicProvider>();
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.GoogleProvider>();
            services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
            services.AddSingleton<Overseer.Services.ParallelExecutionResolver>();
            services.AddScoped<ChatService>();

            var serviceProvider = services.BuildServiceProvider();

            var userId = Guid.NewGuid().ToString();
            long testSessionId = 0;
            long testUserModelId = 0;
            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cryptoService = scope.ServiceProvider.GetRequiredService<CryptoService>();

                // Mock user
                var user = new ApplicationUser { Id = userId, UserName = "testuser" };
                dbContext.Users.Add(user);

                // Setup Settings
                var aiSettings = new UserAiSettings
                {
                    AspNetUserId = userId,
                    EnableGameActions = true
                };
                dbContext.UserAiSettings.Add(aiSettings);
                
                var (ciphertext, nonce, tag) = cryptoService.Encrypt(testApiKey, userId);
                var apiKey = new UserAiApiKey
                {
                    AspNetUserId = userId,
                    Provider = testProvider,
                    EncryptedApiKey = ciphertext,
                    ApiKeyNonce = nonce,
                    ApiKeyTag = tag
                };
                dbContext.UserAiApiKeys.Add(apiKey);
                
                var aiModel = new UserAiModel
                {
                    AspNetUserId = userId,
                    Provider = testProvider,
                    ModelId = testModel,
                    ThinkingLevel = testThinkingLevel,
                    OrderIndex = 0
                };
                dbContext.UserAiModels.Add(aiModel);

                var session = new ChatSession
                {
                    AspNetUserId = userId,
                    Title = "Test Session",
                    CreatedUtc = DateTime.UtcNow,
                    LastMessageUtc = DateTime.UtcNow
                };
                dbContext.ChatSession.Add(session);
                await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

                testSessionId = session.Id;
                testUserModelId = aiModel.Id;
            }

            // 4. Test StreamMessageAsync
            var chatService = serviceProvider.GetRequiredService<ChatService>();

            // Mock ClaimsPrincipal
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            // Create cancellation token (10 second timeout)
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            _output.WriteLine($"Testing Provider: {testProvider}, Model: {testModel}, ThinkingLevel: {testThinkingLevel}");
            _output.WriteLine("Sending Message: 'Say hello in exactly one sentence.'");
            _output.WriteLine("Response:");

            string fullResponse = "";
            bool errorOccurred = false;
            
            bool rateLimitOrServiceError = false;
            
            try
            {
                await foreach (var chunk in chatService.StreamMessageAsync(testSessionId, "Say hello in exactly one sentence.", null, claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) ?? userId, false, cts.Token, testUserModelId))
                {
                    _output.WriteLine(chunk.Data ?? string.Empty);
                    fullResponse += chunk.Data;
                    if (chunk.Data != null && (chunk.Data.StartsWith("Error:") || chunk.Data.StartsWith("API Error:")))
                    {
                        if (chunk.Data.Contains("429") || chunk.Data.Contains("503") || chunk.Data.Contains("Too Many Requests") || chunk.Data.Contains("Service Unavailable"))
                        {
                            rateLimitOrServiceError = true;
                        }
                        else
                        {
                            errorOccurred = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception: {ex.Message}");
                if (ex.Message.Contains("429") || ex.Message.Contains("503") || ex.Message.Contains("Too Many Requests") || ex.Message.Contains("Service Unavailable"))
                {
                    rateLimitOrServiceError = true;
                }
                else
                {
                    errorOccurred = true;
                }
            }

            _output.WriteLine("\nFull Response combined: " + fullResponse);

            if (rateLimitOrServiceError)
            {
                _output.WriteLine("WARNING: The external API returned a 429 (Rate Limit) or 503 (Service Unavailable) error. The test is passing gracefully as this is an expected network/quota condition.");
                Assert.True(true);
                return;
            }

            Assert.False(errorOccurred, "An error occurred during streaming.");
            Assert.False(string.IsNullOrWhiteSpace(fullResponse), "The response from the AI provider was empty.");
        }
    }

    public class DummyClientToolBridge : Overseer.Services.Tools.IClientToolBridge
    {
        public bool IsClientConnected { get; set; } = true;
        
        public Task<Overseer.Services.Tools.ToolResult> SendToolRequestAsync(long sessionId, string toolName, System.Text.Json.JsonElement parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Overseer.Services.Tools.ToolResult { Success = true, Content = "Dummy result" });
        }
    }
}
