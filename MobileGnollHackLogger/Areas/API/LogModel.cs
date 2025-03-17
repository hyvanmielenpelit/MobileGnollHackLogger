using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class LogModel : LoginInfoModel
    {
        public IFormFile? PlainTextDumpLog { get; set; }

        public IFormFile? HtmlDumpLog { get; set; }

        public string? XLogEntry { get; set; }
    }
}
