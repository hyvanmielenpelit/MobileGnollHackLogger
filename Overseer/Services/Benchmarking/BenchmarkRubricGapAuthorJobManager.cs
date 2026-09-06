namespace Overseer.Services.Benchmarking;

/// <summary>
/// Single active Rubric Gap Author job, mirroring <see cref="BenchmarkRubricCheckJobManager"/>.
///
/// The completed job is deliberately kept after it finishes rather than cleared: an acceptance
/// arrives later, from a human who read the drafts, and it needs the draft text and the drafting
/// model still in memory to record provenance against.
/// </summary>
public class BenchmarkRubricGapAuthorJobManager
{
    private readonly object _lock = new();
    private BenchmarkRubricGapAuthorJob? _currentJob;

    public BenchmarkRubricGapAuthorJob? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentJob;
            }
        }
    }

    public bool TryStart(BenchmarkRubricGapAuthorJob job, out BenchmarkRubricGapAuthorJob? existing)
    {
        lock (_lock)
        {
            if (_currentJob != null && _currentJob.Status == BenchmarkRubricGapAuthorJobStatus.Running)
            {
                existing = _currentJob;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    public BenchmarkRubricGapAuthorJob? TryGet(string jobId)
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
            if (_currentJob != null && _currentJob.Id == jobId && _currentJob.Status == BenchmarkRubricGapAuthorJobStatus.Running)
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

    public void Complete(string jobId, BenchmarkRubricGapAuthorJobStatus status)
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
