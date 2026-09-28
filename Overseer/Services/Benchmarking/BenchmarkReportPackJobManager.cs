namespace Overseer.Services.Benchmarking;

/// <summary>Single active report-pack job, mirroring <see cref="BenchmarkRubricGapAuthorJobManager"/>.</summary>
public class BenchmarkReportPackJobManager
{
    private readonly object _lock = new();
    private BenchmarkReportPackJob? _currentJob;

    public BenchmarkReportPackJob? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentJob;
            }
        }
    }

    public bool TryStart(BenchmarkReportPackJob job, out BenchmarkReportPackJob? existing)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Status == BenchmarkReportPackJobStatus.Running)
            {
                existing = _currentJob;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    public BenchmarkReportPackJob? TryGet(string jobId)
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
            if (_currentJob != null && _currentJob.Id == jobId && _currentJob.Status == BenchmarkReportPackJobStatus.Running)
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

    public void Complete(string jobId, BenchmarkReportPackJobStatus status)
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
