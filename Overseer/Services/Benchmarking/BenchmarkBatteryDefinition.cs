namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

/// <summary>One suite of a battery definition, at its 0-based position in run order.</summary>
public sealed record BenchmarkBatteryDefinitionSuite(int Index, long SuiteId, string SuiteName, double? CustomWeight);

/// <summary>
/// What a suite contributes to the weights: its exam question count, and the sum of those
/// questions' difficulty weights (<c>D_s</c>).
/// </summary>
public sealed record BenchmarkBatterySuiteMass(int ItemCount, double DifficultyMass);

/// <summary>
/// The definition of a battery as one battery run executes it: the suites in run order, the
/// weighting scheme and the declared custom weights. A battery run stores this snapshot as JSON,
/// so editing the battery later never changes an existing result.
///
/// <para>The static members are the weighting rules of the method (Statistical Method M2, M5, M6
/// and M9). Pure computation: no I/O, no writes.</para>
/// </summary>
public sealed record BenchmarkBatteryDefinition(
    long? BatteryId,
    string Name,
    int Revision,
    BenchmarkBatteryWeightingScheme Scheme,
    IReadOnlyList<BenchmarkBatteryDefinitionSuite> Suites)
{
    /// <summary>A battery needs at least two suites; with one, the composite would be a suite index.</summary>
    public const int MinSuiteCount = 2;

    /// <summary>The weight of a question whose difficulty was never assessed.</summary>
    public const double DefaultQuestionWeight = 50.0;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>The declared custom weights, in suite order; all null unless the scheme is Custom.</summary>
    [JsonIgnore]
    public IReadOnlyList<double?> CustomWeights => Suites.Select(s => s.CustomWeight).ToList();

    /// <summary>The definition hash of this snapshot (M9).</summary>
    [JsonIgnore]
    public string DefinitionSha256 => ComputeSha256(Scheme, Suites.Select(s => (s.SuiteId, s.CustomWeight)));

    // --- Construction and serialization ----------------------------------------------------------

    /// <summary>
    /// The definition of a battery entity, suites in <see cref="BenchmarkBatterySuite.OrderIndex"/>
    /// order and indexed from 0. An unsaved battery (id 0) has a null <see cref="BatteryId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The battery fails <see cref="Validate(BenchmarkBattery)"/>.</exception>
    public static BenchmarkBatteryDefinition FromEntity(BenchmarkBattery battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        var errors = Validate(battery);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Battery '{battery.Name}' is not valid: {string.Join(" ", errors)}");
        }

        var suites = OrderedSuites(battery)
            .Select((s, i) => new BenchmarkBatteryDefinitionSuite(i, s.BenchmarkSuiteId!.Value, s.SuiteName, s.CustomWeight))
            .ToList();

        return new BenchmarkBatteryDefinition(
            battery.Id == 0 ? null : battery.Id,
            battery.Name,
            battery.Revision,
            battery.WeightingScheme,
            suites);
    }

    /// <summary>Serializes with camelCase property names, as stored in <c>DefinitionJson</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <exception cref="JsonException">The text is not a battery definition.</exception>
    public static BenchmarkBatteryDefinition FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("A battery definition cannot be read from empty text.");
        }

        var definition = JsonSerializer.Deserialize<BenchmarkBatteryDefinition>(json, SerializerOptions)
            ?? throw new JsonException("The battery definition JSON is null.");

        return definition with
        {
            Name = definition.Name ?? string.Empty,
            Suites = definition.Suites ?? Array.Empty<BenchmarkBatteryDefinitionSuite>()
        };
    }

    // --- Definition hash (M9) --------------------------------------------------------------------

    /// <summary>
    /// SHA-256, lower-case hex, over <c>scheme=&lt;int&gt;</c> followed by one <c>suiteId:weight</c>
    /// line per suite in ascending suite id, the weight rendered round-trip in the invariant culture
    /// or <c>-</c> when null, lines joined by <c>\n</c>. Suite order and names are excluded; custom
    /// weights are hashed as declared, not normalized.
    /// </summary>
    public static string ComputeSha256(
        BenchmarkBatteryWeightingScheme scheme,
        IEnumerable<(long SuiteId, double? CustomWeight)> suites)
    {
        ArgumentNullException.ThrowIfNull(suites);

        var lines = new List<string> { "scheme=" + ((int)scheme).ToString(CultureInfo.InvariantCulture) };
        lines.AddRange(suites
            .OrderBy(s => s.SuiteId)
            .ThenBy(s => s.CustomWeight ?? double.NegativeInfinity)
            .Select(s => s.SuiteId.ToString(CultureInfo.InvariantCulture) + ":"
                + (s.CustomWeight.HasValue
                    ? s.CustomWeight.Value.ToString("R", CultureInfo.InvariantCulture)
                    : "-")));

        return Sha256Hex(string.Join("\n", lines));
    }

    /// <summary>The definition hash of a battery entity.</summary>
    /// <exception cref="InvalidOperationException">A suite of the battery has been deleted.</exception>
    public static string ComputeSha256(BenchmarkBattery battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        var deleted = battery.Suites.FirstOrDefault(s => !s.BenchmarkSuiteId.HasValue);
        if (deleted != null)
        {
            throw new InvalidOperationException(
                $"Battery '{battery.Name}' cannot be hashed: suite '{deleted.SuiteName}' has been deleted.");
        }

        return ComputeSha256(
            battery.WeightingScheme,
            battery.Suites.Select(s => (s.BenchmarkSuiteId!.Value, s.CustomWeight)));
    }

    // --- Validation ------------------------------------------------------------------------------

    /// <summary>The problems that make a battery unusable; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(BenchmarkBattery battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        return Validate(
            battery.WeightingScheme,
            OrderedSuites(battery).Select(s => (s.BenchmarkSuiteId, s.SuiteName, s.CustomWeight)).ToList());
    }

    /// <summary>
    /// The problems that make a battery definition unusable; empty when it is valid. At least
    /// <see cref="MinSuiteCount"/> suites; every suite present (a deleted suite, with a null id,
    /// fails by name); no suite twice; under Custom every weight finite and above zero; under any
    /// other scheme no weight at all.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        BenchmarkBatteryWeightingScheme scheme,
        IReadOnlyList<(long? SuiteId, string SuiteName, double? CustomWeight)> suites)
    {
        suites ??= Array.Empty<(long?, string, double?)>();
        var errors = new List<string>();

        if (!Enum.IsDefined(scheme))
        {
            errors.Add($"Unknown weighting scheme {(int)scheme}.");
        }

        if (suites.Count < MinSuiteCount)
        {
            errors.Add($"A battery needs at least {MinSuiteCount} suites; this one has {suites.Count}.");
        }

        foreach (var suite in suites.Where(s => !s.SuiteId.HasValue))
        {
            errors.Add($"Suite '{suite.SuiteName}' has been deleted; remove or replace it.");
        }

        foreach (var duplicate in suites
                     .Where(s => s.SuiteId.HasValue)
                     .GroupBy(s => s.SuiteId!.Value)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Suite '{duplicate.First().SuiteName}' (#{duplicate.Key}) appears {duplicate.Count()} times.");
        }

        if (scheme == BenchmarkBatteryWeightingScheme.Custom)
        {
            foreach (var suite in suites.Where(s => !IsValidCustomWeight(s.CustomWeight)))
            {
                errors.Add($"Suite '{suite.SuiteName}' needs a custom weight that is a finite number above zero.");
            }
        }
        else
        {
            foreach (var suite in suites.Where(s => s.CustomWeight.HasValue))
            {
                errors.Add($"Suite '{suite.SuiteName}' has a custom weight, which only the Custom scheme uses.");
            }
        }

        return errors;
    }

    // --- Weights (M2, M5, M6) --------------------------------------------------------------------

    /// <summary>
    /// The weight of one exam question in a suite's difficulty mass: its assessed difficulty, else
    /// <see cref="DefaultQuestionWeight"/>, never below 1 — the rule
    /// <see cref="BenchmarkGroupItemStatistics.Weight"/> falls back to.
    /// </summary>
    public static double QuestionWeight(int? assessedDifficulty)
        => Math.Max(1.0, assessedDifficulty ?? DefaultQuestionWeight);

    /// <summary>
    /// The suite weights <c>w_s</c> under <paramref name="scheme"/>, normalized to sum to 1, in the
    /// order of <paramref name="perSuite"/>: difficulty mass <c>D_s / D</c>, item count
    /// <c>n_s / Σn</c>, equal <c>1 / K</c>, or the custom weights normalized.
    /// </summary>
    /// <param name="customWeights">One weight per suite; read only under Custom.</param>
    /// <exception cref="ArgumentException">
    /// No suites; a negative or non-finite mass; a zero total under a mass-based scheme; or, under
    /// Custom, custom weights missing, of the wrong count, or not finite and above zero.
    /// </exception>
    public static IReadOnlyList<double> Weights(
        BenchmarkBatteryWeightingScheme scheme,
        IReadOnlyList<BenchmarkBatterySuiteMass> perSuite,
        IReadOnlyList<double?>? customWeights)
    {
        ArgumentNullException.ThrowIfNull(perSuite);
        if (perSuite.Count == 0)
        {
            throw new ArgumentException("Weights need at least one suite.", nameof(perSuite));
        }

        return scheme switch
        {
            BenchmarkBatteryWeightingScheme.DifficultyMass => Normalize(perSuite.Select(m => CheckedMass(m.DifficultyMass)).ToList(), nameof(perSuite)),
            BenchmarkBatteryWeightingScheme.ItemCount => Normalize(perSuite.Select(m => CheckedMass(m.ItemCount)).ToList(), nameof(perSuite)),
            BenchmarkBatteryWeightingScheme.Equal => Enumerable.Repeat(1.0 / perSuite.Count, perSuite.Count).ToList(),
            BenchmarkBatteryWeightingScheme.Custom => CustomNormalized(perSuite.Count, customWeights),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown weighting scheme.")
        };
    }

    /// <summary>
    /// The count weights <c>v_s</c> (M5, M6): question-count weights under Difficulty mass and Item
    /// count, and the declared weights unchanged under Equal and Custom. Dimensions, the
    /// critical-error rate and the Overall Speed Index are composed with these, because each is an
    /// unweighted mean within a suite.
    /// </summary>
    public static IReadOnlyList<double> CountWeights(
        BenchmarkBatteryWeightingScheme scheme,
        IReadOnlyList<BenchmarkBatterySuiteMass> perSuite,
        IReadOnlyList<double?>? customWeights)
    {
        var effective = scheme == BenchmarkBatteryWeightingScheme.DifficultyMass
            ? BenchmarkBatteryWeightingScheme.ItemCount
            : scheme;

        return Weights(effective, perSuite, customWeights);
    }

    /// <summary>
    /// A suite's mass over its exam (<see cref="BenchmarkRunExam.Build"/> over the suite's member
    /// runs): the exam question count, and the sum over every exam question of its item row's
    /// <see cref="BenchmarkGroupItemStatistics.Weight"/> when <paramref name="statistics"/> has one,
    /// else <see cref="QuestionWeight"/> of the exam question. Unscored questions therefore count,
    /// and the mass never depends on how well a model answered.
    /// </summary>
    public static BenchmarkBatterySuiteMass SuiteMass(
        IReadOnlyList<BenchmarkQuestion> examQuestions,
        BenchmarkGroupStatisticsResult? statistics)
    {
        examQuestions ??= Array.Empty<BenchmarkQuestion>();

        var itemWeights = (statistics?.Items ?? Array.Empty<BenchmarkGroupItemStatistics>())
            .GroupBy(i => i.QuestionId)
            .ToDictionary(g => g.Key, g => g.First().Weight);

        double mass = examQuestions.Sum(q => itemWeights.TryGetValue(q.Id, out double weight)
            ? weight
            : QuestionWeight(q.AssessedDifficulty));

        return new BenchmarkBatterySuiteMass(examQuestions.Count, mass);
    }

    /// <summary>
    /// The battery editor's preview of the declared weights, from each suite's current questions'
    /// assessed difficulties (null for an unassessed question) under the per-question rule of
    /// <see cref="QuestionWeight"/>. The analysis records the weights it actually used, from the
    /// exams the runs sat.
    ///
    /// <para>Empty when the weights are undefined: no suites, every suite empty under a mass-based
    /// scheme, or custom weights that <c>Validate</c> would refuse.</para>
    /// </summary>
    public static IReadOnlyList<double> PreviewWeights(
        BenchmarkBatteryWeightingScheme scheme,
        IReadOnlyList<IReadOnlyList<int?>> currentDifficulties,
        IReadOnlyList<double?>? customWeights)
    {
        if (currentDifficulties == null || currentDifficulties.Count == 0) return Array.Empty<double>();

        var masses = currentDifficulties
            .Select(d => d ?? Array.Empty<int?>())
            .Select(d => new BenchmarkBatterySuiteMass(d.Count, d.Sum(q => QuestionWeight(q))))
            .ToList();

        try
        {
            return Weights(scheme, masses, customWeights);
        }
        catch (ArgumentException)
        {
            return Array.Empty<double>();
        }
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static IEnumerable<BenchmarkBatterySuite> OrderedSuites(BenchmarkBattery battery)
        => (battery.Suites ?? new List<BenchmarkBatterySuite>()).OrderBy(s => s.OrderIndex).ThenBy(s => s.Id);

    private static bool IsValidCustomWeight(double? weight)
        => weight.HasValue && double.IsFinite(weight.Value) && weight.Value > 0;

    private static double CheckedMass(double mass)
    {
        if (!double.IsFinite(mass) || mass < 0)
        {
            throw new ArgumentException($"A suite mass must be a finite, non-negative number; got {mass}.");
        }

        return mass;
    }

    private static IReadOnlyList<double> Normalize(IReadOnlyList<double> raw, string paramName)
    {
        double total = raw.Sum();
        if (!(total > 0))
        {
            throw new ArgumentException("The weights are undefined: every suite's mass is zero.", paramName);
        }

        return raw.Select(v => v / total).ToList();
    }

    private static IReadOnlyList<double> CustomNormalized(int suiteCount, IReadOnlyList<double?>? customWeights)
    {
        if (customWeights == null || customWeights.Count != suiteCount)
        {
            throw new ArgumentException(
                $"Custom weighting needs one weight per suite: {suiteCount} suites, "
                + $"{customWeights?.Count.ToString(CultureInfo.InvariantCulture) ?? "no"} weights.",
                nameof(customWeights));
        }

        if (customWeights.Any(w => !IsValidCustomWeight(w)))
        {
            throw new ArgumentException("Every custom weight must be a finite number above zero.", nameof(customWeights));
        }

        return Normalize(customWeights.Select(w => w!.Value).ToList(), nameof(customWeights));
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
