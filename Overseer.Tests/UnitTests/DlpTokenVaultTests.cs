using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Privacy.Dlp;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The per-turn secret-to-placeholder mapping. The property that matters most here is
/// deduplication: one secret, one token, for the whole turn.
/// </summary>
public class DlpTokenVaultTests
{
    private const string SkKey = "sk-proj-9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvXn0BiOoMzQrEyPkSl3W";
    private const string GoogleKey = "AIzaSyB7xQ2mVk9RtLpN4wZc1AeHjD6TgUvXn0";
    private const string Card = "4111111111111111";
    private const string Ssn = "078-05-1120";

    private static DlpTokenVault CreateVault(DlpPolicy? policy = null)
    {
        var scanner = new DlpScannerService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        return new DlpTokenVault(scanner, policy ?? DlpPolicy.Defaults);
    }

    // ── Nothing to do ───────────────────────────────────────────────────────────

    [Fact]
    public void AFreshVaultIsEmpty()
    {
        var vault = CreateVault();

        Assert.True(vault.IsEmpty);
        Assert.Equal(0, vault.Count);
        Assert.Empty(vault.Tokens);
        Assert.Empty(vault.Describe());
    }

    [Fact]
    public void NullPassesThroughBothWays()
    {
        var vault = CreateVault();

        Assert.Null(vault.Mask(null));
        Assert.Null(vault.Unmask(null));
    }

    [Fact]
    public void TextWithNoSecretComesBackUnchangedAndByTheSameReference()
    {
        /* Almost every call is this one, so it must not copy. Assert.Same is the point of the
           test: an equal-but-new string here would mean an allocation per turn per message. */
        var vault = CreateVault();
        const string clean = "how do I configure the retention sweep?";

        Assert.Same(clean, vault.Mask(clean));
        Assert.True(vault.IsEmpty);
    }

    [Fact]
    public void APolicyWithEverythingOffMasksNothing()
    {
        var vault = CreateVault(DlpPolicy.None);
        string text = $"my key is {SkKey}";

        Assert.Same(text, vault.Mask(text));
        Assert.True(vault.IsEmpty);
    }

    // ── Masking ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MaskReplacesASecretWithItsToken()
    {
        var vault = CreateVault();

        Assert.Equal("my key is [REDACTED_API_KEY_1] ok", vault.Mask($"my key is {SkKey} ok"));
        Assert.Equal(1, vault.Count);
        Assert.False(vault.IsEmpty);
    }

    [Fact]
    public void TokensAreNumberedPerClassFromOne()
    {
        var vault = CreateVault();

        string masked = vault.Mask($"a {SkKey} b {GoogleKey} c {Card} d")!;

        Assert.Equal(
            "a [REDACTED_API_KEY_1] b [REDACTED_API_KEY_2] c [REDACTED_CARD_1] d",
            masked);
        Assert.Equal(3, vault.Count);
    }

    [Fact]
    public void TheSameSecretOccurringSeveralTimesInOneStringGetsOneToken()
    {
        var vault = CreateVault();

        string masked = vault.Mask($"{SkKey} and again {SkKey}")!;

        Assert.Equal("[REDACTED_API_KEY_1] and again [REDACTED_API_KEY_1]", masked);
        Assert.Equal(1, vault.Count);
    }

    [Fact]
    public void TheSameSecretGetsOneTokenAcrossSeparateMaskCalls()
    {
        /* A turn masks in several places — replayed history, the new user message, an
           attachment's text, a tool result — and each is its own call. Numbering per call would
           show the model [REDACTED_API_KEY_1] and [REDACTED_API_KEY_2] for one string, and it
           would then reason about them as two credentials: "the first key is invalid, try the
           second". That is worse than not masking at all. */
        var vault = CreateVault();

        string history = vault.Mask($"earlier I said {SkKey}")!;
        string message = vault.Mask($"now check {SkKey}")!;
        string toolResult = vault.Mask($"the file contains {SkKey}")!;

        Assert.Equal("earlier I said [REDACTED_API_KEY_1]", history);
        Assert.Equal("now check [REDACTED_API_KEY_1]", message);
        Assert.Equal("the file contains [REDACTED_API_KEY_1]", toolResult);
        Assert.Equal(1, vault.Count);
    }

