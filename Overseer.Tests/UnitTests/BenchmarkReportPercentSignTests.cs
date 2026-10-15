namespace Overseer.Tests.UnitTests;

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Every GnollBench document writes a percentage in the US style, the sign right after the number
/// (<c>96%</c>), never after a space or a no-break space. The golden files hold the rendered text of
/// every document type and both writer prompts, so they are checked as a whole.
/// </summary>
public class BenchmarkReportPercentSignTests
{
    private static readonly Regex SpacedPercent = new(@"[0-9][   ]%", RegexOptions.CultureInvariant);

    [Fact]
    public void NoRenderedDocumentOrPrompt_PutsASpaceBeforeAPercentSign()
    {
        string directory = Path.GetDirectoryName(BenchmarkReportPackFixture.GoldenPath("any.md"))!;
        var files = Directory.GetFiles(directory, "*.md").Concat(Directory.GetFiles(directory, "*.txt")).ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (Path.GetFileName(path), index + 1, line)))
            .Where(l => SpacedPercent.IsMatch(l.line))
            .Select(l => l.Item1 + ":" + l.Item2 + ": " + SpacedPercent.Match(l.line).Value)
            .ToList();

        Assert.True(offenders.Count == 0, "A space before a percent sign:\n" + string.Join("\n", offenders.Take(20)));
    }
}
