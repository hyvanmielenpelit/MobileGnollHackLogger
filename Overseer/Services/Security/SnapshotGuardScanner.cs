namespace Overseer.Services.Security;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Which game-client text a scan reads; it decides who could have written each part.</summary>
public enum SnapshotGuardTextKind
{
    /// <summary>The flattened AI snapshot, read section by section.</summary>
    Snapshot,

    /// <summary>The in-game message history: everything the game printed, other players' engravings included.</summary>
    MessageHistory,

    /// <summary>The client's directory manifest.</summary>
    Manifest,

    /// <summary>Any other client tool result.</summary>
    ClientData,
}

public enum SnapshotGuardLevel
{
    Clean,
    Suspicious,
    Injection,
}

/// <summary>
/// What a scan found. <see cref="Codes"/> are the signal families that matched, in a fixed order, and are the only
/// thing ever stored about a finding. <see cref="PlayerAuthored"/> is true when the parts of the text only the
/// current player can write would reach <see cref="Level"/> on their own.
/// </summary>
public sealed record SnapshotGuardVerdict(SnapshotGuardLevel Level, IReadOnlyList<string> Codes, bool PlayerAuthored)
{
    public static readonly SnapshotGuardVerdict Clean = new(SnapshotGuardLevel.Clean, Array.Empty<string>(), false);
}

/// <summary>
/// Finds game text written as instructions to an AI: addressing the AI, overriding its instructions, impersonating
/// its operators or the prompt's structure, steering a tool call, exfiltrating by link or image, invisible
/// characters, and output directives. Pure and deterministic; every pattern is bounded to one line or a short
/// window. Heuristic by nature: a paraphrased instruction without a marker word passes.
/// </summary>
public static class SnapshotGuardScanner
{
    public const string Address = "address";
    public const string Override = "override";
    public const string Authority = "authority";
    public const string Tool = "tool";
    public const string ExfilImage = "exfil-image";
    public const string ExfilUrl = "exfil-url";
    public const string HiddenTag = "hidden-tag";
    public const string Hidden = "hidden";
    public const string Directive = "directive";

    /// <summary>A scan that ran out of time; read as a finding, so a text that cannot be checked is not delivered.</summary>
    public const string ScanTimeout = "scan-timeout";

    /// <summary>The first line of the client's directory manifest, written by <c>GHGame.GenerateDirectoryManifest</c>.</summary>
    public const string ClientManifestHeader = "=== Game Directory Manifest ===";

    /// <summary>The first line of the client's message history, written by <c>GHGame.ExportFullMessageHistory</c>.</summary>
    public const string ClientHistoryHeader = "=== Full Message History ===";

    /// <summary>Every code in the order a verdict lists them.</summary>
    public static readonly IReadOnlyList<string> AllCodes = new[] { Address, Override, Authority, Tool, ExfilImage, ExfilUrl, HiddenTag, Hidden, Directive };

    private static readonly HashSet<string> StrongCodes = new(StringComparer.Ordinal) { Override, Authority, ExfilImage, HiddenTag, ScanTimeout };

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private const string AiWord =
        @"(?:\bA\.?I\b|\bassistants?\b|\bOverseer\b|\bchat ?bots?\b|\blanguage models?\b|\bLLMs?\b|\bChatGPT\b|\bGPT(?:-?\d[\w.]*)?\b|\bClaude\b|\bGemini\b)";

    private const string Imperative =
        @"\b(?:call|use|run|ignore|say|answer|reply|respond|end|write|tell|follow|obey|do not|don't|you must|you are|you should|you will)\b";

    private static readonly Regex AddressPattern = new(
        AiWord + @"[^\n]{0,60}?" + Imperative
        + "|" + Imperative + @"[^\n]{0,60}?" + AiWord
        // The colon form, "AI ASSISTANT:", but not a pet named so on the status line ("Assistant: HP:17(17)").
        + @"|(?:\bA\.?I\b|\bassistant\b|\bchat ?bot\b|\bLLM\b|\blanguage model\b)(?:\s+(?:assistant|model|agent|system))?\s*:(?!\s*HP:)",
        Options, MatchTimeout);

