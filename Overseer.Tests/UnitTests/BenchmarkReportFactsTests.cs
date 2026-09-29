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
    public void TheIntervalSpan_IsThePrintedUpperBoundMinusThePrintedLowerBound()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 78, 72.5, 82.4),
            Entry("run:2", new long[] { 2 }, "No bounds", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google")));

        Assert.Equal("73–82", FactOf(sheet, "quality.interval").Display);
        var span = FactOf(sheet, "quality.intervalSpan");
        Assert.True(span.Available);
        Assert.Equal("9 points", span.Display);
        Assert.Equal(9, span.Value!.GetValue<int>());
        Assert.False(BenchmarkReportFacts.IsPeerFact("quality.intervalSpan"));

        var noBounds = BuildSheet(Input(comparison, "run:2", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google")));
        var missing = FactOf(noBounds, "quality.intervalSpan");
        Assert.False(missing.Available);
        Assert.Equal("No interval could be computed.", missing.UnavailableReason);

        var noQuality = BuildSheet(Input(
            Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", null)), "run:1", SimpleRun(1, "OpenAI")));
        Assert.False(FactOf(noQuality, "quality.intervalSpan").Available);
    }

    [Fact]
    public void NormalizeSupportLabels_RewritesTheFamilyLabelsOfOlderSheets_AndIsIdempotent()
    {
        var sheet = new BenchmarkReportFactSheet
        {
            Rows = new List<BenchmarkReportFindingRow>
            {
                new() { Id = "R1", SupportLabel = LegacyDifferentFamilyLabel },
                new() { Id = "R2", SupportLabel = LegacySameFamilyLabel },
                new() { Id = "R3", SupportLabel = BenchmarkReportFacts.SupportBothGraders }
            }
        };

        // Unnormalized, the legacy labels rank nowhere.
        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(new[] { "R1" }, sheet));

        BenchmarkReportFacts.NormalizeSupportLabels(sheet);
        BenchmarkReportFacts.NormalizeSupportLabels(sheet);

        Assert.Equal(
            new[] { BenchmarkReportFacts.SupportOneGraderDifferentProvider, BenchmarkReportFacts.SupportOneGraderSameProvider, BenchmarkReportFacts.SupportBothGraders },
            sheet.Rows.Select(r => r.SupportLabel));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderDifferentProvider, BenchmarkReportFacts.SupportLabelFor(new[] { "R1", "R2" }, sheet));
        Assert.Equal("One grader — different provider", BenchmarkReportFacts.SupportOneGraderDifferentProvider);
        Assert.Equal("One grader — same provider as the model", BenchmarkReportFacts.SupportOneGraderSameProvider);
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
    public void PanelRows_TakeTheirSupportLabelFromTheMembersProviderRelationToTheModel()
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
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderSameProvider, LabelOf("readability"));
        Assert.Equal(BenchmarkReportFacts.SupportGradersDisagree, LabelOf("conciseness"));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderDifferentProvider, LabelOf("tool_use"));

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
                new() { Id = "R2", SupportLabel = BenchmarkReportFacts.SupportOneGraderSameProvider },
                new() { Id = "R3", SupportLabel = BenchmarkReportFacts.SupportSingleAssessor },
                new() { Id = "R4", SupportLabel = BenchmarkReportFacts.SupportOneGraderDifferentProvider },
                new() { Id = "R5", SupportLabel = BenchmarkReportFacts.SupportBothGraders }
            }
        };

        Assert.Equal(BenchmarkReportFacts.SupportGradersDisagree, BenchmarkReportFacts.SupportLabelFor(new[] { "R1" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderSameProvider, BenchmarkReportFacts.SupportLabelFor(new[] { "R1", "R2" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportSingleAssessor, BenchmarkReportFacts.SupportLabelFor(new[] { "R2", "R3", "R1" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportOneGraderDifferentProvider, BenchmarkReportFacts.SupportLabelFor(new[] { "R3", "R4" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportBothGraders, BenchmarkReportFacts.SupportLabelFor(new[] { "R4", "Q2", "R5" }, sheet));

        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(new[] { "quality.index", "Q3" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(new[] { "R9" }, sheet));
        Assert.Equal(BenchmarkReportFacts.SupportComputed, BenchmarkReportFacts.SupportLabelFor(Array.Empty<string>(), sheet));
    }

    [Fact]
    public void AStandaloneSubject_HasEveryPeerFactUnavailable_WithTheStandaloneReason()
    {
        var comparison = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 77, 83, modelTimeP50Ms: 9000, costPerQuestion: 0.02));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI")));

        Assert.Empty(sheet.Peers);
        string[] peerFacts =
        {
            "quality.peerMedian", "quality.peerBest", "quality.intervalOverlap", "quality.rank", "speed.rank", "cost.rank",
            "panel.judgeDependentPairs", "dimension.accuracy.peerMean", "dimension.accuracy.difference",
            "band.simple.peerMean", "band.simple.difference", "tools.callsPerQuestion.peerMean"
        };
        foreach (string key in peerFacts)
        {
            var fact = FactOf(sheet, key);
            Assert.False(fact.Available, key + " is available.");
            Assert.Equal(BenchmarkReportFacts.StandaloneReason, fact.UnavailableReason);
            Assert.Equal(BenchmarkReportFacts.NotAvailable, fact.Display);
        }

        // The subject's own figures stay.
        Assert.True(FactOf(sheet, "quality.index").Available);
        Assert.True(FactOf(sheet, "dimension.accuracy").Available);
        Assert.True(FactOf(sheet, "speed.modelTimeP50").Available);
        Assert.Equal("1", FactOf(sheet, "comparison.models").Display);
    }

    [Fact]
    public void ASubjectWithPeers_KeepsItsPeerFacts()
    {
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 77, 83),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 70, 65, 75));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI"), SimpleRun(2, "Google", 70)));

        Assert.True(FactOf(sheet, "quality.rank").Available);
        Assert.True(FactOf(sheet, "dimension.accuracy.peerMean").Available);
        Assert.DoesNotContain(sheet.Facts, f => f.UnavailableReason == BenchmarkReportFacts.StandaloneReason);
    }

    [Fact]
    public void PurposeStatements_AreTheSubjectRunsOwn_Distinct_InRunOrder()
    {
        var first = SimpleRun(1, "OpenAI");
        first.PurposeStatementUsed = "Internal evaluation, round one.";
        var second = SimpleRun(2, "OpenAI");
        second.PurposeStatementUsed = "Internal evaluation, round one. ";
        var third = SimpleRun(3, "OpenAI");
        third.PurposeStatementUsed = "Internal evaluation, round two.";

        var comparison = Comparison(Entry("group:9", new long[] { 1, 2, 3 }, "Subject", "OpenAI", 80));
        var sheet = BuildSheet(Input(comparison, "group:9", first, second, third));

        Assert.Equal(new[] { "Internal evaluation, round one.", "Internal evaluation, round two." }, sheet.PurposeStatements);
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

        Assert.Equal("80 / 100", FactOf(sheet, "quality.index").Display);
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

    [Fact]
    public void TheIndexAndItsInterval_AreRoundedTheSameWay_AndPrintedTogether()
    {
        var comparison = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 77.4, 73.0, 81.8));

        var sheet = BuildSheet(Input(comparison, "run:1", SimpleRun(1, "OpenAI")));

        Assert.Equal("77 / 100", FactOf(sheet, "quality.index").Display);
        Assert.Equal("73–82", FactOf(sheet, "quality.interval").Display);
        Assert.DoesNotContain(sheet.Facts, f => f.Display.Contains('±'));

        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.Contains("- **Intelligence:** 77 / 100 (interval 73–82).\n", text);
    }

    private static BenchmarkClaimVerification Verification(string claim, BenchmarkClaimVerdict verdict, params string[] roles)
        => new(0, claim, verdict, null, null) { Roles = roles.Length == 0 ? null : roles };

    [Fact]
    public void RefutedAnswerSentences_CountTheAnswersOwnText_AndNeverAGradersStatement()
    {
        static BenchmarkClaimVerification V(string claim, BenchmarkClaimVerdict verdict, params string[] roles)
            => Verification(claim, verdict, roles);

        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 40), new AnswerSpec(12, 2, 1, 70), new AnswerSpec(13, 3, 1, 90));
        run.Answers[0].ClaimsRefutedCount = 2;
        run.Answers[0].ClaimVerificationJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            V("Sentence one.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim),
            V("Sentence two.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote),
            V("Sentence three.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.CriticalErrorQuote),
            V("Sentence four.", BenchmarkClaimVerdict.Refuted),
            V("A grader's statement.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement),
            V("A grader's basis.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.OutOfRubricBasis),
            V("Sentence five.", BenchmarkClaimVerdict.Supported, BenchmarkClaimRoles.UnverifiedClaim),
            V("Sentence six.", BenchmarkClaimVerdict.Indeterminate, BenchmarkClaimRoles.AccusedQuote)
        });
        // Stored before harness 31: no roles, so the count cannot be told apart from the grader's.
        run.Answers[1].ClaimsRefutedCount = 1;
        run.Answers[1].ClaimVerificationJson = System.Text.Json.JsonSerializer.Serialize(new[] { V("A sentence.", BenchmarkClaimVerdict.Refuted) });

        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60)), "run:1", run));

        Assert.Equal(4, sheet.Questions[0].RefutedAnswerSentences);
        Assert.Equal(2, sheet.Questions[0].RefutedClaims);
        Assert.Null(sheet.Questions[1].RefutedAnswerSentences);
        Assert.Equal(1, sheet.Questions[1].RefutedClaims);
        Assert.Equal(0, sheet.Questions[2].RefutedAnswerSentences);
    }

    [Fact]
    public void RefutedAnswerSentences_CountASentenceOnce_ThoughItWasAccusedAndQuotedAsACriticalError()
    {
        var run = Run(1, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 25), new AnswerSpec(12, 2, 1, 70));
        // Run 74's shape: one sentence ruled on as the critical-error quote and as the accused
        // sentence, the two copies differing only in markup and case, beside one other sentence.
        run.Answers[0].ClaimsRefutedCount = 3;
        run.Answers[0].ClaimVerificationJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            Verification("A thrown gem always shatters on impact.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.CriticalErrorQuote),
            Verification("- **A thrown gem** always shatters on  impact.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote),
            Verification("a thrown gem always shatters on impact.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim),
            Verification("Glass is always destroyed.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim)
        });
        // The same sentence on another answer is another answer's sentence.
        run.Answers[1].ClaimVerificationJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            Verification("A thrown gem always shatters on impact.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote)
        });

        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 60)), "run:1", run));

        Assert.Equal(2, sheet.Questions[0].RefutedAnswerSentences);
        Assert.Equal(3, sheet.Questions[0].RefutedClaims);
        Assert.Equal(1, sheet.Questions[1].RefutedAnswerSentences);
    }

    // ---------------------------------------------------------------------------------------------
    // Panel runs: dimensions, response style and the reference reader
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A two-answer panel run whose member A scores every dimension 80 at level 5 and whose member B
    /// grades Accuracy, Conciseness and Readability at level 6 (100) and Completeness at level 2 (35).
    /// </summary>
    private static BenchmarkRun PanelRun(long id = 1)
    {
        var run = AsPanelRun(Run(id, "OpenAI", "subject", new AnswerSpec(11, 1, 1, 80), new AnswerSpec(12, 2, 1, 80)),
            accuracyLevel: 6, completenessLevel: 2, concisenessLevel: 6, readabilityLevel: 6);
        run.AssessorOnlyQualityIndex = 80;
        run.CoAssessorOnlyQualityIndex = 84;
        return run;
    }

    [Fact]
    public void APanelRunsDimensionFacts_AreThePanelRow_TheMeanOfBothMembersAverages()
    {
        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", PanelRun()));

        // Member A 80 everywhere; member B 100, 35, 100, 100 on the default level table.
        Assert.Equal("90", FactOf(sheet, "dimension.accuracy").Display);
        Assert.Equal(57.5, FactOf(sheet, "dimension.completeness").Value!.GetValue<double>());
        Assert.Equal("58", FactOf(sheet, "dimension.completeness").Display);
        Assert.Equal("90", FactOf(sheet, "dimension.conciseness").Display);
        Assert.Equal("90", FactOf(sheet, "dimension.readability").Display);

        var subject = Assert.Single(sheet.Entries, e => e.IsSubject);
        Assert.Equal("58", Assert.Single(subject.Extra, f => f.Key == "dimension.completeness").Display);

        var averages = BenchmarkPanelDimensions.Averages(PanelRun().Answers.ToList(), null)!;
        Assert.Equal(new[] { 80.0, 80.0, 80.0, 80.0 }, averages.MemberA.Select(d => d.Points));
        Assert.Equal(new[] { 100.0, 35.0, 100.0, 100.0 }, averages.MemberB!.Select(d => d.Points));
        Assert.Equal(new[] { 5.5, 3.5, 5.5, 5.5 }, averages.Panel!.Select(d => d.Level));
        Assert.Equal(2, averages.MemberBAnswerCount);
        Assert.Null(BenchmarkPanelDimensions.Averages(new List<BenchmarkRunAnswer>(), null));
    }

    [Fact]
    public void APanelRunsDimensionFacts_AreUnavailable_WhenMemberBRecordedNoLevels()
    {
        var run = PanelRun();
        foreach (var answer in run.Answers) answer.CoAssessmentJson = null;

        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", run));

        var accuracy = FactOf(sheet, "dimension.accuracy");
        Assert.False(accuracy.Available);
        Assert.Contains("member B", accuracy.UnavailableReason);
    }

    [Fact]
    public void TheResponseStyleConflict_OfAPanelRun_IsReadOnThePanelRow_AndItsDisplayIsAClause()
    {
        var panel = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", PanelRun()));

        // Member A alone (80 everywhere) has no conflict; the panel row's Completeness is 32.5 below Accuracy.
        var style = FactOf(panel, "style.responseStyleConflict");
        Assert.True(style.Value!.GetValue<bool>());
        Assert.Equal("Completeness is the lowest dimension, 32.5 points below Accuracy", style.Display);
        Assert.True(BenchmarkReportPackPrompt.IsTrue(style));

        var single = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", SimpleRun(1, "OpenAI")));
        var none = FactOf(single, "style.responseStyleConflict");
        Assert.False(none.Value!.GetValue<bool>());
        Assert.Equal("No response-style conflict", none.Display);
        Assert.False(BenchmarkReportPackPrompt.IsTrue(none));
    }

    [Fact]
    public void TheReferenceReader_OfAPanelRun_HasItsIndexAndMeanOffset_AsTheRunReportComputesThem()
    {
        var run = PanelRun();
        run.SecondOpinionAssessorModelConfigurationId = 3;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "DeepSeek", modelId: "deepseek-v4", displayName: "DeepSeek V4");
        foreach (var answer in run.Answers) answer.SecondOpinionQualityScore = 90;

        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", run));

        Assert.Equal("90 / 100", FactOf(sheet, "panel.referenceReaderIndex").Display);
        // 90 against a panel score of 80 on both answers.
        Assert.Equal("+10.0 points", FactOf(sheet, "panel.referenceReaderOffset").Display);
        Assert.Contains(sheet.Graders, g => g.Role == BenchmarkReportFacts.ReferenceReaderRole && g.Provider == "DeepSeek");
        Assert.Equal("Reference reader (advisory, third provider)", BenchmarkReportFactLabels.Label("panel.referenceReaderIndex"));
        Assert.Equal("Reference reader's mean offset from the panel", BenchmarkReportFactLabels.Label("panel.referenceReaderOffset"));

        var withoutReader = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", PanelRun()));
        Assert.False(FactOf(withoutReader, "panel.referenceReaderIndex").Available);
        Assert.False(FactOf(withoutReader, "panel.referenceReaderOffset").Available);

        var notPanel = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", SimpleRun(1, "OpenAI")));
        Assert.Equal("The model was not graded by an assessor panel in every run.", FactOf(notPanel, "panel.referenceReaderIndex").UnavailableReason);
    }

    [Fact]
    public void TheKnowledgeBaseCount_IsStated_OnlyWhenSomeQuestionIsAKnowledgeBaseTopic()
    {
        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", SimpleRun(1, "OpenAI")));
        var none = FactOf(sheet, "tools.zeroKnowledgeBaseAnswers");
        Assert.False(none.Available);
        Assert.Equal("No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base.", none.UnavailableReason);

        var run = SimpleRun(1, "OpenAI");
        run.Answers[0].QuestionText = "Where do I change the sound settings?";
        var topical = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", run));
        var count = FactOf(topical, "tools.zeroKnowledgeBaseAnswers");
        Assert.True(count.Available);
        Assert.Equal("2 of 2", count.Display);
    }

    [Fact]
    public void TheScoredQuestions_CarryTheirNoun()
    {
        var sheet = BuildSheet(Input(Comparison(Entry("group:9", new long[] { 1, 2 }, "Subject", "OpenAI", 80)), "group:9",
            SimpleRun(1, "OpenAI"), SimpleRun(2, "OpenAI")));
        Assert.Equal("2 of 2 questions", FactOf(sheet, "quality.scoredItems").Display);

        var one = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", SimpleRun(1, "OpenAI")));
        Assert.Equal("1 of 1 question", FactOf(one, "quality.scoredItems").Display);
    }

    [Fact]
    public void ThePricingBasisKind_IsCatalogWithItsDate_OrSnapshot()
    {
        var current = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80));
        current.ComputedAtUtc = new DateTime(2026, 9, 29, 8, 30, 0, DateTimeKind.Utc);
        var catalog = BuildSheet(Input(current, "run:1", SimpleRun(1, "OpenAI")));

        Assert.Equal(BenchmarkReportFacts.PricingBasisCatalog, FactOf(catalog, "comparison.pricingBasisKind").Display);
        Assert.Equal("2026-09-29", FactOf(catalog, "comparison.pricedOn").Display);

        var asRun = Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80));
        asRun.PricingBasis = nameof(BenchmarkModelComparisonPricingBasis.AsRun);
        var snapshot = BuildSheet(Input(asRun, "run:1", SimpleRun(1, "OpenAI")));

        Assert.Equal(BenchmarkReportFacts.PricingBasisSnapshot, FactOf(snapshot, "comparison.pricingBasisKind").Display);
        Assert.False(FactOf(snapshot, "comparison.pricedOn").Available);
    }

    [Fact]
    public void TokenFacts_AreTheMeanOverTheAnswersThatRecordedThem()
    {
        var run = SimpleRun(1, "OpenAI");
        run.Answers[0].InputTokens = 12000;
        run.Answers[0].OutputTokens = 900;
        run.Answers[1].InputTokens = 13001;

        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", run));

        Assert.Equal("12,501", FactOf(sheet, "tokens.inputPerQuestion").Display);
        Assert.Equal("900", FactOf(sheet, "tokens.outputPerQuestion").Display);

        var none = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80)), "run:1", SimpleRun(1, "OpenAI")));
        Assert.False(FactOf(none, "tokens.inputPerQuestion").Available);
    }

    [Theory]
    [InlineData(3.0, "3")]
    [InlineData(2.5, "2.5")]
    [InlineData(2.46, "2.5")]
    [InlineData(2.04, "2")]
    [InlineData(-0.04, "0")]
    public void CompactDecimal_DropsAZeroDecimal(double value, string expected)
    {
        Assert.Equal(expected, BenchmarkReportFormat.CompactDecimal(value));
        Assert.Equal("3.0", BenchmarkReportFormat.OneDecimal(3.0));
    }

    // ---------------------------------------------------------------------------------------------
    // Per-peer facts, paired differences and the compared-models figures
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EachPeer_HasItsOwnFigures_UnavailableOnTheAxesItIsDegradedOn()
    {
        const string explanation = "Plotted with a degraded axis: quality is sound, and speed and cost mix conditions across the set.";
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 77, 83, modelTimeP50Ms: 10000, costPerQuestion: 0.030),
            Entry("run:2", new long[] { 2 }, "Above", "Google", 86, 82, 90, modelTimeP50Ms: 8000, costPerQuestion: 0.050),
            Entry("run:3", new long[] { 3 }, "Degraded", "Anthropic", 50, 45, 55, modelTimeP50Ms: 5000, costPerQuestion: 0.010,
                state: "Degraded", speedDegraded: true, costDegraded: true, explanation: explanation),
            Entry("group:7", new long[] { 4, 5, 6 }, "Group", "xAI", 40, null, null));

        var sheet = BuildSheet(Input(comparison, "run:1",
            SimpleRun(1, "OpenAI"), SimpleRun(2, "Google"), SimpleRun(3, "Anthropic"),
            SimpleRun(4, "xAI"), SimpleRun(5, "xAI"), SimpleRun(6, "xAI")));

        Assert.Equal(new[] { "run:2", "run:3", "group:7" }, sheet.Peers.Select(p => p.EntryKey));

        Assert.Equal("86 / 100", FactOf(sheet, "peer.A.quality.index").Display);
        Assert.Equal("82–90", FactOf(sheet, "peer.A.quality.interval").Display);
        Assert.Equal("1st of 4", FactOf(sheet, "peer.A.quality.rank").Display);
        Assert.Equal("8.0 s", FactOf(sheet, "peer.A.speed.medianSeconds").Display);
        Assert.Equal("$0.050", FactOf(sheet, "peer.A.cost.perQuestion").Display);
        Assert.Equal("1 run", FactOf(sheet, "peer.A.runs").Display);
        var overlapA = FactOf(sheet, "peer.A.intervalOverlap");
        Assert.True(overlapA.Value!.GetValue<bool>());
        Assert.Equal("its 95 % interval overlaps the subject's", overlapA.Display);

        // Model B is degraded on speed and cost: those figures carry its explanation; quality stays.
        Assert.Equal("3rd of 4", FactOf(sheet, "peer.B.quality.rank").Display);
        foreach (var key in new[] { "peer.B.speed.medianSeconds", "peer.B.cost.perQuestion" })
        {
            var fact = FactOf(sheet, key);
            Assert.False(fact.Available, key);
            Assert.Equal(explanation, fact.UnavailableReason);
        }
        var overlapB = FactOf(sheet, "peer.B.intervalOverlap");
        Assert.False(overlapB.Value!.GetValue<bool>());
        Assert.Equal("its 95 % interval does not overlap the subject's", overlapB.Display);

        // A group peer counts its runs; without an interval its overlap cannot be stated.
        Assert.Equal("3 runs", FactOf(sheet, "peer.C.runs").Display);
        Assert.Equal(3, FactOf(sheet, "peer.C.runs").Value!.GetValue<int>());
        Assert.False(FactOf(sheet, "peer.C.quality.interval").Available);
        Assert.Equal("This model's quality interval could not be computed.", FactOf(sheet, "peer.C.intervalOverlap").UnavailableReason);

        Assert.Equal("1", FactOf(sheet, "subject.runs").Display);
        foreach (var fact in sheet.Facts.Where(f => f.Key.StartsWith("peer.", StringComparison.Ordinal)))
        {
            Assert.True(BenchmarkReportFacts.IsPeerFact(fact.Key), fact.Key);
            Assert.True(BenchmarkReportFactLabels.TryLabel(fact.Key, out _), fact.Key);
        }
    }

    [Fact]
    public void AStandaloneSheet_HasNoPerPeerFacts_AndNoPairedDifferences()
    {
        var sheet = BuildSheet(Input(Comparison(Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80, 77, 83)), "run:1", SimpleRun(1, "OpenAI")));

        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("peer.", StringComparison.Ordinal));
        Assert.Empty(sheet.PairedDifferences);
        Assert.True(BenchmarkReportFacts.IsPeerFact("peer.A.quality.index"));
    }

    [Fact]
    public void AGroupSubject_StatesItsRunCount()
    {
        var comparison = Comparison(
            Entry("group:9", new long[] { 1, 2 }, "Subject", "OpenAI", 80),
            Entry("run:3", new long[] { 3 }, "Peer", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "group:9", SimpleRun(1, "OpenAI"), SimpleRun(2, "OpenAI"), SimpleRun(3, "Google")));

        Assert.Equal("2", FactOf(sheet, "subject.runs").Display);
        Assert.Equal("1 run", FactOf(sheet, "peer.A.runs").Display);
    }

    /// <summary>A run answering questions 1 to n at revision 1 with the given qualities.</summary>
    private static BenchmarkRun RunScoring(long id, string provider, params int[] qualities)
        => Run(id, provider, "model-" + id, qualities.Select((q, i) => new AnswerSpec(i + 1, i + 1, 1, q)).ToArray());

    [Fact]
    public void ThePairedDifference_IsTheMeanPerQuestionDifference_OverTheSharedQuestions()
    {
        // Differences 10, 0, 10, 10, -10 and 0: a mean of 20 / 6.
        var subject = RunScoring(1, "OpenAI", 80, 70, 60, 90, 50, 40);
        var peer = RunScoring(2, "Google", 70, 70, 50, 80, 60, 40);
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 65, 60, 70),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 62, 57, 67));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, peer));

        var paired = Assert.Single(sheet.PairedDifferences);
        Assert.Equal("A", paired.PeerLetter);
        Assert.Equal(6, paired.SharedQuestions);
        Assert.Equal(20.0 / 6.0, paired.MeanDifference!.Value, 9);
        Assert.True(paired.Lower <= paired.MeanDifference && paired.MeanDifference <= paired.Upper);

        Assert.Equal("+3.3 points", FactOf(sheet, "peer.A.pairedDifference").Display);
        Assert.Equal("6 questions", FactOf(sheet, "peer.A.sharedQuestions").Display);

        var expected = BenchmarkReportFacts.PairedBootstrap(new double[] { 10, 0, 10, 10, -10, 0 }, BenchmarkReportFacts.PairedSeed(new[] { "run:1", "run:2" }));
        Assert.Equal(expected.Lower, paired.Lower!.Value, 12);
        Assert.Equal(expected.Upper, paired.Upper!.Value, 12);
        Assert.Equal(
            BenchmarkReportFormat.SignedOneDecimal(expected.Lower) + " to " + BenchmarkReportFormat.SignedOneDecimal(expected.Upper),
            FactOf(sheet, "peer.A.pairedInterval").Display);
    }

    [Fact]
    public void ThePairedInterval_OfIdenticalDifferences_IsThatDifference()
    {
        // Every resample of five equal differences has the same mean.
        var subject = RunScoring(1, "OpenAI", 60, 70, 80, 90, 55);
        var peer = RunScoring(2, "Google", 65, 75, 85, 95, 60);
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 70),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 75));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, peer));

        Assert.Equal("-5.0 points", FactOf(sheet, "peer.A.pairedDifference").Display);
        Assert.Equal("-5.0 to -5.0", FactOf(sheet, "peer.A.pairedInterval").Display);
        Assert.Equal("5 questions", FactOf(sheet, "peer.A.sharedQuestions").Display);
    }

    [Fact]
    public void ThePairedDifference_CountsOnlyQuestionsBothScoredOnTheSameItemRevision()
    {
        // The peer answered question 6 at another revision and never saw question 7.
        var subject = Run(1, "OpenAI", "subject",
            new AnswerSpec(1, 1, 1, 80), new AnswerSpec(2, 2, 1, 80), new AnswerSpec(3, 3, 1, 80), new AnswerSpec(4, 4, 1, 80),
            new AnswerSpec(5, 5, 1, 80), new AnswerSpec(6, 6, 2, 80), new AnswerSpec(7, 7, 1, 80));
        var peer = Run(2, "Google", "peer",
            new AnswerSpec(1, 1, 1, 70), new AnswerSpec(2, 2, 1, 70), new AnswerSpec(3, 3, 1, 70), new AnswerSpec(4, 4, 1, 70),
            new AnswerSpec(5, 5, 1, 70), new AnswerSpec(6, 6, 1, 10));
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, peer));

        var paired = Assert.Single(sheet.PairedDifferences);
        Assert.Equal(5, paired.SharedQuestions);
        Assert.Equal(10.0, paired.MeanDifference!.Value, 9);
        Assert.Equal("+10.0 to +10.0", FactOf(sheet, "peer.A.pairedInterval").Display);
    }

    [Fact]
    public void AGroupSubjects_PairedDifference_UsesItsPerQuestionMeanOverRuns()
    {
        // The group scores 70 on every question (80 and 60); the peer 65.
        var first = RunScoring(1, "OpenAI", 80, 80, 80, 80, 80);
        var second = RunScoring(2, "OpenAI", 60, 60, 60, 60, 60);
        var peer = RunScoring(3, "Google", 65, 65, 65, 65, 65);
        var comparison = Comparison(
            Entry("group:9", new long[] { 1, 2 }, "Subject", "OpenAI", 70),
            Entry("run:3", new long[] { 3 }, "Peer", "Google", 65));

        var sheet = BuildSheet(Input(comparison, "group:9", first, second, peer));

        Assert.Equal("+5.0 points", FactOf(sheet, "peer.A.pairedDifference").Display);
        Assert.Equal("+5.0 to +5.0", FactOf(sheet, "peer.A.pairedInterval").Display);
    }

    [Fact]
    public void BelowFiveSharedQuestions_ThePairedFactsAreUnavailable_WithTheReason()
    {
        var subject = RunScoring(1, "OpenAI", 80, 70, 60, 90);
        var peer = RunScoring(2, "Google", 70, 70, 50, 80);
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 75),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 68));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, peer));

        var paired = Assert.Single(sheet.PairedDifferences);
        Assert.Equal(4, paired.SharedQuestions);
        Assert.Null(paired.MeanDifference);
        Assert.Null(paired.Lower);
        Assert.Null(paired.Upper);
        foreach (var key in new[] { "peer.A.pairedDifference", "peer.A.pairedInterval", "peer.A.sharedQuestions" })
        {
            var fact = FactOf(sheet, key);
            Assert.False(fact.Available, key);
            Assert.Equal(BenchmarkReportFacts.NotAvailable, fact.Display);
            Assert.Contains("Fewer than five questions", fact.UnavailableReason);
        }
        Assert.Equal(5, BenchmarkReportFacts.PairedMinimumQuestions);
    }

    [Fact]
    public void ThePairedInterval_IsDeterministic_ForTheSameComparisonInAnyOrder()
    {
        var subject = RunScoring(1, "OpenAI", 80, 72, 61, 90, 45, 38, 77);
        var peer = RunScoring(2, "Google", 70, 75, 50, 82, 60, 41, 70);
        var forward = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 66, 60, 72),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 64, 58, 70));
        var reversed = Comparison(
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 64, 58, 70),
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 66, 60, 72));

        var first = BuildSheet(Input(forward, "run:1", subject, peer));
        var second = BuildSheet(Input(forward, "run:1", subject, peer));
        var third = BuildSheet(Input(reversed, "run:1", subject, peer));

        string Interval(BenchmarkReportFactSheet sheet) => FactOf(sheet, "peer.A.pairedInterval").Display;
        Assert.Equal(Interval(first), Interval(second));
        Assert.Equal(Interval(first), Interval(third));
        Assert.Equal(first.PairedDifferences[0].Lower, third.PairedDifferences[0].Lower);
        Assert.Equal(first.PairedDifferences[0].Upper, third.PairedDifferences[0].Upper);

        var differences = new double[] { 10, -3, 11, 8, -15, -3, 7 };
        Assert.Equal(BenchmarkReportFacts.PairedBootstrap(differences, 12345), BenchmarkReportFacts.PairedBootstrap(differences, 12345));
    }

    [Fact]
    public void ThePairedSeed_IsTheComparisonKeysFirstEightHexDigits_OverEveryEntryKey()
    {
        string key = BenchmarkReportComparisonKey.From(new long[] { 1, 3 }, new long[] { 9 });
        int expected = unchecked((int)uint.Parse(key[..8], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, BenchmarkReportFacts.PairedSeed(new[] { "run:3", "group:9", "run:1" }));
        Assert.Equal(expected, BenchmarkReportFacts.PairedSeed(new[] { "run:1", "run:3", "group:9" }));
        Assert.Equal(BenchmarkReportFacts.FallbackPairedSeed, BenchmarkReportFacts.PairedSeed(new[] { "run:1", "other:2" }));
        Assert.Equal(BenchmarkReportFacts.FallbackPairedSeed, BenchmarkReportFacts.PairedSeed(Array.Empty<string>()));
        Assert.Equal(BenchmarkReportFacts.FallbackPairedSeed, BenchmarkReportFacts.PairedSeed(null));
    }

    [Fact]
    public void AnExcludedEntry_StillSeedsTheBootstrap()
    {
        var subject = RunScoring(1, "OpenAI", 80, 72, 61, 90, 45, 38, 77);
        var peer = RunScoring(2, "Google", 70, 75, 50, 82, 60, 41, 70);
        var withExcluded = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 66, 60, 72),
            Entry("run:2", new long[] { 2 }, "Peer", "Google", 64, 58, 70),
            Entry("run:8", new long[] { 8 }, "Excluded", "xAI", null, state: "Excluded"));

        var sheet = BuildSheet(Input(withExcluded, "run:1", subject, peer));

        var differences = new double[] { 10, -3, 11, 8, -15, -3, 7 };
        var expected = BenchmarkReportFacts.PairedBootstrap(differences, BenchmarkReportFacts.PairedSeed(new[] { "run:1", "run:2", "run:8" }));
        Assert.Equal(expected.Lower, sheet.PairedDifferences[0].Lower!.Value, 12);
        Assert.Equal(expected.Upper, sheet.PairedDifferences[0].Upper!.Value, 12);
    }

    [Fact]
    public void ThePairedBootstrap_CutsTheSortedResampleMeansAtTheTwoPointFiveAndNinetySevenPointFivePercentiles()
    {
        var differences = new double[] { -20, -5, 0, 5, 12, 30 };
        const int seed = 77;

        // The same draws, made here: 10,000 resample means, sorted, the 251st from each end.
        var random = new Random(seed);
        var means = new double[BenchmarkReportFacts.PairedBootstrapResamples];
        for (int b = 0; b < means.Length; b++)
        {
            double sum = 0;
            for (int i = 0; i < differences.Length; i++) sum += differences[random.Next(differences.Length)];
            means[b] = sum / differences.Length;
        }
        Array.Sort(means);

        var (mean, lower, upper) = BenchmarkReportFacts.PairedBootstrap(differences, seed);

        Assert.Equal(differences.Average(), mean, 12);
        Assert.Equal(means[250], lower);
        Assert.Equal(means[9749], upper);
        Assert.True(lower < mean && mean < upper);
    }

    [Fact]
    public void EachEntry_RecordsItsHarnessVersionAndRunDates()
    {
        var subject = SimpleRun(1, "OpenAI");
        subject.CompletedAtUtc = new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);
        var groupFirst = SimpleRun(2, "Google");
        var groupSecond = SimpleRun(3, "Google");
        groupSecond.HarnessVersion = "42";
        groupSecond.CompletedAtUtc = new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);
        var comparison = Comparison(
            Entry("run:1", new long[] { 1 }, "Subject", "OpenAI", 80),
            Entry("group:5", new long[] { 2, 3 }, "Group", "Google", 70));

        var sheet = BuildSheet(Input(comparison, "run:1", subject, groupFirst, groupSecond));

        var own = Assert.Single(sheet.Entries, e => e.IsSubject);
        Assert.Equal("41", own.HarnessVersion);
        Assert.Equal(subject.CompletedAtUtc, own.FirstRunUtc);
        Assert.Equal(subject.CompletedAtUtc, own.LastRunUtc);

        // A run without a completion time is dated by its start.
        var group = Assert.Single(sheet.Entries, e => e.EntryKey == "group:5");
        Assert.Equal(BenchmarkReportFacts.MixedHarnessVersion, group.HarnessVersion);
        Assert.Equal((DateTime?)groupFirst.StartedAtUtc, group.FirstRunUtc);
        Assert.Equal(groupSecond.CompletedAtUtc, group.LastRunUtc);
    }
}
