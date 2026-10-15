namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.RootCause;

/// <summary>The writer's output with every offending item and paragraph removed.</summary>
public sealed class BenchmarkReportCleanResult
{
    /// <summary>A cleaned copy; the input is never changed.</summary>
    public BenchmarkReportWriterOutput Output { get; init; } = default!;

    /// <summary>Every remaining issue and every removal; removals have <see cref="BenchmarkReportValidationNote.Dropped"/> set.</summary>
    public IReadOnlyList<BenchmarkReportValidationNote> Notes { get; init; } = default!;

    /// <summary>The headline is missing or invalid, or a required slot is empty after dropping.</summary>
    public bool Fatal { get; init; }

    public string? FatalReason { get; init; }

    /// <summary>The output is fatal only because of its headline: every required slot kept a paragraph.</summary>
    public bool HeadlineOnlyFatal { get; init; }
}

/// <summary>
/// The report-pack document rules (D7), checked against the fact sheet and the content snapshot.
///
/// <para>Rules 2, 3, 8, 9, 10, 11, 12, 16, 17 and 19 apply to every prose string: the headline, each
/// paragraph of each section, and the text of every item and topic. A section's paragraphs are
/// checked one by one, so <see cref="DropInvalid"/> can remove only the offending ones.</para>
///
/// <list type="number">
/// <item>Structure: headline and required slots present and non-empty (a peer-only slot only when
/// the sheet has peers); no unknown slots; no lists the audience does not use; item texts non-empty.</item>
/// <item>Tokens: <c>{{key}}</c> names a fact, <c>{{peer:X}}</c> a peer letter, <c>{{subject}}</c> the subject;
/// written exactly, without inner spaces; no stray braces.</item>
/// <item>No bare digit once tokens, known names, the fixed phrase <c>95% interval</c> (<see cref="IntervalLevelRegex"/>)
/// and <c>Q&lt;n&gt;</c> / <c>R&lt;n&gt;</c> references (on a battery sheet also <c>S&lt;suite&gt;-Q&lt;n&gt;</c>) are masked.</item>
/// <item>Every question number exists in the subject's exam; topics cover every question where required
/// (on a battery sheet, every question given in detail). A battery sheet's questions are referred to as
/// <c>S&lt;suite&gt;-Q&lt;n&gt;</c>, never as a bare <c>Q&lt;n&gt;</c>.</item>
/// <item>Every evidence id and <c>R&lt;n&gt;</c> reference exists; strengths, weaknesses and model
/// points cite one.</item>
/// <item>A strength cites no weakness row and a weakness no strength row; a finding citing only
/// Conflicting rows says the graders disagree.</item>
/// <item>Word and item limits: every audience's slot word caps, the Executive Summary's item word
/// cap, the list item counts and the points per model.</item>
/// <item>No headings, Markdown tables or HTML.</item>
/// <item>No run of <see cref="ShingleLength"/> words shared with the content snapshot.</item>
/// <item>No peer name, label, model id or provider other than the subject's own provider.</item>
/// <item>No significance claims. Matching is whole-word, so <c>insignificant</c> passes.</item>
/// <item>US English: no word from <see cref="BritishSpellings"/>. It asks for the repair turn, but
/// <see cref="DropInvalid"/> keeps the text and records the note instead of dropping it.</item>
/// <item>The Executive Summary's confidence slot does not call the quality interval narrow, wide,
/// tight or broad (<see cref="IntervalWidthWords"/>). Like rule 12 it asks for the repair turn, and
/// <see cref="DropInvalid"/> keeps the paragraph and records the note.</item>
/// <item>The headline does not mention the claim verifier (<see cref="VerifierRegex"/>): its
/// refutations are advisory and belong, attributed, in the weaknesses. A warning like rule 12: it
/// asks for the repair turn, and <see cref="DropInvalid"/> keeps the text and records the note.</item>
/// <item>Retired (question notes); the number is never reused.</item>
/// <item>A sentence holding <c>{{subject}}</c>, a <c>{{peer:X}}</c> token and a word of
/// <see cref="ComparativeWords"/>, where <c>peer.X.intervalOverlap</c> is true, also says
/// <c>overlap</c> or <c>not established</c>, or cites that fact; where <c>peer.X.pairedExcludesZero</c> is
/// true, saying that the paired interval excludes zero (or citing that fact) does as well. A speed or cost
/// sentence (<see cref="RankingAxes"/>) is not checked against the quality intervals. A warning.</item>
/// <item>No word of <see cref="HypeWords"/>, whole words ignoring case. A warning.</item>
/// <item>Retired (<c>model_developers</c> recommendations); the number is never reused.</item>
/// <item>No negation (<see cref="NegationWords"/>) within <see cref="NegationWindowWords"/> words before a
/// fact token, in the same sentence, whose display starts with <c>0</c>: "no critical errors across
/// {{errors.critical}}" reads "no critical errors across 0 of 18 answers". A warning.</item>
/// </list>
///
/// <para>A comparison-scope sheet (<see cref="BenchmarkReportFactSheet.IsComparison"/>) is checked
/// against its audience's comparison slots: its tokens are <c>{{model:X}}</c> for every covered
/// model's letter and its fact keys, with no <c>{{subject}}</c> or <c>{{peer:X}}</c>; every covered
/// model's name is a name of rule 10; rule 16 reads, for each comparative word, the pair it relates
/// (the nearest <c>{{model:X}}</c> before it and the nearest after it in the sentence, or every pair of
/// the sentence when either is missing), and gives at most one note per sentence: a quality sentence is
/// read against the pair's <c>pair.X.Y.intervalOverlap</c>, where a family's established result
/// (<c>pair.X.Y.quality.reference</c> or <c>.allPairs</c>) may be stated instead, and a speed or cost
/// sentence against the pair's paired result on that axis, where it has one; its
/// <c>models</c> list gives each covered model at most the audience's points, each citing evidence; and
/// rule 21, a warning, asks for an entry for every covered model. Rule 31 (<see cref="ComparisonTokenShapeRule"/>),
/// a warning, reads the text with every token resolved to its display: a sentence continued after a full
/// stop, a sentence-valued fact placed inside a sentence or parentheses, a clause-valued fact
/// (<c>Model A took …</c>) after its own model or verb, a run of unlabeled <c>N of M</c> counts, and in a Root
/// Cause Analysis a sentence-shaped fact (<see cref="RootCauseClaimSupport.IsSentenceShaped"/>) after words of the
/// writer's own sentence. Rule 37
/// (<see cref="BareModelLetterRule"/>), a warning, flags a model written by its bare letter, <c>Model H</c>
/// or <c>Models H and I</c>, outside its token.</para>
///
/// <para>A Root Cause Analysis, in comparison or model scope, adds the claim discipline of rules 32 to 36,
/// each built from <see cref="RootCauseClaimSupport"/>, which the prompt shares: rule 32, a causal word in
/// the scope or an evidence slot, or in an observation, drops the paragraph or item; rule 33, a causal word
/// in an answer slot, a hypotheses slot, the conclusions or the next runs that states a cause without a token of
/// a hypothesis graded Low or higher or of an axis in its sentence (in the next runs, a run token too), or on
/// hypotheses graded Low only without a hedge word, or a negated causal word without any hypothesis or axis
/// token, drops the paragraph; rule 34, a certainty word
/// anywhere without a High hypothesis token or an established axis status token in its sentence, drops the
/// paragraph or item; rule 35, a writer hypothesis without an axis, statement, evidence for, refuted-if
/// statement or known locus, with a confidence other than Low, or citing a model or fact that does not
/// exist, is dropped; rule 36, a warning, wants the conclusions to cite every axis' status. An answer slot
/// is required only for a question the sheet asks
/// (<see cref="RootCauseClaimSupport.Questions"/>). Rule 31 applies to it in both scopes, a hypothesis'
/// label excepted, and rule 4 wants a topic for every question it cites whose text the writer was given.</para>
///
/// <para>A chat consistency sheet (<see cref="BenchmarkReportFactSheet.IsChatConsistency"/>) is checked
/// against its audience's chat consistency slots and word caps, with the claim discipline of rules
/// C1 to C7 (rule numbers 22 to 28), the readable-text rule C8 (rule 29) and the repetition rule C9 (rule 30)
/// on top of the rules above. Its controls are the peers, so <c>{{peer:X}}</c> names one. C1, C2, C3, C4, the
/// wording half of C6, the claim half of C7 and C8 read the headline and every paragraph, and a paragraph
/// failing one is dropped like any other error; C9 reads them too, as a warning that keeps the text. C5 is a
/// warning on the whole document. The other halves of C6 (the document cites
/// <c>{{scope.hours}}</c>, where the periods share any hours) and C7 (<c>ruledOut</c> cites every Overseer event) have nothing to drop,
/// so after the repair turn they are recorded against the kept text without
/// <see cref="BenchmarkReportValidationNote.Dropped"/>.</para>
/// </summary>
public static class BenchmarkReportPackValidator
{
    public const int ShingleLength = 8;
    public const int HeadlineMaxWords = 35;
    public const int TopicMaxWords = 12;

    /// <summary>The Executive Summary's "What this means for use as a game assistant".</summary>
    public const int MeaningMaxWords = 90;

    /// <summary>The Executive Summary's "How reliable this result is".</summary>
    public const int ConfidenceMaxWords = 60;

    /// <summary>The Executive Summary's paragraph under "How it compares".</summary>
    public const int ComparisonMaxWords = 70;

    /// <summary>Each strength and weakness of the Executive Summary.</summary>
    public const int ExecutiveItemMaxWords = 30;

    // Comparison-scope slot caps.
    public const int OverviewMaxWords = 90;
    public const int WhichModelMaxWords = 150;
    public const int TradeOffsMaxWords = 100;
    public const int ReliabilityMaxWords = 80;
    public const int ExecutiveAgainstReferenceMaxWords = 100;

    /// <summary>Each point of a comparison-scope Executive Summary's <c>models</c> list.</summary>
    public const int ExecutiveModelPointMaxWords = 30;

    /// <summary>
    /// The rule number of the check that a comparison-scope document's <c>models</c> list has an entry
    /// for every covered model; a warning with nothing to drop.
    /// </summary>
    public const int ModelCoverageRule = 21;

    /// <summary>The word cap of each point of the audience's comparison-scope <c>models</c> list.</summary>
    public static int ModelPointMaxWords(BenchmarkReportAudience audience) => ExecutiveModelPointMaxWords;

    /// <summary>The rule number of the US English check, whose notes never drop an item.</summary>
    public const int UsSpellingRule = 12;

    /// <summary>
    /// The rule number of the interval-width check in the Executive Summary's confidence slot, whose
    /// notes never drop a paragraph.
    /// </summary>
    public const int IntervalWidthRule = 13;

    /// <summary>The rule number of the claim-verifier check on the headline, whose notes never drop text.</summary>
    public const int VerifierInSummaryRule = 14;

    // Rules 15 (question notes) and 18 (model_developers recommendations) are retired: never reuse them.

    /// <summary>
    /// The rule number of the check that a comparison of the subject with a peer whose interval
    /// overlaps it says so; its notes never drop text.
    /// </summary>
    public const int OverlapHedgeRule = 16;

    /// <summary>The rule number of the hype-word check, whose notes never drop text.</summary>
    public const int HypeWordRule = 17;

    /// <summary>
    /// The rule number of the check that a negation does not precede a token whose value starts with
    /// zero; its notes never drop text.
    /// </summary>
    public const int ZeroTokenNegationRule = 19;

    /// <summary>Rule 19 looks this many words back from a token for a negation.</summary>
    public const int NegationWindowWords = 4;

    // Chat consistency claim discipline, rules C1 to C7 (chat consistency scope only). Each note's
    // message starts with its C id.

    /// <summary>C1: change vocabulary needs a decisive endpoint verdict, estimate or attribution token in its sentence.</summary>
    public const int ChatChangeClaimRule = 22;

    /// <summary>C2: intent and mechanism vocabulary needs a provider-confirmed cause in its sentence.</summary>
    public const int ChatIntentMechanismRule = 23;

    /// <summary>C3: a causal connective needs an attribution token in its sentence.</summary>
    public const int ChatCausalClaimRule = 24;

    /// <summary>C4: public-claim wording needs an Established grade token in its sentence.</summary>
    public const int ChatPublicClaimRule = 25;

    /// <summary>C5: an inconclusive endpoint the document cites needs its minimum detectable effect somewhere in the document. A warning.</summary>
    public const int ChatInconclusiveMdeRule = 26;

    /// <summary>C6: all-hours wording needs a true time-of-day fact in its sentence, and every document cites the hours where the periods share any.</summary>
    public const int ChatHoursRule = 27;

    /// <summary>
    /// C7: in a Provider Issue Report, a claim about the model or its serving needs a provider-side
    /// attribution in its sentence, and the ruled-out slot cites every Overseer event.
    /// </summary>
    public const int ChatProviderReportRule = 28;

    /// <summary>
    /// C8: readable text. A chat consistency headline or paragraph holds no run of twelve or more hex
    /// digits, no JSON (<c>{"</c>), no <see cref="global::Overseer.Services.ChatConsistency.OverseerEventKinds"/>
    /// identifier, and cites no fact kept from the writer
    /// (<see cref="BenchmarkChatConsistencyReportFacts.WriterHidden(string)"/> and
    /// <see cref="BenchmarkChatConsistencyReportFacts.WriterHiddenKeys"/>).
    /// </summary>
    public const int ChatReadableTextRule = 29;

    /// <summary>
    /// C9: no slot-filling repetition and no spliced sentence, read with every fact token resolved to its
    /// value: no <c>A and A, respectively</c> whose two operands are equal (ignoring case), and no full stop
    /// followed by a lower-case <c>and</c> inside one sentence. A warning: it asks for the repair turn and
    /// keeps the text.
    /// </summary>
    public const int ChatRepetitionRule = 30;

    /// <summary>
    /// Rule 31: the shape of the fact tokens, read with each token resolved to its display (comparison scope,
    /// and a Root Cause Analysis in either scope). A warning: it asks for the repair turn and keeps the text.
    /// </summary>
    public const int ComparisonTokenShapeRule = 31;

    // Root Cause Analysis claim discipline, rules 32 to 36 (RootCauseClaimSupport).

    /// <summary>Rule 32: a causal word in the scope or an evidence slot, or in an observation.</summary>
    public const int RootCauseEvidenceCausalRule = 32;

    /// <summary>
    /// Rule 33: a causal word in an answer slot, a hypotheses slot, the conclusions or the next runs needs in its sentence a
    /// token of a hypothesis graded Low or higher, hedged when the hypotheses are Low only, or of an axis; a negated one
    /// may cite any hypothesis, a Not supported one included.
    /// </summary>
    public const int RootCauseCausalClaimRule = 33;

    /// <summary>Rule 34: a certainty word needs a High hypothesis token or an established axis status token in its sentence.</summary>
    public const int RootCauseCertaintyRule = 34;

    /// <summary>Rule 35: a writer hypothesis is complete, at confidence Low, and cites only models and facts that exist.</summary>
    public const int RootCauseWriterHypothesisRule = 35;

    /// <summary>Rule 36: the conclusions cite every axis' <c>rca.axis.&lt;axis&gt;.status</c>. A warning.</summary>
    public const int RootCauseAxisStatusRule = 36;

    /// <summary>
    /// Rule 37: comparison-scope prose names a model only through its <c>{{model:X}}</c> token, never as a bare
    /// <c>Model X</c> or <c>Models X and Y</c>. A warning: it asks for the repair turn and keeps the text.
    /// </summary>
    public const int BareModelLetterRule = 37;

    // Root Cause Analysis slot caps.
    public const int RootCauseScopeMaxWords = 120;
    public const int RootCauseEvidenceMaxWords = 100;

    /// <summary>An evidence slot's cap in a run- or battery-completion Root Cause Analysis.</summary>
    public const int RootCauseCompletionEvidenceMaxWords = 100;

    public const int RootCauseHypothesesMaxWords = 120;
    public const int RootCauseConclusionsMaxWords = 120;
    public const int RootCauseNextRunsMaxWords = 50;
    public const int RootCauseLimitationsMaxWords = 120;

    /// <summary>Each answer slot of a Root Cause Analysis (<see cref="BenchmarkReportSlots.RootCauseAnswerSlots"/>).</summary>
    public const int RootCauseAnswerMaxWords = 60;

    /// <summary>Each observation of a Root Cause Analysis.</summary>
    public const int RootCauseObservationMaxWords = 35;

    /// <summary>Each text of a writer hypothesis: its statement, each item for and against, and its refuted-if statement.</summary>
    public const int WriterHypothesisPartMaxWords = 40;

    /// <summary>Whether a rule's notes are warnings: they ask for the repair turn but never drop text.</summary>
    public static bool IsWarningRule(int rule)
        => rule is UsSpellingRule or IntervalWidthRule or VerifierInSummaryRule or OverlapHedgeRule
            or HypeWordRule or ZeroTokenNegationRule or ModelCoverageRule or ChatInconclusiveMdeRule
            or ChatRepetitionRule or ComparisonTokenShapeRule or RootCauseAxisStatusRule or BareModelLetterRule;

    /// <summary>Words rule 19 reads as a negation, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> NegationWords = new[] { "no", "none", "never", "without", "zero" };

    /// <summary>Words rule 16 reads as ranking one model over another, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ComparativeWords = new[]
    {
        "higher", "lower", "better", "worse", "ahead", "behind", "outperform", "outperforms", "outperformed",
        "beat", "beats", "leads", "trails"
    };

    /// <summary>Hype and filler words rule 17 flags in prose, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> HypeWords = new[]
    {
        "impressive", "remarkable", "outstanding", "stellar", "exceptional", "robust", "seamless", "leverage", "delve",
        "game-changing", "cutting-edge", "settled", "proven", "definitive", "definitively", "conclusive", "conclusively"
    };

    /// <summary>Adjectives rule 13 flags for the quality interval, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> IntervalWidthWords = new[]
    {
        "narrow", "narrower", "wide", "wider", "tight", "tighter", "broad"
    };

    /// <summary>British spellings rule 12 flags in prose, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> BritishSpellings = new[]
    {
        "colour", "behaviour", "analyse", "analysed", "organise", "recognise", "favour", "honour", "centre",
        "defence", "catalogue", "programme", "grey", "travelled", "modelling", "labelled", "cancelled", "judgement"
    };

    /// <summary>Shorter peer names are not checked by rule 10.</summary>
    public const int MinPeerNameLength = 3;

    /// <summary>The word a finding resting only on Conflicting rows must contain, ignoring case.</summary>
    public const string DisagreementWord = "disagree";

