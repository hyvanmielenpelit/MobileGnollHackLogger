using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Overseer.Services.SourceIndexing;

/// <summary>
/// The SHA-256 over makedefs' inputs, the number of input files hashed, and the compiled sources
/// that are missing from the tree.
/// </summary>
public sealed record MakedefsFingerprint(string Sha256, int FileCount, IReadOnlyList<string> MissingRequired);

/// <summary>
/// The files that decide what makedefs generates: the four sources <c>makedefs.vcxproj</c> compiles,
/// every header under <c>include/</c> except the generated outputs and <c>date.h</c>, every header
/// under <c>sys/winnt/</c>, <c>util/mdgrep.h</c>, the project file and <c>dirs.props</c>, plus the
/// build command itself.
/// </summary>
public static class MakedefsInputSet
{
    /// <summary>The headers makedefs writes into <c>include/</c>.</summary>
    public static readonly IReadOnlyList<string> GeneratedHeaders = new[] { "pm.h", "onames.h", "animoff.h", "animtotals.h" };

    /// <summary>The sources <c>makedefs.vcxproj</c> compiles.</summary>
    public static readonly IReadOnlyList<string> CompiledSources = new[] { "src/animdef.c", "util/makedefs.c", "src/monst.c", "src/objects.c" };

    private static readonly string[] SingleFiles = { "util/mdgrep.h", "win/win32/vs/makedefs.vcxproj", "win/win32/vs/dirs.props" };

    private static readonly HashSet<string> ExcludedIncludeNames =
        new(GeneratedHeaders.Append("date.h"), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The repository-relative, forward-slashed paths of the input files present under
    /// <paramref name="root"/>, sorted ordinal-ignore-case.
    /// </summary>
    public static IReadOnlyList<string> EnumerateInputs(string root)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string relative in CompiledSources.Concat(SingleFiles))
        {
            if (File.Exists(FullPath(root, relative))) paths.Add(relative);
        }

        AddHeaders(root, "include", paths, excludeGenerated: true);
        AddHeaders(root, "sys/winnt", paths, excludeGenerated: false);

        var sorted = paths.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    /// <summary>
    /// Hashes each input as its relative path, a NUL and its bytes, in <see cref="EnumerateInputs"/>
    /// order, followed by the build command.
    /// </summary>
    public static MakedefsFingerprint Compute(string root, string? buildCommand)
    {
        var inputs = EnumerateInputs(root);
        var missing = CompiledSources.Where(p => !File.Exists(FullPath(root, p))).ToList();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string relative in inputs)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(File.ReadAllBytes(FullPath(root, relative)));
        }

        hash.AppendData(Encoding.UTF8.GetBytes("\0command\0" + (buildCommand ?? string.Empty)));

        string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new MakedefsFingerprint(sha, inputs.Count, missing);
    }

    private static void AddHeaders(string root, string relativeDirectory, HashSet<string> paths, bool excludeGenerated)
    {
        string directory = FullPath(root, relativeDirectory);
        if (!Directory.Exists(directory)) return;

        foreach (string file in Directory.GetFiles(directory, "*.h", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith(".h", StringComparison.OrdinalIgnoreCase)) continue;
            if (excludeGenerated && ExcludedIncludeNames.Contains(name)) continue;
            paths.Add(relativeDirectory + "/" + name);
        }
    }

    private static string FullPath(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
