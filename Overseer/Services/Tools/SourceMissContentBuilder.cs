using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Extends a <see cref="SourceCodeService"/> "no definition found" sentence with where the name
    /// does occur in the indexed source — or that it occurs nowhere — behind a one-line occurrence
    /// probe. Shared by the tools that surface a <see cref="SourceCodeService.GetFunctionBody"/> or
    /// <see cref="SourceCodeService.FindDefinition"/> miss.
    /// </summary>
    internal static class SourceMissContentBuilder
    {
        internal const string MissPrefix = "No definition found for '";
        private const int ProbeMaxResults = 3;
        private const int ProbeMaxResultLength = 1000;
        private const int MaxMissContentLength = 600;

        /// <summary>
        /// Extends <paramref name="plainMiss"/> with where <paramref name="name"/> does occur in the
        /// indexed <paramref name="repository"/> source — or that it occurs nowhere — followed by
        /// <paramref name="hitGuidance"/> or <paramref name="noHitGuidance"/> respectively. Capped in
        /// length and never throws: on any failure <paramref name="plainMiss"/> is returned unchanged.
        /// </summary>
        internal static string Build(
            SourceCodeService service,
            string plainMiss,
            string name,
            string repository,
            string hitGuidance,
            string noHitGuidance)
        {
            try
            {
                string probe = SafeProbe(service, name);
                bool hit = !string.IsNullOrWhiteSpace(probe);

                var sb = new StringBuilder(plainMiss);
                sb.Append(hit
                    ? $" '{name}' occurs in {SummarizeProbe(probe)}."
                    : $" '{name}' does not occur in the indexed {repository} source.");
                sb.Append(hit ? hitGuidance : noHitGuidance);

                // The guidance is the part worth paying for, so an oversized payload loses the probe detail first.
                string content = sb.ToString();
                if (content.Length > MaxMissContentLength)
                {
                    content = Truncate(plainMiss + (hit ? hitGuidance : noHitGuidance));
                }

                return content;
            }
            catch
            {
                return plainMiss;
            }
        }

        private static string Truncate(string content)
        {
            if (content.Length <= MaxMissContentLength) return content;

            int cut = content.LastIndexOf(' ', MaxMissContentLength - 1);
            if (cut < MaxMissContentLength / 2) cut = MaxMissContentLength - 1;
            return content.Substring(0, cut).TrimEnd() + "…";
        }

        /// <summary>Runs one bounded filenames_only occurrence probe, swallowing errors and "Error:"-prefixed content.</summary>
        private static string SafeProbe(SourceCodeService service, string probeQuery)
        {
            try
            {
                var result = service.SearchFiles(probeQuery, fileFilter: "", maxResults: ProbeMaxResults,
                    includeNetCode: false, maxResultLength: ProbeMaxResultLength, isRegex: false,
                    filenamesOnly: true, contextLines: 0, caseSensitive: false);

                return string.IsNullOrWhiteSpace(result) || result.StartsWith("Error:", System.StringComparison.Ordinal)
                    ? string.Empty
                    : result;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Turns a filenames_only "path (N matches)" listing into a short "N lines across path, path2" summary.</summary>
        private static string SummarizeProbe(string probeContent)
        {
            var files = new List<string>();
            int totalMatches = 0;
            foreach (var line in probeContent.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^(.*?)\s*\((\d+) matches?\)$");
                if (m.Success)
                {
                    totalMatches += int.Parse(m.Groups[2].Value);
                    files.Add(m.Groups[1].Value);
                }
            }

            return files.Count == 0
                ? "some lines"
                : $"{totalMatches} lines across {string.Join(", ", files.Take(3))}";
        }
    }
}
