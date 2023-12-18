using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace MobileGnollHackLogger.Data
{
    /// <summary>
    /// 
    /// </summary>
    public class XLogFileLine
    {
        [MaxLength(32)]
        public string? Version { get; set; } //version

        public int EditLevel { get; set; } //edit

        [MaxLength(32)]
        public string? Platform { get; set; }

        public string? PlatformText
        {
            get
            {
                switch (Platform)
                {
                    case "android":
                        return "Android";
                    case "ios":
                        return "iOS";
                    default:
                        return Platform;
                }
            }
        }
        public string? PlatformLetter
        {
            get
            {
                switch (Platform)
                {
                    case "android":
                        return "a";
                    case "ios":
                        return "i";
                    case null:
                        return null;
                    case "":
                        return "";
                    default:
                        return "o";
                }
            }
        }

        [MaxLength(32)]
        public string? PlatformVersion { get; set; }

        [MaxLength(128)]
        public string? Port { get; set; }

        [MaxLength(32)]
        public string? PortVersion { get; set; }

        [MaxLength(32)]
        public string? PortBuild { get; set; }

        public long Points { get; set; } //points
        public int DeathDungeonNumber { get; set; } //deathdnum
        public int DeathLevel { get; set; } //deathlev
        public int MaxLevel { get; set; } //maxlvl
        public int HitPoints { get; set; } //hp
        public int MaxHitPoints { get; set; } //maxhp
        public int Deaths { get; set; } //deaths

        [MaxLength(8)]
        public string? DeathDateText { get; set; } //deathdate
        public DateTime? DeathDate 
        {
            get 
            {
                if(DeathDateText == null)
                {
                    return null;
                }
                return DateTime.ParseExact(DeathDateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None);
            }
        }

        [MaxLength(8)]
        public string? BirthDateText { get; set; } //birthdate

        public DateTime? BirthDate
        { 
            get
            {
                if(BirthDateText == null)
                {
                    return null;
                }
                return DateTime.ParseExact(BirthDateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None);
            }
        } 

        public int ProcessUserID { get; set; } //uid

        [MaxLength(3)]
        public string? Role { get; set; } //role

        public string? RoleText
        {
            get
            {
                switch(Role)
                {
                    case "Arc":
                        return "Archaeologist";
                    case "Bar":
                        return "Barbarian";
                    case "Cav":
                        if(Gender == "Mal")
                        {
                            return "Caveman";
                        }
                        else
                        {
                            return "Cavewoman";
                        }
                    case "Hea":
                        return "Healer";
                    case "Kni":
                        return "Knight";
                    case "Mon":
                        return "Monk";
                    case "Pri":
                        if (Gender == "Mal")
                        {
                            return "Priest";
                        }
                        else
                        {
                            return "Priestess";
                        }
                    case "Ran":
                        return "Ranger";
                    case "Rog":
                        return "Rogue";
                    case "Sam":
                        return "Samurai";
                    case "Tou":
                        return "Tourist";
                    case "Val":
                        return "Valkyrie";
                    case "Wiz":
                        return "Wizard";
                    default:
                        return Role;
                }
            }
        }

        [MaxLength(3)]
        public string? Race { get; set; } //race

        public string? RaceText
        {
            get
            {
                switch(Race)
                {
                    case "Hum":
                        return "Human";
                    case "Dwa":
                        return "Dwarf";
                    case "Elf":
                        return "Elf";
                    case "Gnl":
                        return "Gnoll";
                    case "Orc":
                        return "Orc";
                    default:
                        return Race;
                }
            }
        }

        [MaxLength(3)]
        public string? Gender { get; set; } //gender

        public string? GenderText
        {
            get
            {
                return ConvertGender(Gender);
            }
        }

        [MaxLength(3)]
        public string? Alignment { get; set; } //align

        public string? AlignmentText
        {
            get
            {
                return ConvertAlignment(Alignment);
            }
        }

        [MaxLength(32)]
        public string? Name { get; set; } //name

        [MaxLength(32)]
        public string? CharacterName { get; set; } //cname

        public string? DeathText { get; set; } //death

        public bool IsAscension
        {
            get
            {
                if(DeathDateText == "ascended")
                {
                    return true;
                }
                return false;
            }
        }

        public string? WhileText { get; set; } //while

        [MaxLength(50)]
        public string? ConductsBinary { get; set; } //conduct 0x904
        public int Turns { get; set; } //turns

        [MaxLength(50)]
        public string? AchievementsBinary { get; set; } //achieve 0xaf0e03

        public string? AchievementsText { get; set; } //achieveX

        public string[]? AchievementsArray
        { 
            get
            {
                return AchievementsText?.Split(',');
            }
        
        }

        public string? ConductsText { get; set; } //conductX
        public string[]? ConductsArray 
        {
            get 
            {
                return ConductsText?.Split(',');
            }
        }

        public long RealTime { get; set; } //realtime

        public TimeSpan RealTimeSpan 
        {
            get 
            {
                return TimeSpan.FromSeconds(RealTime);
            }        
        }

        public long StartTime { get; set; } //starttime
        public long StartTimeUTC { get; set; } //starttimeUTC

        public DateTimeOffset StartTimeUTCDate
        {
            get
            {
                return DateTimeOffset.FromUnixTimeSeconds(StartTimeUTC);
            }
        }

        public long EndTime { get; set; } //endtime

        public long EndTimeUTC { get; set; } //endtimeUTC

        public DateTimeOffset EndTimeUTCDate
        {
            get
            {
                return DateTimeOffset.FromUnixTimeSeconds(EndTimeUTC);
            }
        }

        [MaxLength(3)]
        public string? StartingGender { get; set; } //gender0

        public string? StartingGenderText
        {
            get
            {
                return ConvertGender(StartingGender);
            }
        }

        [MaxLength(3)]
        public string? StartingAlignment { get; set; } //align0

        public string? StartingAlignmentText
        {
            get
            {
                return ConvertAlignment(StartingAlignment);
            }
        }

        [MaxLength(50)]
        public string? FlagsBinary { get; set; } //flags

        public int Difficulty { get; set; } //difficulty

        public string DifficultyText
        {
            get
            {
                switch (Difficulty)
                {
                    case -4:
                        return "Standard";
                    case -3:
                        return "Experienced";
                    case -2:
                        return "Adept";
                    case -1:
                        return "Veteran";
                    case 0:
                        return "Expert";
                    case 1:
                        return "Master";
                    case 2:
                        return "Grand Master";
                    default:
                        return "Unknown: " + Difficulty;

                }
            }
        }

        [MaxLength(32)]
        public string? Mode { get; set; } //mode

        public string ModeText
        {
            get
            {
                switch (Mode)
                {
                    case "normal":
                        return "Classic";
                    case "debug":
                        return "Wizard";
                    case "explore":
                        return "Explore";
                    case "casual":
                        return "Casual";
                    case "reloadable":
                        return "Reloadable";
                    case "modern":
                        return "Modern";
                    default:
                        return "Unknown";
                }
            }
        }

        [MaxLength(32)]
        public string? Scoring { get; set; } //scoring

        public bool IsScoring
        {
            get
            {
                switch (Scoring)
                {
                    case "yes":
                        return true;
                    case "no": 
                        return false;
                    default:
                        return false;
                }
            }
        }

        public string ScoringText
        {
            get
            {
                if (IsScoring)
                {
                    return "Yes";
                }
                else
                {
                    return "No";
                }
            }
        }

        public int DungeonCollapses { get; set; } //collapse

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
                if(split.Length != 2)
                {
                    throw new Exception("XlogLine item '" + item + "' cannot be split into two parts using =.");
                }
                try
                {

                    switch (key)
                    {
                        case "id":
                            if (this is GameLog)
                            {
                                GameLog t = (GameLog)this;
                                t.Id = long.Parse(value);
                            }
                            break;
                        case "version":
                            Version = value;
                            break;
                        case "edit":
                            EditLevel = int.Parse(value);
                            break;
                        case "platform":
                            Platform = value;
                            break;
                        case "platformversion":
                            PlatformVersion = value;
                            break;
                        case "port":
                            Port = value;
                            break;
                        case "portversion":
                            PortVersion = value;
                            break;
                        case "portbuild":
                            PortBuild = value;
                            break;
                        case "points":
                            Points = long.Parse(value);
                            break;
                        case "deathdnum":
                            DeathDungeonNumber = int.Parse(value);
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
                            break;
                        case "birthdate":
                            BirthDateText = value;
                            break;
                        case "uid":
                            ProcessUserID = int.Parse(value);
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
                        case "cname":
                            CharacterName = value;
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
                            break;
                        case "conductX":
                            ConductsText = value;
                            break;
                        case "realtime":
                            RealTime = long.Parse(value);
                            break;
                        case "starttime":
                            StartTime = long.Parse(value);
                            break;
                        case "starttimeUTC":
                            StartTimeUTC = long.Parse(value);
                            break;
                        case "endtime":
                            EndTime = long.Parse(value);
                            break;
                        case "endtimeUTC":
                            EndTimeUTC = long.Parse(value);
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
                        case "collapse":
                            DungeonCollapses = int.Parse(value);
                            break;
                        default:
                            break;
                    }
                }
                catch(Exception ex)
                {
                    throw new Exception($"XLogFileLine item with key '{key}' and value '{value}' is invalid.", ex);
                }
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("version=").Append(Version).Append("\t");
            sb.Append("edit=").Append(EditLevel).Append("\t");
            sb.Append("platform=").Append(Platform).Append("\t");
            sb.Append("platformversion=").Append(PlatformVersion).Append("\t");
            sb.Append("port=").Append(Port).Append("\t");
            sb.Append("portversion=").Append(PortVersion).Append("\t");
            sb.Append("portbuild=").Append(PortBuild).Append("\t");
            sb.Append("points=").Append(Points).Append("\t");
            sb.Append("deathdnum=").Append(DeathDungeonNumber).Append("\t");
            sb.Append("deathlev=").Append(DeathLevel).Append("\t");
            sb.Append("maxlvl=").Append(MaxLevel).Append("\t");
            sb.Append("hp=").Append(HitPoints).Append("\t");
            sb.Append("maxhp=").Append(MaxHitPoints).Append("\t");
            sb.Append("deaths=").Append(Deaths).Append("\t");
            sb.Append("deathdate=").Append(DeathDateText).Append("\t");
            sb.Append("birthdate=").Append(BirthDateText).Append("\t");
            sb.Append("uid=").Append(ProcessUserID).Append("\t");
            sb.Append("role=").Append(Role).Append("\t");
            sb.Append("race=").Append(Race).Append("\t");
            sb.Append("gender=").Append(Gender).Append("\t");
            sb.Append("align=").Append(Alignment).Append("\t");
            sb.Append("name=").Append(Name).Append("\t");
            sb.Append("cname=").Append(CharacterName).Append("\t");
            sb.Append("death=").Append(DeathText).Append("\t");
            sb.Append("while=").Append(WhileText).Append("\t");
            sb.Append("conduct=").Append(ConductsBinary).Append("\t");
            sb.Append("turns=").Append(Turns).Append("\t");
            sb.Append("achieve=").Append(AchievementsBinary).Append("\t");
            sb.Append("achieveX=").Append(AchievementsText).Append("\t");
            sb.Append("conductX=").Append(ConductsText).Append("\t");
            sb.Append("realtime=").Append(RealTime).Append("\t");
            sb.Append("starttime=").Append(StartTime).Append("\t");
            sb.Append("starttimeUTC=").Append(StartTimeUTC).Append("\t");
            sb.Append("endtime=").Append(EndTime).Append("\t");
            sb.Append("endtimeUTC=").Append(EndTimeUTC).Append("\t");
            sb.Append("gender0=").Append(StartingGender).Append("\t");
            sb.Append("align0=").Append(StartingAlignment).Append("\t");
            sb.Append("flags=").Append(FlagsBinary).Append("\t");
            sb.Append("difficulty=").Append(Difficulty).Append("\t");
            sb.Append("mode=").Append(Mode).Append("\t");
            sb.Append("scoring=").Append(Scoring).Append("\t");
            sb.Append("collapse=").Append(DungeonCollapses);

            return sb.ToString();
        }

        private string? ConvertGender(string? genderCode)
        {
            switch(genderCode)
            {
                case "Mal":
                    return "Male";
                case "Fem":
                    return "Female";
                default:
                    return genderCode;
            }
        }

        private string? ConvertAlignment(string? alignmentCode)
        {
            switch (alignmentCode)
            {
                case "Law":
                    return "Lawful";
                case "Neu":
                    return "Neutral";
                case "Cha":
                    return "Chaotic";
                default:
                    return alignmentCode;
            }
        }
    }
}
