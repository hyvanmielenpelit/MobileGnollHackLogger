using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Overseer.Services.CorpusIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="NetHackWikiConversionReportReader"/>: the value shapes of the converter's
/// <c>_conversion_report.json</c>, pinned with a trimmed fixture; lenient mapping of mistyped fields;
/// malformed and missing files; and the cache keyed by the file's last write time, which the tests
/// set explicitly instead of waiting for the clock.
/// </summary>
public sealed class NetHackWikiConversionReportTests : IDisposable
{
    /* Trimmed from the shape of a real report. The dump path is fictitious; the reader never maps it. */
    private const string FixtureJson = """
        {
          "generated": "2026-10-04T20:47:34",
          "dump": {
            "path": "X:\\fixture-dumps\\nethackwiki-fixture-dump.xml",
            "bytes": 197540777,
            "modified": "2026-10-04T18:31:07"
          },
          "duration_seconds": 412,
          "pages": {
            "written": 2512,
            "redirects": 8277,
            "errors": 0,
            "skipped": {
              "namespace": 24938,
              "formerly_converted_namespaces": {
                "nethackwiki": 53,
                "help": 46,
                "category": 854,
                "source": 2294,
                "forum": 575
              },
              "non_english": 529,
              "variant_only": 2434,
              "variant_only_by_rule": {
                "title_suffix": 626,
                "subpage": 65,
                "categories": 1441,
                "banners": 302
              },
              "non_game": 164
            }
          },
          "tables_converted": 1873,
          "dropped_templates": [
            { "name": "Fixture template one", "occurrences": 12, "pages": 4 },
            { "name": "Fixture template two", "occurrences": 40, "pages": 40 },
            { "name": "Fixture template three", "occurrences": 1, "pages": 1 }
          ],
          "quality": {
            "largest_file": { "name": "EvilHack.md", "bytes": 248488 },
            "files_over_500_kb": []
          }
        }
        """;

    private static readonly DateTime FirstWriteUtc = new(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondWriteUtc = new(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "NetHackWikiConversionReportTests_" + Guid.NewGuid().ToString("N"));

    public NetHackWikiConversionReportTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    private string ReportFile => Path.Combine(_root, NetHackWikiConversionReportReader.FileName);

    private static IConfiguration CreateConfig(string? wikiPath)
    {
        var values = new Dictionary<string, string?>();
        if (wikiPath != null) values[NetHackWikiConversionReportReader.ConfigKey] = wikiPath;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private NetHackWikiConversionReportReader CreateReader() => new(CreateConfig(_root));

    private void WriteReport(string json, DateTime lastWriteUtc)
    {
        File.WriteAllText(ReportFile, json);
        File.SetLastWriteTimeUtc(ReportFile, lastWriteUtc);
    }

    // -- the fixture ----------------------------------------------------------------------------------

    [Fact]
    public void Read_Fixture_MapsEveryField()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Equal(FirstWriteUtc, report!.ReportLastWriteUtc);
        Assert.Equal("2026-10-04T20:47:34", report.GeneratedAt);
        Assert.Equal("2026-10-04T18:31:07", report.DumpModified);
        Assert.Equal(197540777L, report.DumpBytes);
        Assert.Equal(412.0, report.DurationSeconds);
        Assert.Equal(2512, report.PagesWritten);
        Assert.Equal(8277, report.Redirects);
        Assert.Equal(0, report.Errors);
        Assert.Equal(1873, report.TablesConverted);
        Assert.Equal(3, report.DroppedTemplates);
        Assert.Equal("EvilHack.md", report.LargestFileName);
        Assert.Equal(248488L, report.LargestFileBytes);
        Assert.Equal(0, report.FilesOver500Kb);
    }

    [Fact]
    public void Read_Fixture_MapsSkippedReasonsInFileOrderWithTheirBreakdowns()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(error);
        var skipped = report!.Skipped;
        Assert.NotNull(skipped);
        Assert.Equal(
            new[] { "namespace", "formerly_converted_namespaces", "non_english", "variant_only", "non_game" },
            skipped!.Select(s => s.Reason).ToArray());

        Assert.Equal(24938, skipped[0].Count);
        Assert.Null(skipped[0].Breakdown);

        var formerly = skipped[1];
        Assert.Equal(3822, formerly.Count);
        Assert.NotNull(formerly.Breakdown);
        Assert.Equal(5, formerly.Breakdown!.Count);
        Assert.Equal(
            new[] { ("nethackwiki", 53), ("help", 46), ("category", 854), ("source", 2294), ("forum", 575) },
            formerly.Breakdown.Select(b => (b.Reason, b.Count)).ToArray());

        Assert.Equal(529, skipped[2].Count);
        Assert.Null(skipped[2].Breakdown);

        var variantOnly = skipped[3];
        Assert.Equal(2434, variantOnly.Count);
        Assert.NotNull(variantOnly.Breakdown);
        Assert.Equal(
            new[] { ("title_suffix", 626), ("subpage", 65), ("categories", 1441), ("banners", 302) },
            variantOnly.Breakdown!.Select(b => (b.Reason, b.Count)).ToArray());

        Assert.Equal(164, skipped[4].Count);
        Assert.Null(skipped[4].Breakdown);
    }

