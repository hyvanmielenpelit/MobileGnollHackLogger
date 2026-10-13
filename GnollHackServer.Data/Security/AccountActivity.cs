using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>
/// Records a successful sign-in on <see cref="ApplicationUser.LastSignInUtc"/>, at most once per
/// <see cref="MinInterval"/> so a busy game client does not write a row per request.
/// </summary>
public static class AccountActivity
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// One set-based update of the user's row, skipped when the stored value is younger than <see cref="MinInterval"/>.
    /// Leaves the concurrency stamp and the tracked entity alone. Never throws; a failure is logged.
    /// </summary>
    public static async Task TouchSignInAsync(ApplicationDbContext db, string userId, DateTime nowUtc, ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return;

        try
        {
            DateTime cutoff = nowUtc - MinInterval;
            await db.Users.OfType<ApplicationUser>()
                .Where(u => u.Id == userId && (u.LastSignInUtc == null || u.LastSignInUtc < cutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSignInUtc, (DateTime?)nowUtc), ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not record the last sign-in time of user {UserId}.", userId);
        }
    }
}
