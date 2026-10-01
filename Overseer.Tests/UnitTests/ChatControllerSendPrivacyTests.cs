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
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What the creating turn tells the client about its own privacy, and what the two session
/// lists say about which chats are confidential.
/// </summary>
/// <remarks>
/// The creating turn is the only moment a client learns the state of a chat it has just
/// started without asking a second time: before this, a chat started as Confidential showed no
/// badge and suppressed no telemetry until the window was reloaded.
/// </remarks>
public class ChatControllerSendPrivacyTests
{
    private const string Owner = "user-1";

    private static ApplicationDbContext CreateDb(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options);

    private static IConfiguration CreateConfig(string? floorPersistence = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "PrivacySettings:Ephemeral:TimeoutMinutes", "30" },
            { "PrivacySettings:ConfidentialFloor:Persistence", floorPersistence },
            { "ConversationsDataLocation", Path.Combine(Path.GetTempPath(), "OverseerSendPrivacy_" + Guid.NewGuid()) },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    /// <summary>Records every <see cref="ChatSession.Title"/> as it is about to be written.</summary>
    private sealed class TitleWriteRecorder : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public List<string?> Written { get; } = new();

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            System.Threading.CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<ChatSession>())
            {
                if (entry.State == EntityState.Added || entry.Property(s => s.Title).IsModified)
                    Written.Add(entry.Entity.Title);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private const string PrivateFirstMessage = "My character is named Alice Example and lives on Dlvl 4";

    [Fact]
    public async Task ANewConfidentialChatStoresItsStartingTitleEncryptedAndNeverInClear()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        var recorder = new TitleWriteRecorder();
        using var db = CreateDb(recorder);
        var controller = CreateController(db, config);

        var result = await controller.Send(new SendMessageRequest { Message = PrivateFirstMessage, IsConfidential = true });
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long id = long.Parse((string)value.sessionId);

        var stored = await db.ChatSession.AsNoTracking().SingleAsync(s => s.Id == id, ct);
        Assert.StartsWith(ContentProtectionService.RowPrefix, stored.Title);

        string expected = PrivateFirstMessage.Length > 50 ? PrivateFirstMessage.Substring(0, 47) + "..." : PrivateFirstMessage;
        var protection = new ContentProtectionService(new ConfigurationContentKeyRing(config));
        Assert.Equal(expected, protection.Decrypt(stored, stored.Title));

        Assert.NotEmpty(recorder.Written);
        Assert.DoesNotContain(recorder.Written, t => t != null && t.Contains("Alice", StringComparison.Ordinal));
        Assert.All(recorder.Written, t => Assert.True(
            t == ChatController.ConfidentialPlaceholderTitle || ContentProtectionService.IsEncrypted(t)));
    }

    [Fact]
    public async Task ANewConfidentialChatUnderAPlaintextPolicyStoresItsTitleInClear()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig(floorPersistence: "Plaintext");
        using var db = CreateDb();
        var controller = CreateController(db, config);

        var result = await controller.Send(new SendMessageRequest { Message = "Readable title", IsConfidential = true });
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        long id = long.Parse((string)value.sessionId);

        var stored = await db.ChatSession.AsNoTracking().SingleAsync(s => s.Id == id, ct);
        Assert.True(stored.IsConfidential);
        Assert.Equal("Readable title", stored.Title);
        Assert.False(ContentProtectionService.HasSessionKey(stored));
    }

    [Fact]
    public async Task TheSessionListCarriesTheDeleteBehavior()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        db.ChatSession.Add(new ChatSession
        {
            Id = 1, AspNetUserId = Owner, Title = "Ordinary",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        db.ChatSession.Add(new ChatSession
        {
            Id = 2, AspNetUserId = Owner, Title = "Private", IsConfidential = true, ImmediatePurgeOnDelete = true,
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        dynamic value = Assert.IsType<OkObjectResult>(await controller.GetSessions()).Value!;
        var rows = ((System.Collections.IEnumerable)value.sessions).Cast<dynamic>().ToList();

        Assert.True((bool)rows.Single(r => (long)r.Id == 2).ImmediatePurgeOnDelete);
        Assert.False((bool)rows.Single(r => (long)r.Id == 1).ImmediatePurgeOnDelete);
        Assert.Equal(1, (int)value.immediatePurgeCount);
        Assert.Equal(0, (int)value.immediatePurgePinnedCount);
    }

    [Fact]
    public async Task SearchIncludesAPlaintextPolicyConfidentialChatAndExcludesAnEncryptedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        ChatSession Confidential(long id, string title, ConfidentialPersistence? persistence)
        {
            var session = new ChatSession
            {
                Id = id, AspNetUserId = Owner, Title = title, IsConfidential = true,
                CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
            };
            if (persistence.HasValue)
            {
                ConfidentialPolicyResolver.ApplyToSession(
                    session, ConfidentialPolicy.Defaults with { Persistence = persistence.Value });
            }
            return session;
        }

        db.ChatSession.Add(Confidential(1, "Mines plan readable", ConfidentialPersistence.Plaintext));
        db.ChatSession.Add(Confidential(2, "Mines plan encrypted", ConfidentialPersistence.Encrypted));
        db.ChatSession.Add(Confidential(3, "Mines plan no snapshot", null));
        db.ChatSession.Add(new ChatSession
        {
            Id = 4, AspNetUserId = Owner, Title = "Mines plan ordinary",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        dynamic value = Assert.IsType<OkObjectResult>(await controller.GetSessions(search: "Mines")).Value!;
        var ids = ((System.Collections.IEnumerable)value.sessions).Cast<dynamic>().Select(r => (long)r.Id).OrderBy(i => i).ToList();

        Assert.Equal(new long[] { 1, 4 }, ids);
        Assert.Equal(2, (int)value.confidentialExcludedCount);
    }

    private static ChatController CreateController(
        ApplicationDbContext db, IConfiguration config, string userId = Owner)
    {
        var protection = new ContentProtectionService(new ConfigurationContentKeyRing(config));
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

    [Fact]
    public async Task SendCreatingAConfidentialChatReportsTheModeAndABadge()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        var result = await controller.Send(new SendMessageRequest
        {
            Message = "Something private",
            IsConfidential = true
        });

        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.True((bool)value.isConfidential);
        Assert.False((bool)value.isEphemeral);
        Assert.NotNull(value.privateBadge);
        Assert.Null(value.ephemeralExpiresUtc);
    }

    [Fact]
    public async Task SendCreatingAStandardChatReportsNoModeAndNoBadge()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        var result = await controller.Send(new SendMessageRequest { Message = "Something ordinary" });

        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.False((bool)value.isConfidential);
        Assert.False((bool)value.isEphemeral);
        Assert.Null(value.privateBadge);
        Assert.Null(value.ephemeralExpiresUtc);
    }

    [Fact]
    public async Task SendCreatingAnIncognitoChatReportsAFutureDeadline()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        var result = await controller.Send(new SendMessageRequest
        {
            Message = "Something transient",
            IsConfidential = true,
            IsEphemeral = true
        });

        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.StartsWith("eph_", (string)value.sessionId);
        Assert.True((bool)value.isEphemeral);
        Assert.True((bool)value.isConfidential);

        DateTime? expires = value.ephemeralExpiresUtc;
        Assert.NotNull(expires);
        Assert.True(expires > DateTime.UtcNow);
    }

    [Fact]
    public async Task TheSessionListCarriesTheConfidentialMarker()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        db.ChatSession.Add(new ChatSession
        {
            Id = 1, AspNetUserId = Owner, Title = "Ordinary",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        db.ChatSession.Add(new ChatSession
        {
            Id = 2, AspNetUserId = Owner, Title = "Private", IsConfidential = true,
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        dynamic value = Assert.IsType<OkObjectResult>(await controller.GetSessions()).Value!;
        var rows = ((System.Collections.IEnumerable)value.sessions).Cast<dynamic>().ToList();

        Assert.Equal(2, rows.Count);
        Assert.True((bool)rows.Single(r => (long)r.Id == 2).IsConfidential);
        Assert.False((bool)rows.Single(r => (long)r.Id == 1).IsConfidential);
    }

    /* The trash list's own projection has no test here: it computes DaysRemaining with
       EF.Functions.DateDiffDay, a SQL Server function that neither the in-memory provider nor
       SQLite can evaluate, so GetTrashSessions cannot be driven from this suite at all. The
       marker it carries is covered by the manual check instead. */

    [Fact]
    public async Task UpgradingToConfidentialReturnsTheBadgeOnBothPaths()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(db, config);

        db.ChatSession.Add(new ChatSession
        {
            Id = 4, AspNetUserId = Owner, Title = "Ordinary",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        var first = await controller.SetSessionConfidential(4, new ChatController.SetConfidentialRequest
        {
            IsConfidential = true
        });
        dynamic firstValue = Assert.IsType<OkObjectResult>(first).Value!;

        Assert.True((bool)firstValue.isConfidential);
        Assert.False((bool)firstValue.retroactive);
        Assert.NotNull(firstValue.privateBadge);

        // The idempotent path a retrying client takes carries it too.
        var again = await controller.SetSessionConfidential(4, new ChatController.SetConfidentialRequest
        {
            IsConfidential = true
        });
        dynamic againValue = Assert.IsType<OkObjectResult>(again).Value!;

        Assert.True((bool)againValue.alreadyConfidential);
        Assert.NotNull(againValue.privateBadge);
    }
}
