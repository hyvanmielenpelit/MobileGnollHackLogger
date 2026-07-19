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
        public IList<GameLog> RecentGameLogs { get; set; } = new List<GameLog>();

        public IList<(GameLog Log, int GlobalRank)> TopScores { get; set; } = new List<(GameLog, int)>();
        public string? Mode { get; set; }
        public string? Death { get; set; }
        public List<string> DisplayModes { get; set; } = new List<string> { "normal", "modern" };
        public TopScoreMode TopScoreMode { get; set; }
        public string ListMode { get; set; } = "recent";

        public string GetUrl(string? list, string? mode, string? death)
        {
            int n = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("/Index");
            if (!string.IsNullOrEmpty(list) || !string.IsNullOrEmpty(mode) || !string.IsNullOrEmpty(death))
            {
                sb.Append("?");
            }
            if (!string.IsNullOrEmpty(list))
            {
                sb.Append("list=").Append(list);
                n++;
            }
            if (!string.IsNullOrEmpty(mode))
            {
                if (n > 0) sb.Append("&");
                sb.Append("mode=").Append(mode);
                n++;
            }
            if (!string.IsNullOrEmpty(death))
            {
                if (n > 0) sb.Append("&");
                sb.Append("death=").Append(death);
            }
            return sb.ToString();
        }
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

        public async Task OnGetAsync(string? list, string? mode, string? death)
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

                        Dashboard.Death = death;
                        Dashboard.Mode = mode;
                        Dashboard.ListMode = string.IsNullOrEmpty(list) ? "recent" : list;

                        if (Dashboard.ListMode == "recent")
                        {
                            var recentQuery = _dbContext.GameLog.Where(gl => gl.AspNetUserId == userId);
                            
                            if (death == "ascended")
                            {
                                Dashboard.TopScoreMode = TopScoreMode.Ascensions;
                                recentQuery = recentQuery.Where(gl => gl.DeathText == "ascended");
                            }
                            else
                            {
                                Dashboard.TopScoreMode = TopScoreMode.Games;
                            }

                            if (mode == "normal" || mode == "modern")
                            {
                                recentQuery = recentQuery.Where(gl => gl.Mode == mode);
                            }

                            Dashboard.RecentGameLogs = await recentQuery
                                .OrderByDescending(gl => gl.EndTimeUTC)
                                .Take(100)
                                .ToListAsync();
                        }
                        else if (Dashboard.ListMode == "top")
                        {
                            IQueryable<GameLog> topScoresQuery = _dbContext.GameLog
                                .Where(gl => gl.AspNetUserId == userId && gl.Scoring == "yes")
                                .OrderByDescending(gl => gl.Points);

                            if (death == "ascended")
                            {
                                Dashboard.TopScoreMode = TopScoreMode.Ascensions;
                                topScoresQuery = topScoresQuery.Where(gl => gl.DeathText == "ascended");
                            }
                            else
                            {
                                Dashboard.TopScoreMode = TopScoreMode.Games;
                            }

                            if (mode == "normal" || mode == "modern")
                            {
                                topScoresQuery = topScoresQuery.Where(gl => gl.Mode == mode);
                            }

                            var myTopScores = await topScoresQuery.Take(100).ToListAsync();

                            foreach (var log in myTopScores)
                            {
                                IQueryable<GameLog> rankQuery = _dbContext.GameLog.Where(gl => gl.Scoring == "yes" && gl.Points > log.Points);
                                if (Dashboard.TopScoreMode == TopScoreMode.Ascensions)
                                {
                                    rankQuery = rankQuery.Where(gl => gl.DeathText == "ascended");
                                }
                                if (mode == "normal" || mode == "modern")
                                {
                                    rankQuery = rankQuery.Where(gl => gl.Mode == mode);
                                }
                                
                                int globalRank = await rankQuery.CountAsync() + 1;
                                Dashboard.TopScores.Add((log, globalRank));
                            }
                        }
                    }
                }
            }
        }
    }
}
