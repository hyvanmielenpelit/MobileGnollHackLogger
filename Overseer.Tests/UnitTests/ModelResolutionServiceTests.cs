namespace Overseer.Tests.UnitTests;

using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Xunit;

/// <summary>
/// Switching a model row to a catalog model and keeping it as a custom model, against the real embedded
/// catalogs and retired-models list.
/// </summary>
public class ModelResolutionServiceTests
{
    private readonly ModelResolutionService _service = new(new ModelMetadataService());

    private static ModelSettings Retired37Flash(string? displayName = "Gemini 3.7 Flash", string? displayNameMode = "model_name") => new()
    {
        Provider = "Google",
        ModelId = "gemini-3.7-flash",
        DisplayName = displayName,
        DisplayNameMode = displayNameMode,
        ThinkingLevel = "medium",
        PricingMode = "default"
    };

    private static ModelResolutionChange? ChangeOf(ModelResolutionOutcome outcome, string field) =>
        outcome.Changes.SingleOrDefault(c => c.Field == field);

    // ---------------------------------------------------------------------------------------------
    // Switch: target.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Switch_UncataloguedTarget_IsRefused()
    {
        var outcome = _service.Switch(Retired37Flash(), "gpt-6-sol");

        Assert.True(outcome.IsRefused);
        Assert.Equal("gpt-6-sol is not a Google model in the catalog.", outcome.Refusal);
        Assert.Null(outcome.Updated);
        Assert.Empty(outcome.Changes);
    }

    [Fact]
    public void Switch_RetiredTarget_IsRefused()
    {
        var current = Retired37Flash() with { ModelId = "gemini-3.6-flash" };

        var outcome = _service.Switch(current, "gemini-3.7-flash");

        Assert.True(outcome.IsRefused);
        Assert.Equal("gemini-3.7-flash is not a Google model in the catalog.", outcome.Refusal);
    }

    [Theory]
    [InlineData("gemini-3.5-flash-preview")]
    [InlineData("gemini-3.8-flash-98")]
    [InlineData("")]
    public void Switch_TargetTheModelPickerDoesNotOffer_IsRefused(string target)
    {
        Assert.True(_service.Switch(Retired37Flash(), target).IsRefused);
    }

    [Fact]
    public void Switch_SetsTheModelAndCatalogMode()
    {
        var current = Retired37Flash() with { ModelCatalogMode = "custom" };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.False(outcome.IsRefused);
        Assert.Equal("gemini-3.8-flash", outcome.Updated!.ModelId);
        Assert.Equal("Google", outcome.Updated.Provider);
        Assert.Equal(ModelCatalogModes.Catalog, outcome.Updated.ModelCatalogMode);

        var model = ChangeOf(outcome, "Model");
        Assert.NotNull(model);
        Assert.Equal("gemini-3.7-flash", model.From);
        Assert.Equal("gemini-3.8-flash", model.To);

        var type = ChangeOf(outcome, "Model type");
        Assert.NotNull(type);
        Assert.Equal("Custom model", type.From);
        Assert.Equal("Catalog model", type.To);
    }

    // ---------------------------------------------------------------------------------------------
    // Switch: thinking level, reasoning mode and reasoning summary.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("high", "high")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Switch_ThinkingLevelTheTargetSupportsOrEmpty_IsKept(string? level, string? expected)
    {
        var current = Retired37Flash() with { ThinkingLevel = level };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal(expected, outcome.Updated!.ThinkingLevel);
        Assert.Null(ChangeOf(outcome, "Thinking level"));
    }

    [Fact]
    public void Switch_UnsupportedThinkingLevel_FallsBackToMedium()
    {
        var current = Retired37Flash() with { ModelId = "gemini-3.6-flash", ThinkingLevel = "minimal" };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal("medium", outcome.Updated!.ThinkingLevel);
        var change = ChangeOf(outcome, "Thinking level");
        Assert.NotNull(change);
        Assert.Equal("minimal", change.From);
        Assert.Equal("medium", change.To);
    }

    [Fact]
    public void Switch_UnsupportedReasoningModeWithoutMedium_FallsBackToTheTargetsFirstValue()
    {
        var current = new ModelSettings { Provider = "OpenAI", ModelId = "gpt-5.6-sol", ReasoningMode = "turbo" };

        var outcome = _service.Switch(current, "gpt-6-sol");

        Assert.Equal("standard", outcome.Updated!.ReasoningMode);
    }

