namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

/// <summary>
/// How far an answer's tool calls reached its question's gold evidence, read with its outcome
/// class: exactly one per answer.
/// </summary>
public enum BenchmarkEvidenceReachClass
{
    /// <summary>No evidence, rows unavailable, no answer, or a run before scoring method 13.</summary>
    NotApplicable = 0,
    GroundedCorrect = 1,
    UngroundedCorrect = 2,

    /// <summary>Not correct, and no gold unit ever appeared in a result.</summary>
    RetrievalFailure = 3,

    /// <summary>Not correct; a gold unit appeared in a result and was never opened.</summary>
    NavigationFailure = 4,

    /// <summary>Not correct with a gold unit opened: answering against it, misreading it, or another page contradicting it.</summary>
    ReadButWrong = 5
}

/// <summary>One answer's evidence reach.</summary>
public sealed record BenchmarkEvidenceReachResult
{
    public BenchmarkEvidenceReachClass Class { get; init; }
    public bool NoToolCall { get; init; }
    public bool GoldSurfaced { get; init; }
    public bool GoldOpened { get; init; }

    /// <summary>Whether a personal unit (the asker's race or role page) was opened; null when the question names none.</summary>
    public bool? PersonalContextOpened { get; init; }

    /// <summary>A pruned tool-call row hides whether the gold was reached.</summary>
    public bool Unavailable { get; init; }

    /// <summary>A gold-relevant result was cut by the recorder, so an absent header proves nothing.</summary>
    public bool RecordCut { get; init; }

    public IReadOnlyList<string> SurfacedUnits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OpenedUnits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PersonalUnitsNotOpened { get; init; } = Array.Empty<string>();
}

/// <summary>
/// What a run report needs beyond the run itself to print the information condition and Evidence
/// Reach: the candidate's resolved tool list and the suite's current gold evidence.
/// </summary>
public sealed class BenchmarkRunConditionContext
{
    /// <summary>The candidate's resolved tool allowlist; null when not known.</summary>
    public IReadOnlyList<string>? CandidateTools { get; init; }

    public IReadOnlyDictionary<long, IReadOnlyList<BenchmarkEvidenceUnit>> EvidenceByQuestionId { get; init; } =
        new Dictionary<long, IReadOnlyList<BenchmarkEvidenceUnit>>();

    /// <summary>The first 12 hex characters of the suite's evidence SHA-256; null when no question has evidence.</summary>
    public string? SuiteEvidenceShaPrefix { get; init; }

    public bool HasEvidence => EvidenceByQuestionId.Count > 0;

    public IReadOnlyList<BenchmarkEvidenceUnit>? EvidenceFor(BenchmarkRunAnswer answer)
        => answer.BenchmarkQuestionId is long id && EvidenceByQuestionId.TryGetValue(id, out var units) ? units : null;
}

/// <summary>
/// Computes an answer's evidence reach from its stored tool-call rows, matched on the result
/// headers the tools print: <c>--- path.md ---</c> for wiki articles and
/// <c>--- file:Ln ---</c>, <c>--- file:La-Lb ---</c>, <c>--- file:La-Lb (name, N lines) ---</c> for
/// source. Matching is case-insensitive with <c>\</c> read as <c>/</c>.
/// </summary>
public static class BenchmarkEvidenceReach
{
    private static readonly Regex HeaderLine = new(@"^--- (?<label>.+) ---\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex SourceLabel = new(
        @"^(?<file>.+?):L(?<a>\d+)(?:-L(?<b>\d+))?(?: \((?<fn>[^,()]+), \d+ lines\))?$",
        RegexOptions.CultureInvariant);

    private static readonly string[] WikiSurfacingTools = { "wiki_search", "item_lookup", "monster_lookup", "wiki_view", "wiki_overview" };
    private static readonly string[] LookupTools = { "item_lookup", "monster_lookup" };

    public static string ClassLabel(BenchmarkEvidenceReachClass reachClass) => reachClass switch
    {
        BenchmarkEvidenceReachClass.GroundedCorrect => "grounded correct",
        BenchmarkEvidenceReachClass.UngroundedCorrect => "ungrounded correct",
        BenchmarkEvidenceReachClass.RetrievalFailure => "retrieval failure",
        BenchmarkEvidenceReachClass.NavigationFailure => "navigation failure",
        BenchmarkEvidenceReachClass.ReadButWrong => "read but wrong",
        _ => "not applicable"
    };

