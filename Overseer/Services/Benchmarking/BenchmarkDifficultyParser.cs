namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

public class BenchmarkDifficultyParsedItem
{
    public long Id { get; set; }
    public int Difficulty { get; set; }
    public string? Rationale { get; set; }
}

public class BenchmarkDifficultyParseResult
{
    public bool Success { get; set; }
    public List<BenchmarkDifficultyParsedItem> Items { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? RawJson { get; set; }
    public string? RawResponse { get; set; }
    public string? Strategy { get; set; }
    public bool Salvaged { get; set; }
}

public static class BenchmarkDifficultyParser
{
    private static readonly string[] KnownArrayPropertyNames = new[]
    {
        "ratings",
        "assessments",
        "results",
        "items",
        "data",
        "difficulties"
    };

    public static BenchmarkDifficultyParseResult Parse(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new BenchmarkDifficultyParseResult
            {
                Success = false,
                RawResponse = rawResponse,
                ErrorMessage = "Empty or null assessor response."
            };
        }

        string trimmedRaw = rawResponse.Trim();

        // 1. Try candidates in order
        var candidates = GetCandidates(rawResponse);
        string? firstCandidateJson = null;

        foreach (var (candidateJson, strategy) in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidateJson))
            {
                continue;
            }

            firstCandidateJson ??= candidateJson;

