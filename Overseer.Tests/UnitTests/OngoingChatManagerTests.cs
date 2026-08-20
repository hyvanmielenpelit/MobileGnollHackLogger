using System;
using System.Threading;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests
{
    public class OngoingChatManagerTests
    {
        [Fact]
        public void TryStart_CreatesActiveState()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1001;

            var result = manager.TryStart(sessionId, cts, out var state);

            Assert.True(result);
            Assert.NotNull(state);
            Assert.False(state.IsCompleted);
            Assert.Null(state.CompletedAtUtc);
            Assert.Null(state.SavedMessageId);
            Assert.Equal(0, state.EventSequence);
            Assert.Empty(state.AccumulatedEvents);
            Assert.True(state.StartedAtUtc <= DateTime.UtcNow);
            Assert.True(state.StartedAtUtc > DateTime.UtcNow.AddSeconds(-5));
        }

        [Fact]
        public void TryStart_ReturnsFalse_WhenActiveGenerationInProgress()
        {
            var manager = new OngoingChatManager();
            var cts1 = new CancellationTokenSource();
            var cts2 = new CancellationTokenSource();
            long sessionId = 1002;

            var result1 = manager.TryStart(sessionId, cts1, out var state1);
            var result2 = manager.TryStart(sessionId, cts2, out var state2);

            Assert.True(result1);
            Assert.False(result2);
            Assert.NotNull(state1);
        }

        [Fact]
        public void ProcessEvent_IncrementsSequenceAndAccumulates()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1003;

            manager.TryStart(sessionId, cts, out var state);

            var evt1 = new ChatEvent { Type = "chunk", Data = "Hello" };
            var evt2 = new ChatEvent { Type = "chunk", Data = " World" };
            var evt3 = new ChatEvent { Type = "done", Data = "" };

            manager.ProcessEvent(sessionId, evt1);
            manager.ProcessEvent(sessionId, evt2);
            manager.ProcessEvent(sessionId, evt3);

            Assert.Equal(1, evt1.SeqNo);
            Assert.Equal(2, evt2.SeqNo);
            Assert.Equal(3, evt3.SeqNo);
            Assert.Equal(3, state.EventSequence);
            Assert.Equal(3, state.AccumulatedEvents.Count);
        }

        [Fact]
        public void Complete_MarksCompletedAndRetainsInActive()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1004;

            manager.TryStart(sessionId, cts, out _);
            manager.ProcessEvent(sessionId, new ChatEvent { Type = "chunk", Data = "Test" });
            manager.ProcessEvent(sessionId, new ChatEvent { Type = "done", Data = "" });

            manager.Complete(sessionId);

            var retrieved = manager.TryGet(sessionId);
            Assert.NotNull(retrieved);
            Assert.True(retrieved.IsCompleted);
            Assert.NotNull(retrieved.CompletedAtUtc);
            Assert.True(retrieved.CompletedAtUtc <= DateTime.UtcNow);
            Assert.Equal(2, retrieved.AccumulatedEvents.Count);
        }

        [Fact]
        public void TryStart_ReplacesCompletedStateForSameSession()
        {
            var manager = new OngoingChatManager();
            var cts1 = new CancellationTokenSource();
            long sessionId = 1005;

            manager.TryStart(sessionId, cts1, out var state1);
            manager.ProcessEvent(sessionId, new ChatEvent { Type = "done", Data = "" });
            manager.Complete(sessionId);

            Assert.True(manager.TryGet(sessionId)?.IsCompleted);

            var cts2 = new CancellationTokenSource();
            var result2 = manager.TryStart(sessionId, cts2, out var state2);

            Assert.True(result2);
            Assert.NotNull(state2);
            Assert.False(state2.IsCompleted);
            Assert.NotSame(state1, state2);
        }

        [Fact]
        public void TryStart_CleansUpStaleCompletedStates()
        {
            var manager = new OngoingChatManager();
            var ctsStale = new CancellationTokenSource();
            long staleSessionId = 1006;

            manager.TryStart(staleSessionId, ctsStale, out var staleState);
            manager.Complete(staleSessionId);
            // Artificially make the completion time 35 seconds in the past
            staleState.CompletedAtUtc = DateTime.UtcNow.AddSeconds(-35);

            var ctsFresh = new CancellationTokenSource();
            long freshSessionId = 1007;
            manager.TryStart(freshSessionId, ctsFresh, out var freshState);
            manager.Complete(freshSessionId);

            // Now trigger TryStart on a new session, which performs lazy pruning
            var ctsNew = new CancellationTokenSource();
            long newSessionId = 1008;
            var result = manager.TryStart(newSessionId, ctsNew, out _);

            Assert.True(result);
            // Stale session (>30s old) should have been pruned
            Assert.Null(manager.TryGet(staleSessionId));
            // Fresh session (<30s old) should still be retained
            Assert.NotNull(manager.TryGet(freshSessionId));
        }

        [Fact]
        public void TryCancelAndRemove_CancelsCtsAndRemovesImmediately()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1009;

            manager.TryStart(sessionId, cts, out _);
            Assert.False(cts.IsCancellationRequested);

            var result = manager.TryCancelAndRemove(sessionId);

            Assert.True(result);
            Assert.True(cts.IsCancellationRequested);
            Assert.Null(manager.TryGet(sessionId));
        }

        [Fact]
        public void Fail_AddsErrorEventAndMarksComplete()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1010;

            manager.TryStart(sessionId, cts, out _);
            manager.Fail(sessionId, "Connection timed out");

            var state = manager.TryGet(sessionId);
            Assert.NotNull(state);
            Assert.True(state.IsCompleted);
            Assert.Single(state.AccumulatedEvents);

            state.AccumulatedEvents.TryPeek(out var evt);
            Assert.NotNull(evt);
            Assert.Equal("error", evt.Type);
            Assert.Equal("Connection timed out", evt.Data);
        }

        [Fact]
        public void SavedMessageId_CanBeRecordedAndRetrieved()
        {
            var manager = new OngoingChatManager();
            var cts = new CancellationTokenSource();
            long sessionId = 1011;

            manager.TryStart(sessionId, cts, out var state);
            state.SavedMessageId = 4294967296L; // 64-bit ID

            var retrieved = manager.TryGet(sessionId);
            Assert.NotNull(retrieved);
            Assert.Equal(4294967296L, retrieved.SavedMessageId);
        }
    }
}
