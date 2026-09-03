namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// One claim an assessor could neither confirm nor refute, with the model that produced it.
/// Built from <c>BenchmarkRunAnswer.UnverifiedClaimsJson</c>.
/// </summary>
public sealed record BenchmarkUnverifiedClaimSample
{
    public long QuestionId { get; init; }
    public int QuestionOrderIndex { get; init; }
    public int? ItemRevisionUsed { get; init; }
    public long RunId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string Claim { get; init; } = string.Empty;
}

/// <summary>What a cluster of near-identical claims means.</summary>
public enum BenchmarkRubricGapVerdict
{
    /// <summary>
    /// Raised by two or more independent model families. Two unrelated models inventing the same
    /// specific fact is unlikely; a rubric that omits a true fact both know is likely. Surfaced
    /// for a human to fold into the rubric — never applied automatically.
    /// </summary>
    LikelyRubricGap = 1,

    /// <summary>
    /// Raised by one model family only. This is a finding about that model, already visible on its
    /// own run, and it is deliberately **not** surfaced as a suite issue: treating one family's
    /// invention as a rubric gap is how a benchmark absorbs a model's hallucinations into its own
    /// answer key.
    /// </summary>
    LikelyHallucination = 2
}

public sealed record BenchmarkRubricGapCluster
{
    public long QuestionId { get; init; }
    public int QuestionOrderIndex { get; init; }

    /// <summary>The claims verbatim, so a human reads what was actually said, not a paraphrase.</summary>
    public IReadOnlyList<string> Claims { get; init; } = Array.Empty<string>();

    /// <summary>`provider/base-model-id` for each family that raised it.</summary>
    public IReadOnlyList<string> ModelFamilies { get; init; } = Array.Empty<string>();

    /// <summary>Every model id that raised it, for the panel's attribution line.</summary>
    public IReadOnlyList<string> ModelIds { get; init; } = Array.Empty<string>();

    public int Occurrences { get; init; }
    public BenchmarkRubricGapVerdict Verdict { get; init; }
}

/// <summary>
/// Groups recurring unverified claims per item and decides which of them are evidence about the
/// *rubric* rather than about a model. No AI calls, no embedding service.
///
/// This is the defensible form of "let the models under evaluation improve the benchmark". The
/// indefensible form — asking a candidate what it thinks the answer key should say — lets a model
/// argue its own score up. What happens here instead is narrow: a claim is only evidence about the
/// rubric when **independent model families** raise the same one, and even then it is surfaced for
/// a human to act on rather than written anywhere.
///
/// Clustering is deliberately simple and explainable: normalised token sets and Jaccard overlap. A
/// human reads every cluster anyway, so the cost of a slightly loose or slightly tight cluster is
/// a moment's reading, while the cost of an opaque similarity model is that nobody can say why two
/// claims were grouped.
/// </summary>
public static class BenchmarkRubricGapDetector
{
    /// <summary>Token-set overlap at or above which two claims are treated as the same claim.</summary>
    public const double SimilarityThreshold = 0.6;

    /// <summary>Distinct model families at or above which a cluster is a likely rubric gap.</summary>
    public const int FamiliesForRubricGap = 2;

    private static readonly Regex TokenRegex = new(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Words that carry no discriminating content in a claim about game facts. Kept short on
    /// purpose: an aggressive stopword list starts removing the words that distinguish two claims.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "for", "from", "has", "have",
        "in", "is", "it", "its", "of", "on", "or", "that", "the", "their", "there", "they",
        "this", "to", "was", "were", "will", "with"
    };

