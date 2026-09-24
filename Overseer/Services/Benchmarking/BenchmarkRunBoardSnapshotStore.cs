namespace Overseer.Services.Benchmarking;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

/// <summary>
/// Captures and deduplicates <see cref="BenchmarkRunBoardSnapshot"/> rows: the board text and digest
/// each benchmark run was asked and graded with.
/// </summary>
public static class BenchmarkRunBoardSnapshotStore
{
    /// <summary>
    /// Lower-case hex SHA-256 of the UTF-16LE <c>sanitizedText + "\0" + (digestText ?? "")</c>.
    /// Byte-identical to the backfill migration's <c>HASHBYTES('SHA2_256', nvarchar)</c>, which is
    /// also UTF-16LE.
    /// </summary>
    public static string ComputeSha256(string sanitizedText, string? digestText)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(sanitizedText + "\0" + (digestText ?? ""))));

    /// <summary>
    /// The stored row holding <paramref name="board"/>'s text and digest, or a new one once added. A
    /// context with no other pending changes saves at once, so a concurrent capture of the same board
    /// is resolved here by the unique index; otherwise the insert rides on the caller's save.
    /// </summary>
    public static async Task<BenchmarkRunBoardSnapshot> GetOrCreateAsync(
        ApplicationDbContext db, BenchmarkGameSnapshot board, CancellationToken ct)
    {
        string sha = ComputeSha256(board.SanitizedText, board.DigestText);

        var local = db.BenchmarkRunBoardSnapshots.Local.FirstOrDefault(s => s.Sha256 == sha);
        if (local != null)
            return local;

        var stored = await db.BenchmarkRunBoardSnapshots.FirstOrDefaultAsync(s => s.Sha256 == sha, ct);
        if (stored != null)
            return stored;

        var candidate = new BenchmarkRunBoardSnapshot
        {
            Sha256 = sha,
            SanitizedText = board.SanitizedText,
            DigestText = board.DigestText,
            CharCount = board.CharCount,
            CreatedAtUtc = DateTime.UtcNow
        };

        bool saveNow = !db.ChangeTracker.HasChanges();
        db.BenchmarkRunBoardSnapshots.Add(candidate);
        if (!saveNow)
            return candidate;

        try
        {
            await db.SaveChangesAsync(ct);
            return candidate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(candidate).State = EntityState.Detached;
            return await db.BenchmarkRunBoardSnapshots.FirstAsync(s => s.Sha256 == sha, ct);
        }
    }
}
