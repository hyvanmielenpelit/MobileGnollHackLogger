namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The battery definition: its weights under every scheme (M2, M5, M6), the suite mass, its
/// validation, its JSON snapshot and its hash (M9).
/// </summary>
public class BenchmarkBatteryDefinitionTests
{
    private const double Tolerance = 1e-12;

    private static readonly BenchmarkBatterySuiteMass[] ThreeSuites =
    {
        new(10, 400.0),
        new(10, 200.0),
        new(5, 200.0)
    };

    private static BenchmarkBattery Battery(
        BenchmarkBatteryWeightingScheme scheme = BenchmarkBatteryWeightingScheme.DifficultyMass,
        params (long? SuiteId, string Name, int OrderIndex, double? Weight)[] suites)
    {
        var battery = new BenchmarkBattery { Id = 7, Name = "Core", Revision = 3, WeightingScheme = scheme };
        long rowId = 1;
        foreach (var suite in suites)
        {
            battery.Suites.Add(new BenchmarkBatterySuite
            {
                Id = rowId++,
                BenchmarkSuiteId = suite.SuiteId,
                SuiteName = suite.Name,
                OrderIndex = suite.OrderIndex,
                CustomWeight = suite.Weight
            });
        }

        return battery;
    }

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // --- Defaults and weights --------------------------------------------------------------------

    [Fact]
    public void ANewBattery_DefaultsToDifficultyMass()
    {
        Assert.Equal(BenchmarkBatteryWeightingScheme.DifficultyMass, new BenchmarkBattery().WeightingScheme);
    }

    [Theory]
    [InlineData(BenchmarkBatteryWeightingScheme.DifficultyMass)]
    [InlineData(BenchmarkBatteryWeightingScheme.ItemCount)]
    [InlineData(BenchmarkBatteryWeightingScheme.Equal)]
    [InlineData(BenchmarkBatteryWeightingScheme.Custom)]
    public void WeightsUnderEveryScheme_SumToOne(BenchmarkBatteryWeightingScheme scheme)
    {
        var custom = new double?[] { 3.0, 1.5, 0.7 };

        var weights = BenchmarkBatteryDefinition.Weights(scheme, ThreeSuites, custom);
        var countWeights = BenchmarkBatteryDefinition.CountWeights(scheme, ThreeSuites, custom);

        Assert.Equal(3, weights.Count);
        Assert.Equal(1.0, weights.Sum(), 12);
        Assert.Equal(1.0, countWeights.Sum(), 12);
        Assert.All(weights, w => Assert.True(w > 0));
    }

    [Fact]
    public void DifficultyMassWeights_GrowWithBothTheQuestionCountAndTheDifficulties()
    {
        // 10 questions at 40, 10 at 20, 5 at 40.
        var weights = BenchmarkBatteryDefinition.PreviewWeights(
            BenchmarkBatteryWeightingScheme.DifficultyMass,
            new IReadOnlyList<int?>[]
            {
                Enumerable.Repeat<int?>(40, 10).ToList(),
                Enumerable.Repeat<int?>(20, 10).ToList(),
                Enumerable.Repeat<int?>(40, 5).ToList()
            },
            null);

        Assert.Equal(0.5, weights[0], 12);
        Assert.Equal(0.25, weights[1], 12);
        Assert.Equal(0.25, weights[2], 12);
        Assert.True(weights[0] > weights[1]);
        Assert.True(weights[0] > weights[2]);
    }

