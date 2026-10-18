using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.SourceIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="MakedefsInputSet"/> and <see cref="MakedefsHeaderGenerator"/> over a temp-directory tree
/// shaped like GnollHack's, with a fake process runner that writes the four outputs: what moves the
/// fingerprint and what does not, when makedefs is skipped, rebuilt and rerun, and the stamp.
/// </summary>
public class MakedefsHeaderGeneratorTests : IDisposable
{
    private const string BuildCommand = "\"C:\\Build Tools\\MSBuild.exe\" win\\win32\\vs\\makedefs.vcxproj";

    private readonly string _root;
    private readonly string _executable;

    public MakedefsHeaderGeneratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MakedefsHeaderGeneratorTests_" + Guid.NewGuid().ToString("N"));
        Write("src/animdef.c", "/* animdef.c */\r\n");
        Write("src/monst.c", "/* monst.c */\r\n");
        Write("src/objects.c", "/* objects.c */\r\n");
        Write("util/makedefs.c", "/* makedefs.c */\r\n");
        Write("util/mdgrep.h", "/* mdgrep.h */\r\n");
        Write("include/permonst.h", "/* permonst.h */\r\n");
        Write("include/objclass.h", "/* objclass.h */\r\n");
        Write("include/date.h", "#define BUILD_DATE \"today\"\r\n");
        Write("sys/winnt/ntconf.h", "/* ntconf.h */\r\n");
        Write("win/win32/vs/makedefs.vcxproj", "<Project />\r\n");
        Write("win/win32/vs/dirs.props", "<Project />\r\n");

