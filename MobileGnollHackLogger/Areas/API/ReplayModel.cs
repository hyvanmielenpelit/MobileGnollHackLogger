using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MobileGnollHackLogger.Areas.API
{
    public class ReplayModel
    {
        [JsonIgnore]
        public IFormFile? ReplayFile { get; set; }

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
        [MaxLength(31)]
        [JsonIgnore]
        public string? UserName { get; set; }

        [Required]
        [MaxLength(63)]
        [JsonIgnore]
        public string? Password { get; set; }

        [Required]
        [JsonIgnore]
        public string? AntiForgeryToken { get; set; }

        public string GetJson()
        {
            return System.Text.Json.JsonSerializer.Serialize<ReplayModel>(this);
        }

        public string GetPlatform()
        {
            return Platform == null ? "Unknown" : Platform;
        }

        public string GetPlatformVersion()
        {
            return PlatformVersion == null ? "" : PlatformVersion;
        }

        public string GetPort()
        {
            return Port == null ? "" : Port;
        }

        public string GetPortVersion()
        {
            return PortVersion == null ? "" : PortVersion;
        }

        public string GetPortBuild()
        {
            return PortBuild == null ? "" : PortBuild;
        }
    }
}
