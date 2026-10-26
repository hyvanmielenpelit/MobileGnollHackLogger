using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>Records every host log message, at every level, for leak assertions.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary><c>category: message</c>, followed by the exception's text when one was logged.</summary>
    public IReadOnlyList<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string message = category + ": " + formatter(state, exception);
            if (exception != null)
            {
                message += Environment.NewLine + exception;
            }
            messages.Enqueue(message);
        }
    }
}
