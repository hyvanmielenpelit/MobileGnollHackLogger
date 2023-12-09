using Microsoft.AspNetCore.Identity.UI.Services;

namespace MobileGnollHackLogger.Data
{
    public class EmailSender : Azure.Communication.Email.EmailClient, IEmailSender
    {
        public static string ConnectionString { get; set; }

        public EmailSender() : base(ConnectionString)
        {

        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            return base.SendAsync(Azure.WaitUntil.Completed, "DoNotReply@gnollhack.com", email, subject, htmlMessage);
        }
    }
}
