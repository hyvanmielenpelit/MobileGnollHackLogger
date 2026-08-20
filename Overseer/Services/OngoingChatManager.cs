using System.Collections.Concurrent;

namespace Overseer.Services;

public class OngoingGenerationState
{
    public ConcurrentQueue<ChatEvent> AccumulatedEvents { get; set; } = new();
    public CancellationTokenSource Cts { get; set; } = null!;
    public int EventSequence = 0;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAtUtc { get; set; } = null;
    public long? SavedMessageId { get; set; } = null;
}

public class OngoingChatManager
{
    private readonly ConcurrentDictionary<long, OngoingGenerationState> _active = new();

    public bool TryStart(long sessionId, CancellationTokenSource cts, out OngoingGenerationState state)
    {
        // Lazy cleanup of stale completed entries (>30 seconds old)
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        foreach (var kvp in _active)
        {
            if (kvp.Value.IsCompleted && kvp.Value.CompletedAtUtc.HasValue && kvp.Value.CompletedAtUtc.Value < cutoff)
            {
                _active.TryRemove(kvp.Key, out _);
            }
        }

        // If existing session is already completed, allow replacing it
        if (_active.TryGetValue(sessionId, out var existing) && existing.IsCompleted)
        {
            _active.TryRemove(sessionId, out _);
        }

        state = new OngoingGenerationState { Cts = cts, StartedAtUtc = DateTime.UtcNow };
        return _active.TryAdd(sessionId, state);
    }

    public void ProcessEvent(long sessionId, ChatEvent evt)
    {
        if (_active.TryGetValue(sessionId, out var state))
        {
            evt.SeqNo = Interlocked.Increment(ref state.EventSequence);
            state.AccumulatedEvents.Enqueue(evt);
        }
    }

    public void Complete(long sessionId)
    {
        if (_active.TryGetValue(sessionId, out var state))
        {
            state.IsCompleted = true;
            state.CompletedAtUtc = DateTime.UtcNow;
        }
    }

    public void Fail(long sessionId, string error)
    {
        if (_active.TryGetValue(sessionId, out var state))
        {
            state.AccumulatedEvents.Enqueue(new ChatEvent { Type = "error", Data = error });
        }
        Complete(sessionId);
    }

    public OngoingGenerationState? TryGet(long sessionId)
    {
        if (_active.TryGetValue(sessionId, out var state))
        {
            return state;
        }
        return null;
    }

    public bool TryCancelAndRemove(long sessionId)
    {
        if (_active.TryRemove(sessionId, out var state))
        {
            try
            {
                state.Cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            return true;
        }
        return false;
    }
}
