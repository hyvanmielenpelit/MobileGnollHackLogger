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
            if (death == "ascended")
            {
                Death = death;
            }
            if (mode == "normal" || mode == "modern")
            {
                Mode = mode;
            }

            IQueryable<GameLog> gameLogs = _dbContext.GameLog
                .OrderByDescending(gl => gl.Points)
                .Where(gl => gl.Scoring == "yes");

            if (!string.IsNullOrEmpty(Death) )
            {
                if(Death == "ascended")
                {
                    TopScoreMode = TopScoreMode.Ascensions;
                    Title = "Top Scores for Ascensions";
                    gameLogs = gameLogs.Where(gl => gl.DeathText == Death);
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

            switch (Mode)
            {
                case "normal":
                    Title += " in Classic Mode";
                    break;
                case "modern":
                    Title += " in Modern Mode";
                    break;
                default:
                    Title += " in All Modes";
                    break;
            }

            if (!string.IsNullOrEmpty(Mode))
            {
                gameLogs = gameLogs.Where(gl => gl.Mode == Mode);
            }

            gameLogs = gameLogs.Take(1000);
            GameLogs = await gameLogs.ToListAsync();
        }
    }
}
