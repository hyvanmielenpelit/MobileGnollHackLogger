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
        sb.AppendLine("GUIDELINES FOR DIFFICULTY RATING (1-100):");
        sb.AppendLine("- 1 to 35 (Simple): Basic game facts, foundational items/roles/monsters, introductory mechanics, widely known NetHack lore.");
        sb.AppendLine("- 36 to 70 (Intermediate): Multi-step tactical interactions, non-trivial mechanics (spell failure, intrinsics, resistances, standard dungeon branch rules), intermediate combat/identification logic.");
        sb.AppendLine("- 71 to 100 (Advanced): Obscure interactions, deep C source code mechanics, complex damage/probability formulas, rare artifact quirks, subtle patch-specific GnollHack changes, multi-layered strategic edge cases.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL INSTRUCTIONS:");
        sb.AppendLine("1. Evaluate ONLY the question text, author difficulty band, and rubric reference points.");
        sb.AppendLine("2. Assign an integer difficulty between 1 and 100 to each question.");
        sb.AppendLine("3. Provide a brief 1-sentence rationale for each difficulty score.");
        sb.AppendLine("4. Respond ONLY with a valid JSON object matching the schema below.");
        sb.AppendLine();
        sb.AppendLine("--- QUESTIONS TO RATE ---");
        sb.AppendLine();

        foreach (var q in questions)
        {
            sb.AppendLine($"### Question {q.OrderIndex} (ID: {q.Id})");
            sb.AppendLine($"Author Band: {q.AuthorBand}");
            sb.AppendLine($"Question Text: {q.QuestionText}");
            if (!string.IsNullOrWhiteSpace(q.ExpectedPoints))
            {
                sb.AppendLine($"Reference Rubric: {q.ExpectedPoints}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("--- OUTPUT JSON SCHEMA ---");
        sb.AppendLine(@"{
  ""questions"": [
    {
      ""id"": 1,
      ""difficulty"": 55,
      ""rationale"": ""Requires knowledge of prayer timeout calculations and altar alignment rules.""
    }
  ]
}");

        return sb.ToString();
    }
}
