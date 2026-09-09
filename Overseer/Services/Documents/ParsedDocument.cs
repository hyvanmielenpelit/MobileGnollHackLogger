namespace Overseer.Services.Documents;

/// <summary>What a parse produced, and what it refused to include.</summary>
public sealed record ParsedDocument
{
    /// <summary>
    /// Extracted text, ready to go inside an untrusted-content wrapper. Never null.
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// True when the file was recognised and read. False means <see cref="Text"/> is empty and
    /// <see cref="Error"/> says why.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>A message safe to show the uploader. Null on success.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The parser that handled it: "pdf", "word", "excel", "csv", "text". Null on failure.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>Pages for a PDF, worksheets for a workbook, otherwise null.</summary>
    public int? PartCount { get; init; }

    /// <summary>
    /// Kinds of active content found and left out, e.g. "a VBA macro project", "an embedded OLE
    /// object", "a PDF /JavaScript action". Empty when there were none.
    /// </summary>
    /// <remarks>
    /// These are shown to the user: the removal is not silent. A user who is told nothing was
    /// dropped cannot tell an extraction that omitted the interesting part from one that had
    /// nothing to omit.
    /// </remarks>
    public IReadOnlyList<string> RemovedActiveContent { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when extraction stopped at <see cref="DocumentParserService.MaxExtractedCharacters"/>.
    /// </summary>
    public bool WasTruncated { get; init; }

    /// <summary>A refusal carrying no text and no format, only the reason.</summary>
    public static ParsedDocument Failed(string error) => new() { Succeeded = false, Error = error };
}
