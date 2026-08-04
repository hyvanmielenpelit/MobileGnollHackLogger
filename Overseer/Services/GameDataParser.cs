using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
                string line = lines[i];
                var match = Regex.Match(line, @"^\s*#define\s+([A-Za-z0-9_]+)\(([^)]*)\)\s*(.*)");
                if (match.Success)
                {
                    var macro = new MacroDefinition
                    {
                        Name = match.Groups[1].Value,
                        Parameters = match.Groups[2].Value.Split(',').Select(p => p.Trim()).ToList(),
                        Body = match.Groups[3].Value.Trim()
                    };
                    
                    int current = i;
                    while (current < lines.Length && lines[current].EndsWith("\\"))
                    {
                        current++;
                        if (current < lines.Length)
                        {
                            macro.Body += " " + lines[current].Trim();
                        }
                    }
                    macro.Body = macro.Body.Replace("\\", "").Trim();

                    // Basic tokenization of the body
                    macro.BodyTokens = TokenizeMacroBody(macro.Body);
                    _macros[macro.Name] = macro;
                }
            }
        }

        private List<string> TokenizeMacroBody(string body)
        {
            var tokens = new List<string>();
            if (body.StartsWith("{"))
            {
                body = body.Substring(1);
                if (body.EndsWith("}")) body = body.Substring(0, body.Length - 1);
            }
            
            int depth = 0;
            string currentToken = "";
            foreach (char c in body)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    tokens.Add(currentToken.Trim());
                    currentToken = "";
                    continue;
                }
                currentToken += c;
            }
            if (!string.IsNullOrWhiteSpace(currentToken))
            {
                tokens.Add(currentToken.Trim());
            }
            return tokens;
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

        public List<string> ParseMonsterMacroArgs(string body)
        {
            return TokenizeMacroBody(body);
        }
    }
}
