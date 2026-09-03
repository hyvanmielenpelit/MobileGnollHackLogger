namespace Overseer.Services.Providers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// One vocabulary for recognising provider transport artifacts — leaked tool-call payloads,
/// routing markers and control tokens — shared by <see cref="ReasoningTextSanitizer"/> on the
/// live streaming path and by the benchmark's artifact scrubber.
///
/// The two were written separately, and they diverged: the scrubber learned to recognise a
/// tool-argument-shaped JSON object by its keys, while the streaming sanitizer still only knew
/// the literal <c>{"tool_uses":</c> form. On the 2026-09-03 GPT-5.6 Luna run that gap let five
/// of eighteen answers reach the benchmark with payloads such as
/// <c>{"function_name":"get_encounter_monster_count","repository":"gnollhack"}</c> in the visible
/// text — and the same model on the same path leaks them into ordinary chat, where nothing
/// scrubs anything. Detection lives here so a rule learned in one place is known in both.
///
/// What is deliberately NOT shared: how each caller acts on a detection. The streaming sanitizer
/// is conservative and releases text intact whenever it is unsure, because it is editing what a
/// user is reading in real time. The scrubber is strict and records everything it removes,
/// because a grader must not see analysis-channel content. Those policies differ on purpose.
/// </summary>
public static class TransportArtifactRules
{
    /// <summary>
    /// Any namespaced routing marker, not just <c>to=functions.&lt;name&gt;</c>. GPT-family models
    /// also emit <c>to=multi_tool_use.parallel</c> when a parallel tool batch leaks into the text
    /// channel, and pinning the namespace let that form through untouched.
    /// </summary>
    public static readonly Regex ToolRoutingMarkerRegex =
        new(@"to=[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+", RegexOptions.Compiled);

    /// <summary>Harness control tokens, e.g. <c>&lt;|start|&gt;</c>.</summary>
    public static readonly Regex ControlTokenRegex =
        new(@"<\|[a-zA-Z_]{1,32}\|>", RegexOptions.Compiled);

    /// <summary>
    /// Tool parameter names that authored prose about GnollHack would not plausibly use as JSON
    /// keys. One of these in a bare object is enough to identify a leaked tool payload.
    ///
    /// <c>function_name</c> and <c>recipient_name</c> are here even though no registered tool
    /// declares them: models hallucinate parameter names, and the 2026-09-03 run leaked
    /// <c>{"function_name":"get_encounter_monster_count","repository":"gnollhack"}</c> — a payload
    /// no schema-derived vocabulary would have matched.
    /// </summary>
    public static readonly IReadOnlySet<string> DistinctiveParameterNames = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "repository", "file_filter", "filenames_only", "context_lines", "search_term",
        "path_filter", "prefix_filter", "namespace_filter", "is_regex", "whole_word",
        "max_results", "line_count", "start_line", "tool_uses", "function_name",
        "recipient_name"
    };

    /// <summary>
    /// Remaining parameter names of the tools the harness exposes. These are ordinary English
    /// words, so one alone proves nothing; two or more with no foreign key does.
    /// </summary>
    public static readonly IReadOnlySet<string> GenericParameterNames = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "query", "name", "type", "file", "article", "section", "category", "topic",
        "symbol", "kind", "parameters"
    };

    /// <summary>
    /// Whether a balanced JSON object looks like a tool argument list: it carries a distinctive
    /// parameter name, or it is made up entirely of two or more known parameter names.
    ///
    /// Shape is never the whole test. Both callers additionally require the object to sit at the
    /// start of a line and outside every fenced code block, because a fenced block is exactly
    /// where a model legitimately shows raw tool JSON to a reader.
    /// </summary>
    public static bool IsToolArgumentShaped(string json)
    {
        var keys = TopLevelKeys(json);
        if (keys.Count == 0) return false;

        if (keys.Any(DistinctiveParameterNames.Contains))
        {
            return true;
        }

        return keys.Count >= 2 &&
               keys.All(k => GenericParameterNames.Contains(k) || DistinctiveParameterNames.Contains(k));
    }

    /// <summary>
    /// Top-level object keys, found by depth tracking rather than by a JSON parser: a leaked
    /// payload is frequently malformed, and a parser would reject it and let it through.
    /// </summary>
    public static List<string> TopLevelKeys(string json)
    {
        var keys = new List<string>();
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int stringStart = -1;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"')
                {
                    inString = false;
                    // A string at depth 1 followed by ':' is a top-level key.
                    if (depth == 1)
                    {
                        int j = i + 1;
                        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                        if (j < json.Length && json[j] == ':')
                        {
                            keys.Add(json.Substring(stringStart + 1, i - stringStart - 1));
                        }
                    }
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    stringStart = i;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    break;
            }
        }

        return keys;
    }
}
