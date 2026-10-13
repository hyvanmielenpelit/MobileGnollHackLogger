namespace Overseer.Services.Telemetry;

using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

/// <summary>
/// Saves chat and title-generation provider attempt rows in a scope of its own, so a rejected row never poisons the
/// caller's context. Runs from iterator finally blocks too, hence <see cref="CancellationToken.None"/>. Never throws.
/// </summary>
public static class ChatProviderAttemptRecorder
{
    public static async Task TryRecordAsync(IServiceScopeFactory scopes, ILogger logger, IReadOnlyList<ChatProviderAttempt> rows)
    {
        if (rows == null || rows.Count == 0) return;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatProviderAttempts.AddRange(rows);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record {Count} chat provider attempt(s).", rows.Count);
        }
    }

    /// <summary>Maps and saves every attempt of <paramref name="result"/>.</summary>
    public static Task TryRecordAsync(IServiceScopeFactory scopes, ILogger logger, AgentRunResult result, ChatAttemptLinks links)
    {
        List<ChatProviderAttempt> rows;
        try
        {
            rows = ModelCallTelemetryWriter.MapChatAttempts(result, links);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not map chat provider attempts.");
            return Task.CompletedTask;
        }
        return TryRecordAsync(scopes, logger, rows);
    }
}
