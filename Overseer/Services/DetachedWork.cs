namespace Overseer.Services;

/// <summary>
/// Work that outlives the request that started it. A fault is logged and passed to <c>onFault</c>,
/// never left for the finalizer thread to report as unobserved.
/// </summary>
public static class DetachedWork
{
    /// <summary>Starts <paramref name="work"/> on the thread pool; the returned task never faults.</summary>
    public static Task Start(ILogger logger, string description, Func<Task> work, Action<Exception>? onFault = null)
        => Start(logger, description, work, ex =>
        {
            onFault?.Invoke(ex);
            return Task.CompletedTask;
        });

    /// <summary>As above, with an asynchronous <paramref name="onFault"/> that is awaited before the returned task completes.</summary>
    public static Task Start(ILogger logger, string description, Func<Task> work, Func<Exception, Task> onFault)
        => Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Detached work {Work} faulted.", description);
                try
                {
                    await onFault(ex);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Cleanup after detached work {Work} faulted.", description);
                }
            }
        });
}
