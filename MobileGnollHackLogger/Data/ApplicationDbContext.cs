using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Metadata;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<GameLog> GameLog {  get; set; }
        public DbSet<Bones> Bones { get; set; }
        public DbSet<LogInfo> Logs { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Bones>()
                .Property(b => b.Created)
                .HasDefaultValueSql("getutcdate()");
            modelBuilder.Entity<LogInfo>()
                .Property(li => li.CreationDate)
                .HasDefaultValueSql("getutcdate()");
        }
    }
}
