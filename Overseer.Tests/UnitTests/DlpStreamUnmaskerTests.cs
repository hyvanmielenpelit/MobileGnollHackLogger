using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Privacy.Dlp;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The boundary-safe unmasker. Everything here is about a token arriving in pieces: a naive
/// per-chunk replace would emit the placeholder to the user verbatim.
/// </summary>
public class DlpStreamUnmaskerTests
{
    private const string SkKey = "sk-proj-9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvXn0BiOoMzQrEyPkSl3W";
    private const string Card = "4111111111111111";

    private static DlpTokenVault CreateVault()
    {
        var scanner = new DlpScannerService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        return new DlpTokenVault(scanner, DlpPolicy.Defaults);
    }

    private static DlpTokenVault CreateLoadedVault()
    {
        var vault = CreateVault();
        vault.Mask($"key {SkKey} card {Card}");
        return vault;
    }

    // ── The common case ─────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyVaultPassesEveryChunkStraightThrough()
    {
        /* Nothing was masked, so nothing can need restoring. Assert.Same is the assertion that
           matters: it proves the chunk was not buffered or copied, which is what keeps the
           overwhelmingly common turn free of cost. */
        var unmasker = new DlpStreamUnmasker(CreateVault());
        const string chunk = "a reply with [brackets] and [REDACTED_API_KEY_1] in it";

        Assert.Same(chunk, unmasker.Push(chunk));
        Assert.Equal(string.Empty, unmasker.Flush());
    }

    [Fact]
    public void PushIgnoresNullAndEmptyChunks()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal(string.Empty, unmasker.Push(null));
        Assert.Equal(string.Empty, unmasker.Push(string.Empty));
    }

    // ── Split tokens ────────────────────────────────────────────────────────────

    [Fact]
    public void ATokenSplitAcrossTwoChunksIsRestored()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal("your key is ", unmasker.Push("your key is [REDACTED_"));
        Assert.Equal($"{SkKey} — keep it safe", unmasker.Push("API_KEY_1] — keep it safe"));
        Assert.Equal(string.Empty, unmasker.Flush());
    }

    [Fact]
    public void ATokenSplitAcrossThreeChunksIsRestored()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal("value ", unmasker.Push("value "));
        Assert.Equal(string.Empty, unmasker.Push("[REDAC"));
        Assert.Equal(string.Empty, unmasker.Push("TED_API_"));
        Assert.Equal($"{SkKey} end", unmasker.Push("KEY_1] end"));
        Assert.Equal(string.Empty, unmasker.Flush());
    }

    [Fact]
    public void TwoTokensInOneChunkAreBothRestored()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal(
            $"{SkKey} pays {Card}",
            unmasker.Push("[REDACTED_API_KEY_1] pays [REDACTED_CARD_1]"));
    }

    // ── Brackets that are not tokens ────────────────────────────────────────────

    [Fact]
    public void ABracketThatNeverBecomesATokenIsReleased()
    {
        /* Held text has to be released once it can no longer become a token, or a reply full of
           markdown links would stall and the buffer would grow without bound. */
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        string first = unmasker.Push("see [note");
        string second = unmasker.Push(" that is far longer than the longest token this vault can mint");

        Assert.Equal("see ", first);
        Assert.Contains("[note", second, StringComparison.Ordinal);
        Assert.Equal(
            "see [note that is far longer than the longest token this vault can mint",
            first + second + unmasker.Flush());
    }

    [Fact]
    public void AnUnknownTokenShapedStringIsPassedThroughUnchanged()
    {
        // The model can invent a placeholder. Substituting the wrong secret for it would be a
        // leak; emitting it as it stands is the only honest option.
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal(
            "[REDACTED_API_KEY_42] was never issued",
            unmasker.Push("[REDACTED_API_KEY_42] was never issued") + unmasker.Flush());
    }

    [Fact]
    public void OrdinaryBracketedProseSurvivesIntact()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());
        const string prose = "read [the docs](https://example.invalid), then check a[0] and b[i][j].";

        Assert.Equal(prose, unmasker.Push(prose) + unmasker.Flush());
    }

    // ── The flush, which is not optional ────────────────────────────────────────

    [Fact]
    public void FlushEmitsTheTailStillHeldInTheWindow()
    {
        /* Without this the last few characters of a reply — up to the longest token, and only
           when the reply happens to end in bracket-like text — never reach the client. The loss
           would be intermittent and would read as a model truncation rather than as a bug here,
           which is the worst kind to find later. */
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());

        Assert.Equal("all done ", unmasker.Push("all done [REDA"));
        Assert.Equal("[REDA", unmasker.Flush());
    }

    [Fact]
    public void FlushIsIdempotentOnceTheWindowIsDrained()
    {
        var unmasker = new DlpStreamUnmasker(CreateLoadedVault());
        unmasker.Push("tail [REDA");

        Assert.Equal("[REDA", unmasker.Flush());
        Assert.Equal(string.Empty, unmasker.Flush());
    }

    // ── The invariant that ties the two together ────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(64)]
    public void TheStreamedResultAlwaysEqualsUnmaskingTheWholeReply(int chunkSize)
    {
        /* The property the whole class exists to hold, checked at every chunk boundary: what
           the browser receives must be exactly what unmasking the finished reply would give,
           whatever sizes the provider happens to stream in. */
        var vault = CreateLoadedVault();
        const string reply =
            "Your key [REDACTED_API_KEY_1] is fine, but [REDACTED_CARD_1] is not. "
            + "See [the docs](https://example.invalid) and [REDACTED_API_KEY_1] again. "
            + "Arrays like a[0] work too, and [this bracketed aside is longer than the longest "
            + "possible token] as well. Ends mid-window: [REDACT";

        var unmasker = new DlpStreamUnmasker(vault);
        var received = new StringBuilder();

        for (int i = 0; i < reply.Length; i += chunkSize)
            received.Append(unmasker.Push(reply.Substring(i, Math.Min(chunkSize, reply.Length - i))));

        received.Append(unmasker.Flush());

        Assert.Equal(vault.Unmask(reply), received.ToString());
    }
}
