using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.SourceIndexing;

/// <summary>
/// The result of one shell command. <see cref="ExitCode"/> is null when the process could not be
/// started, or was killed before it exited. <see cref="OutputTail"/> holds the last
/// <see cref="ExternalProcessRunner.MaxTailLines"/> lines of standard output and standard error,
/// interleaved in arrival order, with standard error lines prefixed <c>[err] </c>.
/// </summary>
public sealed record ProcessRunResult(int? ExitCode, bool TimedOut, bool Canceled,
    TimeSpan Duration, IReadOnlyList<string> OutputTail)
{
    /// <summary>The process ran to completion and exited with code 0.</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Canceled;
}

/// <summary>Runs a shell command with a timeout, cancellation and a bounded output tail.</summary>
public interface IExternalProcessRunner
{
    /// <param name="command">The command line, run through <c>cmd.exe /c</c> on Windows and <c>/bin/bash -c</c> elsewhere.</param>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <param name="timeout">How long the command may run before its process tree is killed; zero or negative means no limit.</param>
    /// <param name="onLine">Receives each output line as it arrives; optional. Exceptions it throws are ignored.</param>
    /// <param name="cancellationToken">Kills the process tree when canceled.</param>
    Task<ProcessRunResult> RunAsync(string command, string workingDirectory, TimeSpan timeout,
        Action<string>? onLine, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IExternalProcessRunner"/> over <see cref="Process"/>. Output is captured through the
/// <c>OutputDataReceived</c> and <c>ErrorDataReceived</c> events into a bounded ring buffer; MSBuild
/// node reuse is disabled in the child environment, so no worker node outlives the build holding the
/// output pipes open; a timeout or a cancellation kills the whole process tree. Never throws for a
/// process failure.
/// </summary>
public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public const int MaxTailLines = 200;

    /// <summary>How long to wait for the process to exit after the tree was killed.</summary>
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait, after the process exited, for its output streams to close.</summary>
    private static readonly TimeSpan DrainWait = TimeSpan.FromSeconds(5);

    public async Task<ProcessRunResult> RunAsync(string command, string workingDirectory, TimeSpan timeout,
        Action<string>? onLine, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var tail = new BoundedLineBuffer(MaxTailLines);

        void Receive(string line)
        {
            tail.Add(line);
            if (onLine == null) return;
            try
            {
                onLine(line);
            }
            catch
            {
                /* A progress observer never fails the command. */
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ProcessRunResult(null, false, true, stopwatch.Elapsed, Array.Empty<string>());
        }

        bool isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo(
            isWindows ? "cmd.exe" : "/bin/bash",
            isWindows ? $"/c {command}" : $"-c \"{command}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Receive(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Receive("[err] " + e.Data); };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(null, false, false, stopwatch.Elapsed, new[] { $"The process for '{command}' did not start." });
            }
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(null, false, false, stopwatch.Elapsed, new[] { ex.Message });
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool timedOut = false;
        bool canceled = false;
        using var timeoutCts = new CancellationTokenSource(timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await exited.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                /* The process may have exited between the wait and the kill. */
            }

            try
            {
                await exited.Task.WaitAsync(KillWait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Receive($"[runner] The process tree did not exit within {KillWait.TotalSeconds:0} s of being killed.");
            }
        }

        if (!timedOut && !canceled)
        {
            /* The Exited event can precede the last output lines; WaitForExitAsync also waits for both
               redirected streams to reach end of file. */
            try
            {
                using var drainCts = new CancellationTokenSource(DrainWait);
                await process.WaitForExitAsync(drainCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Receive($"[runner] The output streams did not close within {DrainWait.TotalSeconds:0} s of the process exiting.");
            }
        }
        else if (timedOut)
        {
            Receive($"[runner] Timed out after {timeout.TotalSeconds:0} s; the process tree was killed.");
        }
        else
        {
            Receive("[runner] Canceled; the process tree was killed.");
        }

        int? exitCode = null;
        if (!timedOut && !canceled)
        {
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                exitCode = null;
            }
        }

        return new ProcessRunResult(exitCode, timedOut, canceled, stopwatch.Elapsed, tail.ToList());
    }

    /// <summary>The last <c>capacity</c> lines added, safe for concurrent writers.</summary>
    private sealed class BoundedLineBuffer
    {
        private readonly int _capacity;
        private readonly Queue<string> _lines = new();
        private readonly object _lock = new();

        public BoundedLineBuffer(int capacity)
        {
            _capacity = capacity;
        }

        public void Add(string line)
        {
            lock (_lock)
            {
                _lines.Enqueue(line);
                while (_lines.Count > _capacity)
                {
                    _lines.Dequeue();
                }
            }
        }

        public IReadOnlyList<string> ToList()
        {
            lock (_lock)
            {
                return _lines.ToArray();
            }
        }
    }
}
