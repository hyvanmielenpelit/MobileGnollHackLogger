using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

namespace Overseer.Services.Security;

/// <summary>
/// Turns what an operator-funded agent run's model calls reported — refusals, safety blocks and policy
/// rejections — into security events. Kinds, codes and the model only; never content.
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
            if (call.IsRefusal)
            {
                bool google = string.Equals(call.Provider, "Google", StringComparison.OrdinalIgnoreCase);
                events.Add(new SecurityEventEntry(
                    google ? SecurityEventKind.ProviderSafetyBlock : SecurityEventKind.ProviderRefusal,
                    UserId: userId,
                    SystemAiApiConfigurationId: systemConfigurationId,
                    Provider: call.Provider,
                    ModelId: call.RequestedModelId,
                    Code: SanitizeCode(call.FinishReason) ?? "refusal"));
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
                    Code: SanitizeCode(last.ProviderErrorCode)));
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

    /// <summary>Keeps <c>[A-Za-z0-9_.-]</c>, at most 64 characters; null when nothing is left.</summary>
    public static string? SanitizeCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var kept = new string(code.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-').ToArray());
        if (kept.Length == 0) return null;
        return kept.Length <= 64 ? kept : kept[..64];
    }
}
