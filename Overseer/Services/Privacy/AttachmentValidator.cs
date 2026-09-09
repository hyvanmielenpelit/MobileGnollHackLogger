using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Privacy;

/// <param name="IsValid">False when the attachment must be refused.</param>
/// <param name="Error">The reason, phrased for the uploader. Null when valid.</param>
public sealed record AttachmentValidationResult(bool IsValid, string? Error)
{
    public static readonly AttachmentValidationResult Valid = new(true, null);

    public static AttachmentValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Server-side validation of chat attachments: count, size, extension, declared MIME type,
/// magic-byte agreement for images, and a control-character scan for text.
/// </summary>
/// <remarks>
/// The picker in chat.component.html implies all of these, but implies them only on the client.
/// Every bound here is read from <c>PrivacySettings:Attachments</c> so the two cannot drift
/// silently; the defaults reproduce what the client already enforced.
/// </remarks>
public class AttachmentValidator
{
    /* Kept in step with chat.component.html's accept attribute. */
    private static readonly string[] DefaultExtensions =
    {
        ".html", ".htm", ".txt", ".md", ".png", ".jpg", ".jpeg", ".webp",
        ".pdf", ".docx", ".xlsx", ".csv",
        ".json", ".xml", ".yml", ".yaml", ".log", ".c", ".h", ".cpp", ".cs", ".sql"
    };

    private static readonly string[] DefaultContentTypes =
    {
        "text/html", "text/plain", "text/markdown", "text/x-markdown",
        "image/png", "image/jpeg", "image/webp",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/csv", "application/csv",
        "application/json", "text/xml", "application/xml",
        "text/yaml", "application/x-yaml", "text/x-csrc", "text/x-c++src", "text/x-csharp",
        "application/sql", "text/x-sql",
        /* Browsers sometimes send nothing useful for a code or data file. The extension
           allowlist is what bounds those; refusing an empty type would refuse ordinary
           uploads of the very formats this exists to accept. */
        "application/octet-stream"
    };

    /* Formats whose bytes are legitimately binary. Without this the control-byte scan below --
       which exists to catch a payload declared as text and containing something else -- would
       reject every PDF and every OpenXML document on sight. Each one is checked against its own
       signature instead, so "binary" never means "unchecked". */
    private static readonly string[] DefaultBinaryDocumentContentTypes =
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    /* Types safe to render in the browser rather than download. Deliberately narrow: SVG is
       excluded because it is a script container, and text/html most of all. */
    private static readonly string[] DefaultInlineSafeContentTypes =
        { "image/png", "image/jpeg", "image/webp" };

    /* Extensions that must never be accepted whatever else the configuration says. */
    private static readonly string[] BlockedExtensions =
    {
        ".exe", ".dll", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc",
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".hta", ".jar", ".lnk", ".reg", ".sh", ".py", ".php", ".apk", ".dmg", ".app"
    };

    private readonly HashSet<string> _allowedExtensions;
    private readonly HashSet<string> _allowedContentTypes;
    private readonly HashSet<string> _inlineSafeContentTypes;
    private readonly HashSet<string> _binaryDocumentContentTypes;

    public AttachmentValidator(IConfiguration configuration)
    {
        /* The decoded-size ceiling defaults to the pre-existing MaxAttachmentSize so this class
           does not quietly change the accepted size while adding the checks around it. */
        MaxDecodedBytes = configuration.GetValue<int?>("PrivacySettings:Attachments:MaxDecodedBytes")
            ?? configuration.GetValue("MaxAttachmentSize", 15_728_640);

        MaxCount = configuration.GetValue("PrivacySettings:Attachments:MaxCount", 5);
        MaxFileNameLength = configuration.GetValue("PrivacySettings:Attachments:MaxFileNameLength", 256);
        RejectOnScanFailure = configuration.GetValue("PrivacySettings:Attachments:RejectOnScanFailure", true);

        _allowedExtensions = Read(configuration, "PrivacySettings:Attachments:AllowedExtensions", DefaultExtensions);
        _allowedContentTypes = Read(configuration, "PrivacySettings:Attachments:AllowedContentTypes", DefaultContentTypes);
        _inlineSafeContentTypes = Read(
            configuration, "PrivacySettings:Attachments:InlineSafeContentTypes", DefaultInlineSafeContentTypes);
        _binaryDocumentContentTypes = Read(
            configuration, "PrivacySettings:Attachments:BinaryDocumentContentTypes",
            DefaultBinaryDocumentContentTypes);
    }

    /// <summary>Maximum size of one attachment once decoded.</summary>
    public int MaxDecodedBytes { get; }

    /// <summary>Maximum attachments in one message. The client's limit is advisory; this one is not.</summary>
    public int MaxCount { get; }

