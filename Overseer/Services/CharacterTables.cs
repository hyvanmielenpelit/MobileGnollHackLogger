using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Overseer.Services.Tools;

namespace Overseer.Services
{
    /// <summary>An intrinsic a role or race gains at an experience level (<c>struct innate</c> in src/attrib.c).</summary>
    public sealed class CharacterAbility
    {
        public int Level { get; init; }

        /// <summary>The property constant, e.g. <c>CORPSE_PROPERTY_APPRAISAL</c>.</summary>
        public string Property { get; init; } = string.Empty;

        /// <summary>The property's <c>prop_name</c> from src/prop.c, or the constant when it has none.</summary>
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>One <c>roles[]</c> initializer of src/role.c.</summary>
    public sealed class CharacterRole
    {
        public string Name { get; init; } = string.Empty;
        public string? FemaleName { get; init; }
        public IReadOnlyList<string> Traits { get; init; } = Array.Empty<string>();

        /// <summary>The role's <c>PM_</c> constant (<c>monsternum</c>).</summary>
        public string? MonsterConstant { get; init; }

        /// <summary>The <c>questarti</c> <c>ART_</c> constant.</summary>
        public string? QuestArtifactConstant { get; init; }

        /// <summary>The quest artifact's name from include/artilist.h, when its constant resolves to one.</summary>
        public string? QuestArtifactName { get; init; }

        /// <summary>The <c>allow</c> mask's tokens, <c>M2_</c> read as <c>MH_</c>.</summary>
        public IReadOnlySet<string> Allow { get; init; } = new HashSet<string>();

        public IReadOnlyList<CharacterAbility> Abilities { get; set; } = Array.Empty<CharacterAbility>();

        /// <summary>The initializer as the source writes it, capped at <see cref="CharacterTables.RawDefinitionMaxChars"/>.</summary>
        public string RawDefinition { get; init; } = string.Empty;

        /// <summary>Why the row's fields could not be read; null when they were.</summary>
        public string? ParseError { get; init; }

        /// <summary>The names the role is looked up by: male and female forms and their plurals.</summary>
        public IEnumerable<string> LookupNames()
        {
            foreach (string? form in new[] { Name, FemaleName })
            {
                if (string.IsNullOrEmpty(form)) continue;
                yield return form;
                foreach (string plural in CharacterTables.Plurals(form)) yield return plural;
            }
        }
    }

    /// <summary>One <c>races[]</c> initializer of src/role.c.</summary>
    public sealed class CharacterRace
    {
        public string Noun { get; init; } = string.Empty;
        public string? Adjective { get; init; }
        public string? Collective { get; init; }
        public string? IndividualMale { get; init; }
        public string? IndividualFemale { get; init; }
        public IReadOnlyList<string> Traits { get; init; } = Array.Empty<string>();

        /// <summary>The race's <c>PM_</c> constant (<c>monsternum</c>).</summary>
        public string? MonsterConstant { get; init; }

        /// <summary>The <c>allow</c> mask's tokens: genders and alignments only.</summary>
        public IReadOnlySet<string> Allow { get; init; } = new HashSet<string>();

        public IReadOnlySet<string> SelfMask { get; init; } = new HashSet<string>();
        public IReadOnlySet<string> LoveMask { get; init; } = new HashSet<string>();
        public IReadOnlySet<string> HateMask { get; init; } = new HashSet<string>();

        public IReadOnlyList<CharacterAbility> Abilities { get; set; } = Array.Empty<CharacterAbility>();

        /// <summary>The initializer as the source writes it, capped at <see cref="CharacterTables.RawDefinitionMaxChars"/>.</summary>
        public string RawDefinition { get; init; } = string.Empty;

        /// <summary>Why the row's fields could not be read; null when they were.</summary>
        public string? ParseError { get; init; }

