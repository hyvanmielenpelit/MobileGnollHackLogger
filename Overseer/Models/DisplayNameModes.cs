namespace Overseer.Models;

/// <summary>
/// The persisted display-name modes. The value only tells the edit form how the stored
/// DisplayName was produced; the resolved name itself is always in DisplayName.
/// </summary>
public static class DisplayNameModes
{
    public const string ModelName = "model_name";
    public const string ModelId = "model_id";
    public const string Custom = "custom";

    /// <summary>
    /// Returns the value if it is one of the known modes, otherwise null.
    /// Guards the [MaxLength(32)] column against arbitrary client input.
    /// </summary>
    public static string? Normalize(string? value) => value switch
    {
        ModelName => ModelName,
        ModelId => ModelId,
        Custom => Custom,
        _ => null
    };
}
