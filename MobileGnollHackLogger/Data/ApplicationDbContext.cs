using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<GameLog> GameLog {  get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }
}
