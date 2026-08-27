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
            ServiceTierUsed = "priority",
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
        Assert.Equal("priority", savedMessage.ServiceTierUsed);
        Assert.Equal(120, savedMessage.TimeToFirstTokenMs);
        Assert.Equal(850, savedMessage.TotalDurationMs);
    }

    [Fact]
    public async Task UserAiModel_And_SystemAiApiConfiguration_Persist_ServiceTier()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;

        var userModel = new UserAiModel
        {
            AspNetUserId = "user-123",
            Provider = "OpenAI",
            ModelId = "gpt-5",
            DisplayName = "GPT-5",
            ServiceTier = "priority"
        };
        db.UserAiModels.Add(userModel);

        var sysConfig = new SystemAiApiConfiguration
        {
            DisplayName = "Claude 3.7",
            Provider = "Anthropic",
            ModelId = "claude-3-7-sonnet-20250219",
            ServiceTier = "auto"
        };
        db.SystemAiApiConfigurations.Add(sysConfig);

        await db.SaveChangesAsync(ct);

        var savedUserModel = await db.UserAiModels.FirstOrDefaultAsync(m => m.Id == userModel.Id, ct);
        Assert.NotNull(savedUserModel);
        Assert.Equal("priority", savedUserModel.ServiceTier);

        var savedSysConfig = await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == sysConfig.Id, ct);
        Assert.NotNull(savedSysConfig);
        Assert.Equal("auto", savedSysConfig.ServiceTier);
    }
}
