namespace Overseer.Services;

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Renders an exception as diagnostics text for admin-facing job logs.
/// </summary>
/// <remarks>
/// A <see cref="DbUpdateException"/> says only "See the inner exception for details", so a
/// log that records <c>ex.Message</c> alone tells the reader nothing at all. These helpers
/// unwrap the chain and add the two facts that actually identify the fault: the SQL Server
/// error number, and which entities the failed save was carrying.
///
/// Deliberately limited to exception messages, SQL error numbers, and entity type names.
/// No stack traces, no connection strings, no configuration values -- the output is copied
/// out of the Diagnostics panel and pasted into bug reports.
/// </remarks>
public static class ExceptionDetails
{
    /// <summary>Bounds a chain that loops or nests pathologically.</summary>
    private const int MaxChainDepth = 5;

    public const int DefaultMaxLength = 4000;

    /// <summary>The full exception chain, outermost first, one level per line.</summary>
    public static string Describe(Exception? ex, int maxLength = DefaultMaxLength)
    {
        if (ex == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        Exception? current = ex;

        for (int depth = 0; current != null && depth < MaxChainDepth; depth++)
        {
            if (depth > 0)
            {
                sb.AppendLine();
                sb.Append("  --> ");
            }

            sb.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current is SqlException sqlEx)
            {
                sb.Append($" (SQL error {sqlEx.Number}, state {sqlEx.State}, class {sqlEx.Class})");
            }

            if (current is DbUpdateException dbUpdateEx)
            {
                string entries = DescribeEntries(dbUpdateEx);
                if (entries.Length > 0)
                {
                    sb.AppendLine();
                    sb.Append("  Entities: ").Append(entries);
                }
            }

            current = current.InnerException;
        }

        if (current != null)
        {
            sb.AppendLine();
            sb.Append("  (inner exception chain truncated)");
        }

        return Truncate(sb.ToString(), maxLength);
    }

    /// <summary>The outermost and innermost messages on one line, for per-item error text.</summary>
    public static string DescribeShort(Exception? ex)
    {
        if (ex == null)
        {
            return string.Empty;
        }

        Exception innermost = ex;
        for (int depth = 0; innermost.InnerException != null && depth < MaxChainDepth; depth++)
        {
            innermost = innermost.InnerException;
        }

        if (ReferenceEquals(innermost, ex))
        {
            return ex.Message;
        }

        string suffix = innermost is SqlException sqlEx
            ? $" (SQL error {sqlEx.Number})"
            : string.Empty;

        return $"{ex.Message} --> {innermost.GetType().Name}: {innermost.Message}{suffix}";
    }

    /// <summary>
    /// The entity types the failed save was carrying, with their tracked states, de-duplicated.
    /// </summary>
    private static string DescribeEntries(DbUpdateException ex)
    {
        try
        {
            // Reading Entries can itself throw depending on the provider and the failure.
            var entries = ex.Entries;
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var described = new List<string>();
            foreach (var entry in entries)
            {
                string item = $"{entry.Entity.GetType().Name}[{entry.State}]";
                if (!described.Contains(item))
                {
                    described.Add(item);
                }
            }

            return string.Join(", ", described);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 0 || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
    }
}
