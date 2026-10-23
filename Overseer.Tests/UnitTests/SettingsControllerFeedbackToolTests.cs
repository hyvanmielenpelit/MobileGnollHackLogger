namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// The feedback tool on <c>/api/settings</c>: GET reports the user's setting (default on) and the
/// administrator's chat switch; PUT stores the user's setting and leaves it alone when the field is absent.
/// </summary>
public sealed class SettingsControllerFeedbackToolTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string UserId = "settings-feedback-user";

    private static readonly ModelMetadataService Metadata = new();

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _provider;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();

    public SettingsControllerFeedbackToolTests()
    {
        string databaseName = _databaseName;
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cache.Dispose();
    }

    private ApplicationDbContext Db()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_databaseName).Options);

    private FeedbackSwitchService CreateSwitch()
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), _cache, _configuration);

    private SettingsController CreateController(ApplicationDbContext db, FeedbackSwitchService? feedbackSwitch)
    {
        var cryptoConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AesEncryptionKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        var service = new SettingsService(
            db, new CryptoService(cryptoConfig), new Overseer.Services.Privacy.ConfidentialityPostureService(cryptoConfig),
            new ModelAvailabilityService(Metadata));

        var controller = new SettingsController(
            service, null!, _configuration, Metadata, null!, null!,
            new Overseer.Services.Privacy.EndpointPolicy(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.ConfidentialPolicyResolver(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.Dlp.DlpScannerService(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.AttachmentValidator(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.EphemeralSessionStore(
                new ConfigurationBuilder().Build(), null, startSweeper: false),
            Array.Empty<Overseer.Services.Providers.IAiProvider>(),
            new ModelPricingService(Metadata, db),
            new ModelAvailabilityService(Metadata),
            new ModelResolutionService(Metadata),
            feedbackSwitch);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId)
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private async Task<JsonElement> GetAsync(FeedbackSwitchService? feedbackSwitch)
    {
        await using var db = Db();
        var ok = Assert.IsType<OkObjectResult>(await CreateController(db, feedbackSwitch).GetSettings());
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    [Fact]
    public async Task Get_DefaultsTheUserSettingToOn()
    {
        var body = await GetAsync(CreateSwitch());

        Assert.True(body.GetProperty("enableFeedbackTool").GetBoolean());
    }

    [Fact]
    public async Task Get_ReportsTheAdminSwitch()
    {
        var feedbackSwitch = CreateSwitch();

        await feedbackSwitch.SetAsync(false, "admin", Ct);
        Assert.False((await GetAsync(feedbackSwitch)).GetProperty("feedbackToolAdminEnabled").GetBoolean());

        await feedbackSwitch.SetAsync(true, "admin", Ct);
        Assert.True((await GetAsync(feedbackSwitch)).GetProperty("feedbackToolAdminEnabled").GetBoolean());
    }

    [Fact]
    public async Task Get_WithoutTheSwitchService_ReportsNull()
    {
        var body = await GetAsync(null);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackToolAdminEnabled").ValueKind);
    }

    [Fact]
    public async Task Put_PersistsTheUserSetting()
    {
        await using (var db = Db())
        {
            Assert.IsType<OkResult>(await CreateController(db, CreateSwitch())
                .UpdateSettings(new UpdateSettingsRequest { EnableFeedbackTool = false }));
        }

        await using (var db = Db())
        {
            Assert.False((await db.UserAiSettings.SingleAsync(s => s.AspNetUserId == UserId, Ct)).EnableFeedbackTool);
        }

        // An older client that does not send the field leaves the stored value alone.
        await using (var db = Db())
        {
            Assert.IsType<OkResult>(await CreateController(db, CreateSwitch())
                .UpdateSettings(new UpdateSettingsRequest { SpoilerFreeMode = false }));
        }

        await using (var db = Db())
        {
            var row = await db.UserAiSettings.SingleAsync(s => s.AspNetUserId == UserId, Ct);
            Assert.False(row.EnableFeedbackTool);
            Assert.False(row.SpoilerFreeMode);
        }

        Assert.False((await GetAsync(CreateSwitch())).GetProperty("enableFeedbackTool").GetBoolean());
    }
}
