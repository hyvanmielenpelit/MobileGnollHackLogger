using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class LogModel
    {
        [Required]
        public IFormFile? PlainTextDumpLog { get; set; }

        [Required]
        public IFormFile? HtmlDumpLog { get; set; }

        [Required]
        public string? XLogEntry { get; set; }

        [Required]
        [RegularExpression(@"^[A-Za-z0-9_]{1,15}$")]
        public string? UserName { get; set; }

        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        public string? Password { get; set; }

        [Required]
        public string? AntiForgeryToken { get; set; }
    }
}
