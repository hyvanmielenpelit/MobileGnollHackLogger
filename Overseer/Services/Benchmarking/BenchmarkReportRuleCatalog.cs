namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>What a validation rule does to the text it flags.</summary>
public enum BenchmarkReportRuleSeverity
{
    /// <summary>The offending paragraph or item is removed from the stored document.</summary>
    RemovesItem,

    /// <summary>The text is kept and the note is recorded; it asks for the repair turn.</summary>
    Warning,

    /// <summary>The document cannot be stored: a missing or invalid headline, or a required section left empty.</summary>
    FailsDocument
}

/// <summary>One validation rule as a reader sees it.</summary>
/// <param name="Rule">The rule number of <see cref="BenchmarkReportPackValidator"/>.</param>
/// <param name="Name">A short name, e.g. "Digit in prose".</param>
/// <param name="Meaning">What the rule checks, in one plain sentence.</param>
/// <param name="Severity">What the rule does to the text it flags.</param>
/// <param name="ForReader">What a flagged item means for someone reading the document.</param>
/// <param name="Fix">One instruction to the writer that clears the rule; holds no digit.</param>
public sealed record BenchmarkReportRuleInfo(
    int Rule,
    string Name,
    string Meaning,
    BenchmarkReportRuleSeverity Severity,
    string ForReader,
    string Fix);

/// <summary>
/// The plain-language catalog of the report-pack validation rules: names, meanings, severities and fix
/// instructions shared by the repair message, the job diagnostics and the UI. The rule numbers and the
/// warning set are <see cref="BenchmarkReportPackValidator"/>'s.
/// </summary>
public static class BenchmarkReportRuleCatalog
{
    /// <summary>The wire value of <see cref="BenchmarkReportRuleSeverity.RemovesItem"/>.</summary>
    public const string RemovesItemSeverity = "removesItem";

    /// <summary>The wire value of <see cref="BenchmarkReportRuleSeverity.Warning"/>.</summary>
    public const string WarningSeverity = "warning";

    /// <summary>The wire value of <see cref="BenchmarkReportRuleSeverity.FailsDocument"/>.</summary>
    public const string FailsDocumentSeverity = "failsDocument";

    private const string Kept = "The text was kept; read the flagged sentence with care.";
    private const string Removed = "The flagged paragraph or item was removed, so the document says less there than the writer wrote.";

