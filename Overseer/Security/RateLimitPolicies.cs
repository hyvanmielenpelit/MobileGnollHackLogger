namespace Overseer.Security;

/// <summary>
/// Names of the rate-limiting policies registered in Program.cs, and the message each
/// rejection carries.
/// </summary>
/// <remarks>
/// The names are constants because <c>OnRejected</c> is global and has to recognise the policy
/// that rejected in order to say something true about it; a literal in two places drifts and
/// the drift is invisible -- a mismatched name simply means no limit applies.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>The Sentry tunnel. Pre-existing name, kept so the attribute keeps matching.</summary>
    public const string SentryTunnel = "TunnelRateLimit";

    /// <summary>Chat turns and other write traffic on the chat API.</summary>
    public const string Chat = "ChatRateLimit";

    /// <summary>Attachment downloads, the enumeration path.</summary>
    public const string Attachment = "AttachmentRateLimit";

    public static string RejectionMessage(string? policyName) => policyName switch
    {
        SentryTunnel => "Too many log events. Please try again later.",
        Attachment => "Too many attachment requests. Please wait a moment and try again.",
        Chat => "Too many chat requests. Please wait a moment and try again.",
        _ => "Too many requests. Please wait a moment and try again."
    };
}