    private static readonly Regex OverridePattern = new(
        @"\b(?:ignore|disregard|forget|override|bypass)\s+(?:(?:all|any|the|your|my|these|those|every|of)\s+){0,3}(?:(?:previous|prior|above|earlier|preceding|original|system|other)\s+){0,2}(?:instructions?|prompts?|directions|guidelines|directives)\b"
        // "Ignore the rules" is ordinary English (an in-game novel quotes it); "ignore your rules" is not.
        + @"|\b(?:ignore|disregard|forget|override|bypass)\s+(?:all\s+(?:of\s+)?)?(?:your|previous|prior|above|earlier|preceding|original|system)\s+(?:\w+\s+)?rules\b"
        + @"|\bnew instructions\b"
        + @"|\bsystem prompt\b"
        + @"|\byou are now\s+(?:(?:an?|the|my)\s+)?(?:\w+\s+)?(?:AI|assistant|chat ?bot|model|DAN|unrestricted|uncensored|jailbroken|in developer mode)\b"
        // "From now on" alone is a quest message ("instructs you to guard ... from now on.").
        + @"|\bfrom now on\b,?\s+(?:you|your|always|only|ignore|respond|answer|reply|act|say|end)\b"
        + @"|\bact as\s+(?:(?:an?|the|my)\s+)?(?:\w+\s+)?(?:AI|assistant|chat ?bot|model|DAN|system|developer|admin(?:istrator)?)\b"
        + @"|\byour new (?:role|instructions|task|goal|purpose)\b"
        + @"|\bdeveloper mode\b"
        + @"|\bjailbreak"
        + @"|\bDAN mode\b|\bdo anything now\b",
        Options, MatchTimeout);

    private static readonly Regex AuthorityPattern = new(
        @"\b(?:from|by)\s+the\s+(?:(?:Overseer|GnollHack)\s+)?(?:team|developers?|devs|dev team|admins?|administrators?|operators?|moderators?)\b"
        + @"|\b(?:message|note|notice|memo|word)\s+(?:from\s+)?the\s+(?:(?:Overseer|GnollHack)\s+)?(?:team|developers?|devs|admins?|administrators?|system|operators?)\b"
        + @"|\b(?:Overseer|GnollHack)\s+(?:team|developers?|devs|dev team|admins?|administrators?|staff|operators?|moderators?|support)\b"
        + @"|\[\s*/?\s*INST\s*\]|<<\s*/?\s*SYS\s*>>|<\|\s*(?:im_start|im_end|system|user|assistant|endoftext)\s*\|>"
        + @"|#{2,}\s*(?:system|instructions?)\b"
        // A turn marker at the start of a line, but not a pet so named on the status line.
        + @"|(?m:^\s*(?:Human|Assistant|System|User)\s*:\s(?!\s*HP:))"
        // A header of the prompt's own structure, inside the body.
        + @"|\bGame Context Snapshot\b|\bFull Message History\b|\bGame Directory Manifest\b|\bSnapshot guard notice\b"
        + @"|<\s*/?\s*untrusted_(?:document_context|game_snapshot)",
        Options, MatchTimeout);

    // Upper case only: "System:" as a turn marker is the line-start form above.
    private static readonly Regex SystemMarkerPattern = new(@"(?:^|[\s\[(])SYSTEM\s*:", RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);

    private static readonly Regex ToolVerbPattern = new(@"\b(?:call|use|run|invoke|execute|trigger)\b", Options, MatchTimeout);

    // The game's own sites, which its texts link to (a junk-mail message, the history file); never an exfiltration.
    private const string OwnSite =
        @"(?:[\w-]+\.)*(?:gnollhack\.com|nethack\.org|nethackwiki\.com|hyvanmielenpelit\.fi)\b|github\.com/hyvanmielenpelit/";

    private static readonly Regex ExfilImagePattern = new(@"!\[[^\]\n]{0,200}\]\(\s*(?:https?:)?//(?!" + OwnSite + ")", Options, MatchTimeout);

    // A link, or a request to send something to an address; "send them to the best schools" is not one.
    private static readonly Regex ExfilUrlPattern = new(
        @"\bhttps?://(?!" + OwnSite + @")|\bwww\.(?!" + OwnSite + @")\w"
        + @"|\b(?:send|post|upload|forward|transmit)\b[^\n]{0,40}?\bto\s+(?:https?://|www\.|\S+@\S+\.\w|\S+\.(?:com|net|org|io|dev|xyz|me|info)\b|(?:my|this|the|a|our)\s+(?:url|link|address|server|webhook|endpoint|site|e-?mail))",
        Options, MatchTimeout);

    private static readonly Regex DirectivePattern = new(
        @"\b(?:end|finish|start|begin|close|sign)\s+(?:every|each|all|your|the)\s+(?:of\s+your\s+)?(?:answers?|responses?|replies|reply|messages?|outputs?)\b"
        // At the start of a clause only: "that's what I always say" is not a directive.
        + @"|(?:(?m:^)\s*|[.!?:;,(]\s*|\b(?:and|please|you must|you should)\s+)always\s+(?:say|reply|respond|include|append|add|mention|answer|end|start|write)\b"
        + @"|\brespond only (?:with|in)\b|\bonly respond with\b"
        + @"|\brepeat after me\b"
        + @"|\bsay the (?:word|phrase)\b"
        + @"|\b(?:include|append|add) the (?:word|phrase|text|string)\b"
        + @"|\btell the (?:player|user) that\b",
        Options, MatchTimeout);

