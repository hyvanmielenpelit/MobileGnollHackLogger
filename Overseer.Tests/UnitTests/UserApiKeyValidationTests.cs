using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The key-save contract on the user's own keys: PUT apikeys, Verify Again and delete, with a
/// stub validator in place of the provider.
/// </summary>
public class UserApiKeyValidationTests
{
    private const string UserId = "key-test-user";
    private const string TestKey = "test-key-not-real-0001";

    private sealed class StubValidator : IApiKeyValidator
    {
        public ApiKeyValidationResult Next { get; set; } = new(ApiKeyVerdict.Valid, "The key works.", null);
        public int Calls { get; private set; }
        public string? LastProvider { get; private set; }
        public string? LastKey { get; private set; }

        public Task<ApiKeyValidationResult> ValidateAsync(string provider, string apiKey, CancellationToken ct)
        {
            Calls++;
            LastProvider = provider;
            LastKey = apiKey;
            return Task.FromResult(Next);
        }
    }

    private static readonly ApiKeyCheckDetail UnavailableDetail = new(
        "GET https://api.openai.com/v1/models", 503, "Service Unavailable", "The server is overloaded.", null, 2100);

    private static readonly ApiKeyCheckDetail RejectedDetail = new(
        "GET https://api.openai.com/v1/models", 401, "Unauthorized", "Incorrect API key provided.", null, 180);

    private static readonly ApiKeyCheckDetail RateLimitedDetail = new(
        "GET https://api.openai.com/v1/models", 429, "Too Many Requests", "Rate limit reached.", null, 150);

    private static ApiKeyValidationResult Unverifiable()
        => new(ApiKeyVerdict.Unverifiable, "OpenAI could not confirm this key.", UnavailableDetail);

    private static ApiKeyValidationResult Invalid()
        => new(ApiKeyVerdict.Invalid, "OpenAI rejected this key: it does not exist, has been revoked or has expired.", RejectedDetail);

