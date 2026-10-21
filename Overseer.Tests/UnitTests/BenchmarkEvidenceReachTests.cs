using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Pins how an answer's stored tool-call rows are matched against its question's gold evidence
/// and read with its outcome class. The synthetic rows are modeled on the cases of GPT-6 Luna
/// run 125: a personal race page never opened (Q9), a role page never surfaced (Q11), a source
/// range viewed beside but not at the gold lines (Q10) and a single-hit artifact lookup (Q2).
/// </summary>
public class BenchmarkEvidenceReachTests
{
    private static BenchmarkRunAnswerToolCall Row(int sortOrder, string name, string? result, string? args = null)
        => new()
        {
            SortOrder = sortOrder,
            Name = name,
            ArgsText = args,
            Result = result,
            ResultLengthChars = result?.Length ?? 0
        };

    private static BenchmarkEvidenceUnit Wiki(string path, bool personal = false)
        => new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = path, Personal = personal };

    private static BenchmarkEvidenceUnit Source(string path, string? lines = null, string? function = null)
        => new() { Kind = BenchmarkEvidenceUnit.SourceKind, Path = path, Lines = lines, Function = function };

    private static BenchmarkEvidenceUnit Kb(string topic)
        => new() { Kind = BenchmarkEvidenceUnit.KnowledgeBaseKind, Path = topic };

    private static string Headers(params string[] labels)
        => string.Join("\n", labels.Select(l => $"--- {l} ---\nbody of {l}\n"));

    [Fact]
    public void Q9_RacePagesSurfacedByASearchButNeverOpened_IsANavigationFailure()
    {
        var units = new List<BenchmarkEvidenceUnit>
        {
            Wiki("Races/Human.md", personal: true),
            Wiki("Races/Orc.md", personal: true),
            Wiki("Races/Gnoll.md", personal: true)
        };
        var rows = new[]
        {
            Row(0, "wiki_search", Headers("Races/Orc.md", "Races/Gnoll.md")),
            Row(1, "wiki_search", Headers("Monsters/Orc.md"))
        };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.NavigationFailure, reach.Class);
        Assert.True(reach.GoldSurfaced);
        Assert.False(reach.GoldOpened);
        Assert.Equal(new[] { "Races/Orc.md", "Races/Gnoll.md" }, reach.SurfacedUnits);
        Assert.Empty(reach.OpenedUnits);
        Assert.False(reach.PersonalContextOpened);
        Assert.Contains("Races/Human.md", reach.PersonalUnitsNotOpened);
        Assert.False(reach.NoToolCall);
        Assert.False(reach.Unavailable);
        Assert.False(reach.RecordCut);

        string line = BenchmarkEvidenceReach.Describe(reach);
        Assert.StartsWith("Evidence: surfaced yes (Races/Orc.md, Races/Gnoll.md), opened no", line);
        Assert.Contains("personal page Races/Human.md not opened", line);
        Assert.EndsWith("; Retrieval of gold: navigation failure", line);
    }

    [Fact]
    public void Q11_ARolePageThatNeverAppearsInAnyResult_IsARetrievalFailure()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Roles/Healer.md", personal: true) };
        var rows = new[]
        {
            Row(0, "wiki_search", Headers("Items/Stethoscope.md", "Spells/Healing.md")),
            Row(1, "wiki_view", Headers("Races/Gnoll.md"))
        };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Partial);

        Assert.Equal(BenchmarkEvidenceReachClass.RetrievalFailure, reach.Class);
        Assert.False(reach.GoldSurfaced);
        Assert.False(reach.GoldOpened);
        Assert.False(reach.PersonalContextOpened);
        Assert.Equal(new[] { "Roles/Healer.md" }, reach.PersonalUnitsNotOpened);
        Assert.Equal(
            "Evidence: surfaced no, opened no; personal page Roles/Healer.md not opened; Retrieval of gold: retrieval failure",
            BenchmarkEvidenceReach.Describe(reach));
    }

    [Fact]
    public void Q10_SourceViewsOfTheGoldFileOutsideTheGoldLines_AreANavigationFailure()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: "4194-4231") };
        var rows = new[]
        {
            Row(0, "source_code_view", Headers("src/artifact.c:L2740-L2959")),
            Row(1, "source_code_view", Headers("src/artifact.c:L845-L954"))
        };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.NavigationFailure, reach.Class);
        Assert.True(reach.GoldSurfaced);
        Assert.False(reach.GoldOpened);
        Assert.Null(reach.PersonalContextOpened);
        Assert.Empty(reach.PersonalUnitsNotOpened);
    }

    [Fact]
    public void Q10_ASourceViewOverlappingTheGoldLines_IsReadButWrong()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: "4194-4231") };
        var rows = new[]
        {
            Row(0, "source_code_view", Headers("src/artifact.c:L2740-L2959")),
            Row(1, "source_code_view", Headers("src/artifact.c:L4200-L4210"))
        };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.ReadButWrong, reach.Class);
        Assert.True(reach.GoldOpened);
        Assert.Equal(new[] { "src/artifact.c" }, reach.OpenedUnits);
    }

    [Theory]
    [InlineData("src/artifact.c:L4150-L4194", true)]
    [InlineData("src/artifact.c:L4231-L4300", true)]
    [InlineData("src/artifact.c:L4100-L4193", false)]
    [InlineData("src/artifact.c:L4232-L4300", false)]
    public void ASourceView_OpensTheUnitOnlyWhenItsRangeOverlapsTheGoldLines(string label, bool opened)
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: "4194-4231") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_view", Headers(label)) }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.True(reach.GoldSurfaced);
        Assert.Equal(opened, reach.GoldOpened);
    }

    [Fact]
    public void ASourceSearchHitInsideTheGoldLines_SurfacesButNeverOpens()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: "4194-4231") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_search", Headers("src/artifact.c:L4200", "src/invent.c:L12")) },
            units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.NavigationFailure, reach.Class);
        Assert.True(reach.GoldSurfaced);
        Assert.False(reach.GoldOpened);
    }

    [Fact]
    public void ASourceUnitWithoutLinesOrFunction_IsOpenedByAnyViewOfTheFile()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_view", Headers("src/artifact.c:L1-L200")) }, units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, reach.Class);
    }

    [Fact]
    public void GetFunctionDefinition_OpensAFunctionUnitWhenTheNameMatches()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", function: "arti_invoke") };

        var matched = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "get_function_definition", Headers("src/artifact.c:L4194-L4231 (arti_invoke, 38 lines)")) },
            units, BenchmarkOutcomeClass.Correct);
        var other = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "get_function_definition", Headers("src/artifact.c:L1200-L1260 (spec_applies, 61 lines)")) },
            units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, matched.Class);
        Assert.True(matched.GoldOpened);
        Assert.Equal(BenchmarkEvidenceReachClass.UngroundedCorrect, other.Class);
        Assert.True(other.GoldSurfaced);
        Assert.False(other.GoldOpened);
    }

    [Fact]
    public void AFunctionNameIsNotMatchedFromASourceView()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", function: "arti_invoke") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_view", Headers("src/artifact.c:L4194-L4231")) }, units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.UngroundedCorrect, reach.Class);
    }

    [Fact]
    public void Q2_AnItemLookupWithExactlyOneHit_IsGroundedCorrect()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Artifacts/The Orb of Fate.md") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "item_lookup", Headers("Artifacts/The Orb of Fate.md"), "{\"name\":\"orb of fate\"}") },
            units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, reach.Class);
        Assert.True(reach.GoldSurfaced);
        Assert.True(reach.GoldOpened);
        Assert.Null(reach.PersonalContextOpened);
        Assert.Equal(
            "Evidence: surfaced yes (Artifacts/The Orb of Fate.md), opened yes (Artifacts/The Orb of Fate.md); Retrieval of gold: grounded correct",
            BenchmarkEvidenceReach.Describe(reach));
    }

    [Fact]
    public void Q2_AnItemLookupWithTwoHits_OnlySurfacesTheGold_AndIsUngroundedCorrect()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Artifacts/The Orb of Fate.md") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "item_lookup", Headers("Artifacts/The Orb of Fate.md", "Items/Crystal ball.md")) },
            units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.UngroundedCorrect, reach.Class);
        Assert.True(reach.GoldSurfaced);
        Assert.False(reach.GoldOpened);
    }

    [Fact]
    public void AWikiView_OpensOnlyTheFirstHeader()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md", personal: true) };

        var first = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "wiki_view", Headers("Races/Human.md", "Races/Elf.md")) }, units, BenchmarkOutcomeClass.Incorrect);
        var second = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "wiki_view", Headers("Races/Elf.md", "Races/Human.md")) }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.ReadButWrong, first.Class);
        Assert.True(first.PersonalContextOpened);
        Assert.Empty(first.PersonalUnitsNotOpened);
        Assert.Contains("personal page opened", BenchmarkEvidenceReach.Describe(first));
        Assert.Equal(BenchmarkEvidenceReachClass.NavigationFailure, second.Class);
        Assert.False(second.PersonalContextOpened);
    }

    [Fact]
    public void WikiMatching_IsCaseInsensitive_ReadsBackslashesAsSlashes_AndIgnoresTheMdExtension()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "wiki_view", Headers("races\\HUMAN")) }, units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, reach.Class);
    }

    [Fact]
    public void SourceMatching_IsCaseInsensitive_AndReadsBackslashesAsSlashes()
    {
        var units = new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: "4194-4231") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_view", Headers("SRC\\Artifact.c:L4200-L4210")) }, units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, reach.Class);
    }

    [Fact]
    public void AWikiHeaderFromANonWikiTool_IsNotMatched()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "source_code_search", Headers("Races/Human.md")) }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.Equal(BenchmarkEvidenceReachClass.RetrievalFailure, reach.Class);
    }

    [Fact]
    public void AKnowledgeBaseUnit_IsMatchedByTheTopicArgument_CaseInsensitively()
    {
        var units = new List<BenchmarkEvidenceUnit> { Kb("Praying") };

        var matched = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "get_knowledge_article", "Article body", "{\"topic\":\" praying \"}") },
            units, BenchmarkOutcomeClass.Correct);
        var other = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "get_knowledge_article", "Article body", "{\"topic\":\"Altars\"}") },
            units, BenchmarkOutcomeClass.Correct);
        var unreadable = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "get_knowledge_article", "Article body", "topic=Praying") },
            units, BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, matched.Class);
        Assert.True(matched.GoldSurfaced);
        Assert.True(matched.GoldOpened);
        Assert.Equal(BenchmarkEvidenceReachClass.UngroundedCorrect, other.Class);
        Assert.False(other.GoldSurfaced);
        Assert.Equal(BenchmarkEvidenceReachClass.UngroundedCorrect, unreadable.Class);
    }

    [Fact]
    public void APrunedRelevantRow_WithTheGoldNotOpened_IsUnavailableAndNotApplicable()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var pruned = new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "wiki_view", Result = null, ResultLengthChars = 4200 };

        var reach = BenchmarkEvidenceReach.Compute(new[] { pruned }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.True(BenchmarkEvidenceReach.IsPruned(pruned));
        Assert.True(reach.Unavailable);
        Assert.Equal(BenchmarkEvidenceReachClass.NotApplicable, reach.Class);
        Assert.Equal("Evidence: not available (the tool-call rows were pruned)", BenchmarkEvidenceReach.Describe(reach));
    }

    [Fact]
    public void APrunedRow_DoesNotHideGoldThatAnotherRowOpened()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var rows = new[]
        {
            new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "wiki_search", Result = null, ResultLengthChars = 900 },
            Row(1, "wiki_view", Headers("Races/Human.md"))
        };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Correct);

        Assert.False(reach.Unavailable);
        Assert.Equal(BenchmarkEvidenceReachClass.GroundedCorrect, reach.Class);
    }

    [Fact]
    public void ARowWithNoResultAndNoLength_IsNotPruned()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var empty = new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "wiki_search", Result = null, ResultLengthChars = 0 };

        var reach = BenchmarkEvidenceReach.Compute(new[] { empty }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.False(BenchmarkEvidenceReach.IsPruned(empty));
        Assert.False(reach.Unavailable);
        Assert.Equal(BenchmarkEvidenceReachClass.RetrievalFailure, reach.Class);
    }

    [Fact]
    public void ATruncatedRelevantRowWithoutTheGold_IsARecordCut()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var truncated = Row(0, "wiki_search", Headers("Races/Elf.md", "Races/Dwarf.md"));
        truncated.ResultTruncated = true;
        truncated.ResultLengthChars = 40000;

        var reach = BenchmarkEvidenceReach.Compute(new[] { truncated }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.True(reach.RecordCut);
        Assert.False(reach.Unavailable);
        Assert.Equal(BenchmarkEvidenceReachClass.RetrievalFailure, reach.Class);
        Assert.Equal(
            "Evidence: surfaced no, opened no, not seen in the stored result (record cut); Retrieval of gold: retrieval failure",
            BenchmarkEvidenceReach.Describe(reach));
    }

    [Fact]
    public void ATruncatedRow_IsNoRecordCutOnceTheGoldIsOpened()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var truncated = Row(0, "wiki_search", Headers("Races/Elf.md"));
        truncated.ResultTruncated = true;
        var rows = new[] { truncated, Row(1, "wiki_view", Headers("Races/Human.md")) };

        var reach = BenchmarkEvidenceReach.Compute(rows, units, BenchmarkOutcomeClass.Incorrect);

        Assert.False(reach.RecordCut);
        Assert.Equal(BenchmarkEvidenceReachClass.ReadButWrong, reach.Class);
    }

    [Fact]
    public void ATruncatedIrrelevantRow_IsNoRecordCut()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };
        var truncated = Row(0, "source_code_search", Headers("src/u_init.c:L10"));
        truncated.ResultTruncated = true;

        var reach = BenchmarkEvidenceReach.Compute(new[] { truncated }, units, BenchmarkOutcomeClass.Incorrect);

        Assert.False(reach.RecordCut);
    }

    [Fact]
    public void NoUnits_IsNotApplicable()
    {
        var withRows = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "wiki_view", Headers("Races/Human.md")) }, null, BenchmarkOutcomeClass.Correct);
        var withoutRows = BenchmarkEvidenceReach.Compute(
            new BenchmarkRunAnswerToolCall[0], new List<BenchmarkEvidenceUnit>(), BenchmarkOutcomeClass.Correct);

        Assert.Equal(BenchmarkEvidenceReachClass.NotApplicable, withRows.Class);
        Assert.False(withRows.NoToolCall);
        Assert.Equal(BenchmarkEvidenceReachClass.NotApplicable, withoutRows.Class);
        Assert.True(withoutRows.NoToolCall);
    }

    [Theory]
    [InlineData(BenchmarkOutcomeClass.NoAnswer)]
    [InlineData(null)]
    public void NoAnswerOrNoOutcome_IsNotApplicable(BenchmarkOutcomeClass? outcome)
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };

        var reach = BenchmarkEvidenceReach.Compute(
            new[] { Row(0, "wiki_view", Headers("Races/Human.md")) }, units, outcome);

        Assert.Equal(BenchmarkEvidenceReachClass.NotApplicable, reach.Class);
        Assert.True(reach.GoldOpened);
    }

    [Fact]
    public void NotAttempted_IsReadAsNotCorrect()
    {
        var units = new List<BenchmarkEvidenceUnit> { Wiki("Races/Human.md") };

        var reach = BenchmarkEvidenceReach.Compute(new BenchmarkRunAnswerToolCall[0], units, BenchmarkOutcomeClass.NotAttempted);

        Assert.Equal(BenchmarkEvidenceReachClass.RetrievalFailure, reach.Class);
        Assert.True(reach.NoToolCall);
        Assert.Equal(
            "Evidence: surfaced no, opened no; no tool call; Retrieval of gold: retrieval failure",
            BenchmarkEvidenceReach.Describe(reach));
    }

    [Theory]
    [InlineData(BenchmarkEvidenceReachClass.GroundedCorrect, "grounded correct")]
    [InlineData(BenchmarkEvidenceReachClass.UngroundedCorrect, "ungrounded correct")]
    [InlineData(BenchmarkEvidenceReachClass.RetrievalFailure, "retrieval failure")]
    [InlineData(BenchmarkEvidenceReachClass.NavigationFailure, "navigation failure")]
    [InlineData(BenchmarkEvidenceReachClass.ReadButWrong, "read but wrong")]
    [InlineData(BenchmarkEvidenceReachClass.NotApplicable, "not applicable")]
    public void ClassLabel_NamesEveryClass(BenchmarkEvidenceReachClass reachClass, string label)
    {
        Assert.Equal(label, BenchmarkEvidenceReach.ClassLabel(reachClass));
    }
}
