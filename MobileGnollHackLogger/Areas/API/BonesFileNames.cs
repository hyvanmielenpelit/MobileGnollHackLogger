using System.Security;
using System.Text.RegularExpressions;

namespace MobileGnollHackLogger.Areas.API
{
    internal static class BonesFileNames
    {
        private const int MaxFileNameLength = 64;

        private static readonly Regex FileNamePattern = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]*\z",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // A bones file name as GnollHack's set_bonesfile_name makes it, e.g. "bon-2D0.15" or "bon3-1QBar.n".
        public static bool IsValid(string? fileName)
        {
            if (fileName == null || fileName.Length < 1 || fileName.Length > MaxFileNameLength)
            {
                return false;
            }

            if (!FileNamePattern.IsMatch(fileName) || fileName.Contains(".."))
            {
                return false;
            }

            return Path.GetFileName(fileName) == fileName;
        }

        // True when path resolves to a location inside root (not root itself).
        public static bool IsInsideDirectory(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            try
            {
                string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                return fullPath.Length > fullRoot.Length
                    && fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                || ex is PathTooLongException || ex is SecurityException)
            {
                return false;
            }
        }
    }
}
