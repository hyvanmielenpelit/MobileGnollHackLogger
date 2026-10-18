using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Legal;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Overseer.Tests.Legal;

/// <summary>The required version, a user's status, and recording an acceptance.</summary>
public class TermsAcceptanceStoreTests : IDisposable
{
    private const string UserId = "terms-user-1";
    private readonly TempWiki _wiki = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _wiki.Dispose();

    [Fact]
    public async Task TheRequiredVersionIsTheDocumentsOwn()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));

        Assert.Equal("2026-10-18", await store.GetRequiredVersionAsync(LegalDocumentKeys.OverseerTerms, Ct));
    }

    [Fact]
    public async Task WhenTheFileIsMissingTheRequiredVersionIsTheNewestAccepted()
    {
        using var db = LegalTestSupport.Db();
        await LegalTestSupport.AddAcceptanceAsync(db, "a", "2026-10-18.2");
        await LegalTestSupport.AddAcceptanceAsync(db, "b", "2026-10-18.10");
        await LegalTestSupport.AddAcceptanceAsync(db, "c", "2026-10-18");
        await LegalTestSupport.AddAcceptanceAsync(db, "d", "2027-01-01", LegalDocumentKeys.AccountTerms);
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));

        Assert.Equal("2026-10-18.10", await store.GetRequiredVersionAsync(LegalDocumentKeys.OverseerTerms, Ct));

        var status = await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct);
        Assert.False(status.IsAccepted);
        Assert.Equal("2026-10-18.10", status.RequiredVersion);
    }

    [Fact]
    public async Task WithNeitherAFileNorAnyRowsTheGateIsOff()
    {
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));

        Assert.Null(await store.GetRequiredVersionAsync(LegalDocumentKeys.OverseerTerms, Ct));
        var status = await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct);
        Assert.True(status.IsAccepted);
        Assert.Null(status.RequiredVersion);
        Assert.Equal(TermsRecordResult.StaleVersion,
            await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceWeb, null, null, Ct));
    }

    [Fact]
    public async Task AStaleVersionRecordsNothing()
    {
        _wiki.Write("**Last updated:** 18 October 2026 (revision 2)\n");
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));

        var result = await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceGnollHackApp, null, "4.2.0", Ct);

        Assert.Equal(TermsRecordResult.StaleVersion, result);
        Assert.Empty(await db.UserTermsAcceptances.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARecordHoldsWhoWhenWhatAndHow()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));
        string hash = new string('a', 64);

        var result = await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceWeb, hash.ToUpperInvariant(), null, Ct);

        Assert.Equal(TermsRecordResult.Recorded, result);
        var row = Assert.Single(await db.UserTermsAcceptances.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(UserId, row.AspNetUserId);
        Assert.Equal(LegalDocumentKeys.OverseerTerms, row.DocumentKey);
        Assert.Equal("2026-10-18", row.Version);
        Assert.Equal(hash, row.ContentSha256);
        Assert.Equal(LegalDocumentKeys.SourceWeb, row.Source);
        Assert.InRange(row.AcceptedUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task ADuplicateIsAlreadyAccepted()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));

        Assert.Equal(TermsRecordResult.Recorded,
            await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceWeb, null, null, Ct));
        Assert.Equal(TermsRecordResult.AlreadyAccepted,
            await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceGnollHackApp, null, null, Ct));

        Assert.Single(await db.UserTermsAcceptances.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheStatusCacheIsEvictedOnAccept()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var cache = LegalTestSupport.Cache();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path), cache);

        Assert.False((await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct)).IsAccepted);

        // Written past the store: the cached status still says not accepted.
        await LegalTestSupport.AddAcceptanceAsync(db, UserId, "2026-10-18");
        Assert.False((await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct)).IsAccepted);

        // Recording through the store evicts it.
        Assert.Equal(TermsRecordResult.AlreadyAccepted,
            await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceWeb, null, null, Ct));
        var status = await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct);
        Assert.True(status.IsAccepted);
        Assert.Equal("2026-10-18", status.AcceptedVersion);
        Assert.Equal(LegalDocumentKeys.SourceWeb, status.AcceptedSource);
    }

    [Fact]
    public async Task ANewRequiredVersionIsNotHiddenByTheCache()
    {
        string file = _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var store = LegalTestSupport.Store(db, LegalTestSupport.Reader(_wiki.Path));
        await store.RecordAsync(UserId, LegalDocumentKeys.OverseerTerms, "2026-10-18", LegalDocumentKeys.SourceWeb, null, null, Ct);
        Assert.True((await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct)).IsAccepted);

        System.IO.File.WriteAllText(file, "**Last updated:** 18 October 2026 (revision 2)\n");
        System.IO.File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

        var status = await store.GetStatusAsync(UserId, LegalDocumentKeys.OverseerTerms, Ct);
        Assert.False(status.IsAccepted);
        Assert.Equal("2026-10-18.2", status.RequiredVersion);
        Assert.Equal("2026-10-18", status.AcceptedVersion);
    }
}
