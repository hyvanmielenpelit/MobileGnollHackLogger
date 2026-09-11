namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// The run-level stage a run is executing. <see cref="Answering"/> covers answering, per-question
/// assessment, and the claim verification and second opinion that follow each assessment, because
/// they are pipelined — a question is assessed, verified and second-guessed while the next one is
/// answered — so no instant of the run belongs to only one of them. <see cref="Verifying"/> and
/// <see cref="SecondOpinion"/> are the run-level follow-up passes that start only once every
/// answer is graded; they catch what the per-answer path left, mainly contested verdicts and
/// critical-error splits. <see cref="Verifying"/> is marked twice, before and after the
/// second-opinion pass, so these values are not a monotonic sequence.
/// </summary>
public enum BenchmarkRunStage
{
    Answering = 0,
    Verifying = 1,
    SecondOpinion = 2,
    Synthesizing = 3,
    Terminal = 4
}

public class BenchmarkRunState
{
    public long RunId { get; set; }
    public CancellationTokenSource Cts { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Which run-level stage is executing. In-process only, like everything else here: a run whose
    /// process restarts reports none, and the client falls back to deriving it from the answer rows.
    /// </summary>
    public BenchmarkRunStage Stage { get; set; } = BenchmarkRunStage.Answering;

    /// <summary>
    /// Order indexes of the questions whose request is currently in flight to the provider.
    /// The executor writes a <see cref="MobileGnollHackLogger.Data.BenchmarkRunAnswer"/> only
    /// after the model replies, so this is the only place an in-flight question is visible.
    /// The progress dialog reads it to tell "not dispatched yet" from "answering".
    /// </summary>
    public ConcurrentDictionary<int, byte> InFlightQuestions { get; } = new();

    /// <summary>
    /// Order indexes currently with the claim verifier. Same contract as
    /// <see cref="InFlightQuestions"/>: an already-scored row is re-graded in place, so without this
    /// the dialog shows it as finished throughout the minutes it is being re-read.
    /// </summary>
    public ConcurrentDictionary<int, byte> InFlightVerification { get; } = new();

    /// <summary>Order indexes currently with the second-opinion assessor.</summary>
    public ConcurrentDictionary<int, byte> InFlightSecondOpinion { get; } = new();

    /// <summary>
    /// Order indexes a failed-question or single-question re-run is repairing; empty for a first run.
    /// A re-run overwrites answer rows in place and adds none, so the suite totals say nothing about
    /// its progress and the client scopes its meters to this set instead.
    /// </summary>
    public IReadOnlyList<int> RerunScopeOrderIndexes { get; set; } = Array.Empty<int>();

    /// <summary>Scope members whose re-executed answer row has been saved.</summary>
    public ConcurrentDictionary<int, byte> RerunAnswered { get; } = new();

    /// <summary>Scope members whose per-question assessment has returned, scored or failed.</summary>
    public ConcurrentDictionary<int, byte> RerunScored { get; } = new();
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
                _currentRun.Stage = BenchmarkRunStage.Terminal;
                _currentRun.InFlightQuestions.Clear();
                _currentRun.InFlightVerification.Clear();
                _currentRun.InFlightSecondOpinion.Clear();
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
    /// Records which run-level stage is executing. Ignored when the run is not the current,
    /// still-running one, so a stale caller cannot move a finished run off
    /// <see cref="BenchmarkRunStage.Terminal"/>.
    /// </summary>
    public void MarkStage(long runId, BenchmarkRunStage stage)
    {
        var state = TryGetRunning(runId);
        if (state != null)
        {
            state.Stage = stage;
        }
    }

    /// <summary>The current run's stage, or null when this run is not the current one.</summary>
    public BenchmarkRunStage? GetStage(long runId) => TryGetRunning(runId)?.Stage;

    /// <summary>
    /// Records that the question's request has been sent to the provider. Ignored when the run
    /// is not the current one, so a stale caller can never resurrect finished state.
    /// </summary>
    public void MarkQuestionInFlight(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.InFlightQuestions.TryAdd(orderIndex, 0);
    }

    /// <summary>Records that the answer is with the claim verifier.</summary>
    public void MarkVerificationInFlight(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.InFlightVerification.TryAdd(orderIndex, 0);
    }

    /// <summary>
    /// Clears the verification mark. Called from a <c>finally</c>, on the same discipline as
    /// <see cref="ClearQuestionInFlight"/>.
    /// </summary>
    public void ClearVerificationInFlight(long runId, int orderIndex)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId)
            {
                _currentRun.InFlightVerification.TryRemove(orderIndex, out _);
            }
        }
    }

    /// <summary>Records that the answer is with the second-opinion assessor.</summary>
    public void MarkSecondOpinionInFlight(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.InFlightSecondOpinion.TryAdd(orderIndex, 0);
    }

    /// <summary>Clears the second-opinion mark. Called from a <c>finally</c>.</summary>
    public void ClearSecondOpinionInFlight(long runId, int orderIndex)
    {
        lock (_lock)
        {
            if (_currentRun != null && _currentRun.RunId == runId)
            {
                _currentRun.InFlightSecondOpinion.TryRemove(orderIndex, out _);
            }
        }
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

    /// <summary>The answers currently with the claim verifier, ascending. Same contract as
    /// <see cref="GetInFlightQuestions"/>.</summary>
    public IReadOnlyList<int> GetInFlightVerification(long runId)
    {
        var state = TryGetRunning(runId);
        return state == null
            ? Array.Empty<int>()
            : state.InFlightVerification.Keys.OrderBy(i => i).ToList();
    }

    /// <summary>The answers currently with the second-opinion assessor, ascending.</summary>
    public IReadOnlyList<int> GetInFlightSecondOpinion(long runId)
    {
        var state = TryGetRunning(runId);
        return state == null
            ? Array.Empty<int>()
            : state.InFlightSecondOpinion.Keys.OrderBy(i => i).ToList();
    }

    /// <summary>
    /// Records which order indexes this re-run is repairing. Ignored when the run is not the
    /// current, still-running one, on the same discipline as <see cref="MarkQuestionInFlight"/>.
    /// </summary>
    public void SetRerunScope(long runId, IEnumerable<int> orderIndexes)
    {
        var state = TryGetRunning(runId);
        if (state != null)
        {
            state.RerunScopeOrderIndexes = orderIndexes.Distinct().OrderBy(i => i).ToList();
        }
    }

    /// <summary>Records that a scope member's re-executed answer row has been saved.</summary>
    public void MarkRerunAnswered(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.RerunAnswered.TryAdd(orderIndex, 0);
    }

    /// <summary>Records that a scope member's per-question assessment has returned.</summary>
    public void MarkRerunScored(long runId, int orderIndex)
    {
        var state = TryGetRunning(runId);
        state?.RerunScored.TryAdd(orderIndex, 0);
    }

    /// <summary>
    /// The order indexes this run's re-run is repairing, ascending. Read through
    /// <see cref="TryGet"/> rather than <see cref="TryGetRunning"/>, so a finished re-run still
    /// reports the scope it covered instead of the progress meters snapping back to suite totals.
    /// A new run replaces the state, which is what drops the previous run's scope.
    /// </summary>
    public IReadOnlyList<int> GetRerunScope(long runId)
    {
        var state = TryGet(runId);
        return state == null
            ? Array.Empty<int>()
            : state.RerunScopeOrderIndexes.OrderBy(i => i).ToList();
    }

    /// <summary>The scope members already re-answered, ascending. Same contract as
    /// <see cref="GetRerunScope"/>.</summary>
    public IReadOnlyList<int> GetRerunAnswered(long runId)
    {
        var state = TryGet(runId);
        return state == null
            ? Array.Empty<int>()
            : state.RerunAnswered.Keys.OrderBy(i => i).ToList();
    }

    /// <summary>The scope members already re-assessed, ascending.</summary>
    public IReadOnlyList<int> GetRerunScored(long runId)
    {
        var state = TryGet(runId);
        return state == null
            ? Array.Empty<int>()
            : state.RerunScored.Keys.OrderBy(i => i).ToList();
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
