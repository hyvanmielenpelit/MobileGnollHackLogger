namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Whether a synthesis finding was raised by both panel members, by one of them alone, or by both
/// with opposite kinds (one member's strength is the other's weakness).
/// </summary>
public enum BenchmarkConvergenceStatus
{
    Convergent,
    MemberAOnly,
    MemberBOnly,
    Conflicting
}

/// <summary>
/// One row of the computed agreement between the two members' syntheses: a finding kind, its
/// category and the questions it names (empty for a run-wide finding), with each member's text where
/// that member raised it. A <see cref="BenchmarkConvergenceStatus.Conflicting"/> row carries member
/// A's kind; member B raised the opposite one.
/// </summary>
public sealed record BenchmarkSynthesisConvergenceRow(
    string Kind,
    string Category,
    IReadOnlyList<int> Questions,
    BenchmarkConvergenceStatus Status,
    string? MemberAText,
    string? MemberBText);

/// <summary>
/// The agreement between member A's and member B's structured synthesis findings in a panel run.
/// Findings are matched on kind, category and overlapping questions, never on their prose, and each
/// finding appears in exactly one row. Nothing here calls a model or changes a score.
/// </summary>
public static class BenchmarkSynthesisConvergence
{
    /// <summary>The fixed category order of the synthesis schema; unknown categories follow alphabetically.</summary>
    private static readonly string[] CategoryOrder =
    {
        "accuracy", "completeness", "conciseness", "readability", "critical_error", "tool_use", "other"
    };

    /// <summary>Separates the texts of two findings joined in one row.</summary>
    private const string TextSeparator = " / ";

    /// <summary>One member's finding, normalized; findings with one key are merged into one.</summary>
    private sealed record Item(string Kind, string Category, IReadOnlyList<int> Questions, List<string> Texts);

