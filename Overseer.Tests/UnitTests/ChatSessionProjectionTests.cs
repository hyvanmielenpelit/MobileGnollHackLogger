using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>What a loaded chat returns of each reply: the measured model and the breakdown, and the stored text with its reasoning.</summary>
public sealed class ChatSessionProjectionTests
{
    private const string UserId = "projection-user";
    private const string Stored = "<div class=\"ai-thought\">\n\nPRIVATE REASONING\n\n</div>\n\nThe answer.";

    private static readonly string BreakdownJson = ContextBreakdownEstimator.ToJson(new ContextBreakdown { History = 7, CacheRead = 3 })!;

    private static (ChatController Controller, EphemeralSessionStore Store, ApplicationDbContext Db) Create()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
            { "ConversationsDataLocation", Path.Combine(Path.GetTempPath(), "OverseerProjection_" + Guid.NewGuid()) },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(new byte[32]) },
            { "PrivacySettings:ActiveKeyVersion", "v1" }
        }).Build();
        var store = new EphemeralSessionStore(config, startSweeper: false);
        var metadata = new ModelMetadataService();
        var settings = new SettingsService(db, new CryptoService(config), new ConfidentialityPostureService(config), new ModelAvailabilityService(metadata));
        var controller = new ChatController(db, null!, config, null!, null!, null!, new OngoingChatManager(config), null!, settings,
            new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance), null!,
            new AttachmentValidator(config), new ConfidentialityPostureService(config), new ConfidentialPolicyResolver(config),
            new ContentProtectionService(new ConfigurationContentKeyRing(config)), store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "TestAuth"))
                }
            }
        };
        return (controller, store, db);
    }

    private static JsonElement ReplyOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return document.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant").Clone();
    }

    [Fact]
    public async Task AStoredReply_ReturnsItsModelBreakdownAndReasoning()
    {
        var (controller, _, db) = Create();
        using (db)
        {
            var session = new ChatSession { AspNetUserId = UserId, Title = "Chat", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
            db.ChatSession.Add(session);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = session.Id, Role = "assistant", Content = Stored, TimestampUtc = DateTime.UtcNow, ProviderUsed = "Google", ModelUsed = "gemini-x", ContextBreakdownJson = BreakdownJson });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var reply = ReplyOf(await controller.GetSession(session.Id.ToString()));

            Assert.Contains("PRIVATE REASONING", reply.GetProperty("content").GetString());
            Assert.Equal("Google", reply.GetProperty("providerUsed").GetString());
            Assert.Equal("gemini-x", reply.GetProperty("modelUsed").GetString());
            Assert.Equal(7, reply.GetProperty("contextBreakdown").GetProperty("history").GetInt32());
            Assert.Equal(3, reply.GetProperty("contextBreakdown").GetProperty("cacheRead").GetInt32());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task AnOldOrMalformedRow_ReturnsNoBreakdown(string? stored)
    {
        var (controller, _, db) = Create();
        using (db)
        {
            var session = new ChatSession { AspNetUserId = UserId, Title = "Chat", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
            db.ChatSession.Add(session);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = session.Id, Role = "assistant", Content = "Old", TimestampUtc = DateTime.UtcNow, ContextBreakdownJson = stored });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var reply = ReplyOf(await controller.GetSession(session.Id.ToString()));

            Assert.Equal(JsonValueKind.Null, reply.GetProperty("contextBreakdown").ValueKind);
            Assert.Equal(JsonValueKind.Null, reply.GetProperty("providerUsed").ValueKind);
        }
    }

    [Fact]
    public async Task AnIncognitoReply_ReturnsTheSameFields()
    {
        var (controller, store, db) = Create();
        using (db)
        {
            var held = store.Create(UserId, new ChatSession { AspNetUserId = UserId, Title = "Incognito", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
            held.AddMessage(id => EphemeralMessage.From(id, new ChatMessage { Role = "assistant", Content = Stored, TimestampUtc = DateTime.UtcNow, ProviderUsed = "Google", ModelUsed = "gemini-x", ContextBreakdownJson = BreakdownJson }));

            var reply = ReplyOf(await controller.GetSession(held.Ref.ToWireString()));

            Assert.Equal("gemini-x", reply.GetProperty("modelUsed").GetString());
            Assert.Equal(7, reply.GetProperty("contextBreakdown").GetProperty("history").GetInt32());
        }
    }
}
