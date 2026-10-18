using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.CorpusIndexing;

/// <summary>
/// Runs the indexing passes of one corpus, one at a time, and keeps their job records: the running
/// job with its live phase and file counts, and the last <see cref="MaxRecentJobs"/> finished jobs.
/// The runner knows nothing about the corpus; each pass is a delegate the owning service supplies.
/// </summary>
public sealed class CorpusIndexJobRunner : IDisposable
{
    /// <summary>The number of finished jobs <see cref="RecentJobs"/> keeps.</summary>
    public const int MaxRecentJobs = 20;

    /// <summary>The longest job message kept.</summary>
    public const int MaxMessageLength = 500;

    internal const string CanceledMessage = "Canceled; the previous index keeps serving.";

    private static readonly TimeSpan DisposeWaitLimit = TimeSpan.FromSeconds(5);

    private readonly string _corpusName;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /* Canceled by Dispose; every pass receives its token. Holds no timer or wait handle, so it is
       left to the collector rather than disposed while a pass may still read its token. */
    private readonly CancellationTokenSource _disposeCts = new();

    /* _running is the gate. It is taken, released and handed to a follow-up under _jobLock, together
       with the follow-up request, so a request that arrives while a pass ends is never lost: it either
       finds the gate free or leaves a request the ending pass picks up. */
    private readonly object _jobLock = new();
    private bool _running;
    private RunningJob? _currentJob;
    private string? _lastStartedJobId;
    private readonly LinkedList<CorpusIndexJob> _recentJobs = new();
    private Func<CorpusPassContext, CancellationToken, Task<string?>>? _pendingPass;
    private TaskCompletionSource? _pendingCompletion;
    private bool _disposed;

    /* The task of the most recently started pass; written under _jobLock when the gate is taken, so
       Dispose always waits for the pass that holds it. */
    private Task _runningPass = Task.CompletedTask;

    /// <param name="corpusName">The corpus's name in log messages, such as <c>GnollHack wiki</c>.</param>
    public CorpusIndexJobRunner(string corpusName, ILogger logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _corpusName = string.IsNullOrWhiteSpace(corpusName) ? "Corpus" : corpusName;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The running job with its current phase and file counts; null when no pass runs.</summary>
    public CorpusIndexJob? CurrentJob
    {
        get
        {
            RunningJob? job;
            lock (_jobLock) job = _currentJob;
            return job?.ToJob(null);
        }
    }

    /// <summary>The most recent finished jobs, newest first, at most <see cref="MaxRecentJobs"/>.</summary>
    public IReadOnlyList<CorpusIndexJob> RecentJobs
    {
        get { lock (_jobLock) return _recentJobs.ToList(); }
    }

    /// <summary>The newest finished job, the same as <c>RecentJobs[0]</c>; null before the first pass ends.</summary>
    public CorpusIndexJob? LastFinishedJob
    {
        get { lock (_jobLock) return _recentJobs.First?.Value; }
    }

    /// <summary>The id of the most recently started job, running or finished; null before the first.</summary>
    public string? LastStartedJobId
    {
        get { lock (_jobLock) return _lastStartedJobId; }
    }

    /// <summary>Whether a pass is running.</summary>
    public bool IsRunning
    {
        get { lock (_jobLock) return _running; }
    }

    /// <summary>
    /// Starts <paramref name="pass"/> on the thread pool when no pass runs.
    /// <list type="bullet">
    /// <item>No pass runs: <see cref="CorpusIndexStartResult.Started"/>; the task completes when this
    /// pass has ended and its job is in <see cref="RecentJobs"/>. It does not wait for a follow-up the
    /// pass starts, so a <see cref="CorpusIndexTrigger.Startup"/> task ends with the first pass.</item>
    /// <item>A pass runs and <paramref name="trigger"/> is <see cref="CorpusIndexTrigger.Manual"/>:
    /// <see cref="CorpusIndexStartResult.AlreadyRunning"/> and null.</item>
    /// <item>A pass runs and the trigger is automatic: <see cref="CorpusIndexStartResult.AlreadyRunning"/>,
    /// and one <see cref="CorpusIndexTrigger.FollowUp"/> pass is queued to run when the current one
    /// ends, with the delegate of the most recent automatic request. Further automatic requests share
    /// that follow-up. The task completes when the follow-up has ended, or when the running pass ends
    /// without starting one because the runner was disposed.</item>
    /// <item>After <see cref="Dispose"/>: <see cref="CorpusIndexStartResult.AlreadyRunning"/> and null.</item>
    /// </list>
    /// No returned task ever faults. A pass that throws is recorded as
    /// <see cref="CorpusIndexOutcome.Failed"/> with the first line of the exception message, and the
    /// exception is logged once. An <see cref="OperationCanceledException"/> after <see cref="Dispose"/>
    /// is recorded as <see cref="CorpusIndexOutcome.Canceled"/>. A pass that calls
    /// <see cref="CorpusPassContext.Fail"/> is recorded as failed with that message; otherwise the
    /// string it returns becomes the job's message. Messages are cut to <see cref="MaxMessageLength"/>.
    /// </summary>
    public Task? TryStart(CorpusIndexTrigger trigger, Func<CorpusPassContext, CancellationToken, Task<string?>> pass, out CorpusIndexStartResult result)
    {
        ArgumentNullException.ThrowIfNull(pass);

        Task<Task> outer;
        Task task;
        lock (_jobLock)
        {
            if (_disposed)
            {
                result = CorpusIndexStartResult.AlreadyRunning;
                return null;
            }

            if (_running)
            {
                result = CorpusIndexStartResult.AlreadyRunning;
                if (trigger == CorpusIndexTrigger.Manual) return null;

                _pendingPass = pass;
                _pendingCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pendingCompletion.Task;
            }

            (outer, task) = BeginJobLocked(trigger, pass);
        }

        result = CorpusIndexStartResult.Started;
        outer.Start(TaskScheduler.Default);
        return task;
    }

    /// <summary>Cancels the running pass and waits at most 5 seconds for it to end. No pass starts afterwards.</summary>
    public void Dispose()
    {
        Task running;
        lock (_jobLock)
        {
            if (_disposed) return;
            _disposed = true;
            running = _runningPass;
        }

        try
        {
            _disposeCts.Cancel();
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "{Corpus}: a cancellation callback threw while the index runner was disposed.", _corpusName);
        }

        try
        {
            running.Wait(DisposeWaitLimit);
        }
        catch (AggregateException)
        {
            /* RunPassAsync never faults; nothing to wait for. */
        }
    }

