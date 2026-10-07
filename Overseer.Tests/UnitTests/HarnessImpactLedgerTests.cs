namespace Overseer.Tests.UnitTests;

using System.Globalization;
using System.Linq;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;

/// <summary>
/// The classified harness changelog. The coverage test is the one that matters most: it fails on
/// the first harness bump nobody classified, because an unclassified version silently resolves to
/// "everything changed" and segments every comparison across it.
/// </summary>
public class HarnessImpactLedgerTests
{
    [Fact]
    public void EveryHarnessVersionUpToTheCurrentOneIsClassified()
    {
        int current = int.Parse(HarnessImpactLedger.CurrentVersion, CultureInfo.InvariantCulture);
        var missing = Enumerable.Range(1, current)
            .Select(v => v.ToString(CultureInfo.InvariantCulture))
            .Where(v => !HarnessImpactLedger.IsClassified(v))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Harness version(s) " + string.Join(", ", missing) + " are not classified. Classify each new "
            + "harness version in HarnessImpactLedger (Overseer/Services/ChatConsistency/HarnessImpactLedger.cs) "
            + "from its changelog entry on BenchmarkAssessmentPrompt.HarnessVersion: which of CandidateInput, "
            + "CandidateTiming, Grading, Scoring, CandidateAccounting or ReportingOnly it changed.");
    }

    [Fact]
    public void CurrentVersionIsTheHarnessConstant()
    {
        Assert.Equal(BenchmarkAssessmentPrompt.HarnessVersion, HarnessImpactLedger.CurrentVersion);
    }

    [Fact]
    public void TheLastEntryIsTheCurrentVersion()
    {
        Assert.Equal(HarnessImpactLedger.CurrentVersion, HarnessImpactLedger.Entries[^1].Version);
    }

    [Fact]
    public void EntriesAreStrictlyIncreasingAndUnique()
    {
        var numbers = HarnessImpactLedger.Entries.Select(e => e.Number).ToList();

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        for (int i = 1; i < numbers.Count; i++)
        {
            Assert.True(numbers[i] > numbers[i - 1], $"Entry {numbers[i]} follows {numbers[i - 1]}.");
        }

        Assert.Equal(Enumerable.Range(1, numbers.Count).ToList(), numbers);
    }

    [Fact]
    public void EveryEntryHasASummaryAndAnImpact()
    {
        Assert.All(HarnessImpactLedger.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Summary), $"Harness {e.Version} has no summary.");
            Assert.NotEqual(HarnessImpact.None, e.Impact);
        });
    }

    [Fact]
    public void ReportingOnlyIsNeverCombinedWithAnotherFlag()
    {
        Assert.All(
            HarnessImpactLedger.Entries.Where(e => e.Impact.HasFlag(HarnessImpact.ReportingOnly)),
            e => Assert.Equal(HarnessImpact.ReportingOnly, e.Impact));
    }

    [Fact]
    public void EqualVersionsChangeNothing()
    {
        Assert.Equal(HarnessImpact.None, HarnessImpactLedger.ImpactBetween("53", "53"));
        Assert.Equal(HarnessImpact.None, HarnessImpactLedger.ImpactBetween("29", "29"));
    }

    [Fact]
    public void AdjacentVersionsYieldTheLaterVersionsImpact()
    {
        Assert.Equal(HarnessImpactLedger.ImpactOf("53"), HarnessImpactLedger.ImpactBetween("52", "53"));
        Assert.Equal(HarnessImpact.Grading, HarnessImpactLedger.ImpactBetween("52", "53"));
        Assert.Equal(HarnessImpact.ReportingOnly, HarnessImpactLedger.ImpactBetween("16", "17"));
    }

    [Fact]
    public void ImpactBetweenIsTheUnionOverTheSpanAndIgnoresArgumentOrder()
    {
        var expected = HarnessImpactLedger.ImpactOf("42") | HarnessImpactLedger.ImpactOf("43");

        Assert.Equal(expected, HarnessImpactLedger.ImpactBetween("41", "43"));
        Assert.Equal(expected, HarnessImpactLedger.ImpactBetween("43", "41"));
        Assert.True(HarnessImpactLedger.ImpactBetween("41", "43").HasFlag(HarnessImpact.CandidateInput));
    }

    [Fact]
    public void ASpanOfReportingOnlyAndGradingVersionsHasNoCandidateInput()
    {
        Assert.False(HarnessImpactLedger.ImpactBetween("52", "53").HasFlag(HarnessImpact.CandidateInput));
        Assert.False(HarnessImpactLedger.ImpactBetween("15", "17").HasFlag(HarnessImpact.CandidateInput));
    }

    [Fact]
    public void Harness29DeliveredTheSystemPromptToTheCandidate()
    {
        Assert.True(HarnessImpactLedger.ImpactBetween("28", "29").HasFlag(HarnessImpact.CandidateInput));
    }

    [Fact]
    public void Harness28RecalibratedTheSpeedScore()
    {
        Assert.True(HarnessImpactLedger.ImpactOf("28").HasFlag(HarnessImpact.Scoring));
    }

    [Fact]
    public void Harness34CountsGeminiThinkingTokens()
    {
        Assert.True(HarnessImpactLedger.ImpactBetween("33", "34").HasFlag(HarnessImpact.CandidateAccounting));
    }

    [Theory]
    [InlineData("32")]
    [InlineData("42")]
    [InlineData("46")]
    [InlineData("53")]
    public void GradingOnlyVersionsDoNotTouchCandidateInput(string version)
    {
        Assert.False(HarnessImpactLedger.ImpactOf(version).HasFlag(HarnessImpact.CandidateInput));
        Assert.True(HarnessImpactLedger.ImpactOf(version).HasFlag(HarnessImpact.Grading));
    }

    [Theory]
    [InlineData("22")]
    [InlineData("47")]
    [InlineData("50")]
    [InlineData("52")]
    public void ToolOutputChangesCountAsCandidateInputEvenWithoutAGuideChange(string version)
    {
        Assert.True(HarnessImpactLedger.ImpactOf(version).HasFlag(HarnessImpact.CandidateInput));
    }

    [Fact]
    public void TheMislabeledStamp12CoversHarness13And14()
    {
        var sameStamp = HarnessImpactLedger.ImpactBetween("12", "12");
        var thirteenAndFourteen = HarnessImpactLedger.ImpactOf("13") | HarnessImpactLedger.ImpactOf("14");

        Assert.Equal(thirteenAndFourteen, sameStamp);
        Assert.True(HarnessImpactLedger.ImpactBetween("11", "12").HasFlag(HarnessImpact.Scoring));
    }

    [Fact]
    public void Stamp18MayStraddleTheGeminiUsageFix()
    {
        Assert.Equal(HarnessImpact.CandidateAccounting, HarnessImpactLedger.ImpactBetween("18", "18"));
        Assert.True(HarnessImpactLedger.ImpactBetween("18", "19").HasFlag(HarnessImpact.CandidateAccounting));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("053")]
    [InlineData("12.5")]
    [InlineData("9999")]
    public void AnUnknownVersionCountsAsEveryChange(string? version)
    {
        Assert.Equal(HarnessImpactLedger.Unclassified, HarnessImpactLedger.ImpactOf(version));
        Assert.Equal(HarnessImpactLedger.Unclassified, HarnessImpactLedger.ImpactBetween(version, "53"));
        Assert.Equal(HarnessImpactLedger.Unclassified, HarnessImpactLedger.ImpactBetween("53", version));
    }

    [Fact]
    public void TwoUnknownVersionsCountAsEveryChange()
    {
        Assert.Equal(HarnessImpactLedger.Unclassified, HarnessImpactLedger.ImpactBetween(null, null));
        Assert.Equal(HarnessImpactLedger.Unclassified, HarnessImpactLedger.ImpactBetween("abc", "abc"));
    }

    [Fact]
    public void UnclassifiedCarriesEveryMeasurementFlagAndCandidateInput()
    {
        var all = HarnessImpactLedger.Unclassified;

        Assert.True(all.HasFlag(HarnessImpact.CandidateInput));
        Assert.True(all.HasFlag(HarnessImpact.CandidateTiming));
        Assert.True(all.HasFlag(HarnessImpact.Grading));
        Assert.True(all.HasFlag(HarnessImpact.Scoring));
        Assert.True(all.HasFlag(HarnessImpact.CandidateAccounting));
        Assert.False(all.HasFlag(HarnessImpact.ReportingOnly));
    }

    [Fact]
    public void IsClassifiedAcceptsCanonicalVersionsOnly()
    {
        int next = int.Parse(HarnessImpactLedger.CurrentVersion, CultureInfo.InvariantCulture) + 1;

        Assert.True(HarnessImpactLedger.IsClassified("1"));
        Assert.True(HarnessImpactLedger.IsClassified(HarnessImpactLedger.CurrentVersion));
        Assert.False(HarnessImpactLedger.IsClassified(next.ToString(CultureInfo.InvariantCulture)));
        Assert.False(HarnessImpactLedger.IsClassified("01"));
        Assert.False(HarnessImpactLedger.IsClassified("v53"));
    }

    [Fact]
    public void EntryOfReturnsTheClassification()
    {
        var entry = HarnessImpactLedger.EntryOf("29");

        Assert.NotNull(entry);
        Assert.Equal("29", entry!.Version);
        Assert.Null(HarnessImpactLedger.EntryOf("not-a-version"));
    }
}
