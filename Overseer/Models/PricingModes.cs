namespace Overseer.Models;

using System;

/// <summary>
/// The persisted pricing modes. "default" uses the catalog pricing for the model,
/// while "custom" uses the configuration or model's own override rates.
/// </summary>
public static class PricingModes
{
    public const string Default = "default";
    public const string Custom = "custom";

    /// <summary>
    /// Normalizes the pricing mode: maps "custom" (case-insensitive) to "custom",
    /// and anything else to "default".
    /// </summary>
    public static string Normalize(string? value) =>
        string.Equals(value, Custom, StringComparison.OrdinalIgnoreCase)
            ? Custom
            : Default;
}
