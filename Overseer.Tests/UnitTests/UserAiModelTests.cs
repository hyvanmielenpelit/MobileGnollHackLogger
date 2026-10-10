using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class UserAiModelTests
{
    private static (SettingsService service, ApplicationDbContext db) CreateTestSettingsService()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var keyBytes = new byte[32];
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(keyBytes) }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        var cryptoService = new CryptoService(config);
        var service = new SettingsService(
            db, cryptoService, new Overseer.Services.Privacy.ConfidentialityPostureService(config),
            new ModelAvailabilityService(Metadata));

        return (service, db);
    }

    private static readonly ModelMetadataService Metadata = new();

    private static SettingsController CreateController(SettingsService service, ApplicationDbContext db, string userId)
    {
        var controller = new SettingsController(
            service, null!, null!, Metadata, null!, null!,
            new Overseer.Services.Privacy.EndpointPolicy(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.ConfidentialPolicyResolver(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.Dlp.DlpScannerService(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.AttachmentValidator(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.EphemeralSessionStore(
                new ConfigurationBuilder().Build(), null, startSweeper: false),
            Array.Empty<Overseer.Services.Providers.IAiProvider>(),
            new ModelPricingService(Metadata, db),
            new ModelAvailabilityService(Metadata),
            new ModelResolutionService(Metadata));
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private static object? Prop(object item, string name) => item.GetType().GetProperty(name)!.GetValue(item);

    private static async Task<UserAiModel> AddRowAsync(
        ApplicationDbContext db, string userId, string modelId, string? catalogMode = ModelCatalogModes.Catalog)
    {
        var row = new UserAiModel
        {
            AspNetUserId = userId,
            Provider = "Google",
            ModelId = modelId,
            DisplayName = modelId == "gemini-3.7-flash" ? "Gemini 3.7 Flash" : modelId,
            DisplayNameMode = DisplayNameModes.ModelName,
            ThinkingLevel = "high",
            ModelCatalogMode = catalogMode
        };
        db.UserAiModels.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return row;
    }

    private static Task UpdateViaServiceAsync(SettingsService service, string userId, UserAiModel row, string? modelId, string? displayName)
        => service.UpdateUserModelAsync(
            userId: userId,
            id: row.Id,
            displayName: displayName,
            displayNameMode: DisplayNameModes.Custom,
            thinkingLevel: "high",
            reasoningMode: null,
            reasoningSummary: null,
            serviceTier: null,
            maxInputTokens: null,
            maxOutputTokens: null,
            modelId: modelId,
            provider: "Google");

    [Fact]
    public async Task UpdateUserModelAsync_WithNewModelId_UpdatesModelIdAndDisplayName()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "test-user-1";
        var ct = TestContext.Current.CancellationToken;

        var initial = new UserAiModel
        {
            AspNetUserId = userId,
            Provider = "Google",
            ModelId = "gemini-3.6-flash",
            DisplayName = "Gemini 3.6 Flash",
            DisplayNameMode = "model_name",
            ThinkingLevel = "high",
            OrderIndex = 0
        };
        db.UserAiModels.Add(initial);
        await db.SaveChangesAsync(ct);

        await service.UpdateUserModelAsync(
            userId: userId,
            id: initial.Id,
            displayName: "Gemini 3.7 Flash",
            displayNameMode: "model_name",
            thinkingLevel: "high",
            reasoningMode: null,
            reasoningSummary: null,
            serviceTier: null,
            maxInputTokens: null,
            maxOutputTokens: null,
            modelId: "gemini-3.7-flash",
            provider: "Google"
        );

        var updated = await db.UserAiModels.FirstOrDefaultAsync(m => m.Id == initial.Id, ct);
        Assert.NotNull(updated);
        Assert.Equal("gemini-3.7-flash", updated.ModelId);
        Assert.Equal("Google", updated.Provider);
        Assert.Equal("Gemini 3.7 Flash", updated.DisplayName);
    }

    [Fact]
    public async Task UpdateUserModelAsync_WithNullModelId_PreservesExistingModelId()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "test-user-2";
        var ct = TestContext.Current.CancellationToken;

        var initial = new UserAiModel
        {
            AspNetUserId = userId,
            Provider = "Google",
            ModelId = "gemini-3.6-flash",
            DisplayName = "Gemini 3.6 Flash",
            DisplayNameMode = "model_name",
            OrderIndex = 0
        };
        db.UserAiModels.Add(initial);
        await db.SaveChangesAsync(ct);

        await service.UpdateUserModelAsync(
            userId: userId,
            id: initial.Id,
            displayName: "My Custom Name",
            displayNameMode: "custom",
            thinkingLevel: null,
            reasoningMode: null,
            reasoningSummary: null,
            serviceTier: null,
            maxInputTokens: null,
            maxOutputTokens: null,
            modelId: null,
            provider: null
        );

        var updated = await db.UserAiModels.FirstOrDefaultAsync(m => m.Id == initial.Id, ct);
        Assert.NotNull(updated);
        Assert.Equal("gemini-3.6-flash", updated.ModelId);
        Assert.Equal("Google", updated.Provider);
        Assert.Equal("My Custom Name", updated.DisplayName);
    }

    [Fact]
    public async Task UpdateUserModel_ViaController_UpdatesModelId()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "test-user-3";
        var ct = TestContext.Current.CancellationToken;

        var initial = new UserAiModel
        {
            AspNetUserId = userId,
            Provider = "Google",
            ModelId = "gemini-3.6-flash",
            DisplayName = "Gemini 3.6 Flash",
            OrderIndex = 0
        };
        db.UserAiModels.Add(initial);
        await db.SaveChangesAsync(ct);

        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(
            new ConfigurationBuilder().Build());
        var policyResolver = new Overseer.Services.Privacy.ConfidentialPolicyResolver(
            new ConfigurationBuilder().Build());
        var controller = new SettingsController(
            service, null!, null!, null!, null!, null!, endpointPolicy, policyResolver,
            new Overseer.Services.Privacy.Dlp.DlpScannerService(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.AttachmentValidator(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.EphemeralSessionStore(
                new ConfigurationBuilder().Build(), null, startSweeper: false),
            Array.Empty<Overseer.Services.Providers.IAiProvider>());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var request = new UpdateUserModelRequest
        {
            ModelId = "gemini-3.7-flash",
            Provider = "Google",
            DisplayName = "Gemini 3.7 Flash",
            DisplayNameMode = "model_name"
        };

        var result = await controller.UpdateUserModel(initial.Id, request);
        Assert.IsType<OkResult>(result);

        var updated = await db.UserAiModels.FirstOrDefaultAsync(m => m.Id == initial.Id, ct);
        Assert.NotNull(updated);
        Assert.Equal("gemini-3.7-flash", updated.ModelId);
        Assert.Equal("Google", updated.Provider);
        Assert.Equal("Gemini 3.7 Flash", updated.DisplayName);
    }

    // -- Model availability ---------------------------------------------------------------------

    [Fact]
    public async Task GetUserModels_EveryItemCarriesItsModelAvailability()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "availability-user";
        var ct = TestContext.Current.CancellationToken;

        var available = await AddRowAsync(db, userId, "gemini-3.8-flash");
        var retired = await AddRowAsync(db, userId, "gemini-3.7-flash");
        var custom = await AddRowAsync(db, userId, "my-tuned-flash", ModelCatalogModes.Custom);
        custom.MaxInputTokens = 200000;
        custom.MaxOutputTokens = 8000;
        custom.PricingMode = PricingModes.Custom;
        custom.InputPricePerMillion = 1.25m;
        custom.OutputPricePerMillion = 5.00m;
        custom.CachedInputPricePerMillion = 0.10m;
        var unknown = await AddRowAsync(db, userId, "my-other-flash");
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            DisplayName = "Gateway Flash",
            Provider = "Google",
            ModelId = "gemini-3.7-flash",
            BaseUrl = "https://llm.example.com/v1",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 7
        });
        await db.SaveChangesAsync(ct);

        var ok = Assert.IsType<OkObjectResult>(await CreateController(service, db, userId).GetUserModels());
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(5, items.Count);

        ModelAvailabilityDto AvailabilityOf(Func<object, bool> match) =>
            Assert.IsType<ModelAvailabilityDto>(Prop(items.Single(match), "ModelAvailability"));

        Assert.Equal("available", AvailabilityOf(i => (long)Prop(i, "Id")! == available.Id && !(bool)Prop(i, "IsSystem")!).Status);
        Assert.Equal("notInCatalog", AvailabilityOf(i => (long)Prop(i, "Id")! == unknown.Id && !(bool)Prop(i, "IsSystem")!).Status);
        Assert.Equal("customEndpoint", AvailabilityOf(i => (bool)Prop(i, "IsSystem")!).Status);

        var retiredAvailability = AvailabilityOf(i => (long)Prop(i, "Id")! == retired.Id && !(bool)Prop(i, "IsSystem")!);
        Assert.Equal("retired", retiredAvailability.Status);
        Assert.True(retiredAvailability.NeedsAttention);
        Assert.Equal("2026-10-10", retiredAvailability.RetiredOn);
        Assert.Equal("gemini-3.8-flash", retiredAvailability.Replacement?.ModelId);
        Assert.Equal(65536, retiredAvailability.SuggestedCustom?.MaxOutputTokens);

        var customItem = items.Single(i => (long)Prop(i, "Id")! == custom.Id && !(bool)Prop(i, "IsSystem")!);
        Assert.Equal(ModelCatalogModes.Custom, Prop(customItem, "ModelCatalogMode"));
        var customAvailability = Assert.IsType<ModelAvailabilityDto>(Prop(customItem, "ModelAvailability"));
        Assert.Equal("custom", customAvailability.Status);
        Assert.False(customAvailability.NeedsAttention);
        Assert.NotNull(customAvailability.SuggestedCustom);
        Assert.Equal(200000, customAvailability.SuggestedCustom.MaxInputTokens);
        Assert.Equal(8000, customAvailability.SuggestedCustom.MaxOutputTokens);
        Assert.Equal(1.25m, customAvailability.SuggestedCustom.InputPricePerMillion);
        Assert.Equal(5.00m, customAvailability.SuggestedCustom.OutputPricePerMillion);
        Assert.Equal(0.10m, customAvailability.SuggestedCustom.CachedInputPricePerMillion);
    }

    [Fact]
    public async Task AddUserModelAsync_DerivesCustomForAnUncataloguedId_AndCatalogForACataloguedOne()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "derive-user";
        var ct = TestContext.Current.CancellationToken;

        var uncatalogued = new UserAiModel { Provider = "Google", ModelId = "my-tuned-flash" };
        var catalogued = new UserAiModel { Provider = "Google", ModelId = "gemini-3.8-flash" };
        await service.AddUserModelAsync(userId, uncatalogued);
        await service.AddUserModelAsync(userId, catalogued);

        db.ChangeTracker.Clear();
        Assert.Equal(ModelCatalogModes.Custom, (await db.UserAiModels.SingleAsync(m => m.Id == uncatalogued.Id, ct)).ModelCatalogMode);
        Assert.Equal(ModelCatalogModes.Catalog, (await db.UserAiModels.SingleAsync(m => m.Id == catalogued.Id, ct)).ModelCatalogMode);
    }

    [Fact]
    public async Task UpdateUserModelAsync_ReDerivesTheCatalogModeOnlyWhenTheModelChanges()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "rederive-user";
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, userId, "gemini-3.7-flash");

        await UpdateViaServiceAsync(service, userId, row, modelId: "gemini-3.7-flash", displayName: "Renamed");
        db.ChangeTracker.Clear();
        var renamed = await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct);
        Assert.Equal("Renamed", renamed.DisplayName);
        Assert.Equal(ModelCatalogModes.Catalog, renamed.ModelCatalogMode);

        await UpdateViaServiceAsync(service, userId, row, modelId: "my-tuned-flash", displayName: null);
        db.ChangeTracker.Clear();
        Assert.Equal(ModelCatalogModes.Custom, (await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct)).ModelCatalogMode);

        await UpdateViaServiceAsync(service, userId, row, modelId: "gemini-3.8-flash", displayName: null);
        db.ChangeTracker.Clear();
        Assert.Equal(ModelCatalogModes.Catalog, (await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct)).ModelCatalogMode);
    }

    [Fact]
    public async Task ResolveUserModel_Switch_MovesTheRowToTheCatalogModel()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "switch-user";
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, userId, "gemini-3.7-flash");

        var ok = Assert.IsType<OkObjectResult>(await CreateController(service, db, userId).ResolveUserModel(row.Id,
            new ModelResolutionRequest { Action = ModelResolutionActions.Switch, TargetModelId = "gemini-3.8-flash" }));

        var result = Assert.IsType<ModelResolutionResult>(ok.Value);
        Assert.Contains(result.Changes, c => c.Field == "Model" && c.From == "gemini-3.7-flash" && c.To == "gemini-3.8-flash");
        Assert.Empty(result.Blockers);
        Assert.NotNull(result.Model);
        Assert.Equal("gemini-3.8-flash", Prop(result.Model, "ModelId"));
        Assert.Equal("available", Assert.IsType<ModelAvailabilityDto>(Prop(result.Model, "ModelAvailability")).Status);

        db.ChangeTracker.Clear();
        var saved = await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct);
        Assert.Equal("gemini-3.8-flash", saved.ModelId);
        Assert.Equal("Gemini 3.8 Flash", saved.DisplayName);
        Assert.Equal(ModelCatalogModes.Catalog, saved.ModelCatalogMode);
    }

    [Fact]
    public async Task ResolveUserModel_KeepCustom_SavesTheCustomLimitsAndPrices()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "keep-user";
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, userId, "gemini-3.7-flash");

        var ok = Assert.IsType<OkObjectResult>(await CreateController(service, db, userId).ResolveUserModel(row.Id,
            new ModelResolutionRequest
            {
                Action = ModelResolutionActions.KeepCustom,
                MaxInputTokens = 983040,
                MaxOutputTokens = 65536,
                InputPricePerMillion = 0.75m,
                OutputPricePerMillion = 3.75m,
                CachedInputPricePerMillion = 0.075m
            }));

        var result = Assert.IsType<ModelResolutionResult>(ok.Value);
        Assert.Equal("custom", Assert.IsType<ModelAvailabilityDto>(Prop(result.Model!, "ModelAvailability")).Status);

        db.ChangeTracker.Clear();
        var saved = await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct);
        Assert.Equal("gemini-3.7-flash", saved.ModelId);
        Assert.Equal(ModelCatalogModes.Custom, saved.ModelCatalogMode);
        Assert.Equal(983040, saved.MaxInputTokens);
        Assert.Equal(65536, saved.MaxOutputTokens);
        Assert.Equal(PricingModes.Custom, saved.PricingMode);
        Assert.Equal(0.75m, saved.InputPricePerMillion);
        Assert.Equal(3.75m, saved.OutputPricePerMillion);
        Assert.Equal(0.075m, saved.CachedInputPricePerMillion);
    }

    [Fact]
    public async Task ResolveUserModel_DryRun_ReturnsTheChangedRow_AndSavesNothing()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "dry-user";
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, userId, "gemini-3.7-flash");

        var ok = Assert.IsType<OkObjectResult>(await CreateController(service, db, userId).ResolveUserModel(row.Id,
            new ModelResolutionRequest { Action = ModelResolutionActions.Switch, TargetModelId = "gemini-3.8-flash", DryRun = true }));

        var result = Assert.IsType<ModelResolutionResult>(ok.Value);
        Assert.NotEmpty(result.Changes);
        Assert.Equal("gemini-3.8-flash", Prop(result.Model!, "ModelId"));

        db.ChangeTracker.Clear();
        var unchanged = await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct);
        Assert.Equal("gemini-3.7-flash", unchanged.ModelId);
        Assert.Equal("Gemini 3.7 Flash", unchanged.DisplayName);
        Assert.Equal(ModelCatalogModes.Catalog, unchanged.ModelCatalogMode);
    }

    [Fact]
    public async Task ResolveUserModel_AnotherUsersRow_IsNotFound()
    {
        var (service, db) = CreateTestSettingsService();
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, "owner", "gemini-3.7-flash");

        var result = await CreateController(service, db, "someone-else").ResolveUserModel(row.Id,
            new ModelResolutionRequest { Action = ModelResolutionActions.Switch, TargetModelId = "gemini-3.8-flash" });

        Assert.IsType<NotFoundResult>(result);
        db.ChangeTracker.Clear();
        Assert.Equal("gemini-3.7-flash", (await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct)).ModelId);
    }

    [Fact]
    public async Task ResolveUserModel_ATargetOutsideTheCatalog_IsABadRequest()
    {
        var (service, db) = CreateTestSettingsService();
        var userId = "bad-target-user";
        var ct = TestContext.Current.CancellationToken;
        var row = await AddRowAsync(db, userId, "gemini-3.7-flash");

        var result = await CreateController(service, db, userId).ResolveUserModel(row.Id,
            new ModelResolutionRequest { Action = ModelResolutionActions.Switch, TargetModelId = "gemini-9-ultra" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var message = Assert.IsType<string>(Prop(badRequest.Value!, "message"));
        Assert.Contains("gemini-9-ultra", message);
        db.ChangeTracker.Clear();
        Assert.Equal("gemini-3.7-flash", (await db.UserAiModels.SingleAsync(m => m.Id == row.Id, ct)).ModelId);
    }

    [Fact]
    public void GetModelCatalog_ListsTheProvidersCatalogModels()
    {
        var (service, db) = CreateTestSettingsService();

        var ok = Assert.IsType<OkObjectResult>(CreateController(service, db, "catalog-user").GetModelCatalog("google"));

        var targets = Assert.IsAssignableFrom<IReadOnlyList<CatalogTargetDto>>(ok.Value);
        Assert.Contains(targets, t => t.ModelId == "gemini-3.8-flash");
        Assert.DoesNotContain(targets, t => t.ModelId == "gemini-3.7-flash");
    }
}
