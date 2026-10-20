namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Index points at risk of one model or group: each answer below the model's median counted once, by its strongest signal.</summary>
public sealed class RootCauseAtRisk
{
    /// <summary>The model's own median answer quality, the reference every answer is measured against.</summary>
    public double? Median { get; init; }

    /// <summary>The scored answers behind the figures.</summary>
    public int Answers { get; init; }

    /// <summary>Points by segment, in <see cref="RootCauseSegments.AtRisk"/> order: index points, out of 100.</summary>
    public double[] Points { get; init; } = new double[4];

    /// <summary>Per question key, its points by segment; the leave-one-question-out check reads them.</summary>
    public IReadOnlyDictionary<string, double[]> ByQuestion { get; init; } = new Dictionary<string, double[]>(StringComparer.Ordinal);

    public double Total => Points.Sum();
}

/// <summary>
/// How a pair's cost is split (<see cref="RootCauseDecomposition.CostSplitOf(RootCauseModelEvidence, RootCauseModelEvidence)"/>): by the five components of
/// <see cref="RootCauseSegments.CostComponents"/> when the two sides bill the same components, otherwise by the input and
/// output groups of <see cref="RootCauseCostKeys"/>, with each side's input list price in USD per token for the caching effect.
/// </summary>
public sealed class RootCauseCostSplit
{
    /// <summary>The split by component.</summary>
    public static readonly RootCauseCostSplit Detailed = new(false, null, null);

    public RootCauseCostSplit(bool grouped, double? inputListFirst, double? inputListSecond)
    {
        Grouped = grouped;
        InputListFirst = inputListFirst;
        InputListSecond = inputListSecond;
        Caching = grouped && inputListFirst is > 0 && inputListSecond is > 0;

        if (!grouped)
        {
            Keys = RootCauseSegments.CostComponents
                .SelectMany(k => new[] { (k, RootCauseCostKeys.Volume), (k, RootCauseCostKeys.Price) })
                .ToList();
            Components = RootCauseSegments.CostComponents;
        }
        else
        {
            var keys = new List<(string Key, string Effect)> { (RootCauseCostKeys.Input, RootCauseCostKeys.Volume), (RootCauseCostKeys.Input, RootCauseCostKeys.Price) };
            if (Caching) keys.Add((RootCauseCostKeys.Input, RootCauseCostKeys.Caching));
            keys.Add((RootCauseCostKeys.Output, RootCauseCostKeys.Volume));
            keys.Add((RootCauseCostKeys.Output, RootCauseCostKeys.Price));
            Keys = keys;
            Components = RootCauseCostKeys.Groups;
        }
        Effects = Caching
            ? new[] { RootCauseCostKeys.Volume, RootCauseCostKeys.Price, RootCauseCostKeys.Caching }
            : new[] { RootCauseCostKeys.Volume, RootCauseCostKeys.Price };
    }

    /// <summary>The components are the input and output groups.</summary>
    public bool Grouped { get; }

    public double? InputListFirst { get; }
    public double? InputListSecond { get; }

    /// <summary>Grouped with an input list price on both sides: the input group's price effect is split into its list price and caching.</summary>
    public bool Caching { get; }

    /// <summary>The component and effect of each value <see cref="RootCauseDecomposition.CostEffects(IReadOnlyList{RootCauseItem}, IReadOnlyList{RootCauseItem}, RootCauseCostSplit, IReadOnlyList{int}?)"/> returns, in order.</summary>
    public IReadOnlyList<(string Key, string Effect)> Keys { get; }

    /// <summary>The components, in table order: the five cost components, or the two groups.</summary>
    public IReadOnlyList<string> Components { get; }

    /// <summary>The effects of the all-components rows: volume and price, and caching with <see cref="Caching"/>.</summary>
    public IReadOnlyList<string> Effects { get; }

    /// <summary>The sum of the <paramref name="effects"/> of <paramref name="key"/> and <paramref name="effect"/>, each null for any.</summary>
    public double Sum(IReadOnlyList<double> effects, string? key, string? effect)
    {
        ArgumentNullException.ThrowIfNull(effects);
        double sum = 0;
        for (int k = 0; k < Keys.Count && k < effects.Count; k++)
        {
            if ((key == null || Keys[k].Key == key) && (effect == null || Keys[k].Effect == effect)) sum += effects[k];
        }
        return sum;
    }
}

