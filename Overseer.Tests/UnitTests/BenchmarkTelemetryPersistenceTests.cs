using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The candidate path of a GnollBench question, driven end to end through
/// <see cref="BenchmarkService.ExecuteSingleQuestionAsync"/> and the real <see cref="AgentLoopRunner"/>
/// and provider, with the "AiProvider" HTTP client answered by a canned stream.
///
/// <para>The outgoing request is compared with a golden captured from the code before call telemetry
/// existed: recording telemetry must not change one byte of what the candidate is sent, which is
/// why call telemetry carries no <c>HarnessVersion</c> bump. Masked before comparison: the API key
/// (in the URL and in authentication headers, which are dropped). Nothing else in the request is
/// volatile.</para>
/// </summary>
public class BenchmarkTelemetryPersistenceTests
{
    private const string SystemPrompt =
        "You are Overseer, an assistant for the roguelike GnollHack. Answer from the game's own source "
        + "code and wiki, name the file you read a fact from, and never invent an item the hero does not "
        + "carry. Keep the answer to the question that was asked, and say so when a tool returned nothing.";

    private const string BoardText =
        "Dlvl:3  HP:14(14)  Pw:5(5)  AC:7  Xp:2/24  T:512\n"
        + "Inventory:\n"
        + "a - a blessed +1 quarterstaff (weapon in hands)\n"
        + "c - 3 fortune cookies\n";

    private const string QuestionText = "Which of my items is worth reading first, and why?";

    private const string ApiKey = "golden-test-key";

    private const string AnswerText = "Read the fortune cookies first: they are the only readable items you carry.";

