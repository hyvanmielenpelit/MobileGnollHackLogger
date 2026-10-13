namespace Overseer.Services.Telemetry;

using MobileGnollHackLogger.Data;

/// <summary>The fields of one <see cref="UserKeyAiUsageLog"/> row.</summary>
public sealed record UserKeyUsageEntry(
    string UserId,
    long? UserAiModelId,
    string Provider,
    string ModelId,
    string? ModelDisplayName,
    string? ThinkingLevel,
    int? InputTokens,
    int? OutputTokens,
    int? CacheReadInputTokens,
    int? CacheCreationInputTokens,
    int? TotalDurationMs,
    int RoleContext,
    decimal? EstimatedCost,
    string? PricingSource,
    DateTime? TimestampUtc = null);

/// <summary>
/// Records one call, turn or title made on a user's own key, in a scope of its own so a rejected row never poisons
/// the caller's context. Never written for an incognito turn (the callers decide). Never throws.
/// </summary>
public static class UserKeyUsageRecorder
{
    public static async Task TryRecordAsync(IServiceScopeFactory scopes, ILogger logger, UserKeyUsageEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.UserId)) return;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserKeyAiUsageLogs.Add(new UserKeyAiUsageLog
            {
                AspNetUserId = entry.UserId,
                UserAiModelId = entry.UserAiModelId,
                TimestampUtc = entry.TimestampUtc ?? DateTime.UtcNow,
                Provider = Cut(entry.Provider, 64) ?? string.Empty,
                ModelId = Cut(entry.ModelId, 128) ?? string.Empty,
                ModelDisplayName = Cut(entry.ModelDisplayName, 256),
                ThinkingLevel = Cut(entry.ThinkingLevel, 32) ?? string.Empty,
                InputTokens = entry.InputTokens,
                OutputTokens = entry.OutputTokens,
                CacheReadInputTokens = entry.CacheReadInputTokens,
                CacheCreationInputTokens = entry.CacheCreationInputTokens,
                TotalDurationMs = entry.TotalDurationMs,
                RoleContext = entry.RoleContext,
                EstimatedCost = entry.EstimatedCost,
                PricingSource = entry.EstimatedCost == null ? null : Cut(entry.PricingSource, 16)
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record user-key usage for role context {RoleContext}.", entry.RoleContext);
        }
    }

    private static string? Cut(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
