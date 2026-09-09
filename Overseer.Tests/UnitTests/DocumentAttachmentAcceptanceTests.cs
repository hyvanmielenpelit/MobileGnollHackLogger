using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Accepting binary document formats without loosening what "text" is allowed to contain.
/// </summary>
/// <remarks>
/// The validator used to classify every non-image as text and reject it for containing binary
/// data — which is right for a mislabelled payload and wrong for every PDF ever uploaded.
/// These tests pin the three-way split that replaced it, and in particular that "binary" still
/// never means "unchecked".
/// </remarks>
public class DocumentAttachmentAcceptanceTests
{
    private const string Pdf = "application/pdf";
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static AttachmentValidator CreateValidator(Dictionary<string, string?>? overrides = null)
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build());

    private static byte[] PdfBytes(string body = "1 0 obj\n<< /Type /Catalog >>\nendobj\n")
        => Encoding.ASCII.GetBytes("%PDF-1.7\n" + body + "%%EOF");

    /* A ZIP local-file header followed by enough bytes to be a plausible container. What is
       inside does not matter here: the validator's job is to establish it is a container and not
       a renamed executable, and which OpenXML format it is comes from the parts, read later. */
    private static byte[] ZipBytes()
    {
        var bytes = new byte[512];
        bytes[0] = 0x50; bytes[1] = 0x4B; bytes[2] = 0x03; bytes[3] = 0x04;
        for (int i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData(".sql")]
    public void TheDocumentExtensionsAreAllowedByDefault(string extension)
    {
        var validator = CreateValidator();

        Assert.Contains(extension, validator.AllowedExtensions);
    }

    [Fact]
    public void TheAllowlistIsWhatThePickerIsBuiltFrom()
    {
        /* Published so the file dialog is sourced from the server's allowlist rather than kept
           in step with it by hand. Lower-cased and dot-prefixed is what an accept attribute
           wants. */
        var validator = CreateValidator();

        Assert.NotEmpty(validator.AllowedExtensions);
        Assert.All(validator.AllowedExtensions, e => Assert.StartsWith(".", e));
        Assert.All(validator.AllowedExtensions, e => Assert.Equal(e.ToLowerInvariant(), e));
        Assert.Equal(validator.AllowedExtensions.OrderBy(e => e), validator.AllowedExtensions);
    }

    [Fact]
    public void ExecutableExtensionsStayBlockedEvenThoughTheListGrew()
    {
        // Widening the allowlist must not have widened the hard denylist.
        var validator = CreateValidator();

        foreach (string blocked in new[] { "payload.exe", "payload.ps1", "payload.js", "payload.jar" })
        {
            var result = validator.ValidateBeforeDecode(blocked, "text/plain", "AAAA");
            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public void APdfIsAcceptedRatherThanRefusedForContainingBinaryData()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("report.pdf", Pdf, PdfBytes());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(Docx)]
    [InlineData(Xlsx)]
    public void AnOpenXmlContainerIsAccepted(string contentType)
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("report" + (contentType == Docx ? ".docx" : ".xlsx"), contentType, ZipBytes());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SomethingDeclaredAPdfThatIsNotOneIsRefused()
    {
        // "Binary" never means "unchecked": each document type is checked against its own
        // signature, exactly as an image is.
        var validator = CreateValidator();

        var result = validator.ValidateBytes(
            "payload.pdf", Pdf, Encoding.ASCII.GetBytes("MZ this is not a pdf"));

        Assert.False(result.IsValid);
        Assert.Contains("PDF", result.Error!);
    }

    [Fact]
    public void SomethingDeclaredAWordDocumentThatIsNotAContainerIsRefused()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("payload.docx", Docx, PdfBytes());

        Assert.False(result.IsValid);
        Assert.Contains("Office document", result.Error!);
    }

    [Fact]
    public void ADocumentSentAsTextIsRefusedRatherThanReclassified()
    {
        /* Refused, not promoted: the declared type is what the rest of the pipeline routes on,
           so quietly re-classifying a mislabelled upload would let a client choose its own
           parser. The message tells the user what to do about it. */
        var validator = CreateValidator();

        var result = validator.ValidateBytes("notes.txt", "text/plain", PdfBytes());

        Assert.False(result.IsValid);
        Assert.Contains("contains a document", result.Error!);
    }

    [Fact]
    public void AZipSentAsTextIsRefusedToo()
    {
        var validator = CreateValidator();

        var result = validator.ValidateBytes("notes.txt", "text/plain", ZipBytes());

        Assert.False(result.IsValid);
        Assert.Contains("contains a document", result.Error!);
    }

    [Fact]
    public void RealTextIsStillScannedForBinaryContent()
    {
        // The check the three-way split exists to preserve: a payload declared as text and
        // containing something else is still refused.
        var validator = CreateValidator();

        var withNul = Encoding.UTF8.GetBytes("hello\0world").ToArray();
        Assert.False(validator.ValidateBytes("notes.txt", "text/plain", withNul).IsValid);
        Assert.True(validator.ValidateBytes("notes.txt", "text/plain", Encoding.UTF8.GetBytes("hello world")).IsValid);
    }

    [Fact]
    public void CsvAndCodeAreTreatedAsTextAndNotAsBinaryDocuments()
    {
        var validator = CreateValidator();

        Assert.False(validator.IsBinaryDocumentContentType("text/csv"));
        Assert.False(validator.IsBinaryDocumentContentType("application/json"));
        Assert.True(validator.IsBinaryDocumentContentType(Pdf));
        Assert.True(validator.IsBinaryDocumentContentType(Docx));

        var csv = Encoding.UTF8.GetBytes("name,total\nWand of digging,3\n");
        Assert.True(validator.ValidateBytes("rows.csv", "text/csv", csv).IsValid);
    }

    [Fact]
    public void TheBinaryDocumentSetIsConfigurable()
    {
        // Sourced from configuration for the same reason as every other bound here: an operator
        // narrowing the allowlist must not have to change code.
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            { "PrivacySettings:Attachments:BinaryDocumentContentTypes:0", Pdf }
        });

        Assert.True(validator.IsBinaryDocumentContentType(Pdf));
        Assert.False(validator.IsBinaryDocumentContentType(Docx));
    }

    [Fact]
    public void AnImageIsUnaffectedByAnyOfThis()
    {
        var validator = CreateValidator();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        Assert.True(validator.ValidateBytes("shot.png", "image/png", png).IsValid);
        Assert.False(validator.ValidateBytes("shot.png", "image/png", PdfBytes()).IsValid);
    }
}
