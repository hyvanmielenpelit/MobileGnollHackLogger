using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ContentProtectionServiceTests
{
    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static ConfigurationContentKeyRing CreateKeyRing(
        Dictionary<string, string>? versions = null, string active = "v1")
    {
        versions ??= new Dictionary<string, string> { ["v1"] = Key() };

        var settings = new Dictionary<string, string?> { ["PrivacySettings:ActiveKeyVersion"] = active };
        foreach (var (version, key) in versions)
            settings[$"PrivacySettings:KeyRing:{version}"] = key;

        return new ConfigurationContentKeyRing(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static ContentProtectionService CreateService(ConfigurationContentKeyRing? ring = null)
        => new(ring ?? CreateKeyRing());

    private static ChatSession Session(long id = 42) => new() { Id = id, IsConfidential = true };

    // ── The keyring ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyKeyRingIsUnusableAndSaysSo()
    {
        /* Not an exception. It surfaces through ConfigHealthService at startup, because
           throwing here would take down an application whose non-confidential functionality is
           fine, and throwing lazily would surface on a user's first confidential turn. */
        var ring = new ConfigurationContentKeyRing(new ConfigurationBuilder().Build());

        Assert.False(ring.IsUsable);
        Assert.Contains("No content keys are configured", ring.ValidationError);
    }

    [Fact]
    public void AKeyOfTheWrongLengthIsRejectedWithItsActualSize()
    {
        var ring = CreateKeyRing(new Dictionary<string, string>
        {
            ["v1"] = Convert.ToBase64String(new byte[16])
        });

        Assert.False(ring.IsUsable);
        Assert.Contains("16 bytes, not 32", ring.ValidationError);
    }

    [Fact]
    public void AKeyThatIsNotBase64IsRejected()
    {
        var ring = CreateKeyRing(new Dictionary<string, string> { ["v1"] = "not base64 at all!!" });

        Assert.False(ring.IsUsable);
        Assert.Contains("not valid base64", ring.ValidationError);
    }

    [Fact]
    public void AMissingActiveVersionIsRejected()
    {
        var ring = CreateKeyRing(active: "");

        Assert.False(ring.IsUsable);
        Assert.Contains("ActiveKeyVersion is not set", ring.ValidationError);
    }

    [Fact]
    public void AnActiveVersionThatIsNotInTheRingIsRejected()
    {
        /* The subtlest of the four, and the one most likely during a rotation: the ring is
           well-formed and the pointer names a version nobody added. */
        var ring = CreateKeyRing(new Dictionary<string, string> { ["v1"] = Key() }, active: "v2");

        Assert.False(ring.IsUsable);
        Assert.Contains("'v2', which is not in the keyring", ring.ValidationError);
    }

    [Fact]
    public void AWellFormedRingIsUsable()
    {
        var ring = CreateKeyRing();

        Assert.True(ring.IsUsable);
        Assert.Null(ring.ValidationError);
        Assert.Equal("v1", ring.ActiveVersion);
        Assert.True(ring.HasVersion("v1"));
        Assert.False(ring.HasVersion("v9"));
    }

    // ── Round trip ──────────────────────────────────────────────────────────────

    [Fact]
    public void EncryptThenDecrypt_RoundTripsExactly()
    {
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        const string plaintext = "The gnoll bites! You feel much better. äöü 😀";
        string? envelope = service.Encrypt(session, plaintext);

        Assert.NotEqual(plaintext, envelope);
        Assert.StartsWith(ContentProtectionService.RowPrefix, envelope);
        Assert.Equal(plaintext, service.Decrypt(session, envelope));
    }

    [Fact]
    public void TheEnvelopeCarriesAFormatVersionAndFourColonSeparatedParts()
    {
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        string envelope = service.Encrypt(session, "x")!;

        Assert.StartsWith("enc:v1:", envelope);
        // prefix's own two colons plus the two separating nonce, tag and ciphertext.
        Assert.Equal(4, envelope.Count(c => c == ':'));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullAndEmptyPassThroughUnchanged(string? value)
    {
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        Assert.Equal(value, service.Encrypt(session, value));
        Assert.Equal(value, service.Decrypt(session, value));
    }

    [Fact]
    public void EncryptIsIdempotent()
    {
        /* The write paths are not all in one place, and double-encrypting would be
           unrecoverable without knowing how many times it happened. */
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        string once = service.Encrypt(session, "secret")!;
        string twice = service.Encrypt(session, once)!;

        Assert.Equal(once, twice);
        Assert.Equal("secret", service.Decrypt(session, twice));
    }

    [Fact]
    public void TwoEncryptionsOfTheSamePlaintextDiffer()
    {
        // A fresh nonce each time, so identical content is not identifiable as identical.
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        Assert.NotEqual(service.Encrypt(session, "same"), service.Encrypt(session, "same"));
    }

    // ── The associated data, which is what binds content to its session ─────────

    [Fact]
    public void ContentEncryptedForOneSessionDoesNotDecryptUnderAnother()
    {
        /* chatsession-content:<id> is the row AAD, so a ciphertext moved between sessions
           fails authentication rather than decrypting under the wrong DEK. */
        var ring = CreateKeyRing();
        var service = CreateService(ring);

        var first = Session(1);
        service.EnsureSessionKey(first);
        string envelope = service.Encrypt(first, "first session's secret")!;

        // A second session that legitimately holds the same wrapped DEK bytes cannot be built
        // -- so re-wrap the first session's key columns onto a different id and try to read.
        var impostor = new ChatSession
        {
            Id = 2,
            IsConfidential = true,
            EncryptedContentKey = first.EncryptedContentKey,
            ContentKeyNonce = first.ContentKeyNonce,
            ContentKeyTag = first.ContentKeyTag,
            ContentKeyVersion = first.ContentKeyVersion
        };

        /* The DEK itself fails to unwrap, because chatsession:<id> is its AAD. That is D-12:
           binding the DEK to the user rather than the session would have let a wrapped DEK be
           replayed onto another of that user's sessions. */
        Assert.Equal(ContentProtectionService.UnreadableNotice, service.Decrypt(impostor, envelope));
    }

    [Fact]
    public void AnUnreadableEnvelopeReturnsANoticeRatherThanThrowingOrEmptyString()
    {
        /* One corrupt row must not hide a whole conversation, and it must not read as an empty
           message either -- silence would look like the model said nothing. */
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        Assert.Equal(ContentProtectionService.UnreadableNotice,
            service.Decrypt(session, "enc:v1:not:valid:base64!!"));
        Assert.Equal(ContentProtectionService.UnreadableNotice,
            service.Decrypt(session, "enc:v1:too-few-parts"));
    }

    // ── Mixed sessions, which upgrade makes normal ──────────────────────────────

    [Fact]
    public void APlaintextRowInAnEncryptedSessionReadsBackUnchanged()
    {
        /* The reason the prefix is per row rather than per session: an upgraded chat
           legitimately holds both, and a per-session boolean would read one kind as the
           other. */
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        const string legacy = "written before the upgrade";
        Assert.Equal(legacy, service.Decrypt(session, legacy));
        Assert.False(ContentProtectionService.IsEncrypted(legacy));
    }

    [Fact]
    public void IsEncrypted_RecognisesOnlyTheRealPrefix()
    {
        Assert.True(ContentProtectionService.IsEncrypted("enc:v1:a:b:c"));
        Assert.False(ContentProtectionService.IsEncrypted("enc:v2:a:b:c"));
        Assert.False(ContentProtectionService.IsEncrypted("Enc:v1:a:b:c"));
        Assert.False(ContentProtectionService.IsEncrypted("prefixed enc:v1:a:b:c"));
        Assert.False(ContentProtectionService.IsEncrypted(null));
    }

    // ── Session keys ────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureSessionKey_SetsAllFourColumnsAndIsIdempotent()
    {
        var service = CreateService();
        var session = Session();

        service.EnsureSessionKey(session);

        Assert.NotNull(session.EncryptedContentKey);
        Assert.NotNull(session.ContentKeyNonce);
        Assert.NotNull(session.ContentKeyTag);
        Assert.Equal("v1", session.ContentKeyVersion);

        string firstKey = session.EncryptedContentKey!;
        service.EnsureSessionKey(session);

        // A second call must not replace the DEK; existing content would become unreadable.
        Assert.Equal(firstKey, session.EncryptedContentKey);
    }

    [Fact]
    public void EncryptingWithoutASessionKeyIsAProgrammingError()
    {
        var service = CreateService();

        var ex = Assert.Throws<InvalidOperationException>(() => service.Encrypt(Session(), "x"));
        Assert.Contains("EnsureSessionKey", ex.Message);
    }

    // ── Rotation: the whole reason for a per-session DEK ────────────────────────

    [Fact]
    public void AfterRotation_ContentStillDecryptsAndNoContentWasRewritten()
    {
        string v1 = Key();
        string v2 = Key();

        var beforeRing = CreateKeyRing(new Dictionary<string, string> { ["v1"] = v1 }, active: "v1");
        var beforeService = CreateService(beforeRing);

        var session = Session();
        beforeService.EnsureSessionKey(session);
        string envelope = beforeService.Encrypt(session, "survives rotation")!;
        string wrappedUnderV1 = session.EncryptedContentKey!;

        // v2 added and made active; v1 retained so existing sessions can still be unwrapped.
        var afterRing = CreateKeyRing(
            new Dictionary<string, string> { ["v1"] = v1, ["v2"] = v2 }, active: "v2");
        var afterService = CreateService(afterRing);

        Assert.True(afterService.TryRewrapSessionKey(session));
        Assert.Equal("v2", session.ContentKeyVersion);
        Assert.NotEqual(wrappedUnderV1, session.EncryptedContentKey);

        /* The envelope is byte-identical: rotation re-wrapped one row and rewrote no content.
           That is the entire point of the per-session DEK. */
        Assert.Equal("survives rotation", afterService.Decrypt(session, envelope));
    }

    [Fact]
    public void RewrapIsANoOpWhenTheSessionIsAlreadyOnTheActiveVersion()
    {
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        Assert.False(service.TryRewrapSessionKey(session));
    }

    [Fact]
    public void ASessionWhoseKeyVersionWasRetiredCannotBeRead()
    {
        /* Retiring a version while a session still names it is the one rotation mistake that
           loses data, so the failure has to be visible rather than silent. */
        string v1 = Key();
        var session = Session();
        CreateService(CreateKeyRing(new Dictionary<string, string> { ["v1"] = v1 })).EnsureSessionKey(session);
        string envelope = CreateService(CreateKeyRing(new Dictionary<string, string> { ["v1"] = v1 }))
            .Encrypt(session, "orphaned")!;

        var withoutV1 = CreateService(CreateKeyRing(
            new Dictionary<string, string> { ["v2"] = Key() }, active: "v2"));

        Assert.Equal(ContentProtectionService.UnreadableNotice, withoutV1.Decrypt(session, envelope));
        Assert.False(withoutV1.TryRewrapSessionKey(session));
    }

    // ── Attachments on disk ─────────────────────────────────────────────────────

    [Fact]
    public void EncryptFileThenDecryptFile_RoundTripsBytes()
    {
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        byte[] original = RandomNumberGenerator.GetBytes(4096);
        byte[] encrypted = service.EncryptFile(session, original);

        Assert.True(ContentProtectionService.IsEncryptedFile(encrypted));
        Assert.NotEqual(original, encrypted);
        Assert.Equal(original, service.DecryptFile(session, encrypted));
    }

    [Fact]
    public void AFileWithoutTheMagicIsLegacyAndIsReturnedUnchanged()
    {
        /* What lets an upgraded session's older attachments keep working, and why the format is
           self-describing rather than gated on the session flag. */
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        byte[] legacy = Encoding.ASCII.GetBytes("PNG-ish bytes that were never encrypted");

        Assert.False(ContentProtectionService.IsEncryptedFile(legacy));
        Assert.Equal(legacy, service.DecryptFile(session, legacy));
    }

    [Fact]
    public void AnEmptyFileIsNotMistakenForAnEncryptedOne()
    {
        Assert.False(ContentProtectionService.IsEncryptedFile(Array.Empty<byte>()));
        Assert.False(ContentProtectionService.IsEncryptedFile(Encoding.ASCII.GetBytes("ENC1")));
    }

    [Fact]
    public void AFileEncryptedForOneSessionDoesNotDecryptUnderAnother()
    {
        var service = CreateService();
        var first = Session(1);
        service.EnsureSessionKey(first);
        byte[] encrypted = service.EncryptFile(first, Encoding.ASCII.GetBytes("private document"));

        var impostor = new ChatSession
        {
            Id = 2,
            IsConfidential = true,
            EncryptedContentKey = first.EncryptedContentKey,
            ContentKeyNonce = first.ContentKeyNonce,
            ContentKeyTag = first.ContentKeyTag,
            ContentKeyVersion = first.ContentKeyVersion
        };

        Assert.Empty(service.DecryptFile(impostor, encrypted));
    }

    // ── The envelope's size, which F3's column width rests on ───────────────────

    [Fact]
    public void A256CharacterTitleFitsTheWidenedColumn()
    {
        /* F3's arithmetic: 49 characters of overhead plus base64's 4/3 over up to 4 bytes per
           character puts 256 plaintext characters at 1417, inside the 2048-character column.
           This asserts the real number rather than the estimate. */
        var service = CreateService();
        var session = Session();
        service.EnsureSessionKey(session);

        string title = new string('x', 256);
        string envelope = service.Encrypt(session, title)!;

        Assert.True(envelope.Length <= 2048, $"envelope was {envelope.Length} characters");
        Assert.Equal(title, service.Decrypt(session, envelope));

        // And the worst case: 256 four-byte characters.
        string wide = string.Concat(Enumerable.Repeat("😀", 128)); // 128 emoji = 256 UTF-16 units
        string wideEnvelope = service.Encrypt(session, wide)!;
        Assert.True(wideEnvelope.Length <= 2048, $"wide envelope was {wideEnvelope.Length} characters");
        Assert.Equal(wide, service.Decrypt(session, wideEnvelope));
    }
}
