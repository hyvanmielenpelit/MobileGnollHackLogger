using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatMessagePersistenceTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ChatMessage_Persists_ReasoningModeUsed_And_ThinkingLevelUsed_And_ModelDisplayNameUsed()
    {
        using var db = CreateInMemoryDbContext();

        var session = new ChatSession
        {
            Id = 1,
            AspNetUserId = "user-123",
            Title = "Test Session",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);

        var message = new ChatMessage
        {
            ChatSessionId = 1,
            Role = "assistant",
            Content = "Test reasoning output",
            TimestampUtc = DateTime.UtcNow,
            ProviderUsed = "Google",
            ModelUsed = "gemini-2.5-pro",
            ModelDisplayNameUsed = "Gemini 2.5 Pro",
            ThinkingLevelUsed = "high",
            ReasoningModeUsed = "pro",
            TimeToFirstTokenMs = 120,
            TotalDurationMs = 850
        };
        db.ChatMessage.Add(message);
        var ct = TestContext.Current.CancellationToken;
        await db.SaveChangesAsync(ct);

        var savedMessage = await db.ChatMessage.FirstOrDefaultAsync(m => m.Id == message.Id, ct);
        Assert.NotNull(savedMessage);
        Assert.Equal("gemini-2.5-pro", savedMessage.ModelUsed);
        Assert.Equal("Gemini 2.5 Pro", savedMessage.ModelDisplayNameUsed);
        Assert.Equal("high", savedMessage.ThinkingLevelUsed);
        Assert.Equal("pro", savedMessage.ReasoningModeUsed);
        Assert.Equal(120, savedMessage.TimeToFirstTokenMs);
        Assert.Equal(850, savedMessage.TotalDurationMs);
    }
}
