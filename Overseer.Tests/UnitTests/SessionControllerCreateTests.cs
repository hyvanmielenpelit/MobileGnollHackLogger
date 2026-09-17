using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The game client's session-create endpoint, and what the optional confidential flag changes
/// about the three context rows and the two files it writes.
/// </summary>
public class SessionControllerCreateTests : IDisposable
{
    private const string Secret = "test-antiforgery-token";
    private const string UserId = "game-user-1";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-session-create-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    // ── Identity stubs ──────────────────────────────────────────────────────────
    // Hand-written rather than mocked: the test project has no mocking library, and only two
    // members of the pair are reached — finding the user and checking the password.

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public ApplicationUser? User { get; set; }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult(User);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult(User);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName?.ToUpperInvariant());

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public void Dispose() { }
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly StubUserStore _store;

        public StubUserManager(StubUserStore store)
            : base(store, null!, null!, Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(), null!, null!, null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _store = store;
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName)
            => Task.FromResult(_store.User);
    }

    private sealed class StubClaimsPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
            => Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth")));
    }

    private sealed class StubSignInManager : SignInManager<ApplicationUser>
    {
        public StubSignInManager(UserManager<ApplicationUser> userManager)
            : base(userManager, new HttpContextAccessor(), new StubClaimsPrincipalFactory(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                null!, null!)
        {
        }

        public override Task<Microsoft.AspNetCore.Identity.SignInResult> CheckPasswordSignInAsync(
            ApplicationUser user, string password, bool lockoutOnFailure)
            => Task.FromResult(password == "correct-password"
                ? Microsoft.AspNetCore.Identity.SignInResult.Success
                : Microsoft.AspNetCore.Identity.SignInResult.Failed);
    }

    // ── Harness ─────────────────────────────────────────────────────────────────

    private IConfiguration CreateConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AntiForgeryToken", Secret },
            { "ConversationsDataLocation", _dataDirectory },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (SessionController Controller, ContentProtectionService Protection) CreateController(
        ApplicationDbContext db, IConfiguration config)
    {
        var store = new StubUserStore
        {
            User = new ApplicationUser { Id = UserId, UserName = "gnollhacker" }
        };
        var signInManager = new StubSignInManager(new StubUserManager(store));
        var protection = new ContentProtectionService(new ConfigurationContentKeyRing(config));
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        var controller = new SessionController(
            signInManager, db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), config,
            null!, new OngoingChatManager(config), retention,
            new ConfidentialPolicyResolver(config), protection,
            NullLogger<SessionController>.Instance);

        return (controller, protection);
    }

    private static CreateSessionRequest Request(bool isConfidential) => new()
    {
        UserName = "gnollhacker",
        Password = "correct-password",
        AntiForgeryToken = Secret,
        SnapshotHtml = "<p>Dungeon Level 3</p>",
        MessageHistory = "You hit the newt.\nThe newt dies.",
        DirectoryManifest = "save\t1024\ndumplog\t2048",
        IsGnollHackSession = true,
        IsConfidential = isConfidential
    };

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AConfidentialCreateStoresEnvelopedRowsAndAPolicySnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var result = await controller.Create(Request(isConfidential: true));
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long sessionId = (long)value.sessionId;

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        Assert.NotNull(session);
        Assert.True(session.IsConfidential);
        Assert.False(string.IsNullOrEmpty(session.ConfidentialPolicyJson));
        Assert.False(string.IsNullOrEmpty(session.EncryptedContentKey));

        var messages = await db.ChatMessage.Where(m => m.ChatSessionId == sessionId).ToListAsync(ct);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.True(ContentProtectionService.IsEncrypted(m.Content)));

        // And they still read back as what they are.
        var snapshot = messages.Single(m => m.IsGameSnapshot);
        Assert.Contains("Dungeon Level 3", protection.Decrypt(session, snapshot.Content));
    }

    [Fact]
    public async Task AConfidentialCreateWritesItsContextFilesEnveloped()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var result = await controller.Create(Request(isConfidential: true));
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long sessionId = (long)value.sessionId;

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        var attachments = await db.ChatMessageAttachment
            .Where(a => a.RelativePath != null && a.RelativePath.StartsWith(sessionId.ToString()))
            .ToListAsync(ct);

        Assert.Equal(2, attachments.Count);

        foreach (var attachment in attachments)
        {
            /* The .enc suffix makes the file's state visible on disk; the magic bytes are what
               the reader trusts. A confidential session whose context files sat on disk in
               clear would leave the badge claiming more than holds. */
            Assert.EndsWith(ContentProtectionService.EncryptedFileSuffix, attachment.RelativePath);
            Assert.True(ContentProtectionService.IsEncrypted(attachment.FileName));

            byte[] raw = await File.ReadAllBytesAsync(
                Path.Combine(_dataDirectory, attachment.RelativePath!), ct);
            Assert.True(ContentProtectionService.IsEncryptedFile(raw));

            string name = protection.Decrypt(session!, attachment.FileName)!;
            string text = System.Text.Encoding.UTF8.GetString(protection.DecryptFile(session!, raw));
            if (name == "message_history.txt") Assert.Contains("The newt dies.", text);
            else Assert.Contains("dumplog", text);
        }
    }

    [Fact]
    public async Task AnOrdinaryCreateIsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, _) = CreateController(db, config);

        var result = await controller.Create(Request(isConfidential: false));
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long sessionId = (long)value.sessionId;

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        Assert.NotNull(session);
        Assert.False(session.IsConfidential);
        Assert.True(string.IsNullOrEmpty(session.ConfidentialPolicyJson));
        Assert.True(string.IsNullOrEmpty(session.EncryptedContentKey));

        var messages = await db.ChatMessage.Where(m => m.ChatSessionId == sessionId).ToListAsync(ct);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.False(ContentProtectionService.IsEncrypted(m.Content)));

        var attachments = await db.ChatMessageAttachment.ToListAsync(ct);
        Assert.Equal(2, attachments.Count);
        Assert.All(attachments, a =>
            Assert.False(a.RelativePath!.EndsWith(ContentProtectionService.EncryptedFileSuffix)));
        Assert.Contains(attachments, a => a.FileName == "message_history.txt");
    }

    [Fact]
    public async Task AWrongAntiForgeryTokenIsRefusedBeforeAnythingIsWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, _) = CreateController(db, config);

        var request = Request(isConfidential: true);
        request.AntiForgeryToken = "wrong";

        Assert.IsType<UnauthorizedResult>(await controller.Create(request));
        Assert.Empty(await db.ChatSession.ToListAsync(ct));
    }

    // ── SnapshotText ────────────────────────────────────────────────────────────
    // The field a current app fills with the snapshot it flattened itself. The cases above
    // stay as they are: they are the regression suite for apps that upload raw dump HTML.

    private const string AppFlattenedText = "Dlvl 3\n  #  . @";

    /* The same board as AppFlattenedText, as the engine writes it. Every test that pairs the
       two is asserting that this is not what gets stored. */
    private const string RawDumpHtml =
        "<div>Dlvl 3</div>\n<pre>\n&nbsp;&nbsp;<span>#</span>&nbsp;&nbsp;. @\n</pre>\n";

    /// <summary>
    /// Creates a session and returns the body of its game-snapshot message — the prefix line
    /// removed, and the envelope opened when the session is confidential. Null when no
    /// snapshot message was written.
    /// </summary>
    private static async Task<string?> CreateAndReadSnapshotAsync(
        SessionController controller, ApplicationDbContext db, ContentProtectionService protection,
        CreateSessionRequest request)
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await controller.Create(request);
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long sessionId = (long)value.sessionId;

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        var snapshots = await db.ChatMessage
            .Where(m => m.ChatSessionId == sessionId && m.IsGameSnapshot)
            .ToListAsync(ct);

        if (snapshots.Count == 0) return null;

        string content = protection.Decrypt(session!, Assert.Single(snapshots).Content)!;
        Assert.StartsWith(ChatService.GameSnapshotPrefix + "\n", content);
        return content[(ChatService.GameSnapshotPrefix.Length + 1)..];
    }

    [Fact]
    public async Task SnapshotText_IsStoredAsGivenWithItsColumnSpacingIntact()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotHtml = null;
        request.SnapshotText = AppFlattenedText;

        string? body = await CreateAndReadSnapshotAsync(controller, db, protection, request);

        /* Sanitize() would have collapsed those runs of spaces and flattened the map. That it
           is stored verbatim is the whole point of the second field. */
        Assert.Equal(AppFlattenedText, body);
    }

    [Fact]
    public async Task SnapshotText_WinsOverSnapshotHtml_WhenACurrentAppSendsBoth()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotText = AppFlattenedText;
        request.SnapshotHtml = RawDumpHtml;

        Assert.Equal(AppFlattenedText, await CreateAndReadSnapshotAsync(controller, db, protection, request));
    }

    [Fact]
    public async Task SnapshotHtml_IsNotReadAtAll_WhenSnapshotTextIsPresent()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotText = "Dlvl 3, the text field won";
        request.SnapshotHtml = "<p>Dlvl 17, the html field won</p>";

        string? body = await CreateAndReadSnapshotAsync(controller, db, protection, request);

        Assert.Equal("Dlvl 3, the text field won", body);
        Assert.DoesNotContain("Dlvl 17", body);
    }

    [Fact]
    public async Task ABlankSnapshotText_DoesNotSuppressAUsableSnapshotHtml()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotText = "  \n";
        request.SnapshotHtml = "<p>Dungeon Level 3</p>";

        Assert.Equal("Dungeon Level 3", await CreateAndReadSnapshotAsync(controller, db, protection, request));
    }

    [Fact]
    public async Task AnOversizeSnapshotText_IsCapped()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotHtml = null;
        request.SnapshotText = new string('x', DumpHtmlSanitizer.MaxFlattenedSnapshotChars + 500);

        string? body = await CreateAndReadSnapshotAsync(controller, db, protection, request);

        Assert.NotNull(body);
        Assert.Equal(DumpHtmlSanitizer.MaxFlattenedSnapshotChars, body!.Length);
    }

    [Fact]
    public async Task LiteralAngleBracketsInSnapshotText_Survive()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotHtml = null;
        request.SnapshotText = "1 - Fido, <12,7>, 3 squares away";

        /* Proof the text path does not run Sanitize(): its tag stripper deletes any literal
           <…>, which is why a pet's position needs a field of its own. */
        Assert.Equal("1 - Fido, <12,7>, 3 squares away",
            await CreateAndReadSnapshotAsync(controller, db, protection, request));
    }

    [Fact]
    public async Task AConfidentialCreateEnvelopesASnapshotTextRowTheSameWay()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: true);
        request.SnapshotText = AppFlattenedText;
        request.SnapshotHtml = RawDumpHtml;

        var result = await controller.Create(request);
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long sessionId = (long)value.sessionId;

        var session = await db.ChatSession.FindAsync([sessionId], ct);
        var messages = await db.ChatMessage.Where(m => m.ChatSessionId == sessionId).ToListAsync(ct);

        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.True(ContentProtectionService.IsEncrypted(m.Content)));

        var snapshot = messages.Single(m => m.IsGameSnapshot);
        Assert.EndsWith(AppFlattenedText, protection.Decrypt(session!, snapshot.Content));
    }

    [Fact]
    public async Task NoSnapshotField_WritesNoSnapshotMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var (controller, protection) = CreateController(db, config);

        var request = Request(isConfidential: false);
        request.SnapshotHtml = null;
        request.SnapshotText = null;

        Assert.Null(await CreateAndReadSnapshotAsync(controller, db, protection, request));

        // The other two context rows are unaffected.
        Assert.Equal(2, await db.ChatMessage.CountAsync(ct));
    }
}
