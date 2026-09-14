using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Privacy;

/// <summary>How strictly a key's posture must be established before a confidential turn may run.</summary>
/// <remarks>
/// Ordered weakest to strongest, and the numbering is load-bearing: the effective gate is the
/// **greater** of the administrator's floor and the user's own choice, which is what lets a
/// user tighten but never weaken.
/// </remarks>
public enum ConfidentialityGateMode
{
    /// <summary>The user decides per key. Unmarked keys are usable. The default admin floor.</summary>
    UserDecides = 0,

    /// <summary>As above, but a key with nothing established prompts once before its first confidential turn.</summary>
    AskWhenUnclear = 1,

    /// <summary>Only an operator-verified posture of ZeroRetention or stronger passes.</summary>
    VerifiedPostureOnly = 2
}

/// <summary>What the gate decided for a turn.</summary>
public enum ConfidentialityGateOutcome
{
    /// <summary>The turn proceeds.</summary>
    Allow,

    /// <summary>Confirm with the user once for this key, then persist the answer.</summary>
    AskOnce,

    /// <summary>The turn does not proceed on this key, and the reason names why.</summary>
    Refuse
}

/// <summary>The four-state privacy badge, plus its absence.</summary>
public enum PrivateBadgeState
{
    /// <summary>Confidentiality Mode is off. No privacy claim is made, so no badge appears.</summary>
    None,

    /// <summary>Operator-verified ZeroRetention or stronger, every control active. The full promise holds.</summary>
    Green,

    /// <summary>NoTraining, or something stronger that is only self-declared. Protected, with a caveat.</summary>
    Yellow,

    /// <summary>Nothing established about provider retention. Overseer's own controls still hold.</summary>
    Orange,

    /// <summary>A policy control is off: the mode is on and is not keeping its promise.</summary>
    Red
}

/// <summary>
/// Which Tier 2 controls are actually active for a session, as opposed to which ones the mode
/// nominally implies.
/// </summary>
/// <remarks>
/// Stage C defines this and nothing populates it from persisted policy yet — Stages E and F do,
/// and this record is the contract they report into. The distinction matters because the
/// confidential storage settings are user-adjustable: a session can have the mode on and a
/// control off, and that has to be visible rather than silent. Hence <see cref="PrivateBadgeState.Red"/>.
/// </remarks>
public sealed record ConfidentialControlState
{
    /// <summary>Message content, titles and tool payloads are stored enveloped.</summary>
    public bool ContentEncrypted { get; init; }

    /// <summary>No tool in this session can reach a third-party network.</summary>
    public bool ExternalEgressBlocked { get; init; }

    /// <summary>No AI-generated title is derived from the user's first message.</summary>
    public bool TitleGenerationSuppressed { get; init; }

    /// <summary>The provider prompt cache is not keyed on this session's content.</summary>
    public bool PromptCacheDisabled { get; init; }

    /// <summary>The session is excluded from server-side search.</summary>
    public bool ExcludedFromSearch { get; init; }

    /// <summary>Every control this framework promises for a confidential session.</summary>
    public static readonly ConfidentialControlState AllActive = new()
    {
        ContentEncrypted = true,
        ExternalEgressBlocked = true,
        TitleGenerationSuppressed = true,
        PromptCacheDisabled = true,
        ExcludedFromSearch = true
    };

    /// <summary>Nothing active. What a normal session looks like.</summary>
    public static readonly ConfidentialControlState NoneActive = new();

    public bool AllControlsActive
        => ContentEncrypted && ExternalEgressBlocked && TitleGenerationSuppressed
            && PromptCacheDisabled && ExcludedFromSearch;

    /// <summary>The controls that are off, phrased for the badge tooltip. Empty when all are on.</summary>
    public IReadOnlyList<string> InactiveControls
    {
        get
        {
            var inactive = new List<string>(5);
            if (!ContentEncrypted) inactive.Add("message content is not encrypted");
            if (!ExternalEgressBlocked) inactive.Add("external tool egress is not blocked");
            if (!TitleGenerationSuppressed) inactive.Add("title generation is not suppressed");
            if (!PromptCacheDisabled) inactive.Add("the provider prompt cache is in use");
            if (!ExcludedFromSearch) inactive.Add("the session is included in search");
            return inactive;
        }
    }

    /// <summary>The controls that are on, phrased for the badge tooltip.</summary>
    public IReadOnlyList<string> ActiveControls
    {
        get
        {
            var active = new List<string>(5);
            if (ContentEncrypted) active.Add("message content encrypted");
            if (ExternalEgressBlocked) active.Add("external tool egress blocked");
            if (TitleGenerationSuppressed) active.Add("title generation suppressed");
            if (PromptCacheDisabled) active.Add("provider prompt cache disabled");
            if (ExcludedFromSearch) active.Add("excluded from search");
            return active;
        }
    }

