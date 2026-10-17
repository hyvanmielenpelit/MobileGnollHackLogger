using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Overseer.Models;

namespace Overseer.Security;

/// <summary>
/// Counts the operator-funded answers each user has being generated at once, across sessions,
/// and refuses one more when <see cref="AiUsageGuardOptions.MaxConcurrentTurnsPerUser"/> is
/// reached. Every successful <see cref="TryEnter"/> is paired with one <see cref="Exit"/>.
/// </summary>
public sealed class UserTurnConcurrencyGate
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<AiUsageGuardOptions> _options;

    public UserTurnConcurrencyGate(IOptionsMonitor<AiUsageGuardOptions> options)
    {
        _options = options;
    }

    /// <summary>Takes a slot for <paramref name="userId"/>; false when the user already holds the limit. A limit of 0 never refuses.</summary>
    public bool TryEnter(string userId)
    {
        int max = _options.CurrentValue.MaxConcurrentTurnsPerUser;

        while (true)
        {
            if (!_counts.TryGetValue(userId, out int current))
            {
                if (_counts.TryAdd(userId, 1)) return true;
                continue;
            }

            if (max > 0 && current >= max) return false;
            if (_counts.TryUpdate(userId, current + 1, current)) return true;
        }
    }

    /// <summary>Releases a slot taken by <see cref="TryEnter"/>; the entry is removed when its count reaches zero.</summary>
    public void Exit(string userId)
    {
        while (true)
        {
            if (!_counts.TryGetValue(userId, out int current)) return;

            if (current <= 1)
            {
                if (_counts.TryRemove(new KeyValuePair<string, int>(userId, current))) return;
            }
            else if (_counts.TryUpdate(userId, current - 1, current))
            {
                return;
            }
        }
    }

    /// <summary>The slots <paramref name="userId"/> holds; 0 when the user has no entry.</summary>
    internal int CountFor(string userId) => _counts.TryGetValue(userId, out int current) ? current : 0;

    /// <summary>The number of users holding at least one slot.</summary>
    internal int TrackedUserCount => _counts.Count;
}