    /// <summary>
    /// One row per finding. An A finding matches a B finding when kind and category are equal and
    /// their question sets intersect, or both are run-wide; findings linked through matches form one
    /// <see cref="BenchmarkConvergenceStatus.Convergent"/> row carrying the union of their questions.
    /// A finding without a match is <see cref="BenchmarkConvergenceStatus.Conflicting"/> when the
    /// other member raised the opposite kind (strength against weakness) in the same category on an
    /// intersecting question set and that finding has no match either; otherwise it is its member's
    /// alone. A finding in the <c>other</c> category never matches or conflicts, so it is always its
    /// member's alone. Rows are ordered by first question (run-wide first), then kind (weaknesses, then
    /// strengths, then any other kind alphabetically), then category in schema order. Kind and
    /// category are compared trimmed and case-insensitively and reported in lower case.
    /// </summary>
    public static IReadOnlyList<BenchmarkSynthesisConvergenceRow> Compute(
        IReadOnlyList<BenchmarkSynthesisFinding> a,
        IReadOnlyList<BenchmarkSynthesisFinding> b)
    {
        var itemsA = ItemsOf(a);
        var itemsB = ItemsOf(b);

        var rows = new List<BenchmarkSynthesisConvergenceRow>();

        // Same-kind matches, grouped into connected components of A and B findings.
        var matchedA = new bool[itemsA.Count];
        var matchedB = new bool[itemsB.Count];
        foreach (var (groupA, groupB) in Components(itemsA, itemsB, SameKindMatch))
        {
            foreach (int i in groupA) matchedA[i] = true;
            foreach (int j in groupB) matchedB[j] = true;
            rows.Add(RowOf(groupA.Select(i => itemsA[i]), groupB.Select(j => itemsB[j]), BenchmarkConvergenceStatus.Convergent));
        }

        // Opposite-kind pairs among the findings that matched nothing.
        var restA = Enumerable.Range(0, itemsA.Count).Where(i => !matchedA[i]).ToList();
        var restB = Enumerable.Range(0, itemsB.Count).Where(j => !matchedB[j]).ToList();
        var unmatchedA = restA.Select(i => itemsA[i]).ToList();
        var unmatchedB = restB.Select(j => itemsB[j]).ToList();
        var conflictedA = new bool[unmatchedA.Count];
        var conflictedB = new bool[unmatchedB.Count];
        foreach (var (groupA, groupB) in Components(unmatchedA, unmatchedB, OppositeKindConflict))
        {
            foreach (int i in groupA) conflictedA[i] = true;
            foreach (int j in groupB) conflictedB[j] = true;
            rows.Add(RowOf(groupA.Select(i => unmatchedA[i]), groupB.Select(j => unmatchedB[j]), BenchmarkConvergenceStatus.Conflicting));
        }

        for (int i = 0; i < unmatchedA.Count; i++)
        {
            if (!conflictedA[i]) rows.Add(RowOf(new[] { unmatchedA[i] }, Array.Empty<Item>(), BenchmarkConvergenceStatus.MemberAOnly));
        }
        for (int j = 0; j < unmatchedB.Count; j++)
        {
            if (!conflictedB[j]) rows.Add(RowOf(Array.Empty<Item>(), new[] { unmatchedB[j] }, BenchmarkConvergenceStatus.MemberBOnly));
        }

        return rows
            .OrderBy(r => r.Questions.Count == 0 ? 0 : 1)
            .ThenBy(r => r.Questions.Count == 0 ? 0 : r.Questions[0])
            .ThenBy(r => KindRank(r.Kind))
            .ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => CategoryRank(r.Category))
            .ThenBy(r => r.Category, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The kind a <see cref="BenchmarkConvergenceStatus.Conflicting"/> row's member B raised.</summary>
    public static string OppositeKind(string kind) => Normalize(kind) switch
    {
        "strength" => "weakness",
        "weakness" => "strength",
        _ => kind
    };

    private static bool SameKindMatch(Item x, Item y) =>
        x.Kind == y.Kind && x.Category == y.Category && !IsOther(x.Category) && QuestionsOverlap(x, y);

    private static bool OppositeKindConflict(Item x, Item y) =>
        x.Category == y.Category && !IsOther(x.Category)
        && IsStrengthOrWeakness(x.Kind) && IsStrengthOrWeakness(y.Kind) && x.Kind != y.Kind
        && QuestionsOverlap(x, y);

    private static bool IsStrengthOrWeakness(string kind) => kind is "strength" or "weakness";

    /// <summary>The catch-all category, which says nothing two findings share; its findings never link.</summary>
    private static bool IsOther(string category) => category == "other";

    /// <summary>Both run-wide, or at least one question in common.</summary>
    private static bool QuestionsOverlap(Item x, Item y) =>
        (x.Questions.Count == 0 && y.Questions.Count == 0) || x.Questions.Intersect(y.Questions).Any();

    /// <summary>
    /// The connected components of the bipartite graph whose edges are the linked A–B pairs, each
    /// as the A and B indices it holds, in the order of each component's first A (then first B)
    /// finding. Findings with no edge form no component.
    /// </summary>
    private static List<(List<int> A, List<int> B)> Components(
        IReadOnlyList<Item> itemsA, IReadOnlyList<Item> itemsB, Func<Item, Item, bool> linked)
    {
        var edgesA = itemsA.Select(x => Enumerable.Range(0, itemsB.Count).Where(j => linked(x, itemsB[j])).ToList()).ToList();
        var edgesB = itemsB.Select(y => Enumerable.Range(0, itemsA.Count).Where(i => linked(itemsA[i], y)).ToList()).ToList();
        var seenA = new bool[itemsA.Count];
        var seenB = new bool[itemsB.Count];
        var components = new List<(List<int>, List<int>)>();

        for (int start = 0; start < itemsA.Count; start++)
        {
            if (seenA[start] || edgesA[start].Count == 0) continue;

            var groupA = new List<int>();
            var groupB = new List<int>();
            var pendingA = new Stack<int>();
            pendingA.Push(start);
            seenA[start] = true;
            while (pendingA.Count > 0)
            {
                int i = pendingA.Pop();
                groupA.Add(i);
                foreach (int j in edgesA[i])
                {
                    if (seenB[j]) continue;
                    seenB[j] = true;
                    groupB.Add(j);
                    foreach (int k in edgesB[j])
                    {
                        if (seenA[k]) continue;
                        seenA[k] = true;
                        pendingA.Push(k);
                    }
                }
            }
            groupA.Sort();
            groupB.Sort();
            components.Add((groupA, groupB));
        }

        return components;
    }

    /// <summary>
    /// A row from the findings of each member it joins: kind and category from the first finding
    /// (member A's when present), the sorted union of their questions, and each member's distinct
    /// texts in the order given.
    /// </summary>
    private static BenchmarkSynthesisConvergenceRow RowOf(
        IEnumerable<Item> fromA, IEnumerable<Item> fromB, BenchmarkConvergenceStatus status)
    {
        var listA = fromA.ToList();
        var listB = fromB.ToList();
        var first = listA.Count > 0 ? listA[0] : listB[0];
        var questions = listA.Concat(listB).SelectMany(x => x.Questions).Distinct().OrderBy(q => q).ToList();
        return new BenchmarkSynthesisConvergenceRow(
            first.Kind, first.Category, questions, status, JoinTexts(listA), JoinTexts(listB));
    }

    /// <summary>The distinct texts of the findings, joined in order; null when there are none.</summary>
    private static string? JoinTexts(IReadOnlyList<Item> items)
    {
        if (items.Count == 0) return null;
        var texts = new List<string>();
        foreach (var text in items.SelectMany(x => x.Texts))
        {
            if (!texts.Contains(text, StringComparer.Ordinal)) texts.Add(text);
        }
        return string.Join(TextSeparator, texts);
    }

    /// <summary>
    /// One member's findings, normalized, with findings that share kind, category and question set
    /// merged into one whose distinct texts keep the order the member gave them.
    /// </summary>
    private static List<Item> ItemsOf(IReadOnlyList<BenchmarkSynthesisFinding>? findings)
    {
        var items = new List<Item>();

        foreach (var finding in findings ?? Array.Empty<BenchmarkSynthesisFinding>())
        {
            if (finding == null) continue;

            string kind = Normalize(finding.Kind);
            string category = Normalize(finding.Category);
            string text = finding.Text?.Trim() ?? string.Empty;
            var questions = (finding.Questions ?? Array.Empty<int>()).Distinct().OrderBy(q => q).ToList();

            var item = items.FirstOrDefault(x => x.Kind == kind && x.Category == category && x.Questions.SequenceEqual(questions));
            if (item == null)
            {
                item = new Item(kind, category, questions, new List<string>());
                items.Add(item);
            }
            if (text.Length > 0 && !item.Texts.Contains(text, StringComparer.Ordinal))
            {
                item.Texts.Add(text);
            }
        }

        return items;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static int KindRank(string kind) => kind switch
    {
        "weakness" => 0,
        "strength" => 1,
        _ => 2
    };

    private static int CategoryRank(string category)
    {
        int index = Array.IndexOf(CategoryOrder, category);
        return index >= 0 ? index : CategoryOrder.Length;
    }
}