    /// <summary>Every control, on or off, with the words the details dialog shows for it.</summary>
    /// <remarks>
    /// The order is the order the dialog lists them, and it is the order
    /// <see cref="ActiveControls"/> and <see cref="InactiveControls"/> use; keep the three in
    /// step so a control never changes position between the badge and the dialog.
    /// </remarks>
    public IReadOnlyList<ConfidentialControlDescriptor> Describe() =>
    [
        new("contentEncrypted", "Encrypted where it is stored",
            "Messages, titles and tool results are encrypted in Overseer's database.", ContentEncrypted),
        new("externalEgressBlocked", "Internet tools off",
            "No tool in this chat can send anything to a third-party service.", ExternalEgressBlocked),
        new("titleGenerationSuppressed", "No AI-made title",
            "The chat title is never generated from your first message.", TitleGenerationSuppressed),
        new("promptCacheDisabled", "Provider prompt cache off",
            "The AI provider's prompt cache is not keyed on this chat's content.", PromptCacheDisabled),
        new("excludedFromSearch", "Excluded from search",
            "This chat never appears in server-side search results.", ExcludedFromSearch)
    ];
}

/// <summary>One control of the confidential promise, as the badge details dialog names it.</summary>
public sealed record ConfidentialControlDescriptor(string Key, string Title, string Description, bool Active);

/// <summary>
/// The posture of one key, and whether an operator stood behind it.
/// </summary>
/// <param name="Posture">Resolved from the stored string; null and unrecognised become Unknown.</param>
/// <param name="IsOperatorVerified">
/// True only for a system configuration an operator dated. A user's own key can never be
/// verified — see <see cref="ConfidentialityPostureService.ResolveForUserKey"/>.
/// </param>
public sealed record PostureResolution(
    ProviderConfidentialityPosture Posture,
    bool IsOperatorVerified,
    string? AgreementRef = null,
    DateTime? EstablishedUtc = null,
    string? DataRegion = null,
    string? Note = null)
{
    /// <summary>What a turn on no key at all resolves to.</summary>
    public static readonly PostureResolution Nothing =
        new(ProviderConfidentialityPosture.Unknown, IsOperatorVerified: false);

    /// <summary>A posture the user asserted but nobody checked.</summary>
    public bool IsSelfDeclared => !IsOperatorVerified && Posture != ProviderConfidentialityPosture.Unknown;
}

/// <summary>How the provider posture was established, as the details dialog names it.</summary>
public enum PostureVerification
{
    /// <summary>An operator dated the agreement. The only form that can carry a green badge.</summary>
    Verified,

    /// <summary>The user asserted it on their own key, and Overseer cannot check it.</summary>
    SelfDeclared,

    /// <summary>Nothing is established, which is what a legacy or unmarked key means.</summary>
    NotEstablished
}

/// <summary>The provider half of the badge details.</summary>
/// <param name="Text">The short phrase, from <c>ToDisplayText()</c>.</param>
/// <param name="Description">The plain sentence, from <c>ToPlainDescription()</c>.</param>
/// <param name="VerificationText">The sentence naming who established the posture.</param>
/// <param name="Region">The data region, when one is recorded; its own field rather than a
/// parenthesis inside <paramref name="Text"/>, because the dialog gives it its own row.</param>
public sealed record BadgePosture(
    string Text,
    string Description,
    PostureVerification Verification,
    string VerificationText,
    string? Region);

/// <summary>The badge to show, and everything its details dialog says about it.</summary>
/// <remarks>
/// Structured rather than one prose string: the server is still the single author of every
/// sentence here, and the client only lays them out. A client that had to parse prose to
/// build the dialog would be one refactor away from making a privacy claim of its own.
/// </remarks>
public sealed record ConfidentialityBadge(
    PrivateBadgeState State,
    string Label,
    string Headline,
    string Summary,
    string Explanation,
    BadgePosture? Posture,
    IReadOnlyList<ConfidentialControlDescriptor> Controls)
{
    public static readonly ConfidentialityBadge NoBadge =
        new(PrivateBadgeState.None, string.Empty, string.Empty, string.Empty, string.Empty, null, []);

    /// <summary>The wire shape both the REST responses and the <c>private_badge</c> event carry.</summary>
    public PrivateBadgeDto ToDto() => new(
        State.ToString().ToLowerInvariant(),
        Label,
        Headline,
        Summary,
        Explanation,
        Posture == null
            ? null
            : new PrivateBadgePostureDto(
                Posture.Text,
                Posture.Description,
                Posture.Verification switch
                {
                    PostureVerification.Verified => "verified",
                    PostureVerification.SelfDeclared => "selfDeclared",
                    _ => "notEstablished"
                },
                Posture.VerificationText,
                Posture.Region),
        [.. Controls.Select(c => new PrivateBadgeControlDto(c.Key, c.Title, c.Description, c.Active))]);
}

