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

/// <summary>
/// Covers ChatController.GetAttachment: what is served inline, what is forced to download, and
/// which stored content types are echoed back.
/// </summary>
public class AttachmentServingTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "OverseerAttachmentServingTests_" + Guid.NewGuid());

    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "ConversationsDataLocation", _dataDir },
            { "MaxAttachmentSize", "15728640" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private ChatController CreateController(ApplicationDbContext db, IConfiguration config, string userId)
    {
        var retentionService = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var validator = new Overseer.Services.Privacy.AttachmentValidator(config);
        var postureService = new Overseer.Services.Privacy.ConfidentialityPostureService(config);
        var policyResolver = new Overseer.Services.Privacy.ConfidentialPolicyResolver(config);
        var contentProtection = new Overseer.Services.Privacy.ContentProtectionService(
            new Overseer.Services.Privacy.ConfigurationContentKeyRing(config));
        return new ChatController(db, null!, config, null!, null!, null!, null!, null!, null!, retentionService, null!, validator, postureService, policyResolver, contentProtection,
            new Overseer.Services.Privacy.EphemeralSessionStore(config, startSweeper: false))
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

    /// <summary>Seeds a session, a message and one attachment whose file exists on disk.</summary>
    private async Task<long> SeedAttachmentAsync(
        ApplicationDbContext db, string userId, string fileName, string? contentType, byte[] bytes)
    {
        var session = new ChatSession
        {
            AspNetUserId = userId,
            Title = "Chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var message = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "user",
            Content = "here",
            TimestampUtc = DateTime.UtcNow
        };
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        string stored = Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
        string relPath = Path.Combine(session.Id.ToString(), stored);
        Directory.CreateDirectory(Path.Combine(_dataDir, session.Id.ToString()));
        await File.WriteAllBytesAsync(Path.Combine(_dataDir, relPath), bytes, TestContext.Current.CancellationToken);

        var attachment = new ChatMessageAttachment
        {
            ChatMessageId = message.Id,
            FileName = fileName,
            ContentType = contentType,
            RelativePath = relPath
        };
        db.ChatMessageAttachment.Add(attachment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return attachment.Id;
    }

    [Fact]
    public async Task GetAttachment_HtmlRequestedInline_IsStillServedAsADownload()
    {
        /* The exploitable case: an uploaded .html fetched with ?inline=true would render in
           the application's own origin. */
        using var db = CreateInMemoryDbContext();
        var config = CreateConfiguration();
        long id = await SeedAttachmentAsync(
            db, "user-1", "dump.html", "text/html", System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"));

        var controller = CreateController(db, config, "user-1");
        var result = await controller.GetAttachment(id, inline: true);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("dump.html", file.FileDownloadName);
        Assert.Equal("text/html", file.ContentType);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public async Task GetAttachment_NonAllowlistedStoredContentType_IsNeverEchoed()
    {
        using var db = CreateInMemoryDbContext();
        var config = CreateConfiguration();
        long id = await SeedAttachmentAsync(
            db, "user-1", "logo.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes("<svg/>"));

        var controller = CreateController(db, config, "user-1");
        var result = await controller.GetAttachment(id, inline: true);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal("logo.svg", file.FileDownloadName);
    }

    [Fact]
    public async Task GetAttachment_AllowlistedImageInline_IsServedInline()
    {
        using var db = CreateInMemoryDbContext();
        var config = CreateConfiguration();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        long id = await SeedAttachmentAsync(db, "user-1", "board.png", "image/png", png);

        var controller = CreateController(db, config, "user-1");
        var result = await controller.GetAttachment(id, inline: true);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("image/png", file.ContentType);
        // Empty rather than null: PhysicalFileResult's default. Either way no attachment disposition.
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
    }

    [Fact]
    public async Task GetAttachment_AllowlistedImageWithoutInline_StillDownloads()
    {
        using var db = CreateInMemoryDbContext();
        var config = CreateConfiguration();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        long id = await SeedAttachmentAsync(db, "user-1", "board.png", "image/png", png);

        var controller = CreateController(db, config, "user-1");
        var result = await controller.GetAttachment(id, inline: false);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("board.png", file.FileDownloadName);
    }

    [Fact]
    public async Task GetAttachment_AnotherUsersAttachment_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var config = CreateConfiguration();
        long id = await SeedAttachmentAsync(
            db, "owner", "notes.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes("private"));

        var controller = CreateController(db, config, "someone-else");
        var result = await controller.GetAttachment(id);

        Assert.IsType<NotFoundResult>(result);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
