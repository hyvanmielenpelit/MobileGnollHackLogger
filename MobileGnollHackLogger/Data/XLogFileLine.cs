using System.Globalization;

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
        public DateTime DeathDate { get; set; } //deathdate
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
                        DeathDate = DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None);
                        break;
                    case "birthdate":
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
    }
}
