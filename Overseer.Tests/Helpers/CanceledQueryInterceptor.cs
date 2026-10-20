using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Overseer.Tests.Helpers;

/// <summary>
/// What SqlClient throws for a query its token canceled mid-execution: a provider exception, not an
/// <see cref="System.OperationCanceledException"/>.
/// </summary>
internal sealed class CanceledQueryException : DbException
{
    internal const string SqlClientMessage =
        "A severe error occurred on the current command. The results, if any, should be discarded. Operation cancelled by user.";

    public CanceledQueryException() : base(SqlClientMessage)
    {
    }
}

/// <summary>
/// Simulates SqlClient canceling a query that is already executing. Once armed, the Nth command after arming cancels
/// the supplied source and throws <see cref="CanceledQueryException"/>. Interceptors run only on relational
/// providers, so the context must be SQLite (see <see cref="SqliteSchemaTemplate"/>).
/// </summary>
internal sealed class CanceledQueryInterceptor : DbCommandInterceptor
{
    private readonly CancellationTokenSource _source;
    private int _fireOn;
    private int _seen;

    public CanceledQueryInterceptor(CancellationTokenSource source)
    {
        _source = source;
    }

    /// <summary>True once the interceptor has canceled the source and thrown.</summary>
    public bool Fired { get; private set; }

    /// <summary>Fires on the <paramref name="onCommand"/>th command from now (1 = the next one).</summary>
    public void Arm(int onCommand = 1)
    {
        _seen = 0;
        _fireOn = onCommand;
        Fired = false;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Intercept();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Intercept();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Intercept();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Intercept();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Intercept();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Intercept();
        return ValueTask.FromResult(result);
    }

    private void Intercept()
    {
        if (_fireOn <= 0 || Fired) return;
        if (Interlocked.Increment(ref _seen) != _fireOn) return;
        Fired = true;
        _source.Cancel();
        throw new CanceledQueryException();
    }
}

/// <summary>
/// The in-memory provider's stand-in for <see cref="CanceledQueryInterceptor"/>, which only relational providers run:
/// once armed, the first materialized entity that <c>when</c> matches cancels the supplied source and throws
/// <see cref="CanceledQueryException"/> out of the query.
/// </summary>
internal sealed class CanceledMaterializationInterceptor : IMaterializationInterceptor
{
    private readonly CancellationTokenSource _source;
    private readonly System.Func<object, bool> _when;

    public CanceledMaterializationInterceptor(CancellationTokenSource source, System.Func<object, bool> when)
    {
        _source = source;
        _when = when;
    }

    public bool Armed { get; set; }

    /// <summary>True once the interceptor has canceled the source and thrown.</summary>
    public bool Fired { get; private set; }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (Armed && !Fired && _when(entity))
        {
            Fired = true;
            _source.Cancel();
            throw new CanceledQueryException();
        }
        return entity;
    }
}
