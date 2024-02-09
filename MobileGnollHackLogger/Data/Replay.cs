using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    [PrimaryKey(nameof(Id))]
    public class Replay
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        [MaxLength(32)]
        public string? AccountName { get; set; }

        [MaxLength(32)]
        public string? CharacterName { get; set; }

        public DateTime Uploaded { get; set; }
        public DateTime Created { get; set; }

        public long ReplayStartTime { get; set; } //starttime
        public long ReplayStartTimeUTC { get; set; } //starttimeUTC

        public DateTimeOffset ReplayStartTimeUTCDate
        {
            get
            {
                return DateTimeOffset.FromUnixTimeSeconds(ReplayStartTimeUTC);
            }
        }

        public long ReplayEndTime { get; set; } //endtime

        public long ReplayEndTimeUTC { get; set; } //endtimeUTC

        public DateTimeOffset ReplayEndTimeUTCDate
        {
            get
            {
                return DateTimeOffset.FromUnixTimeSeconds(ReplayEndTimeUTC);
            }
        }

        [MaxLength(4096)]
        public string? FilePath { get; set; }

        [MaxLength(256)]
        public string? OriginalFileName { get; set; }

        public int DifficultyLevel { get; set; }

        [MaxLength(32)]
        public string? Platform { get; set; }

        [MaxLength(32)]
        public string? PlatformVersion { get; set; }

        [MaxLength(128)]
        public string? Port { get; set; }

        [MaxLength(32)]
        public string? PortVersion { get; set; }

        [MaxLength(32)]
        public string? PortBuild { get; set; }

        public ulong VersionNumber { get; set; }
        public ulong VersionCompatibilityNumber { get; set; }

        public Replay()
        {
            
        }
    }
}
