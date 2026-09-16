namespace Overseer.Tests;

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Xunit;

/// <summary>
/// Regression for the "disposed context" failure: <c>StartQuestionGeneration</c> used to fire
/// its background task with a <see cref="BenchmarkGenerationService"/> captured from the HTTP
/// request's own DI scope, so the job's first database access after the request completed threw
/// <see cref="ObjectDisposedException"/> on the request's <see cref="ApplicationDbContext"/>. The
/// fix dispatches from a fresh scope created inside the background task, so the job must still
/// run to completion after the original request scope is disposed.
/// </summary>
public class BenchmarkGenerationDispatchTests
{
    /// <summary>
    /// Builds the DI graph <see cref="BenchmarkGenerationService"/> needs, on one shared
    /// in-memory database, so a fresh <c>IServiceScopeFactory.CreateScope()</c> call after the
    /// request scope is disposed still resolves working, non-disposed collaborators.
    /// </summary>
    private static (IServiceScopeFactory ScopeFactory, BenchmarkGenerationJobManager JobManager) CreateGenerationDispatchFixture(
        DbContextOptions<ApplicationDbContext> dbOptions, IConfiguration config)
    {
        var jobManager = new BenchmarkGenerationJobManager();

        var cryptoService = new CryptoService(new ConfigurationBuilder().AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
        }).Build());

        // The generation loop never reaches the model call: the dummy-key decrypt throws first,
        // so these collaborators only need to satisfy the constructor, never actually run.
        var agentLoopRunner = new AgentLoopRunner(
            Array.Empty<IAiProvider>(), null!, null!, null!, null!, null!, null!, null!, null!);

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddSingleton(config);
        services.AddScoped<BenchmarkComplianceGuard>();
        services.AddScoped<SystemAiConfigService>();
        services.AddSingleton<ILogger<SystemAiConfigService>>(NullLogger<SystemAiConfigService>.Instance);
        services.AddSingleton<ILogger<BenchmarkGenerationService>>(NullLogger<BenchmarkGenerationService>.Instance);
        services.AddSingleton(jobManager);
        services.AddSingleton(agentLoopRunner);
        services.AddSingleton(cryptoService);
        services.AddScoped<BenchmarkGenerationService>();

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return (scopeFactory, jobManager);
    }

    [Fact]
    public async Task StartQuestionGeneration_RunsToCompletion_AfterTheRequestScopeIsDisposed()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var config = BenchmarkComplianceGuardTests.CreateConfig();
        var (scopeFactory, jobManager) = CreateGenerationDispatchFixture(dbOptions, config);

        long suiteId;
        long generatorConfigId;
        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (suite, modelA, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
            suite.GameSnapshot = new BenchmarkGameSnapshot
            {
                Name = "Dispatch Snapshot",
                SanitizedText = "HP 12/60. A mind flayer is adjacent to the east.",
                Sha256 = "deadbeef",
                CaptureMethod = "Manual"
            };
            await db.SaveChangesAsync(ct);
            suiteId = suite.Id;
            generatorConfigId = modelA.Id; // Benchmark-role config, dummy encrypted key (see SeedConfigsAndSuite).
        }

        using (var requestScope = scopeFactory.CreateScope())
        {
            var db = requestScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guard = requestScope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
            var generationService = requestScope.ServiceProvider.GetRequiredService<BenchmarkGenerationService>();

            var controller = new AdminBenchmarkController(
                db, null!, null!, null!, new BenchmarkDifficultyJobManager(), guard, scopeFactory,
                null!, null!, null!,
                jobManager, generationService,
                new BenchmarkRubricCheckJobManager(), null!, new BenchmarkRubricGapAuthorJobManager(), null!,
                null!, null!, null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-id") }, "TestAuth"))
                    }
                }
            };

            var result = await controller.StartQuestionGeneration(new StartQuestionGenerationRequest
            {
                SuiteId = suiteId,
                GeneratorModelConfigurationId = generatorConfigId,
                SimpleCount = 1
            }, ct);

            Assert.IsType<AcceptedResult>(result);
        }
        // The request scope -- and the ApplicationDbContext and BenchmarkComplianceGuard captured
        // from it -- is disposed here, before the dispatched background task necessarily even ran.

        BenchmarkGenerationJob? job = null;
        for (int waitedMs = 0; waitedMs < 10_000; waitedMs += 50)
        {
            job = jobManager.Current;
            if (job != null && job.Status != BenchmarkGenerationJobStatus.Running) break;
            await Task.Delay(50, ct);
        }

        Assert.NotNull(job);
        Assert.DoesNotContain(job!.Log, l =>
            (l.Message?.Contains("disposed", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (l.RawExcerpt?.Contains("disposed", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.Equal(BenchmarkGenerationJobStatus.Failed, job.Status);
        Assert.Contains(job.Log, l => l.Message.StartsWith("Unexpected failure:", StringComparison.Ordinal));
    }
}
