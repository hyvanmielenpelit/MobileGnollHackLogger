using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Telemetry;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>The queries behind AI Telemetry's Limits sub-tab, and the journaled read of a turn's prompt.</summary>
public class AdminChatTurnLimitsTests
{
    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ChatTurnLimitEvent Event(string code, string? userId = "u1", AiKeySource keys = AiKeySource.System, byte severity = 0) => new()
    {
        OccurredUtc = DateTime.UtcNow,
        Code = code,
        AspNetUserId = userId,
        KeySource = keys,
        Severity = severity,
        AssistantMessageId = Random.Shared.NextInt64(1, long.MaxValue)
    };

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "alice" });
        db.Users.Add(new ApplicationUser { Id = "u2", UserName = "bob" });
        db.ChatTurnLimitEvents.AddRange(
            Event(TurnLimitCodes.ResearchRounds),
            Event(TurnLimitCodes.ResearchRounds, "u2"),
            Event(TurnLimitCodes.NoFinalAnswer, severity: 1),
            Event(TurnLimitCodes.ProviderBusy, "u2", AiKeySource.UserKey));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task Summary_CountsByCode_InTheSelectedKeySources()
    {
        using var db = await SeedAsync();
        var service = new ChatTurnLimitQueryService(db);

        var summary = await service.GetSummaryAsync(null, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(3, summary.TotalHits);
        Assert.Equal(1, summary.TurnsWithoutAnswer);
        Assert.Equal(2, summary.UsersAffected);
        var rounds = Assert.Single(summary.ByCode, c => c.Code == TurnLimitCodes.ResearchRounds);
        Assert.Equal(2, rounds.Hits);
        Assert.Equal(2, rounds.Users);

        var both = await service.GetSummaryAsync(null, null, "both", TestContext.Current.CancellationToken);
        Assert.Equal(4, both.TotalHits);
    }

    [Fact]
    public async Task Events_FilterByCodeSeverityAndUserName_AndClampTake()
    {
        using var db = await SeedAsync();
        var service = new ChatTurnLimitQueryService(db);
        var ct = TestContext.Current.CancellationToken;

        var byCode = await service.GetEventsAsync(null, null, "both", TurnLimitCodes.ProviderBusy, null, null, 0, 20, ct);
        Assert.Equal("bob", Assert.Single(byCode.Items).UserName);

        var bySeverity = await service.GetEventsAsync(null, null, null, null, "error", null, 0, 20, ct);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, Assert.Single(bySeverity.Items).Code);

        var byUser = await service.GetEventsAsync(null, null, null, null, null, "ali", 0, 20, ct);
        Assert.Equal(2, byUser.TotalCount);

        var paged = await service.GetEventsAsync(null, null, "both", null, null, null, 0, 0, ct);
        Assert.Equal(4, paged.TotalCount);
        Assert.Single(paged.Items);
    }

    private static async Task<(ApplicationDbContext Db, long EventId)> SeedPromptAsync(
        bool confidential = false, bool ephemeral = false, string content = "How many pages?", bool messageExists = true)
    {
        var db = NewDb();
        var ct = TestContext.Current.CancellationToken;
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "alice" });
        var session = new ChatSession { AspNetUserId = "u1", Title = "t", IsConfidential = confidential, CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        long messageId = 0;
        if (messageExists)
        {
            var message = new ChatMessage { ChatSessionId = session.Id, Role = "user", Content = content, TimestampUtc = DateTime.UtcNow };
            db.ChatMessage.Add(message);
            await db.SaveChangesAsync(ct);
            messageId = message.Id;
        }

        var limitEvent = Event(TurnLimitCodes.ResearchRounds);
        limitEvent.WasConfidential = confidential;
        limitEvent.WasEphemeral = ephemeral;
        limitEvent.ChatSessionId = ephemeral ? null : session.Id;
        limitEvent.UserMessageId = ephemeral ? null : (messageExists ? messageId : 99999);
        db.ChatTurnLimitEvents.Add(limitEvent);
        await db.SaveChangesAsync(ct);
        return (db, limitEvent.Id);
    }

    [Fact]
    public async Task ReadingAPrompt_ReturnsTheTextAndWritesAJournalRow()
    {
        var (db, id) = await SeedPromptAsync();
        using var _ = db;
        var ct = TestContext.Current.CancellationToken;

        var (status, prompt) = await new ChatTurnLimitQueryService(db).ReadPromptAsync(id, "admin-id", "root", "127.0.0.1", ct);

        Assert.Equal(ChatTurnLimitQueryService.PromptReadStatus.Ok, status);
        Assert.Equal("How many pages?", prompt!.Text);
        Assert.False(prompt.Truncated);

        var row = await db.ChatAccessAuditLogs.SingleAsync(ct);
        Assert.Equal(nameof(ChatAccessAction.AdminReadLimitPrompt), row.Action);
        Assert.Equal("admin-id", row.ActorUserId);
        Assert.Equal("u1", row.SubjectUserId);
        Assert.True(row.ActorWasAdmin);
        Assert.DoesNotContain("How many", row.Detail);
    }

    [Fact]
    public async Task ALongPrompt_IsTruncated()
    {
        var (db, id) = await SeedPromptAsync(content: new string('x', 5000));
        using var _ = db;

        var (_, prompt) = await new ChatTurnLimitQueryService(db).ReadPromptAsync(id, "a", "root", null, TestContext.Current.CancellationToken);

        Assert.Equal(ChatTurnLimitQueryService.MaxPromptChars, prompt!.Text.Length);
        Assert.True(prompt.Truncated);
    }

    [Fact]
    public async Task AConfidentialPrompt_IsRefused_WithNoJournalRow()
    {
        var (db, id) = await SeedPromptAsync(confidential: true);
        using var _ = db;
        var ct = TestContext.Current.CancellationToken;

        var (status, prompt) = await new ChatTurnLimitQueryService(db).ReadPromptAsync(id, "a", "root", null, ct);

        Assert.Equal(ChatTurnLimitQueryService.PromptReadStatus.Confidential, status);
        Assert.Null(prompt);
        Assert.Empty(await db.ChatAccessAuditLogs.ToListAsync(ct));
    }

    [Fact]
    public async Task AGoneMessage_IsNotFound()
    {
        var (db, id) = await SeedPromptAsync(messageExists: false);
        using var _ = db;

        var (status, _) = await new ChatTurnLimitQueryService(db).ReadPromptAsync(id, "a", "root", null, TestContext.Current.CancellationToken);

        Assert.Equal(ChatTurnLimitQueryService.PromptReadStatus.NotFound, status);
    }

    [Theory]
    [InlineData(false, false, true, ChatTurnLimitQueryService.PromptAvailable)]
    [InlineData(true, false, true, ChatTurnLimitQueryService.PromptConfidential)]
    [InlineData(true, true, true, ChatTurnLimitQueryService.PromptIncognito)]
    [InlineData(false, false, false, ChatTurnLimitQueryService.PromptDeleted)]
    public async Task Detail_NamesThePromptState(bool confidential, bool ephemeral, bool exists, string expected)
    {
        var (db, id) = await SeedPromptAsync(confidential, ephemeral, messageExists: exists);
        using var _ = db;

        var detail = await new ChatTurnLimitQueryService(db).GetEventAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(expected, detail!.PromptState);
    }

    [Fact]
    public async Task Detail_IsNull_ForAnUnknownId()
    {
        using var db = NewDb();

        Assert.Null(await new ChatTurnLimitQueryService(db).GetEventAsync(1, TestContext.Current.CancellationToken));
    }
}