/// <summary>The badge as the client reads it.</summary>
/// <remarks>
/// Every member carries an explicit <see cref="JsonPropertyNameAttribute"/> because the two
/// paths that emit this shape serialise differently: the controller through MVC's camelCase
/// policy, <c>ChatService</c> through <c>JsonSerializer.Serialize</c> with default options. A
/// member without one would arrive PascalCase from the event and camelCase from REST, and the
/// client would read <c>undefined</c> on exactly one of the two paths.
/// </remarks>
public sealed record PrivateBadgeDto(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("posture")] PrivateBadgePostureDto? Posture,
    [property: JsonPropertyName("controls")] IReadOnlyList<PrivateBadgeControlDto> Controls);

/// <inheritdoc cref="BadgePosture"/>
public sealed record PrivateBadgePostureDto(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("verification")] string Verification,
    [property: JsonPropertyName("verificationText")] string VerificationText,
    [property: JsonPropertyName("region")] string? Region);

/// <inheritdoc cref="ConfidentialControlDescriptor"/>
public sealed record PrivateBadgeControlDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("active")] bool Active);

/// <summary>The gate's decision and the sentence to show when it is not <c>Allow</c>.</summary>
public sealed record ConfidentialityGateResult(ConfidentialityGateOutcome Outcome, string Reason)
{
    public static readonly ConfidentialityGateResult Allowed = new(ConfidentialityGateOutcome.Allow, string.Empty);
}

/// <summary>
/// Resolves, for a turn: the posture of the funding key, whether an operator verified it, the
/// effective eligibility gate, and which privacy badge to show.
/// </summary>
/// <remarks>
/// Two invariants hold in every mode, and they are what keep the user's freedom honest:
/// <list type="number">
/// <item><description>
/// **A self-declared posture never produces a green badge.** The user may choose to proceed;
/// the product may not claim more than is known. A user's trust level is self-asserted, so
/// gating a green claim on it would let anyone tick "ZeroRetention" and unlock the claim.
/// </description></item>
/// <item><description>
/// **A user may tighten, never weaken.** The effective gate is the stricter of the
/// administrator's floor and the user's own setting.
/// </description></item>
/// </list>
/// Both are unit-tested, because both are the kind of rule that survives review and then dies
/// in a refactor.
/// </remarks>
public class ConfidentialityPostureService
{
    private readonly ConfidentialityGateMode _adminFloor;

    public ConfidentialityPostureService(IConfiguration configuration)
    {
        _adminFloor = ParseGate(configuration["PrivacySettings:ConfidentialFloor:ModelGate"]);
    }

    /// <summary>The administrator's floor, from <c>PrivacySettings:ConfidentialFloor:ModelGate</c>.</summary>
    public ConfidentialityGateMode AdminFloor => _adminFloor;

    /// <summary>
    /// Parses a gate name. Null, empty and unrecognised resolve to
    /// <see cref="ConfidentialityGateMode.UserDecides"/>, the documented default floor.
    /// </summary>
    public static ConfidentialityGateMode ParseGate(string? stored)
        => Enum.TryParse<ConfidentialityGateMode>(stored?.Trim(), ignoreCase: true, out var mode)
            ? mode
            : ConfidentialityGateMode.UserDecides;

    /// <summary>
    /// The posture of a system configuration. Operator-verified exactly when
    /// <see cref="SystemAiApiConfiguration.PostureVerifiedUtc"/> is set.
    /// </summary>
    public PostureResolution ResolveForSystemConfiguration(SystemAiApiConfiguration? configuration)
    {
        if (configuration == null)
            return PostureResolution.Nothing;

        var posture = ProviderConfidentialityPostureExtensions.ParsePosture(configuration.ConfidentialityPosture);

        /* Verification is what a date in PostureVerifiedUtc means, and it is the only thing
           that distinguishes a posture the operator stands behind from one somebody typed.
           An Unknown posture cannot be "verified" into anything: there is nothing to verify. */
        bool verified = configuration.PostureVerifiedUtc.HasValue
            && posture != ProviderConfidentialityPosture.Unknown;

        return new PostureResolution(
            posture,
            verified,
            configuration.PostureAgreementRef,
            configuration.PostureVerifiedUtc,
            configuration.DataRegion,
            configuration.ConfidentialityNote);
    }

