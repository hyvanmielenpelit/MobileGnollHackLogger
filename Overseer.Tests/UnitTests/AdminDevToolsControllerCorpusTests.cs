using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.ConfigHealth;
using Overseer.Services.CorpusIndexing;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The corpus endpoints of <see cref="AdminDevToolsController"/>, called as action methods: the
/// re-index responses (202 with the job id, 409 <c>alreadyRunning</c>, 400 naming the key and never
/// its value), the GnollBench guard (409 <c>benchmarkRunActive</c> while a run is <c>Running</c>,
/// bypassed by <c>acknowledgeActiveBenchmark</c>), the search probes against a recording tool
/// handler, the knowledge base article preview and the status endpoints. The services are real, on
/// temporary folders; the source services are never started. A knowledge base pass is held
/// mid-flight by its file-listing seam.
/// </summary>
public sealed class AdminDevToolsControllerCorpusTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private const string GnollHackWiki = "gnollhack-wiki";
    private const string NetHackWiki = "nethack-wiki";
    private const string KnowledgeBase = "knowledge-base";
    private const string AdminUserId = "admin-user-id";

    private readonly string _root;
    private readonly ApplicationDbContext _db;
    private readonly MemoryCache _cache;
    private readonly List<IDisposable> _services = new();

    public AdminDevToolsControllerCorpusTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AdminDevToolsControllerCorpusTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
    }

    public void Dispose()
    {
        for (int i = _services.Count - 1; i >= 0; i--)
        {
            _services[i].Dispose();
        }

        _cache.Dispose();
        _db.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    /// <summary>Records what the controller passed to the tool and answers with a fixed result.</summary>
    private sealed class RecordingToolHandler : IToolHandler
    {
        private readonly ToolResult _result;

        public RecordingToolHandler(string toolName, ToolResult result, int? maxResultLengthOverride = null)
        {
            ToolName = toolName;
            _result = result;
            MaxResultLengthOverride = maxResultLengthOverride;
        }

        public string ToolName { get; }
        public string Description { get; set; } = "A recording tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });
        public int? MaxResultLengthOverride { get; }

        public int Calls { get; private set; }
        public JsonElement? Parameters { get; private set; }
        public ToolExecutionContext? Context { get; private set; }
        public bool TokenCanBeCanceled { get; private set; }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            /* The controller disposes the arguments document once the call returns. */
            Parameters = parameters.Clone();
            Context = context;
            TokenCanBeCanceled = cancellationToken.CanBeCanceled;
            return Task.FromResult(_result);
        }
    }

    private sealed record CorpusFixture(AdminDevToolsController Controller, Func<CorpusIndexStatusDto> Status, Func<string?> LastStartedJobId);

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private T Track<T>(T service) where T : IDisposable
    {
        _services.Add(service);
        return service;
    }

    /// <summary>A blank <c>WikiPath</c>: an unset key would read the built-in default <c>c:\wiki</c>.</summary>
    private WikiService CreateWiki(string? path) =>
        Track(new WikiService(Configuration(("WikiPath", path ?? string.Empty))));

    private NetHackWikiService CreateNetHackWiki(string? path) =>
        Track(new NetHackWikiService(Configuration(("NetHackWikiPath", path ?? string.Empty)), null, null, startIndexing: false));

    private KnowledgeBaseService CreateKnowledgeBase(string? path, Func<string, IEnumerable<string>>? listArticleFiles = null) =>
        Track(new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance,
            Configuration(("KbPath", path ?? string.Empty)), listArticleFiles));

    private AdminDevToolsController CreateController(WikiService? wiki = null, NetHackWikiService? netHackWiki = null,
        KnowledgeBaseService? knowledgeBase = null, IEnumerable<IToolHandler>? toolHandlers = null)
    {
        var gnollHackSource = Track(new SourceCodeService(Configuration(), NullLogger<SourceCodeService>.Instance));
        var netHackSource = Track(new NetHackSourceCodeService(Configuration(), NullLogger<NetHackSourceCodeService>.Instance));

        return new AdminDevToolsController(
            gnollHackSource,
            netHackSource,
            new ConfigHealthService(Configuration()),
            wiki ?? CreateWiki(null),
            netHackWiki ?? CreateNetHackWiki(null),
            knowledgeBase ?? CreateKnowledgeBase(null),
            toolHandlers ?? Array.Empty<IToolHandler>(),
            new CorpusToolUsageService(_db, _cache),
            _db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, AdminUserId) }, "TestAuth"))
                }
            }
        };
    }

    /// <summary>A folder holding one article in the layout the corpus reads.</summary>
    private string CreateCorpusFolder(string corpus)
    {
        string path = Path.Combine(_root, corpus);
        switch (corpus)
        {
            case GnollHackWiki:
                Directory.CreateDirectory(Path.Combine(path, "Items"));
                File.WriteAllText(Path.Combine(path, "Items", "Long sword.md"), "# Long sword\n\nA versatile blade.\n");
                break;
            case NetHackWiki:
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "Long_sword.md"), "# Long sword\n\nA versatile blade.\n");
                break;
            case KnowledgeBase:
                Directory.CreateDirectory(Path.Combine(path, "Content"));
                File.WriteAllText(Path.Combine(path, "Content", "foo.md"), "---\ntitle: Foo Article\nsummary: About foo\n---\nFoo body.\n");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corpus), corpus, null);
        }
        return path;
    }

    /// <summary>A controller whose <paramref name="corpus"/> service reads <paramref name="path"/> (blank when null), its startup pass ended.</summary>
    private async Task<CorpusFixture> CreateCorpusFixtureAsync(string corpus, string? path)
    {
        var ct = TestContext.Current.CancellationToken;
        switch (corpus)
        {
            case GnollHackWiki:
            {
                var wiki = CreateWiki(path);
                await wiki.InitializationTask.WaitAsync(WaitLimit, ct);
                return new CorpusFixture(CreateController(wiki: wiki), wiki.GetStatus, () => wiki.LastStartedJobId);
            }
            case NetHackWiki:
            {
                var netHackWiki = CreateNetHackWiki(path);
                await netHackWiki.InitializationTask.WaitAsync(WaitLimit, ct);
                return new CorpusFixture(CreateController(netHackWiki: netHackWiki), netHackWiki.GetStatus, () => netHackWiki.LastStartedJobId);
            }
            case KnowledgeBase:
            {
                var knowledgeBase = CreateKnowledgeBase(path);
                await knowledgeBase.InitializationTask.WaitAsync(WaitLimit, ct);
                return new CorpusFixture(CreateController(knowledgeBase: knowledgeBase), knowledgeBase.GetStatus, () => knowledgeBase.LastStartedJobId);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(corpus), corpus, null);
        }
    }

    private static Task<IActionResult> Reindex(AdminDevToolsController controller, string corpus, bool acknowledgeActiveBenchmark = false)
    {
        var ct = TestContext.Current.CancellationToken;
        return corpus switch
        {
            GnollHackWiki => controller.ReindexGnollHackWiki(acknowledgeActiveBenchmark, ct),
            NetHackWiki => controller.ReindexNetHackWiki(acknowledgeActiveBenchmark, ct),
            KnowledgeBase => controller.ReindexKnowledgeBase(acknowledgeActiveBenchmark, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(corpus), corpus, null)
        };
    }

    /// <summary>The response body as JSON, so anonymous-object bodies can be read by property name.</summary>
    private static JsonElement Body(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.NotNull(objectResult.Value);
        return JsonSerializer.SerializeToElement(objectResult.Value, objectResult.Value!.GetType());
    }

    private static string? BodyString(IActionResult result, string property) => Body(result).GetProperty(property).GetString();

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Waits until a job with <paramref name="jobId"/> is in the history, so no pass outlives the test.</summary>
    private static async Task WaitForJobAsync(Func<CorpusIndexStatusDto> status, string jobId)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!status().RecentJobs.Any(j => j.Id == jobId))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Job {jobId} did not finish within {WaitLimit.TotalSeconds} s.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private async Task SeedBenchmarkRunAsync(BenchmarkRunStatus status)
    {
        _db.BenchmarkRuns.Add(new BenchmarkRun
        {
            SuiteName = "Corpus reindex guard",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-test", displayName: "Gemini"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-test", displayName: "Claude"),
            StartedAtUtc = DateTime.UtcNow,
            Status = status
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static ToolResult Succeeded(string content) => new() { Success = true, Content = content };

    /// <summary>One recording handler per corpus tool, so a probe that picks the wrong one shows.</summary>
    private static (RecordingToolHandler Wiki, RecordingToolHandler NetHackWiki, RecordingToolHandler KnowledgeBase) CreateHandlers(
        ToolResult? wikiResult = null, int? wikiMaxResultLengthOverride = null)
    {
        return (
            new RecordingToolHandler("wiki_search", wikiResult ?? Succeeded("wiki_search result"), wikiMaxResultLengthOverride),
            new RecordingToolHandler("nethack_wiki_search", Succeeded("nethack_wiki_search result")),
            new RecordingToolHandler("get_knowledge_article", Succeeded("get_knowledge_article result")));
    }

    private AdminDevToolsController CreateProbeController(RecordingToolHandler wiki, RecordingToolHandler netHackWiki, RecordingToolHandler knowledgeBase) =>
        CreateController(toolHandlers: new IToolHandler[] { netHackWiki, knowledgeBase, wiki });

    // -- re-index: started, already running, not configured, path missing -----------------------------

    [Theory]
    [InlineData(GnollHackWiki)]
    [InlineData(NetHackWiki)]
    [InlineData(KnowledgeBase)]
    public async Task Reindex_WhenIdle_Returns202WithTheStartedJobId(string corpus)
    {
        var fixture = await CreateCorpusFixtureAsync(corpus, CreateCorpusFolder(corpus));
        string? previousJobId = fixture.LastStartedJobId();

        var result = await Reindex(fixture.Controller, corpus);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        string? jobId = BodyString(result, "jobId");
        Assert.False(string.IsNullOrEmpty(jobId));
        Assert.NotEqual(previousJobId, jobId);
        Assert.Equal(fixture.LastStartedJobId(), jobId);

        await WaitForJobAsync(fixture.Status, jobId!);
        var job = fixture.Status().RecentJobs.First(j => j.Id == jobId);
        Assert.Equal(CorpusIndexTrigger.Manual, job.Trigger);
    }

    [Fact]
    public async Task ReindexKnowledgeBase_WhileAPassRuns_Returns409AlreadyRunningAndStartsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        string kbPath = CreateCorpusFolder(KnowledgeBase);
        using var gate = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var knowledgeBase = CreateKnowledgeBase(kbPath, contentPath =>
        {
            entered.TrySetResult();
            gate.Wait(WaitLimit);
            return Directory.GetFiles(contentPath, "*.md", SearchOption.AllDirectories);
        });

        try
        {
            await entered.Task.WaitAsync(WaitLimit, ct);
            var controller = CreateController(knowledgeBase: knowledgeBase);
            string? runningJobId = knowledgeBase.LastStartedJobId;
            Assert.NotNull(runningJobId);

            var result = await controller.ReindexKnowledgeBase(false, ct);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
            Assert.Equal("alreadyRunning", BodyString(result, "code"));
            Assert.Contains("the knowledge base", BodyString(result, "message"));
            Assert.Equal(runningJobId, knowledgeBase.LastStartedJobId);
        }
        finally
        {
            gate.Set();
            await knowledgeBase.InitializationTask.WaitAsync(WaitLimit, ct);
        }

        var job = Assert.Single(knowledgeBase.GetStatus().RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
    }

    [Theory]
    [InlineData(GnollHackWiki, "WikiPath")]
    [InlineData(NetHackWiki, "NetHackWikiPath")]
    [InlineData(KnowledgeBase, "KbPath")]
    public async Task Reindex_KeyBlank_Returns400NamingTheKey(string corpus, string configKey)
    {
        var fixture = await CreateCorpusFixtureAsync(corpus, null);
        string? previousJobId = fixture.LastStartedJobId();

        var result = await Reindex(fixture.Controller, corpus);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal($"{configKey} is not configured.", BodyString(result, "message"));
        Assert.Equal(previousJobId, fixture.LastStartedJobId());
    }

    [Theory]
    [InlineData(GnollHackWiki, "WikiPath")]
    [InlineData(NetHackWiki, "NetHackWikiPath")]
    [InlineData(KnowledgeBase, "KbPath")]
    public async Task Reindex_FolderMissing_Returns400NamingTheKeyNeverItsValue(string corpus, string configKey)
    {
        string missingPath = Path.Combine(_root, "missing-" + corpus);
        var fixture = await CreateCorpusFixtureAsync(corpus, missingPath);
        string? previousJobId = fixture.LastStartedJobId();

        var result = await Reindex(fixture.Controller, corpus);

        Assert.IsType<BadRequestObjectResult>(result);
        string? message = BodyString(result, "message");
        Assert.Equal($"The {configKey} folder does not exist.", message);
        Assert.DoesNotContain(missingPath, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_root, message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousJobId, fixture.LastStartedJobId());
    }

    // -- re-index: the GnollBench guard ---------------------------------------------------------------

    [Theory]
    [InlineData(GnollHackWiki, "the GnollHack wiki")]
    [InlineData(NetHackWiki, "the NetHack wiki")]
    [InlineData(KnowledgeBase, "the knowledge base")]
    public async Task Reindex_WhileABenchmarkRunIsRunning_Returns409BenchmarkRunActiveAndStartsNothing(string corpus, string corpusName)
    {
        var fixture = await CreateCorpusFixtureAsync(corpus, CreateCorpusFolder(corpus));
        await SeedBenchmarkRunAsync(BenchmarkRunStatus.Running);
        string? previousJobId = fixture.LastStartedJobId();

        var result = await Reindex(fixture.Controller, corpus);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("benchmarkRunActive", BodyString(result, "code"));
        Assert.Contains(corpusName, BodyString(result, "message"));
        Assert.Equal(previousJobId, fixture.LastStartedJobId());
    }

    [Theory]
    [InlineData(GnollHackWiki)]
    [InlineData(NetHackWiki)]
    [InlineData(KnowledgeBase)]
    public async Task Reindex_WhileABenchmarkRunIsRunning_Acknowledged_Returns202(string corpus)
    {
        var fixture = await CreateCorpusFixtureAsync(corpus, CreateCorpusFolder(corpus));
        await SeedBenchmarkRunAsync(BenchmarkRunStatus.Running);

        var result = await Reindex(fixture.Controller, corpus, acknowledgeActiveBenchmark: true);

        Assert.IsType<AcceptedResult>(result);
        string? jobId = BodyString(result, "jobId");
        Assert.Equal(fixture.LastStartedJobId(), jobId);
        await WaitForJobAsync(fixture.Status, jobId!);
    }

    [Theory]
    [InlineData(BenchmarkRunStatus.Completed)]
    [InlineData(BenchmarkRunStatus.CompletedWithErrors)]
    [InlineData(BenchmarkRunStatus.CompletedWithLimits)]
    [InlineData(BenchmarkRunStatus.Failed)]
    [InlineData(BenchmarkRunStatus.Canceled)]
    public async Task ReindexKnowledgeBase_WithOnlyAFinishedBenchmarkRun_Returns202(BenchmarkRunStatus status)
    {
        var fixture = await CreateCorpusFixtureAsync(KnowledgeBase, CreateCorpusFolder(KnowledgeBase));
        await SeedBenchmarkRunAsync(status);

        var result = await Reindex(fixture.Controller, KnowledgeBase);

        Assert.IsType<AcceptedResult>(result);
        await WaitForJobAsync(fixture.Status, BodyString(result, "jobId")!);
    }

    // -- probes ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ProbeGnollHackWiki_PassesTheTrimmedQueryAndCategory_AndReturnsTheHandlersResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers(Succeeded("Long sword: a versatile blade."));
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "  long sword  ", Filter = " Items " }, ct);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("wiki_search", dto.ToolName);
        Assert.True(dto.Success);
        Assert.Equal("Long sword: a versatile blade.", dto.Content);
        Assert.Null(dto.Error);
        Assert.Equal("Long sword: a versatile blade.".Length, dto.LengthChars);
        Assert.Equal(10000, dto.MaxResultLength);
        Assert.True(dto.ElapsedMs >= 0);

        Assert.Equal(1, wiki.Calls);
        Assert.Equal(0, netHackWiki.Calls);
        Assert.Equal(0, knowledgeBase.Calls);
        var parameters = wiki.Parameters!.Value;
        Assert.Equal(new[] { "category", "query" }, PropertyNames(parameters));
        Assert.Equal("long sword", parameters.GetProperty("query").GetString());
        Assert.Equal("Items", parameters.GetProperty("category").GetString());
        Assert.Equal(AdminUserId, wiki.Context!.UserId);
        Assert.Equal(10000, wiki.Context.MaxResultLength);
        Assert.True(wiki.TokenCanBeCanceled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProbeGnollHackWiki_BlankFilter_PassesNoCategory(string? filter)
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "gnoll", Filter = filter }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new[] { "query" }, PropertyNames(wiki.Parameters!.Value));
    }

    [Fact]
    public async Task ProbeNetHackWiki_PassesTheQueryAndNeverTheFilter()
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeNetHackWiki(new CorpusProbeRequest { Query = " Excalibur ", Filter = "Main" }, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("nethack_wiki_search", dto.ToolName);
        Assert.Equal("nethack_wiki_search result", dto.Content);
        Assert.Equal(1, netHackWiki.Calls);
        Assert.Equal(0, wiki.Calls);
        Assert.Equal(0, knowledgeBase.Calls);
        var parameters = netHackWiki.Parameters!.Value;
        Assert.Equal(new[] { "query" }, PropertyNames(parameters));
        Assert.Equal("Excalibur", parameters.GetProperty("query").GetString());
        Assert.Equal(AdminUserId, netHackWiki.Context!.UserId);
    }

    [Fact]
    public async Task ProbeKnowledgeBase_PassesTheQueryAsTopicAndNeverTheFilter()
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeKnowledgeBase(new CorpusProbeRequest { Query = "app_navigation", Filter = "ignored" }, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("get_knowledge_article", dto.ToolName);
        Assert.Equal("get_knowledge_article result", dto.Content);
        Assert.Equal(1, knowledgeBase.Calls);
        Assert.Equal(0, wiki.Calls);
        Assert.Equal(0, netHackWiki.Calls);
        var parameters = knowledgeBase.Parameters!.Value;
        Assert.Equal(new[] { "topic" }, PropertyNames(parameters));
        Assert.Equal("app_navigation", parameters.GetProperty("topic").GetString());
    }

    [Theory]
    [InlineData(null, 10000)]
    [InlineData(2000, 10000)]
    [InlineData(10000, 10000)]
    [InlineData(50000, 50000)]
    public async Task Probe_MaxResultLength_IsTheDefaultOrTheHandlersLargerOverride(int? handlerOverride, int expected)
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers(wikiMaxResultLengthOverride: handlerOverride);
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "gnoll" }, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(expected, dto.MaxResultLength);
        Assert.Equal(expected, wiki.Context!.MaxResultLength);
    }

    [Fact]
    public async Task Probe_ContentLongerThanTheCap_IsReturnedWhole()
    {
        string content = new string('w', 12000);
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers(Succeeded(content));
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "gnoll" }, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(content, dto.Content);
        Assert.Equal(12000, dto.LengthChars);
        Assert.Equal(10000, dto.MaxResultLength);
    }

    [Fact]
    public async Task Probe_HandlerFails_ReportsSuccessFalseAndTheError()
    {
        var failure = new ToolResult { Success = false, ErrorMessage = "The wiki index is still being built." };
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers(failure);
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "gnoll" }, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<CorpusProbeResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(dto.Success);
        Assert.Equal("The wiki index is still being built.", dto.Error);
        Assert.Equal(string.Empty, dto.Content);
        Assert.Equal(0, dto.LengthChars);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Probe_EmptyOrWhitespaceQuery_Returns400AndCallsNoTool(string? query)
    {
        var ct = TestContext.Current.CancellationToken;
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);
        var request = new CorpusProbeRequest { Query = query, Filter = "Items" };

        var results = new[]
        {
            await controller.ProbeGnollHackWiki(request, ct),
            await controller.ProbeNetHackWiki(request, ct),
            await controller.ProbeKnowledgeBase(request, ct)
        };

        foreach (var result in results)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Enter something to search for.", BodyString(result, "message"));
        }
        Assert.Equal(0, wiki.Calls + netHackWiki.Calls + knowledgeBase.Calls);
    }

    [Fact]
    public async Task Probe_NullRequest_Returns400()
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);

        var result = await controller.ProbeKnowledgeBase(null!, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, knowledgeBase.Calls);
    }

    [Fact]
    public async Task Probe_QueryOverTheLimit_Returns400AndCallsNoTool()
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);
        string query = new string('q', AdminDevToolsController.MaxProbeQueryLength + 1);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = query }, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("500", BodyString(result, "message"));
        Assert.Equal(0, wiki.Calls);
    }

    [Fact]
    public async Task Probe_QueryAtTheLimitAfterTrimming_IsAccepted()
    {
        var (wiki, netHackWiki, knowledgeBase) = CreateHandlers();
        var controller = CreateProbeController(wiki, netHackWiki, knowledgeBase);
        string query = new string('q', AdminDevToolsController.MaxProbeQueryLength);

        var result = await controller.ProbeGnollHackWiki(new CorpusProbeRequest { Query = "  " + query + "  " }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(query, wiki.Parameters!.Value.GetProperty("query").GetString());
    }

    [Fact]
    public async Task Probe_ToolNotRegistered_Returns400NamingTheTool()
    {
        var controller = CreateController();

        var result = await controller.ProbeNetHackWiki(new CorpusProbeRequest { Query = "Excalibur" }, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("nethack_wiki_search", BodyString(result, "message"));
    }

    // -- knowledge base article -----------------------------------------------------------------------

    private async Task<(AdminDevToolsController Controller, string FooSource)> CreateArticleControllerAsync()
    {
        string kbPath = CreateCorpusFolder(KnowledgeBase);
        File.WriteAllText(Path.Combine(kbPath, "Content", "bar.md"), "Bar body without front matter.\n");
        var knowledgeBase = CreateKnowledgeBase(kbPath);
        await knowledgeBase.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        return (CreateController(knowledgeBase: knowledgeBase), File.ReadAllText(Path.Combine(kbPath, "Content", "foo.md")));
    }

    [Fact]
    public async Task GetKnowledgeArticle_UnknownTopic_Returns404()
    {
        var (controller, _) = await CreateArticleControllerAsync();

        var result = controller.GetKnowledgeArticle("no_such_topic");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetKnowledgeArticle_BlankTopic_Returns400(string? topic)
    {
        var (controller, _) = await CreateArticleControllerAsync();

        var result = controller.GetKnowledgeArticle(topic);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetKnowledgeArticle_KnownTopic_ReturnsTheLoadedTopicTitleAndRawSource()
    {
        var (controller, fooSource) = await CreateArticleControllerAsync();

        var result = controller.GetKnowledgeArticle("FOO");

        var dto = Assert.IsType<KnowledgeArticleSourceDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("foo", dto.Topic);
        Assert.Equal("Foo Article", dto.Title);
        Assert.Equal(fooSource, dto.Source);
        Assert.StartsWith("---", dto.Source);
    }

    [Fact]
    public async Task GetKnowledgeArticle_ArticleWithoutATitle_ReturnsANullTitle()
    {
        var (controller, _) = await CreateArticleControllerAsync();

        var result = controller.GetKnowledgeArticle("bar");

        var dto = Assert.IsType<KnowledgeArticleSourceDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("bar", dto.Topic);
        Assert.Null(dto.Title);
        Assert.Equal("Bar body without front matter.\n", dto.Source);
    }

    // -- status endpoints -----------------------------------------------------------------------------

    [Fact]
    public async Task StatusEndpoints_ReturnOkWithTheirDtoTypes()
    {
        var controller = CreateController();

        Assert.IsType<WikiStatusDto>(Assert.IsType<OkObjectResult>(controller.GetGnollHackWikiStatus()).Value);
        Assert.IsType<NetHackWikiStatusDto>(Assert.IsType<OkObjectResult>(controller.GetNetHackWikiStatus()).Value);
        Assert.IsType<KnowledgeBaseStatusDto>(Assert.IsType<OkObjectResult>(controller.GetKnowledgeBaseStatus()).Value);
        Assert.IsType<SourceDashboardDto>(Assert.IsType<OkObjectResult>(controller.GetSourceStatus()).Value);
        Assert.IsType<ConfigHealthReportDto>(Assert.IsType<OkObjectResult>(controller.GetConfigHealth()).Value);

        var usage = await controller.GetCorpusToolUsage(7, TestContext.Current.CancellationToken);
        var usageDto = Assert.IsType<CorpusToolUsageDto>(Assert.IsType<OkObjectResult>(usage).Value);
        Assert.Equal(7, usageDto.Days);
    }

    [Fact]
    public void CorpusStatusEndpoints_ReportTheConfigKeyOfEachCorpus()
    {
        var controller = CreateController();

        var wiki = Assert.IsType<WikiStatusDto>(Assert.IsType<OkObjectResult>(controller.GetGnollHackWikiStatus()).Value);
        var netHackWiki = Assert.IsType<NetHackWikiStatusDto>(Assert.IsType<OkObjectResult>(controller.GetNetHackWikiStatus()).Value);
        var knowledgeBase = Assert.IsType<KnowledgeBaseStatusDto>(Assert.IsType<OkObjectResult>(controller.GetKnowledgeBaseStatus()).Value);

        Assert.Equal(CorpusKind.GnollHackWiki, wiki.Kind);
        Assert.Equal("WikiPath", wiki.ConfigKey);
        Assert.Equal(CorpusKind.NetHackWiki, netHackWiki.Kind);
        Assert.Equal("NetHackWikiPath", netHackWiki.ConfigKey);
        Assert.Equal(CorpusKind.KnowledgeBase, knowledgeBase.Kind);
        Assert.Equal("KbPath", knowledgeBase.ConfigKey);
    }
}
