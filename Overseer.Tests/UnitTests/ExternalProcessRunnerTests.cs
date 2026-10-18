using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Overseer.Services.SourceIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="ExternalProcessRunner"/> against real <c>cmd.exe</c> commands: exit codes, the captured
/// output, and that a timeout or a cancellation kills a long command promptly. Windows only.
/// </summary>
public class ExternalProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("ExternalProcessRunner's tests run cmd.exe commands and need Windows.");
        }
    }

    [Fact]
    public async Task Echo_ReturnsExitCodeZero_AndItsOutput()
    {
        RequireWindows();
        var lines = new ConcurrentQueue<string>();

        var result = await new ExternalProcessRunner().RunAsync("echo overseer-runner-test", Path.GetTempPath(), Generous,
            lines.Enqueue, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.False(result.Canceled);
        Assert.Contains(result.OutputTail, l => l.Contains("overseer-runner-test", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("overseer-runner-test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StandardError_IsPrefixed()
    {
        RequireWindows();

        var result = await new ExternalProcessRunner().RunAsync("echo to-stderr 1>&2", Path.GetTempPath(), Generous,
            null, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.OutputTail, l => l.StartsWith("[err] ", StringComparison.Ordinal) && l.Contains("to-stderr", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANonZeroExit_IsReported()
    {
        RequireWindows();

        var result = await new ExternalProcessRunner().RunAsync("exit /b 3", Path.GetTempPath(), Generous,
            null, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ATimeout_KillsTheProcessTree_Promptly()
    {
        RequireWindows();
        var stopwatch = Stopwatch.StartNew();

        var result = await new ExternalProcessRunner().RunAsync("ping -n 30 127.0.0.1", Path.GetTempPath(), TimeSpan.FromSeconds(1),
            null, TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.False(result.Canceled);
        Assert.Null(result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Cancellation_KillsTheProcessTree_Promptly()
    {
        RequireWindows();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        var result = await new ExternalProcessRunner().RunAsync("ping -n 30 127.0.0.1", Path.GetTempPath(), Generous,
            null, cts.Token);

        Assert.True(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task AMissingWorkingDirectory_IsAFailedStart_NotAnException()
    {
        RequireWindows();
        string missing = Path.Combine(Path.GetTempPath(), "ExternalProcessRunnerTests_" + Guid.NewGuid().ToString("N"));

        var result = await new ExternalProcessRunner().RunAsync("echo never", missing, Generous,
            null, TestContext.Current.CancellationToken);

        Assert.Null(result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Single(result.OutputTail);
    }
}
