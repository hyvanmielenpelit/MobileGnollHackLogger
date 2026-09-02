using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers the persistence gate that <c>ChatService</c> applies to the context-window columns:
/// the token columns are written only when the provider actually reported usage, so a turn with
/// no usage report leaves the indicator absent rather than showing a fabricated estimate.
/// </summary>
public class ContextWindowUsageTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ChatSession CreateSession() => new ChatSession
    {
        Id = 1,
        AspNetUserId = "user-123",
        Title = "Context Window Test Session",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Mirrors the assignment in <c>ChatService</c>, so the gate is exercised rather than
    /// restated: <c>LastPromptTokens &gt; 0</c> governs both token columns.
    /// </summary>
    private static ChatMessage BuildAssistantMessage(AgentRunResult runResult, int? contextWindowTokens, int? contextInputLimitTokens) => new ChatMessage
    {
        ChatSessionId = 1,
        Role = "assistant",
        Content = "Reply",
        TimestampUtc = DateTime.UtcNow,
        ContextPromptTokens = runResult.LastPromptTokens > 0 ? runResult.LastPromptTokens : null,
        ContextOutputTokens = runResult.LastPromptTokens > 0 ? runResult.LastOutputTokens : null,
        ContextWindowTokens = contextWindowTokens,
        ContextInputLimitTokens = contextInputLimitTokens
    };

    [Fact]
    public async Task TurnWithUsage_PersistsPromptOutputWindowAndInputLimit()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;
        db.ChatSession.Add(CreateSession());

        var runResult = new AgentRunResult { LastPromptTokens = 204400, LastOutputTokens = 1200 };
        var message = BuildAssistantMessage(runResult, 1000000, 936000);
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(ct);

        var saved = await db.ChatMessage.FirstOrDefaultAsync(m => m.Id == message.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(204400, saved.ContextPromptTokens);
        Assert.Equal(1200, saved.ContextOutputTokens);
        Assert.Equal(1000000, saved.ContextWindowTokens);
        Assert.Equal(936000, saved.ContextInputLimitTokens);
    }

    [Fact]
    public async Task TurnWithoutUsage_LeavesBothTokenColumnsNull()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;
        db.ChatSession.Add(CreateSession());

        // No provider usage report at all: LastPromptTokens stays at its default of zero.
        var runResult = new AgentRunResult();
        var message = BuildAssistantMessage(runResult, 1000000, 936000);
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(ct);

        var saved = await db.ChatMessage.FirstOrDefaultAsync(m => m.Id == message.Id, ct);
        Assert.NotNull(saved);
        Assert.Null(saved.ContextPromptTokens);
        Assert.Null(saved.ContextOutputTokens);
        // The window is still known even when the measurement is not, but the client requires
        // both the prompt count and the window before it shows anything.
        Assert.Equal(1000000, saved.ContextWindowTokens);
    }

    [Fact]
    public async Task ZeroOutputWithNonZeroPrompt_IsStoredAsZeroNotNull()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;
        db.ChatSession.Add(CreateSession());

        // An empty reply is a real measurement, not a missing one.
        var runResult = new AgentRunResult { LastPromptTokens = 5000, LastOutputTokens = 0 };
        var message = BuildAssistantMessage(runResult, 200000, 190000);
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(ct);

        var saved = await db.ChatMessage.FirstOrDefaultAsync(m => m.Id == message.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(5000, saved.ContextPromptTokens);
        Assert.Equal(0, saved.ContextOutputTokens);
    }

    [Fact]
    public async Task LegacyMessage_HasAllFourColumnsNull()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;
        db.ChatSession.Add(CreateSession());

        var message = new ChatMessage
        {
            ChatSessionId = 1,
            Role = "assistant",
            Content = "Saved before the feature existed",
            TimestampUtc = DateTime.UtcNow
        };
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(ct);

        var saved = await db.ChatMessage.FirstOrDefaultAsync(m => m.Id == message.Id, ct);
        Assert.NotNull(saved);
        Assert.Null(saved.ContextPromptTokens);
        Assert.Null(saved.ContextOutputTokens);
        Assert.Null(saved.ContextWindowTokens);
        Assert.Null(saved.ContextInputLimitTokens);
    }
}