        /// <summary>The names the race is looked up by: noun, adjective, collective, the individual forms, and plurals.</summary>
        public IEnumerable<string> LookupNames()
        {
            foreach (string? form in new[] { Noun, IndividualMale, IndividualFemale })
            {
                if (string.IsNullOrEmpty(form)) continue;
                yield return form;
                foreach (string plural in CharacterTables.Plurals(form)) yield return plural;
            }
            if (!string.IsNullOrEmpty(Adjective)) yield return Adjective;
            if (!string.IsNullOrEmpty(Collective)) yield return Collective;
        }
    }

    /// <summary>
    /// The GnollHack character tables: the roles and races of src/role.c, their level-gated
    /// intrinsics from src/attrib.c (the <c>*_abil[]</c> tables, mapped through <c>role_abil()</c>
    /// and <c>race_abil()</c>) and the property names of src/prop.c's <c>propertynames[]</c>.
    /// Parsed from the indexed lines with comments, <c>#if 0</c> regions and preprocessor lines
    /// removed. Never throws: a table that cannot be read is listed in <see cref="Problems"/>, and a
    /// row that cannot be read keeps its raw text and its <see cref="CharacterRole.ParseError"/>.
    /// </summary>
    public sealed class CharacterTables
    {
        public const int RawDefinitionMaxChars = 1200;

        public IReadOnlyList<CharacterRole> Roles { get; }
        public IReadOnlyList<CharacterRace> Races { get; }

        /// <summary>Property constant to <c>prop_name</c>.</summary>
        public IReadOnlyDictionary<string, string> PropertyNames { get; }

        /// <summary>Tables or files that could not be read, one sentence each.</summary>
        public IReadOnlyList<string> Problems { get; }

        private CharacterTables(IReadOnlyList<CharacterRole> roles, IReadOnlyList<CharacterRace> races,
            IReadOnlyDictionary<string, string> propertyNames, IReadOnlyList<string> problems)
        {
            Roles = roles;
            Races = races;
            PropertyNames = propertyNames;
            Problems = problems;
        }

