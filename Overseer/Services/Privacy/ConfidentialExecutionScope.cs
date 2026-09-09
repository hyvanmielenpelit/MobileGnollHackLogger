using System;
using System.Threading;

namespace Overseer.Services.Privacy;

/// <summary>
/// Marks the current async flow as belonging to a confidential session, so telemetry raised
/// anywhere inside it can be dropped.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AsyncLocal{T}"/> rather than a Sentry scope tag, deliberately. Tagging a scope
/// works only if a DI-registered <c>ISentryEventProcessor</c> observes scope tags *and* the SDK
/// applies scope before it runs processors — an internal ordering contract, unversioned, and
/// not something to rest a confidentiality guarantee on. This flows with every async
/// continuation regardless of who captured the execution context, and is testable without a
/// Sentry harness.
/// </para>
/// <para>
/// It exists because <c>HttpContext</c> is not enough to decide this. A turn started through
/// <c>OngoingChatManager</c> outlives its request, so a crash during streaming reaches Sentry
/// with a null or recycled context — and the processor's unauthenticated drop is guarded by
/// <c>httpContext != null</c>, which means a null context is *kept*.
/// </para>
/// </remarks>
public static class ConfidentialExecutionScope
{
    private static readonly AsyncLocal<bool> Current = new();

    /// <summary>Whether the current async flow is inside a confidential session's work.</summary>
    public static bool IsConfidential => Current.Value;

    /// <summary>
    /// Marks the flow confidential until the returned handle is disposed. Restores the previous
    /// value rather than clearing, so nesting behaves.
    /// </summary>
    public static IDisposable Enter(bool isConfidential)
    {
        bool previous = Current.Value;
        Current.Value = isConfidential || previous;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public Restore(bool previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
