using Microsoft.AspNetCore.Identity;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<GameLog>? GameLogs { get; set; }

        public ApplicationUser() : base()
        {
            
        }
    }
}
