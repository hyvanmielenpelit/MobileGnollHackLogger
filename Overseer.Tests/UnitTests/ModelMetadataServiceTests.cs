namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

/// <summary>
/// The model ID matching contract, against the real embedded catalogs. Synthetic future IDs use
/// version numbers no provider will plausibly ship (-97, -98), so a real release never collides.
/// </summary>
public class ModelMetadataServiceTests
{
    private static readonly string[] Providers = { "Anthropic", "OpenAI", "Google" };

    private readonly ModelMetadataService _service = new();

    public static IEnumerable<object[]> AllCatalogPrefixes()
    {
        var service = new ModelMetadataService();
        foreach (var provider in Providers)
        {
            foreach (var entry in service.GetCatalogEntries(provider))
            {
                foreach (var prefix in entry.Prefixes)
                {
                    yield return new object[] { provider, prefix };
                }
            }
        }
    }

    private string DisplayNameOf(string provider, string prefix)
    {
        return _service.GetCatalogEntries(provider)
            .Single(e => e.Prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            .DisplayName;
    }

    [Theory]
    [MemberData(nameof(AllCatalogPrefixes))]
    public void EveryCatalogPrefix_IsWhitelistedAndResolvesToItsOwnEntry(string provider, string prefix)
    {
        Assert.True(_service.IsWhitelisted(provider, prefix));
        Assert.Equal(DisplayNameOf(provider, prefix), _service.GetMetadata(provider, prefix).DisplayName);
    }

    [Fact]
    public void Catalogs_HaveNoDuplicatePrefixes()
    {
        foreach (var provider in Providers)
        {
            var duplicates = _service.GetCatalogEntries(provider)
                .SelectMany(e => e.Prefixes)
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, $"{provider} catalog repeats: {string.Join(", ", duplicates)}");
        }
    }

    [Theory]
    [InlineData("OpenAI", "gpt-5.4-2026-03-05", "gpt-5.4")]
    [InlineData("OpenAI", "gpt-5.4-pro-2026-03-05", "gpt-5.4-pro")]
    [InlineData("Google", "gemini-3.8-flash-001", "gemini-3.8-flash")]
    [InlineData("Anthropic", "claude-sonnet-5-20260630", "claude-sonnet-5")]
    public void DatedSnapshots_AreWhitelistedUnderTheBaseEntry(string provider, string modelId, string basePrefix)
    {
        Assert.True(_service.IsWhitelisted(provider, modelId));
        Assert.Equal(DisplayNameOf(provider, basePrefix), _service.GetMetadata(provider, modelId).DisplayName);
    }

    [Theory]
    [InlineData("Anthropic", "claude-opus-5-98")]
    [InlineData("Anthropic", "claude-fable-5-1-97")]
    [InlineData("Google", "gemini-3.8-flash-98")]
    public void UncataloguedPointRelease_IsHiddenAndBorrowsNothing(string provider, string modelId)
    {
        Assert.False(_service.IsWhitelisted(provider, modelId));

        var meta = _service.GetMetadata(provider, modelId);
        Assert.Equal(string.Empty, meta.DisplayName);
        Assert.Equal(modelId, meta.Description);
        Assert.Null(meta.DefaultPricing);
        Assert.Equal(0, meta.ContextWindowSize);
        Assert.Empty(meta.SupportedThinkingLevels);
    }

    [Fact]
    public void UncataloguedPointRelease_LogsOneWarning()
    {
        var logger = new RecordingLogger();
        var service = new ModelMetadataService(logger);

        service.IsWhitelisted("Anthropic", "claude-opus-5-98");
        service.IsWhitelisted("Anthropic", "claude-opus-5-98");

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("claude-opus-5-98", warning.Message);
        Assert.Contains("claude-opus-5", warning.Message);
    }

