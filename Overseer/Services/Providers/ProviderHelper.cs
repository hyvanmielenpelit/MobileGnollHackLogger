using System.Text.Json;

namespace Overseer.Services.Providers;

public static class ProviderHelper
{
    public static object? GetProperty(object? obj, string propertyName)
    {
        if (obj == null) return null;

        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(propertyName, out var prop))
            {
                return UnwrapJsonElement(prop);
            }
            return null;
        }

        if (obj is JsonDocument doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var prop))
            {
                return UnwrapJsonElement(prop);
            }
            return null;
        }

        var objProp = obj.GetType().GetProperty(propertyName);
        return objProp?.GetValue(obj);
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : (element.TryGetDouble(out var d) ? (object)d : element.GetRawText()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Object or JsonValueKind.Array => element,
            _ => element
        };
    }

    /// <summary>
    /// Normalises a provider-reported service tier to a lowercase, prefix-free form.
    /// Google was measured returning "priority"/"standard", but the public ServiceTier enum
    /// table is truncated, so a "SERVICE_TIER_" prefix is stripped defensively. Unknown
    /// values are lowercased and returned unchanged.
    /// </summary>
    public static string? NormalizeServiceTier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        const string prefix = "SERVICE_TIER_";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(prefix.Length);
        }
        value = value.ToLowerInvariant();
        return value.Length == 0 || value == "unspecified" ? null : value;
    }
}