    /// <summary>C1's change vocabulary, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatChangeWords = new[]
    {
        "got slower", "got faster", "got worse", "got better", "became slower", "became faster", "became worse", "became better",
        "slower", "faster", "degraded", "degradation", "improved", "regressed", "dropped", "rose", "declined",
        "slowdown", "slowdowns", "speedup", "speedups", "speed-up", "slowed", "sped up", "worsened", "deteriorated"
    };

    /// <summary>C2's intent vocabulary, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatIntentWords = new[]
    {
        "deliberately", "deliberate", "intentionally", "intentional", "on purpose", "throttled", "throttling", "nerfed", "nerfing",
        "sabotage", "sabotaged", "cheating", "cheated", "secretly", "quietly downgraded", "silently downgraded"
    };

    /// <summary>C2's mechanism vocabulary, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatMechanismWords = new[]
    {
        "quantized", "quantization", "quantised", "quantisation", "speculative decoding", "hardware", "batching"
    };

    /// <summary>
    /// C2's monitoring vocabulary, matched as whole words, ignoring case: GnollBench runs are made by
    /// hand, so nothing monitors the chat, and no annotation lets a sentence say so.
    /// </summary>
    public static readonly IReadOnlyList<string> ChatMonitoringWords = new[]
    {
        "monitor", "monitors", "monitored", "monitoring"
    };

    /// <summary>C3's causal connectives, matched as whole phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatCausalConnectives = new[]
    {
        "because", "due to", "caused by", "as a result of", "led to", "owing to", "driven by", "attributable to"
    };

    /// <summary>
    /// C4's public-claim wording, matched as whole words and phrases, ignoring case; a word after
    /// <c>not</c>, <c>never</c> or <c>not yet</c>, or after a hyphen (<c>provider-confirmed</c>), is not one.
    /// </summary>
    public static readonly IReadOnlyList<string> ChatPublicClaimWords = new[]
    {
        "publishable", "established", "confirmed", "proven", "definitively", "conclusively", "we can state"
    };

    /// <summary>C6's all-hours wording, matched as whole phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatAllHoursWords = new[]
    {
        "at all hours", "around the clock", "any time of day", "all times of day", "every hour of the day", "load-independent",
        "independent of load", "regardless of load", "regardless of the time of day", "24/7"
    };

    /// <summary>C7's model and serving terms in a Provider Issue Report, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> ChatModelServingTerms = new[]
    {
        "the model", "its serving", "serving", "latency", "latencies", "speed", "speeds", "decode", "decoding",
        "served model", "served models", "snapshot", "snapshots"
    };

    /// <summary>The Provider Issue Report slots C7 leaves out of its model and serving check: they identify, and claim nothing.</summary>
    public static readonly IReadOnlyList<string> ChatIdentifyingSlots = new[]
    {
        BenchmarkReportSlots.AffectedModel, BenchmarkReportSlots.SampleRequestIds
    };

    private static readonly Regex TokenRegex = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex QuestionRefRegex = new(@"(?<![\p{L}\p{N}_])Q(\d+)(?![\p{L}\p{N}_])", RegexOptions.Compiled);
    private static readonly Regex RowRefRegex = new(@"(?<![\p{L}\p{N}_])R(\d+)(?![\p{L}\p{N}_])", RegexOptions.Compiled);
    private static readonly Regex QuestionIdRegex = new(@"^Q(\d+)$", RegexOptions.Compiled);

    /// <summary>A battery question's suite-qualified reference, <c>S2-Q7</c>, in prose.</summary>
    private static readonly Regex BatteryRefRegex = new(@"(?<![\p{L}\p{N}_])S\d+-Q\d+(?![\p{L}\p{N}_])", RegexOptions.Compiled);

    /// <summary>A battery question's suite-qualified reference as an evidence id.</summary>
    private static readonly Regex BatteryIdRegex = new(@"^S\d+-Q\d+$", RegexOptions.Compiled);
    private static readonly Regex DigitWordRegex = new(@"\S*\d\S*", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex ParagraphSplitRegex = new(@"\n[ \t]*\n", RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(@"^[ \t]{0,3}#{1,6}(?:[ \t]|$)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex HeadingUnderlineRegex = new(@"^[ \t]{0,3}(?:={2,}|-{3,})[ \t]*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TableRowRegex = new(@"^[ \t]*\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TableSeparatorRegex = new(
        @"^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex HtmlRegex = new(@"<!--|</?[A-Za-z][A-Za-z0-9-]*(?:\s[^<>]*)?/?>", RegexOptions.Compiled);

    /// <summary>A model named by its bare letter, <c>Model H</c> or <c>Models H</c>, case-sensitive (rule 37).</summary>
    private static readonly Regex BareModelLetterRegex = new(@"\bModels? [A-Z]{1,2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SignificanceRegex = new(
        @"\b(?:significant|significantly|statistically|reliably\s+better|reliably\s+worse|clearly\s+outperform(?:s|ed)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BritishSpellingRegex = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", BritishSpellings) + @")(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"verifier" or "verifiers", whole word, ignoring case; it covers "claim verifier".</summary>
    private static readonly Regex VerifierRegex = new(
        @"(?<![\p{L}\p{N}])verifiers?(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IntervalWidthRegex = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", IntervalWidthWords) + @")(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ComparativeRegex = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", ComparativeWords) + @")(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>What rule 16 accepts as saying that the order is open: "overlap" in any form, or "not established".</summary>
    private static readonly Regex OverlapHedgeRegex = new(
        @"(?<![\p{L}\p{N}])(?:overlap\p{L}*|not\s+established)(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Words rule 16 reads as a sentence about speed, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> SpeedWords = new[] { "time", "faster", "slower", "latency", "seconds" };

    /// <summary>Words rule 16 reads as a sentence about cost, matched as whole words, ignoring case.</summary>
    public static readonly IReadOnlyList<string> CostWords = new[] { "cost", "costs", "cheaper", "expensive", "spend", "price", "priced", "tokens" };

    /// <summary>Words that keep a sentence about quality for rule 16 whatever speed or cost word it holds.</summary>
    public static readonly IReadOnlyList<string> QualityWords = new[] { "score", "scored", "index", "intelligence", "accuracy", "quality", "completeness" };

    private static readonly Regex SpeedWordRegex = WordListRegex(SpeedWords);
    private static readonly Regex CostWordRegex = WordListRegex(CostWords);
    private static readonly Regex QualityWordRegex = WordListRegex(QualityWords);

    /// <summary>The fixed interval-level phrase rule 3 masks before its digit search: <c>95% interval</c> and its variants.</summary>
    private static readonly Regex IntervalLevelRegex = new(
        @"\b95\s?%\s?(?:bootstrap\s+|confidence\s+|paired\s+)?intervals?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// What rule 16 accepts, for a peer whose <c>peer.X.pairedExcludesZero</c> is true, as stating the
    /// paired result: "paired" and "excludes zero" (or "exclude", "excluding") in one sentence.
    /// </summary>
    private static readonly Regex PairedWordRegex = new(
        @"(?<![\p{L}\p{N}])paired(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExcludesZeroRegex = new(
        @"(?<![\p{L}\p{N}])exclud(?:es|e|ed|ing)\s+zero(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Rule 19's pattern over <see cref="NegationWords"/>.</summary>
    private static readonly Regex NegationRegex = new(
        @"^(?:no|none|never|without|zero)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Rule 19's words: a token placeholder, or a run of letters, digits and apostrophes.</summary>
    private static readonly Regex NegationWordRegex = new(@"\u0001\d+\u0002|[\p{L}\p{N}'’]+", RegexOptions.Compiled);

    // A hyphenated hype word matches only as a whole: "cutting-edge", never "edge".
    private static readonly Regex HypeWordRegex = new(
        @"(?<![\p{L}\p{N}-])(?:" + string.Join("|", HypeWords.Select(Regex.Escape)) + @")(?![\p{L}\p{N}-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A sentence ends at <c>.</c>, <c>!</c> or <c>?</c> followed by whitespace, or at a line break.</summary>
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.!?])\s+|\n+", RegexOptions.Compiled);

    /// <summary>The placeholder rule 16 puts in place of a token before splitting sentences, so a fact key's dots never end one.</summary>
    private static readonly Regex TokenPlaceholderRegex = new("\u0001(\\d+)\u0002", RegexOptions.Compiled);

    private static readonly Regex ChatChangeRegex = PhraseRegex(ChatChangeWords);
    private static readonly Regex ChatIntentMechanismRegex = PhraseRegex(ChatIntentWords.Concat(ChatMechanismWords));
    private static readonly Regex ChatCausalRegex = PhraseRegex(ChatCausalConnectives);
    private static readonly Regex ChatPublicClaimRegex = PhraseRegex(ChatPublicClaimWords, @"(?<!(?<![\p{L}\p{N}])(?:not|never)\s+(?:yet\s+)?)");
    private static readonly Regex ChatAllHoursRegex = PhraseRegex(ChatAllHoursWords);
    private static readonly Regex ChatModelServingRegex = PhraseRegex(ChatModelServingTerms);
    private static readonly Regex ChatMonitoringRegex = PhraseRegex(ChatMonitoringWords);

    /// <summary>C8: a run of twelve or more hex digits, as a hash or a revision prints.</summary>
    private static readonly Regex HexRunRegex = new("[0-9a-fA-F]{12,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// C9: <c>, respectively</c>; the operands are read back from the text before it, the right one after the
    /// last <see cref="AndWordRegex"/> and the left one, word for word, before it.
    /// </summary>
    private static readonly Regex RespectivelyRegex = new(
        @",?\s+respectively(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>C9: <c>and</c> as a whole word, ignoring case.</summary>
    private static readonly Regex AndWordRegex = new(
        @"(?<![\p{L}\p{N}])and(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>C9: a full stop followed by a lower-case <c>and</c>, except after a common abbreviation.</summary>
    private static readonly Regex SplicedSentenceRegex = new(
        @"(?<!(?<![\p{L}])(?:e\.g|i\.e|etc|vs|cf|approx|incl|resp))\.\s+and\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Rule 31a: a full stop followed by a lower-case letter, except after a common abbreviation.</summary>
    private static readonly Regex TokenShapeSplicedRegex = new(
        @"(?<!(?<![\p{L}])(?:e\.g|i\.e|etc|vs|cf|approx|incl|resp))\.\s+\p{Ll}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Rule 31c: a clause-valued display, <c>Model A took …</c>, with its verb captured.</summary>
    private static readonly Regex ClauseValuedRegex = new(@"^Model ([A-Z]+) (\p{L}+) ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Rule 31d: a count-valued display, <c>3 of 36 questions</c>.</summary>
    private static readonly Regex CountValuedRegex = new(@"^\d[\d,]* of \d[\d,]*(?:\s|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A model letter as rule 31c reads the word after <c>Model</c>.</summary>
    private static readonly Regex ModelLetterRegex = new(@"^[A-Z]{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>C8: every <see cref="global::Overseer.Services.ChatConsistency.OverseerEventKinds"/> identifier, as a whole word.</summary>
    private static readonly Regex EventKindRegex = new(
        @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", global::Overseer.Services.ChatConsistency.OverseerEventKinds.All.Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whole words and phrases of <paramref name="phrases"/>, ignoring case, longest first; a space or
    /// hyphen inside a phrase matches any run of whitespace and hyphens, and a match never touches a
    /// letter, digit or hyphen on either side. <paramref name="lookbehind"/>, when given, guards the start.
    /// </summary>
    private static Regex PhraseRegex(IEnumerable<string> phrases, string? lookbehind = null)
    {
        var alternatives = phrases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => string.Join(@"[\s-]+", Regex.Split(p.Trim(), @"[\s-]+").Select(Regex.Escape)));
        return new Regex(
            @"(?<![\p{L}\p{N}-])" + (lookbehind ?? string.Empty) + "(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Whole words of <paramref name="words"/>, ignoring case.</summary>
    private static Regex WordListRegex(IEnumerable<string> words)
        => new(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", words.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private enum ItemKind
    {
        Strength,
        Weakness,

        /// <summary>One point about a model in a comparison-scope document's <c>models</c> list.</summary>
        ModelPoint,

        /// <summary>One grader-recorded observation of a Root Cause Analysis.</summary>
        Observation,
    }

    /// <summary>
    /// The word cap of a Root Cause Analysis slot, or null when it has none; <paramref name="completion"/>
    /// gives the lower evidence cap of a run- or battery-completion document.
    /// </summary>
    public static int? RootCauseSlotMaxWords(string slot, bool completion) => slot switch
    {
        BenchmarkReportSlots.AnswerBest or BenchmarkReportSlots.AnswerWorst or BenchmarkReportSlots.AnswerSpeed or BenchmarkReportSlots.AnswerCost
            => RootCauseAnswerMaxWords,
        BenchmarkReportSlots.Scope => RootCauseScopeMaxWords,
        BenchmarkReportSlots.EvidenceIntelligence or BenchmarkReportSlots.EvidenceSpeed or BenchmarkReportSlots.EvidenceCost
            => completion ? RootCauseCompletionEvidenceMaxWords : RootCauseEvidenceMaxWords,
        BenchmarkReportSlots.HypothesesIntelligence or BenchmarkReportSlots.HypothesesSpeed or BenchmarkReportSlots.HypothesesCost
            => RootCauseHypothesesMaxWords,
        BenchmarkReportSlots.Conclusions => RootCauseConclusionsMaxWords,
        BenchmarkReportSlots.NextRuns => RootCauseNextRunsMaxWords,
        BenchmarkReportSlots.Limitations => RootCauseLimitationsMaxWords,
        _ => null
    };

    /// <summary>
    /// The word cap of a slot of the document <paramref name="sheet"/> describes, or null when it has none:
    /// a Root Cause Analysis' evidence slots are capped lower in a run- or battery-completion document.
    /// </summary>
    public static int? SlotMaxWords(BenchmarkReportAudience audience, string slot, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (audience == BenchmarkReportAudience.RootCauseAnalysis) return RootCauseSlotMaxWords(slot, RootCauseClaimSupport.IsCompletion(sheet));
        return SlotMaxWords(audience, slot, sheet.IsChatConsistency ? BenchmarkReportScope.ChatConsistency
            : sheet.IsComparison ? BenchmarkReportScope.Comparison
            : BenchmarkReportScope.Model);
    }

    /// <summary>
    /// The word cap of a slot of a per-model or comparison-scope document, or null when it has none; a Root
    /// Cause Analysis' evidence slots at their general cap (see <see cref="SlotMaxWords(BenchmarkReportAudience, string, BenchmarkReportFactSheet)"/>).
    /// </summary>
    public static int? SlotMaxWords(BenchmarkReportAudience audience, string slot, bool comparisonScope) => audience == BenchmarkReportAudience.RootCauseAnalysis
        ? RootCauseSlotMaxWords(slot, completion: false)
        : comparisonScope
        ? slot switch
        {
            BenchmarkReportSlots.Overview => OverviewMaxWords,
            BenchmarkReportSlots.WhichModel => WhichModelMaxWords,
            BenchmarkReportSlots.TradeOffs => TradeOffsMaxWords,
            BenchmarkReportSlots.Reliability => ReliabilityMaxWords,
            BenchmarkReportSlots.AgainstReference => ExecutiveAgainstReferenceMaxWords,
            _ => (int?)null
        }
        : slot switch
    {
        BenchmarkReportSlots.Meaning when audience == BenchmarkReportAudience.ExecutiveSummary => MeaningMaxWords,
        BenchmarkReportSlots.Confidence when audience == BenchmarkReportAudience.ExecutiveSummary => ConfidenceMaxWords,
        BenchmarkReportSlots.Comparison when audience == BenchmarkReportAudience.ExecutiveSummary => ComparisonMaxWords,
        _ => null
    };

    /// <summary>The word cap of a slot of a document of <paramref name="scope"/>, or null when it has none.</summary>
    public static int? SlotMaxWords(BenchmarkReportAudience audience, string slot, BenchmarkReportScope scope) => scope switch
    {
        BenchmarkReportScope.ChatConsistency => ChatConsistencySlotMaxWords(audience, slot),
        BenchmarkReportScope.Comparison => SlotMaxWords(audience, slot, comparisonScope: true),
        _ => SlotMaxWords(audience, slot, comparisonScope: false)
    };

    /// <summary>
    /// The word cap of a chat consistency slot, or null when it has none: the Executive Summary's
    /// slots sixty to one hundred and twenty words, the Provider Issue Report's sixty to two hundred.
    /// </summary>
    public static int? ChatConsistencySlotMaxWords(BenchmarkReportAudience audience, string slot) => (audience, slot) switch
    {
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.AsGoodAsBefore) => 120,
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.PlayerImpact) => 90,
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.OurChanges) => 80,
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.ProviderChanges) => 80,
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.ConfidenceAndScope) => 90,
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.NextRuns) => 60,

        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.IssueSummary) => 120,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.AffectedModel) => 80,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.Timeline) => 150,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.Measurements) => 200,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.HoursObserved) => 80,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.RuledOut) => 200,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.SampleRequestIds) => 60,
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.ProviderRequest) => 100,
        _ => null
    };

    // -----------------------------------------------------------------------------------------
    // Validate
    // -----------------------------------------------------------------------------------------

    /// <summary>Every issue, none dropped. An empty list means the output is valid.</summary>
    public static IReadOnlyList<BenchmarkReportValidationNote> Validate(
        BenchmarkReportAudience audience,
        BenchmarkReportWriterOutput output,
        BenchmarkReportFactSheet sheet,
        BenchmarkReportContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(output);
        var ctx = new Context(audience, sheet, content);
        var spec = ctx.Spec;
        var notes = new List<BenchmarkReportValidationNote>();

        CheckHeadline(ctx, output.Headline, notes);

        var sections = output.Sections ?? new Dictionary<string, string>();
        foreach (string slot in ctx.RequiredSlots)
        {
            string location = SectionLocation(slot);
            if (!sections.TryGetValue(slot, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                Issue(notes, 1, location, "The required slot is missing or empty.");
                continue;
            }

            var paragraphs = SplitParagraphs(text);
            for (int p = 0; p < paragraphs.Count; p++)
            {
                notes.AddRange(WithText(ParagraphIssues(ctx, slot, paragraphs[p], ParagraphLocation(slot, p)), paragraphs[p]));
            }
            CheckChatSlot(ctx, slot, text, location, notes);

            if (ctx.SlotCap(slot) is int cap && WordCount(text) > cap)
            {
                Issue(notes, 7, location, $"{SlotName(slot)} has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {cap.ToString(CultureInfo.InvariantCulture)}.");
            }
        }
        CheckChatHoursCited(ctx, output.Headline, sections, notes);
        CheckChatMdeCited(ctx, output.Headline, sections, notes);
        CheckRootCauseConclusions(ctx, sections, notes);

        foreach (string key in ExtraSlots(ctx.RequiredSlots, sections))
        {
            Issue(notes, 1, SectionLocation(key), UnknownSlotMessage(ctx, key));
        }

        if (spec.UsesStrengthsAndWeaknesses)
        {
            CheckItems(ctx, "strengths", output.Strengths, ItemKind.Strength, spec.MaxStrengths, notes);
            CheckItems(ctx, "weaknesses", output.Weaknesses, ItemKind.Weakness, spec.MaxWeaknesses, notes);
        }
        else
        {
            if (output.Strengths is { Count: > 0 }) Issue(notes, 1, "strengths", UnusedListMessage(spec, "strengths"));
            if (output.Weaknesses is { Count: > 0 }) Issue(notes, 1, "weaknesses", UnusedListMessage(spec, "weaknesses"));
        }

        if (spec.MaxModelPoints > 0)
        {
            CheckModels(ctx, output.Models, notes);
        }
        else if (output.Models is { Count: > 0 })
        {
            Issue(notes, 1, "models", UnusedListMessage(spec, "models"));
        }

        if (spec.MaxObservations > 0)
        {
            CheckItems(ctx, "observations", output.Observations, ItemKind.Observation, spec.MaxObservations, notes);
        }
        else if (output.Observations is { Count: > 0 })
        {
            Issue(notes, 1, "observations", UnusedListMessage(spec, "observations"));
        }

        if (spec.MaxWriterHypotheses > 0)
        {
            var hypotheses = output.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>();
            for (int i = 0; i < hypotheses.Count; i++)
            {
                string location = ItemLocation("writerHypotheses", i);
                notes.AddRange(WriterHypothesisIssues(ctx, hypotheses[i], location));
                if (i >= spec.MaxWriterHypotheses)
                {
                    Issue(notes, 7, location, $"The writerHypotheses list holds at most {spec.MaxWriterHypotheses.ToString(CultureInfo.InvariantCulture)} items.");
                }
            }
        }
        else if (output.WriterHypotheses is { Count: > 0 })
        {
            Issue(notes, 1, "writerHypotheses", UnusedListMessage(spec, "writerHypotheses"));
        }

        var topics = output.QuestionTopics ?? new List<BenchmarkReportQuestionTopic>();
        var topicSeen = new HashSet<int>();
        for (int i = 0; i < topics.Count; i++)
        {
            var topic = topics[i] ?? new BenchmarkReportQuestionTopic();
            string location = $"questionTopics[{i.ToString(CultureInfo.InvariantCulture)}]";
            notes.AddRange(TopicIssues(ctx, topic, location));
            if (!topicSeen.Add(topic.Question) && ctx.Questions.Contains(topic.Question))
            {
                Issue(notes, 4, location, $"{ctx.Reference(topic.Question)} already has a topic in an earlier entry.");
            }
        }
        CheckTopicCoverage(ctx, TopicsNeeded(ctx, output), topicSeen, notes, dropped: false);

        return notes;
    }

    /// <summary>
    /// A comparison-scope <c>models</c> list: each entry names a covered model's letter once and holds
    /// at most the audience's points, each checked as an item that must cite evidence; every covered
    /// model needs an entry (rule 21, a warning).
    /// </summary>
    private static void CheckModels(Context ctx, IReadOnlyList<BenchmarkReportModelPoints>? models, List<BenchmarkReportValidationNote> notes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = models ?? Array.Empty<BenchmarkReportModelPoints>();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i] ?? new BenchmarkReportModelPoints();
            string location = ItemLocation("models", i);
            notes.AddRange(ModelEntryIssues(ctx, entry, location, seen));
            if (ctx.PeerLetters.Contains(entry.Model)) seen.Add(entry.Model);

            var points = entry.Points ?? new List<BenchmarkReportWriterItem>();
            for (int p = 0; p < points.Count; p++)
            {
                string pointLocation = location + ".points[" + p.ToString(CultureInfo.InvariantCulture) + "]";
                notes.AddRange(ItemIssues(ctx, points[p], ItemKind.ModelPoint, pointLocation));
                if (p >= ctx.Spec.MaxModelPoints)
                {
                    Issue(notes, 7, pointLocation, $"A model holds at most {ctx.Spec.MaxModelPoints.ToString(CultureInfo.InvariantCulture)} points.");
                }
            }
        }
        CheckModelCoverage(ctx, seen, notes);
    }

    /// <summary>Rule 1 on one <c>models</c> entry: a letter of the sheet, given once, with at least one point.</summary>
    private static List<BenchmarkReportValidationNote> ModelEntryIssues(
        Context ctx, BenchmarkReportModelPoints entry, string location, IReadOnlySet<string> seen)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        if (!ctx.PeerLetters.Contains(entry.Model ?? string.Empty))
        {
            Issue(notes, 1, location, $"\"model\" is \"{entry.Model}\"; it must be the letter of a model listed under MODELS: {string.Join(", ", ctx.OrderedLetters)}.");
        }
        else if (seen.Contains(entry.Model!))
        {
            Issue(notes, 1, location, $"Model {entry.Model} already has an entry earlier in the list.");
        }
        if (entry.Points == null || entry.Points.Count == 0)
        {
            Issue(notes, 1, location, "The entry has no points.");
        }
        return notes;
    }

    /// <summary>Rule 21: every covered model has its entry in the <c>models</c> list; a warning with nothing to drop.</summary>
    private static void CheckModelCoverage(Context ctx, IReadOnlySet<string> covered, List<BenchmarkReportValidationNote> notes)
    {
        var missing = ctx.OrderedLetters.Where(l => !covered.Contains(l)).ToList();
        if (missing.Count > 0)
        {
            Issue(notes, ModelCoverageRule, "models",
                $"No entry for {string.Join(", ", missing.Select(l => "{{model:" + l + "}}"))}: every model listed under MODELS needs one.");
        }
    }

    // -----------------------------------------------------------------------------------------
    // DropInvalid
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Removes every item and section paragraph with an issue from a copy of the output. The headline
    /// cannot be dropped, so an invalid one is fatal, as is a required slot left empty; when the headline
    /// is the only fatal cause, <see cref="BenchmarkReportCleanResult.HeadlineOnlyFatal"/> says so. Missing
    /// question topics are recorded but not fatal, and so are the warnings of
    /// <see cref="IsWarningRule"/>: their text is kept. A note on a paragraph or item carries that paragraph's
    /// or item's raw text in <see cref="BenchmarkReportValidationNote.Text"/>, removed or kept.
    /// </summary>
    public static BenchmarkReportCleanResult DropInvalid(
        BenchmarkReportAudience audience,
        BenchmarkReportWriterOutput output,
        BenchmarkReportFactSheet sheet,
        BenchmarkReportContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(output);
        var ctx = new Context(audience, sheet, content);
        var spec = ctx.Spec;
        var notes = new List<BenchmarkReportValidationNote>();
        string? fatal = null;
        bool headlineFatal = false;
        bool otherFatal = false;

        var copy = new BenchmarkReportWriterOutput { Headline = output.Headline ?? string.Empty };

        var headlineIssues = new List<BenchmarkReportValidationNote>();
        CheckHeadline(ctx, copy.Headline, headlineIssues);
        notes.AddRange(headlineIssues);
        var blockingHeadline = headlineIssues.FirstOrDefault(Blocks);
        if (blockingHeadline != null)
        {
            fatal ??= $"The headline is invalid and cannot be dropped: {blockingHeadline.Message}";
            headlineFatal = true;
        }

        var sections = output.Sections ?? new Dictionary<string, string>();
        foreach (string slot in ctx.RequiredSlots)
        {
            string location = SectionLocation(slot);
            if (!sections.TryGetValue(slot, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                Issue(notes, 1, location, "The required slot is missing or empty.");
                fatal ??= $"The required slot \"{slot}\" is missing or empty.";
                otherFatal = true;
                copy.Sections[slot] = string.Empty;
                continue;
            }

            var paragraphs = SplitParagraphs(text);
            var kept = new List<(int Index, string Text)>();
            for (int p = 0; p < paragraphs.Count; p++)
            {
                var issues = ParagraphIssues(ctx, slot, paragraphs[p], ParagraphLocation(slot, p));
                if (issues.Any(Blocks))
                {
                    notes.AddRange(WithText(MarkDropped(issues), paragraphs[p]));
                }
                else
                {
                    notes.AddRange(WithText(issues, paragraphs[p]));
                    kept.Add((p, paragraphs[p]));
                }
            }

            if (ctx.SlotCap(slot) is int cap)
            {
                while (kept.Count > 0 && WordCount(string.Join("\n\n", kept.Select(k => k.Text))) > cap)
                {
                    var last = kept[^1];
                    kept.RemoveAt(kept.Count - 1);
                    Dropped(notes, 7, ParagraphLocation(slot, last.Index), $"Removed to bring {LowerFirst(SlotName(slot))} within {cap.ToString(CultureInfo.InvariantCulture)} words.", last.Text);
                }
            }

            if (kept.Count == 0)
            {
                Issue(notes, 1, location, "Every paragraph of the required slot was dropped.");
                fatal ??= $"Every paragraph of the required slot \"{slot}\" was dropped.";
                otherFatal = true;
                copy.Sections[slot] = string.Empty;
            }
            else
            {
                copy.Sections[slot] = kept.Count == paragraphs.Count ? text : string.Join("\n\n", kept.Select(k => k.Text));

                // Nothing to drop for a slot-wide check: its note is recorded against the kept text.
                CheckChatSlot(ctx, slot, copy.Sections[slot], location, notes);
            }
        }
        CheckChatHoursCited(ctx, copy.Headline, copy.Sections, notes);
        CheckChatMdeCited(ctx, copy.Headline, copy.Sections, notes);
        CheckRootCauseConclusions(ctx, copy.Sections, notes);

        foreach (string key in ExtraSlots(ctx.RequiredSlots, sections))
        {
            Dropped(notes, 1, SectionLocation(key), UnknownSlotMessage(ctx, key), sections[key]);
        }

        if (spec.UsesStrengthsAndWeaknesses)
        {
            copy.Strengths = CleanItems(ctx, "strengths", output.Strengths, ItemKind.Strength, spec.MaxStrengths, notes, CloneItem);
            copy.Weaknesses = CleanItems(ctx, "weaknesses", output.Weaknesses, ItemKind.Weakness, spec.MaxWeaknesses, notes, CloneItem);
        }
        else
        {
            if (output.Strengths is { Count: > 0 }) Dropped(notes, 1, "strengths", UnusedListMessage(spec, "strengths"), ItemTexts(output.Strengths));
            if (output.Weaknesses is { Count: > 0 }) Dropped(notes, 1, "weaknesses", UnusedListMessage(spec, "weaknesses"), ItemTexts(output.Weaknesses));
        }

        if (spec.MaxModelPoints > 0)
        {
            copy.Models = CleanModels(ctx, output.Models, notes);
        }
        else if (output.Models is { Count: > 0 })
        {
            Dropped(notes, 1, "models", UnusedListMessage(spec, "models"), ItemTexts(output.Models.SelectMany(m => m?.Points ?? new List<BenchmarkReportWriterItem>())));
        }

        if (spec.MaxObservations > 0)
        {
            copy.Observations = CleanItems(ctx, "observations", output.Observations, ItemKind.Observation, spec.MaxObservations, notes, CloneItem);
        }
        else if (output.Observations is { Count: > 0 })
        {
            Dropped(notes, 1, "observations", UnusedListMessage(spec, "observations"), ItemTexts(output.Observations));
        }

        if (spec.MaxWriterHypotheses > 0)
        {
            copy.WriterHypotheses = CleanWriterHypotheses(ctx, output.WriterHypotheses, spec.MaxWriterHypotheses, notes);
        }
        else if (output.WriterHypotheses is { Count: > 0 })
        {
            Dropped(notes, 1, "writerHypotheses", UnusedListMessage(spec, "writerHypotheses"), string.Join("\n\n", output.WriterHypotheses.Select(HypothesisText)));
        }

        var topicSeen = new HashSet<int>();
        var topics = output.QuestionTopics ?? new List<BenchmarkReportQuestionTopic>();
        for (int i = 0; i < topics.Count; i++)
        {
            var topic = topics[i] ?? new BenchmarkReportQuestionTopic();
            string location = $"questionTopics[{i.ToString(CultureInfo.InvariantCulture)}]";
            var issues = TopicIssues(ctx, topic, location);
            if (!issues.Any(Blocks) && topicSeen.Contains(topic.Question))
            {
                issues.Add(Note(4, location, $"{ctx.Reference(topic.Question)} already has a topic in an earlier entry."));
            }

            if (issues.Any(Blocks))
            {
                notes.AddRange(WithText(MarkDropped(issues), topic.Topic));
                continue;
            }

            notes.AddRange(WithText(issues, topic.Topic));
            topicSeen.Add(topic.Question);
            copy.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = topic.Question, Topic = topic.Topic });
        }
        CheckTopicCoverage(ctx, TopicsNeeded(ctx, copy), topicSeen, notes, dropped: false);

        return new BenchmarkReportCleanResult
        {
            Output = copy,
            Notes = notes,
            Fatal = fatal != null,
            FatalReason = fatal,
            HeadlineOnlyFatal = headlineFatal && !otherFatal
        };
    }