    /// <summary>
    /// The reach of <paramref name="answer"/> against <paramref name="units"/>. The outcome comes
    /// from <see cref="BenchmarkOutcomeSummary.Classify"/>, which applies from scoring method 13.
    /// </summary>
    public static BenchmarkEvidenceReachResult Compute(
        BenchmarkRun run, BenchmarkRunAnswer answer, IReadOnlyList<BenchmarkEvidenceUnit>? units)
    {
        BenchmarkOutcomeClass? outcome = BenchmarkCriticalErrorResolver.Applies(run)
            ? BenchmarkOutcomeSummary.Classify(run, answer, BenchmarkRunFinalizer.IsPanelRun(run))
            : null;
        return Compute(answer.ToolCalls ?? new List<BenchmarkRunAnswerToolCall>(), units, outcome);
    }

    /// <summary>The reach of a set of tool-call rows against <paramref name="units"/>, given the answer's outcome.</summary>
    public static BenchmarkEvidenceReachResult Compute(
        IEnumerable<BenchmarkRunAnswerToolCall> toolCalls,
        IReadOnlyList<BenchmarkEvidenceUnit>? units,
        BenchmarkOutcomeClass? outcome)
    {
        var rows = toolCalls.OrderBy(r => r.SortOrder).ToList();
        if (units == null || units.Count == 0)
        {
            return new BenchmarkEvidenceReachResult { NoToolCall = rows.Count == 0 };
        }

        var surfaced = new List<string>();
        var opened = new List<string>();
        bool anyPruned = false;
        bool recordCut = false;

        foreach (var unit in units)
        {
            bool unitSurfaced = false, unitOpened = false;
            foreach (var row in rows)
            {
                if (IsPruned(row))
                {
                    if (IsRelevant(row.Name, unit)) anyPruned = true;
                    continue;
                }

                var (s, o) = Match(row, unit);
                unitSurfaced |= s || o;
                unitOpened |= o;
                if (!s && !o && row.ResultTruncated && IsRelevant(row.Name, unit)) recordCut = true;
            }

            if (unitSurfaced) surfaced.Add(unit.Path);
            if (unitOpened) opened.Add(unit.Path);
        }

        bool goldSurfaced = surfaced.Count > 0;
        bool goldOpened = opened.Count > 0;
        var personal = units.Where(u => u.Personal).ToList();
        bool? personalOpened = personal.Count == 0 ? null : personal.Any(u => opened.Contains(u.Path));
        var personalNotOpened = personal.Where(u => !opened.Contains(u.Path)).Select(u => u.Path).ToList();
        bool unavailable = anyPruned && !goldOpened;

        BenchmarkEvidenceReachClass reachClass;
        if (unavailable || outcome is null or BenchmarkOutcomeClass.NoAnswer)
            reachClass = BenchmarkEvidenceReachClass.NotApplicable;
        else if (outcome == BenchmarkOutcomeClass.Correct)
            reachClass = goldOpened ? BenchmarkEvidenceReachClass.GroundedCorrect : BenchmarkEvidenceReachClass.UngroundedCorrect;
        else if (goldOpened)
            reachClass = BenchmarkEvidenceReachClass.ReadButWrong;
        else if (goldSurfaced)
            reachClass = BenchmarkEvidenceReachClass.NavigationFailure;
        else
            reachClass = BenchmarkEvidenceReachClass.RetrievalFailure;

        return new BenchmarkEvidenceReachResult
        {
            Class = reachClass,
            NoToolCall = rows.Count == 0,
            GoldSurfaced = goldSurfaced,
            GoldOpened = goldOpened,
            PersonalContextOpened = personalOpened,
            Unavailable = unavailable,
            RecordCut = recordCut && !goldOpened,
            SurfacedUnits = surfaced,
            OpenedUnits = opened,
            PersonalUnitsNotOpened = personalNotOpened
        };
    }

    /// <summary>A row the retention sweep pruned: its result is gone although it had one.</summary>
    public static bool IsPruned(BenchmarkRunAnswerToolCall row)
        => row.Result == null && row.ResultLengthChars > 0;

    /// <summary>The per-question line of the run report.</summary>
    public static string Describe(BenchmarkEvidenceReachResult reach)
    {
        if (reach.Unavailable)
            return "Evidence: not available (the tool-call rows were pruned)";

        string Units(IReadOnlyList<string> paths) => paths.Count == 0 ? string.Empty : " (" + string.Join(", ", paths) + ")";
        var parts = new List<string>
        {
            $"surfaced {(reach.GoldSurfaced ? "yes" : "no")}{Units(reach.SurfacedUnits)}, opened {(reach.GoldOpened ? "yes" : "no")}{Units(reach.OpenedUnits)}"
        };
        if (reach.RecordCut)
            parts[0] += ", not seen in the stored result (record cut)";
        if (reach.NoToolCall)
            parts.Add("no tool call");
        foreach (string page in reach.PersonalUnitsNotOpened)
            parts.Add($"personal page {page} not opened");
        if (reach.PersonalContextOpened == true)
            parts.Add("personal page opened");

        return "Evidence: " + string.Join("; ", parts) + $"; Retrieval of gold: {ClassLabel(reach.Class)}";
    }

