namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

// Any change to rendered output requires bumping BenchmarkReportPackRenderer.ReportFormatVersion in
// the same commit as the updated golden files in Overseer.Tests/UnitTests/Golden/ReportPack/.

/// <summary>
/// The fixed serializer options of the report-pack JSON columns (FactsJson, ContentJson,
/// WriterOutputJson and ValidationNotesJson), used both ways.
/// </summary>
public static class BenchmarkReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options)
           ?? throw new JsonException($"The stored JSON does not hold a {typeof(T).Name}.");

    /// <summary>
    /// <paramref name="value"/> serialized with the fixed options, then indented two spaces with
    /// <c>\n</c> line breaks and every object's keys in ordinal order.
    /// </summary>
    public static string SerializeSorted<T>(T value)
    {
        var sorted = Sort(JsonNode.Parse(Serialize(value)));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            if (sorted == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                sorted.WriteTo(writer);
            }
        }

        return new UTF8Encoding(false).GetString(stream.ToArray());
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node.DeepClone()
    };
}

/// <summary>
/// Renders a stored report-pack document as Markdown at a disclosure level and peer naming. Reads
/// only the row's JSON columns and metadata: no model call, no service, no clock, no culture. The
/// same row and options always yield the same bytes: <c>\n</c> line breaks, invariant formatting,
/// and every collection in a stated order (questions by number, peers by letter, rows by R number,
/// facts by key, writer sections in the audience's slot order).
///
/// <para>A sheet with no peers renders the stand-alone form: no peer tables, no pairwise-significance
/// statement and no peer columns. The Report for AI Researchers and Developers prints one evidence
/// line beneath each strength, weakness and recommendation at Detailed and Full, and its question
/// details at those levels as a section of their own after the Reproducibility appendix; the Executive
/// Summary and the Report for AI Researchers and Developers end with the evaluation terms. Fact keys
/// are printed only through <see cref="BenchmarkReportFactLabels"/>.</para>
///
/// <para>With peers, the Executive Summary adds a How it compares section (the models' figures and
/// critical errors, a compact dimensions table and the writer's comparison paragraph), and the Report
/// for AI Researchers and Developers adds a Compared models table and a paired-difference column. Both
/// appear only on a sheet that carries the per-peer facts (format version 7 and later). Named copies
/// print peers' names wherever a fact states them by letter.</para>
///
/// <para>With <see cref="BenchmarkReportRenderOptions.Charts"/>, a document with peers carries one
/// <c>[[figure:&lt;key&gt;]]</c> line per chart at the anchor <see cref="BenchmarkReportChartPlacement"/>
/// gives it; the PDF and Word writers draw the chart there.</para>
///
/// <para>A battery sheet (<see cref="BenchmarkReportFactSheet.Battery"/>) names the battery instead of a
/// suite, adds the suites of the composite (and in the Report for AI Researchers and Developers the
/// battery profile, its weighting sensitivity and leave-one-suite-out figures), refers to every question
/// as <c>S&lt;suite&gt;-Q&lt;n&gt;</c>, and prints question details only for the questions given in detail.</para>
///
/// <para>A document stored under an earlier format version renders with what it stored: a missing
/// role, count or fact falls back to the wording that version printed, a missing slot or column is
/// left out, and its single-grader support labels are read in their provider wording.</para>
/// </summary>
public static partial class BenchmarkReportPackRenderer
{
    public const int ReportFormatVersion = 11;

    /// <summary>The format version of a comparison-scope document (<see cref="BenchmarkReportScope.Comparison"/>).</summary>
    public const int ComparisonReportFormatVersion = 12;

    /// <summary>The format version of a chat consistency document (<see cref="BenchmarkReportScope.ChatConsistency"/>), versioned on its own.</summary>
    public const int ChatConsistencyReportFormatVersion = 1;

    /// <summary>The format version a document of <paramref name="scope"/> is written and rendered under now.</summary>
    public static int CurrentFormatVersion(BenchmarkReportScope scope) => scope switch
    {
        BenchmarkReportScope.ChatConsistency => ChatConsistencyReportFormatVersion,
        BenchmarkReportScope.Comparison => ComparisonReportFormatVersion,
        _ => ReportFormatVersion
    };

    private const string PairedDifferenceNote = "Paired difference: mean per-question difference, subject minus peer, over the questions "
        + "both answered; 95 % paired-bootstrap interval. It reflects question sampling only, is not adjusted for comparing several "
        + "models, and is not a significance test.";

    /// <summary>Section 6 of the Internal Improvement Brief when the fact sheet is left out.</summary>
    private const string FactSheetElsewhere = "The fact sheet is in the Markdown copy of this document.";

    /// <summary>A question evidence line lists at most this many questions.</summary>
    private const int EvidenceQuestionLimit = 6;

    /// <summary>A finding's text in the findings table below Full disclosure is cut to this many characters.</summary>
    private const int FindingTextChars = 160;

    private const string ReferenceReaderCaveat = "Its scores do not count toward the score; its neutrality between the two panel families is an assumption.";

    private const string ProductName = "Overseer GnollHack Assistant Benchmark";
    private const string TokenPattern = @"\{\{([^{}]+)\}\}";
    private const string NoValue = "—";

    /// <summary>The run report's own prohibition sentence (BenchmarkReportBuilder, Compliance &amp; Evaluation Terms).</summary>
    private const string DistillationProhibition = "No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.";

    /// <summary>What a grader's provider that is also a peer's reads as when peers are anonymized.</summary>
    private const string WithheldProvider = "a withheld provider";

    /// <summary>What a grader whose provider is withheld reads as, in place of its name and model id.</summary>
    private const string WithheldGrader = "a model from " + WithheldProvider;

    private const string SummaryStamp = "Confidential. Prepared for the model's provider. Questions are described, not quoted.";
    private const string DetailedStamp = "Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.";
    private const string FullStamp = "INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.";

    // The Executive Summary quotes no question or rubric at any level.
    private const string ExecutiveDetailedStamp = "Confidential. Prepared for the model's provider. Review before sharing.";
    private const string ExecutiveFullStamp = "INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.";

    private const string SameCompanySentenceOne = "It is from the same company as the model under test, which may read it more favorably.";
    private const string SameCompanySentenceOfTwo = "One of them is from the same company as the model under test, which may read it more favorably.";
    private const string SameCompanySentenceBoth = "Both are from the same company as the model under test, which may read it more favorably.";

    private const string LeadsBanner = "Provisional and un-triaged. A lead is not a finding: it must go through the triage, "
        + "evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.";

