using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Metadata;

namespace MobileGnollHackLogger.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<GameLog> GameLog {  get; set; }
        public DbSet<Bones> Bones { get; set; }
        public DbSet<RequestInfo> RequestLogs { get; set; }
        public DbSet<BonesTransaction> BonesTransactions { get; set; }

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
            modelBuilder.Entity<RequestInfo>()
                .Property(li => li.FirstDate)
                .HasDefaultValueSql("getutcdate()");
            modelBuilder.Entity<RequestInfo>()
                .Property(li => li.LastDate)
                .HasDefaultValueSql("getutcdate()");
            modelBuilder.Entity<BonesTransaction>()
                .Property(li => li.Date)
                .HasDefaultValueSql("getutcdate()");
            //modelBuilder.Entity<ApplicationUser>()
            //    .Property(u => u.IsBanned)
            //    .HasDefaultValue(0);
            //modelBuilder.Entity<ApplicationUser>()
            //    .Property(u => u.IsBonesBanned)
            //    .HasDefaultValue(0);
            //modelBuilder.Entity<ApplicationUser>()
            //    .Property(u => u.IsGameLogBanned)
            //    .HasDefaultValue(0);
        }
    }
}
