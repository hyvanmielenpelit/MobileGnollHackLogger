using System;
using System.IO;
using System.Linq;

namespace Overseer.Services;

/// <summary>
/// Helper utilities for extracting Git repository metadata (such as HEAD SHA and current branch)
/// directly from the filesystem without spawning external git processes.
/// </summary>
public static class GitHelper
{
    /// <summary>
    /// Gets the current Git HEAD commit SHA for the repository at <paramref name="repoPath"/>.
    /// Supports direct branch refs, packed-refs, FETCH_HEAD, and detached HEAD states.
    /// Returns null if the repository or HEAD commit cannot be resolved.
    /// </summary>
    public static string? GetGitHeadSha(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        string gitDir = Path.Combine(repoPath, ".git");
        if (!Directory.Exists(gitDir))
        {
            return null;
        }

        string headFile = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headFile))
        {
            return null;
        }

        try
        {
            string headContent = File.ReadAllText(headFile).Trim();
            if (string.IsNullOrEmpty(headContent))
            {
                return null;
            }

            // Case 1: Symbolic reference (e.g. "ref: refs/heads/master")
            if (headContent.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                string refRelativePath = headContent.Substring("ref:".Length).Trim();
                
                // 1a. Check loose ref file (.git/refs/heads/master)
                string refFilePath = Path.Combine(gitDir, refRelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                if (File.Exists(refFilePath))
                {
                    string sha = File.ReadAllText(refFilePath).Trim();
                    if (!string.IsNullOrEmpty(sha))
                    {
                        return sha;
                    }
                }

                // 1b. Check packed-refs file (.git/packed-refs)
                string packedRefsPath = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packedRefsPath))
                {
                    var lines = File.ReadAllLines(packedRefsPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("#") || trimmed.StartsWith("^") || string.IsNullOrEmpty(trimmed))
                        {
                            continue;
                        }

                        // Format: <40-char-sha> <refPath>
                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && string.Equals(parts[1], refRelativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return parts[0];
                        }
                    }
                }

                // 1c. Fallback to FETCH_HEAD if ref was not found
                string fetchHeadPath = Path.Combine(gitDir, "FETCH_HEAD");
                if (File.Exists(fetchHeadPath))
                {
                    string fetchHead = File.ReadAllText(fetchHeadPath).Trim();
                    var parts = fetchHead.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length >= 40)
                    {
                        return parts[0];
                    }
                }

                return null;
            }

            // Case 2: Detached HEAD (contains the raw 40-character SHA)
            if (headContent.Length >= 40)
            {
                return headContent.Substring(0, 40);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the current branch name (e.g. "master", "main") for the repository at <paramref name="repoPath"/>.
    /// Returns empty string if HEAD is detached or cannot be resolved.
    /// </summary>
    public static string GetCurrentBranch(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return string.Empty;
        }

        string headFile = Path.Combine(repoPath, ".git", "HEAD");
        if (!File.Exists(headFile))
        {
            return string.Empty;
        }

        try
        {
            string headContent = File.ReadAllText(headFile).Trim();
            if (headContent.StartsWith("ref: refs/heads/", StringComparison.OrdinalIgnoreCase))
            {
                return headContent.Substring("ref: refs/heads/".Length).Trim();
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
