using System.Text.Json;

namespace Overseer.Services;

/// <summary>Reads individual values out of the GnollHack client's OverseerSettings JSON
/// stored in ChatSession.ClientSettings.</summary>
public static class ClientSettingsReader
{
    /// <summary>Returns BoolData.<paramref name="key"/>, or null when the JSON is empty,
    /// malformed, or does not carry the key as a boolean.</summary>
    public static bool? ReadBool(string? clientSettingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(clientSettingsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(clientSettingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("BoolData", out var boolData)) return null;
            if (boolData.ValueKind != JsonValueKind.Object) return null;
            if (!boolData.TryGetProperty(key, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
