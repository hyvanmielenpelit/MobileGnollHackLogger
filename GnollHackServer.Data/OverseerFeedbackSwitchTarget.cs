namespace MobileGnollHackLogger.Data;

/// <summary>Which surface an <see cref="OverseerFeedbackSetting"/> row switches <c>report_feedback</c> on or off for.</summary>
public enum OverseerFeedbackSwitchTarget
{
    /// <summary>The production Overseer chat.</summary>
    Chat = 0,

    /// <summary>GnollBench candidates; each run records the value it launched with.</summary>
    GnollBench = 1
}
