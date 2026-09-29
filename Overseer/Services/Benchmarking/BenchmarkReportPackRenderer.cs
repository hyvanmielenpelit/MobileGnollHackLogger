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
/// line beneath each strength, weakness and recommendation at Detailed and Full; the Executive Summary
/// and the Report for AI Researchers and Developers end with the evaluation terms. Fact keys are
/// printed only through <see cref="BenchmarkReportFactLabels"/>.</para>
///
/// <para>A document stored under an earlier format version renders with what it stored: a missing
/// role, count or fact falls back to the wording that version printed, and its single-grader support
/// labels are read in their provider wording.</para>
/// </summary>
public static class BenchmarkReportPackRenderer
{
    public const int ReportFormatVersion = 5;

    private const string ProductName = "Overseer GnollHack Assistant Benchmark";
    private const string TokenPattern = @"\{\{([^{}]+)\}\}";
    private const string NoValue = "—";

    /// <summary>The run report's own prohibition sentence (BenchmarkReportBuilder, Compliance &amp; Evaluation Terms).</summary>
    private const string DistillationProhibition = "No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.";

    /// <summary>What a grader's provider that is also a peer's reads as when peers are anonymized.</summary>
    private const string WithheldProvider = "a withheld provider";

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

    public static string BuildTitle(BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
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

        RemovedContent(sb, ctx);
        if (ctx.PrintsEvaluationTerms)
        {
            EvaluationTerms(sb, ctx);
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

        Heading(sb, "## What it did well");
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.");

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
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.");

        Heading(sb, "## Recommendations for model developers");
        Recommendations(sb, ctx, BenchmarkReportSlots.TargetModelDevelopers);

        PerQuestion(sb, ctx, "## Per-question results");
        ToolUse(sb, ctx);
        GraderReliability(sb, ctx);
        ThreatsToValidity(sb, ctx);
        Reproducibility(sb, ctx);
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
        Heading(sb, "### Strengths");
        Items(sb, ctx, ctx.Writer.Strengths, "No strengths were recorded.");
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
                Line(sb, "- **[" + lead.Triage + "]** " + Prose(ctx, lead.Text) + " *(" + Support(ctx, lead.Evidence) + ")*");
            }
        }
        Line(sb);

        PerQuestion(sb, ctx, "## 5. Per-question results");

        Heading(sb, "## 6. Fact sheet");
        Line(sb, "```json");
        Line(sb, BenchmarkReportJson.SerializeSorted(ctx.Anonymized ? AnonymizedSheet(ctx) : ctx.Sheet));
        Line(sb, "```");
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>The title, then, with <see cref="BenchmarkReportRenderOptions.IncludeFrontMatter"/>, the stamp and the facts list.</summary>
    private static void TitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Line(sb, "# " + BuildTitle(ctx.Document.Audience, sheet));
        Line(sb);
        if (!ctx.Options.IncludeFrontMatter) return;