    [Fact]
    public void Switch_ReasoningModeTheTargetDoesNotSupportAtAll_BecomesNull()
    {
        var current = new ModelSettings { Provider = "OpenAI", ModelId = "gpt-5.6-sol", ReasoningMode = "pro" };

        var outcome = _service.Switch(current, "gpt-5.5");

        Assert.Null(outcome.Updated!.ReasoningMode);
        var change = ChangeOf(outcome, "Reasoning mode");
        Assert.NotNull(change);
        Assert.Equal("pro", change.From);
        Assert.Null(change.To);
    }

    [Fact]
    public void Switch_SupportedReasoningMode_IsKept()
    {
        var current = new ModelSettings { Provider = "OpenAI", ModelId = "gpt-5.6-sol", ReasoningMode = "pro" };

        var outcome = _service.Switch(current, "gpt-6-sol");

        Assert.Equal("pro", outcome.Updated!.ReasoningMode);
    }

    [Theory]
    [InlineData("omitted", "omitted")]
    [InlineData("detailed", "summarized")]
    public void Switch_ReasoningSummary_IsKeptOrFallsBack(string summary, string expected)
    {
        var current = new ModelSettings { Provider = "Anthropic", ModelId = "claude-opus-5", ReasoningSummary = summary };

        var outcome = _service.Switch(current, "claude-opus-5-5");

        Assert.Equal(expected, outcome.Updated!.ReasoningSummary);
    }

    [Fact]
    public void Switch_ServiceTier_IsKept()
    {
        var current = Retired37Flash() with { ServiceTier = "flex" };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal("flex", outcome.Updated!.ServiceTier);
        Assert.Null(ChangeOf(outcome, "Service tier"));
    }

    // ---------------------------------------------------------------------------------------------
    // Switch: limits.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Switch_LimitsAboveTheTargetsMaximum_AreClamped()
    {
        var current = new ModelSettings
        {
            Provider = "OpenAI",
            ModelId = "gpt-5.6-sol",
            MaxInputTokens = 2_000_000,
            MaxOutputTokens = 200_000
        };

        var outcome = _service.Switch(current, "gpt-6-sol");

        Assert.Equal(128_000, outcome.Updated!.MaxOutputTokens);
        Assert.Equal(1_050_000 - 128_000, outcome.Updated.MaxInputTokens);
        Assert.Equal("200000", ChangeOf(outcome, "Max output tokens")!.From);
        Assert.Equal("128000", ChangeOf(outcome, "Max output tokens")!.To);
        Assert.Equal("922000", ChangeOf(outcome, "Max input tokens")!.To);
    }

    [Fact]
    public void Switch_LimitsWithinTheTargetsMaximum_AreKept()
    {
        var current = Retired37Flash() with { MaxInputTokens = 100_000, MaxOutputTokens = 8_000 };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal(100_000, outcome.Updated!.MaxInputTokens);
        Assert.Equal(8_000, outcome.Updated.MaxOutputTokens);
    }

    [Fact]
    public void Switch_UnsetLimits_StayUnset()
    {
        var outcome = _service.Switch(Retired37Flash(), "gemini-3.8-flash");

        Assert.Null(outcome.Updated!.MaxInputTokens);
        Assert.Null(outcome.Updated.MaxOutputTokens);
    }

    // ---------------------------------------------------------------------------------------------
    // Switch: display name.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Switch_ModelNameMode_TakesTheTargetsCatalogName()
    {
        var outcome = _service.Switch(Retired37Flash("Gemini 3.7 Flash", "model_name"), "gemini-3.8-flash");

        Assert.Equal("Gemini 3.8 Flash", outcome.Updated!.DisplayName);
        Assert.Equal("Gemini 3.7 Flash", ChangeOf(outcome, "Display name")!.From);
    }

    [Fact]
    public void Switch_ModelIdMode_TakesTheTargetId()
    {
        var outcome = _service.Switch(Retired37Flash("gemini-3.7-flash", "model_id"), "gemini-3.8-flash");

        Assert.Equal("gemini-3.8-flash", outcome.Updated!.DisplayName);
    }

    [Fact]
    public void Switch_CustomMode_KeepsTheName()
    {
        var outcome = _service.Switch(Retired37Flash("My fast Gemini", "custom"), "gemini-3.8-flash");

        Assert.Equal("My fast Gemini", outcome.Updated!.DisplayName);
        Assert.Null(ChangeOf(outcome, "Display name"));
    }

