namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Whether a synthesis finding was raised by both panel members or by one of them alone.</summary>
public enum BenchmarkConvergenceStatus
{
    Convergent,
    MemberAOnly,
    MemberBOnly
}

/// <summary>
/// One key of the computed agreement between the two members' syntheses: a finding kind, its
/// category and the question it names (null for a run-wide finding), with each member's text where
/// that member raised it.
/// </summary>
public sealed record BenchmarkSynthesisConvergenceRow(
    string Kind,
    string Category,
    int? Question,
    BenchmarkConvergenceStatus Status,
    string? MemberAText,
    string? MemberBText);

/// <summary>
/// The agreement between member A's and member B's structured synthesis findings in a panel run.
/// Findings are matched on <c>(Kind, Category, Question)</c>, never on their prose: a finding naming
/// several questions yields one key per question, and one naming none is keyed run-wide. Nothing
/// here calls a model or changes a score.
/// </summary>
public static class BenchmarkSynthesisConvergence
{
    /// <summary>The fixed category order of the synthesis schema; unknown categories follow alphabetically.</summary>
    private static readonly string[] CategoryOrder =
    {
        "accuracy", "completeness", "conciseness", "readability", "critical_error", "tool_use", "other"
    };

    /// <summary>Separates the texts of two findings a member raised under one key.</summary>
    private const string TextSeparator = " / ";

    /// <summary>
    /// One row per key either member raised: <see cref="BenchmarkConvergenceStatus.Convergent"/>
    /// when both did, otherwise the member that did. Rows are ordered by kind (weaknesses, then
    /// strengths, then any other kind alphabetically), then category in schema order, then question
    /// with the run-wide row first. Kind and category are compared trimmed and case-insensitively and
    /// reported in lower case.
    /// </summary>
    public static IReadOnlyList<BenchmarkSynthesisConvergenceRow> Compute(
        IReadOnlyList<BenchmarkSynthesisFinding> a,
        IReadOnlyList<BenchmarkSynthesisFinding> b)
    {
        var textsA = TextsByKey(a);
        var textsB = TextsByKey(b);

        return textsA.Keys
            .Union(textsB.Keys)
            .OrderBy(k => KindRank(k.Kind))
            .ThenBy(k => k.Kind, StringComparer.Ordinal)
            .ThenBy(k => CategoryRank(k.Category))
            .ThenBy(k => k.Category, StringComparer.Ordinal)
            .ThenBy(k => k.Question.HasValue ? 1 : 0)
            .ThenBy(k => k.Question ?? 0)
            .Select(k =>
            {
                textsA.TryGetValue(k, out string? textA);
                textsB.TryGetValue(k, out string? textB);
                var status = textA != null && textB != null
                    ? BenchmarkConvergenceStatus.Convergent
                    : textA != null ? BenchmarkConvergenceStatus.MemberAOnly : BenchmarkConvergenceStatus.MemberBOnly;
                return new BenchmarkSynthesisConvergenceRow(k.Kind, k.Category, k.Question, status, textA, textB);
            })
            .ToList();
    }

    /// <summary>
    /// Each key one member raised, with the distinct texts of its findings under that key joined in
    /// the order the member gave them.
    /// </summary>
    private static Dictionary<(string Kind, string Category, int? Question), string> TextsByKey(
        IReadOnlyList<BenchmarkSynthesisFinding>? findings)
    {
        var texts = new Dictionary<(string Kind, string Category, int? Question), List<string>>();

        foreach (var finding in findings ?? Array.Empty<BenchmarkSynthesisFinding>())
        {
            if (finding == null) continue;

            string kind = Normalize(finding.Kind);
            string category = Normalize(finding.Category);
            string text = finding.Text?.Trim() ?? string.Empty;

            var questions = (finding.Questions ?? Array.Empty<int>()).Distinct().ToList();
            var keys = questions.Count == 0
                ? new List<(string, string, int?)> { (kind, category, null) }
                : questions.Select(q => (kind, category, (int?)q)).ToList();

            foreach (var key in keys)
            {
                if (!texts.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    texts[key] = list;
                }
                if (text.Length > 0 && !list.Contains(text, StringComparer.Ordinal))
                {
                    list.Add(text);
                }
            }
        }

        return texts.ToDictionary(p => p.Key, p => string.Join(TextSeparator, p.Value));
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
