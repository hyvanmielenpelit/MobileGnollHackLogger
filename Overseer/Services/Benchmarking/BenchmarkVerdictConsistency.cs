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

    /// <summary>
    /// The highest level at which a no-fault evidence string is still treated as a contradiction.
    ///
    /// Scoring method v7 tells the assessor that such a string may accompany level 6 only, so this
    /// constant is deliberately one level *below* the rule it enforces. Level 5 is "highly accurate
    /// and precise; nuanced understanding", and a grader awarding 5 with "Matches rubric" is giving a
    /// defensible reading of its own anchor; flagging that would fire on most answers of a strong run
    /// and devalue the flag. Below 5 the contradiction is material — level 4 costs 28 points of the
    /// 55%-weight dimension against level 6.
    /// </summary>
    public const int UnevidencedDeductionMaxLevel = 4;

    /// <summary>
    /// Evidence strings that assert no defect. Anchored at both ends after trimming trailing
    /// punctuation: "Matches rubric" is a no-fault string, "Matches rubric points 1-3 but omits point
    /// 4" is not, and a substring match would classify the second as the first.
    /// </summary>
    private static readonly Regex NoFaultEvidenceRegex = new(
        @"^\s*(?:fully\s+)?(?:matches|meets|satisfies|consistent\s+with|aligns\s+with)?\s*(?:the\s+)?rubric\s*$|^\s*(?:no|none)\b[\s\w]*(?:error|issue|omission|inaccurac|deduction)\w*\s*$|^\s*(?:n\/?a|none|correct|accurate|fully\s+accurate|complete)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when the evidence string names no defect at all — including when it is absent.</summary>
    public static bool IsNoFaultEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return true;
        return NoFaultEvidenceRegex.IsMatch(evidence.Trim().TrimEnd('.', ';', ':'));
    }

    /// <summary>
    /// Vocabulary citing unverifiability as a reason for an assessment finding.
    /// </summary>
    private static readonly Regex UnverifiabilityRegex = new(
        @"could not (?:be )?verif|cannot (?:be )?verif|unable to verif|unverifi|could not confirm|not confirmed|no confirmation|not verifiable",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Vocabulary naming an actual candidate defect. Used to guard <see cref="IsUnverifiabilityGroundedDeduction"/>:
    /// evidence naming a real defect alongside an unverifiable claim is a legitimate deduction and must not flag.
    /// </summary>
    private static readonly Regex DefectRegex = new(
        @"omit|missing|wrong|incorrect|inaccurat|contradic|error|misstat|conflat|false|mischaracteris|mischaracteriz|fails to|does not (?:state|mention|include)|rubric point",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when accuracy was docked to <see cref="UnevidencedDeductionMaxLevel"/> or below with
    /// unverified claims present, where the evidence cites unverifiability and names no actual defect.
    /// Compliance check on BenchmarkAssessmentPrompt line 206, which instructs the assessor to never
    /// write "unverified", "could not confirm", or equivalent as the basis of an accuracy deduction.
    /// Advisory; changes no score.
    /// </summary>
    public static bool IsUnverifiabilityGroundedDeduction(
        int accuracyLevel,
        string? accuracyEvidence,
        int unverifiedClaimCount)
    {
        if (unverifiedClaimCount <= 0 || accuracyLevel > UnevidencedDeductionMaxLevel || string.IsNullOrWhiteSpace(accuracyEvidence))
        {
            return false;
        }

        return UnverifiabilityRegex.IsMatch(accuracyEvidence)
            && !DefectRegex.IsMatch(accuracyEvidence)
            && !MentionsFabrication(accuracyEvidence);
    }

    /// <summary>
    /// True when either graded dimension was docked to <see cref="UnevidencedDeductionMaxLevel"/> or
    /// below while its own evidence string names no defect.
    ///
    /// Deliberately per-dimension: accuracy evidence never justifies a completeness deduction, and
    /// pairing them would let a detailed completeness finding excuse an empty accuracy one.
    /// </summary>
    public static bool HasUnevidencedDeduction(
        int accuracyLevel, string? accuracyEvidence,
        int completenessLevel, string? completenessEvidence)
    {
        return (accuracyLevel <= UnevidencedDeductionMaxLevel && IsNoFaultEvidence(accuracyEvidence))
            || (completenessLevel <= UnevidencedDeductionMaxLevel && IsNoFaultEvidence(completenessEvidence));
    }

    // Words that describe something the answer did not say.
    private static readonly Regex OmissionRegex = new(
        @"\bomit|\bomission|does not (?:mention|include|state|list|cover|address|provide)|fails? to (?:mention|include|state|list|identify|note|cover|address|provide)|missing|no mention of|never (?:mentions|states)|leaves out|does not name",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "X rather than Y" / "X instead of Y" describes a SUBSTITUTION: the answer stated X, and X is not
    // what GnollHack does. That is an accuracy defect, correctly charged, and must not be reclassified
    // as an omission. Run 11's Q1 evidence — "provides D&D-style relative attribute modifiers ... rather
    // than GnollHack's racial attribute maxima" — flagged under the harness 11 detector and told the
    // reader that Completeness already graded it, which was false. The remaining OmissionRegex
    // alternatives ("omits", "fails to mention", "no mention of") all describe genuinely absent content.
    private static readonly Regex SubstitutionRegex = new(
        @"\b(?:provides?|gives?|states?|lists?|uses?|presents?|reports?|offers?|supplies|describes?|says)\b[^.;]{0,120}?\b(?:rather than|instead of)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Words that assert the answer said something untrue. Their presence means the deduction has a
    // genuine accuracy basis and must not flag, even if the same string also names an omission.
    private static readonly Regex FalsehoodRegex = new(
        @"\bwrong|\bincorrect|\binaccurat|\bfalse|contradic|misstat|conflat|mischaracteris|mischaracteriz|reverses|inverts|hallucinat|fabricat|\binvent(?!or)|does not exist|no such|overstate|understate",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when ACCURACY was docked to <see cref="UnevidencedDeductionMaxLevel"/> or below and its own
    /// evidence describes only an omission — the defect COMPLETENESS grades — with no assertion that
    /// anything the answer said is untrue.
    /// </summary>
    public static bool IsOmissionGroundedAccuracyDeduction(int accuracyLevel, string? accuracyEvidence)
    {
        if (accuracyLevel > UnevidencedDeductionMaxLevel || string.IsNullOrWhiteSpace(accuracyEvidence))
        {
            return false;
        }

        return OmissionRegex.IsMatch(accuracyEvidence)
            && !FalsehoodRegex.IsMatch(accuracyEvidence)
            && !SubstitutionRegex.IsMatch(accuracyEvidence);
    }
}
