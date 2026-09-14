using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatAttachSnapshotTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IConfiguration CreateTestConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "5" },
            { "ChatRetentionSettings:MaxPinnedSessionsPerUser", "2" },
            { "ChatRetentionSettings:InactivityTtlDays", "90" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" },
            { "ChatRetentionSettings:PruneToolCallResultsDays", "30" },
            { "ConversationsDataLocation", Path.Combine(Path.GetTempPath(), "OverseerTestConversations_" + Guid.NewGuid()) },
            // A confidential attach encrypts, so the keyring has to be configured for these tests.
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static ChatController CreateController(ApplicationDbContext db, string userId = "test-user")
        => CreateController(db, CreateTestConfiguration(), userId);

    private static ChatController CreateController(
        ApplicationDbContext db, IConfiguration config, string userId)
    {
        var retentionService = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var attachmentValidator = new Overseer.Services.Privacy.AttachmentValidator(config);
        // GetSession consults it for in-flight generations; a real one is cheap and returns none.
        var ongoingChatManager = new OngoingChatManager(config);
        var postureService = new Overseer.Services.Privacy.ConfidentialityPostureService(config);
        var policyResolver = new Overseer.Services.Privacy.ConfidentialPolicyResolver(config);
        var contentProtection = new Overseer.Services.Privacy.ContentProtectionService(
            new Overseer.Services.Privacy.ConfigurationContentKeyRing(config));
        var controller = new ChatController(db, null!, config, null!, null!, null!, ongoingChatManager, null!, null!, retentionService, null!, attachmentValidator, postureService, policyResolver, contentProtection,
            new Overseer.Services.Privacy.EphemeralSessionStore(config, startSweeper: false))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId)
                    }, "TestAuth"))
                }
            }
        };
        return controller;
    }

    [Fact]
    public async Task AttachSnapshot_WithNoSession_CreatesSession_SetsGameModeFlags_ReturnsId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var request = new AttachGameSnapshotRequest
        {
            SessionId = null,
            SnapshotText = "Dungeon Level 1\nPlayer HP: 20/20"
        };

        var result = await controller.AttachSnapshot(request);
        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic val = okResult.Value!;
        long createdSessionId = long.Parse((string)val.sessionId);
        bool hasGameSnapshot = (bool)val.hasGameSnapshot;

        Assert.True(createdSessionId > 0);
        Assert.True(hasGameSnapshot);
        Assert.False((bool)val.isConfidential);
        Assert.False((bool)val.isEphemeral);
        Assert.Null(val.privateBadge);

        var session = await db.ChatSession.FindAsync([createdSessionId], ct);
        Assert.NotNull(session);
        Assert.Equal("user-1", session.AspNetUserId);
        Assert.Equal("GnollHack Session", session.Title);
        Assert.True(session.IsGnollHackSession);
        Assert.Equal("{\"BoolData\":{\"isGameOn\":true}}", session.ClientSettings);

        var messages = await db.ChatMessage.Where(m => m.ChatSessionId == createdSessionId).ToListAsync(ct);
        Assert.Single(messages);
        var sysMsg = messages[0];
        Assert.Equal("system", sysMsg.Role);
        Assert.StartsWith(ChatService.GameSnapshotPrefix + "\n", sysMsg.Content);
        Assert.Contains("Dungeon Level 1", sysMsg.Content);
        // AttachSnapshot is the Overseer-UI writer, and the flag is what detection reads now.
        Assert.True(sysMsg.IsGameSnapshot);
    }

    [Fact]
    public async Task AttachSnapshot_ToExistingSession_SupersedesOlderSnapshot_LeavesOneLiveSnapshot_PreservesFlags()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var session = new ChatSession
        {
            Id = 10,
            AspNetUserId = "user-1",
            Title = "Existing Chat",
            IsGnollHackSession = false,
            ClientSettings = "{\"BoolData\":{\"isGameOn\":false}}",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);

        var oldSnapshot = new ChatMessage
        {
            ChatSessionId = 10,
            Role = "system",
            Content = ChatService.GameSnapshotPrefix + "\nOld Snapshot Data",
            IsGameSnapshot = true,
            TimestampUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        db.ChatMessage.Add(oldSnapshot);
        await db.SaveChangesAsync(ct);

        var request = new AttachGameSnapshotRequest
        {
            SessionId = "10",
            SnapshotText = "New Snapshot Data"
        };

        var result = await controller.AttachSnapshot(request);
        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic val = okResult.Value!;
        Assert.Equal("10", (string)val.sessionId);
        Assert.True((bool)val.hasGameSnapshot);

        // Check flags preserved
        var updatedSession = await db.ChatSession.FindAsync([10L], ct);
        Assert.NotNull(updatedSession);
        Assert.False(updatedSession.IsGnollHackSession);
        Assert.Equal("{\"BoolData\":{\"isGameOn\":false}}", updatedSession.ClientSettings);

        // Check messages: old is superseded, new is active
        var messages = await db.ChatMessage.Where(m => m.ChatSessionId == 10).OrderBy(m => m.TimestampUtc).ToListAsync(ct);
        Assert.Equal(2, messages.Count);

        Assert.Equal(ChatService.GameSnapshotSupersededMarker, messages[0].Content);
        Assert.False(ChatService.IsGameSnapshotMessage(messages[0].Content));
        /* The flag must be cleared with the content. Left set, hasGameSnapshot fires on the
           marker, the next attach re-selects this row, and StripGameSnapshotPrefix is handed
           marker text -- the regression that content-based detection could not have. */
        Assert.False(messages[0].IsGameSnapshot);

        Assert.StartsWith(ChatService.GameSnapshotPrefix + "\n", messages[1].Content);
        Assert.True(ChatService.IsGameSnapshotMessage(messages[1].Content));
        Assert.True(messages[1].IsGameSnapshot);
    }

    [Fact]
    public async Task AttachSnapshot_Twice_LeavesExactlyOneFlaggedSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var first = await controller.AttachSnapshot(new AttachGameSnapshotRequest { SnapshotText = "First board" });
        dynamic firstVal = Assert.IsType<OkObjectResult>(first).Value!;
        string sessionRef = (string)firstVal.sessionId;
        long sessionId = long.Parse(sessionRef);

        await controller.AttachSnapshot(new AttachGameSnapshotRequest { SessionId = sessionRef, SnapshotText = "Second board" });

        var messages = await db.ChatMessage
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.TimestampUtc)
            .ToListAsync(ct);

        Assert.Equal(2, messages.Count);
        Assert.Single(messages, m => m.IsGameSnapshot);
        Assert.Contains("Second board", messages.Single(m => m.IsGameSnapshot).Content);

        // And the session reads as carrying a snapshot, from the live row only.
        var loaded = Assert.IsType<OkObjectResult>(await controller.GetSession(sessionId.ToString()));
        dynamic loadedVal = loaded.Value!;
        Assert.True((bool)loadedVal.hasGameSnapshot);
    }

    [Fact]
    public async Task AttachSnapshot_EmptyText_ReturnsBadRequest()
    {
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var request = new AttachGameSnapshotRequest
        {
            SnapshotText = "   \t\n  "
        };

        var result = await controller.AttachSnapshot(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AttachSnapshot_AnotherUserSession_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var session = new ChatSession
        {
            Id = 20,
            AspNetUserId = "other-user",
            Title = "Other's Chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new AttachGameSnapshotRequest
        {
            SessionId = "20",
            SnapshotText = "Some Snapshot"
        };

        var result = await controller.AttachSnapshot(request);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AttachSnapshot_WithIsConfidential_CreatesConfidentialSessionWithPolicySnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "Confidential board",
            IsConfidential = true
        });

        dynamic val = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.True((bool)val.isConfidential);
        Assert.False((bool)val.isEphemeral);
        Assert.NotNull(val.privateBadge);

        long sessionId = long.Parse((string)val.sessionId);
        var session = await db.ChatSession.FindAsync([sessionId], ct);
        Assert.NotNull(session);
        Assert.True(session.IsConfidential);
        Assert.False(string.IsNullOrEmpty(session.ConfidentialPolicyJson));
    }

    [Fact]
    public async Task AttachSnapshot_OnConfidentialSession_EncryptsSnapshotAndSupersededMarker()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var first = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "First confidential board",
            IsConfidential = true
        });
        dynamic firstVal = Assert.IsType<OkObjectResult>(first).Value!;
        string sessionRef = (string)firstVal.sessionId;
        long sessionId = long.Parse(sessionRef);

        await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SessionId = sessionRef,
            SnapshotText = "Second confidential board"
        });

        var messages = await db.ChatMessage
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.TimestampUtc)
            .ToListAsync(ct);

        Assert.Equal(2, messages.Count);
        /* Both rows, not only the live one: a superseded marker written in clear would leave a
           confidential session with plaintext rows the badge claims are encrypted. */
        Assert.All(messages, m =>
            Assert.True(Overseer.Services.Privacy.ContentProtectionService.IsEncrypted(m.Content)));
        Assert.Single(messages, m => m.IsGameSnapshot);

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        Assert.NotNull(session);
        Assert.False(string.IsNullOrEmpty(session.EncryptedContentKey));
    }

    [Fact]
    public async Task AttachSnapshot_WithIsEphemeral_WritesNoRowAndReturnsEphemeralReference()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "Incognito board",
            IsConfidential = true,
            IsEphemeral = true
        });

        dynamic val = Assert.IsType<OkObjectResult>(result).Value!;
        string sessionRef = (string)val.sessionId;

        Assert.StartsWith("eph_", sessionRef);
        Assert.True((bool)val.isEphemeral);
        Assert.True((bool)val.isConfidential);
        Assert.NotNull(val.ephemeralExpiresUtc);

        Assert.Empty(await db.ChatSession.ToListAsync(ct));
        Assert.Empty(await db.ChatMessage.ToListAsync(ct));

        var loaded = Assert.IsType<OkObjectResult>(await controller.GetSession(sessionRef));
        dynamic loadedVal = loaded.Value!;
        Assert.True((bool)loadedVal.hasGameSnapshot);
    }

    [Fact]
    public async Task AttachSnapshot_ToEphemeralReference_SupersedesTheEarlierSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var first = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "First incognito board",
            IsConfidential = true,
            IsEphemeral = true
        });
        dynamic firstVal = Assert.IsType<OkObjectResult>(first).Value!;
        string sessionRef = (string)firstVal.sessionId;

        var second = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SessionId = sessionRef,
            SnapshotText = "Second incognito board"
        });
        Assert.IsType<OkObjectResult>(second);

        Assert.Empty(await db.ChatMessage.ToListAsync(ct));

        var loaded = Assert.IsType<OkObjectResult>(await controller.GetSession(sessionRef));
        dynamic loadedVal = loaded.Value!;
        Assert.True((bool)loadedVal.hasGameSnapshot);
    }

    [Fact]
    public async Task AttachSnapshot_EphemeralWithoutConfidential_ReturnsBadRequest()
    {
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "Incognito board",
            IsEphemeral = true
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AttachSnapshot_ToUnknownEphemeralReference_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var controller = CreateController(db, "user-1");

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SessionId = "eph_" + Guid.NewGuid().ToString("D"),
            SnapshotText = "Orphaned board"
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
