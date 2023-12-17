using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class BonesModel
    {
        public IFormFile? BonesFile { get; set; }

        public string? Command { get; set; }
        public string? Data { get; set; }

        public ulong VersionNumber { get; set; }
        public ulong VersionCompatibilityNumber { get; set; }

        public string? Platform { get; set; }
        public string? PlatformVersion { get; set; }
        public string? Port { get; set; }
        public string? PortVersion { get; set; }
        public string? PortBuild { get; set; }

        [Required]
        [RegularExpression(@"^[A-Za-z0-9_]{1,31}$")]
        public string? UserName { get; set; }

        [Required]
        [MaxLength(63)]
        public string? Password { get; set; }

        [Required]
        public string? AntiForgeryToken { get; set; }
    }
}