    /// <summary>Who can write the text under a heading of the AI snapshot.</summary>
    private enum Author
    {
        Player,
        PossiblyThirdParty,
    }

    /*
     * The top-level headings of AI_SNAPSHOT_FORMAT_VERSION 16, each a whole line, and who can write under them.
     * Latest messages: src/end.c:916. Pets: end.c:1189. Inventory: end.c:1194. Map: end.c:1054. Notable locations:
     * src/pager.c:3223; Symbols on this map: pager.c:3199. Map grid: src/detect.c:2529. Dungeon overview:
     * src/dungeon.c:2985. Discoveries: src/o_init.c:1185. Logged events: src/files.c. Background, Basics, Current
     * Status, Known properties, Game, Goals, Quests, Achievements, Voluntary challenges and Role score: src/cmd.c.
     * Current Skills: src/weapon.c.
     *
     * Player: labels and #call names (stripped from bones, src/bones.c:247), annotations, pet names (a bones pet is
     * no longer tame, bones.c:710-715), discoveries' "called X", logged wishes and engravings. Possibly third
     * party: named monsters and the ghost in Notable locations, and anything the game printed in Latest messages,
     * which bones levels can carry (bones.c:669, 710-722). Any other heading is possibly third party. A heading this
     * table does not know (a container's contents, the attributes block) continues the section before it.
     */
    private static readonly IReadOnlyDictionary<string, Author> Headings = new Dictionary<string, Author>(StringComparer.Ordinal)
    {
        ["Map:"] = Author.PossiblyThirdParty,
        ["Symbols on this map:"] = Author.PossiblyThirdParty,
        ["Notable locations:"] = Author.PossiblyThirdParty,
        ["Map grid:"] = Author.PossiblyThirdParty,
        ["Status:"] = Author.PossiblyThirdParty,
        ["Latest messages:"] = Author.PossiblyThirdParty,
        ["Pets:"] = Author.Player,
        ["Inventory:"] = Author.Player,
        ["Background:"] = Author.PossiblyThirdParty,
        ["Basics:"] = Author.PossiblyThirdParty,
        ["Current Characteristics:"] = Author.PossiblyThirdParty,
        ["Current Status:"] = Author.PossiblyThirdParty,
        ["Known properties:"] = Author.PossiblyThirdParty,
        ["Game:"] = Author.PossiblyThirdParty,
        ["Current Skills:"] = Author.PossiblyThirdParty,
        ["Discoveries:"] = Author.Player,
        ["Logged events:"] = Author.Player,
        ["Goals:"] = Author.PossiblyThirdParty,
        ["Quests:"] = Author.PossiblyThirdParty,
        ["Achievements:"] = Author.PossiblyThirdParty,
        ["Voluntary challenges:"] = Author.PossiblyThirdParty,
        ["Role score:"] = Author.PossiblyThirdParty,
        ["Dungeon overview:"] = Author.Player,
    };

    /// <summary>The headings the section attribution knows, for the fixture test that pins them to a real export.</summary>
    public static IReadOnlyCollection<string> KnownHeadings => Headings.Keys.ToList();

    /// <summary>
    /// Scans <paramref name="text"/>. <paramref name="toolNames"/> are the registered tool names a tool-coercion
    /// finding looks for. For <see cref="SnapshotGuardTextKind.Snapshot"/> the text before the first known heading
    /// (the version lines and the character's own line) counts as the player's. A manifest or message history whose
    /// first line is the client's own header for that kind is scanned without that line.
    /// </summary>
    public static SnapshotGuardVerdict Scan(string? text, SnapshotGuardTextKind kind, IReadOnlyCollection<string> toolNames)
    {
        if (string.IsNullOrEmpty(text)) return SnapshotGuardVerdict.Clean;
        if (text[0] == '\uFEFF') text = text[1..];
        text = kind switch
        {
            SnapshotGuardTextKind.Manifest => StripFirstLine(text, ClientManifestHeader),
            SnapshotGuardTextKind.MessageHistory => StripFirstLine(text, ClientHistoryHeader),
            _ => text,
        };

        string playerText;
        string otherText;
        switch (kind)
        {
            case SnapshotGuardTextKind.Snapshot:
                (playerText, otherText) = SplitByAuthor(text);
                break;
            case SnapshotGuardTextKind.MessageHistory:
                (playerText, otherText) = (string.Empty, text);
                break;
            default:
                (playerText, otherText) = (text, string.Empty);
                break;
        }

        var playerCodes = Families(playerText, toolNames);
        var otherCodes = Families(otherText, toolNames);
        var codes = AllCodes.Append(ScanTimeout).Where(c => playerCodes.Contains(c) || otherCodes.Contains(c)).ToList();
        var level = LevelOf(codes);
        if (level == SnapshotGuardLevel.Clean) return SnapshotGuardVerdict.Clean;

        var playerLevel = LevelOf(playerCodes);
        bool playerAuthored = level == SnapshotGuardLevel.Injection ? playerLevel == SnapshotGuardLevel.Injection : playerLevel != SnapshotGuardLevel.Clean;
        return new SnapshotGuardVerdict(level, codes, playerAuthored);
    }