        /// <summary>The role whose name, female name or plural equals <paramref name="query"/>, case-insensitively.</summary>
        public CharacterRole? FindRole(string? query)
        {
            string q = query?.Trim() ?? string.Empty;
            if (q.Length == 0) return null;
            return Roles.FirstOrDefault(r => r.LookupNames().Any(n => string.Equals(n, q, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The race whose noun, adjective, collective, individual form or plural equals <paramref name="query"/>, case-insensitively.</summary>
        public CharacterRace? FindRace(string? query)
        {
            string q = query?.Trim() ?? string.Empty;
            if (q.Length == 0) return null;
            return Races.FirstOrDefault(r => r.LookupNames().Any(n => string.Equals(n, q, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The roles whose <c>allow</c> race bits meet <paramref name="race"/>'s self mask, in table order.</summary>
        public IReadOnlyList<CharacterRole> RolesAllowing(CharacterRace race)
            => Roles.Where(r => r.ParseError == null && r.Allow.Overlaps(race.SelfMask)).ToList();

        /// <summary>The races whose self mask meets <paramref name="role"/>'s <c>allow</c> race bits, in table order.</summary>
        public IReadOnlyList<CharacterRace> RacesAllowedBy(CharacterRole role)
            => Races.Where(r => r.ParseError == null && r.SelfMask.Count > 0 && role.Allow.Overlaps(r.SelfMask)).ToList();

        /// <summary>
        /// <paramref name="mask"/> as race nouns: each token matched against the races' self masks
        /// (<c>MH_x</c> equals <c>M2_x</c>), a token no race carries kept as written.
        /// </summary>
        public IReadOnlyList<string> RaceNouns(IReadOnlySet<string> mask)
        {
            var nouns = new List<string>();
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var race in Races)
            {
                if (race.ParseError != null || !race.SelfMask.Overlaps(mask)) continue;
                nouns.Add(race.Noun);
                matched.UnionWith(race.SelfMask);
            }
            nouns.AddRange(mask.Where(t => !matched.Contains(t)).OrderBy(t => t, StringComparer.Ordinal));
            return nouns;
        }

        /// <summary>The alignments in <paramref name="allow"/>, lawful, neutral, chaotic order.</summary>
        public static IReadOnlyList<string> Alignments(IReadOnlySet<string> allow)
        {
            var result = new List<string>();
            if (allow.Contains("ROLE_LAWFUL") || allow.Contains("AM_LAWFUL")) result.Add("lawful");
            if (allow.Contains("ROLE_NEUTRAL") || allow.Contains("AM_NEUTRAL")) result.Add("neutral");
            if (allow.Contains("ROLE_CHAOTIC") || allow.Contains("AM_CHAOTIC")) result.Add("chaotic");
            return result;
        }

        /// <summary>English plurals of a name: <c>-man</c> (not <c>human</c>) to <c>-men</c>, sibilants take <c>-es</c>, <c>-f</c> both <c>-ves</c> and <c>-fs</c>, <c>-y</c> after a consonant <c>-ies</c>, otherwise <c>-s</c>.</summary>
        internal static IEnumerable<string> Plurals(string name)
        {
            if (string.IsNullOrEmpty(name)) yield break;
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith("man", StringComparison.Ordinal) && !lower.EndsWith("human", StringComparison.Ordinal))
            {
                yield return name.Substring(0, name.Length - 3) + "men";
            }
            else if (lower.EndsWith("s", StringComparison.Ordinal) || lower.EndsWith("x", StringComparison.Ordinal)
                || lower.EndsWith("sh", StringComparison.Ordinal) || lower.EndsWith("ch", StringComparison.Ordinal))
            {
                yield return name + "es";
            }
            else if (lower.EndsWith("f", StringComparison.Ordinal))
            {
                yield return name.Substring(0, name.Length - 1) + "ves";
                yield return name + "s";
            }
            else if (lower.Length > 1 && lower.EndsWith("y", StringComparison.Ordinal) && !"aeiou".Contains(lower[^2]))
            {
                yield return name.Substring(0, name.Length - 1) + "ies";
            }
            else
            {
                yield return name + "s";
            }
        }

        /* --- Parsing ------------------------------------------------------------------- */

        private static readonly Regex RolesTableRegex = new(@"\broles\s*\[\s*\]\s*=\s*\{", RegexOptions.Compiled);
        private static readonly Regex RacesTableRegex = new(@"\braces\s*\[\s*\]\s*=\s*\{", RegexOptions.Compiled);
        private static readonly Regex AbilityTableRegex = new(@"\b([A-Za-z_]\w*_abil)\s*\[\s*\]\s*=\s*\{", RegexOptions.Compiled);
        private static readonly Regex PropertyNamesTableRegex = new(@"\bpropertynames\s*\[\s*\]\s*=\s*\{", RegexOptions.Compiled);
        private static readonly Regex RoleAbilDefinitionRegex = new(@"^role_abil\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RaceAbilDefinitionRegex = new(@"^race_abil\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex CaseAssignmentRegex = new(@"\bcase\s+(PM_\w+)\s*:\s*[A-Za-z_]\w*\s*=\s*([A-Za-z_]\w*)\s*;", RegexOptions.Compiled);
        private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);
        private static readonly Regex ArtifactNameRegex = new(@"^\s*(?:GENERAL_ARTIFACT|A)\(\s*""([^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// Parses the tables. Any of the inputs may be null: roles and races need
        /// <paramref name="roleLines"/>; abilities need <paramref name="attribLines"/>; ability names
        /// fall back to the property constant without <paramref name="propLines"/>; quest artifact
        /// names come from <paramref name="artilistLines"/> when given.
        /// </summary>
        public static CharacterTables Parse(string[]? roleLines, string[]? attribLines, string[]? propLines, string[]? artilistLines = null)
        {
            var problems = new List<string>();
            var roles = new List<CharacterRole>();
            var races = new List<CharacterRace>();
            var propertyNames = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                if (propLines != null) ParsePropertyNames(propLines, propertyNames, problems);
                else problems.Add("src/prop.c is not in the index; abilities are named by their property constants.");
            }
            catch (Exception ex)
            {
                problems.Add($"src/prop.c propertynames[] could not be read: {ex.Message}");
            }

            var artifactNames = ArtifactNamesByConstant(artilistLines);

            try
            {
                if (roleLines != null)
                {
                    var clean = Clean(roleLines);
                    ParseRoles(roleLines, clean, artifactNames, roles, problems);
                    ParseRaces(roleLines, clean, races, problems);
                }
                else
                {
                    problems.Add("src/role.c is not in the index.");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"src/role.c could not be read: {ex.Message}");
            }

            try
            {
                if (attribLines != null) AttachAbilities(attribLines, propertyNames, roles, races, problems);
                else problems.Add("src/attrib.c is not in the index; abilities_by_level is unavailable.");
            }
            catch (Exception ex)
            {
                problems.Add($"src/attrib.c could not be read: {ex.Message}");
            }

            return new CharacterTables(roles, races, propertyNames, problems);
        }

        /// <summary>The source text with <c>#if 0</c> regions, preprocessor lines and comments blanked, positions kept.</summary>
        private sealed class CleanText
        {
            public CleanText(string text, int[] lineStarts)
            {
                Text = text;
                LineStarts = lineStarts;
            }

            public string Text { get; }
            public int[] LineStarts { get; }

            /// <summary>The 0-based line holding <paramref name="offset"/>.</summary>
            public int LineOf(int offset)
            {
                int index = Array.BinarySearch(LineStarts, offset);
                return index >= 0 ? index : ~index - 1;
            }
        }

        private static CleanText Clean(string[] lines)
        {
            var blanked = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                blanked[i] = line.TrimStart().StartsWith('#') ? new string(' ', line.Length) : line;
            }
            foreach (var (start, end) in SourceCompiledOutNote.FindRanges(lines, 1, lines.Length))
            {
                for (int n = start; n <= end; n++) blanked[n - 1] = new string(' ', blanked[n - 1].Length);
            }

            var lineStarts = new int[lines.Length];
            var sb = new StringBuilder();
            for (int i = 0; i < blanked.Length; i++)
            {
                lineStarts[i] = sb.Length;
                sb.Append(blanked[i]);
                sb.Append('\n');
            }

            return new CleanText(StripComments(sb.ToString()), lineStarts);
        }

        /// <summary>Blanks <c>//</c> and <c>/* */</c> comments to spaces, keeping line breaks and string and character literals.</summary>
        private static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? text.Length : end + 2;
                    for (int k = i; k < stop; k++) sb.Append(text[k] == '\n' ? '\n' : ' ');
                    i = stop;
                    continue;
                }
                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        sb.Append(' ');
                        i++;
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int end = SkipLiteral(text, i);
                    sb.Append(text, i, end - i);
                    i = end;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>The index just past the literal opening at <paramref name="start"/>; a literal never spans a line break.</summary>
        private static int SkipLiteral(string text, int start)
        {
            char quote = text[start];
            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (text[i] == quote) return i + 1;
                if (text[i] == '\n') return i;
            }
            return text.Length;
        }

        /// <summary>The index of the bracket closing the one at <paramref name="open"/>, or -1.</summary>
        private static int MatchingClose(string text, int open)
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
                if (c == '{' || c == '(' || c == '[') depth++;
                else if (c == '}' || c == ')' || c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>The top-level comma-separated elements of <paramref name="text"/>[<paramref name="start"/>..<paramref name="end"/>), trimmed, each with its start offset; empty elements dropped.</summary>
        private static List<(string Text, int Start)> SplitTopLevel(string text, int start, int end)
        {
            var parts = new List<(string Text, int Start)>();
            int depth = 0;
            int partStart = start;

            void Add(int stop)
            {
                int s = partStart;
                while (s < stop && char.IsWhiteSpace(text[s])) s++;
                string part = text.Substring(s, stop - s).TrimEnd();
                if (part.Length > 0) parts.Add((part, s));
            }

            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(text, i) - 1;
                    continue;
                }
                if (c == '{' || c == '(' || c == '[') depth++;
                else if (c == '}' || c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    Add(i);
                    partStart = i + 1;
                }
            }
            Add(end);
            return parts;
        }

        /// <summary>The elements inside a braced initializer <c>{ … }</c>; null when <paramref name="element"/> is not one.</summary>
        private static List<string>? BracedElements(string element)
        {
            string trimmed = element.Trim();
            if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}')) return null;
            return SplitTopLevel(trimmed, 1, trimmed.Length - 1).Select(p => p.Text).ToList();
        }

        /// <summary>
        /// The rows of the table whose header <paramref name="header"/> matches in
        /// <paramref name="clean"/>: each row's text and its first and last 0-based line. Null when the
        /// table is not found or not closed.
        /// </summary>
        private static List<(string Text, int FirstLine, int LastLine)>? TableRows(CleanText clean, Regex header, int from = 0)
        {
            var match = header.Match(clean.Text, from);
            if (!match.Success) return null;
            int open = match.Index + match.Length - 1;
            int close = MatchingClose(clean.Text, open);
            if (close < 0) return null;

            var rows = new List<(string Text, int FirstLine, int LastLine)>();
            foreach (var (part, start) in SplitTopLevel(clean.Text, open + 1, close))
            {
                rows.Add((part, clean.LineOf(start), clean.LineOf(start + part.Length - 1)));
            }
            return rows;
        }

        private static string RawRow(string[] lines, int firstLine, int lastLine)
        {
            string raw = string.Join("\n", lines.Skip(firstLine).Take(lastLine - firstLine + 1)).Trim();
            return raw.Length > RawDefinitionMaxChars ? raw.Substring(0, RawDefinitionMaxChars) + " …" : raw;
        }

        /// <summary>
        /// The text of a string literal token (adjacent literals joined); null for <c>0</c>,
        /// <c>NULL</c> or <c>None</c>. <paramref name="ok"/> is false when the token is neither.
        /// </summary>
        private static string? StringValue(string token, out bool ok)
        {
            string t = token.Trim();
            ok = true;
            if (t == "0" || t == "NULL" || t == "None" || t == "(char *) 0" || t == "(const char *) 0") return null;
            if (!t.StartsWith('"'))
            {
                ok = false;
                return null;
            }

            var sb = new StringBuilder();
            int i = 0;
            while (i < t.Length)
            {
                if (char.IsWhiteSpace(t[i]))
                {
                    i++;
                    continue;
                }
                if (t[i] != '"')
                {
                    ok = false;
                    return null;
                }
                int end = SkipLiteral(t, i);
                if (end <= i + 1 || t[end - 1] != '"')
                {
                    ok = false;
                    return null;
                }
                AppendUnescaped(sb, t.Substring(i + 1, end - i - 2));
                i = end;
            }
            return sb.ToString();
        }

        /// <summary>Appends a literal's body with <c>\n</c>, <c>\t</c> and backslash-escaped characters resolved.</summary>
        private static void AppendUnescaped(StringBuilder sb, string body)
        {
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '\\' || i + 1 >= body.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char next = body[++i];
                sb.Append(next switch { 'n' => '\n', 't' => '\t', _ => next });
            }
        }

