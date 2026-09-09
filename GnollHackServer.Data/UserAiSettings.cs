namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class UserAiSettings
{
    [Key]
    [ForeignKey("AspNetUser")]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser? AspNetUser { get; set; }
    
    public bool SpoilerFreeMode { get; set; } = true;
    public bool ShowSourceCodeReferences { get; set; } = false;
    public bool ShowParallelBadge { get; set; } = true;
    public bool ShowContextWindowUsage { get; set; } = true;
    public bool ShowChatCost { get; set; } = true;
    public int ShowThoughtsAndTools { get; set; } = 1;

    public int? MaxResultLength { get; set; }
    public int? MaxCallsPerSession { get; set; }
    public int? MaxToolIterations { get; set; }
    public int? MaxParallelToolCalls { get; set; }

    public bool EnableWebSearch { get; set; } = true;
    public bool EnableToolUse { get; set; } = true;
    public bool EnableSubAgents { get; set; } = false;
    public bool EnableClientTools { get; set; } = true;
    public bool EnableGameActions { get; set; } = false;

    public long? TitleGenerationModelId { get; set; }
    public long? TitleGenerationSystemModelId { get; set; }
    public bool TitleGenerationDisabled { get; set; } = false;

    public int? RequestTimeout { get; set; }

    /* The user's own confidential preferences. Every one of them is resolved as the STRICTER of
       this value and the administrator's floor in PrivacySettings:ConfidentialFloor, so a value
       here can tighten the mode but never weaken it. The resolved result is snapshotted onto
       the session at creation or upgrade, so changing these later cannot retroactively weaken a
       promise already made.

       Confidentiality Mode is available to every user: no group membership is consulted. */

    /// <summary>"Encrypted" (default), "Ephemeral" or "Plaintext". Null = the default.</summary>
    [MaxLength(32)]
    public string? ConfidentialPersistence { get; set; }

    /// <summary>
    /// Days of inactivity before a confidential session expires. Null = the resolved default of
    /// 30, against the global 90 for a normal chat. A smaller value is the stricter one.
    /// </summary>
    public int? ConfidentialRetentionDays { get; set; }

    /// <summary>Blocks every tool that leaves the machine, and the provider's own web search.</summary>
    public bool? ConfidentialDisableToolEgress { get; set; }

    /// <summary>
    /// Suppresses the AI-generated title, which sends the first message to a separately
    /// configured model — often a different provider, and an unvetted second egress.
    /// </summary>
    public bool? ConfidentialDisableTitleGeneration { get; set; }

    /// <summary>Stops prompt prefixes being retained provider-side.</summary>
    public bool? ConfidentialDisablePromptCache { get; set; }

    /// <summary>
    /// Purges on deletion instead of using the 30-day trash. Without it a 30-day TTL yields 60
    /// days of retention.
    /// </summary>
    public bool? ConfidentialImmediatePurge { get; set; }

    /// <summary>
    /// "UserDecides" (default), "AskWhenUnclear" or "VerifiedPostureOnly" — how strictly the
    /// funding key's retention posture must be established. See ConfidentialityGateMode.
    /// </summary>
    [MaxLength(32)]
    public string? ConfidentialModelGate { get; set; }

    /// <summary>Whether the user has seen the notice naming the supported data ceiling.</summary>
    public bool ConfidentialFirstUseNoticeAcknowledged { get; set; } = false;

    /* Outbound DLP masking, one switch per class of secret. Null means the user has expressed
       no preference and the built-in default applies; the administrator's floor in
       PrivacySettings:DlpFloor can force a class ON but never off, so a value here can add
       masking and never remove it.

       Unlike Confidentiality Mode these are NOT per session and NOT opt-in per chat: a class
       switched on applies to every outbound turn, confidential or not. Nor are they
       snapshotted onto a session -- there is nothing to snapshot, because masking changes only
       what leaves the server on the turn it runs and never what is stored. */

    /// <summary>Provider and cloud API keys. Default on.</summary>
    public bool? DlpMaskApiKeys { get; set; }

    /// <summary>PEM and PGP private-key blocks. Default on.</summary>
    public bool? DlpMaskPrivateKeys { get; set; }

    /// <summary>Bearer tokens and JWTs. Default on.</summary>
    public bool? DlpMaskTokens { get; set; }

    /// <summary>Card numbers that pass a Luhn check. Default on.</summary>
    public bool? DlpMaskCreditCards { get; set; }

    /// <summary>US Social Security numbers. Default on.</summary>
    public bool? DlpMaskSsns { get; set; }

    /// <summary>
    /// E-mail addresses. Default <b>off</b>: masking them measurably degrades answers, for a
    /// class of data the user usually intended to send.
    /// </summary>
    public bool? DlpMaskEmails { get; set; }

    /// <summary>Phone numbers. Default <b>off</b>, for the same reason as e-mail addresses.</summary>
    public bool? DlpMaskPhoneNumbers { get; set; }
}
