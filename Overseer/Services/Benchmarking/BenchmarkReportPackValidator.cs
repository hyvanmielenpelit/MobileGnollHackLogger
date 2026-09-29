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
/// <para>Rules 2, 3, 8, 9, 10, 11 and 12 apply to every prose string: the headline, each paragraph of
/// each section, and the text of every item, topic and note. A section's paragraphs are checked one by
/// one, so <see cref="DropInvalid"/> can remove only the offending ones.</para>
///
/// <list type="number">
/// <item>Structure: headline and required slots present and non-empty; no unknown slots; no lists the
/// audience does not use; item texts non-empty; <c>for</c> and <c>triage</c> from their fixed sets.</item>
/// <item>Tokens: <c>{{key}}</c> names a fact, <c>{{peer:X}}</c> a peer letter, <c>{{subject}}</c> the subject;
/// written exactly, without inner spaces; no stray braces.</item>
/// <item>No bare digit once tokens, known names and <c>Q&lt;n&gt;</c> / <c>R&lt;n&gt;</c> references are masked.</item>
/// <item>Every question number exists in the subject's exam; topics cover every question where required.</item>
/// <item>Every evidence id and <c>R&lt;n&gt;</c> reference exists; strengths, weaknesses and leads cite
/// one, and so do the recommendations of the Report for AI Researchers and Developers.</item>
/// <item>A strength cites no weakness row and a weakness no strength row; a finding citing only
/// Conflicting rows says the graders disagree.</item>
/// <item>Word and item limits, the Executive Summary's slot and item word caps, and the report's
/// slot word caps and recommendation count included.</item>
/// <item>No headings, Markdown tables or HTML.</item>
/// <item>No run of <see cref="ShingleLength"/> words shared with the content snapshot.</item>
/// <item>No peer name, label, model id or provider other than the subject's own provider.</item>
/// <item>No significance claims. Matching is whole-word, so <c>insignificant</c> passes.</item>
/// <item>US English: no word from <see cref="BritishSpellings"/>. It asks for the repair turn, but
/// <see cref="DropInvalid"/> keeps the text and records the note instead of dropping it.</item>
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

    /// <summary>Each strength and weakness of the Executive Summary.</summary>
    public const int ExecutiveItemMaxWords = 30;

    /// <summary>Recommendations the Report for AI Researchers and Developers holds.</summary>
    public const int TechnicalReportMaxRecommendations = 6;

    /// <summary>The rule number of the US English check, whose notes never drop an item.</summary>
    public const int UsSpellingRule = 12;

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

    private enum ItemKind
    {
        Strength,
        Weakness,
        Recommendation,
        Lead,
    }

    /// <summary>How many recommendations the audience's document holds.</summary>
    public static int MaxRecommendations(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.TechnicalReport ? TechnicalReportMaxRecommendations : int.MaxValue;

    /// <summary>Rule 5: the Report for AI Researchers and Developers backs every recommendation with evidence.</summary>
    public static bool RecommendationsRequireEvidence(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.TechnicalReport;

    /// <summary>The word cap of a slot, or null when it has none.</summary>
    public static int? SlotMaxWords(BenchmarkReportAudience audience, string slot) => slot switch
    {
        BenchmarkReportSlots.Abstract => AbstractMaxWords,
        BenchmarkReportSlots.Meaning when audience == BenchmarkReportAudience.ExecutiveSummary => MeaningMaxWords,
        BenchmarkReportSlots.Confidence when audience == BenchmarkReportAudience.ExecutiveSummary => ConfidenceMaxWords,
        BenchmarkReportSlots.WhyItScored when audience == BenchmarkReportAudience.TechnicalReport => WhyItScoredMaxWords,
        BenchmarkReportSlots.WhatWorked when audience == BenchmarkReportAudience.TechnicalReport => WhatWorkedMaxWords,
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
        foreach (string slot in spec.RequiredSlots)
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
            }

            if (SlotMaxWords(audience, slot) is int cap && WordCount(text) > cap)
            {
                Issue(notes, 7, location, $"{SlotName(slot)} has {WordCount(text).ToString(CultureInfo.InvariantCulture)} words; the limit is {cap.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        foreach (string key in ExtraSlots(spec, sections))
        {
            Issue(notes, 1, SectionLocation(key), UnknownSlotMessage(spec, key));
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
                Issue(notes, 4, location, $"{Q(topic.Question)} already has a topic in an earlier entry.");
            }
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
                notes.AddRange(NoteIssues(ctx, note, location));
                if (!noteSeen.Add(note.Question) && ctx.Questions.Contains(note.Question))
                {
                    Issue(notes, 4, location, $"{Q(note.Question)} already has a note in an earlier entry.");
                }
            }
        }
        else if (questionNotes.Count > 0)
        {
            Issue(notes, 1, "questionNotes", UnusedListMessage(spec, "questionNotes"));
        }

        if (spec.UsesLeads)
        {
            CheckItems(ctx, "leads", output.Leads, ItemKind.Lead, int.MaxValue, notes);
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
    /// question topics are recorded but not fatal, and so is a rule 12 spelling: its text is kept.
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
        foreach (string slot in spec.RequiredSlots)
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

        foreach (string key in ExtraSlots(spec, sections))
        {
            Dropped(notes, 1, SectionLocation(key), UnknownSlotMessage(spec, key));
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
                issues.Add(Note(4, location, $"{Q(topic.Question)} already has a topic in an earlier entry."));
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
                    issues.Add(Note(4, location, $"{Q(note.Question)} already has a note in an earlier entry."));
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
        }
        else if (questionNotes.Count > 0)
        {
            Dropped(notes, 1, "questionNotes", UnusedListMessage(spec, "questionNotes"));
        }

        if (spec.UsesLeads)
        {
            copy.Leads = CleanItems(ctx, "leads", output.Leads, ItemKind.Lead, int.MaxValue, notes, CloneLead);
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
        masked = QuestionRefRegex.Replace(masked, " ");
        masked = RowRefRegex.Replace(masked, " ");
        var digit = DigitWordRegex.Match(masked);
        if (digit.Success)
        {
            Issue(notes, 3, location, $"Contains the digit form \"{digit.Value}\": place figures only as {{{{key}}}} tokens, write counts as number words, and refer to questions as Q<n>.");
        }

        // Rule 4: question references.
        var missingQuestions = QuestionRefRegex.Matches(stripped)
            .Where(m => !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || !ctx.Questions.Contains(n))
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missingQuestions.Count > 0)
        {
            Issue(notes, 4, location, $"{string.Join(", ", missingQuestions)} {(missingQuestions.Count == 1 ? "is" : "are")} not a question of the subject's exam.");
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
            Issue(notes, 11, location, $"Uses \"{string.Join("\", \"", claims)}\": the comparison runs no significance test, so say only whether the intervals overlap.");
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
            if (qMatch.Success)
            {
                if (!int.TryParse(qMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || !ctx.Questions.Contains(n))
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
            Issue(notes, 4, location, $"Evidence {string.Join(", ", badQuestionIds.Distinct(StringComparer.Ordinal))} {(badQuestionIds.Count == 1 ? "refers" : "refer")} to no question of the subject's exam.");
        }
        if (unknownIds.Count > 0)
        {
            Issue(notes, 5, location, $"Unknown evidence id{Plural(unknownIds.Count)} {string.Join(", ", unknownIds.Distinct(StringComparer.Ordinal))}: cite a fact key, Q<n> or a finding row id from the data.");
        }

        bool requiresEvidence = kind is ItemKind.Strength or ItemKind.Weakness or ItemKind.Lead
            || (kind == ItemKind.Recommendation && RecommendationsRequireEvidence(ctx.Spec.Audience));
        if (requiresEvidence && evidence.Count == 0)
        {
            Issue(notes, 5, location, "Cites no evidence: give at least one fact key, Q<n> or finding row id.");
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

        var missing = ctx.OrderedQuestions.Where(n => !covered.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            notes.Add(Note(4, "questionTopics", $"No topic for {string.Join(", ", missing.Select(Q))}: every question of the exam needs one.", dropped));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static bool IsConflicting(BenchmarkReportFindingRow row)
        => string.Equals(row.Status?.Trim(), "Conflicting", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtraSlots(BenchmarkReportAudienceSpec spec, Dictionary<string, string> sections)
        => sections.Keys
            .Where(k => !spec.RequiredSlots.Contains(k, StringComparer.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal);

    private static string UnknownSlotMessage(BenchmarkReportAudienceSpec spec, string key)
        => $"\"{key}\" is not a slot of the {DocumentName(spec.Audience)}; its slots are {string.Join(", ", spec.RequiredSlots)}.";

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
        _ => $"The \"{slot}\" slot"
    };

    private static string LowerFirst(string text)
        => text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>Every rule but rule 12 removes the offending item or paragraph.</summary>
    private static bool Blocks(BenchmarkReportValidationNote note) => note.Rule != UsSpellingRule;

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
            FactKeys = new HashSet<string>(sheet.Facts.Select(f => f.Key), StringComparer.Ordinal);
            PeerLetters = new HashSet<string>(sheet.Peers.Select(p => p.Letter), StringComparer.Ordinal);
            OrderedQuestions = sheet.Questions.Select(q => q.Number).Distinct().OrderBy(n => n).ToList();
            Questions = new HashSet<int>(OrderedQuestions);

            Rows = new Dictionary<string, BenchmarkReportFindingRow>(StringComparer.Ordinal);
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
                    string qn = Q(q.Number);
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

        public BenchmarkReportAudienceSpec Spec { get; }
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
