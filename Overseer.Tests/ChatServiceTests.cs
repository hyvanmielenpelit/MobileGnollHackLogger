using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services;
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
            var aesKey = config["Overseer:AesEncryptionKey"];
            Assert.False(string.IsNullOrEmpty(aesKey), "Overseer:AesEncryptionKey is not configured in User Secrets.");

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: "ChatServiceTestDb"));

            services.AddSingleton<IConfiguration>(config);
            services.AddHttpClient();
            services.AddScoped<CryptoService>();
            services.AddScoped<WikiService>();
            services.AddScoped<ChatService>();

            var serviceProvider = services.BuildServiceProvider();

            // 3. Seed Database
            var userId = Guid.NewGuid().ToString();
            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cryptoService = scope.ServiceProvider.GetRequiredService<CryptoService>();

                // Mock user
                var user = new ApplicationUser { Id = userId, UserName = "testuser" };
                dbContext.Users.Add(user);

                // Setup Settings
                var (ciphertext, nonce, tag) = cryptoService.Encrypt(testApiKey, userId);
                var aiSettings = new UserAiSettings
                {
                    AspNetUserId = userId,
                    DefaultProvider = testProvider,
                    DefaultModel = testModel,
                    ThinkingLevel = testThinkingLevel,
                    EncryptedApiKey = ciphertext,
                    ApiKeyNonce = nonce,
                    ApiKeyTag = tag
                };
                dbContext.UserAiSettings.Add(aiSettings);
                await dbContext.SaveChangesAsync();
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
                await foreach (var chunk in chatService.StreamMessageAsync(null, "Say hello in exactly one sentence.", claimsPrincipal, cts.Token))
                {
                    _output.WriteLine(chunk);
                    fullResponse += chunk;
                    if (chunk.StartsWith("Error:") || chunk.StartsWith("API Error:"))
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
}
