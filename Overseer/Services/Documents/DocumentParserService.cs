using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Tokens;

/* Both Open XML namespaces declare Text, and Spreadsheet declares Sheet against PdfPig's own
   Word; aliases keep every element reference unambiguous at the point of use. */
using PdfWord = UglyToad.PdfPig.Content.Word;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace Overseer.Services.Documents;

/// <summary>
/// Extracts plain text from an uploaded PDF, Word document, workbook, CSV or text file, and
/// reports the active content it left behind.
/// </summary>
/// <remarks>
/// Nothing here executes document content: PdfPig and the Open XML SDK are managed readers with
/// no scripting host, and a macro or OLE part is never opened at all. What this class adds on
/// top of them is the reporting of what was skipped, the spreadsheet formula neutralisation, and
/// a bound on extracted characters -- the bound is on characters rather than input bytes because
/// a small compressed file can expand a very long way.
/// </remarks>
public sealed class DocumentParserService
{
    private const int DefaultMaxExtractedCharacters = 2_000_000;

    /// <summary>Separator between table cells, worksheet cells and CSV fields.</summary>
    private const string CellSeparator = " | ";

    private const string LineBreak = "\n";

    /* Words whose baselines fall within this many points of each other count as one row. */
    private const double PdfRowTolerance = 3.0;

    /* Bounds on the catalog walk. A hostile PDF can nest dictionaries as deep and as wide as it
       likes, and this scan exists to name what is in the file, not to explore all of it. */
    private const int MaxPdfTokenNodes = 20_000;
    private const int MaxPdfTokenDepth = 16;

    /* Entries read from a package's central directory before the scan gives up. */
    private const int MaxContainerEntries = 5_000;

