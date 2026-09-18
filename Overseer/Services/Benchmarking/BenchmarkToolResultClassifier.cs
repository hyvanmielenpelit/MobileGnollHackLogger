namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

/// <summary>The three outcomes <see cref="BenchmarkToolCallRecorder.Outcomes"/> partitions rows into.</summary>
public enum BenchmarkToolCallOutcome
{
    Succeeded,
    Failed,
    RefusedByBudget
}

/// <summary>The layer that cut a result before the model saw it, if any.</summary>
public enum BenchmarkToolModelVisibleCut
{
    None,
    PerToolCap,
    BatchBudget,
    TurnLimit
}

/// <summary>
/// Independent facets of one stored tool-call row. They overlap by design — a miss can also carry
/// a record cut — so there is no single hit/miss/cut verdict.
/// </summary>
/// <param name="Outcome">Succeeded, failed or refused, exactly as <see cref="BenchmarkToolCallRecorder.Outcomes"/> counts it.</param>
/// <param name="PayloadUnavailable">
/// The retention sweep nulled <c>Result</c> beside a non-zero <c>ResultLengthChars</c>. Such a row
/// is never a hit and never a miss, and carries no model-visible cut, partial or content facet.
/// </param>
/// <param name="NotFound">The result is a tool's own not-found answer.</param>
/// <param name="ModelVisibleCut">The layer that cut the result before the model saw it.</param>
/// <param name="RecordCut">The harness's storage cap cut the stored record, not what the model saw.</param>
/// <param name="Partial">The tool's own continuation or per-article truncation notice.</param>
/// <param name="ContentError">An error returned as successful content, or stats JSON that does not parse.</param>
public sealed record BenchmarkToolResultFacets(
    BenchmarkToolCallOutcome Outcome,
    bool PayloadUnavailable,
    bool NotFound,
    BenchmarkToolModelVisibleCut ModelVisibleCut,
    bool RecordCut,
    bool Partial,
    bool ContentError)
{
    /// <summary>A successful call whose payload is still stored, so it can be read as a hit or a miss.</summary>
    public bool IsInspectableSuccess => Outcome == BenchmarkToolCallOutcome.Succeeded && !PayloadUnavailable;
}

/// <summary>
/// Run-level totals of <see cref="BenchmarkToolResultFacets"/>.
/// </summary>
/// <param name="InspectableSuccessful">Succeeded rows whose payload is still stored.</param>
/// <param name="NotFound">Not-found results among <paramref name="InspectableSuccessful"/>.</param>
/// <param name="NotFoundByTool">The same count by tool name, largest first, then by name.</param>
/// <param name="FailedNotFound">Failed rows whose error is a not-found (<c>get_knowledge_article</c>).</param>
/// <param name="FailedNotFoundByTool">The same count by tool name.</param>
/// <param name="Unavailable">Rows whose payload the retention sweep nulled.</param>
/// <param name="PerToolCap">Rows cut by the per-tool result cap.</param>
/// <param name="BatchBudget">Rows cut by the batch output budget.</param>
/// <param name="TurnLimit">Rows cut by the cumulative turn limit.</param>
/// <param name="RecordCut">Rows whose stored record was cut.</param>
/// <param name="Partial">Rows carrying a tool's own partial-result notice.</param>
/// <param name="ContentError">Rows carrying an error as successful content.</param>
public sealed record BenchmarkToolResultSummary(
    int InspectableSuccessful,
    int NotFound,
    IReadOnlyList<KeyValuePair<string, int>> NotFoundByTool,
    int FailedNotFound,
    IReadOnlyList<KeyValuePair<string, int>> FailedNotFoundByTool,
    int Unavailable,
    int PerToolCap,
    int BatchBudget,
    int TurnLimit,
    int RecordCut,
    int Partial,
    int ContentError)
{
    /// <summary>Rows cut before the model saw them, at any layer.</summary>
    public int ModelVisibleCut => PerToolCap + BatchBudget + TurnLimit;
}

/// <summary>
/// Reads a stored <see cref="BenchmarkRunAnswerToolCall"/> row as a diagnostic instrument: whether
/// its payload is still inspectable, whether the tool reported not-found, which layer cut it, and
/// whether the tool returned an error as content.
///
/// Every marker is matched where its producer puts it. Not-found answers are matched at the
/// start of the result for the tool that produces them, never by length. Model-visible cuts are
/// matched at the end of the result, or as the whole result, so a source excerpt that quotes a
/// marker does not count.
///
/// The record stores a result as <c>ToolExecutor</c> returned it (<c>AgentLoopRunner</c> keeps
/// <c>outcome.Content</c>), which is after the per-tool cap but before the batch budget and the
/// turn limit are applied, so those two markers normally never reach a stored result. They are
/// still recognised where one does.
/// </summary>
public static class BenchmarkToolResultClassifier
{
    /// <summary>Note vocabulary, in the order <see cref="Note(BenchmarkToolResultFacets)"/> writes it.</summary>
    public const string NoteMiss = "miss";
    public const string NoteCut = "cut";
    public const string NoteRecordCut = "record cut";
    public const string NotePartial = "partial";
    public const string NoteContentError = "content error";
    public const string NoteUnavailable = "unavailable";