    private static readonly IReadOnlyDictionary<int, BenchmarkReportRuleInfo> Rules = new[]
    {
        R(1, "Missing or unexpected section",
            "The headline and every required section are present and non-empty, and no section or list the document does not use is filled.",
            BenchmarkReportRuleSeverity.FailsDocument,
            "A missing headline or required section fails the document; an unexpected section or list is removed.",
            "Fill every required section and the headline, and leave out every section and list the document does not use."),
        R(2, "Unknown or malformed fact token",
            "Every {{token}} names a fact of the fact sheet and is written exactly, with no stray braces.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Copy each token exactly as the fact sheet lists it, with no inner spaces, and remove every token the sheet does not have."),
        R(3, "Digit in prose",
            "Prose holds no number of its own: every figure comes from a fact token, so it matches the data.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Replace every number with the fact token that holds it; write no figure yourself."),
        R(4, "Unknown question or missing topic",
            "Every question cited exists in the exam, is written in the sheet's own form, and has a topic where one is required.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite only questions of the fact sheet, in its own reference form, and give each cited question its topic."),
        R(5, "Unknown evidence reference",
            "Every evidence id and row reference exists, and every finding cites one.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite only evidence ids and row references the fact sheet lists, at least one per finding."),
        R(6, "Finding contradicts its evidence",
            "A strength cites no weakness row and a weakness no strength row; a finding on conflicting rows says the graders disagree.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite rows that support the finding's direction, and say the graders disagree when every cited row is conflicting."),
        R(7, "Over the word or item limit",
            "Each section, item and list stays within its word or item cap.",
            BenchmarkReportRuleSeverity.RemovesItem,
            "The text past the limit was removed; the rest of the section stands.",
            "Shorten the section or list to its cap; keep the most important points."),
        R(8, "Heading, table or HTML in prose",
            "Prose holds no Markdown heading, table or HTML; the document supplies its own structure.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Write plain sentences only, with no headings, tables or HTML."),
        R(9, "Text copied from an answer",
            "Prose shares no long run of words with the graded answers or rubrics.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Describe the answers in your own words instead of quoting them."),
        R(10, "Model name written out",
            "Prose names no peer model, model id or other provider; models are named by their tokens.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Name every model with its model or peer token, never by its name."),
        R(11, "Significance claim",
            "Prose does not call a result significant; the document's paired tests say what is established.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Remove the word significant and cite the paired-result token instead."),
        R(12, "British spelling",
            "Prose uses US English spelling.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Use US English spelling throughout."),
        R(13, "Interval called narrow or wide",
            "The reliability section does not call the quality interval narrow, wide, tight or broad.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Cite the interval token instead of describing the interval's width."),
        R(14, "Claim verifier in the headline",
            "The headline does not mention the claim verifier, whose refutations are advisory.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Leave the claim verifier out of the headline."),
        R(16, "Ranking without the overlap caveat",
            "A sentence that ranks two models whose intervals overlap says so, or cites the token that does.",
            BenchmarkReportRuleSeverity.Warning,
            "The ranking was kept, but the difference it states may be sampling noise.",
            "In each sentence that ranks models, say overlap or not established, or cite the pair's interval-overlap or paired-result token; write one sentence per ranking."),
        R(17, "Hype word",
            "Prose holds no hype or filler word.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Replace the hype word with a plain, measured one."),
        R(19, "Negation before a zero",
            "A negation does not precede a fact whose value is zero, which would read as a double negative.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Drop the negation, or cite the fact without it."),
        R(BenchmarkBatteryReportFacts.PromptBudgetRule, "Question detail left out",
            "The battery prompt left out some question detail to stay within its budget.",
            BenchmarkReportRuleSeverity.Warning,
            "Some questions were summarized rather than given in full to the writer.",
            "No action: this note records the prompt's budget."),
        R(BenchmarkReportPackValidator.ModelCoverageRule, "Model missing from the list",
            "The models list has an entry for every covered model.",
            BenchmarkReportRuleSeverity.Warning,
            "At least one model has no entry of its own in the list.",
            "Give every covered model an entry in the models list."),
        R(BenchmarkReportPackValidator.ChatChangeClaimRule, "Change claim without a verdict",
            "A claim that something changed cites a decisive verdict, estimate or attribution in the same sentence.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite the endpoint's verdict, estimate or attribution token in each sentence that says something changed."),
        R(BenchmarkReportPackValidator.ChatIntentMechanismRule, "Intent or mechanism without a confirmed cause",
            "A claim about why a provider changed something cites a provider-confirmed cause.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Remove intent and mechanism words unless the sentence cites a provider-confirmed cause."),
        R(BenchmarkReportPackValidator.ChatCausalClaimRule, "Cause without an attribution",
            "A causal word cites an attribution token in the same sentence.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Use a causal word only beside an attribution token."),
        R(BenchmarkReportPackValidator.ChatPublicClaimRule, "Public claim without an established grade",
            "Wording fit for a public claim cites an Established grade in the same sentence.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Use public-claim wording only beside an Established grade token."),
        R(BenchmarkReportPackValidator.ChatInconclusiveMdeRule, "Inconclusive result without its detectable effect",
            "An inconclusive endpoint the document cites also cites its minimum detectable effect.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Cite the minimum detectable effect of every inconclusive endpoint you mention."),
        R(BenchmarkReportPackValidator.ChatHoursRule, "All-hours wording without the time-of-day fact",
            "Wording about all hours cites a true time-of-day fact, and the document cites the hours where the periods share any.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite the time-of-day fact beside any all-hours wording, and cite the shared hours."),
        R(BenchmarkReportPackValidator.ChatProviderReportRule, "Provider claim without a provider-side attribution",
            "A claim about the model or its serving cites a provider-side attribution, and the ruled-out section cites every Overseer event.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Cite a provider-side attribution beside each claim about the model, and cite every Overseer event under what was ruled out."),
        R(BenchmarkReportPackValidator.ChatReadableTextRule, "Unreadable text",
            "Prose holds no long hex string, JSON, event identifier or fact kept from the writer.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Write readable sentences with no identifiers, hashes or JSON."),
        R(BenchmarkReportPackValidator.ChatRepetitionRule, "Repetition or spliced sentence",
            "Prose holds no slot-filling repetition and no sentence spliced at a full stop.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Rewrite repeated operands and spliced sentences as plain, separate sentences."),
        R(BenchmarkReportPackValidator.ComparisonTokenShapeRule, "Fact placed in the wrong sentence shape",
            "A sentence-valued fact stands as its own sentence, a clause-valued fact follows no model or verb of its own, and counts are labeled.",
            BenchmarkReportRuleSeverity.Warning, Kept,
            "Put each sentence-valued fact (a statement, known fact or check) on its own as a full sentence, and use a short token such as a grade, share or a hypothesis' .ref inside your own sentences."),
        R(BenchmarkReportPackValidator.RootCauseEvidenceCausalRule, "Causal word in the evidence",
            "The scope, the evidence sections and the observations describe what was measured, without causal words.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Describe what was measured in the evidence and scope sections; keep every causal word for the hypotheses and conclusions."),
        R(BenchmarkReportPackValidator.RootCauseCausalClaimRule, "Cause without a hypothesis",
            "A causal word cites a hypothesis graded Low or higher, hedged when Low, or an axis token in the same sentence, so every cause stated has been tested; a Not supported hypothesis is cited only to rule a cause out.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Use a causal word only in a sentence that cites a hypothesis graded Low or higher (one of its tokens, such as its .ref) or an axis token, with may, possibly or consistent with when the hypothesis is Low; cite a Not supported hypothesis only as not due to."),
        R(BenchmarkReportPackValidator.RootCauseCertaintyRule, "Certainty without support",
            "A certainty word cites a High hypothesis or an established axis status in the same sentence.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Use a certainty word only beside a High hypothesis token or an established axis status token; otherwise leave it out."),
        R(BenchmarkReportPackValidator.RootCauseWriterHypothesisRule, "Incomplete writer hypothesis",
            "A writer hypothesis has an axis, statement, evidence for, refuted-if statement and known locus, at confidence Low, citing only models and facts that exist.",
            BenchmarkReportRuleSeverity.RemovesItem, Removed,
            "Give every writer hypothesis all of its parts at confidence Low, citing only existing models and facts."),
        R(BenchmarkReportPackValidator.RootCauseAxisStatusRule, "Conclusions leave an axis out",
            "The conclusions cite every axis' status, so each axis gets a verdict.",
            BenchmarkReportRuleSeverity.Warning,
            "The conclusions do not give a writer's verdict on every axis; the axis table still does.",
            "Cite every axis' status token in the conclusions, one paragraph per axis."),
        R(BenchmarkReportPackValidator.BareModelLetterRule, "Model named by its bare letter",
            "A comparison names each model only through its model token, never as Model X.",
            BenchmarkReportRuleSeverity.Warning,
            "The text was kept; a named copy prints the models' names in place of the letters where it can.",
            "Replace every Model X or Models X and Y with the models' model tokens."),
    }.ToDictionary(r => r.Rule);

