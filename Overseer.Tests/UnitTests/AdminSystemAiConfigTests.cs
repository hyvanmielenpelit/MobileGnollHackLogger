using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AdminSystemAiConfigTests
{
    private static (AdminController controller, ApplicationDbContext db, CryptoService crypto) CreateTestController()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
            { "AiRateLimitSettings:MaxConcurrentModelCalls", "2" },
            { "AiRateLimitSettings:MaxRetryAfterSeconds", "90" }
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var cryptoService = new CryptoService(config);
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var metadataService = new ModelMetadataService();
        var pricingService = new ModelPricingService(metadataService, db);
        var controller = new AdminController(db, config, null!, cryptoService, governor, pricingService);

        return (controller, db, cryptoService);
    }

    [Fact]
    public async Task CreateSystemConfig_WithApiKey_EncryptsAndPersistsApiKey_And_HasApiKeyIsTrue()
    {
        var (controller, db, crypto) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var request = new CreateSystemAiApiConfigurationRequest
        {
            DisplayName = "GPT-5 Test",
            Provider = "OpenAI",
            ModelId = "gpt-5",
            IsEnabled = true,
            IsSystemWide = true,
            ApiKey = "sk-test-secret-key-123"
        };

        var result = await controller.CreateSystemConfig(request);
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var saved = await db.SystemAiApiConfigurations.FirstOrDefaultAsync(ct);
        Assert.NotNull(saved);
        Assert.Equal("GPT-5 Test", saved.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(saved.EncryptedApiKey));
        Assert.False(string.IsNullOrWhiteSpace(saved.ApiKeyNonce));
        Assert.False(string.IsNullOrWhiteSpace(saved.ApiKeyTag));

        var decrypted = crypto.Decrypt(saved.EncryptedApiKey!, saved.ApiKeyNonce!, saved.ApiKeyTag!, "SYSTEM_API_KEY");
        Assert.Equal("sk-test-secret-key-123", decrypted);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First();
        Assert.True(dto.HasApiKey);
    }

    [Fact]
    public async Task CreateSystemConfig_WithoutApiKey_LeavesApiKeyNull_And_HasApiKeyIsFalse()
    {
        var (controller, db, _) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var request = new CreateSystemAiApiConfigurationRequest
        {
            DisplayName = "Claude Test",
            Provider = "Anthropic",
            ModelId = "claude-4",
            IsEnabled = true,
            ApiKey = null
        };

        var result = await controller.CreateSystemConfig(request);
        Assert.IsType<OkObjectResult>(result);

        var saved = await db.SystemAiApiConfigurations.FirstOrDefaultAsync(ct);
        Assert.NotNull(saved);
        Assert.Null(saved.EncryptedApiKey);
        Assert.Null(saved.ApiKeyNonce);
        Assert.Null(saved.ApiKeyTag);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First();
        Assert.False(dto.HasApiKey);
    }

    [Fact]
    public async Task UpdateSystemConfig_WithNewApiKey_ReEncryptsAndUpdatesFields()
    {
        var (controller, db, crypto) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var initial = new SystemAiApiConfiguration
        {
            DisplayName = "Initial Config",
            Provider = "Google",
            ModelId = "gemini-3-flash",
            IsEnabled = true,
            OrderIndex = 0
        };
        db.SystemAiApiConfigurations.Add(initial);
        await db.SaveChangesAsync(ct);

        var updateRequest = new UpdateSystemAiApiConfigurationRequest
        {
            DisplayName = "Initial Config",
            Provider = "Google",
            ModelId = "gemini-3-flash",
            IsEnabled = true,
            ApiKey = "google-new-api-key"
        };

        var updateResult = await controller.UpdateSystemConfig(initial.Id, updateRequest);
        Assert.IsType<OkResult>(updateResult);

        var updated = await db.SystemAiApiConfigurations.FindAsync(new object?[] { initial.Id }, ct);
        Assert.NotNull(updated);
        Assert.False(string.IsNullOrWhiteSpace(updated.EncryptedApiKey));
        Assert.False(string.IsNullOrWhiteSpace(updated.ApiKeyNonce));
        Assert.False(string.IsNullOrWhiteSpace(updated.ApiKeyTag));

        var decrypted = crypto.Decrypt(updated.EncryptedApiKey!, updated.ApiKeyNonce!, updated.ApiKeyTag!, "SYSTEM_API_KEY");
        Assert.Equal("google-new-api-key", decrypted);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First();
        Assert.True(dto.HasApiKey);
    }

    [Fact]
    public async Task UpdateSystemConfig_WithEmptyApiKey_ClearsApiKeyFields()
    {
        var (controller, db, crypto) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var (ciphertext, nonce, tag) = crypto.Encrypt("initial-key", "SYSTEM_API_KEY");
        var initial = new SystemAiApiConfiguration
        {
            DisplayName = "Existing Key Config",
            Provider = "OpenAI",
            ModelId = "gpt-5",
            IsEnabled = true,
            OrderIndex = 0,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
        };
        db.SystemAiApiConfigurations.Add(initial);
        await db.SaveChangesAsync(ct);

        var updateRequest = new UpdateSystemAiApiConfigurationRequest
        {
            DisplayName = "Existing Key Config",
            Provider = "OpenAI",
            ModelId = "gpt-5",
            IsEnabled = true,
            ApiKey = "   " // Whitespace to clear
        };

        var updateResult = await controller.UpdateSystemConfig(initial.Id, updateRequest);
        Assert.IsType<OkResult>(updateResult);

        var updated = await db.SystemAiApiConfigurations.FindAsync(new object?[] { initial.Id }, ct);
        Assert.NotNull(updated);
        Assert.Null(updated.EncryptedApiKey);
        Assert.Null(updated.ApiKeyNonce);
        Assert.Null(updated.ApiKeyTag);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First();
        Assert.False(dto.HasApiKey);
    }

    [Fact]
    public async Task UpdateSystemConfig_WithNullApiKey_RetainsExistingKey()
    {
        var (controller, db, crypto) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var (ciphertext, nonce, tag) = crypto.Encrypt("original-preserved-key", "SYSTEM_API_KEY");
        var initial = new SystemAiApiConfiguration
        {
            DisplayName = "Original Name",
            Provider = "Anthropic",
            ModelId = "claude-4",
            IsEnabled = true,
            OrderIndex = 0,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
        };
        db.SystemAiApiConfigurations.Add(initial);
        await db.SaveChangesAsync(ct);

        var updateRequest = new UpdateSystemAiApiConfigurationRequest
        {
            DisplayName = "Updated Name",
            Provider = "Anthropic",
            ModelId = "claude-4",
            IsEnabled = true,
            ApiKey = null // null means do not modify key
        };

        var updateResult = await controller.UpdateSystemConfig(initial.Id, updateRequest);
        Assert.IsType<OkResult>(updateResult);

        var updated = await db.SystemAiApiConfigurations.FindAsync(new object?[] { initial.Id }, ct);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.DisplayName);
        Assert.Equal(ciphertext, updated.EncryptedApiKey);
        Assert.Equal(nonce, updated.ApiKeyNonce);
        Assert.Equal(tag, updated.ApiKeyTag);

        var decrypted = crypto.Decrypt(updated.EncryptedApiKey!, updated.ApiKeyNonce!, updated.ApiKeyTag!, "SYSTEM_API_KEY");
        Assert.Equal("original-preserved-key", decrypted);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First();
        Assert.True(dto.HasApiKey);
    }

    [Fact]
    public async Task CreateSystemConfig_WithCustomPricing_PersistsOverrides_AndReturnsEffectivePricing()
    {
        var (controller, db, _) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var request = new CreateSystemAiApiConfigurationRequest
        {
            DisplayName = "Custom Pricing Config",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = 3.50m,
            OutputPricePerMillion = 12.00m,
            CachedInputPricePerMillion = 0.50m
        };

        var result = await controller.CreateSystemConfig(request);
        Assert.IsType<OkObjectResult>(result);

        var saved = await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.DisplayName == "Custom Pricing Config", ct);
        Assert.NotNull(saved);
        Assert.Equal("custom", saved.PricingMode);
        Assert.Equal(3.50m, saved.InputPricePerMillion);
        Assert.Equal(12.00m, saved.OutputPricePerMillion);
        Assert.Equal(0.50m, saved.CachedInputPricePerMillion);

        var listResult = await controller.GetSystemConfigs();
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var configs = Assert.IsAssignableFrom<IEnumerable<SystemAiApiConfigurationDto>>(okList.Value);
        var dto = configs.First(c => c.DisplayName == "Custom Pricing Config");
        Assert.Equal("custom", dto.PricingMode);
        Assert.Equal(3.50m, dto.EffectiveInputPricePerMillion);
        Assert.Equal(12.00m, dto.EffectiveOutputPricePerMillion);
        Assert.Equal(0.50m, dto.EffectiveCachedInputPricePerMillion);
        Assert.Equal("custom", dto.PricingSource);
    }

    [Fact]
    public async Task CreateSystemConfig_WithNegativePrice_ReturnsBadRequest()
    {
        var (controller, _, _) = CreateTestController();

        var request = new CreateSystemAiApiConfigurationRequest
        {
            DisplayName = "Invalid Negative Price",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = -1.00m,
            OutputPricePerMillion = 10.00m
        };

        var result = await controller.CreateSystemConfig(request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Prices cannot be negative.", badRequest.Value);
    }

    [Fact]
    public async Task CreateSystemConfig_WithCustomModeAndNullOutput_ReturnsBadRequest()
    {
        var (controller, _, _) = CreateTestController();

        var request = new CreateSystemAiApiConfigurationRequest
        {
            DisplayName = "Missing Output Rate",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = 2.50m,
            OutputPricePerMillion = null
        };

        var result = await controller.CreateSystemConfig(request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Custom pricing requires both input and output prices per million.", badRequest.Value);
    }

    [Fact]
    public async Task UpdateSystemConfig_WithCustomPricing_UpdatesFields()
    {
        var (controller, db, _) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var initial = new SystemAiApiConfiguration
        {
            DisplayName = "Initial Default Config",
            Provider = "Anthropic",
            ModelId = "claude-sonnet-4-6",
            IsEnabled = true,
            OrderIndex = 0,
            PricingMode = "default"
        };
        db.SystemAiApiConfigurations.Add(initial);
        await db.SaveChangesAsync(ct);

        var updateRequest = new UpdateSystemAiApiConfigurationRequest
        {
            DisplayName = "Initial Default Config",
            Provider = "Anthropic",
            ModelId = "claude-sonnet-4-6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = 4.00m,
            OutputPricePerMillion = 18.00m,
            CachedInputPricePerMillion = 0.40m
        };

        var updateResult = await controller.UpdateSystemConfig(initial.Id, updateRequest);
        Assert.IsType<OkResult>(updateResult);

        var updated = await db.SystemAiApiConfigurations.FindAsync(new object?[] { initial.Id }, ct);
        Assert.NotNull(updated);
        Assert.Equal("custom", updated.PricingMode);
        Assert.Equal(4.00m, updated.InputPricePerMillion);
        Assert.Equal(18.00m, updated.OutputPricePerMillion);
        Assert.Equal(0.40m, updated.CachedInputPricePerMillion);
    }

    [Fact]
    public async Task UpdateSystemConfig_WithNegativePrice_ReturnsBadRequest()
    {
        var (controller, db, _) = CreateTestController();
        var ct = TestContext.Current.CancellationToken;

        var initial = new SystemAiApiConfiguration
        {
            DisplayName = "Initial Config",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            OrderIndex = 0
        };
        db.SystemAiApiConfigurations.Add(initial);
        await db.SaveChangesAsync(ct);

        var updateRequest = new UpdateSystemAiApiConfigurationRequest
        {
            DisplayName = "Initial Config",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = 2.00m,
            OutputPricePerMillion = -5.00m
        };

        var updateResult = await controller.UpdateSystemConfig(initial.Id, updateRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(updateResult);
        Assert.Equal("Prices cannot be negative.", badRequest.Value);
    }
}
