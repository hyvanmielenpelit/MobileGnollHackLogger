using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public enum RecentGamesMode { Games, Ascensions }

    public class RecentGamesModel : PageModel
    {
        ApplicationDbContext _dbContext;

        public RecentGamesModel(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
            Title = "Recent Games";
            RecentGamesMode = RecentGamesMode.Games;
        }

        public string Title { get; set; }
        public RecentGamesMode RecentGamesMode { get; set; }

        public IList<GameLog>? GameLogs { get; set; }

        public async Task OnGetAsync(string? death, string? mode)
        {
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
            }

            if(!string.IsNullOrEmpty(mode))
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == mode);
            }

            GameLogs = await gameLogs.ToListAsync();;
        }
    }
}
