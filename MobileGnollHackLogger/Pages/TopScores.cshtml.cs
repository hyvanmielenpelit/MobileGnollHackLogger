using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public enum TopScoreMode { Games, Ascensions }

    public class TopScoresModel : PageModel
    {
        ApplicationDbContext _dbContext;

        public TopScoresModel(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
            Title = "Recent Games";
            TopScoreMode = TopScoreMode.Games;
        }

        public string Title { get; set; }
        public TopScoreMode TopScoreMode { get; set; }

        public IList<GameLog>? GameLogs { get; set; }

        public void OnGet(string? death, string? mode)
        {
            IEnumerable<GameLog> gameLogs = _dbContext.GameLog
                .Take(1000)
                .OrderByDescending(gl => gl.Points)
                .Where(gl => gl.Scoring == "yes");

            if(!string.IsNullOrEmpty(death) )
            {
                gameLogs = gameLogs.Where(gl => gl.DeathText == death);

                if(death == "ascended")
                {
                    TopScoreMode = TopScoreMode.Ascensions;
                    Title = "Ascensions";
                }
            }

            if(!string.IsNullOrEmpty(mode) )
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == mode);
            }

            IList<GameLog> gameLogList = gameLogs.ToList();

            GameLogs = gameLogList;
        }
    }
}