using System.Globalization;
using System.Text;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>Whether an account's deletion is held for review, and the reasons as an administrator reads them.</summary>
public sealed record AccountHoldVerdict(bool IsHeld, bool Manual, bool Automatic, string Reasons);

public static class AccountHoldEvaluator
{
    /// <summary>
    /// Held when an active manual hold exists, or when the serious provider events in the window (summing each run's
    /// Count) reach the threshold. Reads nothing else: no usage, refusals, rate-limit or quota events.
    /// </summary>
    /// <param name="holdPlacedByName">The name of the administrator who placed the hold, for the reasons text.</param>
    public static AccountHoldVerdict Evaluate(AccountHold? activeHold, IReadOnlyList<SecurityEvent> providerSafetyEvents,
        AccountHoldOptions options, DateTime utcNow, string? holdPlacedByName = null)
    {
        var reasons = new List<string>();

        bool manual = activeHold != null && activeHold.ReleasedUtc == null;
        if (manual)
        {
            string by = string.IsNullOrWhiteSpace(holdPlacedByName) ? "an administrator" : holdPlacedByName!;
            reasons.Add($"Manual hold placed by {by} on {activeHold!.PlacedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {activeHold.Reason}");
        }

        bool automatic = false;
        if (options.AutoHoldEnabled)
        {
            DateTime windowStart = utcNow.AddDays(-options.AutoHoldWindowDays);
            var serious = new HashSet<string>(options.EffectiveSeriousProviderCodes, StringComparer.OrdinalIgnoreCase);
            var counted = providerSafetyEvents
                .Where(e => e.Kind is SecurityEventKind.ProviderSafetyBlock or SecurityEventKind.ProviderPolicyRejection)
                .Where(e => e.LastOccurredUtc >= windowStart && e.OccurredUtc <= utcNow)
                .Where(e => e.Code != null && serious.Contains(e.Code))
                .ToList();

            long total = counted.Sum(e => (long)Math.Max(1, e.Count));
            if (total >= options.AutoHoldEventThreshold && total > 0)
            {
                automatic = true;
                var parts = counted
                    .GroupBy(e => (Code: e.Code!, Provider: e.Provider ?? "unknown provider"),
                        e => e, new CodeProviderComparer())
                    .Select(g =>
                    {
                        long n = g.Sum(e => (long)Math.Max(1, e.Count));
                        DateTime first = g.Min(e => e.OccurredUtc);
                        DateTime last = g.Max(e => e.LastOccurredUtc);
                        string dates = first.Date == last.Date
                            ? first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : $"{first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
                        return (Count: n, Text: $"{g.Key.Code} ×{n} ({g.Key.Provider}, {dates})");
                    })
                    .OrderByDescending(p => p.Count)
                    .Select(p => p.Text);

                var sb = new StringBuilder();
                sb.Append("Automatic: ").Append(total.ToString(CultureInfo.InvariantCulture))
                  .Append(total == 1 ? " serious provider safety event in " : " serious provider safety events in ")
                  .Append(options.AutoHoldWindowDays.ToString(CultureInfo.InvariantCulture)).Append(" days — ")
                  .Append(string.Join(", ", parts)).Append('.');
                reasons.Add(sb.ToString());
            }
        }

        return new AccountHoldVerdict(manual || automatic, manual, automatic, string.Join("\n", reasons));
    }

    /// <summary>Groups codes case-insensitively and providers exactly.</summary>
    private sealed class CodeProviderComparer : IEqualityComparer<(string Code, string Provider)>
    {
        public bool Equals((string Code, string Provider) x, (string Code, string Provider) y) =>
            string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Provider, y.Provider, StringComparison.Ordinal);

        public int GetHashCode((string Code, string Provider) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code), obj.Provider);
    }
}
