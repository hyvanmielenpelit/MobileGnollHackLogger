namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The content-addressed store of the boards runs were made with. The in-memory provider enforces no
/// unique index, so the concurrent-insert branch is not exercised here.
/// </summary>
public class BenchmarkRunBoardSnapshotStoreTests
{
    private const string BoardText = "Dungeon Level 3\nHP: 12/60\na - a blessed +1 quarterstaff (weapon in hands)";

    private static BenchmarkGameSnapshot Board(string? digest = "HP 12/60; quarterstaff") => new()
    {
        Name = "Board",
        SanitizedText = BoardText,
        DigestText = digest,
        CharCount = BoardText.Length,
        Sha256 = "board-sha",
        CaptureMethod = "Upload"
    };

    [Fact]
    public async Task TheSameTextAndDigest_GiveOneRow_AndTheSameId()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();

        long firstId;
        await using (var db = new ApplicationDbContext(options))
        {
            firstId = (await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board(), ct)).Id;
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var second = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board(), ct);
            Assert.Equal(firstId, second.Id);
            Assert.Equal(1, await db.BenchmarkRunBoardSnapshots.CountAsync(ct));
            Assert.Equal(BoardText, second.SanitizedText);
            Assert.Equal(BoardText.Length, second.CharCount);
        }
    }

    [Fact]
    public async Task TheSameTextInOneContext_IsFoundLocally_BeforeItIsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());

        // A pending change keeps the first capture from saving at once, as it does inside a launch.
        db.BenchmarkSuites.Add(new BenchmarkSuite { Name = "Pending" });
        var first = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board(), ct);
        var second = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board(), ct);

        Assert.Same(first, second);
        Assert.Equal(EntityState.Added, db.Entry(first).State);
    }

    [Fact]
    public async Task ADifferentDigest_GivesASecondRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());

        var withDigest = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board("digest one"), ct);
        var otherDigest = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board("digest two"), ct);
        var noDigest = await BenchmarkRunBoardSnapshotStore.GetOrCreateAsync(db, Board(null), ct);

        Assert.Equal(3, new[] { withDigest.Id, otherDigest.Id, noDigest.Id }.Distinct().Count());
        Assert.Equal(3, await db.BenchmarkRunBoardSnapshots.CountAsync(ct));
        Assert.Null(noDigest.DigestText);
    }

    [Fact]
    public void TheKey_IsTheLowerCaseSha256_OfTheUtf16LeTextNulDigest()
    {
        // Computed here independently of the store, the way the backfill migration's
        // HASHBYTES('SHA2_256', CAST(nvarchar AS varbinary)) computes it.
        string expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.Unicode.GetBytes("board text\u0000the digest")));

        Assert.Equal(expected, BenchmarkRunBoardSnapshotStore.ComputeSha256("board text", "the digest"));
        Assert.Equal("139b046b4f33a311c26c42bcb67d492334d05973e957dc678eb59917dc72e039", expected);

        // A null digest hashes as the empty one.
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes("board text\u0000"))),
            BenchmarkRunBoardSnapshotStore.ComputeSha256("board text", null));
    }
}