        _executable = Path.Combine(_root, "tools", "Release", "x64", "makedefs.exe");
        Write("tools/Release/x64/makedefs.exe", "fake executable");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private IConfiguration Config(string? buildCommand = BuildCommand, params (string Key, string? Value)[] extra)
    {
        var values = new List<KeyValuePair<string, string?>> { new("MakedefsBuildCommand", buildCommand) };
        values.AddRange(extra.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)));
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private MakedefsHeaderGenerator Generator(FakeMakedefsRunner runner, IConfiguration? config = null)
        => new(_root, config ?? Config(), runner, NullLogger.Instance);

    private static Task<MakedefsOutcome> EnsureAsync(MakedefsHeaderGenerator generator, bool force = false)
        => generator.EnsureHeadersAsync(force, null, TestContext.Current.CancellationToken);

    [Fact]
    public void Fingerprint_Changes_WhenAnIncludeHeaderChanges()
    {
        string before = MakedefsInputSet.Compute(_root, BuildCommand).Sha256;
        Write("include/permonst.h", "/* permonst.h, edited */\r\n");
        Assert.NotEqual(before, MakedefsInputSet.Compute(_root, BuildCommand).Sha256);
    }

    [Fact]
    public void Fingerprint_Changes_WhenTheBuildCommandChanges()
    {
        Assert.NotEqual(
            MakedefsInputSet.Compute(_root, BuildCommand).Sha256,
            MakedefsInputSet.Compute(_root, BuildCommand + " /p:Configuration=Debug").Sha256);
    }

    [Theory]
    [InlineData("include/pm.h")]
    [InlineData("include/onames.h")]
    [InlineData("include/animoff.h")]
    [InlineData("include/animtotals.h")]
    [InlineData("include/date.h")]
    public void Fingerprint_IgnoresTheGeneratedOutputsAndDateH(string relative)
    {
        string before = MakedefsInputSet.Compute(_root, BuildCommand).Sha256;
        Write(relative, "/* written by a build */\r\n");
        Assert.Equal(before, MakedefsInputSet.Compute(_root, BuildCommand).Sha256);
    }

    [Fact]
    public void Inputs_AreTheCompiledSourcesHeadersAndProjectFiles_SortedIgnoringCase()
    {
        var inputs = MakedefsInputSet.EnumerateInputs(_root);

        Assert.Equal(new[]
        {
            "include/objclass.h",
            "include/permonst.h",
            "src/animdef.c",
            "src/monst.c",
            "src/objects.c",
            "sys/winnt/ntconf.h",
            "util/makedefs.c",
            "util/mdgrep.h",
            "win/win32/vs/dirs.props",
            "win/win32/vs/makedefs.vcxproj"
        }, inputs);
        Assert.Empty(MakedefsInputSet.Compute(_root, BuildCommand).MissingRequired);

        File.Delete(Path.Combine(_root, "src", "monst.c"));
        Assert.Equal(new[] { "src/monst.c" }, MakedefsInputSet.Compute(_root, BuildCommand).MissingRequired);
    }

    [Fact]
    public async Task AFirstCheck_BuildsAndRunsMakedefs_AndWritesTheStampAtomically()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);

        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.True(outcome.HeadersChanged);
        Assert.True(outcome.BuildRan);
        Assert.False(outcome.UsedUnbuiltExecutable);
        Assert.Equal(new[] { BuildCommand, $"\"{_executable}\" -o", $"\"{_executable}\" -p", $"\"{_executable}\" -a" }, runner.Commands);
        Assert.Equal(new[] { _root, Path.Combine(_root, "util"), Path.Combine(_root, "util"), Path.Combine(_root, "dat") }, runner.WorkingDirectories);

        string stampPath = Path.Combine(_root, "tools", "Release", "x64", MakedefsHeaderGenerator.StampFileName);
        Assert.Equal(stampPath, generator.StampPath);
        Assert.True(File.Exists(stampPath));
        Assert.False(File.Exists(stampPath + ".tmp"));

        using (var json = JsonDocument.Parse(File.ReadAllText(stampPath)))
        {
            var root = json.RootElement;
            Assert.Equal(MakedefsInputSet.Compute(_root, BuildCommand).Sha256, root.GetProperty("inputsSha256").GetString());
            var outputs = root.GetProperty("outputs");
            Assert.Equal(MakedefsInputSet.GeneratedHeaders.OrderBy(n => n), outputs.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            Assert.True(root.TryGetProperty("generatedAtUtc", out _));
            Assert.True(root.TryGetProperty("headSha", out _));
        }

        var stamp = generator.ReadStamp();
        Assert.NotNull(stamp);
        Assert.Equal(MakedefsInputSet.Compute(_root, BuildCommand).Sha256, stamp!.InputsSha256);
        Assert.Equal(4, stamp.Outputs.Count);
        Assert.Equal(MakedefsInputsState.UpToDate, generator.GetState().InputsState);
    }

    [Fact]
    public async Task UpToDate_StartsNoProcess()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        runner.Reset();

        var outcome = await EnsureAsync(Generator(runner));

        Assert.Equal(MakedefsOutcomeKind.UpToDate, outcome.Kind);
        Assert.Empty(runner.Commands);
        Assert.False(outcome.HeadersChanged);
    }

    [Fact]
    public async Task AnInputChange_Regenerates()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        runner.Reset();
        Assert.Equal(MakedefsInputsState.UpToDate, generator.GetState().InputsState);

        Write("include/permonst.h", "/* permonst.h, a new field */\r\n");
        Assert.Equal(MakedefsInputsState.Stale, generator.GetState().InputsState);
        runner.PmContent = "#define NUM_MONSTERS 4\r\n";

        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.True(outcome.HeadersChanged);
        Assert.Equal(4, runner.Commands.Count);
    }

    [Fact]
    public async Task AMissingOutput_Regenerates()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        runner.Reset();

        File.Delete(Path.Combine(_root, "include", "onames.h"));
        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.True(outcome.HeadersChanged);
        Assert.Equal(4, runner.Commands.Count);
    }

    [Fact]
    public async Task AHandEditedOutput_Regenerates()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        runner.Reset();

        Write("include/pm.h", "#define NUM_MONSTERS 999\r\n");
        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.True(outcome.HeadersChanged);
        Assert.Equal(runner.PmContent, Read("include/pm.h"));
    }

    [Fact]
    public async Task Force_BuildsEvenWhenUpToDate()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        runner.Reset();

        var outcome = await EnsureAsync(generator, force: true);

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.False(outcome.HeadersChanged);
        Assert.Equal(4, runner.Commands.Count);
        Assert.Equal(BuildCommand, runner.Commands[0]);
    }

    [Fact]
    public async Task ABuildFailure_RunsNoExecutable_AndLeavesTheStampUntouched()
    {
        var runner = new FakeMakedefsRunner(_root);
        var generator = Generator(runner);
        await EnsureAsync(generator);
        string stampBefore = File.ReadAllText(generator.StampPath);
        runner.Reset();

        Write("include/permonst.h", "/* permonst.h, edited */\r\n");
        runner.FailWhen = command => command == BuildCommand;
        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.BuildFailed, outcome.Kind);
        Assert.Equal(new[] { BuildCommand }, runner.Commands);
        Assert.Contains("exit code 1", outcome.Message);
        Assert.Contains("build failed", outcome.OutputTail);
        Assert.Equal(stampBefore, File.ReadAllText(generator.StampPath));
        Assert.Equal(MakedefsOutcomeKind.BuildFailed, generator.LastOutcome!.Kind);
        Assert.NotNull(generator.GetState().LastBuildAtUtc);
    }

    [Fact]
    public async Task ARunFailure_WritesNoStamp_SoTheNextCheckRetries()
    {
        var runner = new FakeMakedefsRunner(_root) { FailWhen = command => command.EndsWith(" -p", StringComparison.Ordinal) };
        var generator = Generator(runner);

        var outcome = await EnsureAsync(generator);

        Assert.Equal(MakedefsOutcomeKind.RunFailed, outcome.Kind);
        Assert.Contains("makedefs -p", outcome.Message);
        Assert.False(File.Exists(generator.StampPath));
        Assert.Null(generator.ReadStamp());

        runner.Reset();
        runner.FailWhen = null;
        Assert.Equal(MakedefsOutcomeKind.Generated, (await EnsureAsync(generator)).Kind);
        Assert.Equal(4, runner.Commands.Count);
    }

    [Fact]
    public async Task AMissingExecutable_IsReported_WithoutRunningIt()
    {
        File.Delete(_executable);
        var runner = new FakeMakedefsRunner(_root);

        var outcome = await EnsureAsync(Generator(runner, Config(buildCommand: null)));

        Assert.Equal(MakedefsOutcomeKind.ExecutableMissing, outcome.Kind);
        Assert.Contains(_executable, outcome.Message);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task WithoutABuildCommand_TheExistingExecutableRuns_AndIsMarkedUnbuilt()
    {
        var runner = new FakeMakedefsRunner(_root);

        var outcome = await EnsureAsync(Generator(runner, Config(buildCommand: null)));

        Assert.Equal(MakedefsOutcomeKind.Generated, outcome.Kind);
        Assert.True(outcome.UsedUnbuiltExecutable);
        Assert.False(outcome.BuildRan);
        Assert.Equal(3, runner.Commands.Count);
    }

    [Fact]
    public async Task ABranchRestriction_SkipsMakedefs_OnAnotherBranch()
    {
        Write(".git/HEAD", "ref: refs/heads/feature\n");
        var runner = new FakeMakedefsRunner(_root);

        var outcome = await EnsureAsync(Generator(runner, Config(BuildCommand, ("MakedefsBranch", "main"))));

        Assert.Equal(MakedefsOutcomeKind.SkippedBranch, outcome.Kind);
        Assert.Contains("feature", outcome.Message);
        Assert.Empty(runner.Commands);

        var onBranch = await EnsureAsync(Generator(runner, Config(BuildCommand, ("MakedefsBranch", "feature"))));
        Assert.Equal(MakedefsOutcomeKind.Generated, onBranch.Kind);
    }

    [Fact]
    public async Task TheTimeouts_ComeFromConfiguration()
    {
        var runner = new FakeMakedefsRunner(_root);
        var config = Config(BuildCommand, ("MakedefsBuildTimeoutSeconds", "42"), ("MakedefsRunTimeoutSeconds", "7"));

        await EnsureAsync(Generator(runner, config));

        Assert.Equal(TimeSpan.FromSeconds(42), runner.Timeouts[0]);
        Assert.All(runner.Timeouts.Skip(1), t => Assert.Equal(TimeSpan.FromSeconds(7), t));

        var defaults = Generator(runner, Config(BuildCommand, ("MakedefsBuildTimeoutSeconds", "not a number"), ("MakedefsRunTimeoutSeconds", "0")));
        Assert.Equal(TimeSpan.FromSeconds(MakedefsHeaderGenerator.DefaultBuildTimeoutSeconds), defaults.BuildTimeout);
        Assert.Equal(TimeSpan.FromSeconds(MakedefsHeaderGenerator.DefaultRunTimeoutSeconds), defaults.RunTimeout);
    }

    [Fact]
    public async Task ACanceledBuild_Throws()
    {
        using var cts = new CancellationTokenSource();
        var runner = new FakeMakedefsRunner(_root) { OnRun = () => cts.Cancel() };
        var generator = Generator(runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.EnsureHeadersAsync(false, null, cts.Token));
        Assert.Single(runner.Commands);
        Assert.False(File.Exists(generator.StampPath));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\MSBuild\\MSBuild.exe\" makedefs.vcxproj /p:x=1", "C:\\Program Files\\MSBuild\\MSBuild.exe")]
    [InlineData("msbuild.exe makedefs.vcxproj", "msbuild.exe")]
    [InlineData("  build.cmd  ", "build.cmd")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CommandTool_IsTheQuotedOrFirstToken(string? command, string? expected)
    {
        Assert.Equal(expected, MakedefsHeaderGenerator.CommandTool(command));
    }

    /// <summary>
    /// Answers every command with exit code 0 unless <see cref="FailWhen"/> matches, and writes the
    /// outputs a real makedefs would: <c>-o</c> onames.h, <c>-p</c> pm.h, <c>-a</c> animoff.h and animtotals.h.
    /// </summary>
    private sealed class FakeMakedefsRunner : IExternalProcessRunner
    {
        private readonly string _root;
        private readonly List<(string Command, string Directory, TimeSpan Timeout)> _calls = new();

        public FakeMakedefsRunner(string root)
        {
            _root = root;
        }

        public string PmContent { get; set; } = "#define NUM_MONSTERS 3\r\n";
        public Func<string, bool>? FailWhen { get; set; }
        public Action? OnRun { get; set; }

        public IReadOnlyList<string> Commands => _calls.Select(c => c.Command).ToList();
        public IReadOnlyList<string> WorkingDirectories => _calls.Select(c => c.Directory).ToList();
        public IReadOnlyList<TimeSpan> Timeouts => _calls.Select(c => c.Timeout).ToList();

        public void Reset() => _calls.Clear();

        public Task<ProcessRunResult> RunAsync(string command, string workingDirectory, TimeSpan timeout,
            Action<string>? onLine, CancellationToken cancellationToken)
        {
            _calls.Add((command, workingDirectory, timeout));
            OnRun?.Invoke();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(new ProcessRunResult(null, false, true, TimeSpan.Zero, new[] { "[runner] Canceled" }));
            }

            if (FailWhen?.Invoke(command) == true)
            {
                return Task.FromResult(new ProcessRunResult(1, false, false, TimeSpan.FromMilliseconds(1), new[] { "build failed" }));
            }

            string include = Path.Combine(_root, "include");
            Directory.CreateDirectory(include);
            if (command.EndsWith(" -o", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(include, "onames.h"), "#define NUM_OBJECTS 7\r\n");
            }
            else if (command.EndsWith(" -p", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(include, "pm.h"), PmContent);
            }
            else if (command.EndsWith(" -a", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(include, "animoff.h"), "#define ANIMOFF 1\r\n");
                File.WriteAllText(Path.Combine(include, "animtotals.h"), "#define ANIMTOTAL 2\r\n");
            }

            return Task.FromResult(new ProcessRunResult(0, false, false, TimeSpan.FromMilliseconds(1), new[] { "ok" }));
        }
    }
}
