using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Privacy;

/// <summary>How a confidential session's content is stored, weakest to strongest.</summary>
/// <remarks>
/// The numbering is load-bearing: resolution takes the greater of the user's choice and the
/// administrator's floor, which is what lets a user tighten but never weaken.
/// </remarks>
public enum ConfidentialPersistence
{
    /// <summary>Stored readable. The mode's other controls still apply, but the badge goes red.</summary>
    Plaintext = 0,

    /// <summary>Message content is enveloped at rest. The default.</summary>
    Encrypted = 1,

    /// <summary>Never persisted at all.</summary>
    Ephemeral = 2
}

/// <summary>
/// The confidential policy for one session: the resolved values, snapshotted at creation or
/// upgrade.
/// </summary>
/// <remarks>
/// Serialised into <see cref="ChatSession.ConfidentialPolicyJson"/>. Two of its values are also
/// materialised into their own columns, because the retention sweep is a set-based query that
/// cannot read JSON — see <see cref="ChatSession.EffectiveRetentionDays"/>.
/// </remarks>
public sealed record ConfidentialPolicy
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConfidentialPersistence Persistence { get; init; } = ConfidentialPersistence.Encrypted;

    public int RetentionDays { get; init; } = 30;

    public bool DisableToolEgress { get; init; } = true;

    public bool DisableTitleGeneration { get; init; } = true;

    public bool DisablePromptCache { get; init; } = true;

    public bool ImmediatePurge { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConfidentialityGateMode ModelGate { get; init; } = ConfidentialityGateMode.UserDecides;

    /// <summary>Everything the framework promises for a confidential session.</summary>
    public static readonly ConfidentialPolicy Defaults = new();

    /// <summary>
    /// Which Tier 2 controls this policy actually leaves active, for the privacy badge.
    /// </summary>
    /// <remarks>
    /// The badge is red whenever any of them is off. That state is reachable by configuration
    /// rather than by bug, which is exactly why it has to be visible: a session can be in the
    /// mode and not keeping its promise.
    /// </remarks>
    public ConfidentialControlState ToControlState() => new()
    {
        ContentEncrypted = Persistence != ConfidentialPersistence.Plaintext,
        ExternalEgressBlocked = DisableToolEgress,
        TitleGenerationSuppressed = DisableTitleGeneration,
        PromptCacheDisabled = DisablePromptCache,
        /* Search exclusion follows persistence: an unencrypted confidential session stays
           searchable, because there is nothing to hide from the query that the row does not
           already show in plain text. */
        ExcludedFromSearch = Persistence != ConfidentialPersistence.Plaintext
    };
}

/// <summary>
/// Resolves the effective confidential policy as the stricter of the user's settings and the
/// administrator's floor.
/// </summary>
/// <remarks>
/// Confidentiality Mode is available to **every user**: this resolver consults no group
/// membership and does not read <c>UserGroups</c>. Should that change, it is a resolver change
/// and not a schema change.
/// </remarks>
public class ConfidentialPolicyResolver
{
    private readonly ConfidentialPolicy _floor;

    public ConfidentialPolicyResolver(IConfiguration configuration)
    {
        var section = configuration.GetSection("PrivacySettings:ConfidentialFloor");

        /* Parsed by hand rather than through ConfigurationBinder.GetValue, which THROWS on a
           value it cannot convert -- so "DisableToolEgress": "yes" or "RetentionDays":
           "thirty" would take the application down at startup, from inside this constructor,
           with an error naming DI rather than the setting. Every other malformed input in this
           framework is reported and defaulted; ParsePersistence beside it already promises
           exactly that, and this now matches it. The default is the strict end of the range,
           so a typo cannot quietly weaken the floor. */
        _floor = new ConfidentialPolicy
        {
            Persistence = ParsePersistence(section["Persistence"]) ?? ConfidentialPersistence.Encrypted,
            RetentionDays = ReadInt(section, "RetentionDays", 30),
            DisableToolEgress = ReadBool(section, "DisableToolEgress", true),
            DisableTitleGeneration = ReadBool(section, "DisableTitleGeneration", true),
            DisablePromptCache = ReadBool(section, "DisablePromptCache", true),
            ImmediatePurge = ReadBool(section, "ImmediatePurge", true),
            ModelGate = ConfidentialityPostureService.ParseGate(section["ModelGate"])
        };
    }

    /// <summary>A missing, empty or unparseable setting reads as <paramref name="fallback"/>.</summary>
    private static bool ReadBool(IConfiguration section, string key, bool fallback)
        => bool.TryParse(section[key]?.Trim(), out bool value) ? value : fallback;

