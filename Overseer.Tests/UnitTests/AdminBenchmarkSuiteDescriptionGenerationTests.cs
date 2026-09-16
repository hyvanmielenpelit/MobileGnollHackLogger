namespace Overseer.Tests.UnitTests;

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Xunit;

/// <summary>
/// POST suites/{id}/description-generation: the validation paths that return before the
/// description service or the spend guard is reached.
/// </summary>
public class AdminBenchmarkSuiteDescriptionGenerationTests
{
    private static (AdminBenchmarkController Controller, ApplicationDbContext Db) CreateController()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        // The validation paths touch only the DbContext.
        var controller = new AdminBenchmarkController(
            db, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "admin-1") }, "TestAuth"))
                }
            }
        };

        return (controller, db);
    }

    [Fact]
    public async Task UnknownSuite_Returns404()
    {
        var (controller, _) = CreateController();

        var result = await controller.GenerateSuiteDescription(
            404,
            new GenerateSuiteDescriptionRequest { GeneratorModelConfigurationId = 1 },
            null!,
            TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MissingGeneratorConfiguration_Returns400(long configId)
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = new BenchmarkSuite { Name = "Suite", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1 });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(ct);

        var result = await controller.GenerateSuiteDescription(
            suite.Id,
            new GenerateSuiteDescriptionRequest { GeneratorModelConfigurationId = configId },
            null!,
            ct);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
