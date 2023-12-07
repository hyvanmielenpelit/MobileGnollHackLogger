using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace MobileGnollHackLogger.Data
{
    [PrimaryKey(nameof(Id))]
    public class GameLog : XLogFileLine
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("AspNetUser")]
        public string AspNetUserId { get; set; }
        public ApplicationUser AspNetUser { get; set; }

        public GameLog()
        {
            
        }

        public GameLog(string entry) : base(entry)
        {

        }

        public GameLog(XLogFileLine line, ApplicationDbContext dbContext)
        {
            Version = line.Version;
            EditLevel = line.EditLevel;
            Points = line.Points;
            DeathDungeonNumber = line.DeathDungeonNumber;
            DeathLevel = line.DeathLevel;
            MaxLevel = line.MaxLevel;
            HitPoints = line.HitPoints;
            MaxHitPoints = line.MaxHitPoints;
            Deaths = line.Deaths;
            DeathDateText = line.DeathDateText;
            BirthDateText = line.BirthDateText;
            ProcessUserID = line.ProcessUserID;
            Role = line.Role;
            Race = line.Race;
            Gender = line.Gender;
            Alignment = line.Alignment;
            Name = line.Name;
            CharacterName = line.CharacterName;
            DeathText = line.DeathText;
            WhileText = line.WhileText;
            ConductsBinary = line.ConductsBinary;
            Turns = line.Turns;
            AchievementsBinary = line.AchievementsBinary;
            AchievementsText = line.AchievementsText;
            ConductsText = line.ConductsText;
            RealTime = line.RealTime;
            StartTime = line.StartTime;
            EndTime = line.EndTime;
            StartingGender = line.StartingGender;
            StartingAlignment = line.StartingAlignment;
            FlagsBinary = line.FlagsBinary;
            Difficulty = line.Difficulty;
            Mode = line.Mode;
            Scoring = line.Scoring;
            DungeonCollapses = line.DungeonCollapses;

            var user = dbContext.Users.First(u => u.UserName == line.Name);
            AspNetUserId = user.Id;
            AspNetUser = (ApplicationUser)user;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("id=").Append(Id).Append("\t");
            sb.Append(base.ToString());
            return sb.ToString();
        }
    }
}
