namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

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
}

/// <summary>
/// The report-pack document rules (D7), checked against the fact sheet and the content snapshot.
///
/// <para>Rules 2, 3, 8, 9, 10, 11, 12, 16, 17 and 19 apply to every prose string: the headline, each
/// paragraph of each section, and the text of every item, topic and note. A section's paragraphs are
/// checked one by one, so <see cref="DropInvalid"/> can remove only the offending ones.</para>
///
/// <list type="number">
/// <item>Structure: headline and required slots present and non-empty (a peer-only slot only when
/// the sheet has peers); no unknown slots; no lists the audience does not use; item texts non-empty;
/// <c>for</c> and <c>triage</c> from their fixed sets.</item>
/// <item>Tokens: <c>{{key}}</c> names a fact, <c>{{peer:X}}</c> a peer letter, <c>{{subject}}</c> the subject;
/// written exactly, without inner spaces; no stray braces.</item>
/// <item>No bare digit once tokens, known names and <c>Q&lt;n&gt;</c> / <c>R&lt;n&gt;</c> references (on a battery
/// sheet also <c>S&lt;suite&gt;-Q&lt;n&gt;</c>) are masked.</item>
/// <item>Every question number exists in the subject's exam; topics cover every question where required
/// (on a battery sheet, every question given in detail). A battery sheet's questions are referred to as
/// <c>S&lt;suite&gt;-Q&lt;n&gt;</c>, never as a bare <c>Q&lt;n&gt;</c>.</item>
/// <item>Every evidence id and <c>R&lt;n&gt;</c> reference exists; strengths, weaknesses and leads cite
/// one, and so do the recommendations of the Report for AI Researchers and Developers.</item>
/// <item>A strength cites no weakness row and a weakness no strength row; a finding citing only
/// Conflicting rows says the graders disagree.</item>
/// <item>Word and item limits: every audience's slot word caps, the Executive Summary's item word
/// cap, the report's recommendation count, and the Internal Improvement Brief's recommendation and
/// lead counts.</item>
/// <item>No headings, Markdown tables or HTML.</item>
/// <item>No run of <see cref="ShingleLength"/> words shared with the content snapshot.</item>
/// <item>No peer name, label, model id or provider other than the subject's own provider.</item>
/// <item>No significance claims. Matching is whole-word, so <c>insignificant</c> passes.</item>
/// <item>US English: no word from <see cref="BritishSpellings"/>. It asks for the repair turn, but
/// <see cref="DropInvalid"/> keeps the text and records the note instead of dropping it.</item>
/// <item>The Executive Summary's confidence slot does not call the quality interval narrow, wide,
/// tight or broad (<see cref="IntervalWidthWords"/>). Like rule 12 it asks for the repair turn, and
/// <see cref="DropInvalid"/> keeps the paragraph and records the note.</item>
/// <item>The headline and the abstract do not mention the claim verifier (<see cref="VerifierRegex"/>):
/// its refutations are advisory and belong, attributed, in the weaknesses. A warning like rule 12: it
/// asks for the repair turn, and <see cref="DropInvalid"/> keeps the text and records the note.</item>
/// <item>Every question listed under QUESTIONS NEEDING A NOTE has a note in <c>questionNotes</c>, where
/// the audience uses them. A warning with nothing to drop.</item>
/// <item>A sentence holding <c>{{subject}}</c>, a <c>{{peer:X}}</c> token and a word of
/// <see cref="ComparativeWords"/>, where <c>peer.X.intervalOverlap</c> is true, also says
/// <c>overlap</c> or <c>not established</c>; where <c>peer.X.pairedExcludesZero</c> is true, saying
/// that the paired interval excludes zero (or citing that fact) does as well. A warning.</item>
/// <item>No word of <see cref="HypeWords"/>, whole words ignoring case. A warning.</item>
/// <item>A <c>model_developers</c> recommendation names nothing a model developer cannot change
/// (<see cref="OverseerOnlyTerms"/>): the Overseer's rubrics, retrieval, index, corpus, regression
/// tests, system prompt, the assistant's prompt or tool guides, prompting the model, or GnollHack
/// itself. A warning.</item>
/// <item>No negation (<see cref="NegationWords"/>) within <see cref="NegationWindowWords"/> words before a
/// fact token, in the same sentence, whose display starts with <c>0</c>: "no critical errors across
/// {{errors.critical}}" reads "no critical errors across 0 of 18 answers". A warning.</item>
/// </list>
/// </summary>
public static class BenchmarkReportPackValidator
{
    public const int ShingleLength = 8;
    public const int HeadlineMaxWords = 35;
    public const int TopicMaxWords = 12;
    public const int AbstractMaxWords = 150;

