using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Overseer.Services;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="DetachedWork.Start"/>: a fault in the work, or in its <c>onFault</c> handler, is logged and never escapes
/// the returned task, where it would surface as an unobserved task exception.
/// </summary>
public sealed class DetachedWorkTests
{
    /// <summary>Keeps every entry it is given.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, exception));
            }
        }
    }

    [Fact]
    public async Task AFaultIsLoggedAndPassedToOnFault()
    {
        var logger = new ListLogger();
        var thrown = new InvalidOperationException("boom");
        Exception? seen = null;

        var task = DetachedWork.Start(logger, "test work", () => throw thrown, ex => seen = ex);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(thrown, entry.Exception);
        Assert.Same(thrown, seen);
    }

    [Fact]
    public async Task AThrowingOnFaultIsLoggedAndDoesNotEscape()
    {
        var logger = new ListLogger();
        var cleanupFault = new InvalidOperationException("cleanup");

        var task = DetachedWork.Start(logger, "test work",
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            (Action<Exception>)(_ => throw cleanupFault));
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Error, e.Level));
        Assert.Same(cleanupFault, logger.Entries[1].Exception);
    }

    [Fact]
    public async Task AnAsynchronousOnFaultIsAwaitedAndItsFaultLogged()
    {
        var logger = new ListLogger();
        var cleanupFault = new InvalidOperationException("cleanup");
        bool handlerRan = false;

        var task = DetachedWork.Start(logger, "test work",
            () => throw new InvalidOperationException("boom"),
            async _ =>
            {
                await Task.Yield();
                handlerRan = true;
                throw cleanupFault;
            });
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(handlerRan);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Error, e.Level));
        Assert.Same(cleanupFault, logger.Entries[1].Exception);
    }

    [Fact]
    public async Task SucceedingWorkLogsNothingAndSkipsOnFault()
    {
        var logger = new ListLogger();
        bool ran = false;
        bool faultHandled = false;

        await DetachedWork.Start(logger, "test work", () =>
        {
            ran = true;
            return Task.CompletedTask;
        }, _ => faultHandled = true);

        Assert.True(ran);
        Assert.False(faultHandled);
        Assert.Empty(logger.Entries);
    }
}
