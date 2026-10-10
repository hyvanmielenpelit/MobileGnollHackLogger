namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using ChatConsistencyEventText = global::Overseer.Services.ChatConsistency.ChatConsistencyEventText;

/// <summary>
/// The chat consistency documents (<see cref="BenchmarkReportFactSheet.IsChatConsistency"/>): the Overseer
/// chat with one model measured over a baseline and a comparison period, its control models lettered
/// as peers. Every document opens with the overall verdict and the verdict table, then the writer's
/// slots in slot order under their titles, each code-built block standing under the slot that
/// discusses it or in its default place. The blocks differ per audience:
///
/// <list type="bullet">
/// <item>The Executive Summary: a three-column verdict table of the computed endpoints with one sentence
/// for where the change came from, one sentence on the Overseer updates under "Our changes", a short
/// "How to read this", the analysis's data-quality notes and the evaluation terms.</item>
/// <item>The Report for AI Researchers and Developers and the Provider Issue Report: a five-column
/// verdict table with a note line per endpoint and a secondary measures table, the table of Overseer
/// updates, the control models per period, the attribution, the robustness table (researcher report
/// only), the full "How to read this", every limitation and the reproducibility list.</item>
/// <item>The Internal Improvement Brief: the researcher report's tables without p-values in the note
/// lines, no "How to read this", the data-quality notes and the reproducibility list without the price
/// card's rates.</item>
/// </list>
///
/// No block prints a hash, JSON or an internal field name; a document written before the readable
/// event facts renders its events from their stored raw values (<see cref="ChatConsistencyEventText"/>).
/// A named copy prints the control models by name; the Provider Issue Report never does. The text is the
/// same at every disclosure level, under its own stamp.
/// </summary>
public static partial class BenchmarkReportPackRenderer
{
    private const string ChatFullStamp = "INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.";
    private const string ChatSharedStamp = "Confidential. Unpublished chat consistency results. Review before sharing.";
    private const string ChatProviderStamp = "Confidential. Prepared for the model's provider.";

    private const string ChatLogRatioNote = @"\s*\(log ratio [^)]*\)";

    /// <summary>A code-rendered block of a chat consistency document.</summary>
    private enum ChatBlock
    {
        Verdicts,
        Events,
        Controls,
        Attributions,
        Robustness,
        Limitations,
        Reproducibility
    }