/// <summary>
/// The exact decompositions of the root-cause engine (§ A3): the logarithmic-mean Divisia index (LMDI)
/// of a speed or cost difference between two models, the share of each component of one model's time or
/// cost, the Intelligence Index difference split by the scoring weights, and the points at risk.
///
/// <para>Speed, per question: <c>model time = TTFT + decode + gap</c>, where TTFT = calls × per-call latency
/// and decode = output tokens × (1 / decode rate). Between two models the difference splits into
/// <c>callCount</c>, <c>perCallLatency</c>, <c>outputTokens</c>, <c>decodeRate</c> and <c>gap</c>. Cost:
/// <c>cost = Σ_k tokens_k × price_k</c>, each component's difference split into a volume and a price
/// effect, the price being the effective price (cost ÷ tokens) of the model's own answers, which carries
/// the long-context card and the served tier. A pair whose two sides do not bill the same components
/// (<see cref="Comparable"/>) is split by the input and output groups instead, the input's price effect
/// split again into its list price and caching (<see cref="RootCauseStatistics.Lmdi3"/>). Every split is
/// exact: its effects sum to the difference.
/// Shares carry 95% bootstrap intervals over the shared questions (<see cref="RootCauseStatistics.Seed"/>,
/// <see cref="RootCauseStatistics.Resamples"/>, percentile).</para>
/// </summary>
public static class RootCauseDecomposition
{
    /// <summary>The speed components of a pair, in table order.</summary>
    public static readonly IReadOnlyList<string> SpeedComponents = new[] { "callCount", "perCallLatency", "outputTokens", "decodeRate", "gap" };

    /// <summary>A pair decomposition needs at least this many shared questions.</summary>
    public const int MinSharedQuestions = 5;

    /// <summary>Why a pair whose difference is not established carries no components.</summary>
    public const string NotEstablishedReason = "Not decomposed: the difference is not established.";

    public static string SpeedComponentLabel(string key) => key switch
    {
        "callCount" => "number of model calls",
        "perCallLatency" => "latency per call (time to first token)",
        "outputTokens" => "output tokens",
        "decodeRate" => "decode rate",
        "gap" => "time outside the calls",
        _ => key
    };

    // ---------------------------------------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------------------------------------

    /// <summary>The speed effects of one set of shared items, first minus second, in <see cref="SpeedComponents"/> order, and the total.</summary>
    public static (double[] Effects, double Total, double FirstTotal, double SecondTotal) SpeedEffects(
        IReadOnlyList<RootCauseItem> first, IReadOnlyList<RootCauseItem> second, IReadOnlyList<int>? sample = null)
    {
        var a = Means(first, sample);
        var b = Means(second, sample);

        // TTFT = calls × per-call latency; decode = tokens × (1 / rate).
        var (callCount, perCall) = RootCauseStatistics.Lmdi(a.Ttft, b.Ttft, a.Calls, b.Calls);
        var (tokens, rate) = RootCauseStatistics.Lmdi(a.Decode, b.Decode, a.Tokens, b.Tokens);
        double total = a.Model - b.Model;
        double gap = total - callCount - perCall - tokens - rate;
        return (new[] { callCount, perCall, tokens, rate, gap }, total, a.Model, b.Model);
    }

