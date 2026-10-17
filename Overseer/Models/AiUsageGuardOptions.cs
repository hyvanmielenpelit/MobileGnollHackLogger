namespace Overseer.Models;

/// <summary>Per-user limits on operator-funded AI use, bound to the <c>AiUsageGuard</c> section.</summary>
public sealed class AiUsageGuardOptions
{
    public const string SectionName = "AiUsageGuard";

    /// <summary>Longest message accepted for an operator-funded chat turn; 0 turns the cap off.</summary>
    public int MaxMessageCharacters { get; set; } = 100000;

    /// <summary>Length <c>session/create</c>'s <c>InitialPrompt</c> is truncated to; 0 turns truncation off.</summary>
    public int MaxInitialPromptCharacters { get; set; } = 2000;

    /// <summary>Operator-funded estimated spend per user per UTC day before chat is refused; 0 turns the ceiling off.</summary>
    public decimal MaxUserDailyCostUsd { get; set; } = 50.00m;

    /// <summary>Operator-funded chat turns plus sub-agent runs per user per UTC day; 0 turns the cap off.</summary>
    public int MaxUserDailyRuns { get; set; } = 2000;

    /// <summary>Operator-funded answers generated at once per user, across sessions; 0 turns the gate off.</summary>
    public int MaxConcurrentTurnsPerUser { get; set; } = 4;

    /// <summary>Operator-funded spend per user in the last 24 hours that raises an admin alert; never limits anyone. 0 turns the alert off.</summary>
    public decimal AlertUserDailyCostUsd { get; set; } = 20.00m;

    /// <summary>Administrators are never limited by the guard.</summary>
    public bool ExemptAdmins { get; set; } = true;
}
