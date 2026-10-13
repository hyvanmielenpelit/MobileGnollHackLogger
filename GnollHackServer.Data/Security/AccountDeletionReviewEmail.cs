using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>
/// The email that tells the administrators a deletion is waiting for review, and its throttle: at most one per
/// user account per <see cref="AccountHoldOptions.ReviewEmailMinIntervalHours"/>, across all of the account's
/// requests, kept in the database so it survives restarts. The email never carries the hold reasons.
/// </summary>
public static class AccountDeletionReviewEmail
{
    /// <summary>True when no review email was sent for this user within the interval (across all of the user's requests).</summary>
    public static bool ShouldSend(DateTime? lastSentForUserUtc, DateTime utcNow, TimeSpan minInterval) =>
        lastSentForUserUtc == null || utcNow - lastSentForUserUtc.Value >= minInterval;

    public static (string Subject, string Body) Compose(string userName, DateTime requestedUtc, bool manual, bool automatic, string adminUrl)
    {
        string holdType = manual && automatic ? "manual hold and automatic rule"
            : manual ? "manual hold"
            : automatic ? "automatic rule"
            : "hold";
        string encodedName = WebUtility.HtmlEncode(userName);
        string subject = $"GnollHack: account deletion needs review ({userName})";

        string link = string.IsNullOrWhiteSpace(adminUrl)
            ? string.Empty
            : $"<p><a href=\"{WebUtility.HtmlEncode(ReviewLink(adminUrl))}\">Review deletion requests in Overseer</a></p>";

        string body =
            $"<p>The account <strong>{encodedName}</strong> requested deletion on " +
            $"{requestedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, and the deletion is held for review " +
            $"({holdType}).</p>" +
            "<p>Sign in to the Overseer admin page to see why it was held and to approve or decline it.</p>" +
            link;
        return (subject, body);
    }

    /// <summary>The Overseer admin page's Deletion requests sub-tab under <paramref name="adminUrl"/>.</summary>
    public static string ReviewLink(string adminUrl)
    {
        string trimmed = adminUrl.Trim();
        string separator = trimmed.Contains('?') ? "&" : "?";
        return trimmed + separator + "tab=users&subtab=deletions";
    }

    /// <summary>
    /// Sends the review email for <paramref name="request"/> when an address is configured and the throttle allows,
    /// then stamps the request and saves. A send that cannot be started is logged by the caller through
    /// <paramref name="onError"/> and leaves the stamp unchanged, so the next trigger retries. Never throws.
    /// </summary>
    /// <returns>True when an email was handed to the sender.</returns>
    public static async Task<bool> SendIfDueAsync(ApplicationDbContext db, AccountDeletionRequest request, IEmailSender? sender,
        AccountHoldOptions options, bool manual, bool automatic, DateTime utcNow, Action<Exception>? onError = null,
        CancellationToken ct = default)
    {
        try
        {
            if (sender == null || string.IsNullOrWhiteSpace(options.ReviewNotificationEmail) || string.IsNullOrEmpty(request.AspNetUserId))
            {
                return false;
            }

            string userId = request.AspNetUserId;
            DateTime? lastSent = await db.AccountDeletionRequests.AsNoTracking()
                .Where(r => r.AspNetUserId == userId && r.LastReviewEmailUtc != null)
                .MaxAsync(r => r.LastReviewEmailUtc, ct);
            if (!ShouldSend(lastSent, utcNow, TimeSpan.FromHours(options.ReviewEmailMinIntervalHours)))
            {
                return false;
            }

            var (subject, body) = Compose(request.UserNameSnapshot ?? "(unknown)", request.RequestedUtc, manual, automatic,
                options.OverseerAdminUrl ?? string.Empty);
            await sender.SendEmailAsync(options.ReviewNotificationEmail!, subject, body);

            request.LastReviewEmailUtc = utcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return false;
        }
    }
}