        /// <summary>
        /// The tokens of a <c>|</c>-joined mask, outer parentheses removed and <c>M2_</c> read as
        /// <c>MH_</c>; <c>0</c> contributes nothing. Null when a token is neither an identifier nor 0.
        /// </summary>
        private static HashSet<string>? MaskTokens(string token)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in token.Split('|'))
            {
                string part = raw.Trim().Trim('(', ')').Trim();
                if (part.Length == 0) continue;
                if (part.EndsWith("UL", StringComparison.OrdinalIgnoreCase) && part.Length > 2 && char.IsDigit(part[0]))
                {
                    part = part.Substring(0, part.Length - 2);
                }
                if (part == "0" || part == "0x0" || part == "0x00000000") continue;
                if (!IdentifierRegex.IsMatch(part)) return null;
                result.Add(part.StartsWith("M2_", StringComparison.Ordinal) ? "MH_" + part.Substring(3) : part);
            }
            return result;
        }

        private static List<string> TraitValues(string element)
        {
            var traits = new List<string>();
            var items = BracedElements(element);
            if (items == null) return traits;
            foreach (string item in items)
            {
                string? value = StringValue(item, out bool ok);
                if (ok && !string.IsNullOrWhiteSpace(value)) traits.Add(value);
            }
            return traits;
        }

