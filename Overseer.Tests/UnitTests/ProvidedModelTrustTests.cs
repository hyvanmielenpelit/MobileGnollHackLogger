using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A user's decision about an operator-provided model: where it is read, where it is written,
/// and that account deletion takes it away.
/// </summary>
public class ProvidedModelTrustTests
{
    private const string Owner = "user-1";

    private static IConfiguration CreateConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
            { "PrivacySettings:Ephemeral:TimeoutMinutes", "45" }
        }).Build();

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /* SQLite in memory rather than the InMemory provider: CryptoShred is deliberately set-based
       and the InMemory provider cannot translate ExecuteDeleteAsync, so proving what the
       statement does needs a relational one. */
    private static (ApplicationDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) CreateRelationalDb()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();

        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options);
        db.Database.EnsureCreated();

        // A relational provider enforces the foreign keys the InMemory one ignores.
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = Owner, Email = "u1@example.com" });
        db.SaveChanges();

        return (db, connection);
    }

    private static SettingsService CreateService(ApplicationDbContext db, IConfiguration config)
        => new(db, new CryptoService(config), new ConfidentialityPostureService(config));

    private static SettingsController CreateController(
        SettingsService service, IConfiguration config, string userId = Owner)
        => new(
            service, null!, config, null!, null!, null!,
            new EndpointPolicy(config), new ConfidentialPolicyResolver(config),
            new Overseer.Services.Privacy.Dlp.DlpScannerService(config),
            new AttachmentValidator(config),
            new EphemeralSessionStore(config, startSweeper: false),
            Array.Empty<Overseer.Services.Providers.IAiProvider>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"))
                }
            }
        };

    private static async Task<long> SeedSystemModelAsync(
        ApplicationDbContext db, string displayName = "Provided Stub",
        string? posture = null, DateTime? verifiedUtc = null)
    {
        var config = new SystemAiApiConfiguration
        {
            Provider = "OpenAI",
            ModelId = "provided-model-1",
            DisplayName = displayName,
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 1,
            ConfidentialityPosture = posture,
            PostureVerifiedUtc = verifiedUtc
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config.Id;
    }

    [Fact]
    public async Task TheListReportsEachProvidedModelWithItsPostureAndUndecidedState()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        long configId = await SeedSystemModelAsync(
            db, posture: "ZeroRetention", verifiedUtc: new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        var models = await CreateService(db, config).GetProvidedModelsForConfidentialAsync(Owner);

        var model = Assert.Single(models);
        Assert.Equal(configId, model.Id);
        Assert.Equal("Provided Stub", model.DisplayName);
        Assert.Equal("ZeroRetention", model.Posture);
        Assert.True(model.IsOperatorVerified);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), model.PostureVerifiedUtc);
        // No row exists, and that absence is the undecided state the gate prompts about.
        Assert.Null(model.UserTrustsForConfidential);
        Assert.Null(model.DecidedUtc);
    }

    [Fact]
    public async Task ASelfDeclaredPostureIsNeverReportedAsOperatorVerified()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        await SeedSystemModelAsync(db, posture: "ZeroRetention");

        var model = Assert.Single(
            await CreateService(db, config).GetProvidedModelsForConfidentialAsync(Owner));

        // A posture with no verification date is a claim nobody checked.
        Assert.False(model.IsOperatorVerified);
    }

    [Fact]
    public async Task SavingTrueThenFalseThenNullUpsertsAndFinallyRemovesTheDecision()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();
        long configId = await SeedSystemModelAsync(db);
        var service = CreateService(db, config);

        Assert.True(await service.SaveSystemModelConfidentialTrustAsync(Owner, configId, true));
        var row = await db.UserSystemModelConfidentialTrusts.SingleAsync(ct);
        Assert.True(row.UserTrustsForConfidential);
        Assert.NotEqual(default, row.DecidedUtc);

        Assert.True(await service.SaveSystemModelConfidentialTrustAsync(Owner, configId, false));
        // Upserted, not duplicated: a second row would let two answers coexist.
        row = await db.UserSystemModelConfidentialTrusts.SingleAsync(ct);
        Assert.False(row.UserTrustsForConfidential);

        Assert.True(await service.SaveSystemModelConfidentialTrustAsync(Owner, configId, null));
        Assert.Empty(await db.UserSystemModelConfidentialTrusts.ToListAsync(ct));
    }

    [Fact]
    public async Task ADecisionAboutAModelTheUserCannotSelectIsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateConfig();
        using var db = CreateDb();

        // Enabled but assigned to nobody, so this user cannot select it.
        var unreachable = new SystemAiApiConfiguration
        {
            Provider = "OpenAI",
            ModelId = "hidden-model",
            DisplayName = "Hidden Model",
            IsEnabled = true,
            IsSystemWide = false,
            ModelRole = 1
        };
        db.SystemAiApiConfigurations.Add(unreachable);
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, config);
        Assert.False(await service.SaveSystemModelConfidentialTrustAsync(Owner, unreachable.Id, true));
        Assert.Empty(await db.UserSystemModelConfidentialTrusts.ToListAsync(ct));

        var controller = CreateController(service, config);
        var result = await controller.SetProvidedModelConfidentialTrust(
            unreachable.Id, new SetApiKeyConfidentialTrustRequest { Trusted = true });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task TheEndpointsReadAndWriteOneDecision()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        long configId = await SeedSystemModelAsync(db);
        var controller = CreateController(CreateService(db, config), config);

        Assert.IsType<OkResult>(await controller.SetProvidedModelConfidentialTrust(
            configId, new SetApiKeyConfidentialTrustRequest { Trusted = true }));

        var listed = Assert.IsType<OkObjectResult>(await controller.GetProvidedModelsForConfidential());
        var rows = ((System.Collections.IEnumerable)listed.Value!).Cast<dynamic>().ToList();

        var row = Assert.Single(rows);
        Assert.Equal(configId, (long)row.id);
        Assert.True((bool?)row.userTrustsForConfidential);
        Assert.NotNull(row.decidedUtc);
    }

    [Fact]
    public async Task TheSettingsPayloadCarriesTheIncognitoTimeout()
    {
        var config = CreateConfig();
        using var db = CreateDb();
        var controller = CreateController(CreateService(db, config), config);

        dynamic value = Assert.IsType<OkObjectResult>(await controller.GetSettings()).Value!;

        Assert.Equal(45, (int)value.ephemeralTimeoutMinutes);
    }

    [Fact]
    public async Task AccountDeletionRemovesTheUsersProvidedModelDecisions()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = CreateRelationalDb();
        using var _ = db;
        using var __ = connection;

        db.Users.Add(new ApplicationUser { Id = "u2", UserName = "u2", Email = "u2@example.com" });
        var config = new SystemAiApiConfiguration
        {
            Provider = "OpenAI", ModelId = "provided-model-1", DisplayName = "Provided Stub",
            IsEnabled = true, IsSystemWide = true, ModelRole = 1
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(ct);

        db.UserSystemModelConfidentialTrusts.Add(new UserSystemModelConfidentialTrust
        {
            AspNetUserId = Owner, SystemAiApiConfigurationId = config.Id,
            UserTrustsForConfidential = true, DecidedUtc = DateTime.UtcNow
        });
        db.UserSystemModelConfidentialTrusts.Add(new UserSystemModelConfidentialTrust
        {
            AspNetUserId = "u2", SystemAiApiConfigurationId = config.Id,
            UserTrustsForConfidential = false, DecidedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await GnollHackServer.Data.Privacy.CryptoShred
            .DeleteSystemModelTrustForUserAsync(db, Owner, ct));
        db.ChangeTracker.Clear();

        // Only the deleted account's decision goes.
        var remaining = await db.UserSystemModelConfidentialTrusts.ToListAsync(ct);
        Assert.Equal("u2", Assert.Single(remaining).AspNetUserId);

        Assert.Equal(0, await GnollHackServer.Data.Privacy.CryptoShred
            .DeleteSystemModelTrustForUserAsync(db, "", ct));
    }
}
