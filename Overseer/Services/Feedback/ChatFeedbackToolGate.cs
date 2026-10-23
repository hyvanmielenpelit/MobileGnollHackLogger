namespace Overseer.Services.Feedback;

/// <summary>
/// Whether a production chat turn declares <c>report_feedback</c>. The administrator's chat switch and the
/// user's own setting must both be on; ephemeral and confidential sessions never get it. The registry
/// further requires the user's server tools to be on.
/// </summary>
public static class ChatFeedbackToolGate
{
    public static bool IsOffered(bool adminSwitchOn, bool userSettingOn, bool isEphemeralSession, bool isConfidentialSession)
        => adminSwitchOn && userSettingOn && !isEphemeralSession && !isConfidentialSession;
}
