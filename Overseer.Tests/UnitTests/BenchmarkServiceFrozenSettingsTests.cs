using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A benchmark call runs with the settings and endpoint its run recorded, and only the key comes
/// from the live configuration. Every request site copies its model, settings and endpoint from the
/// configuration these resolvers return, so the resolution is what these tests pin.
/// </summary>
public class BenchmarkServiceFrozenSettingsTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
        { "PrivacySettings:CustomEndpoints:AllowedHostPatterns:0", "gw.example.com" }
    }).Build();

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private CryptoService Crypto => new(_configuration);

    private BenchmarkService Service() => new(
        null!, null!, null!, Crypto, new BenchmarkRunManager(), new BenchmarkDifficultyJobManager(), null!,
        new EndpointPolicy(_configuration), _configuration, NullLogger<BenchmarkService>.Instance);

    private async Task<SystemAiApiConfiguration> AddConfigAsync(string? baseUrl = null, string key = "launch-key")
    {
        var (cipher, nonce, tag) = Crypto.Encrypt(key, "SYSTEM_API_KEY");
        var config = new SystemAiApiConfiguration
        {
            DisplayName = "Verifier", Provider = "OpenAI", ModelId = "gpt-x", ThinkingLevel = "high",
            MaxOutputTokens = 8000, BaseUrl = baseUrl, IsEnabled = true, ModelRole = 4,
            EncryptedApiKey = cipher, ApiKeyNonce = nonce, ApiKeyTag = tag
        };
        _db.SystemAiApiConfigurations.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    /// <summary>A run launched with <paramref name="config"/> as both assessor and claim verifier.</summary>
    private async Task<BenchmarkRun> LaunchAsync(SystemAiApiConfiguration config)
    {
        var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAsync(_db, config, CancellationToken.None);
        return new BenchmarkRun
        {
            AssessorModelConfigurationId = config.Id,
            AssessorModelSnapshot = snapshot,
            AssessorModelSnapshotId = snapshot.Id,
            AssessorEffectiveMaxOutputTokens = 12345,
            ClaimVerifierModelConfigurationId = config.Id,
            ClaimVerifierModelSnapshot = snapshot,
            ClaimVerifierModelSnapshotId = snapshot.Id
        };
    }

    [Fact]
    public async Task AConfigurationEditedAfterLaunch_StillRunsWithTheRecordedSettings_AndTheCurrentKey()
    {
        var config = await AddConfigAsync();
        var run = await LaunchAsync(config);

        var (rotated, nonce, tag) = Crypto.Encrypt("rotated-key", "SYSTEM_API_KEY");
        config.ThinkingLevel = "low";
        config.ModelId = "gpt-x-edited";
        config.EncryptedApiKey = rotated;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
        await _db.SaveChangesAsync();

        var (bound, apiKey, error) = await Service().ResolveClaimVerifierAsync(_db, run, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("gpt-x", bound!.ModelId);
        Assert.Equal("high", bound.ThinkingLevel);
        Assert.Equal("rotated-key", apiKey);
    }

    [Fact]
    public async Task ACustomEndpoint_ReachesTheRequest()
    {
        var config = await AddConfigAsync(baseUrl: "https://gw.example.com/openai");
        var run = await LaunchAsync(config);
        var service = Service();

        var (bound, apiKey, error) = await service.ResolveClaimVerifierAsync(_db, run, CancellationToken.None);
        Assert.Null(error);

        var endpoint = service.EndpointFor(bound!);
        var request = BenchmarkService.BuildClaimVerificationRequest(
            bound!, apiKey!, endpoint, "prompt", new List<string>(), 16000, 8, 12, 15, 10000, 1, 0, null);

        Assert.True(request.Endpoint.IsCustom);
        Assert.Equal("https://gw.example.com/openai", request.Endpoint.BaseUrl);
    }

    [Fact]
    public async Task AnEndpointThePolicyRejects_FailsTheRole_InsteadOfCallingTheOfficialEndpoint()
    {
        // The row was saved when this host was allowed; the allowlist has since dropped it.
        var config = await AddConfigAsync(baseUrl: "https://withdrawn.example.org");
        var run = await LaunchAsync(config);

        var (bound, apiKey, error) = await Service().ResolveClaimVerifierAsync(_db, run, CancellationToken.None);

        Assert.Null(bound);
        Assert.Null(apiKey);
        Assert.Contains("not allowed by the endpoint policy", error);
    }

    [Fact]
    public async Task AProviderChangedAfterLaunch_FailsTheRole()
    {
        var config = await AddConfigAsync();
        var run = await LaunchAsync(config);
        config.Provider = "Anthropic";
        await _db.SaveChangesAsync();

        var (bound, _, error) = await Service().ResolveAssessorAsync(_db, run, null, CancellationToken.None);

        Assert.Null(bound);
        Assert.Equal(SystemAiConfigurationSnapshotStore.MismatchMessage, error);
    }

    [Fact]
    public async Task TheRunsAssessor_SendsTheRecordedEffectiveCap_AndAnOverrideItsOwn()
    {
        var config = await AddConfigAsync();
        var run = await LaunchAsync(config);
        var service = Service();

        var (runAssessor, _, _) = await service.ResolveAssessorAsync(_db, run, null, CancellationToken.None);
        Assert.Equal(12345, service.GraderOutputCap(runAssessor!, run.AssessorModelSnapshotId, run.AssessorEffectiveMaxOutputTokens));

        config.MaxOutputTokens = 9000;
        await _db.SaveChangesAsync();
        var (overrideAssessor, _, _) = await service.ResolveAssessorAsync(_db, run, config.Id, CancellationToken.None);
        Assert.Equal(9000, service.GraderOutputCap(overrideAssessor!, run.AssessorModelSnapshotId, run.AssessorEffectiveMaxOutputTokens));
    }
}
