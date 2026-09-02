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
        var service = new SettingsService(db, cryptoService);

        return (service, db);
    }

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

        var controller = new SettingsController(service, null!, null!, null!, null!, null!, Array.Empty<Overseer.Services.Providers.IAiProvider>());
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
}
