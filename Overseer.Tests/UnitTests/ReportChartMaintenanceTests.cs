using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The clear-report-charts maintenance action and the chart-storage configuration alert.
/// </summary>
public class ReportChartMaintenanceTests
{
    private const long ExistingDocumentId = 5;

    private static (AdminController controller, ApplicationDbContext db, ChatRetentionService retention) CreateTestController()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
                { "AiRateLimitSettings:MaxConcurrentModelCalls", "2" },
                { "AiRateLimitSettings:MaxRetryAfterSeconds", "90" }
            })
            .Build();

        var cryptoService = new CryptoService(config);
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var metadataService = new ModelMetadataService();
        var pricingService = new ModelPricingService(metadataService, db);
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);
        var usageGuard = new SystemConfigUsageGuard(
            db,
            new BenchmarkDifficultyJobManager(),
            new BenchmarkGenerationJobManager(),
            new BenchmarkRubricCheckJobManager(),
            new BenchmarkRubricGapAuthorJobManager(),
            new BenchmarkReportPackJobManager());
        var controller = new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, usageGuard, pricingService);
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return (controller, db, retention);
    }

    private static async Task SeedDocumentAsync(ApplicationDbContext db)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.InternalBrief);
        document.Id = ExistingDocumentId;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A chart folder for the existing document and one for a document that no longer exists.</summary>
    private static async Task SeedChartsAsync(BenchmarkReportChartStore store)
    {
        var ct = TestContext.Current.CancellationToken;
        var validated = BenchmarkReportChartStore.ValidateCharts(new[]
        {
            new ReportDocumentChartUpload
            {
                FigureKey = "p1a-quality",
                Naming = BenchmarkReportChartStore.Named,
                Title = "Quality",
                AltText = "Quality index by model",
                SettingsHash = new string('c', 64),
                PngBase64 = TestPngs.MakeBase64(640, 400)
            }
        });

        await store.SetChartsAsync(ExistingDocumentId, validated, ct);
        await store.SetChartsAsync(99, validated, ct);
    }

    private static MaintenanceResultDto ResultOf(IActionResult actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        return Assert.IsType<MaintenanceResultDto>(ok.Value);
    }

    [Fact]
    public async Task ClearReportCharts_DryRun_RecordsTheCountsAndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        var (controller, db, retention) = CreateTestController();
        await SeedDocumentAsync(db);
        await SeedChartsAsync(charts.Store);
        File.WriteAllText(Path.Combine(charts.Root, "notes.txt"), "not a chart");

        var result = ResultOf(await controller.ClearReportCharts(new MaintenanceRequestDto { DryRun = true }, retention, charts.Store));

        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.Equal(MaintenanceTriggers.ClearReportCharts, result.Trigger);
        Assert.Equal(2, result.DeletedDiskFolderCount);
        Assert.Equal(4, result.DeletedDiskFileCount);
        Assert.True(result.ReclaimedDiskBytes > 0);
        Assert.Contains(result.Logs, l => l.StartsWith("[DRY RUN] Would delete 2 chart folders (4 files, ", StringComparison.Ordinal)
            && l.EndsWith("1 of them for documents that no longer exist.", StringComparison.Ordinal));
        Assert.Contains(result.Logs, l => l == "Left alone: notes.txt");

        Assert.True(Directory.Exists(Path.Combine(charts.Root, ExistingDocumentId.ToString())));
        Assert.True(Directory.Exists(Path.Combine(charts.Root, "99")));

        var row = Assert.Single(await db.MaintenanceRunLogs.ToListAsync(ct));
        Assert.Equal(MaintenanceTriggers.ClearReportCharts, row.Trigger);
        Assert.True(row.IsDryRun);
        Assert.True(row.Success);
        Assert.Equal(2, row.DeletedDiskFolderCount);
        Assert.Equal(4, row.DeletedDiskFileCount);
        Assert.Equal(result.ReclaimedDiskBytes, row.ReclaimedDiskBytes);
    }

    [Fact]
    public async Task ClearReportCharts_RealRun_DeletesTheFoldersAndRecordsTheCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        var (controller, db, retention) = CreateTestController();
        await SeedDocumentAsync(db);
        await SeedChartsAsync(charts.Store);

        var result = ResultOf(await controller.ClearReportCharts(new MaintenanceRequestDto { DryRun = false }, retention, charts.Store));

        Assert.True(result.Success);
        Assert.False(result.IsDryRun);
        Assert.Equal(2, result.DeletedDiskFolderCount);
        Assert.Equal(4, result.DeletedDiskFileCount);
        Assert.Contains(result.Logs, l => l.StartsWith("Deleted 2 chart folders (4 files, ", StringComparison.Ordinal)
            && l.EndsWith("1 of them for documents that no longer exist.", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Logs, l => l.StartsWith("Left alone", StringComparison.Ordinal));

        Assert.False(Directory.Exists(Path.Combine(charts.Root, ExistingDocumentId.ToString())));
        Assert.False(Directory.Exists(Path.Combine(charts.Root, "99")));
        Assert.True(Directory.Exists(charts.Root));

        var row = Assert.Single(await db.MaintenanceRunLogs.ToListAsync(ct));
        Assert.False(row.IsDryRun);
        Assert.Equal(2, row.DeletedDiskFolderCount);
        Assert.Equal(4, row.DeletedDiskFileCount);
        Assert.Equal(result.ReclaimedDiskBytes, row.ReclaimedDiskBytes);
    }

    [Fact]
    public async Task ClearReportCharts_Unconfigured_IsBadRequestAndRecordsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, retention) = CreateTestController();

        var actionResult = await controller.ClearReportCharts(new MaintenanceRequestDto { DryRun = true }, retention, TestChartStores.Unconfigured());

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var message = badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value) as string;
        Assert.Equal(BenchmarkReportChartStore.NotConfiguredMessage, message);
        Assert.Empty(await db.MaintenanceRunLogs.ToListAsync(ct));
    }

    public static TheoryData<string?> UnusableRoots => new() { null!, "", "   ", "relative\\charts" };

    [Theory]
    [MemberData(nameof(UnusableRoots))]
    public void ConfigHealthService_WhenChartsLocationUnusable_ReturnsAlert(string? root)
    {
        var healthService = new ConfigHealthService(TestChartStores.ConfigurationFor(root));

        var alert = healthService.GetSystemAlerts().FirstOrDefault(a => a.Id == "report-charts-location-missing");

        Assert.NotNull(alert);
        Assert.Equal("warning", alert.Type);
        Assert.Contains("Benchmark:ReportPack:ChartsDataLocation", alert.Message);
    }

    [Fact]
    public void ConfigHealthService_WhenChartsLocationAbsolute_NoAlert()
    {
        var root = Path.Combine(Path.GetTempPath(), "OverseerChartAlertTest");
        var healthService = new ConfigHealthService(TestChartStores.ConfigurationFor(root));

        Assert.DoesNotContain(healthService.GetSystemAlerts(), a => a.Id == "report-charts-location-missing");
    }
}
