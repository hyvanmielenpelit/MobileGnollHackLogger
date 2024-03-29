﻿using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class LogModel
    {
        public IFormFile? PlainTextDumpLog { get; set; }

        public IFormFile? HtmlDumpLog { get; set; }

        public string? XLogEntry { get; set; }

        [Required]
        [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9_]{0,30}$")]
        public string? UserName { get; set; }

        [Required]
        [MaxLength(63)]
        public string? Password { get; set; }

        [Required]
        public string? AntiForgeryToken { get; set; }
    }
}
