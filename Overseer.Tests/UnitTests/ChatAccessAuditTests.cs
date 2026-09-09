using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Privacy;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The access journal: what it records, what it refuses to record, and what it survives.
/// </summary>
public class ChatAccessAuditTests
{
    private const string Owner = "audit-user";

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// SQLite in memory, for the one operation that needs a relational provider.
    /// </summary>
    /// <remarks>
    /// <see cref="ChatAccessAudit.PruneAsync"/> is set-based — it reads no rows — and the EF
    /// in-memory provider cannot translate <c>ExecuteDeleteAsync</c>. Asserting a retention
    /// policy by "it compiles" is not enough for the one operation that removes an audit row.
    /// </remarks>
    private static (ApplicationDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) NewRelationalDb()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return (db, connection);
    }

    [Fact]
    public async Task ASessionReadIsRecordedWithWhoWhatAndWhen()
    {
        using var db = NewDb();

        bool written = await ChatAccessAudit.RecordAsync(
            db,
            ChatAccessAction.SessionRead,
            actorUserId: Owner,
            actorUserName: "reader",
            actorWasAdmin: false,
            chatSessionId: 42,
            sessionRef: "42",
            wasConfidential: true,
            ipAddress: "203.0.113.7",
            detail: "12 messages",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(written);

        var row = Assert.Single(await db.ChatAccessAuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(nameof(ChatAccessAction.SessionRead), row.Action);
        Assert.Equal(Owner, row.ActorUserId);
        Assert.Equal("reader", row.ActorUserName);
        Assert.Equal(42, row.ChatSessionId);
        Assert.Equal("42", row.SessionRef);
        Assert.True(row.WasConfidential);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("12 messages", row.Detail);
        Assert.True(row.OccurredUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task TheActionIsStoredAsAStringSoAnOldRowStaysReadable()
    {
        /* An audit row outlives the code that wrote it. Stored as an integer, reordering the enum
           would silently change what old rows mean. */
        using var db = NewDb();

        foreach (var action in Enum.GetValues<ChatAccessAction>())
        {
            await ChatAccessAudit.RecordAsync(
                db, action, Owner, cancellationToken: TestContext.Current.CancellationToken);
        }

        var stored = await db.ChatAccessAuditLogs
            .Select(a => a.Action)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Enum.GetValues<ChatAccessAction>().Select(a => a.ToString()).OrderBy(a => a),
            stored.OrderBy(a => a));
    }

    [Fact]
    public async Task TheSubjectDefaultsToTheActorSoSelfAccessIsStillAttributed()
    {
        // The interesting rows are the ones where subject and actor differ; the default keeps the
        // column usable as a filter either way.
        using var db = NewDb();

        await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.SessionRead, actorUserId: Owner,
            cancellationToken: TestContext.Current.CancellationToken);
        await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.SessionRead, actorUserId: "administrator", subjectUserId: Owner,
            cancellationToken: TestContext.Current.CancellationToken);

        var rows = await db.ChatAccessAuditLogs.OrderBy(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Owner, rows[0].SubjectUserId);
        Assert.Equal("administrator", rows[1].ActorUserId);
        Assert.Equal(Owner, rows[1].SubjectUserId);
    }

    [Fact]
    public async Task AMaintenancePassRecordsNoActorRatherThanAStandIn()
    {
        /* Recording a placeholder id would make an automated purge indistinguishable from a
           person's, which is the one distinction an investigation most needs. */
        using var db = NewDb();

        await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.Erasure, actorUserId: null, actorUserName: "retention",
            chatSessionId: 7, detail: "permanent purge",
            cancellationToken: TestContext.Current.CancellationToken);

        var row = Assert.Single(await db.ChatAccessAuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(row.ActorUserId);
        Assert.Equal("retention", row.ActorUserName);
    }

    [Fact]
    public async Task AnOverlongValueIsClampedRatherThanThrowing()
    {
        // A long user agent or a long detail string must not turn a read into a 500 on a column
        // width.
        using var db = NewDb();

        bool written = await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.AttachmentRead, actorUserId: Owner,
            detail: new string('x', 5000),
            sessionRef: new string('y', 500),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(written);
        var row = Assert.Single(await db.ChatAccessAuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(512, row.Detail!.Length);
        Assert.Equal(64, row.SessionRef!.Length);
    }

    [Fact]
    public async Task AnEphemeralAccessIsRecordedByItsReferenceAndNoId()
    {
        /* An incognito session has no row and therefore no id, but it is still someone's
           conversation being read. That it was read is a different class of fact from what was in
           it, and the mode's promise is about the latter. */
        using var db = NewDb();
        string reference = "eph_11111111-2222-3333-4444-555555555555";

        await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.SessionRead, actorUserId: Owner,
            sessionRef: reference, wasConfidential: true, detail: "3 messages, incognito",
            cancellationToken: TestContext.Current.CancellationToken);

        var row = Assert.Single(await db.ChatAccessAuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(row.ChatSessionId);
        Assert.Equal(reference, row.SessionRef);
    }

    [Fact]
    public async Task ARecordingFailureIsReportedRatherThanThrown()
    {
        /* A read must not fail because its journal entry could not be written. Whether that is
           the right trade is a deployment decision, and it is recorded in the class remarks --
           what matters here is that the behaviour is deliberate and observable. */
        var db = NewDb();
        db.Dispose();

        bool written = await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.SessionRead, actorUserId: Owner,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(written);
    }

    // ── retention ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PruningRemovesOnlyRowsPastTheWindow()
    {
        var (db, connection) = NewRelationalDb();
        using var _ = db;
        using var __ = connection;

        db.ChatAccessAuditLogs.AddRange(
            new ChatAccessAuditLog { Action = "SessionRead", OccurredUtc = DateTime.UtcNow.AddDays(-400) },
            new ChatAccessAuditLog { Action = "SessionRead", OccurredUtc = DateTime.UtcNow.AddDays(-366) },
            new ChatAccessAuditLog { Action = "SessionRead", OccurredUtc = DateTime.UtcNow.AddDays(-364) },
            new ChatAccessAuditLog { Action = "SessionRead", OccurredUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        int pruned = await ChatAccessAudit.PruneAsync(db, 365, TestContext.Current.CancellationToken);

        Assert.Equal(2, pruned);
        Assert.Equal(2, await db.ChatAccessAuditLogs.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RetentionCanBeDisabledEntirely(int retentionDays)
    {
        /* Zero means the journal is never pruned, which is what a deployment that needs it
           genuinely append-only sets -- although real immutability needs storage the application
           cannot rewrite, which no setting here provides. */
        var (db, connection) = NewRelationalDb();
        using var _ = db;
        using var __ = connection;

        db.ChatAccessAuditLogs.Add(new ChatAccessAuditLog
        {
            Action = "SessionRead",
            OccurredUtc = DateTime.UtcNow.AddYears(-10)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        int pruned = await ChatAccessAudit.PruneAsync(db, retentionDays, TestContext.Current.CancellationToken);

        Assert.Equal(0, pruned);
        Assert.Equal(1, await db.ChatAccessAuditLogs.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnErasureRecordSurvivesTheConversationItRecords()
    {
        /* The reason no relationship is configured to ChatSession: a cascade would delete the
           audit row along with the conversation, which is exactly backwards. This is the test
           that would fail if someone added the foreign key that looks missing. */
        var (db, connection) = NewRelationalDb();
        using var _ = db;
        using var __ = connection;

        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "u", Email = "u@example.com" });
        var session = new ChatSession
        {
            AspNetUserId = Owner,
            Title = "Doomed",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ChatAccessAudit.RecordAsync(
            db, ChatAccessAction.Erasure, actorUserId: null, actorUserName: "retention",
            chatSessionId: session.Id, sessionRef: session.Id.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);

        db.ChatSession.Remove(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(await db.ChatAccessAuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(session.Id, row.ChatSessionId);
        Assert.Empty(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }
}