    /// <summary>Cap on the display filename retained for the attachment row.</summary>
    public int MaxFileNameLength { get; }

    /// <summary>Whether a scanner error refuses the upload. See <see cref="IAntiMalwareScanner"/>.</summary>
    public bool RejectOnScanFailure { get; }

    private static HashSet<string> Read(IConfiguration configuration, string key, string[] fallback)
    {
        var values = configuration.GetSection(key).Get<string[]>();
        if (values == null || values.Length == 0)
            values = fallback;

        return new HashSet<string>(values.Select(v => v.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    public AttachmentValidationResult ValidateCount(int count)
    {
        if (count > MaxCount)
            return AttachmentValidationResult.Invalid($"At most {MaxCount} attachments can be sent with one message.");

        return AttachmentValidationResult.Valid;
    }

    /// <summary>
    /// Everything checkable without decoding: the name, the declared type, and the payload's
    /// encoded length.
    /// </summary>
    /// <remarks>
    /// The length check runs here on purpose. Convert.FromBase64String allocates the decoded
    /// buffer first and checks nothing, so an unbounded string is a memory cost taken before
    /// any other rule gets a say.
    /// </remarks>
    public AttachmentValidationResult ValidateBeforeDecode(string? fileName, string? contentType, string? base64Payload)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(ext))
            return AttachmentValidationResult.Invalid($"\"{Describe(fileName)}\" has no file extension.");

        if (BlockedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return AttachmentValidationResult.Invalid(
                $"\"{Describe(fileName)}\" is an executable file type and cannot be attached.");

        if (!_allowedExtensions.Contains(ext))
            return AttachmentValidationResult.Invalid($"\"{Describe(fileName)}\" has an unsupported file type ({ext}).");

        if (string.IsNullOrWhiteSpace(contentType) || !_allowedContentTypes.Contains(contentType))
            return AttachmentValidationResult.Invalid(
                $"\"{Describe(fileName)}\" has an unsupported content type ({Describe(contentType)}).");

        if (string.IsNullOrEmpty(base64Payload))
            return AttachmentValidationResult.Invalid($"\"{Describe(fileName)}\" is empty.");

        if (base64Payload.Length > MaxEncodedLengthFor(MaxDecodedBytes))
            return AttachmentValidationResult.Invalid(TooLargeMessage(fileName));

        return AttachmentValidationResult.Valid;
    }

    /// <summary>
    /// The checks that need the bytes: the exact decoded size, magic-byte agreement with the
    /// declared image type, and a control-character scan of the head of a text file.
    /// </summary>
    public AttachmentValidationResult ValidateBytes(string? fileName, string? contentType, byte[] bytes)
    {
        if (bytes.Length == 0)
            return AttachmentValidationResult.Invalid($"\"{Describe(fileName)}\" is empty.");

        if (bytes.Length > MaxDecodedBytes)
            return AttachmentValidationResult.Invalid(TooLargeMessage(fileName));

        /* Three classes, not two. An image and a binary document each have a signature to
           check; everything else is text and gets the control-byte scan. Treating a document as
           text is what a two-way split does, and it rejects every PDF. */
        if (contentType != null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (!MagicBytesAgree(contentType, bytes))
                return AttachmentValidationResult.Invalid(
                    $"\"{Describe(fileName)}\" does not contain the {contentType} image it claims to be.");
        }
        else if (contentType != null && _binaryDocumentContentTypes.Contains(contentType))
        {
            if (!DocumentMagicBytesAgree(contentType, bytes))
                return AttachmentValidationResult.Invalid(
                    $"\"{Describe(fileName)}\" does not contain the {DescribeFormat(contentType)} it claims to be.");
        }
        else if (LooksLikeBinaryDocument(bytes))
        {
            /* Declared as text and actually a PDF or a ZIP container. Refused rather than
               re-classified: the declared type is what the rest of the pipeline routes on, and
               quietly promoting a mislabelled upload would let a client pick its own parser. */
            return AttachmentValidationResult.Invalid(
                $"\"{Describe(fileName)}\" was sent as {contentType ?? "text"} but contains a document. " +
                "Re-attach it and let the browser set the type.");
        }
        else if (HasBinaryControlBytes(bytes))
        {
            return AttachmentValidationResult.Invalid(
                $"\"{Describe(fileName)}\" was sent as text but contains binary data.");
        }

        return AttachmentValidationResult.Valid;
    }

    /// <summary>Whether a stored content type may be rendered in the browser.</summary>
    public bool IsInlineSafeContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType) && _inlineSafeContentTypes.Contains(contentType);

    /// <summary>
    /// The content type to serve. A stored type outside the allowlist is never echoed back --
    /// the row holds whatever the client declared at upload time.
    /// </summary>
    public string ResolveServedContentType(string? storedContentType)
        => !string.IsNullOrWhiteSpace(storedContentType) && _allowedContentTypes.Contains(storedContentType)
            ? storedContentType
            : "application/octet-stream";

    /// <summary>
    /// The name to store for display: directories stripped, control characters removed, length
    /// capped. Never used to build a path.
    /// </summary>
    public string SanitizeDisplayFileName(string? fileName)
    {
        string name = fileName ?? string.Empty;

        /* Both separators, because the client's name is not a server path and may carry either. */
        int slash = name.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0)
            name = name[(slash + 1)..];

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (!char.IsControl(c))
                sb.Append(c);
        }

