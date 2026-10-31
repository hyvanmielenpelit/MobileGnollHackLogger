using System.Globalization;

namespace Overseer.Services;

/// <summary>A shared admin task parsed from an alert id: any administrator can mark it handled for all of them.</summary>
public readonly record struct AdminSharedTask(string Key, long Sequence)
{
    private const int MaxAlertIdLength = 128;
    private const int SpendDateLength = 8;

    /// <summary>
    /// Reads "snapshot-refusals:&lt;id&gt;", "snapshot-autoblock:&lt;id&gt;" and "ai-spend:&lt;yyyyMMdd&gt;:&lt;count&gt;".
    /// Any other id, or a sequence that is not a positive number, is not a shared task.
    /// </summary>
    public static bool TryParse(string? alertId, out AdminSharedTask task)
    {
        task = default;
        if (string.IsNullOrEmpty(alertId) || alertId.Length > MaxAlertIdLength)
        {
            return false;
        }

        var parts = alertId.Split(':');
        string key;
        if (parts.Length == 2 && parts[0] is "snapshot-refusals" or "snapshot-autoblock")
        {
            key = parts[0];
        }
        else if (parts.Length == 3 && parts[0] == "ai-spend" && parts[1].Length == SpendDateLength && parts[1].All(char.IsAsciiDigit))
        {
            key = parts[0] + ":" + parts[1];
        }
        else
        {
            return false;
        }

        if (!long.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) || sequence <= 0)
        {
            return false;
        }

        task = new AdminSharedTask(key, sequence);
        return true;
    }
}
