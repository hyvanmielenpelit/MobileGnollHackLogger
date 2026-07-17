using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Data.Migrations;

namespace MobileGnollHackLogger.Pages
{
    public class StatisticsModel : ModeModel
    {
        private const int _minTurns = 1000;
        public int MinTurns { get {  return _minTurns; } }

        private const int _years = 1;
        public int Years { get { return _years; } }
        public string YearString
        {
            get
            {
                return _years + " Year" + ((_years == 1) ? "" : "s");
            }
        }

        private DateTime _cutoffDate = DateTime.Now.AddYears(-1 * _years);
        public DateTime CutOffDate {  get { return _cutoffDate; } }

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
            if (mode == "normal" || mode == "modern")
            {
                Mode = mode;
            }

            switch (mode)
            {
                case "normal":
                    Title = "Statistics for Classic Mode";
                    break;
                case "modern":
                    Title = "Statistics for Modern Mode";
                    break;
                default:
                    break;
            }

            GameLogs = _dbContext.GameLog.Where(gl => (Mode == null || gl.Mode == Mode) && gl.Mode != "debug" && gl.Mode != "explore" && gl.Scoring == "yes" && gl.Turns >= MinTurns && gl.CreatedDate != null && gl.CreatedDate >= CutOffDate);
            GroupByRole = await GameLogs.GroupBy(gl => gl.Role).ToListAsync();
            GroupByRoleAscended = await GameLogs.Where(gl=>gl.DeathText == "ascended").GroupBy(gl => gl.Role).ToListAsync();
        }
    }
}
