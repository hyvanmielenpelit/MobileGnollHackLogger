using System;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;

namespace Overseer.Tests.Helpers;

/// <summary>
/// An in-memory SQLite database holding the <see cref="ApplicationDbContext"/> schema, created once per test run,
/// that the SQLite-backed test hosts copy instead of each running <c>EnsureCreated</c>. The template is built with
/// the same provider and Identity registration the hosts use, so a copy has the schema they would create.
/// </summary>
internal static class SqliteSchemaTemplate
{
    private static readonly object BackupLock = new();

    private static readonly Lazy<SqliteConnection> Template = new(CreateTemplate, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// A new, open in-memory connection holding a private copy of the schema. The caller owns the connection and
    /// keeps it open for the database's lifetime.
    /// </summary>
    internal static SqliteConnection OpenCopy()
    {
        var copy = new SqliteConnection("Filename=:memory:");
        try
        {
            copy.Open();
            lock (BackupLock)
            {
                Template.Value.BackupDatabase(copy);
            }
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static SqliteConnection CreateTemplate()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        }
        return connection;
    }
}
