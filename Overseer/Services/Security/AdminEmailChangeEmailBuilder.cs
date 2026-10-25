using System.Net;
using System.Text;

namespace Overseer.Services.Security;

/// <summary>
/// Builds the email sent to a new address after an administrator changes it: how to confirm it from the account
/// website's Resend email confirmation page, and why. Every interpolated value is HTML-encoded. Holds no token.
/// </summary>
public static class AdminEmailChangeEmailBuilder
{
    public const string ResendConfirmationPath = "/Identity/Account/ResendEmailConfirmation";

    public const string Subject = "Confirm the new email address of your GnollHack account";

    public static string ResendConfirmationUrl(string accountSiteBaseUrl) =>
        (accountSiteBaseUrl ?? string.Empty).TrimEnd('/') + ResendConfirmationPath;

    public static (string Subject, string Html) Build(string userName, string newEmail, string resendConfirmationUrl)
    {
        string name = E(userName);
        string email = E(newEmail);
        string url = E(resendConfirmationUrl);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E(Subject)).Append("</title></head>");
        sb.Append("<body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;\">");
        sb.Append("<h1>").Append(E(Subject)).Append("</h1>");
        sb.Append("<p>Hello ").Append(name).Append(",</p>");
        sb.Append("<p>A GnollHack administrator has changed the email address of your GnollHack account <b>").Append(name)
          .Append("</b> to ").Append(email).Append(".</p>");

        sb.Append("<h2>What you need to do</h2>");
        sb.Append("<p>You cannot sign in to the GnollHack website, Gnoll Overseer or GnollHack online features until you confirm this address. It takes a minute:</p>");
        sb.Append("<ol>");
        sb.Append("<li>Open the Resend email confirmation page: <a href=\"").Append(url).Append("\">").Append(url).Append("</a></li>");
        sb.Append("<li>Enter ").Append(email).Append(" and select Resend.</li>");
        sb.Append("<li>Open the confirmation link in the email that arrives, and sign in as usual.</li>");
        sb.Append("</ol>");

        sb.Append("<h2>Why this extra step</h2>");
        sb.Append("<p>This email cannot contain the confirmation link itself. For your security, confirmation links are created only by the GnollHack account website, and this change was made by an administrator in Gnoll Overseer. Confirming proves that this address is yours.</p>");

        sb.Append("<p>Your user name stays ").Append(name).Append(", and your password is unchanged.</p>");
        sb.Append("<p>If you did not ask for this change, do not follow these steps, and contact the GnollHack team.</p>");
        sb.Append("<p style=\"color:#555;\">This message was sent from an address that does not receive replies.</p>");
        sb.Append("</body></html>");
        return (Subject, sb.ToString());
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
