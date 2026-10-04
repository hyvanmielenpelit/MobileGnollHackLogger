using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class CorpusContentFingerprintTests
{
    [Fact]
    public void InsertionOrder_DoesNotMatter()
    {
        var first = new CorpusContentFingerprintBuilder();
        first.Add("Races/Gnoll.md", "Gnolls are canine humanoids.");
        first.Add("Items/Wand.md", "A wand has charges.");
        first.Add("Index.md", "Welcome.");

        var second = new CorpusContentFingerprintBuilder();
        second.Add("Index.md", "Welcome.");
        second.Add("Races/Gnoll.md", "Gnolls are canine humanoids.");
        second.Add("Items/Wand.md", "A wand has charges.");

        var a = first.Build();
        var b = second.Build();

        Assert.Equal(a.Sha256, b.Sha256);
        Assert.Equal(3, a.FileCount);
    }

    [Fact]
    public void BackslashAndSlash_GiveTheSameManifest()
    {
        var backslash = new CorpusContentFingerprintBuilder();
        backslash.Add(@"src\monst.c", "struct permonst mons[];");

        var slash = new CorpusContentFingerprintBuilder();
        slash.Add("src/monst.c", "struct permonst mons[];");

        Assert.Equal(backslash.Build().Sha256, slash.Build().Sha256);
    }

    [Fact]
    public void EditedFile_ChangesTheHash()
    {
        var before = new CorpusContentFingerprintBuilder();
        before.Add("Races/Gnoll.md", "Gnolls are canine humanoids.");
        before.Add("Index.md", "Welcome.");

        var after = new CorpusContentFingerprintBuilder();
        after.Add("Races/Gnoll.md", "Gnolls are canine humanoids with flails.");
        after.Add("Index.md", "Welcome.");

        Assert.NotEqual(before.Build().Sha256, after.Build().Sha256);
    }

    [Fact]
    public void EmptyCorpus_GivesAStableHash_WithNoFiles()
    {
        var first = new CorpusContentFingerprintBuilder().Build();
        var second = new CorpusContentFingerprintBuilder().Build();

        Assert.Equal(0, first.FileCount);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(Hex(SHA256.HashData(Array.Empty<byte>())), first.Sha256);
    }

    [Fact]
    public void Manifest_IsSortedPathColonFileHashLines()
    {
        var builder = new CorpusContentFingerprintBuilder();
        builder.Add("b.md", "two");
        builder.Add("a.md", "one");

        string manifest = $"a.md:{Hex(SHA256.HashData(Encoding.UTF8.GetBytes("one")))}\n"
                        + $"b.md:{Hex(SHA256.HashData(Encoding.UTF8.GetBytes("two")))}\n";

        Assert.Equal(Hex(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))), builder.Build().Sha256);
    }

    [Fact]
    public void ProviderParse_RoundTripsTheSerializedSnapshot()
    {
        var wiki = new CorpusContentFingerprint(new string('a', 64), 12, new System.DateTime(2026, 10, 4, 12, 0, 0, System.DateTimeKind.Utc));
        var snapshot = new Dictionary<string, CorpusContentFingerprint?>
        {
            ["gnollhackWiki"] = wiki,
            ["gnollhackSource"] = null
        };

        string json = Overseer.Services.Benchmarking.BenchmarkService.SerializeCorpusIndexFingerprints(snapshot);
        var parsed = CorpusIndexFingerprintProvider.Parse(json);

        Assert.Equal(
            "{\"gnollhackWiki\":{\"sha256\":\"" + new string('a', 64) + "\",\"fileCount\":12,\"indexedAtUtc\":\"2026-10-04T12:00:00.000Z\"},"
            + "\"gnollhackSource\":null,\"knowledgeBase\":null,\"nethackWiki\":null,\"nethackSource\":null}",
            json);
        Assert.NotNull(parsed);
        Assert.Equal(5, parsed!.Count);
        Assert.Equal(wiki, parsed["gnollhackWiki"]);
        Assert.Null(parsed["nethackSource"]);
        Assert.Null(CorpusIndexFingerprintProvider.Parse(null));
        Assert.Null(CorpusIndexFingerprintProvider.Parse("not json"));
    }

    private static string Hex(byte[] bytes) => System.Convert.ToHexString(bytes).ToLowerInvariant();
}