    [Theory]
    [InlineData("gemini-3.7-flash", "Gemini 3.8 Flash")]
    [InlineData("Gemini 3.7 Flash", "Gemini 3.8 Flash")]
    [InlineData("My fast Gemini", "My fast Gemini")]
    public void Switch_LegacyModeFromARetiredModel_ReplacesOnlyADerivedName(string displayName, string expected)
    {
        var outcome = _service.Switch(Retired37Flash(displayName, null), "gemini-3.8-flash");

        Assert.Equal(expected, outcome.Updated!.DisplayName);
    }

    [Fact]
    public void Switch_LegacyModeFromACatalogModel_ReplacesItsCatalogName()
    {
        var current = Retired37Flash("Gemini 3.6 Flash", null) with { ModelId = "gemini-3.6-flash" };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal("Gemini 3.8 Flash", outcome.Updated!.DisplayName);
    }

    // ---------------------------------------------------------------------------------------------
    // Switch: pricing.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Switch_CustomPrice_IsKeptWithANote()
    {
        var current = Retired37Flash() with
        {
            PricingMode = "custom",
            InputPricePerMillion = 1.11m,
            OutputPricePerMillion = 2.22m
        };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.Equal("custom", outcome.Updated!.PricingMode);
        Assert.Equal(1.11m, outcome.Updated.InputPricePerMillion);
        Assert.Equal(2.22m, outcome.Updated.OutputPricePerMillion);

        var price = ChangeOf(outcome, "Price");
        Assert.NotNull(price);
        Assert.Equal(ModelResolutionService.CustomPriceKeptNote, price.Note);
        Assert.Null(ChangeOf(outcome, "Pricing"));
    }

    [Fact]
    public void Switch_DefaultPrice_HasNoPriceEntry()
    {
        var outcome = _service.Switch(Retired37Flash(), "gemini-3.8-flash");

        Assert.Equal("default", outcome.Updated!.PricingMode);
        Assert.Null(ChangeOf(outcome, "Price"));
        Assert.Null(ChangeOf(outcome, "Pricing"));
    }

    [Fact]
    public void Switch_ToTheSameModel_HasNoChanges()
    {
        var current = Retired37Flash("Gemini 3.8 Flash") with { ModelId = "gemini-3.8-flash", ModelCatalogMode = "catalog" };

        var outcome = _service.Switch(current, "gemini-3.8-flash");

        Assert.False(outcome.IsRefused);
        Assert.Empty(outcome.Changes);
    }

    // ---------------------------------------------------------------------------------------------
    // Keep as custom.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void KeepAsCustom_WithoutValues_ChangesOnlyTheCatalogMode()
    {
        var current = Retired37Flash() with { MaxInputTokens = 50_000 };

        var outcome = _service.KeepAsCustom(current, new ModelResolutionRequest { Action = ModelResolutionActions.KeepCustom });

        Assert.False(outcome.IsRefused);
        Assert.Equal(ModelCatalogModes.Custom, outcome.Updated!.ModelCatalogMode);
        Assert.Equal("gemini-3.7-flash", outcome.Updated.ModelId);
        Assert.Equal(50_000, outcome.Updated.MaxInputTokens);
        Assert.Equal("default", outcome.Updated.PricingMode);

        var change = Assert.Single(outcome.Changes);
        Assert.Equal("Model type", change.Field);
        Assert.Equal("Catalog model", change.From);
        Assert.Equal("Custom model", change.To);
    }

    [Fact]
    public void KeepAsCustom_WithLimitsAndPrices_SetsThemAndACustomPrice()
    {
        var request = new ModelResolutionRequest
        {
            Action = ModelResolutionActions.KeepCustom,
            MaxInputTokens = 983_040,
            MaxOutputTokens = 65_536,
            InputPricePerMillion = 0.75m,
            OutputPricePerMillion = 3.75m,
            CachedInputPricePerMillion = 0.075m
        };

        var outcome = _service.KeepAsCustom(Retired37Flash(), request);

        var updated = outcome.Updated!;
        Assert.Equal(983_040, updated.MaxInputTokens);
        Assert.Equal(65_536, updated.MaxOutputTokens);
        Assert.Equal("custom", updated.PricingMode);
        Assert.Equal(0.75m, updated.InputPricePerMillion);
        Assert.Equal(3.75m, updated.OutputPricePerMillion);
        Assert.Equal(0.075m, updated.CachedInputPricePerMillion);

        Assert.Equal("Custom", ChangeOf(outcome, "Pricing")!.To);
        Assert.Equal("0.75", ChangeOf(outcome, "Input price per million")!.To);
        Assert.Equal("983040", ChangeOf(outcome, "Max input tokens")!.To);
    }

