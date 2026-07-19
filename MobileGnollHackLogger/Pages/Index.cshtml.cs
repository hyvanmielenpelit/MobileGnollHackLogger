using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Globalization;

namespace MobileGnollHackLogger.Pages
{
    public class DashboardViewModel
    {
        public string? UserName { get; set; }
        public int Rank { get; set; }
        public string? LastActive { get; set; }
        public string? Joined { get; set; }
        public int GameLogCount { get; set; }
        public IList<GameLog> RecentQuestLogs { get; set; } = new List<GameLog>();
        public IList<BonesFileInfo> UploadedBonesFiles { get; set; } = new List<BonesFileInfo>();
    }

    public class BonesFileInfo
    {
        public string? OriginalFileName { get; set; }
        public int DifficultyLevel { get; set; }
        public string? DifficultyText { get; set; }
        public int Downloads { get; set; }
        public bool IsActive { get; set; }
    }

    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;

        public int GameLogCount { get; set; } = 0;
        public DashboardViewModel? Dashboard { get; set; }

        public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _dbContext = dbContext;
            _userManager = userManager;
        }

        public async Task OnGetAsync()
        {
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                var userId = _userManager.GetUserId(User);
                var userName = User.Identity.Name;
                if (!string.IsNullOrEmpty(userId))
                {
                    GameLogCount = await _dbContext.GameLog.CountAsync(gl => gl.AspNetUserId == userId);

                    if (GameLogCount > 0 || await _dbContext.BonesTransactions.AnyAsync(bt => bt.AspNetUserId == userId))
                    {
                        Dashboard = new DashboardViewModel { UserName = userName, GameLogCount = GameLogCount };

                        var myBestScore = await _dbContext.GameLog.Where(gl => gl.AspNetUserId == userId && gl.Scoring == "yes").MaxAsync(gl => (long?)gl.Points);
                        if (myBestScore.HasValue)
                        {
                            Dashboard.Rank = await _dbContext.GameLog.Where(gl => gl.Scoring == "yes" && gl.Points > myBestScore).CountAsync() + 1;
                        }

                        var lastLog = await _dbContext.GameLog.Where(gl => gl.AspNetUserId == userId).OrderByDescending(gl => gl.EndTimeUTC).FirstOrDefaultAsync();
                        if (lastLog != null)
                        {
                            var timeSpan = DateTimeOffset.UtcNow - lastLog.EndTimeUTCDate;
                            if (timeSpan.TotalMinutes < 60)
                                Dashboard.LastActive = $"{(int)timeSpan.TotalMinutes}m ago";
                            else if (timeSpan.TotalHours < 24)
                                Dashboard.LastActive = $"{(int)timeSpan.TotalHours}h ago";
                            else
                                Dashboard.LastActive = $"{(int)timeSpan.TotalDays}d ago";
                        }
                        else
                        {
                            Dashboard.LastActive = "Never";
                        }

                        var firstLog = await _dbContext.GameLog.Where(gl => gl.AspNetUserId == userId).OrderBy(gl => gl.EndTimeUTC).FirstOrDefaultAsync();
                        var firstBones = await _dbContext.BonesTransactions.Where(bt => bt.AspNetUserId == userId).OrderBy(bt => bt.Date).FirstOrDefaultAsync();

                        DateTime? joinedDate = null;
                        DateTime? logDate = firstLog?.CreatedDate ?? firstLog?.EndTimeUTCDate.UtcDateTime;
                        DateTime? bonesDate = firstBones?.Date;

                        if (logDate != null && bonesDate != null)
                        {
                            joinedDate = logDate < bonesDate ? logDate : bonesDate;
                        }
                        else if (logDate != null)
                        {
                            joinedDate = logDate;
                        }
                        else if (bonesDate != null)
                        {
                            joinedDate = bonesDate;
                        }

                        Dashboard.Joined = joinedDate?.ToString("MMM yyyy", CultureInfo.InvariantCulture) ?? "Unknown";

                        Dashboard.RecentQuestLogs = await _dbContext.GameLog
                            .Where(gl => gl.AspNetUserId == userId)
                            .OrderByDescending(gl => gl.EndTimeUTC)
                            .Take(100)
                            .ToListAsync();

                        var uploadTransactions = await _dbContext.BonesTransactions
                            .Where(bt => bt.AspNetUserId == userId && bt.Type == TransactionType.Upload)
                            .OrderByDescending(bt => bt.Date)
                            .Take(100)
                            .ToListAsync();

                        foreach (var tx in uploadTransactions)
                        {
                            bool isActive = await _dbContext.Bones.AnyAsync(b => b.Id == tx.BonesId);
                            int downloads = await _dbContext.BonesTransactions.CountAsync(bt => bt.BonesId == tx.BonesId && bt.Type == TransactionType.Download);
                            
                            string fileName = $"BONE_{userName}_{tx.BonesId}.dth";
                            if (isActive)
                            {
                                var originalName = await _dbContext.Bones.Where(b => b.Id == tx.BonesId).Select(b => b.OriginalFileName).FirstOrDefaultAsync();
                                if (!string.IsNullOrEmpty(originalName))
                                {
                                    fileName = originalName;
                                }
                            }

                            string diffText = "Unknown";
                            if (GnollHackHelper.Difficulties.ContainsKey(tx.DifficultyLevel))
                            {
                                diffText = GnollHackHelper.Difficulties[tx.DifficultyLevel];
                            }

                            Dashboard.UploadedBonesFiles.Add(new BonesFileInfo
                            {
                                OriginalFileName = fileName,
                                DifficultyLevel = tx.DifficultyLevel,
                                DifficultyText = diffText,
                                Downloads = downloads,
                                IsActive = isActive
                            });
                        }
                    }
                }
            }
        }
    }
}
