namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>One subsystem the suite does not test, as reported by the analysing model.</summary>
public class BenchmarkCoverageGap
{
    [JsonPropertyName("subsystem")]
    public string? Subsystem { get; set; }

    /// <summary>
    /// Where in the source or wiki the subsystem lives. Required: a gap with no location is not
    /// actionable, and a draft rubric that cannot cite a source is not usable as an answer key.
    /// </summary>
    [JsonPropertyName("sourceLocation")]
    public string? SourceLocation { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    /// <summary>
    /// The band the model thinks a question here would sit in. Advisory: a real question gets an
    /// independent difficulty rating through the normal assessment path.
    /// </summary>
    [JsonPropertyName("suggestedBand")]
    public string? SuggestedBand { get; set; }
}

public class BenchmarkCoverageAnalysisResult
{
    [JsonPropertyName("gaps")]
    public List<BenchmarkCoverageGap> Gaps { get; set; } = new();

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class BenchmarkCoverageParseResult
{
    public bool Success { get; set; }
    public BenchmarkCoverageAnalysisResult? Result { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Builds the prompt for suite coverage analysis, and parses its reply.
///
/// This is the only AI-using part of the suite-analysis work, and the only one that could shape
/// the benchmark itself — so what it is *not* allowed to see is as much of the design as what it
/// is asked. The prompt carries the suite's **question texts only**: no rubrics, no candidate
/// answers, no scores, no per-item statistics. Withholding the scores is the point. A model shown
/// which questions models did badly on would report gaps that flatter or punish particular runs,
/// and the resulting suite would encode last run's outcome rather than the domain.
///
/// The result is a **read-only report**. Nothing here writes a question or a rubric, and no
/// endpoint exists that would: a generated draft must be edited and approved by a human, and a
/// draft rubric without a source location is not usable.
/// </summary>
public static class BenchmarkCoveragePrompt
{
    /// <summary>Upper bound on reported gaps, so one call cannot return an unbounded list.</summary>
    public const int MaxGaps = 25;

    public static string BuildPrompt(
        string suiteName,
        IReadOnlyList<string> questionTexts,
        IReadOnlyList<string> sourceInventory,
        IReadOnlyList<string> wikiTopicInventory)
    {
        questionTexts ??= Array.Empty<string>();
        sourceInventory ??= Array.Empty<string>();
        wikiTopicInventory ??= Array.Empty<string>();

        var sb = new StringBuilder();
        sb.AppendLine("You are an expert game mechanics analyst for GnollHack (a complex roguelike derived from NetHack 3.6.2).");
        sb.AppendLine($"Suite: {suiteName}");
        sb.AppendLine();
        sb.AppendLine("TASK: Identify GnollHack subsystems that this benchmark suite does NOT test.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL INSTRUCTIONS:");
        sb.AppendLine("1. You are shown the suite's question TEXTS only. Rubrics, model answers, and scores are deliberately withheld — a coverage judgement must describe the domain, not how any model happened to perform.");
        sb.AppendLine("2. Report a subsystem only when no question in the list tests it. A subsystem tested shallowly is covered; say so by omitting it.");
        sb.AppendLine("3. Every gap MUST carry a 'sourceLocation' — a source file, a symbol, or a wiki article title from the inventories below. A gap with no location is not actionable and will be discarded.");
        sb.AppendLine($"4. Report at most {MaxGaps} gaps, most significant first. Significance means how much of the game's behaviour the subsystem governs, not how obscure it is.");
        sb.AppendLine("5. Do NOT write questions or rubrics. This is a coverage report; a human authors any question that follows from it.");
        sb.AppendLine("6. Respond with the JSON object and nothing else — no prose, no explanation, no Markdown code fences.");
        sb.AppendLine();

        sb.AppendLine("--- SUITE QUESTION TEXTS ---");
        sb.AppendLine();
        for (int i = 0; i < questionTexts.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {questionTexts[i]}");
        }
        sb.AppendLine();

        if (sourceInventory.Count > 0)
        {
            sb.AppendLine("--- SOURCE INVENTORY ---");
            foreach (string entry in sourceInventory)
            {
                sb.AppendLine($"- {entry}");
            }
            sb.AppendLine();
        }

        if (wikiTopicInventory.Count > 0)
        {
            sb.AppendLine("--- WIKI TOPIC INVENTORY ---");
            foreach (string entry in wikiTopicInventory)
            {
                sb.AppendLine($"- {entry}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("--- OUTPUT JSON SCHEMA ---");
        sb.AppendLine(@"{
  ""gaps"": [
  {
    ""subsystem"": ""Polymorph control and system shock"",
    ""sourceLocation"": ""src/polyself.c"",
    ""rationale"": ""No question in the suite asks about polymorph outcomes, control, or the system shock roll."",
    ""suggestedBand"": ""Advanced""
  }
  ],
  ""comment"": ""One sentence on the suite's overall coverage shape.""
}");

        return sb.ToString();
    }

    /// <summary>
    /// Parses the reply. A gap with no source location is **dropped**, not reported with an empty
    /// one: the location is what makes a gap checkable, and a list mixing checkable with
    /// uncheckable entries invites acting on the uncheckable ones.
    /// </summary>
    public static BenchmarkCoverageParseResult Parse(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new BenchmarkCoverageParseResult
            {
                Success = false,
                ErrorMessage = "The coverage analysis returned no text."
            };
        }

        string json = ExtractJson(responseText);

        try
        {
            var parsed = JsonSerializer.Deserialize<BenchmarkCoverageAnalysisResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (parsed == null)
            {
                return new BenchmarkCoverageParseResult
                {
                    Success = false,
                    RawJson = json,
                    ErrorMessage = "The coverage analysis response did not deserialize into the expected schema."
                };
            }

            parsed.Gaps = parsed.Gaps
                .Where(g => !string.IsNullOrWhiteSpace(g.Subsystem) && !string.IsNullOrWhiteSpace(g.SourceLocation))
                .Take(MaxGaps)
                .ToList();

            return new BenchmarkCoverageParseResult
            {
                Success = true,
                Result = parsed,
                RawJson = json
            };
        }
        catch (JsonException ex)
        {
            return new BenchmarkCoverageParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"The coverage analysis response was not valid JSON: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Pulls the JSON object out of a reply that wrapped it in a fence or in prose, despite being
    /// told not to. Same tolerance the other benchmark parsers apply.
    /// </summary>
    private static string ExtractJson(string text)
    {
        string trimmed = text.Trim();

        int fence = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            int start = trimmed.IndexOf('\n', fence);
            int end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                trimmed = trimmed.Substring(start + 1, end - start - 1).Trim();
            }
        }

        int open = trimmed.IndexOf('{');
        int close = trimmed.LastIndexOf('}');
        return open >= 0 && close > open ? trimmed.Substring(open, close - open + 1) : trimmed;
    }
}
