namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// The layout preview of a report-pack document: the document a request would write, built in memory
/// from its preparation with placeholder text in every slot and list the writer fills, and real
/// deterministic sections and tables, rendered by the real Markdown and PDF renderers with the given
/// charts and layout. Plain code over a preparation: it calls no model and stores nothing.
/// </summary>
public static class BenchmarkReportLayoutPreview
{
    /// <summary>What the cover's classification banner opens with.</summary>
    public const string Stamp = "LAYOUT PREVIEW — no AI text";

    /// <summary>The most charts one layout preview takes.</summary>
    public const int MaxCharts = 12;

    /// <summary>The share of a slot's word limit its placeholder fills: the length a writer typically uses.</summary>
    public const double TypicalShareOfLimit = 0.8;

    /// <summary>The writer named in a preview requested without one.</summary>
    public const string NoWriterName = "No writer";

    /// <summary>The provider named for <see cref="NoWriterName"/>.</summary>
    public const string NoWriterProvider = "layout preview";

    /// <summary>A question topic's placeholder, within the topic word limit.</summary>
    public const string TopicText = "The report writer's topic for this question appears here.";

    /// <summary>The words of a slot whose limit is not known.</summary>
    private const int DefaultSlotWords = 100;

    /// <summary>The words of a list item without a word limit.</summary>
    private const int ItemWords = 32;

    /// <summary>The strengths and the weaknesses of a document that is not an Executive Summary.</summary>
    private const int TypicalItems = 5;

    /// <summary>The leads of a document that uses them.</summary>
    private const int TypicalLeads = 4;

    private const int SentencesPerParagraph = 8;

    /// <summary><c>The report writer's text for &lt;slot&gt; appears here.</c></summary>
    public static string Sentence(string slot) => "The report writer's text for " + slot + " appears here.";

    /// <summary>
    /// <see cref="Sentence"/> repeated to at least <paramref name="words"/> words, in paragraphs of
    /// <see cref="SentencesPerParagraph"/> sentences separated by a blank line.
    /// </summary>
    public static string PlaceholderText(string slot, int words)
    {
        string sentence = Sentence(slot);
        int count = Math.Max(1, (int)Math.Ceiling(Math.Max(1, words) / (double)WordCount(sentence)));
        return string.Join("\n\n", Enumerable.Repeat(sentence, count)
            .Chunk(SentencesPerParagraph)
            .Select(paragraph => string.Join(" ", paragraph)));
    }

    /// <summary>
    /// The writer's output a preview prints: the headline, every slot the document requires, and the
    /// lists the audience uses (strengths and weaknesses, recommendations per target, leads, the points
    /// about each covered model, and a topic for every question where topics are required), each of
    /// typical length.
    /// </summary>
    public static BenchmarkReportWriterOutput PlaceholderOutput(BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        bool comparison = sheet.IsComparison;
        var spec = BenchmarkReportSlots.For(audience, sheet);
        bool executive = audience == BenchmarkReportAudience.ExecutiveSummary;

        var output = new BenchmarkReportWriterOutput
        {
            Headline = PlaceholderText("headline", Typical(BenchmarkReportPackValidator.HeadlineMaxWords))
        };

        var scope = sheet.IsChatConsistency ? BenchmarkReportScope.ChatConsistency
            : comparison ? BenchmarkReportScope.Comparison
            : BenchmarkReportScope.Model;
        foreach (string slot in spec.SlotsFor(comparison || sheet.Peers.Count > 0))
        {
            int limit = BenchmarkReportPackValidator.SlotMaxWords(audience, slot, scope) ?? DefaultSlotWords;
            output.Sections[slot] = PlaceholderText(slot, Typical(limit));
        }

        int itemWords = executive ? Typical(BenchmarkReportPackValidator.ExecutiveItemMaxWords) : ItemWords;
        if (spec.UsesStrengthsAndWeaknesses)
        {
            output.Strengths = Items<BenchmarkReportWriterItem>("a strength", executive ? spec.MaxStrengths : Math.Min(TypicalItems, spec.MaxStrengths), itemWords);
            output.Weaknesses = Items<BenchmarkReportWriterItem>("a weakness", executive ? spec.MaxWeaknesses : Math.Min(TypicalItems, spec.MaxWeaknesses), itemWords);
        }

        if (spec.UsesRecommendations && spec.RecommendationTargets.Count > 0)
        {
            int perTarget = Math.Max(1, Math.Min(3, BenchmarkReportPackValidator.MaxRecommendations(audience) / spec.RecommendationTargets.Count));
            foreach (string target in spec.RecommendationTargets)
            {
                foreach (var item in Items<BenchmarkReportWriterRecommendation>("a recommendation", perTarget, ItemWords))
                {
                    item.For = target;
                    output.Recommendations.Add(item);
                }
            }
        }

        if (spec.UsesLeads && spec.LeadTriages.Count > 0)
        {
            var leads = Items<BenchmarkReportLead>("a lead", Math.Min(TypicalLeads, spec.MaxLeads), ItemWords);
            for (int i = 0; i < leads.Count; i++)
            {
                leads[i].Triage = spec.LeadTriages[i % spec.LeadTriages.Count];
            }
            output.Leads = leads;
        }

        if (spec.MaxModelPoints > 0)
        {
            int pointWords = Typical(BenchmarkReportPackValidator.ModelPointMaxWords(audience));
            output.Models = sheet.Peers
                .OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal)
                .Select(p => new BenchmarkReportModelPoints
                {
                    Model = p.Letter,
                    Points = Items<BenchmarkReportWriterItem>("a point about this model", spec.MaxModelPoints, pointWords)
                })
                .ToList();
        }

