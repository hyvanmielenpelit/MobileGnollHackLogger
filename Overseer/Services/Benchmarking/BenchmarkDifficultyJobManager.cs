namespace Overseer.Services.Benchmarking;

using System;

public class BenchmarkDifficultyJobManager
{
    private readonly object _lock = new();
    private BenchmarkDifficultyJob? _currentJob;

    public BenchmarkDifficultyJob? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentJob;
            }
        }
    }

    public bool TryStart(BenchmarkDifficultyJob job, out BenchmarkDifficultyJob? existing)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Status == BenchmarkDifficultyJobStatus.Running)
            {
                existing = _currentJob;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    public BenchmarkDifficultyJob? TryGet(string jobId)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Id == jobId)
            {
                return _currentJob;
            }
            return null;
        }
    }

    public bool TryCancel(string jobId)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Id == jobId && _currentJob.Status == BenchmarkDifficultyJobStatus.Running)
            {
                try
                {
                    _currentJob.Cts.Cancel();
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
            return false;
        }
    }

    public void Complete(string jobId, BenchmarkDifficultyJobStatus status)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Id == jobId)
            {
                _currentJob.SetStatus(status);
            }
        }
    }
}
