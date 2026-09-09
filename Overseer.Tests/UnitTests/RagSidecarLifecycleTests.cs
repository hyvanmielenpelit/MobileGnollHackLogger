using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Rag;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A retrieval sidecar treated as the content it is: encrypted in a confidential session, never
/// written for an ephemeral one, and removed by the deletions that already remove attachments.
/// </summary>
public class RagSidecarLifecycleTests : IDisposable
{
    private const string Owner = "sidecar-user";

    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "overseer-sidecar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private IConfiguration Config(bool writeSidecars = true)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ConversationsDataLocation", _dataDir },
            { "RagSettings:WriteSidecars", writeSidecars ? "true" : "false" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private static ContentProtectionService Protection(IConfiguration config)
        => new(new ConfigurationContentKeyRing(config));

    private static RagSidecarStore Store(IConfiguration config, ContentProtectionService protection)
        => new(config, protection, NullLogger<RagSidecarStore>.Instance);

    private static ChatSession NewSession(long id = 1, bool confidential = false) => new()
    {
        Id = id,
        AspNetUserId = Owner,
        Title = "Sidecar chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = confidential
    };

    private static IReadOnlyList<DocumentChunk> Chunks() => new[]
    {
        new DocumentChunk { Index = 0, Text = "Quarterly revenue was 4.2 million.", TokenCount = 9, SourceOffset = 0 },
        new DocumentChunk { Index = 1, Text = "Headcount rose to 61 people.", TokenCount = 8, SourceOffset = 34 }
    };

    private string AttachmentPath(long sessionId, string storedName)
    {
        string relative = Path.Combine(sessionId.ToString(), storedName);
        string full = Path.Combine(_dataDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Encoding.UTF8.GetBytes("the original document bytes"));
        return relative;
    }

    [Fact]
    public async Task ANormalSessionsSidecarIsWrittenAsReadableJsonBesideTheAttachment()
    {
        var config = Config();
        var protection = Protection(config);
        var store = Store(config, protection);
        var session = NewSession();
        string attachment = AttachmentPath(session.Id, "a1b2c3.pdf");

        string? written = await store.TryWriteAsync(
            _dataDir, attachment, session, isEphemeralSession: false, encrypt: false,
            "report.pdf", Chunks(), Array.Empty<float[]>(), "none",
            TestContext.Current.CancellationToken);

        Assert.NotNull(written);
        // Inside the session directory, which is what makes the existing recursive deletes
        // remove it without a fourth code path.
        Assert.StartsWith(session.Id.ToString(), written!);
        Assert.EndsWith(RagSidecarStore.SidecarSuffix, written);

        string text = await File.ReadAllTextAsync(Path.Combine(_dataDir, written), TestContext.Current.CancellationToken);
        Assert.Contains("Quarterly revenue", text);
    }

    [Fact]
    public async Task AConfidentialSessionsSidecarIsEncrypted()
    {
        /* Chunk text is verbatim document content and an embedding is invertible enough to be
           treated the same way, so the sidecar gets the session's own DEK exactly as the
           attachment bytes do. */
        var config = Config();
        var protection = Protection(config);
        var store = Store(config, protection);
        var session = NewSession(id: 2, confidential: true);
        protection.EnsureSessionKey(session);
        string attachment = AttachmentPath(session.Id, "d4e5f6.pdf.enc");

        string? written = await store.TryWriteAsync(
            _dataDir, attachment, session, isEphemeralSession: false, encrypt: true,
            "report.pdf", Chunks(), new[] { new[] { 0.1f, 0.2f } }, "all-MiniLM-L6-v2",
            TestContext.Current.CancellationToken);

        Assert.NotNull(written);
        Assert.EndsWith(ContentProtectionService.EncryptedFileSuffix, written);

        byte[] onDisk = await File.ReadAllBytesAsync(Path.Combine(_dataDir, written!), TestContext.Current.CancellationToken);
        string raw = Encoding.UTF8.GetString(onDisk);
        Assert.DoesNotContain("Quarterly revenue", raw);
        Assert.DoesNotContain("Headcount", raw);
        // The file magic, not the suffix, is what a reader trusts.
        Assert.StartsWith("ENC1", raw);

        var readBack = await store.TryReadAsync(_dataDir, attachment, session, TestContext.Current.CancellationToken);
        Assert.NotNull(readBack);
        Assert.Equal(2, readBack!.Chunks.Count);
        Assert.Contains("Quarterly revenue", readBack.Chunks[0].Text);
        Assert.Equal("all-MiniLM-L6-v2", readBack.EmbeddingModel);
    }

    [Fact]
    public async Task AnEphemeralSessionWritesNoSidecar()
    {
        // The same rule as every other file in an incognito chat, not an exception to it: a
        // sidecar is a file.
        var config = Config();
        var store = Store(config, Protection(config));
        var session = NewSession(id: 0);

        string? written = await store.TryWriteAsync(
            _dataDir, Path.Combine("0", "x.pdf"), session, isEphemeralSession: true, encrypt: false,
            "report.pdf", Chunks(), Array.Empty<float[]>(), "none",
            TestContext.Current.CancellationToken);

        Assert.Null(written);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "0")));
    }

    [Fact]
    public async Task WritingIsOffByDefaultAndTheFlagIsWhatEnablesIt()
    {
        /* Off by default because nothing reads a sidecar yet: an uploaded document reaches the
           model on the turn it is attached and is not replayed afterwards, so writing one today
           would put content on disk for no consumer. */
        var config = Config(writeSidecars: false);
        var store = Store(config, Protection(config));
        var session = NewSession(id: 3);
        string attachment = AttachmentPath(session.Id, "g7h8i9.pdf");

        Assert.False(store.WriteEnabled);

        string? written = await store.TryWriteAsync(
            _dataDir, attachment, session, isEphemeralSession: false, encrypt: false,
            "report.pdf", Chunks(), Array.Empty<float[]>(), "none",
            TestContext.Current.CancellationToken);

        Assert.Null(written);
        Assert.False(File.Exists(Path.Combine(_dataDir, attachment + RagSidecarStore.SidecarSuffix)));
    }

    [Fact]
    public void AMalformedFlagReadsAsOffRatherThanThrowing()
    {
        // ConfigurationBinder.GetValue throws on a value it cannot convert, and this is resolved
        // during a turn: a typo in appsettings.json must not become a failed chat.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "RagSettings:WriteSidecars", "yes please" }
        }).Build();

        var store = new RagSidecarStore(config, Protection(Config()), NullLogger<RagSidecarStore>.Instance);

        Assert.False(store.WriteEnabled);
    }

    [Fact]
    public async Task ASidecarIsRemovedWithTheSessionDirectoryOnAPermanentPurge()
    {
        /* The plan's assertion, and the reason the sidecar lives inside the session directory:
           the purge, the orphan sweep and account deletion all remove that directory
           recursively, so a sidecar needs no deletion path of its own -- and therefore has no
           path that could be forgotten. */
        var config = Config();
        var protection = Protection(config);
        var store = Store(config, protection);

        /* SQLite in memory rather than the InMemory provider: the purge crypto-shreds the
           session key first, through ExecuteUpdateAsync, which InMemory cannot translate. A
           relational provider is the only way to run the real purge path rather than a stand-in
           for it. */
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        // A relational provider enforces the foreign key the InMemory provider ignores.
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "sidecar-tester", Email = "sidecar@example.com" });
        var session = NewSession(id: 77, confidential: true);
        protection.EnsureSessionKey(session);
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        string attachment = AttachmentPath(session.Id, "j1k2l3.pdf.enc");
        string? sidecar = await store.TryWriteAsync(
            _dataDir, attachment, session, isEphemeralSession: false, encrypt: true,
            "report.pdf", Chunks(), Array.Empty<float[]>(), "none",
            TestContext.Current.CancellationToken);

        Assert.NotNull(sidecar);
        Assert.True(File.Exists(Path.Combine(_dataDir, sidecar!)));

        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        await retention.PermanentlyPurgeSessionsAsync(
            new List<long> { session.Id }, isDryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(_dataDir, sidecar!)));
        Assert.False(Directory.Exists(Path.Combine(_dataDir, session.Id.ToString())));
    }

    [Fact]
    public async Task AnAbsentSidecarReadsAsNullRatherThanThrowing()
    {
        var config = Config();
        var store = Store(config, Protection(config));

        var result = await store.TryReadAsync(
            _dataDir, Path.Combine("999", "nothing.pdf"), NewSession(id: 999),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task AShreddedSessionsSidecarReadsAsNullRatherThanThrowing()
    {
        /* A crypto-shredded session is a normal outcome of a partial deletion, not a
           programming error -- the same reason Decrypt returns a notice rather than throwing. */
        var config = Config();
        var protection = Protection(config);
        var store = Store(config, protection);
        var session = NewSession(id: 4, confidential: true);
        protection.EnsureSessionKey(session);
        string attachment = AttachmentPath(session.Id, "m4n5o6.pdf.enc");

        await store.TryWriteAsync(
            _dataDir, attachment, session, isEphemeralSession: false, encrypt: true,
            "report.pdf", Chunks(), Array.Empty<float[]>(), "none",
            TestContext.Current.CancellationToken);

        session.EncryptedContentKey = null;
        session.ContentKeyNonce = null;
        session.ContentKeyTag = null;

        var result = await store.TryReadAsync(
            _dataDir, attachment, session, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public void TheSidecarPathDropsTheAttachmentsOwnEncryptedSuffix()
    {
        // Otherwise an encrypted attachment's sidecar would be named "x.pdf.enc.rag.json.enc",
        // which reads as though the .enc applied twice.
        Assert.Equal("7/x.pdf.rag.json", RagSidecarStore.SidecarRelativePath("7/x.pdf.enc").Replace('\\', '/'));
        Assert.Equal("7/x.pdf.rag.json", RagSidecarStore.SidecarRelativePath("7/x.pdf").Replace('\\', '/'));
    }
}
