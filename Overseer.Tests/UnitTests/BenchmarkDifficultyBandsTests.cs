namespace Overseer.Tests.UnitTests;

using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The band boundaries used to be defined twice and disagreed: the difficulty assessor was told
/// 1-35 / 36-70 / 71-100 while the report bucketed 1-33 / 34-66 / 67-100. These tests pin the
/// single definition, with explicit coverage of every boundary value where the two used to
/// diverge.
/// </summary>
public class BenchmarkDifficultyBandsTests
{
    [Theory]
    [InlineData(1, BenchmarkDifficulty.Simple)]
    [InlineData(33, BenchmarkDifficulty.Simple)]
    [InlineData(34, BenchmarkDifficulty.Simple)]   // was Intermediate under the report's old 33 cut
    [InlineData(35, BenchmarkDifficulty.Simple)]   // was Intermediate under the report's old 33 cut
    [InlineData(36, BenchmarkDifficulty.Intermediate)]
    [InlineData(42, BenchmarkDifficulty.Intermediate)]
    [InlineData(66, BenchmarkDifficulty.Intermediate)]
    [InlineData(67, BenchmarkDifficulty.Intermediate)] // was Advanced under the report's old 67 cut
    [InlineData(70, BenchmarkDifficulty.Intermediate)] // was Advanced under the report's old 67 cut
    [InlineData(71, BenchmarkDifficulty.Advanced)]
    [InlineData(100, BenchmarkDifficulty.Advanced)]
    public void BandOf_MatchesThePromptBoundaries(int difficulty, BenchmarkDifficulty expected)
    {
        Assert.Equal(expected, BenchmarkDifficultyBands.BandOf(difficulty));
    }

    [Fact]
    public void BandPredicates_AgreeWithBandOf()
    {
        for (int d = BenchmarkDifficultyBands.MinDifficulty; d <= BenchmarkDifficultyBands.MaxDifficulty; d++)
        {
            var band = BenchmarkDifficultyBands.BandOf(d);

            Assert.Equal(band == BenchmarkDifficulty.Simple, BenchmarkDifficultyBands.IsSimple(d));
            Assert.Equal(band == BenchmarkDifficulty.Intermediate, BenchmarkDifficultyBands.IsIntermediate(d));
            Assert.Equal(band == BenchmarkDifficulty.Advanced, BenchmarkDifficultyBands.IsAdvanced(d));
        }
    }

    [Fact]
    public void EveryDifficultyBelongsToExactlyOneBand()
    {
        for (int d = BenchmarkDifficultyBands.MinDifficulty; d <= BenchmarkDifficultyBands.MaxDifficulty; d++)
        {
            int matches =
                (BenchmarkDifficultyBands.IsSimple(d) ? 1 : 0) +
                (BenchmarkDifficultyBands.IsIntermediate(d) ? 1 : 0) +
                (BenchmarkDifficultyBands.IsAdvanced(d) ? 1 : 0);

            Assert.Equal(1, matches);
        }
    }

    [Fact]
    public void BandRangesAreContiguousAndNonOverlapping()
    {
        Assert.Equal(BenchmarkDifficultyBands.SimpleMax + 1, BenchmarkDifficultyBands.IntermediateMin);
        Assert.Equal(BenchmarkDifficultyBands.IntermediateMax + 1, BenchmarkDifficultyBands.AdvancedMin);
        Assert.True(BenchmarkDifficultyBands.SimpleMin < BenchmarkDifficultyBands.SimpleMax);
        Assert.True(BenchmarkDifficultyBands.AdvancedMin <= BenchmarkDifficultyBands.MaxDifficulty);
    }

    [Fact]
    public void RangeLabel_RendersInclusiveRanges()
    {
        Assert.Equal("1–35", BenchmarkDifficultyBands.RangeLabel(BenchmarkDifficulty.Simple));
        Assert.Equal("36–70", BenchmarkDifficultyBands.RangeLabel(BenchmarkDifficulty.Intermediate));
        Assert.Equal("71–100", BenchmarkDifficultyBands.RangeLabel(BenchmarkDifficulty.Advanced));
    }

    [Fact]
    public void DifficultyPrompt_StatesTheSameBoundariesItIsScoredAgainst()
    {
        // The whole point of the shared constants: the assessor must be told the ranges the
        // report will bucket its answer into.
        var prompt = BenchmarkDifficultyPrompt.BuildPrompt("Suite", new[]
        {
            new BenchmarkDifficultyQuestionItem
            {
                Id = 7,
                OrderIndex = 1,
                QuestionText = "What is the Gnoll race?",
                AuthorBand = BenchmarkDifficulty.Simple
            }
        });

        Assert.Contains("1 to 35 (Simple)", prompt);
        Assert.Contains("36 to 70 (Intermediate)", prompt);
        Assert.Contains("71 to 100 (Advanced)", prompt);
    }

    [Fact]
    public void DifficultyPrompt_DoesNotDiscloseTheAuthorBand()
    {
        // Showing it anchored the rating on the very judgement the assessor is asked to make
        // independently.
        var prompt = BenchmarkDifficultyPrompt.BuildPrompt("Suite", new[]
        {
            new BenchmarkDifficultyQuestionItem
            {
                Id = 7,
                OrderIndex = 1,
                QuestionText = "What is the Gnoll race?",
                AuthorBand = BenchmarkDifficulty.Advanced
            }
        });

        Assert.DoesNotContain("Author Band:", prompt);
        Assert.Contains("deliberately withheld", prompt);
    }
}
