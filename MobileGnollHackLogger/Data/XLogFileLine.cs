using System.Globalization;
using System.Text;

namespace MobileGnollHackLogger.Data
{
    /// <summary>
    /// 
    /// </summary>
    public class XLogFileLine
    {
        public string? Version { get; set; } //version
        public long Points { get; set; } //points
        public int DeathDNum { get; set; } //deathdnum
        public int DeathLevel { get; set; } //deathlev
        public int MaxLevel { get; set; } //maxlvl
        public int HitPoints { get; set; } //hp
        public int MaxHitPoints { get; set; } //maxhp
        public int Deaths { get; set; } //deaths
        public string DeathDateText { get; set; }
        public DateTime DeathDate { get; set; } //deathdate
        public string BirthDateText { get; set; }
        public DateTime BirthDate { get; set; } //birthdate
        public int UserID { get; set; } //uid
        public string? Role { get; set; } //role
        public string? Race { get; set; } //race
        public string? Gender { get; set; } //gender
        public string? Alignment { get; set; } //align
        public string? Name { get; set; } //name
        public string? DeathText { get; set; } //death
        public string? WhileText { get; set; } //while
        public string? ConductsBinary { get; set; } //conduct 0x904
        public int Turns { get; set; } //turns
        public string? AchievementsBinary { get; set; } //achieve 0xaf0e03
        public string? AchievementsText { get; set; } //achieveX
        public string[]? AchievementsArray { get; set; }
        public string? ConductsText { get; set; } //conductX
        public string[]? ConductsArray { get; set; }
        public long RealTime { get; set; } //realtime
        public long StartTime { get; set; } //starttime
        public long EndTime { get; set; } //endtime
        public string? StartingGender { get; set; } //gender0
        public string? StartingAlignment { get; set; } //align0
        public string? FlagsBinary { get; set; } //flags
        public int Difficulty { get; set; } //difficulty
        public string? Mode { get; set; } //mode
        public string? Scoring { get; set; } //scoring

        public XLogFileLine()
        {
            
        }

        public XLogFileLine(string entry)
        {
            var array = entry.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in array)
            {
                var split = item.Split('=');
                var key = split[0];
                var value = split[1];
                switch(key) 
                {
                    case "version":
                        Version = value;
                        break;
                    case "points":
                        Points = long.Parse(value);
                        break;
                    case "deathdnum":
                        DeathDNum = int.Parse(value);
                        break;
                    case "deathlev":
                        DeathLevel = int.Parse(value);
                        break;
                    case "maxlvl":
                        MaxLevel = int.Parse(value);
                        break;
                    case "hp":
                        HitPoints = int.Parse(value);
                        break;
                    case "maxhp":
                        MaxHitPoints = int.Parse(value);
                        break;
                    case "deaths":
                        Deaths = int.Parse(value);
                        break;
                    case "deathdate":
                        DeathDateText = value;
                        DeathDate = DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None);
                        break;
                    case "birthdate":
                        BirthDateText = value;
                        BirthDate = DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None);
                        break;
                    case "uid":
                        UserID = int.Parse(value);
                        break;
                    case "role":
                        Role = value;
                        break;
                    case "race":
                        Race = value;
                        break;
                    case "gender":
                        Gender = value;
                        break;
                    case "align":
                        Alignment = value;
                        break;
                    case "name":
                        Name = value;
                        break;
                    case "death":
                        DeathText = value;
                        break;
                    case "while":
                        WhileText = value;
                        break;
                    case "conduct":
                        ConductsBinary = value;
                        break;
                    case "turns":
                        Turns = int.Parse(value);
                        break;
                    case "achieve":
                        AchievementsBinary = value;
                        break;
                    case "achieveX":
                        AchievementsText = value;
                        AchievementsArray = AchievementsText.Split(',');
                        break;
                    case "conductX":
                        ConductsText = value;
                        ConductsArray = ConductsText.Split(',');
                        break;
                    case "realtime":
                        RealTime = long.Parse(value);
                        break;
                    case "starttime":
                        StartTime = long.Parse(value);
                        break;
                    case "endtime":
                        EndTime = long.Parse(value);
                        break;
                    case "gender0":
                        StartingGender = value;
                        break;
                    case "align0":
                        StartingAlignment = value;
                        break;
                    case "flags":
                        FlagsBinary = value;
                        break;
                    case "difficulty":
                        Difficulty = int.Parse(value);
                        break;
                    case "mode":
                        Mode = value;
                        break;
                    case "scoring":
                        Scoring = value;
                        break;
                    default:
                        break;
                }
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("version=").Append(Version).Append("\t");
            sb.Append("points=").Append(Points).Append("\t");
            sb.Append("deathdnum=").Append(DeathDNum).Append("\t");
            sb.Append("deathlev=").Append(DeathLevel).Append("\t");
            sb.Append("maxlvl=").Append(MaxLevel).Append("\t");
            sb.Append("hp=").Append(HitPoints).Append("\t");
            sb.Append("maxhp=").Append(MaxHitPoints).Append("\t");
            sb.Append("deaths=").Append(Deaths).Append("\t");
            sb.Append("deathdate=").Append(DeathDateText).Append("\t");
            sb.Append("birthdate=").Append(BirthDateText).Append("\t");
            sb.Append("uid=").Append(UserID).Append("\t");
            sb.Append("role=").Append(Role).Append("\t");
            sb.Append("race=").Append(Race).Append("\t");
            sb.Append("gender=").Append(Gender).Append("\t");
            sb.Append("align=").Append(Alignment).Append("\t");
            sb.Append("name=").Append(Name).Append("\t");
            sb.Append("death=").Append(DeathText).Append("\t");
            sb.Append("while=").Append(WhileText).Append("\t");
            sb.Append("conduct=").Append(ConductsBinary).Append("\t");
            sb.Append("turns=").Append(Turns).Append("\t");
            sb.Append("achieve=").Append(AchievementsBinary).Append("\t");
            sb.Append("achieveX=").Append(AchievementsText).Append("\t");
            sb.Append("realtime=").Append(RealTime).Append("\t");
            sb.Append("starttime=").Append(StartTime).Append("\t");
            sb.Append("endtime=").Append(EndTime).Append("\t");
            sb.Append("gender0=").Append(StartingGender).Append("\t");
            sb.Append("align0=").Append(StartingAlignment).Append("\t");
            sb.Append("flags=").Append(FlagsBinary).Append("\t");
            sb.Append("difficulty=").Append(Difficulty).Append("\t");
            sb.Append("mode=").Append(Mode).Append("\t");
            sb.Append("scoring=").Append(Scoring);

            return sb.ToString();
        }
    }
}