    /// <summary>
    /// Clusters the samples per question and revision, and verdicts each cluster. Samples whose
    /// claim is empty are ignored; samples are never merged across questions, because the same
    /// sentence means different things against two different rubrics.
    /// </summary>
    public static IReadOnlyList<BenchmarkRubricGapCluster> Detect(
        IEnumerable<BenchmarkUnverifiedClaimSample> samples)
    {
        if (samples == null) return Array.Empty<BenchmarkRubricGapCluster>();

        var usable = samples
            .Where(s => !string.IsNullOrWhiteSpace(s.Claim))
            .ToList();

        var clusters = new List<BenchmarkRubricGapCluster>();

        // Grouped by revision as well as question: an edited question is a different item, and a
        // claim raised against the old wording is not evidence about the new rubric.
        foreach (var group in usable.GroupBy(s => (s.QuestionId, s.ItemRevisionUsed)))
        {
            clusters.AddRange(ClusterOne(group.ToList()));
        }

        return clusters
            .OrderByDescending(c => c.Verdict == BenchmarkRubricGapVerdict.LikelyRubricGap)
            .ThenByDescending(c => c.ModelFamilies.Count)
            .ThenByDescending(c => c.Occurrences)
            .ThenBy(c => c.QuestionOrderIndex)
            .ToList();
    }

    /// <summary>
    /// Single-link agglomeration against cluster members: a claim joins the first cluster holding
    /// a claim it is similar enough to. Order-dependent in principle; in practice a cluster here
    /// is a handful of near-identical sentences, and the alternative is a similarity matrix nobody
    /// can read.
    /// </summary>
    private static IEnumerable<BenchmarkRubricGapCluster> ClusterOne(
        List<BenchmarkUnverifiedClaimSample> samples)
    {
        var tokenSets = samples.Select(s => Tokenize(s.Claim)).ToList();
        var buckets = new List<List<int>>();

        for (int i = 0; i < samples.Count; i++)
        {
            var bucket = buckets.FirstOrDefault(b =>
                b.Any(j => Jaccard(tokenSets[i], tokenSets[j]) >= SimilarityThreshold));

            if (bucket == null)
            {
                buckets.Add(new List<int> { i });
            }
            else
            {
                bucket.Add(i);
            }
        }

        foreach (var bucket in buckets)
        {
            var members = bucket.Select(i => samples[i]).ToList();
            var families = members
                .Select(m => ModelFamilyOf(m.Provider, m.ModelId))
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            yield return new BenchmarkRubricGapCluster
            {
                QuestionId = members[0].QuestionId,
                QuestionOrderIndex = members[0].QuestionOrderIndex,
                Claims = members.Select(m => m.Claim.Trim()).Distinct(StringComparer.Ordinal).ToList(),
                ModelFamilies = families,
                ModelIds = members
                    .Select(m => m.ModelId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Occurrences = members.Count,
                Verdict = families.Count >= FamiliesForRubricGap
                    ? BenchmarkRubricGapVerdict.LikelyRubricGap
                    : BenchmarkRubricGapVerdict.LikelyHallucination
            };
        }
    }

    /// <summary>
    /// `provider/base`, where base is the first two hyphen-separated segments of the model id.
    /// Follows the same idea as the catalog's prefix matching without needing the catalog:
    /// <c>gpt-5.6-luna</c> and <c>gpt-5.6</c> are one family, <c>gpt-5.6</c> and
    /// <c>gemini-3.7-flash</c> are two.
    ///
    /// The provider is part of the key deliberately. Two ids that happen to share a prefix across
    /// providers are not one family, and the whole verdict rests on families being *independent*.
    /// </summary>
    public static string ModelFamilyOf(string? provider, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(modelId))
        {
            return string.Empty;
        }

        string id = (modelId ?? string.Empty).Trim().ToLowerInvariant();
        var segments = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        string basis = segments.Length >= 2
            ? segments[0] + "-" + segments[1]
            : id;

        return $"{(provider ?? string.Empty).Trim().ToLowerInvariant()}/{basis}";
    }

    private static HashSet<string> Tokenize(string claim)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TokenRegex.Matches(claim.ToLowerInvariant()))
        {
            if (!Stopwords.Contains(m.Value))
            {
                tokens.Add(m.Value);
            }
        }
        return tokens;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;

        int intersection = a.Count(b.Contains);
        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : intersection / (double)union;
    }
}
