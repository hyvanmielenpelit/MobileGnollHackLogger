using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<GameLog>? GameLogs { get; set; }

        public bool? IsBanned { get; set; }

        public bool? IsGameLogBanned { get; set; }

        public bool? IsBonesBanned { get; set; }

        [MaxLength(255)]
        [RegularExpression("^[a-zA-Z0-9_]$")]
        public string? JunetHackUserName { get; set; }

        /// <summary>When an administrator blocked the user from the Overseer AI chat; null when not blocked.</summary>
        public DateTime? OverseerBlockedUtc { get; set; }

        /// <summary>Shown to the user while blocked.</summary>
        [MaxLength(256)]
        public string? OverseerBlockReason { get; set; }

        /// <summary>The administrator who blocked; attribution only, not a foreign key.</summary>
        [MaxLength(450)]
        public string? OverseerBlockedByUserId { get; set; }

        public ICollection<UserGroup>? UserGroups { get; set; }
        public ICollection<UserSystemAiApiConfiguration>? UserSystemAiApiConfigurations { get; set; }
        public ICollection<SystemAiUsageLog>? SystemAiUsageLogs { get; set; }
        public ICollection<SecurityEvent>? SecurityEvents { get; set; }
        public ICollection<UserSystemModelOrder>? SystemModelOrders { get; set; }
        public ICollection<UserKeyAiUsageLog>? UserKeyAiUsageLogs { get; set; }
        public ICollection<ChatProviderAttempt>? ChatProviderAttempts { get; set; }
        public ICollection<OverseerDailyActivity>? DailyActivities { get; set; }

        public ApplicationUser() : base()
        {
            
        }
    }
}
