using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
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

/// <summary>
/// The two Stage F regressions the plan singles out as failing silently, plus the read paths
/// that would otherwise hand ciphertext to a user or a model.
/// </summary>
public class ConfidentialEncryptionRegressionTests
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "OverseerEncryptionTests_" + Guid.NewGuid());

    private ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /* SQLite in memory rather than the InMemory provider, for the crypto-shred tests only.
       CryptoShred is deliberately set-based -- ExecuteUpdateAsync reads no ciphertext and needs
       no key material -- and the InMemory provider does not support that translation. A
       relational provider is the only way to assert what the statement actually does, as
       opposed to only that it compiles. */
    private static (ApplicationDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) CreateRelationalDb()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();

        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options);
        db.Database.EnsureCreated();

        /* A relational provider enforces the foreign keys the InMemory provider ignores, so
           the owning user has to exist. Worth knowing: a test that passes on InMemory can be
           storing rows the real database would reject. */
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1", Email = "u1@example.com" });
        db.SaveChanges();

        return (db, connection);
    }

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ConversationsDataLocation", _dataDir },
            { "MaxAttachmentSize", "15728640" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private static ContentProtectionService CreateProtection(IConfiguration config)
        => new(new ConfigurationContentKeyRing(config));

    private ChatController CreateController(
        ApplicationDbContext db, IConfiguration config, ContentProtectionService protection, string userId)
    {
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        return new ChatController(
            db, null!, config, null!, null!, null!, new OngoingChatManager(config), null!, null!,
            retention, null!, new AttachmentValidator(config),
            new ConfidentialityPostureService(config), new ConfidentialPolicyResolver(config), protection,
            new EphemeralSessionStore(config, startSweeper: false))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"))
                }
            }
        };
    }

    // ── R-11: the most dangerous line in the stage ──────────────────────────────

    [Fact]
    public async Task DecryptingHistoryDoesNotWritePlaintextBackIntoTheSession()
    {
        /* pastMessages is loaded TRACKED. Decrypting pm.Content in place would make the
           SaveChangesAsync at the end of the turn write plaintext into a confidential
           session -- inverting the feature, silently, and only for the sessions that asked for
           protection.

           This test models that exact sequence: read the tracked entity, decrypt the way
           prompt assembly does, then save. The stored value must still be an envelope. */
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        var protection = CreateProtection(config);
        using var db = CreateDb();

        var session = new ChatSession
        {
            Id = 1,
            AspNetUserId = "u1",
            IsConfidential = true,
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        protection.EnsureSessionKey(session);
        db.ChatSession.Add(session);

        db.ChatMessage.Add(new ChatMessage
        {
            Id = 1,
            ChatSessionId = 1,
            Role = "user",
            Content = protection.Encrypt(session, "my private question"),
            TimestampUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Prompt assembly: the entities are tracked, and the decrypt goes into a local.
        var loadedSession = await db.ChatSession.FirstAsync(s => s.Id == 1, ct);
        var pastMessages = await db.ChatMessage.Where(m => m.ChatSessionId == 1).ToListAsync(ct);

        var history = new List<string>();
        foreach (var pm in pastMessages)
        {
            var content = protection.Decrypt(loadedSession, pm.Content) ?? "";
            history.Add(content);
        }

        // The turn ends with a save, as every turn does.
        loadedSession.LastMessageUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // The model saw plaintext...
        Assert.Equal("my private question", history.Single());

        // ...and the database still holds the envelope.
        var reloaded = await db.ChatMessage.FirstAsync(m => m.Id == 1, ct);
        Assert.True(ContentProtectionService.IsEncrypted(reloaded.Content),
            $"stored content was written back as plaintext: {reloaded.Content}");
        Assert.StartsWith(ContentProtectionService.RowPrefix, reloaded.Content);
    }

    // ── V-6: the cap the widened column no longer enforces ──────────────────────

    [Fact]
    public async Task ATitleOf256CharactersIsAcceptedAnd257IsRejectedByTheApplication()
    {
        /* The column is 2048 to hold an envelope, so it no longer rejects the 257th character.
           If the application did not, a 2048-character title on a session later upgraded to
           confidential would need 10,973 characters and throw on save -- F3 would have caused
           the very overflow it exists to prevent. */
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        var protection = CreateProtection(config);
        using var db = CreateDb();

        db.ChatSession.Add(new ChatSession
        {
            Id = 1, AspNetUserId = "u1", Title = "start",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        var controller = CreateController(db, config, protection, "u1");

        var accepted = await controller.UpdateSessionTitle(
            1, new ChatController.UpdateTitleRequest { Title = new string('a', 256) });
        Assert.IsType<OkResult>(accepted);

        var rejected = await controller.UpdateSessionTitle(
            1, new ChatController.UpdateTitleRequest { Title = new string('a', 257) });
        var bad = Assert.IsType<BadRequestObjectResult>(rejected);
        Assert.Contains("256", bad.Value!.ToString());
    }

    [Fact]
    public async Task RenamingAConfidentialSessionStoresAnEnvelopeNotPlaintext()
    {
        /* A manual rename that stored plaintext would silently undo the guarantee for the one
           field a user is most likely to type something identifying into. */
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        var protection = CreateProtection(config);
        using var db = CreateDb();

        var session = new ChatSession
        {
            Id = 1, AspNetUserId = "u1", IsConfidential = true, Title = "start",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var controller = CreateController(db, config, protection, "u1");
        var result = await controller.UpdateSessionTitle(
            1, new ChatController.UpdateTitleRequest { Title = "Notes about my medical results" });

        Assert.IsType<OkResult>(result);
        db.ChangeTracker.Clear();

        var stored = await db.ChatSession.FirstAsync(s => s.Id == 1, ct);
        Assert.True(ContentProtectionService.IsEncrypted(stored.Title),
            $"title was stored in clear: {stored.Title}");
        Assert.DoesNotContain("medical", stored.Title);

        // And it comes back readable through the session-list decryption path.
        Assert.Equal("Notes about my medical results", protection.Decrypt(stored, stored.Title));
    }

    [Fact]
    public async Task RenamingANormalSessionStillStoresPlaintext()
    {
        // The encryption is confidential-only; normal chats keep working exactly as before.
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();

        db.ChatSession.Add(new ChatSession
        {
            Id = 1, AspNetUserId = "u1", Title = "start",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var controller = CreateController(db, config, CreateProtection(config), "u1");
        await controller.UpdateSessionTitle(1, new ChatController.UpdateTitleRequest { Title = "A normal chat" });
        db.ChangeTracker.Clear();

        var stored = await db.ChatSession.FirstAsync(s => s.Id == 1, ct);
        Assert.Equal("A normal chat", stored.Title);
        Assert.False(ContentProtectionService.IsEncrypted(stored.Title));
    }

    // ── Crypto-shredding ────────────────────────────────────────────────────────

    [Fact]
    public async Task NullingTheSessionKeyMakesEveryStoredFieldUnreadable()
    {
        /* Crypto-shredding: one column update destroys the key, and the content becomes
           unreadable whatever else survives the deletion. */
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        var protection = CreateProtection(config);
        var (db, connection) = CreateRelationalDb();
        using var _ = db;
        using var __ = connection;

        var session = new ChatSession
        {
            Id = 1, AspNetUserId = "u1", IsConfidential = true,
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        };
        protection.EnsureSessionKey(session);
        session.Title = protection.Encrypt(session, "Sensitive title");
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        await GnollHackServer.Data.Privacy.CryptoShred.NullSessionKeysAsync(db, new[] { 1L }, ct);
        db.ChangeTracker.Clear();

        var shredded = await db.ChatSession.FirstAsync(s => s.Id == 1, ct);
        Assert.Null(shredded.EncryptedContentKey);
        Assert.Null(shredded.ContentKeyNonce);
        Assert.Null(shredded.ContentKeyTag);
        Assert.Null(shredded.ContentKeyVersion);

        // The ciphertext is still there and is now permanently unreadable.
        Assert.True(ContentProtectionService.IsEncrypted(shredded.Title));
        var freshProtection = CreateProtection(config);
        Assert.Equal(ContentProtectionService.UnreadableNotice, freshProtection.Decrypt(shredded, shredded.Title));
    }

    [Fact]
    public async Task ShreddingApiKeyMaterialClearsAllThreeColumns()
    {
        /* A different master key protects these -- AesEncryptionKey, which does not rotate --
           so nulling session content keys does not reach them. Account deletion needs both. */
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = CreateRelationalDb();
        using var _ = db;
        using var __ = connection;

        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            Id = 1, AspNetUserId = "u1", Provider = "OpenAI",
            EncryptedApiKey = "cipher", ApiKeyNonce = "nonce", ApiKeyTag = "tag"
        });
        await db.SaveChangesAsync(ct);

        await GnollHackServer.Data.Privacy.CryptoShred.NullUserApiKeyMaterialAsync(db, "u1", ct);
        db.ChangeTracker.Clear();

        var key = await db.UserAiApiKeys.FirstAsync(k => k.Id == 1, ct);
        Assert.Null(key.EncryptedApiKey);
        Assert.Null(key.ApiKeyNonce);
        Assert.Null(key.ApiKeyTag);

        // The row itself survives: it still carries the posture and the parallel mode.
        Assert.Equal("OpenAI", key.Provider);
    }

    [Fact]
    public async Task ShreddingIsANoOpForAnEmptyIdList()
    {
        var (db, connection) = CreateRelationalDb();
        using var _ = db;
        using var __ = connection;

        Assert.Equal(0, await GnollHackServer.Data.Privacy.CryptoShred.NullSessionKeysAsync(
            db, Array.Empty<long>(), TestContext.Current.CancellationToken));
        Assert.Equal(0, await GnollHackServer.Data.Privacy.CryptoShred.NullUserApiKeyMaterialAsync(
            db, "", TestContext.Current.CancellationToken));
    }
}
