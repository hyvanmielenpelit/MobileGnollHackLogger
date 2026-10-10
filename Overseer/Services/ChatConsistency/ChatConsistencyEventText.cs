namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// An Overseer event in words, for the chat consistency documents: a kind's label, and the change as a
/// short before → after. Works from the raw values an event stores, so a document written before these
/// words existed renders them too. A change never holds more than <see cref="MaxChars"/> characters, a
/// run of twelve or more hex digits, or a <c>{</c>; a value it cannot read becomes <see cref="Changed"/>.
/// </summary>
public static class ChatConsistencyEventText
{
    /// <summary>The longest change text.</summary>
    public const int MaxChars = 120;

    /// <summary>What a change reads as when its values cannot be shown.</summary>
    public const string Changed = "changed";

    /// <summary>The characters of a revision a change shows.</summary>
    public const int RevisionChars = 7;

    private const string Arrow = " → ";

    private static readonly Regex LongHexRun = new("[0-9a-fA-F]{12,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Revision = new("^[0-9a-fA-F]{7,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReRun = new(@"^(?<main>[^()]+?)\s*\(re-run (?<rerun>[^()]+)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The corpora of a corpus index fingerprint, in the order a change lists them.</summary>
    private static readonly IReadOnlyList<(string Key, string Name)> Corpora = new[]
    {
        ("gnollhackWiki", "GnollHack wiki"),
        ("gnollhackSource", "GnollHack source code"),
        ("knowledgeBase", "knowledge base"),
        ("nethackWiki", "NetHack wiki"),
        ("nethackSource", "NetHack source code"),
    };

    /// <summary>The candidate prompt options by their JSON name.</summary>
    private static readonly IReadOnlyDictionary<string, string> PromptOptionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["verboseMode"] = "verbose answers",
        ["spoilerFreeMode"] = "spoiler-free mode",
        ["overseerMode"] = "Overseer mode",
        ["enableToolUse"] = "tool use",
        ["enableWebSearch"] = "web search",
        ["allowSourceCodeReferences"] = "source code references",
        ["enableSubAgents"] = "subagents",
        ["isGameOn"] = "game on",
        ["developerMode"] = "developer mode",
        ["hasMessageHistory"] = "message history",
        ["hasWikiContext"] = "wiki context",
        ["hasGameSnapshot"] = "game snapshot",
    };

    /// <summary>The kind's label: <c>Harness version</c>, <c>Wiki revision</c>; another kind in words.</summary>
    public static string Label(string? kind) => kind switch
    {
        OverseerEventKinds.CandidateSystemPrompt => "System prompt",
        OverseerEventKinds.ToolGuides => "Tool guides",
        OverseerEventKinds.KnowledgeBase => "Knowledge base revision",
        OverseerEventKinds.Wiki => "Wiki revision",
        OverseerEventKinds.SourceCode => "Source code revision",
        OverseerEventKinds.CorpusIndex => "Corpus index",
        OverseerEventKinds.CandidatePromptOptions => "Prompt options",
        OverseerEventKinds.ToolIterationCaps => "Tool iterations per question",
        OverseerEventKinds.TotalModelCallCaps => "Model calls per question",
        OverseerEventKinds.QuestionTimeouts => "Question timeout",
        OverseerEventKinds.MaxToolCallsPerQuestion => "Tool calls per question",
        OverseerEventKinds.HarnessVersion => "Harness version",
        _ => UpperFirst(Words(kind) is { Length: > 0 } words ? words : "Overseer setting")
    };

    /// <summary>
    /// The change as a short before → after: <c>harness 53 → 54</c>, <c>wiki revision a8fa85a → 4bb80dc</c>,
    /// <c>re-indexed: GnollHack wiki (file counts unchanged)</c>, <c>game snapshot: off → on</c>,
    /// <c>tool iterations per question: 8 → 10</c>, <c>12 → 16</c>; <see cref="Changed"/> for the system
    /// prompt, the tool guides and any value that cannot be read.
    /// </summary>
    public static string Change(string? kind, string? from, string? to)
    {
        string? before = Clean(from);
        string? after = Clean(to);
        if (before == null || after == null) return Changed;

        string text;
        try
        {
            text = kind switch
            {
                OverseerEventKinds.HarnessVersion => HarnessChange(before, after),
                OverseerEventKinds.KnowledgeBase => RevisionChange("knowledge base revision", before, after),
                OverseerEventKinds.Wiki => RevisionChange("wiki revision", before, after),
                OverseerEventKinds.SourceCode => RevisionChange("source code revision", before, after),
                OverseerEventKinds.CorpusIndex => CorpusChange(before, after),
                OverseerEventKinds.CandidatePromptOptions => PromptOptionsChange(before, after),
                OverseerEventKinds.ToolIterationCaps => CapsChange("tool iterations per question", string.Empty, before, after),
                OverseerEventKinds.TotalModelCallCaps => CapsChange("model calls per question", string.Empty, before, after),
                OverseerEventKinds.QuestionTimeouts => CapsChange("question timeout", "\u00A0s", before, after),
                OverseerEventKinds.MaxToolCallsPerQuestion => IntegerChange(before, after),
                OverseerEventKinds.CandidateSystemPrompt or OverseerEventKinds.ToolGuides => Changed,
                _ => PlainChange(before, after)
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            text = Changed;
        }

        return Safe(text);
    }

    /// <summary>
    /// The change named on its own, for a list of changes: <see cref="Change"/> where it names what
    /// changed, otherwise led by the kind's label: <c>tool calls per question: 12 → 16</c>,
    /// <c>system prompt changed</c>.
    /// </summary>
    public static string Describe(string? kind, string? from, string? to)
    {
        string change = Change(kind, from, to);
        string label = LowerFirst(Label(kind));
        if (string.Equals(change, Changed, StringComparison.Ordinal)) return Safe(label + " " + Changed);

        bool named = kind is OverseerEventKinds.HarnessVersion or OverseerEventKinds.KnowledgeBase or OverseerEventKinds.Wiki
            or OverseerEventKinds.SourceCode or OverseerEventKinds.CorpusIndex or OverseerEventKinds.CandidatePromptOptions
            or OverseerEventKinds.ToolIterationCaps or OverseerEventKinds.TotalModelCallCaps or OverseerEventKinds.QuestionTimeouts;
        return named ? change : Safe(label + ": " + change);
    }

    /// <summary>The text with any <c>{</c> or long hex run replaced by <see cref="Changed"/>, and cut to <see cref="MaxChars"/>.</summary>
    private static string Safe(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('{', StringComparison.Ordinal) || LongHexRun.IsMatch(text)) return Changed;
        return text.Length <= MaxChars ? text : text[..(MaxChars - 1)].TrimEnd(' ', ',', ';', ':') + "…";
    }

    // --- Kinds -----------------------------------------------------------------------------------

    /// <summary><c>harness 53 → 54</c>; a re-run harness kept in words.</summary>
    private static string HarnessChange(string before, string after)
        => "harness " + HarnessText(before) + Arrow + HarnessText(after);

    private static string HarnessText(string value)
    {
        var match = ReRun.Match(value);
        return match.Success ? match.Groups["main"].Value.Trim() + " (re-run " + match.Groups["rerun"].Value.Trim() + ")" : value;
    }

    /// <summary><c>wiki revision a8fa85a → 4bb80dc</c>: each hex revision cut to <see cref="RevisionChars"/> characters.</summary>
    private static string RevisionChange(string noun, string before, string after)
    {
        string? a = RevisionText(before);
        string? b = RevisionText(after);
        return a == null || b == null ? Changed : noun + " " + a + Arrow + b;
    }

    private static string? RevisionText(string value)
        => Revision.IsMatch(value) ? value[..RevisionChars].ToLowerInvariant()
            : value.Length <= 24 && !value.Contains('{', StringComparison.Ordinal) && !LongHexRun.IsMatch(value) ? value
            : null;

    /// <summary>
    /// <c>re-indexed: GnollHack wiki, knowledge base (file counts unchanged)</c>: the corpora whose content
    /// hash or file count moved, with their file counts where those moved; <c>re-indexed, contents
    /// unchanged</c> when only the index times moved.
    /// </summary>
    private static string CorpusChange(string before, string after)
    {
        using var a = JsonDocument.Parse(before);
        using var b = JsonDocument.Parse(after);
        if (a.RootElement.ValueKind != JsonValueKind.Object || b.RootElement.ValueKind != JsonValueKind.Object) return Changed;

        var keys = Corpora.Select(c => c.Key)
            .Concat(a.RootElement.EnumerateObject().Select(p => p.Name))
            .Concat(b.RootElement.EnumerateObject().Select(p => p.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var moved = new List<string>();
        bool countsMoved = false;
        foreach (string key in keys)
        {
            var (shaA, countA, presentA) = CorpusEntry(a.RootElement, key);
            var (shaB, countB, presentB) = CorpusEntry(b.RootElement, key);
            if (!presentA && !presentB) continue;

            string name = Corpora.FirstOrDefault(c => c.Key == key).Name ?? Words(key);
            if (presentA != presentB)
            {
                moved.Add(name + (presentB ? " (indexed)" : " (no longer indexed)"));
                continue;
            }

            bool shaMoved = !string.Equals(shaA, shaB, StringComparison.Ordinal);
            bool countMoved = countA != countB;
            if (!shaMoved && !countMoved) continue;

            if (countMoved && countA is long ca && countB is long cb)
            {
                countsMoved = true;
                moved.Add(name + " (" + Count(ca) + Arrow + Count(cb) + " files)");
            }
            else
            {
                countsMoved |= countMoved;
                moved.Add(name);
            }
        }

        if (moved.Count == 0) return "re-indexed, contents unchanged";
        return "re-indexed: " + string.Join(", ", moved) + (countsMoved ? string.Empty : " (file counts unchanged)");
    }

    /// <summary>A corpus entry's content hash and file count; not present when the key is missing or null.</summary>
    private static (string? Sha, long? Count, bool Present) CorpusEntry(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var entry) || entry.ValueKind != JsonValueKind.Object) return (null, null, false);

        string? sha = entry.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        long? count = entry.TryGetProperty("fileCount", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out long n) ? n : null;
        return (sha, count, true);
    }

    /// <summary><c>game snapshot: off → on; web search: off → on</c>: only the options that changed.</summary>
    private static string PromptOptionsChange(string before, string after)
    {
        using var a = JsonDocument.Parse(before);
        using var b = JsonDocument.Parse(after);
        if (a.RootElement.ValueKind != JsonValueKind.Object || b.RootElement.ValueKind != JsonValueKind.Object) return Changed;

        var names = a.RootElement.EnumerateObject().Select(p => p.Name)
            .Concat(b.RootElement.EnumerateObject().Select(p => p.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var changes = new List<string>();
        foreach (string name in names)
        {
            bool inA = a.RootElement.TryGetProperty(name, out var va);
            bool inB = b.RootElement.TryGetProperty(name, out var vb);
            string rawA = inA ? va.GetRawText() : string.Empty;
            string rawB = inB ? vb.GetRawText() : string.Empty;
            if (string.Equals(rawA, rawB, StringComparison.Ordinal)) continue;

            string label = PromptOptionNames.TryGetValue(name, out string? known) ? known : Words(name);
            string? from = inA ? OptionValue(va) : "not set";
            string? to = inB ? OptionValue(vb) : "not set";
            changes.Add(from == null || to == null ? label + " " + Changed : label + ": " + from + Arrow + to);
        }

        return changes.Count == 0 ? Changed : string.Join("; ", changes);
    }

    /// <summary>An option's value in words: <c>on</c> / <c>off</c>, a number, or a short string; null for anything else.</summary>
    private static string? OptionValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "on",
        JsonValueKind.False => "off",
        JsonValueKind.Null => "not set",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String when value.GetString() is { Length: > 0 and <= 24 } s && !LongHexRun.IsMatch(s) && !s.Contains('{', StringComparison.Ordinal) => s,
        _ => null
    };

    /// <summary>
    /// A per-question cap rendered for every difficulty band: <c>tool iterations per question: 8 → 10</c>
    /// when every band carries one figure, else one change per band that moved,
    /// <c>question timeout (Advanced): 300 → 600 s</c>.
    /// </summary>
    private static string CapsChange(string noun, string unit, string before, string after)
    {
        var a = Bands(before);
        var b = Bands(after);
        if (a == null || b == null) return Changed;

        var flatA = a.Values.Distinct().ToList();
        var flatB = b.Values.Distinct().ToList();
        if (flatA.Count == 1 && flatB.Count == 1 && a.Keys.SequenceEqual(b.Keys))
        {
            return noun + ": " + Count(flatA[0]) + Arrow + Count(flatB[0]) + unit;
        }

        var changes = a.Keys.Concat(b.Keys).Distinct(StringComparer.Ordinal)
            .Where(band => !(a.TryGetValue(band, out long x) && b.TryGetValue(band, out long y) && x == y))
            .Select(band => noun + " (" + band + "): "
                + (a.TryGetValue(band, out long x) ? Count(x) : "not set") + Arrow
                + (b.TryGetValue(band, out long y) ? Count(y) + unit : "not set"))
            .ToList();
        return changes.Count == 0 ? Changed : string.Join("; ", changes);
    }

    /// <summary>A banded cap's figures by band, in their stored order; null when the value is not such an object.</summary>
    private static Dictionary<string, long>? Bands(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        var bands = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out long value)) return null;
            bands[property.Name] = value;
        }
        return bands.Count == 0 ? null : bands;
    }

