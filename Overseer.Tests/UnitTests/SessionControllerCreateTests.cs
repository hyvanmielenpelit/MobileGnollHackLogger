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
            new ConfidentialPolicyResolver(config), protection);

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
}