    [Fact]
    public void ItemCountAndEqualWeights_FollowTheirDefinitions()
    {
        var itemCount = BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.ItemCount, ThreeSuites, null);
        var equal = BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Equal, ThreeSuites, null);

        Assert.Equal(new[] { 0.4, 0.4, 0.2 }, itemCount.Select(w => Math.Round(w, 12)));
        Assert.All(equal, w => Assert.Equal(1.0 / 3.0, w, 12));
    }

    [Fact]
    public void CustomWeights_AreNormalized()
    {
        var a = BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, new double?[] { 2, 1, 1 });
        var b = BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, new double?[] { 4, 2, 2 });

        Assert.Equal(new[] { 0.5, 0.25, 0.25 }, a.Select(w => Math.Round(w, 12)));
        Assert.Equal(a, b);
    }

    [Fact]
    public void CustomWeights_ThatAreMissingOrNotPositive_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, null));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, new double?[] { 1, 1 }));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, new double?[] { 1, 0, 1 }));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, new double?[] { 1, double.NaN, 1 }));
    }

    [Fact]
    public void Weights_WithNoSuitesOrNoMass_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Equal, Array.Empty<BenchmarkBatterySuiteMass>(), null));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkBatteryDefinition.Weights(
                BenchmarkBatteryWeightingScheme.DifficultyMass,
                new[] { new BenchmarkBatterySuiteMass(0, 0), new BenchmarkBatterySuiteMass(0, 0) },
                null));
    }

    [Fact]
    public void CountWeights_AreQuestionCountsUnderTheMassSchemes_AndTheDeclaredWeightsOtherwise()
    {
        var custom = new double?[] { 2, 1, 1 };

        Assert.Equal(
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.ItemCount, ThreeSuites, null),
            BenchmarkBatteryDefinition.CountWeights(BenchmarkBatteryWeightingScheme.DifficultyMass, ThreeSuites, null));
        Assert.Equal(
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.ItemCount, ThreeSuites, null),
            BenchmarkBatteryDefinition.CountWeights(BenchmarkBatteryWeightingScheme.ItemCount, ThreeSuites, null));
        Assert.Equal(
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Equal, ThreeSuites, null),
            BenchmarkBatteryDefinition.CountWeights(BenchmarkBatteryWeightingScheme.Equal, ThreeSuites, null));
        Assert.Equal(
            BenchmarkBatteryDefinition.Weights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, custom),
            BenchmarkBatteryDefinition.CountWeights(BenchmarkBatteryWeightingScheme.Custom, ThreeSuites, custom));
    }

    // --- Suite mass ------------------------------------------------------------------------------

    [Fact]
    public void SuiteMass_UsesTheItemWeightWherePresent_AndTheExamFallbackWhereNot()
    {
        var exam = new List<BenchmarkQuestion>
        {
            new() { Id = 1, AssessedDifficulty = 30 },  // item row present: its weight wins
            new() { Id = 2, AssessedDifficulty = null }, // no row, never assessed: 50
            new() { Id = 3, AssessedDifficulty = 0 },    // no row, floored at 1
            new() { Id = 4, AssessedDifficulty = 64 }    // no row: its own difficulty
        };
        var statistics = new BenchmarkGroupStatisticsResult
        {
            Items = new[] { new BenchmarkGroupItemStatistics { QuestionId = 1, Weight = 37.5 } }
        };

        var mass = BenchmarkBatteryDefinition.SuiteMass(exam, statistics);

        Assert.Equal(4, mass.ItemCount);
        Assert.Equal(37.5 + 50 + 1 + 64, mass.DifficultyMass, 12);
    }

    [Fact]
    public void SuiteMass_WithoutStatistics_UsesTheExamFallbackForEveryQuestion()
    {
        var exam = new List<BenchmarkQuestion>
        {
            new() { Id = 1, AssessedDifficulty = 30 },
            new() { Id = 2, AssessedDifficulty = null }
        };

        var mass = BenchmarkBatteryDefinition.SuiteMass(exam, null);

        Assert.Equal(new BenchmarkBatterySuiteMass(2, 80), mass);
    }

    [Fact]
    public void PreviewWeights_AreEmptyWhenUndefined()
    {
        Assert.Empty(BenchmarkBatteryDefinition.PreviewWeights(
            BenchmarkBatteryWeightingScheme.DifficultyMass,
            new IReadOnlyList<int?>[] { Array.Empty<int?>(), Array.Empty<int?>() },
            null));
        Assert.Empty(BenchmarkBatteryDefinition.PreviewWeights(
            BenchmarkBatteryWeightingScheme.Custom,
            new IReadOnlyList<int?>[] { new int?[] { 40 }, new int?[] { 40 } },
            new double?[] { 1, null }));
    }

    // --- Validation ------------------------------------------------------------------------------

    [Fact]
    public void Validate_AcceptsAValidBattery()
    {
        Assert.Empty(BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.DifficultyMass,
            (10, "Alpha", 0, null),
            (20, "Beta", 1, null))));
        Assert.Empty(BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Custom,
            (10, "Alpha", 0, 2.0),
            (20, "Beta", 1, 0.5))));
    }

    [Fact]
    public void Validate_RefusesFewerThanTwoSuites()
    {
        var errors = BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Equal,
            (10, "Alpha", 0, null)));

        Assert.Contains(errors, e => e.Contains("at least 2 suites"));
    }

    [Fact]
    public void Validate_NamesADeletedSuite()
    {
        var errors = BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Equal,
            (10, "Alpha", 0, null),
            (null, "Gone", 1, null)));

        Assert.Contains(errors, e => e.Contains("'Gone'") && e.Contains("deleted"));
    }

    [Fact]
    public void Validate_RefusesADuplicateSuite()
    {
        var errors = BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Equal,
            (10, "Alpha", 0, null),
            (10, "Alpha", 1, null)));

        Assert.Contains(errors, e => e.Contains("#10") && e.Contains("2 times"));
    }

    [Fact]
    public void Validate_RequiresPositiveFiniteWeightsUnderCustom()
    {
        var errors = BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Custom,
            (10, "Alpha", 0, null),
            (20, "Beta", 1, 0.0),
            (30, "Gamma", 2, double.PositiveInfinity),
            (40, "Delta", 3, 1.0)));

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("'Alpha'"));
        Assert.Contains(errors, e => e.Contains("'Beta'"));
        Assert.Contains(errors, e => e.Contains("'Gamma'"));
    }

    [Fact]
    public void Validate_RefusesCustomWeightsUnderAnotherScheme()
    {
        var errors = BenchmarkBatteryDefinition.Validate(Battery(
            BenchmarkBatteryWeightingScheme.Equal,
            (10, "Alpha", 0, 2.0),
            (20, "Beta", 1, null)));

        var error = Assert.Single(errors);
        Assert.Contains("'Alpha'", error);
        Assert.Contains("Custom", error);
    }

    // --- Entity, JSON and hash -------------------------------------------------------------------

    [Fact]
    public void FromEntity_OrdersTheSuitesAndIndexesThemFromZero()
    {
        var definition = BenchmarkBatteryDefinition.FromEntity(Battery(
            BenchmarkBatteryWeightingScheme.ItemCount,
            (20, "Beta", 5, null),
            (10, "Alpha", 2, null)));

        Assert.Equal((long?)7, definition.BatteryId);
        Assert.Equal("Core", definition.Name);
        Assert.Equal(3, definition.Revision);
        Assert.Equal(BenchmarkBatteryWeightingScheme.ItemCount, definition.Scheme);
        Assert.Equal(new[] { 10L, 20L }, definition.Suites.Select(s => s.SuiteId));
        Assert.Equal(new[] { 0, 1 }, definition.Suites.Select(s => s.Index));
        Assert.Equal(new double?[] { null, null }, definition.CustomWeights);
    }

    [Fact]
    public void FromEntity_RefusesAnInvalidBattery()
    {
        Assert.Throws<InvalidOperationException>(() => BenchmarkBatteryDefinition.FromEntity(Battery(
            BenchmarkBatteryWeightingScheme.Equal,
            (10, "Alpha", 0, null),
            (null, "Gone", 1, null))));
    }

    [Fact]
    public void Json_RoundTripsWithCamelCaseNames()
    {
        var definition = BenchmarkBatteryDefinition.FromEntity(Battery(
            BenchmarkBatteryWeightingScheme.Custom,
            (10, "Alpha", 0, 2.5),
            (20, "Beta", 1, 1.0)));

        string json = definition.ToJson();
        var back = BenchmarkBatteryDefinition.FromJson(json);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("suites", out var suites));
        Assert.True(suites[0].TryGetProperty("suiteId", out _));
        Assert.True(doc.RootElement.TryGetProperty("scheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("definitionSha256", out _));
        Assert.False(doc.RootElement.TryGetProperty("customWeights", out _));

        Assert.Equal(definition.BatteryId, back.BatteryId);
        Assert.Equal(definition.Name, back.Name);
        Assert.Equal(definition.Revision, back.Revision);
        Assert.Equal(definition.Scheme, back.Scheme);
        Assert.Equal(definition.Suites, back.Suites);
        Assert.Equal(definition.DefinitionSha256, back.DefinitionSha256);
    }

    [Fact]
    public void FromJson_RefusesEmptyText()
    {
        Assert.Throws<JsonException>(() => BenchmarkBatteryDefinition.FromJson(" "));
    }

    [Fact]
    public void ComputeSha256_HashesTheCanonicalText()
    {
        string expected = Sha256Hex("scheme=4\n3:2.5\n7:0.1");

        string actual = BenchmarkBatteryDefinition.ComputeSha256(
            BenchmarkBatteryWeightingScheme.Custom,
            new (long, double?)[] { (7, 0.1), (3, 2.5) });

        Assert.Equal(expected, actual);
        Assert.Equal(
            Sha256Hex("scheme=1\n3:-\n7:-"),
            BenchmarkBatteryDefinition.ComputeSha256(
                BenchmarkBatteryWeightingScheme.DifficultyMass,
                new (long, double?)[] { (3, null), (7, null) }));
        Assert.Equal("0.1", 0.1.ToString("R", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ComputeSha256_IgnoresOrderAndName()
    {
        var a = Battery(BenchmarkBatteryWeightingScheme.Equal, (10, "Alpha", 0, null), (20, "Beta", 1, null));
        var b = Battery(BenchmarkBatteryWeightingScheme.Equal, (20, "Renamed", 0, null), (10, "Other", 1, null));

        Assert.Equal(BenchmarkBatteryDefinition.ComputeSha256(a), BenchmarkBatteryDefinition.ComputeSha256(b));
        Assert.Equal(BenchmarkBatteryDefinition.ComputeSha256(a), BenchmarkBatteryDefinition.FromEntity(b).DefinitionSha256);
    }

    [Fact]
    public void ComputeSha256_ChangesWithTheSchemeACustomWeightOrASuite()
    {
        string baseline = BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Custom, (10, "Alpha", 0, 2.0), (20, "Beta", 1, 1.0)));

        string otherScheme = BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Equal, (10, "Alpha", 0, 2.0), (20, "Beta", 1, 1.0)));
        string otherWeight = BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Custom, (10, "Alpha", 0, 2.0), (20, "Beta", 1, 1.5)));
        string scaledWeights = BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Custom, (10, "Alpha", 0, 4.0), (20, "Beta", 1, 2.0)));
        string otherSuite = BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Custom, (10, "Alpha", 0, 2.0), (30, "Beta", 1, 1.0)));

        Assert.NotEqual(baseline, otherScheme);
        Assert.NotEqual(baseline, otherWeight);
        Assert.NotEqual(baseline, scaledWeights);
        Assert.NotEqual(baseline, otherSuite);
    }

    [Fact]
    public void ComputeSha256_RefusesABatteryWithADeletedSuite()
    {
        Assert.Throws<InvalidOperationException>(() => BenchmarkBatteryDefinition.ComputeSha256(Battery(
            BenchmarkBatteryWeightingScheme.Equal, (10, "Alpha", 0, null), (null, "Gone", 1, null))));
    }
}
