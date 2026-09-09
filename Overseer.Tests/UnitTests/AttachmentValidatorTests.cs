using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AttachmentValidatorTests
{
    private static AttachmentValidator CreateValidator(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            { "MaxAttachmentSize", "15728640" }
        };

        if (overrides != null)
        {
            foreach (var kv in overrides)
                settings[kv.Key] = kv.Value;
        }

        return new AttachmentValidator(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static byte[] PngBytes(int payloadLength = 32)
    {
        var bytes = new byte[8 + payloadLength];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] JpegBytes() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    private static byte[] WebpBytes()
    {
        var bytes = new byte[16];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        return bytes;
    }

    [Theory]
    [InlineData("board.png", "image/png")]
    [InlineData("shot.jpg", "image/jpeg")]
    [InlineData("shot.jpeg", "image/jpeg")]
    [InlineData("dump.html", "text/html")]
    [InlineData("dump.htm", "text/html")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("notes.md", "text/markdown")]
    public void ValidateBeforeDecode_AcceptsEveryAllowlistedType(string fileName, string contentType)
    {
        var validator = CreateValidator();
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"));

        var result = validator.ValidateBeforeDecode(fileName, contentType, payload);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateBytes_AcceptsEachAllowlistedImageWithMatchingSignature()
    {
        var validator = CreateValidator();

        Assert.True(validator.ValidateBytes("a.png", "image/png", PngBytes()).IsValid);
        Assert.True(validator.ValidateBytes("a.jpg", "image/jpeg", JpegBytes()).IsValid);
        Assert.True(validator.ValidateBytes("a.webp", "image/webp", WebpBytes()).IsValid);
    }

    [Fact]
    public void ValidateBeforeDecode_RejectsDisallowedExtension()
    {
        var validator = CreateValidator();
        string payload = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var result = validator.ValidateBeforeDecode("payload.svg", "image/svg+xml", payload);

        Assert.False(result.IsValid);
        Assert.Contains("unsupported file type", result.Error);
    }

    [Fact]
    public void ValidateBeforeDecode_RejectsExecutableExtensionEvenWhenConfigurationAllowsIt()
    {
        /* The blocked list is unconditional. A configuration that allowlists .exe -- by
           accident or otherwise -- must not be able to open that door. */
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            { "PrivacySettings:Attachments:AllowedExtensions:0", ".exe" },
            { "PrivacySettings:Attachments:AllowedContentTypes:0", "application/octet-stream" }
        });

        var result = validator.ValidateBeforeDecode(
            "tool.exe", "application/octet-stream", Convert.ToBase64String(new byte[] { 1 }));

        Assert.False(result.IsValid);
        Assert.Contains("executable", result.Error);
    }

    [Fact]
    public void ValidateBeforeDecode_RejectsDisallowedContentTypeForAnAllowedExtension()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBeforeDecode(
            "notes.txt", "application/x-msdownload", Convert.ToBase64String(new byte[] { 1 }));

        Assert.False(result.IsValid);
        Assert.Contains("unsupported content type", result.Error);
    }

    [Fact]
    public void ValidateBeforeDecode_RejectsOnEncodedLengthBeforeAnythingIsDecoded()
    {
        /* The point of this check is that it costs one comparison against a string length,
           where the decode it precedes would have allocated the whole buffer. A 1 KB ceiling
           with a 4 KB payload exercises exactly that ordering. */
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            { "PrivacySettings:Attachments:MaxDecodedBytes", "1024" }
        });

        string oversized = Convert.ToBase64String(new byte[4096]);

        var result = validator.ValidateBeforeDecode("notes.txt", "text/plain", oversized);

        Assert.False(result.IsValid);
        Assert.Contains("larger than", result.Error);
    }

    [Fact]
    public void ValidateBytes_RejectsOversizedPayload()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            { "PrivacySettings:Attachments:MaxDecodedBytes", "1024" }
        });

        var result = validator.ValidateBytes("notes.txt", "text/plain", new byte[2048]);

        Assert.False(result.IsValid);
        Assert.Contains("larger than", result.Error);
    }

    [Fact]
    public void ValidateBytes_RejectsPngDeclaredFileWhoseMagicBytesDisagree()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("board.png", "image/png", Encoding.UTF8.GetBytes("<html>not a png</html>"));

        Assert.False(result.IsValid);
        Assert.Contains("does not contain", result.Error);
    }

    [Fact]
    public void ValidateBytes_RejectsTextFileCarryingBinaryData()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("notes.txt", "text/plain", new byte[] { 0x48, 0x69, 0x00, 0x01 });

        Assert.False(result.IsValid);
        Assert.Contains("binary data", result.Error);
    }

    [Fact]
    public void ValidateBytes_AcceptsTextWithTabsAndNewlines()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes(
            "notes.txt", "text/plain", Encoding.UTF8.GetBytes("line one\r\n\tindented\fform feed\n"));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateCount_RejectsASixthAttachment()
    {
        var validator = CreateValidator();

        Assert.True(validator.ValidateCount(5).IsValid);

        var result = validator.ValidateCount(6);
        Assert.False(result.IsValid);
        Assert.Contains("At most 5", result.Error);
    }

    [Fact]
    public void ResolveServedContentType_NeverEchoesANonAllowlistedType()
    {
        var validator = CreateValidator();

        Assert.Equal("image/png", validator.ResolveServedContentType("image/png"));
        Assert.Equal("text/html", validator.ResolveServedContentType("text/html"));
        Assert.Equal("application/octet-stream", validator.ResolveServedContentType("image/svg+xml"));
        Assert.Equal("application/octet-stream", validator.ResolveServedContentType("text/html; charset=utf-8"));
        Assert.Equal("application/octet-stream", validator.ResolveServedContentType(null));
    }

    [Fact]
    public void IsInlineSafeContentType_AdmitsOnlyTheThreeImageTypes()
    {
        var validator = CreateValidator();

        Assert.True(validator.IsInlineSafeContentType("image/png"));
        Assert.True(validator.IsInlineSafeContentType("image/jpeg"));
        Assert.True(validator.IsInlineSafeContentType("image/webp"));

        // Allowlisted for upload, but never rendered in the browser.
        Assert.False(validator.IsInlineSafeContentType("text/html"));
        Assert.False(validator.IsInlineSafeContentType("text/plain"));
        Assert.False(validator.IsInlineSafeContentType("image/svg+xml"));
        Assert.False(validator.IsInlineSafeContentType(null));
    }

    [Theory]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("..\\..\\windows\\system32\\notes.txt", "notes.txt")]
    [InlineData("C:\\Users\\someone\\board.png", "board.png")]
    public void SanitizeDisplayFileName_StripsDirectories(string input, string expected)
    {
        var validator = CreateValidator();

        Assert.Equal(expected, validator.SanitizeDisplayFileName(input));
    }

    [Fact]
    public void SanitizeDisplayFileName_RemovesControlCharactersAndCapsLength()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            { "PrivacySettings:Attachments:MaxFileNameLength", "16" }
        });

        Assert.Equal("abc.txt", validator.SanitizeDisplayFileName("a\u0000b\nc.txt"));
        Assert.Equal(16, validator.SanitizeDisplayFileName(new string('x', 400) + ".txt").Length);
        Assert.Equal("attachment", validator.SanitizeDisplayFileName("   "));
        Assert.Equal("attachment", validator.SanitizeDisplayFileName(null));
    }

    [Fact]
    public void BuildStoredFileName_KeepsOnlyAnAllowlistedExtensionAndNothingOfTheOriginalName()
    {
        var validator = CreateValidator();

        string stored = validator.BuildStoredFileName("../../secret notes.TXT");

        Assert.EndsWith(".txt", stored);
        Assert.DoesNotContain("secret", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", stored);
        Assert.DoesNotContain("\\", stored);
        // A GUID's 32 hex characters plus the extension.
        Assert.Equal(32 + 4, stored.Length);

        // An extension outside the allowlist is dropped rather than carried through.
        Assert.Equal(32, validator.BuildStoredFileName("payload.exe").Length);
    }

    [Fact]
    public void BuildStoredFileName_IsUniquePerCall()
    {
        var validator = CreateValidator();

        var names = Enumerable.Range(0, 50).Select(_ => validator.BuildStoredFileName("a.png")).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