    /// <summary>
    /// The posture a user declared for their own key. <c>IsOperatorVerified</c> is always
    /// false — Overseer cannot see the agreement, so it cannot verify it, and pretending
    /// otherwise is the whole defect this separation exists to prevent.
    /// </summary>
    public PostureResolution ResolveForUserKey(UserAiApiKey? key)
    {
        if (key == null)
            return PostureResolution.Nothing;

        return new PostureResolution(
            ProviderConfidentialityPostureExtensions.ParsePosture(key.ConfidentialityPosture),
            IsOperatorVerified: false,
            AgreementRef: null,
            EstablishedUtc: key.PostureDeclaredUtc,
            DataRegion: null,
            Note: key.ConfidentialityNote);
    }

    /// <summary>
    /// The stricter of the administrator's floor and the user's own choice. A null user choice
    /// means the user has expressed none, so the floor stands.
    /// </summary>
    public ConfidentialityGateMode EffectiveGate(ConfidentialityGateMode? userChoice)
        => EffectiveGate(_adminFloor, userChoice);

    /// <inheritdoc cref="EffectiveGate(System.Nullable{Overseer.Services.Privacy.ConfidentialityGateMode})"/>
    public static ConfidentialityGateMode EffectiveGate(
        ConfidentialityGateMode adminFloor, ConfidentialityGateMode? userChoice)
        => userChoice.HasValue && userChoice.Value > adminFloor ? userChoice.Value : adminFloor;

    /// <summary>
    /// Whether a confidential turn may run on this key.
    /// </summary>
    /// <param name="posture">The resolved posture of the funding key.</param>
    /// <param name="userTrustsForConfidential">
    /// The user's own per-key judgement: null = undecided, false = refused, true = accepted.
    /// Only meaningful for the user's own keys; pass null for a system configuration.
    /// </param>
    /// <param name="effectiveGate">From <see cref="EffectiveGate(System.Nullable{ConfidentialityGateMode})"/>.</param>
    /// <param name="isSystemProvidedModel">
    /// Selects where the refusal sends the user to change their answer. A decision about an
    /// operator-provided model is stored per user in <c>UserSystemModelConfidentialTrust</c> and
    /// edited in its own settings section, not under API keys.
    /// </param>
    public ConfidentialityGateResult EvaluateGate(
        PostureResolution posture,
        bool? userTrustsForConfidential,
        ConfidentialityGateMode effectiveGate,
        bool isSystemProvidedModel = false)
    {
        /* An explicit "no" is a decision, not an absence, and it is checked first in every
           mode: a user who has said this key is not adequate for confidential work should not
           have to say it again because the gate happens to be permissive. */
        if (userTrustsForConfidential == false)
        {
            return new ConfidentialityGateResult(
                ConfidentialityGateOutcome.Refuse,
                isSystemProvidedModel
                    ? "You marked this model as not suitable for confidential chats. Change that under "
                      + "Settings, System Model Confidentiality, to use it here."
                    : "You marked this key as not suitable for confidential chats. Change that in API keys to use it here.");
        }

        switch (effectiveGate)
        {
            case ConfidentialityGateMode.VerifiedPostureOnly:
                if (posture.IsOperatorVerified && posture.Posture.MeetsConfidentialThreshold())
                    return ConfidentialityGateResult.Allowed;

                /* The refusal names why, because "not allowed" invites the user to try the
                   same thing again. A self-declared posture is the interesting case: it is not
                   distrust of the user, it is that the product cannot verify it. */
                return new ConfidentialityGateResult(
                    ConfidentialityGateOutcome.Refuse,
                    posture.IsSelfDeclared
                        ? $"This model's retention posture ({posture.Posture.ToDisplayText()}) is self-declared, and Overseer cannot verify it. "
                          + "Confidential chats are restricted to models an administrator has verified as zero-retention or stronger."
                        : "Confidential chats are restricted to models an administrator has verified as zero-retention or stronger. "
                          + "Nothing is established about this model's data retention.");

            case ConfidentialityGateMode.AskWhenUnclear:
                /* Asked once per credential, not once per turn: the answer persists, on the key
                   row or in the per-user trust table, and an answer that exists is the end of
                   the question. The trigger is therefore the absence of a decision alone -- an
                   unestablished posture is what the question is *about*, so asking again after
                   it has been answered would make the prompt unanswerable. Which question is
                   asked still depends on the posture. */
                if (userTrustsForConfidential == null)
                {
                    return new ConfidentialityGateResult(
                        ConfidentialityGateOutcome.AskOnce,
                        posture.Posture == ProviderConfidentialityPosture.Unknown
                            ? "Nothing is established about this model's data retention. Overseer's own protections still apply. Use it for confidential chats?"
                            : $"This model's posture is {posture.Posture.ToDisplayText()}, self-declared. Use it for confidential chats?");
                }

                return ConfidentialityGateResult.Allowed;

            default: // UserDecides
                // Unmarked keys are usable; the badge reports what is actually known.
                return ConfidentialityGateResult.Allowed;
        }
    }

