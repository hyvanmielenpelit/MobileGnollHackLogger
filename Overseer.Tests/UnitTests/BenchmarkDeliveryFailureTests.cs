using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What a question does when the request about to be sent does not carry the board. The answer is
/// recorded as a failure of the harness, not graded as a failure of the model.
/// </summary>
public class BenchmarkDeliveryFailureTests
{
    private const string ProviderName = "BoardDropping";
    private const string SystemPrompt = "You are Overseer, an assistant for the roguelike GnollHack. "
        + "Answer from the game's own source code and wiki, and never invent an item the hero does not carry.";
    private const string BoardText = "Dlvl:3  HP:14(14)\na - a blessed +1 quarterstaff (weapon in hands)";

    /// <summary>
    /// A provider that emits the system prompt and the question and silently drops every later
    /// system message — which is what Google and Anthropic did to the board before harness 29.
    /// </summary>
    private sealed class BoardDroppingProvider : IAiProvider
    {
        public string ProviderName => BenchmarkDeliveryFailureTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public Dictionary<string, object> BuildChatRequestBody(
            string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel,
            ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null,
            string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null,
            string? promptCacheKey = null, bool cacheConversationTail = true)
        {
            string? first = messageHistory
                .Select(m => ProviderHelper.GetProperty(m, "content")?.ToString())
                .FirstOrDefault();

            return new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["system"] = first ?? string.Empty,
                ["input"] = messageHistory.Skip(2).ToList()
            };
        }

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null) { }
        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results) { }
        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/stream";
        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => null;

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(
            HttpResponseMessage response, bool showDebugLog,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task AQuestionWhoseRequestLosesTheBoard_IsRecordedFailed_AndNeverReachesTheAgentLoop()
    {
        var services = new ServiceCollection();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<IAiProvider, BoardDroppingProvider>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        // The agent loop is null on purpose: reaching it is the failure this test rules out, and a
        // null reference would carry a different message than the one asserted below.
        var service = new BenchmarkService(
            scopeFactory,
            chatService: null!,
            agentLoopRunner: null!,
            cryptoService: null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        var run = new BenchmarkRun
        {
            Id = 1,
            BenchmarkSuite = new BenchmarkSuite
            {
                Id = 1,
                Name = "Snapshot Suite",
                GameSnapshot = new BenchmarkGameSnapshot
                {
                    Id = 1,
                    Name = "Tommi2",
                    SanitizedText = BoardText,
                    Sha256 = new string('0', 64),
                    CaptureMethod = "YamlImport"
                }
            }
        };

        var question = new BenchmarkQuestion
        {
            Id = 11,
            OrderIndex = 4,
            QuestionText = "Which of my items is worth reading first?",
            Difficulty = BenchmarkDifficulty.Intermediate
        };

        var testedConfig = new SystemAiApiConfiguration
        {
            Id = 3,
            Provider = ProviderName,
            ModelId = "board-dropper-1",
            DisplayName = "Board Dropper"
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var answer = await service.ExecuteSingleQuestionAsync(
            db, configService: null!, run, question, testedConfig, "api-key",
            SystemPrompt, segmentedPrompt: null, new List<string>(),
            maxResultLength: 10000, maxCallsPerSession: 45, CancellationToken.None);

        Assert.Equal(BenchmarkAnswerStatus.Failed, answer.Status);
        Assert.Equal(
            $"Harness delivery check failed: the {ProviderName} request for question 4 did not contain the board.",
            answer.ErrorMessage);
        Assert.Equal(string.Empty, answer.AnswerText);
        Assert.Null(answer.ThoughtText);
        Assert.Equal(BenchmarkAssessmentStatus.Pending, answer.AssessmentStatus);
        Assert.Null(answer.AccuracyLevel);
        Assert.Null(answer.QualityScore);

        // The counterpart of BenchmarkRunFinalizer.HasTerminalFailure: it is a transport defect,
        // not a provider outage, so it can be repeated with "re-run failed questions".
        Assert.True(BenchmarkRunFinalizer.NeedsReExecution(answer));
        Assert.Null(answer.HttpStatusCode);
    }
}