    /// <summary>Request captures live beside the report-pack goldens, under <c>Golden/Request/</c>.</summary>
    private static string GoldenPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MobileGnollHackLogger.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "Repository root (MobileGnollHackLogger.slnx) not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "Overseer.Tests", "UnitTests", "Golden", "Request", fileName);
    }

    /// <summary>Records every request body and answers each with the provider's canned stream.</summary>
    internal sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _sse;

        public CapturingHandler(string sse) => _sse = sse;

        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await DescribeAsync(request));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_sse, Encoding.UTF8, "text/event-stream")
            };
        }

        private static async Task<string> DescribeAsync(HttpRequestMessage request)
        {
            var sb = new StringBuilder();
            sb.Append(request.Method.Method).Append(' ')
              .Append(request.RequestUri!.ToString().Replace(ApiKey, "<API_KEY>", StringComparison.Ordinal)).Append('\n');

            var headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Where(h => !IsAuthHeader(h.Key))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
            {
                sb.Append(h.Key.ToLowerInvariant()).Append(": ").Append(string.Join(", ", h.Value)).Append('\n');
            }

            sb.Append('\n');
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(body))
            {
                sb.Append(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            }
            sb.Append('\n');
            return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static bool IsAuthHeader(string name) =>
            name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            || name.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)
            || name.Equals("x-goog-api-key", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FixedHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
    }

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }

    internal static string CannedStream(string providerName) => providerName switch
    {
        "OpenAI" =>
            "event: response.created\n"
            + "data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_golden\",\"model\":\"gpt-6.1-sol-2026-09-01\",\"status\":\"in_progress\"}}\n\n"
            + "event: response.output_text.delta\n"
            + "data: {\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(AnswerText) + "}\n\n"
            + "event: response.completed\n"
            + "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_golden\",\"model\":\"gpt-6.1-sol-2026-09-01\",\"status\":\"completed\",\"service_tier\":\"default\","
            + "\"usage\":{\"input_tokens\":420,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":64,\"output_tokens_details\":{\"reasoning_tokens\":40}}}}\n\n",
        "Anthropic" =>
            "event: message_start\n"
            + "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_golden\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5\",\"content\":[],"
            + "\"usage\":{\"input_tokens\":420,\"output_tokens\":1,\"service_tier\":\"standard\"}}}\n\n"
            + "event: content_block_start\n"
            + "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n"
            + "event: content_block_delta\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":" + JsonSerializer.Serialize(AnswerText) + "}}\n\n"
            + "event: content_block_stop\n"
            + "data: {\"type\":\"content_block_stop\",\"index\":0}\n\n"
            + "event: message_delta\n"
            + "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":64}}\n\n"
            + "event: message_stop\n"
            + "data: {\"type\":\"message_stop\"}\n\n",
        _ =>
            "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + JsonSerializer.Serialize(AnswerText) + "}]},\"finishReason\":\"STOP\"}],"
            + "\"usageMetadata\":{\"promptTokenCount\":420,\"candidatesTokenCount\":24,\"thoughtsTokenCount\":40,\"totalTokenCount\":484},"
            + "\"modelVersion\":\"gemini-3.8-flash\",\"responseId\":\"golden-response\"}\n\n"
    };

    internal static SystemAiApiConfiguration TestedConfig(string providerName) => providerName switch
    {
        "OpenAI" => new SystemAiApiConfiguration
        {
            Id = 31, Provider = "OpenAI", ModelId = "gpt-6.1-sol", DisplayName = "GPT-6.1 Sol (medium)",
            ThinkingLevel = "medium", ReasoningSummary = "auto", ServiceTier = "default", IsEnabled = true
        },
        "Anthropic" => new SystemAiApiConfiguration
        {
            Id = 32, Provider = "Anthropic", ModelId = "claude-opus-5", DisplayName = "Claude Opus 5 (high)",
            ThinkingLevel = "high", IsEnabled = true
        },
        _ => new SystemAiApiConfiguration
        {
            Id = 33, Provider = "Google", ModelId = "gemini-3.8-flash", DisplayName = "Gemini 3.8 Flash (medium)",
            ThinkingLevel = "medium", IsEnabled = true
        }
    };

    private static IConfiguration ProviderConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" },
            { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
        })
        .Build();

    internal sealed record CandidateRun(BenchmarkRunAnswer Answer, CapturingHandler Handler, ApplicationDbContext Db, IServiceScope Scope);

    /// <summary>One question of a board suite, answered by the named provider through the production candidate path.</summary>
    internal static async Task<CandidateRun> RunCandidateAsync(string providerName)
    {
        var providerConfig = ProviderConfig();
        IAiProvider[] providers =
        {
            new OpenAiResponsesProvider(providerConfig),
            new AnthropicProvider(providerConfig),
            new GoogleProvider(providerConfig)
        };

        var services = new ServiceCollection();
        services.AddLogging();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        foreach (var p in providers)
        {
            services.AddSingleton(p);
        }
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handlers = new List<IToolHandler>();
        var clientBridge = new NullClientBridge();
        var handler = new CapturingHandler(CannedStream(providerName));

        var runner = new AgentLoopRunner(
            providers,
            new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config),
            new FixedHttpClientFactory(handler),
            config,
            scopeFactory,
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var service = new BenchmarkService(
            scopeFactory,
            chatService: null!,
            agentLoopRunner: runner,
            cryptoService: null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new EndpointPolicy(config),
            config,
            NullLogger<BenchmarkService>.Instance);

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = 70,
            StartedByUserId = "user-golden",
            BenchmarkSuite = new BenchmarkSuite { Id = 4, Name = "Snapshot Suite" },
            GameSnapshotNameUsed = "Tommi2",
            GameSnapshotSha256Used = new string('0', 64),
            BoardSnapshotId = 9,
            BoardSnapshot = new BenchmarkRunBoardSnapshot
            {
                Id = 9,
                Sha256 = BenchmarkRunBoardSnapshotStore.ComputeSha256(BoardText, null),
                SanitizedText = BoardText,
                CharCount = BoardText.Length
            }
        });

        var question = new BenchmarkQuestion
        {
            Id = 11,
            OrderIndex = 4,
            ItemRevision = 2,
            QuestionText = QuestionText,
            ExpectedPoints = "- Fortune cookies are readable.",
            Difficulty = BenchmarkDifficulty.Intermediate
        };

        var segments = new SegmentedPrompt(SystemPrompt[..160], SystemPrompt[160..], string.Empty);

        var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var answer = await service.ExecuteSingleQuestionAsync(
            db, configService: null!, run, question, TestedConfig(providerName), ApiKey,
            SystemPrompt, segments, new List<string>(),
            maxResultLength: 10000, maxCallsPerSession: 45, TestContext.Current.CancellationToken);

        return new CandidateRun(answer, handler, db, scope);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    public async Task CandidateRequest_IsByteForByteTheGolden(string providerName)
    {
        var result = await RunCandidateAsync(providerName);
        using var scope = result.Scope;

        Assert.Equal(BenchmarkAnswerStatus.Ok, result.Answer.Status);
        Assert.Equal(AnswerText, result.Answer.AnswerText);
        string request = Assert.Single(result.Handler.Requests);

        string fileName = "candidate_" + providerName.ToLowerInvariant() + ".txt";
        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath(fileName))!);
            File.WriteAllBytes(GoldenPath(fileName), new UTF8Encoding(false).GetBytes(request));
            return;
        }

        string path = GoldenPath(fileName);
        Assert.True(File.Exists(path), "Golden file not found: " + path);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(Array.IndexOf(bytes, (byte)'\r') >= 0, "golden file has CRLF line endings; see .gitattributes (" + fileName + ")");
        Assert.Equal(new UTF8Encoding(false).GetString(bytes), request);
    }

    [Theory]
    [InlineData("OpenAI", "gpt-6.1-sol-2026-09-01")]
    [InlineData("Anthropic", "claude-opus-5")]
    [InlineData("Google", "gemini-3.8-flash")]
    public async Task CandidateRun_RecordsAnswerTimingAndOneCallRow(string providerName, string servedModelId)
    {
        var result = await RunCandidateAsync(providerName);
        using var scope = result.Scope;
        var answer = result.Answer;

        Assert.Single(result.Handler.Requests);
        Assert.NotNull(answer.StartedAtUtc);
        Assert.NotNull(answer.CompletedAtUtc);
        Assert.True(answer.StartedAtUtc <= answer.CompletedAtUtc);
        Assert.Equal(0L, answer.PermitWaitMs);
        Assert.Equal(0L, answer.BackoffWaitMs);
        Assert.Equal(0, answer.RetryAttemptCount);
        Assert.Equal(servedModelId, answer.ServedModelId);

        var tested = TestedConfig(providerName);
        var row = Assert.Single(answer.ModelCalls);
        Assert.Equal(ModelCallSource.BenchmarkCandidate, row.Source);
        Assert.Null(row.GraderRole);
        Assert.Equal(tested.Id, row.SystemAiApiConfigurationId);
        Assert.Equal(providerName, row.Provider);
        Assert.Equal(tested.ModelId, row.RequestedModelId);
        Assert.Equal(servedModelId, row.ServedModelId);
        Assert.NotNull(row.ResponseId);
        Assert.NotNull(row.FirstOutputMs);
        Assert.Equal(0, row.CallIndex);
        Assert.Equal(420, row.InputTokens);
        Assert.Equal(answer.OutputTokens, row.OutputTokens);
        Assert.True(row.OutputTokens > 0);

        var saved = Assert.Single(await result.Db.ModelCallTelemetry.AsNoTracking()
            .Where(t => t.BenchmarkRunAnswerId == answer.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ModelCallSource.BenchmarkCandidate, saved.Source);
        Assert.Equal(servedModelId, saved.ServedModelId);
        Assert.Equal(row.ResponseId, saved.ResponseId);
    }

    [Fact]
    public void CandidateTelemetry_RefusedDelivery_RecordsNothing()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.ApplyCandidateTelemetry(answer, new AgentRunResult(), testedConfigId: 31, startedAtUtc: null, completedAtUtc: null);

        Assert.Null(answer.StartedAtUtc);
        Assert.Null(answer.CompletedAtUtc);
        Assert.Null(answer.PermitWaitMs);
        Assert.Null(answer.BackoffWaitMs);
        Assert.Null(answer.RetryAttemptCount);
        Assert.Null(answer.ServedModelId);
        Assert.Empty(answer.ModelCalls);
    }

    [Fact]
    public void GraderCalls_MapInCallOrderAcrossTurns()
    {
        var calls = new[]
        {
            new ModelCallRecord { CallIndex = 0, Provider = "Google", RequestedModelId = "gemini-3.8-flash" },
            new ModelCallRecord { CallIndex = 0, Provider = "Google", RequestedModelId = "gemini-3.8-flash" }
        };

        var rows = BenchmarkService.MapGraderCalls(
            calls, ModelCallGraderRole.ClaimVerifier,
            new Overseer.Services.Telemetry.ModelCallLinks(BenchmarkRunAnswerId: 5, SystemAiApiConfigurationId: 33));

        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.CallIndex));
        Assert.All(rows, r =>
        {
            Assert.Equal(ModelCallSource.BenchmarkGrader, r.Source);
            Assert.Equal(ModelCallGraderRole.ClaimVerifier, r.GraderRole);
            Assert.Equal(5L, r.BenchmarkRunAnswerId);
            Assert.Equal(33L, r.SystemAiApiConfigurationId);
        });
    }

    [Fact]
    public void ServedModelIdsJson_IsCompactSortedAndMerged()
    {
        string? json = BenchmarkRunFinalizer.BuildServedModelIdsJson(new[]
        {
            ("gpt-6.1-sol-2026-09-01", 2),
            ("claude-opus-5", 3),
            ("gpt-6.1-sol-2026-09-01", 1)
        });

        Assert.Equal("{\"claude-opus-5\":3,\"gpt-6.1-sol-2026-09-01\":3}", json);
        Assert.Null(BenchmarkRunFinalizer.BuildServedModelIdsJson(Array.Empty<(string, int)>()));
    }

    [Fact]
    public void ServedModelIdsJson_KeepsTheMostFrequentIdsThatFit()
    {
        var counts = Enumerable.Range(1, 10)
            .Select(i => (new string((char)('a' + i - 1), 200), i))
            .ToList();

        string? json = BenchmarkRunFinalizer.BuildServedModelIdsJson(counts);

        Assert.NotNull(json);
        Assert.True(json!.Length <= BenchmarkRunFinalizer.ServedModelIdsJsonMaxLength);
        var kept = JsonSerializer.Deserialize<Dictionary<string, int>>(json)!;
        Assert.Equal(4, kept.Count);
        Assert.Equal(new[] { 7, 8, 9, 10 }, kept.Values.OrderBy(v => v));
    }

    [Fact]
    public void ServedModelIds_AreNullOnARunWithoutCallTelemetry()
    {
        var withoutTelemetry = new BenchmarkRun { ServedModelIdsJson = "{\"stale\":1}" };
        var withTelemetry = new BenchmarkRun { CallTelemetryVersion = BenchmarkService.CurrentCallTelemetryVersion };
        var counts = new[] { ("gemini-3.8-flash", 2) };

        BenchmarkRunFinalizer.ApplyServedModelIds(withoutTelemetry, counts);
        BenchmarkRunFinalizer.ApplyServedModelIds(withTelemetry, counts);
        BenchmarkRunFinalizer.ApplyServedModelIds(withTelemetry, counts);

        Assert.Null(withoutTelemetry.ServedModelIdsJson);
        Assert.Equal("{\"gemini-3.8-flash\":2}", withTelemetry.ServedModelIdsJson);
    }
}
