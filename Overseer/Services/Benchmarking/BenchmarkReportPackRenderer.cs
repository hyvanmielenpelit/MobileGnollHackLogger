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
/// </summary>
public static class BenchmarkReportPackRenderer
{
    public const int ReportFormatVersion = 1;

    private const string ProductName = "Overseer GnollHack Assistant Benchmark";
    private const string TokenPattern = @"\{\{([^{}]+)\}\}";
    private const string NoValue = "—";

    /// <summary>A question this far below the peer mean, or with a critical error, gets its note printed.</summary>
    private const double NoteThreshold = -15.0;

    private const string SummaryStamp = "Confidential. Prepared for the model's provider. Questions are described, not quoted.";
    private const string DetailedStamp = "Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.";
    private const string FullStamp = "INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.";

    private const string LeadsBanner = "Provisional and un-triaged. A lead is not a finding: it must go through the triage, "
        + "evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.";

    public static IReadOnlyList<BenchmarkReportDisclosure> AllowedDisclosures(BenchmarkReportAudience audience)
        => audience == BenchmarkReportAudience.InternalBrief
            ? new[] { BenchmarkReportDisclosure.Full }
            : new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full };

    /// <summary>
    /// Executive Summary and Technical Report render at every level; the Internal Improvement Brief at
    /// Full only. Either peer naming is allowed for all three.
    /// </summary>
    public static bool IsAllowed(BenchmarkReportAudience audience, BenchmarkReportRenderOptions options)
    {
        if (options == null) return false;
        if (!Enum.IsDefined(audience) || !Enum.IsDefined(options.PeerNaming) || !Enum.IsDefined(options.Disclosure)) return false;
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
        Footer(sb, ctx);
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

        Heading(sb, "## How confident are we");
        Slot(sb, ctx, BenchmarkReportSlots.Confidence);
        NoSignificance(sb, ctx);

        AboutBenchmark(sb, ctx);
    }

    private static void RenderTechnicalReport(StringBuilder sb, Context ctx)
    {
        TitleBlock(sb, ctx);

        Heading(sb, "## Abstract");
        Slot(sb, ctx, BenchmarkReportSlots.Abstract);

        KeyFigures(sb, ctx, "## Key figures");
        SetupAndMethod(sb, ctx);
        ResultsAgainstPeers(sb, ctx);

        Heading(sb, "## Why it scored this way");
        Slot(sb, ctx, BenchmarkReportSlots.WhyItScored);
        Items(sb, ctx, ctx.Writer.Weaknesses, "No weaknesses were recorded.");

        Heading(sb, "## What worked well");
        Slot(sb, ctx, BenchmarkReportSlots.WhatWorked);
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

    private static void TitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        Line(sb, "# " + BuildTitle(ctx.Document.Audience, sheet));
        Line(sb);
        Line(sb, "*" + Stamp(ctx.Options.Disclosure) + "*");
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
        if (peers.Count == 0) return "none";

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
            string text = "median answer time " + speed.Display;
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
        Line(sb, "- **Serious errors:** " + (errors == null || !errors.Available ? NotAvailableText(errors) : errors.Display + "."));
        Line(sb);
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
        bool verifier = graders.Any(g => g.Role == BenchmarkReportFacts.ClaimVerifierRole);

        string grading = memberA != null && memberB != null
            ? (string.Equals(memberA.Provider, memberB.Provider, StringComparison.OrdinalIgnoreCase)
                ? "Two AI graders score every answer"
                : "Two AI graders from two different companies score every answer")
            : "An AI grader scores every answer";

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
            + (verifier ? " A separate verifier checks disputed claims against the game's source code." : string.Empty)
            + " Speed is the model's own time per answer, with time spent in tools excluded. Cost is list price for the "
            + "model under test, on this basis: " + D(ctx, "comparison.pricingBasis"));
        Line(sb);
        Line(sb, "What it does not measure: conversations longer than one question, the wiki text the live assistant is "
            + "given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here "
            + "describes the assistant as configured for this benchmark; it may not carry over to those situations.");
        Line(sb);
    }

    private static void SetupAndMethod(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        int runs = sheet.SubjectRunIds.Count;

        Heading(sb, "## Setup and method");
        Line(sb, "- **Suite:** " + sheet.SuiteName + ", " + D(ctx, "suite.questions") + " questions.");
        Line(sb, "- **Chat configuration under test:** " + D(ctx, "config.chat"));
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
                    + (grader.SameFamilyAsSubject ? "same family as the model under test" : "different family from the model under test"));
            }
        }

        Line(sb, "- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a "
            + "critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the "
            + "difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with "
            + "tool time excluded. Cost per question is the model under test's spend divided by the questions asked.");
        Line(sb, "- **Comparability:** every model in this report was measured under one instrument condition, signature `"
            + D(ctx, "comparison.signature") + "`.");
        Line(sb, "- **Pricing basis:** " + D(ctx, "comparison.pricingBasis"));
        Line(sb, "- **Versions:** harness " + D(ctx, "run.harnessVersion") + ", scoring method " + D(ctx, "scoring.methodVersion") + ".");
        Line(sb);
    }

    private static void ResultsAgainstPeers(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var rows = OrderedEntries(sheet);

        Heading(sb, "## Results against peers");

        Heading(sb, "### Quality");
        TableHeader(sb, ctx, new[] { "Intelligence Index", "95 % interval", "Rank" });
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
        TableHeader(sb, ctx, new[] { "Median answer time", "Rank" });
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
        TableHeader(sb, ctx, new[] { "Cost per question", "Rank" });
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
        foreach (var (key, name) in new[] { ("accuracy", "Accuracy"), ("completeness", "Completeness"), ("conciseness", "Conciseness"), ("readability", "Readability") })
        {
            string prefix = "dimension." + key;
            Line(sb, "| " + name + " | " + D(ctx, prefix) + " | " + D(ctx, prefix + ".peerMean") + " | " + D(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Heading(sb, "### Difficulty bands");
        Line(sb, "| Difficulty band | Questions | " + Cell(sheet.SubjectLabel) + " | Peer mean | Difference |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var (key, name) in new[] { ("simple", "Simple"), ("intermediate", "Intermediate"), ("advanced", "Advanced") })
        {
            string prefix = "band." + key;
            Line(sb, "| " + name + " | " + D(ctx, prefix + ".questions") + " | " + D(ctx, prefix + ".score") + " | "
                + D(ctx, prefix + ".peerMean") + " | " + D(ctx, prefix + ".difference") + " |");
        }
        Line(sb);

        Heading(sb, "### Judge-dependent pairs");
        var pairs = Fact(ctx, "panel.judgeDependentPairs");
        Line(sb, pairs != null && pairs.Available
            ? "Pairs whose order depends on which panel member graded them, involving " + sheet.SubjectLabel + ": " + pairs.Display + "."
            : "Judge-dependent pairs: " + NotAvailableText(pairs));
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

        Heading(sb, heading);
        Line(sb, "| Q | Topic | Band | Score | Peer mean | Difference | Critical error | Refuted claims | Tool calls | Model time |");
        Line(sb, "|---|---|---|---|---|---|---|---|---|---|");
        foreach (var q in questions)
        {
            Line(sb, "| Q" + Inv(q.Number)
                + " | " + Cell(Topic(ctx, q.Number) ?? NoValue)
                + " | " + q.Band
                + " | " + (q.Score.HasValue ? BenchmarkReportFormat.Whole(q.Score.Value) : NoValue)
                + " | " + (q.PeerMean.HasValue ? BenchmarkReportFormat.Whole(q.PeerMean.Value) : NoValue)
                + " | " + (q.Difference.HasValue ? BenchmarkReportFormat.Signed(q.Difference.Value) : NoValue)
                + " | " + (q.CriticalError ? "yes" : "no")
                + " | " + Inv(q.RefutedClaims)
                + " | " + BenchmarkReportFormat.OneDecimal(q.ToolCalls)
                + " | " + (q.ModelTimeMs.HasValue ? BenchmarkReportFormat.Seconds(q.ModelTimeMs.Value) : NoValue)
                + " |");
        }
        Line(sb);

        Heading(sb, "### Questions below the peer mean or with a critical error");
        var noted = questions.Where(q => q.CriticalError || (q.Difference.HasValue && q.Difference.Value < NoteThreshold)).ToList();
        if (noted.Count == 0)
        {
            Line(sb, "No question was more than 15 points below the peer mean or carried a critical error.");
            Line(sb);
        }

        foreach (var q in noted)
        {
            string? topic = Topic(ctx, q.Number);
            var note = ctx.Writer.QuestionNotes.FirstOrDefault(n => n.Question == q.Number);
            Line(sb, "**Q" + Inv(q.Number) + "**" + (topic != null ? " (" + topic + ")" : string.Empty) + ": "
                + (note != null ? Prose(ctx, note.Note) : "No note was written for this question."));
            Line(sb);

            if (ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
            {
                var blocks = ContentFor(ctx, q.Number);
                if (blocks.Count > 0)
                {
                    Quote(sb, "**Question:** " + blocks[0].Question.QuestionText);
                    foreach (var (runId, item) in blocks)
                    {
                        Line(sb, ">");
                        Quote(sb, "**Answer excerpt" + (ctx.Content.Runs.Count > 1 ? " (run " + Inv(runId) + ")" : string.Empty) + ":** "
                            + item.AnswerExcerpt);
                    }
                    Line(sb);
                }
            }
        }

        if (ctx.Options.Disclosure == BenchmarkReportDisclosure.Full)
        {
            QuestionDetails(sb, ctx, questions);
        }
    }

    private static void QuestionDetails(StringBuilder sb, Context ctx, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        Heading(sb, "### Question details");

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

            foreach (var (runId, item) in blocks)
            {
                if (ctx.Content.Runs.Count > 1)
                {
                    Heading(sb, "##### Run " + Inv(runId));
                }

                Line(sb, "**Answer excerpt:**");
                Line(sb);
                Quote(sb, item.AnswerExcerpt);
                Line(sb);

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
                    Line(sb, "- **" + ruling.Verdict + ":** \"" + OneLine(ruling.Claim) + "\""
                        + (string.IsNullOrWhiteSpace(ruling.Rationale) ? string.Empty : " — " + OneLine(ruling.Rationale)));
                }
                Line(sb);
            }
        }
    }

    private static void ToolUse(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## Tool-use behavior");
        Line(sb, "- **Tool calls per question:** " + D(ctx, "tools.callsPerQuestion") + " (peer mean " + D(ctx, "tools.callsPerQuestion.peerMean") + ")");
        Line(sb, "- **Source code share:** " + D(ctx, "tools.share.sourceCode"));
        Line(sb, "- **Wiki share:** " + D(ctx, "tools.share.wiki"));
        Line(sb, "- **Structured lookup share:** " + D(ctx, "tools.share.structuredLookup"));
        Line(sb, "- **Knowledge base share:** " + D(ctx, "tools.share.knowledgeBase"));
        Line(sb, "- **Other tools share:** " + D(ctx, "tools.share.other"));
        Line(sb, "- **Answers without a knowledge-base article:** " + D(ctx, "tools.zeroKnowledgeBaseAnswers"));
        Line(sb, "- **Failed tool calls:** " + D(ctx, "tools.failed"));
        Line(sb, "- **Calls refused by the tool budget:** " + D(ctx, "tools.refusedByBudget"));
        Line(sb);
    }

    private static void GraderReliability(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        int runCount = sheet.SubjectRunIds.Count;

        Heading(sb, "## Grader reliability");
        Line(sb, "- **Panel mean absolute difference:** " + D(ctx, "panel.meanAbsDelta"));
        Line(sb, "- **Intraclass correlation, ICC(A,1):** " + D(ctx, "panel.icc"));
        Line(sb, "- **Panel disagreements:** " + D(ctx, "panel.disagreements"));
        Line(sb, "- **Panel member A alone:** " + D(ctx, "panel.memberAAlone"));
        Line(sb, "- **Panel member B alone:** " + D(ctx, "panel.memberBAlone"));
        Line(sb, "- **Response-style conflict:** " + D(ctx, "style.responseStyleConflict"));
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
                    parts.Add((row.Status == BenchmarkReportFacts.SingleStatus ? "assessor: " : "member A: ") + OneLine(row.MemberAText));
                }
                if (!string.IsNullOrWhiteSpace(row.MemberBText))
                {
                    parts.Add("member B: " + OneLine(row.MemberBText));
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
        if (!string.IsNullOrWhiteSpace(sheet.NoSignificanceSummary))
        {
            Line(sb, "- Significance: " + sheet.NoSignificanceSummary);
        }
        Line(sb, "- The graders are AI models. Each grader's family relation to the model under test is stated under "
            + "Setup and method; a grader from the model's own family may read it more favorably.");
        if (string.Equals(sheet.SubjectState, "Degraded", StringComparison.Ordinal))
        {
            Line(sb, "- Comparability: " + sheet.SubjectExplanation);
        }
        Line(sb);
    }

    private static void Reproducibility(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;

        Heading(sb, "## Reproducibility appendix");
        Line(sb, "- **Run ids:** " + D(ctx, "run.ids"));
        Line(sb, "- **Run dates:** " + D(ctx, "run.dates"));
        Line(sb, "- **Model under test:** " + sheet.SubjectProvider + " " + sheet.SubjectModelId + ", thinking level "
            + (sheet.SubjectThinkingLevel ?? "not set"));
        Line(sb, "- **Grader models:** " + (sheet.Graders.Count == 0
            ? "not recorded"
            : string.Join("; ", sheet.Graders.Select(g => g.Role + ": " + g.Provider + " " + g.ModelId
                + (string.IsNullOrWhiteSpace(g.ThinkingLevel) ? string.Empty : ", thinking level " + g.ThinkingLevel)))));
        Line(sb, "- **Harness version:** " + D(ctx, "run.harnessVersion"));
        Line(sb, "- **Scoring method version:** " + D(ctx, "scoring.methodVersion"));
        Line(sb, "- **Comparability signature:** `" + D(ctx, "comparison.signature") + "`");
        Line(sb, "- **System prompt SHA-256 prefix:** `" + D(ctx, "run.promptSha256") + "`");
        Line(sb, "- **Tool guides SHA-256 prefix:** `" + D(ctx, "run.toolGuidesSha256") + "`");
        Line(sb, "- **Pricing basis:** " + D(ctx, "comparison.pricingBasis"));
        Line(sb, "- **Price card date:** " + D(ctx, "cost.pricingAsOf"));
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

    private static void Footer(StringBuilder sb, Context ctx)
    {
        var document = ctx.Document;
        string version = Inv(ReportFormatVersion)
            + (document.ReportFormatVersion != ReportFormatVersion ? " (generated under format version " + Inv(document.ReportFormatVersion) + ")" : string.Empty);

        Line(sb, "---");
        Line(sb);
        Line(sb, "*Report " + document.Id.ToString(CultureInfo.InvariantCulture)
            + " · format version " + version
            + " · created " + document.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            + " · writer " + document.WriterDisplayName + " (" + document.WriterProvider + ", " + document.WriterModelId + ")"
            + " · disclosure " + ctx.Options.Disclosure.ToString()
            + " · peers " + (ctx.Anonymized ? "anonymized" : "named") + "*");
        Line(sb);
        Line(sb, "*Figures and tables were computed by Overseer. The prose was written by " + document.WriterDisplayName
            + " from those figures and checked automatically.*");
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
            Line(sb, "- " + Prose(ctx, item.Text) + " *(" + Support(ctx, item.Evidence) + ")*");
        }
        Line(sb);
    }

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

    internal static string Stamp(BenchmarkReportDisclosure disclosure) => disclosure switch
    {
        BenchmarkReportDisclosure.Summary => SummaryStamp,
        BenchmarkReportDisclosure.Detailed => DetailedStamp,
        _ => FullStamp
    };

    private static string AudienceName(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => "Executive Summary",
        BenchmarkReportAudience.TechnicalReport => "Technical Report",
        BenchmarkReportAudience.InternalBrief => "Internal Improvement Brief",
        _ => audience.ToString()
    };

    private static string JoinSentences(string? first, string? second)
    {
        string a = first?.Trim() ?? string.Empty;
        string b = second?.Trim() ?? string.Empty;
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + " " + b;
    }

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
    }
}
