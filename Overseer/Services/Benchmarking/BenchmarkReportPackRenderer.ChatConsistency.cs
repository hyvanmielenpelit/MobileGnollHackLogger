namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The chat consistency documents (<see cref="BenchmarkReportFactSheet.IsChatConsistency"/>): the Overseer
/// chat with one model measured over a baseline and a comparison period, its control models lettered
/// as peers. Every document opens with the overall verdict and the verdict table, prints the Overseer
/// events, the control models and the attributions as code-rendered tables, then the writer's slots in
/// slot order under their titles, a fixed "How to read this" block, the limitations the analysis
/// recorded and a reproducibility list. In the researcher report and the Provider Issue Report a table
/// stands under the slot that discusses it. A named copy prints the control models by name; the
/// Provider Issue Report never does. The text is the same at every disclosure level, under its own stamp.
/// </summary>
public static partial class BenchmarkReportPackRenderer
{
    private const string ChatFullStamp = "INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.";
    private const string ChatSharedStamp = "Confidential. Unpublished chat consistency results. Review before sharing.";
    private const string ChatProviderStamp = "Confidential. Prepared for the model's provider.";

    /// <summary>A code-rendered block of a chat consistency document.</summary>
    private enum ChatBlock
    {
        Verdicts,
        Events,
        Controls,
        Attributions,
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
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Limitations) => new[] { ChatBlock.Limitations },
        (BenchmarkReportAudience.TechnicalReport, BenchmarkReportSlots.Reproducibility) => new[] { ChatBlock.Reproducibility },
        (BenchmarkReportAudience.InternalBrief, BenchmarkReportSlots.ChangeEffects) => new[] { ChatBlock.Events },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.IssueSummary) => new[] { ChatBlock.Attributions },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.Measurements) => new[] { ChatBlock.Verdicts },
        (BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportSlots.RuledOut) => new[] { ChatBlock.Events, ChatBlock.Controls },
        _ => Array.Empty<ChatBlock>()
    };

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
        ChatOverallVerdict(sb, ctx);

        Heading(sb, "## The result in one sentence");
        Line(sb, Prose(ctx, ctx.Writer.Headline));
        Line(sb);

        foreach (var block in new[] { ChatBlock.Verdicts, ChatBlock.Events, ChatBlock.Controls, ChatBlock.Attributions })
        {
            if (!attached.Contains(block)) ChatBlockSection(sb, ctx, block, heading: true);
        }

        foreach (string slot in spec.RequiredSlots)
        {
            string title = BenchmarkReportSlots.ChatConsistencySlotTitles.TryGetValue(slot, out string? t) ? t : slot;
            Heading(sb, "## " + title);
            foreach (var block in ChatBlocksOf(audience, slot)) ChatBlockSection(sb, ctx, block, heading: false);
            Slot(sb, ctx, slot);
        }

        ChatHowToRead(sb, ctx);
        foreach (var block in new[] { ChatBlock.Limitations, ChatBlock.Reproducibility })
        {
            if (!attached.Contains(block)) ChatBlockSection(sb, ctx, block, heading: true);
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
        switch (block)
        {
            case ChatBlock.Verdicts:
                if (heading) Heading(sb, "## Verdicts by endpoint");
                ChatVerdictTable(sb, ctx);
                Figures(sb, ctx, BenchmarkReportChartAnchor.ChatConsistencyResults);
                break;
            case ChatBlock.Events:
                if (heading) Heading(sb, "## Overseer events between the periods");
                ChatEventsTable(sb, ctx);
                Figures(sb, ctx, BenchmarkReportChartAnchor.ChatConsistencyEvents);
                break;
            case ChatBlock.Controls:
                if (heading) Heading(sb, "## Control models");
                ChatControlsTable(sb, ctx);
                break;
            case ChatBlock.Attributions:
                if (heading) Heading(sb, "## Where the change came from");
                ChatAttributionsTable(sb, ctx);
                break;
            case ChatBlock.Limitations:
                if (heading) Heading(sb, "## Limitations");
                ChatLimitations(sb, ctx);
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

    private static void ChatOverallVerdict(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## Overall verdict");
        Line(sb, "- **Verdict:** " + D(ctx, "verdict.overall"));
        string headline = ctx.Sheet.ChatConsistency?.Headline ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(headline))
        {
            Line(sb, "- **The analysis's headline:** " + OneLine(ctx.Anonymized ? headline : NamedLetters(ctx, headline)));
        }
        Line(sb, "- **" + Label("verdict.reliabilityIncreases") + ":** " + D(ctx, "verdict.reliabilityIncreases"));
        Line(sb, "- **Hours:** " + ChatHoursText(ctx));
        Line(sb);
    }

    /// <summary>The primary endpoints: estimate, 95 % interval, verdict, grade and minimum detectable effect, with each grade's reasons below.</summary>
    private static void ChatVerdictTable(StringBuilder sb, Context ctx)
    {
        var endpoints = ChatEndpointIds(ctx);
        if (endpoints.Count == 0)
        {
            Line(sb, "The analysis recorded no primary endpoint.");
            Line(sb);
            return;
        }

        Line(sb, "| Endpoint | Estimate | 95 % interval | Verdict | Grade | Minimum detectable effect |");
        Line(sb, "|---|---|---|---|---|---|");
        foreach (string id in endpoints)
        {
            string p = "endpoint." + id + ".";
            Line(sb, "| " + Cell(ChatEndpointName(ctx, id)) + " | " + Num(ctx, p + "estimate") + " | " + Num(ctx, p + "ci95") + " | "
                + Cell(D(ctx, p + "verdict")) + " | " + Cell(ChatValue(ctx, p + "grade") ?? D(ctx, p + "grade")) + " | " + Num(ctx, p + "mde") + " |");
        }
        Line(sb);

        var notes = new List<string>();
        foreach (string id in endpoints)
        {
            string p = "endpoint." + id + ".";
            if (Fact(ctx, p + "estimate") is { Available: false } estimate)
            {
                notes.Add("- **" + ChatEndpointName(ctx, id) + ":** " + NotAvailableText(estimate));
            }
            else if (Fact(ctx, p + "grade") is { Available: true } grade
                     && ChatValue(ctx, p + "grade") is string value
                     && !string.Equals(grade.Display, value, StringComparison.Ordinal))
            {
                notes.Add("- **" + ChatEndpointName(ctx, id) + ":** " + OneLine(Shown(ctx, grade)));
            }
        }
        foreach (string note in notes) Line(sb, note);
        if (notes.Count > 0) Line(sb);

        Line(sb, "*Each estimate is the comparison period against the baseline period: a difference in the endpoint's unit, or a change in percent for a ratio. The verdict is read against the endpoint's equivalence margin.*");
        Line(sb);
    }

    /// <summary>Every Overseer event between the periods: when, what changed, from what to what, in whose series, and at which run.</summary>
    private static void ChatEventsTable(StringBuilder sb, Context ctx)
    {
        var events = ChatIndexes(ctx, "events.", ".at");
        if (events.Count == 0)
        {
            Line(sb, "No Overseer event fell between the periods.");
            Line(sb);
            return;
        }

        Line(sb, "| Date | Kind | Change | From → to | Series | Run |");
        Line(sb, "|---|---|---|---|---|---|");
        foreach (int n in events)
        {
            string p = "events." + Inv(n) + ".";
            string fromTo = IsAvailable(ctx, p + "from") || IsAvailable(ctx, p + "to")
                ? (IsAvailable(ctx, p + "from") ? D(ctx, p + "from") : NoValue) + " → " + (IsAvailable(ctx, p + "to") ? D(ctx, p + "to") : NoValue)
                : NoValue;
            string run = IsAvailable(ctx, p + "run")
                ? D(ctx, p + "run") + (IsAvailable(ctx, p + "previousRun") ? " (after " + D(ctx, p + "previousRun") + ")" : string.Empty)
                : NoValue;
            Line(sb, "| " + Cell(D(ctx, p + "at")) + " | " + Cell(D(ctx, p + "kind")) + " | " + Cell(D(ctx, p + "label")) + " | "
                + Cell(fromTo) + " | " + Cell(D(ctx, p + "series")) + " | " + Cell(run) + " |");
        }
        Line(sb);
    }

    /// <summary>
    /// The lettered control models: their runs, periods and difference-in-differences estimates, then
    /// the periods without a control. Named in a named copy, by letter otherwise.
    /// </summary>
    private static void ChatControlsTable(StringBuilder sb, Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        if (peers.Count == 0)
        {
            Line(sb, "No control model was run, so no change can be told apart as shared with other models.");
            Line(sb);
        }
        else
        {
            TableHeader(sb, ctx, new[] { "Runs", "Periods", "Difference in differences" });
            for (int i = 0; i < peers.Count; i++)
            {
                var peer = peers[i];
                string p = "controls." + Inv(i + 1) + ".";
                TableRow(sb, ctx, peer, new[]
                {
                    Num(ctx, p + "runs"),
                    Num(ctx, p + "periods"),
                    Cell(ChatDidText(ctx, peer))
                });
            }
            Line(sb);
            Line(sb, "*A control model's chat was run on the same suites in both periods. The difference in differences is the change of the model under test minus the control's own change.*");
            Line(sb);
        }

        var missing = ChatIndexes(ctx, "controls.missing.", ".period");
        if (missing.Count == 0) return;

        Line(sb, "Periods without a control:");
        Line(sb);
        foreach (int n in missing)
        {
            string p = "controls.missing." + Inv(n) + ".";
            Line(sb, "- **" + OneLine(D(ctx, p + "period")) + ", " + OneLine(D(ctx, p + "suite")) + ":** " + OneLine(D(ctx, p + "suggestion")));
        }
        Line(sb);
    }

    /// <summary>The difference-in-differences estimates against one control, <c>Quality (P1): -4.0 index points (-6.1 to -2.0)</c>; <see cref="NoValue"/> without one.</summary>
    private static string ChatDidText(Context ctx, BenchmarkReportPeer peer)
    {
        var parts = new List<string>();
        foreach (int n in ChatIndexes(ctx, "did.", ".model"))
        {
            string p = "did." + Inv(n) + ".";
            if (!string.Equals(ChatValue(ctx, p + "model"), "Model " + peer.Letter, StringComparison.Ordinal)) continue;

            string estimate = IsAvailable(ctx, p + "estimate") ? D(ctx, p + "estimate") : BenchmarkReportFacts.NotAvailable;
            string interval = IsAvailable(ctx, p + "ci95") ? " (" + D(ctx, p + "ci95") + ")" : string.Empty;
            parts.Add(D(ctx, p + "endpoint") + ": " + estimate + interval);
        }
        return parts.Count == 0 ? NoValue : string.Join("; ", parts);
    }

    /// <summary>Each attribution with its side, grade, rule and endpoints, then its recorded evidence.</summary>
    private static void ChatAttributionsTable(StringBuilder sb, Context ctx)
    {
        var attributions = ChatIndexes(ctx, "attribution.", ".label");
        if (attributions.Count == 0)
        {
            Line(sb, "The analysis recorded no attribution.");
            Line(sb);
            return;
        }

        Line(sb, "| Attribution | Side | Grade | Endpoints |");
        Line(sb, "|---|---|---|---|");
        foreach (int n in attributions)
        {
            string p = "attribution." + Inv(n) + ".";
            Line(sb, "| " + Cell(D(ctx, p + "label")) + " | " + Cell(D(ctx, p + "side")) + " | " + Cell(D(ctx, p + "grade")) + " | "
                + Cell(D(ctx, p + "endpoints")) + " |");
        }
        Line(sb);

        var evidence = attributions.Where(n => IsAvailable(ctx, "attribution." + Inv(n) + ".evidence")).ToList();
        foreach (int n in evidence)
        {
            string p = "attribution." + Inv(n) + ".";
            Line(sb, "- **" + OneLine(D(ctx, p + "label")) + ":** " + OneLine(D(ctx, p + "evidence")));
        }
        if (evidence.Count > 0) Line(sb);
    }

    /// <summary>
    /// What the document's terms mean: the endpoints, the verdicts, the evidence grades, the hours the
    /// result holds for, the attribution, and what the analysis never infers. Fixed text.
    /// </summary>
    private static void ChatHowToRead(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## How to read this");

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
            + "no change. The minimum detectable effect is the smallest change these runs could have detected.");
        Line(sb, "- **Evidence grades.** *Established*: a decisive verdict, every robustness check passed, the minimum sample met, "
            + "telemetry-grade data and no pooling across a measurement change. *Indicated*: a decisive verdict with a failed "
            + "robustness check, legacy data, pooling across a measurement change or a sample below the minimum. *Not established*: "
            + "inconclusive, or nothing to support a claim. Only an Established result is fit to publish.");
        Line(sb, "- **Hours.** Every result holds for " + ChatHoursText(ctx) + " only, the hours both periods share. It says nothing "
            + "about the hours outside them.");
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

    /// <summary>The limitations and data-quality notes the analysis recorded.</summary>
    private static void ChatLimitations(StringBuilder sb, Context ctx)
    {
        var limitations = ChatIndexes(ctx, "limitation.", string.Empty);
        var dataQuality = ChatIndexes(ctx, "limitation.dataQuality.", string.Empty);
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

    /// <summary>What reproduces the analysis: its id, input hash, protocol, code version, the runs of each period, the price card and the format version.</summary>
    private static void ChatReproducibility(StringBuilder sb, Context ctx)
    {
        var subject = ctx.Sheet.ChatConsistency ?? new BenchmarkReportChatConsistencySubject();
        var document = ctx.Document;
        string version = document.ReportFormatVersion == ChatConsistencyReportFormatVersion
            ? Inv(ChatConsistencyReportFormatVersion)
            : Inv(document.ReportFormatVersion) + " (rendered with format version " + Inv(ChatConsistencyReportFormatVersion) + ")";

        Line(sb, "- **Analysis:** " + ChatAnalysisText(ctx));
        Line(sb, "- **Input SHA-256:** `" + (string.IsNullOrWhiteSpace(subject.InputSha256) ? BenchmarkReportFacts.NotAvailable : subject.InputSha256) + "`");
        Line(sb, "- **Protocol:** " + ChatProtocolText(ctx) + "; " + D(ctx, "protocol.overridden") + "; alpha " + D(ctx, "protocol.alpha"));
        Line(sb, "- **Analysis code version:** " + Inv(subject.AnalysisCodeVersion));
        Line(sb, "- **Baseline runs:** " + ChatRunList(subject.BaselineRunIds));
        Line(sb, "- **Comparison runs:** " + ChatRunList(subject.ComparisonRunIds));
        Line(sb, "- **Control runs:** " + ChatRunList(subject.ControlRunIds));
        Line(sb, "- **" + Label("pricing.card") + ":** " + D(ctx, "pricing.card"));
        Line(sb, "- **Report format version:** " + version);
        Line(sb);
    }

    /// <summary>The terms the subject was evaluated under: the distillation prohibition, and whose content the document rests on.</summary>
    private static void ChatEvaluationTerms(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Heading(sb, "## Evaluation terms");
        Line(sb, "- **Distillation / training prohibition:** " + DistillationProhibition);
        Line(sb, "- **Third-party model content:** Outputs generated by **" + sheet.SubjectLabel + "** (" + sheet.SubjectProvider + ") "
            + "through the Overseer chat, measured by the Overseer benchmark and described in this document by **"
            + ctx.Document.WriterDisplayName + "** (" + ctx.Document.WriterProvider + "), are third-party content evaluated solely "
            + "for monitoring the Overseer chat and operational model selection.");
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
            + "limits, control-model names, hype words, spelling and the claim rules on change, cause, intent, mechanism, public "
            + "claims and hours; the checks do not verify the prose's interpretations.*");
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

    /// <summary><c>Quality (P1)</c>.</summary>
    private static string ChatEndpointName(Context ctx, string id)
    {
        string key = "endpoint." + id + ".name";
        return IsAvailable(ctx, key) ? D(ctx, key) + " (" + id + ")" : id;
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

    /// <summary><c>2026-09-01 00:00 UTC to 2026-09-08 00:00 UTC, 2 runs</c>.</summary>
    private static string ChatPeriodText(Context ctx, string period)
    {
        string p = "period." + period + ".";
        return D(ctx, p + "start") + " to " + D(ctx, p + "end") + ", " + D(ctx, p + "runs");
    }

    /// <summary>The hours the result holds for, or what stands in for them when the periods share none.</summary>
    private static string ChatHoursText(Context ctx)
        => IsAvailable(ctx, "scope.hours")
            ? D(ctx, "scope.hours")
            : "the hours both periods share, which could not be determined";

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
}
