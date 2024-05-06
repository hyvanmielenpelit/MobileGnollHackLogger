using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public enum RecentGamesMode { Games, Ascensions }

    public class RecentGamesModel : DeathModel
    {
        ApplicationDbContext _dbContext;

        public RecentGamesModel(ApplicationDbContext dbContext) : base("RecentGames")
        {
            _dbContext = dbContext;
            Title = "Recent Games";
            RecentGamesMode = RecentGamesMode.Games;
        }

        public RecentGamesMode RecentGamesMode { get; set; }

        public string? SubTitle { get; set; }

        public IList<GameLog>? GameLogs { get; set; }

        public async Task OnGetAsync(string? death, string? mode)
        {
            Death = death;
            Mode = mode;

            IQueryable<GameLog> gameLogs = _dbContext.GameLog
                .OrderByDescending(gl => gl.EndTimeUTC);

            if (!string.IsNullOrEmpty(death) )
            {
                if (death == "ascended")
                {
                    gameLogs = gameLogs.Where(gl => gl.DeathText == death);
                    RecentGamesMode = RecentGamesMode.Ascensions;
                    Title = "Recent Ascensions";
                }
                else
                {
                    Title = "Recent Games";
                }
            }
            else
            {
                Title = "Recent Games";
            }

            if(!string.IsNullOrEmpty(mode))
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == mode);
            }

            int totalCount = gameLogs.Count();

            gameLogs = gameLogs.Take(1000);

            GameLogs = await gameLogs.ToListAsync();

            int recentCount = GameLogs.Count();

            if (recentCount < totalCount)
            {
                SubTitle = $"Last {recentCount} of {totalCount} "
                    + (RecentGamesMode == RecentGamesMode.Ascensions ? "Ascension" : "Game")
                    + (totalCount != 1 ? "s" : "");
            }
            else
            {
                SubTitle = $"Last {recentCount} "
                    + (RecentGamesMode == RecentGamesMode.Ascensions ? "Ascension" : "Game")
                    + (recentCount != 1 ? "s" : "");
            }

            switch (Mode)
            {
                case "normal":
                    Title += " in Classic Mode";
                    break;
                case "modern":
                    Title += " in Modern Mode";
                    break;
                case "casual":
                    Title += " in Casual Mode";
                    break;
                case "reloadable":
                    Title += " in Reloadable Mode";
                    break;
                default:
                    Title += " in All Modes";
                    break;
            }

        }
    }
}