        if (spec.RequiresQuestionTopics)
        {
            output.QuestionTopics = sheet.Questions
                .Select(q => q.Number)
                .Distinct()
                .OrderBy(n => n)
                .Select(n => new BenchmarkReportQuestionTopic { Question = n, Topic = TopicText })
                .ToList();
        }

        return output;
    }

    /// <summary>
    /// The document the request would write about <paramref name="prep"/>, held in memory only and
    /// never saved: of the numbered <paramref name="comparison"/> when there is one (so the cover and
    /// running header print it), by <paramref name="writer"/> or <see cref="NoWriterName"/>, with
    /// <see cref="PlaceholderOutput"/> as its writer's output.
    /// </summary>
    public static BenchmarkReportDocument BuildDocument(
        BenchmarkReportPackPreparation prep,
        BenchmarkReportAudience audience,
        BenchmarkComparison? comparison,
        SystemAiApiConfiguration? writer,
        BenchmarkReportPackRequest request,
        int answerExcerptChars,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(prep);
        ArgumentNullException.ThrowIfNull(request);

        bool comparisonScope = prep.Scope == BenchmarkReportScope.Comparison;
        var runIds = (request.RunIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList();
        var groupIds = (request.GroupIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList();
        var batteryRunIds = (request.BatteryRunIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList();
        var numbered = comparison == null
            ? null
            : new BenchmarkPdfComparison(comparison.Id, comparison.DisplayName, comparison.EntryCount > 0 ? comparison.EntryCount : null);

        return new BenchmarkReportDocument
        {
            PackId = Guid.Empty,
            Audience = audience,
            Origin = BenchmarkReportDocumentOrigin.ReportPack,
            Scope = prep.Scope,
            ComparisonId = comparison?.Id,
            Comparison = comparison,
            CoveredEntryKeysJson = BenchmarkReportJson.Serialize(prep.CoveredEntryKeys.ToList()),
            CoveredSetKey = prep.CoveredSetKey.Length > 0 ? prep.CoveredSetKey : null,
            SubjectKey = comparisonScope ? prep.Sheet.SubjectKey : prep.Subject.Key,
            SubjectLabel = prep.Sheet.SubjectLabel,
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(prep.SubjectRuns.Select(r => r.Id).ToList()),
            ComparisonRequestJson = BenchmarkReportJson.Serialize(new BenchmarkModelComparisonRequest
            {
                RunIds = runIds,
                GroupIds = groupIds,
                BatteryRunIds = batteryRunIds,
                PricingBasis = request.PricingBasis
            }),
            ComparisonKey = BenchmarkReportComparisonKey.From(runIds, groupIds, batteryRunIds),
            SuiteId = prep.Sheet.SuiteId,
            SuiteName = prep.Sheet.SuiteName,
            WriterConfigId = writer?.Id,
            WriterDisplayName = writer?.DisplayName ?? writer?.ModelId ?? NoWriterName,
            WriterProvider = writer?.Provider ?? NoWriterProvider,
            WriterModelId = writer?.ModelId ?? string.Empty,
            WriterThinkingLevel = writer?.ThinkingLevel,
            ReportFormatVersion = BenchmarkReportPackRenderer.CurrentFormatVersion(prep.Scope),
            WriterPromptSha256 = BenchmarkReportPackPrompt.PromptSha256(audience, prep.Scope),
            AnswerExcerptChars = answerExcerptChars,
            FactsJson = BenchmarkReportJson.Serialize(prep.Sheet),
            ContentJson = BenchmarkReportJson.Serialize(prep.Content),
            WriterOutputJson = BenchmarkReportJson.Serialize(PlaceholderOutput(audience, prep.Sheet)),
            ValidationNotesJson = BenchmarkReportJson.Serialize(prep.Notes.ToList()),
            Title = comparisonScope
                ? BenchmarkReportPackRenderer.BuildComparisonTitle(audience, prep.Sheet, numbered, BenchmarkReportPeerNaming.Named)
                : BenchmarkReportPackRenderer.BuildTitle(audience, prep.Sheet),
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = createdAtUtc
        };
    }

    /// <summary>
    /// The options a preview renders with, as a PDF download's: at the fullest disclosure the audience
    /// renders at (Full for every audience), in <paramref name="naming"/>, with the charts placed.
    /// </summary>
    public static BenchmarkReportRenderOptions Options(
        BenchmarkReportAudience audience, BenchmarkReportPeerNaming naming, IReadOnlyList<BenchmarkReportRenderChart> charts) => new()
    {
        Disclosure = BenchmarkReportPackRenderer.AllowedDisclosures(audience).Max(),
        PeerNaming = naming,
        IncludeFrontMatter = false,
        IncludeDocumentFooter = false,
        IncludeFactSheet = false,
        Charts = charts
    };

    /// <summary>
    /// The PDF cover and frame of a preview: the document's own, its classification banner opened by
    /// <see cref="Stamp"/>, and its Document ID row saying it has none.
    /// </summary>
    public static BenchmarkPdfDocumentInfo Info(BenchmarkReportDocument document, BenchmarkReportRenderOptions options, BenchmarkPdfPaper paper)
    {
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, paper);
        return info with
        {
            ClassificationText = Stamp + ". " + info.ClassificationText,
            Facts = info.Facts
                .Select(f => string.Equals(f.Label, "Document ID", StringComparison.Ordinal) ? new BenchmarkPdfFact(f.Label, "none (layout preview)") : f)
                .ToList()
        };
    }

    /// <summary>
    /// The preview as a PDF: the document rendered to Markdown as a download renders it, then to a PDF
    /// with <paramref name="charts"/> placed by <paramref name="layout"/> on <paramref name="paper"/>.
    /// </summary>
    public static byte[] RenderPdf(
        BenchmarkReportDocument document,
        BenchmarkReportPeerNaming naming,
        BenchmarkPdfPaper paper,
        IReadOnlyList<BenchmarkReportRenderChart> charts,
        BenchmarkReportChartLayout? layout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Options(document.Audience, naming, charts ?? Array.Empty<BenchmarkReportRenderChart>());
        string markdown = BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent: layout != null);
        return BenchmarkPdfRenderer.RenderMarkdown(markdown, Info(document, options, paper), ct, options.Charts, layout);
    }

    private static List<T> Items<T>(string label, int count, int words) where T : BenchmarkReportWriterItem, new()
        => Enumerable.Range(0, Math.Max(0, count))
            .Select(_ => new T { Text = PlaceholderText(label, words).Replace("\n\n", " ", StringComparison.Ordinal) })
            .ToList();

    private static int Typical(int limit) => Math.Max(1, (int)Math.Round(limit * TypicalShareOfLimit));

    private static int WordCount(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}
