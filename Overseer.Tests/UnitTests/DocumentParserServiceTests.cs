using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Documents;
using Xunit;

using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The properties under test throughout: what the bytes are decides which parser runs, active
/// content is named rather than silently dropped, extraction stays inside its character bound,
/// and no document -- however malformed or hostile -- makes the parser throw.
/// </summary>
/// <remarks>
/// Every fixture is built in memory. No binary test asset is committed, so there is nothing in
/// the repository whose contents cannot be read from this file.
/// </remarks>
public class DocumentParserServiceTests
{
    private const string WordContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---- happy paths -----------------------------------------------------------------------

    [Fact]
    public async Task APdfYieldsItsTextPageByPageWithAPageMarker()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            BuildTwoPagePdf(), "application/pdf", "dungeon.pdf", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("pdf", result.Format);
        Assert.Equal(2, result.PartCount);
        Assert.Contains("[Page 1]", result.Text);
        Assert.Contains("[Page 2]", result.Text);
        Assert.Contains("Dungeon level one", result.Text);
        Assert.Contains("Dungeon level two", result.Text);

        /* "Gnoll" and "12" sit on one baseline 200 points apart. The row grouping has to bring
           them onto the same line, because that is what puts a value next to its heading; how
           many words PdfPig makes of the gap between them is not this test's business. */
        Assert.Contains(
            result.Text.Split('\n'),
            line => line.Contains("Gnoll", StringComparison.Ordinal) && line.Contains("12", StringComparison.Ordinal));

        // Page order, because an answer that cites page 2 has to mean the second page.
        Assert.True(
            result.Text.IndexOf("[Page 1]", StringComparison.Ordinal)
            < result.Text.IndexOf("[Page 2]", StringComparison.Ordinal));

