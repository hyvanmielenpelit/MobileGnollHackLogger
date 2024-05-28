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

        public ApplicationUser() : base()
        {
            
        }
    }
}