    [Fact]
    public void MaskIsIdempotent()
    {
        var vault = CreateVault();
        string text = $"key {SkKey} card {Card} ssn {Ssn}";

        string once = vault.Mask(text)!;
        string twice = vault.Mask(once)!;

        // A second pass must be a no-op. Were a token itself detectable, the second pass would
        // replace it and the original secret would become unrecoverable.
        Assert.Equal(once, twice);
        Assert.Equal(3, vault.Count);
    }

    [Fact]
    public async Task TheSameSecretGetsOneTokenEvenWhenParallelCallsMaskItAtOnce()
    {
        // A turn runs its tool calls in parallel and each masks its own result, so two threads
        // can be allocating a token for the same secret at the same moment.
        var vault = CreateVault();
        var tasks = new List<Task<string?>>();

        for (int i = 0; i < 16; i++)
            tasks.Add(Task.Run(() => vault.Mask($"value {SkKey}"), TestContext.Current.CancellationToken));

        string?[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, vault.Count);
        Assert.All(results, r => Assert.Equal("value [REDACTED_API_KEY_1]", r));
    }

    // ── Unmasking ───────────────────────────────────────────────────────────────

    [Fact]
    public void UnmaskRestoresTheOriginalTextExactly()
    {
        var vault = CreateVault();
        string text = $"key {SkKey}, card {Card}, ssn {Ssn}, and the key again {SkKey}.";

        string masked = vault.Mask(text)!;

        Assert.DoesNotContain(SkKey, masked, StringComparison.Ordinal);
        // The user must see their own secret intact and never learn a placeholder existed.
        Assert.Equal(text, vault.Unmask(masked));
    }

    [Fact]
    public void UnmaskRestoresEveryOccurrenceOfAToken()
    {
        var vault = CreateVault();
        vault.Mask(SkKey);

        Assert.Equal(
            $"{SkKey} then {SkKey} then {SkKey}",
            vault.Unmask("[REDACTED_API_KEY_1] then [REDACTED_API_KEY_1] then [REDACTED_API_KEY_1]"));
    }

    [Fact]
    public void UnmaskLeavesATokenItNeverIssuedAlone()
    {
        /* The model can invent a token-shaped string, or repeat one from replayed history that
           belongs to an earlier turn's vault. Leaving it as it stands is the only honest
           option: substituting the wrong secret would be a cross-turn leak, and throwing would
           lose the reply. */
        var vault = CreateVault();
        vault.Mask(SkKey);

        const string reply = "you also mentioned [REDACTED_API_KEY_9] and [REDACTED_CARD_1].";

        Assert.Equal(reply, vault.Unmask(reply));
    }

    [Fact]
    public void AnEmptyVaultUnmasksNothing()
    {
        var vault = CreateVault();
        const string reply = "nothing here, though [REDACTED_API_KEY_1] looks like a token.";

        Assert.Same(reply, vault.Unmask(reply));
    }

    // ── What may be shown ───────────────────────────────────────────────────────

    [Fact]
    public void TokensMapToClassesAndNeverToValues()
    {
        var vault = CreateVault();
        vault.Mask($"key {SkKey} card {Card}");

        var tokens = vault.Tokens;

        Assert.Equal(DlpClass.ApiKey, tokens["[REDACTED_API_KEY_1]"]);
        Assert.Equal(DlpClass.CreditCard, tokens["[REDACTED_CARD_1]"]);
        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public void DescribeNamesEachTokenWithoutRepeatingItsValue()
    {
        var vault = CreateVault();
        vault.Mask($"key {SkKey} card {Card}");

        var lines = vault.Describe();

        Assert.Equal(
            new[]
            {
                "[REDACTED_API_KEY_1] — provider or cloud API key",
                "[REDACTED_CARD_1] — payment card number"
            },
            lines);

        /* These lines are logged and streamed to the user's own browser, so a secret appearing
           in one would defeat the whole exercise. */
        foreach (string line in lines)
        {
            Assert.DoesNotContain(SkKey, line, StringComparison.Ordinal);
            Assert.DoesNotContain(Card, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MaxTokenLengthCoversTheLongestTokenTheVaultCanProduce()
    {
        var vault = CreateVault();

        // An upper bound on the token shape, not the longest token issued so far: a tool result
        // masked mid-stream can mint a token after the unmasker has already sized its window.
        Assert.True(vault.MaxTokenLength >= "[REDACTED_PRIVATE_KEY_999999999]".Length);

        foreach (string token in vault.Tokens.Keys)
            Assert.True(token.Length <= vault.MaxTokenLength);
    }
}
