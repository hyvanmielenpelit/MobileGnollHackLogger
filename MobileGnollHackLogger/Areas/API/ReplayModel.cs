using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MobileGnollHackLogger.Areas.API
{
    public class ReplayModel
    {
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

    }
}
