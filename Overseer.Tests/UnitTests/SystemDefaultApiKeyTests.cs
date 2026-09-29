using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SystemDefaultApiKeyTests
{
    private const string KeyA = "test-key-not-real-0001";
    private const string KeyB = "test-key-not-real-0002";
    private const string OtherProviderKey = "test-key-not-real-0003";
    private const string CustomKey = "test-key-not-real-0004";

    private sealed class StubApiKeyValidator : IApiKeyValidator
    {
        public ApiKeyValidationResult Next { get; set; } = new(ApiKeyVerdict.Valid, "The key is valid.", null);
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

    private sealed class Fixture
    {
        public required ApplicationDbContext Db { get; init; }
        public required CryptoService Crypto { get; init; }
        public required StubApiKeyValidator Validator { get; init; }
        public required SystemDefaultApiKeyService Service { get; init; }
        public required AdminDefaultApiKeysController Controller { get; init; }
    }

    private static Fixture Create()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        var keyBytes = new byte[32];
        keyBytes[0] = 41;
        keyBytes[31] = 7;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } })
            .Build();

        var crypto = new CryptoService(config);
        var validator = new StubApiKeyValidator();
        var service = new SystemDefaultApiKeyService(db, crypto, validator, NullLogger<SystemDefaultApiKeyService>.Instance);
        return new Fixture
        {
            Db = db,
            Crypto = crypto,
            Validator = validator,
            Service = service,
            Controller = new AdminDefaultApiKeysController(service)
        };
    }

    private static ApiKeyCheckDetail Detail(int? status = 503, string? reason = "Service Unavailable") =>
        new("GET https://api.openai.com/v1/models", status, reason, "The server is overloaded.", null, 2100);

    private static ApiKeyValidationResult Unverifiable() =>
        new(ApiKeyVerdict.Unverifiable, "OpenAI could not confirm this key.", Detail());

    private static ApiKeyValidationResult Invalid() =>
        new(ApiKeyVerdict.Invalid, "OpenAI rejected this key: it does not exist, has been revoked or has expired.", Detail(401, "Unauthorized"));

    private static async Task<SystemAiApiConfiguration> AddConfigAsync(
        ApplicationDbContext db, string displayName, string provider, bool useDefault, bool isEnabled, int orderIndex = 0)
    {
        var config = new SystemAiApiConfiguration
        {
            DisplayName = displayName,
            Provider = provider,
            ModelId = "model-" + displayName,
            UseDefaultApiKey = useDefault,
            IsEnabled = isEnabled,
            OrderIndex = orderIndex
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config;
    }

    private static string DecryptConfig(CryptoService crypto, SystemAiApiConfiguration config) =>
        crypto.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");

    private static async Task<SystemAiApiConfiguration> ReloadAsync(ApplicationDbContext db, long id) =>
        await db.SystemAiApiConfigurations.AsNoTracking().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);

    private static async Task<SystemDefaultApiKey?> DefaultRowAsync(ApplicationDbContext db, string provider) =>
        await db.SystemDefaultApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Provider == provider, TestContext.Current.CancellationToken);

    private static int StatusCodeOf(IActionResult result) =>
        Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode ?? 200;

    [Fact]
    public async Task Save_Invalid_Returns400_AndStoresNothing()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        f.Validator.Next = Invalid();

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA, SaveUnverified = true }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var refusal = Assert.IsType<ApiKeyRefusalDto>(bad.Value);
        Assert.Equal("invalid", refusal.Verdict);
        Assert.NotNull(refusal.Detail);
        Assert.Empty(await f.Db.SystemDefaultApiKeys.ToListAsync(ct));
        Assert.Null((await ReloadAsync(f.Db, config.Id)).EncryptedApiKey);
    }

    [Fact]
    public async Task Save_Unverifiable_Returns409_AndStoresNothing()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        f.Validator.Next = Unverifiable();

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var refusal = Assert.IsType<ApiKeyRefusalDto>(conflict.Value);
        Assert.Equal("unverifiable", refusal.Verdict);
        Assert.Empty(await f.Db.SystemDefaultApiKeys.ToListAsync(ct));
        Assert.Null((await ReloadAsync(f.Db, config.Id)).EncryptedApiKey);
    }

    [Fact]
    public async Task SaveAnyway_StillUnverifiable_StoresNotVerifiedWithDetailText()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        f.Validator.Next = Unverifiable();

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA, SaveUnverified = true }, ct);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<DefaultApiKeySaveResultDto>(ok.Value);
        Assert.Equal("NotVerified", dto.Status.Verification.Status);
        Assert.Equal(1, f.Validator.Calls);

        var row = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.NotVerified, row.ApiKeyVerification);
        Assert.Equal(Detail().ToText(), row.ApiKeyVerificationMessage);
        Assert.NotNull(row.ApiKeyVerificationCheckedAtUtc);
        Assert.Equal(KeyA, DecryptConfig(f.Crypto, await ReloadAsync(f.Db, config.Id)));
    }

    [Fact]
    public async Task SaveAnyway_ProviderNowAnswers_StoresVerified()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        f.Validator.Next = new ApiKeyValidationResult(ApiKeyVerdict.Valid, "The key is valid.", null);

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA, SaveUnverified = true }, ct);

        var dto = Assert.IsType<DefaultApiKeySaveResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Verified", dto.Status.Verification.Status);
        Assert.Null(dto.Warning);
        Assert.Equal(1, f.Validator.Calls);

        var row = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.Verified, row.ApiKeyVerification);
        Assert.Null(row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task Save_ValidWithWarning_StoresVerifiedAndReturnsWarning()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        f.Validator.Next = new ApiKeyValidationResult(ApiKeyVerdict.ValidWithWarning, "OpenAI accepted this key but is rate-limiting it.", Detail(429, "Too Many Requests"));

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);

        var dto = Assert.IsType<DefaultApiKeySaveResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("OpenAI accepted this key but is rate-limiting it.", dto.Warning);
        var row = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(row);
        Assert.Equal(ApiKeyVerificationStatus.Verified, row.ApiKeyVerification);
        Assert.Equal(dto.Warning, row.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task Save_UnknownProviderOrEmptyKey_Returns400_WithoutChecking()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;

        var unknown = await f.Controller.Save("Mistral", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);
        var empty = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = "   " }, ct);

        Assert.IsType<BadRequestObjectResult>(unknown);
        Assert.IsType<BadRequestObjectResult>(empty);
        Assert.Equal(0, f.Validator.Calls);
        Assert.Empty(await f.Db.SystemDefaultApiKeys.ToListAsync(ct));
    }

    [Fact]
    public async Task Save_CanonicalizesProvider_TrimsKey_AndStoresLastFourAsHint()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;

        var result = await f.Controller.Save("openai", new SaveDefaultApiKeyRequest { ApiKey = "  " + KeyA + "  " }, ct);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("OpenAI", f.Validator.LastProvider);
        Assert.Equal(KeyA, f.Validator.LastKey);
        var row = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(row);
        Assert.Equal("0001", row.KeyHint);
        Assert.Equal(KeyA, f.Crypto.Decrypt(row.EncryptedApiKey!, row.ApiKeyNonce!, row.ApiKeyTag!, "SYSTEM_DEFAULT_API_KEY:OpenAI"));
    }

    [Fact]
    public async Task Save_ShortKey_StoresNoHint()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;

        await f.Controller.Save("Google", new SaveDefaultApiKeyRequest { ApiKey = "fake-7c" }, ct);

        var row = await DefaultRowAsync(f.Db, "Google");
        Assert.NotNull(row);
        Assert.Null(row.KeyHint);
    }

    [Fact]
    public async Task SaveAndReplace_RewriteEveryDefaultConfigOfThatProviderOnly()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var defaultEnabled = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        var defaultDisabled = await AddConfigAsync(f.Db, "Default GPT mini", "openai", useDefault: true, isEnabled: false);
        var custom = await AddConfigAsync(f.Db, "Custom GPT", "OpenAI", useDefault: false, isEnabled: true);
        var otherProvider = await AddConfigAsync(f.Db, "Default Claude", "Anthropic", useDefault: true, isEnabled: true);
        {
            var tracked = await f.Db.SystemAiApiConfigurations.SingleAsync(c => c.Id == custom.Id, ct);
            var (c, n, t) = f.Crypto.Encrypt(CustomKey, "SYSTEM_API_KEY");
            tracked.EncryptedApiKey = c;
            tracked.ApiKeyNonce = n;
            tracked.ApiKeyTag = t;
            await f.Db.SaveChangesAsync(ct);
        }
        await f.Controller.Save("Anthropic", new SaveDefaultApiKeyRequest { ApiKey = OtherProviderKey }, ct);

        var first = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);
        var firstDto = Assert.IsType<DefaultApiKeySaveResultDto>(Assert.IsType<OkObjectResult>(first).Value);
        Assert.Equal(2, firstDto.UpdatedConfigCount);
        Assert.Equal(KeyA, DecryptConfig(f.Crypto, await ReloadAsync(f.Db, defaultEnabled.Id)));
        Assert.Equal(KeyA, DecryptConfig(f.Crypto, await ReloadAsync(f.Db, defaultDisabled.Id)));

        var second = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyB }, ct);
        var secondDto = Assert.IsType<DefaultApiKeySaveResultDto>(Assert.IsType<OkObjectResult>(second).Value);
        Assert.Equal(2, secondDto.UpdatedConfigCount);

        var reloadedEnabled = await ReloadAsync(f.Db, defaultEnabled.Id);
        var reloadedDisabled = await ReloadAsync(f.Db, defaultDisabled.Id);
        Assert.Equal(KeyB, DecryptConfig(f.Crypto, reloadedEnabled));
        Assert.Equal(KeyB, DecryptConfig(f.Crypto, reloadedDisabled));
        Assert.True(reloadedEnabled.IsEnabled);
        Assert.False(reloadedDisabled.IsEnabled);
        Assert.Equal(CustomKey, DecryptConfig(f.Crypto, await ReloadAsync(f.Db, custom.Id)));
        Assert.Equal(OtherProviderKey, DecryptConfig(f.Crypto, await ReloadAsync(f.Db, otherProvider.Id)));
        Assert.Single(await f.Db.SystemDefaultApiKeys.Where(k => k.Provider == "OpenAI").ToListAsync(ct));
    }

    [Fact]
    public async Task Delete_DisablesExactlyTheDefaultConfigs_AndNullsTheirCopies()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var defaultEnabled = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        var custom = await AddConfigAsync(f.Db, "Custom GPT", "OpenAI", useDefault: false, isEnabled: true);
        var otherProvider = await AddConfigAsync(f.Db, "Default Claude", "Anthropic", useDefault: true, isEnabled: true);
        await f.Controller.Save("Anthropic", new SaveDefaultApiKeyRequest { ApiKey = OtherProviderKey }, ct);
        await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);

        var result = await f.Controller.Delete("OpenAI", ct);

        var dto = Assert.IsType<DefaultApiKeyDeleteResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, dto.DisabledCount);
        Assert.Null(await DefaultRowAsync(f.Db, "OpenAI"));

        var reloadedDefault = await ReloadAsync(f.Db, defaultEnabled.Id);
        Assert.False(reloadedDefault.IsEnabled);
        Assert.True(reloadedDefault.UseDefaultApiKey);
        Assert.Null(reloadedDefault.EncryptedApiKey);
        Assert.Null(reloadedDefault.ApiKeyNonce);
        Assert.Null(reloadedDefault.ApiKeyTag);

        Assert.True((await ReloadAsync(f.Db, custom.Id)).IsEnabled);
        var reloadedOther = await ReloadAsync(f.Db, otherProvider.Id);
        Assert.True(reloadedOther.IsEnabled);
        Assert.Equal(OtherProviderKey, DecryptConfig(f.Crypto, reloadedOther));
    }

    [Fact]
    public async Task ReAdd_AfterDelete_RestoresCopies_AndLeavesConfigsDisabled()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);
        await f.Controller.Delete("OpenAI", ct);

        var result = await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyB }, ct);

        var dto = Assert.IsType<DefaultApiKeySaveResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, dto.UpdatedConfigCount);
        var reloaded = await ReloadAsync(f.Db, config.Id);
        Assert.Equal(KeyB, DecryptConfig(f.Crypto, reloaded));
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task Delete_And_VerifyAgain_Return404_WhenNoKeyIsStored()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(404, StatusCodeOf(await f.Controller.Delete("OpenAI", ct)));
        Assert.Equal(404, StatusCodeOf(await f.Controller.VerifyAgain("OpenAI", ct)));
        Assert.Equal(0, f.Validator.Calls);
    }

    [Fact]
    public async Task VerifyAgain_UpdatesOnlyTheVerificationColumns()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true);
        await f.Controller.Save("OpenAI", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);
        var before = await DefaultRowAsync(f.Db, "OpenAI");
        var configBefore = await ReloadAsync(f.Db, config.Id);
        Assert.NotNull(before);

        f.Validator.Next = Invalid();
        var result = await f.Controller.VerifyAgain("openai", ct);

        var dto = Assert.IsType<DefaultApiKeyVerifyResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("NotVerified", dto.Status.Verification.Status);
        Assert.Equal(KeyA, f.Validator.LastKey);

        var after = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(after);
        Assert.Equal(ApiKeyVerificationStatus.NotVerified, after.ApiKeyVerification);
        Assert.Equal("OpenAI now rejects this key. " + Detail(401, "Unauthorized").ToText(), after.ApiKeyVerificationMessage);
        Assert.Equal(before.EncryptedApiKey, after.EncryptedApiKey);
        Assert.Equal(before.ApiKeyNonce, after.ApiKeyNonce);
        Assert.Equal(before.ApiKeyTag, after.ApiKeyTag);
        Assert.Equal(before.KeyHint, after.KeyHint);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);

        var configAfter = await ReloadAsync(f.Db, config.Id);
        Assert.True(configAfter.IsEnabled);
        Assert.Equal(configBefore.EncryptedApiKey, configAfter.EncryptedApiKey);

        f.Validator.Next = new ApiKeyValidationResult(ApiKeyVerdict.Valid, "The key is valid.", null);
        await f.Controller.VerifyAgain("OpenAI", ct);
        var restored = await DefaultRowAsync(f.Db, "OpenAI");
        Assert.NotNull(restored);
        Assert.Equal(ApiKeyVerificationStatus.Verified, restored.ApiKeyVerification);
        Assert.Null(restored.ApiKeyVerificationMessage);
    }

    [Fact]
    public async Task DeletionCheck_CountsTheDefaultConfigsOfThatProvider()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        await AddConfigAsync(f.Db, "Default GPT", "OpenAI", useDefault: true, isEnabled: true, orderIndex: 2);
        await AddConfigAsync(f.Db, "Default GPT mini", "OpenAI", useDefault: true, isEnabled: false, orderIndex: 1);
        await AddConfigAsync(f.Db, "Custom GPT", "OpenAI", useDefault: false, isEnabled: true);
        await AddConfigAsync(f.Db, "Default Claude", "Anthropic", useDefault: true, isEnabled: true);

        var result = await f.Controller.GetDeletionCheck("OpenAI", ct);

        var dto = Assert.IsType<DefaultApiKeyDeletionCheckDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, dto.Count);
        Assert.Equal(new[] { "Default GPT mini", "Default GPT" }, dto.Configs.Select(c => c.DisplayName));
        Assert.Equal(new[] { false, true }, dto.Configs.Select(c => c.IsEnabled));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.GetDeletionCheck("Mistral", ct));
    }

    [Fact]
    public async Task GetStatus_ListsProvidersInOrder_WithHintAndUsers()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        await AddConfigAsync(f.Db, "Default Gemini", "Google", useDefault: true, isEnabled: true);
        await f.Controller.Save("Google", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);

        var result = await f.Controller.GetStatus(ct);

        var list = Assert.IsAssignableFrom<IEnumerable<DefaultApiKeyStatusDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        Assert.Equal(new[] { "Anthropic", "Google", "OpenAI" }, list.Select(s => s.Provider));
        var google = list[1];
        Assert.True(google.HasKey);
        Assert.Equal("0001", google.KeyHint);
        Assert.NotNull(google.UpdatedAtUtc);
        Assert.Equal("Verified", google.Verification.Status);
        Assert.Equal("Default Gemini", Assert.Single(google.UsedBy).DisplayName);
        Assert.False(list[0].HasKey);
        Assert.Null(list[0].KeyHint);
        Assert.Null(list[0].Verification.Status);
    }

    [Fact]
    public async Task ApplyDefaultKey_CopiesTheKey_OrReturnsFalseWithoutOne()
    {
        var f = Create();
        var ct = TestContext.Current.CancellationToken;
        var config = new SystemAiApiConfiguration { DisplayName = "New", Provider = "anthropic", ModelId = "model-new", UseDefaultApiKey = true };

        Assert.False(await SystemDefaultApiKeyService.ApplyDefaultKeyAsync(f.Db, f.Crypto, config, ct));
        Assert.Null(config.EncryptedApiKey);

        await f.Controller.Save("Anthropic", new SaveDefaultApiKeyRequest { ApiKey = KeyA }, ct);

        Assert.True(await SystemDefaultApiKeyService.ApplyDefaultKeyAsync(f.Db, f.Crypto, config, ct));
        Assert.Equal(KeyA, DecryptConfig(f.Crypto, config));
    }
}