    /// <summary>
    /// The blocks a slot prints before its prose, per audience; a block no slot takes stands in its
    /// default place: the verdicts, events, controls and attributions after the overall verdict, the
    /// limitations and the reproducibility list at the end.
    /// </summary>
    private static ChatBlock[] ChatBlocksOf(BenchmarkReportAudience audience, string slot) => (audience, slot) switch
    {
        (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportSlots.OurChanges) => new[] { ChatBlock.Events },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.OverseerEvents) => new[] { ChatBlock.Events },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.EndpointResults) => new[] { ChatBlock.Verdicts },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Attribution) => new[] { ChatBlock.Attributions, ChatBlock.Controls },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Robustness) => new[] { ChatBlock.Robustness },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Limitations) => new[] { ChatBlock.Limitations },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Reproducibility) => new[] { ChatBlock.Reproducibility },
        (BenchmarkReportAudience.InternalBrief, BenchmarkReportSlots.ChangeEffects) => new[] { ChatBlock.Events },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.IssueSummary) => new[] { ChatBlock.Attributions },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.Measurements) => new[] { ChatBlock.Verdicts },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.RuledOut) => new[] { ChatBlock.Events, ChatBlock.Controls },
        _ => Array.Empty<ChatBlock>()
    };

    /// <summary>
    /// The blocks a document of the audience prints at all: the Executive Summary's controls stand in its
    /// overall verdict and its attribution under its verdict table, and it has neither a robustness
    /// table nor a reproducibility list; only the researcher report has a robustness table.
    /// </summary>
    private static bool ChatPrints(BenchmarkReportAudience audience, ChatBlock block) => block switch
    {
        ChatBlock.Controls or ChatBlock.Attributions or ChatBlock.Reproducibility => audience != BenchmarkReportAudience.ExecutiveSummary,
        ChatBlock.Robustness => audience == BenchmarkReportAudience.TechnicalReport,
        _ => true
    };

    /// <summary>What a block is, as the writer prompt names the blocks printed above a slot.</summary>
    private static string ChatBlockName(BenchmarkReportAudience audience, ChatBlock block) => block switch
    {
        ChatBlock.Verdicts => audience == BenchmarkReportAudience.ExecutiveSummary
            ? "the verdict table with one sentence on where the change came from, and where the chat stands in each period"
            : "the verdict table with a note line per endpoint, the endpoints not computable, the secondary measures and where the chat stands in each period",
        ChatBlock.Events => audience == BenchmarkReportAudience.ExecutiveSummary
            ? "one sentence listing the Overseer updates"
            : "the table of Overseer updates",
        ChatBlock.Controls => "the control models and the periods without a control",
        ChatBlock.Attributions => "the attribution",
        ChatBlock.Robustness => "the robustness table",
        ChatBlock.Limitations => "the limitations the analysis recorded",
        _ => "the reproducibility list"
    };

    /// <summary>The blocks printed under a slot's title, before its prose, in words; empty when none.</summary>
    internal static string ChatBlocksAboveText(BenchmarkReportAudience audience, string slot)
        => BenchmarkReportFormat.LetterList(ChatBlocksOf(audience, slot).Select(b => ChatBlockName(audience, b)).ToList());

    /// <summary>The blocks printed before the first slot, in words: the overall verdict and the blocks no slot takes.</summary>
    internal static string ChatBlocksBeforeSlotsText(BenchmarkReportAudience audience)
    {
        var spec = BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency);
        var attached = spec.RequiredSlots.SelectMany(slot => ChatBlocksOf(audience, slot)).ToHashSet();
        var names = new List<string>
        {
            "the overall verdict (the outcome, the endpoints not computable and why, the sample, the hours and the control runs)"
        };
        names.AddRange(ChatLeadingBlocks
            .Where(b => ChatPrints(audience, b) && !attached.Contains(b))
            .Select(b => ChatBlockName(audience, b)));
        return BenchmarkReportFormat.LetterList(names);
    }

    /// <summary>The blocks that stand after the overall verdict when no slot takes them.</summary>
    private static ChatBlock[] ChatLeadingBlocks => [ChatBlock.Verdicts, ChatBlock.Events, ChatBlock.Controls, ChatBlock.Attributions];

    /// <summary><c>Overseer Chat Consistency Report: &lt;model&gt;</c>, the title of every chat consistency document.</summary>
    public static string BuildChatConsistencyTitle(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return BenchmarkChatConsistencyReportFacts.TitlePrefix + sheet.SubjectLabel;
    }

    /// <summary>
    /// The stamp of a chat consistency document: internal at Full; below it, prepared for the model's
    /// provider in a Provider Issue Report and to be reviewed before sharing otherwise.
    /// </summary>
    public static string ChatConsistencyStamp(BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure)
        => disclosure == BenchmarkReportDisclosure.Full ? ChatFullStamp
            : audience == BenchmarkReportAudience.ProviderIssueReport ? ChatProviderStamp
            : ChatSharedStamp;

    private static void RenderChatConsistency(StringBuilder sb, Context ctx)
    {
        var audience = ctx.Document.Audience;
        var spec = BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency);
        var attached = spec.RequiredSlots.SelectMany(slot => ChatBlocksOf(audience, slot)).ToHashSet();

        ChatTitleBlock(sb, ctx);
        ChatOutOfDateBox(sb, ctx);
        ChatOverallVerdict(sb, ctx);

        Heading(sb, "## The result in one sentence");
        Line(sb, Prose(ctx, ctx.Writer.Headline));
        Line(sb);

        foreach (var block in ChatLeadingBlocks)
        {
            if (ChatPrints(audience, block) && !attached.Contains(block)) ChatBlockSection(sb, ctx, block, heading: true);
        }

        foreach (string slot in spec.RequiredSlots)
        {
            string title = BenchmarkReportSlots.ChatConsistencySlotTitles.TryGetValue(slot, out string? t) ? t : slot;
            Heading(sb, "## " + title);
            foreach (var block in ChatBlocksOf(audience, slot)) ChatBlockSection(sb, ctx, block, heading: false);
            Slot(sb, ctx, slot);
        }

        if (audience != BenchmarkReportAudience.InternalBrief)
        {
            ChatHowToRead(sb, ctx);
        }
        foreach (var block in new[] { ChatBlock.Limitations, ChatBlock.Reproducibility })
        {
            if (ChatPrints(audience, block) && !attached.Contains(block)) ChatBlockSection(sb, ctx, block, heading: true);
        }

        RemovedContent(sb, ctx);
        if (ctx.PrintsEvaluationTerms)
        {
            ChatEvaluationTerms(sb, ctx);
        }
        if (ctx.Options.IncludeDocumentFooter)
        {
            ChatFooter(sb, ctx);
        }
    }

    /// <summary>One code-rendered block: under its own heading in its default place, or under the slot that takes it.</summary>
    private static void ChatBlockSection(StringBuilder sb, Context ctx, ChatBlock block, bool heading)
    {
        bool executive = ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary;
        switch (block)
        {
            case ChatBlock.Verdicts:
                if (heading) Heading(sb, "## Verdicts by endpoint");
                if (executive) ChatExecutiveVerdictTable(sb, ctx);
                else ChatVerdictTable(sb, ctx);
                ChatLevelsTable(sb, ctx);
                Figures(sb, ctx, BenchmarkReportChartAnchor.ChatConsistencyResults);
                break;
            case ChatBlock.Events:
                if (heading) Heading(sb, "## Overseer updates between the periods");
                if (executive) ChatEventsSummary(sb, ctx);
                else ChatEventsTable(sb, ctx);
                Figures(sb, ctx, BenchmarkReportChartAnchor.ChatConsistencyEvents);
                break;
            case ChatBlock.Controls:
                if (heading) Heading(sb, "## Control models");
                ChatControls(sb, ctx);
                break;
            case ChatBlock.Attributions:
                if (heading) Heading(sb, "## Where the change came from");
                ChatAttributions(sb, ctx);
                break;
            case ChatBlock.Robustness:
                if (heading) Heading(sb, "## Robustness");
                ChatRobustnessTable(sb, ctx);
                break;
            case ChatBlock.Limitations:
                ChatLimitations(sb, ctx, heading);
                break;
            default:
                if (heading) Heading(sb, "## Reproducibility");
                ChatReproducibility(sb, ctx);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>The title, then, with <see cref="BenchmarkReportRenderOptions.IncludeFrontMatter"/>, the stamp and the facts list.</summary>
    private static void ChatTitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Line(sb, "# " + BuildChatConsistencyTitle(sheet));
        Line(sb);
        if (!ctx.Options.IncludeFrontMatter) return;

        Line(sb, "*" + ChatConsistencyStamp(ctx.Document.Audience, ctx.Options.Disclosure) + "*");
        Line(sb);
        Line(sb, "- **Document:** " + AudienceName(ctx.Document.Audience));
        Line(sb, "- **Date:** " + ctx.Document.CreatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Line(sb, "- **Analysis:** " + ChatAnalysisText(ctx));
        Line(sb, "- **Model:** " + ChatModelText(sheet));
        if (IsAvailable(ctx, "analysis.compared"))
        {
            Line(sb, "- **Compared:** " + OneLine(D(ctx, "analysis.compared")));
        }
        Line(sb, "- **Baseline period:** " + ChatPeriodText(ctx, "baseline"));
        Line(sb, "- **Comparison period:** " + ChatPeriodText(ctx, "comparison"));
        Line(sb, "- **Hours:** " + ChatHoursText(ctx));
        if (!string.IsNullOrWhiteSpace(sheet.SuiteName))
        {
            Line(sb, "- **Suites:** " + OneLine(sheet.SuiteName));
        }
        Line(sb, "- **Control models:** " + ChatControlsText(ctx));
        Line(sb, "- **Protocol:** " + ChatProtocolText(ctx));
        Line(sb);
    }

    /// <summary>
    /// The reasons the document's analysis is out of date, decided when the document is rendered: saved
    /// under an earlier analysis code version than <see cref="ChatConsistencyAnalysisCodeVersion"/>, or
    /// written after the operator confirmed changed inputs (<c>analysis.writtenOutOfDate</c>). Empty when current.
    /// </summary>
    internal static List<string> ChatOutOfDateReasons(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var reasons = new List<string>();
        int version = sheet.ChatConsistency?.AnalysisCodeVersion ?? 0;
        if (version <= 0
            && sheet.Facts.FirstOrDefault(f => f.Key == "analysis.codeVersion") is { Available: true, Value: JsonValue v }
            && v.TryGetValue(out int stored))
        {
            version = stored;
        }
        if (version > 0 && version < ChatConsistencyAnalysisCodeVersion)
        {
            reasons.Add("it was saved under analysis code version " + Inv(version) + ", and Overseer now analyzes under version "
                + Inv(ChatConsistencyAnalysisCodeVersion));
        }

        var written = sheet.Facts.FirstOrDefault(f => f.Key == "analysis.writtenOutOfDate");
        if (written is { Available: true } && written.Display.Contains("changed inputs", StringComparison.Ordinal))
        {
            reasons.Add("its runs, grades, controls, annotations or prices changed after it was saved");
        }
        return reasons;
    }

    /// <summary>The analysis code version a chat consistency document is current against.</summary>
    private static int ChatConsistencyAnalysisCodeVersion => global::Overseer.Services.ChatConsistency.ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion;

    /// <summary>Under the title block, in every audience: the warning that the analysis is out of date, when it is.</summary>
    private static void ChatOutOfDateBox(StringBuilder sb, Context ctx)
    {
        var reasons = ChatOutOfDateReasons(ctx.Sheet);
        if (reasons.Count == 0) return;

        Line(sb, "> **This document comes from an out-of-date analysis:** " + string.Join("; and ", reasons)
            + ". Saved analyses never change; analyze the same periods again for a current result before relying on this document.");
        Line(sb);
    }

    /// <summary>
    /// The outcome, each group of endpoints not computable with its reason (in plain words in the Executive
    /// Summary), the sample against the protocol's minimum, the hours and the control runs; outside the
    /// Executive Summary also the time strata. An established reliability increase is stated where there is one.
    /// </summary>
    private static void ChatOverallVerdict(StringBuilder sb, Context ctx)
    {
        bool executive = ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary;
        Heading(sb, "## Overall verdict");

        Line(sb, "- **Verdict:** " + (IsAvailable(ctx, "verdict.short") ? D(ctx, "verdict.short") : D(ctx, "verdict.overall")));

        foreach (var group in ChatNotComputableGroups(ctx, executive))
        {
            Line(sb, "- **" + UpperFirst(BenchmarkReportFormat.LetterList(group.Names)) + " not computable:** " + OneLine(group.Reason));
        }

        if (Fact(ctx, "sample.met") is { Available: true } met)
        {
            string sample = BenchmarkReportPackPrompt.IsTrue(met) || !IsAvailable(ctx, "sample.shortfall")
                ? D(ctx, "sample.met")
                : D(ctx, "sample.shortfall");
            Line(sb, "- **Sample:** " + UpperFirst(OneLine(sample)));
        }

        Line(sb, "- **Hours:** " + ChatScopeSentence(ctx));
        Line(sb, "- **Controls:** " + ChatControlsSentence(ctx));

        if (!executive)
        {
            Line(sb, "- **Time strata:** " + ChatStrataText(ctx));
        }

        if (Fact(ctx, "verdict.reliabilityIncreases") is { Available: true, Value: JsonValue increases }
            && increases.TryGetValue(out int count) && count > 0)
        {
            Line(sb, "- **" + Label("verdict.reliabilityIncreases") + ":** " + D(ctx, "verdict.reliabilityIncreases"));
        }
        Line(sb);
    }

    /// <summary>
    /// The Executive Summary's verdicts: <c>Measure · Result · What it means</c> for the computed endpoints,
    /// one line naming those not computable, and one sentence on where the change came from.
    /// </summary>
    private static void ChatExecutiveVerdictTable(StringBuilder sb, Context ctx)
    {
        var computed = ChatComputedEndpoints(ctx);
        if (computed.Count == 0)
        {
            Line(sb, "No endpoint could be computed; the overall verdict says why.");
            Line(sb);
        }
        else
        {
            Line(sb, "| Measure | Result | What it means |");
            Line(sb, "|---|---|---|");
            foreach (string id in computed)
            {
                string p = "endpoint." + id + ".";
                string verdict = ChatValue(ctx, p + "verdict") ?? D(ctx, p + "verdict");
                string result = UpperFirst(verdict) + (IsAvailable(ctx, p + "estimate") ? ": " + ChatFigure(ctx, p + "estimate") : string.Empty);
                Line(sb, "| " + Cell(ChatEndpointPlainName(ctx, id)) + " | " + Cell(result) + " | " + Cell(ChatMeaning(ctx, id, verdict)) + " |");
            }
            Line(sb);
            ChatOneUnitCaveat(sb, ctx);
        }

        var notComputable = ChatEndpointIds(ctx).Except(computed, StringComparer.Ordinal).ToList();
        if (computed.Count > 0 && notComputable.Count > 0)
        {
            Line(sb, "*Not computable: " + BenchmarkReportFormat.LetterList(notComputable.Select(id => ChatEndpointPlainName(ctx, id)).ToList())
                + "; the overall verdict says why.*");
            Line(sb);
        }

        Line(sb, "**Where the change came from.** " + ChatAttributionSentence(ctx, plain: true));
        Line(sb);
    }

    /// <summary>
    /// Under an endpoint table, when a period has one unit: the intervals and smallest detectable changes
    /// leave out run-to-run variation. Nothing on a sheet that does not count units.
    /// </summary>
    private static void ChatOneUnitCaveat(StringBuilder sb, Context ctx)
    {
        long? baseline = ChatNumber(ctx, "period.baseline.units");
        long? comparison = ChatNumber(ctx, "period.comparison.units");
        if (baseline != 1 && comparison != 1) return;

        string unit = ChatValue(ctx, "period.baseline.unitNoun") ?? "run";
        Line(sb, "*With one " + unit + " per period, the intervals and smallest detectable changes reflect question-to-question variation "
            + "only; run-to-run variation is not included, so the true uncertainty is larger.*");
        Line(sb);
    }

    /// <summary>
    /// "Where the chat stands": each period's descriptive level of quality, time to first answer text, answer
    /// streaming rate, work per turn, cost per question and failed answers, a measure not comparable marked so;
    /// nothing on a sheet without <c>level.*</c> facts.
    /// </summary>
    private static void ChatLevelsTable(StringBuilder sb, Context ctx)
    {
        bool Any(string measure) => IsAvailable(ctx, "level.baseline." + measure) || IsAvailable(ctx, "level.comparison." + measure);
        if (!new[] { "quality", "overallIndex", "timeToFirstAnswerText", "streamingRate", "outputTokens", "costPerQuestion", "answers" }.Any(Any)) return;

        string Value(string period, params string[] measures)
        {
            foreach (string measure in measures)
            {
                string key = "level." + period + "." + measure;
                if (IsAvailable(ctx, key)) return (measure == "overallIndex" ? "Overall Index " : string.Empty) + D(ctx, key);
            }
            return NoValue;
        }

        string Measure(string name, string? endpoint)
        {
            string? kind = endpoint == null ? null : ChatValue(ctx, "endpoint." + endpoint + ".notComputedKind");
            return kind is "measurementChanged" or "noCommonStratum" ? name + " (not comparable)" : name;
        }

        var rows = new List<(string Measure, string Baseline, string Comparison)>
        {
            (Measure("Quality", "P1"), Value("baseline", "overallIndex", "quality"), Value("comparison", "overallIndex", "quality")),
            (Measure("Time to first answer text", "P2"), Value("baseline", "timeToFirstAnswerText"), Value("comparison", "timeToFirstAnswerText")),
            (Measure("Answer streaming rate", "P3"), Value("baseline", "streamingRate"), Value("comparison", "streamingRate")),
            (Measure("Work per turn (output tokens per answer)", "P4"), Value("baseline", "outputTokens"), Value("comparison", "outputTokens")),
            (Measure("Cost per question", "P5"), Value("baseline", "costPerQuestion"), Value("comparison", "costPerQuestion")),
            ("Failed answers", Value("baseline", "failedAnswers"), Value("comparison", "failedAnswers"))
        };

        Line(sb, "**Where the chat stands**");
        Line(sb);
        Line(sb, "| Measure | Baseline | Comparison |");
        Line(sb, "|---|---|---|");
        foreach (var (measure, baseline, comparison) in rows)
        {
            Line(sb, "| " + Cell(measure) + " | " + Cell(baseline) + " | " + Cell(comparison) + " |");
        }
        Line(sb);
        Line(sb, "*Descriptive levels of each period, not a comparison: the verdicts above say what changed.*");
        Line(sb);
    }

    /// <summary>What an endpoint's verdict means, in plain words, against its margin; with its grade where the verdict decides.</summary>
    private static string ChatMeaning(Context ctx, string id, string verdict)
    {
        string margin = IsAvailable(ctx, "protocol.margin." + id) ? " of " + D(ctx, "protocol.margin." + id) : string.Empty;
        string grade = ChatValue(ctx, "endpoint." + id + ".grade") ?? string.Empty;
        string graded = grade.Length == 0 ? string.Empty : "; graded " + grade;
        return verdict switch
        {
            "degraded" => "Worse than before, beyond the margin" + margin + graded,
            "improved" => "Better than before, beyond the margin" + margin + graded,
            "more work" => "More work per turn than before, beyond the margin" + margin + graded,
            "less work" => "Less work per turn than before, beyond the margin" + margin + graded,
            "equivalent" => "The same as before, within the margin" + margin + graded,
            "changed, negligible" => "A change too small to matter, inside the margin" + margin + graded,
            "inconclusive" => IsAvailable(ctx, "endpoint." + id + ".mde")
                ? "Undecided: these runs could detect only a change of " + ChatMde(ctx, id) + " or more"
                : "Undecided: these runs cannot tell a change from no change",
            _ => UpperFirst(verdict) + graded
        };
    }

    /// <summary>
    /// The verdicts outside the Executive Summary: <c>Endpoint · Change (95 % interval) · Margin · Verdict
    /// and grade · Smallest detectable</c> for the computed endpoints, a note line per endpoint (the 90 %
    /// interval, the Holm-adjusted p-value except in the Internal Improvement Brief, the note on the
    /// minimum detectable effect and the grade's reasons), the endpoints not computable with their
    /// reasons, and the secondary measures.
    /// </summary>
    private static void ChatVerdictTable(StringBuilder sb, Context ctx)
    {
        var all = ChatEndpointIds(ctx);
        if (all.Count == 0)
        {
            Line(sb, "The analysis recorded no primary endpoint.");
            Line(sb);
            return;
        }

        var computed = ChatComputedEndpoints(ctx);
        if (computed.Count > 0)
        {
            Line(sb, "| Endpoint | Change (95 % interval) | Margin | Verdict and grade | Smallest detectable |");
            Line(sb, "|---|---|---|---|---|");
            foreach (string id in computed)
            {
                string p = "endpoint." + id + ".";
                string change = IsAvailable(ctx, p + "estimate")
                    ? ChatFigure(ctx, p + "estimate") + (IsAvailable(ctx, p + "ci95") ? " (" + ChatFigure(ctx, p + "ci95") + ")" : string.Empty)
                    : NoValue;
                string verdict = UpperFirst(ChatValue(ctx, p + "verdict") ?? D(ctx, p + "verdict"))
                    + (ChatValue(ctx, p + "grade") is string grade ? ", " + grade : string.Empty);
                string margin = IsAvailable(ctx, "protocol.margin." + id) ? D(ctx, "protocol.margin." + id) : NoValue;
                string mde = IsAvailable(ctx, p + "mde") ? ChatMde(ctx, id) : NoValue;
                Line(sb, "| " + Cell(ChatEndpointName(ctx, id)) + " | " + Cell(change) + " | " + Cell(margin) + " | " + Cell(verdict) + " | " + Cell(mde) + " |");
            }
            Line(sb);
            ChatOneUnitCaveat(sb, ctx);

            bool pValues = ctx.Document.Audience != BenchmarkReportAudience.InternalBrief;
            var notes = computed
                .Select(id => (Id: id, Parts: ChatNoteParts(ctx, id, pValues)))
                .Where(n => n.Parts.Count > 0)
                .ToList();
            foreach (var (id, parts) in notes) Line(sb, "- **" + ChatEndpointName(ctx, id) + ":** " + string.Join("; ", parts));
            if (notes.Count > 0) Line(sb);
        }

        var notComputable = all.Except(computed, StringComparer.Ordinal).ToList();
        foreach (string id in notComputable)
        {
            var estimate = Fact(ctx, "endpoint." + id + ".estimate");
            string reason = estimate is { Available: false } ? NotAvailableReason(estimate) : "not computable.";
            Line(sb, "- **" + ChatEndpointName(ctx, id) + ", not computable:** " + OneLine(reason));
        }
        if (notComputable.Count > 0) Line(sb);

        Line(sb, "*Each change is the comparison period against the baseline period: a difference in the endpoint's unit, or a change in percent for a ratio, read against the endpoint's equivalence margin.*");
        Line(sb);

        ChatSecondaryTable(sb, ctx);
    }

    /// <summary>One computed endpoint's note line: its 90 % interval, adjusted p-value, minimum-detectable-effect note and grade reasons.</summary>
    private static List<string> ChatNoteParts(Context ctx, string id, bool pValues)
    {
        string p = "endpoint." + id + ".";
        var parts = new List<string>();
        if (IsAvailable(ctx, p + "ci90")) parts.Add("90 % interval " + ChatFigure(ctx, p + "ci90"));
        if (pValues && IsAvailable(ctx, p + "adjustedP")) parts.Add("Holm-adjusted p " + D(ctx, p + "adjustedP"));
        if (IsAvailable(ctx, p + "mdeNote")) parts.Add(OneLine(D(ctx, p + "mdeNote")).TrimEnd('.'));
        if (Fact(ctx, p + "legacyProxy") is { Available: true } proxy && BenchmarkReportPackPrompt.IsTrue(proxy)) parts.Add(OneLine(proxy.Display));
        if (Fact(ctx, p + "grade") is { Available: true } grade
            && ChatValue(ctx, p + "grade") is string value
            && grade.Display.StartsWith(value + ": ", StringComparison.Ordinal))
        {
            parts.Add(OneLine(grade.Display[(value.Length + 2)..]));
        }
        return parts;
    }

    /// <summary>
    /// The secondary measures: <c>Measure · Change (95 % interval) · Verdict</c> for the quality detail,
    /// tool use, reliability rates and every other secondary family but the descriptive shift function;
    /// nothing when the analysis recorded none.
    /// </summary>
    private static void ChatSecondaryTable(StringBuilder sb, Context ctx)
    {
        var rows = new List<(string Measure, string Change, string Verdict)>();
        var prefixes = ChatSecondaryPrefixes(ctx);

        // Every reliability rate zero in both periods: one row instead of one per rate.
        var reliability = prefixes.Where(p => p.StartsWith("reliability.", StringComparison.Ordinal)).ToList();
        bool Zero(string key) => Fact(ctx, key) is { Available: true, Value: JsonValue v } && v.TryGetValue(out double d) && d == 0.0;
        bool noFailures = reliability.Count > 0 && reliability.All(p => Zero(p + "baseline") && Zero(p + "comparison"));
        bool failuresRowAdded = false;

        foreach (string prefix in prefixes)
        {
            string measure = D(ctx, prefix + "name");
            if (noFailures && prefix.StartsWith("reliability.", StringComparison.Ordinal))
            {
                if (failuresRowAdded) continue;
                failuresRowAdded = true;
                var names = reliability.Select(p => LowerFirstWord(D(ctx, p + "name"))).ToList();
                rows.Add(("Failures of any kind", "none in either period (" + BenchmarkReportFormat.LetterList(names) + ")", "no established increase"));
                continue;
            }

            if (prefix.StartsWith("reliability.", StringComparison.Ordinal))
            {
                string rate = IsAvailable(ctx, prefix + "baseline") && IsAvailable(ctx, prefix + "comparison")
                    ? D(ctx, prefix + "baseline") + " → " + D(ctx, prefix + "comparison")
                    : NoValue;
                string verdict = Fact(ctx, prefix + "establishedIncrease") is { } increase && BenchmarkReportPackPrompt.IsTrue(increase)
                    ? "an established increase" + (IsAvailable(ctx, prefix + "adjustedP") ? ", adjusted p " + D(ctx, prefix + "adjustedP") : string.Empty)
                    : "no established increase";
                rows.Add((measure, rate, verdict));
                continue;
            }

            if (!IsAvailable(ctx, prefix + "estimate"))
            {
                string reason = Fact(ctx, prefix + "estimate") is { Available: false } missing ? OneLine(NotAvailableReason(missing)).TrimEnd('.') : string.Empty;
                rows.Add((measure, NoValue, reason.Length == 0 || reason == "Not computable" ? "not computable" : "not computable: " + LowerFirstWord(reason)));
                continue;
            }

            string change = ChatFigure(ctx, prefix + "estimate") + (IsAvailable(ctx, prefix + "ci95") ? " (" + ChatFigure(ctx, prefix + "ci95") + ")" : string.Empty);
            string reading = IsAvailable(ctx, prefix + "adjustedP")
                ? (Fact(ctx, prefix + "rejected") is { } rejected && BenchmarkReportPackPrompt.IsTrue(rejected) ? "differs" : "no difference shown")
                  + ", adjusted p " + D(ctx, prefix + "adjustedP")
                : IsAvailable(ctx, prefix + "p") ? "p " + D(ctx, prefix + "p") + ", not adjusted" : "descriptive";
            rows.Add((measure, change, reading));
        }
        if (rows.Count == 0) return;

        Line(sb, "Secondary measures:");
        Line(sb);
        Line(sb, "| Measure | Baseline → comparison, or change (95 % interval) | Verdict |");
        Line(sb, "|---|---|---|");
        foreach (var (measure, change, verdict) in rows)
        {
            Line(sb, "| " + Cell(measure) + " | " + Cell(change) + " | " + Cell(verdict) + " |");
        }
        Line(sb);
    }

    /// <summary>The key prefixes of the secondary results the table shows, in key order.</summary>
    private static List<string> ChatSecondaryPrefixes(Context ctx)
        => ctx.Sheet.Facts
            .Select(f => f.Key)
            .Where(k => k.EndsWith(".name", StringComparison.Ordinal))
            .Select(k => k[..^"name".Length])
            .Where(p => p.StartsWith("quality.dimensions.", StringComparison.Ordinal)
                        || p == "quality.criticalErrors."
                        || p == "flip."
                        || p.StartsWith("tools.", StringComparison.Ordinal)
                        || p.StartsWith("reliability.", StringComparison.Ordinal)
                        || (p.StartsWith("secondary.", StringComparison.Ordinal) && !p.StartsWith("secondary.shiftFunction.", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The Executive Summary's Overseer updates in one sentence: when Overseer was updated in the series
    /// of the model under test, before which run, and what changed; a count when there are more than
    /// three; and how many more the control models' runs show.
    /// </summary>
    private static void ChatEventsSummary(StringBuilder sb, Context ctx)
    {
        var groups = ChatEventGroups(ctx);
        var target = groups.Where(g => g.Target).ToList();
        int controls = groups.Count - target.Count;

        string sentence;
        if (target.Count == 0)
        {
            sentence = "No Overseer update fell between the periods in the runs of " + ctx.Sheet.SubjectLabel + ".";
        }
        else if (target.Count > 3)
        {
            sentence = "Between the periods, Overseer was updated " + Inv(target.Count) + " times; the Report for AI Researchers and Developers lists each update.";
        }
        else
        {
            var parts = target.Select(g => g.Tag + ", " + ChatSpan(g) + " — " + g.Changes).ToList();
            sentence = "Between the periods, Overseer was updated " + Times(target.Count) + ": "
                + (parts.Count == 1 ? parts[0] : string.Join("; ", parts.Take(parts.Count - 1)) + "; and " + parts[^1]) + ".";
        }
        if (controls > 0)
        {
            sentence += " The control models' runs show " + (controls == 1 ? "one more update." : Inv(controls) + " more updates.");
        }

        Line(sb, sentence);
        Line(sb);
    }

    /// <summary><c>once</c>, <c>twice</c>, <c>three times</c>.</summary>
    private static string Times(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        3 => "three times",
        _ => Inv(count) + " times"
    };

    /// <summary>The Overseer updates as <c>Event · When (UTC) · Run · What changed</c>, one row per update, a control model's series named.</summary>
    private static void ChatEventsTable(StringBuilder sb, Context ctx)
    {
        var groups = ChatEventGroups(ctx);
        if (groups.Count == 0)
        {
            Line(sb, "No Overseer update fell between the periods.");
            Line(sb);
            return;
        }

        Line(sb, "| Event | When (UTC) | Run | What changed |");
        Line(sb, "|---|---|---|---|");
        foreach (var g in groups)
        {
            string changes = g.Target ? g.Changes : g.Changes + " (in the runs of " + g.Series + ")";
            Line(sb, "| " + Cell(g.Tag) + " | " + Cell(ChatSpan(g, withZone: false)) + " | " + Cell(g.Run) + " | " + Cell(UpperFirst(changes)) + " |");
        }
        Line(sb);
    }

    /// <summary>
    /// When an update happened: <c>2026-10-08 14:49 UTC</c>, or <c>2026-10-08 14:49–16:00 UTC</c> when its last
    /// event came later (the date repeated on another day); without <c>UTC</c> for a column headed in it.
    /// </summary>
    private static string ChatSpan(ChatEventGroup g, bool withZone = true)
    {
        static string Bare(string when) => when.EndsWith(" UTC", StringComparison.Ordinal) ? when[..^4] : when;
        string from = Bare(g.When);
        string text = from;
        if (g.LastWhen is string lastWhen && !string.Equals(Bare(lastWhen), from, StringComparison.Ordinal))
        {
            string to = Bare(lastWhen);
            bool sameDay = from.Length >= 16 && to.Length >= 16 && string.Equals(from[..10], to[..10], StringComparison.Ordinal);
            text = from + "–" + (sameDay ? to[11..] : to);
        }
        return withZone ? text + " UTC" : text;
    }

    /// <summary>One Overseer update: its tag, its first and last time and first runs as printed, its changes in words, and its series.</summary>
    private sealed record ChatEventGroup(string Tag, string When, string? LastWhen, string Run, string Changes, string Series, bool Target);

    /// <summary>
    /// The Overseer updates: from the sheet's <c>eventGroups.*</c> (tagged E1, E2, … in order where a sheet
    /// written before tags has none), or, on a sheet written before groups, grouped here from the stored
    /// events by UTC day and series, each change put in words from the event's stored kind and raw values.
    /// </summary>
    private static List<ChatEventGroup> ChatEventGroups(Context ctx)
    {
        if (Fact(ctx, "eventGroups.count") != null)
        {
            return ChatIndexes(ctx, "eventGroups.", ".at")
                .Select((n, i) => (Prefix: "eventGroups." + Inv(n) + ".", Index: i))
                .Select(x => new ChatEventGroup(
                    IsAvailable(ctx, x.Prefix + "tag") ? D(ctx, x.Prefix + "tag") : "E" + Inv(x.Index + 1),
                    D(ctx, x.Prefix + "at"),
                    IsAvailable(ctx, x.Prefix + "lastAt") ? D(ctx, x.Prefix + "lastAt") : null,
                    D(ctx, x.Prefix + "run"),
                    OneLine(D(ctx, x.Prefix + "changes")),
                    D(ctx, x.Prefix + "series"),
                    !string.Equals(ChatValue(ctx, x.Prefix + "series"), "control", StringComparison.Ordinal)))
                .ToList();
        }

        var events = ChatIndexes(ctx, "events.", ".at")
            .Select(n => "events." + Inv(n) + ".")
            .Select(p => new
            {
                At = ChatValue(ctx, p + "at") ?? string.Empty,
                When = D(ctx, p + "at"),
                Run = ChatNumber(ctx, p + "run"),
                RunText = D(ctx, p + "run"),
                Series = D(ctx, p + "series"),
                Target = !string.Equals(ChatValue(ctx, p + "series"), "control", StringComparison.Ordinal),
                Change = ChatConsistencyEventText.Describe(ChatValue(ctx, p + "kind"), ChatValue(ctx, p + "from"), ChatValue(ctx, p + "to"))
            })
            .ToList();

        return events
            .GroupBy(e => (Day: e.At.Length >= 10 ? e.At[..10] : e.At, e.Target, e.Series))
            .Select(g => g.ToList())
            .Select((g, i) => new ChatEventGroup(
                "E" + Inv(i + 1),
                g[0].When,
                null,
                ChatRunsText(g.Select(e => e.Run).OfType<long>().ToList(), g[0].RunText),
                string.Join("; ", g.Select(e => e.Change).Distinct(StringComparer.Ordinal)),
                g[0].Series,
                g[0].Target))
            .ToList();
    }

    /// <summary><c>run #20</c>, <c>runs #20 and #21</c>; <paramref name="fallback"/> without a run id.</summary>
    private static string ChatRunsText(IReadOnlyCollection<long> runIds, string fallback)
    {
        var ids = runIds.Distinct().OrderBy(id => id).Select(id => "#" + Inv(id)).ToList();
        return ids.Count switch
        {
            0 => fallback,
            1 => "run " + ids[0],
            _ => "runs " + BenchmarkReportFormat.LetterList(ids)
        };
    }

    /// <summary>
    /// The control models, one bullet each with its runs, periods and difference-in-differences
    /// estimates, then one bullet per period without a control, its suggestions without instrument
    /// hashes. Named in a named copy, by letter otherwise.
    /// </summary>
    private static void ChatControls(StringBuilder sb, Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        if (peers.Count == 0)
        {
            Line(sb, "No control model was run, so no change can be told apart as shared with other models.");
            Line(sb);
        }
        else
        {
            for (int i = 0; i < peers.Count; i++)
            {
                var peer = peers[i];
                string p = "controls." + Inv(i + 1) + ".";
                string name = ctx.Anonymized ? "Model " + peer.Letter
                    : string.IsNullOrWhiteSpace(peer.Provider) ? peer.Label : peer.Label + " (" + peer.Provider + ")";
                string did = ChatDidText(ctx, peer);
                Line(sb, "- **" + OneLine(name) + ":** " + D(ctx, p + "runs") + " in the " + D(ctx, p + "periods")
                    + (did == NoValue ? string.Empty : "; difference in differences " + did));
            }
            Line(sb);
            Line(sb, "*A control model's chat was run on the same suites in both periods. The difference in differences is the change of the model under test minus the control's own change.*");
            Line(sb);
        }

        var missing = ChatIndexes(ctx, "controls.missing.", ".period")
            .Select(n => "controls.missing." + Inv(n) + ".")
            .Select(p => (
                Period: D(ctx, p + "period"),
                Unit: ChatMissingControlUnit(ctx, p),
                Suggestion: OneLine(ChatSuggestion(ctx, p + "suggestion"))))
            .Distinct()
            .OrderBy(m => m.Period == "baseline" ? 0 : m.Period == "comparison" ? 1 : 2)
            .ToList();
        if (missing.Count == 0) return;

        string unitHeader = ChatValue(ctx, "period.baseline.unitNoun") == "battery run" ? "Battery run (suites)" : "Run (suite)";
        Line(sb, "| Period | " + unitHeader + " | What to do |");
        Line(sb, "|---|---|---|");
        foreach (var (period, unit, suggestion) in missing)
        {
            Line(sb, "| " + Cell(UpperFirst(OneLine(period))) + " | " + Cell(unit) + " | " + Cell(suggestion) + " |");
        }
        Line(sb);
    }

    /// <summary>A missing-control note's unit: <c>Battery run #12 (suite A, suite B)</c>, or <c>Run #98 (suite A)</c>.</summary>
    private static string ChatMissingControlUnit(Context ctx, string prefix)
    {
        string suite = IsAvailable(ctx, prefix + "suite") ? OneLine(D(ctx, prefix + "suite")) : string.Empty;
        string unit = IsAvailable(ctx, prefix + "batteryRun")
            ? UpperFirst(D(ctx, prefix + "batteryRun"))
            : UpperFirst(D(ctx, prefix + "targetRun"));
        return suite.Length == 0 ? unit : unit + " (" + suite + ")";
    }

    /// <summary>A suggestion as printed, without an <c>(instrument …)</c> note a stored sheet may still carry.</summary>
    private static string ChatSuggestion(Context ctx, string key)
        => BenchmarkChatConsistencyReportFacts.WithoutInstrument(D(ctx, key));

    /// <summary>The difference-in-differences estimates against one control, <c>Quality (P1) −4.0 index points (−6.1 to −2.0 index points)</c>; <see cref="NoValue"/> without one.</summary>
    private static string ChatDidText(Context ctx, BenchmarkReportPeer peer)
    {
        var parts = new List<string>();
        foreach (int n in ChatIndexes(ctx, "did.", ".model"))
        {
            string p = "did." + Inv(n) + ".";
            if (!string.Equals(ChatValue(ctx, p + "model"), "Model " + peer.Letter, StringComparison.Ordinal)) continue;

            string estimate = IsAvailable(ctx, p + "estimate") ? D(ctx, p + "estimate") : BenchmarkReportFacts.NotAvailable;
            string interval = IsAvailable(ctx, p + "ci95") ? " (" + D(ctx, p + "ci95") + ")" : string.Empty;
            parts.Add(D(ctx, p + "endpoint") + " " + estimate + interval);
        }
        return parts.Count == 0 ? NoValue : string.Join("; ", parts);
    }

    /// <summary>
    /// The attributions: one sentence for a single one, else <c>Endpoints · Side · Grade</c>; then the
    /// evidence each records.
    /// </summary>
    private static void ChatAttributions(StringBuilder sb, Context ctx)
    {
        var attributions = ChatIndexes(ctx, "attribution.", ".label");
        if (attributions.Count == 0)
        {
            Line(sb, "The analysis recorded no attribution.");
            Line(sb);
            return;
        }

        if (attributions.Count == 1)
        {
            string p = "attribution." + Inv(attributions[0]) + ".";
            Line(sb, OneLine(D(ctx, p + "label")) + ": " + LowerFirstWord(ChatAttributionStatement(ctx, p, plain: false)));
            Line(sb);
        }
        else
        {
            Line(sb, "| Endpoints | Side | Grade |");
            Line(sb, "|---|---|---|");
            foreach (int n in attributions)
            {
                string p = "attribution." + Inv(n) + ".";
                Line(sb, "| " + Cell(ChatEndpointList(ctx, p + "endpoints")) + " | " + Cell(D(ctx, p + "side")) + " | " + Cell(D(ctx, p + "grade")) + " |");
            }
            Line(sb);
        }

        var evidence = attributions.Where(n => IsAvailable(ctx, "attribution." + Inv(n) + ".evidence")).ToList();
        foreach (int n in evidence)
        {
            string p = "attribution." + Inv(n) + ".";
            Line(sb, "- **" + OneLine(D(ctx, p + "label")) + ":** " + OneLine(D(ctx, p + "evidence")));
        }
        if (evidence.Count > 0) Line(sb);
    }

    /// <summary>The Executive Summary's sentences on where the change came from; endpoint names without ids when <paramref name="plain"/>.</summary>
    private static string ChatAttributionSentence(Context ctx, bool plain = false)
    {
        var attributions = ChatIndexes(ctx, "attribution.", ".label");
        if (attributions.Count == 0) return "The analysis recorded no attribution.";
        return string.Join(" ", attributions.Select(n => ChatAttributionStatement(ctx, "attribution." + Inv(n) + ".", plain)));
    }

    /// <summary>
    /// One attribution as a sentence: <c>The analysis attributes Quality (P1) to the provider's side, graded
    /// Indicated.</c>, or for an undetermined side <c>The analysis cannot say where the change in Quality (P1)
    /// came from (graded Not established).</c>
    /// </summary>
    private static string ChatAttributionStatement(Context ctx, string prefix, bool plain)
    {
        string endpoints = ChatEndpointList(ctx, prefix + "endpoints", plain);
        if (string.Equals(ChatValue(ctx, prefix + "side"), "undetermined", StringComparison.Ordinal))
        {
            return "The analysis cannot say where the change in " + endpoints + " came from (graded " + D(ctx, prefix + "grade") + ").";
        }
        return "The analysis attributes " + endpoints + " to " + D(ctx, prefix + "side") + ", graded " + D(ctx, prefix + "grade") + ".";
    }

    /// <summary>
    /// An attribution's endpoints by name, <c>Quality (P1) and Cost per question (P5)</c>, without the ids when
    /// <paramref name="plain"/>; <c>no endpoint</c> without one.
    /// </summary>
    private static string ChatEndpointList(Context ctx, string key, bool plain = false)
    {
        if (!IsAvailable(ctx, key)) return "no endpoint";
        var ids = D(ctx, key).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var known = ChatEndpointIds(ctx);
        return ids.Count == 0 || ids.Any(id => !known.Contains(id, StringComparer.Ordinal))
            ? D(ctx, key)
            : BenchmarkReportFormat.LetterList(ids.Select(id => plain ? ChatEndpointPlainName(ctx, id) : ChatEndpointName(ctx, id)).ToList());
    }

    /// <summary>
    /// The robustness checks as <c>Check · P4 · P5</c>: one row per check, one column per computed
    /// endpoint it ran on (at most four), then the detail of each check that did not pass.
    /// </summary>
    private static void ChatRobustnessTable(StringBuilder sb, Context ctx)
    {
        var checks = ChatIndexes(ctx, "robustness.", ".name")
            .Select(n => "robustness." + Inv(n) + ".")
            .Select(p => (Endpoint: ChatValue(ctx, p + "endpoint") ?? D(ctx, p + "endpoint"), Name: D(ctx, p + "name"),
                Status: D(ctx, p + "status"), Detail: IsAvailable(ctx, p + "detail") ? D(ctx, p + "detail") : null))
            .ToList();
        var computed = ChatComputedEndpoints(ctx);
        var columns = computed.Where(id => checks.Any(c => c.Endpoint == id)).Take(4).ToList();
        if (checks.Count == 0 || columns.Count == 0)
        {
            Line(sb, "The analysis ran no robustness check on a computed endpoint.");
            Line(sb);
            return;
        }

        Line(sb, "| Check | " + string.Join(" | ", columns) + " |");
        Line(sb, "|---|" + string.Concat(columns.Select(_ => "---|")));
        foreach (string name in checks.Where(c => columns.Contains(c.Endpoint)).Select(c => c.Name).Distinct(StringComparer.Ordinal))
        {
            var cells = columns.Select(id => checks.FirstOrDefault(c => c.Name == name && c.Endpoint == id).Status ?? NoValue);
            Line(sb, "| " + Cell(name) + " | " + string.Join(" | ", cells.Select(Cell)) + " |");
        }
        Line(sb);

        var details = checks.Where(c => columns.Contains(c.Endpoint) && c.Status != "passed" && !string.IsNullOrWhiteSpace(c.Detail)).ToList();
        foreach (var c in details) Line(sb, "- **" + OneLine(c.Name) + ", " + c.Endpoint + ":** " + OneLine(c.Detail));
        if (details.Count > 0) Line(sb);
    }

    /// <summary>
    /// What the document's terms mean. The Executive Summary gets three points: what was measured, the
    /// verdicts and the hours; the other documents the endpoints, the evidence grades, the attribution,
    /// the control models and what the analysis never infers too. Fixed text.
    /// </summary>
    private static void ChatHowToRead(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## How to read this");

        if (ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary)
        {
            Line(sb, "- **What was measured.** The Overseer chat with " + ctx.Sheet.SubjectLabel + ": the model together with the chat "
                + "system prompt, the tools, the knowledge corpora and the agent loop, run on the same benchmark suites in a baseline "
                + "period and a later comparison period.");
            Line(sb, "- **The five measures.** *Quality* is the Intelligence Index of the graded answers; *time to first answer text* is how "
                + "long a player waits before the answer starts; *answer streaming rate* is how fast it then appears; *work per turn* is the "
                + "output tokens per answer; *cost per question* is US dollars per question at one price card.");
            Line(sb, "- **Grades.** *Established* means fit to publish; *Indicated*, a decisive result with a caveat; *Not established*, no finding.");
            Line(sb, "- **Verdicts.** *Equivalent* means the same as before within the endpoint's margin; *degraded* or *improved* a "
                + "change beyond it; *inconclusive* that these runs cannot tell a change from no change, and the smallest detectable "
                + "change says how large a change they could have missed. Only an Established result is fit to publish.");
            Line(sb, "- **Hours.** " + ChatHoursSentence(ctx));
            Line(sb);
            return;
        }

        var endpoints = ChatEndpointIds(ctx);
        Line(sb, "- **What was measured.** The Overseer chat with " + ctx.Sheet.SubjectLabel + ": the model together with the chat "
            + "system prompt, the tools, the knowledge corpora and the agent loop. The same benchmark suites were run in a baseline "
            + "period and a later comparison period, and each endpoint compares the two.");
        if (endpoints.Count > 0)
        {
            Line(sb, "- **The endpoints.** " + BenchmarkReportFormat.LetterList(endpoints.Select(id => ChatEndpointName(ctx, id)).ToList())
                + ". Each estimate comes with its 95 % interval.");
        }
        Line(sb, "- **Verdicts.** Each verdict is read against the endpoint's equivalence margin, set by the protocol: *equivalent* when "
            + "the 90 % interval lies inside the margin; *degraded* or *improved* (*more work* or *less work* for an endpoint that "
            + "counts work) when the 95 % interval lies wholly beyond the margin on one side; *changed, negligible* when the 95 % "
            + "interval excludes zero but lies inside the margin; *inconclusive* otherwise, when the runs cannot tell a change from "
            + "no change. The smallest detectable change, ± the minimum detectable effect, is the smallest change these runs could have detected.");
        Line(sb, "- **Evidence grades.** *Established*: a decisive verdict, every robustness check passed, the minimum sample met, "
            + "telemetry-grade data and no pooling across a measurement change. *Indicated*: a decisive verdict with a failed "
            + "robustness check, legacy data, pooling across a measurement change or a sample below the minimum. *Not established*: "
            + "inconclusive, or nothing to support a claim. Only an Established result is fit to publish.");
        Line(sb, "- **Hours.** " + ChatHoursSentence(ctx));
        Line(sb, "- **Attribution.** The total change is reported first; the attribution then grades where it came from: our change, "
            + "our infrastructure, the provider's side, or undetermined. An attribution never goes beyond its grade.");
        if (ctx.Sheet.Peers.Count > 0)
        {
            Line(sb, "- **Control models.** A change the model under test shares with a control model is told apart from a change "
                + "of the model under test alone by the difference in differences.");
        }
        Line(sb, "- **What is never inferred.** The analysis measures what changed. It never infers anyone's intent, and it names "
            + "no mechanism unless the provider has confirmed one.");
        Line(sb);
    }

    /// <summary>
    /// The limitations: every one the analysis recorded in the researcher report and the Provider Issue
    /// Report; in the Executive Summary and the Internal Improvement Brief only its data-quality notes,
    /// and no section at all without one.
    /// </summary>
    private static void ChatLimitations(StringBuilder sb, Context ctx, bool heading)
    {
        bool all = ctx.Document.Audience is BenchmarkReportAudience.TechnicalReport or BenchmarkReportAudience.ProviderIssueReport;
        var limitations = all ? ChatIndexes(ctx, "limitation.", string.Empty) : new List<int>();
        var dataQuality = ChatIndexes(ctx, "limitation.dataQuality.", string.Empty);
        if (!all && dataQuality.Count == 0) return;

        if (heading) Heading(sb, "## Limitations");
        if (limitations.Count == 0 && dataQuality.Count == 0)
        {
            Line(sb, "The analysis recorded no limitation.");
            Line(sb);
            return;
        }

        foreach (int n in limitations) Line(sb, "- " + OneLine(D(ctx, "limitation." + Inv(n))));
        foreach (int n in dataQuality) Line(sb, "- **Data quality:** " + OneLine(D(ctx, "limitation.dataQuality." + Inv(n))));
        Line(sb);
    }

    /// <summary>
    /// What identifies and reproduces the analysis: its number, code version, protocol, when it was
    /// saved, the common grader, the price card (its source alone in the Internal Improvement Brief), each
    /// period's battery runs or runs with their member runs, the control runs and the format version.
    /// Never a hash.
    /// </summary>
    private static void ChatReproducibility(StringBuilder sb, Context ctx)
    {
        var subject = ctx.Sheet.ChatConsistency ?? new BenchmarkReportChatConsistencySubject();
        var document = ctx.Document;
        string version = document.ReportFormatVersion == ChatConsistencyReportFormatVersion
            ? Inv(ChatConsistencyReportFormatVersion)
            : Inv(document.ReportFormatVersion) + " (rendered with format version " + Inv(ChatConsistencyReportFormatVersion) + ")";

        Line(sb, "- **Analysis:** " + ChatAnalysisText(ctx));
        Line(sb, "- **Analysis code version:** " + Inv(subject.AnalysisCodeVersion));
        Line(sb, "- **Protocol:** " + ChatProtocolText(ctx) + "; " + D(ctx, "protocol.overridden") + "; alpha " + D(ctx, "protocol.alpha"));
        if (IsAvailable(ctx, "analysis.savedAt"))
        {
            Line(sb, "- **Saved:** " + D(ctx, "analysis.savedAt"));
        }
        Line(sb, "- **" + Label("quality.commonGrader") + ":** " + (IsAvailable(ctx, "quality.commonGrader")
            ? D(ctx, "quality.commonGrader")
            : "none. " + NotAvailableReason(Fact(ctx, "quality.commonGrader"))));
        Line(sb, "- **" + Label("pricing.card") + ":** " + (ctx.Document.Audience == BenchmarkReportAudience.InternalBrief && IsAvailable(ctx, "pricing.source")
            ? D(ctx, "pricing.source")
            : D(ctx, "pricing.card")));
        Line(sb, "- **Baseline:** " + ChatPeriodRuns(ctx, "baseline", subject.BaselineRunIds));
        Line(sb, "- **Comparison:** " + ChatPeriodRuns(ctx, "comparison", subject.ComparisonRunIds));
        Line(sb, "- **Control runs:** " + ChatRunList(subject.ControlRunIds));
        Line(sb, "- **Report format version:** " + version);
        Line(sb);
    }

    /// <summary>A period's battery runs with their member runs, or its runs; from the stored run ids on a sheet without them.</summary>
    private static string ChatPeriodRuns(Context ctx, string period, IReadOnlyCollection<long>? runIds)
        => IsAvailable(ctx, "period." + period + ".memberRuns") ? OneLine(D(ctx, "period." + period + ".memberRuns")) : ChatRunList(runIds);

    /// <summary>The terms the subject was evaluated under: the distillation prohibition, and whose content the document rests on.</summary>
    private static void ChatEvaluationTerms(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Heading(sb, "## Evaluation terms");
        Line(sb, "- **Distillation / training prohibition:** " + DistillationProhibition);
        Line(sb, "- **Third-party model content:** Outputs generated by **" + sheet.SubjectLabel + "** (" + sheet.SubjectProvider + ") "
            + "through the Overseer chat, measured by the Overseer benchmark and described in this document by **"
            + ctx.Document.WriterDisplayName + "** (" + ctx.Document.WriterProvider + "), are third-party content evaluated solely "
            + "to check whether the Overseer chat with this model stays consistent over time, and for operational model selection.");
        Line(sb);
    }

    private static void ChatFooter(StringBuilder sb, Context ctx)
    {
        var document = ctx.Document;
        int current = ChatConsistencyReportFormatVersion;
        string version = document.ReportFormatVersion == current
            ? "format version " + Inv(current)
            : "generated under format version " + Inv(document.ReportFormatVersion) + " · rendered with format version " + Inv(current);

        Line(sb, "---");
        Line(sb);
        Line(sb, "*Document ID " + document.Id.ToString(CultureInfo.InvariantCulture)
            + " · " + version
            + " · created " + document.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            + " · writer " + document.WriterDisplayName + " (" + document.WriterProvider + ", " + document.WriterModelId + ")"
            + " · disclosure " + ctx.Options.Disclosure.ToString()
            + " · controls " + (ctx.Anonymized ? "anonymized" : "named") + "*");
        Line(sb);
        Line(sb, "*Figures and tables were computed by Overseer from the saved chat consistency analysis. The prose was written by "
            + document.WriterDisplayName + " from those figures and checked automatically for structure, permitted figures, word "
            + "limits, control-model names, hype words, spelling, readable text and the claim rules on change, cause, intent, "
            + "mechanism, public claims and hours; the checks do not verify the prose's interpretations.*");
    }

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>The primary endpoints' ids, ordinal: <c>P1</c> to <c>P5</c>.</summary>
    private static List<string> ChatEndpointIds(Context ctx)
    {
        const string prefix = "endpoint.";
        const string suffix = ".name";
        return ctx.Sheet.Facts
            .Select(f => f.Key)
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.EndsWith(suffix, StringComparison.Ordinal)
                        && k.Length > prefix.Length + suffix.Length)
            .Select(k => k[prefix.Length..^suffix.Length])
            .Where(id => !id.Contains('.', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The primary endpoints whose verdict is not <c>not computable</c>, ordinal.</summary>
    private static List<string> ChatComputedEndpoints(Context ctx)
        => ChatEndpointIds(ctx)
            .Where(id => !string.Equals(ChatValue(ctx, "endpoint." + id + ".verdict"), "not computable", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// The endpoints not computable, grouped by their reason, in endpoint order. In <paramref name="plain"/>
    /// words (the Executive Summary) the names carry no id and a reason of a known kind is put plainly
    /// (<see cref="ChatPlainReason"/>).
    /// </summary>
    private static List<(IReadOnlyList<string> Names, string Reason)> ChatNotComputableGroups(Context ctx, bool plain = false)
    {
        var computed = ChatComputedEndpoints(ctx);
        return ChatEndpointIds(ctx)
            .Except(computed, StringComparer.Ordinal)
            .Select(id =>
            {
                string reason = NotAvailableReason(Fact(ctx, "endpoint." + id + ".estimate"));
                return (Id: id, Reason: plain ? ChatPlainReason(ctx, id) ?? reason : reason);
            })
            .GroupBy(e => e.Reason, StringComparer.Ordinal)
            .Select(g => (Names: (IReadOnlyList<string>)g.Select(e => plain ? ChatEndpointPlainName(ctx, e.Id) : ChatEndpointName(ctx, e.Id)).ToList(), Reason: g.Key))
            .ToList();
    }

    /// <summary>
    /// A not-computed reason in plain words, by the endpoint's <c>notComputedKind</c>: a grading change on
    /// quality, or periods run at different hours; null for any other kind, and on a sheet without kinds.
    /// </summary>
    private static string? ChatPlainReason(Context ctx, string id)
    {
        string? kind = ChatValue(ctx, "endpoint." + id + ".notComputedKind");
        if (kind == "measurementChanged" && id == "P1")
        {
            return "Quality cannot be compared yet: the way answers are graded changed between the periods. "
                + "Re-grading both periods with one grader makes it comparable.";
        }
        if (kind == "noCommonStratum")
        {
            string baseline = IsAvailable(ctx, "period.baseline.hours") ? D(ctx, "period.baseline.hours") : "at other hours";
            string comparison = IsAvailable(ctx, "period.comparison.hours") ? D(ctx, "period.comparison.hours") : "at other hours";
            return "Speed cannot be compared: the baseline ran " + baseline + " and the comparison " + comparison
                + ", and response times vary with the time of day.";
        }
        return null;
    }

    /// <summary><c>Quality (P1)</c>.</summary>
    private static string ChatEndpointName(Context ctx, string id)
    {
        string key = "endpoint." + id + ".name";
        return IsAvailable(ctx, key) ? D(ctx, key) + " (" + id + ")" : id;
    }

    /// <summary><c>Quality</c>: the name alone, for the Executive Summary.</summary>
    private static string ChatEndpointPlainName(Context ctx, string id)
    {
        string key = "endpoint." + id + ".name";
        return IsAvailable(ctx, key) ? D(ctx, key) : id;
    }

    /// <summary>A figure's display without the <c>(log ratio …)</c> a sheet written before readable displays carries.</summary>
    private static string ChatFigure(Context ctx, string key) => Regex.Replace(D(ctx, key), ChatLogRatioNote, string.Empty, RegexOptions.CultureInvariant);

    /// <summary>An endpoint's minimum detectable effect, <c>±12.7 %</c>; the ± added to a display stored without it.</summary>
    private static string ChatMde(Context ctx, string id)
    {
        string display = D(ctx, "endpoint." + id + ".mde");
        return display.StartsWith('±') ? display : "±" + display;
    }

    /// <summary>The numbers <c>n</c> of the facts keyed <c>&lt;prefix&gt;&lt;n&gt;&lt;suffix&gt;</c>, ascending.</summary>
    private static List<int> ChatIndexes(Context ctx, string prefix, string suffix)
        => ctx.Sheet.Facts
            .Select(f => f.Key)
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.EndsWith(suffix, StringComparison.Ordinal)
                        && k.Length > prefix.Length + suffix.Length)
            .Select(k => int.TryParse(k.AsSpan(prefix.Length, k.Length - prefix.Length - suffix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

    /// <summary>A fact's raw string value; null when it is missing, unavailable or not a string.</summary>
    private static string? ChatValue(Context ctx, string key)
        => Fact(ctx, key) is { Available: true, Value: JsonValue value } && value.TryGetValue(out string? text) ? text : null;

    /// <summary>A fact's raw whole-number value; null when it is missing, unavailable or not a number.</summary>
    private static long? ChatNumber(Context ctx, string key)
    {
        if (Fact(ctx, key) is not { Available: true, Value: JsonValue value }) return null;
        if (value.TryGetValue(out long whole)) return whole;
        return value.TryGetValue(out double number) && double.IsFinite(number) ? (long)number : null;
    }

    /// <summary><c>#7 — name</c>; the analysis's name only in a named copy, since an operator's name can name a model.</summary>
    private static string ChatAnalysisText(Context ctx)
    {
        var subject = ctx.Sheet.ChatConsistency ?? new BenchmarkReportChatConsistencySubject();
        string id = subject.AnalysisId is int analysisId ? "#" + Inv(analysisId) : "not saved";
        return !ctx.Anonymized && !string.IsNullOrWhiteSpace(subject.Name) ? id + " — " + OneLine(subject.Name) : id;
    }

    /// <summary><c>Test Model (TestProvider, test-model-1, thinking level high)</c>, each empty part left out.</summary>
    private static string ChatModelText(BenchmarkReportFactSheet sheet)
    {
        var parts = new[] { sheet.SubjectProvider, sheet.SubjectModelId }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        if (!string.IsNullOrWhiteSpace(sheet.SubjectThinkingLevel)) parts.Add("thinking level " + sheet.SubjectThinkingLevel.Trim());
        return parts.Count == 0 ? sheet.SubjectLabel : sheet.SubjectLabel + " (" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// <c>2026-10-08 00:00 UTC until 14:49 UTC, 1 battery run</c> or <c>from 2026-10-08 14:49 UTC to 2026-10-08 23:59 UTC,
    /// 1 battery run</c> (<see cref="BenchmarkChatConsistencyReportFacts.WindowText(IReadOnlyList{BenchmarkReportFact}, string)"/>);
    /// the runs on a sheet without units.
    /// </summary>
    private static string ChatPeriodText(Context ctx, string period)
    {
        string p = "period." + period + ".";
        string window = BenchmarkChatConsistencyReportFacts.WindowText(ctx.Sheet.Facts, period) ?? D(ctx, p + "start") + " to " + D(ctx, p + "end");
        return window + ", " + (IsAvailable(ctx, p + "units") ? D(ctx, p + "units") : D(ctx, p + "runs"));
    }

    /// <summary>The hours the result holds for, or that the periods ran at different hours.</summary>
    private static string ChatHoursText(Context ctx)
        => IsAvailable(ctx, "scope.hours")
            ? D(ctx, "scope.hours")
            : "none: the periods ran at different hours" + ChatPeriodHoursClause(ctx);

    /// <summary><c> (baseline weekdays 04–08 UTC, comparison weekdays 12–16 UTC)</c>; empty on a sheet without the periods' hours.</summary>
    private static string ChatPeriodHoursClause(Context ctx)
        => IsAvailable(ctx, "period.baseline.hours") && IsAvailable(ctx, "period.comparison.hours")
            ? " (baseline " + D(ctx, "period.baseline.hours") + ", comparison " + D(ctx, "period.comparison.hours") + ")"
            : string.Empty;

    /// <summary>The overall verdict's hours point.</summary>
    private static string ChatScopeSentence(Context ctx)
        => IsAvailable(ctx, "scope.hours")
            ? "every result holds for " + D(ctx, "scope.hours") + " only, the hours both periods share"
            : "the periods ran at different hours" + ChatPeriodHoursClause(ctx) + ", so they share no hours a result could be confined to";

    /// <summary>"How to read this"'s hours point.</summary>
    private static string ChatHoursSentence(Context ctx)
        => IsAvailable(ctx, "scope.hours")
            ? "Every result holds for " + D(ctx, "scope.hours") + " only, the hours both periods share. It says nothing about the hours outside them."
            : "The periods ran at different hours" + ChatPeriodHoursClause(ctx) + ". Response times vary with the time of day, so the speed measures could not be compared.";

    /// <summary><c>2 strata (weekdays 04–08 UTC, weekdays 08–12 UTC); time of day not assessable: …</c>; <c>none: …</c> without common strata.</summary>
    private static string ChatStrataText(Context ctx)
    {
        var strata = ChatIndexes(ctx, "coverage.strata.", string.Empty).Select(n => D(ctx, "coverage.strata." + Inv(n))).ToList();
        if (strata.Count == 0) return "none: the periods share no hours";
        string count = IsAvailable(ctx, "coverage.strata.count") ? D(ctx, "coverage.strata.count") : Inv(strata.Count) + " strata";
        return count + " (" + string.Join(", ", strata) + "); time of day " + D(ctx, "serving.timeOfDayAssessable");
    }

    /// <summary>The overall verdict's controls point: the control models, or that no control run was made, and the missing controls.</summary>
    private static string ChatControlsSentence(Context ctx)
    {
        string missing = Fact(ctx, "controls.missing.count") is { Available: true, Value: JsonValue value } && value.TryGetValue(out int count) && count > 0
            ? "; " + D(ctx, "controls.missing.count")
            : string.Empty;
        return ctx.Sheet.Peers.Count == 0
            ? "no control run was made, so a change shared with other models cannot be told apart from a change of this model alone" + missing
            : ChatControlsText(ctx) + ", run on the same suites in both periods" + missing;
    }

    private static string ChatProtocolText(Context ctx)
    {
        string label = ctx.Sheet.ChatConsistency?.ProtocolLabel ?? string.Empty;
        return string.IsNullOrWhiteSpace(label) ? D(ctx, "protocol.label") : OneLine(label);
    }

    /// <summary>
    /// Named: the control models' labels; anonymized: <c>2 control models (A and B), identities withheld</c>;
    /// <c>none</c> without any.
    /// </summary>
    private static string ChatControlsText(Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        if (peers.Count == 0) return "none";
        if (!ctx.Anonymized) return BenchmarkReportFormat.LetterList(peers.Select(p => p.Label).ToList());

        string letters = peers.Count switch
        {
            1 => peers[0].Letter,
            2 => peers[0].Letter + " and " + peers[1].Letter,
            _ => peers[0].Letter + " to " + peers[^1].Letter
        };
        return Inv(peers.Count) + (peers.Count == 1 ? " control model (" : " control models (") + letters + "), "
            + (peers.Count == 1 ? "identity withheld" : "identities withheld");
    }

    private static string ChatRunList(IReadOnlyCollection<long>? runIds)
        => runIds == null || runIds.Count == 0 ? "none" : string.Join(", ", runIds.Select(id => "#" + Inv(id)));

    private static string UpperFirst(string text)
        => text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    /// <summary>The text with its first letter lowercased, unless it starts an acronym (<c>IDs</c>, <c>P1</c>).</summary>
    private static string LowerFirstWord(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) && !char.IsDigit(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;
}
