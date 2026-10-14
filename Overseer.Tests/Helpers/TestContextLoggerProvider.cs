using System;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Overseer.Tests.Helpers
{
    /// <summary>
    /// Writes log lines to the output of the xUnit test that is currently running, so a test host's log
    /// appears only with that test's result. Lines logged outside a test, or after it ended, are dropped.
    /// </summary>
    public sealed class TestContextLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestContextLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class TestContextLogger : ILogger
        {
            private readonly string _category;

            public TestContextLogger(string category)
            {
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var output = TestContext.Current.TestOutputHelper;
                if (output == null) return;

                try
                {
                    output.WriteLine($"{logLevel}: {_category}: {formatter(state, exception)}");
                    if (exception != null) output.WriteLine(exception.ToString());
                }
                catch (InvalidOperationException)
                {
                    // The test has already finished.
                }
            }
        }
    }
}
