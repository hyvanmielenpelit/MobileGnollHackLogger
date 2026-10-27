using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

namespace Overseer.Services.Security;

/// <summary>
/// Turns what an operator-funded agent run's model calls reported — refusals, safety blocks and policy
/// rejections — into security events. Kinds, codes, the model and the provider's request id only; never content.
/// </summary>
public static class ProviderRefusalEvents
{
    /// <summary>Fragments of a provider error code or type that name a usage-policy rejection.</summary>
    public static readonly IReadOnlyList<string> PolicyErrorCodeFragments = new[]
    {
        "invalid_prompt", "policy", "content_filter", "safety", "prohibited", "blocklist"
    };

    /// <summary>The events one run's calls justify.</summary>
    public static List<SecurityEventEntry> FromRun(AgentRunResult run, string? userId, long? systemConfigurationId)
    {
        var events = new List<SecurityEventEntry>();
        if (string.IsNullOrEmpty(userId)) return events;

        foreach (var call in run.ModelCalls)
        {
            // The provider's request id, which an operator quotes to the provider.
            string? requestId = ProviderAttemptClassifier.SanitizeErrorCode(call.RequestId);
            string? detail = requestId == null ? null : "req:" + requestId;

            if (call.IsRefusal)
            {
                bool google = string.Equals(call.Provider, "Google", StringComparison.OrdinalIgnoreCase);
                events.Add(new SecurityEventEntry(
                    google ? SecurityEventKind.ProviderSafetyBlock : SecurityEventKind.ProviderRefusal,
                    UserId: userId,
                    SystemAiApiConfigurationId: systemConfigurationId,
                    Provider: call.Provider,
                    ModelId: call.RequestedModelId,
                    Code: ProviderAttemptClassifier.SanitizeErrorCode(call.FinishReason) ?? "refusal",
                    Detail: detail));
            }

            var last = call.Attempts.Count > 0 ? call.Attempts[^1] : null;
            if (last != null && last.Outcome == ProviderAttemptOutcome.OurRequest
                && last.HttpStatus is >= 400 and < 500 && IsPolicyCode(last.ProviderErrorCode))
            {
                events.Add(new SecurityEventEntry(
                    SecurityEventKind.ProviderPolicyRejection,
                    UserId: userId,
                    SystemAiApiConfigurationId: systemConfigurationId,
                    Provider: call.Provider,
                    ModelId: call.RequestedModelId,
                    Code: ProviderAttemptClassifier.SanitizeErrorCode(last.ProviderErrorCode),
                    Detail: detail));
            }
        }
        return events;
    }

    /// <summary>Records every event the run's calls justify. Never throws.</summary>
    public static void Record(ISecurityEventRecorder? recorder, AgentRunResult run, string? userId, long? systemConfigurationId)
    {
        if (recorder == null) return;
        try
        {
            foreach (var entry in FromRun(run, userId, systemConfigurationId))
            {
                recorder.Record(entry);
            }
        }
        catch
        {
            // Recording is best effort and never fails a turn.
        }
    }

    public static bool IsPolicyCode(string? code) =>
        !string.IsNullOrEmpty(code) && PolicyErrorCodeFragments.Any(f => code.Contains(f, StringComparison.OrdinalIgnoreCase));
}
