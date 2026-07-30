using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }
        public DateTime? CreatedDate { get; set; }
        public long? ByteStart { get; set; }
        public long? ByteEnd { get; set; }
        public long? ByteLength { get; set; }

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
            Platform = line.Platform;
            PlatformVersion = line.PlatformVersion;
            Port = line.Port;
            PortVersion = line.PortVersion;
            PortBuild = line.PortBuild;
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
            StartTimeUTC = line.StartTimeUTC;
            EndTime = line.EndTime;
            EndTimeUTC = line.EndTimeUTC;
            StartingGender = line.StartingGender;
            StartingAlignment = line.StartingAlignment;
            FlagsBinary = line.FlagsBinary;
            Difficulty = line.Difficulty;
            Mode = line.Mode;
            Scoring = line.Scoring;
            Tournament = line.Tournament;
            DungeonCollapses = line.DungeonCollapses;
            SecurityLevel = line.SecurityLevel;
            Store = line.Store;
            PortSecurityLevel = line.PortSecurityLevel;
            ExperienceLevel = line.ExperienceLevel;

            var user = dbContext.Users.First(u => u.UserName == line.Name);
            AspNetUserId = user.Id;
            AspNetUser = (ApplicationUser)user;
        }

        public override string ToString(OutputMode outputMode)
        {
            return ToString(outputMode, Id);
        }
    }
}
