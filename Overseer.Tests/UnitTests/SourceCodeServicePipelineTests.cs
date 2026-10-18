using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.SourceIndexing;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The background pipeline of <see cref="SourceCodeService"/>: <c>StartAsync</c> returns before the
/// first pass ends and the tools answer with the guard message meanwhile; a pass publishes a whole
/// new snapshot or nothing; one gate serializes passes, with at most one queued follow-up; a pass
/// can be canceled; an unconfigured path still completes initialization. A hook in a test subclass
/// blocks or fails a pass just before it reads the files.
/// </summary>
public class SourceCodeServicePipelineTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);
    private static readonly string ShaA = new string('a', 40);
    private static readonly string ShaB = new string('b', 40);

    private readonly string _sourceDir;

    public SourceCodeServicePipelineTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SourceCodeServicePipelineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        File.WriteAllText(Path.Combine(_sourceDir, "src", "potion.c"),
            "/* pipeline potion.c */\r\n" +
            "#include \"hack.h\"\r\n" +
            "\r\n" +
            "void\r\n" +
            "potionhit(struct monst *mtmp, struct obj *otmp)\r\n" +
            "{\r\n" +
            "    /* pipeline potion hit logic */\r\n" +
            "}\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "src", "monst.c"),
            "/* pipeline monst.c */\r\n" +
            "struct permonst mons_init[] = {\r\n" +
            "    MON(\"fixture newt\", S_LIZARD, LVL(0, 6, 8, 0, 0, 0), (G_GENO | 5)),\r\n" +
            "};\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "include", "hack.h"),
            "/* pipeline hack.h */\r\n" +
            "#define PM_GNOLL 100\r\n" +
            "#define POTION_TEST 1\r\n");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private IConfiguration Config(string? sourcePath = null, params (string Key, string? Value)[] extra)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("SourceCodePath", sourcePath ?? _sourceDir),
            new("MaxSourceFileSizeKB", "800")
        };
        values.AddRange(extra.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)));
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private void WriteHead(string sha)
    {
        string gitDir = Path.Combine(_sourceDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), sha + "\n");
    }

    private static async Task StartAndWaitAsync(SourceCodeService service)
    {
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    private static void AssertSearchFinds(SourceCodeService service)
    {
        string result = service.SearchFiles("potionhit", null, 10, false, 100000);
        Assert.Contains("src/potion.c", result);
    }

    [Fact]
    public async Task StartAsync_ReturnsWhileThePassIsBlocked_AndTheToolsAnswerWithTheGuardMessage()
    {
        var config = Config();
        using var service = new GatedSourceCodeService(config);
        using var netHack = new NetHackSourceCodeService(Config(string.Empty), NullLogger<NetHackSourceCodeService>.Instance);
        var block = service.BlockNextPass();

        Task start = service.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(start.IsCompleted);
        await block.Entered.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.False(service.IsIndexingComplete);
        Assert.False(service.InitializationTask.IsCompleted);
        var status = service.GetStatus();
        Assert.NotNull(status.CurrentJob);
        Assert.Equal(SourceIndexJobOutcome.Running, status.CurrentJob!.Outcome);
        Assert.Equal(SourceIndexTrigger.Startup, status.CurrentJob.Trigger);
        Assert.Equal(status.CurrentJob.Id, service.CurrentJobId);

        var tools = new (IToolHandler Tool, string Arguments)[]
        {
            (new SourceCodeSearchTool(service, netHack, config), @"{""query"": ""potionhit""}"),
            (new SourceCodeViewTool(service, netHack, config), @"{""file"": ""src/potion.c"", ""start_line"": 1}"),
            (new GetFunctionDefinitionTool(service, netHack), @"{""name"": ""potionhit""}"),
            (new SearchDefinitionsTool(service, netHack), @"{""symbol"": ""potionhit""}"),
            (new GetConstantsTool(service, netHack), @"{""name"": ""PM_GNOLL""}"),
            (new GetMonsterStatsTool(service, config), @"{""name"": ""fixture newt""}"),
            (new GetItemStatsTool(service, config), @"{""name"": ""long sword""}"),
            (new GetArtifactStatsTool(service, config), @"{""name"": ""Excalibur""}"),
            (new ListIndexedFilesTool(service, netHack), @"{}")
        };

        foreach (var (tool, arguments) in tools)
        {
            var result = await tool.ExecuteAsync(JsonDocument.Parse(arguments).RootElement, new ToolExecutionContext(), TestContext.Current.CancellationToken);
            Assert.False(result.Success, tool.GetType().Name);
            Assert.Equal(ToolGuardMessages.SourceCodeIndexingInProgress, result.ErrorMessage);
        }

        block.Release.TrySetResult();
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.True(service.IsIndexingComplete);
        Assert.Equal(1, service.IndexGeneration);
        var search = await tools[0].Tool.ExecuteAsync(JsonDocument.Parse(tools[0].Arguments).RootElement, new ToolExecutionContext(), TestContext.Current.CancellationToken);
        Assert.True(search.Success);
        Assert.Contains("src/potion.c", search.Content);
    }

    [Fact]
    public async Task Reindexing_NeverExposesATornIndex_ToConcurrentReaders()
    {
        using var service = new GatedSourceCodeService(Config());
        await StartAndWaitAsync(service);

        var failures = new ConcurrentQueue<string>();
        using var done = new CancellationTokenSource();

        Task Reader(string name, Func<string?> check) => Task.Run(() =>
        {
            int reads = 0;
            while (!done.IsCancellationRequested || reads == 0)
            {
                try
                {
                    string? failure = check();
                    if (failure != null) failures.Enqueue($"{name}: {failure}");
                }
                catch (Exception ex)
                {
                    failures.Enqueue($"{name} threw {ex.GetType().Name}: {ex.Message}");
                }
                reads++;
            }
        });

        var readers = new[]
        {
            Reader("SearchFiles", () => service.SearchFiles("potionhit", null, 10, false, 100000).Contains("src/potion.c") ? null : "no hit"),
            Reader("GetConstants", () => service.GetConstants("PM_GNOLL", null).SingleOrDefault()?.Value == "100" ? null : "no constant"),
            Reader("GetMonsterStats", () => service.GetMonsterStats("fixture newt").Error),
            Reader("GetFileExcerpt", () => service.GetFileExcerpt("src/potion.c", 1, 5).StartsWith("--- src/potion.c:L1-L5", StringComparison.Ordinal) ? null : "no excerpt")
        };

        try
        {
            for (int i = 0; i < 50; i++)
            {
                while (service.RequestReindex(SourceIndexTrigger.Manual) != SourceIndexStartResult.Started)
                {
                    await Task.Delay(1, TestContext.Current.CancellationToken);
                }
                Assert.True(await service.WaitForIdleAsync(WaitLimit));
            }
        }
        finally
        {
            done.Cancel();
            await Task.WhenAll(readers).WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        }

        Assert.Empty(failures);
        Assert.Equal(51, service.IndexGeneration);
    }

    [Fact]
    public async Task APassThatThrows_PublishesNothing_AndThePreviousSnapshotStillAnswers()
    {
        using var service = new GatedSourceCodeService(Config());
        await StartAndWaitAsync(service);
        Assert.Equal(1, service.IndexGeneration);

        File.WriteAllText(Path.Combine(_sourceDir, "src", "potion.c"), "/* emptied */\r\n");
        service.FailNextPass(new InvalidOperationException("injected pass failure"));

        Assert.Equal(SourceIndexStartResult.Started, service.RequestReindex(SourceIndexTrigger.Manual));
        Assert.True(await service.WaitForIdleAsync(WaitLimit));

        Assert.Equal(1, service.IndexGeneration);
        AssertSearchFinds(service);
        var job = service.GetStatus().RecentJobs.First();
        Assert.Equal(SourceIndexJobOutcome.Failed, job.Outcome);
        Assert.Contains("injected pass failure", job.Message);
        Assert.True(service.IsIndexingComplete);
    }

    [Fact]
    public async Task HeadMovingDuringAPass_QueuesOneFollowUpPass()
    {
        WriteHead(ShaA);
        using var service = new GatedSourceCodeService(Config());
        var block = service.BlockNextPass();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await block.Entered.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        WriteHead(ShaB);
        block.Release.TrySetResult();
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.True(await service.WaitForIdleAsync(WaitLimit));

        var status = service.GetStatus();
        Assert.Equal(2, service.IndexGeneration);
        Assert.Equal(ShaB, status.Index!.HeadSha);
        Assert.True(status.UpToDate);
        Assert.Equal(new[] { SourceIndexTrigger.Startup, SourceIndexTrigger.FollowUp }, service.TriggersSeen);
        Assert.Equal(SourceIndexTrigger.FollowUp, status.RecentJobs[0].Trigger);
        Assert.Equal(SourceIndexJobOutcome.Succeeded, status.RecentJobs[0].Outcome);
        Assert.Equal(SourceIndexJobOutcome.Succeeded, Assert.Single(status.RecentJobs, j => j.Trigger == SourceIndexTrigger.Startup).Outcome);
    }

    [Fact]
    public async Task AFollowUpForAnAlreadyIndexedHead_IsSkipped()
    {
        WriteHead(ShaA);
        using var service = new GatedSourceCodeService(Config());
        await StartAndWaitAsync(service);
        var block = service.BlockNextPass();

        Assert.Equal(SourceIndexStartResult.Started, service.RequestReindex(SourceIndexTrigger.Manual));
        await block.Entered.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        /* The poll finds the gate taken, so it only queues a follow-up; that pass then finds HEAD indexed. */
        Assert.Equal(SourceIndexStartResult.AlreadyRunning, service.RequestReindex(SourceIndexTrigger.RepositoryChange));
        block.Release.TrySetResult();
        Assert.True(await service.WaitForIdleAsync(WaitLimit));

        var jobs = service.GetStatus().RecentJobs;
        Assert.Equal(SourceIndexTrigger.FollowUp, jobs[0].Trigger);
        Assert.Equal(SourceIndexJobOutcome.SkippedNoChange, jobs[0].Outcome);
        Assert.Equal(2, service.IndexGeneration);
    }

    [Fact]
    public async Task ARequestWhileBusy_IsRefused_AndCancelingKeepsThePreviousSnapshot()
    {
        using var service = new GatedSourceCodeService(Config());
        await StartAndWaitAsync(service);
        Assert.False(service.CancelCurrentJob());
        var block = service.BlockNextPass();

        Assert.Equal(SourceIndexStartResult.Started, service.RequestReindex(SourceIndexTrigger.Manual));
        await block.Entered.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.Equal(SourceIndexStartResult.AlreadyRunning, service.RequestReindex(SourceIndexTrigger.Manual));
        Assert.Equal(SourceIndexStartResult.AlreadyRunning, service.RequestMakedefsBuild());
        string? runningId = service.CurrentJobId;
        Assert.NotNull(runningId);
        Assert.Equal(runningId, service.LastStartedJobId);

        Assert.True(service.CancelCurrentJob());
        Assert.True(await service.WaitForIdleAsync(WaitLimit));

        var job = service.GetStatus().RecentJobs.First();
        Assert.Equal(runningId, job.Id);
        Assert.Equal(SourceIndexJobOutcome.Canceled, job.Outcome);
        Assert.NotNull(job.FinishedAtUtc);
        Assert.Null(service.CurrentJobId);
        Assert.Equal(1, service.IndexGeneration);
        AssertSearchFinds(service);

        /* A refused manual request queues nothing. */
        Assert.Equal(2, service.GetStatus().RecentJobs.Count);
    }

    [Fact]
    public async Task AnUnconfiguredPath_CompletesInitialization_WithAnEmptyIndex()
    {
        using var service = new GatedSourceCodeService(Config(string.Empty));
        await StartAndWaitAsync(service);

        Assert.True(service.IsIndexingComplete);
        Assert.Equal(0, service.IndexGeneration);
        Assert.Equal(string.Empty, service.ListFiles(null, false));
        Assert.Null(service.ContentFingerprint);
        Assert.Empty(service.GetMissingGeneratedHeaders());
        Assert.Equal(SourceIndexStartResult.NotConfigured, service.RequestReindex(SourceIndexTrigger.Manual));
        Assert.Equal(SourceIndexStartResult.NotConfigured, service.RequestMakedefsBuild());

        var status = service.GetStatus();
        Assert.False(status.Configured);
        Assert.False(status.PathExists);
        Assert.True(status.IsIndexingComplete);
        Assert.Null(status.Index);
        Assert.False(status.UpToDate);
        Assert.Empty(status.GeneratedHeaders);
        Assert.Equal(SourceIndexJobOutcome.Failed, Assert.Single(status.RecentJobs).Outcome);
        Assert.Equal(0, service.PassesReachingFiles);
    }

    [Fact]
    public async Task APass_GeneratesTheHeadersWithMakedefs_BeforeReadingTheFiles()
    {
        string executable = Path.Combine(_sourceDir, "tools", "makedefs.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "fake");

        var runner = new FakeProcessRunner(command =>
        {
            if (command.EndsWith(" -p", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(_sourceDir, "include", "pm.h"), "#define NUM_MONSTERS 3\r\n");
            }
            else if (command.EndsWith(" -o", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(_sourceDir, "include", "onames.h"), "#define NUM_OBJECTS 7\r\n");
            }
            else if (command.EndsWith(" -a", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(_sourceDir, "include", "animoff.h"), "#define ANIMOFF 1\r\n");
                File.WriteAllText(Path.Combine(_sourceDir, "include", "animtotals.h"), "#define ANIMTOTAL 2\r\n");
            }
            return FakeProcessRunner.Ok;
        });

        var config = Config(null, ("MakedefsBuildCommand", "build-makedefs.cmd"), ("MakedefsExecutablePath", executable));
        using var service = new GatedSourceCodeService(config, runner);
        Assert.Equal(4, service.GetMissingGeneratedHeaders().Count);
        Assert.Null(service.LastMakedefsOutcome);

        await StartAndWaitAsync(service);

        Assert.Equal(new[] { "build-makedefs.cmd", $"\"{executable}\" -o", $"\"{executable}\" -p", $"\"{executable}\" -a" }, runner.Commands);
        Assert.Equal("3", service.GetConstants("NUM_MONSTERS", null).Single().Value);
        Assert.Empty(service.GetMissingGeneratedHeaders());
        Assert.Equal(MakedefsOutcomeKind.Generated, service.LastMakedefsOutcome!.Kind);

        var status = service.GetStatus();
        Assert.True(status.Makedefs.Supported);
        Assert.Equal("Generated", status.Makedefs.LastOutcome);
        Assert.Equal(MakedefsInputsState.UpToDate, status.Makedefs.InputsState);
        Assert.All(status.GeneratedHeaders, h => Assert.True(h.Exists && h.InIndex, h.Name));
        Assert.Equal("Generated", status.RecentJobs[0].MakedefsOutcome);

        /* Unchanged inputs: the next pass starts no process. */
        Assert.Equal(SourceIndexStartResult.Started, service.RequestReindex(SourceIndexTrigger.Manual));
        Assert.True(await service.WaitForIdleAsync(WaitLimit));
        Assert.Equal(4, runner.Commands.Count);
        Assert.Equal(MakedefsOutcomeKind.UpToDate, service.LastMakedefsOutcome!.Kind);

        /* A forced build with unchanged outputs keeps the index. */
        Assert.Equal(SourceIndexStartResult.Started, service.RequestMakedefsBuild());
        Assert.True(await service.WaitForIdleAsync(WaitLimit));
        Assert.Equal(8, runner.Commands.Count);
        var buildJob = service.GetStatus().RecentJobs[0];
        Assert.Equal(SourceIndexJobKind.BuildMakedefs, buildJob.Kind);
        Assert.Equal(SourceIndexJobOutcome.Succeeded, buildJob.Outcome);
        Assert.Equal(2, service.IndexGeneration);
    }

    [Fact]
    public async Task TheNetHackService_HasNoMakedefs()
    {
        var runner = new FakeProcessRunner(_ => FakeProcessRunner.Ok);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("NetHackSourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MakedefsBuildCommand", "build-makedefs.cmd")
            })
            .Build();
        using var service = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance, runner);
        await StartAndWaitAsync(service);

        Assert.Empty(runner.Commands);
        Assert.Null(service.LastMakedefsOutcome);
        Assert.Empty(service.GetMissingGeneratedHeaders());
        Assert.Equal(SourceIndexStartResult.NotConfigured, service.RequestMakedefsBuild());

        var status = service.GetStatus();
        Assert.False(status.Makedefs.Supported);
        Assert.Empty(status.GeneratedHeaders);
        Assert.Equal(1, status.Index!.Generation);
        Assert.Null(status.RecentJobs[0].MakedefsOutcome);
    }

    [Fact]
    public void TheStatusDto_SerializesEnumsAsNames()
    {
        var job = new SourceIndexJob
        {
            Id = "index-1",
            Trigger = SourceIndexTrigger.Manual,
            Kind = SourceIndexJobKind.BuildMakedefs,
            Phase = SourceIndexPhase.ReadingFiles,
            Outcome = SourceIndexJobOutcome.Running
        };

        string json = JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"trigger\":\"Manual\"", json);
        Assert.Contains("\"kind\":\"BuildMakedefs\"", json);
        Assert.Contains("\"phase\":\"ReadingFiles\"", json);
        Assert.Contains("\"outcome\":\"Running\"", json);
    }

    /// <summary>A <see cref="SourceCodeService"/> whose passes can be held or failed before they read the files.</summary>
    private sealed class GatedSourceCodeService : SourceCodeService
    {
        private PassBlock? _nextBlock;
        private Exception? _nextFailure;
        private int _passesReachingFiles;
        private readonly List<SourceIndexTrigger> _triggers = new();

        public GatedSourceCodeService(IConfiguration configuration, IExternalProcessRunner? runner = null)
            : base(configuration, NullLogger<SourceCodeService>.Instance, runner ?? new FakeProcessRunner(_ => FakeProcessRunner.Ok))
        {
        }

        protected override TimeSpan QuietPeriod => TimeSpan.FromMilliseconds(10);
        protected override TimeSpan QuietWaitLimit => TimeSpan.FromSeconds(5);
        protected override TimeSpan LockPollInterval => TimeSpan.FromMilliseconds(10);

        public int PassesReachingFiles => Volatile.Read(ref _passesReachingFiles);

        public IReadOnlyList<SourceIndexTrigger> TriggersSeen
        {
            get { lock (_triggers) return _triggers.ToList(); }
        }

        public PassBlock BlockNextPass()
        {
            var block = new PassBlock();
            Volatile.Write(ref _nextBlock, block);
            return block;
        }

        public void FailNextPass(Exception exception) => Volatile.Write(ref _nextFailure, exception);

        protected override async Task OnBeforeReadFilesAsync(SourceIndexTrigger trigger, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _passesReachingFiles);
            lock (_triggers) _triggers.Add(trigger);

            var block = Interlocked.Exchange(ref _nextBlock, null);
            if (block != null)
            {
                block.Entered.TrySetResult();
                await block.Release.Task.WaitAsync(cancellationToken);
            }

            var failure = Interlocked.Exchange(ref _nextFailure, null);
            if (failure != null) throw failure;
        }
    }

    private sealed class PassBlock
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Records every command and answers with <c>handler</c>; starts no process.</summary>
    private sealed class FakeProcessRunner : IExternalProcessRunner
    {
        public static readonly ProcessRunResult Ok = new(0, false, false, TimeSpan.FromMilliseconds(1), Array.Empty<string>());

        private readonly Func<string, ProcessRunResult> _handler;
        private readonly List<string> _commands = new();

        public FakeProcessRunner(Func<string, ProcessRunResult> handler)
        {
            _handler = handler;
        }

        public IReadOnlyList<string> Commands
        {
            get { lock (_commands) return _commands.ToList(); }
        }

        public Task<ProcessRunResult> RunAsync(string command, string workingDirectory, TimeSpan timeout,
            Action<string>? onLine, CancellationToken cancellationToken)
        {
            lock (_commands) _commands.Add(command);
            return Task.FromResult(_handler(command));
        }
    }
}
