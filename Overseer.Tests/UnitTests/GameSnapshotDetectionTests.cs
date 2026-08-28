using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Hubs;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class GameSnapshotDetectionTests
{
    private static ChatService CreateChatService()
    {
        var services = new ServiceCollection();
        var dummyKey = Convert.ToBase64String(new byte[32]);
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", dummyKey }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, DummyClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddScoped<IAiProvider, OpenAiResponsesProvider>();
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddScoped<ChatService>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ChatService>();
    }

    [Theory]
    [InlineData("Game Context Snapshot:\n<h1>Map</h1>", true)]
    [InlineData("Game Context Snapshot: Map data", true)]
    [InlineData("Game Snapshot:\nPlayer HP: 20", true)]
    [InlineData("Game Snapshot: Turn 100", true)]
    [InlineData("game context snapshot:\nlower case", true)]
    [InlineData("game snapshot: lower case", true)]
    [InlineData("Game Context Snapshot", true)]
    [InlineData("Game Snapshot", true)]
    [InlineData("Full Message History (last messages shown):\nHello", false)]
    [InlineData("Game Directory Manifest:\nfile.txt", false)]
    [InlineData("Hello, how are you?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGameSnapshotMessage_DetectsPrefixesCorrectly(string? content, bool expected)
    {
        bool actual = ChatService.IsGameSnapshotMessage(content);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Full Message History (last messages shown):\nLine 1", true)]
    [InlineData("Full Message History\nLine 1", true)]
    [InlineData("full message history", true)]
    [InlineData("Game Context Snapshot:\nMap", false)]
    [InlineData("Random text", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMessageHistoryMessage_DetectsPrefixesCorrectly(string? content, bool expected)
    {
        bool actual = ChatService.IsMessageHistoryMessage(content);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Predicates_MatchConstantPrefixes()
    {
        Assert.True(ChatService.IsGameSnapshotMessage(ChatService.GameSnapshotPrefix));
        Assert.True(ChatService.IsMessageHistoryMessage(ChatService.MessageHistoryPrefix));
    }

    [Fact]
    public void BuildSystemPrompt_WhenHasGameSnapshotIsTrue_IncludesSnapshotDeclaration()
    {
        var chatService = CreateChatService();
        var prompt = chatService.BuildSystemPrompt(
            wikiContext: Array.Empty<string>(),
            spoilerFreeMode: false,
            verboseMode: false,
            isGameOn: false,
            developerMode: false,
            overseerMode: 0,
            hasGameSnapshot: true,
            hasMessageHistory: false,
            clientSettings: null,
            enableToolUse: false,
            enableWebSearch: false,
            allowSourceCodeReferences: false);

        Assert.Contains("Game snapshot (current map, stats, inventory, recent messages, spells, skills, attributes, and the player's Discoveries list", prompt);
        Assert.DoesNotContain("No game context was provided for this session.", prompt);
        Assert.Contains("When greeting the player, briefly introduce yourself and give a short observation about their current situation based on the game snapshot", prompt);
        Assert.Contains("Your objective is to maximize the player's chance of winning", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WhenNoContextProvided_IncludesNoContextWarning()
    {
        var chatService = CreateChatService();
        var prompt = chatService.BuildSystemPrompt(
            wikiContext: Array.Empty<string>(),
            spoilerFreeMode: false,
            verboseMode: false,
            isGameOn: false,
            developerMode: false,
            overseerMode: 0,
            hasGameSnapshot: false,
            hasMessageHistory: false,
            clientSettings: null,
            enableToolUse: false,
            enableWebSearch: false,
            allowSourceCodeReferences: false);

        Assert.DoesNotContain("Game snapshot (current map, stats, inventory, recent messages, spells, skills, attributes, and the player's Discoveries list", prompt);
        Assert.Contains("No game context was provided for this session. Answer based on general GnollHack knowledge and wiki content.", prompt);
        Assert.Contains("The user is not currently in an active game.", prompt);
        Assert.Contains("Your objective is to help the player understand GnollHack's mechanics, review past games via dumplogs", prompt);
    }

    private class DummyClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, System.Text.Json.JsonElement parameters, System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "Dummy" });
        }
    }
}
