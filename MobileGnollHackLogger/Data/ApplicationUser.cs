using Microsoft.AspNetCore.Identity;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<GameLog>? GameLogs { get; set; }
        public bool IsBanned { get; set; }
        public bool IsGameLogBanned { get; set; }
        public bool IsBonesBanned { get; set; }

        public ApplicationUser() : base()
        {
            
        }
    }
}