    [Theory]
    [InlineData("Anthropic", "claude-opus-5")]
    [InlineData("Google", "gemini-3.5-flash-preview")]
    [InlineData("OpenAI", "gpt-4o")]
    public void CataloguedVariantOrUnknownModel_LogsNothing(string provider, string modelId)
    {
        var logger = new RecordingLogger();
        var service = new ModelMetadataService(logger);

        service.IsWhitelisted(provider, modelId);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void PrefixWithoutBoundary_DoesNotMatch()
    {
        Assert.False(_service.IsWhitelisted("OpenAI", "gpt-5.45"));
        Assert.Equal(string.Empty, _service.GetMetadata("OpenAI", "gpt-5.45").DisplayName);
    }

    [Theory]
    [InlineData("Google", "gemini-3.5-flash-preview", "gemini-3.5-flash")]
    [InlineData("OpenAI", "gpt-5.4-mydeployment", "gpt-5.4")]
    public void HandTypedVariant_KeepsFamilyMetadataButIsNotWhitelisted(string provider, string modelId, string familyPrefix)
    {
        Assert.False(_service.IsWhitelisted(provider, modelId));

        var meta = _service.GetMetadata(provider, modelId);
        Assert.Equal(DisplayNameOf(provider, familyPrefix), meta.DisplayName);
        Assert.NotNull(meta.DefaultPricing);
    }

    [Theory]
    [InlineData("OpenAI", "gpt-4o")]
    [InlineData("Nope", "gpt-5.4")]
    public void UnknownProviderOrModel_ReturnsFallback(string provider, string modelId)
    {
        Assert.False(_service.IsWhitelisted(provider, modelId));

        var meta = _service.GetMetadata(provider, modelId);
        Assert.Equal(string.Empty, meta.DisplayName);
        Assert.Equal(modelId, meta.Description);
    }

    // ---------------------------------------------------------------------------------------------
    // Retired models.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnthropicDisplayNames_PutTheVersionLast()
    {
        var entries = _service.GetCatalogEntries("Anthropic");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Matches(@"^Claude [A-Z][a-z]+ \d+(\.\d+)?$", e.DisplayName));
    }

    [Fact]
    public void FormerDisplayNames_AreNeverACurrentName_AndBelongToOneEntry()
    {
        var renames = _service.GetDisplayNameRenames();

        Assert.NotEmpty(renames);
        Assert.Empty(renames.Select(r => r.Former).Intersect(_service.GetCatalogDisplayNames()));
        Assert.Equal(renames.Count, renames.Select(r => r.Former).Distinct().Count());
    }

    [Fact]
    public void GetMetadata_CarriesTheFormerNames()
    {
        var meta = _service.GetMetadata("Anthropic", "claude-opus-5-5");

        Assert.Equal("Claude Opus 5.5", meta.DisplayName);
        Assert.Equal(new[] { "Claude 5.5 Opus" }, meta.FormerDisplayNames);
        Assert.Empty(_service.GetMetadata("Anthropic", "claude-unknown").FormerDisplayNames);
    }

