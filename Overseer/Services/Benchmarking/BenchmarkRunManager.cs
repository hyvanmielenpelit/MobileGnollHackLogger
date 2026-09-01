namespace Overseer.Services.Benchmarking;

using System;
using System.Threading;

public class BenchmarkRunState
{
    public long RunId { get; set; }
    public CancellationTokenSource Cts { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
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
                    return true;
                }
                catch (ObjectDisposedException) { }
            }
            return false;
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
