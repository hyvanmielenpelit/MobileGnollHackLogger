using Microsoft.SqlServer.Server;
using System.Data;
using System.Diagnostics;

namespace MobileGnollHackLogger.Data
{
    public static class GnollHackHelper
    {
        public static List<string> Roles { get; private set; }
        public static Dictionary<int, string> Difficulties { get; private set; }
        public static Dictionary<string, string> Modes { get; private set; }

        static GnollHackHelper()
        {
            Roles = new List<string>()
            {
                "Arc",
                "Bar",
                "Cav",
                "Hea",
                "Kni",
                "Mon",
                "Pri",
                "Ran",
                "Rog",
                "Sam",
                "Tou",
                "Val",
                "Wiz"
            };

            Difficulties = new Dictionary<int, string>()
            {
                { -4, "Standard" },
                { -3, "Experienced" },
                { -2, "Adept" },
                { -1, "Veteran" },
                { 0, "Expert" },
                { 1, "Master" },
                { 2, "Grand Master" }
            };

            Modes = new Dictionary<string, string>()
            {
                { "normal", "Classic" },
                { "debug", "Wizard" },
                { "explore", "Explore" },
                { "casual", "Casual" },
                { "reloadable", "Reloadable" },
                { "modern", "Modern" }
            };
        }

        public static string? GetRoleText(string? roleCode, string? genderCode)
        {
            switch (roleCode)
            {
                case "Arc":
                    return "Archaeologist";
                case "Bar":
                    return "Barbarian";
                case "Cav":
                    if (genderCode == "Mal")
                    {
                        return "Caveman";
                    }
                    else if (genderCode == "Fem")
                    {
                        return "Cavewoman";
                    }
                    else
                    {
                        return "Caveman/Cavewoman";
                    }
                case "Hea":
                    return "Healer";
                case "Kni":
                    return "Knight";
                case "Mon":
                    return "Monk";
                case "Pri":
                    if (genderCode == "Mal")
                    {
                        return "Priest";
                    }
                    else if (genderCode == "Fem")
                    {
                        return "Priestess";
                    }
                    else
                    {
                        return "Priest/Priestess";
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
                    return roleCode;
            }
        }

        public static string? GetRaceText(string? raceCode)
        {
            switch (raceCode)
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
                    return raceCode;
            }

        }
    }
}