    /// <summary>
    /// The headline's issues alone, as <see cref="Validate"/> and <see cref="DropInvalid"/> find them: for
    /// rescuing a document whose only fatal cause is its headline, with an earlier draft's headline or a
    /// headline-only turn. <see cref="BlocksText"/> tells the blocking ones from the warnings.
    /// </summary>
    public static IReadOnlyList<BenchmarkReportValidationNote> HeadlineIssues(
        BenchmarkReportAudience audience,
        string? headline,
        BenchmarkReportFactSheet sheet,
        BenchmarkReportContentSnapshot content)
    {
        var ctx = new Context(audience, sheet, content);
        var notes = new List<BenchmarkReportValidationNote>();
        CheckHeadline(ctx, headline, notes);
        return notes;
    }

    /// <summary>Whether the note blocks its text: every rule but the warning rules of <see cref="IsWarningRule"/>.</summary>
    public static bool BlocksText(BenchmarkReportValidationNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return Blocks(note);
    }

    // -----------------------------------------------------------------------------------------
    // Checks
    // -----------------------------------------------------------------------------------------

    private static void CheckHeadline(Context ctx, string? headline, List<BenchmarkReportValidationNote> notes)
    {
        if (string.IsNullOrWhiteSpace(headline))
        {
            Issue(notes, 1, "headline", "The headline is empty.");
            return;
        }

        CheckProse(ctx, headline, "headline", notes);
        CheckVerifierMention(headline, "headline", notes);
        CheckChatClaims(ctx, null, headline, "headline", notes);
        CheckRootCauseClaims(ctx, null, headline, "headline", notes);

        int words = WordCount(headline);
        if (words > HeadlineMaxWords)
        {
            Issue(notes, 7, "headline", $"The headline has {words.ToString(CultureInfo.InvariantCulture)} words; the limit is {HeadlineMaxWords.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    /// <summary>Every paragraph-level rule on one paragraph of <paramref name="slot"/>.</summary>
    private static List<BenchmarkReportValidationNote> ParagraphIssues(Context ctx, string slot, string paragraph, string location)
    {
        var issues = new List<BenchmarkReportValidationNote>();
        CheckProse(ctx, paragraph, location, issues, slot);
        CheckIntervalWidth(ctx, slot, paragraph, location, issues);
        CheckChatClaims(ctx, slot, paragraph, location, issues);
        CheckRootCauseClaims(ctx, slot, paragraph, location, issues);
        return issues;
    }

    /// <summary>
    /// Rules 2, 3, 4 (question references), 5 (row references), 8 to 12, 16, 17, 19, 31 and 37 on one prose
    /// string; <paramref name="slot"/> is null for the headline and an item, and lets rule 16 read a speed or
    /// cost slot's sentences on that axis.
    /// </summary>
    private static void CheckProse(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes, string? slot = null)
    {
        string stripped = TokenRegex.Replace(text, " ");

        // Rule 2: tokens.
        var badTokens = TokenRegex.Matches(text)
            .Where(m => !ctx.IsValidToken(m.Groups[1].Value))
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (badTokens.Count > 0)
        {
            Issue(notes, 2, location, ctx.Comparison
                ? $"Unknown token{Plural(badTokens.Count)} {string.Join(", ", badTokens)}: use {{{{model:X}}}} with a letter from MODELS, or {{{{key}}}} with a fact key written exactly as listed, without spaces inside the braces."
                : $"Unknown token{Plural(badTokens.Count)} {string.Join(", ", badTokens)}: use {{{{subject}}}}, {{{{peer:X}}}} with a letter from PEERS, or {{{{key}}}} with a fact key written exactly as listed, without spaces inside the braces.");
        }
        if (stripped.Contains("{{", StringComparison.Ordinal) || stripped.Contains("}}", StringComparison.Ordinal))
        {
            Issue(notes, 2, location, "Unbalanced token braces: write every token as {{...}} with nothing but the token name between the braces.");
        }

        // Rule 3: bare digits; the fixed interval-level phrase is the one digit prose may hold.
        string masked = ctx.MaskKnownNames(stripped);
        masked = IntervalLevelRegex.Replace(masked, " ");
        if (ctx.Battery) masked = BatteryRefRegex.Replace(masked, " ");
        masked = QuestionRefRegex.Replace(masked, " ");
        masked = RowRefRegex.Replace(masked, " ");
        var digit = DigitWordRegex.Match(masked);
        if (digit.Success)
        {
            Issue(notes, 3, location, ctx.Battery
                ? $"Contains the digit form \"{digit.Value}\": place figures only as {{{{key}}}} tokens, write counts as number words, and refer to questions as S<suite>-Q<n>."
                : ctx.ChatConsistency
                    ? $"Contains the digit form \"{digit.Value}\": place figures, dates, hours and run ids only as {{{{key}}}} tokens, and write counts as number words."
                    : $"Contains the digit form \"{digit.Value}\": place figures only as {{{{key}}}} tokens, write counts as number words, and refer to questions as Q<n>.");
        }

        // Rule 4: question references.
        if (ctx.Battery)
        {
            var unknownReferences = BatteryRefRegex.Matches(stripped)
                .Select(m => m.Value)
                .Where(r => !ctx.IsReference(r))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unknownReferences.Count > 0)
            {
                Issue(notes, 4, location, $"{string.Join(", ", unknownReferences)} {(unknownReferences.Count == 1 ? "is" : "are")} not a question of the battery.");
            }

            var plainReferences = QuestionRefRegex.Matches(BatteryRefRegex.Replace(stripped, " "))
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (plainReferences.Count > 0)
            {
                Issue(notes, 4, location, $"{string.Join(", ", plainReferences)} {(plainReferences.Count == 1 ? "names" : "name")} no suite: in a battery report, refer to a question as S<suite>-Q<n>, as its QUESTIONS row does.");
            }
        }
        else
        {
            var missingQuestions = QuestionRefRegex.Matches(stripped)
                .Where(m => !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || !ctx.Questions.Contains(n))
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (missingQuestions.Count > 0)
            {
                Issue(notes, 4, location, $"{string.Join(", ", missingQuestions)} {(missingQuestions.Count == 1 ? "is" : "are")} not a question of the subject's exam.");
            }
        }

        // Rule 5: row references.
        var missingRows = RowRefRegex.Matches(stripped)
            .Select(m => m.Value)
            .Where(id => !ctx.Rows.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missingRows.Count > 0)
        {
            Issue(notes, 5, location, $"{string.Join(", ", missingRows)} {(missingRows.Count == 1 ? "is" : "are")} not a finding row.");
        }

        // Rule 8: headings, tables, HTML.
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var markup = new List<string>();
        if (HeadingRegex.IsMatch(normalized) || HeadingUnderlineRegex.IsMatch(normalized)) markup.Add("a heading");
        if (TableRowRegex.IsMatch(normalized) || TableSeparatorRegex.IsMatch(normalized)) markup.Add("a table");
        if (HtmlRegex.IsMatch(normalized)) markup.Add("HTML");
        if (markup.Count > 0)
        {
            Issue(notes, 8, location, $"Contains {string.Join(" and ", markup)}: the text holds plain Markdown paragraphs without headings, tables or HTML.");
        }

        // Rule 9: disclosure.
        var shared = ctx.FindSharedRun(stripped);
        if (shared != null)
        {
            Issue(notes, 9, location, $"Shares the {ShingleLength.ToString(CultureInfo.InvariantCulture)}-word run \"{shared.Value.Run}\" with {shared.Value.Source}: describe it in your own words instead of quoting it.");
        }

        // Rule 10: peer names.
        var names = ctx.FindPeerNames(stripped);
        if (names.Count > 0)
        {
            Issue(notes, 10, location, ctx.Comparison
                ? $"Names a model or its provider ({string.Join(", ", names)}): refer to a model only as {{{{model:X}}}}."
                : $"Names another model or its provider ({string.Join(", ", names)}): refer to a peer only as {{{{peer:X}}}}.");
        }

        // Rule 37: a bare model letter in comparison-scope prose.
        if (ctx.Comparison)
        {
            var letters = BareModelLetterRegex.Matches(stripped)
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (letters.Count > 0)
            {
                Issue(notes, BareModelLetterRule, location, $"Writes \"{string.Join("\", \"", letters)}\": name each model only through its {{{{model:X}}}} token, never as Model X.");
            }
        }

        // Rule 11: significance claims.
        var claims = SignificanceRegex.Matches(stripped)
            .Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " "))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (claims.Count > 0)
        {
            Issue(notes, 11, location, ctx.ChatConsistency
                ? $"Uses \"{string.Join("\", \"", claims)}\": state what the analysis decided instead, citing the endpoint's verdict and grade tokens, and its interval where it matters."
                : $"Uses \"{string.Join("\", \"", claims)}\": the comparison runs no significance test, so say only whether the intervals overlap, or whether a paired interval excludes zero.");
        }

        // Rule 12: US English.
        var british = BritishSpellingRegex.Matches(stripped)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (british.Count > 0)
        {
            Issue(notes, UsSpellingRule, location, $"Uses the British spelling{Plural(british.Count)} \"{string.Join("\", \"", british)}\": write in US English (color, behavior, analyze, center, gray, labeled, canceled).");
        }

        // Rule 16: an unhedged ranking against a peer whose interval overlaps the subject's; on a
        // comparison-scope sheet, of one model against another whose interval overlaps it, or whose
        // paired speed or cost result is not established. One note per sentence.
        if (ctx.Comparison)
        {
            foreach (var found in UnhedgedRankings(ctx, text, slot))
            {
                Issue(notes, OverlapHedgeRule, location, RankingMessage(ctx, found));
            }
        }

        var unhedged = ctx.Comparison ? new List<string>() : UnhedgedOverlappingPeers(ctx, text, slot);
        var unpaired = unhedged.Where(l => !ctx.PairedExcludesZero(l)).ToList();
        var paired = unhedged.Where(ctx.PairedExcludesZero).ToList();
        if (unpaired.Count > 0)
        {
            string peers = string.Join(", ", unpaired.Select(l => "{{peer:" + l + "}}"));
            Issue(notes, OverlapHedgeRule, location, $"Ranks {{{{subject}}}} against {peers}, whose interval overlaps the subject's, without saying so: in the same sentence, say that the intervals overlap and that the order between them is not established, or cite the peer's interval-overlap token.");
        }
        if (paired.Count > 0)
        {
            string peers = string.Join(", ", paired.Select(l => "{{peer:" + l + "}}"));
            Issue(notes, OverlapHedgeRule, location, $"Ranks {{{{subject}}}} against {peers}, whose interval overlaps the subject's, without the paired result: in the same sentence, say that on the same questions the higher-scoring model scored higher on average and that the paired interval excludes zero, not adjusted for comparing several models.");
        }

        // Rule 31: the shape of the fact tokens; in a Root Cause Analysis, a clause-valued fact after words of the writer's own sentence too.
        if (ctx.Comparison || ctx.RootCause)
        {
            CheckTokenShape(ctx, text, location, notes);
        }
        CheckSentenceShapedTokens(ctx, text, location, notes);

        // Rule 17: hype and filler words.
        var hype = HypeWordRegex.Matches(stripped)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hype.Count > 0)
        {
            Issue(notes, HypeWordRule, location, $"Uses the hype word{Plural(hype.Count)} \"{string.Join("\", \"", hype)}\": state what the figures and findings show in plain words instead.");
        }

        // Rule 19: a negation before a token whose value starts with zero.
        var negated = NegatedZeroTokens(ctx, text);
        if (negated.Count > 0)
        {
            string found = string.Join("; ", negated.Select(n => $"\"{n.Negation}\" before {{{{{n.Key}}}}}, which reads \"{n.Display}\""));
            Issue(notes, ZeroTokenNegationRule, location, $"Puts {found}: a token whose value reads 'N of M' is a noun phrase, so a negation before it says none twice. Write '{{{{errors.critical}}}} had a critical error', and to say none occurred, 'no critical errors across all {{{{answers.scored}}}} answers'.");
        }
    }

    /// <summary>
    /// Rule 19: each negation of <see cref="NegationWords"/> that stands within
    /// <see cref="NegationWindowWords"/> words before a fact token, in the same sentence, whose
    /// available display starts with <c>0</c>, with the token's key and display. Tokens are set aside
    /// before the text is split into sentences, so the dots of a fact key never end one.
    /// </summary>
    private static List<(string Negation, string Key, string Display)> NegatedZeroTokens(Context ctx, string text)
    {
        var tokens = new List<string>();
        string masked = TokenRegex.Replace(text ?? string.Empty, m =>
        {
            tokens.Add(m.Groups[1].Value);
            return "\u0001" + (tokens.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
        });

        var found = new List<(string Negation, string Key, string Display)>();
        if (tokens.Count == 0) return found;

        foreach (string sentence in SentenceSplitRegex.Split(masked))
        {
            var words = NegationWordRegex.Matches(sentence).Select(m => m.Value).ToList();
            for (int i = 0; i < words.Count; i++)
            {
                var placeholder = TokenPlaceholderRegex.Match(words[i]);
                if (!placeholder.Success) continue;

                string key = tokens[int.Parse(placeholder.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)];
                if (ctx.ZeroDisplay(key) is not string display) continue;

                for (int j = i - 1; j >= Math.Max(0, i - NegationWindowWords); j--)
                {
                    if (!NegationRegex.IsMatch(words[j])) continue;
                    found.Add((words[j].ToLowerInvariant(), key, display));
                    break;
                }
            }
        }

        return found.Distinct().ToList();
    }

    /// <summary>
    /// Rule 16: the letters of the peers a sentence ranks <c>{{subject}}</c> against with a word of
    /// <see cref="ComparativeWords"/> while <c>peer.X.intervalOverlap</c> is true, and the sentence
    /// says neither <c>overlap</c> nor <c>not established</c> and cites no <c>peer.X.intervalOverlap</c>. A
    /// peer whose <c>peer.X.pairedExcludesZero</c> is true is also hedged by a sentence saying that the
    /// paired interval excludes zero, or citing that fact as a token. A speed or cost sentence
    /// (<see cref="RankingAxes"/>) is left alone: a peer has no paired speed or cost result. Tokens are set
    /// aside before the text is split into sentences, so the dots of a fact key never end one.
    /// </summary>
    private static List<string> UnhedgedOverlappingPeers(Context ctx, string text, string? slot)
    {
        var tokens = new List<string>();
        string masked = TokenRegex.Replace(text ?? string.Empty, m =>
        {
            tokens.Add(m.Groups[1].Value);
            return "\u0001" + (tokens.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
        });
        if (tokens.Count == 0) return new List<string>();

        var letters = new List<string>();
        foreach (string sentence in SentenceSplitRegex.Split(masked))
        {
            var inSentence = TokenPlaceholderRegex.Matches(sentence)
                .Select(m => tokens[int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)])
                .ToList();
            if (!inSentence.Contains("subject", StringComparer.Ordinal)) continue;

            string plain = TokenPlaceholderRegex.Replace(sentence, " ");
            if (!ComparativeRegex.IsMatch(plain) || OverlapHedgeRegex.IsMatch(plain)) continue;
            if (!RankingAxes(slot, plain).Contains(RootCauseAxes.Intelligence, StringComparer.Ordinal)) continue;

            bool statesPaired = PairedWordRegex.IsMatch(plain) && ExcludesZeroRegex.IsMatch(plain);
            letters.AddRange(inSentence
                .Where(t => t.StartsWith("peer:", StringComparison.Ordinal))
                .Select(t => t["peer:".Length..])
                .Where(ctx.OverlapsSubject)
                .Where(l => !inSentence.Contains(BenchmarkReportFacts.PeerPrefix(l) + "intervalOverlap", StringComparer.Ordinal))
                .Where(l => !ctx.PairedExcludesZero(l)
                            || !(statesPaired || inSentence.Contains(BenchmarkReportFacts.PeerPrefix(l) + "pairedExcludesZero", StringComparer.Ordinal))));
        }

        return letters.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The axes rule 16 reads a ranking sentence on: speed or cost by its slot (an evidence, hypotheses or
    /// answer slot of that axis); otherwise speed and cost by their words (<see cref="SpeedWords"/>,
    /// <see cref="CostWords"/>) when the sentence holds no <see cref="QualityWords"/> word; and the quality
    /// axis, <see cref="RootCauseAxes.Intelligence"/>, for every other sentence.
    /// </summary>
    private static IReadOnlyList<string> RankingAxes(string? slot, string plain)
    {
        switch (slot)
        {
            case BenchmarkReportSlots.EvidenceSpeed or BenchmarkReportSlots.HypothesesSpeed or BenchmarkReportSlots.AnswerSpeed:
                return new[] { RootCauseAxes.Speed };
            case BenchmarkReportSlots.EvidenceCost or BenchmarkReportSlots.HypothesesCost or BenchmarkReportSlots.AnswerCost:
                return new[] { RootCauseAxes.Cost };
        }

        if (!QualityWordRegex.IsMatch(plain))
        {
            var axes = new List<string>();
            if (SpeedWordRegex.IsMatch(plain)) axes.Add(RootCauseAxes.Speed);
            if (CostWordRegex.IsMatch(plain)) axes.Add(RootCauseAxes.Cost);
            if (axes.Count > 0) return axes;
        }
        return new[] { RootCauseAxes.Intelligence };
    }

    /// <summary>One pair a sentence ranks without its caveat, earlier letter first, and on which axis.</summary>
    private readonly record struct UnhedgedRanking(string First, string Second, string Axis, bool Established);

    /// <summary>
    /// Rule 16 on a comparison-scope sheet, sentence by sentence: the pairs of models, earlier letter first,
    /// that a sentence ranks against each other with a word of <see cref="ComparativeWords"/> without the
    /// caveat its axis (<see cref="RankingAxes"/>) needs; a sentence saying <c>overlap</c> or
    /// <c>not established</c> needs none. A comparative word relates the nearest <c>{{model:X}}</c> before it
    /// and the nearest after it; when either is missing, the sentence's every pair is read.
    /// <list type="bullet">
    /// <item>Quality: a pair whose <c>pair.X.Y.intervalOverlap</c> is true. Citing that fact hedges it, as does
    /// a cited <c>model.X.quality.rank</c> of either model or <c>pair.X.Y.*.short</c> whose display carries the
    /// caveat; where a family establishes the pair's quality order, saying <c>paired</c> or citing one of
    /// the pair's own facts hedges it as well, and <c>Established</c> tells the two cases apart.</item>
    /// <item>Speed or cost: a pair with a paired result on that axis (<c>pair.X.Y.&lt;axis&gt;.reference</c>,
    /// <c>.allPairs</c> or <c>.short</c>) that is not established, unless the sentence cites one of those
    /// facts. A pair with no paired result on the axis is not checked.</item>
    /// </list>
    /// Each list holds one sentence's pairs. Tokens are set aside before the text is split into sentences.
    /// </summary>
    private static List<List<UnhedgedRanking>> UnhedgedRankings(Context ctx, string text, string? slot)
    {
        var tokens = new List<string>();
        string masked = TokenRegex.Replace(text ?? string.Empty, m =>
        {
            tokens.Add(m.Groups[1].Value);
            return "\u0001" + (tokens.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
        });
        var sentences = new List<List<UnhedgedRanking>>();
        if (tokens.Count == 0) return sentences;

        foreach (string sentence in SentenceSplitRegex.Split(masked))
        {
            var inSentence = TokenPlaceholderRegex.Matches(sentence)
                .Select(m => tokens[int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)])
                .ToList();
            var spots = TokenPlaceholderRegex.Matches(sentence)
                .Select(m => (m.Index, Token: tokens[int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)]))
                .Where(s => s.Token.StartsWith("model:", StringComparison.Ordinal) && ctx.PeerLetters.Contains(s.Token["model:".Length..]))
                .Select(s => (s.Index, Letter: s.Token["model:".Length..]))
                .ToList();
            var letters = spots.Select(s => s.Letter).Distinct(StringComparer.Ordinal).ToList();
            if (letters.Count < 2) continue;

            string plain = TokenPlaceholderRegex.Replace(sentence, " ");
            if (!ComparativeRegex.IsMatch(plain) || OverlapHedgeRegex.IsMatch(plain)) continue;

            // The pairs the comparative words relate; every pair when a word lacks a model on either side.
            var related = new List<(string, string)>();
            bool everyPair = false;
            foreach (Match comparative in ComparativeRegex.Matches(sentence))
            {
                var before = spots.Where(s => s.Index < comparative.Index).Select(s => s.Letter).LastOrDefault();
                var after = spots.Where(s => s.Index > comparative.Index).Select(s => s.Letter).FirstOrDefault();
                if (before == null || after == null)
                {
                    everyPair = true;
                    break;
                }
                if (!string.Equals(before, after, StringComparison.Ordinal)) related.Add((before, after));
            }
            if (everyPair)
            {
                related.Clear();
                for (int i = 0; i < letters.Count; i++)
                {
                    for (int j = i + 1; j < letters.Count; j++) related.Add((letters[i], letters[j]));
                }
            }

            var axes = RankingAxes(slot, plain);
            var found = new List<UnhedgedRanking>();
            foreach (var (a, b) in related)
            {
                var (first, second) = ctx.LetterIndex(a) <= ctx.LetterIndex(b) ? (a, b) : (b, a);
                string prefix = BenchmarkComparisonReportFacts.PairPrefix(first, second);
                foreach (string axis in axes)
                {
                    if (axis == RootCauseAxes.Intelligence)
                    {
                        if (!ctx.FactIsTrue(prefix + "intervalOverlap")) continue;

                        bool established = ctx.PairEstablished(prefix);
                        bool statesPaired = PairedWordRegex.IsMatch(plain)
                                            || inSentence.Any(t => t.StartsWith(prefix, StringComparison.Ordinal));
                        if (established && statesPaired) continue;
                        if (QualityHedged(ctx, inSentence, prefix, first, second)) continue;
                        found.Add(new UnhedgedRanking(first, second, axis, established));
                    }
                    else
                    {
                        if (!ctx.PairHasResult(prefix, axis) || ctx.PairAxisEstablished(prefix, axis)) continue;
                        if (inSentence.Any(t => IsAxisResultKey(t, prefix, axis))) continue;
                        found.Add(new UnhedgedRanking(first, second, axis, false));
                    }
                }
            }

            var distinct = found.Distinct().ToList();
            if (distinct.Count > 0) sentences.Add(distinct);
        }

        return sentences;
    }

    /// <summary>
    /// A cited token hedges a quality ranking of the pair: its <c>intervalOverlap</c> fact, or a
    /// <c>model.X.quality.rank</c> of either model or a <c>.short</c> fact of the pair whose display says
    /// that the intervals overlap or that the order is not established.
    /// </summary>
    private static bool QualityHedged(Context ctx, IReadOnlyList<string> inSentence, string prefix, string first, string second)
    {
        foreach (string token in inSentence)
        {
            if (string.Equals(token, prefix + "intervalOverlap", StringComparison.Ordinal)) return true;

            bool rank = string.Equals(token, BenchmarkComparisonReportFacts.ModelPrefix(first) + "quality.rank", StringComparison.Ordinal)
                        || string.Equals(token, BenchmarkComparisonReportFacts.ModelPrefix(second) + "quality.rank", StringComparison.Ordinal);
            bool shortResult = token.StartsWith(prefix, StringComparison.Ordinal) && token.EndsWith(".short", StringComparison.Ordinal);
            if ((rank || shortResult) && ctx.FactDisplay(token) is string display && OverlapHedgeRegex.IsMatch(display)) return true;
        }
        return false;
    }

    /// <summary>A pair's paired-result fact on a speed or cost axis: its family results or its short display.</summary>
    private static bool IsAxisResultKey(string key, string prefix, string axis)
        => string.Equals(key, prefix + axis + "." + BenchmarkComparisonReportFacts.ReferenceFamilyName, StringComparison.Ordinal)
           || string.Equals(key, prefix + axis + "." + BenchmarkComparisonReportFacts.AllPairsFamilyName, StringComparison.Ordinal)
           || string.Equals(key, prefix + axis + ".short", StringComparison.Ordinal);

    /// <summary>
    /// Rule 16's one note for a sentence: the models it ranks, and for each kind of shortfall what would
    /// clear it. Holds no digit, and never asks the writer to call a result established.
    /// </summary>
    private static string RankingMessage(Context ctx, IReadOnlyList<UnhedgedRanking> found)
    {
        string Who(IReadOnlyList<UnhedgedRanking> pairs)
        {
            if (pairs.Count == 1) return $"{{{{model:{pairs[0].First}}}}} against {{{{model:{pairs[0].Second}}}}}";
            var letters = pairs.SelectMany(p => new[] { p.First, p.Second })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ctx.LetterIndex)
                .Select(l => "{{model:" + l + "}}")
                .ToList();
            return BenchmarkReportFormat.LetterList(letters) + " against each other";
        }

        var parts = new List<string>();
        var open = found.Where(f => f.Axis == RootCauseAxes.Intelligence && !f.Established).ToList();
        var paired = found.Where(f => f.Axis == RootCauseAxes.Intelligence && f.Established).ToList();
        if (open.Count > 0)
        {
            parts.Add($"Ranks {Who(open)}, whose intervals overlap, without saying so: in the same sentence, say that the intervals overlap and that the order between them is not established, or cite the pair's interval-overlap token.");
        }
        if (paired.Count > 0)
        {
            parts.Add($"Ranks {Who(paired)}, whose intervals overlap, without the paired result: in the same sentence, cite the pair's paired-result token, or say that the intervals overlap.");
        }
        foreach (string axis in new[] { RootCauseAxes.Speed, RootCauseAxes.Cost })
        {
            var onAxis = found.Where(f => f.Axis == axis).ToList();
            if (onAxis.Count == 0) continue;
            parts.Add($"Ranks {Who(onAxis)} on {axis}, though the paired {axis} result is not established: in the same sentence, say that the order is not established, or cite the pair's {axis} paired-result token.");
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Rule 13: the Executive Summary's confidence slot (on a comparison-scope sheet, its reliability
    /// slot) does not call the quality interval narrow, wide, tight or broad. Other slots and audiences
    /// are not checked.
    /// </summary>
    private static void CheckIntervalWidth(
        Context ctx, string slot, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        string checkedSlot = ctx.Comparison ? BenchmarkReportSlots.Reliability : BenchmarkReportSlots.Confidence;
        if (ctx.Spec.Audience != BenchmarkReportAudience.ExecutiveSummary
            || !string.Equals(slot, checkedSlot, StringComparison.Ordinal))
        {
            return;
        }

        var words = IntervalWidthRegex.Matches(TokenRegex.Replace(text ?? string.Empty, " "))
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count > 0)
        {
            Issue(notes, IntervalWidthRule, location, $"Calls the interval \"{string.Join("\", \"", words)}\": do not describe its width; the sentence appended after this paragraph states the interval and its span.");
        }
    }

    /// <summary>Rule 14: the text does not mention the claim verifier.</summary>
    private static void CheckVerifierMention(string? text, string location, List<BenchmarkReportValidationNote> notes)
    {
        var match = VerifierRegex.Match(TokenRegex.Replace(text ?? string.Empty, " "));
        if (match.Success)
        {
            Issue(notes, VerifierInSummaryRule, location, $"Mentions the \"{match.Value}\": a claim-verifier ruling is an advisory judgment by an AI model that is sometimes wrong. Leave refuted claims out of the headline, and state them among the weaknesses, attributed to the claim verifier.");
        }
    }

    /// <summary>
    /// Rules C1, C2, C3, C4, the wording half of C6 and, in a Provider Issue Report, the claim half of
    /// C7, sentence by sentence, then C8, on one prose string of a chat consistency sheet; every other
    /// sheet is not checked. <paramref name="slot"/> is null for the headline. Each rule gives one note
    /// per string, naming every offending phrase.
    /// </summary>
    private static void CheckChatClaims(
        Context ctx, string? slot, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.ChatConsistency || string.IsNullOrWhiteSpace(text)) return;

        var claims = ctx.Claims;
        bool providerReport = ctx.Spec.Audience == BenchmarkReportAudience.ProviderIssueReport
                              && (slot == null || !ChatIdentifyingSlots.Contains(slot, StringComparer.Ordinal));
        var change = new List<string>();
        var intent = new List<string>();
        var causal = new List<string>();
        var publicClaim = new List<string>();
        var allHours = new List<string>();
        var modelServing = new List<string>();
        var monitoring = new List<string>();

        foreach (var (plain, tokens) in TokenSentences(text))
        {
            if (!tokens.Any(claims.SupportsChange)) change.AddRange(Phrases(ChatChangeRegex, plain));
            if (!tokens.Any(claims.IsProviderConfirmedCause)) intent.AddRange(Phrases(ChatIntentMechanismRegex, plain));
            monitoring.AddRange(Phrases(ChatMonitoringRegex, plain));
            if (!tokens.Any(claims.IsAttribution)) causal.AddRange(Phrases(ChatCausalRegex, plain));
            if (!tokens.Any(claims.IsEstablished))
            {
                // A provider-confirmed cause is what lets a sentence say the provider confirmed it.
                bool providerConfirmed = tokens.Any(claims.IsProviderConfirmedCause);
                publicClaim.AddRange(Phrases(ChatPublicClaimRegex, plain)
                    .Where(p => !(providerConfirmed && p.Equals("confirmed", StringComparison.OrdinalIgnoreCase))));
            }
            if (!(claims.TimeOfDayAssessable && tokens.Contains(BenchmarkReportPackPrompt.ChatClaimSupport.TimeOfDayKey, StringComparer.Ordinal)))
            {
                allHours.AddRange(Phrases(ChatAllHoursRegex, plain));
            }
            if (providerReport && !tokens.Any(claims.IsProviderSideAttribution))
            {
                modelServing.AddRange(Phrases(ChatModelServingRegex, plain));
            }
        }

        string Quoted(List<string> phrases) => "\"" + string.Join("\", \"", phrases.Distinct(StringComparer.OrdinalIgnoreCase)) + "\"";

        if (change.Count > 0)
        {
            Issue(notes, ChatChangeClaimRule, location, $"C1: Uses {Quoted(change)} without a result that shows a change: in the same sentence, cite an endpoint's verdict or estimate token, or an attribution token, whose verdict is not inconclusive (CLAIM SUPPORT lists them), or leave the change word out.");
        }
        if (intent.Count > 0)
        {
            Issue(notes, ChatIntentMechanismRule, location, $"C2: Uses {Quoted(intent)}: the analysis measures what changed, never why or how anyone changed it. Describe the measured change instead; a mechanism may be named only in a sentence that cites an annotation.<n> token whose kind is ProviderConfirmedCause.");
        }
        if (monitoring.Count > 0)
        {
            Issue(notes, ChatIntentMechanismRule, location, $"C2: Uses {Quoted(monitoring)}: GnollBench is not a monitoring service. Its runs are made by hand, so say that the runs check or measure the chat, and say which runs to make.");
        }
        if (causal.Count > 0)
        {
            Issue(notes, ChatCausalClaimRule, location, $"C3: Uses the causal connective {Quoted(causal)} without an attribution token in the same sentence: state a cause only as the analysis attributes it, citing its attribution.<n> token, or write two sentences instead.");
        }
        if (publicClaim.Count > 0)
        {
            Issue(notes, ChatPublicClaimRule, location, $"C4: Uses {Quoted(publicClaim)} without an Established grade in the same sentence: cite an endpoint.<P>.grade or attribution.<n>.grade token whose grade is Established, or state the grade the result has (Indicated, or not established).");
        }
        if (allHours.Count > 0)
        {
            Issue(notes, ChatHoursRule, location, claims.TimeOfDayAssessable
                ? $"C6: Uses {Quoted(allHours)} without {{{{{BenchmarkReportPackPrompt.ChatClaimSupport.TimeOfDayKey}}}}} in the same sentence: cite it there, or say that the result holds for {{{{{BenchmarkReportPackPrompt.ChatClaimSupport.HoursKey}}}}}."
                : claims.HoursAvailable
                    ? $"C6: Uses {Quoted(allHours)}, but the analysis cannot assess time of day: say that the result holds for {{{{{BenchmarkReportPackPrompt.ChatClaimSupport.HoursKey}}}}} only."
                    : $"C6: Uses {Quoted(allHours)}, but the periods ran at different hours: say so, citing {{{{period.baseline.hours}}}} and {{{{period.comparison.hours}}}}.");
        }
        if (modelServing.Count > 0)
        {
            Issue(notes, ChatProviderReportRule, location, $"C7: Makes a claim about the model or its serving ({Quoted(modelServing)}) without a provider-side attribution in the same sentence: cite an attribution.<n> token whose side is the provider, or describe what was measured of the Overseer chat instead.");
        }

        CheckChatReadable(ctx, text, location, notes);
        CheckChatRepetition(ctx, text, location, notes);
    }

    /// <summary>
    /// Rule C8 on one prose string of a chat consistency sheet: no hex run of twelve or more digits, no
    /// JSON, no <see cref="global::Overseer.Services.ChatConsistency.OverseerEventKinds"/> identifier, and no
    /// token of a fact kept from the writer; one note naming every offending token.
    /// </summary>
    private static void CheckChatReadable(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        string plain = TokenRegex.Replace(text, " ");
        var found = new List<string>();
        found.AddRange(TokenRegex.Matches(text)
            .Where(m => ctx.ChatWriterHidden(m.Groups[1].Value.Trim()))
            .Select(m => m.Value));
        found.AddRange(HexRunRegex.Matches(plain).Select(m => m.Value));
        if (plain.Contains("{\"", StringComparison.Ordinal)) found.Add("{\"");
        found.AddRange(EventKindRegex.Matches(plain).Select(m => m.Value));
        if (found.Count == 0) return;

        Issue(notes, ChatReadableTextRule, location, $"C8: Contains \"{string.Join("\", \"", found.Distinct(StringComparer.Ordinal))}\": "
            + "write for a reader, without hashes, hex revisions, JSON or internal field names. Describe the change in plain words, "
            + "such as a new harness version or an updated wiki, and cite the readable fact tokens, such as events.<n>.change.");
    }

    /// <summary>
    /// Rule C9, a warning, on one prose string of a chat consistency sheet, read with each fact token replaced by
    /// its value as the document prints it: an <c>A and A, respectively</c> whose operands are the same words
    /// (ignoring case), and a full stop followed by a lower-case <c>and</c> (a sentence-valued fact spliced into a
    /// sentence) outside the abbreviations <c>e.g.</c>, <c>i.e.</c> and the like; one note for each.
    /// </summary>
    private static void CheckChatRepetition(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        string resolved = TokenRegex.Replace(text, m => ctx.FactDisplay(m.Groups[1].Value.Trim()) ?? m.Value);

        var repeated = new List<string>();
        foreach (Match respectively in RespectivelyRegex.Matches(resolved))
        {
            string head = resolved[..respectively.Index];
            var and = AndWordRegex.Matches(head).LastOrDefault();
            if (and == null) continue;

            var right = OperandWords(head[(and.Index + and.Length)..]);
            var before = OperandWords(head[..and.Index]);
            if (right.Count == 0 || before.Count < right.Count) continue;

            var left = before.Skip(before.Count - right.Count).ToList();
            if (left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase))
            {
                repeated.Add(string.Join(" ", left) + " and " + string.Join(" ", right) + ", respectively");
            }
        }
        if (repeated.Count > 0)
        {
            Issue(notes, ChatRepetitionRule, location, $"C9: Repeats one value as two (\"{string.Join("\", \"", repeated.Distinct(StringComparer.Ordinal))}\"): "
                + "when both periods or both endpoints share a value or a verdict, name it once, as in \"inconclusive for both\", "
                + "or cite the fact FACTS gives for both periods.");
        }

        var spliced = SplicedSentenceRegex.Matches(resolved).Select(m => Excerpt(resolved, m)).ToList();
        if (spliced.Count > 0)
        {
            Issue(notes, ChatRepetitionRule, location, $"C9: Continues a sentence after a full stop (\"{string.Join("\", \"", spliced.Distinct(StringComparer.Ordinal))}\"): "
                + "write a fact whose value is a sentence as a sentence of its own, or quote it without its final full stop.");
        }
    }

    /// <summary>An operand's words, split at whitespace, each without the punctuation around it.</summary>
    private static List<string> OperandWords(string text)
        => Regex.Split(text.Trim(), @"\s+")
            .Select(w => w.Trim(',', ';', ':', '(', ')', '"', '“', '”', '*', '_'))
            .Where(w => w.Length > 0)
            .ToList();

    /// <summary>The match with a few words around it, cut with an ellipsis.</summary>
    private static string Excerpt(string text, Match match)
    {
        int start = Math.Max(0, match.Index - 30);
        int end = Math.Min(text.Length, match.Index + match.Length + 20);
        return (start > 0 ? "…" : string.Empty) + text[start..end].Trim() + (end < text.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// The slot-wide chat consistency checks: rule C5 (a warning) on every slot, and in a Provider
    /// Issue Report the half of C7 that wants every Overseer event cited under <c>ruledOut</c>.
    /// </summary>
    private static void CheckChatSlot(
        Context ctx, string slot, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.ChatConsistency || string.IsNullOrWhiteSpace(text)) return;

        var cited = TokenRegex.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        if (ctx.Spec.Audience == BenchmarkReportAudience.ProviderIssueReport
            && string.Equals(slot, BenchmarkReportSlots.RuledOut, StringComparison.Ordinal))
        {
            var missingEvents = ctx.Claims.EventNumbers
                .Where(n => !cited.Any(t => t.StartsWith(BenchmarkReportPackPrompt.ChatClaimSupport.EventPrefix(n), StringComparison.Ordinal)))
                .ToList();
            if (missingEvents.Count > 0)
            {
                string events = string.Join(", ", missingEvents.Select(n => BenchmarkReportPackPrompt.ChatClaimSupport.EventPrefix(n) + "*"));
                Issue(notes, ChatProviderReportRule, location, $"C7: Does not cite the Overseer event{Plural(missingEvents.Count)} {events}: list every Overseer event of the period under {SlotName(slot)}, each with one of its events.<n> tokens, so the provider sees what we ruled out on our side.");
            }
        }
    }

    /// <summary>
    /// The half of rule C6 that wants every chat consistency document to cite the hours its result covers,
    /// where the periods share any (<see cref="BenchmarkReportPackPrompt.ChatClaimSupport.HoursAvailable"/>).
    /// </summary>
    private static void CheckChatHoursCited(
        Context ctx, string? headline, IReadOnlyDictionary<string, string> sections, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.ChatConsistency || !ctx.Claims.HoursAvailable) return;

        string hours = "{{" + BenchmarkReportPackPrompt.ChatClaimSupport.HoursKey + "}}";
        bool cited = (headline ?? string.Empty).Contains(hours, StringComparison.Ordinal)
                     || ctx.RequiredSlots.Any(s => sections.TryGetValue(s, out string? text) && (text ?? string.Empty).Contains(hours, StringComparison.Ordinal));
        if (!cited)
        {
            Issue(notes, ChatHoursRule, "sections", $"C6: The document never cites {hours}: every chat consistency result holds for the hours the two periods share, so state them at least once.");
        }
    }

    /// <summary>
    /// Rule C5, a warning, once per document: an inconclusive endpoint the document cites needs its minimum
    /// detectable effect somewhere in the document (the headline or any required slot).
    /// </summary>
    private static void CheckChatMdeCited(
        Context ctx, string? headline, IReadOnlyDictionary<string, string> sections, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.ChatConsistency) return;

        var cited = TokenRegex.Matches(headline ?? string.Empty)
            .Concat(ctx.RequiredSlots.SelectMany(s => sections.TryGetValue(s, out string? text) ? TokenRegex.Matches(text ?? string.Empty) : Enumerable.Empty<Match>()))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missingMde = ctx.Claims.InconclusiveEndpoints
            .Where(id => cited.Any(t => t.StartsWith(BenchmarkReportPackPrompt.ChatClaimSupport.EndpointPrefix(id), StringComparison.Ordinal)))
            .Where(id => !cited.Contains(BenchmarkReportPackPrompt.ChatClaimSupport.MdeKey(id)))
            .ToList();
        if (missingMde.Count > 0)
        {
            string keys = string.Join(", ", missingMde.Select(id => "{{" + BenchmarkReportPackPrompt.ChatClaimSupport.MdeKey(id) + "}}"));
            Issue(notes, ChatInconclusiveMdeRule, "sections", $"C5: Cites the inconclusive endpoint{Plural(missingMde.Count)} {string.Join(", ", missingMde)} without {(missingMde.Count == 1 ? "its" : "their")} minimum detectable effect anywhere in the document: cite {keys} once, where the endpoint is first discussed, so the reader knows how large a change the runs could have missed.");
        }
    }

    /// <summary>
    /// Rule 31, a warning, on one prose string, read with each token resolved to its display (a model token
    /// to <c>Model X</c>): a full stop followed by a lower-case letter; a sentence-valued fact (its display
    /// starts with a capital and ends with a full stop) after anything but the paragraph's start, a list
    /// marker or <c>.</c>, <c>!</c>, <c>?</c> or <c>:</c>, or before <c>)</c>; a clause-valued fact
    /// (<c>Model A took …</c>) right after a model or its own verb; and two or more <c>N of M</c> facts
    /// separated only by commas and semicolons. One note per kind found. A hypothesis' label
    /// (<see cref="RootCauseClaimSupport.IsLabel"/>) is a noun phrase, never read as either shape.
    /// </summary>
    private static void CheckTokenShape(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (string.IsNullOrWhiteSpace(text) || !TokenRegex.IsMatch(text)) return;

        var (all, spans) = ResolveTokens(ctx, text);

        var spliced = TokenShapeSplicedRegex.Matches(all).Select(m => Excerpt(all, m)).Distinct(StringComparer.Ordinal).ToList();
        if (spliced.Count > 0)
        {
            Issue(notes, ComparisonTokenShapeRule, location, $"Continues a sentence after a full stop (\"{string.Join("\", \"", spliced)}\"): "
                + "write a fact whose value is a sentence as a sentence of its own, and never continue it after its full stop.");
        }

        var misplaced = new List<string>();
        var doubled = new List<string>();
        foreach (var span in spans)
        {
            // A hypothesis' label is a noun phrase: it may stand anywhere in a sentence.
            if (RootCauseClaimSupport.IsLabel(span.Key)) continue;

            string display = span.Display.Trim();
            if (IsSentenceValuedDisplay(span.Key, display))
            {
                int after = span.End;
                while (after < all.Length && char.IsWhiteSpace(all[after])) after++;

                bool closesParenthesis = after < all.Length && all[after] == ')';
                if (!StartsSentence(all, span.Start) || closesParenthesis) misplaced.Add("{{" + span.Key + "}}");
            }

            var clause = ClauseValuedRegex.Match(display);
            if (clause.Success)
            {
                var words = all[..span.Start].TrimEnd()
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim(',', ';', ':', '(', ')', '"', '*', '_'))
                    .ToList();
                bool afterModel = words.Count >= 2 && words[^2] == "Model" && ModelLetterRegex.IsMatch(words[^1]);
                bool afterVerb = words.Count >= 1 && string.Equals(words[^1], clause.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
                if (afterModel || afterVerb) doubled.Add("{{" + span.Key + "}}");
            }
        }
        if (misplaced.Count > 0)
        {
            Issue(notes, ComparisonTokenShapeRule, location, $"Places the sentence-valued fact{Plural(misplaced.Distinct().Count())} {string.Join(", ", misplaced.Distinct(StringComparer.Ordinal))} inside a sentence or parentheses: "
                + "a fact whose value is a whole sentence stands as a sentence of its own, at most once, never inside parentheses.");
        }
        if (doubled.Count > 0)
        {
            Issue(notes, ComparisonTokenShapeRule, location, $"Writes a model or a verb before {string.Join(", ", doubled.Distinct(StringComparer.Ordinal))}, whose value already reads 'Model X <verb> …': "
                + "that fact already says who did what, so write it alone as the clause, never with a model or that verb before it.");
        }

        var lists = new List<string>();
        for (int i = 0; i + 1 < spans.Count; i++)
        {
            if (!CountValuedRegex.IsMatch(spans[i].Display.Trim())) continue;
            int j = i;
            while (j + 1 < spans.Count
                   && CountValuedRegex.IsMatch(spans[j + 1].Display.Trim())
                   && all[spans[j].End..spans[j + 1].Start].All(c => char.IsWhiteSpace(c) || c is ',' or ';'))
            {
                j++;
            }
            if (j > i)
            {
                lists.Add(string.Join(", ", spans.Skip(i).Take(j - i + 1).Select(s => "{{" + s.Key + "}}")));
                i = j;
            }
        }
        if (lists.Count > 0)
        {
            Issue(notes, ComparisonTokenShapeRule, location, $"Lists counts in a row without saying what each counts ({string.Join("; ", lists)}): "
                + "each reads 'N of M', so say what each one counts.");
        }
    }

    /// <summary>
    /// Rule 31, a warning, on one prose string of a Root Cause Analysis: a sentence-shaped fact
    /// (<see cref="RootCauseClaimSupport.IsSentenceShaped"/>), one whose display is a clause, a sentence or a list
    /// of clauses, preceded in its sentence by a word of the writer's own: it reads as a broken sentence
    /// (<c>graded under the concise style at Completeness is the lowest dimension</c>). Standing at the start of a
    /// sentence, after a list marker or after a colon it is left alone. A display that already reads as a
    /// sentence is <see cref="CheckTokenShape"/>'s. A warning, never a drop: dropping would remove whole answers.
    /// </summary>
    private static void CheckSentenceShapedTokens(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.RootCause || string.IsNullOrWhiteSpace(text) || !TokenRegex.IsMatch(text)) return;

        var (all, spans) = ResolveTokens(ctx, text);
        var misplaced = spans
            .Where(s => !IsSentenceValuedDisplay(s.Key, s.Display.Trim()))
            .Where(s => RootCauseClaimSupport.IsSentenceShaped(s.Key, s.Display))
            .Where(s => !StartsSentence(all, s.Start))
            .Select(s => "{{" + s.Key + "}}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (misplaced.Count > 0)
        {
            Issue(notes, ComparisonTokenShapeRule, location, $"Places the clause-valued fact{Plural(misplaced.Count)} {string.Join(", ", misplaced)} after words of your own sentence: "
                + "its value reads as a clause or a list of clauses, so it breaks the sentence around it. Give it a sentence of its own, or write it after a colon, "
                + "and say in your own words what it shows.");
        }
    }

    /// <summary>
    /// The text with every token resolved to its display (a model token to <c>Model X</c>), and where each display
    /// stands in it, for rule 31.
    /// </summary>
    private static (string All, List<(int Start, int End, string Key, string Display)> Spans) ResolveTokens(Context ctx, string text)
    {
        var resolved = new StringBuilder();
        var spans = new List<(int Start, int End, string Key, string Display)>();
        int last = 0;
        foreach (Match match in TokenRegex.Matches(text))
        {
            resolved.Append(text, last, match.Index - last);
            string key = match.Groups[1].Value.Trim();
            string display = key.StartsWith("model:", StringComparison.Ordinal) ? "Model " + key["model:".Length..]
                : key.StartsWith("peer:", StringComparison.Ordinal) ? "Model " + key["peer:".Length..]
                : key == "subject" ? "the subject"
                : ctx.FactDisplay(key) ?? match.Value;
            int start = resolved.Length;
            resolved.Append(display);
            spans.Add((start, resolved.Length, key, display));
            last = match.Index + match.Length;
        }
        resolved.Append(text, last, text.Length - last);
        return (resolved.ToString(), spans);
    }

    /// <summary>A fact's display reads as a whole sentence: it starts with a capital and ends with a full stop, and is no model token.</summary>
    private static bool IsSentenceValuedDisplay(string key, string display)
        => display.Length > 1 && char.IsUpper(display[0]) && display.EndsWith('.')
           && !key.StartsWith("model:", StringComparison.Ordinal) && !key.StartsWith("peer:", StringComparison.Ordinal);

    /// <summary>
    /// Nothing but whitespace stands between <paramref name="index"/> and the start of its sentence: the text's
    /// start, <c>.</c>, <c>!</c>, <c>?</c>, <c>:</c> or a list marker.
    /// </summary>
    private static bool StartsSentence(string text, int index)
    {
        int before = index - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before])) before--;
        return before < 0 || text[before] is '.' or '!' or '?' or ':' || IsListMarker(text, before);
    }

    /// <summary>The character at <paramref name="index"/> is a list marker, <c>-</c> or <c>*</c>, with only whitespace before it on its line.</summary>
    private static bool IsListMarker(string text, int index)
    {
        if (text[index] is not ('-' or '*')) return false;
        for (int i = index - 1; i >= 0 && text[i] != '\n'; i--)
        {
            if (!char.IsWhiteSpace(text[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Rules 32, 33 and 34 on one prose string of a Root Cause Analysis, sentence by sentence; every other
    /// document is not checked. <paramref name="slot"/> is null for the headline and for an item;
    /// <paramref name="observation"/> marks an observation, which rule 32 reads as an observation slot.
    /// </summary>
    private static void CheckRootCauseClaims(
        Context ctx, string? slot, string text, string location, List<BenchmarkReportValidationNote> notes, bool observation = false)
    {
        if (!ctx.RootCause || string.IsNullOrWhiteSpace(text)) return;

        var claims = ctx.RootCauseClaims;
        bool observing = observation || (slot != null && RootCauseClaimSupport.ObservationSlots.Contains(slot, StringComparer.Ordinal));
        bool inferring = slot != null && RootCauseClaimSupport.InferenceSlots.Contains(slot, StringComparer.Ordinal);
        var observed = new List<string>();
        var unsupported = new List<string>();
        var unhedged = new List<string>();
        var certain = new List<string>();

        foreach (var (plain, tokens) in TokenSentences(text))
        {
            if (observing)
            {
                observed.AddRange(RootCauseClaimSupport.CausalPhrases(plain));
            }
            else if (inferring)
            {
                var stated = RootCauseClaimSupport.UnnegatedCausalPhrases(plain).ToList();
                if (stated.Count > 0)
                {
                    if (!tokens.Any(t => claims.SupportsCausal(t, slot)))
                    {
                        unsupported.AddRange(stated);
                    }
                    else if (claims.NeedsHedge(tokens, slot) && !RootCauseClaimSupport.HedgePhrases(plain).Any())
                    {
                        unhedged.AddRange(stated);
                    }
                }
                if (!tokens.Any(t => claims.SupportsNegatedCausal(t, slot)))
                {
                    unsupported.AddRange(RootCauseClaimSupport.NegatedCausalPhrases(plain));
                }
            }
            if (!tokens.Any(claims.SupportsCertainty))
            {
                certain.AddRange(RootCauseClaimSupport.CertaintyPhrases(plain));
            }
        }

        string Quoted(List<string> phrases) => "\"" + string.Join("\", \"", phrases.Distinct(StringComparer.OrdinalIgnoreCase)) + "\"";

        if (observed.Count > 0)
        {
            Issue(notes, RootCauseEvidenceCausalRule, location, $"Uses the causal wording {Quoted(observed)} where the document states only what was measured or recorded: "
                + "describe what the data shows, and leave why to the hypotheses and the conclusions.");
        }
        if (unsupported.Count > 0)
        {
            Issue(notes, RootCauseCausalClaimRule, location, $"Uses the causal wording {Quoted(unsupported)} without a hypothesis graded Low or higher in the same sentence: "
                + "cite one of its tokens, such as its .ref (CLAIM SUPPORT lists the hypotheses)"
                + (string.Equals(slot, BenchmarkReportSlots.NextRuns, StringComparison.Ordinal) ? ", or the rca.run token of the run it describes" : string.Empty)
                + ". A Not supported hypothesis may be cited only to rule a cause out (not due to …).");
        }
        if (unhedged.Count > 0)
        {
            Issue(notes, RootCauseCausalClaimRule, location, $"Uses the causal wording {Quoted(unhedged)} on a hypothesis graded Low only: "
                + "a Low hypothesis is a lead, so say so (may, possibly, consistent with …).");
        }
        if (certain.Count > 0)
        {
            Issue(notes, RootCauseCertaintyRule, location, claims.CertaintySupport().Count == 0
                ? $"Uses {Quoted(certain)}, but no hypothesis is graded High and no axis has an established root cause: say what the grades and statuses say (probable, Moderate, Low, not established)."
                : $"Uses {Quoted(certain)} without a High hypothesis token or an established axis status token in the same sentence: cite one (CLAIM SUPPORT lists them), or say what the grade says.");
        }
    }

    /// <summary>
    /// Rule 36, a warning: the conclusions cite every axis' <c>rca.axis.&lt;axis&gt;.status</c>.
    /// </summary>
    private static void CheckRootCauseConclusions(Context ctx, IReadOnlyDictionary<string, string> sections, List<BenchmarkReportValidationNote> notes)
    {
        if (!ctx.RootCause) return;

        if (sections.TryGetValue(BenchmarkReportSlots.Conclusions, out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            var cited = CitedTokens(text);
            var missing = ctx.RootCauseClaims.Axes.Where(a => !cited.Contains(RootCauseClaimSupport.StatusKey(a))).ToList();
            if (missing.Count > 0)
            {
                Issue(notes, RootCauseAxisStatusRule, SectionLocation(BenchmarkReportSlots.Conclusions),
                    $"Does not cite {string.Join(", ", missing.Select(a => "{{" + RootCauseClaimSupport.StatusKey(a) + "}}"))}: "
                    + "the conclusions state every axis' status, citing its token, and then its root cause or why it is not established.");
            }
        }
    }

    /// <summary>The fact keys a text cites, trimmed.</summary>
    private static HashSet<string> CitedTokens(string text)
        => TokenRegex.Matches(text).Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Rule 35 and the prose rules on one writer hypothesis of a Root Cause Analysis: an axis, a statement,
    /// evidence for, a refuted-if statement and a known locus; confidence Low (an empty confidence reads as
    /// Low); only models of the document and evidence that exists; each text within
    /// <see cref="WriterHypothesisPartMaxWords"/> words, and through rules 2 to 12, 17, 19, 31, 34 and 37.
    /// </summary>
    private static List<BenchmarkReportValidationNote> WriterHypothesisIssues(Context ctx, BenchmarkReportWriterHypothesis? hypothesis, string location)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        var h = hypothesis ?? new BenchmarkReportWriterHypothesis();
        var supports = (h.For ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var against = (h.Against ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        var missing = new List<string>();
        if (!RootCauseAxes.All.Contains((h.Axis ?? string.Empty).Trim().ToLowerInvariant(), StringComparer.Ordinal)) missing.Add("an axis (intelligence, speed or cost)");
        if (string.IsNullOrWhiteSpace(h.Statement)) missing.Add("a statement");
        if (supports.Count == 0) missing.Add("evidence for it");
        if (string.IsNullOrWhiteSpace(h.RefutedIf)) missing.Add("a refuted-if statement");
        if (!RootCauseClaimSupport.IsLocus(h.Locus)) missing.Add("a locus (" + string.Join(", ", RootCauseClaimSupport.Loci) + ")");
        if (missing.Count > 0)
        {
            Issue(notes, RootCauseWriterHypothesisRule, location, $"Lacks {BenchmarkReportFormat.LetterList(missing)}: every writer hypothesis names its axis, models and locus, "
                + "states itself, gives its evidence for and against and what would refute it.");
        }

        string confidence = (h.Confidence ?? string.Empty).Trim();
        if (confidence.Length > 0 && !string.Equals(confidence, RootCauseGrades.Low, StringComparison.OrdinalIgnoreCase))
        {
            Issue(notes, RootCauseWriterHypothesisRule, location, $"Has the confidence \"{confidence}\": a writer hypothesis rests on grader comments and excerpts only, so its confidence is Low.");
        }

        var unknownModels = (h.Models ?? new List<string>()).Where(m => !ctx.ModelTokens.Contains(m ?? string.Empty)).Distinct(StringComparer.Ordinal).ToList();
        if (unknownModels.Count > 0)
        {
            Issue(notes, RootCauseWriterHypothesisRule, location, $"Names the model{Plural(unknownModels.Count)} \"{string.Join("\", \"", unknownModels)}\", which the document does not have: "
                + (ctx.Comparison ? "name a model by its letter from MODELS." : "name the subject as subject and a peer by its letter from PEERS."));
        }

        var unknownEvidence = new List<string>();
        foreach (string id in (h.Evidence ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()))
        {
            bool known = ctx.Battery
                ? ctx.IsReference(id) || ctx.FactKeys.Contains(id)
                : QuestionIdRegex.Match(id) is { Success: true } q
                    ? int.TryParse(q.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && ctx.Questions.Contains(n)
                    : ctx.FactKeys.Contains(id) || ctx.Rows.ContainsKey(id);
            if (!known) unknownEvidence.Add(id);
        }
        if (unknownEvidence.Count > 0)
        {
            Issue(notes, RootCauseWriterHypothesisRule, location, $"Cites {string.Join(", ", unknownEvidence.Distinct(StringComparer.Ordinal))}, which {(unknownEvidence.Count == 1 ? "does" : "do")} not exist in the data: "
                + "cite only fact keys and question references from the data.");
        }

        var texts = new List<(string Text, string Location)>();
        if (!string.IsNullOrWhiteSpace(h.Statement)) texts.Add((h.Statement, location + ".statement"));
        texts.AddRange(supports.Select((t, i) => (t, location + ".for[" + i.ToString(CultureInfo.InvariantCulture) + "]")));
        texts.AddRange(against.Select((t, i) => (t, location + ".against[" + i.ToString(CultureInfo.InvariantCulture) + "]")));
        if (!string.IsNullOrWhiteSpace(h.RefutedIf)) texts.Add((h.RefutedIf, location + ".refutedIf"));
        foreach (var (text, textLocation) in texts)
        {
            CheckProse(ctx, text, textLocation, notes);
            CheckRootCauseClaims(ctx, null, text, textLocation, notes);
            if (WordCount(text) > WriterHypothesisPartMaxWords)
            {
                Issue(notes, 7, textLocation, $"The text has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {WriterHypothesisPartMaxWords.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        return notes;
    }

    /// <summary>
    /// The writer hypotheses with each invalid one removed, and those past <paramref name="max"/>; each kept
    /// one copied with its axis in lower case and its confidence Low.
    /// </summary>
    private static List<BenchmarkReportWriterHypothesis> CleanWriterHypotheses(
        Context ctx, IReadOnlyList<BenchmarkReportWriterHypothesis>? hypotheses, int max, List<BenchmarkReportValidationNote> notes)
    {
        var kept = new List<BenchmarkReportWriterHypothesis>();
        if (hypotheses == null) return kept;

        for (int i = 0; i < hypotheses.Count; i++)
        {
            string location = ItemLocation("writerHypotheses", i);
            var issues = WriterHypothesisIssues(ctx, hypotheses[i], location);
            string text = HypothesisText(hypotheses[i]);
            if (issues.Any(Blocks))
            {
                notes.AddRange(WithText(MarkDropped(issues), text));
                continue;
            }
            if (kept.Count >= max)
            {
                Dropped(notes, 7, location, $"Removed: the writerHypotheses list holds at most {max.ToString(CultureInfo.InvariantCulture)} items.", text);
                continue;
            }

            notes.AddRange(WithText(issues, text));
            var h = hypotheses[i];
            kept.Add(new BenchmarkReportWriterHypothesis
            {
                Axis = (h.Axis ?? string.Empty).Trim().ToLowerInvariant(),
                Models = new List<string>(h.Models ?? new List<string>()),
                Locus = (h.Locus ?? string.Empty).Trim(),
                Statement = h.Statement,
                For = (h.For ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
                Against = (h.Against ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
                RefutedIf = h.RefutedIf,
                Confidence = RootCauseGrades.Low,
                Evidence = new List<string>(h.Evidence ?? new List<string>())
            });
        }

        return kept;
    }

    /// <summary>
    /// The questions that need a topic: in a Root Cause Analysis, each question it cites whose text the writer
    /// was given; in every other document, <see cref="Context.QuestionsNeedingTopic"/>.
    /// </summary>
    private static IReadOnlyList<int> TopicsNeeded(Context ctx, BenchmarkReportWriterOutput output)
    {
        if (!ctx.RootCause) return ctx.QuestionsNeedingTopic;
        var needing = ctx.QuestionsNeedingTopic.ToHashSet();
        return CitedQuestionNumbers(ctx, output).Where(needing.Contains).ToList();
    }

    /// <summary>
    /// The questions a writer's output cites, ascending: by reference in its headline, slots, observations and
    /// writer hypotheses (<c>Q7</c>, or a battery's <c>S2-Q7</c>), and in its items' question lists and evidence.
    /// Topics do not count.
    /// </summary>
    public static IReadOnlyList<int> CitedQuestions(BenchmarkReportWriterOutput output, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sheet);
        var references = QuestionReferences(sheet);
        var numbers = new HashSet<int>(sheet.Questions.Select(q => q.Number));
        return CitedNumbers(output, references, numbers);
    }

    /// <summary>The questions <paramref name="texts"/> refer to as the sheet numbers them, ascending: <c>Q7</c>, or on a battery sheet <c>S2-Q7</c>.</summary>
    public static IReadOnlyList<int> QuestionsReferencedIn(IEnumerable<string?> texts, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(sheet);
        var references = QuestionReferences(sheet);
        var numbers = new HashSet<int>(sheet.Questions.Select(q => q.Number));
        var found = new HashSet<int>();
        foreach (string? text in texts) AddReferenced(text, references, numbers, found);
        return found.OrderBy(n => n).ToList();
    }

    private static IReadOnlyList<int> CitedQuestionNumbers(Context ctx, BenchmarkReportWriterOutput output)
        => CitedNumbers(output, ctx.ReferenceNumbers, ctx.Questions);

    /// <summary>A battery sheet's question numbers by reference; null on a sheet whose questions are <c>Q&lt;n&gt;</c>.</summary>
    private static IReadOnlyDictionary<string, int>? QuestionReferences(BenchmarkReportFactSheet sheet)
    {
        bool battery = sheet.Battery != null || (sheet.IsComparison && sheet.Questions.Any(q => !string.IsNullOrWhiteSpace(q.Reference)));
        if (!battery) return null;
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var q in sheet.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Reference))) map.TryAdd(q.Reference!, q.Number);
        return map;
    }

    private static IReadOnlyList<int> CitedNumbers(BenchmarkReportWriterOutput output, IReadOnlyDictionary<string, int>? references, IReadOnlySet<int> numbers)
    {
        var found = new HashSet<int>();
        var texts = new List<string?> { output.Headline };
        texts.AddRange((output.Sections ?? new Dictionary<string, string>()).Values);
        foreach (var item in output.Observations ?? new List<BenchmarkReportWriterItem>())
        {
            texts.Add(item?.Text);
            texts.AddRange(item?.Evidence ?? new List<string>());
            foreach (int n in item?.Questions ?? new List<int>())
            {
                if (numbers.Contains(n)) found.Add(n);
            }
        }
        foreach (var h in output.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>())
        {
            if (h == null) continue;
            texts.Add(h.Statement);
            texts.AddRange(h.For ?? new List<string>());
            texts.AddRange(h.Against ?? new List<string>());
            texts.Add(h.RefutedIf);
            texts.AddRange(h.Evidence ?? new List<string>());
        }
        foreach (string? text in texts) AddReferenced(text, references, numbers, found);
        return found.OrderBy(n => n).ToList();
    }

    /// <summary>Adds the questions <paramref name="text"/> refers to, its tokens blanked first.</summary>
    private static void AddReferenced(string? text, IReadOnlyDictionary<string, int>? references, IReadOnlySet<int> numbers, HashSet<int> found)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string stripped = TokenRegex.Replace(text, " ");
        if (references != null)
        {
            foreach (Match match in BatteryRefRegex.Matches(stripped))
            {
                if (references.TryGetValue(match.Value, out int number)) found.Add(number);
            }
            return;
        }
        foreach (Match match in QuestionRefRegex.Matches(stripped))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && numbers.Contains(number)) found.Add(number);
        }
    }

    /// <summary>
    /// Each sentence of <paramref name="text"/>: its text with every token blanked, and the tokens it
    /// holds. Tokens are set aside before the text is split, so the dots of a fact key never end a sentence.
    /// </summary>
    private static List<(string Plain, List<string> Tokens)> TokenSentences(string text)
    {
        var tokens = new List<string>();
        string masked = TokenRegex.Replace(text ?? string.Empty, m =>
        {
            tokens.Add(m.Groups[1].Value);
            return "\u0001" + (tokens.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
        });

        return SentenceSplitRegex.Split(masked)
            .Select(sentence => (
                TokenPlaceholderRegex.Replace(sentence, " "),
                TokenPlaceholderRegex.Matches(sentence)
                    .Select(m => tokens[int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)])
                    .ToList()))
            .ToList();
    }

    /// <summary>Every match of <paramref name="regex"/>, its whitespace folded to single spaces.</summary>
    private static IEnumerable<string> Phrases(Regex regex, string text)
        => regex.Matches(text).Select(m => Regex.Replace(m.Value, @"\s+", " "));

    private static void CheckItems<T>(
        Context ctx,
        string array,
        IReadOnlyList<T>? items,
        ItemKind kind,
        int max,
        List<BenchmarkReportValidationNote> notes)
        where T : BenchmarkReportWriterItem
    {
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            string location = ItemLocation(array, i);
            notes.AddRange(ItemIssues(ctx, items[i], kind, location));
            if (i >= max)
            {
                Issue(notes, 7, location, $"The {array} list holds at most {max.ToString(CultureInfo.InvariantCulture)} items.");
            }
        }
    }

    private static List<T> CleanItems<T>(
        Context ctx,
        string array,
        IReadOnlyList<T>? items,
        ItemKind kind,
        int max,
        List<BenchmarkReportValidationNote> notes,
        Func<T, T> clone)
        where T : BenchmarkReportWriterItem
    {
        var kept = new List<T>();
        if (items == null) return kept;

        for (int i = 0; i < items.Count; i++)
        {
            string location = ItemLocation(array, i);
            var issues = ItemIssues(ctx, items[i], kind, location);
            string? text = items[i]?.Text;
            if (issues.Any(Blocks))
            {
                notes.AddRange(WithText(MarkDropped(issues), text));
                continue;
            }

            if (kept.Count >= max)
            {
                Dropped(notes, 7, location, $"Removed: the {array} list holds at most {max.ToString(CultureInfo.InvariantCulture)} items.", text);
                continue;
            }

            notes.AddRange(WithText(issues, text));
            kept.Add(clone(items[i]));
        }

        return kept;
    }

    /// <summary>
    /// The <c>models</c> list with each invalid entry and point removed, and points past the audience's
    /// cap; an entry left without points is removed. A covered model without an entry is a rule 21 note.
    /// </summary>
    private static List<BenchmarkReportModelPoints> CleanModels(
        Context ctx, IReadOnlyList<BenchmarkReportModelPoints>? models, List<BenchmarkReportValidationNote> notes)
    {
        var kept = new List<BenchmarkReportModelPoints>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = models ?? Array.Empty<BenchmarkReportModelPoints>();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i] ?? new BenchmarkReportModelPoints();
            string location = ItemLocation("models", i);
            var entryIssues = ModelEntryIssues(ctx, entry, location, seen);
            if (entryIssues.Any(Blocks))
            {
                notes.AddRange(WithText(MarkDropped(entryIssues), ItemTexts(entry.Points)));
                continue;
            }

            var points = new List<BenchmarkReportWriterItem>();
            var source = entry.Points ?? new List<BenchmarkReportWriterItem>();
            for (int p = 0; p < source.Count; p++)
            {
                string pointLocation = location + ".points[" + p.ToString(CultureInfo.InvariantCulture) + "]";
                var issues = ItemIssues(ctx, source[p], ItemKind.ModelPoint, pointLocation);
                string? text = source[p]?.Text;
                if (issues.Any(Blocks))
                {
                    notes.AddRange(WithText(MarkDropped(issues), text));
                    continue;
                }
                if (points.Count >= ctx.Spec.MaxModelPoints)
                {
                    Dropped(notes, 7, pointLocation, $"Removed: a model holds at most {ctx.Spec.MaxModelPoints.ToString(CultureInfo.InvariantCulture)} points.", text);
                    continue;
                }
                notes.AddRange(WithText(issues, text));
                points.Add(CloneItem(source[p]));
            }

            if (points.Count == 0)
            {
                Dropped(notes, 1, location, "Every point of the entry was removed.");
                continue;
            }

            seen.Add(entry.Model);
            kept.Add(new BenchmarkReportModelPoints { Model = entry.Model, Points = points });
        }

        CheckModelCoverage(ctx, seen, notes);
        return kept
            .OrderBy(m => m.Model.Length)
            .ThenBy(m => m.Model, StringComparer.Ordinal)
            .ToList();
    }

    private static List<BenchmarkReportValidationNote> ItemIssues(Context ctx, BenchmarkReportWriterItem? item, ItemKind kind, string location)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        string text = item?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            Issue(notes, 1, location, "The text is empty.");
        }
        else
        {
            CheckProse(ctx, text, location, notes);

            if (kind is ItemKind.Strength or ItemKind.Weakness
                && ctx.Spec.Audience == BenchmarkReportAudience.ExecutiveSummary
                && WordCount(text) > ExecutiveItemMaxWords)
            {
                Issue(notes, 7, location, $"The item has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {ExecutiveItemMaxWords.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (kind == ItemKind.ModelPoint && WordCount(text) > ModelPointMaxWords(ctx.Spec.Audience))
            {
                Issue(notes, 7, location, $"The point has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {ModelPointMaxWords(ctx.Spec.Audience).ToString(CultureInfo.InvariantCulture)}.");
            }

            if (kind == ItemKind.Observation)
            {
                CheckRootCauseClaims(ctx, null, text, location, notes, observation: true);
                if (WordCount(text) > RootCauseObservationMaxWords)
                {
                    Issue(notes, 7, location, $"The observation has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {RootCauseObservationMaxWords.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
        }

        var questions = item?.Questions ?? new List<int>();
        var badQuestions = questions.Where(n => !ctx.Questions.Contains(n)).Distinct().ToList();
        if (badQuestions.Count > 0)
        {
            Issue(notes, 4, location, $"Question number{Plural(badQuestions.Count)} {string.Join(", ", badQuestions.Select(n => n.ToString(CultureInfo.InvariantCulture)))} {(badQuestions.Count == 1 ? "is" : "are")} not in the subject's exam.");
        }

        var evidence = (item?.Evidence ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();
        var badQuestionIds = new List<string>();
        var unknownIds = new List<string>();
        var citedRows = new List<BenchmarkReportFindingRow>();
        foreach (string id in evidence)
        {
            var qMatch = QuestionIdRegex.Match(id);
            if (ctx.Battery && BatteryIdRegex.IsMatch(id))
            {
                if (!ctx.IsReference(id)) badQuestionIds.Add(id);
            }
            else if (qMatch.Success)
            {
                // A battery question is cited by its suite-qualified reference only.
                if (ctx.Battery
                    || !int.TryParse(qMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                    || !ctx.Questions.Contains(n))
                {
                    badQuestionIds.Add(id);
                }
            }
            else if (ctx.Rows.TryGetValue(id, out var row))
            {
                citedRows.Add(row);
            }
            else if (!ctx.FactKeys.Contains(id))
            {
                unknownIds.Add(id);
            }
        }

        if (badQuestionIds.Count > 0)
        {
            Issue(notes, 4, location, ctx.Battery
                ? $"Evidence {string.Join(", ", badQuestionIds.Distinct(StringComparer.Ordinal))} {(badQuestionIds.Count == 1 ? "refers" : "refer")} to no question of the battery: cite S<suite>-Q<n> as its QUESTIONS row shows it."
                : $"Evidence {string.Join(", ", badQuestionIds.Distinct(StringComparer.Ordinal))} {(badQuestionIds.Count == 1 ? "refers" : "refer")} to no question of the subject's exam.");
        }
        if (unknownIds.Count > 0)
        {
            Issue(notes, 5, location, ctx.Battery
                ? $"Unknown evidence id{Plural(unknownIds.Count)} {string.Join(", ", unknownIds.Distinct(StringComparer.Ordinal))}: cite a fact key or S<suite>-Q<n> from the data."
                : ctx.Comparison
                    ? $"Unknown evidence id{Plural(unknownIds.Count)} {string.Join(", ", unknownIds.Distinct(StringComparer.Ordinal))}: cite a fact key or Q<n> from the data."
                    : $"Unknown evidence id{Plural(unknownIds.Count)} {string.Join(", ", unknownIds.Distinct(StringComparer.Ordinal))}: cite a fact key, Q<n> or a finding row id from the data.");
        }

        bool requiresEvidence = kind is ItemKind.Strength or ItemKind.Weakness or ItemKind.ModelPoint or ItemKind.Observation;
        if (requiresEvidence && evidence.Count == 0)
        {
            Issue(notes, 5, location, ctx.Battery
                ? "Cites no evidence: give at least one fact key or S<suite>-Q<n>."
                : ctx.Comparison
                    ? "Cites no evidence: give at least one fact key or Q<n>."
                    : "Cites no evidence: give at least one fact key, Q<n> or finding row id.");
        }

        // An observation records what the graders recorded on a question, so it names one.
        var referenced = new HashSet<int>();
        if (kind == ItemKind.Observation) AddReferenced(text + " " + string.Join(" ", evidence), ctx.ReferenceNumbers, ctx.Questions, referenced);
        if (kind == ItemKind.Observation && !questions.Any(ctx.Questions.Contains) && referenced.Count == 0)
        {
            Issue(notes, 4, location, ctx.Battery
                ? "Cites no question: give the question it records, as S<suite>-Q<n> in \"evidence\", or its number in \"questions\"."
                : "Cites no question: give the question it records, as Q<n> in \"evidence\", or its number in \"questions\".");
        }

        if (kind is ItemKind.Strength or ItemKind.Weakness)
        {
            string opposite = kind == ItemKind.Strength ? "weakness" : "strength";
            string own = kind == ItemKind.Strength ? "strength" : "weakness";
            var contrary = citedRows
                .Where(r => !IsConflicting(r) && string.Equals(r.Kind.Trim(), opposite, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (contrary.Count > 0)
            {
                Issue(notes, 6, location, $"A {own} cites the {opposite} row{Plural(contrary.Count)} {string.Join(", ", contrary)}.");
            }

            if (citedRows.Count > 0 && citedRows.All(IsConflicting) && !text.Contains(DisagreementWord, StringComparison.OrdinalIgnoreCase))
            {
                Issue(notes, 6, location, $"Rests only on Conflicting row{Plural(citedRows.Count)} ({string.Join(", ", citedRows.Select(r => r.Id).Distinct(StringComparer.Ordinal))}) but does not say that the graders disagree.");
            }
        }

        return notes;
    }

    private static List<BenchmarkReportValidationNote> TopicIssues(Context ctx, BenchmarkReportQuestionTopic topic, string location)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        if (string.IsNullOrWhiteSpace(topic.Topic))
        {
            Issue(notes, 1, location, "The topic is empty.");
        }
        else
        {
            CheckProse(ctx, topic.Topic, location, notes);
        }

        if (!ctx.Questions.Contains(topic.Question))
        {
            Issue(notes, 4, location, $"Question {topic.Question.ToString(CultureInfo.InvariantCulture)} is not in the subject's exam.");
        }

        int words = WordCount(topic.Topic ?? string.Empty);
        if (words > TopicMaxWords)
        {
            Issue(notes, 7, location, $"The topic has {words.ToString(CultureInfo.InvariantCulture)} words; the limit is {TopicMaxWords.ToString(CultureInfo.InvariantCulture)}.");
        }

        return notes;
    }

    private static void CheckTopicCoverage(
        Context ctx, IReadOnlyList<int> needed, HashSet<int> covered, List<BenchmarkReportValidationNote> notes, bool dropped)
    {
        if (!ctx.Spec.RequiresQuestionTopics) return;

        var missing = needed.Where(n => !covered.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            string why = ctx.RootCause ? "every question the document cites, of those listed under QUESTIONS NEEDING A TOPIC, needs one."
                : ctx.Battery ? "every question given in detail needs one."
                : "every question of the exam needs one.";
            notes.Add(Note(4, "questionTopics", $"No topic for {string.Join(", ", missing.Select(ctx.Reference))}: {why}", dropped));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static bool IsConflicting(BenchmarkReportFindingRow row)
        => string.Equals(row.Status?.Trim(), "Conflicting", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtraSlots(IReadOnlyList<string> requiredSlots, Dictionary<string, string> sections)
        => sections.Keys
            .Where(k => !requiredSlots.Contains(k, StringComparer.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal);

    private static string UnknownSlotMessage(Context ctx, string key)
        => ctx.Spec.PeerOnlySlots.Contains(key, StringComparer.Ordinal)
            ? $"\"{key}\" is a slot of the {DocumentName(ctx.Spec.Audience)} with peers only; a stand-alone document has none. Its slots are {string.Join(", ", ctx.RequiredSlots)}."
            : ctx.RootCause && RootCauseClaimSupport.QuestionOf(key) != null
            ? $"\"{key}\" answers a question this data does not ask; leave it out. The document's slots are {string.Join(", ", ctx.RequiredSlots)}."
            : $"\"{key}\" is not a slot of the {DocumentName(ctx.Spec.Audience)}; its slots are {string.Join(", ", ctx.RequiredSlots)}.";

    private static string UnusedListMessage(BenchmarkReportAudienceSpec spec, string array)
        => $"The {DocumentName(spec.Audience)} does not use \"{array}\"; leave it out.";

    private static string DocumentName(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.ProviderIssueReport ? "Provider Issue Report" : BenchmarkReportRenderService.AudienceName(audience);

    private static string SlotName(string slot) => slot switch
    {
        BenchmarkReportSlots.Meaning => "\"What this means for use as a game assistant\"",
        BenchmarkReportSlots.Confidence => "\"How reliable this result is\"",
        BenchmarkReportSlots.Comparison => "\"How it compares\"",
        BenchmarkReportSlots.Overview => "\"The comparison in one paragraph\"",
        BenchmarkReportSlots.WhichModel => "\"Which model to use\"",
        BenchmarkReportSlots.TradeOffs => "\"Trade-offs\"",
        BenchmarkReportSlots.Reliability => "\"How reliable this is\"",
        BenchmarkReportSlots.AgainstReference => "\"Compared with the reference model\"",
        _ when BenchmarkReportSlots.RootCauseSlotTitles.TryGetValue(slot, out string? title) && slot != BenchmarkReportSlots.NextRuns => $"\"{title}\"",
        _ when BenchmarkReportSlots.ChatConsistencySlotTitles.TryGetValue(slot, out string? title) => $"\"{title}\"",
        _ => $"The \"{slot}\" slot"
    };

    private static string LowerFirst(string text)
        => text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>Every rule but the warning rules of <see cref="IsWarningRule"/> removes the offending item or paragraph.</summary>
    private static bool Blocks(BenchmarkReportValidationNote note) => !IsWarningRule(note.Rule);

    /// <summary>A section's text split on blank lines, each paragraph trimmed, empty ones left out.</summary>
    internal static List<string> SplitParagraphs(string text)
        => ParagraphSplitRegex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n').Trim())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

    internal static int WordCount(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string SectionLocation(string slot) => $"sections.{slot}";

    /// <summary><c>sections.&lt;slot&gt;[p&lt;n&gt;]</c>, with <c>n</c> counted from one.</summary>
    private static string ParagraphLocation(string slot, int index)
        => $"sections.{slot}[p{(index + 1).ToString(CultureInfo.InvariantCulture)}]";

    private static string ItemLocation(string array, int index)
        => $"{array}[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static string Q(int number) => "Q" + number.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static BenchmarkReportValidationNote Note(int rule, string location, string message, bool dropped = false, string? text = null)
        => new() { Rule = rule, Location = location, Message = message, Dropped = dropped, Text = CapText(text) };

    private static void Issue(List<BenchmarkReportValidationNote> notes, int rule, string location, string message)
        => notes.Add(Note(rule, location, message));

    private static void Dropped(List<BenchmarkReportValidationNote> notes, int rule, string location, string message, string? text = null)
        => notes.Add(Note(rule, location, message, dropped: true, text: text));

    private static IEnumerable<BenchmarkReportValidationNote> MarkDropped(IEnumerable<BenchmarkReportValidationNote> issues)
        => issues.Select(n => Note(n.Rule, n.Location, n.Message, dropped: true, text: n.Text));

    /// <summary>The notes, each given <paramref name="text"/> unless it already holds a text of its own.</summary>
    private static IEnumerable<BenchmarkReportValidationNote> WithText(IEnumerable<BenchmarkReportValidationNote> issues, string? text)
        => issues.Select(n => Note(n.Rule, n.Location, n.Message, n.Dropped, n.Text ?? text));

    /// <summary><paramref name="text"/> cut at <see cref="BenchmarkReportValidationNote.MaxTextLength"/>; null for an empty one.</summary>
    private static string? CapText(string? text)
        => string.IsNullOrWhiteSpace(text) ? null
            : text.Length <= BenchmarkReportValidationNote.MaxTextLength ? text
            : text[..BenchmarkReportValidationNote.MaxTextLength];

    /// <summary>The items' texts, a blank line between them; null when none has one.</summary>
    private static string? ItemTexts(IEnumerable<BenchmarkReportWriterItem?>? items)
    {
        var texts = (items ?? Enumerable.Empty<BenchmarkReportWriterItem?>())
            .Select(i => i?.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return texts.Count == 0 ? null : string.Join("\n\n", texts);
    }

    /// <summary>A writer hypothesis as its texts: the statement, each item for and against, and the refuted-if statement.</summary>
    private static string HypothesisText(BenchmarkReportWriterHypothesis? hypothesis)
    {
        var h = hypothesis ?? new BenchmarkReportWriterHypothesis();
        var parts = new List<string?> { h.Statement };
        parts.AddRange((h.For ?? new List<string>()).Select(t => "For: " + t));
        parts.AddRange((h.Against ?? new List<string>()).Select(t => "Against: " + t));
        if (!string.IsNullOrWhiteSpace(h.RefutedIf)) parts.Add("Refuted if: " + h.RefutedIf);
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static BenchmarkReportWriterItem CloneItem(BenchmarkReportWriterItem item) => new()
    {
        Text = item.Text,
        Questions = new List<int>(item.Questions ?? new List<int>()),
        Evidence = new List<string>(item.Evidence ?? new List<string>())
    };

    /// <summary>Everything one validation pass looks up, built once per call.</summary>
    private sealed class Context
    {
        public Context(BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet, BenchmarkReportContentSnapshot content)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            ArgumentNullException.ThrowIfNull(content);

            Spec = BenchmarkReportSlots.For(audience, sheet);
            Comparison = sheet.IsComparison;
            ChatConsistency = sheet.IsChatConsistency;
            Scope = ChatConsistency ? BenchmarkReportScope.ChatConsistency
                : Comparison ? BenchmarkReportScope.Comparison
                : BenchmarkReportScope.Model;
            Claims = BenchmarkReportPackPrompt.ChatClaimSupport.From(ChatConsistency ? sheet : new BenchmarkReportFactSheet());
            RootCause = Spec.IsRootCauseAnalysis;
            RootCauseClaims = RootCauseClaimSupport.From(RootCause ? sheet : new BenchmarkReportFactSheet());
            Completion = RootCause && RootCauseClaimSupport.IsCompletion(sheet);
            _chatHiddenKeys = ChatConsistency
                ? BenchmarkChatConsistencyReportFacts.WriterHiddenKeys(sheet.Facts)
                : new HashSet<string>(StringComparer.Ordinal);
            _factDisplays = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fact in sheet.Facts) _factDisplays.TryAdd(fact.Key, fact.Display ?? string.Empty);
            var slots = Comparison
                ? Spec.ComparisonSlotsFor(hasNotableFindings: BenchmarkNotableFindings.CountOf(sheet) > 0)
                : Spec.SlotsFor(hasPeers: sheet.Peers.Count > 0);

            // An answer slot belongs to the document only when the sheet asks its question.
            RequiredSlots = RootCause
                ? slots.Where(s => RootCauseClaimSupport.QuestionOf(s) is not int n || RootCauseClaims.Questions.Contains(n)).ToList()
                : slots;
            FactKeys = new HashSet<string>(sheet.Facts.Select(f => f.Key), StringComparer.Ordinal);
            _availableFacts = new HashSet<string>(sheet.Facts.Where(f => f.Available).Select(f => f.Key), StringComparer.Ordinal);
            _trueFacts = new HashSet<string>(
                sheet.Facts.Where(BenchmarkReportPackPrompt.IsTrue).Select(f => f.Key), StringComparer.Ordinal);
            OrderedLetters = BenchmarkReportPackPrompt.OrderedPeers(sheet.Peers).Select(p => p.Letter).ToList();
            _zeroDisplays = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fact in sheet.Facts.Where(f => f.Available && (f.Display ?? string.Empty).TrimStart().StartsWith('0')))
            {
                _zeroDisplays.TryAdd(fact.Key, fact.Display.Trim());
            }
            PeerLetters = new HashSet<string>(sheet.Peers.Select(p => p.Letter), StringComparer.Ordinal);
            ModelTokens = new HashSet<string>(PeerLetters, StringComparer.Ordinal);
            if (!Comparison) ModelTokens.Add(RootCauseEngine.SubjectToken);
            OrderedQuestions = sheet.Questions.Select(q => q.Number).Distinct().OrderBy(n => n).ToList();
            Questions = new HashSet<int>(OrderedQuestions);
            QuestionsNeedingTopic = BenchmarkReportPackPrompt.QuestionsNeedingTopic(sheet);
            Battery = sheet.Battery != null || (Comparison && sheet.Questions.Any(q => !string.IsNullOrWhiteSpace(q.Reference)));
            _referenceByNumber = new Dictionary<int, string>();
            _numberByReference = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var q in sheet.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Reference)))
            {
                _referenceByNumber.TryAdd(q.Number, q.Reference!);
                _numberByReference.TryAdd(q.Reference!, q.Number);
            }
            _overlappingPeers = new HashSet<string>(
                sheet.Peers
                    .Select(p => p.Letter)
                    .Where(letter => sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, BenchmarkReportFacts.PeerPrefix(letter) + "intervalOverlap", StringComparison.Ordinal))
                        is { } fact && BenchmarkReportPackPrompt.IsTrue(fact)),
                StringComparer.Ordinal);
            _pairedExcludesZeroPeers = new HashSet<string>(
                sheet.Peers
                    .Select(p => p.Letter)
                    .Where(letter => sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, BenchmarkReportFacts.PeerPrefix(letter) + "pairedExcludesZero", StringComparison.Ordinal))
                        is { } fact && BenchmarkReportPackPrompt.IsTrue(fact)),
                StringComparer.Ordinal);

            Rows =new Dictionary<string, BenchmarkReportFindingRow>(StringComparer.Ordinal);
            foreach (var row in sheet.Rows)
            {
                Rows.TryAdd(row.Id, row);
            }

            // A comparison-scope sheet has no subject: every covered model's name is a peer's.
            var subjectNames = Comparison
                ? Array.Empty<string>()
                : new[] { sheet.SubjectLabel, sheet.SubjectDisplayName, sheet.SubjectModelId, sheet.SubjectProvider };

            var known = new List<string>(sheet.KnownNames ?? new List<string>());
            known.AddRange(subjectNames);
            known.Add(sheet.SuiteName);
            foreach (var peer in sheet.Peers)
            {
                known.AddRange(new[] { peer.Label, peer.DisplayName, peer.ModelId, peer.Provider });
            }
            foreach (var grader in sheet.Graders)
            {
                known.AddRange(new[] { grader.Label, grader.ModelId, grader.Provider });
            }
            _knownNames = BuildNameRegex(known.Where(n => n != null && n.Any(char.IsLetter)), minLength: 1);

            _subjectNames = BuildNameRegex(subjectNames, minLength: 1);

            var subjectSet = new HashSet<string>(
                subjectNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var peerNames = sheet.Peers
                .SelectMany(p => new[] { p.Label, p.DisplayName, p.ModelId, p.Provider })
                .Where(n => !string.IsNullOrWhiteSpace(n) && !subjectSet.Contains(n.Trim()));
            _peerNames = BuildNameRegex(peerNames, MinPeerNameLength);

            _shingles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var q in content.Questions ?? new List<BenchmarkReportContentQuestion>())
            {
                string qn = Reference(q.Number);
                AddShingles(q.QuestionText, $"the question text of {qn}");
                AddShingles(q.ExpectedPoints, $"the rubric of {qn}");
            }
            foreach (var run in content.Runs.OrderBy(r => r.RunId))
            {
                foreach (var q in run.Questions)
                {
                    string qn = Reference(q.Number);
                    AddShingles(q.QuestionText, $"the question text of {qn}");
                    AddShingles(q.ExpectedPoints, $"the rubric of {qn}");
                    AddShingles(q.AnswerExcerpt, $"the answer excerpt of {qn}");
                    foreach (var grader in q.Graders ?? new List<BenchmarkReportContentGrader>())
                    {
                        AddShingles(grader.Comment, $"a grader comment on {qn}");
                        foreach (string evidence in grader.Evidence ?? new List<string>())
                        {
                            AddShingles(evidence, $"grader evidence on {qn}");
                        }
                    }
                    foreach (var ruling in q.ClaimRulings ?? new List<BenchmarkReportContentClaimRuling>())
                    {
                        AddShingles(ruling.Claim, $"a verifier claim on {qn}");
                        AddShingles(ruling.Rationale, $"a verifier rationale on {qn}");
                    }
                }
            }
            foreach (var note in content.ModelNotes ?? new List<BenchmarkReportModelNote>())
            {
                AddShingles(note.Comment, $"a grader comment on {Reference(note.Question)}");
            }
        }

        private readonly Regex? _knownNames;
        private readonly Regex? _subjectNames;
        private readonly Regex? _peerNames;
        private readonly Dictionary<string, string> _shingles;
        private readonly HashSet<string> _overlappingPeers;
        private readonly HashSet<string> _pairedExcludesZeroPeers;
        private readonly Dictionary<string, string> _zeroDisplays;
        private readonly Dictionary<int, string> _referenceByNumber;
        private readonly Dictionary<string, int> _numberByReference;
        private readonly IReadOnlySet<string> _chatHiddenKeys;
        private readonly Dictionary<string, string> _factDisplays;
        private readonly HashSet<string> _availableFacts;

        public BenchmarkReportAudienceSpec Spec { get; }

        /// <summary>A chat consistency fact kept from the writer: <see cref="BenchmarkChatConsistencyReportFacts.WriterHidden(string)"/>, or one of this sheet's <see cref="BenchmarkChatConsistencyReportFacts.WriterHiddenKeys"/>.</summary>
        public bool ChatWriterHidden(string key)
            => BenchmarkChatConsistencyReportFacts.WriterHidden(key) || _chatHiddenKeys.Contains(key);

        /// <summary>The display of the sheet's first fact keyed <paramref name="key"/>; null when there is none.</summary>
        public string? FactDisplay(string key) => _factDisplays.TryGetValue(key, out string? display) ? display : null;

        /// <summary>The sheet is a battery's: its questions are referred to as <c>S&lt;suite&gt;-Q&lt;n&gt;</c>.</summary>
        public bool Battery { get; }

        /// <summary>The questions that need a topic where the document requires topics.</summary>
        public IReadOnlyList<int> QuestionsNeedingTopic { get; }

        /// <summary>How the document refers to a question: its battery reference, else <c>Q&lt;n&gt;</c>.</summary>
        public string Reference(int number) => _referenceByNumber.TryGetValue(number, out string? reference) ? reference : Q(number);

        /// <summary>The text is a battery question's reference, exactly as its QUESTIONS row shows it.</summary>
        public bool IsReference(string text) => _numberByReference.ContainsKey(text);

        /// <summary>A battery sheet's question numbers by reference; null on a sheet whose questions are <c>Q&lt;n&gt;</c>.</summary>
        public IReadOnlyDictionary<string, int>? ReferenceNumbers => Battery ? _numberByReference : null;

        /// <summary>The document is a Root Cause Analysis: rules 32 to 36 apply, and rule 31 in either scope.</summary>
        public bool RootCause { get; }

        /// <summary>What each kind of Root Cause Analysis claim may cite; empty on every other document.</summary>
        public RootCauseClaimSupport RootCauseClaims { get; }

        /// <summary>A Root Cause Analysis of a run or battery run on its own: its evidence slots are capped lower.</summary>
        public bool Completion { get; }

        /// <summary>The model tokens a writer hypothesis may name: the letters, and <c>subject</c> in a per-model document.</summary>
        public HashSet<string> ModelTokens { get; }

        /// <summary>The word cap of one of this document's slots, or null when it has none.</summary>
        public int? SlotCap(string slot)
            => RootCause ? RootCauseSlotMaxWords(slot, Completion) : SlotMaxWords(Spec.Audience, slot, Scope);

        /// <summary>The display of an available fact whose display starts with <c>0</c>; null for any other key.</summary>
        public string? ZeroDisplay(string key) => _zeroDisplays.TryGetValue(key, out string? display) ? display : null;

        /// <summary>The audience's slots this sheet requires: the peer-only slots only when it has peers.</summary>
        public IReadOnlyList<string> RequiredSlots { get; }

        /// <summary>The peer's <c>peer.X.intervalOverlap</c> fact is true.</summary>
        public bool OverlapsSubject(string letter) => _overlappingPeers.Contains(letter);

        /// <summary>The peer's <c>peer.X.pairedExcludesZero</c> fact is true.</summary>
        public bool PairedExcludesZero(string letter) => _pairedExcludesZeroPeers.Contains(letter);
        public HashSet<string> FactKeys { get; }
        public HashSet<string> PeerLetters { get; }
        public HashSet<int> Questions { get; }
        public List<int> OrderedQuestions { get; }
        public Dictionary<string, BenchmarkReportFindingRow> Rows { get; }

        /// <summary>The sheet is a comparison-scope sheet: models are <c>{{model:X}}</c>, and there is no subject.</summary>
        public bool Comparison { get; }

        /// <summary>The sheet is a chat consistency sheet: rules C1 to C8 apply.</summary>
        public bool ChatConsistency { get; }

        /// <summary>The document scope the sheet describes.</summary>
        public BenchmarkReportScope Scope { get; }

        /// <summary>What each kind of chat consistency claim may cite; empty on every other sheet.</summary>
        public BenchmarkReportPackPrompt.ChatClaimSupport Claims { get; }

        /// <summary>The sheet's letters in letter order: shorter letters first, then ordinal.</summary>
        public IReadOnlyList<string> OrderedLetters { get; }

        private readonly HashSet<string> _trueFacts;

        /// <summary>A letter's place in <see cref="OrderedLetters"/>; <see cref="int.MaxValue"/> for an unknown one.</summary>
        public int LetterIndex(string letter)
        {
            for (int i = 0; i < OrderedLetters.Count; i++)
            {
                if (string.Equals(OrderedLetters[i], letter, StringComparison.Ordinal)) return i;
            }
            return int.MaxValue;
        }

        /// <summary>The fact is available and true (<see cref="BenchmarkReportPackPrompt.IsTrue"/>).</summary>
        public bool FactIsTrue(string key) => _trueFacts.Contains(key);

        /// <summary>
        /// A family of the paired tests establishes the pair's quality order: its <c>quality.reference</c>,
        /// <c>quality.allPairs</c> or <c>quality.short</c> fact is true.
        /// </summary>
        public bool PairEstablished(string pairPrefix) => PairAxisEstablished(pairPrefix, "quality");

        /// <summary>
        /// A family of the paired tests establishes the pair's order on <paramref name="measure"/>
        /// (<c>quality</c>, <c>speed</c> or <c>cost</c>): its family fact or its <c>.short</c> fact is true.
        /// </summary>
        public bool PairAxisEstablished(string pairPrefix, string measure)
            => FactIsTrue(pairPrefix + measure + "." + BenchmarkComparisonReportFacts.ReferenceFamilyName)
               || FactIsTrue(pairPrefix + measure + "." + BenchmarkComparisonReportFacts.AllPairsFamilyName)
               || FactIsTrue(pairPrefix + measure + ".short");

        /// <summary>The pair has a paired result on <paramref name="measure"/>: an available family fact or <c>.short</c> fact.</summary>
        public bool PairHasResult(string pairPrefix, string measure)
            => _availableFacts.Contains(pairPrefix + measure + "." + BenchmarkComparisonReportFacts.ReferenceFamilyName)
               || _availableFacts.Contains(pairPrefix + measure + "." + BenchmarkComparisonReportFacts.AllPairsFamilyName)
               || _availableFacts.Contains(pairPrefix + measure + ".short");

        public bool IsValidToken(string inner)
        {
            if (Comparison)
            {
                if (inner.StartsWith("model:", StringComparison.Ordinal)) return PeerLetters.Contains(inner.Substring("model:".Length));
                if (inner == "subject" || inner.StartsWith("peer:", StringComparison.Ordinal)) return false;
                return FactKeys.Contains(inner);
            }

            if (inner == "subject") return true;
            if (inner.StartsWith("peer:", StringComparison.Ordinal)) return PeerLetters.Contains(inner.Substring("peer:".Length));
            return FactKeys.Contains(inner);
        }

        public string MaskKnownNames(string text) => _knownNames?.Replace(text, " ") ?? text;

        /// <summary>
        /// Peer names in the text, as written. A match that lies inside a longer occurrence of one of
        /// the subject's names (a peer's <c>GPT-5.2</c> inside the subject's <c>GPT-5.2 Pro</c>) is not one.
        /// </summary>
        public List<string> FindPeerNames(string text)
        {
            if (_peerNames == null) return new List<string>();

            var subjectSpans = _subjectNames?.Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList()
                ?? new List<(int Index, int End)>();

            return _peerNames.Matches(text)
                .Where(m => !subjectSpans.Any(s => s.Index <= m.Index && m.Index + m.Length <= s.End && s.End - s.Index > m.Length))
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public (string Run, string Source)? FindSharedRun(string text)
        {
            if (_shingles.Count == 0) return null;

            var words = Words(text);
            for (int i = 0; i + ShingleLength <= words.Length; i++)
            {
                string key = string.Join(' ', words, i, ShingleLength);
                if (_shingles.TryGetValue(key, out string? source))
                {
                    return (key, source);
                }
            }

            return null;
        }

        private void AddShingles(string? text, string source)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var words = Words(text);
            for (int i = 0; i + ShingleLength <= words.Length; i++)
            {
                _shingles.TryAdd(string.Join(' ', words, i, ShingleLength), source);
            }
        }

        private static string[] Words(string text)
            => WordRegex.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToArray();

        private static Regex? BuildNameRegex(IEnumerable<string?> names, int minLength)
        {
            var list = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .Where(n => n.Length >= minLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(n => n.Length)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
            if (list.Count == 0) return null;

            string pattern = @"(?<![\p{L}\p{N}])(?:" + string.Join("|", list.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