        Line(sb, "*" + Stamp(ctx.Document.Audience, ctx.Options.Disclosure) + "*");
        Line(sb);
        Line(sb, "- **Date:** " + ctx.Document.CreatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Line(sb, "- **Suite:** " + sheet.SuiteName);
        Line(sb, "- **Questions:** " + D(ctx, "suite.questions"));
        Line(sb, "- **Runs:** " + Inv(sheet.SubjectRunIds.Count) + " (" + (sheet.SubjectRunIds.Count == 1 ? "run " : "runs ")
            + string.Join(", ", sheet.SubjectRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture))) + ")");
        Line(sb, "- **Peers:** " + PeersText(ctx));
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
    /// The interval, its span and the scored questions in one code-rendered sentence; nothing on a
    /// document stored without the span fact.
    /// </summary>
    private static void IntervalSpanLine(StringBuilder sb, Context ctx)
    {
        if (!IsAvailable(ctx, "quality.interval") || !IsAvailable(ctx, "quality.intervalSpan") || !IsAvailable(ctx, "quality.scoredItems")) return;

        Line(sb, "The 95 % interval is " + D(ctx, "quality.interval") + ", a span of " + D(ctx, "quality.intervalSpan")
            + ", and rests on " + D(ctx, "quality.scoredItems") + " questions with a scored answer.");
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

    private static void NoSignificance(StringBuilder sb, Context ctx)
    {
        string text = JoinSentences(ctx.Sheet.NoSignificanceSummary, ctx.Sheet.NoSignificanceInstead);
        if (text.Length == 0) return;
        Line(sb, text);
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
    private static (string Text, bool IncludesPriceCardDate) PricingBasisText(Context ctx)
    {
        string card = IsAvailable(ctx, "cost.pricingAsOf") ? " (price card dated " + D(ctx, "cost.pricingAsOf") + ")" : string.Empty;
        switch (PricingBasisKind(ctx))
        {
            case BenchmarkReportFacts.PricingBasisCatalog:
                string date = IsAvailable(ctx, "comparison.pricedOn") ? " on " + D(ctx, "comparison.pricedOn") : string.Empty;
                return ("Catalog prices" + date + card, true);
            case BenchmarkReportFacts.PricingBasisSnapshot:
                return ("Prices stored with each run" + card, true);
            default:
                return (D(ctx, "comparison.pricingBasis"), false);
        }
    }

    private static string? PricingBasisKind(Context ctx)
        => Fact(ctx, "comparison.pricingBasisKind") is { Available: true } kind ? kind.Display : null;

    private static void SetupAndMethod(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        int runs = sheet.SubjectRunIds.Count;

        Heading(sb, "## Setup and method");
        Line(sb, "- **Suite:** " + sheet.SuiteName + ", " + D(ctx, "suite.questions") + " questions.");
        Line(sb, "- **" + Label("config.chat") + ":** " + D(ctx, "config.chat"));
        Line(sb, "- **Model under test:** " + sheet.SubjectLabel + " (" + sheet.SubjectProvider + ", " + sheet.SubjectModelId
            + "), thinking level " + (sheet.SubjectThinkingLevel ?? "not set") + "; " + Inv(runs) + (runs == 1 ? " run." : " runs."));
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
                Line(sb, "  - " + grader.Role + ": " + grader.Label + " (" + grader.Provider + ", " + grader.ModelId + "), "
                    + (grader.SameFamilyAsSubject ? "same provider as the model under test" : "different provider from the model under test"));
            }
        }

        Line(sb, "- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a "
            + "critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the "
            + "difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with "
            + "tool time excluded. Cost per question is the model under test's spend divided by the questions asked.");
        Line(sb, ctx.Standalone
            ? "- **Comparability:** this report describes the model on its own, measured under the instrument condition with signature `"
                + D(ctx, "comparison.signature") + "`."
            : "- **Comparability:** every model in this report was measured under one instrument condition, signature `"
                + D(ctx, "comparison.signature") + "`.");
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + PricingBasisText(ctx).Text);
        Line(sb, "- **Versions:** harness " + D(ctx, "run.harnessVersion") + ", scoring method " + D(ctx, "scoring.methodVersion") + ".");
        Line(sb);
    }

    private static void ResultsAgainstPeers(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var rows = OrderedEntries(sheet);

        Heading(sb, "## Results against peers");

        Heading(sb, "### Quality");
        TableHeader(sb, ctx, new[] { Label("quality.index"), Label("quality.interval"), "Rank" });
        foreach (var (entry, peer) in rows)
        {
            string index = entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : BenchmarkReportFacts.NotAvailable;
            string interval = entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value)
                : BenchmarkReportFacts.NotAvailable;
            TableRow(sb, ctx, peer, new[] { index, interval, RankCell(entry.QualityRank) });
        }
        Line(sb);

        if (IsAvailable(ctx, "quality.intervalOverlap"))
        {
            Line(sb, "*" + sheet.SubjectLabel + ": " + D(ctx, "quality.intervalOverlap")
                + ". This describes where the intervals overlap; it is not a significance test.*");
            Line(sb);
        }
        NoSignificance(sb, ctx);

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

        Heading(sb, "### Dimensions");
        Line(sb, "| Dimension | " + Cell(sheet.SubjectLabel) + " | Peer mean | Difference |");
        Line(sb, "|---|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Dimensions)
        {
            string prefix = "dimension." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix) + " | " + Num(ctx, prefix + ".peerMean") + " | " + Num(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Heading(sb, "### Difficulty bands");
        Line(sb, "| Difficulty band | Questions | " + Cell(sheet.SubjectLabel) + " | Peer mean | Difference |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Bands)
        {
            string prefix = "band." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix + ".questions") + " | " + Num(ctx, prefix + ".score") + " | "
                + Num(ctx, prefix + ".peerMean") + " | " + Num(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Heading(sb, "### Judge-dependent pairs");
        var pairs = Fact(ctx, "panel.judgeDependentPairs");
        Line(sb, pairs != null && pairs.Available
            ? "Pairs whose order depends on which panel member graded them, involving " + sheet.SubjectLabel + ": " + pairs.Display + "."
            : Label("panel.judgeDependentPairs") + ": " + NotAvailableText(pairs));
        Line(sb);
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
            Line(sb, "- " + PlainName(ctx, peer) + ": " + (peer == null ? ctx.Sheet.SubjectExplanation : peer.Explanation));
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

        Heading(sb, "### Difficulty bands");
        Line(sb, "| Difficulty band | Questions | " + Cell(sheet.SubjectLabel) + " |");
        Line(sb, "|---|---|---|");
        foreach (var (key, name) in BenchmarkReportFactLabels.Bands)
        {
            string prefix = "band." + key;
            Line(sb, "| " + name + " | " + Num(ctx, prefix + ".questions") + " | " + Num(ctx, prefix + ".score") + " |");
        }
        Line(sb);
    }

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
            Line(sb, "| " + Cell(Label(fact.Key)) + " | " + Cell(fact.Display) + " |");
        }
        Line(sb);
    }

    /// <summary>The facts of the Speed and cost table, in order.</summary>
    internal static IReadOnlyList<string> SpeedAndCostKeys() => new[]
    {
        "speed.modelTimeP50", "speed.modelTimeP90", "cost.perQuestion", "cost.perRun", "cost.totalRunPerRun",
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

    private static void PerQuestion(StringBuilder sb, Context ctx, string heading)
    {
        var sheet = ctx.Sheet;
        var questions = sheet.Questions.OrderBy(q => q.Number).ToList();

        // Answer sentences need every question's count; a sheet stored without them keeps its claims column.
        bool sentences = questions.Count > 0 && questions.All(q => q.RefutedAnswerSentences.HasValue);
        string refutedHeader = sentences ? "Refuted answer sentences" : "Refuted claims";

        Heading(sb, heading);
        if (ctx.Standalone)
        {
            Line(sb, "| Q | Topic | Band | Score | Critical error | " + refutedHeader + " | Tool calls | Model time |");
            Line(sb, "|---|---|---|---|---|---|---|---|");
        }
        else
        {
            Line(sb, "| Q | Topic | Band | Score | Peer mean | Difference | Critical error | " + refutedHeader + " | Tool calls | Model time |");
            Line(sb, "|---|---|---|---|---|---|---|---|---|---|");
        }
        foreach (var q in questions)
        {
            string peerCells = ctx.Standalone
                ? string.Empty
                : " | " + (q.PeerMean.HasValue ? BenchmarkReportFormat.Whole(q.PeerMean.Value) : NoValue)
                  + " | " + (q.Difference.HasValue ? BenchmarkReportFormat.Signed(q.Difference.Value) : NoValue);
            Line(sb, "| Q" + Inv(q.Number)
                + " | " + Cell(Topic(ctx, q.Number) ?? NoValue)
                + " | " + q.Band
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
            Heading(sb, "### Questions below the peer mean or with a critical error");
            if (noted.Count == 0)
            {
                Line(sb, "No question was more than " + BenchmarkReportFormat.Whole(BenchmarkReportPackPrompt.QuestionNoteGapPoints)
                    + " points below the peer mean or carried a critical error.");
                Line(sb);
            }
        }

        foreach (var q in noted)
        {
            string? topic = Topic(ctx, q.Number);
            var note = ctx.Writer.QuestionNotes.FirstOrDefault(n => n.Question == q.Number);
            Line(sb, "**Q" + Inv(q.Number) + "**" + (topic != null ? " (" + topic + ")" : string.Empty) + ": "
                + (note != null ? Prose(ctx, note.Note) : "No note was written for this question."));
            Line(sb);
        }

        if (ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
        {
            QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full);
        }
    }

    /// <summary>
    /// Every question with each run's answer excerpt; with <paramref name="grading"/>, the complete
    /// answer where it was captured, and also the rubric, the graders' verdicts and the claim
    /// verifier's rulings.
    /// </summary>
    private static void QuestionDetails(StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportQuestion> questions, bool grading)
    {
        Heading(sb, grading ? "### Question details" : "### Questions and answers");

        foreach (var q in questions)
        {
            string? topic = Topic(ctx, q.Number);
            Heading(sb, "#### Q" + Inv(q.Number) + (topic != null ? ": " + topic : string.Empty));

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
                    Heading(sb, "##### Run " + Inv(runId));
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
                    Line(sb, "- **" + grader.Role + " (" + grader.Label + "):** "
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
            LabeledLine(sb, ctx, key);
        }
        Line(sb);

        Line(sb, "*Recorded success or failure describes whether a tool call executed. It does not show that the query was "
            + "well chosen, that the result was relevant, or that the corpus was current.*");
        Line(sb);

        if (ctx.Options.Disclosure == BenchmarkReportDisclosure.Full)
        {
            // tools.failed exists only where the runs recorded per-call rows (harness 17 and later).
            Line(sb, IsAvailable(ctx, "tools.failed")
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
        foreach (string key in new[]
        {
            "panel.meanAbsDelta", "panel.icc", "panel.disagreements", "panel.memberAAlone", "panel.memberBAlone", "style.responseStyleConflict"
        })
        {
            LabeledLine(sb, ctx, key);
        }
        Line(sb);

        var rows = OrderedRows(sheet);
        if (rows.Count == 0)
        {
            Line(sb, "No synthesis findings were recorded.");
            Line(sb);
            return;
        }

        Line(sb, "| Row | Finding | Questions | Support | Recurrence |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var row in rows)
        {
            string kind = row.Status == nameof(BenchmarkConvergenceStatus.Conflicting)
                ? row.Kind + " (A) vs " + BenchmarkSynthesisConvergence.OppositeKind(row.Kind) + " (B)"
                : row.Kind;
            string questions = row.Questions.Count > 0
                ? string.Join(", ", row.Questions.OrderBy(n => n).Select(n => "Q" + Inv(n)))
                : NoValue;
            Line(sb, "| " + row.Id + " | " + Cell(kind + " · " + row.Category.Replace('_', ' ')) + " | " + questions + " | "
                + row.SupportLabel + " | " + Inv(row.Recurrence) + " of " + Inv(runCount) + (runCount == 1 ? " run" : " runs") + " |");
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

    private static void ThreatsToValidity(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;

        Heading(sb, "## Threats to validity");
        Line(sb, "- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation "
            + "history, pre-injected wiki context, spoiler-free mode, web search or subagents.");
        Line(sb, "- Interval: " + D(ctx, "quality.intervalBasis"));
        if (!ctx.Standalone && !string.IsNullOrWhiteSpace(sheet.NoSignificanceSummary))
        {
            Line(sb, "- Significance: " + sheet.NoSignificanceSummary);
        }
        Line(sb, "- The graders are AI models. Each grader's provider relation to the model under test is stated under "
            + "Setup and method; a grader from the model's own provider may read it more favorably.");
        if (string.Equals(sheet.SubjectState, "Degraded", StringComparison.Ordinal))
        {
            Line(sb, "- Comparability: " + sheet.SubjectExplanation);
        }
        if (WriterIndependenceCaveat(ctx) is string caveat)
        {
            Line(sb, "- " + caveat);
        }
        Line(sb);
    }

    private static void Reproducibility(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var (pricing, includesCardDate) = PricingBasisText(ctx);

        Heading(sb, "## Reproducibility appendix");
        LabeledLine(sb, ctx, "run.ids");
        LabeledLine(sb, ctx, "run.dates");
        Line(sb, "- **Model under test:** " + sheet.SubjectProvider + " " + sheet.SubjectModelId + ", thinking level "
            + (sheet.SubjectThinkingLevel ?? "not set"));
        Line(sb, "- **Grader models:** " + (sheet.Graders.Count == 0
            ? "not recorded"
            : string.Join("; ", sheet.Graders.Select(g => g.Role + ": " + g.Provider + " " + g.ModelId
                + (string.IsNullOrWhiteSpace(g.ThinkingLevel) ? string.Empty : ", thinking level " + g.ThinkingLevel)))));
        LabeledLine(sb, ctx, "run.harnessVersion");
        LabeledLine(sb, ctx, "scoring.methodVersion");
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
    {
        bool peerProvider = ctx.Sheet.Peers.Any(p => string.Equals(p.Provider?.Trim(), provider, StringComparison.OrdinalIgnoreCase));
        bool subjectProvider = string.Equals(ctx.Sheet.SubjectProvider?.Trim(), provider, StringComparison.OrdinalIgnoreCase);
        return ctx.Anonymized && peerProvider && !subjectProvider ? WithheldProvider : provider;
    }

    private static void Footer(StringBuilder sb, Context ctx)
    {
        var document = ctx.Document;
        string version = document.ReportFormatVersion == ReportFormatVersion
            ? "format version " + Inv(ReportFormatVersion)
            : "generated under format version " + Inv(document.ReportFormatVersion) + " · rendered with format version " + Inv(ReportFormatVersion);

        Line(sb, "---");
        Line(sb);
        Line(sb, "*Document ID " + document.Id.ToString(CultureInfo.InvariantCulture)
            + " · " + version
            + " · created " + document.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            + " · writer " + document.WriterDisplayName + " (" + document.WriterProvider + ", " + document.WriterModelId + ")"
            + " · disclosure " + ctx.Options.Disclosure.ToString()
            + " · peers " + (ctx.Anonymized ? "anonymized" : "named") + "*");
        Line(sb);
        Line(sb, "*Figures and tables were computed by Overseer. The prose was written by " + document.WriterDisplayName
            + " from those figures and checked automatically for structure, permitted figures and references, word limits, "
            + "disclosure of benchmark text, peer names, significance claims and spelling; the checks do not verify the prose's "
            + "interpretations.*");
    }

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    private static void Items(StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportWriterItem> items, string emptyText)
    {
        if (items.Count == 0)
        {
            Line(sb, emptyText);
        }
        foreach (var item in items)
        {
            string support = Support(ctx, item.Evidence);
            if (ctx.Document.Audience == BenchmarkReportAudience.ExecutiveSummary)
            {
                support = PlainSupportLabel(support);
            }

            Line(sb, "- " + Prose(ctx, item.Text) + " *(" + support + ")*");
            if (ctx.PrintsEvidence)
            {
                EvidenceLine(sb, ctx, item);
            }
        }
        Line(sb);
    }

    /// <summary>A support label in the Executive Summary's plain words.</summary>
    internal static string PlainSupportLabel(string supportLabel) => BenchmarkReportFacts.NormalizeSupportLabel(supportLabel) switch
    {
        BenchmarkReportFacts.SupportBothGraders => "both graders agreed",
        BenchmarkReportFacts.SupportOneGraderDifferentProvider => "raised by one grader",
        BenchmarkReportFacts.SupportOneGraderSameProvider => "raised by one grader, from the model's own company",
        BenchmarkReportFacts.SupportGradersDisagree => "the graders disagree",
        BenchmarkReportFacts.SupportSingleAssessor => "raised by the grader",
        BenchmarkReportFacts.SupportComputed => "computed from the figures",
        _ => supportLabel
    };

    /// <summary>
    /// One line under an item: <c>Both graders — accuracy · Q6 (59 / 100), Q10 (57 / 100) · the claim
    /// verifier refuted an answer sentence on Q6</c>. The rows it cites, the facts it cites by label,
    /// its questions with their scores, and the questions on which the verifier refuted the answer's
    /// own text; each part only where it applies.
    /// </summary>
    private static void EvidenceLine(StringBuilder sb, Context ctx, BenchmarkReportWriterItem item)
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
            parts.Add(string.Join("; ", rows.Select(row => row.SupportLabel + " — " + row.Category.Replace('_', ' ')
                + (runCount > 1 ? ", in " + Inv(row.Recurrence) + " of " + Inv(runCount) + " runs" : string.Empty))));
        }

        foreach (string id in ids)
        {
            if (QuestionNumber(id) != null || rows.Any(r => string.Equals(r.Id, id, StringComparison.Ordinal))) continue;

            var fact = Fact(ctx, id);
            parts.Add(Label(id) + (fact == null ? string.Empty : ": " + (fact.Available ? fact.Display : BenchmarkReportFacts.NotAvailable)));
        }

        var numbers = CitedQuestions(ctx, item, ids);
        if (numbers.Count > 0)
        {
            parts.Add(string.Join(", ", numbers.Select(n => "Q" + Inv(n) + " (" + ScoreText(ctx, n) + ")")));
        }

        foreach (var group in numbers
            .Select(n => (Number: n, Kind: RefutedKind(ctx, n)))
            .Where(x => x.Kind != null)
            .GroupBy(x => x.Kind!)
            .OrderBy(g => g.Key == AnswerSentence ? 0 : 1))
        {
            parts.Add("the claim verifier refuted " + group.Key + " on "
                + BenchmarkReportFormat.LetterList(group.Select(x => "Q" + Inv(x.Number)).ToList()));
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
        numbers.AddRange(ids.Select(QuestionNumber).Where(n => n.HasValue).Select(n => n!.Value));

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

    /// <summary>The number of a <c>Q&lt;n&gt;</c> evidence id, or null for any other id.</summary>
    private static int? QuestionNumber(string id)
        => id.Length > 1 && id[0] == 'Q' && int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;

    private static void Slot(StringBuilder sb, Context ctx, string slot)
    {
        Line(sb, ctx.Writer.Sections.TryGetValue(slot, out var text) && !string.IsNullOrWhiteSpace(text)
            ? Prose(ctx, text)
            : "*Not written.*");
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
            : ctx.Anonymized ? "Model " + peer.Letter : Cell(peer.Label) + " (" + peer.Letter + ")";

    private static string PlainName(Context ctx, BenchmarkReportPeer? peer)
        => peer == null
            ? ctx.Sheet.SubjectLabel
            : ctx.Anonymized ? "Model " + peer.Letter : peer.Label + " (" + peer.Letter + ")";

    private static string RankCell(int? rank) => rank.HasValue ? Inv(rank.Value) : NoValue;

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

    private static string Support(Context ctx, IReadOnlyList<string> evidence)
        => BenchmarkReportFacts.SupportLabelFor(evidence, ctx.Sheet);

    private static BenchmarkReportFact? Fact(Context ctx, string key)
        => ctx.Sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    private static bool IsAvailable(Context ctx, string key) => Fact(ctx, key) is { Available: true };

    /// <summary>The fact's display, or "not available" when the sheet has no such fact.</summary>
    private static string D(Context ctx, string key) => Fact(ctx, key)?.Display ?? BenchmarkReportFacts.NotAvailable;

    /// <summary>A numeric table cell: the fact's display, or <see cref="NoValue"/> when it is unavailable or missing.</summary>
    private static string Num(Context ctx, string key) => Fact(ctx, key) is { Available: true } fact ? Cell(fact.Display) : NoValue;

    private static string Label(string key) => BenchmarkReportFactLabels.Label(key);

    private static string NotAvailableText(BenchmarkReportFact? fact)
    {
        string reason = fact?.UnavailableReason?.Trim() ?? string.Empty;
        return reason.Length == 0 ? "not available." : "not available. " + reason;
    }

    /// <summary>Writer prose with its tokens resolved: <c>{{subject}}</c>, <c>{{peer:X}}</c> and <c>{{fact.key}}</c>.</summary>
    private static string Prose(Context ctx, string? text)
        => Regex.Replace(Normalize(text), TokenPattern, match => Resolve(ctx, match), RegexOptions.CultureInvariant);

    private static string Resolve(Context ctx, Match match)
    {
        string token = match.Groups[1].Value.Trim();

        if (string.Equals(token, "subject", StringComparison.Ordinal)) return ctx.Sheet.SubjectLabel;

        if (token.StartsWith("peer:", StringComparison.Ordinal))
        {
            string letter = token["peer:".Length..].Trim();
            var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, letter, StringComparison.Ordinal));
            if (peer == null) return match.Value;
            return ctx.Anonymized ? "Model " + peer.Letter : peer.Label;
        }

        return Fact(ctx, token)?.Display ?? match.Value;
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

    private static string JoinSentences(string? first, string? second)
    {
        string a = first?.Trim() ?? string.Empty;
        string b = second?.Trim() ?? string.Empty;
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + " " + b;
    }

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

        public bool Anonymized => Options.PeerNaming == BenchmarkReportPeerNaming.Anonymized;

        /// <summary>The sheet has no peers: a stand-alone report.</summary>
        public bool Standalone => Sheet.Peers.Count == 0;

        /// <summary>Evidence lines belong to the Report for AI Researchers and Developers alone, at Detailed and Full.</summary>
        public bool PrintsEvidence => Document.Audience == BenchmarkReportAudience.TechnicalReport
                                      && Options.Disclosure >= BenchmarkReportDisclosure.Detailed;

        /// <summary>The evaluation terms belong to every audience but the Internal Improvement Brief.</summary>
        public bool PrintsEvaluationTerms => Document.Audience != BenchmarkReportAudience.InternalBrief;
    }
}
