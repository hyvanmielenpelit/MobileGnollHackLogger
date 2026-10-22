namespace MobileGnollHackLogger.Data;

/// <summary>Which surface an <see cref="OverseerFeedbackSetting"/> row switches <c>report_feedback</c> on or off for.</summary>
public enum OverseerFeedbackSwitchTarget
{
    /// <summary>The production Overseer chat.</summary>
    Chat = 0,

    /// <summary>
    /// Retired. GnollBench runs choose the feedback tool per run in the launcher; rows of this target
    /// are history and are never read.
    /// </summary>
    GnollBench = 1
}
