using System.Data;
using Microsoft.Data.Sqlite;

namespace Overseer.Tests.Helpers;

/// <summary>
/// An in-memory SQLite connection whose commands cancel the supplied source and throw
/// <see cref="CanceledQueryException"/> when <c>when</c> matches their text. It reaches the raw commands a
/// service builds with <c>GetDbConnection().CreateCommand()</c>, which <see cref="CanceledQueryInterceptor"/>
/// never sees. Open it, then give it the schema with <see cref="SqliteSchemaTemplate.CopyInto"/>.
/// </summary>
internal sealed class CancelingSqliteConnection : SqliteConnection
{
    private readonly CancellationTokenSource _source;
    private readonly Func<string, bool> _when;

    public CancelingSqliteConnection(CancellationTokenSource source, Func<string, bool> when)
        : base("Filename=:memory:")
    {
        _source = source;
        _when = when;
    }

    /// <summary>True once a command has canceled the source and thrown.</summary>
    public bool Fired { get; private set; }

    public override SqliteCommand CreateCommand()
        => new CancelingCommand(this) { Connection = this, CommandTimeout = DefaultTimeout, Transaction = Transaction };

    private sealed class CancelingCommand : SqliteCommand
    {
        private readonly CancelingSqliteConnection _owner;

        public CancelingCommand(CancelingSqliteConnection owner)
        {
            _owner = owner;
        }

        public override Task<SqliteDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            if (!_owner.Fired && _owner._when(CommandText))
            {
                _owner.Fired = true;
                _owner._source.Cancel();
                throw new CanceledQueryException();
            }
            return base.ExecuteReaderAsync(behavior, cancellationToken);
        }
    }
}