    /* Generous, because the shared string table holds every distinct string in the workbook and
       the row loop applies the real bound anyway. */
    private const long SharedStringCharacterCeiling = 64L * 1024 * 1024;

    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46, 0x2D };      // "%PDF-"
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };            // "PK\x03\x04"

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf32LittleEndianBom = { 0xFF, 0xFE, 0x00, 0x00 };
    private static readonly byte[] Utf32BigEndianBom = { 0x00, 0x00, 0xFE, 0xFF };
    private static readonly byte[] Utf16LittleEndianBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BigEndianBom = { 0xFE, 0xFF };

    /* Deliberately excludes .js, .ps1 and .sh: AttachmentValidator refuses those outright, and a
       parser that claimed to handle them would read as permission they do not have. */
    private static readonly Dictionary<string, DocumentKind> ExtensionKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = DocumentKind.Pdf,
            [".docx"] = DocumentKind.Word,
            [".docm"] = DocumentKind.Word,
            [".xlsx"] = DocumentKind.Excel,
            [".xlsm"] = DocumentKind.Excel,
            [".csv"] = DocumentKind.Csv,
            [".tsv"] = DocumentKind.Csv,
            [".txt"] = DocumentKind.Text,
            [".md"] = DocumentKind.Text,
            [".markdown"] = DocumentKind.Text,
            [".log"] = DocumentKind.Text,
            [".json"] = DocumentKind.Text,
            [".xml"] = DocumentKind.Text,
            [".html"] = DocumentKind.Text,
            [".htm"] = DocumentKind.Text,
            [".yml"] = DocumentKind.Text,
            [".yaml"] = DocumentKind.Text,
            [".ini"] = DocumentKind.Text,
            [".cfg"] = DocumentKind.Text,
            [".cs"] = DocumentKind.Text,
            [".ts"] = DocumentKind.Text,
            [".py"] = DocumentKind.Text,
            [".c"] = DocumentKind.Text,
            [".h"] = DocumentKind.Text,
            [".cpp"] = DocumentKind.Text,
            [".hpp"] = DocumentKind.Text,
            [".sql"] = DocumentKind.Text
        };

    private static readonly Dictionary<string, DocumentKind> ContentTypeKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = DocumentKind.Pdf,
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = DocumentKind.Word,
            ["application/vnd.ms-word.document.macroenabled.12"] = DocumentKind.Word,
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = DocumentKind.Excel,
            ["application/vnd.ms-excel.sheet.macroenabled.12"] = DocumentKind.Excel,
            ["text/csv"] = DocumentKind.Csv,
            ["text/tab-separated-values"] = DocumentKind.Csv,
            ["application/json"] = DocumentKind.Text,
            ["application/xml"] = DocumentKind.Text
        };

    /* Catalog entries that carry an action. /JavaScript and /Launch also occur as the value of an
       action's /S, so both keys and name values are matched. */
    private static readonly (string Token, string Description)[] PdfActionTokens =
    {
        ("JavaScript", "a PDF /JavaScript action"),
        ("JS", "a PDF /JS action"),
        ("OpenAction", "a PDF /OpenAction entry"),
        ("AA", "a PDF /AA additional-actions entry"),
        ("Launch", "a PDF /Launch action")
    };

    private readonly ILogger<DocumentParserService>? _logger;

    public DocumentParserService(IConfiguration configuration, ILogger<DocumentParserService>? logger = null)
    {
        _logger = logger;

        int max = configuration.GetValue("DocumentSettings:MaxExtractedCharacters", DefaultMaxExtractedCharacters);
        MaxExtractedCharacters = max > 0 ? max : DefaultMaxExtractedCharacters;
    }

    /// <summary>
    /// Upper bound on extracted text, from <c>DocumentSettings:MaxExtractedCharacters</c>
    /// (default 2,000,000).
    /// </summary>
    public int MaxExtractedCharacters { get; }

    /// <summary>Whether this content type or extension has a parser at all.</summary>
    /// <remarks>
    /// Judges the declaration alone, because that is all a caller has before reading the bytes.
    /// <see cref="ParseAsync"/> is the stricter of the two: it also sniffs the content, so it can
    /// parse a file whose declaration says nothing useful and can refuse one whose declaration
    /// the bytes contradict.
    /// </remarks>
    public bool CanParse(string? contentType, string? fileName) => DeclaredKind(contentType, fileName) != DocumentKind.None;

    /// <summary>
    /// Extracts text. Never throws for a malformed or hostile document: a failure is a
    /// <see cref="ParsedDocument"/> with <see cref="ParsedDocument.Succeeded"/> false.
    /// </summary>
    public Task<ParsedDocument> ParseAsync(
        byte[] content, string? contentType, string? fileName, CancellationToken cancellationToken)
    {
        /* Every format here is CPU- and allocation-bound with no I/O of its own, so the work goes
           to the thread pool rather than pretending to be asynchronous on the request thread. */
        return Task.Run(() => Parse(content, contentType, fileName, cancellationToken), cancellationToken);
    }

    private ParsedDocument Parse(byte[] content, string? contentType, string? fileName, CancellationToken cancellationToken)
    {
        if (content == null || content.Length == 0)
            return ParsedDocument.Failed("The file is empty.");

        DocumentKind declared = DeclaredKind(contentType, fileName);
        DocumentKind kind = declared;
        IReadOnlyList<string> removed = Array.Empty<string>();

        try
        {
            /* The sniff comes first because both the content type and the extension are supplied
               by the client. A .txt that is really a ZIP must not be read as text, and a text file
               named .pdf must not be handed to a PDF parser. The declaration only breaks the ties
               the bytes cannot: CSV against plain text. Word against Excel is settled by the
               container's own parts, not by the extension. */
            ContainerScan sniffed = Sniff(content);
            if (sniffed.Refusal != null)
                return Fail(declared, contentType, fileName, sniffed.Refusal);

            if (sniffed.Kind != DocumentKind.None)
            {
                kind = sniffed.Kind;
                removed = sniffed.ActiveContent;

                if (sniffed.DeclaredUncompressedBytes > DecompressedByteCeiling)
                    return Fail(kind, contentType, fileName, $"The {Describe(kind)} is too large to extract text from.");
            }
            else if (declared is DocumentKind.Pdf or DocumentKind.Word or DocumentKind.Excel)
            {
                /* Declared a binary format that the bytes contradict. Refusing is the honest
                   answer: reading it as text instead would hand the model a page of mojibake and
                   call it a document. */
                return Fail(declared, contentType, fileName,
                    $"The file is not the {Describe(declared)} its name or type claims to be.");
            }

            if (kind == DocumentKind.None)
                return Fail(kind, contentType, fileName, "This file type is not supported for text extraction.");

            return kind switch
            {
                DocumentKind.Pdf => ParsePdf(content, cancellationToken),
                DocumentKind.Word => ParseWord(content, removed, cancellationToken),
                DocumentKind.Excel => ParseExcel(content, removed, cancellationToken),
                DocumentKind.Csv => ParseCsv(content),
                _ => ParseText(content)
            };
        }
        catch (OperationCanceledException)
        {
            /* Cancellation is the caller giving up, not a bad document; swallowing it into a
               failure would hide a shutdown behind a message about the file. */
            throw;
        }
        catch (PdfDocumentEncryptedException)
        {
            return Fail(kind, contentType, fileName, "The PDF is password-protected, so its text cannot be read.");
        }
        catch (Exception ex)
        {
            return Fail(kind, contentType, fileName, $"The {Describe(kind)} could not be read: it may be damaged or incomplete.", ex);
        }
    }

    /* A package that claims more than this once expanded is refused before a byte is inflated.
       The floor keeps a small configured character bound -- a test's, or a deliberately tight
       deployment's -- from rejecting ordinary documents, whose XML overhead dwarfs their text. */
    private long DecompressedByteCeiling => Math.Max(64L * MaxExtractedCharacters, 32L * 1024 * 1024);

    private ParsedDocument Fail(
        DocumentKind kind, string? contentType, string? fileName, string error, Exception? exception = null)
    {
        /* The format, the declared type and the extension: enough to tell which parser gave up on
           what kind of file, and nothing of the document's contents. */
        if (exception != null)
        {
            _logger?.LogWarning(
                exception, "Document text extraction failed. Format={Format}, ContentType={ContentType}, Extension={Extension}",
                FormatName(kind) ?? "unknown", NormaliseContentType(contentType), SafeExtension(fileName));
        }
        else
        {
            _logger?.LogWarning(
                "Document text extraction refused: {Reason} Format={Format}, ContentType={ContentType}, Extension={Extension}",
                error, FormatName(kind) ?? "unknown", NormaliseContentType(contentType), SafeExtension(fileName));
        }

        return ParsedDocument.Failed(error);
    }

    // ---- format dispatch -------------------------------------------------------------------

    private enum DocumentKind
    {
        None,
        Pdf,
        Word,
        Excel,
        Csv,
        Text
    }

    /// <param name="Kind">What the bytes are, or None when they carry no signature this class knows.</param>
    /// <param name="Refusal">Set when the bytes are recognisable but unsupported.</param>
    /// <param name="ActiveContent">Active content named in the package's part list.</param>
    /// <param name="DeclaredUncompressedBytes">What the central directory claims the package expands to.</param>
    private sealed record ContainerScan(
        DocumentKind Kind,
        string? Refusal,
        IReadOnlyList<string> ActiveContent,
        long DeclaredUncompressedBytes)
    {
        public static readonly ContainerScan Unrecognised =
            new(DocumentKind.None, null, Array.Empty<string>(), 0);
    }

    private static ContainerScan Sniff(byte[] content)
    {
        if (StartsWith(content, PdfSignature))
            return new ContainerScan(DocumentKind.Pdf, null, Array.Empty<string>(), 0);

        if (StartsWith(content, ZipSignature))
            return ScanOpenXmlContainer(content);

        return ContainerScan.Unrecognised;
    }

    /// <summary>
    /// Reads a package's part list to decide Word from Excel and to name the active content, all
    /// from the central directory so nothing is decompressed.
    /// </summary>
    private static ContainerScan ScanOpenXmlContainer(byte[] content)
    {
        var kind = DocumentKind.None;
        var active = new List<string>();
        long uncompressed = 0;
        bool isPresentation = false;

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            int examined = 0;
            foreach (var entry in archive.Entries)
            {
                if (++examined > MaxContainerEntries)
                    break;

                string path = entry.FullName.Replace('\\', '/');
                uncompressed += entry.Length;

                if (kind == DocumentKind.None)
                {
                    if (path.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                        kind = DocumentKind.Word;
                    else if (path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                        kind = DocumentKind.Excel;
                    else if (path.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                        isPresentation = true;
                }

                /* The macro and OLE parts are named, never opened: reading one is the only way to
                   be hurt by it, and the text extraction has no use for its bytes. */
                if (path.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                    AddOnce(active, "a VBA macro project");
                else if (path.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("oleObject", StringComparison.OrdinalIgnoreCase))
                {
                    /* Both spellings, because a part's folder and its name each identify an
                       embedded object on their own and a hand-built package may carry only one. */
                    AddOnce(active, "an embedded OLE object");
                }
                else if (path.Contains("/activeX/", StringComparison.OrdinalIgnoreCase))
                    AddOnce(active, "an ActiveX control");
                else if (path.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase))
                    AddOnce(active, "an Excel 4.0 macro sheet");
            }
        }
        catch (Exception)
        {
            return new ContainerScan(
                DocumentKind.None, "The file looks like a ZIP archive but could not be opened.", Array.Empty<string>(), 0);
        }

        if (kind == DocumentKind.None)
        {
            string refusal = isPresentation
                ? "PowerPoint presentations are not supported for text extraction."
                : "The file is a ZIP archive rather than a Word document or workbook.";
            return new ContainerScan(DocumentKind.None, refusal, Array.Empty<string>(), 0);
        }

        return new ContainerScan(kind, null, active, uncompressed);
    }

    private static DocumentKind DeclaredKind(string? contentType, string? fileName)
    {
        string extension = SafeExtension(fileName);
        if (extension.Length > 0 && ExtensionKinds.TryGetValue(extension, out DocumentKind byExtension))
            return byExtension;

        string type = NormaliseContentType(contentType);
        if (type.Length == 0)
            return DocumentKind.None;

        if (ContentTypeKinds.TryGetValue(type, out DocumentKind byContentType))
            return byContentType;

        return type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? DocumentKind.Text : DocumentKind.None;
    }

    // ---- PDF -------------------------------------------------------------------------------

    private ParsedDocument ParsePdf(byte[] content, CancellationToken cancellationToken)
    {
        var options = new ParsingOptions
        {
            /* A broken or hostile file is the expected case, so recover where recovery is
               possible: a missing font costs that page's word positions, not the whole parse.
               Password stays unset -- an encrypted document is a refusal, not something to
               guess at. */
            UseLenientParsing = true,
            SkipMissingFonts = true,
            ClipPaths = false
        };

        using var document = PdfDocument.Open(content, options);

        var text = new BoundedTextBuilder(MaxExtractedCharacters);
        var removed = CollectPdfActiveContent(document);
        int pages = document.NumberOfPages;

        for (int page = 1; page <= pages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.IsFull)
                break;

            /* The marker is what lets an answer cite a page, so it is emitted even for a page
               whose own content stream turns out to be unreadable. */
            text.AppendLine($"[Page {page.ToString(CultureInfo.InvariantCulture)}]");

            try
            {
                AppendPdfPage(document, page, text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                /* One damaged page does not lose the other nine. */
                _logger?.LogWarning(ex, "A PDF page could not be read. Page={Page}", page);
            }
        }

        return new ParsedDocument
        {
            Text = text.ToString(),
            Succeeded = true,
            Format = "pdf",
            PartCount = pages,
            RemovedActiveContent = removed,
            WasTruncated = text.WasTruncated
        };
    }

    private static void AppendPdfPage(PdfDocument document, int pageNumber, BoundedTextBuilder text)
    {
        var page = document.GetPage(pageNumber);

        bool wroteAnything = false;
        foreach (string row in GroupWordsIntoRows(page.GetWords()))
        {
            text.AppendLine(row);
            wroteAnything = true;
            if (text.IsFull)
                return;
        }

        /* No word positions -- a page drawn with a font PdfPig could not load, most often. The
           raw letter run is worse to read but better than nothing. */
        if (!wroteAnything)
            text.AppendLine(page.Text);
    }

    /// <summary>
    /// Reconstructs rows from word positions: words sharing a baseline, top to bottom, each row
    /// left to right.
    /// </summary>
    /// <remarks>
    /// A heuristic, not a table recogniser. It keeps a simple table's columns on one line, which
    /// is what makes a value readable next to its heading; spanned cells, nested tables and
    /// multi-column page layout it will get wrong.
    /// </remarks>
    private static IEnumerable<string> GroupWordsIntoRows(IEnumerable<PdfWord> words)
    {
        var ordered = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var row = new List<PdfWord>();
        double baseline = 0;

        foreach (var word in ordered)
        {
            if (row.Count == 0)
            {
                baseline = word.BoundingBox.Bottom;
                row.Add(word);
                continue;
            }

            if (Math.Abs(baseline - word.BoundingBox.Bottom) <= PdfRowTolerance)
            {
                row.Add(word);
                continue;
            }

            yield return JoinRow(row);
            row.Clear();
            baseline = word.BoundingBox.Bottom;
            row.Add(word);
        }

        if (row.Count > 0)
            yield return JoinRow(row);

        static string JoinRow(List<PdfWord> row)
            => string.Join(" ", row.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
    }

    /// <summary>
    /// Names the actions the catalog carries: <c>/JavaScript</c>, <c>/JS</c>, <c>/OpenAction</c>,
    /// <c>/AA</c> and <c>/Launch</c>.
    /// </summary>
    /// <remarks>
    /// These are reported, not neutralised. PdfPig gives no execution of any kind, and the text
    /// extraction never carries an action into the prompt either way -- so there is nothing here
    /// to disarm, only something the uploader deserves to be told about their file. Targets hidden
    /// behind an indirect reference are not chased: resolving references an attacker chose is
    /// work this scan has no reason to do.
    /// </remarks>
    private IReadOnlyList<string> CollectPdfActiveContent(PdfDocument document)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            int budget = MaxPdfTokenNodes;
            WalkPdfTokens(document.Structure.Catalog.CatalogDictionary, 0, found, ref budget);
        }
        catch (Exception ex)
        {
            /* A catalog too broken to walk is not a reason to lose the page text. */
            _logger?.LogWarning(ex, "The PDF catalog could not be scanned for actions.");
            return Array.Empty<string>();
        }

        return PdfActionTokens
            .Where(t => found.Contains(t.Token))
            .Select(t => t.Description)
            .ToList();
    }

    private static void WalkPdfTokens(IToken? token, int depth, ISet<string> found, ref int budget)
    {
        if (token == null || budget <= 0 || depth > MaxPdfTokenDepth)
            return;

        budget--;

        switch (token)
        {
            case DictionaryToken dictionary:
                foreach (var pair in dictionary.Data)
                {
                    if (budget <= 0)
                        return;

                    if (PdfActionTokens.Any(t => string.Equals(t.Token, pair.Key, StringComparison.OrdinalIgnoreCase)))
                        found.Add(pair.Key);

                    WalkPdfTokens(pair.Value, depth + 1, found, ref budget);
                }

                break;

            case ArrayToken array:
                foreach (var item in array.Data)
                {
                    if (budget <= 0)
                        return;

                    WalkPdfTokens(item, depth + 1, found, ref budget);
                }

                break;

            case NameToken name:
                if (PdfActionTokens.Any(t => string.Equals(t.Token, name.Data, StringComparison.OrdinalIgnoreCase)))
                    found.Add(name.Data);

                break;
        }
    }

    // ---- Word ------------------------------------------------------------------------------

    private ParsedDocument ParseWord(byte[] content, IReadOnlyList<string> removed, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);

        var main = document.MainDocumentPart;
        if (main == null)
        {
            _logger?.LogWarning("Document text extraction refused: a Word package with no main document part. Format=word");
            return ParsedDocument.Failed("The Word document has no readable body.");
        }

        var text = new BoundedTextBuilder(MaxExtractedCharacters);

        /* Streamed rather than loaded as a tree: the DOM would materialise the whole body before
           the character bound got a say, which is exactly what a compressed bomb counts on. */
        using var reader = OpenXmlReader.Create(main);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.IsFull)
                break;

            if (!reader.IsStartElement)
                continue;

            /* Table rows are matched before paragraphs on purpose: loading the row consumes the
               paragraphs inside it, so a cell's text arrives once, as part of its row, instead of
               a second time as a loose line. */
            if (reader.ElementType == typeof(Wordprocessing.TableRow))
            {
                if (reader.LoadCurrentElement() is Wordprocessing.TableRow row)
                {
                    text.AppendLine(string.Join(
                        CellSeparator, row.Elements<Wordprocessing.TableCell>().Select(cell => Collapse(cell.InnerText))));
                }
            }
            else if (reader.ElementType == typeof(Wordprocessing.Paragraph))
            {
                if (reader.LoadCurrentElement() is Wordprocessing.Paragraph paragraph)
                    text.AppendLine(paragraph.InnerText);
            }
        }

        return new ParsedDocument
        {
            Text = text.ToString(),
            Succeeded = true,
            Format = "word",
            RemovedActiveContent = removed,
            WasTruncated = text.WasTruncated
        };
    }

    // ---- Excel -----------------------------------------------------------------------------

    private ParsedDocument ParseExcel(byte[] content, IReadOnlyList<string> removed, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);

        var workbookPart = document.WorkbookPart;
        if (workbookPart == null)
        {
            _logger?.LogWarning("Document text extraction refused: a workbook package with no workbook part. Format=excel");
            return ParsedDocument.Failed("The workbook has no readable sheets.");
        }

        var text = new BoundedTextBuilder(MaxExtractedCharacters);
        var sharedStrings = LoadSharedStrings(workbookPart, cancellationToken);

        var sheets = workbookPart.Workbook?.Sheets?.Elements<Spreadsheet.Sheet>().ToList()
            ?? new List<Spreadsheet.Sheet>();

        int worksheets = 0;
        foreach (var sheet in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.IsFull)
                break;

            worksheets++;
            string name = sheet.Name?.Value ?? $"Sheet{worksheets.ToString(CultureInfo.InvariantCulture)}";
            text.AppendLine($"[Sheet: {Collapse(name)}]");

            try
            {
                if (sheet.Id?.Value is string relationshipId
                    && workbookPart.GetPartById(relationshipId) is WorksheetPart worksheetPart)
                {
                    AppendWorksheet(worksheetPart, sharedStrings, text, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                /* A sheet whose part is missing or malformed costs that sheet, not the workbook. */
                _logger?.LogWarning(ex, "A worksheet could not be read. Index={Index}", worksheets);
            }
        }

        return new ParsedDocument
        {
            Text = text.ToString(),
            Succeeded = true,
            Format = "excel",
            PartCount = worksheets,
            RemovedActiveContent = removed,
            WasTruncated = text.WasTruncated
        };
    }

    private static void AppendWorksheet(
        WorksheetPart worksheetPart,
        IReadOnlyList<string> sharedStrings,
        BoundedTextBuilder text,
        CancellationToken cancellationToken)
    {
        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.IsFull)
                return;

            if (!reader.IsStartElement || reader.ElementType != typeof(Spreadsheet.Row))
                continue;

            if (reader.LoadCurrentElement() is not Spreadsheet.Row row)
                continue;

            var cells = row.Elements<Spreadsheet.Cell>()
                .Select(cell => NeutraliseFormulaLeader(Collapse(CellText(cell, sharedStrings))))
                .ToList();

            if (cells.Count == 0 || cells.All(c => c.Length == 0))
                continue;

            text.AppendLine(string.Join(CellSeparator, cells));
        }
    }

    private static string CellText(Spreadsheet.Cell cell, IReadOnlyList<string> sharedStrings)
    {
        /* HasValue is checked rather than trusted: an unparseable t="..." attribute makes Value
           throw, and one hand-edited cell must not cost the whole workbook. */
        if (cell.DataType is { HasValue: true } declared)
        {
            var type = declared.Value;

            if (type == Spreadsheet.CellValues.SharedString)
            {
                return int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && index >= 0 && index < sharedStrings.Count
                        ? sharedStrings[index]
                        : string.Empty;
            }

            if (type == Spreadsheet.CellValues.InlineString)
                return cell.InlineString?.InnerText ?? string.Empty;

            if (type == Spreadsheet.CellValues.Boolean)
                return cell.CellValue?.Text == "0" ? "FALSE" : "TRUE";
        }

        /* The cached value, not the formula. What the sheet shows is what a reader needs, and the
           formula string is the one thing this method is trying not to pass on. */
        return cell.CellValue?.Text ?? string.Empty;
    }

    private static IReadOnlyList<string> LoadSharedStrings(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        var part = workbookPart.SharedStringTablePart;
        if (part == null)
            return Array.Empty<string>();

        var strings = new List<string>();
        long characters = 0;

        /* The table is the one place a workbook keeps all of its text together, so it is streamed
           and counted rather than loaded whole. */
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!reader.IsStartElement || reader.ElementType != typeof(Spreadsheet.SharedStringItem))
                continue;

            if (reader.LoadCurrentElement() is not Spreadsheet.SharedStringItem item)
                continue;

            string value = item.InnerText;
            strings.Add(value);

            characters += value.Length;
            if (characters > SharedStringCharacterCeiling)
                break;
        }

        return strings;
    }

    /// <summary>
    /// Prefixes a leading <c>=</c>, <c>@</c>, <c>+</c> or <c>-</c> with an apostrophe.
    /// </summary>
    /// <remarks>
    /// This is about where the text goes next, not about the model. The extracted text ends up
    /// pasted into a spreadsheet, re-exported to CSV or written to a file often enough that a cell
    /// reading <c>=cmd|' /c calc'!A1</c> has to stop being a formula here, at the only point that
    /// sees every format. The apostrophe is the spreadsheet world's own literal marker, so a
    /// reader loses nothing.
    /// </remarks>
    private static string NeutraliseFormulaLeader(string value)
    {
        if (value.Length == 0)
            return value;

        return value[0] is '=' or '@' or '+' or '-' ? "'" + value : value;
    }

    // ---- CSV -------------------------------------------------------------------------------

    private ParsedDocument ParseCsv(byte[] content)
    {
        if (!TryDecodeText(content, out string decoded, out string? error))
        {
            _logger?.LogWarning("Document text extraction refused: {Reason} Format=csv", error);
            return ParsedDocument.Failed(error ?? "The file does not appear to contain text.");
        }

        var text = new BoundedTextBuilder(MaxExtractedCharacters);
        AppendCsv(decoded, DetectDelimiter(decoded), text);

        return new ParsedDocument
        {
            Text = text.ToString(),
            Succeeded = true,
            Format = "csv",
            WasTruncated = text.WasTruncated
        };
    }

    /// <summary>Picks between comma, semicolon and tab by counting them in the header line.</summary>
    /// <remarks>
    /// The header only, and outside quotes: a body row can hold a stray semicolon in prose, while
    /// the header is where the file declares its own shape.
    /// </remarks>
    private static char DetectDelimiter(string text)
    {
        int end = text.IndexOfAny(new[] { '\r', '\n' });
        string header = end >= 0 ? text[..end] : text;

        int commas = 0, semicolons = 0, tabs = 0;
        bool inQuotes = false;

        foreach (char c in header)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
                continue;

            if (c == ',')
                commas++;
            else if (c == ';')
                semicolons++;
            else if (c == '\t')
                tabs++;
        }

        if (tabs > commas && tabs > semicolons)
            return '\t';

        if (semicolons > commas && semicolons >= tabs)
            return ';';

        return ',';
    }

    /* RFC 4180 as it is actually written by exporters: quoted fields, a doubled quote for a
       literal one, and delimiters or newlines inside the quotes. */
    private static void AppendCsv(string text, char delimiter, BoundedTextBuilder builder)
    {
        var field = new StringBuilder();
        var fields = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (builder.IsFull)
                return;

            char c = text[i];

            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            if (c == '"')
                inQuotes = true;
            else if (c == delimiter)
                Flush();
            else if (c == '\n')
                EmitRecord();
            else if (c != '\r')
                field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0)
            EmitRecord();

        void Flush()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EmitRecord()
        {
            Flush();
            builder.AppendLine(string.Join(
                CellSeparator, fields.Select(f => NeutraliseFormulaLeader(Collapse(f)))));
            fields.Clear();
        }
    }

    // ---- text and code ---------------------------------------------------------------------

    private ParsedDocument ParseText(byte[] content)
    {
        if (!TryDecodeText(content, out string decoded, out string? error))
        {
            _logger?.LogWarning("Document text extraction refused: {Reason} Format=text", error);
            return ParsedDocument.Failed(error ?? "The file does not appear to contain text.");
        }

        var text = new BoundedTextBuilder(MaxExtractedCharacters);
        text.Append(decoded);

        return new ParsedDocument
        {
            Text = text.ToString(),
            Succeeded = true,
            Format = "text",
            WasTruncated = text.WasTruncated
        };
    }

    /// <summary>
    /// Decodes bytes as text, honouring a UTF-8, UTF-16 or UTF-32 byte-order mark and falling
    /// back to UTF-8 with replacement.
    /// </summary>
    /// <remarks>
    /// Replacement rather than an exception, because a file with one bad byte is still worth
    /// reading. The count of replacement and NUL characters afterwards is what separates that
    /// from a file that is not text at all: a UTF-16 file sent without its mark, or a binary blob
    /// with a text extension, decodes to almost nothing but those two.
    /// </remarks>
    private static bool TryDecodeText(byte[] content, out string text, out string? error)
    {
        int offset;
        Encoding encoding;

        /* UTF-32LE before UTF-16LE: its mark begins with the same two bytes. */
        if (StartsWith(content, Utf8Bom))
        {
            encoding = new UTF8Encoding(false);
            offset = Utf8Bom.Length;
        }
        else if (StartsWith(content, Utf32LittleEndianBom))
        {
            encoding = new UTF32Encoding(false, false);
            offset = Utf32LittleEndianBom.Length;
        }
        else if (StartsWith(content, Utf32BigEndianBom))
        {
            encoding = new UTF32Encoding(true, false);
            offset = Utf32BigEndianBom.Length;
        }
        else if (StartsWith(content, Utf16LittleEndianBom))
        {
            encoding = new UnicodeEncoding(false, false);
            offset = Utf16LittleEndianBom.Length;
        }
        else if (StartsWith(content, Utf16BigEndianBom))
        {
            encoding = new UnicodeEncoding(true, false);
            offset = Utf16BigEndianBom.Length;
        }
        else
        {
            encoding = new UTF8Encoding(false);
            offset = 0;
        }

        text = encoding.GetString(content, offset, content.Length - offset);

        // U+FFFD, the replacement character every fallback above produces for a byte it cannot read.
        int undecodable = 0;
        foreach (char c in text)
        {
            if (c == '\uFFFD' || c == '\0')
                undecodable++;
        }

        if (text.Length > 0 && undecodable > Math.Max(4, text.Length / 10))
        {
            text = string.Empty;
            error = "The file does not appear to contain text.";
            return false;
        }

        error = null;
        return true;
    }

    // ---- shared helpers --------------------------------------------------------------------

    /// <summary>Accumulates text up to a character ceiling, recording whether it clipped.</summary>
    private sealed class BoundedTextBuilder
    {
        private readonly StringBuilder _builder = new();
        private readonly int _maximum;

        public BoundedTextBuilder(int maximumCharacters) => _maximum = Math.Max(1, maximumCharacters);

        /// <summary>True once something had to be left out.</summary>
        public bool WasTruncated { get; private set; }

        /// <summary>
        /// True when nothing more will fit, so a caller can stop producing text instead of
        /// producing it to be thrown away.
        /// </summary>
        public bool IsFull => _builder.Length >= _maximum;

        public void Append(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            int room = _maximum - _builder.Length;
            if (room <= 0)
            {
                WasTruncated = true;
                return;
            }

            if (value.Length > room)
            {
                _builder.Append(value, 0, room);
                WasTruncated = true;
                return;
            }

            _builder.Append(value);
        }

        public void AppendLine(string? value)
        {
            Append(value);
            Append(LineBreak);
        }

        public override string ToString() => _builder.ToString();
    }

    /* Cell and field text has to stay on its own line for the separators to mean anything, and
       the trim is what lets NeutraliseFormulaLeader see the real first character rather than a
       space in front of it. */
    private static string Collapse(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
            builder.Append(c is '\r' or '\n' or '\t' ? ' ' : c);

        return builder.ToString().Trim();
    }

    private static void AddOnce(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
            values.Add(value);
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

    private static string SafeExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        try
        {
            return Path.GetExtension(fileName);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /* The declared type arrives as the client wrote it, parameters and all: "text/csv; charset=utf-8". */
    private static string NormaliseContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        int semicolon = contentType.IndexOf(';');
        string type = semicolon >= 0 ? contentType[..semicolon] : contentType;
        return type.Trim();
    }

    private static string? FormatName(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "pdf",
        DocumentKind.Word => "word",
        DocumentKind.Excel => "excel",
        DocumentKind.Csv => "csv",
        DocumentKind.Text => "text",
        _ => null
    };

    private static string Describe(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "PDF",
        DocumentKind.Word => "Word document",
        DocumentKind.Excel => "workbook",
        DocumentKind.Csv => "CSV file",
        DocumentKind.Text => "text file",
        _ => "file"
    };
}