    [Fact]
    public void KeepAsCustom_WithOnlyOnePrice_LeavesPricingUnchanged()
    {
        var request = new ModelResolutionRequest { Action = ModelResolutionActions.KeepCustom, InputPricePerMillion = 0.75m };

        var outcome = _service.KeepAsCustom(Retired37Flash(), request);

        Assert.Equal("default", outcome.Updated!.PricingMode);
        Assert.Null(outcome.Updated.InputPricePerMillion);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(null, 0)]
    public void KeepAsCustom_NonPositiveTokenCount_IsRefused(int? maxInput, int? maxOutput)
    {
        var request = new ModelResolutionRequest
        {
            Action = ModelResolutionActions.KeepCustom,
            MaxInputTokens = maxInput,
            MaxOutputTokens = maxOutput
        };

        var outcome = _service.KeepAsCustom(Retired37Flash(), request);

        Assert.True(outcome.IsRefused);
        Assert.Null(outcome.Updated);
    }

    [Fact]
    public void KeepAsCustom_NegativePrice_IsRefused()
    {
        var request = new ModelResolutionRequest
        {
            Action = ModelResolutionActions.KeepCustom,
            InputPricePerMillion = -1m,
            OutputPricePerMillion = 3.75m
        };

        Assert.True(_service.KeepAsCustom(Retired37Flash(), request).IsRefused);
    }

    [Fact]
    public void KeepAsCustom_AlreadyCustom_HasNoChanges()
    {
        var current = Retired37Flash() with { ModelCatalogMode = "custom" };

        var outcome = _service.KeepAsCustom(current, new ModelResolutionRequest { Action = ModelResolutionActions.KeepCustom });

        Assert.False(outcome.IsRefused);
        Assert.Empty(outcome.Changes);
    }

    // ---------------------------------------------------------------------------------------------
    // Dispatch and mapping.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_DispatchesOnTheAction()
    {
        var switched = _service.Resolve(Retired37Flash(), new ModelResolutionRequest { Action = "switch", TargetModelId = "gemini-3.8-flash" });
        var kept = _service.Resolve(Retired37Flash(), new ModelResolutionRequest { Action = "keepCustom" });
        var unknown = _service.Resolve(Retired37Flash(), new ModelResolutionRequest { Action = "delete" });

        Assert.Equal("gemini-3.8-flash", switched.Updated!.ModelId);
        Assert.Equal(ModelCatalogModes.Custom, kept.Updated!.ModelCatalogMode);
        Assert.True(unknown.IsRefused);
    }

    [Fact]
    public void ModelSettings_RoundTripsThroughBothEntities()
    {
        var userModel = new UserAiModel
        {
            Provider = "Google",
            ModelId = "gemini-3.7-flash",
            DisplayName = "Gemini 3.7 Flash",
            DisplayNameMode = "model_name",
            ThinkingLevel = "high",
            MaxOutputTokens = 8_000
        };
        var config = new SystemAiApiConfiguration
        {
            Provider = "Google",
            ModelId = "gemini-3.7-flash",
            DisplayName = "Gemini 3.7 Flash",
            DisplayNameMode = "model_name",
            PricingMode = "custom",
            InputPricePerMillion = 1.00m,
            OutputPricePerMillion = 2.00m
        };

        var userOutcome = _service.Switch(ModelSettings.FromUserModel(userModel), "gemini-3.8-flash");
        var configOutcome = _service.Switch(ModelSettings.FromSystemConfig(config), "gemini-3.8-flash");
        userOutcome.Updated!.ApplyTo(userModel);
        configOutcome.Updated!.ApplyTo(config);

        Assert.Equal("gemini-3.8-flash", userModel.ModelId);
        Assert.Equal("Gemini 3.8 Flash", userModel.DisplayName);
        Assert.Equal("high", userModel.ThinkingLevel);
        Assert.Equal(8_000, userModel.MaxOutputTokens);
        Assert.Equal("catalog", userModel.ModelCatalogMode);

        Assert.Equal("gemini-3.8-flash", config.ModelId);
        Assert.Equal("Gemini 3.8 Flash", config.DisplayName);
        Assert.Equal("custom", config.PricingMode);
        Assert.Equal(1.00m, config.InputPricePerMillion);
        Assert.Equal("catalog", config.ModelCatalogMode);
    }
}
