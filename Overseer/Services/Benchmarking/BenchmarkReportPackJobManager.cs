namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Single active report-pack job, mirroring <see cref="BenchmarkRubricGapAuthorJobManager"/>. A
/// run-completion job waits for the slot in a first-in, first-out queue; a manual start is refused
/// while a job runs or any job waits.
/// </summary>
public class BenchmarkReportPackJobManager
{
    private readonly object _lock = new();
    private readonly LinkedList<BenchmarkReportPackJob> _waiting = new();
    private readonly TimeSpan _pollInterval;
    private BenchmarkReportPackJob? _currentJob;

    /// <param name="pollInterval">How often a waiting job checks the slot; half a second when not given.</param>
    public BenchmarkReportPackJobManager(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    }

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

    /// <summary>Jobs waiting for the slot.</summary>
    public int WaitingCount
    {
        get
        {
            lock (_lock)
            {
                return _waiting.Count;
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

            if (_waiting.First != null)
            {
                existing = _waiting.First.Value;
                return false;
            }

            _currentJob = job;
            existing = null;
            return true;
        }
    }

    /// <summary>
    /// Waits, first in first out, until no job is running and every job queued earlier has had its
    /// turn, then makes <paramref name="job"/> the current one. A canceled wait leaves the queue.
    /// </summary>
    public async Task WaitForSlotAsync(BenchmarkReportPackJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        LinkedListNode<BenchmarkReportPackJob> node;
        lock (_lock)
        {
            node = _waiting.AddLast(job);
        }

        try
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_waiting.First == node && (_currentJob == null || _currentJob.Status != BenchmarkReportPackJobStatus.Running))
                    {
                        _waiting.RemoveFirst();
                        _currentJob = job;
                        return;
                    }
                }

                await Task.Delay(_pollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                if (node.List != null) _waiting.Remove(node);
            }
            throw;
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
