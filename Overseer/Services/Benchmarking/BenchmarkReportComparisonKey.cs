namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The identity of a model comparison for its report documents: the set of runs, analysis groups
/// and battery results compared, regardless of order, subject or pricing basis. The only
/// implementation; the client sends entry keys and never hashes.
/// </summary>
public static class BenchmarkReportComparisonKey
{
    /// <summary>
    /// Lower-case hex SHA-256 of <c>runs=&lt;ids&gt;;groups=&lt;ids&gt;</c>, followed by
    /// <c>;batteries=&lt;ids&gt;</c> only when <paramref name="batteryRunIds"/> is non-empty; ids sorted,
    /// distinct and comma-joined (64 characters).
    /// </summary>
    public static string From(IEnumerable<long> runIds, IEnumerable<long> groupIds, IEnumerable<long>? batteryRunIds = null)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        ArgumentNullException.ThrowIfNull(groupIds);

        string canonical = "runs=" + Join(runIds) + ";groups=" + Join(groupIds);

        string batteries = batteryRunIds == null ? string.Empty : Join(batteryRunIds);
        if (batteries.Length > 0) canonical += ";batteries=" + batteries;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The key of the entry keys <c>run:&lt;id&gt;</c>, <c>group:&lt;id&gt;</c> and
    /// <c>battery:&lt;id&gt;</c>. False, with an empty key, when any key has another form, the list is
    /// empty, or it mixes battery results with runs or groups, which no comparison holds.
    /// </summary>
    public static bool TryFromEntryKeys(IEnumerable<string> keys, out string key)
    {
        key = string.Empty;
        if (keys == null) return false;

        var runIds = new List<long>();
        var groupIds = new List<long>();
        var batteryRunIds = new List<long>();
        foreach (var raw in keys)
        {
            string entry = (raw ?? string.Empty).Trim();
            if (TryParseId(entry, "run:", out long runId)) runIds.Add(runId);
            else if (TryParseId(entry, "group:", out long groupId)) groupIds.Add(groupId);
            else if (TryParseId(entry, "battery:", out long batteryRunId)) batteryRunIds.Add(batteryRunId);
            else return false;
        }
        if (runIds.Count == 0 && groupIds.Count == 0 && batteryRunIds.Count == 0) return false;
        if (batteryRunIds.Count > 0 && (runIds.Count > 0 || groupIds.Count > 0)) return false;

        key = From(runIds, groupIds, batteryRunIds);
        return true;
    }

    private static bool TryParseId(string entry, string prefix, out long id)
    {
        id = 0;
        return entry.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(entry.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }

    private static string Join(IEnumerable<long> ids)
        => string.Join(",", ids.Distinct().OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)));
}
