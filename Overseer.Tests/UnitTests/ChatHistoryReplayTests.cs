using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What a chat turn sends of its stored history, and what it reports about the turn: reasoning is stripped
/// from the request and kept in the row, images are sized by their dimensions, and every stored format keeps
/// replaying.
/// </summary>
public sealed class ChatHistoryReplayTests : ChatTurnHarness
{
    private const string Thought = "<div class=\"ai-thought\">\n\nPRIVATE REASONING\n\n</div>\n\n";

    private static ScriptedAiProvider Answering() => new()
    {
        Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Completed() }
    };

    private static async Task<long> AddReplyAsync(ServiceProvider container, long sessionId, string content, Action<ChatMessage>? configure = null)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = "user", Content = "Earlier", TimestampUtc = DateTime.UtcNow.AddMinutes(-2) });
        var reply = new ChatMessage { ChatSessionId = sessionId, Role = "assistant", Content = content, TimestampUtc = DateTime.UtcNow.AddMinutes(-1) };
        configure?.Invoke(reply);
        db.ChatMessage.Add(reply);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return reply.Id;
    }

    private static string ReplayedAssistantText(ScriptedAiProvider provider)
    {
        var message = Assert.Single(provider.Histories[0], m => ProviderHelper.GetProperty(m, "role")?.ToString() == "assistant");
        return ProviderHelper.GetProperty(message, "content")!.ToString()!;
    }

    [Fact]
    public async Task HistoryReplay_SkipsAnEmptyAssistantRow()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Completed() }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = "user", Content = "Earlier", TimestampUtc = DateTime.UtcNow.AddMinutes(-2) });
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = "assistant", Content = "", TimestampUtc = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        var history = Assert.Single(provider.Histories);
        Assert.DoesNotContain(history, m => ProviderHelper.GetProperty(m, "role")?.ToString() == "assistant");
    }

    [Fact]
    public async Task AStoredReply_ReachesTheProviderWithoutItsReasoning_AndTheRowKeepsIt()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        long replyId = await AddReplyAsync(container, sessionId, Thought + "The visible answer.");

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.Equal("The visible answer.", ReplayedAssistantText(provider));
        Assert.Contains("PRIVATE REASONING", Read(container, db => db.ChatMessage.Single(m => m.Id == replyId)).Content);
    }

    [Fact]
    public async Task TheSettingOn_ReplaysTheReasoningAsBefore()
    {
        var provider = Answering();
        using var container = BuildContainer(provider, new Dictionary<string, string?> { { "AiPerformanceSettings:IncludeThoughtsInHistory", "true" } });
        var (userId, sessionId) = await SeedAsync(container);
        await AddReplyAsync(container, sessionId, Thought + "The visible answer.");

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.Contains("PRIVATE REASONING", ReplayedAssistantText(provider));
    }

    [Fact]
    public async Task AConfidentialReply_IsStrippedAfterDecryption_AndNeverWrittenBack()
    {
        var provider = Answering();
        using var container = BuildContainer(provider, new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(new byte[32]) },
            { "PrivacySettings:ActiveKeyVersion", "v1" }
        });
        var (userId, _) = await SeedAsync(container);

        long sessionId;
        long replyId;
        string stored;
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var protection = scope.ServiceProvider.GetRequiredService<ContentProtectionService>();
            var session = new ChatSession { AspNetUserId = userId, Title = "Confidential", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow, IsConfidential = true };
            ConfidentialPolicyResolver.ApplyToSession(session, ConfidentialPolicy.Defaults with { ModelGate = ConfidentialityGateMode.UserDecides });
            db.ChatSession.Add(session);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            protection.EnsureSessionKey(session);
            stored = protection.Encrypt(session, Thought + "The visible answer.")!;
            var reply = new ChatMessage { ChatSessionId = session.Id, Role = "assistant", Content = stored, TimestampUtc = DateTime.UtcNow.AddMinutes(-1) };
            db.ChatMessage.Add(reply);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            sessionId = session.Id;
            replyId = reply.Id;
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.Equal("The visible answer.", ReplayedAssistantText(provider));
        Assert.Equal(stored, Read(container, db => db.ChatMessage.Single(m => m.Id == replyId)).Content);
    }

    [Fact]
    public async Task MaskingAndTheToolDigest_StillApplyToTheStrippedText()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserAiSettings.Single(s => s.AspNetUserId == userId).DlpMaskEmails = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        long replyId = await AddReplyAsync(container, sessionId, Thought + "Write to alice.smith@example.com about it.");
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatMessageToolCall.Add(new ChatMessageToolCall { ChatMessageId = replyId, ToolCallId = "t1", Name = "wiki_search", ArgsText = "{\"query\":\"cockatrice\"}", Status = "completed" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        string replayed = ReplayedAssistantText(provider);
        Assert.DoesNotContain("alice.smith@example.com", replayed);
        Assert.DoesNotContain("PRIVATE REASONING", replayed);
        Assert.Contains("wiki_search", replayed);
    }

    public static TheoryData<string, string, string> StoredFormats => new()
    {
        { "no markup", "A plain old answer.", "A plain old answer." },
        { "the first format", "\n\n<div class=\"ai-thought\">\n\nThinking\n\n</div>\n\nThe answer.", "The answer." },
        { "single quotes", "<div class='ai-thought'>Thinking</div>The answer.", "The answer." },
        { "interleaved blocks", Thought + "One." + Thought + "Two.", "One.\n\nTwo." },
        { "a nested block", "<div class=\"ai-thought\">Outer<div class=\"ai-thought\">Inner</div>Tail</div>The answer.", "Tail</div>The answer." },
        { "a block left open", "The answer.\n\n<div class=\"ai-thought\">Unfinished", "The answer." },
        { "only reasoning", Thought, "PRIVATE REASONING" },
        { "several reasoning blocks", "<div class=\"ai-thought\">First</div><div class=\"ai-thought\">Last</div>", "Last" }
    };

    [Theory]
    [MemberData(nameof(StoredFormats))]
    public async Task EveryStoredFormat_Replays(string name, string stored, string expected)
    {
        Assert.NotEmpty(name);
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        await AddReplyAsync(container, sessionId, stored);

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.Equal(expected, ReplayedAssistantText(provider));
    }

    [Fact]
    public async Task ARowOfOnlyEmptyReasoning_IsNotSentAsAnEmptyMessage()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        await AddReplyAsync(container, sessionId, "<div class=\"ai-thought\">\n\n</div>");

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.DoesNotContain(provider.Histories[0], m => ProviderHelper.GetProperty(m, "role")?.ToString() == "assistant");
    }

    [Fact]
    public async Task AReasoningOnlyRow_KeepsItsLastBlockAndItsDigest_WithOldToolRows()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        long replyId = await AddReplyAsync(container, sessionId, Thought);
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatMessageToolCall.Add(new ChatMessageToolCall { ChatMessageId = replyId, ToolCallId = "t1", Name = "wiki_search", ArgsText = null, BatchIndex = null, Status = "completed" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        string replayed = ReplayedAssistantText(provider);
        Assert.StartsWith("PRIVATE REASONING", replayed);
        Assert.Contains("wiki_search", replayed);
    }

    [Fact]
    public async Task AChatWithOneLargeImage_KeepsItsHistory()
    {
        var provider = Answering();
        provider.Formatter = (role, text, images) => images is { Count: > 0 }
            ? new { role, content = images.Select(i => (object)new { type = "image", source = new { type = "base64", media_type = i.ContentType, data = i.Base64Data } }).Prepend(new { type = "text", text }).ToList() }
            : new { role, content = text };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        byte[] png = new byte[1024 * 1024];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 3, 0, 0, 0, 3, 0 }.CopyTo(png, 0);
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_dataDirectory, "large.png"), png, TestContext.Current.CancellationToken);

        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserAiModels.Single(m => m.AspNetUserId == userId).MaxInputTokens = 200000;
            var start = DateTime.UtcNow.AddHours(-1);
            for (int i = 0; i < 20; i++)
            {
                db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = i % 2 == 0 ? "user" : "assistant", Content = $"Message {i}", TimestampUtc = start.AddMinutes(i) });
            }
            var withImage = new ChatMessage { ChatSessionId = sessionId, Role = "user", Content = "Look at this", TimestampUtc = start.AddMinutes(30) };
            db.ChatMessage.Add(withImage);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChatMessageAttachment.Add(new ChatMessageAttachment { ChatMessageId = withImage.Id, FileName = "large.png", ContentType = "image/png", RelativePath = "large.png" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        string history = JsonSerializer.Serialize(provider.Histories[0]);
        Assert.Contains("\"Message 0\"", history);
        Assert.True(history.Length > 1_000_000, "The image must have been sent.");
        Assert.DoesNotContain("earlier messages truncated", history);
    }

    private sealed class SizedMetadataService : ModelMetadataService
    {
        public override ModelMetadata GetMetadata(string provider, string modelId)
        {
            var metadata = base.GetMetadata(provider, modelId);
            metadata.ContextWindowSize = 200000;
            metadata.MaxInputTokens = 190000;
            return metadata;
        }
    }

    private static void SizeTheModel(IServiceCollection services)
    {
        services.AddScoped<ModelMetadataService, SizedMetadataService>();
    }

    private static ChatEvent Usage(int prompt, int output = 50, int cacheRead = 0, int cacheWrite = 0, int reasoning = 0) => new()
    {
        Type = "usage",
        UsageReport = new TokenUsageReport
        {
            TotalPromptTokens = prompt,
            UncachedInputTokens = prompt - cacheRead,
            CacheReadTokens = cacheRead,
            CacheCreationTokens = cacheWrite,
            OutputTokens = output,
            ReasoningTokens = reasoning
        }
    };

    [Fact]
    public async Task TurnModel_ComesBeforeTheFirstChunk_WithTheEffectiveFacts()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        int modelIndex = events.FindIndex(e => e.Type == "turn_model");
        Assert.InRange(modelIndex, 0, events.FindIndex(e => e.Type == "chunk") - 1);
        using var facts = JsonDocument.Parse(events[modelIndex].Data);
        Assert.Equal(ProviderName, facts.RootElement.GetProperty("provider").GetString());
        Assert.Equal(ModelId, facts.RootElement.GetProperty("modelId").GetString());
        Assert.False(facts.RootElement.GetProperty("selectionUnavailable").GetBoolean());
        Assert.StartsWith("u_", facts.RootElement.GetProperty("modelKey").GetString());
    }

    [Fact]
    public async Task ADeletedUserModel_FallsBackAndSaysSo()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId, userModelId: 987654);

        using var facts = JsonDocument.Parse(events.Single(e => e.Type == "turn_model").Data);
        Assert.True(facts.RootElement.GetProperty("selectionUnavailable").GetBoolean());
        Assert.Equal(ModelId, facts.RootElement.GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task AGnollHackSession_NeverSetsTheFlag()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatSession.Single(s => s.Id == sessionId).IsGnollHackSession = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId, userModelId: 987654);

        using var facts = JsonDocument.Parse(events.Single(e => e.Type == "turn_model").Data);
        Assert.False(facts.RootElement.GetProperty("selectionUnavailable").GetBoolean());
    }

    [Fact]
    public async Task ARefusedSystemConfiguration_CarriesTheModelUnavailableCode()
    {
        var provider = Answering();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId, systemModelId: 987654);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Equal("model_unavailable", error.ErrorCode);
    }

    [Fact]
    public async Task TheBreakdown_IsStoredAndSent_AndSumsToTheMeasuredPrompt()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Usage(10000, cacheRead: 8000, cacheWrite: 500, reasoning: 20), Completed() }
        };
        using var container = BuildContainer(provider, configure: SizeTheModel);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
        var breakdown = ContextBreakdownEstimator.FromJson(stored.ContextBreakdownJson);
        Assert.NotNull(breakdown);
        Assert.Equal(10000, breakdown.SystemPrompt + breakdown.ToolDefinitions + breakdown.GameContext + breakdown.History + breakdown.Documents + breakdown.Images + breakdown.ToolActivity);
        Assert.Equal(8000, breakdown.CacheRead);
        Assert.Equal(500, breakdown.CacheWrite);
        Assert.Equal(20, breakdown.Reasoning);

        using var context = JsonDocument.Parse(events.Single(e => e.Type == "context").Data);
        Assert.Equal(1, context.RootElement.GetProperty("breakdown").GetProperty("version").GetInt32());
        Assert.Equal(8000, context.RootElement.GetProperty("breakdown").GetProperty("cacheRead").GetInt32());
    }

    [Fact]
    public async Task ATurnWithNoUsage_StoresNoBreakdown()
    {
        var provider = Answering();
        using var container = BuildContainer(provider, configure: SizeTheModel);
        var (userId, sessionId) = await SeedAsync(container);

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.Null(Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant")).ContextBreakdownJson);
    }

    [Fact]
    public async Task AnIncognitoReply_KeepsItsBreakdownInTheStore()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Usage(10000), Completed() }
        };
        using var container = BuildContainer(provider, configure: SizeTheModel);
        var (userId, _) = await SeedAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, new ChatSession { AspNetUserId = userId, Title = "Incognito", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });

        await RunTurnAsync(container, held.Ref, userId);

        var reply = held.Messages.Single(m => m.Role == "assistant");
        Assert.NotNull(reply.ContextBreakdownJson);
        Assert.Equal(reply.ContextBreakdownJson, reply.ToChatMessage().ContextBreakdownJson);
    }
}
