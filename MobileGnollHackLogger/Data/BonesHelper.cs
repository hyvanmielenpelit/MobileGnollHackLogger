using System.CodeDom;

namespace MobileGnollHackLogger.Data
{
    public class BonesVersionCompatibilityInfo
    {
        public uint Version { get; set; }
        public string? Label { get; set; }

        public BonesVersionCompatibilityInfo(uint version, string label)
        {
            Version = version;
            Label = label;
        }
    }

    public static class BonesHelper
    {
        public static List<BonesVersionCompatibilityInfo>? VersionCompatibilityList { get; set; }

        static BonesHelper()
        {
            VersionCompatibilityList = new List<BonesVersionCompatibilityInfo>();
        }
    }
}
