using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Xunit;
using Xunit.Abstractions;

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
        public async Task StreamMessageAsync_Returns_Valid_Response()
        {
            // 1. Build Configuration from User Secrets using the local project secrets
            var config = new ConfigurationBuilder()
                .AddUserSecrets<ChatServiceTests>()
                .Build();

            // Read AI config
            var testProvider = config["AI:Provider"];
            var testApiKey = config["AI:APIKey"];
            var testModel = config["AI:Model"];
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
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiProvider>();
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.AnthropicProvider>();
            services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.GoogleProvider>();
            services.AddScoped<ChatService>();

            var serviceProvider = services.BuildServiceProvider();

            // 3. Seed Database
            var userId = Guid.NewGuid().ToString();
            long testSessionId = 0;
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
                    AllowMultipleModels = true
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
                await dbContext.SaveChangesAsync();

                testSessionId = session.Id;
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
            
            try
            {
                await foreach (var chunk in chatService.StreamMessageAsync(testSessionId, "Say hello in exactly one sentence.", null, claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), false, cts.Token))
                {
                    _output.WriteLine(chunk.Data);
                    fullResponse += chunk.Data;
                    if (chunk.Data != null && (chunk.Data.StartsWith("Error:") || chunk.Data.StartsWith("API Error:")))
                    {
                        errorOccurred = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception: {ex.Message}");
                errorOccurred = true;
            }

            _output.WriteLine("\nFull Response combined: " + fullResponse);

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