    /// <summary>
    /// The speed decomposition of two models over the questions both have candidate telemetry for, with a
    /// bootstrap interval for each component's share and for the difference. Without
    /// <paramref name="decompose"/>, only the difference and its interval: the pair is marked
    /// <see cref="RootCausePairDecomposition.NotEstablished"/> and carries no component.
    /// </summary>
    public static RootCausePairDecomposition Speed(
        string firstToken, RootCauseModelEvidence first, string secondToken, RootCauseModelEvidence second, bool decompose = true)
    {
        var result = new RootCausePairDecomposition { Axis = RootCauseAxes.Speed, First = firstToken, Second = secondToken };
        var (a, b) = Shared(first.SpeedItems, second.SpeedItems);
        result.SharedQuestions = a.Count;

        if (first.SpeedItems.Count == 0 || second.SpeedItems.Count == 0)
        {
            result.UnavailableReason = RootCauseEvidence.NoTelemetryReason.Replace("this model", first.SpeedItems.Count == 0 ? "the first model" : "the second model", StringComparison.Ordinal);
            return result;
        }
        if (a.Count < MinSharedQuestions)
        {
            result.UnavailableReason = "Fewer than five questions have candidate telemetry for both models.";
            return result;
        }

        var (effects, total, firstTotal, secondTotal) = SpeedEffects(a, b);
        result.Difference = total;
        result.FirstTotal = firstTotal;
        result.SecondTotal = secondTotal;
        if (!decompose) return DifferenceOnly(result, a.Count, sample => SpeedEffects(a, b, sample).Total);

        var intervals = RootCauseStatistics.Bootstrap(a.Count, effects.Length + 1, sample =>
        {
            var (e, t, _, _) = SpeedEffects(a, b, sample);
            var values = new double?[e.Length + 1];
            for (int k = 0; k < e.Length; k++) values[k] = t == 0 ? null : e[k] / t;
            values[e.Length] = t;
            return values;
        });

        for (int k = 0; k < effects.Length; k++)
        {
            result.Components.Add(new RootCauseComponent
            {
                Key = SpeedComponents[k],
                Label = SpeedComponentLabel(SpeedComponents[k]),
                Amount = effects[k],
                Share = total == 0 ? null : effects[k] / total,
                ShareLower = intervals[k]?.Lower,
                ShareUpper = intervals[k]?.Upper
            });
        }
        result.DifferenceLower = intervals[effects.Length]?.Lower;
        result.DifferenceUpper = intervals[effects.Length]?.Upper;
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Cost
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The cost effects of one set of shared items, first minus second: per component, the volume effect
    /// then the price effect (10 values in <see cref="RootCauseSegments.CostComponents"/> order), and the total.
    /// </summary>
    public static (double[] Effects, double Total, double FirstTotal, double SecondTotal) CostEffects(
        IReadOnlyList<RootCauseItem> first, IReadOnlyList<RootCauseItem> second, IReadOnlyList<int>? sample = null)
    {
        var indices = sample ?? Enumerable.Range(0, first.Count).ToList();
        var effects = new double[10];
        double firstTotal = 0, secondTotal = 0;
        for (int k = 0; k < 5; k++)
        {
            double tokensA = indices.Average(i => first[i].Tokens[k]);
            double tokensB = indices.Average(i => second[i].Tokens[k]);
            double costA = indices.Average(i => first[i].Cost[k]);
            double costB = indices.Average(i => second[i].Cost[k]);
            var (volume, price) = RootCauseStatistics.Lmdi(costA, costB, tokensA, tokensB);
            effects[2 * k] = volume;
            effects[2 * k + 1] = price;
            firstTotal += costA;
            secondTotal += costB;
        }
        return (effects, firstTotal - secondTotal, firstTotal, secondTotal);
    }

    /// <summary>
    /// The cost effects of one set of shared items under <paramref name="split"/>, first minus second, in
    /// <see cref="RootCauseCostSplit.Keys"/> order, and the total. A detailed split is <see cref="CostEffects(IReadOnlyList{RootCauseItem}, IReadOnlyList{RootCauseItem}, IReadOnlyList{int}?)"/>;
    /// a grouped one splits the input group (uncached input, cache reads and writes) into volume, list price and,
    /// with both list prices, caching, and the output group (output and reasoning) into volume and price.
    /// </summary>
    public static (double[] Effects, double Total, double FirstTotal, double SecondTotal) CostEffects(
        IReadOnlyList<RootCauseItem> first, IReadOnlyList<RootCauseItem> second, RootCauseCostSplit split, IReadOnlyList<int>? sample = null)
    {
        ArgumentNullException.ThrowIfNull(split);
        if (!split.Grouped) return CostEffects(first, second, sample);

        var indices = sample ?? Enumerable.Range(0, first.Count).ToList();
        // The first three cost components are the input, the last two the output.
        static double Input(double[] v) => v[0] + v[1] + v[2];
        static double Output(double[] v) => v[3] + v[4];
        double inTokensA = indices.Average(i => Input(first[i].Tokens)), inTokensB = indices.Average(i => Input(second[i].Tokens));
        double inCostA = indices.Average(i => Input(first[i].Cost)), inCostB = indices.Average(i => Input(second[i].Cost));
        double outTokensA = indices.Average(i => Output(first[i].Tokens)), outTokensB = indices.Average(i => Output(second[i].Tokens));
        double outCostA = indices.Average(i => Output(first[i].Cost)), outCostB = indices.Average(i => Output(second[i].Cost));

        var effects = new List<double>(5);
        if (split.Caching)
        {
            var (volume, list, caching) = RootCauseStatistics.Lmdi3(inCostA, inCostB, inTokensA, inTokensB, split.InputListFirst!.Value, split.InputListSecond!.Value);
            effects.Add(volume);
            effects.Add(list);
            effects.Add(caching);
        }
        else
        {
            var (volume, price) = RootCauseStatistics.Lmdi(inCostA, inCostB, inTokensA, inTokensB);
            effects.Add(volume);
            effects.Add(price);
        }
        var (outVolume, outPrice) = RootCauseStatistics.Lmdi(outCostA, outCostB, outTokensA, outTokensB);
        effects.Add(outVolume);
        effects.Add(outPrice);

        double firstTotal = inCostA + outCostA, secondTotal = inCostB + outCostB;
        return (effects.ToArray(), firstTotal - secondTotal, firstTotal, secondTotal);
    }

    /// <summary>
    /// Whether two sides bill the same components over their shared items: each of the five components has positive
    /// mean tokens on both sides or on neither, and both sides report reasoning apart from output or neither does.
    /// </summary>
    public static bool Comparable(IReadOnlyList<RootCauseItem> first, IReadOnlyList<RootCauseItem> second, bool firstReasoningReported, bool secondReasoningReported)
    {
        if (firstReasoningReported != secondReasoningReported) return false;
        if (first.Count == 0 || second.Count == 0) return true;
        for (int k = 0; k < RootCauseSegments.CostComponents.Count; k++)
        {
            if ((first.Average(i => i.Tokens[k]) > 0) != (second.Average(i => i.Tokens[k]) > 0)) return false;
        }
        return true;
    }

    /// <summary>How the cost of two models is split over the questions both have token counts for (<see cref="Comparable"/>).</summary>
    public static RootCauseCostSplit CostSplitOf(RootCauseModelEvidence first, RootCauseModelEvidence second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var (a, b) = Shared(first.CostItems, second.CostItems);
        return CostSplitOf(a, b, first, second);
    }

    private static RootCauseCostSplit CostSplitOf(IReadOnlyList<RootCauseItem> a, IReadOnlyList<RootCauseItem> b, RootCauseModelEvidence first, RootCauseModelEvidence second)
        => Comparable(a, b, first.ReasoningReported, second.ReasoningReported)
            ? RootCauseCostSplit.Detailed
            : new RootCauseCostSplit(true, InputListPrice(first), InputListPrice(second));

    /// <summary>The card's uncached input price in USD per token, the input group's list price; null without one.</summary>
    private static double? InputListPrice(RootCauseModelEvidence evidence)
        => evidence.PricesPerMillion.Length > 0 && evidence.PricesPerMillion[0] is double perMillion ? perMillion / 1_000_000 : null;

    /// <summary>
    /// <c>exp(effect ÷ L(firstTotal, secondTotal))</c>: the factor of the cost ratio that <paramref name="effect"/>, an
    /// LMDI effect of the pair, accounts for. Null when either total is missing or not positive.
    /// </summary>
    public static double? EffectRatio(double effect, double? firstTotal, double? secondTotal)
    {
        if (firstTotal is not double t1 || secondTotal is not double t2) return null;
        double l = RootCauseStatistics.LogMean(t1, t2);
        return l > 0 ? Math.Exp(effect / l) : null;
    }

    /// <summary>
    /// The cost decomposition of two models over the questions both have token counts and prices for, by
    /// component or, when the two do not bill the same components, by the input and output groups
    /// (<see cref="CostSplitOf(RootCauseModelEvidence, RootCauseModelEvidence)"/>), with its
    /// <see cref="RootCausePairDecomposition.VolumeRatio"/>; without <paramref name="decompose"/>, only the
    /// difference and its interval, as <see cref="Speed"/>.
    /// </summary>
    public static RootCausePairDecomposition Cost(
        string firstToken, RootCauseModelEvidence first, string secondToken, RootCauseModelEvidence second, bool decompose = true)
    {
        var result = new RootCausePairDecomposition { Axis = RootCauseAxes.Cost, First = firstToken, Second = secondToken };
        if (!first.Priced || !second.Priced)
        {
            result.UnavailableReason = RootCauseEvidence.NoPriceReason.Replace("this model", !first.Priced ? "the first model" : "the second model", StringComparison.Ordinal);
            return result;
        }

        var (a, b) = Shared(first.CostItems, second.CostItems);
        result.SharedQuestions = a.Count;
        if (a.Count < MinSharedQuestions)
        {
            result.UnavailableReason = "Fewer than five questions have token counts for both models.";
            return result;
        }

        var split = CostSplitOf(a, b, first, second);
        var (effects, total, firstTotal, secondTotal) = CostEffects(a, b, split);
        result.Difference = total;
        result.FirstTotal = firstTotal;
        result.SecondTotal = secondTotal;
        if (!decompose) return DifferenceOnly(result, a.Count, sample => CostEffects(a, b, split, sample).Total);

        // Each component's share, then each all-components effect's, then the difference.
        var keys = split.Keys;
        var allEffects = split.Effects;
        int width = keys.Count + allEffects.Count + 1;
        var intervals = RootCauseStatistics.Bootstrap(a.Count, width, sample =>
        {
            var (e, t, _, _) = CostEffects(a, b, split, sample);
            var values = new double?[width];
            for (int k = 0; k < e.Length; k++) values[k] = t == 0 ? null : e[k] / t;
            for (int j = 0; j < allEffects.Count; j++) values[keys.Count + j] = t == 0 ? null : split.Sum(e, null, allEffects[j]) / t;
            values[width - 1] = t;
            return values;
        });

        for (int k = 0; k < keys.Count; k++)
        {
            result.Components.Add(new RootCauseComponent
            {
                Key = keys[k].Key,
                Effect = keys[k].Effect,
                Label = RootCauseCostKeys.Label(keys[k].Key, keys[k].Effect, split.Grouped),
                Amount = effects[k],
                Share = total == 0 ? null : effects[k] / total,
                ShareLower = intervals[k]?.Lower,
                ShareUpper = intervals[k]?.Upper
            });
        }

        for (int j = 0; j < allEffects.Count; j++)
        {
            double amount = split.Sum(effects, null, allEffects[j]);
            result.Components.Add(new RootCauseComponent
            {
                Key = RootCauseCostKeys.All,
                Effect = allEffects[j],
                Label = RootCauseCostKeys.Label(RootCauseCostKeys.All, allEffects[j], split.Grouped),
                Amount = amount,
                Share = total == 0 ? null : amount / total,
                ShareLower = intervals[keys.Count + j]?.Lower,
                ShareUpper = intervals[keys.Count + j]?.Upper
            });
        }
        result.DifferenceLower = intervals[width - 1]?.Lower;
        result.DifferenceUpper = intervals[width - 1]?.Upper;
        result.Grouped = split.Grouped;
        result.VolumeRatio = EffectRatio(split.Sum(effects, null, RootCauseCostKeys.Volume), firstTotal, secondTotal);
        return result;
    }

    /// <summary>
    /// The pair with the difference's bootstrap interval and no component, marked not established. The
    /// resamples are the ones a full decomposition draws, so the interval is the same as its own.
    /// </summary>
    private static RootCausePairDecomposition DifferenceOnly(RootCausePairDecomposition result, int count, Func<IReadOnlyList<int>, double> total)
    {
        var interval = RootCauseStatistics.Bootstrap(count, sample => total(sample));
        result.DifferenceLower = interval?.Lower;
        result.DifferenceUpper = interval?.Upper;
        result.NotEstablished = true;
        result.UnavailableReason = NotEstablishedReason;
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // One model's own time and cost
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Mean milliseconds per question of a model's wall time by segment (<see cref="RootCauseSegments.Time"/>):
    /// the wait before output summed over the calls, output generation, tool execution, own waits and retries, and the rest of the
    /// turn. <c>other</c> is what the turn spent outside the four, floored at zero, so the segments sum to
    /// their own total. Null without a question with candidate telemetry.
    /// </summary>
    public static double[]? TimeSegments(IReadOnlyList<RootCauseItem> items, IReadOnlyList<int>? sample = null)
    {
        if (items.Count == 0) return null;
        var indices = sample ?? Enumerable.Range(0, items.Count).ToList();
        double ttft = indices.Average(i => items[i].TtftMs);
        double decode = indices.Average(i => items[i].DecodeMs);
        double tools = indices.Average(i => items[i].ToolMs);
        double waits = indices.Average(i => items[i].WaitMs);
        double duration = indices.Average(i => items[i].DurationMs);
        double other = Math.Max(0, duration - ttft - decode - tools - waits);
        return new[] { ttft, decode, tools, waits, other };
    }

    /// <summary>The model-time components of one model (TTFT, decode, gap) as shares of its model time, with intervals.</summary>
    public static (double[] Shares, RootCauseInterval?[] Intervals, RootCauseInterval?[] LeadOverNext)? ModelTimeShares(IReadOnlyList<RootCauseItem> items)
    {
        if (items.Count == 0) return null;

        double?[] Shares(IReadOnlyList<int>? sample)
        {
            var indices = sample ?? Enumerable.Range(0, items.Count).ToList();
            double model = indices.Average(i => items[i].ModelTimeMs);
            if (model <= 0) return new double?[3];
            double ttft = indices.Average(i => items[i].TtftMs);
            double decode = indices.Average(i => items[i].DecodeMs);
            return new double?[] { ttft / model, decode / model, (model - ttft - decode) / model };
        }

        var point = Shares(null);
        if (point.Any(s => !s.HasValue)) return null;
        var shares = point.Select(s => s!.Value).ToArray();
        int lead = Array.IndexOf(shares, shares.Max());
        int next = Enumerable.Range(0, 3).Where(k => k != lead).OrderByDescending(k => shares[k]).First();

        var intervals = RootCauseStatistics.Bootstrap(items.Count, 4, sample =>
        {
            var s = Shares(sample);
            return new[] { s[0], s[1], s[2], s[lead] - s[next] };
        });
        return (shares, intervals.Take(3).ToArray(), new[] { intervals[3] });
    }

    /// <summary>
    /// The cost components of one model as shares of its cost per question, with intervals, and the
    /// interval of the largest share's lead over the next. Null without a costed question.
    /// </summary>
    public static (double[] Shares, RootCauseInterval?[] Intervals, RootCauseInterval? LeadOverNext, int Lead)? CostShares(IReadOnlyList<RootCauseItem> items)
    {
        if (items.Count == 0) return null;

        double?[] Shares(IReadOnlyList<int>? sample)
        {
            var indices = sample ?? Enumerable.Range(0, items.Count).ToList();
            double total = indices.Average(i => items[i].TotalCost);
            if (total <= 0) return new double?[5];
            return Enumerable.Range(0, 5).Select(k => (double?)(indices.Average(i => items[i].Cost[k]) / total)).ToArray();
        }

        var point = Shares(null);
        if (point.Any(s => !s.HasValue)) return null;
        var shares = point.Select(s => s!.Value).ToArray();
        int lead = Array.IndexOf(shares, shares.Max());
        int next = Enumerable.Range(0, 5).Where(k => k != lead).OrderByDescending(k => shares[k]).First();

        var intervals = RootCauseStatistics.Bootstrap(items.Count, 6, sample =>
        {
            var s = Shares(sample);
            return new[] { s[0], s[1], s[2], s[3], s[4], s[lead] - s[next] };
        });
        return (shares, intervals.Take(5).ToArray(), intervals[5], lead);
    }

    // ---------------------------------------------------------------------------------------------
    // Intelligence
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The Intelligence Index difference of two models split by the scoring weights. The index is a
    /// weighted geometric mean of the dimension scores, so the split is the LMDI of
    /// <c>G = Π dimension^weight</c> at the models' mean dimension scores: dimension <c>d</c> contributes
    /// <c>L(G₁, G₂) · w_d · ln(dim₁ / dim₂)</c>, and <c>other</c> is the remainder (the critical-error cap,
    /// difficulty weighting and the spread of the answers), so the parts sum exactly to the difference.
    /// Null when a dimension mean is missing.
    /// </summary>
    public static RootCauseQualitySplit? QualitySplit(
        string firstToken, double firstIndex, IReadOnlyList<double?> firstDimensions,
        string secondToken, double secondIndex, IReadOnlyList<double?> secondDimensions,
        IReadOnlyList<double> weights)
    {
        if (firstDimensions.Count != 4 || secondDimensions.Count != 4 || weights.Count != 4) return null;
        if (firstDimensions.Any(d => !d.HasValue) || secondDimensions.Any(d => !d.HasValue)) return null;

        var a = firstDimensions.Select(d => Math.Max(1.0, d!.Value)).ToArray();
        var b = secondDimensions.Select(d => Math.Max(1.0, d!.Value)).ToArray();
        double ga = Enumerable.Range(0, 4).Aggregate(1.0, (g, k) => g * Math.Pow(a[k], weights[k]));
        double gb = Enumerable.Range(0, 4).Aggregate(1.0, (g, k) => g * Math.Pow(b[k], weights[k]));
        double l = RootCauseStatistics.LogMean(ga, gb);

        var split = new RootCauseQualitySplit { First = firstToken, Second = secondToken, IndexDifference = firstIndex - secondIndex };
        double sum = 0;
        string[] names = { "accuracy", "completeness", "conciseness", "readability" };
        for (int k = 0; k < 4; k++)
        {
            double points = l * weights[k] * Math.Log(a[k] / b[k]);
            sum += points;
            split.Components.Add(new RootCauseComponent
            {
                Key = names[k],
                Label = char.ToUpperInvariant(names[k][0]) + names[k][1..],
                Amount = points,
                Share = split.IndexDifference == 0 ? null : points / split.IndexDifference
            });
        }
        double other = split.IndexDifference - sum;
        split.Components.Add(new RootCauseComponent
        {
            Key = "other",
            Label = "Critical-error cap, difficulty weighting and spread",
            Amount = other,
            Share = split.IndexDifference == 0 ? null : other / split.IndexDifference
        });
        return split;
    }

    /// <summary>
    /// The index points at risk of <paramref name="answers"/>, measured against <paramref name="median"/>,
    /// the model's own median answer quality: each scored answer below it loses <c>median − quality</c>,
    /// counted once under its strongest signal (confirmed critical error, then budget, truncation or output
    /// defects, then no tool call, then other), divided by the scored answers. An upper bound of what each
    /// signal costs, not a causal estimate.
    /// </summary>
    public static RootCauseAtRisk AtRisk(IReadOnlyList<RootCauseAnswerRecord> answers, double? median)
    {
        var scored = answers.Where(a => a.Counting && a.Quality.HasValue).ToList();
        var points = new double[4];
        var byQuestion = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (scored.Count == 0 || median is not double m) return new RootCauseAtRisk { Median = median, Answers = scored.Count, Points = points, ByQuestion = byQuestion };

        foreach (var answer in scored)
        {
            double lost = Math.Max(0, m - answer.Quality!.Value) / scored.Count;
            if (lost <= 0) continue;
            int segment = answer.CriticalError ? 2 : answer.BudgetOrDefect ? 1 : answer.NoTool ? 0 : 3;
            points[segment] += lost;
            if (!byQuestion.TryGetValue(answer.QuestionKey, out var row)) byQuestion[answer.QuestionKey] = row = new double[4];
            row[segment] += lost;
        }
        return new RootCauseAtRisk { Median = m, Answers = scored.Count, Points = points, ByQuestion = byQuestion };
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The items both sides have, in key order, aligned.</summary>
    public static (List<RootCauseItem> First, List<RootCauseItem> Second) Shared(IReadOnlyList<RootCauseItem> first, IReadOnlyList<RootCauseItem> second)
    {
        var byKey = second.ToDictionary(i => i.Key, StringComparer.Ordinal);
        var a = new List<RootCauseItem>();
        var b = new List<RootCauseItem>();
        foreach (var item in first.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            if (!byKey.TryGetValue(item.Key, out var other)) continue;
            a.Add(item);
            b.Add(other);
        }
        return (a, b);
    }

    private readonly record struct SpeedMeans(double Model, double Ttft, double Decode, double Calls, double Tokens);

    private static SpeedMeans Means(IReadOnlyList<RootCauseItem> items, IReadOnlyList<int>? sample)
    {
        var indices = sample ?? Enumerable.Range(0, items.Count).ToList();
        return new SpeedMeans(
            indices.Average(i => items[i].ModelTimeMs),
            indices.Average(i => items[i].TtftMs),
            indices.Average(i => items[i].DecodeMs),
            indices.Average(i => items[i].Calls),
            indices.Average(i => items[i].OutputTokens));
    }
}
