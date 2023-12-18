using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    //public enum StatisticsType { RoleStatistics, DifficultyStatistics };

    public class StatisticsModel : PageModel
    {
        private ApplicationDbContext _dbContext;

        public List<string> DisplayModes { get; private set; }

        public StatisticsModel(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
            Title = "Statistics";
            Mode = null;

            DisplayModes = new List<string>()
            {
                "normal",
                "modern",
                "casual",
                "reloadable"
            };
        }

        public string Title { get; set; }
        public string? Mode { get; set; }

        public IQueryable<GameLog>? GameLogs { get; set; }

        public IList<IGrouping<string?, GameLog>>? GroupByRole { get; set; }
        public IList<IGrouping<string?, GameLog>>? GroupByRoleAscended { get; set; }

        public async Task OnGetAsync(string? mode)
        {
            Mode = mode;
            GameLogs = _dbContext.GameLog.Where(gl => (mode == null || gl.Mode == mode) && gl.Mode != "debug" && gl.Mode != "explore" && gl.Scoring == "yes");
            GroupByRole = await GameLogs.GroupBy(gl => gl.Role).ToListAsync();
            GroupByRoleAscended = await GameLogs.Where(gl=>gl.DeathText == "ascended").GroupBy(gl => gl.Role).ToListAsync();
        }
    }
}
