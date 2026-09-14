namespace Overseer.Services.Privacy;

/// <summary>The privacy mode a new chat starts in. Stored on UserAiSettings by name.</summary>
public enum ChatPrivacyMode
{
    Standard,
    Confidential,
    Incognito
}

public static class ChatPrivacyModes
{
    /// <summary>Case-insensitive; null for an unrecognised or empty name.</summary>
    public static ChatPrivacyMode? Parse(string? stored)
        => Enum.TryParse<ChatPrivacyMode>(stored?.Trim(), ignoreCase: true, out var mode)
           && Enum.IsDefined(mode)
            ? mode
            : null;
}
