using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
        public DbSet<GameLog> GameLog { get; set; } = null!;
        public DbSet<Bones> Bones { get; set; } = null!;
        public DbSet<RequestInfo> RequestLogs { get; set; } = null!;
        public DbSet<BonesTransaction> BonesTransactions { get; set; } = null!;
        public DbSet<SaveFileTracking> SaveFileTrackings { get; set; } = null!;
        public DbSet<ChatSession> ChatSession { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessage { get; set; } = null!;
        public DbSet<ChatMessageAttachment> ChatMessageAttachment { get; set; } = null!;
        public DbSet<UserAiSettings> UserAiSettings { get; set; } = null!;
        public DbSet<UserAiApiKey> UserAiApiKeys { get; set; } = null!;
        public DbSet<UserAiModel> UserAiModels { get; set; } = null!;
        public DbSet<ChatMessageToolCall> ChatMessageToolCall { get; set; } = null!;

        public DbSet<Group> Groups { get; set; } = null!;
        public DbSet<UserGroup> UserGroups { get; set; } = null!;
        public DbSet<SystemAiApiConfiguration> SystemAiApiConfigurations { get; set; } = null!;
        public DbSet<UserSystemAiApiConfiguration> UserSystemAiApiConfigurations { get; set; } = null!;
        public DbSet<GroupSystemAiApiConfiguration> GroupSystemAiApiConfigurations { get; set; } = null!;
        public DbSet<SystemAiUsageLog> SystemAiUsageLogs { get; set; } = null!;
        public DbSet<SystemAiErrorLog> SystemAiErrorLogs { get; set; } = null!;
        public DbSet<BenchmarkSuite> BenchmarkSuites { get; set; } = null!;
        public DbSet<BenchmarkQuestion> BenchmarkQuestions { get; set; } = null!;
        public DbSet<BenchmarkRun> BenchmarkRuns { get; set; } = null!;
        public DbSet<BenchmarkRunAnswer> BenchmarkRunAnswers { get; set; } = null!;
        public DbSet<BenchmarkScoringProfile> BenchmarkScoringProfiles { get; set; } = null!;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<UserAiApiKey>()
                .HasIndex(k => new { k.AspNetUserId, k.Provider })
                .IsUnique();

            modelBuilder.Entity<UserGroup>()
                .HasKey(ug => new { ug.AspNetUserId, ug.GroupId });

            modelBuilder.Entity<UserGroup>()
                .HasOne(ug => ug.AspNetUser)
                .WithMany(u => u.UserGroups)
                .HasForeignKey(ug => ug.AspNetUserId)
                .HasPrincipalKey(u => u.Id);

            modelBuilder.Entity<UserGroup>()
                .HasOne(ug => ug.Group)
                .WithMany(g => g.UserGroups)
                .HasForeignKey(ug => ug.GroupId);

            modelBuilder.Entity<SystemAiUsageLog>()
                .HasOne(l => l.AspNetUser)
                .WithMany(u => u.SystemAiUsageLogs)
                .HasForeignKey(l => l.AspNetUserId)
                .HasPrincipalKey(u => u.Id);

            modelBuilder.Entity<SystemAiUsageLog>()
                .HasIndex(l => new { l.SystemAiApiConfigurationId, l.TimestampUtc })
                .IncludeProperties(l => new { l.AspNetUserId, l.RoleContext, l.InputTokens, l.OutputTokens });

            modelBuilder.Entity<SystemAiErrorLog>()
                .HasOne(l => l.DismissedByUser)
                .WithMany()
                .HasForeignKey(l => l.DismissedByUserId)
                .HasPrincipalKey(u => u.Id);

            modelBuilder.Entity<ChatSession>()
                .HasIndex(s => new { s.AspNetUserId, s.IsDeleted, s.LastMessageUtc })
                .IsDescending(false, false, true)
                .IncludeProperties(s => new { s.Title, s.IsGnollHackSession, s.IsPinned });

            modelBuilder.Entity<ChatSession>()
                .HasIndex(s => new { s.IsDeleted, s.DeletedUtc });

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => new { m.ChatSessionId, m.TimestampUtc })
                .IncludeProperties(m => new { m.Role, m.IsHidden, m.ModelUsed, m.ProviderUsed, m.TimeToFirstTokenMs, m.TotalDurationMs });

            modelBuilder.Entity<ChatMessageToolCall>()
                .HasIndex(tc => new { tc.ChatMessageId, tc.SortOrder });

            modelBuilder.Entity<BenchmarkSuite>()
                .HasIndex(s => s.Name)
                .IsUnique();

            modelBuilder.Entity<BenchmarkQuestion>()
                .HasOne(q => q.BenchmarkSuite)
                .WithMany(s => s.Questions)
                .HasForeignKey(q => q.BenchmarkSuiteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BenchmarkQuestion>()
                .HasOne(q => q.AssessedDifficultyModelConfiguration)
                .WithMany()
                .HasForeignKey(q => q.AssessedDifficultyModelConfigurationId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasOne(r => r.BenchmarkSuite)
                .WithMany(s => s.Runs)
                .HasForeignKey(r => r.BenchmarkSuiteId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasOne(r => r.TestedModelConfiguration)
                .WithMany()
                .HasForeignKey(r => r.TestedModelConfigurationId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasOne(r => r.AssessorModelConfiguration)
                .WithMany()
                .HasForeignKey(r => r.AssessorModelConfigurationId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasOne(r => r.StartedByUser)
                .WithMany()
                .HasForeignKey(r => r.StartedByUserId)
                .HasPrincipalKey(u => u.Id)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasOne(r => r.ScoringProfile)
                .WithMany()
                .HasForeignKey(r => r.ScoringProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<BenchmarkRun>()
                .HasIndex(r => r.StartedAtUtc);

            modelBuilder.Entity<BenchmarkScoringProfile>()
                .HasIndex(p => p.Name)
                .IsUnique();

            modelBuilder.Entity<BenchmarkScoringProfile>()
                .HasIndex(p => p.IsDefault)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");

            modelBuilder.Entity<BenchmarkRunAnswer>()
                .HasOne(a => a.BenchmarkRun)
                .WithMany(r => r.Answers)
                .HasForeignKey(a => a.BenchmarkRunId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BenchmarkRunAnswer>()
                .HasIndex(a => new { a.BenchmarkRunId, a.OrderIndex });

            modelBuilder.Entity<BenchmarkRunAnswer>()
                .HasOne(a => a.AssessedByModelConfiguration)
                .WithMany()
                .HasForeignKey(a => a.AssessedByModelConfigurationId)
                .OnDelete(DeleteBehavior.ClientSetNull);

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
            //modelBuilder.Entity<GameLog>()
            //    .HasIndex(gl => new { gl.ByteStart, gl.ByteEnd, gl.ByteLength });
        }

        public async Task<TopScoreNumberData> GetTopScoreNumberAsync(long databaseId, string? mode, string? death = null)
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
                .Where(gl => gl.AspNetUserId != null)
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
            long displayIndex = 0;
            long lastPoints = -1;
            long currentIndex = 0;
            long recordsInDraw = 1;
            foreach (var gameLog in gameLogsList)
            {
                if(gameLog.Points < lastPoints || lastPoints == -1)
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
