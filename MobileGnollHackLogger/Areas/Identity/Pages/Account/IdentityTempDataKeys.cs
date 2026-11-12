namespace MobileGnollHackLogger.Areas.Identity.Pages.Account;

/// <summary>
/// TempData keys of the Identity pages that receive an emailed link and redirect to their own path without its query.
/// </summary>
public static class IdentityTempDataKeys
{
    /// <summary>The decoded password reset code, kept until the reset succeeds.</summary>
    public const string ResetPasswordCode = "Identity.ResetPasswordCode";

    /// <summary>Set when a registration sent a confirmation email; RegisterConfirmation renders only when it is set.</summary>
    public const string RegistrationEmailSent = "Identity.RegistrationEmailSent";

    /// <summary>The outcome of ConfirmEmail, read once by the redirected request.</summary>
    public const string ConfirmEmailSucceeded = "Identity.ConfirmEmailSucceeded";

    /// <summary>The outcome of ConfirmEmailChange, read once by the redirected request.</summary>
    public const string ConfirmEmailChangeSucceeded = "Identity.ConfirmEmailChangeSucceeded";
}
