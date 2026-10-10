namespace Overseer.Tests.UnitTests;

using System.Text.Json;
using Overseer.Models;
using Overseer.Services;
using Xunit;

/// <summary>
/// Classifying a model row against the real embedded catalogs and retired-models list.
/// </summary>
public class ModelAvailabilityServiceTests
{
    private readonly ModelAvailabilityService _service = new(new ModelMetadataService());

    [Fact]
    public void CatalogModel_IsAvailable()
    {
        var result = _service.Evaluate("Google", "gemini-3.8-flash", null, null);

        Assert.Equal(ModelAvailabilityStatus.Available, result.Kind);
        Assert.Equal("available", result.Status);
        Assert.False(result.NeedsAttention);
        Assert.Null(result.Replacement);
        Assert.Null(result.SuggestedCustom);
    }

    [Fact]
    public void Variant_IsAvailable()
    {
        var result = _service.Evaluate("Google", "gemini-3.5-flash-preview", "catalog", null);

        Assert.Equal(ModelAvailabilityStatus.Available, result.Kind);
    }

    [Fact]
    public void UnknownVersion_IsNotInCatalog()
    {
        var result = _service.Evaluate("Google", "gemini-3.8-flash-98", null, null);

        Assert.Equal(ModelAvailabilityStatus.NotInCatalog, result.Kind);
        Assert.Equal("notInCatalog", result.Status);
        Assert.True(result.NeedsAttention);
        Assert.Null(result.Replacement);
        Assert.Null(result.SuggestedCustom);
    }

    [Fact]
    public void UncataloguedModel_IsNotInCatalog()
    {
        var result = _service.Evaluate("OpenAI", "gpt-4o", "catalog", null);

        Assert.Equal(ModelAvailabilityStatus.NotInCatalog, result.Kind);
    }

    [Theory]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.7-flash-001")]
    public void RetiredModel_IsRetiredWithReplacementAndSuggestedCustom(string modelId)
    {
        var result = _service.Evaluate("Google", modelId, null, null);

        Assert.Equal(ModelAvailabilityStatus.Retired, result.Kind);
        Assert.Equal("retired", result.Status);
        Assert.True(result.NeedsAttention);
        Assert.Equal("2026-10-10", result.RetiredOn);
        Assert.False(string.IsNullOrWhiteSpace(result.Note));
        Assert.Equal("Gemini 3.7 Flash", result.CatalogDisplayName);

        Assert.NotNull(result.Replacement);
        Assert.Equal("gemini-3.8-flash", result.Replacement.ModelId);
        Assert.Equal("Gemini 3.8 Flash", result.Replacement.DisplayName);

        Assert.NotNull(result.SuggestedCustom);
        Assert.Equal(1048576 - 65536, result.SuggestedCustom.MaxInputTokens);
        Assert.Equal(65536, result.SuggestedCustom.MaxOutputTokens);
        Assert.Equal(0.75m, result.SuggestedCustom.InputPricePerMillion);
        Assert.Equal(3.75m, result.SuggestedCustom.OutputPricePerMillion);
        Assert.Equal(0.075m, result.SuggestedCustom.CachedInputPricePerMillion);
    }

    [Fact]
    public void CustomMode_IsCustom_EvenForARetiredModel()
    {
        var result = _service.Evaluate("Google", "gemini-3.7-flash", "custom", null);

        Assert.Equal(ModelAvailabilityStatus.Custom, result.Kind);
        Assert.Equal("custom", result.Status);
        Assert.False(result.NeedsAttention);
        Assert.Null(result.SuggestedCustom);
    }

    [Fact]
    public void CustomMode_BeatsAvailable()
    {
        var result = _service.Evaluate("Google", "gemini-3.8-flash", "custom", null);

        Assert.Equal(ModelAvailabilityStatus.Custom, result.Kind);
    }

    [Theory]
    [InlineData("gemini-3.8-flash", null)]
    [InlineData("gemini-3.7-flash", "custom")]
    [InlineData("my-azure-deployment", "catalog")]
    public void BaseUrl_IsCustomEndpoint_BeforeEveryOtherStatus(string modelId, string? mode)
    {
        var result = _service.Evaluate("Google", modelId, mode, "https://gateway.example.com/google");

        Assert.Equal(ModelAvailabilityStatus.CustomEndpoint, result.Kind);
        Assert.Equal("customEndpoint", result.Status);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public void WhitespaceBaseUrl_IsNoCustomEndpoint()
    {
        var result = _service.Evaluate("Google", "gemini-3.7-flash", null, "  ");

        Assert.Equal(ModelAvailabilityStatus.Retired, result.Kind);
    }

    [Fact]
    public void Status_SerializesAsCamelCaseString_AndKindIsNotSerialized()
    {
        var result = _service.Evaluate("Google", "gemini-3.8-flash-98", null, null);

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("\"Status\":\"notInCatalog\"", json);
        Assert.Contains("\"NeedsAttention\":true", json);
        Assert.DoesNotContain("\"Kind\"", json);
    }

    [Theory]
    [InlineData("Google", "gemini-3.8-flash", "catalog")]
    [InlineData("Google", "gemini-3.5-flash-preview", "catalog")]
    [InlineData("Google", "gemini-3.7-flash", "custom")]
    [InlineData("OpenAI", "my-fine-tune", "custom")]
    public void DeriveCatalogMode_IsCatalogOnlyForADescribedModel(string provider, string modelId, string expected)
    {
        Assert.Equal(expected, _service.DeriveCatalogMode(provider, modelId));
    }
}
