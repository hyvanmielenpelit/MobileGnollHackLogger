using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Pages;
using System.Reflection.Metadata;

namespace MobileGnollHackLogger.Data
{
    public class TopScoreNumberData
    {
        public long DisplayIndex { get; set; }
        public long Index { get; set; }
    }

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

        public async Task<TopScoreNumberData> GetTopScoreNumberAsync(long databaseId, string? mode, string? death)
        {
            if(mode == null)
            {
                throw new ArgumentNullException("mode");
            }

            if(!GnollHackHelper.Modes.ContainsKey(mode))
            {
                throw new ArgumentOutOfRangeException("mode", "mode out of range");
            }

            IQueryable<GameLog> gameLogs = GameLog
                .OrderByDescending(gl => gl.Points)
                .Where(gl => gl.Scoring == "yes");

            //Only filter ascended
            if (death == "ascended")
            {
                gameLogs = gameLogs.Where(gl => gl.DeathText == death);
            }

            if (!string.IsNullOrEmpty(mode))
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == mode);
            }

            var gameLogsList = await gameLogs.ToListAsync();
            long displayIndex = 1;
            long lastPoints = -1;
            long currentIndex = 0;
            long recordsInDraw = 1;
            foreach (var gameLog in gameLogsList)
            {
                if(lastPoints == -1)
                {
                    lastPoints = gameLog.Points;
                }
                if(gameLog.Points < lastPoints)
                {
                    displayIndex += recordsInDraw;
                    recordsInDraw = 1;
                    lastPoints = gameLog.Points;
                }
                else if (gameLog.Points == lastPoints)
                {
                    recordsInDraw++;
                }
                currentIndex++;
                if(gameLog.Id == databaseId)
                {
                    break;
                }
            }

            return new TopScoreNumberData()
            {
                Index = currentIndex,
                DisplayIndex = displayIndex
            };
        }
    }
}
