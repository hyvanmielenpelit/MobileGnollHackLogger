namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The Root Cause Analysis documents and prompts against their golden files: comparison scope named and
/// anonymized, a per-model pack, a run completion with and without controls, and a battery completion;
/// then the structure every one of them must hold whatever the golden says.
/// </summary>
public class RootCauseReportGoldenTests
{
    private const string PromptSeparator = "\n===== USER MESSAGE =====\n";

    private static readonly BenchmarkReportRenderOptions FullNamed = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
    private static readonly BenchmarkReportRenderOptions FullAnonymized = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized };

    private static void AssertGolden(string file, string text)
    {
        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, text);
            return;
        }
        Assert.Equal(BenchmarkReportPackFixture.ReadGolden(file), text);
    }

    private static BenchmarkReportDocument Document(string name) => name switch
    {
        "comparison" => RootCauseReportFixture.ComparisonDocument(),
        "pack" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack),
        "runWithControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion),
        "runNoControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: false), BenchmarkReportDocumentOrigin.RunCompletion),
        "battery" => RootCauseReportFixture.BatteryDocument(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("comparison", false, "comparison_rca_full_named.md")]
    [InlineData("comparison", true, "comparison_rca_full_anonymized.md")]
    [InlineData("pack", false, "rca_full_named.md")]
    [InlineData("runWithControls", false, "rca_run_with_controls.md")]
    [InlineData("runNoControls", false, "rca_run_no_controls.md")]
    [InlineData("battery", false, "rca_battery_standalone.md")]
    public void TheRootCauseAnalysis_MatchesItsGoldenFile(string name, bool anonymized, string file)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(name), anonymized ? FullAnonymized : FullNamed);

        AssertGolden(file, text);
    }

    [Theory]
    [InlineData("comparison")]
    [InlineData("pack")]
    [InlineData("runWithControls")]
    [InlineData("runNoControls")]
    [InlineData("battery")]
    public void EveryRootCauseAnalysis_HasItsSectionsInOrder_AndItsFourServerFigureMarkers(string name)
    {
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            Charts = BenchmarkReportChartPlacement.ServerFigureKeys.Select(key => new BenchmarkReportRenderChart
            {
                FigureKey = key,
                Title = "Chart " + key,
                Caption = "Caption of " + key,
                AltText = "Alt text of " + key,
                Png = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                WidthPx = 1600,
                HeightPx = 900,
                Sha256 = new string('0', 64)
            }).ToList()
        };
        string text = BenchmarkReportPackRenderer.Render(Document(name), options);

        string[] headings = { "Scope and design", "Evidence: intelligence", "Evidence: speed", "Evidence: cost", "Known facts", "Hypotheses", "Conclusions", "Next runs", "Limitations" };
        int last = -1;
        foreach (string heading in headings)
        {
            int at = text.IndexOf("## " + heading, StringComparison.Ordinal);
            Assert.True(at > last, heading + " is missing or out of order");
            last = at;
        }
        foreach (string key in BenchmarkReportChartPlacement.ServerFigureKeys)
        {
            Assert.Contains("[[figure:" + key + "]]", text);
        }
        Assert.Contains("INTERNAL", text);
    }

    [Fact]
    public void TheComparisonFailureProfile_IsTwoTables_OfAtMostNineColumnsEach()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullNamed);

        int start = text.IndexOf("Failure profile", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string section = text[start..];
        section = section[..section.IndexOf("\n#", 1, StringComparison.Ordinal)];
        Assert.Contains("**Outcomes**", section);
        Assert.Contains("**Signals**", section);

        // A header row is a table row followed by its delimiter row.
        var lines = section.Split('\n');
        var headerRows = lines
            .Where((line, i) => line.StartsWith("|", StringComparison.Ordinal)
                                && i + 1 < lines.Length
                                && System.Text.RegularExpressions.Regex.IsMatch(lines[i + 1], @"^\|\s*:?-"))
            .ToList();
        Assert.Equal(2, headerRows.Count);
        Assert.All(headerRows, row => Assert.True(row.Count(c => c == '|') - 1 <= 9, row));
    }

    [Fact]
    public void TheAnonymizedComparison_NamesNoModel()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullAnonymized);

        foreach (var m in BenchmarkReportPackFixture.ComparisonModels)
        {
            Assert.DoesNotContain(m.Label, text);
            Assert.DoesNotContain(m.Provider, text);
        }
    }

    [Fact]
    public void ARunWithoutControls_SaysNoOtherModelAnsweredTheQuestions_AndProposesAControlRun()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("runNoControls"), FullNamed);

        Assert.Contains("no other model answered these questions", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Control run", text);
    }

    [Fact]
    public void ARunWithControls_NamesThem()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("runWithControls"), FullNamed);

        Assert.Contains("Vega Pro (high)", text);
        Assert.Contains("Zeta Prime (high)", text);
    }

    [Fact]
    public void TheRootCauseAnalysis_RendersOnlyAtFullDisclosure()
    {
        Assert.Equal(new[] { BenchmarkReportDisclosure.Full }, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.RootCauseAnalysis));
    }

    [Theory]
    [InlineData(true, "prompt_rca_comparison.txt")]
    [InlineData(false, "prompt_rca_standalone_run.txt")]
    public void TheRootCausePrompt_IsPinned(bool comparison, string file)
    {
        var sheet = comparison ? RootCauseReportFixture.ComparisonFacts().Sheet! : RootCauseReportFixture.RunSheet(controls: false);
        var content = comparison
            ? RootCauseReportFixture.ComparisonFacts().Content!
            : BenchmarkReportContent.Build(new[] { RootCauseReportFixture.Runs()[RootCauseReportFixture.SubjectRunId] }, 120);
        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.RootCauseAnalysis, sheet, content);
        string text = prompt.SystemPrompt + PromptSeparator + prompt.UserMessage;

        AssertGolden(file, text);
        if (BenchmarkReportPackFixture.UpdateGoldens) return;

        var scope = comparison ? BenchmarkReportScope.Comparison : BenchmarkReportScope.Model;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt.SystemPrompt))),
            BenchmarkReportPackPrompt.PromptSha256(BenchmarkReportAudience.RootCauseAnalysis, scope));
        Assert.Contains("rca.axis.speed.status", prompt.UserMessage);
        Assert.Contains("CLAIM SUPPORT", prompt.UserMessage);
    }

    [Fact]
    public void TheExecutiveSummaryPrompt_SeesNoRootCauseFact()
    {
        var built = RootCauseReportFixture.ComparisonFacts();
        Assert.Contains(built.Sheet!.Facts, f => f.Key.StartsWith("rca.", StringComparison.Ordinal));

        var prompt = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, built.Sheet!, built.Content!);

        Assert.DoesNotContain("rca.", prompt.UserMessage);
        Assert.DoesNotContain(".speed.ttftP90", prompt.UserMessage);
    }
}
