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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The detached generation task <c>ChatController.Send</c> starts must observe its own faults.
/// An unobserved one is raised later by the finalizer, on a thread that carries none of the
/// turn's confidentiality scope.
/// </summary>
public class DetachedGenerationFaultTests
{
    private const string Owner = "owner-user";

    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "OverseerDetachedFault_" + Guid.NewGuid().ToString("N"));

    /// <summary>Stands in for the scope factory so the detached task faults deterministically.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public const string Marker = "Scope creation refused by the test fixture.";

        public IServiceScope CreateScope() => throw new InvalidOperationException(Marker);
    }

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
            db, null!, config, null!, null!, null!, ongoing, new ThrowingScopeFactory(), null!,
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

    [Fact]
    public async Task AFaultInTheDetachedGenerationTaskIsReportedToTheSessionRatherThanLeftUnobserved()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        using var store = new EphemeralSessionStore(config, startSweeper: false);
        var ongoing = new OngoingChatManager(config);
        var controller = CreateController(db, config, store, ongoing, Owner);

        var result = await controller.Send(new SendMessageRequest
        {
            Message = "A question whose generation faults before it starts",
            IsConfidential = true,
            IsEphemeral = true
        });

        /* The endpoint answers immediately; the fault happens on the detached task afterwards. */
        var ok = Assert.IsType<OkObjectResult>(result);
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
        Assert.True(SessionRef.TryParse(wire, out var sessionRef));

        OngoingGenerationState? state = null;
        for (int waited = 0; waited < 500; waited += 10)
        {
            state = ongoing.TryGet(sessionRef);
            if (state?.IsCompleted == true) break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(state?.IsCompleted == true, "The detached generation task did not complete within 500 ms.");

        var error = state!.AccumulatedEvents.SingleOrDefault(e => e.Type == "error");
        Assert.NotNull(error);

        /* Sequenced like any other event: the client discards by SeqNo, and a null one sorts to
           the head of a resumed replay instead of into its place. */
        Assert.NotNull(error!.SeqNo);
    }
}