        Assert.Empty(result.RemovedActiveContent);
        Assert.False(result.WasTruncated);
    }

    [Fact]
    public async Task AWordDocumentYieldsParagraphsInOrderAndTableCellsRowByRow()
    {
        var parser = CreateParser();
        byte[] document = BuildWordDocument(
            new[] { "First paragraph.", "Second paragraph." },
            tableRows: new[]
            {
                new[] { "Monster", "Level" },
                new[] { "Gnoll", "3" }
            });

        var result = await parser.ParseAsync(document, WordContentType, "notes.docx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("word", result.Format);

        // PartCount counts pages and worksheets; a Word document has neither.
        Assert.False(result.PartCount.HasValue);

        Assert.Contains("First paragraph.", result.Text);
        Assert.Contains("Monster | Level", result.Text);
        Assert.Contains("Gnoll | 3", result.Text);

        /* A cell's paragraph must arrive as part of its row and not a second time as a loose
           line -- the reader loads the row, which consumes the paragraphs inside it. */
        Assert.Equal(1, CountOccurrences(result.Text, "Gnoll"));

        Assert.True(
            result.Text.IndexOf("First paragraph.", StringComparison.Ordinal)
            < result.Text.IndexOf("Second paragraph.", StringComparison.Ordinal));

        Assert.Empty(result.RemovedActiveContent);
    }

    [Fact]
    public async Task AWorkbookYieldsEachSheetUnderItsNameWithSharedStringsResolved()
    {
        var parser = CreateParser();
        byte[] workbook = BuildWorkbook(new[]
        {
            ("Monsters", new[] { new[] { "Name", "Level" }, new[] { "Gnoll", "3" } }),
            ("Items", new[] { new[] { "Name" }, new[] { "Wand of digging" } })
        });

        var result = await parser.ParseAsync(workbook, ExcelContentType, "data.xlsx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("excel", result.Format);
        Assert.Equal(2, result.PartCount);
        Assert.Contains("[Sheet: Monsters]", result.Text);
        Assert.Contains("[Sheet: Items]", result.Text);

        // Every value here lives in the shared string table, so plain text proves it was resolved.
        Assert.Contains("Name | Level", result.Text);
        Assert.Contains("Gnoll | 3", result.Text);
        Assert.Contains("Wand of digging", result.Text);
    }

    [Fact]
    public async Task ACsvYieldsItsFieldsWithQuotingHonoured()
    {
        var parser = CreateParser();
        byte[] csv = Encoding.UTF8.GetBytes("name,note\r\n\"a,b\",\"c\"\"d\"\r\n");

        var result = await parser.ParseAsync(csv, "text/csv", "rows.csv", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("csv", result.Format);
        Assert.Contains("name | note", result.Text);

        /* The quoted comma stays inside its field and the doubled quote becomes one literal
           quote: the two places a naive Split would produce the wrong number of cells. */
        Assert.Contains("a,b | c\"d", result.Text);
    }

    [Fact]
    public async Task ATextFileIsReturnedVerbatim()
    {
        var parser = CreateParser();
        const string content = "line one\r\n\tindented\nlast line";

        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes(content), "text/plain", "notes.txt", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("text", result.Format);
        Assert.Equal(content, result.Text);
        Assert.False(result.PartCount.HasValue);
    }

    // ---- the bytes decide, not the declaration ---------------------------------------------

    [Fact]
    public async Task AWordDocumentDeclaredAsPlainTextStillParsesAsWord()
    {
        var parser = CreateParser();
        byte[] document = BuildWordDocument(new[] { "Sent with the wrong content type." });

        /* Both halves of the declaration are wrong -- the type and the extension -- and the
           container's own parts are what settle it. */
        var result = await parser.ParseAsync(document, "text/plain", "resume.txt", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("word", result.Format);
        Assert.Contains("Sent with the wrong content type.", result.Text);
    }

    [Fact]
    public async Task AWorkbookNamedAsAWordDocumentStillParsesAsExcel()
    {
        var parser = CreateParser();
        byte[] workbook = BuildWorkbook(new[] { ("Sheet1", new[] { new[] { "Cell value" } }) });

        // Word against Excel is decided inside the container, never by the extension.
        var result = await parser.ParseAsync(workbook, WordContentType, "data.docx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("excel", result.Format);
        Assert.Contains("Cell value", result.Text);
    }

    [Fact]
    public async Task APdfNamedAsATextFileStillParsesAsPdf()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            BuildTwoPagePdf(), "text/plain", "dungeon.txt", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("pdf", result.Format);
        Assert.Contains("Dungeon level one", result.Text);
    }

    [Fact]
    public async Task ATextFileNamedAsAPdfFailsCleanlyRatherThanBeingReadAsAPdf()
    {
        var parser = CreateParser();
        byte[] content = Encoding.UTF8.GetBytes("This is plain prose that was renamed, not a PDF.");

        var result = await parser.ParseAsync(content, "application/pdf", "report.pdf", TestContext.Current.CancellationToken);

        /* Reading it as text instead would hand the model a document that is not the one the
           name promised, with nothing to say so. */
        Assert.False(result.Succeeded);
        Assert.Null(result.Format);
        Assert.Equal(string.Empty, result.Text);
        Assert.Contains("not the PDF", result.Error);
    }

    [Fact]
    public async Task AZipThatIsNeitherWordNorExcelIsRefused()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            BuildPlainZip("hello.txt", "hello"), WordContentType, "archive.docx", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("ZIP archive", result.Error);
    }

    // ---- active content --------------------------------------------------------------------

    [Fact]
    public async Task AWordDocumentWithAMacroYieldsTextButNoMacro()
    {
        var parser = CreateParser();
        byte[] document = BuildWordDocument(new[] { "Ordinary paragraph." }, withMacro: true);

        var result = await parser.ParseAsync(document, WordContentType, "notes.docm", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("Ordinary paragraph.", result.Text);

        // Named, so the uploader knows what was left out.
        Assert.Contains("a VBA macro project", result.RemovedActiveContent);

        /* The macro part is never opened, so none of its bytes can reach the prompt. */
        Assert.DoesNotContain(MacroMarker, result.Text);
    }

    [Fact]
    public async Task AWordDocumentWithAnEmbeddedObjectYieldsTextButNoObject()
    {
        var parser = CreateParser();
        byte[] document = BuildWordDocument(new[] { "Ordinary paragraph." }, withEmbeddedObject: true);

        var result = await parser.ParseAsync(document, WordContentType, "notes.docx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("Ordinary paragraph.", result.Text);
        Assert.Contains("an embedded OLE object", result.RemovedActiveContent);
        Assert.DoesNotContain(EmbeddedObjectMarker, result.Text);
    }

    [Fact]
    public async Task AWorkbookWithAMacroYieldsCellsButNoMacro()
    {
        var parser = CreateParser();
        byte[] workbook = BuildWorkbook(
            new[] { ("Sheet1", new[] { new[] { "Ordinary cell" } }) }, withMacro: true);

        var result = await parser.ParseAsync(workbook, ExcelContentType, "data.xlsm", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("Ordinary cell", result.Text);
        Assert.Contains("a VBA macro project", result.RemovedActiveContent);
        Assert.DoesNotContain(MacroMarker, result.Text);
    }

    [Fact]
    public async Task APdfWithAJavaScriptActionReportsItAndStillYieldsThePageText()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            BuildPdfWithJavaScriptAction(), "application/pdf", "hostile.pdf", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);

        /* Reported, not neutralised: PdfPig never executes anything, and the text extraction
           does not carry the action into the prompt either way. What the user gains is being
           told their file contains one. */
        Assert.Contains("a PDF /JavaScript action", result.RemovedActiveContent);
        Assert.Contains("a PDF /JS action", result.RemovedActiveContent);
        Assert.Contains("a PDF /OpenAction entry", result.RemovedActiveContent);

        Assert.Contains("Dungeon level one", result.Text);
        Assert.DoesNotContain("this is the script body", result.Text);
    }

    // ---- formula and DDE neutralisation ----------------------------------------------------

    [Theory]
    [InlineData("=cmd|' /c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-2-2")]
    [InlineData("@SUM(A1)")]
    public async Task AWorkbookCellThatStartsLikeAFormulaComesOutApostrophePrefixed(string cell)
    {
        var parser = CreateParser();
        byte[] workbook = BuildWorkbook(new[] { ("Sheet1", new[] { new[] { cell } }) });

        var result = await parser.ParseAsync(workbook, ExcelContentType, "data.xlsx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);

        /* The apostrophe is for where the text goes next -- a spreadsheet it is pasted into, or
           a tool that re-exports it -- not for the model, which reads either form the same way. */
        Assert.Contains("'" + cell, result.Text);
    }

    [Fact]
    public async Task AWorkbookCellWithAnEqualsSignInTheMiddleIsLeftAlone()
    {
        var parser = CreateParser();
        byte[] workbook = BuildWorkbook(new[] { ("Sheet1", new[] { new[] { "a=b", "x + y" } }) });

        var result = await parser.ParseAsync(workbook, ExcelContentType, "data.xlsx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("a=b", result.Text);

        // Only the leading character matters; a formula-shaped middle is ordinary text.
        Assert.DoesNotContain("'a=b", result.Text);
        Assert.DoesNotContain("'x + y", result.Text);
    }

    [Fact]
    public async Task ACsvFieldThatStartsLikeAFormulaComesOutApostrophePrefixed()
    {
        var parser = CreateParser();
        byte[] csv = Encoding.UTF8.GetBytes("formula,plain\r\n=1+1,ordinary\r\n");

        var result = await parser.ParseAsync(csv, "text/csv", "rows.csv", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("'=1+1 | ordinary", result.Text);
    }

    // ---- CSV delimiter detection -----------------------------------------------------------

    [Theory]
    [InlineData("name;note\r\nGnoll;3\r\n")]
    [InlineData("name\tnote\r\nGnoll\t3\r\n")]
    [InlineData("name,note\r\nGnoll,3\r\n")]
    public async Task ACsvDelimiterIsDetectedFromTheHeaderLine(string content)
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes(content), "text/csv", "rows.csv", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("name | note", result.Text);
        Assert.Contains("Gnoll | 3", result.Text);
    }

    [Fact]
    public async Task ACsvWithAnUnterminatedQuoteStillReturnsWhatItCanRead()
    {
        var parser = CreateParser();
        byte[] csv = Encoding.UTF8.GetBytes("name,note\r\n\"never closed,and the rest\r\n");

        var result = await parser.ParseAsync(csv, "text/csv", "rows.csv", TestContext.Current.CancellationToken);

        // Malformed, but not a reason to refuse: the header parsed and the rest is one field.
        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("name | note", result.Text);
        Assert.Contains("never closed,and the rest", result.Text);
    }

    // ---- text decoding ---------------------------------------------------------------------

    [Fact]
    public async Task EveryByteOrderMarkIsHonouredAndNotLeftInTheText()
    {
        var parser = CreateParser();
        const string expected = "Gnoll";

        var withMarks = new (string Name, byte[] Bytes)[]
        {
            ("utf-8", Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(expected))),
            ("utf-16le", Concat(new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false).GetBytes(expected))),
            ("utf-16be", Concat(new byte[] { 0xFE, 0xFF }, new UnicodeEncoding(true, false).GetBytes(expected))),
            ("utf-32le", Concat(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, new UTF32Encoding(false, false).GetBytes(expected))),
            ("utf-32be", Concat(new byte[] { 0x00, 0x00, 0xFE, 0xFF }, new UTF32Encoding(true, false).GetBytes(expected)))
        };

        foreach (var (name, bytes) in withMarks)
        {
            var result = await parser.ParseAsync(bytes, "text/plain", "notes.txt", TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, name + ": " + result.Error);

            /* The mark is consumed rather than decoded: a leading U+FEFF in the prompt is a
               character the model has to guess at, and the UTF-32 mark begins with the same two
               bytes as the UTF-16 one, so the order the marks are tested in matters. */
            Assert.Equal(expected, result.Text);
        }
    }

    [Fact]
    public async Task AFileThatIsNotTextAtAllFailsCleanlyRatherThanReturningGarbage()
    {
        var parser = CreateParser();

        // 0xFF is never a valid UTF-8 byte, so the whole payload decodes to replacements.
        byte[] content = Enumerable.Repeat((byte)0xFF, 256).ToArray();

        var result = await parser.ParseAsync(content, "text/plain", "notes.txt", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Text);
        Assert.Contains("does not appear to contain text", result.Error);
    }

    [Fact]
    public async Task AFileWithOneBadByteIsStillReadRatherThanRefused()
    {
        var parser = CreateParser();
        byte[] content = Concat(Encoding.UTF8.GetBytes("A long line of perfectly ordinary prose"), new byte[] { 0xFF });

        var result = await parser.ParseAsync(content, "text/plain", "notes.txt", TestContext.Current.CancellationToken);

        // Replacement, not an exception: one damaged byte does not make a file unreadable.
        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("perfectly ordinary prose", result.Text);
    }

    // ---- bounds ----------------------------------------------------------------------------

    [Fact]
    public void MaxExtractedCharactersComesFromConfigurationAndDefaultsToTwoMillion()
    {
        Assert.Equal(2_000_000, CreateParser().MaxExtractedCharacters);
        Assert.Equal(40, CreateParser(40).MaxExtractedCharacters);

        // A nonsense bound falls back rather than making every parse return nothing.
        Assert.Equal(2_000_000, CreateParser(0).MaxExtractedCharacters);
    }

    [Fact]
    public async Task ATextFileLongerThanTheBoundIsTruncatedAndSaysSo()
    {
        var parser = CreateParser(40);

        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes(new string('x', 500)), "text/plain", "notes.txt", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.WasTruncated);
        Assert.Equal(40, result.Text.Length);
    }

    [Fact]
    public async Task AWordDocumentLongerThanTheBoundStopsAtTheBound()
    {
        var parser = CreateParser(40);
        byte[] document = BuildWordDocument(Enumerable.Range(0, 50).Select(i => "Paragraph number " + i));

        var result = await parser.ParseAsync(document, WordContentType, "notes.docx", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.WasTruncated);

        /* The bound is applied as the text is produced, not to the finished string: a package
           that expands a thousandfold must not be materialised first and trimmed afterwards. */
        Assert.True(result.Text.Length <= 40, "Extracted " + result.Text.Length + " characters for a bound of 40.");
    }

    // ---- malformed and hostile input never throws ------------------------------------------

    [Fact]
    public async Task AnEmptyFileIsACleanFailure()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            Array.Empty<byte>(), "text/plain", "notes.txt", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("empty", result.Error);
    }

    [Theory]
    [InlineData("application/pdf", "a.pdf", false)]
    [InlineData(WordContentType, "a.docx", false)]
    [InlineData(ExcelContentType, "a.xlsx", false)]
    [InlineData("text/csv", "a.csv", true)]
    [InlineData("text/plain", "a.txt", true)]
    public async Task ASingleByteIsAnsweredRatherThanThrownForEveryFormat(
        string contentType, string fileName, bool expected)
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            new byte[] { 0x50 }, contentType, fileName, TestContext.Current.CancellationToken);

        /* One byte is the cheapest hostile input there is. A binary format refuses it because the
           signature cannot be there; a text format reads it, because one printable byte is a
           perfectly good one-character file. */
        Assert.Equal(expected, result.Succeeded);
        if (!expected)
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ATruncatedPdfIsACleanFailure()
    {
        var parser = CreateParser();
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog");

        var result = await parser.ParseAsync(content, "application/pdf", "broken.pdf", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Text);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));

        /* The message is written for the uploader, so nothing of the exception behind it shows
           through -- no type name, no stack frame, no path from this machine. */
        Assert.DoesNotContain("Exception", result.Error);
        Assert.DoesNotContain("UglyToad", result.Error);
        Assert.DoesNotContain("\\", result.Error);
    }

    [Fact]
    public async Task ATruncatedOpenXmlPackageIsACleanFailure()
    {
        var parser = CreateParser();
        byte[] document = BuildWordDocument(new[] { "Complete for now." });
        byte[] truncated = document.Take(document.Length / 2).ToArray();

        var result = await parser.ParseAsync(truncated, WordContentType, "broken.docx", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Text);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task BytesThatOnlyBeginLikeAZipAreACleanFailure()
    {
        var parser = CreateParser();
        byte[] content = Concat(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, Encoding.ASCII.GetBytes("and then nothing useful"));

        var result = await parser.ParseAsync(content, WordContentType, "broken.docx", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task BinaryBytesSentAsCsvAreACleanFailure()
    {
        var parser = CreateParser();
        byte[] content = Enumerable.Repeat((byte)0xFF, 256).ToArray();

        var result = await parser.ParseAsync(content, "text/csv", "rows.csv", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("does not appear to contain text", result.Error);
    }

    [Fact]
    public async Task AnUnsupportedFileTypeIsRefusedWithoutBeingParsed()
    {
        var parser = CreateParser();

        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes("MZ binary-ish"), "application/octet-stream", "tool.bin",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.Error);
    }

    // ---- CanParse --------------------------------------------------------------------------

    [Theory]
    [InlineData("application/pdf", "a.pdf", true)]
    [InlineData(WordContentType, "a.docx", true)]
    [InlineData(ExcelContentType, "a.xlsx", true)]
    [InlineData("text/csv", "a.csv", true)]
    [InlineData("text/plain", "a.txt", true)]
    [InlineData("text/markdown", "a.md", true)]
    [InlineData("text/plain; charset=utf-8", null, true)]
    [InlineData(null, "a.docm", true)]
    [InlineData("application/octet-stream", "a.bin", false)]
    [InlineData("application/zip", "a.zip", false)]
    [InlineData("image/png", "a.png", false)]
    [InlineData(null, null, false)]
    public void CanParseJudgesTheDeclarationAlone(string? contentType, string? fileName, bool expected)
    {
        Assert.Equal(expected, CreateParser().CanParse(contentType, fileName));
    }

    [Theory]
    [InlineData("application/octet-stream", "a.bin")]
    [InlineData("application/zip", "a.zip")]
    [InlineData("image/png", "a.png")]
    public async Task WhateverCanParseRefusesParseAsyncRefusesToo(string contentType, string fileName)
    {
        var parser = CreateParser();
        Assert.False(parser.CanParse(contentType, fileName));

        /* The two must agree on what is even attempted, or a caller that pre-checks with
           CanParse gets a different answer from the one that does the work. */
        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes("some content"), contentType, fileName, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.Error);
    }

    [Theory]
    [InlineData("text/csv", "a.csv", "csv")]
    [InlineData("text/plain", "a.txt", "text")]
    [InlineData("text/markdown", "a.md", "text")]
    [InlineData(null, "a.json", "text")]
    public async Task WhatCanParseAdmitsParseAsyncActuallyAttempts(string? contentType, string fileName, string format)
    {
        var parser = CreateParser();
        Assert.True(parser.CanParse(contentType, fileName));

        var result = await parser.ParseAsync(
            Encoding.UTF8.GetBytes("a,b\r\n1,2\r\n"), contentType, fileName, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(format, result.Format);
    }

    // ---- fixtures --------------------------------------------------------------------------

    private const string MacroMarker = "VB-MACRO-PROJECT-BYTES";

    private const string EmbeddedObjectMarker = "EMBEDDED-OLE-OBJECT-BYTES";

    private static DocumentParserService CreateParser(int? maxExtractedCharacters = null)
    {
        var settings = new Dictionary<string, string?>();
        if (maxExtractedCharacters.HasValue)
        {
            settings["DocumentSettings:MaxExtractedCharacters"] =
                maxExtractedCharacters.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new DocumentParserService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static byte[] BuildWordDocument(
        IEnumerable<string> paragraphs,
        IEnumerable<string[]>? tableRows = null,
        bool withMacro = false,
        bool withEmbeddedObject = false)
    {
        using var stream = new MemoryStream();

        var documentType = withMacro
            ? WordprocessingDocumentType.MacroEnabledDocument
            : WordprocessingDocumentType.Document;

        using (var document = WordprocessingDocument.Create(stream, documentType))
        {
            var main = document.AddMainDocumentPart();
            var body = new Wordprocessing.Body();

            foreach (string paragraph in paragraphs)
            {
                body.AppendChild(new Wordprocessing.Paragraph(
                    new Wordprocessing.Run(new Wordprocessing.Text(paragraph))));
            }

            if (tableRows != null)
            {
                var table = new Wordprocessing.Table();
                foreach (string[] cells in tableRows)
                {
                    var row = new Wordprocessing.TableRow();
                    foreach (string cell in cells)
                    {
                        row.AppendChild(new Wordprocessing.TableCell(new Wordprocessing.Paragraph(
                            new Wordprocessing.Run(new Wordprocessing.Text(cell)))));
                    }

                    table.AppendChild(row);
                }

                body.AppendChild(table);
            }

            main.Document = new Wordprocessing.Document(body);

            /* The payloads are recognisable strings rather than real macro or OLE bytes: what the
               assertions need is proof that these parts were not read, and a marker proves it
               where a genuine binary would only make the fixture harder to follow. */
            if (withMacro)
                FeedPart(main.AddNewPart<VbaProjectPart>(), MacroMarker);

            if (withEmbeddedObject)
            {
                FeedPart(
                    main.AddNewPart<EmbeddedObjectPart>("application/vnd.openxmlformats-officedocument.oleObject"),
                    EmbeddedObjectMarker);
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildWorkbook(
        IEnumerable<(string Name, string[][] Rows)> sheets, bool withMacro = false)
    {
        using var stream = new MemoryStream();

        var documentType = withMacro
            ? SpreadsheetDocumentType.MacroEnabledWorkbook
            : SpreadsheetDocumentType.Workbook;

        using (var document = SpreadsheetDocument.Create(stream, documentType))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Spreadsheet.Workbook();
            var sheetList = workbookPart.Workbook.AppendChild(new Spreadsheet.Sheets());

            /* Every value goes into the shared string table, which is where a real exporter puts
               text -- and therefore the path the parser has to resolve. */
            var sharedStringTablePart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedStrings = new List<string>();

            uint sheetId = 1;
            foreach (var (name, rows) in sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new Spreadsheet.SheetData();

                uint rowIndex = 1;
                foreach (string[] cells in rows)
                {
                    var row = new Spreadsheet.Row { RowIndex = rowIndex };
                    for (int column = 0; column < cells.Length; column++)
                    {
                        int index = sharedStrings.IndexOf(cells[column]);
                        if (index < 0)
                        {
                            sharedStrings.Add(cells[column]);
                            index = sharedStrings.Count - 1;
                        }

                        row.AppendChild(new Spreadsheet.Cell
                        {
                            CellReference = ColumnName(column) + rowIndex.ToString(CultureInfo.InvariantCulture),
                            DataType = Spreadsheet.CellValues.SharedString,
                            CellValue = new Spreadsheet.CellValue(index.ToString(CultureInfo.InvariantCulture))
                        });
                    }

                    sheetData.AppendChild(row);
                    rowIndex++;
                }

                worksheetPart.Worksheet = new Spreadsheet.Worksheet(sheetData);
                sheetList.AppendChild(new Spreadsheet.Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId,
                    Name = name
                });

                sheetId++;
            }

            var table = new Spreadsheet.SharedStringTable();
            foreach (string value in sharedStrings)
                table.AppendChild(new Spreadsheet.SharedStringItem(new Spreadsheet.Text(value)));

            sharedStringTablePart.SharedStringTable = table;

            if (withMacro)
                FeedPart(workbookPart.AddNewPart<VbaProjectPart>(), MacroMarker);
        }

        return stream.ToArray();
    }

    private static void FeedPart(OpenXmlPart part, string payload)
    {
        using var data = new MemoryStream(Encoding.ASCII.GetBytes(payload));
        part.FeedData(data);
    }

    private static string ColumnName(int index)
    {
        var name = new StringBuilder();
        int remaining = index;
        do
        {
            name.Insert(0, (char)('A' + remaining % 26));
            remaining = remaining / 26 - 1;
        }
        while (remaining >= 0);

        return name.ToString();
    }

    /// <summary>
    /// A two-page PDF with Helvetica text, the first page carrying two words on one baseline
    /// 200 points apart so the row heuristic has something to group.
    /// </summary>
    private static byte[] BuildTwoPagePdf() => BuildPdf("<< /Type /Catalog /Pages 2 0 R >>");

    /// <summary>
    /// The same PDF with an <c>/OpenAction</c> in its catalog that runs JavaScript -- the shape
    /// a hostile PDF actually takes.
    /// </summary>
    private static byte[] BuildPdfWithJavaScriptAction() => BuildPdf(
        "<< /Type /Catalog /Pages 2 0 R /OpenAction << /S /JavaScript /JS (this is the script body) >> >>");

    private static byte[] BuildPdf(string catalog)
    {
        const string firstPageContent =
            "BT\n/F1 12 Tf\n72 720 Td\n(Dungeon level one) Tj\n0 -20 Td\n(Gnoll) Tj\n200 0 Td\n(12) Tj\nET";

        const string secondPageContent =
            "BT\n/F1 12 Tf\n72 720 Td\n(Dungeon level two) Tj\nET";

        return AssemblePdf(
            catalog,
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            StreamObject(firstPageContent),
            StreamObject(secondPageContent));
    }

    private static string StreamObject(string content)
        => "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream";

    /// <summary>
    /// Writes the objects out with a cross-reference table whose offsets are measured rather than
    /// guessed, because a PDF reader that cannot find an object is testing the wrong thing.
    /// </summary>
    private static byte[] AssemblePdf(params string[] objectBodies)
    {
        var encoding = Encoding.Latin1;
        using var stream = new MemoryStream();

        void Write(string value)
        {
            byte[] bytes = encoding.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");

        var offsets = new List<long>();
        for (int i = 0; i < objectBodies.Length; i++)
        {
            offsets.Add(stream.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + objectBodies[i] + "\nendobj\n");
        }

        long crossReferenceOffset = stream.Length;
        Write("xref\n0 " + (objectBodies.Length + 1).ToString(CultureInfo.InvariantCulture) + "\n");
        Write("0000000000 65535 f \n");
        foreach (long offset in offsets)
            Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

        Write(
            "trailer\n<< /Size " + (objectBodies.Length + 1).ToString(CultureInfo.InvariantCulture)
            + " /Root 1 0 R >>\nstartxref\n" + crossReferenceOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");

        return stream.ToArray();
    }

    private static byte[] BuildPlainZip(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return stream.ToArray();
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        return combined;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
