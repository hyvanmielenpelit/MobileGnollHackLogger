using System.Data.Common;

namespace Overseer.Services;

/// <summary>
/// Recognizes an exception that a canceled token caused. SqlClient reports a query canceled
/// mid-execution as a <c>SqlException</c> ("Operation cancelled by user"), EF Core wraps one from
/// <c>SaveChanges</c> in <c>DbUpdateException</c>, and Kestrel reports an aborted request body as an
/// <see cref="IOException"/> -- none of them an <see cref="OperationCanceledException"/>.
/// </summary>
public static class CancellationExceptionExtensions
{
    private const int MaxChainDepth = 5;

    /// <summary>
    /// True when <paramref name="token"/> has been canceled and the exception chain carries an
    /// <see cref="OperationCanceledException"/>, a <see cref="DbException"/> or an <see cref="IOException"/>.
    /// </summary>
    public static bool IsCanceledBy(this Exception ex, CancellationToken token) =>
        token.IsCancellationRequested && IsCancellationShaped(ex, 0);

    private static bool IsCancellationShaped(Exception? ex, int depth)
    {
        if (ex == null || depth >= MaxChainDepth) return false;
        if (ex is OperationCanceledException or DbException or IOException) return true;
        if (ex is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(inner => IsCancellationShaped(inner, depth + 1));
        return IsCancellationShaped(ex.InnerException, depth + 1);
    }
}
