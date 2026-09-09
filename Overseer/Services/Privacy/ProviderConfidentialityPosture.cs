using System;
using System.Collections.Generic;
using System.Linq;

namespace Overseer.Services.Privacy;

/// <summary>
/// What has actually been agreed with the account behind an API key, ordered weakest to
/// strongest. Nothing here is ever inferred from a model name.
/// </summary>
/// <remarks>
/// Persisted as a <c>MaxLength(32)</c> string on both key holders, with **null meaning a
/// legacy row, treated as <see cref="Unknown"/>** — the same convention as
/// <c>SystemAiApiConfiguration.PricingMode</c> and <c>DisplayNameMode</c>, which exists so an
/// explicit value can be told apart from an unset one. A string rather than an int enum for
/// consistency with those neighbours, and because this ladder will gain members.
/// </remarks>
public enum ProviderConfidentialityPosture
{
    /// <summary>Nothing is established. The honest default, and what every legacy row means.</summary>
    Unknown = 0,

    /// <summary>Ordinary consumer or pay-as-you-go terms: retention for abuse monitoring, no special agreement.</summary>
    Standard = 1,

    /// <summary>The provider has undertaken not to train on the content. Retention may still apply.</summary>
    NoTraining = 2,

    /// <summary>Zero data retention: the content is not stored after the response is served.</summary>
    ZeroRetention = 3,

    /// <summary>A dedicated deployment in a named region under the operator's own agreement.</summary>
    PrivateCloud = 4,

    /// <summary>Inference runs on hardware the operator controls. Nothing leaves it.</summary>
    SelfHosted = 5
}

public static class ProviderConfidentialityPostureExtensions
{
    /// <summary>
    /// The weakest posture the confidentiality mode's full promise can rest on. Below this,
    /// nothing about provider-side retention has been established.
    /// </summary>
    public const ProviderConfidentialityPosture ConfidentialThreshold = ProviderConfidentialityPosture.ZeroRetention;

    private static readonly Dictionary<string, ProviderConfidentialityPosture> ByName =
        Enum.GetValues<ProviderConfidentialityPosture>()
            .ToDictionary(v => v.ToString(), v => v, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a stored value. Null, empty and unrecognised all resolve to
    /// <see cref="ProviderConfidentialityPosture.Unknown"/>.
    /// </summary>
    /// <remarks>
    /// Unrecognised resolves down rather than throwing on purpose: a value written by a newer
    /// build, or corrupted, must degrade to "nothing is established" — the reading that cannot
    /// over-promise. A posture that fails to parse into something stronger than it is would be
    /// the one failure mode this whole ladder exists to prevent.
    /// </remarks>
    public static ProviderConfidentialityPosture ParsePosture(string? stored)
        => !string.IsNullOrWhiteSpace(stored) && ByName.TryGetValue(stored.Trim(), out var posture)
            ? posture
            : ProviderConfidentialityPosture.Unknown;

    /// <summary>The canonical string to persist. Never null.</summary>
    public static string ToStoredValue(this ProviderConfidentialityPosture posture) => posture.ToString();

    /// <summary>Whether this posture is at least as strong as <paramref name="other"/>.</summary>
    public static bool IsAtLeast(this ProviderConfidentialityPosture posture, ProviderConfidentialityPosture other)
        => posture >= other;

    /// <summary>Whether the posture clears <see cref="ConfidentialThreshold"/>.</summary>
    public static bool MeetsConfidentialThreshold(this ProviderConfidentialityPosture posture)
        => posture.IsAtLeast(ConfidentialThreshold);

    /// <summary>A short phrase for a badge tooltip or a settings list.</summary>
    public static string ToDisplayText(this ProviderConfidentialityPosture posture) => posture switch
    {
        ProviderConfidentialityPosture.Standard => "Standard provider terms",
        ProviderConfidentialityPosture.NoTraining => "No training on content",
        ProviderConfidentialityPosture.ZeroRetention => "Zero data retention",
        ProviderConfidentialityPosture.PrivateCloud => "Private cloud deployment",
        ProviderConfidentialityPosture.SelfHosted => "Self-hosted inference",
        _ => "Not established"
    };
}