    private static bool IsRelevant(string? toolName, BenchmarkEvidenceUnit unit) => unit.Kind switch
    {
        BenchmarkEvidenceUnit.WikiKind => WikiSurfacingTools.Contains(toolName, StringComparer.OrdinalIgnoreCase),
        BenchmarkEvidenceUnit.SourceKind => toolName is "source_code_search" or "source_code_view" or "get_function_definition",
        BenchmarkEvidenceUnit.KnowledgeBaseKind => toolName == "get_knowledge_article",
        _ => false
    };

    /// <summary>(surfaced, opened) of one row against one unit.</summary>
    private static (bool Surfaced, bool Opened) Match(BenchmarkRunAnswerToolCall row, BenchmarkEvidenceUnit unit)
    {
        string tool = row.Name ?? string.Empty;
        switch (unit.Kind)
        {
            case BenchmarkEvidenceUnit.WikiKind:
            {
                if (!WikiSurfacingTools.Contains(tool, StringComparer.OrdinalIgnoreCase) || row.Result == null) return (false, false);
                var labels = Headers(row.Result).ToList();
                string target = WikiKey(unit.Path);
                bool surfaced = labels.Any(l => WikiKey(l) == target);
                bool opened = tool.Equals("wiki_view", StringComparison.OrdinalIgnoreCase)
                    ? labels.Count > 0 && WikiKey(labels[0]) == target
                    : LookupTools.Contains(tool, StringComparer.OrdinalIgnoreCase) && labels.Count == 1 && WikiKey(labels[0]) == target;
                return (surfaced, opened);
            }
            case BenchmarkEvidenceUnit.SourceKind:
            {
                if (row.Result == null) return (false, false);
                bool isSearch = tool == "source_code_search";
                bool isView = tool == "source_code_view";
                bool isDefinition = tool == "get_function_definition";
                if (!isSearch && !isView && !isDefinition) return (false, false);

                string target = PathKey(unit.Path);
                bool hasLines = BenchmarkEvidence.TryParseLines(unit.Lines, out int a, out int b);
                bool surfaced = false, opened = false;
                foreach (string label in Headers(row.Result))
                {
                    var match = SourceLabel.Match(label);
                    if (!match.Success || PathKey(match.Groups["file"].Value) != target) continue;
                    surfaced = true;
                    if (isSearch) continue;

                    int start = int.Parse(match.Groups["a"].Value);
                    int end = match.Groups["b"].Success ? int.Parse(match.Groups["b"].Value) : start;
                    string? function = match.Groups["fn"].Success ? match.Groups["fn"].Value.Trim() : null;

                    bool linesOk = hasLines && start <= b && end >= a;
                    bool functionOk = unit.Function != null && isDefinition
                        && string.Equals(function, unit.Function, StringComparison.Ordinal);
                    if (linesOk || functionOk || (!hasLines && unit.Function == null))
                    {
                        opened = true;
                    }
                }
                return (surfaced, opened);
            }
            case BenchmarkEvidenceUnit.KnowledgeBaseKind:
            {
                if (tool != "get_knowledge_article") return (false, false);
                string? topic = TopicOf(row.ArgsText);
                bool opened = topic != null && string.Equals(topic.Trim(), unit.Path.Trim(), StringComparison.OrdinalIgnoreCase);
                return (opened, opened);
            }
            default:
                return (false, false);
        }
    }

    private static IEnumerable<string> Headers(string result)
        => HeaderLine.Matches(result).Select(m => m.Groups["label"].Value.Trim());

    private static string PathKey(string path) => path.Replace('\\', '/').Trim().ToLowerInvariant();

    private static string WikiKey(string path)
    {
        string key = PathKey(path);
        return key.EndsWith(".md", StringComparison.Ordinal) ? key[..^3] : key;
    }

    private static string? TopicOf(string? argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText)) return null;
        try
        {
            using var document = JsonDocument.Parse(argsText);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("topic", out var topic)
                && topic.ValueKind == JsonValueKind.String
                ? topic.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
