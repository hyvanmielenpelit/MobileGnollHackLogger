using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AiRequestGovernorTests
{
    private static AiRequestGovernor CreateGovernor(int maxConcurrent = 2, int maxRetryAfterSeconds = 90)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AiRateLimitSettings:MaxConcurrentModelCalls", maxConcurrent.ToString() },
            { "AiRateLimitSettings:MaxRetryAfterSeconds", maxRetryAfterSeconds.ToString() }
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
    }

    [Fact]
    public async Task AcquirePermitAsync_EnforcesConcurrencyLimit()
    {
        var governor = CreateGovernor(maxConcurrent: 2);
        string key = "openai:user:user_123";

        var permit1 = await governor.AcquirePermitAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);
        var permit2 = await governor.AcquirePermitAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.NotNull(permit1);
        Assert.NotNull(permit2);

        // Third permit must time out
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await governor.AcquirePermitAsync(key, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        });

        // Releasing permit1 should allow permit3 to be acquired
        permit1.Dispose();

        var permit3 = await governor.AcquirePermitAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.NotNull(permit3);

        permit2.Dispose();
        permit3.Dispose();
    }

    [Fact]
    public void RecordRateLimit_SetsActiveCooldown()
    {
        var governor = CreateGovernor();
        string key = "anthropic:user:user_456";

        governor.RecordRateLimit(key, TimeSpan.FromSeconds(5));

        bool isRateLimited = governor.IsRateLimited(key, out var remaining);
        Assert.True(isRateLimited);
        Assert.True(remaining.TotalSeconds > 1 && remaining.TotalSeconds <= 5);
    }

    [Fact]
    public void UpdateLimitsFromHeaders_ParsesRetryAfterHeader()
    {
        var governor = CreateGovernor();
        string key = "google:user:user_789";

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", "12");

        governor.UpdateLimitsFromHeaders(key, response);

        bool isRateLimited = governor.IsRateLimited(key, out var remaining);
        Assert.True(isRateLimited);
        Assert.True(remaining.TotalSeconds > 8 && remaining.TotalSeconds <= 12);
    }

    [Fact]
    public async Task GetStatus_ReportsInFlightCallsAndCooldown()
    {
        var governor = CreateGovernor(maxConcurrent: 2);
        string keyA = "openai:user:in_flight";
        string keyB = "anthropic:user:cooling_down";

        var permit = await governor.AcquirePermitAsync(keyA, TimeSpan.FromSeconds(1), CancellationToken.None);
        governor.RecordRateLimit(keyB, TimeSpan.FromSeconds(30));

        var status = governor.GetStatus();
        var a = Assert.Single(status, s => s.CredentialKey == keyA);
        var b = Assert.Single(status, s => s.CredentialKey == keyB);

        Assert.Equal(1, a.InFlightCalls);
        Assert.False(a.IsRateLimited);
        Assert.Equal(0, b.InFlightCalls);
        Assert.True(b.IsRateLimited);
        Assert.True(b.RemainingCooldownSeconds > 0);

        permit.Dispose();

        a = Assert.Single(governor.GetStatus(), s => s.CredentialKey == keyA);
        Assert.Equal(0, a.InFlightCalls);
    }

    [Fact]
    public void GetCredentialKey_FormatsPartitionsCorrectly()
    {
        var userKey = AiRequestGovernor.GetCredentialKey("openai", "user_abc", null);
        Assert.Equal("openai:user:user_abc", userKey);

        var systemKey = AiRequestGovernor.GetCredentialKey("anthropic", "user_abc", 42);
        Assert.Equal("anthropic:system:42", systemKey);

        var anonKey = AiRequestGovernor.GetCredentialKey("google", null, null);
        Assert.Equal("google:user:anonymous", anonKey);
    }
}