    private static readonly IReadOnlyDictionary<string, string> ListLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["observations"] = "What the graders recorded",
        ["writerHypotheses"] = "The writer's hypotheses",
        ["models"] = "Models",
        ["strengths"] = "Strengths",
        ["weaknesses"] = "Weaknesses",
        ["questionTopics"] = "Question topics",
    };

    private static readonly IReadOnlyDictionary<string, string> PartLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["statement"] = "statement",
        ["refutedIf"] = "refuted-if statement",
        ["for"] = "evidence for",
        ["against"] = "evidence against",
        ["points"] = "point",
    };

    private static readonly IReadOnlyDictionary<string, string> ExecutiveSlotTitles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BenchmarkReportSlots.Comparison] = "How it compares",
        [BenchmarkReportSlots.Meaning] = "What this means for use as a game assistant",
        [BenchmarkReportSlots.Confidence] = "How reliable this result is",
        [BenchmarkReportSlots.Overview] = "The comparison in one paragraph",
        [BenchmarkReportSlots.WhichModel] = "Which model to use",
        [BenchmarkReportSlots.TradeOffs] = "Trade-offs",
        [BenchmarkReportSlots.Reliability] = "How reliable this is",
        [BenchmarkReportSlots.AgainstReference] = "Compared with the reference model",
    };

    private static readonly Regex ParagraphLocationRegex = new(@"^sections\.(?<slot>[A-Za-z]+)(?:\[p(?<p>\d+)\])?$", RegexOptions.CultureInvariant);
    private static readonly Regex ItemLocationRegex = new(@"^(?<list>[A-Za-z]+)\[(?<i>\d+)\](?<rest>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex PartRegex = new(@"^\.(?<part>[A-Za-z]+)(?:\[(?<i>\d+)\])?", RegexOptions.CultureInvariant);

    /// <summary>Every rule of the catalog, by number.</summary>
    public static IReadOnlyCollection<BenchmarkReportRuleInfo> All => Rules.Values.OrderBy(r => r.Rule).ToList();

    /// <summary>The rule's entry; a rule the catalog does not know gets a generic one.</summary>
    public static BenchmarkReportRuleInfo Get(int rule)
        => Rules.TryGetValue(rule, out BenchmarkReportRuleInfo? info)
            ? info
            : new BenchmarkReportRuleInfo(
                rule,
                "Rule " + rule.ToString(CultureInfo.InvariantCulture),
                "A report-pack validation rule.",
                BenchmarkReportPackValidator.IsWarningRule(rule) ? BenchmarkReportRuleSeverity.Warning : BenchmarkReportRuleSeverity.RemovesItem,
                BenchmarkReportPackValidator.IsWarningRule(rule) ? Kept : Removed,
                "Follow the message of each issue.");

    /// <summary>The rule's short name.</summary>
    public static string Name(int rule) => Get(rule).Name;

    /// <summary>
    /// What a note of the rule does at its location: a warning rule warns; a non-warning issue on the
    /// headline, or a rule-1 issue that was not dropped (a missing section), fails the document; every
    /// other issue removes its paragraph or item.
    /// </summary>
    public static BenchmarkReportRuleSeverity SeverityFor(int rule, string location, bool dropped)
    {
        if (BenchmarkReportPackValidator.IsWarningRule(rule) || rule == BenchmarkBatteryReportFacts.PromptBudgetRule)
        {
            return BenchmarkReportRuleSeverity.Warning;
        }
        if (string.Equals(location, "headline", StringComparison.Ordinal) || (rule == 1 && !dropped))
        {
            return BenchmarkReportRuleSeverity.FailsDocument;
        }
        return BenchmarkReportRuleSeverity.RemovesItem;
    }

    /// <summary>The wire value of a severity: <c>removesItem</c>, <c>warning</c> or <c>failsDocument</c>.</summary>
    public static string SeverityValue(BenchmarkReportRuleSeverity severity) => severity switch
    {
        BenchmarkReportRuleSeverity.Warning => WarningSeverity,
        BenchmarkReportRuleSeverity.FailsDocument => FailsDocumentSeverity,
        _ => RemovesItemSeverity
    };

    /// <summary>A severity in words: "removes the item", "warning, text kept" or "fails the document".</summary>
    public static string SeverityLabel(BenchmarkReportRuleSeverity severity) => severity switch
    {
        BenchmarkReportRuleSeverity.Warning => "warning, text kept",
        BenchmarkReportRuleSeverity.FailsDocument => "fails the document",
        _ => "removes the item"
    };

    /// <summary>
    /// A validation location in words: <c>sections.conclusions[p1]</c> becomes "Conclusions, paragraph 1",
    /// <c>observations[2]</c> "What the graders recorded, item 3". <paramref name="slotTitles"/> names the
    /// document's sections; without it, or for a slot it lacks, the known titles of every audience are used.
    /// A location of no known shape is returned unchanged.
    /// </summary>
    public static string LocationLabel(string location, IReadOnlyDictionary<string, string>? slotTitles = null)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;
        if (string.Equals(location, "headline", StringComparison.Ordinal)) return "Headline";
        if (ListLabels.TryGetValue(location, out string? listOnly)) return listOnly;

        Match section = ParagraphLocationRegex.Match(location);
        if (section.Success)
        {
            string title = SlotTitle(section.Groups["slot"].Value, slotTitles);
            return section.Groups["p"].Success ? $"{title}, paragraph {section.Groups["p"].Value}" : title;
        }

        Match item = ItemLocationRegex.Match(location);
        if (item.Success && int.TryParse(item.Groups["i"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            string list = item.Groups["list"].Value;
            string label = (ListLabels.TryGetValue(list, out string? l) ? l : list) + ", item " + (index + 1).ToString(CultureInfo.InvariantCulture);
            string rest = item.Groups["rest"].Value;
            while (rest.Length > 0)
            {
                Match part = PartRegex.Match(rest);
                if (!part.Success) return location;
                string name = part.Groups["part"].Value;
                label += ", " + (PartLabels.TryGetValue(name, out string? p) ? p : name);
                if (part.Groups["i"].Success && int.TryParse(part.Groups["i"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sub))
                {
                    label += " " + (sub + 1).ToString(CultureInfo.InvariantCulture);
                }
                rest = rest[part.Length..];
            }
            return label;
        }

        return location;
    }

    private static string SlotTitle(string slot, IReadOnlyDictionary<string, string>? slotTitles)
    {
        if (slotTitles is not null && slotTitles.TryGetValue(slot, out string? own)) return own;
        if (ExecutiveSlotTitles.TryGetValue(slot, out string? exec)) return exec;
        if (BenchmarkReportSlots.ChatConsistencySlotTitles.TryGetValue(slot, out string? chat)) return chat;
        if (BenchmarkReportSlots.RootCauseSlotTitles.TryGetValue(slot, out string? rca)) return rca;
        return slot;
    }

    /// <summary>The section titles of an audience's document, for <see cref="LocationLabel"/>; null when the defaults serve.</summary>
    public static IReadOnlyDictionary<string, string>? SlotTitlesFor(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.RootCauseAnalysis ? BenchmarkReportSlots.RootCauseSlotTitles : null;

    /// <summary>A stored validation note as the job diagnostics and the UI show it: with its rule name, severity and location in words.</summary>
    public static BenchmarkReportPackValidationIssueDto ToIssueDto(BenchmarkReportValidationNote note, IReadOnlyDictionary<string, string>? slotTitles = null)
        => new()
        {
            Rule = note.Rule,
            Location = note.Location,
            Message = note.Message,
            Dropped = note.Dropped,
            RuleName = Name(note.Rule),
            Severity = SeverityValue(SeverityFor(note.Rule, note.Location, note.Dropped)),
            LocationLabel = LocationLabel(note.Location, slotTitles),
            Text = note.Text
        };

    private static BenchmarkReportRuleInfo R(int rule, string name, string meaning, BenchmarkReportRuleSeverity severity, string forReader, string fix)
        => new(rule, name, meaning, severity, forReader, fix);
}
