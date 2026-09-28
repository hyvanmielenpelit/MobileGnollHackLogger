namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The report-pack fact sheet: who the subject and its peers are, how peers are lettered and
/// ranked, how degraded axes are withheld, how questions pair with the peers' answers, and the
/// support labels computed from synthesis rows.
/// </summary>
public class BenchmarkReportFactsTests
{
    private static BenchmarkReportFact FactOf(BenchmarkReportFactSheet sheet, string key)
        => Assert.Single(sheet.Facts, f => f.Key == key);

    private static BenchmarkReportFactSheet BuildSheet(BenchmarkReportFactsInput input)
    {
        var result = BenchmarkReportFacts.Build(input);
        Assert.Null(result.Refusal);
        return Assert.IsType<BenchmarkReportFactSheet>(result.Sheet);
    }

    private static BenchmarkRun SimpleRun(long id, string provider, int quality = 80)
        => Run(id, provider, "model-" + id, new AnswerSpec(1, 1, 1, quality), new AnswerSpec(2, 2, 1, quality));

    [Fact]
    public void AnUnknownSubject_IsRefused()
    {
        var comparison = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80));

        var result = BenchmarkReportFacts.Build(Input(comparison, "run:99", SimpleRun(1, "OpenAI")));

        Assert.Null(result.Sheet);
        Assert.Contains("run:99", result.Refusal);
    }

    [Fact]
    public void AnExcludedSubject_IsRefused_WithTheComparisonsOwnExplanation()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", null, state: "Excluded",
                explanation: "Excluded: this run was graded under a different rubric."),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 80));

        var result = BenchmarkReportFacts.Build(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google")));

        Assert.Null(result.Sheet);
        Assert.Equal("Excluded: this run was graded under a different rubric.", result.Refusal);
    }

    [Fact]
    public void ADegradedSubject_IsIncluded_WithItsDegradedAxisUnavailable()
    {
        const string explanation = "Degraded: speed was measured with parallel execution disabled.";
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 75, 85, modelTimeP50Ms: 9000,
                state: "Degraded", speedDegraded: true, explanation: explanation),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 70, 65, 75, modelTimeP50Ms: 12000));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google")));

        Assert.Equal("Degraded", sheet.SubjectState);
        foreach (var key in new[] { "speed.modelTimeP50", "speed.rank" })
        {
            var fact = FactOf(sheet, key);
            Assert.False(fact.Available);
            Assert.Equal(BenchmarkReportFacts.NotAvailable, fact.Display);
            Assert.Equal(explanation, fact.UnavailableReason);
        }

        // Quality is still comparable on a degraded entry.
        Assert.True(FactOf(sheet, "quality.index").Available);
        Assert.Equal("1st of 2", FactOf(sheet, "quality.rank").Display);

        var subjectFigures = Assert.Single(sheet.Entries, e => e.IsSubject);
        Assert.Null(subjectFigures.ModelTimeP50Ms);
        Assert.Null(subjectFigures.SpeedRank);
    }

    [Fact]
    public void AnExcludedEntry_IsNeverAPeer()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80),
            Entry("run:2", new long[] { 2 }, "Excluded peer", "Google", null, state: "Excluded"),
            Entry("run:3", new long[] { 3 }, "Kept peer", "Anthropic", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(3, "Anthropic")));

        var peer = Assert.Single(sheet.Peers);
        Assert.Equal("run:3", peer.EntryKey);
        Assert.DoesNotContain(sheet.Entries, e => e.EntryKey == "run:2");
    }

    [Fact]
    public void Peers_AreLetteredByQualityDescending_WithTiesBrokenByEntryKey()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60),
            Entry("run:3", new long[] { 3 }, "Low", "Google", 70),
            Entry("run:5", new long[] { 5 }, "Tied five", "Anthropic", 85),
            Entry("run:4", new long[] { 4 }, "Tied four", "xAI", 85));

        var sheet = BuildSheet(Input(comparison, "run:1",
            SimpleRun(1, "OpenAI"), SimpleRun(3, "Google"), SimpleRun(4, "xAI"), SimpleRun(5, "Anthropic")));

        Assert.Equal(new[] { "A", "B", "C" }, sheet.Peers.Select(p => p.Letter));
        Assert.Equal(new[] { "run:4", "run:5", "run:3" }, sheet.Peers.Select(p => p.EntryKey));

        // The subject first, then peers in letter order.
        Assert.Equal(new[] { "run:1", "run:4", "run:5", "run:3" }, sheet.Entries.Select(e => e.EntryKey));
        Assert.Equal("4th of 4", FactOf(sheet, "quality.rank").Display);
    }

    [Fact]
    public void Ranks_AreComputedPerAxis_OverTheEntriesThatHaveTheFigure_SkippingDegradedAxes()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, modelTimeP50Ms: 10000, costPerQuestion: 0.030),
            Entry("run:2", new long[] { 2 }, "Fast but degraded", "Google", 70, modelTimeP50Ms: 5000, costPerQuestion: 0.050,
                state: "Degraded", speedDegraded: true, explanation: "Degraded: parallel execution differed."),
            Entry("run:3", new long[] { 3 }, "Cheap", "Anthropic", 90, modelTimeP50Ms: 20000, costPerQuestion: 0.010));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google"), SimpleRun(3, "Anthropic")));

        Assert.Equal("2nd of 3", FactOf(sheet, "quality.rank").Display);
        Assert.Equal("1st of 2", FactOf(sheet, "speed.rank").Display);
        Assert.Equal("2nd of 3", FactOf(sheet, "cost.rank").Display);
        Assert.Equal("10.0 s", FactOf(sheet, "speed.modelTimeP50").Display);
        Assert.Equal("$0.030", FactOf(sheet, "cost.perQuestion").Display);

        var degraded = Assert.Single(sheet.Entries, e => e.EntryKey == "run:2");
        Assert.True(degraded.SpeedDegraded);
        Assert.Null(degraded.ModelTimeP50Ms);
        Assert.Null(degraded.SpeedRank);
        Assert.Equal(3, degraded.CostRank);
    }

    [Fact]
    public void TheIntervalOverlapNote_NamesOverlappingPeersByLetter_AndIsNeverATestResult()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 77, 83),
            Entry("run:2", new long[] { 2 }, "Above", "Google", 86, 82, 90),
            Entry("run:3", new long[] { 3 }, "Below", "Anthropic", 78, 74, 81),
            Entry("run:4", new long[] { 4 }, "Far below", "xAI", 50, 45, 55));

        var sheet = BuildSheet(Input(comparison, "run:1",
            SimpleRun(1, "OpenAI"), SimpleRun(2, "Google"), SimpleRun(3, "Anthropic"), SimpleRun(4, "xAI")));

        var overlap = FactOf(sheet, "quality.intervalOverlap");
        Assert.Equal("its 95 % interval overlaps those of Models A and B", overlap.Display);
        Assert.DoesNotContain("significan", overlap.Display, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("its 95 % interval overlaps no other model's", BenchmarkReportFacts.OverlapSentence(Array.Empty<string>()));
        Assert.Equal("its 95 % interval overlaps that of Model B", BenchmarkReportFacts.OverlapSentence(new[] { "B" }));
        Assert.Equal("its 95 % interval overlaps those of Models A, B and C", BenchmarkReportFacts.OverlapSentence(new[] { "A", "B", "C" }));
    }

    [Fact]
    public void TheNoSignificanceStatement_IsTheComparisonsOwn()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google")));

        var measure = comparison.ExcludedMeasures.Single(m => m.Measure == "Pairwise significance");
        Assert.Equal(measure.Summary, sheet.NoSignificanceSummary);
        Assert.Equal(measure.Instead, sheet.NoSignificanceInstead);
    }

    [Fact]
    public void PerQuestionFigures_PairOnQuestionAndItemRevision_AgainstThePeersMean()
    {
        var subject = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 90, 20), new AnswerSpec(12, 2, 2, 60, 60));
        var peerA = Run(2, "Google", "peer-a", new AnswerSpec(11, 1, 1, 80, 20), new AnswerSpec(12, 2, 1, 100, 60));
        var peerB = Run(3, "Anthropic", "peer-b", new AnswerSpec(11, 1, 1, 70, 20));

        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 75),
            Entry("run:2", new long[] { 2 }, "Peer A", "Google", 90),
            Entry("run:3", new long[] { 3 }, "Peer B", "Anthropic", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, peerA, peerB));

        Assert.Equal(new[] { 1, 2 }, sheet.Questions.Select(q => q.Number));

        var q1 = sheet.Questions[0];
        Assert.Equal("11", q1.QuestionKey);
        Assert.Equal(90, q1.Score);
        Assert.Equal(75, q1.PeerMean);
        Assert.Equal(15, q1.Difference);
        Assert.Equal(2, q1.PeerCount);
        Assert.Equal("Simple", q1.Band);

        // Peer A answered question 12 at revision 1; the subject at revision 2. They do not pair.
        var q2 = sheet.Questions[1];
        Assert.Equal(60, q2.Score);
        Assert.Null(q2.PeerMean);
        Assert.Null(q2.Difference);
        Assert.Equal(0, q2.PeerCount);
        Assert.Equal("Intermediate", q2.Band);
    }

    [Fact]
    public void AGroupSubject_AveragesPerQuestionFiguresOverItsRuns_AndCountsFindingRecurrence()
    {
        var first = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80), new AnswerSpec(12, 2, 1, 40));
        var second = Run(2, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 60), new AnswerSpec(12, 2, 1, 50));
        first.AssessmentJson = Synthesis(("weakness", "accuracy", new[] { 2 }, "Q2 is wrong."));
        second.AssessmentJson = Synthesis(
            ("weakness", "accuracy", new[] { 2 }, "Q2 is still wrong."),
            ("strength", "readability", new[] { 1 }, "Q1 reads well."));
        var peer = SimpleRun(3, "Google", 70);

        var comparison = Comparison(
            Entry("group:9", new long[] { 1, 2 }, "Subject", "OpenAI", 58),
            Entry("run:3", new long[] { 3 }, "Peer", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "group:9", first, second, peer));

        Assert.Equal("Group", sheet.SubjectKind);
        Assert.Equal(new long[] { 1, 2 }, sheet.SubjectRunIds);

        var q1 = sheet.Questions[0];
        Assert.Equal(70, q1.Score);
        Assert.Equal(2, q1.RunCount);

        Assert.Equal(2, sheet.Rows.Count);
        var recurring = sheet.Rows[0];
        Assert.Equal("R1", recurring.Id);
        Assert.Equal("weakness", recurring.Kind);
        Assert.Equal(new[] { 2 }, recurring.Questions);
        Assert.Equal(2, recurring.Recurrence);
        Assert.Equal("Q2 is wrong.", recurring.MemberAText);
        Assert.Equal(BenchmarkReportFacts.SupportSingleAssessor, recurring.SupportLabel);

        var once = sheet.Rows[1];
        Assert.Equal("R2", once.Id);
        Assert.Equal(1, once.Recurrence);
        Assert.Equal(new[] { 1 }, once.Questions);
    }

    [Fact]
    public void SynthesisQuestionNumbers_AreMappedToTheReportsNumbering()
    {
        // Order indexes 5 and 9 become questions 1 and 2.
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 5, 1, 80), new AnswerSpec(12, 9, 1, 40));
        run.AssessmentJson = Synthesis(("weakness", "completeness", new[] { 9 }, "Omits the timeout."));

        var comparison = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60));
        var sheet = BuildSheet(Input(comparison, "run:1", run));

        Assert.Equal(new[] { 5, 9 }, sheet.Questions.Select(q => q.OrderIndex));
        Assert.Equal(new[] { 2 }, Assert.Single(sheet.Rows).Questions);
    }

    [Fact]
    public void PanelRows_TakeTheirSupportLabelFromTheMembersFamilyRelationToTheModel()
    {
        var run = Run(1, "Google", "subject", new AnswerSpec(11, 1, 1, 80), new AnswerSpec(12, 2, 1, 40), new AnswerSpec(13, 3, 1, 60));
        run.CoAssessorModelConfigurationId = 2;
        run.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-haiku-5");
        // Member A is Google, the subject's own provider.
        run.AssessmentJson = Synthesis(
            ("weakness", "accuracy", new[] { 1 }, "A: wrong on Q1."),
            ("strength", "readability", new[] { 2 }, "A: reads well."),
            ("strength", "conciseness", new[] { 3 }, "A: brief."));
        run.CoAssessorSynthesisJson = Synthesis(
            ("weakness", "accuracy", new[] { 1 }, "B: wrong on Q1."),
            ("weakness", "conciseness", new[] { 3 }, "B: too terse."),
            ("weakness", "tool_use", Array.Empty<int>(), "B: few tool calls."));

        var comparison = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "Google", 60));
        var sheet = BuildSheet(Input(comparison, "run:1", run));

        string LabelOf(string category) => sheet.Rows.Single(r => r.Category == category).SupportLabel;

        Assert.Equal(BenchmarkReportFacts.SupportBothGraders, LabelOf("accuracy"));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderSameFamily, LabelOf("readability"));
        Assert.Equal(BenchmarkReportFacts.SupportGradersDisagree, LabelOf("conciseness"));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderDifferentFamily, LabelOf("tool_use"));

        var memberA = Assert.Single(sheet.Graders, g => g.Role == BenchmarkReportFacts.PanelMemberARole);
        Assert.True(memberA.SameFamilyAsSubject);
        var memberB = Assert.Single(sheet.Graders, g => g.Role == BenchmarkReportFacts.PanelMemberBRole);
        Assert.False(memberB.SameFamilyAsSubject);
    }

    [Fact]
    public void SupportLabelFor_PicksTheStrongestCitedRow_AndIsComputedWithoutRows()
    {
        var sheet = new BenchmarkReportFactSheet
        {
            Rows = new List<BenchmarkReportFindingRow>
            {
                new() { Id = "R1", SupportLabel = BenchmarkReportFacts.SupportGradersDisagree },
                new() { Id = "R2", SupportLabel = BenchmarkReportFacts.SupportOneGraderSameFamily },
                new() { Id = "R3", SupportLabel = BenchmarkReportFacts.SupportSingleAssessor },
                new() { Id = "R4", SupportLabel = BenchmarkReportFacts.SupportOneGraderDifferentFamily },
                new() { Id = "R5", SupportLabel = BenchmarkReportFacts.SupportBothGraders }
            }
        };

        Assert.Equal(BenchmarkReportFacts.SupportGradersDisagree, BenchmarkReportFacts.SupportLabelFor(new[] { "R1" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderSameFamily, BenchmarkReportFacts.SupportLabelFor(new[] { "R1", "R2" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportSingleAssessor, BenchmarkReportFacts.SupportLabelFor(new[] { "R2", "R3", "R1" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderDifferentFamily, BenchmarkReportFacts.SupportLabelFor(new[] { "R3", "R4" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportBothGraders, BenchmarkReportFacts.SupportLabelFor(new[] { "R4", "Q2", "R5" }, sheet));

        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(new[] { "quality.index", "Q3" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(new[] { "R9" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(Array.Empty<string>(), sheet));
    }

    [Fact]
    public void Facts_AreSortedByKeyOrdinal_WithUniqueKeys_AndInvariantDisplays()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80.4, 77.1, 83.6, modelTimeP50Ms: 12345, costPerQuestion: 0.0364),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 70, 65, 75));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google", 70)));

        var keys = sheet.Facts.Select(f => f.Key).ToList();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToList(), keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal("80 ± 3 / 100", FactOf(sheet, "quality.index").Display);
        Assert.Equal("77–84", FactOf(sheet, "quality.interval").Display);
        Assert.Equal("12.3 s", FactOf(sheet, "speed.modelTimeP50").Display);
        Assert.Equal("$0.036", FactOf(sheet, "cost.perQuestion").Display);
        Assert.Equal("1st of 2", FactOf(sheet, "quality.rank").Display);
        Assert.Equal("+10", FactOf(sheet, "dimension.accuracy.difference").Display);

        // Every entry's extra facts are sorted the same way.
        foreach (var entry in sheet.Entries)
        {
            var extra = entry.Extra.Select(f => f.Key).ToList();
            Assert.Equal(extra.OrderBy(k => k, StringComparer.Ordinal).ToList(), extra);
        }

        var names = sheet.KnownNames;
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
        Assert.Contains("Peer", names);
        Assert.Contains("GnollHack Core Suite", names);
    }
}
