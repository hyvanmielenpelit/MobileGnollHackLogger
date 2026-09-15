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

public class AdminMaintenanceHistoryTests
{
    private const int SeededRunCount = 25;

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
    /// Ids 1..25 with ascending start times, so id 25 is the newest. Every third run has log
    /// text and every fifth an error message.
    /// </summary>
    private static async Task SeedRunsAsync(ApplicationDbContext db)
    {
        var start = new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Unspecified);
        for (var i = 1; i <= SeededRunCount; i++)
        {
            db.MaintenanceRunLogs.Add(new MaintenanceRunLog
            {
                Id = i,
                StartedUtc = start.AddHours(i),
                CompletedUtc = start.AddHours(i).AddSeconds(1),
                Trigger = MaintenanceTriggers.Scheduled,
                Success = i % 5 != 0,
                ElapsedMilliseconds = i * 10,
                LogText = i % 3 == 0 ? $"log of run {i}" : null,
                ErrorMessage = i % 5 == 0 ? $"error of run {i}" : null
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static MaintenanceHistoryPageDto PageOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<MaintenanceHistoryPageDto>(ok.Value);
    }

    [Fact]
    public async Task GetMaintenanceHistory_Defaults_ReturnsFirstTenNewestFirstWithTotal()
    {
        var (controller, db) = CreateTestController();
        await SeedRunsAsync(db);

        var page = PageOf(await controller.GetMaintenanceHistory());

        Assert.Equal(SeededRunCount, page.TotalCount);
        Assert.Equal(Enumerable.Range(16, 10).Reverse().Select(i => (long)i), page.Rows.Select(r => r.Id));
        Assert.All(page.Rows, r => Assert.Equal(DateTimeKind.Utc, r.StartedUtc.Kind));
        Assert.All(page.Rows, r => Assert.Equal(DateTimeKind.Utc, r.CompletedUtc!.Value.Kind));
    }

    [Fact]
    public async Task GetMaintenanceHistory_LastPartialPage_ReturnsRemainingRows()
    {
        var (controller, db) = CreateTestController();
        await SeedRunsAsync(db);

        var page = PageOf(await controller.GetMaintenanceHistory(page: 3, pageSize: 10));

        Assert.Equal(SeededRunCount, page.TotalCount);
        Assert.Equal(new long[] { 5, 4, 3, 2, 1 }, page.Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task GetMaintenanceHistory_OutOfRangeArguments_AreClamped()
    {
        var (controller, db) = CreateTestController();
        await SeedRunsAsync(db);

        var oversized = PageOf(await controller.GetMaintenanceHistory(page: 1, pageSize: 5000));
        Assert.Equal(SeededRunCount, oversized.Rows.Count);

        var pageZero = PageOf(await controller.GetMaintenanceHistory(page: 0, pageSize: 10));
        Assert.Equal(25L, pageZero.Rows.First().Id);

        var sizeZero = PageOf(await controller.GetMaintenanceHistory(page: 1, pageSize: 0));
        Assert.Single(sizeZero.Rows);
    }

    [Fact]
    public async Task GetMaintenanceHistory_HasLog_IsTrueOnlyWhenLogTextOrErrorExists()
    {
        var (controller, db) = CreateTestController();
        await SeedRunsAsync(db);

        var page = PageOf(await controller.GetMaintenanceHistory(page: 1, pageSize: 1000));

        Assert.Equal(SeededRunCount, page.Rows.Count);
        foreach (var row in page.Rows)
        {
            var expected = row.Id % 3 == 0 || row.Id % 5 == 0;
            Assert.Equal(expected, row.HasLog);
        }
    }

    [Fact]
    public async Task GetMaintenanceRunLog_ReturnsTextForKnownId_AndNotFoundForUnknown()
    {
        var (controller, db) = CreateTestController();
        await SeedRunsAsync(db);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetMaintenanceRunLog(15));
        var text = Assert.IsType<MaintenanceRunLogTextDto>(ok.Value);
        Assert.Equal("log of run 15", text.LogText);
        Assert.Equal("error of run 15", text.ErrorMessage);

        Assert.IsType<NotFoundResult>(await controller.GetMaintenanceRunLog(9999));
    }
}
