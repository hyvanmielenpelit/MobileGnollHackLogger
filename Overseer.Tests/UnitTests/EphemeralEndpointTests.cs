using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
/// The HTTP surface an incognito chat needs: creating one, reading it back, serving an
/// attachment out of memory, and destroying it.
/// </summary>
public class EphemeralEndpointTests
{
    private const string Owner = "owner-user";
    private const string Other = "other-user";

    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "OverseerEphemeralEndpoints_" + Guid.NewGuid().ToString("N"));

    private ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
            { "ConversationsDataLocation", _dataDir },
            { "MaxAttachmentSize", "15728640" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private ChatController CreateController(
        ApplicationDbContext db, IConfiguration config, EphemeralSessionStore store,
        OngoingChatManager ongoing, string userId)
    {
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        return new ChatController(
            db, null!, config, null!, null!, null!, ongoing, null!, null!,
            retention, null!, new AttachmentValidator(config),
            new ConfidentialityPostureService(config), new ConfidentialPolicyResolver(config),
            new ContentProtectionService(new ConfigurationContentKeyRing(config)), store)
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

    private static ChatSession Template(string userId) => new()
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    [Fact]
    public async Task SendWithIsEphemeralCreatesNoSessionRowAndReturnsAnEphemeralReference()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);

        var result = await controller.Send(new SendMessageRequest
        {
            Message = "Something I would rather not keep",
            IsConfidential = true,
            IsEphemeral = true
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
        Assert.StartsWith(SessionRef.EphemeralPrefix, wire);

        Assert.Empty(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(SessionRef.TryParse(wire, out var reference));
        Assert.True(store.IsOwnedBy(reference, Owner));
        Assert.False(store.IsOwnedBy(reference, Other));
    }

    [Fact]
    public async Task IncognitoWithoutConfidentialityIsRefusedRatherThanQuietlyCorrected()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);

        var result = await controller.Send(new SendMessageRequest
        {
            Message = "hello",
            IsConfidential = false,
            IsEphemeral = true
        });

        // A client asking for one without the other has misunderstood the pair; turning
        // confidentiality on for it would hide that from whoever has to debug it.
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task TheEphemeralSessionCarriesTheConfidentialPolicySnapshot()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        db.UserAiSettings.Add(new UserAiSettings
        {
            AspNetUserId = Owner,
            ConfidentialDisableToolEgress = true
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var ok = Assert.IsType<OkObjectResult>(await controller.Send(
            new SendMessageRequest { Message = "hi", IsConfidential = true, IsEphemeral = true }));
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;

        Assert.True(SessionRef.TryParse(wire, out var reference));
        var held = store.Get(reference, Owner);
        Assert.NotNull(held);
        Assert.True(held!.Session.IsConfidential);
        // Resolved by the same resolver a persisted confidential session uses, so the promises
        // the downstream code reads are identical.
        var policy = ConfidentialPolicyResolver.ReadSnapshot(held.Session);
        Assert.True(policy.DisableToolEgress);
        // Neither retention scalar means anything for a session that is never stored.
        Assert.Equal(0, held.Session.EffectiveRetentionDays);
        Assert.True(held.Session.ImmediatePurgeOnDelete);
    }


    [Fact]
    public async Task GetSessionServesAnIncognitoChatFromMemoryAndMarksItEphemeral()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        held.AddMessage(id => new EphemeralMessage { Id = id, Role = "user", Content = "a question" });
        held.AddMessage(id => new EphemeralMessage { Id = id, Role = "assistant", Content = "an answer" });
        held.AddAttachment(1, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("body"));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var ok = Assert.IsType<OkObjectResult>(await controller.GetSession(held.Ref.ToWireString()));

        dynamic payload = ok.Value!;
        Assert.True((bool)payload.IsEphemeral);
        Assert.True((bool)payload.IsConfidential);
        Assert.Equal(0L, (long)payload.Id);
        Assert.Equal(held.Ref.ToWireString(), (string)payload.SessionRef);
        Assert.Equal(2, ((System.Collections.ICollection)payload.Messages).Count);
    }

    [Fact]
    public async Task GetSessionRefusesSomebodyElsesIncognitoChat()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Other);

        // Not Forbid: a 403 would confirm the chat exists, which is itself information about a
        // session whose whole point is leaving no trace.
        Assert.IsType<NotFoundResult>(await controller.GetSession(held.Ref.ToWireString()));
    }

    [Fact]
    public async Task GetSessionRefusesAClosedIncognitoChat()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        string wire = held.Ref.ToWireString();
        store.Close(held.Ref, Owner);

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        Assert.IsType<NotFoundResult>(await controller.GetSession(wire));
    }

    [Fact]
    public async Task GetSessionStillServesAPersistedSessionByItsNumericId()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        db.ChatSession.Add(new ChatSession { Id = 5, AspNetUserId = Owner, Title = "Saved chat" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var ok = Assert.IsType<OkObjectResult>(await controller.GetSession("5"));

        dynamic payload = ok.Value!;
        Assert.False((bool)payload.IsEphemeral);
        Assert.Equal(5L, (long)payload.Id);
        Assert.Equal("5", (string)payload.SessionRef);
    }

    [Fact]
    public async Task GetSessionRefusesAReferenceThatDoesNotParse()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);

        Assert.IsType<NotFoundResult>(await controller.GetSession("-1"));
        Assert.IsType<NotFoundResult>(await controller.GetSession("eph_nonsense"));
        Assert.IsType<NotFoundResult>(await controller.GetSession(""));
    }

    [Fact]
    public void ClosingDestroysTheChatAndTheSecondCallReportsItGone()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        held.AddAttachment(1, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("secret"));
        var buffer = held.Attachments.Single().Bytes;

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        string wire = held.Ref.ToWireString();

        Assert.IsType<OkResult>(controller.CloseEphemeralSession(wire));
        Assert.All(buffer, b => Assert.Equal(0, b));
        Assert.IsType<NotFoundResult>(controller.CloseEphemeralSession(wire));
    }

    [Fact]
    public void ClosingCancelsAnInFlightTurnFirst()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var ongoing = new OngoingChatManager(config);
        var held = store.Create(Owner, Template(Owner));
        using var cts = new System.Threading.CancellationTokenSource();
        Assert.True(ongoing.TryStart(held.Ref, cts, out _));

        var controller = CreateController(db, config, store, ongoing, Owner);
        Assert.IsType<OkResult>(controller.CloseEphemeralSession(held.Ref.ToWireString()));

        /* A turn still streaming holds this session's plaintext in its own locals; closing the
           store underneath it would leave it talking to a session that no longer exists. */
        Assert.True(cts.IsCancellationRequested);
        Assert.Null(ongoing.TryGet(held.Ref));
    }

    [Fact]
    public void SomebodyElseCannotCloseYourIncognitoChat()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Other);
        Assert.IsType<NotFoundResult>(controller.CloseEphemeralSession(held.Ref.ToWireString()));
        Assert.NotNull(store.Get(held.Ref, Owner));
    }

    [Fact]
    public void TheCloseEndpointRefusesAPersistedReference()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);

        // A saved chat is deleted, not closed. Accepting an id here would make one endpoint mean
        // two different things.
        Assert.IsType<BadRequestObjectResult>(controller.CloseEphemeralSession("5"));
    }

    [Fact]
    public async Task AnEphemeralAttachmentIsServedFromMemoryToItsOwnerOnly()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var bytes = Encoding.UTF8.GetBytes("PNG-ish bytes");
        var attachment = held.AddAttachment(1, "chart.png", "image/png", bytes);
        string wire = held.Ref.ToWireString();

        var owner = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var file = Assert.IsType<FileContentResult>(await owner.GetEphemeralAttachment(wire, attachment.Id, inline: true));
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal("nosniff", owner.Response.Headers["X-Content-Type-Options"]);

        var intruder = CreateController(db, config, store, new OngoingChatManager(config), Other);
        Assert.IsType<NotFoundResult>(await intruder.GetEphemeralAttachment(wire, attachment.Id, inline: true));
    }

    [Fact]
    public async Task ANonImageIsNeverServedInlineUnderItsStoredContentType()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var attachment = held.AddAttachment(1, "report.html", "text/html", Encoding.UTF8.GetBytes("<script>x()</script>"));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var file = Assert.IsType<FileContentResult>(
            await controller.GetEphemeralAttachment(held.Ref.ToWireString(), attachment.Id, inline: true));

        // Same rule as the persisted path: inline rendering is for images, and everything else
        // becomes an octet-stream download rather than a page in the app's own origin.
        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal("report.html", file.FileDownloadName);
    }

    [Fact]
    public async Task AnUnknownAttachmentIndexIsNotFound()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        Assert.IsType<NotFoundResult>(await controller.GetEphemeralAttachment(held.Ref.ToWireString(), 99));
        Assert.IsType<NotFoundResult>(await controller.GetEphemeralAttachment("5", 1));
    }

    [Fact]
    public async Task ContinuingAnIncognitoChatDoesNotFallThroughToCreatingASavedOne()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);
        var result = await controller.Send(new SendMessageRequest
        {
            SessionId = held.Ref.ToWireString(),
            Message = "a follow-up"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
        Assert.Equal(held.Ref.ToWireString(), wire);
        Assert.Empty(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContinuingSomebodyElsesIncognitoChatIsNotFound()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));

        var controller = CreateController(db, config, store, new OngoingChatManager(config), Other);
        var result = await controller.Send(new SendMessageRequest
        {
            SessionId = held.Ref.ToWireString(),
            Message = "let me in"
        });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, held.MessageCount);
    }

    [Fact]
    public async Task AnOrdinarySendStillCreatesASavedSession()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var controller = CreateController(db, config, store, new OngoingChatManager(config), Owner);

        var result = await controller.Send(new SendMessageRequest { Message = "An ordinary question" });

        var ok = Assert.IsType<OkObjectResult>(result);
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
        var session = Assert.Single(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
        // Still the bare decimal id on the wire, so nothing that already stored one has to change.
        Assert.Equal(session.Id.ToString(), wire);
        Assert.False(session.IsConfidential);
        Assert.Equal(0, store.Count);
    }
}
