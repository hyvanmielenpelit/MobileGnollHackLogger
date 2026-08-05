using System.Collections.Concurrent;

namespace Overseer.Services;

public class OngoingGenerationState
{
    public ConcurrentQueue<ChatEvent> AccumulatedEvents { get; set; } = new();
    public CancellationTokenSource Cts { get; set; } = null!;
    public int EventSequence = 0;
}

public class OngoingChatManager
{
    private readonly ConcurrentDictionary<long, OngoingGenerationState> _active = new();

    public bool TryStart(long sessionId, CancellationTokenSource cts, out OngoingGenerationState state)
    {
        state = new OngoingGenerationState { Cts = cts };
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
        _active.TryRemove(sessionId, out _);
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