    /// <summary>
    /// The levels offered for an audience: every level for the Report for AI Researchers and
    /// Developers, Summary and Full for the Executive Summary, whose Detailed text is its Summary
    /// text, and Full alone for the Internal Improvement Brief. <see cref="IsAllowed"/> also accepts
    /// the Executive Summary at Detailed.
    /// </summary>
    public static IReadOnlyList<BenchmarkReportDisclosure> AllowedDisclosures(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.InternalBrief => new[] { BenchmarkReportDisclosure.Full },
        BenchmarkReportAudience.ExecutiveSummary => new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Full },
        _ => new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full }
    };

    /// <summary>
    /// The levels a document renders at: those <see cref="AllowedDisclosures"/> offers, and the
    /// Executive Summary at Detailed too, which prints its Summary text under its own Detailed stamp.
    /// Either peer naming is allowed for all three audiences.
    /// </summary>
    public static bool IsAllowed(BenchmarkReportAudience audience, BenchmarkReportRenderOptions options)
    {
        if (options == null) return false;
        if (!Enum.IsDefined(audience) || !Enum.IsDefined(options.PeerNaming) || !Enum.IsDefined(options.Disclosure)) return false;
        if (audience == BenchmarkReportAudience.ExecutiveSummary && options.Disclosure == BenchmarkReportDisclosure.Detailed) return true;
        return AllowedDisclosures(audience).Contains(options.Disclosure);
    }

    /// <summary>
    /// A per-model document's title. A comparison-scope sheet's title is
    /// <see cref="BuildComparisonTitle"/>'s, numbered from the sheet's subject key and without the
    /// comparison's name, which only the stored comparison holds.
    /// </summary>
    public static string BuildTitle(BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.IsChatConsistency) return BuildChatConsistencyTitle(sheet);
        if (sheet.IsComparison)
        {
            string key = sheet.SubjectKey ?? string.Empty;
            string number = key.StartsWith(BenchmarkComparisonReportFacts.SubjectKeyPrefix, StringComparison.Ordinal)
                ? new string(key[BenchmarkComparisonReportFacts.SubjectKeyPrefix.Length..].TakeWhile(char.IsAsciiDigit).ToArray())
                : string.Empty;
            var comparison = int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                ? new Pdf.BenchmarkPdfComparison(id, null, sheet.ComparisonEntryCount)
                : null;
            return BuildComparisonTitle(audience, sheet, comparison, BenchmarkReportPeerNaming.Named);
        }
        return sheet.SubjectLabel + " on the " + ProductName + " — " + AudienceName(audience);
    }

    public static string Render(BenchmarkReportDocument document, BenchmarkReportRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!IsAllowed(document.Audience, options))
        {
            throw new ArgumentException(
                $"A {AudienceName(document.Audience)} cannot be rendered at {options.Disclosure} disclosure with {options.PeerNaming} peers.",
                nameof(options));
        }

        var ctx = new Context
        {
            Document = document,
            Options = options,
            Sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson),
            Content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson),
            Writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson),
            Notes = string.IsNullOrWhiteSpace(document.ValidationNotesJson)
                ? new List<BenchmarkReportValidationNote>()
                : BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(document.ValidationNotesJson)
        };
        BenchmarkReportFacts.NormalizeSupportLabels(ctx.Sheet);

        var sb = new StringBuilder();
        if (ctx.ChatConsistency)
        {
            RenderChatConsistency(sb, ctx);
            return sb.ToString();
        }
        if (ctx.Comparison)
        {
            RenderComparison(sb, ctx);
        }
        else
        {
            switch (document.Audience)
            {
                case BenchmarkReportAudience.ExecutiveSummary:
                    RenderExecutiveSummary(sb, ctx);
                    break;
                case BenchmarkReportAudience.TechnicalReport:
                    RenderTechnicalReport(sb, ctx);
                    break;
                default:
                    RenderInternalBrief(sb, ctx);
                    break;
            }
        }

        RemovedContent(sb, ctx);
        if (ctx.PrintsEvaluationTerms)
        {
            if (ctx.Comparison) ComparisonEvaluationTerms(sb, ctx);
            else EvaluationTerms(sb, ctx);
        }
        if (options.IncludeDocumentFooter)
        {
            Footer(sb, ctx);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Documents
    // ---------------------------------------------------------------------------------------------

    private static void RenderExecutiveSummary(StringBuilder sb, Context ctx)
    {
        TitleBlock(sb, ctx);

        Heading(sb, "## The result in one sentence");
        Line(sb, Prose(ctx, ctx.Writer.Headline));
        Line(sb);

        KeyFigures(sb, ctx, "## Key figures");
        if (ctx.Battery)
        {
            BatterySuites(sb, ctx, "## The suites of this battery");
        }
        if (ctx.ComparesPeers)
        {
            HowItCompares(sb, ctx);
        }

        Heading(sb, "## What it did well");
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.", strengths: true);

        Heading(sb, "## Where it fell short");
        Items(sb, ctx, ctx.Writer.Weaknesses, "No weaknesses were recorded.");

        Heading(sb, "## What this means for use as a game assistant");
        Slot(sb, ctx, BenchmarkReportSlots.Meaning);

        Heading(sb, "## How reliable this result is");
        Slot(sb, ctx, BenchmarkReportSlots.Confidence);
        IntervalSpanLine(sb, ctx);
        if (WriterIndependenceCaveat(ctx) is string caveat)
        {
            Line(sb, caveat);
            Line(sb);
        }
        if (!ctx.Standalone)
        {
            NoSignificance(sb, ctx);
        }

        AboutBenchmark(sb, ctx);
    }

    private static void RenderTechnicalReport(StringBuilder sb, Context ctx)
    {
        TitleBlock(sb, ctx);

        Heading(sb, "## Abstract");
        Slot(sb, ctx, BenchmarkReportSlots.Abstract);

        KeyFigures(sb, ctx, "## Key figures");
        SetupAndMethod(sb, ctx);
        if (ctx.Battery)
        {
            BatteryProfile(sb, ctx);
        }
        if (ctx.Standalone)
        {
            StandaloneResults(sb, ctx);
        }
        else
        {
            ResultsAgainstPeers(sb, ctx);
        }
        SpeedAndCost(sb, ctx);

        Heading(sb, "## Why it scored this way");
        Slot(sb, ctx, BenchmarkReportSlots.WhyItScored);
        Heading(sb, "### Weaknesses");
        Items(sb, ctx, ctx.Writer.Weaknesses, "No weaknesses were recorded.");

        Heading(sb, "## What worked well");
        Slot(sb, ctx, BenchmarkReportSlots.WhatWorked);
        Heading(sb, "### Strengths");
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.", strengths: true);

        Heading(sb, "## Recommendations for model developers");
        Recommendations(sb, ctx, BenchmarkReportSlots.TargetModelDevelopers);

        PerQuestion(sb, ctx, "## Per-question results", withDetails: false);
        ToolUse(sb, ctx);
        GraderReliability(sb, ctx);
        ThreatsToValidity(sb, ctx);
        Reproducibility(sb, ctx);

        if (ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
        {
            QuestionDetails(sb, ctx, ctx.Sheet.Questions.OrderBy(q => q.Number).ToList(),
                grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 2);
        }
    }

    private static void RenderInternalBrief(StringBuilder sb, Context ctx)
    {
        TitleBlock(sb, ctx);

        Heading(sb, "## 1. The Overseer chat and its tools");
        Slot(sb, ctx, BenchmarkReportSlots.OverseerChat);
        Heading(sb, "### Recommendations for the Overseer chat");
        Recommendations(sb, ctx, BenchmarkReportSlots.TargetOverseerChat);

        Heading(sb, "## 2. The benchmarking system");
        Slot(sb, ctx, BenchmarkReportSlots.BenchmarkSystem);
        Heading(sb, "### Recommendations for the benchmarking system");
        Recommendations(sb, ctx, BenchmarkReportSlots.TargetBenchmark);

        Heading(sb, "## 3. The model's result");
        Slot(sb, ctx, BenchmarkReportSlots.ModelResult);
        KeyFigures(sb, ctx, "### Key figures");
        IntervalSpanLine(sb, ctx);
        if (ctx.Battery)
        {
            BatterySuites(sb, ctx, "### The suites of this battery");
        }
        Figures(sb, ctx, BenchmarkReportChartAnchor.ModelResult);
        Heading(sb, "### Strengths");
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.", strengths: true);
        Heading(sb, "### Weaknesses");
        Items(sb, ctx, ctx.Writer.Weaknesses, "No weaknesses were recorded.");
        Heading(sb, "### Recommendations for model developers");
        Recommendations(sb, ctx, BenchmarkReportSlots.TargetModelDevelopers);

        Heading(sb, "## 4. Leads");
        Line(sb, "*" + LeadsBanner + "*");
        Line(sb);
        if (ctx.Writer.Leads.Count == 0)
        {
            Line(sb, "No leads were recorded.");
        }
        else
        {
            foreach (var lead in ctx.Writer.Leads)
            {
                Line(sb, "- **[" + lead.Triage + "]** " + Prose(ctx, lead.Text) + " *(" + SupportText(ctx, lead) + ")*");
            }
        }
        Line(sb);

        PerQuestion(sb, ctx, "## 5. Per-question results");

        Heading(sb, "## 6. Fact sheet");
        if (!ctx.Options.IncludeFactSheet)
        {
            Line(sb, FactSheetElsewhere);
            Line(sb);
            return;
        }
        Line(sb, "```json");
        Line(sb, BenchmarkReportJson.SerializeSorted(ctx.Anonymized ? AnonymizedSheet(ctx) : ctx.Sheet));
        Line(sb, "```");
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The title, then, with <see cref="BenchmarkReportRenderOptions.IncludeFrontMatter"/>, the
    /// comparison line of a document of a numbered comparison, the stamp and the facts list; with
    /// peers, the list names them under Compared with and states the pricing basis.
    /// </summary>
    private static void TitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Line(sb, "# " + BuildTitle(ctx.Document.Audience, sheet));
        Line(sb);
        if (!ctx.Options.IncludeFrontMatter) return;

        if (Pdf.BenchmarkPdfDocumentInfo.ComparisonOf(ctx.Document) is { } comparison)
        {
            Line(sb, "**Comparison:** " + Pdf.BenchmarkPdfDocumentInfo.ComparisonText(
                comparison, Pdf.BenchmarkPdfDocumentInfo.ComparisonComputedOn(sheet), ctx.Options.PeerNaming));
            Line(sb);
        }

        Line(sb, "*" + Stamp(ctx.Document.Audience, ctx.Options.Disclosure) + "*");
        Line(sb);
        Line(sb, "- **Date:** " + ctx.Document.CreatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (sheet.Battery is { } battery)
        {
            // A battery's member runs are listed under the Reproducibility appendix, not here.
            Line(sb, "- **Battery:** " + BatteryDescription(battery));
            Line(sb, "- **Questions:** " + D(ctx, "suite.questions"));
            Line(sb, "- **Member runs:** " + Inv(sheet.SubjectRunIds.Count) + " (battery run " + Inv(battery.BatteryRunId) + ")");
        }
        else
        {
            Line(sb, "- **Suite:** " + sheet.SuiteName);
            Line(sb, "- **Questions:** " + D(ctx, "suite.questions"));
            Line(sb, "- **Runs:** " + Inv(sheet.SubjectRunIds.Count) + " (" + (sheet.SubjectRunIds.Count == 1 ? "run " : "runs ")
                + string.Join(", ", sheet.SubjectRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture))) + ")");
        }
        if (ctx.Standalone)
        {
            Line(sb, "- **Peers:** " + PeersText(ctx));
        }
        else
        {
            Line(sb, "- **Compared with:** " + PeersText(ctx));
            Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + PricingBasisText(sheet).Text);
        }
        Line(sb);
    }

    private static string PeersText(Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        if (peers.Count == 0) return "none; this is a stand-alone report";

        if (ctx.Anonymized)
        {
            return peers.Count == 1
                ? "Model " + peers[0].Letter + ", identity withheld"
                : "Models " + BenchmarkReportFormat.LetterList(peers.Select(p => p.Letter).ToList()) + ", identities withheld";
        }

        return string.Join("; ", peers.Select(p => "Model " + p.Letter + " = " + p.Label + " (" + p.Provider + ", " + p.ModelId + ")"));
    }

    /// <summary>
    /// The key figures: intelligence with its interval, rank and overlap, speed as the median and mean
    /// answer time with its rank, cost and critical errors.
    /// </summary>
    private static void KeyFigures(StringBuilder sb, Context ctx, string heading)
    {
        Heading(sb, heading);

        var index = Fact(ctx, "quality.index");
        if (index == null || !index.Available)
        {
            Line(sb, "- **Intelligence:** " + NotAvailableText(index));
        }
        else
        {
            string text = index.Display;
            // A display stored before format version 3 carries its half-width and no interval.
            if (IsAvailable(ctx, "quality.interval") && !text.Contains('±'))
            {
                text += " (interval " + D(ctx, "quality.interval") + ")";
            }
            if (IsAvailable(ctx, "quality.rank")) text += ", " + D(ctx, "quality.rank");
            if (IsAvailable(ctx, "quality.intervalOverlap")) text += "; " + D(ctx, "quality.intervalOverlap");
            Line(sb, "- **Intelligence:** " + text + ".");
        }

        var speed = Fact(ctx, "speed.modelTimeP50");
        if (speed == null || !speed.Available)
        {
            Line(sb, "- **Speed:** " + NotAvailableText(speed));
        }
        else
        {
            string text = LowerFirst(Label("speed.modelTimeP50")) + " " + speed.Display;
            // A sheet stored before format version 9 has no mean.
            if (IsAvailable(ctx, "speed.modelTimeMean")) text += ", mean " + D(ctx, "speed.modelTimeMean");
            if (IsAvailable(ctx, "speed.rank")) text += ", " + D(ctx, "speed.rank");
            Line(sb, "- **Speed:** " + text + ".");
        }

        var cost = Fact(ctx, "cost.perQuestion");
        if (cost == null || !cost.Available)
        {
            Line(sb, "- **Cost:** " + NotAvailableText(cost));
        }
        else
        {
            string text = cost.Display + " per question";
            if (IsAvailable(ctx, "cost.rank")) text += ", " + D(ctx, "cost.rank");
            Line(sb, "- **Cost:** " + text + ".");
        }

        var errors = Fact(ctx, "errors.critical");
        Line(sb, "- **" + Label("errors.critical") + ":** " + (errors == null || !errors.Available ? NotAvailableText(errors) : errors.Display + "."));
        Line(sb);
    }

    /// <summary>
    /// The Executive Summary's comparison: every entry's Intelligence Index with its interval, median
    /// answer time, cost per question and critical errors, the subject in bold and then the peers by
    /// letter; the subject's dimensions against the peer mean; the figures of this anchor; and the
    /// writer's comparison paragraph where it was written.
    /// </summary>
    private static void HowItCompares(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## How it compares");
        Line(sb, "| Model | " + Label("quality.index") + " | " + Label("speed.modelTimeP50") + " | " + Label("cost.perQuestion")
            + " | " + Label("errors.critical") + " |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var (entry, peer) in OrderedEntries(ctx.Sheet))
        {
            string index = entry.QualityIndex.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value)
                    + (entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                        ? " (" + BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value) + ")"
                        : string.Empty)
                : BenchmarkReportFacts.NotAvailable;
            string time = !entry.SpeedDegraded && entry.ModelTimeP50Ms.HasValue
                ? BenchmarkReportFormat.Seconds(entry.ModelTimeP50Ms.Value)
                : BenchmarkReportFacts.NotAvailable;
            string cost = !entry.CostDegraded && entry.CostPerQuestionUsd.HasValue
                ? BenchmarkReportFormat.Usd(entry.CostPerQuestionUsd.Value)
                : BenchmarkReportFacts.NotAvailable;
            Line(sb, "| " + TableName(ctx, peer) + " | " + index + " | " + time + " | " + cost + " | " + CriticalErrorsCell(ctx, entry, peer) + " |");
        }
        Line(sb);

        Line(sb, "| Dimension | " + Cell(ctx.Sheet.SubjectLabel) + " | Peer mean | Difference |");
        Line(sb, "|---|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Dimensions)
        {
            string prefix = "dimension." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix) + " | " + Num(ctx, prefix + ".peerMean") + " | " + Num(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Figures(sb, ctx, BenchmarkReportChartAnchor.HowItCompares);
        OptionalSlot(sb, ctx, BenchmarkReportSlots.Comparison);
    }

    /// <summary>
    /// An entry's critical errors: its own <c>errors.critical</c> figure, else for the subject the
    /// sheet's; "not available" without either.
    /// </summary>
    private static string CriticalErrorsCell(Context ctx, BenchmarkReportEntryFigures entry, BenchmarkReportPeer? peer)
    {
        var own = entry.Extra?.FirstOrDefault(f => string.Equals(f.Key, "errors.critical", StringComparison.Ordinal));
        if (own != null) return own.Available ? Cell(own.Display) : BenchmarkReportFacts.NotAvailable;
        if (peer == null && Fact(ctx, "errors.critical") is { Available: true } sheetFigure) return Cell(sheetFigure.Display);
        return BenchmarkReportFacts.NotAvailable;
    }

    /// <summary>
    /// The interval, its span and the scored questions in one code-rendered sentence after the
    /// writer's reliability paragraph; nothing on a document stored without the span fact, or whose
    /// paragraph already places the interval token, as writers before format version 6 were asked to.
    /// </summary>
    private static void IntervalSpanLine(StringBuilder sb, Context ctx)
    {
        if (!IsAvailable(ctx, "quality.interval") || !IsAvailable(ctx, "quality.intervalSpan") || !IsAvailable(ctx, "quality.scoredItems")) return;
        if (ctx.Writer.Sections.TryGetValue(BenchmarkReportSlots.Confidence, out var confidence)
            && Regex.IsMatch(confidence ?? string.Empty, @"\{\{\s*quality\.interval\s*\}\}", RegexOptions.CultureInvariant))
        {
            return;
        }

        // A display stored before format version 6 carries no noun.
        string scored = D(ctx, "quality.scoredItems");
        if (!scored.EndsWith(" question", StringComparison.Ordinal) && !scored.EndsWith(" questions", StringComparison.Ordinal))
        {
            scored += " questions";
        }

        Line(sb, "The 95 % interval is " + D(ctx, "quality.interval") + ", a span of " + D(ctx, "quality.intervalSpan")
            + ", and rests on " + scored + " with a scored answer.");
        Line(sb);
    }

    /// <summary>
    /// The caveat of a document whose writer shares the subject's provider (trimmed, case-insensitive,
    /// as <see cref="BenchmarkComplianceGuard.IsSameProvider(string?, string?)"/> compares); null for an independent writer.
    /// </summary>
    private static string? WriterIndependenceCaveat(Context ctx)
    {
        string writer = ctx.Document.WriterProvider?.Trim() ?? string.Empty;
        string subject = ctx.Sheet.SubjectProvider?.Trim() ?? string.Empty;
        if (writer.Length == 0 || subject.Length == 0 || !string.Equals(writer, subject, StringComparison.OrdinalIgnoreCase)) return null;

        return "This document was written by " + ctx.Document.WriterDisplayName + ", a model from " + subject
            + ", the provider of the model under test. A writer from the same provider may describe it more favorably; "
            + "the figures and tables were computed by Overseer, not by the writer.";
    }

    /// <summary>
    /// Why no pair of models is tested for significance, in the document's own words, where the
    /// comparison left pairwise significance out; the comparison view's own sentences address its
    /// operator and are never printed.
    /// </summary>
    private static void NoSignificance(StringBuilder sb, Context ctx)
    {
        if (ctx.Standalone || string.IsNullOrWhiteSpace(ctx.Sheet.NoSignificanceSummary)) return;

        int models = ctx.Sheet.Peers.Count + 1;
        Line(sb, models == 2
            ? "The two models are not tested for significance, so a gap between them may be noise."
            : "No pair of models is tested for significance: with " + Inv(models) + " models, testing every pair would flag chance differences.");
        Line(sb);
    }

    private static void AboutBenchmark(StringBuilder sb, Context ctx)
    {
        var graders = ctx.Sheet.Graders;
        var memberA = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.PanelMemberARole);
        var memberB = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.PanelMemberBRole);
        var assessor = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.AssessorRole);
        bool verifier = graders.Any(g => g.Role == BenchmarkReportFacts.ClaimVerifierRole);

        string grading;
        string sameCompany = string.Empty;
        if (memberA != null && memberB != null)
        {
            grading = string.Equals(memberA.Provider, memberB.Provider, StringComparison.OrdinalIgnoreCase)
                ? "Two AI graders score every answer"
                : "Two AI graders from two different companies score every answer";
            int same = (memberA.SameFamilyAsSubject ? 1 : 0) + (memberB.SameFamilyAsSubject ? 1 : 0);
            sameCompany = same == 2 ? SameCompanySentenceBoth : same == 1 ? SameCompanySentenceOfTwo : string.Empty;
        }
        else
        {
            grading = "An AI grader scores every answer";
            var single = memberA ?? assessor;
            if (single is { SameFamilyAsSubject: true }) sameCompany = SameCompanySentenceOne;
        }

        Heading(sb, "## About this benchmark");
        Line(sb, "GnollHack is a roguelike game descended from NetHack. The Overseer is its AI assistant: players ask it "
            + "questions about the game, and it answers with the help of tools that search the game's source code, its "
            + "wiki and a knowledge base.");
        Line(sb);
        Line(sb, "This benchmark gives the production assistant prompt and tools, unchanged, a fixed set of single-turn "
            + "questions about the game, and asks for the concise answer style the live assistant uses.");
        Line(sb);
        Line(sb, grading + " for accuracy, completeness, conciseness and readability, weighted "
            + D(ctx, "scoring.weights") + "."
            + (sameCompany.Length > 0 ? " " + sameCompany : string.Empty)
            + (verifier ? " A separate verifier checks disputed claims against the game's source code." : string.Empty)
            + " Speed is the model's own time per answer, with time spent in tools excluded. " + CostSentence(ctx));
        Line(sb);
        Line(sb, "What it does not measure: conversations longer than one question, the wiki text the live assistant is "
            + "given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here "
            + "describes the assistant as configured for this benchmark; it may not carry over to those situations.");
        Line(sb);
    }

    /// <summary>What the cost figure is, by the pricing basis; a document without the basis kind keeps its stored sentence.</summary>
    private static string CostSentence(Context ctx)
    {
        switch (PricingBasisKind(ctx))
        {
            case BenchmarkReportFacts.PricingBasisCatalog:
                string date = IsAvailable(ctx, "comparison.pricedOn") ? " of " + D(ctx, "comparison.pricedOn") : string.Empty;
                return "Cost is the model under test's price per question at the catalog prices" + date
                    + ", which is comparable across dates but is not what was actually spent.";
            case BenchmarkReportFacts.PricingBasisSnapshot:
                return "Cost is what the model under test's answers actually cost, at the prices stored with each run.";
            default:
                return "Cost is list price for the model under test, on this basis: " + D(ctx, "comparison.pricingBasis");
        }
    }

    /// <summary>
    /// The pricing basis in one phrase, with the price card's date, and whether that date is in it;
    /// a document without the basis kind prints its stored basis sentence.
    /// </summary>
    private static (string Text, bool IncludesPriceCardDate) PricingBasisText(BenchmarkReportFactSheet sheet)
    {
        string SheetDisplay(string key) => SheetFact(sheet, key) is { } fact ? DisplayOf(fact) : BenchmarkReportFacts.NotAvailable;
        bool Available(string key) => SheetFact(sheet, key) is { Available: true };

        string card = Available("cost.pricingAsOf") ? " (price card dated " + SheetDisplay("cost.pricingAsOf") + ")" : string.Empty;
        switch (PricingBasisKind(sheet))
        {
            case BenchmarkReportFacts.PricingBasisCatalog:
                string date = Available("comparison.pricedOn") ? " on " + SheetDisplay("comparison.pricedOn") : string.Empty;
                return ("Catalog prices" + date + card, true);
            case BenchmarkReportFacts.PricingBasisSnapshot:
                return ("Prices stored with each run" + card, true);
            default:
                return (SheetDisplay("comparison.pricingBasis"), false);
        }
    }

    /// <summary>The pricing basis in one phrase, as a document's facts list and the PDF and Word covers print it.</summary>
    internal static string PricingBasisSummary(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return PricingBasisText(sheet).Text;
    }

    private static string? PricingBasisKind(Context ctx) => PricingBasisKind(ctx.Sheet);

    private static string? PricingBasisKind(BenchmarkReportFactSheet sheet)
        => SheetFact(sheet, "comparison.pricingBasisKind") is { Available: true } kind ? kind.Display : null;

    private static void SetupAndMethod(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        int runs = sheet.SubjectRunIds.Count;

        Heading(sb, "## Setup and method");
        if (sheet.Battery is { } battery)
        {
            Line(sb, "- **Battery:** " + BatteryDescription(battery) + "; " + D(ctx, "suite.questions") + " questions in all.");
        }
        else
        {
            Line(sb, "- **Suite:** " + sheet.SuiteName + ", " + D(ctx, "suite.questions") + " questions.");
        }
        Line(sb, "- **" + Label("config.chat") + ":** " + D(ctx, "config.chat"));
        Line(sb, "- **Model under test:** " + sheet.SubjectLabel + " (" + sheet.SubjectProvider + ", " + sheet.SubjectModelId
            + "), thinking level " + (sheet.SubjectThinkingLevel ?? "not set") + "; " + Inv(runs)
            + (ctx.Battery ? (runs == 1 ? " member run." : " member runs.") : (runs == 1 ? " run." : " runs.")));
        Line(sb, "- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted "
            + D(ctx, "scoring.weights") + ". Each dimension is graded on behaviorally anchored levels scored "
            + D(ctx, "scoring.levels") + ". A critical error caps the answer's quality at " + D(ctx, "scoring.criticalErrorCap") + ".");

        if (sheet.Graders.Count == 0)
        {
            Line(sb, "- **Graders:** not recorded.");
        }
        else
        {
            Line(sb, "- **Graders:**");
            foreach (var grader in sheet.Graders)
            {
                string identity = GraderWithheld(ctx, grader)
                    ? WithheldGrader
                    : grader.Label + " (" + grader.Provider + ", " + grader.ModelId + ")";
                Line(sb, "  - " + grader.Role + ": " + identity + ", "
                    + (grader.SameFamilyAsSubject ? "same provider as the model under test" : "different provider from the model under test"));
            }
        }

        Line(sb, "- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a "
            + "critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the "
            + "difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with "
            + "tool time excluded. Cost per question is the model under test's spend divided by the questions asked.");
        if (ctx.Battery)
        {
            Line(sb, "- **Battery composite:** the Overall Index is the sum over the suites of each suite's weight times its "
                + "Intelligence Index, under the battery's weighting scheme; it is not comparable with a single suite's index. "
                + "Speed and cost are pooled over the member runs, and cost per run is per battery pass, one run of every suite.");
            Line(sb, ctx.Standalone
                ? "- **Comparability:** this report describes the battery result on its own, measured in the comparability class `"
                    + D(ctx, "comparison.signature") + "`."
                : "- **Comparability:** every battery result in this report ran the same battery definition in one comparability class, `"
                    + D(ctx, "comparison.signature") + "`.");
        }
        else
        {
            Line(sb, ctx.Standalone
                ? "- **Comparability:** this report describes the model on its own, measured under the instrument condition with signature `"
                    + D(ctx, "comparison.signature") + "`."
                : "- **Comparability:** every model in this report was measured under one instrument condition, signature `"
                    + D(ctx, "comparison.signature") + "`.");
        }
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + PricingBasisText(sheet).Text);
        Line(sb, "- **Versions:** harness " + D(ctx, "run.harnessVersion") + ", scoring method " + D(ctx, "scoring.methodVersion") + ".");
        Line(sb);

        if (!ctx.Standalone)
        {
            ComparedModels(sb, ctx);
        }
    }

    /// <summary>
    /// One row per entry: its letter (the subject has none), kind, runs, thinking level, and where the
    /// sheet stores them, the harness version and the runs' dates. A column no entry has data for is
    /// left out; the letter column is printed under both namings, so a named and an anonymized copy can
    /// be matched.
    /// </summary>
    private static void ComparedModels(StringBuilder sb, Context ctx)
    {
        var rows = OrderedEntries(ctx.Sheet);
        if (rows.Count == 0) return;

        bool harness = rows.Any(r => r.Entry.HarnessVersion != null);
        bool dates = rows.Any(r => r.Entry.FirstRunUtc.HasValue || r.Entry.LastRunUtc.HasValue);

        var columns = new List<string> { "Letter", "Kind", "Runs", "Thinking level" };
        if (harness) columns.Add("Harness version");
        if (dates) columns.Add("Run dates (UTC)");

        Heading(sb, "### Compared models");
        TableHeader(sb, ctx, columns);
        foreach (var (entry, peer) in rows)
        {
            var cells = new List<string>
            {
                peer == null ? NoValue : peer.Letter,
                entry.EntryKey.StartsWith("group:", StringComparison.Ordinal) ? "group"
                    : entry.EntryKey.StartsWith("battery:", StringComparison.Ordinal) ? "battery" : "run",
                Inv(entry.RunCount),
                Cell((peer == null ? ctx.Sheet.SubjectThinkingLevel : peer.ThinkingLevel) ?? "not set")
            };
            if (harness) cells.Add(Cell(entry.HarnessVersion ?? NoValue));
            if (dates) cells.Add(RunDates(entry));
            TableRow(sb, ctx, peer, cells);
        }
        Line(sb);
    }

    /// <summary><c>2026-09-20</c>, or <c>2026-09-20 to 2026-09-22</c> when the runs span days; <see cref="NoValue"/> without dates.</summary>
    private static string RunDates(BenchmarkReportEntryFigures entry)
    {
        var first = entry.FirstRunUtc ?? entry.LastRunUtc;
        var last = entry.LastRunUtc ?? entry.FirstRunUtc;
        if (first == null || last == null) return NoValue;

        string a = first.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string b = last.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return string.Equals(a, b, StringComparison.Ordinal) ? a : a + " to " + b;
    }

    private static void ResultsAgainstPeers(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var rows = OrderedEntries(sheet);

        Heading(sb, "## Results against peers");

        // The paired-difference column exists on sheets stored from format version 7.
        bool paired = sheet.Peers.Any(p => Fact(ctx, BenchmarkReportFacts.PeerPrefix(p.Letter) + "pairedDifference") != null);

        Heading(sb, "### Quality");
        var qualityColumns = new List<string> { Label("quality.index"), Label("quality.interval"), "Rank" };
        if (paired) qualityColumns.Add("Paired difference");
        TableHeader(sb, ctx, qualityColumns);
        foreach (var (entry, peer) in rows)
        {
            string index = entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : BenchmarkReportFacts.NotAvailable;
            string interval = entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value)
                : BenchmarkReportFacts.NotAvailable;
            var cells = new List<string> { index, interval, QualityRankCell(ctx, entry) };
            if (paired) cells.Add(PairedCell(ctx, peer));
            TableRow(sb, ctx, peer, cells);
        }
        Line(sb);

        if (paired)
        {
            Line(sb, "*" + PairedDifferenceNote + "*");
            Line(sb);

            var unpaired = OrderedPeers(sheet)
                .Select(p => (Peer: p, Fact: Fact(ctx, BenchmarkReportFacts.PeerPrefix(p.Letter) + "pairedDifference")))
                .Where(x => x.Fact is { Available: false })
                .ToList();
            if (unpaired.Count > 0)
            {
                Line(sb, "No paired difference:");
                Line(sb);
                foreach (var (peer, fact) in unpaired)
                {
                    Line(sb, "- " + PlainName(ctx, peer) + ": " + NotAvailableReason(fact));
                }
                Line(sb);
            }
        }

        if (IsAvailable(ctx, "quality.intervalOverlap"))
        {
            Line(sb, "*" + sheet.SubjectLabel + ": " + D(ctx, "quality.intervalOverlap")
                + ". This describes where the intervals overlap; it is not a significance test.*");
            Line(sb);
        }
        foreach (var peer in OrderedPeers(sheet))
        {
            if (Fact(ctx, BenchmarkReportFacts.PeerPrefix(peer.Letter) + "pairedExcludesZero") is not { } excludes
                || !BenchmarkReportPackPrompt.IsTrue(excludes))
            {
                continue;
            }
            Line(sb, "On the same questions the paired difference with " + ProseName(ctx, peer)
                + " excludes zero (not adjusted for several comparisons).");
            Line(sb);
        }
        NoSignificance(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.QualityResults);

        Heading(sb, "### Speed");
        TableHeader(sb, ctx, new[] { Label("speed.modelTimeP50"), "Rank" });
        foreach (var (entry, peer) in rows)
        {
            string time = !entry.SpeedDegraded && entry.ModelTimeP50Ms.HasValue
                ? BenchmarkReportFormat.Seconds(entry.ModelTimeP50Ms.Value)
                : BenchmarkReportFacts.NotAvailable;
            TableRow(sb, ctx, peer, new[] { time, RankCell(entry.SpeedRank) });
        }
        Line(sb);
        DegradedNotes(sb, ctx, rows, "Not ranked on speed:", e => e.SpeedDegraded);
        Figures(sb, ctx, BenchmarkReportChartAnchor.SpeedResults);

        Heading(sb, "### Cost");
        TableHeader(sb, ctx, new[] { Label("cost.perQuestion"), "Rank" });
        foreach (var (entry, peer) in rows)
        {
            string cost = !entry.CostDegraded && entry.CostPerQuestionUsd.HasValue
                ? BenchmarkReportFormat.Usd(entry.CostPerQuestionUsd.Value)
                : BenchmarkReportFacts.NotAvailable;
            TableRow(sb, ctx, peer, new[] { cost, RankCell(entry.CostRank) });
        }
        Line(sb);
        DegradedNotes(sb, ctx, rows, "Not ranked on cost:", e => e.CostDegraded);
        Figures(sb, ctx, BenchmarkReportChartAnchor.CostResults);

        Heading(sb, "### Dimensions");
        Line(sb, "| Dimension | " + Cell(sheet.SubjectLabel) + " | Peer mean | Difference |");
        Line(sb, "|---|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Dimensions)
        {
            string prefix = "dimension." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix) + " | " + Num(ctx, prefix + ".peerMean") + " | " + Num(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Heading(sb, "### " + DifficultyBandsHeading);
        bool bandScores = HasBandScores(ctx);
        Line(sb, "| Difficulty band | Questions | " + AuthoredQuestionsColumn
            + (bandScores ? " | " + Cell(sheet.SubjectLabel) + " | Peer mean | Difference |" : " |"));
        Line(sb, bandScores ? "|---|---|---|---|---|---|" : "|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Bands)
        {
            string prefix = "band." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix + ".questions") + " | " + Num(ctx, AuthoredKey(key)) + " |"
                + (bandScores
                    ? " " + Num(ctx, prefix + ".score") + " | " + Num(ctx, prefix + ".peerMean") + " | " + Num(ctx, prefix + ".difference") + " |"
                    : string.Empty));
        }
        Line(sb);

        Heading(sb, "### Judge-dependent pairs");
        var pairs = Fact(ctx, "panel.judgeDependentPairs");
        Line(sb, pairs != null && pairs.Available
            ? "Pairs whose order depends on which panel member graded them, involving " + sheet.SubjectLabel + ": " + Shown(ctx, pairs) + "."
            : Label("panel.judgeDependentPairs") + ": " + NotAvailableText(pairs));
        Line(sb);

        Figures(sb, ctx, BenchmarkReportChartAnchor.ResultsAgainstPeersEnd);
    }

    private static void DegradedNotes(
        StringBuilder sb, Context ctx, IReadOnlyList<(BenchmarkReportEntryFigures Entry, BenchmarkReportPeer? Peer)> rows,
        string lead, Func<BenchmarkReportEntryFigures, bool> degraded)
    {
        var flagged = rows.Where(r => degraded(r.Entry)).ToList();
        if (flagged.Count == 0) return;

        Line(sb, lead);
        Line(sb);
        foreach (var (_, peer) in flagged)
        {
            string explanation = peer == null ? ctx.Sheet.SubjectExplanation : peer.Explanation;
            Line(sb, "- " + PlainName(ctx, peer) + ": " + (ctx.Comparison ? SheetText(ctx, explanation) : explanation));
        }
        Line(sb);
    }

    /// <summary>The stand-alone form of the results: the subject's own dimensions and bands, with no peer column.</summary>
    private static void StandaloneResults(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;

        Heading(sb, "## Results");

        Heading(sb, "### Dimensions");
        Line(sb, "| Dimension | " + Cell(sheet.SubjectLabel) + " |");
        Line(sb, "|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Dimensions)
        {
            Line(sb, "| " + name + " | " + Num(ctx, "dimension." + key) + " |");
        }
        Line(sb);

        Heading(sb, "### " + DifficultyBandsHeading);
        bool bandScores = HasBandScores(ctx);
        Line(sb, "| Difficulty band | Questions | " + AuthoredQuestionsColumn + (bandScores ? " | " + Cell(sheet.SubjectLabel) + " |" : " |"));
        Line(sb, bandScores ? "|---|---|---|---|" : "|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Bands)
        {
            string prefix = "band." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix + ".questions") + " | " + Num(ctx, AuthoredKey(key)) + " |"
                + (bandScores ? " " + Num(ctx, prefix + ".score") + " |" : string.Empty));
        }
        Line(sb);
    }

    /// <summary>Whether any difficulty band has a score; a sheet with none (a battery's) leaves the score columns out.</summary>
    private static bool HasBandScores(Context ctx)
        => BenchmarkReportFactLabels.Bands.Any(b => IsAvailable(ctx, "band." + b.Key + ".score"));

    /// <summary><c>Core knowledge, revision 3: 12 suites, 10 runs per suite, weighting scheme Questions and difficulty</c>.</summary>
    private static string BatteryDescription(BenchmarkReportBatterySubject battery)
        => battery.Name
           + (battery.Revision is int revision ? ", revision " + Inv(revision) : string.Empty)
           + ": " + Inv(battery.SuiteCount) + (battery.SuiteCount == 1 ? " suite, " : " suites, ")
           + Inv(battery.RunsPerSuite) + (battery.RunsPerSuite == 1 ? " run per suite" : " runs per suite")
           + ", weighting scheme " + battery.Scheme;

    /// <summary>The composite in one sentence, and each suite's weight and index with its interval.</summary>
    private static void BatterySuites(StringBuilder sb, Context ctx, string heading)
    {
        var battery = ctx.Sheet.Battery!;
        Heading(sb, heading);
        Line(sb, "This result is a battery of " + Inv(battery.SuiteCount) + (battery.SuiteCount == 1 ? " suite" : " suites")
            + ", each run " + Inv(battery.RunsPerSuite) + (battery.RunsPerSuite == 1 ? " time" : " times")
            + ". Its Intelligence Index is the battery's Overall Index: the suites' indices weighted under the "
            + battery.Scheme + " scheme. It is not comparable with a single suite's Intelligence Index.");
        Line(sb);
        Line(sb, "| Suite | Weight | " + Label("quality.index") + " |");
        Line(sb, "|---|---|---|");
        foreach (var suite in battery.Suites.OrderBy(s => s.Number))
        {
            string prefix = "suite." + Inv(suite.Number) + ".";
            string index = IsAvailable(ctx, prefix + "index")
                ? Cell(D(ctx, prefix + "index")) + (IsAvailable(ctx, prefix + "interval") ? " (" + Cell(D(ctx, prefix + "interval")) + ")" : string.Empty)
                : NoValue;
            Line(sb, "| S" + Inv(suite.Number) + " · " + Cell(suite.Name) + " | " + Num(ctx, prefix + "weight") + " | " + index + " |");
        }
        Line(sb);
    }

    /// <summary>
    /// The battery's profile: each suite's row of the persisted analysis, the battery-wide figures, the
    /// index under the other weighting schemes and with each suite left out.
    /// </summary>
    private static void BatteryProfile(StringBuilder sb, Context ctx)
    {
        var battery = ctx.Sheet.Battery!;
        Heading(sb, "## Battery profile");
        Line(sb, "The Overall Index is the sum over the suites of each suite's weight times its Intelligence Index, under the "
            + battery.Scheme + " scheme. It is not comparable with a single suite's Intelligence Index.");
        Line(sb);
        Line(sb, "| Suite | Weight | " + Label("quality.index") + " | " + Label("quality.interval") + " | Contribution | "
            + Label("quality.scoredItems") + " | Runs | Speed Index | Cost per run, graders included | Critical-error rate |");
        Line(sb, "|---|---|---|---|---|---|---|---|---|---|");
        foreach (var suite in battery.Suites.OrderBy(s => s.Number))
        {
            string prefix = "suite." + Inv(suite.Number) + ".";
            Line(sb, "| S" + Inv(suite.Number) + " · " + Cell(suite.Name)
                + " | " + Num(ctx, prefix + "weight")
                + " | " + Num(ctx, prefix + "index")
                + " | " + Num(ctx, prefix + "interval")
                + " | " + Num(ctx, prefix + "contribution")
                + " | " + Num(ctx, prefix + "scoredItems")
                + " | " + Num(ctx, prefix + "runs")
                + " | " + Num(ctx, prefix + "speedIndex")
                + " | " + Num(ctx, prefix + "costPerRun")
                + " | " + Num(ctx, prefix + "criticalErrorRate")
                + " |");
        }
        Line(sb);
        Line(sb, "*A suite's cost per run is its mean over its member runs, graders included, at the prices stored with each run.*");
        Line(sb);

        foreach (var (key, label) in new[]
        {
            ("battery.rounds", "Complete rounds"),
            ("battery.suiteIndexSd", "Standard deviation of the suite indices"),
            ("battery.suiteIndexRange", "Range of the suite indices"),
            ("battery.pooledIdentity", "Pooled identity"),
            ("battery.criticalErrorRate", "Critical-error rate"),
            ("battery.speedIndex", "Speed Index"),
            ("battery.excludedMembers", "Member runs left out of the analysis")
        })
        {
            Line(sb, "- **" + label + ":** " + (Fact(ctx, key) is { } fact ? (fact.Available ? Shown(ctx, fact) : NotAvailableText(fact)) : BenchmarkReportFacts.NotAvailable));
        }
        Line(sb);

        var sensitivity = ctx.Sheet.Facts
            .Where(f => f.Available && f.Key.StartsWith("sensitivity.", StringComparison.Ordinal))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
        if (sensitivity.Count > 0)
        {
            Heading(sb, "### Weighting sensitivity");
            foreach (var fact in sensitivity)
            {
                Line(sb, "- " + OneLine(fact.Display));
            }
            Line(sb);
        }

        var leaveOneOut = ctx.Sheet.Facts
            .Where(f => f.Key.StartsWith("loo.", StringComparison.Ordinal))
            .OrderBy(f => NumberAfter(f.Key, "loo."))
            .ToList();
        if (leaveOneOut.Count > 0)
        {
            Heading(sb, "### Leave one suite out");
            foreach (var fact in leaveOneOut)
            {
                Line(sb, "- S" + Inv(NumberAfter(fact.Key, "loo.")) + ": " + (fact.Available ? OneLine(fact.Display) : NotAvailableText(fact)));
            }
            Line(sb);
        }
    }

    /// <summary>The battery analysis's caveats, in their stored order.</summary>
    private static List<BenchmarkReportFact> BatteryCaveats(Context ctx)
        => ctx.Sheet.Facts
            .Where(f => f.Available && f.Key.StartsWith("battery.caveat.", StringComparison.Ordinal))
            .OrderBy(f => NumberAfter(f.Key, "battery.caveat."))
            .ToList();

    /// <summary>The number that follows <paramref name="prefix"/> in a key; <see cref="int.MaxValue"/> when none does.</summary>
    private static int NumberAfter(string key, string prefix)
        => key.StartsWith(prefix, StringComparison.Ordinal)
           && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : int.MaxValue;

    /// <summary>The difficulty-band table's heading: its bands are the assessed ones.</summary>
    private const string DifficultyBandsHeading = "Difficulty bands (assessed)";

    /// <summary>
    /// The difficulty-band table's column of questions authored in each band; <see cref="NoValue"/> on a
    /// sheet stored before format version 9.
    /// </summary>
    private const string AuthoredQuestionsColumn = "Authored questions";

    private static string AuthoredKey(string band) => "bands.authored." + band;

    /// <summary>The speed and cost facts the sheet states, one row each; nothing when it states none.</summary>
    private static void SpeedAndCost(StringBuilder sb, Context ctx)
    {
        var facts = SpeedAndCostKeys()
            .Select(key => Fact(ctx, key))
            .Where(f => f is { Available: true })
            .Select(f => f!)
            .ToList();
        if (facts.Count == 0) return;

        Heading(sb, "## Speed and cost");
        Line(sb, "| Measure | " + Cell(ctx.Sheet.SubjectLabel) + " |");
        Line(sb, "|---|---|");
        foreach (var fact in facts)
        {
            Line(sb, "| " + Cell(SpeedAndCostLabel(ctx, fact.Key)) + " | " + Cell(fact.Display) + " |");
        }
        Line(sb);

        Figures(sb, ctx, BenchmarkReportChartAnchor.SpeedAndCost);
    }

    /// <summary>A Speed and cost row's label: a battery's per-run costs are per battery pass.</summary>
    private static string SpeedAndCostLabel(Context ctx, string key)
    {
        if (ctx.Battery)
        {
            if (key == "cost.perQuestion") return "Candidate cost per question";
            if (key == "cost.perRun") return "Candidate cost per battery pass";
            if (key == "cost.totalRunPerRun") return "Total cost per battery pass (every grading and synthesis role; report writer excluded)";
        }
        return Label(key);
    }

    /// <summary>The facts of the Speed and cost table, in order.</summary>
    internal static IReadOnlyList<string> SpeedAndCostKeys() => new[]
    {
        "speed.modelTimeP50", "speed.modelTimeMean", "speed.modelTimeP90", "cost.perQuestion", "cost.perRun", "cost.totalRunPerRun",
        "tokens.inputPerQuestion", "tokens.outputPerQuestion"
    };

    private static void Recommendations(StringBuilder sb, Context ctx, string target)
    {
        var items = ctx.Writer.Recommendations
            .Where(r => string.Equals(r.For, target, StringComparison.Ordinal))
            .Cast<BenchmarkReportWriterItem>()
            .ToList();
        Items(sb, ctx, items, "No recommendations were recorded.");
    }

    /// <summary>
    /// The per-question table, with each question's assessed and authored band, and the notes on the
    /// questions that need one; with <paramref name="withDetails"/>, the question details at Detailed
    /// and Full as a subsection.
    /// </summary>
    private static void PerQuestion(StringBuilder sb, Context ctx, string heading, bool withDetails = true)
    {
        var sheet = ctx.Sheet;
        var questions = sheet.Questions.OrderBy(q => q.Number).ToList();

        // Answer sentences need every question's count; a sheet stored without them keeps its claims column.
        bool sentences = questions.Count > 0 && questions.All(q => q.RefutedAnswerSentences.HasValue);
        string refutedHeader = sentences ? "Refuted answer sentences" : "Refuted claims";

        Heading(sb, heading);
        if (ctx.Battery)
        {
            BatteryQuestionTable(sb, ctx, questions, sentences, refutedHeader);
            BatteryNotesAndDetails(sb, ctx, questions, withDetails);
            return;
        }
        if (ctx.Standalone)
        {
            Line(sb, "| Q | Topic | Assessed band | Authored | Score | Critical error | " + refutedHeader + " | Tool calls | Model time |");
            Line(sb, "|---|---|---|---|---|---|---|---|---|");
        }
        else
        {
            Line(sb, "| Q | Topic | Assessed band | Authored | Score | Peer mean | Difference | Critical error | " + refutedHeader + " | Tool calls | Model time |");
            Line(sb, "|---|---|---|---|---|---|---|---|---|---|---|");
        }
        foreach (var q in questions)
        {
            // The difference of the two printed whole numbers, so the row's arithmetic holds.
            string difference = q.Score.HasValue && q.PeerMean.HasValue
                ? BenchmarkReportFormat.WholeDifference(q.Score.Value, q.PeerMean.Value)
                : q.Difference.HasValue ? BenchmarkReportFormat.Signed(q.Difference.Value) : NoValue;
            string peerCells = ctx.Standalone
                ? string.Empty
                : " | " + (q.PeerMean.HasValue ? BenchmarkReportFormat.Whole(q.PeerMean.Value) : NoValue)
                  + " | " + difference;
            Line(sb, "| " + ctx.QuestionLabel(q.Number)
                + " | " + Cell(Topic(ctx, q.Number) ?? NoValue)
                + " | " + q.Band
                + " | " + (string.IsNullOrWhiteSpace(q.AuthoredBand) ? NoValue : Cell(q.AuthoredBand))
                + " | " + (q.Score.HasValue ? BenchmarkReportFormat.Whole(q.Score.Value) : NoValue)
                + peerCells
                + " | " + (q.CriticalError ? "yes" : "no")
                + " | " + Inv(sentences ? q.RefutedAnswerSentences!.Value : q.RefutedClaims)
                + " | " + BenchmarkReportFormat.CompactDecimal(q.ToolCalls)
                + " | " + (q.ModelTimeMs.HasValue ? BenchmarkReportFormat.Seconds(q.ModelTimeMs.Value) : NoValue)
                + " |");
        }
        Line(sb);

        var needingNote = BenchmarkReportPackPrompt.QuestionsNeedingNote(sheet);
        var noted = questions.Where(q => needingNote.Contains(q.Number)).ToList();
        if (ctx.Standalone)
        {
            string below = BenchmarkReportFormat.Whole(BenchmarkReportPackPrompt.StandaloneNoteScore);
            Heading(sb, "### Questions scoring below " + below + " or with a critical error");
            if (noted.Count == 0)
            {
                Line(sb, "No question scored below " + below + " or carried a critical error.");
                Line(sb);
            }
        }
        else
        {
            string gap = BenchmarkReportFormat.Whole(BenchmarkReportPackPrompt.QuestionNoteGapPoints);
            Heading(sb, "### Questions more than " + gap + " points below the peer mean, or with a critical error");
            if (noted.Count == 0)
            {
                Line(sb, "No question was more than " + gap + " points below the peer mean or carried a critical error.");
                Line(sb);
            }
        }

        foreach (var q in noted)
        {
            string? topic = Topic(ctx, q.Number);
            var note = ctx.Writer.QuestionNotes.FirstOrDefault(n => n.Question == q.Number);
            Line(sb, "**" + ctx.QuestionLabel(q.Number) + "**" + (topic != null ? " (" + topic + ")" : string.Empty) + ": "
                + (note != null ? Prose(ctx, note.Note) : "No note was written for this question."));
            Line(sb);
        }

        if (withDetails && ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
        {
            QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 3);
        }
    }

    /// <summary>
    /// A battery's per-question table: each question by its suite-qualified reference, with its mean
    /// score over the runs that scored it, those runs, and the rounds with a critical error. The runs
    /// column is left out when one run scored every question.
    /// </summary>
    private static void BatteryQuestionTable(
        StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportQuestion> questions, bool sentences, string refutedHeader)
    {
        bool oneRunEach = questions.Count > 0 && questions.All(q => q.RunCount == 1);
        Line(sb, "| Question | Topic | Assessed band | Authored | Mean score | " + (oneRunEach ? string.Empty : "Runs scored | ")
            + "Critical errors | " + refutedHeader + " | Tool calls | Model time |");
        Line(sb, oneRunEach ? "|---|---|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|---|---|---|---|");
        foreach (var q in questions)
        {
            Line(sb, "| " + ctx.QuestionLabel(q.Number)
                + " | " + Cell(Topic(ctx, q.Number) ?? NoValue)
                + " | " + q.Band
                + " | " + (string.IsNullOrWhiteSpace(q.AuthoredBand) ? NoValue : Cell(q.AuthoredBand))
                + " | " + (q.Score.HasValue ? BenchmarkReportFormat.Whole(q.Score.Value) : NoValue)
                + (oneRunEach ? string.Empty : " | " + Inv(q.RunCount))
                + " | " + Inv(q.CriticalErrorCount ?? (q.CriticalError ? 1 : 0))
                + " | " + Inv(sentences ? q.RefutedAnswerSentences!.Value : q.RefutedClaims)
                + " | " + BenchmarkReportFormat.CompactDecimal(q.ToolCalls)
                + " | " + (q.ModelTimeMs.HasValue ? BenchmarkReportFormat.Seconds(q.ModelTimeMs.Value) : NoValue)
                + " |");
        }
        Line(sb);
        Line(sb, oneRunEach
            ? "*One member run scored each question, so each mean score is that run's score; its critical errors count whether that run had a critical error.*"
            : "*Each question's mean score is over the member runs that scored it; its critical errors count those runs with a critical error.*");
        Line(sb);
    }

    /// <summary>
    /// A battery's notes, on the questions given in detail that scored below
    /// <see cref="BenchmarkReportPackPrompt.StandaloneNoteScore"/> or carried a critical error, and, with
    /// <paramref name="withDetails"/> at Detailed and Full, the questions given in detail.
    /// </summary>
    private static void BatteryNotesAndDetails(
        StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportQuestion> questions, bool withDetails)
    {
        var needingNote = BenchmarkReportPackPrompt.QuestionsNeedingNote(ctx.Sheet);
        var noted = questions.Where(q => needingNote.Contains(q.Number)).ToList();
        string below = BenchmarkReportFormat.Whole(BenchmarkReportPackPrompt.StandaloneNoteScore);

        Heading(sb, "### Questions given in detail that scored below " + below + " or had a critical error");
        if (noted.Count == 0)
        {
            Line(sb, "No question given in detail scored below " + below + " or had a critical error.");
            Line(sb);
        }
        foreach (var q in noted)
        {
            string? topic = Topic(ctx, q.Number);
            var note = ctx.Writer.QuestionNotes.FirstOrDefault(n => n.Question == q.Number);
            Line(sb, "**" + ctx.QuestionLabel(q.Number) + "**" + (topic != null ? " (" + topic + ")" : string.Empty) + ": "
                + (note != null ? Prose(ctx, note.Note) : "No note was written for this question."));
            Line(sb);
        }

        if (withDetails && ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
        {
            QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 3);
        }
    }

    /// <summary>
    /// Every question with each run's answer excerpt; with <paramref name="grading"/>, the complete
    /// answer where it was captured, and also the rubric, the graders' verdicts and the claim
    /// verifier's rulings. The section's heading is at <paramref name="level"/>, each question's one
    /// level below it and each run's two.
    /// </summary>
    private static void QuestionDetails(
        StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportQuestion> questions, bool grading, int level)
    {
        string prefix = new('#', level);
        Heading(sb, prefix + (grading ? " Question details" : " Questions and answers"));

        if (ctx.Battery)
        {
            questions = questions.Where(q => ContentFor(ctx, q.Number).Count > 0).ToList();
            Line(sb, questions.Count == 0
                ? "*No question was given in detail. Every question is listed under Per-question results.*"
                : "*Only the questions given in detail are shown, each with one answer: from the run whose score was the median of its rounds. Every question is listed under Per-question results.*");
            Line(sb);
        }

        foreach (var q in questions)
        {
            string? topic = Topic(ctx, q.Number);
            Heading(sb, prefix + "# " + ctx.QuestionLabel(q.Number) + (topic != null ? ": " + topic : string.Empty));

            var blocks = ContentFor(ctx, q.Number);
            if (blocks.Count == 0)
            {
                Line(sb, "*No verbatim content was captured for this question.*");
                Line(sb);
                continue;
            }

            var first = blocks[0].Question;
            Line(sb, "**Question:**");
            Line(sb);
            Quote(sb, first.QuestionText);
            Line(sb);

            if (grading)
            {
                Line(sb, "**Rubric:**");
                Line(sb);
                if (!first.ExpectedPointsRecorded)
                {
                    Line(sb, "*Rubric not recorded for this answer.*");
                }
                else if (string.IsNullOrWhiteSpace(first.ExpectedPoints))
                {
                    Line(sb, "*No rubric points.*");
                }
                else
                {
                    Quote(sb, first.ExpectedPoints);
                }
                Line(sb);
            }

            foreach (var (runId, item) in blocks)
            {
                if (ctx.Content.Runs.Count > 1)
                {
                    Heading(sb, prefix + "## Run " + Inv(runId));
                }

                if (!grading)
                {
                    Line(sb, "**Answer excerpt:**");
                    Line(sb);
                    Quote(sb, item.AnswerExcerpt);
                    Line(sb);
                    continue;
                }

                if (item.AnswerText != null || !item.AnswerExcerptCut)
                {
                    Line(sb, "**Answer:**");
                    Line(sb);
                    Quote(sb, item.AnswerText ?? item.AnswerExcerpt);
                    Line(sb);
                }
                else
                {
                    Line(sb, "**Answer excerpt:**");
                    Line(sb);
                    Quote(sb, item.AnswerExcerpt);
                    Line(sb);
                    Line(sb, "*This document predates complete-answer capture; the answer is shown as the excerpt stored when it was written.*");
                    Line(sb);
                }

                Line(sb, "**Graders:**");
                Line(sb);
                if (item.Graders.Count == 0)
                {
                    Line(sb, "- No grader verdict was recorded.");
                }
                foreach (var grader in item.Graders)
                {
                    var graderOnSheet = ctx.Sheet.Graders.FirstOrDefault(g => string.Equals(g.Role, grader.Role, StringComparison.Ordinal));
                    Line(sb, "- **" + grader.Role + " (" + (GraderWithheld(ctx, graderOnSheet) ? WithheldGrader : grader.Label) + "):** "
                        + (grader.Score.HasValue ? "score " + Inv(grader.Score.Value) + "." : "not scored.")
                        + (string.IsNullOrWhiteSpace(grader.Comment) ? string.Empty : " " + OneLine(grader.Comment)));
                    foreach (var evidence in grader.Evidence)
                    {
                        Line(sb, "  - " + OneLine(evidence));
                    }
                }
                Line(sb);

                Line(sb, "**Claim verifier:**");
                Line(sb);
                if (item.ClaimRulings.Count == 0)
                {
                    Line(sb, "- No claims were checked.");
                }
                foreach (var ruling in item.ClaimRulings)
                {
                    Line(sb, "- **" + BenchmarkReportContent.RulingLabel(ruling.Role, ruling.Verdict) + ":** \"" + OneLine(ruling.Claim) + "\""
                        + (string.IsNullOrWhiteSpace(ruling.Rationale) ? string.Empty : " — " + OneLine(ruling.Rationale)));
                }
                Line(sb);
            }
        }
    }

    private static void ToolUse(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## Tool-use behavior");
        Line(sb, "- **" + Label("tools.callsPerQuestion") + ":** " + D(ctx, "tools.callsPerQuestion")
            + (ctx.Standalone ? string.Empty : " (peer mean " + D(ctx, "tools.callsPerQuestion.peerMean") + ")"));
        foreach (string key in new[]
        {
            "tools.share.sourceCode", "tools.share.wiki", "tools.share.structuredLookup", "tools.share.knowledgeBase", "tools.share.other",
            "tools.zeroKnowledgeBaseAnswers", "tools.failed", "tools.refusedByBudget"
        })
        {
            // The knowledge-base count is stated only where some question is a knowledge-base topic.
            if (key == "tools.zeroKnowledgeBaseAnswers" && !IsAvailable(ctx, key)) continue;
            LabeledLine(sb, ctx, key);
        }
        Line(sb);

        Line(sb, "*Recorded success or failure describes whether a tool call executed. It does not show that the query was "
            + "well chosen, that the result was relevant, or that the corpus was current.*");
        Line(sb);

        if (ctx.Options.Disclosure == BenchmarkReportDisclosure.Full)
        {
            // tools.failed exists only where the runs recorded per-call rows (harness 17 and later); a
            // battery's per-call rows stay with its member runs.
            Line(sb, ctx.Battery
                ? "*Per-call arguments and results are in each member run's Tool-call log until the retention sweep prunes them.*"
                : IsAvailable(ctx, "tools.failed")
                    ? "*Per-call arguments and results are in each run's Tool-call log until the retention sweep prunes them.*"
                    : "*These runs did not record per-call arguments or results.*");
            Line(sb);
        }
    }

    private static void GraderReliability(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        int runCount = sheet.SubjectRunIds.Count;

        Heading(sb, "## Grader reliability");
        foreach (string key in new[] { "panel.meanAbsDelta", "panel.icc", "panel.disagreements", "panel.memberAAlone", "panel.memberBAlone" })
        {
            LabeledLine(sb, ctx, key);
        }
        ReferenceReaderLines(sb, ctx);
        StyleLine(sb, ctx);
        Line(sb);

        var rows = OrderedRows(sheet);
        if (rows.Count == 0)
        {
            Line(sb, ctx.Battery
                ? "A battery report lists no synthesis findings; each member run's report has its own."
                : "No synthesis findings were recorded.");
            Line(sb);
            return;
        }

        // The findings' own wording may quote questions and answers, which Summary disclosure never prints.
        if (ctx.Options.Disclosure == BenchmarkReportDisclosure.Summary)
        {
            Line(sb, "*The graders' findings are listed at Detailed and Full disclosure only, since their wording may quote the benchmark's questions and answers.*");
            Line(sb);
            return;
        }

        // Full lists each finding's whole text below the table; Detailed carries it in the table, cut.
        bool textInTable = ctx.Options.Disclosure != BenchmarkReportDisclosure.Full;
        bool recurrence = runCount > 1;
        Line(sb, "| Row | Finding | Questions | Support |" + (recurrence ? " Recurrence |" : string.Empty));
        Line(sb, "|---|---|---|---|" + (recurrence ? "---|" : string.Empty));
        foreach (var row in rows)
        {
            string kind = row.Status == nameof(BenchmarkConvergenceStatus.Conflicting)
                ? row.Kind + " (A) vs " + BenchmarkSynthesisConvergence.OppositeKind(row.Kind) + " (B)"
                : row.Kind;
            string finding = kind + " · " + row.Category.Replace('_', ' ');
            if (textInTable && FindingText(row) is string text)
            {
                finding += ": " + text;
            }
            string questions = row.Questions.Count > 0
                ? string.Join(", ", row.Questions.OrderBy(n => n).Select(ctx.QuestionLabel))
                : NoValue;
            Line(sb, "| " + row.Id + " | " + Cell(finding) + " | " + questions + " | " + row.SupportLabel + " |"
                + (recurrence ? " " + Inv(row.Recurrence) + " of " + Inv(runCount) + " runs |" : string.Empty));
        }
        Line(sb);

        if (ctx.Options.Disclosure == BenchmarkReportDisclosure.Full)
        {
            foreach (var row in rows)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(row.MemberAText))
                {
                    parts.Add((row.Status == BenchmarkReportFacts.SingleStatus ? BenchmarkReportFacts.AssessorRole : BenchmarkReportFacts.PanelMemberARole)
                        + ": " + OneLine(row.MemberAText));
                }
                if (!string.IsNullOrWhiteSpace(row.MemberBText))
                {
                    parts.Add(BenchmarkReportFacts.PanelMemberBRole + ": " + OneLine(row.MemberBText));
                }
                Line(sb, "- **" + row.Id + ":** " + (parts.Count > 0 ? string.Join(" · ", parts) : "no text recorded"));
            }
            Line(sb);
        }
    }

    /// <summary>
    /// A finding row's text on one line, cut to <see cref="FindingTextChars"/> characters: member A's,
    /// with member B's beside it when the members disagree; null when none is recorded.
    /// </summary>
    private static string? FindingText(BenchmarkReportFindingRow row)
    {
        string a = OneLine(row.MemberAText).Trim();
        string b = OneLine(row.MemberBText).Trim();
        string text = row.Status == nameof(BenchmarkConvergenceStatus.Conflicting) && a.Length > 0 && b.Length > 0
            ? "A: " + a + " B: " + b
            : a.Length > 0 ? a : b;
        return text.Length == 0 ? null : BenchmarkReportContent.Excerpt(text, FindingTextChars).Excerpt;
    }

    /// <summary>
    /// The reference reader's index and its mean offset from the panel, each where the sheet states
    /// it, the last followed by <see cref="ReferenceReaderCaveat"/>; nothing without either.
    /// </summary>
    private static void ReferenceReaderLines(StringBuilder sb, Context ctx)
    {
        var lines = new[] { "panel.referenceReaderIndex", "panel.referenceReaderOffset" }
            .Where(key => IsAvailable(ctx, key))
            .Select(key => "- **" + Label(key) + ":** " + D(ctx, key))
            .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            Line(sb, i == lines.Count - 1 ? lines[i] + ". " + ReferenceReaderCaveat : lines[i]);
        }
    }

    /// <summary>The response-style line: the conflict as a clause, <c>none</c> without one.</summary>
    private static void StyleLine(StringBuilder sb, Context ctx)
    {
        const string key = "style.responseStyleConflict";
        var fact = Fact(ctx, key);
        if (fact is { Available: true } && !BenchmarkReportPackPrompt.IsTrue(fact)
            && !fact.Display.StartsWith("yes", StringComparison.OrdinalIgnoreCase))
        {
            Line(sb, "- **" + Label(key) + ":** none");
            return;
        }
        LabeledLine(sb, ctx, key);
    }

    private static void ThreatsToValidity(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;

        Heading(sb, "## Threats to validity");
        Line(sb, "- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation "
            + "history, pre-injected wiki context, spoiler-free mode, web search or subagents.");
        Line(sb, "- Interval: " + D(ctx, "quality.intervalBasis"));
        Line(sb, "- The graders are AI models. Each grader's provider relation to the model under test is stated under "
            + "Setup and method; a grader from the model's own provider may read it more favorably.");
        if (string.Equals(sheet.SubjectState, "Degraded", StringComparison.Ordinal))
        {
            Line(sb, "- Comparability: " + sheet.SubjectExplanation);
        }
        if (ctx.Battery)
        {
            Line(sb, "- Composite: the Overall Index weights the suites under the battery's scheme; another scheme or "
                + "another set of suites gives another figure, and it is not comparable with a single suite's index.");
            Line(sb, "- Detail: the writer was given the full text of at most a few questions per suite, each with one answer, "
                + "and the other questions as one-line rows.");
            foreach (var fact in BatteryCaveats(ctx))
            {
                Line(sb, "- Battery analysis: " + OneLine(fact.Display));
            }
        }
        if (WriterIndependenceCaveat(ctx) is string caveat)
        {
            Line(sb, "- " + caveat);
        }
        Line(sb);

        OptionalSlot(sb, ctx, BenchmarkReportSlots.Limitations);
    }

    private static void Reproducibility(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var (pricing, includesCardDate) = PricingBasisText(sheet);

        Heading(sb, "## Reproducibility appendix");
        LabeledLine(sb, ctx, "run.ids");
        LabeledLine(sb, ctx, "run.dates");
        Line(sb, "- **Model under test:** " + sheet.SubjectProvider + " " + sheet.SubjectModelId + ", thinking level "
            + (sheet.SubjectThinkingLevel ?? "not set"));
        Line(sb, "- **Grader models:** " + (sheet.Graders.Count == 0
            ? "not recorded"
            : string.Join("; ", sheet.Graders.Select(g => g.Role + ": " + (GraderWithheld(ctx, g) ? WithheldGrader : g.Provider + " " + g.ModelId)
                + (string.IsNullOrWhiteSpace(g.ThinkingLevel) ? string.Empty : ", thinking level " + g.ThinkingLevel)))));
        LabeledLine(sb, ctx, "run.harnessVersion");
        LabeledLine(sb, ctx, "scoring.methodVersion");
        if (ctx.Battery)
        {
            Line(sb, "- **Battery definition SHA-256 prefix:** `" + D(ctx, "battery.definitionSha256") + "`");
            Line(sb, "- **Battery comparability class SHA-256 prefix:** `" + D(ctx, "battery.classSha256") + "`");
        }
        Line(sb, "- **" + Label("comparison.signature") + ":** `" + D(ctx, "comparison.signature") + "`");
        Line(sb, "- **" + Label("run.promptSha256") + ":** `" + D(ctx, "run.promptSha256") + "`");
        Line(sb, "- **" + Label("run.toolGuidesSha256") + ":** `" + D(ctx, "run.toolGuidesSha256") + "`");
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + pricing);
        if (!includesCardDate)
        {
            LabeledLine(sb, ctx, "cost.pricingAsOf");
        }
        Line(sb);
    }

    private static void RemovedContent(StringBuilder sb, Context ctx)
    {
        if (ctx.Document.Status != BenchmarkReportDocumentStatus.CompletedWithWarnings) return;

        var dropped = ctx.Notes.Where(n => n.Dropped).ToList();

        Heading(sb, "## Removed content");
        if (dropped.Count == 0)
        {
            Line(sb, "Automatic validation raised warnings; no item was removed.");
            Line(sb);
            return;
        }

        Line(sb, "Automatic validation removed these items from the writer's output before it was stored:");
        Line(sb);
        foreach (var note in dropped)
        {
            Line(sb, "- `" + note.Location + "` (rule " + Inv(note.Rule) + ")"
                + (ctx.Options.Disclosure == BenchmarkReportDisclosure.Full && !string.IsNullOrWhiteSpace(note.Message)
                    ? ": " + OneLine(note.Message)
                    : string.Empty));
        }
        Line(sb);
    }

    /// <summary>
    /// The terms the subject was evaluated under: its runs' purpose statements, the run report's
    /// distillation prohibition, and whose content the document rests on. The subject is always named;
    /// a grader's provider that is also a peer's is withheld when peers are anonymized.
    /// </summary>
    private static void EvaluationTerms(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var purposes = (sheet.PurposeStatements ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(OneLine)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Heading(sb, "## Evaluation terms");
        if (purposes.Count == 0)
        {
            Line(sb, "- **Purpose statement:** not recorded for this document.");
        }
        else if (purposes.Count == 1)
        {
            Line(sb, "- **Purpose statement:** " + purposes[0]);
        }
        else
        {
            Line(sb, "- **Purpose statements:**");
            foreach (var purpose in purposes)
            {
                Line(sb, "  - " + purpose);
            }
        }

        Line(sb, "- **Distillation / training prohibition:** " + DistillationProhibition);

        var graderProviders = sheet.Graders
            .Select(g => g.Provider?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => GraderProviderText(ctx, p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        string graded = graderProviders.Count == 0
            ? "graded by AI models"
            : "graded by models from " + BenchmarkReportFormat.LetterList(graderProviders);

        Line(sb, "- **Third-party model content:** Outputs generated by **" + sheet.SubjectLabel + "** (" + sheet.SubjectProvider + "), "
            + graded + ", and described in this document by **" + ctx.Document.WriterDisplayName + "** (" + ctx.Document.WriterProvider
            + ") are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.");
        Line(sb);
    }

    /// <summary>A grader's provider, or <see cref="WithheldProvider"/> when anonymized peers share it and the subject does not.</summary>
    private static string GraderProviderText(Context ctx, string provider)
        => ProviderWithheld(ctx, provider) ? WithheldProvider : provider;

    private static bool ProviderWithheld(Context ctx, string provider)
    {
        bool peerProvider = ctx.Sheet.Peers.Any(p => string.Equals(p.Provider?.Trim(), provider, StringComparison.OrdinalIgnoreCase));
        bool subjectProvider = string.Equals(ctx.Sheet.SubjectProvider?.Trim(), provider, StringComparison.OrdinalIgnoreCase);
        return ctx.Anonymized && peerProvider && !subjectProvider;
    }

    /// <summary>
    /// A grader whose provider <see cref="GraderProviderText"/> withholds: its name and model id are
    /// withheld with it, since either names the provider.
    /// </summary>
    private static bool GraderWithheld(Context ctx, BenchmarkReportGrader? grader)
    {
        string provider = grader?.Provider?.Trim() ?? string.Empty;
        return provider.Length > 0 && ProviderWithheld(ctx, provider);
    }

    private static void Footer(StringBuilder sb, Context ctx)
    {
        var document = ctx.Document;
        int current = ctx.Comparison ? ComparisonReportFormatVersion : ReportFormatVersion;
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
            + (ctx.Comparison ? " · models " : " · peers ") + (ctx.Anonymized ? "anonymized" : "named") + "*");
        Line(sb);
        // Documents written from format version 7 were also checked for interval-overlap wording and hype words.
        string claims = document.ReportFormatVersion >= 7
            ? "significance claims, interval-overlap wording, hype words and spelling"
            : "significance claims and spelling";
        Line(sb, "*Figures and tables were computed by Overseer. The prose was written by " + document.WriterDisplayName
            + " from those figures and checked automatically for structure, permitted figures and references, word limits, "
            + "disclosure of benchmark text, peer names, " + claims + "; the checks do not verify the prose's "
            + "interpretations.*");
    }

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The items, each with its support label for the questions it cites and, where the document
    /// prints them, its evidence line. The Executive Summary prints no label for an item both graders
    /// agreed on.
    /// </summary>
    private static void Items(
        StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportWriterItem> items, string emptyText, bool strengths = false)
    {
        if (items.Count == 0)
        {
            Line(sb, emptyText);
        }
        foreach (var item in items)
        {
            bool unlabeled = ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary
                && BenchmarkReportFacts.NormalizeSupportLabel(BenchmarkReportFacts.SupportLabelFor(item, ctx.Sheet))
                    == BenchmarkReportFacts.SupportBothGraders;
            Line(sb, "- " + Prose(ctx, item.Text) + (unlabeled ? string.Empty : " *(" + SupportText(ctx, item) + ")*"));
            if (ctx.PrintsEvidence)
            {
                EvidenceLine(sb, ctx, item, listsRefutations: !strengths);
            }
        }
        Line(sb);
    }

    /// <summary>An item's support label as the document prints it: plain words in the Executive Summary.</summary>
    private static string SupportText(Context ctx, BenchmarkReportWriterItem item)
    {
        string label = BenchmarkReportFacts.SupportLabelFor(item, ctx.Sheet);
        return ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary ? PlainSupportLabel(label) : SupportLabelText(label);
    }

    /// <summary>A support label as printed: <see cref="BenchmarkReportFacts.SupportComputed"/> reads <see cref="BenchmarkReportFacts.SupportComputedDisplay"/>.</summary>
    internal static string SupportLabelText(string supportLabel)
        => string.Equals(supportLabel, BenchmarkReportFacts.SupportComputed, StringComparison.Ordinal)
            ? BenchmarkReportFacts.SupportComputedDisplay
            : supportLabel;

    /// <summary>A support label in the Executive Summary's plain words.</summary>
    internal static string PlainSupportLabel(string supportLabel) => BenchmarkReportFacts.NormalizeSupportLabel(supportLabel) switch
    {
        BenchmarkReportFacts.SupportBothGraders => "both graders agreed",
        BenchmarkReportFacts.SupportOneGraderDifferentProvider => "raised by one grader",
        BenchmarkReportFacts.SupportOneGraderSameProvider => "raised by one grader, from the model's own company",
        BenchmarkReportFacts.SupportGradersDisagree => "the graders disagree",
        BenchmarkReportFacts.SupportSingleAssessor => "raised by the grader",
        BenchmarkReportFacts.SupportComputed => "from per-question results",
        _ => supportLabel
    };

    /// <summary>
    /// One line under an item: <c>Both graders — accuracy · Q6 (59 / 100), Q10 (57 / 100) · the claim
    /// verifier refuted an answer sentence on Q6</c>. The rows it cites, each with its support label for
    /// the item's questions (<see cref="BenchmarkReportFacts.RowSupportLabelFor"/>), the facts it cites by label,
    /// its questions with their scores, and, with <paramref name="listsRefutations"/>, the questions on
    /// which the verifier refuted the answer's own text; each part only where it applies. A strength's
    /// line lists no refutation. At most <see cref="EvidenceQuestionLimit"/> questions are listed, the
    /// highest scores first for a strength and the lowest first otherwise, then <c>and N more</c>.
    /// </summary>
    private static void EvidenceLine(StringBuilder sb, Context ctx, BenchmarkReportWriterItem item, bool listsRefutations)
    {
        var ids = (item.Evidence ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var parts = new List<string>();
        int runCount = ctx.Sheet.SubjectRunIds.Count;

        var rows = ids
            .Select(id => ctx.Sheet.Rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal)))
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();
        if (rows.Count > 0)
        {
            var cited = BenchmarkReportFacts.CitedQuestionsOf(item.Questions, ids);
            parts.Add(string.Join("; ", rows.Select(row => BenchmarkReportFacts.RowSupportLabelFor(row, cited, ctx.Sheet)
                + " — " + row.Category.Replace('_', ' ')
                + (runCount > 1 ? ", in " + Inv(row.Recurrence) + " of " + Inv(runCount) + " runs" : string.Empty))));
        }

        foreach (string id in ids)
        {
            if (QuestionNumber(ctx, id) != null || rows.Any(r => string.Equals(r.Id, id, StringComparison.Ordinal))) continue;

            var fact = Fact(ctx, id);
            parts.Add(FactLabel(ctx, id) + (fact == null ? string.Empty : ": " + (fact.Available ? Shown(ctx, fact) : BenchmarkReportFacts.NotAvailable)));
        }

        var numbers = CitedQuestions(ctx, item, ids);
        if (numbers.Count > 0)
        {
            var listed = ByScore(ctx, numbers, highestFirst: !listsRefutations).Take(EvidenceQuestionLimit).ToList();
            int more = numbers.Count - listed.Count;
            parts.Add(string.Join(", ", listed.Select(n => ctx.QuestionLabel(n) + " (" + ScoreText(ctx, n) + ")"))
                + (more > 0 ? " and " + Inv(more) + " more" : string.Empty));
        }

        foreach (var group in numbers
            .Where(_ => listsRefutations)
            .Select(n => (Number: n, Kind: RefutedKind(ctx, n)))
            .Where(x => x.Kind != null)
            .GroupBy(x => x.Kind!)
            .OrderBy(g => g.Key == AnswerSentence ? 0 : 1))
        {
            parts.Add("the claim verifier refuted " + group.Key + " on "
                + BenchmarkReportFormat.LetterList(group.Select(x => ctx.QuestionLabel(x.Number)).ToList()));
        }

        if (parts.Count > 0)
        {
            Line(sb, "  - *Evidence:* " + string.Join(" · ", parts));
        }
    }

    private const string AnswerSentence = "an answer sentence";

    /// <summary>
    /// What the verifier refuted on a question: <c>an answer sentence</c> from the role-labeled rulings,
    /// else from the sheet's count; <c>a claim</c> for a document stored without roles; null for nothing.
    /// </summary>
    private static string? RefutedKind(Context ctx, int number)
    {
        var rulings = ContentFor(ctx, number).SelectMany(b => b.Question.ClaimRulings).ToList();
        if (rulings.Any(r => r.Role != null))
        {
            return rulings.Any(r => BenchmarkReportContent.IsAnswerSentenceRole(r.Role)
                                    && string.Equals(r.Verdict, "refuted", StringComparison.Ordinal))
                ? AnswerSentence
                : null;
        }

        var q = ctx.Sheet.Questions.FirstOrDefault(x => x.Number == number);
        if (q == null) return null;
        if (q.RefutedAnswerSentences is int sentences) return sentences > 0 ? AnswerSentence : null;
        return q.RefutedClaims > 0 ? "a claim" : null;
    }

    /// <summary>Question numbers by score, highest or lowest first, unscored last, ties by number.</summary>
    private static IEnumerable<int> ByScore(Context ctx, IReadOnlyList<int> numbers, bool highestFirst)
    {
        double? ScoreOf(int n) => ctx.Sheet.Questions.FirstOrDefault(q => q.Number == n)?.Score;

        return numbers
            .OrderBy(n => ScoreOf(n).HasValue ? 0 : 1)
            .ThenBy(n => ScoreOf(n) is double score ? (highestFirst ? -score : score) : 0)
            .ThenBy(n => n);
    }

    private static string ScoreText(Context ctx, int number)
    {
        var q = ctx.Sheet.Questions.FirstOrDefault(x => x.Number == number);
        return q?.Score is double score ? BenchmarkReportFormat.Whole(score) + " / 100" : "not scored";
    }

    /// <summary>
    /// The questions an item is about: its own question list and the questions it cites as evidence,
    /// in order; the questions of the rows it cites only when it names none itself.
    /// </summary>
    private static List<int> CitedQuestions(Context ctx, BenchmarkReportWriterItem item, IReadOnlyList<string> ids)
    {
        var numbers = new List<int>(item.Questions ?? new List<int>());
        numbers.AddRange(ids.Select(id => QuestionNumber(ctx, id)).Where(n => n.HasValue).Select(n => n!.Value));

        if (numbers.Count == 0)
        {
            foreach (string id in ids)
            {
                var row = ctx.Sheet.Rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
                if (row != null) numbers.AddRange(row.Questions);
            }
        }

        return numbers.Distinct().OrderBy(n => n).ToList();
    }

    /// <summary>
    /// The number of a <c>Q&lt;n&gt;</c> evidence id, or on a battery sheet of an <c>S&lt;suite&gt;-Q&lt;n&gt;</c>
    /// one; null for any other id.
    /// </summary>
    private static int? QuestionNumber(Context ctx, string id)
    {
        if (ctx.Battery) return ctx.QuestionNumberOf(id);

        return id.Length > 1 && id[0] == 'Q' && int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;
    }

    private static void Slot(StringBuilder sb, Context ctx, string slot)
    {
        Line(sb, ctx.Writer.Sections.TryGetValue(slot, out var text) && !string.IsNullOrWhiteSpace(text)
            ? Prose(ctx, text)
            : "*Not written.*");
        Line(sb);
    }

    /// <summary>A slot a document stored before format version 7 lacks: its prose where written, nothing otherwise.</summary>
    private static void OptionalSlot(StringBuilder sb, Context ctx, string slot)
    {
        if (!ctx.Writer.Sections.TryGetValue(slot, out var text) || string.IsNullOrWhiteSpace(text)) return;

        Line(sb, Prose(ctx, text));
        Line(sb);
    }

    private static void Heading(StringBuilder sb, string heading)
    {
        Line(sb, heading);
        Line(sb);
    }

    /// <summary><c>- **Label:** display</c> for one fact.</summary>
    private static void LabeledLine(StringBuilder sb, Context ctx, string key)
        => Line(sb, "- **" + Label(key) + ":** " + D(ctx, key));

    /// <summary>The text as a blockquote, one <c>&gt; </c> per line; an empty line becomes <c>&gt;</c>.</summary>
    private static void Quote(StringBuilder sb, string? text)
    {
        foreach (var line in Normalize(text).Split('\n'))
        {
            Line(sb, line.Length == 0 ? ">" : "> " + line);
        }
    }

    private static void TableHeader(StringBuilder sb, Context ctx, IReadOnlyList<string> columns)
    {
        var header = new List<string> { "Model" };
        if (!ctx.Anonymized) header.Add("Provider");
        header.AddRange(columns);
        Line(sb, "| " + string.Join(" | ", header) + " |");
        Line(sb, "|" + string.Concat(header.Select(_ => "---|")));
    }

    private static void TableRow(StringBuilder sb, Context ctx, BenchmarkReportPeer? peer, IReadOnlyList<string> cells)
    {
        var row = new List<string> { TableName(ctx, peer) };
        if (!ctx.Anonymized) row.Add(Cell(peer == null ? ctx.Sheet.SubjectProvider : peer.Provider));
        row.AddRange(cells);
        Line(sb, "| " + string.Join(" | ", row) + " |");
    }

    private static string TableName(Context ctx, BenchmarkReportPeer? peer)
        => peer == null
            ? "**" + Cell(ctx.Sheet.SubjectLabel) + "**"
            : ctx.Anonymized ? "Model " + peer.Letter : Cell(peer.Label);

    private static string PlainName(Context ctx, BenchmarkReportPeer? peer)
        => peer == null
            ? ctx.Sheet.SubjectLabel
            : ctx.Anonymized ? "Model " + peer.Letter : peer.Label;

    private static string RankCell(int? rank) => rank.HasValue ? Inv(rank.Value) : NoValue;

    /// <summary>An entry's quality rank cell: <c>joint 1</c> where its interval overlaps a neighbor's, else <see cref="RankCell"/>.</summary>
    private static string QualityRankCell(Context ctx, BenchmarkReportEntryFigures entry)
        => QualityJointRanks(ctx.Sheet).TryGetValue(entry.EntryKey, out var joint) && joint.Joint
            ? "joint " + Inv(joint.Rank)
            : RankCell(entry.QualityRank);

    /// <summary>
    /// The joint quality ranks of the sheet's entries with an Intelligence Index
    /// (<see cref="BenchmarkReportFacts.JointRanks"/>), by entry key; empty on a sheet without entries.
    /// </summary>
    private static IReadOnlyDictionary<string, BenchmarkReportFacts.JointRank> QualityJointRanks(BenchmarkReportFactSheet sheet)
    {
        var ranked = sheet.Entries
            .Where(e => e.QualityIndex.HasValue)
            .GroupBy(e => e.EntryKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(e => (Key: e.EntryKey, Score: e.QualityIndex!.Value, Lower: e.QualityLower, Upper: e.QualityUpper))
            .ToList();
        return BenchmarkReportFacts.JointRanks(ranked);
    }

    /// <summary>
    /// <c>joint 1st of 2 (intervals overlap)</c> for the entry of a <c>quality.rank</c> or
    /// <c>peer.X.quality.rank</c> fact whose rank is joint; null otherwise, and on a sheet without entries.
    /// </summary>
    private static string? JointQualityRank(Context ctx, string key)
    {
        string? entryKey = null;
        if (string.Equals(key, "quality.rank", StringComparison.Ordinal))
        {
            entryKey = ctx.Sheet.Entries.FirstOrDefault(e => e.IsSubject)?.EntryKey;
        }
        else if (key.StartsWith("peer.", StringComparison.Ordinal) && key.EndsWith(".quality.rank", StringComparison.Ordinal))
        {
            string letter = key["peer.".Length..^".quality.rank".Length];
            entryKey = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, letter, StringComparison.Ordinal))?.EntryKey;
        }
        if (entryKey == null) return null;

        var ranks = QualityJointRanks(ctx.Sheet);
        return ranks.TryGetValue(entryKey, out var rank) && rank.Joint ? BenchmarkReportFacts.JointRankText(rank, ranks.Count) : null;
    }

    /// <summary>A peer's paired difference with its interval, <c>+4.2 points (-1.3 to +9.8)</c>; <see cref="NoValue"/> for the subject.</summary>
    private static string PairedCell(Context ctx, BenchmarkReportPeer? peer)
    {
        if (peer == null) return NoValue;

        string prefix = BenchmarkReportFacts.PeerPrefix(peer.Letter);
        if (Fact(ctx, prefix + "pairedDifference") is not { Available: true } difference) return BenchmarkReportFacts.NotAvailable;

        return Cell(IsAvailable(ctx, prefix + "pairedInterval")
            ? difference.Display + " (" + D(ctx, prefix + "pairedInterval") + ")"
            : difference.Display);
    }

    private static string NotAvailableReason(BenchmarkReportFact? fact)
    {
        string reason = fact?.UnavailableReason?.Trim() ?? string.Empty;
        return reason.Length == 0 ? "not recorded." : reason;
    }

    /// <summary>The subject's figures first, then each peer's in letter order.</summary>
    private static List<(BenchmarkReportEntryFigures Entry, BenchmarkReportPeer? Peer)> OrderedEntries(BenchmarkReportFactSheet sheet)
    {
        var rows = new List<(BenchmarkReportEntryFigures, BenchmarkReportPeer?)>();
        var subject = sheet.Entries.FirstOrDefault(e => e.IsSubject);
        if (subject != null) rows.Add((subject, null));

        foreach (var peer in OrderedPeers(sheet))
        {
            var entry = sheet.Entries.FirstOrDefault(e => string.Equals(e.EntryKey, peer.EntryKey, StringComparison.Ordinal));
            if (entry != null) rows.Add((entry, peer));
        }

        return rows;
    }

    private static List<BenchmarkReportPeer> OrderedPeers(BenchmarkReportFactSheet sheet)
        => sheet.Peers.OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal).ToList();

    private static List<BenchmarkReportFindingRow> OrderedRows(BenchmarkReportFactSheet sheet)
        => sheet.Rows.OrderBy(r => RowNumber(r.Id)).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

    private static int RowNumber(string id)
        => id.Length > 1 && int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;

    /// <summary>Every content block for a question number, in stored run order.</summary>
    private static List<(long RunId, BenchmarkReportContentQuestion Question)> ContentFor(Context ctx, int number)
        => ctx.Content.Runs
            .SelectMany(r => r.Questions.Where(q => q.Number == number).Select(q => (RunId: r.RunId, Question: q)))
            .ToList();

    private static string? Topic(Context ctx, int number)
    {
        var topic = ctx.Writer.QuestionTopics.FirstOrDefault(t => t.Question == number);
        return topic == null || string.IsNullOrWhiteSpace(topic.Topic) ? null : OneLine(Prose(ctx, topic.Topic));
    }

    private static BenchmarkReportFact? Fact(Context ctx, string key) => SheetFact(ctx.Sheet, key);

    private static BenchmarkReportFact? SheetFact(BenchmarkReportFactSheet sheet, string key)
        => sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    private static bool IsAvailable(Context ctx, string key) => Fact(ctx, key) is { Available: true };

    /// <summary>The fact's display as printed, or "not available" when the sheet has no such fact.</summary>
    private static string D(Context ctx, string key) => Fact(ctx, key) is { } fact ? Shown(ctx, fact) : BenchmarkReportFacts.NotAvailable;

    /// <summary>
    /// A fact's display as this document prints it: <see cref="DisplayOf"/>; a joint quality rank as
    /// <see cref="JointQualityRank"/> words it; and in a named copy the peers of
    /// <c>quality.intervalOverlap</c> and <c>panel.judgeDependentPairs</c> by name instead of by letter.
    /// </summary>
    private static string Shown(Context ctx, BenchmarkReportFact fact)
    {
        string display = DisplayOf(fact);
        if (fact.Available && JointQualityRank(ctx, fact.Key) is string joint) return joint;
        if (ctx.Anonymized || !fact.Available) return display;

        // A comparison-scope or chat consistency display names its models by letter; a named copy names them by label.
        if (ctx.Comparison || ctx.ChatConsistency) return NamedLetters(ctx, display);

        return fact.Key switch
        {
            "quality.intervalOverlap" => NamedOverlap(ctx) ?? NamedLetters(ctx, display),
            "panel.judgeDependentPairs" => NamedLetters(ctx, display),
            _ => display
        };
    }

    /// <summary>
    /// The subject's interval overlap with the peers named, from each peer's <c>peer.X.intervalOverlap</c>:
    /// <c>every peer's</c> when two or more peers all overlap; null when a peer lacks the fact.
    /// </summary>
    private static string? NamedOverlap(Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        var flags = peers.Select(p => (Peer: p, Fact: Fact(ctx, BenchmarkReportFacts.PeerPrefix(p.Letter) + "intervalOverlap"))).ToList();
        if (peers.Count == 0 || flags.Any(f => f.Fact is not { Available: true })) return null;

        var overlapping = flags.Where(f => BenchmarkReportPackPrompt.IsTrue(f.Fact!)).Select(f => f.Peer).ToList();
        if (overlapping.Count == 0) return BenchmarkReportFacts.OverlapSentence(Array.Empty<string>());
        if (overlapping.Count == peers.Count && peers.Count > 1) return "its 95 % interval overlaps every peer's";

        return overlapping.Count == 1
            ? "its 95 % interval overlaps that of " + overlapping[0].Label
            : "its 95 % interval overlaps those of " + BenchmarkReportFormat.LetterList(overlapping.Select(p => p.Label).ToList());
    }

    /// <summary>
    /// <paramref name="display"/> with each <c>Model A</c> or <c>Models A, B and C</c> naming the peers
    /// by label; unchanged where a letter belongs to no peer.
    /// </summary>
    private static string NamedLetters(Context ctx, string display)
        => Regex.Replace(display, @"\bModels? (?<list>[A-Z]{1,3}(?:, [A-Z]{1,3})*(?: and [A-Z]{1,3})?)(?![A-Za-z0-9])", match =>
        {
            var letters = Regex.Split(match.Groups["list"].Value, ", | and ", RegexOptions.CultureInvariant);
            var labels = new List<string>();
            foreach (string letter in letters)
            {
                var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, letter, StringComparison.Ordinal));
                if (peer == null) return match.Value;
                labels.Add(peer.Label);
            }
            return BenchmarkReportFormat.LetterList(labels);
        }, RegexOptions.CultureInvariant);

    /// <summary>
    /// A fact's display as printed. A response-style display stored before format version 6 loses its
    /// leading <c>yes: </c>, so it reads as a clause in prose.
    /// </summary>
    private static string DisplayOf(BenchmarkReportFact fact)
    {
        const string LegacyYes = "yes: ";
        string display = fact.Display ?? string.Empty;
        if (fact.Key.Contains(BenchmarkReportPackPrompt.ResponseStyleConflictKeyFragment, StringComparison.OrdinalIgnoreCase)
            && display.StartsWith(LegacyYes, StringComparison.OrdinalIgnoreCase)
            && display.Length > LegacyYes.Length)
        {
            string rest = display[LegacyYes.Length..];
            return char.ToUpperInvariant(rest[0]) + rest[1..];
        }
        return display;
    }

    /// <summary>A numeric table cell: the fact's display, or <see cref="NoValue"/> when it is unavailable or missing.</summary>
    private static string Num(Context ctx, string key) => Fact(ctx, key) is { Available: true } fact ? Cell(fact.Display) : NoValue;

    private static string Label(string key) => BenchmarkReportFactLabels.Label(key);

    /// <summary>
    /// A fact's label with the peer naming applied: a peer's own fact reads <c>Model A's …</c> when
    /// peers are anonymized and <c>Grok 5's …</c> when they are named.
    /// </summary>
    private static string FactLabel(Context ctx, string key)
    {
        string label = Label(key);
        if (ctx.Anonymized || !key.StartsWith("peer.", StringComparison.Ordinal)) return label;

        string[] parts = key.Split('.');
        var peer = parts.Length >= 3 ? ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, parts[1], StringComparison.Ordinal)) : null;
        if (peer == null) return label;

        return Regex.Replace(label, @"\bModel " + Regex.Escape(peer.Letter) + @"(?![A-Za-z])",
            _ => peer.Label, RegexOptions.CultureInvariant);
    }

    private static string NotAvailableText(BenchmarkReportFact? fact)
    {
        string reason = fact?.UnavailableReason?.Trim() ?? string.Empty;
        return reason.Length == 0 ? "not available." : "not available. " + reason;
    }

    /// <summary>
    /// Writer prose with its tokens resolved: <c>{{subject}}</c>, <c>{{peer:X}}</c> and <c>{{fact.key}}</c>;
    /// on a comparison-scope sheet <c>{{model:X}}</c> as a peer's.
    /// </summary>
    private static string Prose(Context ctx, string? text)
        => Regex.Replace(Normalize(text), TokenPattern, match => Resolve(ctx, match), RegexOptions.CultureInvariant);

    private static string Resolve(Context ctx, Match match)
    {
        string token = match.Groups[1].Value.Trim();

        if (string.Equals(token, "subject", StringComparison.Ordinal)) return ctx.Sheet.SubjectLabel;

        if (token.StartsWith("peer:", StringComparison.Ordinal) || (ctx.Comparison && token.StartsWith("model:", StringComparison.Ordinal)))
        {
            string letter = token[(token.IndexOf(':') + 1)..].Trim();
            var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, letter, StringComparison.Ordinal));
            if (peer == null) return match.Value;
            return ProseName(ctx, peer);
        }

        return Fact(ctx, token) is { } fact ? Shown(ctx, fact) : match.Value;
    }

    /// <summary>A peer as prose names it: its label when named, <c>Model A</c> when anonymized.</summary>
    private static string ProseName(Context ctx, BenchmarkReportPeer peer)
        => ctx.Anonymized ? "Model " + peer.Letter : peer.Label;

    /// <summary>
    /// One <c>[[figure:&lt;key&gt;]]</c> line, between blank lines, for each chart of
    /// <see cref="BenchmarkReportRenderOptions.Charts"/> placed at <paramref name="anchor"/> in this
    /// audience, in placement order; nothing without charts or in a stand-alone document. A chat
    /// consistency document plots the model under test over time, so it carries its charts with or
    /// without control models.
    /// </summary>
    private static void Figures(StringBuilder sb, Context ctx, BenchmarkReportChartAnchor anchor)
    {
        var charts = ctx.Options.Charts;
        if ((ctx.Standalone && !ctx.ChatConsistency) || charts == null || charts.Count == 0) return;

        var scope = ctx.ChatConsistency ? BenchmarkReportScope.ChatConsistency
            : ctx.Comparison ? BenchmarkReportScope.Comparison
            : BenchmarkReportScope.Model;
        foreach (string key in BenchmarkReportChartPlacement.KeysAt(ctx.Document.Audience, anchor, scope))
        {
            if (!charts.Any(c => c != null && string.Equals(c.FigureKey, key, StringComparison.Ordinal))) continue;

            if (sb.Length > 0 && !(sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')) Line(sb);
            Line(sb, BenchmarkReportChartPlacement.Marker(key));
            Line(sb);
        }
    }

    /// <summary>
    /// A copy of the sheet with each peer's name, model id and provider replaced by its letter, and
    /// the peers' names removed from the known names unless the subject, suite or a grader shares them.
    /// </summary>
    private static BenchmarkReportFactSheet AnonymizedSheet(Context ctx)
    {
        var copy = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(ctx.Document.FactsJson);
        BenchmarkReportFacts.NormalizeSupportLabels(copy);

        var kept = new List<string?> { copy.SubjectLabel, copy.SubjectDisplayName, copy.SubjectModelId, copy.SubjectProvider, copy.SuiteName };
        foreach (var grader in copy.Graders)
        {
            kept.Add(grader.Label);
            kept.Add(grader.ModelId);
            kept.Add(grader.Provider);
        }

        var withheld = new List<string>();
        foreach (var peer in copy.Peers)
        {
            foreach (var name in new[] { peer.Label, peer.DisplayName, peer.ModelId, peer.Provider })
            {
                if (!string.IsNullOrWhiteSpace(name) && !kept.Contains(name, StringComparer.Ordinal)) withheld.Add(name);
            }

            string alias = "Model " + peer.Letter;
            peer.Label = alias;
            peer.DisplayName = alias;
            peer.ModelId = string.Empty;
            peer.Provider = string.Empty;
        }

        copy.KnownNames = copy.KnownNames.Where(n => !withheld.Contains(n, StringComparer.Ordinal)).ToList();
        return copy;
    }

    /// <summary>The stamp of a document that may print questions and rubrics: every audience but the Executive Summary, and the run report.</summary>
    internal static string Stamp(BenchmarkReportDisclosure disclosure) => disclosure switch
    {
        BenchmarkReportDisclosure.Summary => SummaryStamp,
        BenchmarkReportDisclosure.Detailed => DetailedStamp,
        _ => FullStamp
    };

    /// <summary>The stamp of a document: the Executive Summary quotes no question or rubric, so its Detailed and Full stamps say so.</summary>
    internal static string Stamp(BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure)
        => audience != BenchmarkReportAudience.ExecutiveSummary
            ? Stamp(disclosure)
            : disclosure switch
            {
                BenchmarkReportDisclosure.Summary => SummaryStamp,
                BenchmarkReportDisclosure.Detailed => ExecutiveDetailedStamp,
                _ => ExecutiveFullStamp
            };

    private static string AudienceName(BenchmarkReportAudience audience) => BenchmarkReportRenderService.AudienceName(audience);

    private static string LowerFirst(string text)
        => text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>A table cell: pipes escaped, line breaks folded to spaces.</summary>
    private static string Cell(string? text) => OneLine(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static string OneLine(string? text) => Normalize(text).Replace('\n', ' ');

    /// <summary><c>\n</c> line breaks and no trailing whitespace.</summary>
    private static string Normalize(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb) => sb.Append('\n');

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');

    /// <summary>Everything one render reads, deserialized once.</summary>
    private sealed class Context
    {
        public required BenchmarkReportDocument Document { get; init; }
        public required BenchmarkReportRenderOptions Options { get; init; }
        public required BenchmarkReportFactSheet Sheet { get; init; }
        public required BenchmarkReportContentSnapshot Content { get; init; }
        public required BenchmarkReportWriterOutput Writer { get; init; }
        public required List<BenchmarkReportValidationNote> Notes { get; init; }

        /// <summary>Peers print by letter: as the options ask, and always in a chat consistency Provider Issue Report.</summary>
        public bool Anonymized => Options.PeerNaming == BenchmarkReportPeerNaming.Anonymized
                                  || (ChatConsistency && Document.Audience == BenchmarkReportAudience.ProviderIssueReport);

        /// <summary>The sheet is a comparison-scope sheet: every covered model lettered, no subject.</summary>
        public bool Comparison => Sheet.IsComparison;

        /// <summary>The sheet is a chat consistency sheet: one model's chat over two periods, its control models as peers.</summary>
        public bool ChatConsistency => Sheet.IsChatConsistency;

        /// <summary>The sheet has no peers: a stand-alone report.</summary>
        public bool Standalone => Sheet.Peers.Count == 0;

        /// <summary>The sheet has peers and their own facts, as sheets from format version 7 do.</summary>
        public bool ComparesPeers => !Standalone && Sheet.Facts.Any(f => f.Key.StartsWith("peer.", StringComparison.Ordinal));

        /// <summary>Evidence lines belong to the Report for AI Researchers and Developers alone, at Detailed and Full.</summary>
        public bool PrintsEvidence => Document.Audience == BenchmarkReportAudience.TechnicalReport
                                      && Options.Disclosure >= BenchmarkReportDisclosure.Detailed;

        /// <summary>The evaluation terms belong to every audience but the Internal Improvement Brief.</summary>
        public bool PrintsEvaluationTerms => Document.Audience != BenchmarkReportAudience.InternalBrief;

        /// <summary>The sheet is a battery result's.</summary>
        public bool Battery => Sheet.Battery != null;

        private Dictionary<int, string>? _labels;
        private Dictionary<string, int>? _numbers;

        /// <summary>How the document names a question: its battery reference <c>S2-Q7</c>, else <c>Q7</c>.</summary>
        public string QuestionLabel(int number)
        {
            EnsureReferences();
            return _labels!.TryGetValue(number, out string? label) ? label : "Q" + Inv(number);
        }

        /// <summary>The number of a battery question's reference; null for any other text.</summary>
        public int? QuestionNumberOf(string reference)
        {
            EnsureReferences();
            return _numbers!.TryGetValue(reference, out int number) ? number : null;
        }

        private void EnsureReferences()
        {
            if (_labels != null) return;

            _labels = new Dictionary<int, string>();
            _numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var q in Sheet.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Reference)))
            {
                _labels.TryAdd(q.Number, q.Reference!);
                _numbers.TryAdd(q.Reference!, q.Number);
            }
        }
    }
}