    private const string UnnamedTool = "(unnamed)";

    /// <summary>
    /// Not-found openings, by tool. Producers: <c>SourceCodeSearchTool</c> (both the "for '" report
    /// and the bare fallback), <c>SourceMissContentBuilder.MissPrefix</c>, <c>GetConstantsTool</c>,
    /// <c>ListIndexedFilesTool</c>, <c>WikiSearchTool</c>, <c>WikiViewTool</c>,
    /// <c>NetHackWikiSearchTool</c>, <c>NetHackWikiViewTool</c>, <c>MonsterLookupTool</c> and
    /// <c>ItemLookupTool</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> MissOpenings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["source_code_search"] = new[] { "No relevant source code found" },
        ["get_function_definition"] = new[] { "No definition found for '" },
        ["search_definitions"] = new[] { "No definition found for '" },
        ["get_constants"] = new[] { "No constants found matching" },
        ["list_indexed_files"] = new[] { "No indexed files found matching" },
        ["wiki_search"] = new[] { "No GnollHack wiki article matched '", "No relevant information found in the GnollHack wiki." },
        ["wiki_view"] = new[] { "No wiki article matched '", "Wiki article matching '" },
        ["nethack_wiki_search"] = new[] { "No relevant information found in the NetHack wiki." },
        ["nethack_wiki_view"] = new[] { "No NetHack wiki article matched '", "NetHack wiki article matching '" },
        ["monster_lookup"] = new[] { "No GnollHack wiki article matched the monster '", "No information found for monster: " },
        ["item_lookup"] = new[] { "No GnollHack wiki article matched the item '", "No information found for item: " }
    };

    /// <summary>The stats tools, whose result is <c>StatsResponse</c> JSON rather than text.</summary>
    private static readonly HashSet<string> StatsTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_monster_stats",
        "get_item_stats",
        "get_artifact_stats"
    };

    /// <summary>
    /// Openings of the stats JSON <c>error</c> field that mean not-found (<c>SourceCodeService</c>).
    /// Any other <c>error</c> — the file is not in the index, a definition block failed to parse —
    /// is a structured error and counts as a content error.
    /// </summary>
    private static readonly string[] StatsMissOpenings = { "No monster named '", "No item named '", "No artifact named '" };

    /// <summary>The same openings found in raw text, for a stats record whose cut left JSON that does not parse.</summary>
    private static readonly Regex StatsMissInRawRegex = new(
        @"""error""\s*:\s*""No (?:monster|item|artifact) named '",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The one not-found that is returned as a failure: <c>KnowledgeBaseTool</c>.</summary>
    private const string KnowledgeArticleTool = "get_knowledge_article";
    private const string KnowledgeArticleMissOpening = "Article not found for topic '";

    /// <summary>The minification notice the three stats tools put in <c>message</c>.</summary>
    private const string StatsMinifiedMessageOpening = "Response truncated due to size limits.";

    /// <summary>Opening of an error returned as successful content (<c>SourceCodeService</c>, <c>source_code_view</c>).</summary>
    private const string ErrorContentOpening = "Error:";

    /// <summary><c>ToolExecutor.BuildTruncationSuffix</c>, at the end of the result.</summary>
    private static readonly Regex PerToolCapRegex = new(
        @"\.\.\. \[Truncated: showing \d+ of \d+ characters\.[^\]]*\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The marker both <c>ToolExecutor</c> and <c>BenchmarkToolCallRecorder</c> once used. No current
    /// code emits it. On a row whose stored record was cut short of <c>ResultLengthChars</c> it is
    /// the recorder's cut; otherwise it is the per-tool cap.
    /// </summary>
    private const string LegacyTruncationMarker = "... [Result truncated for length]";

    /// <summary><c>BenchmarkToolCallRecorder.ResultTruncationMarker</c>, at the end of the stored result.</summary>
    private static readonly Regex RecordCutRegex = new(
        @"\.\.\. \[Record truncated: stored \d+ of \d+ characters\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>ToolBatchResultBudget.Apply</c>: a cut tail, or the whole result when skipped.</summary>
    private const string BatchBudgetTruncatedMarker = "... (truncated: batch output budget reached)";
    private const string BatchBudgetSkippedResult = "(skipped: batch output budget reached)";

    /// <summary><c>AgentLoopRunner</c>'s cumulative turn ceiling: a cut tail, or the whole result when omitted.</summary>
    private const string TurnLimitTruncatedMarker = "[Tool output truncated: cumulative turn limit reached]";
    private const string TurnLimitOmittedResult = "[Tool output omitted: cumulative turn limit reached]";

    /// <summary>The budget notice <c>AgentLoopRunner</c> may append after a turn-limit cut.</summary>
    private static readonly Regex TrailingBudgetNoticeRegex = new(
        @"\s*\[Tool budget: \d+ of \d+ calls remaining for this question\.\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A tool's own partial-result notices: <c>SourceCodeService</c>'s line-window continuation
    /// (<c>source_code_view</c>, <c>get_function_definition</c>) and search-output cap, and
    /// <c>NetHackWikiSearchTool</c>'s per-article cut.
    /// </summary>
    private static readonly string[] PartialMarkers =
    {
        "[Output truncated at line ",
        "[... output truncated ...]",
        "... [Article truncated: showing "
    };

    /// <summary><c>SourceCodeService.SearchFiles</c>' hidden-match-group notice.</summary>
    private static readonly Regex HiddenMatchGroupsRegex = new(
        @"\[\.\.\. \d+ additional match groups in this file hidden \.\.\.\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The facets of one stored row. A failed or refused row is inspected only for the one failure
    /// that is a not-found (<c>get_knowledge_article</c>); a row with an unavailable payload only for
    /// its record-cut flag, which survives the retention sweep.
    /// </summary>
    public static BenchmarkToolResultFacets Classify(BenchmarkRunAnswerToolCall row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var (_, failed, refused) = BenchmarkToolCallRecorder.Outcomes(new[] { row });
        var outcome = refused > 0
            ? BenchmarkToolCallOutcome.RefusedByBudget
            : failed > 0 ? BenchmarkToolCallOutcome.Failed : BenchmarkToolCallOutcome.Succeeded;

        string? result = row.Result;
        bool unavailable = result == null && row.ResultLengthChars > 0;
        bool recordCut = row.ResultTruncated;
        bool notFound = false;
        bool partial = false;
        bool contentError = false;
        var cut = BenchmarkToolModelVisibleCut.None;

        if (outcome == BenchmarkToolCallOutcome.Failed)
        {
            notFound = string.Equals(row.Name, KnowledgeArticleTool, StringComparison.OrdinalIgnoreCase)
                && row.Error != null
                && row.Error.TrimStart().StartsWith(KnowledgeArticleMissOpening, StringComparison.Ordinal);
        }
        else if (outcome == BenchmarkToolCallOutcome.Succeeded && result != null)
        {
            string tail = result.TrimEnd();
            recordCut |= RecordCutRegex.IsMatch(tail);

            bool legacyMarker = tail.EndsWith(LegacyTruncationMarker, StringComparison.Ordinal);
            if (legacyMarker && row.ResultTruncated && result.Length < row.ResultLengthChars)
            {
                recordCut = true;
            }
            else
            {
                cut = legacyMarker ? BenchmarkToolModelVisibleCut.PerToolCap : ModelVisibleCutOf(tail);
            }

            string tool = row.Name ?? string.Empty;
            if (StatsTools.Contains(tool))
            {
                ClassifyStatsPayload(result, recordCut, out notFound, out partial, out contentError);
            }
            else
            {
                string head = result.TrimStart();
                if (head.StartsWith(ErrorContentOpening, StringComparison.Ordinal))
                {
                    contentError = true;
                }
                else if (MissOpenings.TryGetValue(tool, out var openings))
                {
                    notFound = StartsWithAny(head, openings);
                }

                partial = ContainsAny(result, PartialMarkers) || HiddenMatchGroupsRegex.IsMatch(result);
            }
        }

        return new BenchmarkToolResultFacets(outcome, unavailable, notFound, cut, recordCut, partial, contentError);
    }

    /// <summary>
    /// The facets as the fixed <c>Note</c> vocabulary — <c>miss</c>, <c>cut</c>, <c>record cut</c>,
    /// <c>partial</c>, <c>content error</c>, <c>unavailable</c> — joined by <c>", "</c>, or the empty
    /// string when none applies. Nothing in it needs Markdown escaping.
    /// </summary>
    public static string Note(BenchmarkToolResultFacets facets)
    {
        ArgumentNullException.ThrowIfNull(facets);

        var parts = new List<string>(6);
        if (facets.NotFound) parts.Add(NoteMiss);
        if (facets.ModelVisibleCut != BenchmarkToolModelVisibleCut.None) parts.Add(NoteCut);
        if (facets.RecordCut) parts.Add(NoteRecordCut);
        if (facets.Partial) parts.Add(NotePartial);
        if (facets.ContentError) parts.Add(NoteContentError);
        if (facets.PayloadUnavailable) parts.Add(NoteUnavailable);
        return string.Join(", ", parts);
    }

    /// <summary>The <c>Note</c> cell for one row.</summary>
    public static string Note(BenchmarkRunAnswerToolCall row) => Note(Classify(row));

    /// <summary>Run-level totals over <paramref name="rows"/>; null rows are skipped.</summary>
    public static BenchmarkToolResultSummary Summarize(IEnumerable<BenchmarkRunAnswerToolCall> rows)
    {
        var classified = (rows ?? Enumerable.Empty<BenchmarkRunAnswerToolCall>())
            .Where(r => r != null)
            .Select(r => (Row: r, Facets: Classify(r)))
            .ToList();

        static IReadOnlyList<KeyValuePair<string, int>> ByTool(IEnumerable<BenchmarkRunAnswerToolCall> source) =>
            source
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Name) ? UnnamedTool : r.Name!, StringComparer.Ordinal)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

        var inspectable = classified.Where(c => c.Facets.IsInspectableSuccess).ToList();
        var misses = inspectable.Where(c => c.Facets.NotFound).Select(c => c.Row).ToList();
        var failedMisses = classified
            .Where(c => c.Facets.Outcome == BenchmarkToolCallOutcome.Failed && c.Facets.NotFound)
            .Select(c => c.Row)
            .ToList();

        return new BenchmarkToolResultSummary(
            InspectableSuccessful: inspectable.Count,
            NotFound: misses.Count,
            NotFoundByTool: ByTool(misses),
            FailedNotFound: failedMisses.Count,
            FailedNotFoundByTool: ByTool(failedMisses),
            Unavailable: classified.Count(c => c.Facets.PayloadUnavailable),
            PerToolCap: classified.Count(c => c.Facets.ModelVisibleCut == BenchmarkToolModelVisibleCut.PerToolCap),
            BatchBudget: classified.Count(c => c.Facets.ModelVisibleCut == BenchmarkToolModelVisibleCut.BatchBudget),
            TurnLimit: classified.Count(c => c.Facets.ModelVisibleCut == BenchmarkToolModelVisibleCut.TurnLimit),
            RecordCut: classified.Count(c => c.Facets.RecordCut),
            Partial: classified.Count(c => c.Facets.Partial),
            ContentError: classified.Count(c => c.Facets.ContentError));
    }

    /// <summary>
    /// The outermost layer whose marker ends <paramref name="tail"/>: the turn limit, then the
    /// batch budget, then the per-tool cap, because each later layer cuts away the earlier one's
    /// marker. A trailing budget notice is set aside first.
    /// </summary>
    private static BenchmarkToolModelVisibleCut ModelVisibleCutOf(string tail)
    {
        string text = TrailingBudgetNoticeRegex.Replace(tail, string.Empty).TrimEnd();

        if (text == TurnLimitOmittedResult || text.EndsWith(TurnLimitTruncatedMarker, StringComparison.Ordinal))
        {
            return BenchmarkToolModelVisibleCut.TurnLimit;
        }

        if (text == BatchBudgetSkippedResult || text.EndsWith(BatchBudgetTruncatedMarker, StringComparison.Ordinal))
        {
            return BenchmarkToolModelVisibleCut.BatchBudget;
        }

        return PerToolCapRegex.IsMatch(text)
            ? BenchmarkToolModelVisibleCut.PerToolCap
            : BenchmarkToolModelVisibleCut.None;
    }

    private static bool StartsWithAny(string text, string[] openings)
    {
        foreach (string opening in openings)
        {
            if (text.StartsWith(opening, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, string[] markers)
    {
        foreach (string marker in markers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A stats tool's JSON payload: an <c>error</c> opening with a not-found phrase is a miss, any
    /// other <c>error</c> is a structured error (a content error), a <c>message</c> opening with the
    /// minification notice is partial, and a <c>message</c> beside a null <c>error</c> is otherwise a
    /// degraded success with no facet. JSON that does not parse is a content error, unless the
    /// record itself was cut, in which case only a not-found opening is looked for in the raw text.
    /// </summary>
    private static void ClassifyStatsPayload(
        string result, bool recordCut, out bool notFound, out bool partial, out bool contentError)
    {
        notFound = false;
        partial = false;
        contentError = false;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                contentError = true;
                return;
            }

            string? error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            string? message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (StartsWithAny(error, StatsMissOpenings))
                {
                    notFound = true;
                }
                else
                {
                    contentError = true;
                }
            }

            partial = message != null && message.StartsWith(StatsMinifiedMessageOpening, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            if (recordCut)
            {
                notFound = StatsMissInRawRegex.IsMatch(result);
            }
            else
            {
                contentError = true;
            }
        }
    }
}
