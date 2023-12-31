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

        public IList<GameLog>? GameLogs { get; set; }

        public async Task OnGetAsync(string? death, string? mode)
        {
            Death = death;
            Mode = mode;

            IQueryable<GameLog> gameLogs = _dbContext.GameLog
                .Take(1000)
                .OrderByDescending(gl => gl.EndTimeUTC);

            if(!string.IsNullOrEmpty(death) )
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

            GameLogs = await gameLogs.ToListAsync();;

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
