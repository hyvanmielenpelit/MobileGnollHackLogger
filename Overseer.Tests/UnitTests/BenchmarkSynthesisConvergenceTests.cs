namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The computed agreement between the two panel members' structured synthesis findings: matched on
/// kind, category and question, never on prose.
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
        Assert.Equal(3, row.Question);
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

        var onlyB = rows.Single(r => r.Question == 5);
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, onlyB.Status);
        Assert.Null(onlyB.MemberAText);
        Assert.Equal("Q5 omits the prerequisite.", onlyB.MemberBText);

        var onlyA = rows.Single(r => r.Question == 2);
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
        Assert.Null(row.Question);
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, row.Status);
    }

    [Fact]
    public void ARunWideFinding_DoesNotConvergeWithAQuestionSpecificOne()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "readability", "Dense formatting throughout.") },
            new[] { Finding("weakness", "readability", "Q4 is a wall of text.", 4) });

        Assert.Equal(2, rows.Count);
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, rows.Single(r => r.Question == null).Status);
        Assert.Equal(BenchmarkConvergenceStatus.MemberBOnly, rows.Single(r => r.Question == 4).Status);
    }

    [Fact]
    public void AFindingNamingSeveralQuestions_BecomesOneRowPerQuestion()
    {
        var rows = BenchmarkSynthesisConvergence.Compute(
            new[] { Finding("weakness", "critical_error", "Fabricated item properties.", 7, 2, 7) },
            new[] { Finding("weakness", "critical_error", "Invented a spell effect.", 2) });

        Assert.Equal(new int?[] { 2, 7 }, rows.Select(r => r.Question));
        Assert.Equal(BenchmarkConvergenceStatus.Convergent, rows[0].Status);
        Assert.Equal(BenchmarkConvergenceStatus.MemberAOnly, rows[1].Status);
        Assert.Equal("Fabricated item properties.", rows[1].MemberAText);
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
    public void Rows_AreOrderedByKindThenSchemaCategoryThenQuestion_WithTheRunWideRowFirst()
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
                ("weakness", "accuracy", (int?)null),
                ("weakness", "accuracy", (int?)3),
                ("weakness", "accuracy", (int?)9),
                ("weakness", "completeness", (int?)1),
                ("weakness", "tool_use", (int?)2),
                ("weakness", "other", (int?)null),
                ("strength", "accuracy", (int?)1),
                ("strength", "readability", (int?)null)
            },
            rows.Select(r => (r.Kind, r.Category, r.Question)));
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
}
