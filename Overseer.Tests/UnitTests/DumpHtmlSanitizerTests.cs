using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class DumpHtmlSanitizerTests
{
    [Fact]
    public void StyleBlock_IsRemovedIncludingCss()
    {
        string html = "<html><head><style>body{margin:0}.nh_color_0{color:#000}</style></head><body>Player text</body></html>";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("Player text", result);
        Assert.DoesNotContain("nh_color_0", result);
        Assert.DoesNotContain("{", result);
    }

    [Fact]
    public void MapRows_RemainOnSeparateLines()
    {
        string html = "<pre class=\"nh_screen\">\n" +
                      "|....|\n" +
                      "|.@..|\n" +
                      "|....|\n" +
                      "</pre>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        string expected = "|....|\n|.@..|\n|....|";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BlockTags_DoNotDoubleLineBreaks()
    {
        string html = "<div>a</div>\n<div>b</div>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("a\nb", result);
    }

    [Fact]
    public void OpeningBlockTag_DoesNotLeaveLeadingNewline()
    {
        string html = "<pre>\nrow1\n</pre>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("row1", result);
    }

    [Fact]
    public void MapPadding_SurvivesCollapse()
    {
        string html = "<h2>Map</h2>\n<pre>\n" +
                      "&nbsp;&nbsp;&nbsp;#\n" +
                      "&nbsp;&nbsp;&nbsp;.\n" +
                      "</pre>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        string expected = "Map\n   #\n   .";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NonBreakingSpaces_BecomeAsciiSpaces()
    {
        string html = "<p>Item&nbsp;Name&nbsp;&nbsp;Description</p>";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.DoesNotContain('\u00A0', result);
        Assert.Equal("Item Name  Description", result);
    }

    [Fact]
    public void TableCells_AreSeparated()
    {
        string html = "<table><tr><td>Force bolt</td><td>75%</td></tr></table>";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("Force bolt 75%", result);
    }

    [Fact]
    public void Headings_BecomeTheirOwnLine()
    {
        string html = "<h2>Discoveries:</h2>\n<div>a potion of healing</div>";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("Discoveries:\na potion of healing", result);
    }

    [Fact]
    public void InlineTags_AreRemovedWithoutSpacing()
    {
        string html = "<b>Elb</b>ereth";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("Elbereth", result);
    }

    [Fact]
    public void LineBreakTag_BecomesNewline()
    {
        string html = "a<br />b";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("a\nb", result);
    }

    [Fact]
    public void Entities_AreDecodedAfterTagStripping()
    {
        string html = "&lt;script&gt;alert(1)&lt;/script&gt;";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("<script>alert(1)</script>", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void NullEmptyWhitespace_ReturnEmpty(string? input)
    {
        string result = DumpHtmlSanitizer.Sanitize(input);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TableHeaders_TheadAndTheader_AreSanitizedCleanly()
    {
        string html = "<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>Item</td><td>10</td></tr></tbody></table>";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Equal("Name Value\n\nItem 10", result);
    }

    [Fact]
    public void PetSectionCoordinates_SurviveTagStripping_WhenEscaped()
    {
        // html_dump_char() escapes '<' and '>', and DumpHtmlSanitizer decodes
        // entities last, so a pet position reaches the model intact.
        string html = "<h2>Pets:</h2>\n"
                    + "<div>3 pets on this level.</div>\n"
                    + "<div>1 - Fido the little dog, &lt;12,7&gt;, 3 squares away, 7/8 HP</div>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.Contains("<12,7>", result);
        Assert.Equal("Pets:\n3 pets on this level.\n1 - Fido the little dog, <12,7>, 3 squares away, 7/8 HP", result);
    }

    [Fact]
    public void UnescapedAngleBracketCoordinates_AreDestroyed()
    {
        // Documents the failure mode: if the engine ever writes the Pets section
        // through the unescaped dump_html_ai_write() path, RemainingTagRegex eats
        // every position silently. If this test starts failing, the sanitizer
        // changed - reread the precondition in the pet-snapshot plan.
        string html = "<div>1 - Fido the little dog, <12,7>, 3 squares away, 7/8 HP</div>\n";
        string result = DumpHtmlSanitizer.Sanitize(html);

        Assert.DoesNotContain("12,7", result);
    }
}