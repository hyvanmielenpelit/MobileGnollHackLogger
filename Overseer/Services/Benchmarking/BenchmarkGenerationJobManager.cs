namespace Overseer.Services.Benchmarking;

public class BenchmarkGenerationJobManager
{
    private readonly object _lock = new();
    private BenchmarkGenerationJob? _currentJob;

    public BenchmarkGenerationJob? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentJob;
            }
        }
    }

    public bool TryStart(BenchmarkGenerationJob job, out BenchmarkGenerationJob? existing)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Status == BenchmarkGenerationJobStatus.Running)
            {
                existing = _currentJob;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    public BenchmarkGenerationJob? TryGet(string jobId)
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
            if (_currentJob != null && _currentJob.Id == jobId && _currentJob.Status == BenchmarkGenerationJobStatus.Running)
            {
                try
                {
                    _currentJob.Cts.Cancel();
                    return true;
                }
                catch (System.ObjectDisposedException)
                {
                    return false;
                }
            }
            return false;
        }
    }

    public void Complete(string jobId, BenchmarkGenerationJobStatus status)
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
