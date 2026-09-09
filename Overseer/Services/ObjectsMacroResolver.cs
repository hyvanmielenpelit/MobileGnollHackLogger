using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services
{
    /// <summary>
    /// The outcome of resolving one objects.c item entry. <see cref="Success"/> false always
    /// carries a <see cref="FailureReason"/>, so a caller can log a warning and fall back to
    /// the raw source dump instead of throwing.
    /// </summary>
    public sealed class ItemResolution
    {
        public bool Success { get; set; }

        /// <summary>Why the resolve failed. Null when <see cref="Success"/> is true.</summary>
        public string? FailureReason { get; set; }

        /// <summary>The item's oc_name, without quotes.</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>The macro invoked at the call site, e.g. DRGN_ARMR, or OBJECT for a raw entry.</summary>
        public string MacroName { get; set; } = string.Empty;

        /// <summary>oc_class, e.g. ARMOR_CLASS.</summary>
        public string ObjectClass { get; set; } = string.Empty;

        /// <summary>First line of the call site in objects.c, 1-based.</summary>
        public int StartLine { get; set; }

        /// <summary>Last line of the call site in objects.c, 1-based.</summary>
        public int EndLine { get; set; }

        /// <summary>
        /// The stats bag, ready to assign to <see cref="ItemStats.Fields"/>. Insertion ordered.
        /// Numeric values are boxed <see cref="int"/> where they fit, otherwise <see cref="long"/>;
        /// anything the expression evaluator declines is the normalised expression text.
        /// </summary>
        public Dictionary<string, object> Fields { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Footnotes about values whose meaning in the source is overloaded, discarded or
        /// unknown. Also mirrored into <c>Fields["notes"]</c>.
        /// </summary>
        public List<string> Notes { get; } = new();

        /// <summary>
        /// The class-versus-instance caveat, for <see cref="StatsResponse{T}.Message"/>.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Every resolved slot's text, for
        /// <c>PopulateFlagDescriptions(response, resolution.FlagTokens)</c>.
        /// </summary>
        public List<string> FlagTokens { get; } = new();

        /// <summary>The fully substituted pass-2 OBJECT argument list, one entry per slot.</summary>
        public List<string> ObjectSlots { get; } = new();

        public static ItemResolution Failed(string reason) =>
            new() { Success = false, FailureReason = reason };
    }

    /// <summary>
    /// Resolves an objects.c item entry to the fifty-three pass-2 OBJECT slots by repeated
    /// symbolic substitution: bind a call's arguments to the macro's parameter names, substitute
    /// them into the macro body, and repeat on the resulting call until the head is OBJECT.
    /// Constants a wrapper injects -- DRGN_ARMR's delay of 5, weight of 550, ARM_SUIT,
    /// MAT_DRAGON_HIDE and its forced flag ORs -- arrive through the same substitution as the
    /// call site's own arguments, and BITS's argument reordering and its dropped ctnr argument
    /// come out of expanding BITS's own body.
    ///
    /// Alongside the value expansion the resolver runs a second, structural expansion in which
    /// every parameter is bound to its own name. Comparing the two tells it what the source
    /// does to a value rather than what the value is: whether oc_armor_class holds 10 - ac or a
    /// raw bonus, whether oc_cost is derived from another argument, whether oc_nutrition is a
    /// second copy of oc_weight, and which call-site arguments the macro discards.
    /// </summary>
    public sealed class ObjectsMacroResolver
    {
        /// <summary>general.h:1145; applied to oc_spell_casting_penalty at do.c:2733.</summary>
        private const long ArmorSpellCastingPenaltyMultiplier = 30L;

        private const int MaxExpansionHops = 16;
        private const int MaxInlineExpansionPasses = 4;

        /// <summary>
        /// The two attrib.h strength helpers (attrib.h:67-68). They are members of the closed
        /// expression set the resolver evaluates but are defined outside objects.c.
        /// </summary>
        private static readonly Dictionary<string, string> _builtinExpressionMacros =
            new(StringComparer.Ordinal)
            {
                ["STR18"] = "(18 + (@1))",
                ["STR19"] = "(100 + (@1))"
            };

        private static readonly Regex _callHeadRegex =
            new(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        private static readonly Regex _itemNameRegex =
            new(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*(?:OBJ\s*\(\s*)?(""(?:[^""\\]|\\.)*""|[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);

        /// <summary>Recognises the armour AC inversion, objects.c:1005.</summary>
        private static readonly Regex _acInversionRegex =
            new(@"^10\s*-\s*([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

        private sealed class MacroDefine
        {
            public string Name = string.Empty;
            public List<string> Parameters = new();
            public string Body = string.Empty;
            public bool IsObjectLike;
            public int FirstLine;
            public int LastLine;
            public int BodyTokenCount;
        }

        private sealed class CallSite
        {
            public string ItemName = string.Empty;
            public string MacroName = string.Empty;
            public List<string> Arguments = new();
            public int FirstLine;
            public int LastLine;
        }

        private readonly GameDataParser _parser;
        private readonly object _sync = new();

        private string[]? _loadedObjectsLines;
        private string[]? _loadedObjclassLines;

        private readonly Dictionary<string, MacroDefine> _defines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _constants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _structuralCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CallSite> _callSites = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _itemNames = new();

        /// <summary>
        /// Takes <see cref="GameDataParser"/> so that argument tokenizing goes through the one
        /// comment-stripping, literal-aware tokenizer the repository already has, rather than a
        /// second copy of it that can drift.
        /// </summary>
        public ObjectsMacroResolver(GameDataParser parser)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        public bool IsLoaded
        {
            get { lock (_sync) { return _loadedObjectsLines != null; } }
        }

        /// <summary>
        /// Names of every item entry in objects.c, in file order, excluding macro-body
        /// delegations, the <c>#if 0</c> block and the two array sentinels.
        /// </summary>
        public IReadOnlyList<string> ItemNames
        {
            get { lock (_sync) { return _itemNames.ToList(); } }
        }

        /// <summary>
        /// Indexes objects.c. <paramref name="objclassLines"/> supplies the enum constants that
        /// the ternary conditions in CHARGEDRING, MISCELLANEOUSITEM, GENERAL_TOOL,
        /// GENERAL_SPELLTOOL, CONTAINER and GENERAL_ROCK compare against; without it those
        /// conditions do not reduce and their slots come back as expression text.
        /// </summary>
        public void Load(string[] objectsLines, string[]? objclassLines = null)
        {
            if (objectsLines == null) throw new ArgumentNullException(nameof(objectsLines));

            lock (_sync)
            {
                LoadLocked(objectsLines, objclassLines);
            }
        }

        /// <summary>
        /// Resolves the item named <paramref name="itemName"/>, indexing
        /// <paramref name="objectsLines"/> first when they are not the lines already loaded.
        /// This is the entry point for SourceCodeService.GetItemStats.
        /// </summary>
        public ItemResolution Resolve(string[] objectsLines, string[]? objclassLines, string itemName)
        {
            if (objectsLines == null) return ItemResolution.Failed("objects.c content was not supplied.");

            lock (_sync)
            {
                if (!ReferenceEquals(objectsLines, _loadedObjectsLines)
                    || !ReferenceEquals(objclassLines, _loadedObjclassLines))
                {
                    LoadLocked(objectsLines, objclassLines);
                }
                return ResolveLocked(itemName);
            }
        }

        /// <summary>Resolves the item named <paramref name="itemName"/> from the loaded source.</summary>
        public ItemResolution Resolve(string itemName)
        {
            lock (_sync)
            {
                if (_loadedObjectsLines == null)
                {
                    return ItemResolution.Failed("objects.c has not been indexed by ObjectsMacroResolver.");
                }
                return ResolveLocked(itemName);
            }
        }

        /// <summary>Item names containing <paramref name="query"/>, case-insensitively.</summary>
        public IReadOnlyList<string> FindItemNames(string query)
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(query)) return _itemNames.ToList();
                return _itemNames
                    .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /* ==================================================================================
         * Indexing
         * =============================================================================== */

        private void LoadLocked(string[] objectsLines, string[]? objclassLines)
        {
            _defines.Clear();
            _constants.Clear();
            _structuralCache.Clear();
            _callSites.Clear();
            _itemNames.Clear();
            _loadedObjectsLines = objectsLines;
            _loadedObjclassLines = objclassLines;

            if (objclassLines != null) CollectConstants(objclassLines);

            var inactive = FindInactiveRanges(objectsLines);
            var defineSpans = new List<(int First, int Last)>();
            var candidates = new List<(int First, int Last)>();
            var shorthand = new Dictionary<string, (int Define, int Undef)>(StringComparer.Ordinal);

            for (int i = 0; i < objectsLines.Length; i++)
            {
                int last = LastContinuedLine(objectsLines, i);
                string line = objectsLines[i];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    var undef = Regex.Match(trimmed, @"^#\s*undef\s+([A-Za-z_][A-Za-z0-9_]*)");
                    if (undef.Success && shorthand.TryGetValue(undef.Groups[1].Value, out var window)
                        && window.Undef < 0)
                    {
                        shorthand[undef.Groups[1].Value] = (window.Define, i);
                    }

                    if (Regex.IsMatch(trimmed, @"^#\s*define\s+[A-Za-z_]"))
                    {
                        defineSpans.Add((i, last));
                        if (!IsInside(inactive, i))
                        {
                            var define = CollectDefine(objectsLines, i, last);
                            if (define != null)
                            {
                                Register(define);
                                if (define.IsObjectLike
                                    && define.Name.Length == 1
                                    && (define.Body == "PIERCE" || define.Body == "SLASH" || define.Body == "WHACK"))
                                {
                                    shorthand[define.Name] = (i, -1);
                                }
                            }
                        }
                    }

                    i = last;
                    continue;
                }

                if (IsInside(inactive, i) || IsInside(defineSpans, i))
                {
                    i = last;
                    continue;
                }

                if (_itemNameRegex.IsMatch(line)) candidates.Add((i, last));
                i = last;
            }

            int consumedThrough = -1;
            foreach (var (first, _) in candidates)
            {
                /* A candidate inside the argument list of the entry above it is an argument,
                   not an entry of its own. */
                if (first <= consumedThrough) continue;

                var site = BuildCallSite(objectsLines, first, shorthand);
                if (site == null) continue;
                consumedThrough = site.LastLine;

                /* The dummy objects[0] and the array terminator are well-formed OBJECT calls;
                   only their object class tells them apart (objects.c:94-102, 5551-5558). */
                if (site.ItemName.Length == 0) continue;
                if (string.Equals(GetObjectClassOf(site), "ILLOBJ_CLASS", StringComparison.Ordinal)) continue;

                if (!_callSites.ContainsKey(site.ItemName))
                {
                    _callSites[site.ItemName] = site;
                    _itemNames.Add(site.ItemName);
                }
            }
        }

        private void Register(MacroDefine define)
        {
            if (!_defines.TryGetValue(define.Name, out var existing))
            {
                _defines[define.Name] = define;
                return;
            }

            /* objects.c is compiled twice, so OBJECT has two definitions: the pass-1 one at
               :72 keeps only the descriptive text, the pass-2 one at :81 builds the objclass
               row. The pass-2 body is the one that mentions every field, so the definition
               with more body tokens is the one that produces struct objclass. HARDGEM's two
               definitions (:85, :87) have one body token each, so the first -- the non-lint
               (n >= 8) -- stands. */
            if (define.BodyTokenCount > existing.BodyTokenCount)
            {
                _defines[define.Name] = define;
            }
        }

        private MacroDefine? CollectDefine(string[] lines, int first, int last)
        {
            string logical = JoinContinuedLines(lines, first, last);

            var function = Regex.Match(logical, @"^\s*#\s*define\s+([A-Za-z_][A-Za-z0-9_]*)\(([^)]*)\)\s*(.*)$");
            if (function.Success)
            {
                var define = new MacroDefine
                {
                    Name = function.Groups[1].Value,
                    Parameters = function.Groups[2].Value
                        .Split(',')
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList(),
                    Body = StripCComments(function.Groups[3].Value).Trim(),
                    IsObjectLike = false,
                    FirstLine = first,
                    LastLine = last
                };
                define.BodyTokenCount = SafeTokenCount(define.Body);
                return define;
            }

            var obj = Regex.Match(logical, @"^\s*#\s*define\s+([A-Za-z_][A-Za-z0-9_]*)\s+(.*)$");
            if (obj.Success)
            {
                return new MacroDefine
                {
                    Name = obj.Groups[1].Value,
                    Body = StripTrailingComment(obj.Groups[2].Value).Trim(),
                    IsObjectLike = true,
                    FirstLine = first,
                    LastLine = last,
                    BodyTokenCount = 0
                };
            }

            return null;
        }

        private int SafeTokenCount(string body)
        {
            try { return _parser.ParseMacroArgs(body).Count; }
            catch (Exception) { return 0; }
        }

        private static string StripTrailingComment(string text)
        {
            int line = text.IndexOf("//", StringComparison.Ordinal);
            int block = text.IndexOf("/*", StringComparison.Ordinal);
            int cut = line < 0 ? block : block < 0 ? line : Math.Min(line, block);
            return cut < 0 ? text : text.Substring(0, cut);
        }

        /// <summary>
        /// Replaces every C comment with a single space, leaving literals intact, so a word
        /// inside a comment can never be mistaken for a macro parameter or an enum member.
        /// </summary>
        private static string StripCComments(string text)
        {
            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '"' || c == '\'')
                {
                    int end = SkipLiteral(text, i);
                    result.Append(text, i, end - i);
                    i = end - 1;
                    continue;
                }

                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                    result.Append(' ');
                    i--;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    result.Append(' ');
                    if (end < 0) break;
                    i = end + 1;
                    continue;
                }

                result.Append(c);
            }

            return result.ToString();
        }

        /// <summary>
        /// Spans of lines the preprocessor discards. Only <c>#if 0</c> is treated as dead --
        /// objects.c has exactly one, the RING("conflict") at :1928-1933 that became an
        /// artifact. Every other conditional in the file drives the two-pass compilation or
        /// the lint build, and both of those branches contain definitions the resolver needs.
        /// </summary>
        private static List<(int First, int Last)> FindInactiveRanges(string[] lines)
        {
            var ranges = new List<(int, int)>();
            var stack = new Stack<int>();
            int deadStart = -1;
            int deadDepth = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(trimmed, @"^#\s*if(n?def)?\b"))
                {
                    stack.Push(i);
                    if (deadStart < 0 && Regex.IsMatch(trimmed, @"^#\s*if\s+0(?![0-9xX])"))
                    {
                        deadStart = i;
                        deadDepth = stack.Count;
                    }
                }
                else if (Regex.IsMatch(trimmed, @"^#\s*el(se|if)\b"))
                {
                    if (deadStart >= 0 && stack.Count == deadDepth)
                    {
                        ranges.Add((deadStart, i));
                        deadStart = -1;
                        deadDepth = -1;
                    }
                }
                else if (Regex.IsMatch(trimmed, @"^#\s*endif\b"))
                {
                    if (deadStart >= 0 && stack.Count == deadDepth)
                    {
                        ranges.Add((deadStart, i));
                        deadStart = -1;
                        deadDepth = -1;
                    }
                    if (stack.Count > 0) stack.Pop();
                }
            }

            return ranges;
        }

        private static bool IsInside(List<(int First, int Last)> ranges, int line)
        {
            foreach (var (first, last) in ranges)
            {
                if (line >= first && line <= last) return true;
            }
            return false;
        }

        private static int LastContinuedLine(string[] lines, int first)
        {
            int last = first;
            while (last < lines.Length - 1 && lines[last].TrimEnd().EndsWith("\\", StringComparison.Ordinal))
            {
                last++;
            }
            return last;
        }

        private static string JoinContinuedLines(string[] lines, int first, int last)
        {
            var joined = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                string part = i == first ? lines[i] : lines[i].Trim();
                string trimmedEnd = part.TrimEnd();
                if (i < last && trimmedEnd.EndsWith("\\", StringComparison.Ordinal))
                {
                    part = trimmedEnd.Substring(0, trimmedEnd.Length - 1);
                }
                if (i > first) joined.Append(' ');
                joined.Append(part);
            }
            return joined.ToString();
        }

        private CallSite? BuildCallSite(
            string[] lines, int first, Dictionary<string, (int Define, int Undef)> shorthand)
        {
            var head = _callHeadRegex.Match(lines[first]);
            if (!head.Success) return null;

            string macroName = head.Groups[1].Value;
            if (!string.Equals(macroName, "OBJECT", StringComparison.Ordinal)
                && (!_defines.TryGetValue(macroName, out var define) || define.IsObjectLike))
            {
                return null;
            }

            var block = CLexer.ExtractParenBlock(lines, first);
            if (block == null) return null;

            List<string> args;
            try { args = _parser.ParseMacroArgs(InnerArgs(block.Content)); }
            catch (Exception) { return null; }
            if (args.Count == 0) return null;

            /* #define P PIERCE / S SLASH / B WHACK are live only between their own #define and
               #undef (objects.c:175-177, 980-982). Outside that window a bare P, S or B does
               not occur, but P_BOW, P_SLING and P_NONE do, so the replacement is confined to
               the window and to whole one-character tokens. */
            for (int a = 0; a < args.Count; a++)
            {
                string token = args[a].Trim();
                if (token.Length != 1) continue;
                if (shorthand.TryGetValue(token, out var window)
                    && first > window.Define
                    && (window.Undef < 0 || first < window.Undef)
                    && _defines.TryGetValue(token, out var alias))
                {
                    args[a] = alias.Body;
                }
            }

            return new CallSite
            {
                ItemName = UnquoteName(FirstNameArgument(macroName, args)),
                MacroName = macroName,
                Arguments = args,
                FirstLine = first,
                LastLine = block.EndLine
            };
        }

        private string FirstNameArgument(string macroName, List<string> args)
        {
            if (!string.Equals(macroName, "OBJECT", StringComparison.Ordinal)) return args[0];

            /* A raw OBJECT entry carries its name inside the OBJ(...) block. */
            if (TryParseCall(args[0], out string head, out var objArgs)
                && string.Equals(head, "OBJ", StringComparison.Ordinal)
                && objArgs.Count > 0)
            {
                return objArgs[0];
            }
            return args[0];
        }

        private static string UnquoteName(string token)
        {
            string trimmed = token.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }
            return string.Empty;
        }

        private string GetObjectClassOf(CallSite site)
        {
            if (!TryExpandToObject(site.MacroName, site.Arguments, out var slots, out _)) return string.Empty;
            int slot = ObjectClassFieldMap.ObjectSlotOf("oc_class");
            return slot >= 0 && slot < slots.Count ? Normalise(slots[slot]) : string.Empty;
        }

        /* ==================================================================================
         * Constants, for the ternary conditions
         * =============================================================================== */

        private void CollectConstants(string[] lines)
        {
            var inactive = FindInactiveRanges(lines);

            for (int i = 0; i < lines.Length; i++)
            {
                if (IsInside(inactive, i)) continue;
                string trimmed = lines[i].TrimStart();

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    var define = Regex.Match(trimmed, @"^#\s*define\s+([A-Za-z_][A-Za-z0-9_]*)\s+(-?\d+)[UL]*\s*(?:$|\s|/[/*])");
                    if (define.Success
                        && long.TryParse(define.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    {
                        _constants[define.Groups[1].Value] = value;
                    }
                    continue;
                }

                if (!Regex.IsMatch(trimmed, @"^enum\b")) continue;

                var block = CLexer.ExtractBracedBlock(lines, i);
                if (block == null) continue;

                long next = 0;
                foreach (var raw in SplitTopLevel(StripBraces(StripCComments(block.Content))))
                {
                    string entry = raw.Trim();
                    if (entry.Length == 0) continue;

                    var member = Regex.Match(entry, @"^([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*(.+))?$", RegexOptions.Singleline);
                    if (!member.Success) continue;

                    if (member.Groups[2].Success)
                    {
                        string valueText = ReplaceKnownConstants(Normalise(member.Groups[2].Value));
                        if (!TryEvaluateExpression(valueText, out long assigned)) continue;
                        next = assigned;
                    }

                    _constants[member.Groups[1].Value] = next;
                    next++;
                }

                i = block.EndLine;
            }
        }

        private static string StripBraces(string content)
        {
            string trimmed = content.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) trimmed = trimmed.Substring(1);
            int close = trimmed.LastIndexOf('}');
            if (close >= 0) trimmed = trimmed.Substring(0, close);
            return trimmed;
        }

        /* ==================================================================================
         * Substitution
         * =============================================================================== */

        private ItemResolution ResolveLocked(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return ItemResolution.Failed("No item name was supplied.");
            }

            if (!_callSites.TryGetValue(itemName.Trim(), out var site))
            {
                return ItemResolution.Failed(
                    $"objects.c has no item entry named '{itemName}'. Macro-body delegations, the #if 0 block and the two array sentinels are not item entries.");
            }

            if (!TryExpandToObject(site.MacroName, site.Arguments, out var slots, out string? error))
            {
                return ItemResolution.Failed(
                    $"Could not expand {site.MacroName}(\"{site.ItemName}\") at objects.c:{site.FirstLine + 1} to an OBJECT call: {error}");
            }

            var structural = ResolveStructural(site.MacroName);
            if (structural == null)
            {
                return ItemResolution.Failed(
                    $"Could not expand the macro {site.MacroName} symbolically, so derived and raw values cannot be told apart.");
            }

            if (!TryExpandBlock(slots[ObjectClassFieldMap.ObjBlockSlot], "OBJ",
                    ObjectClassFieldMap.ObjDescriptorSlotCount, out var objValues, out error))
            {
                return ItemResolution.Failed(
                    $"Could not expand the OBJ block of '{site.ItemName}': {error}");
            }

            if (!TryExpandBlock(slots[ObjectClassFieldMap.BitsBlockSlot], "BITS",
                    ObjectClassFieldMap.BitsSlotCount, out var bitsValues, out error))
            {
                return ItemResolution.Failed(
                    $"Could not expand the BITS block of '{site.ItemName}': {error}");
            }

            TryExpandBlock(structural[ObjectClassFieldMap.ObjBlockSlot], "OBJ",
                ObjectClassFieldMap.ObjDescriptorSlotCount, out var structuralObjValues, out _);
            TryExpandBlock(structural[ObjectClassFieldMap.BitsBlockSlot], "BITS",
                ObjectClassFieldMap.BitsSlotCount, out var structuralBitsValues, out _);

            var resolution = new ItemResolution
            {
                Success = true,
                ItemName = site.ItemName,
                MacroName = site.MacroName,
                StartLine = site.FirstLine + 1,
                EndLine = site.LastLine + 1
            };

            Emit(resolution, site, slots, objValues, bitsValues,
                structural, structuralObjValues, structuralBitsValues);

            return resolution;
        }

        /// <summary>
        /// Expands a macro with every parameter bound to its own name, so the resulting slots
        /// read as the source's own formulae -- "10 - ac", "nutrition / 20 + 5", "wt" -- rather
        /// than as values.
        /// </summary>
        private List<string>? ResolveStructural(string macroName)
        {
            if (_structuralCache.TryGetValue(macroName, out var cached)) return cached;

            if (!_defines.TryGetValue(macroName, out var define) || define == null || define.IsObjectLike)
            {
                return null;
            }

            var identity = define.Parameters.ToList();

            if (!TryExpandToObject(macroName, identity, out var slots, out _)) return null;

            _structuralCache[macroName] = slots;
            return slots;
        }

        private bool TryExpandToObject(
            string macroName, List<string> arguments, out List<string> slots, out string? error)
        {
            slots = new List<string>();
            string head = macroName;
            var current = arguments;

            for (int hop = 0; hop <= MaxExpansionHops; hop++)
            {
                if (string.Equals(head, "OBJECT", StringComparison.Ordinal))
                {
                    if (current.Count != ObjectClassFieldMap.ObjectSlotCount)
                    {
                        error = $"OBJECT was reached with {current.Count} arguments, expected {ObjectClassFieldMap.ObjectSlotCount}.";
                        return false;
                    }
                    slots = current;
                    error = null;
                    return true;
                }

                if (!_defines.TryGetValue(head, out var define) || define == null || define.IsObjectLike)
                {
                    error = $"objects.c has no function-like #define for '{head}'.";
                    return false;
                }

                if (define.Parameters.Count != current.Count)
                {
                    error = $"'{head}' takes {define.Parameters.Count} arguments but the call supplies {current.Count}.";
                    return false;
                }

                string body = SubstituteParameters(define.Body, define.Parameters, current);
                if (!TryParseCall(body, out head, out current))
                {
                    error = $"the body of '{define.Name}' did not expand to a single macro call.";
                    return false;
                }
            }

            error = $"expansion did not reach OBJECT within {MaxExpansionHops} substitutions.";
            return false;
        }

        private bool TryExpandBlock(
            string? token, string expectedMacro, int expectedCount, out List<string> values, out string? error)
        {
            values = new List<string>();

            if (token == null || !TryParseCall(token, out string head, out var args))
            {
                error = $"slot did not hold a {expectedMacro}(...) block.";
                return false;
            }

            if (!string.Equals(head, expectedMacro, StringComparison.Ordinal))
            {
                error = $"expected a {expectedMacro}(...) block but found '{head}'.";
                return false;
            }

            if (!_defines.TryGetValue(expectedMacro, out var define) || define == null)
            {
                error = $"objects.c has no #define for {expectedMacro}.";
                return false;
            }

            if (define.Parameters.Count != args.Count)
            {
                error = $"{expectedMacro} takes {define.Parameters.Count} arguments but the block supplies {args.Count}.";
                return false;
            }

            try
            {
                values = _parser.ParseMacroArgs(SubstituteParameters(define.Body, define.Parameters, args));
            }
            catch (Exception ex)
            {
                error = $"tokenizing the expanded {expectedMacro} body failed: {ex.Message}";
                return false;
            }

            if (values.Count != expectedCount)
            {
                error = $"the expanded {expectedMacro} body produced {values.Count} values, expected {expectedCount}.";
                return false;
            }

            error = null;
            return true;
        }

        private bool TryParseCall(string text, out string head, out List<string> arguments)
        {
            head = string.Empty;
            arguments = new List<string>();

            string trimmed = text.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                trimmed = StripBraces(trimmed).Trim();
            }

            var match = _callHeadRegex.Match(trimmed);
            if (!match.Success) return false;
            if (!trimmed.EndsWith(")", StringComparison.Ordinal)) return false;

            head = match.Groups[1].Value;
            int open = trimmed.IndexOf('(');
            string inner = trimmed.Substring(open + 1, trimmed.Length - open - 2);

            try { arguments = _parser.ParseMacroArgs(inner); }
            catch (Exception) { return false; }
            return true;
        }

        private static string InnerArgs(string call)
        {
            int open = call.IndexOf('(');
            if (open < 0) return call;
            int close = call.LastIndexOf(')');
            if (close <= open) return call.Substring(open + 1);
            return call.Substring(open + 1, close - open - 1);
        }

        /// <summary>
        /// Replaces whole identifier tokens naming a macro parameter with the bound argument
        /// text, exactly as the C preprocessor does, and never inside a string or character
        /// literal. Only whole tokens match, so a parameter called <c>ac</c> does not touch
        /// MAT_DRAGON_HIDE or oc_class.
        /// </summary>
        private static string SubstituteParameters(
            string body, List<string> parameters, List<string> arguments)
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < parameters.Count && i < arguments.Count; i++)
            {
                bindings[parameters[i]] = arguments[i].Trim();
            }
            return ReplaceIdentifiers(body, bindings);
        }

        private static string ReplaceIdentifiers(string text, Dictionary<string, string> bindings)
        {
            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"' || c == '\'')
                {
                    int end = SkipLiteral(text, i);
                    result.Append(text, i, end - i);
                    i = end - 1;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int start = i;
                    while (i < text.Length && IsIdentifierPart(text[i])) i++;
                    string identifier = text.Substring(start, i - start);
                    result.Append(bindings.TryGetValue(identifier, out string? bound) ? bound : identifier);
                    i--;
                    continue;
                }

                result.Append(c);
            }

            return result.ToString();
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static int SkipLiteral(string text, int start)
        {
            char quote = text[start];
            if (quote == '\'' && !IsCharLiteralStart(text, start)) return start + 1;

            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (text[i] == quote) return i + 1;
            }
            return text.Length;
        }

        /// <summary>
        /// Whether the apostrophe at <paramref name="index"/> opens a character literal, which
        /// needs a closing apostrophe within the span a literal can occupy. An apostrophe in
        /// prose is an ordinary character.
        /// </summary>
        private static bool IsCharLiteralStart(string text, int index)
        {
            int limit = Math.Min(text.Length, index + 8);
            for (int i = index + 1; i < limit; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (text[i] == '\'') return i > index + 1;
            }
            return false;
        }

        /// <summary>Collapses whitespace runs outside literals to one space.</summary>
        private static string Normalise(string text)
        {
            var result = new StringBuilder(text.Length);
            bool pendingSpace = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"' || c == '\'')
                {
                    if (pendingSpace && result.Length > 0) result.Append(' ');
                    pendingSpace = false;
                    int end = SkipLiteral(text, i);
                    result.Append(text, i, end - i);
                    i = end - 1;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && result.Length > 0) result.Append(' ');
                pendingSpace = false;
                result.Append(c);
            }

            return result.ToString();
        }

        private static List<string> SplitTopLevel(string text)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(text, i) - 1;
                    continue;
                }
                if (c == '(' || c == '{' || c == '[') depth++;
                else if (c == ')' || c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (start <= text.Length - 1) parts.Add(text.Substring(start));
            return parts;
        }

        /* ==================================================================================
         * The closed expression set: integer arithmetic, ternaries, HARDGEM, STR18 / STR19
         * =============================================================================== */

        /// <summary>
        /// Reduces one slot to a number where the source's expression is made only of members
        /// of the evaluated set, and otherwise hands back the normalised expression text. A
        /// bitwise expression -- every flag field -- always comes back as text, because its
        /// flag names are the answer.
        /// </summary>
        private object EvaluateSlot(string text)
        {
            string normalised = ExpandExpressionMacros(Normalise(text));

            if (TryEvaluateTernary(normalised, out string? branch))
            {
                normalised = branch!;
            }

            if (TryEvaluateExpression(normalised, out long value)) return Number(value);
            return normalised;
        }

        private static object Number(long value) =>
            value >= int.MinValue && value <= int.MaxValue ? (int)value : value;

        /// <summary>
        /// Expands the expression-shaped macros a slot can contain: HARDGEM(n) from objects.c,
        /// whose value is the boolean stored in oc_tough and not a Mohs hardness, and attrib.h's
        /// STR18 / STR19.
        /// </summary>
        private string ExpandExpressionMacros(string text)
        {
            string current = text;

            for (int pass = 0; pass < MaxInlineExpansionPasses; pass++)
            {
                string expanded = ExpandExpressionMacrosOnce(current);
                if (string.Equals(expanded, current, StringComparison.Ordinal)) return current;
                current = expanded;
            }

            return current;
        }

        private string ExpandExpressionMacrosOnce(string text)
        {
            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"' || c == '\'')
                {
                    int end = SkipLiteral(text, i);
                    result.Append(text, i, end - i);
                    i = end - 1;
                    continue;
                }

                if (!IsIdentifierStart(c))
                {
                    result.Append(c);
                    continue;
                }

                int start = i;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;
                string identifier = text.Substring(start, i - start);

                int probe = i;
                while (probe < text.Length && text[probe] == ' ') probe++;
                if (probe >= text.Length || text[probe] != '(')
                {
                    result.Append(identifier);
                    i--;
                    continue;
                }

                string? template = ExpressionTemplateFor(identifier, out int parameterCount);
                if (template == null)
                {
                    result.Append(identifier);
                    i--;
                    continue;
                }

                int close = MatchingParen(text, probe);
                if (close < 0)
                {
                    result.Append(identifier);
                    i--;
                    continue;
                }

                var arguments = SplitTopLevel(text.Substring(probe + 1, close - probe - 1))
                    .Select(a => a.Trim())
                    .ToList();
                if (arguments.Count != parameterCount)
                {
                    result.Append(identifier);
                    i--;
                    continue;
                }

                string body = template;
                for (int a = 0; a < arguments.Count; a++)
                {
                    body = body.Replace("@" + (a + 1).ToString(CultureInfo.InvariantCulture), arguments[a], StringComparison.Ordinal);
                }
                result.Append('(').Append(body).Append(')');
                i = close;
            }

            return result.ToString();
        }

        private string? ExpressionTemplateFor(string name, out int parameterCount)
        {
            if (_builtinExpressionMacros.TryGetValue(name, out string? builtin))
            {
                parameterCount = 1;
                return builtin;
            }

            parameterCount = 0;
            if (!_defines.TryGetValue(name, out var define) || define.IsObjectLike) return null;
            if (define.Parameters.Count == 0 || define.Parameters.Count > 3) return null;
            if (define.Body.Contains('#', StringComparison.Ordinal)) return null;
            if (define.Body.Contains("OBJECT(", StringComparison.Ordinal)
                || define.Body.Contains("OBJ(", StringComparison.Ordinal)
                || define.Body.Contains("BITS(", StringComparison.Ordinal))
            {
                return null;
            }

            var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < define.Parameters.Count; i++)
            {
                placeholders[define.Parameters[i]] = "@" + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            parameterCount = define.Parameters.Count;
            return ReplaceIdentifiers(define.Body, placeholders);
        }

        private static int MatchingParen(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(text, i) - 1;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Chooses a ternary's branch when its condition reduces to a comparison of literals.
        /// Enum names are substituted from the objclass.h constant table first; a comparison of
        /// two names neither of which is in that table is decided by name equality.
        /// </summary>
        private bool TryEvaluateTernary(string text, out string? branch)
        {
            branch = null;
            string trimmed = StripRedundantParens(text.Trim());

            int question = -1;
            int colon = -1;
            int depth = 0;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(trimmed, i) - 1;
                    continue;
                }
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (depth == 0 && c == '?' && question < 0) question = i;
                else if (depth == 0 && c == ':' && question >= 0 && colon < 0) colon = i;
            }

            if (question < 0 || colon < 0) return false;

            string condition = trimmed.Substring(0, question);
            string ifTrue = trimmed.Substring(question + 1, colon - question - 1).Trim();
            string ifFalse = trimmed.Substring(colon + 1).Trim();

            if (!TryEvaluateCondition(condition, out bool result)) return false;

            branch = StripRedundantParens(result ? ifTrue : ifFalse);
            if (TryEvaluateTernary(branch, out string? nested)) branch = nested;
            return true;
        }

        private bool TryEvaluateCondition(string condition, out bool result)
        {
            result = false;

            string resolved = ReplaceKnownConstants(Normalise(condition));
            if (TryEvaluateExpression(resolved, out long value))
            {
                result = value != 0;
                return true;
            }

            resolved = ReduceNameComparisons(resolved);
            if (TryEvaluateExpression(resolved, out value))
            {
                result = value != 0;
                return true;
            }

            return false;
        }

        private string ReplaceKnownConstants(string text)
        {
            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    int end = SkipLiteral(text, i);
                    result.Append(text, i, end - i);
                    i = end - 1;
                    continue;
                }
                if (!IsIdentifierStart(c))
                {
                    result.Append(c);
                    continue;
                }

                int start = i;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;
                string identifier = text.Substring(start, i - start);
                result.Append(_constants.TryGetValue(identifier, out long value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : identifier);
                i--;
            }

            return result.ToString();
        }

        /// <summary>
        /// Decides a comparison of two enum names by name equality, for the case where the
        /// constant table is unavailable. Distinct names in one enum have distinct values.
        /// </summary>
        private static string ReduceNameComparisons(string text)
        {
            return Regex.Replace(
                text,
                @"\(?\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)?\s*(==|!=)\s*\(?\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)?",
                m =>
                {
                    bool equal = string.Equals(m.Groups[1].Value, m.Groups[3].Value, StringComparison.Ordinal);
                    bool wanted = m.Groups[2].Value == "==";
                    return equal == wanted ? "1" : "0";
                });
        }

        private static string StripRedundantParens(string text)
        {
            string current = text.Trim();
            while (current.Length >= 2 && current[0] == '(' && MatchingParen(current, 0) == current.Length - 1)
            {
                current = current.Substring(1, current.Length - 2).Trim();
            }
            return current;
        }

        /// <summary>
        /// Evaluates an expression built only from integer literals, parentheses, unary + - !,
        /// * / %, + -, the relational and equality operators, and &amp;&amp; / ||. Division is C
        /// integer division, truncating toward zero, and operands may be negative. Any
        /// remaining identifier, or any bitwise or shift operator, makes the whole expression
        /// unevaluable.
        /// </summary>
        private static bool TryEvaluateExpression(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            int position = 0;
            if (!TryParseLogicalOr(text, ref position, out long parsed)) return false;

            SkipSpaces(text, ref position);
            if (position != text.Length) return false;

            value = parsed;
            return true;
        }

        private static void SkipSpaces(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }

        private static bool Consume(string text, ref int position, string op)
        {
            SkipSpaces(text, ref position);
            if (position + op.Length > text.Length) return false;
            if (string.CompareOrdinal(text, position, op, 0, op.Length) != 0) return false;
            position += op.Length;
            return true;
        }

        private static bool Peek(string text, int position, string op)
        {
            SkipSpaces(text, ref position);
            if (position + op.Length > text.Length) return false;
            return string.CompareOrdinal(text, position, op, 0, op.Length) == 0;
        }

        private static bool TryParseLogicalOr(string text, ref int position, out long value)
        {
            if (!TryParseLogicalAnd(text, ref position, out value)) return false;
            while (Peek(text, position, "||"))
            {
                Consume(text, ref position, "||");
                if (!TryParseLogicalAnd(text, ref position, out long right)) return false;
                value = (value != 0 || right != 0) ? 1 : 0;
            }
            return true;
        }

        private static bool TryParseLogicalAnd(string text, ref int position, out long value)
        {
            if (!TryParseEquality(text, ref position, out value)) return false;
            while (Peek(text, position, "&&"))
            {
                Consume(text, ref position, "&&");
                if (!TryParseEquality(text, ref position, out long right)) return false;
                value = (value != 0 && right != 0) ? 1 : 0;
            }
            return true;
        }

        private static bool TryParseEquality(string text, ref int position, out long value)
        {
            if (!TryParseRelational(text, ref position, out value)) return false;
            while (Peek(text, position, "==") || Peek(text, position, "!="))
            {
                bool equals = Peek(text, position, "==");
                Consume(text, ref position, equals ? "==" : "!=");
                if (!TryParseRelational(text, ref position, out long right)) return false;
                value = (equals ? value == right : value != right) ? 1 : 0;
            }
            return true;
        }

        private static bool TryParseRelational(string text, ref int position, out long value)
        {
            if (!TryParseAdditive(text, ref position, out value)) return false;

            while (true)
            {
                string? op =
                    Peek(text, position, "<=") ? "<=" :
                    Peek(text, position, ">=") ? ">=" :
                    Peek(text, position, "<<") ? null :
                    Peek(text, position, ">>") ? null :
                    Peek(text, position, "<") ? "<" :
                    Peek(text, position, ">") ? ">" : null;

                if (op == null) return true;

                Consume(text, ref position, op);
                if (!TryParseAdditive(text, ref position, out long right)) return false;
                value = op switch
                {
                    "<" => value < right ? 1 : 0,
                    "<=" => value <= right ? 1 : 0,
                    ">" => value > right ? 1 : 0,
                    _ => value >= right ? 1 : 0
                };
            }
        }

        private static bool TryParseAdditive(string text, ref int position, out long value)
        {
            if (!TryParseMultiplicative(text, ref position, out value)) return false;

            while (true)
            {
                SkipSpaces(text, ref position);
                if (position >= text.Length) return true;

                char op = text[position];
                if (op != '+' && op != '-') return true;
                position++;

                if (!TryParseMultiplicative(text, ref position, out long right)) return false;
                value = op == '+' ? value + right : value - right;
            }
        }

        private static bool TryParseMultiplicative(string text, ref int position, out long value)
        {
            if (!TryParseUnary(text, ref position, out value)) return false;

            while (true)
            {
                SkipSpaces(text, ref position);
                if (position >= text.Length) return true;

                char op = text[position];
                if (op != '*' && op != '/' && op != '%') return true;
                position++;

                if (!TryParseUnary(text, ref position, out long right)) return false;
                if ((op == '/' || op == '%') && right == 0) return false;

                value = op switch
                {
                    '*' => value * right,
                    '/' => value / right,
                    _ => value % right
                };
            }
        }

        private static bool TryParseUnary(string text, ref int position, out long value)
        {
            value = 0;
            SkipSpaces(text, ref position);
            if (position >= text.Length) return false;

            char c = text[position];
            if (c == '-' || c == '+' || c == '!')
            {
                position++;
                if (!TryParseUnary(text, ref position, out long operand)) return false;
                value = c switch
                {
                    '-' => -operand,
                    '!' => operand == 0 ? 1 : 0,
                    _ => operand
                };
                return true;
            }

            return TryParsePrimary(text, ref position, out value);
        }

        private static bool TryParsePrimary(string text, ref int position, out long value)
        {
            value = 0;
            SkipSpaces(text, ref position);
            if (position >= text.Length) return false;

            if (text[position] == '(')
            {
                position++;
                if (!TryParseLogicalOr(text, ref position, out value)) return false;
                return Consume(text, ref position, ")");
            }

            if (!char.IsDigit(text[position])) return false;

            int start = position;
            if (text[position] == '0' && position + 1 < text.Length
                && (text[position + 1] == 'x' || text[position + 1] == 'X'))
            {
                position += 2;
                int hexStart = position;
                while (position < text.Length && Uri.IsHexDigit(text[position])) position++;
                if (position == hexStart) return false;
                if (!long.TryParse(text.AsSpan(hexStart, position - hexStart), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
            }
            else
            {
                while (position < text.Length && char.IsDigit(text[position])) position++;
                if (!long.TryParse(text.AsSpan(start, position - start), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
            }

            while (position < text.Length && (text[position] == 'U' || text[position] == 'u'
                || text[position] == 'L' || text[position] == 'l'))
            {
                position++;
            }

            return true;
        }

        /* ==================================================================================
         * Emission
         * =============================================================================== */

        private void Emit(
            ItemResolution resolution,
            CallSite site,
            List<string> slots,
            List<string> objValues,
            List<string> bitsValues,
            List<string> structuralSlots,
            List<string> structuralObjValues,
            List<string> structuralBitsValues)
        {
            resolution.ObjectSlots.AddRange(slots.Select(Normalise));
            resolution.FlagTokens.AddRange(resolution.ObjectSlots);
            resolution.FlagTokens.AddRange(objValues.Select(Normalise));
            resolution.FlagTokens.AddRange(bitsValues.Select(Normalise));

            var fields = resolution.Fields;
            var notes = resolution.Notes;

            string objectClass = Normalise(Slot(slots, "oc_class") ?? string.Empty);
            resolution.ObjectClass = objectClass;

            /* --- struct objdescr, from the OBJ block ------------------------------------ */
            fields["name"] = site.ItemName;
            AddDescriptorText(fields, objValues, "oc_descr", "description");
            AddDescriptorText(fields, objValues, "oc_content_name", "content_name");
            AddDescriptorText(fields, objValues, "oc_content_description", "content_description");
            AddDescriptorText(fields, objValues, "oc_item_description", "item_description");

            /* --- identity and generation ------------------------------------------------ */
            fields["object_class"] = objectClass;
            fields["source_macro"] = site.MacroName;
            fields["source_location"] = $"objects.c:{resolution.StartLine}-{resolution.EndLine}";
            fields["generation_probability"] = EvaluateSlot(Slot(slots, "oc_prob") ?? "0");
            fields["multigen_type"] = EvaluateSlot(Slot(slots, "oc_multigen_type") ?? "0");
            fields["delay"] = EvaluateSlot(Slot(slots, "oc_delay") ?? "0");
            fields["weight"] = EvaluateSlot(Slot(slots, "oc_weight") ?? "0");

            EmitCost(fields, site, slots, structuralSlots);
            EmitNutrition(fields, slots, structuralSlots);

            /* --- material, category, skill ---------------------------------------------- */
            fields["material"] = EvaluateSlot(BitsValue(bitsValues, "oc_material") ?? "0");
            fields["material_init_type"] = EvaluateSlot(BitsValue(bitsValues, "oc_material_init_type") ?? "0");
            fields[Key(objectClass, "subtyp", "subtype")] =
                EvaluateSlot(BitsValue(bitsValues, "oc_subtyp") ?? "0");
            fields["skill"] = EvaluateSlot(BitsValue(bitsValues, "oc_skill") ?? "0");

            EmitDirection(fields, notes, objectClass, bitsValues, structuralBitsValues, structuralSlots);

            /* --- damage, duration and effect triples ------------------------------------ */
            EmitTriples(fields, objectClass, slots);

            fields[Key(objectClass, "aflags", "attack_flags")] = EvaluateSlot(Slot(slots, "oc_aflags") ?? "0");
            fields[Key(objectClass, "aflags2", "attack_flags2")] = EvaluateSlot(Slot(slots, "oc_aflags2") ?? "0");
            fields[Key(objectClass, "critical_strike_percentage", "critical_strike_percentage")] =
                EvaluateSlot(Slot(slots, "oc_critical_strike_percentage") ?? "0");

            EmitToHitBonus(fields, notes, slots, structuralSlots, structuralBitsValues);

            fields[Key(objectClass, "mc_adjustment", "mc_adjustment")] =
                EvaluateSlot(Slot(slots, "oc_mc_adjustment") ?? "0");
            fields["fixed_damage_bonus"] = EvaluateSlot(Slot(slots, "oc_fixed_damage_bonus") ?? "0");
            fields["range"] = EvaluateSlot(Slot(slots, "oc_range") ?? "0");

            /* --- oc_oc1..oc_oc8, whose meaning depends on the object class -------------- */
            EmitOcSlots(fields, objectClass, site, slots, structuralSlots);

            /* --- conveyed properties and power flags ------------------------------------ */
            AddUnlessAbsent(fields, "conveyed_property", EvaluateSlot(Slot(slots, "oc_oprop") ?? "0"));
            AddUnlessAbsent(fields, "conveyed_property2", EvaluateSlot(Slot(slots, "oc_oprop2") ?? "0"));
            AddUnlessAbsent(fields, "conveyed_property3", EvaluateSlot(Slot(slots, "oc_oprop3") ?? "0"));
            fields["power_property_flags"] = EvaluateSlot(Slot(slots, "oc_pflags") ?? "0");

            /* --- presentation and miscellany -------------------------------------------- */
            fields["color"] = EvaluateSlot(Slot(slots, "oc_color") ?? "0");
            fields["soundset"] = EvaluateSlot(Slot(slots, "oc_soundset") ?? "0");
            EmitDirSubtype(fields, notes, objectClass, slots);
            fields["material_components"] = EvaluateSlot(Slot(slots, "oc_material_components") ?? "0");
            fields["item_cooldown"] = EvaluateSlot(Slot(slots, "oc_item_cooldown") ?? "0");
            fields["special_quality"] = EvaluateSlot(Slot(slots, "oc_special_quality") ?? "0");

            /* --- the BITS booleans ------------------------------------------------------ */
            fields["name_known"] = EvaluateSlot(BitsValue(bitsValues, "oc_name_known") ?? "0");
            fields["merge"] = EvaluateSlot(BitsValue(bitsValues, "oc_merge") ?? "0");
            fields["uses_known"] = EvaluateSlot(BitsValue(bitsValues, "oc_uses_known") ?? "0");
            fields["pre_discovered"] = EvaluateSlot(BitsValue(bitsValues, "oc_pre_discovered") ?? "0");
            fields["magic"] = EvaluateSlot(BitsValue(bitsValues, "oc_magic") ?? "0");
            fields["enchantment_type"] = EvaluateSlot(BitsValue(bitsValues, "oc_enchantable") ?? "0");
            fields["charged"] = EvaluateSlot(BitsValue(bitsValues, "oc_charged") ?? "0");
            fields["recharging"] = EvaluateSlot(BitsValue(bitsValues, "oc_recharging") ?? "0");
            fields["unique"] = EvaluateSlot(BitsValue(bitsValues, "oc_unique") ?? "0");
            fields["nowish"] = EvaluateSlot(BitsValue(bitsValues, "oc_nowish") ?? "0");
            fields[Key(objectClass, "big", "big")] =
                EvaluateSlot(BitsValue(bitsValues, "oc_big") ?? "0");
            fields["tough"] = EvaluateSlot(BitsValue(bitsValues, "oc_tough") ?? "0");

            /* --- tile and descriptor data ----------------------------------------------- */
            fields["tile_floor_height"] = EvaluateSlot(ObjValue(objValues, "oc_tile_floor_height") ?? "0");
            fields["descriptor_flags"] = EvaluateSlot(ObjValue(objValues, "oc_descr_flags") ?? "0");

            /* --- permissions and object flags ------------------------------------------- */
            fields["power_permissions"] = EvaluateSlot(Slot(slots, "oc_power_permissions") ?? "0");
            fields["target_permissions"] = EvaluateSlot(Slot(slots, "oc_target_permissions") ?? "0");
            for (int f = 1; f <= 6; f++)
            {
                string field = f == 1 ? "oc_flags" : $"oc_flags{f}";
                string key = f == 1 ? "object_flags" : $"object_flags{f}";
                fields[key] = EvaluateSlot(Slot(slots, field) ?? "0");
            }

            EmitExceptionality(fields, notes, objectClass, slots);
            NoteDiscardedArguments(notes, site, structuralSlots, structuralObjValues, structuralBitsValues);

            resolution.Message =
                "These are the object class values. A particular object's numbers are further "
                + "modified at run time by its material, enchantment, exceptionality and erosion "
                + "(weapon.c:4867-4947, hack.h:681-686, spell.c:5159-5178).";

            if (notes.Count > 0) fields["notes"] = notes.ToList();
        }

        private static void AddUnlessAbsent(Dictionary<string, object> fields, string key, object value)
        {
            if (value is string text && (text == "NO_POWER" || text == "0" || text == "None")) return;
            if (value is int i && i == 0) return;
            if (value is long l && l == 0) return;
            fields[key] = value;
        }

        private void AddDescriptorText(
            Dictionary<string, object> fields, List<string> objValues, string objdescrField, string key)
        {
            string? raw = ObjValue(objValues, objdescrField);
            if (raw == null) return;

            string normalised = Normalise(raw);
            if (normalised.Length == 0 || normalised == "None" || normalised == "NULL"
                || normalised == "(char *) 0" || normalised == "0")
            {
                return;
            }

            fields[key] = UnquoteLiteral(normalised);
        }

        private static string UnquoteLiteral(string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '"') return trimmed;

            /* Adjacent literals stay one token, and a macro call may sit between two of them;
               the literals are unescaped and joined, and anything between them is kept as the
               source wrote it. */
            var joined = new StringBuilder();
            int cursor = 0;
            while (cursor < trimmed.Length)
            {
                int quote = trimmed.IndexOf('"', cursor);
                if (quote < 0)
                {
                    joined.Append(trimmed.Substring(cursor).Trim());
                    break;
                }

                joined.Append(trimmed.Substring(cursor, quote - cursor).Trim());
                int end = SkipLiteral(trimmed, quote);
                joined.Append(trimmed, quote + 1, end - quote - 2);
                cursor = end;
            }

            return joined.ToString()
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        private void EmitCost(
            Dictionary<string, object> fields, CallSite site, List<string> slots, List<string> structuralSlots)
        {
            fields["cost"] = EvaluateSlot(Slot(slots, "oc_cost") ?? "0");

            string? structural = Slot(structuralSlots, "oc_cost");
            if (structural == null) return;

            string formula = Normalise(structural);
            if (IsParameterOf(site.MacroName, formula)) return;

            /* No cost argument reaches this family; the macro computes or hardcodes it --
               FOOD's nutrition / 20 + 5 (objects.c:2958), SPELL's level formula (:3552),
               AMULET's flat 150 (:2031). */
            fields["cost_is_derived"] = true;
            fields["cost_formula"] = formula;
        }

        private void EmitNutrition(
            Dictionary<string, object> fields, List<string> slots, List<string> structuralSlots)
        {
            string? nutrition = Slot(structuralSlots, "oc_nutrition");
            string? weight = Slot(structuralSlots, "oc_weight");

            /* Eleven wrappers pass their weight argument into the nutrition slot as well, so
               oc_nutrition carries no food value for those families. */
            if (nutrition != null && weight != null
                && string.Equals(Normalise(nutrition), Normalise(weight), StringComparison.Ordinal))
            {
                fields["nutrition_is_weight_alias"] = true;
                return;
            }

            fields["nutrition"] = EvaluateSlot(Slot(slots, "oc_nutrition") ?? "0");
        }

        private void EmitDirection(
            Dictionary<string, object> fields,
            List<string> notes,
            string objectClass,
            List<string> bitsValues,
            List<string> structuralBitsValues,
            List<string> structuralSlots)
        {
            object value = EvaluateSlot(BitsValue(bitsValues, "oc_dir") ?? "0");

            if (IsSharedParameter(structuralBitsValues.Count > 0
                    ? BitsValue(structuralBitsValues, "oc_dir")
                    : null,
                Slot(structuralSlots, "oc_hitbonus")))
            {
                fields["damage_class"] = value;
                return;
            }

            string key = Key(objectClass, "dir", "dir");
            fields[key] = value;
            if (key == "dir" && !IsZero(value))
            {
                notes.Add("oc_dir is the targeting mode for spell-like items and the physical "
                    + "damage class for weapon-like items (objclass.h:443-483, 501-503); this "
                    + "family uses one of the two and the source does not say which.");
            }
        }

        private void EmitTriples(Dictionary<string, object> fields, string objectClass, List<string> slots)
        {
            bool always = objectClass == "WEAPON_CLASS";

            fields[Key(objectClass, "damagetype", "damage_type")] =
                EvaluateSlot(Slot(slots, "oc_damagetype") ?? "0");

            EmitTriple(fields, objectClass, slots, always,
                "oc_wsdice", "oc_wsdam", "oc_wsdmgplus", "small_damage");
            EmitTriple(fields, objectClass, slots, always,
                "oc_wldice", "oc_wldam", "oc_wldmgplus", "large_damage");

            fields[Key(objectClass, "extra_damagetype", "extra_damage_type")] =
                EvaluateSlot(Slot(slots, "oc_extra_damagetype") ?? "0");

            EmitTriple(fields, objectClass, slots, always,
                "oc_wedice", "oc_wedam", "oc_wedmgplus", "extra_damage");
        }

        private void EmitTriple(
            Dictionary<string, object> fields,
            string objectClass,
            List<string> slots,
            bool always,
            string diceField,
            string diesizeField,
            string plusField,
            string genericPrefix)
        {
            object dice = EvaluateSlot(Slot(slots, diceField) ?? "0");
            object diesize = EvaluateSlot(Slot(slots, diesizeField) ?? "0");
            object plus = EvaluateSlot(Slot(slots, plusField) ?? "0");

            if (!always && IsZero(dice) && IsZero(diesize) && IsZero(plus)) return;

            string diceKey = ObjectClassFieldMap.GetFieldName(objectClass, Stripped(diceField));
            string diesizeKey = ObjectClassFieldMap.GetFieldName(objectClass, Stripped(diesizeField));
            string plusKey = ObjectClassFieldMap.GetFieldName(objectClass, Stripped(plusField));

            bool renamed = diceKey != Stripped(diceField);
            if (!renamed)
            {
                diceKey = genericPrefix + "_dice";
                diesizeKey = genericPrefix + "_diesize";
                plusKey = genericPrefix + "_plus";
            }

            fields[diceKey] = dice;
            fields[diesizeKey] = diesize;
            fields[plusKey] = plus;

            string? notation = DiceNotation(dice, diesize, plus);
            if (notation != null)
            {
                string notationKey = renamed
                    ? TrimTrailingComponent(diceKey) + "_notation"
                    : genericPrefix;
                fields[notationKey] = notation;
            }

            /* For spellbooks the large triple is duration unless
               S1_LDMG_IS_PER_LEVEL_DMG_INCREASE selects a per-level damage increase; the two
               meanings share the same three fields (objclass.h:794-805, 631). */
            if (objectClass == "SPBOOK_CLASS" && diceField == "oc_wldice")
            {
                string spellFlags = Normalise(Slot(slots, "oc_aflags") ?? string.Empty);
                bool perLevel = spellFlags.Contains("S1_LDMG_IS_PER_LEVEL_DMG_INCREASE", StringComparison.Ordinal);
                bool readable = Regex.IsMatch(spellFlags, @"^[A-Z0-9_ |()]*$");

                if (perLevel)
                {
                    Rename(fields, "spell_dur_dice", "spell_per_level_dice");
                    Rename(fields, "spell_dur_diesize", "spell_per_level_diesize");
                    Rename(fields, "spell_dur_plus", "spell_per_level_plus");
                    Rename(fields, "spell_dur_notation", "spell_per_level_notation");
                }
                else if (!readable)
                {
                    fields["spell_per_level_dice"] = dice;
                    fields["spell_per_level_diesize"] = diesize;
                    fields["spell_per_level_plus"] = plus;
                    fields["spell_duration_or_per_level_unresolved"] = true;
                }
            }
        }

        private static void Rename(Dictionary<string, object> fields, string from, string to)
        {
            if (!fields.TryGetValue(from, out object? value) || value == null) return;
            fields.Remove(from);
            fields[to] = value;
        }

        private static string TrimTrailingComponent(string key)
        {
            int underscore = key.LastIndexOf('_');
            return underscore > 0 ? key.Substring(0, underscore) : key;
        }

        private static string? DiceNotation(object dice, object diesize, object plus)
        {
            if (dice is not int n || diesize is not int m || plus is not int p) return null;

            if (n <= 0 || m <= 0)
            {
                return p != 0 ? p.ToString(CultureInfo.InvariantCulture) : null;
            }

            string notation = $"{n}d{m}";
            if (p > 0) notation += "+" + p.ToString(CultureInfo.InvariantCulture);
            else if (p < 0) notation += p.ToString(CultureInfo.InvariantCulture);
            return notation;
        }

        private void EmitToHitBonus(
            Dictionary<string, object> fields,
            List<string> notes,
            List<string> slots,
            List<string> structuralSlots,
            List<string> structuralBitsValues)
        {
            object value = EvaluateSlot(Slot(slots, "oc_hitbonus") ?? "0");
            fields["to_hit_bonus"] = value;

            string? structuralHit = Slot(structuralSlots, "oc_hitbonus");
            string? structuralDir = structuralBitsValues.Count > 0
                ? BitsValue(structuralBitsValues, "oc_dir")
                : null;

            if (IsSharedParameter(structuralDir, structuralHit))
            {
                notes.Add("This family passes one argument into both BITS's damage-class slot "
                    + "and oc_hitbonus (objects.c:2402-2412), so to_hit_bonus repeats the "
                    + "physical damage class rather than stating a to-hit bonus.");
            }
        }

        private void EmitOcSlots(
            Dictionary<string, object> fields,
            string objectClass,
            CallSite site,
            List<string> slots,
            List<string> structuralSlots)
        {
            var names = ObjectClassFieldMap.GetOcSlotNames(objectClass);
            bool general = ObjectClassFieldMap.UsesGeneralOcSlots(objectClass);
            bool alwaysEmit = objectClass == "WEAPON_CLASS" || objectClass == "ARMOR_CLASS";

            for (int i = 0; i < names.Count; i++)
            {
                string? name = names[i];
                if (name == null) continue;

                string field = $"oc_oc{i + 1}";
                object value = EvaluateSlot(Slot(slots, field) ?? "0");
                if (general && !alwaysEmit && IsZero(value)) continue;

                fields[name] = value;

                if (name == "ac_bonus")
                {
                    EmitBaseAc(fields, site, structuralSlots, value);
                }
                else if (name == "spell_casting_penalty" && value is int penalty)
                {
                    fields["spell_casting_penalty_percent"] =
                        Number(penalty * ArmorSpellCastingPenaltyMultiplier);
                }
            }
        }

        /// <summary>
        /// Emits the call site's own AC alongside the stored bonus, but only where the
        /// expansion actually inverts it. Armour stores 10 - ac (objects.c:1005) while
        /// GENERAL_CHARGED_WEAPON, WEAPONSHIELD, WEAPONBOOTS and WEAPONGLOVES store their acbon
        /// argument raw (objects.c:1060), and WEAPONBOOTS is ARMOR_CLASS, so the object class
        /// does not decide it -- the presence of the 10 - x form in the expansion does.
        /// </summary>
        private void EmitBaseAc(
            Dictionary<string, object> fields,
            CallSite site,
            List<string> structuralSlots,
            object storedBonus)
        {
            string? structural = Slot(structuralSlots, "oc_oc1");
            if (structural == null) return;

            var inversion = _acInversionRegex.Match(Normalise(structural));
            if (!inversion.Success) return;

            if (_defines.TryGetValue(site.MacroName, out var define))
            {
                int index = define.Parameters.IndexOf(inversion.Groups[1].Value);
                if (index >= 0 && index < site.Arguments.Count)
                {
                    fields["base_ac"] = EvaluateSlot(site.Arguments[index]);
                    return;
                }
            }

            if (storedBonus is int bonus) fields["base_ac"] = Number(10 - bonus);
        }

        private void EmitDirSubtype(
            Dictionary<string, object> fields, List<string> notes, string objectClass, List<string> slots)
        {
            /* GENERAL_PROJECTILE stores a launcher skill here and GEM stores a weapon type;
               nothing reads either, because ammunition is matched by the sign of oc_skill
               (obj.h:441). Reporting the raw value would name a mechanic that does not exist. */
            if (objectClass == "WEAPON_CLASS" || objectClass == "GEM_CLASS")
            {
                if (!IsZero(EvaluateSlot(Slot(slots, "oc_dir_subtype") ?? "0")))
                {
                    notes.Add("oc_dir_subtype is omitted for this object class: the value "
                        + "stored there has no reader, and launcher matching goes by the sign "
                        + "of oc_skill (obj.h:441).");
                }
                return;
            }

            fields["dir_subtype"] = EvaluateSlot(Slot(slots, "oc_dir_subtype") ?? "0");
        }

        private void EmitExceptionality(
            Dictionary<string, object> fields, List<string> notes, string objectClass, List<string> slots)
        {
            string flags4 = Normalise(Slot(slots, "oc_flags4") ?? string.Empty);
            string flags6 = Normalise(Slot(slots, "oc_flags6") ?? string.Empty);

            bool forbidden = flags4.Contains("O4_NON_EXCEPTIONAL", StringComparison.Ordinal);
            bool allowed = !forbidden && flags4.Contains("O4_CAN_HAVE_EXCEPTIONALITY", StringComparison.Ordinal);

            fields["exceptionality_eligible"] = allowed;

            if (flags6.Contains("O6_NORMALLY_NON_EXCEPTIONAL", StringComparison.Ordinal))
            {
                fields["exceptionality_normally_absent"] = true;
            }

            if (!allowed) return;

            if (objectClass == "WEAPON_CLASS")
            {
                notes.Add("Exceptionality multiplies the number of damage rolls, not the die "
                    + "size (weapon.c:489-517): Normal 1, Exceptional 2, Elite 3, Celestial, "
                    + "Primordial and Infernal 4 (weapon.c:4669-4681). An Elite 1d8 weapon "
                    + "rolls 3d8.");
            }
            else
            {
                notes.Add("Exceptionality adds an AC bonus rather than multiplying damage "
                    + "rolls: the multiplier is 4 for a suit, 3 for a shield and 2 otherwise, "
                    + "times the exceptionality level (o_init.c:1837-1852).");
            }
        }

        /// <summary>
        /// Reports call-site arguments the expansion never uses -- WEPTOOL's splcastpen, for
        /// instance -- so a reader is not left assuming every argument reached a field.
        /// </summary>
        private void NoteDiscardedArguments(
            List<string> notes,
            CallSite site,
            List<string> structuralSlots,
            List<string> structuralObjValues,
            List<string> structuralBitsValues)
        {
            if (!_defines.TryGetValue(site.MacroName, out var define)) return;

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string text in structuralSlots.Concat(structuralObjValues).Concat(structuralBitsValues))
            {
                CollectIdentifiers(text, used);
            }

            var discarded = define.Parameters.Where(p => !used.Contains(p)).ToList();
            if (discarded.Count == 0) return;

            notes.Add($"{site.MacroName} discards these arguments; they reach no struct objclass "
                + $"field: {string.Join(", ", discarded)}.");
        }

        private static void CollectIdentifiers(string text, HashSet<string> into)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(text, i) - 1;
                    continue;
                }
                if (!IsIdentifierStart(c)) continue;

                int start = i;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;
                into.Add(text.Substring(start, i - start));
                i--;
            }
        }

        /* ==================================================================================
         * Small helpers
         * =============================================================================== */

        private static string Stripped(string objclassField) =>
            objclassField.StartsWith("oc_", StringComparison.Ordinal)
                ? objclassField.Substring(3)
                : objclassField;

        private static string? Slot(List<string> slots, string objclassField)
        {
            int index = ObjectClassFieldMap.ObjectSlotOf(objclassField);
            return index >= 0 && index < slots.Count ? slots[index] : null;
        }

        private static string? BitsValue(List<string> values, string objclassField)
        {
            int index = ObjectClassFieldMap.BitsSlotOf(objclassField);
            return index >= 0 && index < values.Count ? values[index] : null;
        }

        private static string? ObjValue(List<string> values, string objdescrField)
        {
            int index = ObjectClassFieldMap.ObjDescriptorSlotOf(objdescrField);
            return index >= 0 && index < values.Count ? values[index] : null;
        }

        /// <summary>
        /// The key a field is emitted under: the name the object class gives it (Table 2), or
        /// <paramref name="readable"/> where the class does not rename it.
        /// </summary>
        private static string Key(string objectClass, string strippedField, string readable)
        {
            string alias = ObjectClassFieldMap.GetFieldName(objectClass, strippedField);
            return alias == strippedField ? readable : alias;
        }

        private static bool IsZero(object? value) =>
            (value is int i && i == 0) || (value is long l && l == 0);

        private bool IsParameterOf(string macroName, string text)
        {
            if (!_defines.TryGetValue(macroName, out var define)) return false;
            return define.Parameters.Contains(text, StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether two structural slots hold the same single identifier, which means the source
        /// routed one argument to both fields.
        /// </summary>
        private static bool IsSharedParameter(string? left, string? right)
        {
            if (left == null || right == null) return false;

            string a = Normalise(left);
            string b = Normalise(right);
            if (a.Length == 0 || !string.Equals(a, b, StringComparison.Ordinal)) return false;

            return Regex.IsMatch(a, @"^[A-Za-z_][A-Za-z0-9_]*$") && !Regex.IsMatch(a, @"^[A-Z0-9_]+$");
        }
    }
}
