using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public class StatisticsModel : ModeModel
    {
        private ApplicationDbContext _dbContext;

        public StatisticsModel(ApplicationDbContext dbContext) : base()
        {
            _dbContext = dbContext;
            Title = "Statistics for All Gameplay Modes";
        }

        public IQueryable<GameLog>? GameLogs { get; set; }

        public IList<IGrouping<string?, GameLog>>? GroupByRole { get; set; }
        public IList<IGrouping<string?, GameLog>>? GroupByRoleAscended { get; set; }

        public async Task OnGetAsync(string? mode)
        {
            Mode = mode;
            GameLogs = _dbContext.GameLog.Where(gl => (mode == null || gl.Mode == mode) && gl.Mode != "debug" && gl.Mode != "explore" && gl.Scoring == "yes");
            GroupByRole = await GameLogs.GroupBy(gl => gl.Role).ToListAsync();
            GroupByRoleAscended = await GameLogs.Where(gl=>gl.DeathText == "ascended").GroupBy(gl => gl.Role).ToListAsync();

            switch (Mode)
            {
                case "normal":
                    Title = "Statistics for Classic Mode";
                    break;
                case "modern":
                    Title = "Statistics for Modern Mode";
                    break;
                case "casual":
                    Title = "Statistics for Casual Mode";
                    break;
                case "reloadable":
                    Title = "Statistics for Reloadable Mode";
                    break;
                default:
                    break;
            }
        }
    }
}
