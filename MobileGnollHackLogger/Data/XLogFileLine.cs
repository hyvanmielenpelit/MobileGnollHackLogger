using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data.Migrations;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace MobileGnollHackLogger.Data
{
    public enum OutputMode { XLog, CSV };

    /// <summary>
    /// 
    /// </summary>
    public class XLogFileLine
    {
        private const char _separator = '\t';

        private static readonly List<string> _headerTexts = new List<string>()
        {
            "version",
            "edit",
            "platform",
            "platformversion",
            "port",
            "portversion",
            "portbuild",
            "points",
            "deathdnum",
            "deathlev",
            "maxlvl",
            "hp",
            "maxhp",
            "deaths",
            "deathdate",
            "birthdate",
            "uid",
            "role",
            "race",
            "gender",
            "align",
            "name",
            "cname",
            "death",
            "while",
            "conduct",
            "turns",
            "achieve",
            "achieveX",
            "conductX",
            "realtime",
            "starttime",
            "starttimeUTC",
            "endtime",
            "endtimeUTC",
            "gender0",
            "align0",
            "flags",
            "difficulty",
            "mode",
            "scoring",
            "tournament",
            "collapse",
            "seclvl",
            "store",
            "portseclvl"
        };

        
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
                    case "maccatalyst":
                        return "Mac Catalyst";
                    case "macos":
                        return "macOS";
                    case "winui":
                        return "Windows";
                    case "windows":
                        return "Windows";
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
                    case "maccatalyst":
                        return "m";
                    case "macos":
                        return "m";
                    case "winui":
                        return "w";
                    case "windows":
                        return "w";
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
                if (DeathDateText == null)
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
                if (BirthDateText == null)
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
                return GnollHackHelper.GetRoleText(Role, Gender);
            }
        }

        [MaxLength(3)]
        public string? Race { get; set; } //race

        public string? RaceText
        {
            get
            {
                return GnollHackHelper.GetRaceText(Race);
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
                if (DeathText == "ascended")
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
                if(GnollHackHelper.Difficulties.ContainsKey(Difficulty))
                {
                    return GnollHackHelper.Difficulties[Difficulty];
                }
                else
                {
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
                if (Mode != null && GnollHackHelper.Modes.ContainsKey(Mode))
                {
                    return GnollHackHelper.Modes[Mode];
                }
                else
                {
                    return "Unknown: " + Mode;
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
            get { return Scoring ?? "yes"; }
        }

        [MaxLength(32)]
        public string? Tournament { get; set; } //tournament

        public bool IsTournament
        {
            get
            {
                switch (Tournament)
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

        public string TournamentText
        {
            get { return Tournament ?? "no"; }
        }

        public int DungeonCollapses { get; set; } //collapse

        public int? SecurityLevel { get; set; }

        [MaxLength(32)]
        public string? Store { get; set; }

        public string? StoreText
        {
            get
            {
                return ConvertStore(Store);
            }
        }

        public string? StoreLetter
        {
            get
            {
                switch (Store)
                {
                    case "apple":
                        return "";
                    case "google":
                        return "";
                    case "microsoft":
                        return "m";
                    case "steam":
                        return "s";
                    case "steam-playtest":
                        return "t";
                    case "unpackaged":
                        return "u";
                    case "packaged":
                        return "p";
                    case "none":
                        return "n";
                    case "unknown":
                        return "?";
                    case null:
                        return null;
                    case "":
                        return "";
                    default:
                        return "";
                }
            }
        }

        public int? PortSecurityLevel { get; set; }

        public XLogFileLine()
        {

        }

        public XLogFileLine(string entry)
        {
            var trimmedEntry = entry.Trim();
            var array = trimmedEntry.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in array)
            {
                var split = item.Split('=');
                
                if (split.Length != 2)
                {
                    throw new Exception("XlogLine item '" + item + "' cannot be split into two parts using =.");
                }
                
                var key = split[0].Trim();
                var value = split[1].Trim();

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
                        case "tournament":
                            Tournament = value;
                            break;
                        case "collapse":
                            DungeonCollapses = int.Parse(value);
                            break;
                        case "seclvl":
                            SecurityLevel = int.Parse(value);
                            break;
                        case "store":
                            Store = value;
                            break;
                        case "portseclvl":
                            PortSecurityLevel = int.Parse(value);
                            break;
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"XLogFileLine item with key '{key}' and value '{value}' is invalid.", ex);
                }
            }
        }

        public override string ToString()
        {
            return ToXLogString();
        }

        public virtual string ToString(OutputMode outputMode)
        {
            return ToString(outputMode, null);
        }

        public string ToString(OutputMode outputMode, long? id)
        {
            StringBuilder sb = new StringBuilder();
            int fieldNum = 0;

            if(id != null)
            {
                AddField(sb, "id", id.Value, outputMode);
            }

            AddField(sb, fieldNum++, Version, outputMode);
            AddField(sb, fieldNum++, EditLevel, outputMode);
            AddField(sb, fieldNum++, Platform, outputMode);
            AddField(sb, fieldNum++, PlatformVersion, outputMode);
            AddField(sb, fieldNum++, Port, outputMode);
            AddField(sb, fieldNum++, PortVersion, outputMode);
            AddField(sb, fieldNum++, PortBuild, outputMode);
            AddField(sb, fieldNum++, Points, outputMode);
            AddField(sb, fieldNum++, DeathDungeonNumber, outputMode);
            AddField(sb, fieldNum++, DeathLevel, outputMode);
            AddField(sb, fieldNum++, MaxLevel, outputMode);
            AddField(sb, fieldNum++, HitPoints, outputMode);
            AddField(sb, fieldNum++, MaxHitPoints, outputMode);
            AddField(sb, fieldNum++, Deaths, outputMode);
            AddField(sb, fieldNum++, DeathDateText, outputMode);
            AddField(sb, fieldNum++, BirthDateText, outputMode);
            AddField(sb, fieldNum++, ProcessUserID, outputMode);
            AddField(sb, fieldNum++, Role, outputMode);
            AddField(sb, fieldNum++, Race, outputMode);
            AddField(sb, fieldNum++, Gender, outputMode);
            AddField(sb, fieldNum++, Alignment, outputMode);
            AddField(sb, fieldNum++, Name, outputMode);
            AddField(sb, fieldNum++, CharacterName, outputMode);
            AddField(sb, fieldNum++, DeathText, outputMode);
            AddField(sb, fieldNum++, WhileText, outputMode);
            AddField(sb, fieldNum++, ConductsBinary, outputMode);
            AddField(sb, fieldNum++, Turns, outputMode);
            AddField(sb, fieldNum++, AchievementsBinary, outputMode);
            AddField(sb, fieldNum++, AchievementsText, outputMode);
            AddField(sb, fieldNum++, ConductsText, outputMode);
            AddField(sb, fieldNum++, RealTime, outputMode);
            AddField(sb, fieldNum++, StartTime, outputMode);
            AddField(sb, fieldNum++, StartTimeUTC, outputMode);
            AddField(sb, fieldNum++, EndTime, outputMode);
            AddField(sb, fieldNum++, EndTimeUTC, outputMode);
            AddField(sb, fieldNum++, StartingGender, outputMode);
            AddField(sb, fieldNum++, StartingAlignment, outputMode);
            AddField(sb, fieldNum++, FlagsBinary, outputMode);
            AddField(sb, fieldNum++, Difficulty, outputMode);
            AddField(sb, fieldNum++, Mode, outputMode);
            AddField(sb, fieldNum++, ScoringText, outputMode);
            AddField(sb, fieldNum++, TournamentText, outputMode);
            AddField(sb, fieldNum++, DungeonCollapses, outputMode);

            //Note 2025-03-19
            //We are adding new fields only if they are non-null, not to break byte ranges in NetHack Scoreboard and Junethack
            AddField(sb, fieldNum++, SecurityLevel, outputMode, true);
            AddField(sb, fieldNum++, Store, outputMode, true);
            AddField(sb, fieldNum++, PortSecurityLevel, outputMode, true);

            return sb.ToString();
        }

        public virtual string ToCsvString()
        {
            return ToString(OutputMode.CSV);
        }

        public virtual string ToXLogString()
        {
            return ToString(OutputMode.XLog);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, string? value, OutputMode outputMode)
        {
            string? key = null;

            if (outputMode == OutputMode.XLog)
            {
                key = _headerTexts[fieldNum];
            }

            AddField(sbBody, key, value);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, string? value, OutputMode outputMode, bool skipIfNull)
        {
            if (skipIfNull && value == null)
            {
                if (outputMode == OutputMode.CSV)
                {
                    //Add empty cell to CSV
                    AddField(sbBody, fieldNum, "", outputMode);
                }
                else
                {
                    //Do nothing
                    return;
                }
            }
            else
            {
                AddField(sbBody, fieldNum, value, outputMode);
            }
        }

        private void AddField(StringBuilder sbBody, string key, string? value, OutputMode outputMode)
        {
            AddField(sbBody, outputMode == OutputMode.XLog ? key : null, value);
        }

        private void AddField(StringBuilder sbBody, string key, long? value, OutputMode outputMode)
        {
            AddField(sbBody, outputMode == OutputMode.XLog ? key : null, value.HasValue ? value.Value.ToString() : null);
        }

        private void AddField(StringBuilder sbBody, string? key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                value = value.Trim();
            }

            if (sbBody.Length > 0)
            {
                sbBody.Append(_separator);
            }

            if(!string.IsNullOrEmpty(key))
            {
                sbBody.Append(key).Append('=');
            }

            sbBody.Append(value);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, int? value, OutputMode outputMode)
        {
            string? val2 = value.HasValue ? value.ToString() : null;

            AddField(sbBody, fieldNum, val2, outputMode);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, int? value, OutputMode outputMode, bool skipIfNull)
        {
            string? val2 = value.HasValue ? value.ToString() : null;

            AddField(sbBody, fieldNum, val2, outputMode, skipIfNull);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, long? value, OutputMode outputMode)
        {
            string? val2 = value.HasValue ? value.ToString() : null;

            AddField(sbBody, fieldNum, val2, outputMode);
        }

        private void AddField(StringBuilder sbBody, int fieldNum, long? value, OutputMode outputMode, bool skipIfNull)
        {
            string? val2 = value.HasValue ? value.ToString() : null;

            AddField(sbBody, fieldNum, val2, outputMode, skipIfNull);
        }

        public static string GetCsvHeader(bool hasIdColumn = false)
        {
            StringBuilder sb = new StringBuilder();

            if(hasIdColumn)
            {
                sb.Append("id").Append(_separator);
            }

            for(int fieldNum = 0; fieldNum < _headerTexts.Count; fieldNum++)
            {
                sb.Append(_headerTexts[fieldNum]);
                if(fieldNum < _headerTexts.Count - 1)
                {
                    sb.Append(_separator);
                }
            }

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

        private string? ConvertStore(string? storeCode)
        {
            switch (storeCode)
            {
                case "google":
                    return "Google Play Store";
                case "apple":
                    return "Apple App Store";
                case "microsoft":
                    return "Microsoft Store";
                case "steam":
                    return "Steam";
                case "steam-playtest":
                    return "Steam Playtest";
                case "none":
                    return "None";
                default:
                    return storeCode;
            }
        }
    }
}
