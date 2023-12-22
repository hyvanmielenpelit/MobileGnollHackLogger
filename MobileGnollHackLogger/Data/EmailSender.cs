using Microsoft.AspNetCore.Identity.UI.Services;

namespace MobileGnollHackLogger.Data
{
    public class EmailSender : Azure.Communication.Email.EmailClient, IEmailSender
    {
        public static string? ConnectionString { get; set; }
        public static string? ConfirmAccountEmailHtml { get; set; }
        public static string? ForgotPasswordEmailHtml { get; set; }

        public Azure.WaitUntil WaitUntil { get; set; }

        public EmailSender() : base(ConnectionString)
        {
            if(string.IsNullOrEmpty(ConnectionString))
            {
                throw new Exception("ConnectionString is null");
            }

            WaitUntil = Azure.WaitUntil.Started;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            return base.SendAsync(WaitUntil, "DoNotReply@gnollhack.com", email, subject, htmlMessage);
        }
    }
}
