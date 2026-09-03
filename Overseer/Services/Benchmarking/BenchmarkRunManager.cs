namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public class BenchmarkRunState
{
    public long RunId { get; set; }
    public CancellationTokenSource Cts { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Order indexes of the questions whose request is currently in flight to the provider.
    /// The executor writes a <see cref="MobileGnollHackLogger.Data.BenchmarkRunAnswer"/> only
    /// after the model replies, so this is the only place an in-flight question is visible.
    /// The progress dialog reads it to tell "not dispatched yet" from "answering".
    /// </summary>
    public ConcurrentDictionary<int, byte> InFlightQuestions { get; } = new();
}

public class BenchmarkRunManager
{
    private readonly object _lock = new();
    private BenchmarkRunState? _currentRun;

    public long? CurrentRunId
    {
        get
        {
            lock (_lock)
            {
                if (_currentRun != null && !_currentRun.IsCompleted)
                {
                    return _currentRun.RunId;
                }
                return null;
            }
        }
    }

    public bool TryStart(long runId, CancellationTokenSource cts, out BenchmarkRunState state)
    {
        lock (_lock)
        {
            if (_currentRun != null && !_currentRun.IsCompleted)
            {
                state = _currentRun;
                return false;
            }

            state = new BenchmarkRunState
            {
                RunId = runId,
                Cts = cts,
                StartedAtUtc = DateTime.UtcNow,
                IsCompleted = false
            };
            _currentRun = state;
            return true;
        }
    }

    public void Complete(long runId)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId)
            {
                _currentRun.IsCompleted = true;
                _currentRun.CompletedAtUtc = DateTime.UtcNow;
                _currentRun.InFlightQuestions.Clear();
            }
        }
    }

    public bool TryCancel(long runId)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId && !_currentRun.IsCompleted)
            {
                try
                {
                    _currentRun.Cts.Cancel();
                    _currentRun.IsCompleted = true;
                    _currentRun.CompletedAtUtc = DateTime.UtcNow;
                    _currentRun.InFlightQuestions.Clear();
                    return true;
                }
                catch (ObjectDisposedException) { }
            }
            return false;
        }
    }

    /// <summary>
    /// Records that the question's request has been sent to the provider. Ignored when the run
    /// is not the current one, so a stale caller can never resurrect finished state.
    /// </summary>
    public void MarkQuestionInFlight(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.InFlightQuestions.TryAdd(orderIndex, 0);
    }

    /// <summary>
    /// Clears the in-flight mark. Called from a <c>finally</c>, so a throw, a timeout or a
    /// cancellation cannot leave a question showing as answering forever.
    /// </summary>
    public void ClearQuestionInFlight(long runId, int orderIndex)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId)
            {
                _currentRun.InFlightQuestions.TryRemove(orderIndex, out _);
            }
        }
    }

    /// <summary>
    /// The questions currently awaiting a provider reply, ascending. Empty for any run that is
    /// not the current, still-running one: a completed or restarted run reports nothing rather
    /// than stale state.
    /// </summary>
    public IReadOnlyList<int> GetInFlightQuestions(long runId)
    {
        var state = TryGetRunning(runId);
        if (state == null)
        {
            return Array.Empty<int>();
        }
        return state.InFlightQuestions.Keys.OrderBy(i => i).ToList();
    }

    private BenchmarkRunState? TryGetRunning(long runId)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId && !_currentRun.IsCompleted)
            {
                return _currentRun;
            }
            return null;
        }
    }

    public BenchmarkRunState? TryGet(long runId)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId)
            {
                return _currentRun;
            }
            return null;
        }
    }
}
