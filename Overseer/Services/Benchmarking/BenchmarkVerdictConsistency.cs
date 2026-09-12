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
    /// Scoring method v7 tells the assessor that such a string may accompany level 6 only, so any
    /// level docked to 5 or below whose evidence names no defect is a deduction nobody can name — and
    /// the final synthesis will otherwise invent one to explain it.
    /// </summary>
    public const int UnevidencedDeductionMaxLevel = 5;

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
    /// Vocabulary citing unverifiability, absent corroboration, or an out-of-rubric basis as the
    /// reason for an assessment finding.
    ///
    /// The "not in/from/covered by/supported by/given in ... rubric" alternative carries a negative
    /// lookahead against a following colon. The assessment prompt requires an out-of-rubric
    /// deduction's evidence to begin with the literal marker <c>Not in rubric:</c>, a separate signal
    /// this class reads through <c>BenchmarkAssessmentParser.HasOutOfRubricMarker</c>; the lookahead
    /// keeps that marker sentence from also matching here.
    /// </summary>
    private static readonly Regex UnverifiabilityRegex = new(
        @"could not (?:be )?verif|cannot (?:be )?verif|unable to verif|unverifi|could not confirm|not confirmed|no confirmation|not verifiable"
        + @"|corroborat"
        + @"|unsupported"
        + @"|without\s+(?:any\s+)?(?:basis|support|source\s+support|corroboration|rubric\s+corroboration)"
        + @"|adjudicat"
        + @"|beyond\s+(?:the\s+)?(?:verifiable\s+)?rubric"
        + @"|outside\s+(?:the\s+)?rubric"
        + @"|not\s+(?:in|from|covered\s+by|supported\s+by|given\s+in)\s+(?:the\s+)?rubric(?!\s*:)"
        + @"|rubric\s+(?:does\s+not|doesn't|did\s+not|neither)\s+(?:support|cover|mention|corroborate|state|include)"
        + @"|(?:withh?old|kept|keeping|held)\s+(?:it\s+)?(?:below|under)\s+(?:level\s+)?[56]",
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
    ///
    /// The test each dimension applies is "names no defect", not "is one of the boilerplate no-fault
    /// strings" — <see cref="NamesAnAccuracyDefect"/> and <see cref="NamesACompletenessDefect"/>
    /// rather than <see cref="IsNoFaultEvidence"/> alone. Run 38's Q1 evidence read *"Matches
    /// rubric; accurately describes Gnoll alignment options, available roles, and core racial traits
    /// without error."* beside Accuracy 5: nothing in it is a defect, but it is a sentence rather
    /// than the anchored boilerplate, so the narrower test let it through. The completeness side
    /// additionally sets the <c>OUT-OF-SCOPE:</c> sentences aside first, because scoring method v8
    /// tells the assessor to record those instead of deducting for them — a level docked to 5 whose
    /// only stated basis is such a point is a deduction the instruction says should not exist.
    /// </summary>
    public static bool HasUnevidencedDeduction(
        int accuracyLevel, string? accuracyEvidence,
        int completenessLevel, string? completenessEvidence)
    {
        return (accuracyLevel <= UnevidencedDeductionMaxLevel && !NamesAnAccuracyDefect(accuracyLevel, accuracyEvidence))
            || (completenessLevel <= UnevidencedDeductionMaxLevel && !NamesACompletenessDefect(completenessLevel, completenessEvidence));
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

    /// <summary>
    /// The level at which ACCURACY is faultless. Below it the assessor has recorded a defect, and
    /// scoring method v7 onwards requires its evidence string to name what that defect was — which
    /// is what makes <see cref="NamesAnAccuracyDefect"/> a usable signal rather than a guess.
    /// </summary>
    public const int FullAccuracyLevel = 6;

    /// <summary>
    /// Prose asserting that the run contained no false statements.
    ///
    /// Each alternative is a form a synthesis has actually produced, and each is here for a reason:
    /// - "free of factual errors" / "devoid of factual errors" — the direct claim. "devoid" costs
    ///   nothing and is the same assertion in a register these models reach for.
    /// - "no factual errors" — the same claim in the negative, including "zero factual errors".
    /// - "rather than factual errors" / "instead of factual errors" — the run 14 form: *"Identified
    ///   weaknesses were confined to secondary omissions rather than factual errors"*. The claim is
    ///   made by contrast rather than by assertion, which the first two alternatives do not catch.
    /// - "without factual errors" — the participle form of the same contrast.
    /// - "confined to … omissions" — the same sentence's other half, so the claim is still detected
    ///   when the writer drops the words "factual errors" and says only that the weaknesses were
    ///   omissions. The gap is bounded to 120 characters and excludes sentence punctuation for the
    ///   same reason <see cref="SubstitutionRegex"/> bounds its own: an unbounded gap turns a
    ///   two-word pattern into a paragraph-wide one.
    /// </summary>
    private static readonly Regex NoFactualErrorsRegex = new(
        @"(?:free|devoid)\s+of\s+(?:any\s+)?factual\s+error|\b(?:no|zero)\s+(?:material\s+|significant\s+|outright\s+)?factual\s+error|(?:rather\s+than|instead\s+of)\s+(?:any\s+)?factual\s+error|without\s+(?:any\s+)?factual\s+error|confined\s+to\b[^.;!?]{0,120}?\bomission",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The same vocabulary used to *admit* factual errors. A synthesis that writes "the run was not
    /// free of factual errors" or "Q14 contains a factual error" is doing the opposite of the thing
    /// this detector exists to catch, and matching it would accuse an honest synthesis — the
    /// expensive direction, exactly as in <see cref="QuestionsNamedWithFabrication"/>. A sentence
    /// matching this is disqualified even when it also matches <see cref="NoFactualErrorsRegex"/>.
    /// </summary>
    private static readonly Regex FactualErrorsAdmittedRegex = new(
        @"\bnot\s+(?:entirely\s+|wholly\s+|completely\s+)?(?:free|devoid)\s+of\s+(?:any\s+)?factual\s+error|\bnot\s+without\s+(?:any\s+)?factual\s+error|\b(?:contains?|contained|carries|carried|includes?|included|exhibits?|exhibited)\s+(?:a\s+|some\s+|several\s+|two\s+|three\s+)?factual\s+error|\bfactual\s+errors?\s+(?:were|was|are|is)\s+(?:present|found|identified|recorded|noted)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the run-level synthesis claims the run held no factual errors.
    ///
    /// Sentence-scoped, never paragraph-scoped, for the same reason
    /// <see cref="QuestionsNamedWithFabrication"/> is: the synthesis is multi-paragraph prose, and
    /// a paragraph-wide "confined to … omissions" would match a paragraph that names an omission in
    /// one sentence and a factual error in the next — the shape of an *honest* synthesis. A missed
    /// match costs an advisory line; a false match accuses a correct one.
    ///
    /// Advisory on both sides. This says only what the synthesis claimed; whether that claim is
    /// contradicted takes <see cref="AnswersWithNamedAccuracyDefects"/> as well, and the caller
    /// renders nothing unless both are true.
    /// </summary>
    public static bool SynthesisClaimsNoFactualErrors(string? synthesisText)
    {
        if (string.IsNullOrWhiteSpace(synthesisText))
        {
            return false;
        }

        foreach (string sentence in SplitSentences(synthesisText))
        {
            if (NoFactualErrorsRegex.IsMatch(sentence) && !FactualErrorsAdmittedRegex.IsMatch(sentence))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An explicit denial that ACCURACY has any defect, written anywhere in the evidence sentence
    /// rather than as the whole string <see cref="NoFaultEvidenceRegex"/> requires. Modelled on
    /// <see cref="NoFactualErrorsRegex"/>, which encodes the same vocabulary for synthesis prose;
    /// this is a separate regex because that one also matches "confined to ... omission", a
    /// synthesis-level alternative that does not belong in a per-verdict evidence check.
    ///
    /// Each alternative is a denial form a grader has actually written:
    /// - "no factual error(s)" / "zero factual errors" — the run 22 Q3/Q4/Q17 form: "All stated
    ///   stats ... match the rubric with no factual errors."
    /// - "no contradiction(s)" / "no contradicted claims" — the same denial aimed at the
    ///   contradiction vocabulary instead of "factual error".
    /// - "no error(s)" — the bare form, without "factual" in front.
    /// - "no inaccurac(y|ies)" — the near neighbour of "error".
    /// - "no false statement(s)" / "no misstatement(s)" — the remaining near neighbours in the
    ///   family, included because they cost nothing once the others are here.
    /// - "free of factual errors" / "devoid of factual errors" — the direct-claim form
    ///   <see cref="NoFactualErrorsRegex"/> also carries.
    /// - "without error(s)" / "without factual error(s)" — the participle form.
    /// </summary>
    private static readonly Regex DefectDenialRegex = new(
        @"(?:free|devoid)\s+of\s+(?:any\s+)?factual\s+error|\b(?:no|zero)\s+(?:material\s+|significant\s+|outright\s+)?(?:factual\s+error|contradict(?:ion|ed\s+claim)|error|inaccurac|false\s+statement|misstatement)|\bwithout\s+(?:any\s+)?(?:factual\s+)?error",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A clause boundary that turns a denial into a concession. "No factual errors, but states the
    /// level as 40 when it is 25" denies one defect and charges another, and only the first half is
    /// a denial — so any of these markers disqualifies <see cref="DefectDenialRegex"/> from
    /// suppressing.
    ///
    /// Needed because <see cref="FalsehoodRegex"/> cannot carry this on its own. Its vocabulary is
    /// the words that *assert* a falsehood, and a grader charging a defect in neutral prose
    /// ("states the level as 40 when it is 25") uses none of them; the concession marker is then the
    /// only signal that the sentence did not stop at its denial.
    /// </summary>
    private static readonly Regex ConcessionRegex = new(
        @"\b(?:but|however|although|though|yet|except|aside\s+from|apart\s+from|other\s+than|save\s+for)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when this verdict docked ACCURACY and its own evidence string names the defect.
    ///
    /// The boilerplate exclusion is the whole point. Level 5 is the modal level of a strong run,
    /// and a level-5 verdict whose evidence reads "Matches rubric." has named nothing — flagging it
    /// would fire on most answers of most runs and devalue the signal. The exclusion reuses
    /// <see cref="IsNoFaultEvidence"/> rather than a fresh string list, so "Matches rubric.",
    /// "Matches rubric" and "Aligns with rubric." are covered along with the rest of the family the
    /// prompt offers as the *full-level* form, and an absent evidence string is excluded with them.
    ///
    /// A second exclusion covers the same denial written as a sentence rather than as boilerplate —
    /// run 22's Q3/Q4/Q17 evidence read "... match the rubric with no factual errors." — via the
    /// unanchored <see cref="DefectDenialRegex"/>. That exclusion yields whenever the same evidence
    /// also names a real defect: <see cref="FalsehoodRegex"/> overrides it exactly as it overrides
    /// <see cref="OmissionRegex"/> in <see cref="IsOmissionGroundedAccuracyDeduction"/>, and
    /// <see cref="OmissionRegex"/> overrides it too, so a denial paired with an admitted omission
    /// ("no factual errors, but omits the weapon-swap cost") still names a defect.
    /// <see cref="ConcessionRegex"/> covers the general shape of that sentence, where the defect
    /// the grader went on to charge is written in prose neither of those two regexes recognises.
    /// </summary>
    public static bool NamesAnAccuracyDefect(int? accuracyLevel, string? accuracyEvidence)
    {
        if (!accuracyLevel.HasValue || accuracyLevel.Value >= FullAccuracyLevel)
        {
            return false;
        }

        return !NamesNoDefect(accuracyEvidence);
    }

    /// <summary>
    /// The evidence test shared by <see cref="NamesAnAccuracyDefect"/>,
    /// <see cref="NamesACompletenessDefect"/> and <see cref="IsFormOnlyDeduction"/>: true when the
    /// string charges nothing, either as the anchored boilerplate <see cref="IsNoFaultEvidence"/>
    /// recognises or as a sentence-form denial. One implementation rather than three copies, because
    /// the four regexes only compose correctly together — the denial suppresses, and each of
    /// <see cref="FalsehoodRegex"/>, <see cref="OmissionRegex"/> and <see cref="ConcessionRegex"/>
    /// overrides that suppression — and a second copy would drift out of that arrangement silently.
    /// </summary>
    private static bool NamesNoDefect(string? evidence)
    {
        if (IsNoFaultEvidence(evidence))
        {
            return true;
        }

        return DefectDenialRegex.IsMatch(evidence!)
            && !FalsehoodRegex.IsMatch(evidence!)
            && !OmissionRegex.IsMatch(evidence!)
            && !ConcessionRegex.IsMatch(evidence!);
    }

    /// <summary>
    /// The evidence with every sentence that opens with <paramref name="marker"/> removed, so what
    /// remains is what the assessor charged in its own right.
    ///
    /// A sentence that starts with the marker is the whole recorded point, not a prefix of a larger
    /// claim: the assessor is instructed to write the marker at the start of the point it records,
    /// so everything up to the sentence terminator belongs to it. Anything the assessor also meant
    /// as a deduction is written as a separate sentence, and that is exactly what survives here.
    /// Ordinal and case-insensitive, matching how <see cref="IsNoFaultEvidence"/> treats its own
    /// input; a null or blank evidence string yields the empty string, which names no defect.
    /// </summary>
    private static string StripMarkerSentences(string? evidence, string marker)
    {
        if (string.IsNullOrWhiteSpace(evidence) || string.IsNullOrEmpty(marker))
        {
            return evidence ?? string.Empty;
        }

        var kept = SplitSentences(evidence)
            .Select(s => s.Trim())
            .Where(s => !s.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return string.Join(" ", kept);
    }

    /// <summary>
    /// True when this verdict docked COMPLETENESS and its own evidence string names the defect,
    /// counting only what the assessor charged rather than what it set aside.
    ///
    /// The accuracy counterpart's reasoning applies unchanged — see
    /// <see cref="NamesAnAccuracyDefect"/> for why the boilerplate and the sentence-form denial are
    /// both excluded — with one addition this dimension alone needs. Scoring method v8 tells the
    /// assessor that the question defines the scope and that a rubric point the question did not ask
    /// for is recorded under <see cref="BenchmarkAssessmentParser.OutOfScopeCompletenessMarker"/>
    /// rather than deducted for, so those sentences are removed before the evidence is judged.
    /// Run 38's Q6 carried Completeness 5 whose evidence was one such sentence and nothing else:
    /// with the marker text left in, the evidence looks like a named defect, when what it names is
    /// the reason there should have been no deduction at all.
    /// </summary>
    public static bool NamesACompletenessDefect(int? level, string? evidence)
    {
        if (!level.HasValue || level.Value >= FullAccuracyLevel)
        {
            return false;
        }

        string rest = StripMarkerSentences(evidence, BenchmarkAssessmentParser.OutOfScopeCompletenessMarker);
        return !NamesNoDefect(rest);
    }

    /// <summary>
    /// True when a COMPLETENESS level below full rests on nothing but out-of-scope rubric points:
    /// the marker is present, the level is at <see cref="UnevidencedDeductionMaxLevel"/> or below,
    /// and what remains after the marked sentences names no defect.
    ///
    /// Narrower than the marker count the assessor's own flag gives, and that is the point. A
    /// verdict may record an out-of-scope point beside a genuine omission and deduct for the
    /// omission — run 38's Q18 — or record one at level 6 and deduct for nothing at all — its Q12.
    /// Neither is a case of the instruction being disregarded; only this one is, and this is the
    /// count the report and the admin DTO print beside the raw marker total.
    /// </summary>
    public static bool IsOutOfScopeOnlyDeduction(int completenessLevel, string? completenessEvidence)
    {
        return HasOutOfScopeMarker(completenessEvidence)
            && completenessLevel <= UnevidencedDeductionMaxLevel
            && !NamesACompletenessDefect(completenessLevel, completenessEvidence);
    }

    /// <summary>
    /// The READABILITY counterpart of <see cref="IsOutOfScopeOnlyDeduction"/>, over scoring method
    /// v9's <see cref="BenchmarkAssessmentParser.FormOnlyReadabilityMarker"/>: a level docked to
    /// <see cref="UnevidencedDeductionMaxLevel"/> or below whose only stated basis is a rubric
    /// format suggestion the prompt says to record instead of deducting for.
    ///
    /// Counted, never flagged. <see cref="HasUnevidencedDeduction"/> covers the two dimensions whose
    /// evidence strings are a deduction basis; readability evidence is not one, so a shortfall here
    /// is a measurement of the instrument rather than a contested verdict.
    ///
    /// <paramref name="markerRecorded"/> exists because a run graded before <c>readability</c> joined
    /// <c>BenchmarkService.BuildEvidenceJson</c> has no stored readability evidence at all, leaving
    /// the per-answer <c>ReadabilityFormOnly</c> column as its sole surviving marker signal. Passing
    /// it with a null evidence string is the right reading rather than a concession: the FORM regex
    /// is anchored at the start of the evidence, so a string carrying the marker has no second half
    /// to charge a defect in, and a null one names no defect either way.
    /// </summary>
    public static bool IsFormOnlyDeduction(int readabilityLevel, string? readabilityEvidence, bool markerRecorded = false)
    {
        if (!markerRecorded && !HasFormOnlyMarker(readabilityEvidence))
        {
            return false;
        }

        if (readabilityLevel > UnevidencedDeductionMaxLevel)
        {
            return false;
        }

        string rest = StripMarkerSentences(readabilityEvidence, BenchmarkAssessmentParser.FormOnlyReadabilityMarker);
        return NamesNoDefect(rest);
    }

    /// <summary>
    /// The marker as models actually write it: hyphens or spaces between the words, any casing, and
    /// the colon possibly spaced away from it. The colon is the one part that is required — without
    /// it the pattern would be the ordinary English phrase, which appears in evidence strings that
    /// are describing something else. Matched anywhere in the string rather than only at the start,
    /// because an assessor that records a deduction and an out-of-scope point in one evidence string
    /// puts the marker in front of the second half.
    /// </summary>
    private static readonly Regex OutOfScopeMarkerRegex = new(
        @"\bout[-\s]?of[-\s]?scope\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when completeness evidence carries the out-of-scope marker. Total on its input: a null,
    /// an empty string or a marker the assessor mangled all return false rather than failing the
    /// parse — absence is the normal case, and one lost measurement must never cost a verdict.
    /// </summary>
    public static bool HasOutOfScopeMarker(string? completenessEvidence)
    {
        return !string.IsNullOrWhiteSpace(completenessEvidence)
            && OutOfScopeMarkerRegex.IsMatch(completenessEvidence);
    }

    /// <summary>
    /// Anchored to the start of the evidence string, unlike <see cref="OutOfScopeMarkerRegex"/>.
    /// The word is short enough to occur in ordinary prose about an answer's presentation, and
    /// <c>readabilityEvidence</c> carries nothing but this marker, so there is no second half for
    /// it to introduce. Leading whitespace and casing are tolerated; the colon is required.
    /// </summary>
    private static readonly Regex FormOnlyMarkerRegex = new(
        @"^\s*form\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when readability evidence opens with the FORM marker. Total on its input, for the same
    /// reason <see cref="HasOutOfScopeMarker"/> is: absence is the normal case, and one lost
    /// measurement must never cost a verdict.
    /// </summary>
    public static bool HasFormOnlyMarker(string? readabilityEvidence)
    {
        return !string.IsNullOrWhiteSpace(readabilityEvidence)
            && FormOnlyMarkerRegex.IsMatch(readabilityEvidence);
    }

    /// <summary>
    /// The order indexes of the verdicts that docked ACCURACY with evidence naming a concrete
    /// defect — the questions a synthesis may not describe the run as free of factual errors over.
    ///
    /// On the 2026-09-06 run this is exactly Q11 ("overlooks that weapon swapping between sets
    /// takes 0 turns") and Q14 ("lists Level as 40 and Hit dice as 25; … his level/HD is 25, while
    /// 40 is his monster difficulty"), both Accuracy 5/6, while the synthesis reported the run's
    /// weaknesses as "confined to secondary omissions rather than factual errors".
    ///
    /// Takes a tuple sequence rather than a verdict type so that this class keeps its only
    /// dependencies on <c>string</c> and <c>int</c>: both the synthesis prompt (which holds
    /// <see cref="BenchmarkPerQuestionVerdictSummary"/>) and the report builder (which holds
    /// answers plus their stored evidence JSON) project into it in one line.
    /// </summary>
    public static IReadOnlyList<int> AnswersWithNamedAccuracyDefects(
        IEnumerable<(int OrderIndex, int? AccuracyLevel, string? AccuracyEvidence)> verdicts)
    {
        if (verdicts == null)
        {
            return Array.Empty<int>();
        }

        return verdicts
            .Where(v => NamesAnAccuracyDefect(v.AccuracyLevel, v.AccuracyEvidence))
            .Select(v => v.OrderIndex)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
    }
}