        private static void ParseRoles(string[] lines, CleanText clean, IReadOnlyDictionary<string, string> artifactNames,
            List<CharacterRole> roles, List<string> problems)
        {
            var rows = TableRows(clean, RolesTableRegex);
            if (rows == null)
            {
                problems.Add("src/role.c: the roles[] table was not found.");
                return;
            }

            foreach (var (text, firstLine, lastLine) in rows)
            {
                var fields = BracedElements(text);
                if (fields == null || fields.Count == 0) continue;

                var names = BracedElements(fields[0]);
                string? name = names != null && names.Count > 0 ? StringValue(names[0], out _) : null;
                if (string.IsNullOrEmpty(name)) continue; /* the { { 0, 0 } } terminator */
                string? femaleName = names!.Count > 1 ? StringValue(names[1], out _) : null;

                string raw = RawRow(lines, firstLine, lastLine);
                string? error = null;
                HashSet<string>? allow = null;
                string? monster = null;
                string? quest = null;
                var traits = new List<string>();

                if (fields.Count < 22)
                {
                    error = $"the initializer has {fields.Count} top-level fields where struct Role needs at least 22 before the attributes.";
                }
                else
                {
                    traits = TraitValues(fields[10]);
                    monster = fields[11].Trim();
                    quest = fields[20].Trim();
                    allow = MaskTokens(fields[21]);
                    if (!monster.StartsWith("PM_", StringComparison.Ordinal))
                        error = $"field 12 (monsternum) is '{monster}', not a PM_ constant.";
                    else if (!IdentifierRegex.IsMatch(quest))
                        error = $"field 21 (questarti) is '{quest}', not an ART_ constant.";
                    else if (allow == null)
                        error = $"field 22 (allow) '{fields[21].Trim()}' is not a mask of named flags.";
                }

                roles.Add(new CharacterRole
                {
                    Name = name,
                    FemaleName = string.IsNullOrEmpty(femaleName) ? null : femaleName,
                    Traits = traits,
                    MonsterConstant = error == null ? monster : null,
                    QuestArtifactConstant = error == null ? quest : null,
                    QuestArtifactName = error == null && quest != null && artifactNames.TryGetValue(quest, out var artifactName) ? artifactName : null,
                    Allow = allow ?? new HashSet<string>(),
                    RawDefinition = raw,
                    ParseError = error
                });
            }

            if (roles.Count == 0) problems.Add("src/role.c: the roles[] table holds no readable role.");
        }