    [Fact]
    public void RetiredList_Loads()
    {
        var entries = _service.GetRetiredEntries();

        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Provider));
            Assert.NotEmpty(e.Prefixes);
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
        });
    }

    [Fact]
    public void RetiredPrefixes_AreNeitherActivePrefixesNorShadowedByOne()
    {
        foreach (var entry in _service.GetRetiredEntries())
        {
            foreach (var prefix in entry.Prefixes)
            {
                Assert.False(_service.IsDescribedByCatalog(entry.Provider, prefix),
                    $"Retired {entry.Provider} prefix {prefix} is described by the active catalog.");
                Assert.Same(entry, _service.GetRetiredEntry(entry.Provider, prefix));
            }
        }
    }

    [Fact]
    public void RetiredReplacements_AreWhitelisted()
    {
        foreach (var entry in _service.GetRetiredEntries().Where(e => e.Replacement != null))
        {
            Assert.True(_service.IsWhitelisted(entry.Provider, entry.Replacement!),
                $"Replacement {entry.Replacement} of {entry.DisplayName} is not a whitelisted {entry.Provider} model.");
        }
    }

    [Fact]
    public void RetiredDates_ParseAsIsoDates()
    {
        foreach (var entry in _service.GetRetiredEntries())
        {
            Assert.True(
                DateOnly.TryParseExact(entry.RetiredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"{entry.DisplayName} has retiredOn \"{entry.RetiredOn}\".");
        }
    }

    [Theory]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.7-flash-001")]
    public void GetRetiredEntry_MatchesTheRetiredPrefixAndItsSnapshots(string modelId)
    {
        var entry = _service.GetRetiredEntry("Google", modelId);

        Assert.NotNull(entry);
        Assert.Equal("Gemini 3.7 Flash", entry.DisplayName);
    }

    [Theory]
    [InlineData("Google", "gemini-3.8-flash")]
    [InlineData("Google", "gemini-3.7-flash-preview")]
    [InlineData("OpenAI", "gemini-3.7-flash")]
    public void GetRetiredEntry_IgnoresActiveModelsVariantsAndOtherProviders(string provider, string modelId)
    {
        Assert.Null(_service.GetRetiredEntry(provider, modelId));
    }

    [Theory]
    [InlineData("Google", "gemini-3.8-flash", true)]
    [InlineData("Google", "gemini-3.8-flash-001", true)]
    [InlineData("Google", "gemini-3.5-flash-preview", true)]
    [InlineData("Google", "gemini-3.8-flash-98", false)]
    [InlineData("Google", "gemini-3.7-flash", false)]
    [InlineData("OpenAI", "gpt-4o", false)]
    public void IsDescribedByCatalog_AcceptsExactSnapshotAndVariant(string provider, string modelId, bool expected)
    {
        Assert.Equal(expected, _service.IsDescribedByCatalog(provider, modelId));
    }

    [Fact]
    public void GetCatalogTargets_ListsEveryEntryNewestFirst()
    {
        var targets = _service.GetCatalogTargets("Google", new DateOnly(2026, 10, 14));
        var pricing = _service.GetCatalogEntries("Google").Single(e => e.Prefixes[0] == "gemini-3.8-flash").Pricing!;

        Assert.Equal(_service.GetCatalogEntries("Google").Count, targets.Count);
        Assert.Equal(targets.OrderByDescending(t => t.ReleaseDate, StringComparer.Ordinal).Select(t => t.ReleaseDate), targets.Select(t => t.ReleaseDate));
        Assert.DoesNotContain(targets, t => t.ModelId == "gemini-3.7-flash");

        var flash = Assert.Single(targets, t => t.ModelId == "gemini-3.8-flash");
        Assert.Equal("Gemini 3.8 Flash", flash.DisplayName);
        Assert.Equal(1048576, flash.ContextWindowSize);
        Assert.Equal(65536, flash.MaxOutputTokens);
        Assert.Equal(pricing.InputPerMillion, flash.InputPerMillion);
        Assert.Equal(pricing.OutputPerMillion, flash.OutputPerMillion);
    }

    [Fact]
    public void GetCatalogTargets_PricesEachTargetAtTheRateInForceOnTheDay()
    {
        var pricing = _service.GetCatalogEntries("Google").Single(e => e.Prefixes[0] == "gemini-3.8-flash").Pricing!;
        var scheduled = Assert.IsType<ModelCatalogScheduledPricing>(pricing.ScheduledChange);

        var before = Assert.Single(_service.GetCatalogTargets("Google", new DateOnly(2026, 12, 31)), t => t.ModelId == "gemini-3.8-flash");
        Assert.Equal(pricing.InputPerMillion, before.InputPerMillion);
        Assert.Equal(pricing.OutputPerMillion, before.OutputPerMillion);

        var after = Assert.Single(_service.GetCatalogTargets("Google", new DateOnly(2027, 1, 1)), t => t.ModelId == "gemini-3.8-flash");
        Assert.Equal(scheduled.InputPerMillion, after.InputPerMillion);
        Assert.Equal(scheduled.OutputPerMillion, after.OutputPerMillion);
        Assert.NotEqual(before.InputPerMillion, after.InputPerMillion);
    }

    [Fact]
    public void GetCatalogTargets_UnknownProvider_IsEmpty()
    {
        Assert.Empty(_service.GetCatalogTargets("Nope"));
    }

    [Fact]
    public void GetCatalogTargets_TierOnlySchedule_KeepsTheBaseRatesOnBothSides()
    {
        foreach (var day in new[] { new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 1) })
        {
            var sol = Assert.Single(_service.GetCatalogTargets("OpenAI", day), t => t.ModelId == "gpt-6.2-sol");
            Assert.Equal(2.00m, sol.InputPerMillion);
            Assert.Equal(10.00m, sol.OutputPerMillion);
        }
    }

    [Fact]
    public void GetCatalogTargets_Gpt56Sol_MovesToTheScheduledRatesOnItsDate()
    {
        var before = Assert.Single(_service.GetCatalogTargets("OpenAI", new DateOnly(2026, 11, 30)), t => t.ModelId == "gpt-5.6-sol");
        Assert.Equal(4.00m, before.InputPerMillion);
        Assert.Equal(20.00m, before.OutputPerMillion);

        var after = Assert.Single(_service.GetCatalogTargets("OpenAI", new DateOnly(2026, 12, 1)), t => t.ModelId == "gpt-5.6-sol");
        Assert.Equal(5.00m, after.InputPerMillion);
        Assert.Equal(30.00m, after.OutputPerMillion);
    }

    // ---------------------------------------------------------------------------------------------
    // Per-model service tiers.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetMetadata_ServiceTiers_AreTheEntrysOwnList_OrEmpty()
    {
        Assert.Equal(
            new[] { "auto", "default", "priority", "fast", "ultrafast" },
            _service.GetMetadata("OpenAI", "gpt-6.2-astra").ServiceTiers);
        Assert.Empty(_service.GetMetadata("OpenAI", "gpt-6.1-terra").ServiceTiers);
    }

    [Fact]
    public void ResolveServiceTiers_ModelListWins_ElseTheProviderList()
    {
        var providerTiers = new[] { "auto", "default", "flex" };
        var own = new ModelMetadata { ServiceTiers = new List<string> { "default", "ultrafast" } };

        var resolved = ModelMetadataService.ResolveServiceTiers(providerTiers, own);
        Assert.Equal(new[] { "default", "ultrafast" }, resolved);
        Assert.NotSame(own.ServiceTiers, resolved);

        Assert.Equal(providerTiers, ModelMetadataService.ResolveServiceTiers(providerTiers, new ModelMetadata()));
        Assert.Equal(providerTiers, ModelMetadataService.ResolveServiceTiers(providerTiers, null));
        Assert.Empty(ModelMetadataService.ResolveServiceTiers(null, new ModelMetadata()));
        Assert.Empty(ModelMetadataService.ResolveServiceTiers(Array.Empty<string>(), null));
    }

    [Fact]
    public void ShippedCatalogs_ServiceTiers_AreRequestablePricedAndConsistent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "DefaultMaxOutputTokens:Anthropic", "8192" } })
            .Build();

        var anthropic = new AnthropicProvider(configuration).SupportedServiceTiers;
        var google = new GoogleProvider(configuration).SupportedServiceTiers;
        var providers = new (string Name, IReadOnlyList<string> Supported, IReadOnlyList<string> Vocabulary)[]
        {
            ("OpenAI", new OpenAiResponsesProvider(configuration).SupportedServiceTiers, OpenAiResponsesProvider.RequestableServiceTiers),
            ("Anthropic", anthropic, anthropic),
            ("Google", google, google)
        };

        int checkedEntries = 0;
        foreach (var (name, supported, vocabulary) in providers)
        {
            bool openAi = name == "OpenAI";

            foreach (var entry in _service.GetCatalogEntries(name))
            {
                string where = $"{name} {entry.Prefixes[0]}";
                var own = entry.ServiceTiers ?? new List<string>();

                // 1. Every listed tier is normalized, requestable and listed once.
                foreach (var tier in own)
                {
                    Assert.True(tier == ProviderHelper.NormalizeServiceTier(tier),
                        $"{where}: service tier '{tier}' is not in normalized form.");
                    Assert.True(vocabulary.Contains(tier), $"{where}: service tier '{tier}' is not requestable.");
                    Assert.True(own.Count(t => t == tier) == 1, $"{where}: service tier '{tier}' is listed twice.");
                }

                var effective = ModelMetadataService.ResolveServiceTiers(supported, _service.GetMetadata(name, entry.Prefixes[0]));
                var multipliers = entry.Pricing?.ServiceTierMultipliers ?? new Dictionary<string, decimal>();

                bool Covered(string key) =>
                    effective.Contains(key)
                    || (openAi && key == "priority" && effective.Contains("fast"))
                    || (openAi && key == "fast" && effective.Contains("priority"));

                // 2. A tier outside the provider's default list is priced.
                if (entry.Pricing != null)
                {
                    foreach (var tier in effective.Where(t => !supported.Contains(t)))
                    {
                        Assert.True(multipliers.ContainsKey(tier), $"{where}: service tier '{tier}' has no multiplier.");
                    }
                }

                // 3. Every priced tier is requestable on this model.
                foreach (var key in multipliers.Keys.Where(k => k != "batch"))
                {
                    Assert.True(Covered(key), $"{where}: multiplier '{key}' prices a tier the model does not list.");
                }
                foreach (var key in entry.Pricing?.ScheduledChange?.ServiceTierMultipliers?.Keys ?? Enumerable.Empty<string>())
                {
                    Assert.True(Covered(key), $"{where}: scheduled multiplier '{key}' prices a tier the model does not list.");
                }

                // 4. OpenAI prices priority and fast together, at the same value.
                if (openAi && (multipliers.ContainsKey("priority") || multipliers.ContainsKey("fast")))
                {
                    Assert.True(
                        multipliers.TryGetValue("priority", out var p) && multipliers.TryGetValue("fast", out var f) && p == f,
                        $"{where}: service tier 'priority' and 'fast' must be priced together, at the same value.");
                }

                checkedEntries++;
            }
        }

        Assert.True(checkedEntries > 0, "No catalog entries were found.");
    }

    [Fact]
    public void ShippedCatalogs_ScheduledLongContextCard_KeepsTheBaseThreshold()
    {
        foreach (var provider in new[] { "OpenAI", "Anthropic", "Google" })
        {
            foreach (var entry in _service.GetCatalogEntries(provider))
            {
                var scheduled = entry.Pricing?.ScheduledChange?.LongContext;
                if (scheduled == null) continue;

                var baseCard = entry.Pricing!.LongContext;
                Assert.True(baseCard != null, $"{provider} {entry.Prefixes[0]}: a scheduled long-context card needs a base one.");
                Assert.True(baseCard!.ThresholdInputTokens == scheduled.ThresholdInputTokens,
                    $"{provider} {entry.Prefixes[0]}: the scheduled long-context card moves the threshold.");
            }
        }
    }

    [Theory]
    [InlineData("gpt-5.4-mini")]
    [InlineData("gpt-5.4-nano")]
    public void GetMetadata_Gpt54SmallModels_HaveTheir400KWindow(string modelId)
    {
        var metadata = _service.GetMetadata("OpenAI", modelId);

        Assert.Equal(400_000, metadata.ContextWindowSize);
        Assert.Equal(272_000, metadata.MaxInputTokens);
    }

    [Fact]
    public void GetMetadata_OpenAiEffortLists_FollowTheModelPages()
    {
        Assert.Equal(new[] { "none", "low", "medium", "high", "xhigh" }, _service.GetMetadata("OpenAI", "gpt-5.5").SupportedThinkingLevels);
        Assert.Equal(new[] { "medium", "high", "xhigh" }, _service.GetMetadata("OpenAI", "gpt-5.5-pro").SupportedThinkingLevels);
        Assert.Equal("max", _service.GetMetadata("OpenAI", "gpt-5.6-luna").SupportedThinkingLevels.Last());
    }

    private sealed class RecordingLogger : ILogger<ModelMetadataService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