    /// <summary><c>12 → 16</c>.</summary>
    private static string IntegerChange(string before, string after)
        => long.TryParse(before, NumberStyles.Integer, CultureInfo.InvariantCulture, out long a)
           && long.TryParse(after, NumberStyles.Integer, CultureInfo.InvariantCulture, out long b)
            ? Count(a) + Arrow + Count(b)
            : Changed;

    /// <summary>Two short plain values as they are; <see cref="Changed"/> otherwise.</summary>
    private static string PlainChange(string before, string after)
        => Short(before) && Short(after) ? before + Arrow + after : Changed;

    private static bool Short(string value)
        => value.Length <= 24 && !value.Contains('{', StringComparison.Ordinal) && !value.Contains('[', StringComparison.Ordinal) && !LongHexRun.IsMatch(value);

    // --- Helpers ---------------------------------------------------------------------------------

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Count(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>
    /// An identifier in lower-case words: <c>ToolGuidesSha256</c> reads <c>tool guides</c>, <c>hasWikiContext</c>
    /// reads <c>has wiki context</c>; a trailing <c>Sha256</c>, <c>Sha</c>, <c>Json</c> or <c>Used</c> is left out.
    /// </summary>
    private static string Words(string? identifier)
    {
        string text = (identifier ?? string.Empty).Trim();
        foreach (string suffix in new[] { "Sha256", "Json", "Sha", "Used" })
        {
            if (text.Length > suffix.Length && text.EndsWith(suffix, StringComparison.Ordinal)) text = text[..^suffix.Length];
        }

        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsAsciiLetterOrDigit(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }
            if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != ' ' && (char.IsLower(text[i - 1]) || (i + 1 < text.Length && char.IsLower(text[i + 1]))))
            {
                sb.Append(' ');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        string words = sb.ToString().Trim();
        return LongHexRun.IsMatch(words) ? string.Empty : words;
    }

    private static string UpperFirst(string text) => text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    private static string LowerFirst(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;
}