    /// <summary>Injection for a strong family or two families; Suspicious for one weak family.</summary>
    public static SnapshotGuardLevel LevelOf(IReadOnlyCollection<string> codes)
    {
        if (codes.Count == 0) return SnapshotGuardLevel.Clean;
        if (codes.Count >= 2 || codes.Any(StrongCodes.Contains)) return SnapshotGuardLevel.Injection;
        return SnapshotGuardLevel.Suspicious;
    }

    /// <summary><paramref name="text"/> without its first line when that line, less a trailing carriage return, is exactly <paramref name="header"/>.</summary>
    private static string StripFirstLine(string text, string header)
    {
        int newline = text.IndexOf('\n');
        string firstLine = newline < 0 ? text : text[..newline];
        if (!string.Equals(firstLine.TrimEnd('\r'), header, StringComparison.Ordinal)) return text;
        return newline < 0 ? string.Empty : text[(newline + 1)..];
    }

    private static (string Player, string Other) SplitByAuthor(string text)
    {
        var player = new StringBuilder();
        var other = new StringBuilder();
        var author = Author.Player;
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (Headings.TryGetValue(line.TrimEnd(), out var next)) author = next;
            (author == Author.Player ? player : other).Append(line).Append('\n');
        }
        return (player.ToString(), other.ToString());
    }

    private static HashSet<string> Families(string text, IReadOnlyCollection<string> toolNames)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (text.Length == 0) return found;
        try
        {
            if (AddressPattern.IsMatch(text)) found.Add(Address);
            if (OverridePattern.IsMatch(text)) found.Add(Override);
            if (AuthorityPattern.IsMatch(text) || SystemMarkerPattern.IsMatch(text)) found.Add(Authority);
            if (HasToolCoercion(text, toolNames)) found.Add(Tool);
            if (ExfilImagePattern.IsMatch(text)) found.Add(ExfilImage);
            if (ExfilUrlPattern.IsMatch(text)) found.Add(ExfilUrl);
            if (DirectivePattern.IsMatch(text)) found.Add(Directive);
        }
        catch (RegexMatchTimeoutException)
        {
            found.Add(ScanTimeout);
        }

        var (tag, hidden) = HiddenCharacters(text);
        if (tag) found.Add(HiddenTag);
        if (hidden) found.Add(Hidden);
        return found;
    }

    /// <summary>A registered tool name, as a whole word, within 40 characters of a verb such as call or run.</summary>
    private static bool HasToolCoercion(string text, IReadOnlyCollection<string> toolNames)
    {
        foreach (string name in toolNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            int at = 0;
            while ((at = text.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = at + name.Length;
                bool whole = (at == 0 || !IsWordChar(text[at - 1])) && (end == text.Length || !IsWordChar(text[end]));
                if (whole)
                {
                    int lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
                    int lineEnd = text.IndexOf('\n', end);
                    if (lineEnd < 0) lineEnd = text.Length;
                    int from = Math.Max(lineStart, at - 40);
                    int to = Math.Min(lineEnd, end + 40);
                    if (ToolVerbPattern.IsMatch(text.AsSpan(from, at - from).ToString())
                        || ToolVerbPattern.IsMatch(text.AsSpan(end, to - end).ToString()))
                        return true;
                }
                at = end;
            }
        }
        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Tag characters (U+E0000–U+E007F); and any other format character, control character other than tab, line
    /// feed and carriage return, U+2028, U+2029 or lone surrogate.
    /// </summary>
    private static (bool Tag, bool Other) HiddenCharacters(string text)
    {
        bool tag = false, other = false;
        for (int i = 0; i < text.Length && !(tag && other); i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                int codePoint = char.ConvertToUtf32(c, text[i + 1]);
                i++;
                if (codePoint is >= 0xE0000 and <= 0xE007F) tag = true;
                else if (CharUnicodeInfo.GetUnicodeCategory(codePoint) == UnicodeCategory.Format) other = true;
                continue;
            }
            if (char.IsSurrogate(c)) { other = true; continue; }
            if (c is '\t' or '\n' or '\r') continue;
            if (c is '\u2028' or '\u2029') { other = true; continue; }
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.Control) other = true;
        }
        return (tag, other);
    }
}
