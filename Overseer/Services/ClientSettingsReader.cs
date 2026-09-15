using System.Text.Json;

namespace Overseer.Services;

/// <summary>Reads individual values out of the GnollHack client's OverseerSettings JSON
/// stored in ChatSession.ClientSettings.</summary>
public static class ClientSettingsReader
{
    /// <summary>StringData key under which the client reports its game version.</summary>
    public const string GnollHackVersionKey = "GHVersion";

    /// <summary>Length of BenchmarkGameSnapshot.SourceGnollHackVersion.</summary>
    public const int MaxGnollHackVersionLength = 64;

    /// <summary>Returns BoolData.<paramref name="key"/>, or null when the JSON is empty,
    /// malformed, or does not carry the key as a boolean.</summary>
    public static bool? ReadBool(string? clientSettingsJson, string key)
    {
        return ReadValue<bool?>(clientSettingsJson, "BoolData", key, value => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => (bool?)false,
            _ => null
        });
    }

    /// <summary>Returns StringData.<paramref name="key"/> trimmed, or null when the JSON is
    /// empty, malformed, does not carry the key as a string, or the string is blank.</summary>
    public static string? ReadString(string? clientSettingsJson, string key)
    {
        return ReadValue<string>(clientSettingsJson, "StringData", key, value =>
        {
            if (value.ValueKind != JsonValueKind.String) return null;
            string? text = value.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        });
    }

    /// <summary>The game version the client reported, capped to the length a board can store.</summary>
    public static string? ReadGnollHackVersion(string? clientSettingsJson)
    {
        return CapGnollHackVersion(ReadString(clientSettingsJson, GnollHackVersionKey));
    }

    /// <summary>Trims a version string and caps it to the length a board can store; null when blank.</summary>
    public static string? CapGnollHackVersion(string? version)
    {
        string? trimmed = version?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > MaxGnollHackVersionLength
            ? trimmed.Substring(0, MaxGnollHackVersionLength).TrimEnd()
            : trimmed;
    }

    private static T? ReadValue<T>(string? clientSettingsJson, string section, string key, System.Func<JsonElement, T?> read)
    {
        if (string.IsNullOrWhiteSpace(clientSettingsJson)) return default;

        try
        {
            using var doc = JsonDocument.Parse(clientSettingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return default;
            if (!doc.RootElement.TryGetProperty(section, out var sectionElement)) return default;
            if (sectionElement.ValueKind != JsonValueKind.Object) return default;
            if (!sectionElement.TryGetProperty(key, out var value)) return default;
            return read(value);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