        private static void ParseRaces(string[] lines, CleanText clean, List<CharacterRace> races, List<string> problems)
        {
            var rows = TableRows(clean, RacesTableRegex);
            if (rows == null)
            {
                problems.Add("src/role.c: the races[] table was not found.");
                return;
            }

            foreach (var (text, firstLine, lastLine) in rows)
            {
                var fields = BracedElements(text);
                if (fields == null || fields.Count == 0) continue;

                string? noun = StringValue(fields[0], out _);
                if (string.IsNullOrEmpty(noun)) continue; /* the { 0, 0, 0, 0 } terminator */

                string raw = RawRow(lines, firstLine, lastLine);
                string? error = null;
                string? adjective = fields.Count > 1 ? StringValue(fields[1], out _) : null;
                string? collective = fields.Count > 2 ? StringValue(fields[2], out _) : null;
                string? individualMale = null;
                string? individualFemale = null;
                var traits = new List<string>();
                string? monster = null;
                HashSet<string>? allow = null, self = null, love = null, hate = null;

                if (fields.Count < 15)
                {
                    error = $"the initializer has {fields.Count} top-level fields where struct Race needs at least 15 before the attributes.";
                }
                else
                {
                    var individual = BracedElements(fields[4]);
                    if (individual != null && individual.Count > 0) individualMale = StringValue(individual[0], out _);
                    if (individual != null && individual.Count > 1) individualFemale = StringValue(individual[1], out _);
                    traits = TraitValues(fields[7]);
                    monster = fields[8].Trim();
                    allow = MaskTokens(fields[11]);
                    self = MaskTokens(fields[12]);
                    love = MaskTokens(fields[13]);
                    hate = MaskTokens(fields[14]);
                    if (!monster.StartsWith("PM_", StringComparison.Ordinal))
                        error = $"field 9 (monsternum) is '{monster}', not a PM_ constant.";
                    else if (allow == null)
                        error = $"field 12 (allow) '{fields[11].Trim()}' is not a mask of named flags.";
                    else if (self == null || self.Count == 0)
                        error = $"field 13 (selfmask) '{fields[12].Trim()}' is not a mask of named race flags.";
                    else if (love == null)
                        error = $"field 14 (lovemask) '{fields[13].Trim()}' is not a mask of named race flags.";
                    else if (hate == null)
                        error = $"field 15 (hatemask) '{fields[14].Trim()}' is not a mask of named race flags.";
                }

                races.Add(new CharacterRace
                {
                    Noun = noun,
                    Adjective = adjective,
                    Collective = collective,
                    IndividualMale = individualMale,
                    IndividualFemale = individualFemale,
                    Traits = traits,
                    MonsterConstant = error == null ? monster : null,
                    Allow = allow ?? new HashSet<string>(),
                    SelfMask = error == null ? self! : new HashSet<string>(),
                    LoveMask = error == null ? love! : new HashSet<string>(),
                    HateMask = error == null ? hate! : new HashSet<string>(),
                    RawDefinition = raw,
                    ParseError = error
                });
            }

            if (races.Count == 0) problems.Add("src/role.c: the races[] table holds no readable race.");
        }