    private static (SettingsController controller, SettingsService service, ApplicationDbContext db) CreateController()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
            })
            .Build();
        var service = new SettingsService(
            db, new CryptoService(config), new Overseer.Services.Privacy.ConfidentialityPostureService(config));

        var controller = new SettingsController(
            service, null!, null!, null!, null!, null!,
            new Overseer.Services.Privacy.EndpointPolicy(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.ConfidentialPolicyResolver(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.Dlp.DlpScannerService(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.AttachmentValidator(new ConfigurationBuilder().Build()),
            new Overseer.Services.Privacy.EphemeralSessionStore(
                new ConfigurationBuilder().Build(), null, startSweeper: false),
            Array.Empty<Overseer.Services.Providers.IAiProvider>());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId)
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        return (controller, service, db);
    }

    private static object? Prop(object? value, string name)
    {
        Assert.NotNull(value);
        var property = value.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(value);
    }

    private static ApiKeyVerificationDto VerificationOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<ApiKeyVerificationDto>(Prop(ok.Value, "verification"));
    }

    private static Task<UserAiApiKey?> KeyRowAsync(ApplicationDbContext db)
        => db.UserAiApiKeys.SingleOrDefaultAsync(
            k => k.AspNetUserId == UserId && k.Provider == "OpenAI", TestContext.Current.CancellationToken);

    // -- PUT apikeys ----------------------------------------------------------------------------

    [Fact]
    public async Task SaveApiKey_InvalidKey_Returns400WithTheRefusal_AndStoresNothing()
    {
        var (controller, _, db) = CreateController();
        var validator = new StubValidator { Next = Invalid() };

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = TestKey, SaveUnverified = true },
            validator, TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var refusal = Assert.IsType<ApiKeyRefusalDto>(badRequest.Value);
        Assert.Equal("invalid", refusal.Verdict);
        Assert.Equal(RejectedDetail, refusal.Detail);
        Assert.Null(await KeyRowAsync(db));
    }

    [Fact]
    public async Task SaveApiKey_Unverifiable_Returns409WithTheRefusal_AndStoresNothing()
    {
        var (controller, _, db) = CreateController();
        var validator = new StubValidator { Next = Unverifiable() };

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = TestKey },
            validator, TestContext.Current.CancellationToken);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var refusal = Assert.IsType<ApiKeyRefusalDto>(conflict.Value);
        Assert.Equal("unverifiable", refusal.Verdict);
        Assert.Equal(UnavailableDetail, refusal.Detail);
        Assert.Null(await KeyRowAsync(db));
    }

    [Fact]
    public async Task SaveApiKey_SaveAnyway_StillUnverifiable_StoresTheKeyAsNotVerifiedWithTheDetailText()
    {
        var (controller, service, db) = CreateController();
        var validator = new StubValidator { Next = Unverifiable() };

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = TestKey, SaveUnverified = true },
            validator, TestContext.Current.CancellationToken);

        Assert.Equal(1, validator.Calls);
        var verification = VerificationOf(result);
        Assert.Equal("NotVerified", verification.Status);
        Assert.Equal(UnavailableDetail.ToText(), verification.Message);
        Assert.NotNull(verification.CheckedAtUtc);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.NotVerified, row.ApiKeyVerification);
        Assert.Equal(UnavailableDetail.ToText(), row.ApiKeyVerificationMessage);
        Assert.NotNull(row.ApiKeyVerificationCheckedAtUtc);
        Assert.Equal(TestKey, await service.GetDecryptedApiKeyForProviderAsync(UserId, "OpenAI"));
    }

    [Fact]
    public async Task SaveApiKey_SaveAnyway_WhenTheProviderNowAnswers_StoresTheKeyAsVerified()
    {
        var (controller, _, db) = CreateController();
        var validator = new StubValidator { Next = new(ApiKeyVerdict.Valid, "The key works.", null) };

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = TestKey, SaveUnverified = true },
            validator, TestContext.Current.CancellationToken);

        Assert.Equal(1, validator.Calls);
        var verification = VerificationOf(result);
        Assert.Equal("Verified", verification.Status);
        Assert.Null(verification.Message);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.Verified, row.ApiKeyVerification);
        Assert.Null(row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task SaveApiKey_ValidWithWarning_StoresTheKeyAsVerified_AndReturnsTheWarning()
    {
        var (controller, _, db) = CreateController();
        const string warning = "OpenAI accepted this key but is rate-limiting it.";
        var validator = new StubValidator { Next = new(ApiKeyVerdict.ValidWithWarning, warning, RateLimitedDetail) };

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = TestKey },
            validator, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(warning, Prop(ok.Value, "warning"));
        var verification = VerificationOf(result);
        Assert.Equal("Verified", verification.Status);
        Assert.Equal(warning, verification.Message);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.Verified, row.ApiKeyVerification);
        Assert.Equal(warning, row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task SaveApiKey_Valid_CanonicalizesTheProvider_AndReturnsNoWarning()
    {
        var (controller, _, db) = CreateController();
        var validator = new StubValidator();

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "openai", ApiKey = "  " + TestKey + "  " },
            validator, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Null(Prop(ok.Value, "warning"));
        Assert.Equal("OpenAI", validator.LastProvider);
        Assert.Equal(TestKey, validator.LastKey);
        Assert.NotNull(await KeyRowAsync(db));
    }

    [Fact]
    public async Task SaveApiKey_UnknownProvider_Returns400_WithoutCallingTheValidator()
    {
        var (controller, _, db) = CreateController();
        var validator = new StubValidator();

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "NotAProvider", ApiKey = TestKey },
            validator, TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Unsupported provider", (string?)Prop(badRequest.Value, "message"));
        Assert.Equal(0, validator.Calls);
        Assert.False(await db.UserAiApiKeys.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveApiKey_EmptyKey_Returns400_WithoutCallingTheValidator()
    {
        var (controller, _, _) = CreateController();
        var validator = new StubValidator();

        var result = await controller.SaveApiKey(
            new SaveApiKeyRequest { Provider = "OpenAI", ApiKey = "   " },
            validator, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, validator.Calls);
    }

    // -- POST apikeys/{provider}/verify -----------------------------------------------------------

    [Fact]
    public async Task VerifyApiKey_WithNoStoredKey_Returns404()
    {
        var (controller, _, _) = CreateController();
        var validator = new StubValidator();

        var result = await controller.VerifyApiKey("OpenAI", validator, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public async Task VerifyApiKey_WhenTheProviderNowRejectsTheKey_MarksItNotVerified_AndKeepsTheKey()
    {
        var (controller, service, db) = CreateController();
        await service.SaveApiKeyForProviderAsync(UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.Verified, null);
        var validator = new StubValidator { Next = Invalid() };

        var result = await controller.VerifyApiKey("openai", validator, TestContext.Current.CancellationToken);

        Assert.Equal(TestKey, validator.LastKey);
        var verification = VerificationOf(result);
        Assert.Equal("NotVerified", verification.Status);
        Assert.Equal("OpenAI now rejects this key. " + RejectedDetail.ToText(), verification.Message);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.NotVerified, row.ApiKeyVerification);
        Assert.Equal(TestKey, await service.GetDecryptedApiKeyForProviderAsync(UserId, "OpenAI"));
    }

    [Fact]
    public async Task VerifyApiKey_WhenTheProviderNowAnswers_MarksANotVerifiedKeyVerified()
    {
        var (controller, service, db) = CreateController();
        await service.SaveApiKeyForProviderAsync(
            UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.NotVerified, UnavailableDetail.ToText());
        var validator = new StubValidator();

        var result = await controller.VerifyApiKey("OpenAI", validator, TestContext.Current.CancellationToken);

        var verification = VerificationOf(result);
        Assert.Equal("Verified", verification.Status);
        Assert.Null(verification.Message);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.Verified, row.ApiKeyVerification);
        Assert.Null(row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task VerifyApiKey_StillUnverifiable_StoresTheDetailText()
    {
        var (controller, service, db) = CreateController();
        await service.SaveApiKeyForProviderAsync(UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.Verified, null);
        var validator = new StubValidator { Next = Unverifiable() };

        var result = await controller.VerifyApiKey("OpenAI", validator, TestContext.Current.CancellationToken);

        var verification = VerificationOf(result);
        Assert.Equal("NotVerified", verification.Status);
        Assert.Equal(UnavailableDetail.ToText(), verification.Message);

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(UnavailableDetail.ToText(), row.ApiKeyVerificationMessage);
    }

    // -- GET apikeys and DELETE apikeys/{provider} ----------------------------------------------

    [Fact]
    public async Task GetApiKeys_IncludesEachKeysVerification()
    {
        var (controller, service, _) = CreateController();
        await service.SaveApiKeyForProviderAsync(
            UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.NotVerified, UnavailableDetail.ToText());

        var ok = Assert.IsType<OkObjectResult>(await controller.GetApiKeys());
        var statuses = Assert.IsAssignableFrom<IEnumerable>(ok.Value).Cast<object>().ToList();

        var openAi = statuses.Single(s => (string?)Prop(s, "Provider") == "OpenAI");
        var verification = Assert.IsType<ApiKeyVerificationDto>(Prop(openAi, "Verification"));
        Assert.Equal("NotVerified", verification.Status);
        Assert.Equal(UnavailableDetail.ToText(), verification.Message);

        var google = statuses.Single(s => (string?)Prop(s, "Provider") == "Google");
        var none = Assert.IsType<ApiKeyVerificationDto>(Prop(google, "Verification"));
        Assert.Null(none.Status);
    }

    [Fact]
    public async Task DeleteApiKey_ClearsTheVerificationColumns()
    {
        var (controller, service, db) = CreateController();
        await service.SaveApiKeyForProviderAsync(
            UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.NotVerified, UnavailableDetail.ToText());

        Assert.IsType<OkResult>(await controller.DeleteApiKeyForProvider("OpenAI"));

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Null(row.EncryptedApiKey);
        Assert.Null(row.ApiKeyVerification);
        Assert.Null(row.ApiKeyVerificationCheckedAtUtc);
        Assert.Null(row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task SaveApiKeyForProviderAsync_TruncatesALongVerificationMessage()
    {
        var (_, service, db) = CreateController();

        await service.SaveApiKeyForProviderAsync(
            UserId, "OpenAI", TestKey, ApiKeyVerificationStatus.NotVerified, new string('x', 1500));

        var row = await KeyRowAsync(db);
        Assert.NotNull(row);
        Assert.Equal(1000, row.ApiKeyVerificationMessage!.Length);
    }
}
