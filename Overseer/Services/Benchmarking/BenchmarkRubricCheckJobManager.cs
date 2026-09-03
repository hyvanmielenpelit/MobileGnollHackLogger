namespace Overseer.Services.Benchmarking;

public class BenchmarkRubricCheckJobManager
{
    private readonly object _lock = new();
    private BenchmarkRubricCheckJob? _currentJob;

    public BenchmarkRubricCheckJob? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentJob;
            }
        }
    }

    public bool TryStart(BenchmarkRubricCheckJob job, out BenchmarkRubricCheckJob? existing)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Status == BenchmarkRubricCheckJobStatus.Running)
            {
                existing = _currentJob;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    public BenchmarkRubricCheckJob? TryGet(string jobId)
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
            if (_currentJob != null && _currentJob.Id == jobId && _currentJob.Status == BenchmarkRubricCheckJobStatus.Running)
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

    public void Complete(string jobId, BenchmarkRubricCheckJobStatus status)
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
