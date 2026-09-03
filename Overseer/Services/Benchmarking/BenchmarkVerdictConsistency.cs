namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Detects the case where an assessor's own prose describes a fabrication while its
/// <c>criticalError</c> flag says otherwise.
///
/// On the 2026-09-03 GPT-5.6 Luna run, Q10's verdict read "hallucinates 'adamantium',
/// mischaracterizes gemstone armor, and omits bronze" and returned <c>criticalError: false</c>,
/// so no cap applied and no Critical Errors headline was printed — and the run-level synthesis
/// then made that same hallucination the headline finding of the whole run. Q1 was the same shape
/// from the other side: its rubric named an explicit critical-error condition ("Invents racial
/// intrinsics ... that are not in the list above"), the answer invented two, and the flag still
/// came back false.
///
/// Whether either should have been capped is a judgement call, and forcing the cap mechanically
/// would be worse than leaving it: a false 25-point cap costs far more than a missed advisory.
/// So nothing here changes a score. It records the divergence, which becomes a second-opinion
/// trigger (<see cref="BenchmarkService"/>) and an advisory line in the report.
/// </summary>
public static class BenchmarkVerdictConsistency
{
    /// <summary>
    /// Vocabulary that marks a claim as invented rather than merely wrong.
    ///
    /// "invent" carries a negative lookahead for "or" because this is a benchmark about a
    /// roguelike: "inventory" appears in a large share of answers and assessor comments, and
    /// matching it would flag most of the suite. "inventor" is excluded by the same guard, which
    /// costs nothing here.
    /// </summary>
    private static readonly Regex FabricationRegex = new(
        @"hallucinat|fabricat|\binvent(?!or)|non-?existent|does not exist|no such|made up",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Question references inside a synthesis sentence: "Question 10", "Q 10", "Q10".</summary>
    private static readonly Regex QuestionReferenceRegex = new(
        @"\b(?:question|q)\s*#?\s*(\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the text describes a fabrication. Applied to an assessor's comment and to each
    /// of its evidence strings; a hit alongside <c>criticalError == false</c> is a contested
    /// verdict.
    /// </summary>
    public static bool MentionsFabrication(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) && FabricationRegex.IsMatch(text);
    }

    /// <summary>
    /// The order indexes named in <paramref name="synthesisText"/> by a sentence that also
    /// describes a fabrication, restricted to <paramref name="orderIndexes"/>.
    ///
    /// Deliberately sentence-scoped rather than paragraph-scoped: the synthesis names several
    /// questions per paragraph, and a paragraph-wide match would attribute one question's
    /// hallucination to every question mentioned near it. A missed match costs an advisory line;
    /// a false match accuses a clean verdict, so the narrower scope is the right error to make.
    /// </summary>
    public static IReadOnlyList<int> QuestionsNamedWithFabrication(
        string? synthesisText,
        IEnumerable<int> orderIndexes)
    {
        if (string.IsNullOrWhiteSpace(synthesisText) || orderIndexes == null)
        {
            return Array.Empty<int>();
        }

        var known = new HashSet<int>(orderIndexes);
        if (known.Count == 0)
        {
            return Array.Empty<int>();
        }

        var found = new HashSet<int>();
        foreach (string sentence in SplitSentences(synthesisText))
        {
            if (!FabricationRegex.IsMatch(sentence)) continue;

            foreach (Match m in QuestionReferenceRegex.Matches(sentence))
            {
                if (int.TryParse(m.Groups[1].Value, out int index) && known.Contains(index))
                {
                    found.Add(index);
                }
            }
        }

        return found.OrderBy(i => i).ToList();
    }

    /// <summary>
    /// Splits on sentence terminators and on line breaks. Line breaks count because the synthesis
    /// is multi-paragraph prose and a heading or list item is a boundary even without a full stop.
    /// </summary>
    private static IEnumerable<string> SplitSentences(string text)
    {
        return text
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => Regex.Split(line, @"(?<=[.!?])\s+"))
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }
}