        private static void ParsePropertyNames(string[] propLines, Dictionary<string, string> propertyNames, List<string> problems)
        {
            var rows = TableRows(Clean(propLines), PropertyNamesTableRegex);
            if (rows == null)
            {
                problems.Add("src/prop.c: the propertynames[] table was not found; abilities are named by their property constants.");
                return;
            }

            foreach (var (text, _, _) in rows)
            {
                var fields = BracedElements(text);
                if (fields == null || fields.Count < 2) continue;
                string constant = fields[0].Trim();
                if (!IdentifierRegex.IsMatch(constant)) continue;
                string? name = StringValue(fields[1], out bool ok);
                if (ok && !string.IsNullOrEmpty(name)) propertyNames.TryAdd(constant, name);
            }
        }

        /// <summary>Reads the <c>*_abil[]</c> tables and the <c>role_abil()</c> / <c>race_abil()</c> mappings, and sets each role's and race's abilities.</summary>
        private static void AttachAbilities(string[] attribLines, IReadOnlyDictionary<string, string> propertyNames,
            List<CharacterRole> roles, List<CharacterRace> races, List<string> problems)
        {
            var clean = Clean(attribLines);
            var tables = new Dictionary<string, List<CharacterAbility>>(StringComparer.Ordinal);

            foreach (Match header in AbilityTableRegex.Matches(clean.Text))
            {
                string tableName = header.Groups[1].Value;
                int open = header.Index + header.Length - 1;
                int close = MatchingClose(clean.Text, open);
                if (close < 0) continue;

                var abilities = new List<CharacterAbility>();
                foreach (var (row, _) in SplitTopLevel(clean.Text, open + 1, close))
                {
                    var fields = BracedElements(row);
                    if (fields == null || fields.Count < 2) continue;
                    if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)) continue;
                    string property = fields[1].Trim();
                    if (level == 0 && property == "0") continue; /* the { 0, 0, 0, 0 } terminator */
                    if (!IdentifierRegex.IsMatch(property)) continue;
                    abilities.Add(new CharacterAbility
                    {
                        Level = level,
                        Property = property,
                        Name = propertyNames.TryGetValue(property, out var name) ? name : property
                    });
                }
                tables[tableName] = abilities;
            }

            if (tables.Count == 0)
            {
                problems.Add("src/attrib.c: no *_abil[] table was found; abilities_by_level is unavailable.");
                return;
            }

            var roleMap = FunctionCaseMap(clean.Text, RoleAbilDefinitionRegex);
            var raceMap = FunctionCaseMap(clean.Text, RaceAbilDefinitionRegex);
            if (roleMap == null) problems.Add("src/attrib.c: the role_abil() definition was not found; role abilities are unavailable.");
            if (raceMap == null) problems.Add("src/attrib.c: the race_abil() definition was not found; race abilities are unavailable.");

            foreach (var role in roles)
            {
                if (roleMap != null && role.MonsterConstant != null
                    && roleMap.TryGetValue(role.MonsterConstant, out var table) && tables.TryGetValue(table, out var abilities))
                {
                    role.Abilities = Ordered(abilities);
                }
            }

            foreach (var race in races)
            {
                if (raceMap != null && race.MonsterConstant != null
                    && raceMap.TryGetValue(race.MonsterConstant, out var table) && tables.TryGetValue(table, out var abilities))
                {
                    race.Abilities = Ordered(abilities);
                }
            }
        }

        private static IReadOnlyList<CharacterAbility> Ordered(List<CharacterAbility> abilities)
            => abilities.Select((a, i) => (a, i)).OrderBy(p => p.a.Level).ThenBy(p => p.i).Select(p => p.a).ToList();

        /// <summary>
        /// The <c>case PM_X: abil = table;</c> pairs in the body of the column-0 function definition
        /// <paramref name="definition"/> matches, the first match followed by a body; null when none is.
        /// </summary>
        private static Dictionary<string, string>? FunctionCaseMap(string text, Regex definition)
        {
            foreach (Match match in definition.Matches(text))
            {
                int paren = text.IndexOf('(', match.Index);
                int closeParen = MatchingClose(text, paren);
                if (closeParen < 0) continue;

                int brace = closeParen + 1;
                while (brace < text.Length && char.IsWhiteSpace(text[brace])) brace++;
                if (brace >= text.Length || text[brace] != '{') continue;

                int end = MatchingClose(text, brace);
                if (end < 0) continue;

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match pair in CaseAssignmentRegex.Matches(text.Substring(brace, end - brace)))
                {
                    map.TryAdd(pair.Groups[1].Value, pair.Groups[2].Value);
                }
                return map;
            }
            return null;
        }

        /// <summary>The artifact names of include/artilist.h by their <c>ART_</c> constant.</summary>
        private static IReadOnlyDictionary<string, string> ArtifactNamesByConstant(string[]? artilistLines)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (artilistLines == null) return names;
            foreach (string line in artilistLines)
            {
                var match = ArtifactNameRegex.Match(line);
                if (!match.Success) continue;
                string? constant = SourceReferenceIndex.ArtifactConstant(match.Groups[1].Value);
                if (constant != null) names.TryAdd(constant, match.Groups[1].Value);
            }
            return names;
        }
    }
}