    /// <summary>
    /// The badge for a session: absent when the mode is off, red when a control is off, and
    /// otherwise graded by what is established about the provider.
    /// </summary>
    public ConfidentialityBadge ResolveBadge(
        bool isConfidential, PostureResolution posture, ConfidentialControlState controls)
    {
        if (!isConfidential)
            return ConfidentialityBadge.NoBadge;

        /* Red first, and it outranks any posture. A strong verified agreement is worth nothing
           to a session whose content is being written in clear, and the storage settings are
           user-adjustable, so this state is reachable by configuration rather than by bug. */
        var badgePosture = DescribePosture(posture);
        var describedControls = controls.Describe();

        if (!controls.AllControlsActive)
        {
            var inactive = controls.InactiveControls;

            return new ConfidentialityBadge(
                PrivateBadgeState.Red,
                "Private",
                "Not fully protected",
                "Not fully protected: a setting is off.",
                $"This chat is marked confidential, but {inactive.Count} of Overseer's 5 protections "
                    + (inactive.Count == 1 ? "is" : "are") + " off: "
                    + string.Join("; ", inactive) + ".",
                badgePosture,
                describedControls);
        }

        /* Invariant 1, and the only place it is enforced: green requires operator verification.
           A self-declared ZeroRetention lands in yellow no matter how it was entered. */
        if (posture.IsOperatorVerified && posture.Posture.MeetsConfidentialThreshold())
        {
            return new ConfidentialityBadge(
                PrivateBadgeState.Green,
                "Private",
                "Fully protected",
                "Fully protected, on a verified model.",
                "Every protection Overseer offers is on, and the AI model runs under an agreement "
                    + "an administrator has verified.",
                badgePosture,
                describedControls);
        }

        if (posture.Posture.IsAtLeast(ProviderConfidentialityPosture.NoTraining))
        {
            return new ConfidentialityBadge(
                PrivateBadgeState.Yellow,
                "Private",
                "Protected, with a caveat",
                "Protected, with a caveat about the provider.",
                "Every protection Overseer offers is on. "
                    + (posture.IsOperatorVerified
                        ? "The provider has agreed not to train on your messages, but may still keep "
                          + "them for a time, which is weaker than zero retention."
                        : "The provider's data handling is as you declared it on your own key, and "
                          + "Overseer cannot verify that."),
                badgePosture,
                describedControls);
        }

        return new ConfidentialityBadge(
            PrivateBadgeState.Orange,
            "Private",
            "Protected by Overseer only",
            "Protected by Overseer. Provider data handling unknown.",
            "Every protection Overseer offers is on. Nothing is known about what the AI provider "
                + "keeps, so no promise is made about that.",
            badgePosture,
            describedControls);
    }

    /// <summary>
    /// The provider half of the badge. <see cref="PostureVerification.Verified"/> is derived
    /// from <c>IsOperatorVerified</c>, the same flag the green rule uses, so the dialog cannot
    /// say "verified" about a posture that could not earn a green badge.
    /// </summary>
    private static BadgePosture DescribePosture(PostureResolution posture)
    {
        var verification = posture.IsOperatorVerified
            ? PostureVerification.Verified
            : posture.IsSelfDeclared
                ? PostureVerification.SelfDeclared
                : PostureVerification.NotEstablished;

        return new BadgePosture(
            posture.Posture.ToDisplayText(),
            posture.Posture.ToPlainDescription(),
            verification,
            verification switch
            {
                PostureVerification.Verified => "An administrator, who recorded the agreement.",
                PostureVerification.SelfDeclared => "You, on your own API key. Overseer cannot check it.",
                _ => "Nobody. Overseer makes no claim about what this provider keeps."
            },
            string.IsNullOrWhiteSpace(posture.DataRegion) ? null : posture.DataRegion);
    }
}
