namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Overseer.Services;
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
        var targets = _service.GetCatalogTargets("Google");

        Assert.Equal(_service.GetCatalogEntries("Google").Count, targets.Count);
        Assert.Equal(targets.OrderByDescending(t => t.ReleaseDate, StringComparer.Ordinal).Select(t => t.ReleaseDate), targets.Select(t => t.ReleaseDate));
        Assert.DoesNotContain(targets, t => t.ModelId == "gemini-3.7-flash");

        var flash = Assert.Single(targets, t => t.ModelId == "gemini-3.8-flash");
        Assert.Equal("Gemini 3.8 Flash", flash.DisplayName);
        Assert.Equal(1048576, flash.ContextWindowSize);
        Assert.Equal(65536, flash.MaxOutputTokens);
        Assert.Equal(0.75m, flash.InputPerMillion);
        Assert.Equal(3.75m, flash.OutputPerMillion);
    }

    [Fact]
    public void GetCatalogTargets_UnknownProvider_IsEmpty()
    {
        Assert.Empty(_service.GetCatalogTargets("Nope"));
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
