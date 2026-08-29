using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.Providers;

public class AiRequestGovernor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiRequestGovernor> _logger;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _rateLimitCooldownUntil = new(StringComparer.OrdinalIgnoreCase);

    public AiRequestGovernor(IConfiguration configuration, ILogger<AiRequestGovernor> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public static string GetCredentialKey(string provider, string? userId, long? systemModelId)
    {
        if (systemModelId.HasValue)
        {
            return $"{provider}:system:{systemModelId.Value}";
        }
        return $"{provider}:user:{userId ?? "anonymous"}";
    }

    private SemaphoreSlim GetSemaphore(string credentialKey)
    {
        int maxConcurrent = _configuration.GetValue<int>("AiRateLimitSettings:MaxConcurrentModelCalls", 4);
        if (maxConcurrent <= 0) maxConcurrent = 4;
        return _semaphores.GetOrAdd(credentialKey, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));
    }

    public int MaxConcurrentCalls => Math.Max(1, _configuration.GetValue<int>("AiRateLimitSettings:MaxConcurrentModelCalls", 4));
    public int MaxRetryAfterSeconds => _configuration.GetValue<int>("AiRateLimitSettings:MaxRetryAfterSeconds", 90);

    public List<(string CredentialKey, bool IsRateLimited, double RemainingCooldownSeconds)> GetStatus()
    {
        var keys = _semaphores.Keys.Union(_rateLimitCooldownUntil.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<(string CredentialKey, bool IsRateLimited, double RemainingCooldownSeconds)>();
        foreach (var key in keys)
        {
            bool isLimited = IsRateLimited(key, out var remaining);
            result.Add((key, isLimited, remaining.TotalSeconds));
        }
        return result;
    }

    public void ClearCooldown(string? credentialKey = null)
    {
        if (string.IsNullOrWhiteSpace(credentialKey))
        {
            _rateLimitCooldownUntil.Clear();
            _logger.LogInformation("[AiRequestGovernor] All rate limit cooldowns cleared by admin.");
        }
        else
        {
            _rateLimitCooldownUntil.TryRemove(credentialKey, out _);
            _logger.LogInformation("[AiRequestGovernor] Rate limit cooldown cleared for {CredentialKey} by admin.", credentialKey);
        }
    }

    public bool IsRateLimited(string credentialKey, out TimeSpan remainingCooldown)
    {
        if (_rateLimitCooldownUntil.TryGetValue(credentialKey, out var untilUtc))
        {
            var remaining = untilUtc - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                remainingCooldown = remaining;
                return true;
            }
            _rateLimitCooldownUntil.TryRemove(credentialKey, out _);
        }
        remainingCooldown = TimeSpan.Zero;
        return false;
    }

    public void RecordRateLimit(string credentialKey, TimeSpan retryAfter)
    {
        var maxRetryAfterSec = _configuration.GetValue<int>("AiRateLimitSettings:MaxRetryAfterSeconds", 90);
        if (retryAfter > TimeSpan.FromSeconds(maxRetryAfterSec))
        {
            retryAfter = TimeSpan.FromSeconds(maxRetryAfterSec);
        }

        var cooldownUntil = DateTime.UtcNow.Add(retryAfter);
        _rateLimitCooldownUntil.AddOrUpdate(
            credentialKey,
            cooldownUntil,
            (_, existing) => existing > cooldownUntil ? existing : cooldownUntil);

        _logger.LogWarning("[AiRequestGovernor] Rate limit recorded for {CredentialKey}; cooldown set for {Duration:F1}s until {Until:O}",
            credentialKey, retryAfter.TotalSeconds, cooldownUntil);
    }

    public async Task<IDisposable> AcquirePermitAsync(string credentialKey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var semaphore = GetSemaphore(credentialKey);

        if (IsRateLimited(credentialKey, out var cooldown))
        {
            if (cooldown > timeout)
            {
                throw new TimeoutException($"Rate limit cooldown active for {credentialKey} ({cooldown.TotalSeconds:F1}s remaining), exceeding wait timeout of {timeout.TotalSeconds:F1}s.");
            }
            _logger.LogInformation("[AiRequestGovernor] Waiting {Cooldown:F1}s for rate limit cooldown on {CredentialKey} before acquiring semaphore",
                cooldown.TotalSeconds, credentialKey);
            await Task.Delay(cooldown, cancellationToken);
        }

        bool acquired = await semaphore.WaitAsync(timeout, cancellationToken);
        if (!acquired)
        {
            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:F1}s waiting for concurrent model slot on {credentialKey}.");
        }

        return new PermitReleaser(semaphore);
    }

    public void UpdateLimitsFromHeaders(string credentialKey, HttpResponseMessage response)
    {
        try
        {
            if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
            {
                var val = retryAfterValues.FirstOrDefault();
                if (int.TryParse(val, out int seconds) && seconds > 0)
                {
                    RecordRateLimit(credentialKey, TimeSpan.FromSeconds(seconds));
                }
                else if (DateTimeOffset.TryParse(val, out var dateOffset))
                {
                    var diff = dateOffset.UtcDateTime - DateTime.UtcNow;
                    if (diff > TimeSpan.Zero)
                    {
                        RecordRateLimit(credentialKey, diff);
                    }
                }
            }
            else if (response.Headers.TryGetValues("retry-after-ms", out var retryMsValues))
            {
                var val = retryMsValues.FirstOrDefault();
                if (int.TryParse(val, out int ms) && ms > 0)
                {
                    RecordRateLimit(credentialKey, TimeSpan.FromMilliseconds(ms));
                }
            }
            else if (response.Headers.TryGetValues("anthropic-ratelimit-unified-reset", out var anthropicReset))
            {
                var val = anthropicReset.FirstOrDefault();
                if (DateTimeOffset.TryParse(val, out var dateOffset))
                {
                    var diff = dateOffset.UtcDateTime - DateTime.UtcNow;
                    if (diff > TimeSpan.Zero)
                    {
                        RecordRateLimit(credentialKey, diff);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AiRequestGovernor] Error parsing rate limit headers for {CredentialKey}", credentialKey);
        }
    }

    private sealed class PermitReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        public PermitReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }
}
