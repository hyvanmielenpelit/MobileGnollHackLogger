using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services;

public class OngoingGenerationState
{
    public ConcurrentQueue<ChatEvent> AccumulatedEvents { get; set; } = new();
    public ConcurrentDictionary<string, CancellationTokenSource> ActiveSubAgents { get; } = new();
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
    private readonly int _maxAccumulatedEvents;

    public OngoingChatManager(IConfiguration? configuration = null)
    {
        _maxAccumulatedEvents = configuration?.GetValue<int>("SubAgentSettings:MaxAccumulatedEvents", 5000) ?? 5000;
    }

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

            // Skip accumulation for debug events to keep reconnect payload lightweight
            if (string.Equals(evt.Type, "debug", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            state.AccumulatedEvents.Enqueue(evt);

            // Bound queue to prevent memory leak
            while (state.AccumulatedEvents.Count > _maxAccumulatedEvents && state.AccumulatedEvents.TryDequeue(out _))
            {
            }
        }
    }

    public bool TryRegisterSubAgent(long sessionId, string toolCallId, CancellationTokenSource cts)
    {
        if (_active.TryGetValue(sessionId, out var state) && !state.IsCompleted)
        {
            return state.ActiveSubAgents.TryAdd(toolCallId, cts);
        }
        return false;
    }

    public void UnregisterSubAgent(long sessionId, string toolCallId)
    {
        if (_active.TryGetValue(sessionId, out var state))
        {
            state.ActiveSubAgents.TryRemove(toolCallId, out _);
        }
    }

    public bool TryCancelSubAgent(long sessionId, string toolCallId)
    {
        if (_active.TryGetValue(sessionId, out var state)
            && state.ActiveSubAgents.TryRemove(toolCallId, out var cts))
        {
            try
            {
                cts.Cancel();
                return true;
            }
            catch (ObjectDisposedException) { }
        }
        return false;
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