    [Fact]
    public void Read_Fixture_NeverExposesTheDumpPath()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out _);

        Assert.NotNull(report);
        string serialized = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("nethackwiki-fixture-dump", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixture-dumps", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_SkippedArraysAndOtherShapes_AreCountedOrLeftOut()
    {
        const string json = """
            {
              "pages": {
                "skipped": {
                  "too_short": ["A", "B", "C"],
                  "described_only": "a string is not a count",
                  "empty_group": {},
                  "mixed_group": { "counted": 7, "label": "not a number" },
                  "orphan_by_rule": { "first": 2, "second": 3 },
                  "flag": true
                }
              }
            }
            """;
        WriteReport(json, FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(error);
        var skipped = report!.Skipped;
        Assert.NotNull(skipped);
        Assert.Equal(new[] { "too_short", "mixed_group", "orphan_by_rule" }, skipped!.Select(s => s.Reason).ToArray());
        Assert.Equal(3, skipped[0].Count);
        Assert.Null(skipped[0].Breakdown);
        Assert.Equal(7, skipped[1].Count);
        Assert.Equal(new[] { ("counted", 7) }, skipped[1].Breakdown!.Select(b => (b.Reason, b.Count)).ToArray());
        Assert.Equal(5, skipped[2].Count);
        Assert.Equal(2, skipped[2].Breakdown!.Count);
    }

    // -- lenient mapping and bad files ----------------------------------------------------------------

    [Fact]
    public void Read_MistypedFields_AreNullAndDoNotThrow()
    {
        const string json = """
            {
              "generated": 20261004,
              "dump": "not an object",
              "duration_seconds": "long",
              "pages": {
                "written": "many",
                "redirects": 1.5,
                "errors": null,
                "skipped": [24938, 529]
              },
              "tables_converted": true,
              "dropped_templates": { "name": "not an array" },
              "quality": {
                "largest_file": "EvilHack.md",
                "files_over_500_kb": 3
              }
            }
            """;
        WriteReport(json, FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Equal(FirstWriteUtc, report!.ReportLastWriteUtc);
        Assert.Null(report.GeneratedAt);
        Assert.Null(report.DumpModified);
        Assert.Null(report.DumpBytes);
        Assert.Null(report.DurationSeconds);
        Assert.Null(report.PagesWritten);
        Assert.Null(report.Redirects);
        Assert.Null(report.Errors);
        Assert.Null(report.Skipped);
        Assert.Null(report.TablesConverted);
        Assert.Null(report.DroppedTemplates);
        Assert.Null(report.LargestFileName);
        Assert.Null(report.LargestFileBytes);
        Assert.Null(report.FilesOver500Kb);
    }

    [Fact]
    public void Read_EmptyObject_ReturnsAReportWithEveryFieldNull()
    {
        WriteReport("{}", FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Null(report!.GeneratedAt);
        Assert.Null(report.PagesWritten);
        Assert.Null(report.Skipped);
    }

    [Fact]
    public void Read_MalformedJson_ReturnsNullWithAnErrorAndDoesNotThrow()
    {
        WriteReport("{ \"generated\": \"2026-10-04T20:47:34\", \"pages\": { \"written\": ", FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(report);
        Assert.NotNull(error);
        Assert.StartsWith("The conversion report is not valid JSON", error);
    }

    [Fact]
    public void Read_RootIsNotAnObject_ReturnsNullWithAnError()
    {
        WriteReport("[1, 2, 3]", FirstWriteUtc);
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(report);
        Assert.Equal("The conversion report is not a JSON object.", error);
    }

    [Fact]
    public void Read_KeyNotSet_ReturnsNullWithoutAnError()
    {
        var reader = new NetHackWikiConversionReportReader(CreateConfig(null));

        var report = reader.Read(out string? error);

        Assert.Null(report);
        Assert.Null(error);
        Assert.Null(reader.ReportPath);
        Assert.Null(reader.GetReportLastWriteUtc());
    }

    [Fact]
    public void Read_FileMissing_ReturnsNullWithoutAnError()
    {
        var reader = CreateReader();

        var report = reader.Read(out string? error);

        Assert.Null(report);
        Assert.Null(error);
        Assert.Equal(ReportFile, reader.ReportPath);
        Assert.Null(reader.GetReportLastWriteUtc());
    }

    [Fact]
    public void GetReportLastWriteUtc_ReturnsTheFileTime()
    {
        WriteReport(FixtureJson, SecondWriteUtc);
        var reader = CreateReader();

        Assert.Equal(SecondWriteUtc, reader.GetReportLastWriteUtc());
    }

    // -- the cache ------------------------------------------------------------------------------------

    [Fact]
    public void Read_SameLastWriteTime_ReturnsTheCachedInstanceEvenWhenTheContentChanged()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();

        var first = reader.Read(out string? firstError);
        var second = reader.Read(out string? secondError);

        Assert.NotNull(first);
        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Same(first, second);

        WriteReport(FixtureJson.Replace("\"written\": 2512", "\"written\": 9999"), FirstWriteUtc);
        var third = reader.Read(out string? thirdError);

        Assert.Null(thirdError);
        Assert.Same(first, third);
        Assert.Equal(2512, third!.PagesWritten);
    }

    [Fact]
    public void Read_ChangedLastWriteTime_ParsesTheFileAgain()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();
        var first = reader.Read(out _);
        Assert.NotNull(first);

        WriteReport(FixtureJson.Replace("\"written\": 2512", "\"written\": 9999"), SecondWriteUtc);
        var second = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(9999, second!.PagesWritten);
        Assert.Equal(SecondWriteUtc, second.ReportLastWriteUtc);
        Assert.Same(second, reader.Read(out _));
    }

    [Fact]
    public void Read_MalformedFile_KeepsItsErrorUntilTheLastWriteTimeChanges()
    {
        WriteReport("{ not json", FirstWriteUtc);
        var reader = CreateReader();

        Assert.Null(reader.Read(out string? firstError));
        Assert.NotNull(firstError);

        WriteReport(FixtureJson, FirstWriteUtc);
        Assert.Null(reader.Read(out string? cachedError));
        Assert.Equal(firstError, cachedError);

        File.SetLastWriteTimeUtc(ReportFile, SecondWriteUtc);
        var report = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Equal(2512, report!.PagesWritten);
    }

    [Fact]
    public void Read_FileLockedByAnotherWriter_ReturnsAnErrorThatIsNotCached()
    {
        WriteReport(FixtureJson, FirstWriteUtc);
        var reader = CreateReader();

        string? lockedError;
        using (new FileStream(ReportFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(reader.Read(out lockedError));
        }

        Assert.NotNull(lockedError);
        Assert.StartsWith("The conversion report could not be read", lockedError);

        var report = reader.Read(out string? error);

        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Equal(2512, report!.PagesWritten);
    }
}
