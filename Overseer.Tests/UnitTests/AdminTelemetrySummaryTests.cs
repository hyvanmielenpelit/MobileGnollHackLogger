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

public class AdminTelemetrySummaryTests
{
    private static (AdminController controller, ApplicationDbContext db) CreateTestController()
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
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);
        var controller = new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, pricingService);

        return (controller, db);
    }

    /// <summary>
    /// Model X: one request, 200 uncached + 800 cache-read input tokens, 1000 ms.
    /// Model Y: three requests, 100 uncached input tokens each, 100 ms each.
    /// </summary>
    private static async Task SeedUsageAsync(ApplicationDbContext db)
    {
        const string userId = "telemetry-user";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "telemetry-user" });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 1,
            DisplayName = "Telemetry Model",
            Provider = "Anthropic",
            ModelId = "model-x"
        });

        var timestamp = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        db.SystemAiUsageLogs.Add(new SystemAiUsageLog
        {
            SystemAiApiConfigurationId = 1,
            AspNetUserId = userId,
            TimestampUtc = timestamp,
            Provider = "Anthropic",
            ModelId = "model-x",
            InputTokens = 200,
            OutputTokens = 10,
            CacheReadInputTokens = 800,
            CacheCreationInputTokens = 0,
            TotalDurationMs = 1000
        });
        for (var i = 0; i < 3; i++)
        {
            db.SystemAiUsageLogs.Add(new SystemAiUsageLog
            {
                SystemAiApiConfigurationId = 1,
                AspNetUserId = userId,
                TimestampUtc = timestamp,
                Provider = "Anthropic",
                ModelId = "model-y",
                InputTokens = 100,
                OutputTokens = 10,
                CacheReadInputTokens = 0,
                CacheCreationInputTokens = 0,
                TotalDurationMs = 100
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<AiTelemetrySummaryDto> GetSummaryAsync()
    {
        var (controller, db) = CreateTestController();
        await SeedUsageAsync(db);

        var result = await controller.GetAiTelemetrySummary(null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<AiTelemetrySummaryDto>(ok.Value);
    }

    [Fact]
    public async Task GetAiTelemetrySummary_CacheHitRatio_IsShareOfWholePrompt()
    {
        var summary = await GetSummaryAsync();

        var modelX = Assert.Single(summary.Models, m => m.ModelId == "model-x");
        Assert.Equal(0.8, modelX.CacheHitRatio);
        Assert.Equal(Math.Round(800.0 / 1300.0, 4), summary.CacheHitRatio);
    }

    [Fact]
    public async Task GetAiTelemetrySummary_AvgDuration_IsWeightedByRequests()
    {
        var summary = await GetSummaryAsync();

        Assert.Equal(4, summary.TotalRequests);
        Assert.Equal(325, summary.AvgDurationMs);
    }
}
