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
