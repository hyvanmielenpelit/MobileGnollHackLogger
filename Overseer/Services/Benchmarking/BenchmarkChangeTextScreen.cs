namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

/// <summary>A run of words a change text shares with a benchmark question or its rubric.</summary>
/// <param name="Text">The shared words, lower-cased, separated by single spaces.</param>
/// <param name="QuestionNumber">The question's <c>OrderIndex + 1</c>.</param>
/// <param name="Field"><see cref="BenchmarkChangeTextScreen.QuestionField"/> or <see cref="BenchmarkChangeTextScreen.RubricField"/>.</param>
public sealed record BenchmarkChangeOverlap(
    string Text, long SuiteId, string SuiteName, long QuestionId, int QuestionNumber, string Field);

/// <summary>The parts of a benchmark question the screen compares a text with.</summary>
public sealed record BenchmarkScreenQuestion(
    long QuestionId, long SuiteId, string SuiteName, int OrderIndex, string? QuestionText, string? ExpectedPoints);

/// <summary>
/// The overlap check of the experimental-change editor: every text is compared with every benchmark
/// question's text and rubric, of all suites, as lower-cased word sequences (letters and digits;
/// punctuation and spacing ignored). A run of <see cref="MinimumSharedWords"/> or more consecutive
/// words is reported, once per question field, at most <see cref="MaxFindings"/> times. It warns and
/// never blocks.
/// </summary>
public class BenchmarkChangeTextScreen
{
    public const int MinimumSharedWords = 8;
    public const int MaxFindings = 10;
    public const string QuestionField = "question";
    public const string RubricField = "rubric";

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _db;

    public BenchmarkChangeTextScreen(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>The overlaps of <paramref name="texts"/> with every stored benchmark question.</summary>
    public async Task<IReadOnlyList<BenchmarkChangeOverlap>> ScreenAsync(IEnumerable<string?> texts, CancellationToken ct = default)
    {
        var list = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
        if (list.Count == 0) return Array.Empty<BenchmarkChangeOverlap>();

        var questions = await _db.BenchmarkQuestions
            .AsNoTracking()
            .OrderBy(q => q.BenchmarkSuiteId)
            .ThenBy(q => q.OrderIndex)
            .ThenBy(q => q.Id)
            .Select(q => new BenchmarkScreenQuestion(
                q.Id, q.BenchmarkSuiteId, q.BenchmarkSuite.Name, q.OrderIndex, q.QuestionText, q.ExpectedPoints))
            .ToListAsync(ct);

        return FindOverlaps(list, questions);
    }

    /// <summary>The overlaps of <paramref name="texts"/> with <paramref name="questions"/>, in text order.</summary>
    public static IReadOnlyList<BenchmarkChangeOverlap> FindOverlaps(
        IReadOnlyList<string> texts, IReadOnlyList<BenchmarkScreenQuestion> questions)
    {
        var fields = new List<(BenchmarkScreenQuestion Question, string Field, string[] Words)>();
        foreach (var question in questions)
        {
            fields.Add((question, QuestionField, Words(question.QuestionText)));
            fields.Add((question, RubricField, Words(question.ExpectedPoints)));
        }

        var index = new Dictionary<string, List<(int Field, int Position)>>(StringComparer.Ordinal);
        for (int f = 0; f < fields.Count; f++)
        {
            string[] words = fields[f].Words;
            for (int p = 0; p + MinimumSharedWords <= words.Length; p++)
            {
                string key = string.Join(' ', words, p, MinimumSharedWords);
                if (!index.TryGetValue(key, out var hits))
                {
                    hits = new List<(int Field, int Position)>();
                    index[key] = hits;
                }
                hits.Add((f, p));
            }
        }

        var findings = new List<BenchmarkChangeOverlap>();
        var reported = new HashSet<int>();
        foreach (string text in texts)
        {
            string[] words = Words(text);
            for (int i = 0; i + MinimumSharedWords <= words.Length; i++)
            {
                if (!index.TryGetValue(string.Join(' ', words, i, MinimumSharedWords), out var hits)) continue;

                foreach (var (f, p) in hits)
                {
                    if (reported.Contains(f)) continue;

                    string[] fieldWords = fields[f].Words;
                    int length = MinimumSharedWords;
                    while (i + length < words.Length && p + length < fieldWords.Length && words[i + length] == fieldWords[p + length])
                    {
                        length++;
                    }

                    reported.Add(f);
                    var question = fields[f].Question;
                    findings.Add(new BenchmarkChangeOverlap(
                        string.Join(' ', words, i, length),
                        question.SuiteId,
                        question.SuiteName,
                        question.QuestionId,
                        question.OrderIndex + 1,
                        fields[f].Field));
                    if (findings.Count >= MaxFindings) return findings;
                }
            }
        }
        return findings;
    }

    private static string[] Words(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : WordRegex.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToArray();
}