            try
            {
                using var doc = JsonDocument.Parse(candidateJson);
                if (TryExtractItems(doc.RootElement, out var items) && items.Count > 0)
                {
                    return new BenchmarkDifficultyParseResult
                    {
                        Success = true,
                        Items = items,
                        RawJson = candidateJson,
                        RawResponse = rawResponse,
                        Strategy = strategy,
                        Salvaged = false
                    };
                }
            }
            catch (JsonException)
            {
                // Continue to next candidate
            }
            catch (Exception)
            {
                // Continue to next candidate
            }
        }

        // 2. Salvage layer if every candidate failed
        var salvagedItems = TrySalvage(rawResponse);
        if (salvagedItems.Count > 0)
        {
            return new BenchmarkDifficultyParseResult
            {
                Success = true,
                Items = salvagedItems,
                RawJson = firstCandidateJson ?? trimmedRaw,
                RawResponse = rawResponse,
                Strategy = "salvage",
                Salvaged = true
            };
        }

        return new BenchmarkDifficultyParseResult
        {
            Success = false,
            Items = new List<BenchmarkDifficultyParsedItem>(),
            RawJson = firstCandidateJson ?? trimmedRaw,
            RawResponse = rawResponse,
            ErrorMessage = "No question difficulty ratings parsed from response."
        };
    }

    private static List<(string Candidate, string Strategy)> GetCandidates(string rawText)
    {
        var list = new List<(string Candidate, string Strategy)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Fenced blocks
        var fenceMatches = Regex.Matches(rawText, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        foreach (Match match in fenceMatches)
        {
            string content = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(content) && seen.Add(content))
            {
                list.Add((content, "fenced"));
            }
        }

        // 2. Balanced-delimiter scan
        var balanced = ScanBalancedDelimiters(rawText);
        foreach (var item in balanced)
        {
            string content = item.Trim();
            if (!string.IsNullOrEmpty(content) && seen.Add(content))
            {
                list.Add((content, "balanced-scan"));
            }
        }

        // 3. Trimmed raw text
        string trimmed = rawText.Trim();
        if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
        {
            list.Add((trimmed, "raw"));
        }

        return list;
    }

    private static List<string> ScanBalancedDelimiters(string text)
    {
        var results = new List<string>();
        var stack = new Stack<char>();
        int startIndex = -1;
        bool inString = false;
        bool escape = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                escape = false;
                continue;
            }

            if (c == '{' || c == '[')
            {
                if (stack.Count == 0)
                {
                    startIndex = i;
                }
                stack.Push(c);
            }
            else if (c == '}' || c == ']')
            {
                if (stack.Count > 0)
                {
                    char open = stack.Peek();
                    if ((c == '}' && open == '{') || (c == ']' && open == '['))
                    {
                        stack.Pop();
                        if (stack.Count == 0 && startIndex >= 0)
                        {
                            results.Add(text.Substring(startIndex, i - startIndex + 1));
                            startIndex = -1;
                        }
                    }
                }
            }
        }

        return results;
    }

    private static bool TryExtractItems(JsonElement root, out List<BenchmarkDifficultyParsedItem> items)
    {
        items = new List<BenchmarkDifficultyParsedItem>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in root.EnumerateArray())
            {
                if (TryParseItem(elem, out var item))
                {
                    items.Add(item);
                }
            }
            return items.Count > 0;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Case 1: "questions" property
            if (root.TryGetProperty("questions", out var questionsElem))
            {
                if (questionsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in questionsElem.EnumerateArray())
                    {
                        if (TryParseItem(elem, out var item))
                        {
                            items.Add(item);
                        }
                    }
                    return items.Count > 0;
                }
                else if (questionsElem.ValueKind == JsonValueKind.Object)
                {
                    if (TryParseItem(questionsElem, out var item))
                    {
                        items.Add(item);
                        return true;
                    }
                }
            }

            // Case 2: Known array wrapper keys
            foreach (string key in KnownArrayPropertyNames)
            {
                if (TryGetPropertyIgnoreCase(root, key, out var propElem) && propElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in propElem.EnumerateArray())
                    {
                        if (TryParseItem(elem, out var item))
                        {
                            items.Add(item);
                        }
                    }
                    if (items.Count > 0)
                    {
                        return true;
                    }
                }
            }

            // Case 3: Root itself is a single item with id and difficulty
            if (TryParseItem(root, out var rootItem))
            {
                items.Add(rootItem);
                return true;
            }

            // Case 4: Any object with exactly one array-valued property whose first element has id and difficulty
            JsonProperty? singleCandidateArray = null;
            int arrayPropCount = 0;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    arrayPropCount++;
                    singleCandidateArray = prop;
                }
            }

            if (arrayPropCount == 1 && singleCandidateArray.HasValue)
            {
                var arr = singleCandidateArray.Value.Value;
                bool firstHasKeys = false;
                foreach (var elem in arr.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.Object &&
                        HasPropertyIgnoreCase(elem, "id") &&
                        HasPropertyIgnoreCase(elem, "difficulty"))
                    {
                        firstHasKeys = true;
                    }
                    break;
                }

                if (firstHasKeys)
                {
                    foreach (var elem in arr.EnumerateArray())
                    {
                        if (TryParseItem(elem, out var item))
                        {
                            items.Add(item);
                        }
                    }
                    return items.Count > 0;
                }
            }
        }

        return false;
    }

    private static bool TryParseItem(JsonElement elem, out BenchmarkDifficultyParsedItem item)
    {
        item = new BenchmarkDifficultyParsedItem();
        if (elem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        bool hasId = false;
        long id = 0;
        if (TryGetPropertyIgnoreCase(elem, "id", out var idProp))
        {
            if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var numId))
            {
                id = numId;
                hasId = true;
            }
            else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var strId))
            {
                id = strId;
                hasId = true;
            }
        }

        bool hasDiff = false;
        int difficulty = 50;
        if (TryGetPropertyIgnoreCase(elem, "difficulty", out var diffProp))
        {
            if (diffProp.ValueKind == JsonValueKind.Number && diffProp.TryGetInt32(out var numDiff))
            {
                difficulty = Math.Clamp(numDiff, 1, 100);
                hasDiff = true;
            }
            else if (diffProp.ValueKind == JsonValueKind.String && int.TryParse(diffProp.GetString(), out var strDiff))
            {
                difficulty = Math.Clamp(strDiff, 1, 100);
                hasDiff = true;
            }
        }

        if (!hasId && !hasDiff)
        {
            return false;
        }

        string? rationale = null;
        if (TryGetPropertyIgnoreCase(elem, "rationale", out var ratProp) && ratProp.ValueKind == JsonValueKind.String)
        {
            rationale = ratProp.GetString();
        }

        item.Id = id;
        item.Difficulty = difficulty;
        item.Rationale = rationale;
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool HasPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        return TryGetPropertyIgnoreCase(element, propertyName, out _);
    }

    private static List<BenchmarkDifficultyParsedItem> TrySalvage(string text)
    {
        var results = new List<BenchmarkDifficultyParsedItem>();

        // Find brace-delimited fragments: { ... }
        var matches = Regex.Matches(text, @"\{[^{}]*\}");
        foreach (Match m in matches)
        {
            string fragment = m.Value;

            var idMatch = Regex.Match(fragment, @"""id""\s*:\s*[""']?(\d+)[""']?", RegexOptions.IgnoreCase);
            var diffMatch = Regex.Match(fragment, @"""difficulty""\s*:\s*[""']?(\d+)[""']?", RegexOptions.IgnoreCase);

            if (idMatch.Success && diffMatch.Success)
            {
                if (long.TryParse(idMatch.Groups[1].Value, out long id) &&
                    int.TryParse(diffMatch.Groups[1].Value, out int diff))
                {
                    string? rationale = null;
                    var ratMatch = Regex.Match(fragment, @"""rationale""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.IgnoreCase);
                    if (ratMatch.Success)
                    {
                        rationale = ratMatch.Groups[1].Value;
                    }

                    results.Add(new BenchmarkDifficultyParsedItem
                    {
                        Id = id,
                        Difficulty = Math.Clamp(diff, 1, 100),
                        Rationale = rationale
                    });
                }
            }
        }

        return results;
    }
}
