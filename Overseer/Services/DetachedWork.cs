namespace Overseer.Services;

/// <summary>
/// Work that outlives the request that started it. A fault is logged and passed to <c>onFault</c>,
/// never left for the finalizer thread to report as unobserved.
/// </summary>
public static class DetachedWork
{
    /// <summary>Starts <paramref name="work"/> on the thread pool; the returned task never faults.</summary>
    public static Task Start(ILogger logger, string description, Func<Task> work, Action<Exception>? onFault = null)
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
                    onFault?.Invoke(ex);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Cleanup after detached work {Work} faulted.", description);
                }
            }
        });
}