    /// <summary>The Executive Summary's "What this means for use as a game assistant".</summary>
    public const int MeaningMaxWords = 90;

    /// <summary>The Executive Summary's "How reliable this result is".</summary>
    public const int ConfidenceMaxWords = 60;

    /// <summary>The Report for AI Researchers and Developers' "Why it scored this way", before its weaknesses list.</summary>
    public const int WhyItScoredMaxWords = 300;

    /// <summary>The Report for AI Researchers and Developers' "What worked well", before its strengths list.</summary>
    public const int WhatWorkedMaxWords = 150;

    /// <summary>The Executive Summary's paragraph under "How it compares".</summary>
    public const int ComparisonMaxWords = 70;

    /// <summary>The Report for AI Researchers and Developers' last paragraph of "Threats to validity".</summary>
    public const int LimitationsMaxWords = 120;

    /// <summary>The Internal Improvement Brief's "The Overseer chat and its tools".</summary>
    public const int OverseerChatMaxWords = 200;

    /// <summary>The Internal Improvement Brief's "The benchmarking system".</summary>
    public const int BenchmarkSystemMaxWords = 150;

    /// <summary>The Internal Improvement Brief's "The model's result".</summary>
    public const int ModelResultMaxWords = 150;

    /// <summary>Each strength and weakness of the Executive Summary.</summary>
    public const int ExecutiveItemMaxWords = 30;

    /// <summary>Recommendations the Report for AI Researchers and Developers holds.</summary>
    public const int TechnicalReportMaxRecommendations = 6;

    /// <summary>Recommendations the Internal Improvement Brief holds.</summary>
    public const int InternalBriefMaxRecommendations = 8;

    /// <summary>Leads the Internal Improvement Brief holds.</summary>
    public const int MaxLeads = 6;

    /// <summary>The rule number of the US English check, whose notes never drop an item.</summary>
    public const int UsSpellingRule = 12;

    /// <summary>
    /// The rule number of the interval-width check in the Executive Summary's confidence slot, whose
    /// notes never drop a paragraph.
    /// </summary>
    public const int IntervalWidthRule = 13;

    /// <summary>
    /// The rule number of the claim-verifier check on the headline and the abstract, whose notes never
    /// drop text.
    /// </summary>
    public const int VerifierInSummaryRule = 14;

    /// <summary>The rule number of the check that every question needing a note has one; nothing to drop.</summary>
    public const int MissingQuestionNoteRule = 15;

    /// <summary>
    /// The rule number of the check that a comparison of the subject with a peer whose interval
    /// overlaps it says so; its notes never drop text.
    /// </summary>
    public const int OverlapHedgeRule = 16;

    /// <summary>The rule number of the hype-word check, whose notes never drop text.</summary>
    public const int HypeWordRule = 17;

    /// <summary>
    /// The rule number of the check that a <c>model_developers</c> recommendation stays within what a
    /// model developer can change; its notes never drop text.
    /// </summary>
    public const int ModelDeveloperScopeRule = 18;

    /// <summary>
    /// The rule number of the check that a negation does not precede a token whose value starts with
    /// zero; its notes never drop text.
    /// </summary>
    public const int ZeroTokenNegationRule = 19;

    /// <summary>Rule 19 looks this many words back from a token for a negation.</summary>
    public const int NegationWindowWords = 4;

    /// <summary>Whether a rule's notes are warnings: they ask for the repair turn but never drop text.</summary>
    public static bool IsWarningRule(int rule)
        => rule is UsSpellingRule or IntervalWidthRule or VerifierInSummaryRule or MissingQuestionNoteRule or OverlapHedgeRule
            or HypeWordRule or ModelDeveloperScopeRule or ZeroTokenNegationRule;

    /// <summary>
    /// Terms rule 18 flags in a <c>model_developers</c> recommendation, matched as whole words ignoring
    /// case, with their plural and inflected forms: parts of the Overseer, and the game itself, which a
    /// model developer cannot change.
    /// </summary>
    public static readonly IReadOnlyList<string> OverseerOnlyTerms = new[]
    {
        "rubric", "retrieval", "index", "corpus", "regression test", "system prompt", "tool guide",
        "prompt the model", "the assistant's prompt", "GnollHack"
    };

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
        "game-changing", "cutting-edge"
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

    public static readonly IReadOnlyList<string> LeadTriages = new[] { "harness", "suite", "chat", "corpus" };

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

