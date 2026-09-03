namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text;
using MobileGnollHackLogger.Data;

public class BenchmarkDifficultyQuestionItem
{
    public long Id { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty AuthorBand { get; set; }
    public string? ExpectedPoints { get; set; }
}

public static class BenchmarkDifficultyPrompt
{
    public static string BuildPrompt(string suiteName, IReadOnlyList<BenchmarkDifficultyQuestionItem> questions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert game mechanics analyst and evaluator for GnollHack (a complex roguelike game derived from NetHack 3.6.2).");
        sb.AppendLine($"Suite: {suiteName}");
        sb.AppendLine();
        sb.AppendLine("TASK: Rate the intrinsic difficulty of each question on a 1-100 scale.");
        sb.AppendLine();
        sb.AppendLine($"GUIDELINES FOR DIFFICULTY RATING ({BenchmarkDifficultyBands.MinDifficulty}-{BenchmarkDifficultyBands.MaxDifficulty}):");
        sb.AppendLine($"- {BenchmarkDifficultyBands.SimpleMin} to {BenchmarkDifficultyBands.SimpleMax} (Simple): Basic game facts, foundational items/roles/monsters, introductory mechanics, widely known NetHack lore.");
        sb.AppendLine($"- {BenchmarkDifficultyBands.IntermediateMin} to {BenchmarkDifficultyBands.IntermediateMax} (Intermediate): Multi-step tactical interactions, non-trivial mechanics (spell failure, intrinsics, resistances, standard dungeon branch rules), intermediate combat/identification logic.");
        sb.AppendLine($"- {BenchmarkDifficultyBands.AdvancedMin} to {BenchmarkDifficultyBands.MaxDifficulty} (Advanced): Obscure interactions, deep C source code mechanics, complex damage/probability formulas, rare artifact quirks, subtle patch-specific GnollHack changes, multi-layered strategic edge cases.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL INSTRUCTIONS:");
        sb.AppendLine("1. Evaluate ONLY the question text and rubric reference points. The author's own difficulty band is deliberately withheld so your rating is independent of it.");
        sb.AppendLine("2. Assign an integer difficulty between 1 and 100 to each question.");
        sb.AppendLine("3. Provide a brief 1-sentence rationale for each difficulty score.");
        sb.AppendLine("4. The 'id' value MUST be copied verbatim from the 'ID:' field of the question it rates. Do not renumber.");
        sb.AppendLine("5. Respond with the JSON object and nothing else — no prose, no explanation, no Markdown code fences.");
        sb.AppendLine();
        sb.AppendLine("--- QUESTIONS TO RATE ---");
        sb.AppendLine();

        foreach (var q in questions)
        {
            sb.AppendLine($"### Question {q.OrderIndex} (ID: {q.Id})");
            // The author band is intentionally not disclosed: showing it anchored the rating on
            // the very judgement the assessor is asked to make independently.
            sb.AppendLine($"Question Text: {q.QuestionText}");
            if (!string.IsNullOrWhiteSpace(q.ExpectedPoints))
            {
                sb.AppendLine("Reference Rubric:");
                sb.AppendLine("--- BEGIN RUBRIC ---");
                sb.AppendLine(q.ExpectedPoints);
                sb.AppendLine("--- END RUBRIC ---");
            }
            sb.AppendLine();
        }

        long exampleId = questions.Count > 0 ? questions[0].Id : 1;

        sb.AppendLine("--- OUTPUT JSON SCHEMA ---");
        sb.AppendLine(@$"{{
  ""questions"": [
  {{
    ""id"": {exampleId},
    ""difficulty"": 55,
    ""rationale"": ""Requires knowledge of prayer timeout calculations and altar alignment rules.""
  }}
  ]
}}");

        return sb.ToString();
    }

    public static string BuildRepairPrompt(string suiteName, IReadOnlyList<BenchmarkDifficultyQuestionItem> questions, string? previousResponseExcerpt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RETRY REQUEST: Your previous response could not be parsed into valid question difficulty ratings.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(previousResponseExcerpt))
        {
            string excerpt = previousResponseExcerpt.Length > 1000
                ? previousResponseExcerpt.Substring(0, 1000) + "..."
                : previousResponseExcerpt;

            sb.AppendLine("--- PREVIOUS RESPONSE EXCERPT (FAILED TO PARSE) ---");
            sb.AppendLine(excerpt);
            sb.AppendLine("---------------------------------------------------");
            sb.AppendLine();
        }

        sb.AppendLine("Please correct your output. You MUST respond with ONLY a valid JSON object matching the required schema below.");
        sb.AppendLine("Ensure all question IDs match the exact database IDs listed below without renumbering.");
        sb.AppendLine();
        sb.Append(BuildPrompt(suiteName, questions));

        return sb.ToString();
    }
}
