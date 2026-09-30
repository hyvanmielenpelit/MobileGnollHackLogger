namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The computed agreement between the two panel members' structured synthesis findings: matched on
/// kind, category and overlapping questions, never on prose, one row per finding.
/// </summary>
public class BenchmarkSynthesisConvergenceTests
{
    private static BenchmarkSynthesisFinding Finding(string kind, string category, string text, params int[] questions)
        => new(kind, category, questions, text);

    private static IReadOnlyList<BenchmarkSynthesisFinding> None() => Array.Empty<BenchmarkSynthesisFinding>();

    [Fact]
    public void AFindingBothMembersRaised_IsConvergent_AndCarriesBothTexts()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "accuracy", "Q3 misstates the damage roll.", 3) },
            new[] { Finding(" Weakness ", "ACCURACY", "The damage figure in Q3 is wrong.", 3) });

        var row = Assert.Single(rows);
        Assert.Equal("weakness", row.Kind);
        Assert.Equal("accuracy", row.Category);
        Assert.Equal(new[] { 3 }, row.Questions);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
        Assert.Equal("Q3 misstates the damage roll.", row.MemberAText);
        Assert.Equal("The damage figure in Q3 is wrong.", row.MemberBText);
    }

    [Fact]
    public void AFindingOneMemberRaised_IsThatMembersAlone_WithNoTextForTheOther()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "tool_use", "Searched the source before answering.", 2) },
            new[] { Finding("weakness", "completeness", "Q5 omits the prerequisite.", 5) });

        Assert.Equal(2, rows.Count);

        var onlyB = rows.Single(r => r.Questions.Contains(5));
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, onlyB.Status);
        Assert.Null(onlyB.MemberAText);
        Assert.Equal("Q5 omits the prerequisite.", onlyB.MemberBText);

        var onlyA = rows.Single(r => r.Questions.Contains(2));
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, onlyA.Status);
        Assert.Equal("Searched the source before answering.", onlyA.MemberAText);
        Assert.Null(onlyA.MemberBText);
    }

    [Fact]
    public void TheSameCategoryOnDifferentQuestions_DoesNotConverge()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "accuracy", "A on Q1.", 1) },
            new[] { Finding("weakness", "accuracy", "B on Q2.", 2) });

        Assert.Equal(
            new[] { BenchmarkConvergenceStatus.MemberAOnly, BenchmarkConvergenceStatus.MemberBOnly },
            rows.Select(r => r.Status));
    }

    [Fact]
    public void AFindingNamingNoQuestion_IsKeyedRunWide()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "conciseness", "Answers run long throughout.") },
            new[] { Finding("weakness", "conciseness", "Verbose across the run.") });

        var row = Assert.Single(rows);
        Assert.Empty(row.Questions);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
    }

    [Fact]
    public void ARunWideFinding_DoesNotConvergeWithAQuestionSpecificOne()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "readability", "Dense formatting throughout.") },
            new[] { Finding("weakness", "readability", "Q4 is a wall of text.", 4) });

        Assert.Equal(2, rows.Count);
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, rows.Single(r => r.Questions.Count == 0).Status);
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, rows.Single(r => r.Questions.Contains(4)).Status);
    }

    [Fact]
    public void AFindingNamingSeveralQuestions_IsOneRowCarryingAllItsQuestions()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "critical_error", "Fabricated item properties.", 7, 2, 7) },
            new[] { Finding("weakness", "critical_error", "Invented a spell effect.", 2) });

        var row = Assert.Single(rows);
        Assert.Equal(new[] { 2, 7 }, row.Questions);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
        Assert.Equal("Fabricated item properties.", row.MemberAText);
        Assert.Equal("Invented a spell effect.", row.MemberBText);
    }

    [Fact]
    public void FindingsLinkedThroughOverlappingQuestions_AreOneRow()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[]
            {
                Finding("weakness", "accuracy", "Wrong on Q1 and Q2.", 1, 2),
                Finding("weakness", "accuracy", "Wrong on Q3.", 3)
            },
            new[] { Finding("weakness", "accuracy", "Errors in Q2 and Q3.", 2, 3) });

        var row = Assert.Single(rows);
        Assert.Equal(new[] { 1, 2, 3 }, row.Questions);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
        Assert.Equal("Wrong on Q1 and Q2. / Wrong on Q3.", row.MemberAText);
        Assert.Equal("Errors in Q2 and Q3.", row.MemberBText);
    }

    [Fact]
    public void AStrengthAndAWeaknessOnTheSameQuestion_AreConflicting()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "accuracy", "Q3 gets the damage right.", 3) },
            new[] { Finding("weakness", "accuracy", "Q3 and Q4 misstate the damage.", 3, 4) });

        var row = Assert.Single(rows);
        Assert.Equal(BenchmarkConvergenceStatus.Conflicting, row.Status);
        Assert.Equal("strength", row.Kind);
        Assert.Equal("weakness", BenchmarkSynthesisConvergence.OppositeKind(row.Kind));
        Assert.Equal("accuracy", row.Category);
        Assert.Equal(new[] { 3, 4 }, row.Questions);
        Assert.Equal("Q3 gets the damage right.", row.MemberAText);
        Assert.Equal("Q3 and Q4 misstate the damage.", row.MemberBText);
    }

    [Fact]
    public void AnOppositeKindFinding_WhoseCounterpartHasASameKindMatch_IsNotConflicting()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "accuracy", "A praises Q3.", 3) },
            new[]
            {
                Finding("strength", "accuracy", "B praises Q3.", 3),
                Finding("weakness", "accuracy", "B faults Q3.", 3)
            });

        Assert.Equal(2, rows.Count);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, rows.Single(r => r.Kind == "strength").Status);
        var weakness = rows.Single(r => r.Kind == "weakness");
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, weakness.Status);
        Assert.Equal("B faults Q3.", weakness.MemberBText);
    }

    [Fact]
    public void OppositeKindsInDifferentCategories_AreNotConflicting()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "accuracy", "Accurate Q3.", 3) },
            new[] { Finding("weakness", "readability", "Dense Q3.", 3) });

        Assert.Equal(
            new[] { BenchmarkConvergenceStatus.MemberBOnly, BenchmarkConvergenceStatus.MemberAOnly },
            rows.Select(r => r.Status));
    }

    [Fact]
    public void TwoOtherFindings_OnePerMember_AreEachTheirMembersAlone()
    {
        // The catch-all category says nothing the two findings share.
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "other", "Consistent tone across answers.", 2) },
            new[] { Finding("strength", " Other ", "Good use of the board digest.", 2) });

        Assert.Equal(
            new[] { BenchmarkConvergenceStatus.MemberAOnly, BenchmarkConvergenceStatus.MemberBOnly },
            rows.Select(r => r.Status));
        Assert.Equal("Consistent tone across answers.", rows[0].MemberAText);
        Assert.Null(rows[0].MemberBText);
        Assert.Null(rows[1].MemberAText);
        Assert.Equal("Good use of the board digest.", rows[1].MemberBText);
    }

    [Fact]
    public void OppositeKindsInTheOtherCategory_AreNotConflicting()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("strength", "other", "A praises Q3.", 3) },
            new[] { Finding("weakness", "other", "B faults Q3.", 3) });

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Status == BenchmarkConvergenceStatus.Conflicting);
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, rows.Single(r => r.Kind == "strength").Status);
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, rows.Single(r => r.Kind == "weakness").Status);
    }

    [Fact]
    public void TwoFindingsOfOneMemberUnderOneKey_JoinTheirDistinctTexts()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[]
            {
                Finding("weakness", "accuracy", "Wrong weight.", 1),
                Finding("weakness", "accuracy", "Wrong price.", 1),
                Finding("weakness", "accuracy", "Wrong weight.", 1)
            },
            None());

        var row = Assert.Single(rows);
        Assert.Equal("Wrong weight. / Wrong price.", row.MemberAText);
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, row.Status);
    }

    [Fact]
    public void Rows_AreOrderedByFirstQuestionThenKindThenSchemaCategory_WithRunWideRowsFirst()
    {
        var a = new[]
        {
            Finding("strength", "accuracy", "S-acc-1", 1),
            Finding("weakness", "other", "W-other"),
            Finding("weakness", "accuracy", "W-acc-9", 9),
            Finding("weakness", "tool_use", "W-tool-2", 2),
            Finding("weakness", "accuracy", "W-acc-run-wide"),
        };
        var b = new[]
        {
            Finding("weakness", "accuracy", "W-acc-3", 3),
            Finding("weakness", "completeness", "W-comp-1", 1),
            Finding("strength", "readability", "S-read"),
        };

        var rows = BenchmarkSynthesisConvergence.Compute(a, b);

        Assert.Equal(
            new[]
            {
                ("weakness", "accuracy", ""),
                ("weakness", "other", ""),
                ("strength", "readability", ""),
                ("weakness", "completeness", "1"),
                ("strength", "accuracy", "1"),
                ("weakness", "tool_use", "2"),
                ("weakness", "accuracy", "3"),
                ("weakness", "accuracy", "9")
            },
            rows.Select(r => (r.Kind, r.Category, string.Join(",", r.Questions))));
    }

    [Fact]
    public void Categories_FollowTheSchemaOrder_NotTheAlphabet()
    {
        var a = new[]
        {
            Finding("weakness", "other", "o", 1),
            Finding("weakness", "tool_use", "t", 1),
            Finding("weakness", "critical_error", "ce", 1),
            Finding("weakness", "readability", "r", 1),
            Finding("weakness", "conciseness", "cn", 1),
            Finding("weakness", "completeness", "cp", 1),
            Finding("weakness", "accuracy", "a", 1)
        };

        var rows = BenchmarkSynthesisConvergence.Compute(a, None());

        Assert.Equal(
            new[] { "accuracy", "completeness", "conciseness", "readability", "critical_error", "tool_use", "other" },
            rows.Select(r => r.Category));
    }

    [Fact]
    public void NoFindingsOnEitherSide_GivesNoRows()
    {
        Assert.Empty(BenchmarkSynthesisConvergence.Compute(None(), None()));
    }

    [Fact]
    public void AConvergentRow_CarriesEachMembersQuestions_AndTheQuestionsBothNamed_Sorted()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "accuracy", "A.", 15, 3, 13, 1, 11) },
            new[] { Finding("weakness", "accuracy", "B.", 18, 13, 9, 16, 15) });

        var row = Assert.Single(rows);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
        Assert.Equal(new[] { 1, 3, 9, 11, 13, 15, 16, 18 }, row.Questions);
        Assert.Equal(new[] { 1, 3, 11, 13, 15 }, row.QuestionsA);
        Assert.Equal(new[] { 9, 13, 15, 16, 18 }, row.QuestionsB);
        Assert.Equal(new[] { 13, 15 }, row.SharedQuestions);
    }

    [Fact]
    public void AComponentOfSeveralFindings_UnitesEachMembersQuestionsBeforeIntersecting()
    {
        // A's two findings each meet B's one on a different question.
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "accuracy", "A1.", 2, 4), Finding("weakness", "accuracy", "A2.", 6) },
            new[] { Finding("weakness", "accuracy", "B.", 4, 6, 8) });

        var row = Assert.Single(rows);
        Assert.Equal(new[] { 2, 4, 6 }, row.QuestionsA);
        Assert.Equal(new[] { 4, 6, 8 }, row.QuestionsB);
        Assert.Equal(new[] { 4, 6 }, row.SharedQuestions);
    }

    [Fact]
    public void OneMemberAndRunWideRows_HaveNoSharedQuestions_AndTheStatusesAreUnchanged()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[]
            {
                Finding("strength", "tool_use", "A run-wide."),
                Finding("weakness", "completeness", "A alone.", 5),
                Finding("strength", "accuracy", "A strength.", 7, 8)
            },
            new[]
            {
                Finding("strength", "tool_use", "B run-wide."),
                Finding("weakness", "accuracy", "B weakness.", 8, 9)
            });

        var runWide = rows.Single(r => r.Questions.Count == 0);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, runWide.Status);
        Assert.Empty(runWide.QuestionsA);
        Assert.Empty(runWide.QuestionsB);
        Assert.Empty(runWide.SharedQuestions);

        var alone = rows.Single(r => r.Questions.Contains(5));
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, alone.Status);
        Assert.Equal(new[] { 5 }, alone.QuestionsA);
        Assert.Empty(alone.QuestionsB);
        Assert.Empty(alone.SharedQuestions);

        var conflicting = rows.Single(r => r.Questions.Contains(7));
        Assert.Equal(BenchmarkConvergenceStatus.Conflicting, conflicting.Status);
        Assert.Equal(new[] { 7, 8 }, conflicting.QuestionsA);
        Assert.Equal(new[] { 8, 9 }, conflicting.QuestionsB);
        Assert.Equal(new[] { 8 }, conflicting.SharedQuestions);
    }
}