    /// <summary>Rule 18's pattern over <see cref="OverseerOnlyTerms"/> and their inflected forms.</summary>
    private static readonly Regex OverseerOnlyTermRegex = new(
        @"(?<![\p{L}\p{N}])(?:rubrics?|retrieval|index(?:es|ed|ing)?|indices|corpus|corpora|regression[\s-]+tests?|system[\s-]+prompts?|tool[\s-]+guides?"
        + @"|prompt(?:s|ed|ing)?\s+the\s+model|the\s+assistant['’]s\s+prompts?|gnollhack)(?![\p{L}\p{N}])",
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

    private enum ItemKind
    {
        Strength,
        Weakness,
        Recommendation,
        Lead,
    }

    /// <summary>How many recommendations the audience's document holds.</summary>
    public static int MaxRecommendations(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.TechnicalReport => TechnicalReportMaxRecommendations,
        BenchmarkReportAudience.InternalBrief => InternalBriefMaxRecommendations,
        _ => int.MaxValue
    };

    /// <summary>Rule 5: the Report for AI Researchers and Developers backs every recommendation with evidence.</summary>
    public static bool RecommendationsRequireEvidence(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.TechnicalReport;

    /// <summary>The word cap of a slot, or null when it has none.</summary>
    public static int? SlotMaxWords(BenchmarkReportAudience audience, string slot) => slot switch
    {
        BenchmarkReportSlots.Abstract => AbstractMaxWords,
        BenchmarkReportSlots.Meaning when audience == BenchmarkReportAudience.ExecutiveSummary => MeaningMaxWords,
        BenchmarkReportSlots.Confidence when audience == BenchmarkReportAudience.ExecutiveSummary => ConfidenceMaxWords,
        BenchmarkReportSlots.Comparison when audience == BenchmarkReportAudience.ExecutiveSummary => ComparisonMaxWords,
        BenchmarkReportSlots.WhyItScored when audience == BenchmarkReportAudience.TechnicalReport => WhyItScoredMaxWords,
        BenchmarkReportSlots.WhatWorked when audience == BenchmarkReportAudience.TechnicalReport => WhatWorkedMaxWords,
        BenchmarkReportSlots.Limitations when audience == BenchmarkReportAudience.TechnicalReport => LimitationsMaxWords,
        BenchmarkReportSlots.OverseerChat when audience == BenchmarkReportAudience.InternalBrief => OverseerChatMaxWords,
        BenchmarkReportSlots.BenchmarkSystem when audience == BenchmarkReportAudience.InternalBrief => BenchmarkSystemMaxWords,
        BenchmarkReportSlots.ModelResult when audience == BenchmarkReportAudience.InternalBrief => ModelResultMaxWords,
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
                CheckProse(ctx, paragraphs[p], ParagraphLocation(slot, p), notes);
                CheckIntervalWidth(audience, slot, paragraphs[p], ParagraphLocation(slot, p), notes);
                CheckAbstractVerifier(slot, paragraphs[p], ParagraphLocation(slot, p), notes);
            }

            if (SlotMaxWords(audience, slot) is int cap && WordCount(text) > cap)
            {
                Issue(notes, 7, location, $"{SlotName(slot)} has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {cap.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        foreach (string key in ExtraSlots(ctx.RequiredSlots, sections))
        {
            Issue(notes, 1, SectionLocation(key), UnknownSlotMessage(ctx, key));
        }

        CheckItems(ctx, "strengths", output.Strengths, ItemKind.Strength, spec.MaxStrengths, notes);
        CheckItems(ctx, "weaknesses", output.Weaknesses, ItemKind.Weakness, spec.MaxWeaknesses, notes);

        if (spec.UsesRecommendations)
        {
            CheckItems(ctx, "recommendations", output.Recommendations, ItemKind.Recommendation, MaxRecommendations(audience), notes);
        }
        else if (output.Recommendations is { Count: > 0 })
        {
            Issue(notes, 1, "recommendations", UnusedListMessage(spec, "recommendations"));
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
        CheckTopicCoverage(ctx, topicSeen, notes, dropped: false);

        var questionNotes = output.QuestionNotes ?? new List<BenchmarkReportQuestionNote>();
        if (spec.UsesQuestionNotes)
        {
            var noteSeen = new HashSet<int>();
            var noted = new HashSet<int>();
            for (int i = 0; i < questionNotes.Count; i++)
            {
                var note = questionNotes[i] ?? new BenchmarkReportQuestionNote();
                string location = $"questionNotes[{i.ToString(CultureInfo.InvariantCulture)}]";
                notes.AddRange(NoteIssues(ctx, note, location));
                if (!noteSeen.Add(note.Question) && ctx.Questions.Contains(note.Question))
                {
                    Issue(notes, 4, location, $"{ctx.Reference(note.Question)} already has a note in an earlier entry.");
                }
                if (!string.IsNullOrWhiteSpace(note.Note)) noted.Add(note.Question);
            }
            CheckNoteCoverage(ctx, noted, notes);
        }
        else if (questionNotes.Count > 0)
        {
            Issue(notes, 1, "questionNotes", UnusedListMessage(spec, "questionNotes"));
        }

        if (spec.UsesLeads)
        {
            CheckItems(ctx, "leads", output.Leads, ItemKind.Lead, MaxLeads, notes);
        }
        else if (output.Leads is { Count: > 0 })
        {
            Issue(notes, 1, "leads", UnusedListMessage(spec, "leads"));
        }

        return notes;
    }

    // -----------------------------------------------------------------------------------------
    // DropInvalid
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Removes every item and section paragraph with an issue from a copy of the output. The headline
    /// cannot be dropped, so an invalid one is fatal, as is a required slot left empty. Missing
    /// question topics and notes are recorded but not fatal, and so are the warnings of rules 12 to
    /// 19: their text is kept.
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

        var copy = new BenchmarkReportWriterOutput { Headline = output.Headline ?? string.Empty };

        var headlineIssues = new List<BenchmarkReportValidationNote>();
        CheckHeadline(ctx, copy.Headline, headlineIssues);
        notes.AddRange(headlineIssues);
        var blockingHeadline = headlineIssues.FirstOrDefault(Blocks);
        if (blockingHeadline != null)
        {
            fatal ??= $"The headline is invalid and cannot be dropped: {blockingHeadline.Message}";
        }

        var sections = output.Sections ?? new Dictionary<string, string>();
        foreach (string slot in ctx.RequiredSlots)
        {
            string location = SectionLocation(slot);
            if (!sections.TryGetValue(slot, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                Issue(notes, 1, location, "The required slot is missing or empty.");
                fatal ??= $"The required slot \"{slot}\" is missing or empty.";
                copy.Sections[slot] = string.Empty;
                continue;
            }

            var paragraphs = SplitParagraphs(text);
            var kept = new List<(int Index, string Text)>();
            for (int p = 0; p < paragraphs.Count; p++)
            {
                var issues = new List<BenchmarkReportValidationNote>();
                CheckProse(ctx, paragraphs[p], ParagraphLocation(slot, p), issues);
                CheckIntervalWidth(audience, slot, paragraphs[p], ParagraphLocation(slot, p), issues);
                CheckAbstractVerifier(slot, paragraphs[p], ParagraphLocation(slot, p), issues);
                if (issues.Any(Blocks))
                {
                    notes.AddRange(MarkDropped(issues));
                }
                else
                {
                    notes.AddRange(issues);
                    kept.Add((p, paragraphs[p]));
                }
            }

            if (SlotMaxWords(audience, slot) is int cap)
            {
                while (kept.Count > 0 && WordCount(string.Join("\n\n", kept.Select(k => k.Text))) > cap)
                {
                    var last = kept[^1];
                    kept.RemoveAt(kept.Count - 1);
                    Dropped(notes, 7, ParagraphLocation(slot, last.Index), $"Removed to bring {LowerFirst(SlotName(slot))} within {cap.ToString(CultureInfo.InvariantCulture)} words.");
                }
            }

            if (kept.Count == 0)
            {
                Issue(notes, 1, location, "Every paragraph of the required slot was dropped.");
                fatal ??= $"Every paragraph of the required slot \"{slot}\" was dropped.";
                copy.Sections[slot] = string.Empty;
            }
            else
            {
                copy.Sections[slot] = kept.Count == paragraphs.Count ? text : string.Join("\n\n", kept.Select(k => k.Text));
            }
        }

        foreach (string key in ExtraSlots(ctx.RequiredSlots, sections))
        {
            Dropped(notes, 1, SectionLocation(key), UnknownSlotMessage(ctx, key));
        }

        copy.Strengths = CleanItems(ctx, "strengths", output.Strengths, ItemKind.Strength, spec.MaxStrengths, notes, CloneItem);
        copy.Weaknesses = CleanItems(ctx, "weaknesses", output.Weaknesses, ItemKind.Weakness, spec.MaxWeaknesses, notes, CloneItem);

        if (spec.UsesRecommendations)
        {
            copy.Recommendations = CleanItems(ctx, "recommendations", output.Recommendations, ItemKind.Recommendation, MaxRecommendations(audience), notes, CloneRecommendation);
        }
        else if (output.Recommendations is { Count: > 0 })
        {
            Dropped(notes, 1, "recommendations", UnusedListMessage(spec, "recommendations"));
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
                notes.AddRange(MarkDropped(issues));
                continue;
            }

            notes.AddRange(issues);
            topicSeen.Add(topic.Question);
            copy.QuestionTopics.Add(new BenchmarkReportQuestionTopic { Question = topic.Question, Topic = topic.Topic });
        }
        CheckTopicCoverage(ctx, topicSeen, notes, dropped: false);

        var questionNotes = output.QuestionNotes ?? new List<BenchmarkReportQuestionNote>();
        if (spec.UsesQuestionNotes)
        {
            var noteSeen = new HashSet<int>();
            for (int i = 0; i < questionNotes.Count; i++)
            {
                var note = questionNotes[i] ?? new BenchmarkReportQuestionNote();
                string location = $"questionNotes[{i.ToString(CultureInfo.InvariantCulture)}]";
                var issues = NoteIssues(ctx, note, location);
                if (!issues.Any(Blocks) && noteSeen.Contains(note.Question))
                {
                    issues.Add(Note(4, location, $"{ctx.Reference(note.Question)} already has a note in an earlier entry."));
                }

                if (issues.Any(Blocks))
                {
                    notes.AddRange(MarkDropped(issues));
                    continue;
                }

                notes.AddRange(issues);
                noteSeen.Add(note.Question);
                copy.QuestionNotes.Add(new BenchmarkReportQuestionNote { Question = note.Question, Note = note.Note });
            }
            CheckNoteCoverage(ctx, noteSeen, notes);
        }
        else if (questionNotes.Count > 0)
        {
            Dropped(notes, 1, "questionNotes", UnusedListMessage(spec, "questionNotes"));
        }

        if (spec.UsesLeads)
        {
            copy.Leads = CleanItems(ctx, "leads", output.Leads, ItemKind.Lead, MaxLeads, notes, CloneLead);
        }
        else if (output.Leads is { Count: > 0 })
        {
            Dropped(notes, 1, "leads", UnusedListMessage(spec, "leads"));
        }

        return new BenchmarkReportCleanResult
        {
            Output = copy,
            Notes = notes,
            Fatal = fatal != null,
            FatalReason = fatal
        };
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

        int words = WordCount(headline);
        if (words > HeadlineMaxWords)
        {
            Issue(notes, 7, "headline", $"The headline has {words.ToString(CultureInfo.InvariantCulture)} words; the limit is {HeadlineMaxWords.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    /// <summary>Rules 2, 3, 4 (question references), 5 (row references), 8, 9, 10 and 11 on one prose string.</summary>
    private static void CheckProse(Context ctx, string text, string location, List<BenchmarkReportValidationNote> notes)
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
            Issue(notes, 2, location, $"Unknown token{Plural(badTokens.Count)} {string.Join(", ", badTokens)}: use {{{{subject}}}}, {{{{peer:X}}}} with a letter from PEERS, or {{{{key}}}} with a fact key written exactly as listed, without spaces inside the braces.");
        }
        if (stripped.Contains("{{", StringComparison.Ordinal) || stripped.Contains("}}", StringComparison.Ordinal))
        {
            Issue(notes, 2, location, "Unbalanced token braces: write every token as {{...}} with nothing but the token name between the braces.");
        }

        // Rule 3: bare digits.
        string masked = ctx.MaskKnownNames(stripped);
        if (ctx.Battery) masked = BatteryRefRegex.Replace(masked, " ");
        masked = QuestionRefRegex.Replace(masked, " ");
        masked = RowRefRegex.Replace(masked, " ");
        var digit = DigitWordRegex.Match(masked);
        if (digit.Success)
        {
            Issue(notes, 3, location, ctx.Battery
                ? $"Contains the digit form \"{digit.Value}\": place figures only as {{{{key}}}} tokens, write counts as number words, and refer to questions as S<suite>-Q<n>."
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
            Issue(notes, 10, location, $"Names another model or its provider ({string.Join(", ", names)}): refer to a peer only as {{{{peer:X}}}}.");
        }

        // Rule 11: significance claims.
        var claims = SignificanceRegex.Matches(stripped)
            .Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " "))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (claims.Count > 0)
        {
            Issue(notes, 11, location, $"Uses \"{string.Join("\", \"", claims)}\": the comparison runs no significance test, so say only whether the intervals overlap, or whether a paired interval excludes zero.");
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

        // Rule 16: an unhedged ranking against a peer whose interval overlaps the subject's.
        var unhedged = UnhedgedOverlappingPeers(ctx, text);
        var unpaired = unhedged.Where(l => !ctx.PairedExcludesZero(l)).ToList();
        var paired = unhedged.Where(ctx.PairedExcludesZero).ToList();
        if (unpaired.Count > 0)
        {
            string peers = string.Join(", ", unpaired.Select(l => "{{peer:" + l + "}}"));
            Issue(notes, OverlapHedgeRule, location, $"Ranks {{{{subject}}}} against {peers}, whose 95 % interval overlaps the subject's, without saying so: in the same sentence, say that the intervals overlap and that the order between them is not established.");
        }
        if (paired.Count > 0)
        {
            string peers = string.Join(", ", paired.Select(l => "{{peer:" + l + "}}"));
            Issue(notes, OverlapHedgeRule, location, $"Ranks {{{{subject}}}} against {peers}, whose 95 % interval overlaps the subject's, without the paired result: in the same sentence, say that on the same questions the higher-scoring model scored higher on average and that the paired interval excludes zero, not adjusted for comparing several models.");
        }

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
    /// says neither <c>overlap</c> nor <c>not established</c>. A peer whose
    /// <c>peer.X.pairedExcludesZero</c> is true is also hedged by a sentence saying that the paired
    /// interval excludes zero, or citing that fact as a token. Tokens are set aside before the text is
    /// split into sentences, so the dots of a fact key never end one.
    /// </summary>
    private static List<string> UnhedgedOverlappingPeers(Context ctx, string text)
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

            bool statesPaired = PairedWordRegex.IsMatch(plain) && ExcludesZeroRegex.IsMatch(plain);
            letters.AddRange(inSentence
                .Where(t => t.StartsWith("peer:", StringComparison.Ordinal))
                .Select(t => t["peer:".Length..])
                .Where(ctx.OverlapsSubject)
                .Where(l => !ctx.PairedExcludesZero(l)
                            || !(statesPaired || inSentence.Contains(BenchmarkReportFacts.PeerPrefix(l) + "pairedExcludesZero", StringComparer.Ordinal))));
        }

        return letters.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Rule 13: the Executive Summary's confidence slot states the interval's span as a figure and
    /// does not call the interval narrow, wide, tight or broad. Other slots and audiences are not checked.
    /// </summary>
    private static void CheckIntervalWidth(
        BenchmarkReportAudience audience, string slot, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (audience != BenchmarkReportAudience.ExecutiveSummary
            || !string.Equals(slot, BenchmarkReportSlots.Confidence, StringComparison.Ordinal))
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

    /// <summary>Rule 14 on one paragraph of the abstract; other slots are not checked.</summary>
    private static void CheckAbstractVerifier(string slot, string text, string location, List<BenchmarkReportValidationNote> notes)
    {
        if (string.Equals(slot, BenchmarkReportSlots.Abstract, StringComparison.Ordinal))
        {
            CheckVerifierMention(text, location, notes);
        }
    }

    /// <summary>Rule 14: the text does not mention the claim verifier.</summary>
    private static void CheckVerifierMention(string? text, string location, List<BenchmarkReportValidationNote> notes)
    {
        var match = VerifierRegex.Match(TokenRegex.Replace(text ?? string.Empty, " "));
        if (match.Success)
        {
            Issue(notes, VerifierInSummaryRule, location, $"Mentions the \"{match.Value}\": a claim-verifier ruling is an advisory judgment by an AI model that is sometimes wrong. Leave refuted claims out of the headline and the abstract, and state them among the weaknesses, attributed to the claim verifier.");
        }
    }

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
            if (issues.Any(Blocks))
            {
                notes.AddRange(MarkDropped(issues));
                continue;
            }

            if (kept.Count >= max)
            {
                Dropped(notes, 7, location, $"Removed: the {array} list holds at most {max.ToString(CultureInfo.InvariantCulture)} items.");
                continue;
            }

            notes.AddRange(issues);
            kept.Add(clone(items[i]));
        }

        return kept;
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
        }

        if (kind == ItemKind.Recommendation)
        {
            string target = (item as BenchmarkReportWriterRecommendation)?.For ?? string.Empty;
            if (!ctx.Spec.RecommendationTargets.Contains(target, StringComparer.Ordinal))
            {
                Issue(notes, 1, location, $"\"for\" is \"{target}\"; it must be one of: {string.Join(", ", ctx.Spec.RecommendationTargets)}.");
            }

            // Rule 18: a model developer's recommendation concerns the model, never the Overseer.
            if (string.Equals(target, BenchmarkReportSlots.TargetModelDevelopers, StringComparison.Ordinal))
            {
                var terms = OverseerOnlyTermRegex.Matches(TokenRegex.Replace(text, " "))
                    .Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"[\s-]+", " ").Replace('’', '\''))
                    .Select(t => t == "gnollhack" ? "GnollHack" : t)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (terms.Count > 0)
                {
                    Issue(notes, ModelDeveloperScopeRule, location, $"A recommendation for model developers mentions \"{string.Join("\", \"", terms)}\", which belong{(terms.Count == 1 ? "s" : string.Empty)} to the Overseer or the game, not to the model: name a general capability a model developer can train or tune (for example stating the decisive mechanic behind a verdict, or committing to a conclusion the inputs already settle), never a GnollHack fact, a change to the assistant's prompt or tools, or a rubric point. Put anything about the Overseer's prompts, tools, retrieval, corpus, rubrics or tests under \"{BenchmarkReportSlots.TargetOverseerChat}\" or \"{BenchmarkReportSlots.TargetBenchmark}\" where the document has them, a game-specific gap under a \"corpus\" or \"chat\" lead where it has leads, or leave it out.");
                }
            }
        }
        else if (kind == ItemKind.Lead)
        {
            string triage = (item as BenchmarkReportLead)?.Triage ?? string.Empty;
            if (!LeadTriages.Contains(triage, StringComparer.Ordinal))
            {
                Issue(notes, 1, location, $"\"triage\" is \"{triage}\"; it must be one of: {string.Join(", ", LeadTriages)}.");
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
                : $"Unknown evidence id{Plural(unknownIds.Count)} {string.Join(", ", unknownIds.Distinct(StringComparer.Ordinal))}: cite a fact key, Q<n> or a finding row id from the data.");
        }

        bool requiresEvidence = kind is ItemKind.Strength or ItemKind.Weakness or ItemKind.Lead
            || (kind == ItemKind.Recommendation && RecommendationsRequireEvidence(ctx.Spec.Audience));
        if (requiresEvidence && evidence.Count == 0)
        {
            Issue(notes, 5, location, ctx.Battery
                ? "Cites no evidence: give at least one fact key or S<suite>-Q<n>."
                : "Cites no evidence: give at least one fact key, Q<n> or finding row id.");
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

    private static List<BenchmarkReportValidationNote> NoteIssues(Context ctx, BenchmarkReportQuestionNote note, string location)
    {
        var notes = new List<BenchmarkReportValidationNote>();
        if (string.IsNullOrWhiteSpace(note.Note))
        {
            Issue(notes, 1, location, "The note is empty.");
        }
        else
        {
            CheckProse(ctx, note.Note, location, notes);
        }

        if (!ctx.Questions.Contains(note.Question))
        {
            Issue(notes, 4, location, $"Question {note.Question.ToString(CultureInfo.InvariantCulture)} is not in the subject's exam.");
        }

        return notes;
    }

    private static void CheckTopicCoverage(Context ctx, HashSet<int> covered, List<BenchmarkReportValidationNote> notes, bool dropped)
    {
        if (!ctx.Spec.RequiresQuestionTopics) return;

        var missing = ctx.QuestionsNeedingTopic.Where(n => !covered.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            notes.Add(Note(4, "questionTopics", $"No topic for {string.Join(", ", missing.Select(ctx.Reference))}: {(ctx.Battery ? "every question given in detail needs one." : "every question of the exam needs one.")}", dropped));
        }
    }

    /// <summary>Rule 15: every question listed under QUESTIONS NEEDING A NOTE has a note; a warning with nothing to drop.</summary>
    private static void CheckNoteCoverage(Context ctx, HashSet<int> noted, List<BenchmarkReportValidationNote> notes)
    {
        var missing = ctx.QuestionsNeedingNote.Where(n => !noted.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            Issue(notes, MissingQuestionNoteRule, "questionNotes",
                $"No note for {string.Join(", ", missing.Select(ctx.Reference))}: every question listed under QUESTIONS NEEDING A NOTE needs one.");
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
            : $"\"{key}\" is not a slot of the {DocumentName(ctx.Spec.Audience)}; its slots are {string.Join(", ", ctx.RequiredSlots)}.";

    private static string UnusedListMessage(BenchmarkReportAudienceSpec spec, string array)
        => $"The {DocumentName(spec.Audience)} does not use \"{array}\"; leave it out.";

    private static string DocumentName(BenchmarkReportAudience audience) => BenchmarkReportRenderService.AudienceName(audience);

    private static string SlotName(string slot) => slot switch
    {
        BenchmarkReportSlots.Abstract => "The abstract",
        BenchmarkReportSlots.Meaning => "\"What this means for use as a game assistant\"",
        BenchmarkReportSlots.Confidence => "\"How reliable this result is\"",
        BenchmarkReportSlots.WhyItScored => "\"Why it scored this way\"",
        BenchmarkReportSlots.WhatWorked => "\"What worked well\"",
        BenchmarkReportSlots.Comparison => "\"How it compares\"",
        BenchmarkReportSlots.Limitations => "The limitations paragraph",
        BenchmarkReportSlots.OverseerChat => "\"The Overseer chat and its tools\"",
        BenchmarkReportSlots.BenchmarkSystem => "\"The benchmarking system\"",
        BenchmarkReportSlots.ModelResult => "\"The model's result\"",
        _ => $"The \"{slot}\" slot"
    };

    private static string LowerFirst(string text)
        => text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>Every rule but the warning rules 12 to 19 removes the offending item or paragraph.</summary>
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

    private static BenchmarkReportValidationNote Note(int rule, string location, string message, bool dropped = false)
        => new() { Rule = rule, Location = location, Message = message, Dropped = dropped };

    private static void Issue(List<BenchmarkReportValidationNote> notes, int rule, string location, string message)
        => notes.Add(Note(rule, location, message));

    private static void Dropped(List<BenchmarkReportValidationNote> notes, int rule, string location, string message)
        => notes.Add(Note(rule, location, message, dropped: true));

    private static IEnumerable<BenchmarkReportValidationNote> MarkDropped(IEnumerable<BenchmarkReportValidationNote> issues)
        => issues.Select(n => Note(n.Rule, n.Location, n.Message, dropped: true));

    private static BenchmarkReportWriterItem CloneItem(BenchmarkReportWriterItem item) => new()
    {
        Text = item.Text,
        Questions = new List<int>(item.Questions ?? new List<int>()),
        Evidence = new List<string>(item.Evidence ?? new List<string>())
    };

    private static BenchmarkReportWriterRecommendation CloneRecommendation(BenchmarkReportWriterRecommendation item) => new()
    {
        For = item.For,
        Text = item.Text,
        Questions = new List<int>(item.Questions ?? new List<int>()),
        Evidence = new List<string>(item.Evidence ?? new List<string>())
    };

    private static BenchmarkReportLead CloneLead(BenchmarkReportLead item) => new()
    {
        Triage = item.Triage,
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

            Spec = BenchmarkReportSlots.For(audience);
            RequiredSlots = Spec.SlotsFor(hasPeers: sheet.Peers.Count > 0);
            FactKeys = new HashSet<string>(sheet.Facts.Select(f => f.Key), StringComparer.Ordinal);
            _zeroDisplays = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fact in sheet.Facts.Where(f => f.Available && (f.Display ?? string.Empty).TrimStart().StartsWith('0')))
            {
                _zeroDisplays.TryAdd(fact.Key, fact.Display.Trim());
            }
            PeerLetters = new HashSet<string>(sheet.Peers.Select(p => p.Letter), StringComparer.Ordinal);
            OrderedQuestions = sheet.Questions.Select(q => q.Number).Distinct().OrderBy(n => n).ToList();
            Questions = new HashSet<int>(OrderedQuestions);
            QuestionsNeedingNote = BenchmarkReportPackPrompt.QuestionsNeedingNote(sheet);
            QuestionsNeedingTopic = BenchmarkReportPackPrompt.QuestionsNeedingTopic(sheet);
            Battery = sheet.Battery != null;
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

            var subjectNames = new[] { sheet.SubjectLabel, sheet.SubjectDisplayName, sheet.SubjectModelId, sheet.SubjectProvider };

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

        public BenchmarkReportAudienceSpec Spec { get; }

        /// <summary>The sheet is a battery's: its questions are referred to as <c>S&lt;suite&gt;-Q&lt;n&gt;</c>.</summary>
        public bool Battery { get; }

        /// <summary>The questions that need a topic where the document requires topics.</summary>
        public IReadOnlyList<int> QuestionsNeedingTopic { get; }

        /// <summary>How the document refers to a question: its battery reference, else <c>Q&lt;n&gt;</c>.</summary>
        public string Reference(int number) => _referenceByNumber.TryGetValue(number, out string? reference) ? reference : Q(number);

        /// <summary>The text is a battery question's reference, exactly as its QUESTIONS row shows it.</summary>
        public bool IsReference(string text) => _numberByReference.ContainsKey(text);

        /// <summary>The display of an available fact whose display starts with <c>0</c>; null for any other key.</summary>
        public string? ZeroDisplay(string key) => _zeroDisplays.TryGetValue(key, out string? display) ? display : null;

        /// <summary>The audience's slots this sheet requires: the peer-only slots only when it has peers.</summary>
        public IReadOnlyList<string> RequiredSlots { get; }

        public IReadOnlyList<int> QuestionsNeedingNote { get; }

        /// <summary>The peer's <c>peer.X.intervalOverlap</c> fact is true.</summary>
        public bool OverlapsSubject(string letter) => _overlappingPeers.Contains(letter);

        /// <summary>The peer's <c>peer.X.pairedExcludesZero</c> fact is true.</summary>
        public bool PairedExcludesZero(string letter) => _pairedExcludesZeroPeers.Contains(letter);
        public HashSet<string> FactKeys { get; }
        public HashSet<string> PeerLetters { get; }
        public HashSet<int> Questions { get; }
        public List<int> OrderedQuestions { get; }
        public Dictionary<string, BenchmarkReportFindingRow> Rows { get; }

        public bool IsValidToken(string inner)
        {
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
