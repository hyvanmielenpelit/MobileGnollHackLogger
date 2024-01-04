using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public enum TopScoreMode { Games, Ascensions }

    public class TopScoresModel : DeathModel
    {
        ApplicationDbContext _dbContext;

        public TopScoresModel(ApplicationDbContext dbContext) : base("TopScores")
        {
            _dbContext = dbContext;
            Title = "Top Scores for Games";
            TopScoreMode = TopScoreMode.Games;
        }

        public TopScoreMode TopScoreMode { get; set; }

        public IList<GameLog>? GameLogs { get; set; }

        public async Task OnGetAsync(string? death, string? mode)
        {
            Death = death;
            Mode = mode;

            IQueryable<GameLog> gameLogs = _dbContext.GameLog
                .Take(1000)
                .OrderByDescending(gl => gl.Points)
                .Where(gl => gl.Scoring == "yes");

            if(!string.IsNullOrEmpty(death) )
            {
                gameLogs = gameLogs.Where(gl => gl.DeathText == death);

                if(death == "ascended")
                {
                    TopScoreMode = TopScoreMode.Ascensions;
                    Title = "Top Scores for Ascensions";
                }
                else
                {
                    Title = "Top Scores for Games";
                }
            }
            else
            {
                Title = "Top Scores for Games";
            }

            if (!string.IsNullOrEmpty(mode))
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == mode);
            }

            GameLogs = await gameLogs.ToListAsync();

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