    /// <summary>
    /// Takes the gate, records a new running job and creates its pass's task, which the caller starts
    /// with <see cref="TaskScheduler.Default"/> once it has left the lock. Caller holds <see cref="_jobLock"/>.
    /// </summary>
    private (Task<Task> Outer, Task Pass) BeginJobLocked(CorpusIndexTrigger trigger,
        Func<CorpusPassContext, CancellationToken, Task<string?>> pass)
    {
        _running = true;
        var job = new RunningJob(NewJobId(), trigger, _time.GetUtcNow().UtcDateTime);
        _currentJob = job;
        _lastStartedJobId = job.Id;
        _logger.LogInformation("{Corpus}: index job {Id} started ({Trigger}).", _corpusName, job.Id, trigger);

        var outer = new Task<Task>(() => RunPassAsync(job, pass));
        var task = outer.Unwrap();
        _runningPass = task;
        return (outer, task);
    }

    /// <summary>Runs one pass and records its outcome; never throws. Starts a queued follow-up.</summary>
    private async Task RunPassAsync(RunningJob job, Func<CorpusPassContext, CancellationToken, Task<string?>> pass)
    {
        CancellationToken token = _disposeCts.Token;
        try
        {
            string? message = await pass(job.Context, token).ConfigureAwait(false);
            string? failure = job.Context.FailureMessage;
            if (failure != null)
            {
                job.Complete(CorpusIndexOutcome.Failed, Limit(failure));
                _logger.LogWarning("{Corpus}: index job {Id} failed: {Message}", _corpusName, job.Id, failure);
            }
            else
            {
                job.Complete(CorpusIndexOutcome.Succeeded, Limit(message));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.Complete(CorpusIndexOutcome.Canceled, CanceledMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Corpus}: index job {Id} failed.", _corpusName, job.Id);
            job.Complete(CorpusIndexOutcome.Failed, Limit(FirstLine(ex)));
        }
        finally
        {
            Finish(job);
        }
    }

    /// <summary>Moves the job to the history, then hands the gate to a queued follow-up or releases it.</summary>
    private void Finish(RunningJob job)
    {
        var finished = job.ToJob(_time.GetUtcNow().UtcDateTime);

        Task<Task>? followUpOuter = null;
        Task? followUpTask = null;
        Func<CorpusPassContext, CancellationToken, Task<string?>>? followUpPass;
        TaskCompletionSource? completion;
        lock (_jobLock)
        {
            _recentJobs.AddFirst(finished);
            while (_recentJobs.Count > MaxRecentJobs) _recentJobs.RemoveLast();
            if (ReferenceEquals(_currentJob, job)) _currentJob = null;

            followUpPass = _pendingPass;
            completion = _pendingCompletion;
            _pendingPass = null;
            _pendingCompletion = null;

            if (followUpPass != null && !_disposed)
            {
                (followUpOuter, followUpTask) = BeginJobLocked(CorpusIndexTrigger.FollowUp, followUpPass);
            }
            else
            {
                _running = false;
            }
        }

        if (finished.Outcome != CorpusIndexOutcome.Failed)
        {
            _logger.LogInformation("{Corpus}: index job {Id} ended: {Outcome}. {Message}", _corpusName, job.Id, finished.Outcome, finished.Message);
        }

        if (followUpOuter != null && followUpTask != null)
        {
            _logger.LogInformation("{Corpus}: starting the queued follow-up pass.", _corpusName);
            followUpOuter.Start(TaskScheduler.Default);
            if (completion != null)
            {
                _ = followUpTask.ContinueWith(_ => completion.TrySetResult(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        else
        {
            completion?.TrySetResult();
        }
    }

    private static string NewJobId() => Guid.NewGuid().ToString("N").Substring(0, 12);

    private static string FirstLine(Exception ex)
    {
        string text = ex.Message;
        int end = text.IndexOfAny(new[] { '\r', '\n' });
        if (end >= 0) text = text.Substring(0, end);
        text = text.Trim();
        return text.Length > 0 ? text : ex.GetType().Name;
    }

    private static string? Limit(string? message)
    {
        if (message == null || message.Length <= MaxMessageLength) return message;
        return message.Substring(0, MaxMessageLength - 1) + "…";
    }

    /// <summary>The mutable record of the running job; <see cref="ToJob"/> copies it for the status API.</summary>
    private sealed class RunningJob
    {
        private readonly object _lock = new();
        private CorpusIndexOutcome _outcome = CorpusIndexOutcome.Running;
        private string? _message;

        public RunningJob(string id, CorpusIndexTrigger trigger, DateTime startedAtUtc)
        {
            Context = new CorpusPassContext(id, trigger);
            StartedAtUtc = startedAtUtc;
        }

        public string Id => Context.JobId;
        public CorpusPassContext Context { get; }
        public DateTime StartedAtUtc { get; }

        /// <summary>Records the outcome; the first completion wins.</summary>
        public void Complete(CorpusIndexOutcome outcome, string? message)
        {
            lock (_lock)
            {
                if (_outcome != CorpusIndexOutcome.Running) return;
                _outcome = outcome;
                _message = message;
            }
        }

        public CorpusIndexJob ToJob(DateTime? finishedAtUtc)
        {
            CorpusIndexOutcome outcome;
            string? message;
            lock (_lock)
            {
                outcome = _outcome;
                message = _message;
            }
            if (finishedAtUtc.HasValue && outcome == CorpusIndexOutcome.Running)
            {
                outcome = CorpusIndexOutcome.Failed;
            }

            return new CorpusIndexJob(
                Id,
                Context.Trigger,
                StartedAtUtc,
                finishedAtUtc,
                Context.Phase,
                Context.FilesRead,
                Context.FilesTotal,
                outcome,
                message);
        }
    }
}

/// <summary>
/// What a running pass reports about itself. Every member is lock-free and safe to call from
/// several threads; <see cref="CorpusIndexJobRunner.CurrentJob"/> reads the values as they stand.
/// </summary>
public sealed class CorpusPassContext
{
    private int _phase = (int)CorpusIndexPhase.Idle;
    private int _filesRead;
    private int _filesTotal = -1;
    private string? _failure;

    internal CorpusPassContext(string jobId, CorpusIndexTrigger trigger)
    {
        JobId = jobId;
        Trigger = trigger;
    }

    public string JobId { get; }
    public CorpusIndexTrigger Trigger { get; }

    public CorpusIndexPhase Phase => (CorpusIndexPhase)Volatile.Read(ref _phase);
    public int FilesRead => Volatile.Read(ref _filesRead);

    /// <summary>The number of files the pass reads; null while unknown.</summary>
    public int? FilesTotal
    {
        get
        {
            int total = Volatile.Read(ref _filesTotal);
            return total < 0 ? null : total;
        }
    }

    public void SetPhase(CorpusIndexPhase phase) => Volatile.Write(ref _phase, (int)phase);

    /// <summary>Sets the files read so far and the total; a null <paramref name="total"/> means not known.</summary>
    public void ReportFiles(int read, int? total)
    {
        Volatile.Write(ref _filesTotal, total is int t && t >= 0 ? t : -1);
        Volatile.Write(ref _filesRead, Math.Max(read, 0));
    }

    /// <summary>Sets the total alone, for a pass whose workers count with <see cref="IncrementFilesRead"/>.</summary>
    public void SetFilesTotal(int? total) => Volatile.Write(ref _filesTotal, total is int t && t >= 0 ? t : -1);

    /// <summary>Adds one file read, atomically; returns the new count.</summary>
    public int IncrementFilesRead() => Interlocked.Increment(ref _filesRead);

    /// <summary>
    /// Marks the pass failed with <paramref name="message"/>, without an exception: the job records
    /// <see cref="CorpusIndexOutcome.Failed"/> when the pass returns, and the string it returns is
    /// ignored. The message names configuration keys, never their values.
    /// </summary>
    public void Fail(string message) => Volatile.Write(ref _failure, string.IsNullOrWhiteSpace(message) ? "The pass failed." : message);

    internal string? FailureMessage => Volatile.Read(ref _failure);
}