        name = sb.ToString().Trim();
        if (name.Length == 0)
            name = "attachment";

        return name.Length > MaxFileNameLength ? name[..MaxFileNameLength] : name;
    }

    /// <summary>
    /// The name the file is stored under: a GUID plus the validated extension, so the client's
    /// name never reaches the file system.
    /// </summary>
    public string BuildStoredFileName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(ext) || !_allowedExtensions.Contains(ext))
            ext = string.Empty;

        return Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
    }

    /// <summary>Longest base64 string that can decode to <paramref name="decodedBytes"/> or fewer.</summary>
    private static long MaxEncodedLengthFor(int decodedBytes)
        => 4L * ((decodedBytes + 2) / 3) + 4; // +4 tolerates padding and a stray newline

    private string TooLargeMessage(string? fileName)
        => $"\"{Describe(fileName)}\" is larger than the {MaxDecodedBytes / (1024 * 1024)} MB attachment limit.";

    /// <summary>
    /// The allowed extensions, lower-cased and dot-prefixed, for the file picker's
    /// <c>accept</c> attribute.
    /// </summary>
    /// <remarks>
    /// Published so the picker is <i>sourced</i> from this allowlist rather than kept in step
    /// with it by hand. The two drifting apart is not a hypothetical: it offers the user a file
    /// dialog full of formats the server then refuses, with no explanation at the point of
    /// choosing.
    /// </remarks>
    public IReadOnlyList<string> AllowedExtensions => _allowedExtensions.OrderBy(e => e).ToArray();

    /// <summary>Whether this content type is one whose bytes are legitimately binary.</summary>
    public bool IsBinaryDocumentContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType) && _binaryDocumentContentTypes.Contains(contentType);

    /* "%PDF-" and the local-file header of a ZIP, which is what an OpenXML container is. */
    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46, 0x2D };
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };

    private static bool DocumentMagicBytesAgree(string contentType, byte[] bytes)
    {
        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return StartsWith(bytes, PdfSignature);

        /* Word and Excel are both ZIP containers, so the signature cannot tell them apart --
           and it does not need to. Which one it is comes from the parts inside, read by the
           parser; the check here is only that this is a container at all and not a renamed
           executable. */
        return StartsWith(bytes, ZipSignature);
    }

    private static bool LooksLikeBinaryDocument(byte[] bytes)
        => StartsWith(bytes, PdfSignature) || StartsWith(bytes, ZipSignature);

    private static string DescribeFormat(string contentType)
        => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? "PDF"
            : "Office document";

    private static bool MagicBytesAgree(string contentType, byte[] bytes)
    {
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return StartsWith(bytes, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return StartsWith(bytes, new byte[] { 0xFF, 0xD8, 0xFF });

        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            // "RIFF" then four size bytes then "WEBP"
            return bytes.Length >= 12
                && StartsWith(bytes, new byte[] { 0x52, 0x49, 0x46, 0x46 })
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        }

        /* An image type nobody here has a signature for is not silently trusted. */
        return false;
    }

    private static bool StartsWith(byte[] bytes, byte[] signature)
    {
        if (bytes.Length < signature.Length)
            return false;

        for (int i = 0; i < signature.Length; i++)
        {
            if (bytes[i] != signature[i])
                return false;
        }

        return true;
    }

    /* Scans the first 8 KB. A NUL, or any C0 control other than tab, newline, carriage return
       and form feed, means the payload is not the text it was declared to be. */
    private static bool HasBinaryControlBytes(byte[] bytes)
    {
        int limit = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < limit; i++)
        {
            byte b = bytes[i];
            if (b == 0)
                return true;

            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D && b != 0x0C)
                return true;
        }

        return false;
    }

    /* Error messages quote the uploader's own filename back, and it reaches a client and a log,
       so control characters come out of it first. */
    private static string Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(unnamed)";

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(char.IsControl(c) ? ' ' : c);

        string s = sb.ToString().Trim();
        return s.Length > 128 ? s[..128] + "..." : s;
    }
}
