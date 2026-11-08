using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public class StatisticsModel : ModeModel
    {
        private const int _minTurns = 1000;
        public int MinTurns { get {  return _minTurns; } }

        private const int _years = 2;
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

        public sealed record StatisticsCell(string? Role, int Difficulty, int Games, int Ascensions);

        // One row per role and difficulty that has qualifying games, including roles outside GnollHackHelper.Roles.
        public IReadOnlyList<StatisticsCell> Cells { get; private set; } = [];

        public long Games(string role, int difficulty) => Cells.Where(c => c.Role == role && c.Difficulty == difficulty).Sum(c => (long)c.Games);
        public long Ascensions(string role, int difficulty) => Cells.Where(c => c.Role == role && c.Difficulty == difficulty).Sum(c => (long)c.Ascensions);

        public long RoleGames(string role) => Cells.Where(c => c.Role == role).Sum(c => (long)c.Games);
        public long RoleAscensions(string role) => Cells.Where(c => c.Role == role).Sum(c => (long)c.Ascensions);

        // Every role counts here, the ones without a column of their own included.
        public long DifficultyGames(int difficulty) => Cells.Where(c => c.Difficulty == difficulty).Sum(c => (long)c.Games);
        public long DifficultyAscensions(int difficulty) => Cells.Where(c => c.Difficulty == difficulty).Sum(c => (long)c.Ascensions);

        public long TotalGames => Cells.Sum(c => (long)c.Games);
        public long TotalAscensions => Cells.Sum(c => (long)c.Ascensions);

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

            Cells = await _dbContext.GameLog.AsNoTracking()
                .Where(gl => gl.AspNetUserId != null && (Mode == null || gl.Mode == Mode) && gl.Mode != "debug" && gl.Mode != "explore" && gl.Scoring == "yes" && gl.Turns >= MinTurns && gl.CreatedDate != null && gl.CreatedDate >= CutOffDate)
                .GroupBy(gl => new { gl.Role, gl.Difficulty })
                .Select(g => new StatisticsCell(g.Key.Role, g.Key.Difficulty, g.Count(), g.Count(gl => gl.DeathText == "ascended")))
                .ToListAsync();
        }
    }
}
