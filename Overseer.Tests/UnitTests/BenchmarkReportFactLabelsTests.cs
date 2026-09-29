namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The labels of the report pack's fact keys: every key the fact builder emits has one of its own,
/// patterned keys follow their rules, and an unknown key still reads as words.
/// </summary>
public class BenchmarkReportFactLabelsTests
{
    /// <summary>Every fact key of a panel-graded subject with a peer, of a stand-alone subject, and of the fixture sheet.</summary>
    private static IReadOnlyList<string> EmittedKeys()
    {
        var subject = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80, 20), new AnswerSpec(12, 2, 1, 40, 60), new AnswerSpec(13, 3, 1, 60, 90));
        subject.CoAssessorModelConfigurationId = 2;
        subject.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-haiku-5");
        var peer = Run(2, "Google", "peer", new AnswerSpec(11, 1, 1, 70, 20), new AnswerSpec(12, 2, 1, 50, 60));

        var withPeer = BenchmarkReportFacts.Build(Input(
            Comparison(
                Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60, 55, 65),
                Entry("run:2", new long[] { 2 }, "Peer", "Google", 70, 64, 76)),
            "run:1", subject, peer)).Sheet!;
        var standalone = BenchmarkReportFacts.Build(Input(
            Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60, 55, 65)), "run:1", subject)).Sheet!;

        return withPeer.Facts.Select(f => f.Key)
            .Concat(withPeer.Entries.SelectMany(e => e.Extra).Select(f => f.Key))
            .Concat(standalone.Facts.Select(f => f.Key))
            .Concat(Sheet().Facts.Select(f => f.Key))
            .Distinct()
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void EveryKeyTheFactBuilderEmits_HasALabel()
    {
        var keys = EmittedKeys();
        Assert.Contains("comparison.pricingBasisKind", keys);
        Assert.Contains("tokens.inputPerQuestion", keys);
        Assert.Contains("band.intermediate.difference", keys);

        var unlabeled = keys.Where(k => !BenchmarkReportFactLabels.TryLabel(k, out _)).ToList();

        Assert.Empty(unlabeled);
    }

    [Fact]
    public void EveryLabel_IsWords_NeverTheKey()
    {
        foreach (string key in EmittedKeys())
        {
            string label = BenchmarkReportFactLabels.Label(key);
            Assert.DoesNotContain(".", label.Replace("ICC(A,1)", string.Empty).Replace(" %", string.Empty));
            Assert.NotEqual(key, label);
            Assert.True(char.IsUpper(label[0]) || char.IsDigit(label[0]), key + " → " + label);
        }
    }

    [Theory]
    [InlineData("quality.index", "Intelligence Index")]
    [InlineData("quality.intervalSpan", "Interval span")]
    [InlineData("dimension.accuracy", "Accuracy score")]
    [InlineData("dimension.completeness.peerMean", "Completeness peer mean")]
    [InlineData("dimension.readability.difference", "Readability difference from the peer mean")]
    [InlineData("band.intermediate.score", "Intermediate band score")]
    [InlineData("band.advanced.questions", "Advanced band questions")]
    [InlineData("band.simple.peerMean", "Simple band peer mean")]
    [InlineData("errors.critical", "Critical errors")]
    [InlineData("claims.supported", "Claims the verifier supported")]
    [InlineData("style.responseStyleConflict", "Response-style conflict")]
    [InlineData("tools.callsPerQuestion", "Tool calls per question")]
    [InlineData("peer.B.dimension.accuracy", "Model B: accuracy score")]
    public void Labels_AreExplicitOrFollowTheirPattern(string key, string expected)
    {
        Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label));
        Assert.Equal(expected, label);
        Assert.Equal(expected, BenchmarkReportFactLabels.Label(key));
    }

    [Theory]
    [InlineData("tools.notFound", "Tools not found")]
    [InlineData("quality.dim.accuracy", "Quality dim accuracy")]
    [InlineData("speed.p50Ms", "Speed p50 ms")]
    [InlineData("dimension.charm", "Dimension charm")]
    [InlineData("band.expert.score", "Band expert score")]
    public void AnUnknownKey_ReadsAsWords(string key, string expected)
    {
        Assert.False(BenchmarkReportFactLabels.TryLabel(key, out _));
        Assert.Equal(expected, BenchmarkReportFactLabels.Label(key));
    }
}