    /// <summary>A missing, empty or unparseable setting reads as <paramref name="fallback"/>.</summary>
    private static int ReadInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key]?.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    /// <summary>The administrator's floor. Read-only, and reported to the client so the UI can disable what it cannot change.</summary>
    public ConfidentialPolicy Floor => _floor;

    /// <summary>
    /// Parses a stored persistence value. Null, empty and unrecognised resolve to
    /// <see cref="ConfidentialPersistence.Encrypted"/> — the default, not the weakest.
    /// </summary>
    /// <remarks>
    /// Unlike the posture ladder, an unparseable value here resolves to the *middle* of the
    /// range rather than down. Resolving down would mean a corrupt value silently produced a
    /// plaintext confidential session, which is the outcome this setting exists to prevent.
    /// </remarks>
    public static ConfidentialPersistence? ParsePersistence(string? stored)
        => Enum.TryParse<ConfidentialPersistence>(stored?.Trim(), ignoreCase: true, out var value)
            ? value
            : null;

    /// <summary>
    /// The effective policy for a user: each value the stricter of theirs and the floor's.
    /// </summary>
    /// <param name="settings">The user's settings, or null when they have none.</param>
    public ConfidentialPolicy Resolve(UserAiSettings? settings)
    {
        if (settings == null)
            return _floor;

        return new ConfidentialPolicy
        {
            // For persistence, higher is stricter.
            Persistence = Max(ParsePersistence(settings.ConfidentialPersistence) ?? _floor.Persistence, _floor.Persistence),

            /* For retention, SMALLER is stricter -- fewer days of retention is a stronger
               promise. This is the one value where the comparison inverts, and getting it
               backwards would let a user extend retention past the administrator's ceiling. */
            RetentionDays = Math.Min(
                settings.ConfidentialRetentionDays.HasValue && settings.ConfidentialRetentionDays.Value > 0
                    ? settings.ConfidentialRetentionDays.Value
                    : _floor.RetentionDays,
                _floor.RetentionDays),

            // For the booleans, true is stricter.
            DisableToolEgress = (settings.ConfidentialDisableToolEgress ?? _floor.DisableToolEgress) || _floor.DisableToolEgress,
            DisableTitleGeneration = (settings.ConfidentialDisableTitleGeneration ?? _floor.DisableTitleGeneration) || _floor.DisableTitleGeneration,
            DisablePromptCache = (settings.ConfidentialDisablePromptCache ?? _floor.DisablePromptCache) || _floor.DisablePromptCache,
            ImmediatePurge = (settings.ConfidentialImmediatePurge ?? _floor.ImmediatePurge) || _floor.ImmediatePurge,

            ModelGate = ConfidentialityPostureService.EffectiveGate(
                _floor.ModelGate,
                settings.ConfidentialModelGate == null
                    ? null
                    : ConfidentialityPostureService.ParseGate(settings.ConfidentialModelGate))
        };
    }

    /// <summary>
    /// Reads the policy a session was created or upgraded under.
    /// </summary>
    /// <remarks>
    /// A session with no snapshot — one created before this column existed — resolves to the
    /// defaults rather than to whatever the user's settings say today. The whole point of the
    /// snapshot is that the session's promise does not move.
    /// </remarks>
    public static ConfidentialPolicy ReadSnapshot(ChatSession session)
    {
        if (string.IsNullOrWhiteSpace(session.ConfidentialPolicyJson))
            return ConfidentialPolicy.Defaults;

        try
        {
            return JsonSerializer.Deserialize<ConfidentialPolicy>(session.ConfidentialPolicyJson)
                ?? ConfidentialPolicy.Defaults;
        }
        catch (JsonException)
        {
            return ConfidentialPolicy.Defaults;
        }
    }

    /// <summary>
    /// Writes the policy onto a session: the JSON snapshot, and the two scalars the retention
    /// sweep needs as real columns.
    /// </summary>
    public static void ApplyToSession(ChatSession session, ConfidentialPolicy policy)
    {
        session.ConfidentialPolicyJson = JsonSerializer.Serialize(policy);
        session.EffectiveRetentionDays = policy.RetentionDays;
        session.ImmediatePurgeOnDelete = policy.ImmediatePurge;
    }

    private static ConfidentialPersistence Max(ConfidentialPersistence a, ConfidentialPersistence b)
        => a > b ? a : b;
}
