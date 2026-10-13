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

        /// <summary>When the account was registered.</summary>
        public DateTime? CreatedUtc { get; set; }

        /// <summary>True when <see cref="CreatedUtc"/> was backfilled; it is then an upper bound ("on or before").</summary>
        public bool CreatedUtcIsEstimate { get; set; }

        /// <summary>Last successful sign-in on any surface; written at most once an hour.</summary>
        public DateTime? LastSignInUtc { get; set; }

        /// <summary>When an administrator set <see cref="IsBanned"/>; null for legacy bans.</summary>
        public DateTime? BannedUtc { get; set; }

        /// <summary>Admin-only; never shown to the user.</summary>
        [MaxLength(512)]
        public string? BanReason { get; set; }

        /// <summary>The administrator who banned; attribution only, not a foreign key.</summary>
        [MaxLength(450)]
        public string? BannedByUserId { get; set; }

        /// <summary>The LockoutEnd an administrator set. The lockout is admin-set while LockoutEnd equals this and is in the future.</summary>
        public DateTimeOffset? AdminLockoutEndUtc { get; set; }

        /// <summary>Admin-only reason for <see cref="AdminLockoutEndUtc"/>.</summary>
        [MaxLength(512)]
        public string? AdminLockoutReason { get; set; }

        /// <summary>The administrator who locked the account out; attribution only, not a foreign key.</summary>
        [MaxLength(450)]
        public string? AdminLockoutByUserId { get; set; }

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
