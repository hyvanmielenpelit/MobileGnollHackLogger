using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services
{
    public class MacroDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Parameters { get; set; } = new();
        public string Body { get; set; } = string.Empty;
        public bool IsStructInitializer => Body.TrimStart().StartsWith("{");
        public List<string> BodyTokens { get; set; } = new();
    }

    public class StructDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Fields { get; set; } = new();
        public string RawDefinition { get; set; } = string.Empty;
    }

    public class GameDataParser
    {
        private readonly Dictionary<string, MacroDefinition> _macros = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StructDefinition> _structs = new(StringComparer.OrdinalIgnoreCase);

        public void ParseStructs(string[] permonstLines, string[] objclassLines)
        {
            _structs["permonst"] = ParseStruct("permonst", permonstLines);
            _structs["attack"] = ParseStruct("attack", permonstLines);
            _structs["objclass"] = ParseStruct("objclass", objclassLines);
        }

        private StructDefinition ParseStruct(string name, string[] lines)
        {
            var def = new StructDefinition { Name = name };
            int startIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], $@"^\s*struct\s+{name}\s*{{"))
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx >= 0)
            {
                var extraction = CLexer.ExtractBracedBlock(lines, startIdx);
                if (extraction != null)
                {
                    def.RawDefinition = $"struct {name} {{\n{extraction.Content}\n}};";
                    
                    // Parse fields
                    var fieldLines = extraction.Content.Split('\n');
                    foreach (var fl in fieldLines)
                    {
                        var line = fl.Trim();
                        // Ignore directives and comments
                        if (line.StartsWith("#") || line.StartsWith("/*") || string.IsNullOrEmpty(line)) continue;
                        
                        // Remove inline comments
                        int commentIdx = line.IndexOf("/*");
                        if (commentIdx >= 0) line = line.Substring(0, commentIdx).Trim();
                        
                        if (line.EndsWith(";"))
                        {
                            line = line.TrimEnd(';');
                            // e.g. "int str, dex, con"
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                var type = string.Join(" ", parts.Take(parts.Length - 1));
                                var vars = parts.Last().Split(',');
                                foreach (var v in vars)
                                {
                                    var cleanedVar = v.Replace("*", "").Trim();
                                    // Handle array like mattk[NATTK]
                                    int bracketIdx = cleanedVar.IndexOf('[');
                                    if (bracketIdx >= 0) cleanedVar = cleanedVar.Substring(0, bracketIdx);
                                    if (!string.IsNullOrEmpty(cleanedVar))
                                    {
                                        def.Fields.Add(cleanedVar);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return def;
        }

        public void ParseMacros(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                // Both the parameter list and the body may be backslash-continued,
                // so the directive is matched against its joined logical line.
                if (!Regex.IsMatch(lines[i], @"^\s*#define\s+[A-Za-z0-9_]+\(")) continue;

                int lastLine = i;
                while (lastLine < lines.Length - 1 && lines[lastLine].EndsWith("\\")) lastLine++;

                string logicalLine = JoinContinuedLines(lines, i, lastLine);

                var match = Regex.Match(logicalLine, @"^\s*#define\s+([A-Za-z0-9_]+)\(([^)]*)\)\s*(.*)");
                if (match.Success)
                {
                    var macro = new MacroDefinition
                    {
                        Name = match.Groups[1].Value,
                        // Parameter names are kept in declaration order so a call's
                        // arguments can be bound to them by position.
                        Parameters = match.Groups[2].Value.Split(',').Select(p => p.Trim()).ToList(),
                        Body = match.Groups[3].Value.Replace("\\", "").Trim()
                    };

                    // Basic tokenization of the body
                    macro.BodyTokens = ParseMacroArgs(macro.Body);
                    _macros[macro.Name] = macro;
                }
            }
        }

        /// <summary>
        /// Joins the physical lines <paramref name="firstLine"/>..<paramref name="lastLine"/> of a
        /// backslash-continued directive into one logical line, dropping the continuation backslashes.
        /// </summary>
        private static string JoinContinuedLines(string[] lines, int firstLine, int lastLine)
        {
            var joined = new StringBuilder();
            for (int i = firstLine; i <= lastLine; i++)
            {
                string part = i == firstLine ? lines[i] : lines[i].Trim();
                if (i < lastLine && part.EndsWith("\\"))
                {
                    part = part.Substring(0, part.Length - 1);
                }
                if (i > firstLine) joined.Append(' ');
                joined.Append(part);
            }
            return joined.ToString();
        }

        /// <summary>
        /// Splits a macro body, or the argument list of a macro call, into its top-level arguments.
        /// Comments are discarded. String and character literals are opaque, so commas and
        /// parentheses inside them are not separators, and adjacent literals with a macro call
        /// between them stay in one argument. A nested call survives as a single argument.
        /// </summary>
        public List<string> ParseMacroArgs(string body)
        {
            var tokens = new List<string>();
            string text = StripComments(body).Trim();
            if (text.StartsWith("{"))
            {
                text = text.Substring(1);
                if (text.EndsWith("}")) text = text.Substring(0, text.Length - 1);
            }

            int depth = 0;
            var currentToken = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || (c == '\'' && IsCharLiteralStart(text, i)))
                {
                    int literalEnd = SkipLiteral(text, i);
                    currentToken.Append(text, i, literalEnd - i);
                    i = literalEnd - 1;
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    tokens.Add(currentToken.ToString().Trim());
                    currentToken.Clear();
                    continue;
                }
                currentToken.Append(c);
            }
            if (!string.IsNullOrWhiteSpace(currentToken.ToString()))
            {
                tokens.Add(currentToken.ToString().Trim());
            }
            return tokens;
        }

        /// <summary>
        /// Replaces every C comment with a single space, leaving string and character literals intact.
        /// </summary>
        private static string StripComments(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '"' || (c == '\'' && IsCharLiteralStart(text, i)))
                {
                    int literalEnd = SkipLiteral(text, i);
                    result.Append(text, i, literalEnd - i);
                    i = literalEnd - 1;
                    continue;
                }

                if (c == '/' && next == '/')
                {
                    // The line break that ends the comment is left for the normal path to copy.
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                    result.Append(' ');
                    i--;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    int commentEnd = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    result.Append(' ');
                    if (commentEnd < 0) break;
                    i = commentEnd + 1;
                    continue;
                }

                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>
        /// Returns the index just past the string or character literal starting at
        /// <paramref name="start"/>, or the end of the text if the literal is unterminated.
        /// A backslash escapes the character that follows it.
        /// </summary>
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
            }
            return text.Length;
        }

        /// <summary>
        /// Reports whether the apostrophe at <paramref name="index"/> opens a character literal,
        /// which requires a closing apostrophe within the span a literal can occupy. An apostrophe
        /// in prose is therefore treated as an ordinary character.
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

        public Dictionary<string, string> GetMacroDefinitions(params string[] names)
        {
            var res = new Dictionary<string, string>();
            foreach (var n in names)
            {
                if (_macros.TryGetValue(n, out var m))
                {
                    res[n] = $"#define {m.Name}({string.Join(", ", m.Parameters)}) {m.Body}";
                }
            }
            return res;
        }

        public Dictionary<string, string> GetStructDefinitions(params string[] names)
        {
            var res = new Dictionary<string, string>();
            foreach (var n in names)
            {
                if (_structs.TryGetValue(n, out var s) && !string.IsNullOrEmpty(s.RawDefinition))
                {
                    res[n] = s.RawDefinition;
                }
            }
            return res;
        }

        // Hardcoded fallbacks
        public List<string> GetMonsterHardcodedFields()
        {
            return new List<string>
            {
                "mname", "mtitle", "mdescription", "mfemalename", "mcommonname", "mlet",
                "mlevel", "mmove", "ac", "mc", "mr", "maligntyp",
                "geno",
                "mattk",
                "cwt", "cnutrit", "msound", "msize", "heads", "lightrange", "body_material_type",
                "str", "dex", "con", "intl", "wis", "cha",
                "mresists", "mresists2", "mconveys",
                "mflags1", "mflags2", "mflags3", "mflags4", "mflags5", "mflags6", "mflags7", "mflags8",
                "difficulty", "mcolor"
            };
        }
        
        public List<string> GetAttackHardcodedFields()
        {
            return new List<string>
            {
                "aatyp", "adtyp", "damn", "damd", "damp", "mcadj", "mlevel", "range", "aflags", "action_tile"
            };
        }

        /// <summary>
        /// Alias of <see cref="ParseMacroArgs"/> retained for the monster and artifact call sites.
        /// </summary>
        public List<string> ParseMonsterMacroArgs(string body)
        {
            return ParseMacroArgs(body);
        }
    }
}
