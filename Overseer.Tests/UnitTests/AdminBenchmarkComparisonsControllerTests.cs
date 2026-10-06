namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The comparison identity endpoints: identify a selection (the same comparison every time, refusals
/// as 400), rename and reset (404 and 400 refusals), and the newest-first list with document counts.
/// </summary>
public class AdminBenchmarkComparisonsControllerTests
{
    private const string UserId = "admin-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        AdminBenchmarkComparisonsController Controller, ApplicationDbContext Db, long FirstBattery, long SecondBattery);

    /// <summary>Two battery results of one definition, one model each, and a controller over them signed in as <see cref="UserId"/>.</summary>
    private static async Task<Fixture> CreateAsync()
    {
        var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var definition = BenchmarkBatteryTestData.Definition();
        long first = await BenchmarkBatteryTestData.SeedAsync(db, definition,
            (BenchmarkBatteryTestData.Run(9101, BenchmarkBatteryTestData.SuiteA, new[] { 70, 80 }, new[] { 50, 50 }, modelId: "gpt-5.6-luna"), 0, 1),
            (BenchmarkBatteryTestData.Run(9102, BenchmarkBatteryTestData.SuiteB, new[] { 70, 80 }, new[] { 50, 50 }, modelId: "gpt-5.6-luna"), 1, 1));
        long second = await BenchmarkBatteryTestData.SeedAsync(db, definition,
            (BenchmarkBatteryTestData.Run(9201, BenchmarkBatteryTestData.SuiteA, new[] { 60, 70 }, new[] { 50, 50 }, modelId: "gemini-3.8-flash"), 0, 1),
            (BenchmarkBatteryTestData.Run(9202, BenchmarkBatteryTestData.SuiteB, new[] { 60, 70 }, new[] { 50, 50 }, modelId: "gemini-3.8-flash"), 1, 1));
        db.ChangeTracker.Clear();

        var controller = new AdminBenchmarkComparisonsController(new BenchmarkComparisonIdentityService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "TestAuth"))
                }
            }
        };

        return new Fixture(controller, db, first, second);
    }

    private static BenchmarkComparisonIdentifyRequest Batteries(params long[] ids) => new() { BatteryRunIds = ids.ToList() };

    private static string? ErrorOf(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;
        return value?.GetType().GetProperty("error")?.GetValue(value) as string;
    }

    // --- Identify -------------------------------------------------------------------------------------

    [Fact]
    public async Task Identify_CreatesTheComparison_AndReturnsTheSameOneForTheSameSelection()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var created = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(Batteries(f.SecondBattery, f.FirstBattery), Ct)).Value);

        Assert.True(created.Id > 0);
        Assert.Equal("gpt-5.6-luna vs gemini-3.8-flash", created.DefaultName);
        Assert.Equal(created.DefaultName, created.Name);
        Assert.Null(created.CustomName);
        Assert.Equal(2, created.EntryCount);
        Assert.Equal("Batteries", created.SubjectKind);
        Assert.Equal(new[] { $"battery:{f.FirstBattery}", $"battery:{f.SecondBattery}" }, created.EntryKeys);
        Assert.Null(created.RenamedAtUtc);

        var again = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(new BenchmarkComparisonIdentifyRequest
            {
                RunIds = new List<long>(),
                GroupIds = null,
                BatteryRunIds = new List<long> { f.FirstBattery, f.SecondBattery, f.FirstBattery }
            }, Ct)).Value);

        Assert.Equal(created.Id, again.Id);
        var stored = Assert.Single(await f.Db.BenchmarkComparisons.ToListAsync(Ct));
        Assert.Equal(UserId, stored.CreatedByUserId);
    }

    [Fact]
    public async Task Identify_AnEmptyOrMixedSelection_IsA400_AndStoresNothing()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;

        var empty = await f.Controller.Identify(new BenchmarkComparisonIdentifyRequest(), Ct);
        Assert.IsType<BadRequestObjectResult>(empty);
        Assert.Equal(BenchmarkComparisonIdentityService.EmptySelectionError, ErrorOf(empty));

        var mixed = await f.Controller.Identify(new BenchmarkComparisonIdentifyRequest
        {
            RunIds = new List<long> { 9101 },
            BatteryRunIds = new List<long> { f.FirstBattery }
        }, Ct);
        Assert.IsType<BadRequestObjectResult>(mixed);
        Assert.Equal(BenchmarkBatteryModelComparison.MixedSourcesError, ErrorOf(mixed));

        Assert.IsType<BadRequestObjectResult>(await f.Controller.Identify(null!, Ct));
        Assert.Equal(0, await f.Db.BenchmarkComparisons.CountAsync(Ct));
    }

    // --- Rename ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Rename_SetsTheName_AndAnEmptyNameResetsToTheDefault()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;
        var created = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(Batteries(f.FirstBattery, f.SecondBattery), Ct)).Value);

        var renamed = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Rename(created.Id, new BenchmarkComparisonRenameRequest { Name = "  Luna against Flash  " }, Ct)).Value);
        Assert.Equal(created.Id, renamed.Id);
        Assert.Equal("Luna against Flash", renamed.Name);
        Assert.Equal("Luna against Flash", renamed.CustomName);
        Assert.Equal(created.DefaultName, renamed.DefaultName);
        Assert.NotNull(renamed.RenamedAtUtc);

        var reset = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Rename(created.Id, new BenchmarkComparisonRenameRequest { Name = null }, Ct)).Value);
        Assert.Null(reset.CustomName);
        Assert.Equal(created.DefaultName, reset.Name);
    }

    [Fact]
    public async Task Rename_AnUnknownComparisonIsA404_AndATooLongNameIsA400()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;
        var created = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(Batteries(f.FirstBattery, f.SecondBattery), Ct)).Value);

        var missing = await f.Controller.Rename(created.Id + 1000, new BenchmarkComparisonRenameRequest { Name = "Anything" }, Ct);
        Assert.IsType<NotFoundObjectResult>(missing);
        Assert.Equal(BenchmarkComparisonIdentityService.NotFoundError, ErrorOf(missing));

        var tooLong = await f.Controller.Rename(
            created.Id, new BenchmarkComparisonRenameRequest { Name = new string('n', BenchmarkComparisonIdentityService.MaxNameLength + 1) }, Ct);
        Assert.IsType<BadRequestObjectResult>(tooLong);
        Assert.Equal(BenchmarkComparisonIdentityService.NameTooLongError, ErrorOf(tooLong));

        Assert.Null((await f.Db.BenchmarkComparisons.AsNoTracking().SingleAsync(Ct)).Name);
    }

    // --- List -----------------------------------------------------------------------------------------

    [Fact]
    public async Task List_IsNewestFirst_WithEachComparisonsDocumentCount()
    {
        var f = await CreateAsync();
        await using var _ = f.Db;
        var pair = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(Batteries(f.FirstBattery, f.SecondBattery), Ct)).Value);
        var single = Assert.IsType<BenchmarkComparisonDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.Identify(Batteries(f.FirstBattery), Ct)).Value);
        await f.Controller.Rename(pair.Id, new BenchmarkComparisonRenameRequest { Name = "Pair" }, Ct);

        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.Id = 0;
        document.Origin = BenchmarkReportDocumentOrigin.ReportPack;
        document.SubjectKey = $"battery:{f.FirstBattery}";
        document.ComparisonId = pair.Id;
        document.Runs = new List<BenchmarkReportDocumentRun>();
        f.Db.BenchmarkReportDocuments.Add(document);
        await f.Db.SaveChangesAsync(Ct);

        var list = Assert.IsType<List<BenchmarkComparisonListItemDto>>(Assert.IsType<OkObjectResult>(await f.Controller.List(Ct)).Value);

        Assert.Equal(new[] { single.Id, pair.Id }, list.Select(c => c.Id));

        Assert.Equal(0, list[0].DocumentCount);
        Assert.Null(list[0].LastDocumentAtUtc);
        Assert.Null(list[0].CustomName);
        Assert.Equal(list[0].DefaultName, list[0].Name);
        Assert.Equal(1, list[0].EntryCount);

        Assert.Equal(1, list[1].DocumentCount);
        Assert.Equal(document.CreatedAtUtc, list[1].LastDocumentAtUtc);
        Assert.Equal("Pair", list[1].Name);
        Assert.Equal("Pair", list[1].CustomName);
        Assert.Equal(pair.DefaultName, list[1].DefaultName);
        Assert.Equal(2, list[1].EntryCount);
        Assert.Equal("Batteries", list[1].SubjectKind);
        Assert.Equal(pair.CreatedAtUtc, list[1].CreatedAtUtc);
    }
}
