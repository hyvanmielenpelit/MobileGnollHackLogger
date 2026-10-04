using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Overseer.Services;

/// <summary>
/// What one tool corpus's index held: the SHA-256 of an ordinal-sorted <c>path:sha256</c> manifest
/// of the indexed files, the number of files, and when the index was swapped in.
/// </summary>
public sealed record CorpusContentFingerprint(string Sha256, int FileCount, DateTime IndexedAtUtc);

/// <summary>
/// Builds a <see cref="CorpusContentFingerprint"/> in the manifest shape of
/// <c>BenchmarkService.ComputeToolGuidesSha256</c>. Each file is hashed from the text the indexer
/// read (UTF-8 encoded), not from its raw bytes, so a BOM or an encoding-only change does not move
/// the fingerprint. <see cref="Add"/> is thread-safe.
/// </summary>
public sealed class CorpusContentFingerprintBuilder
{
    private readonly object _lock = new();
    private readonly List<(string RelPath, string FileHash)> _entries = new();

    /// <param name="relativePath">The file's path relative to the corpus root; <c>\</c> is normalized to <c>/</c>, case is kept.</param>
    /// <param name="indexedText">The text the indexer stored for the file.</param>
    public void Add(string relativePath, string indexedText)
    {
        string relPath = relativePath.Replace('\\', '/');
        string fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(indexedText))).ToLowerInvariant();
        lock (_lock)
        {
            _entries.Add((relPath, fileHash));
        }
    }

    public CorpusContentFingerprint Build()
    {
        List<(string RelPath, string FileHash)> entries;
        lock (_lock)
        {
            entries = new List<(string RelPath, string FileHash)>(_entries);
        }

        entries.Sort((a, b) =>
        {
            int byPath = string.Compare(a.RelPath, b.RelPath, StringComparison.Ordinal);
            return byPath != 0 ? byPath : string.Compare(a.FileHash, b.FileHash, StringComparison.Ordinal);
        });

        var sb = new StringBuilder();
        foreach (var (relPath, fileHash) in entries)
        {
            sb.Append(relPath).Append(':').Append(fileHash).Append('\n');
        }

        string manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        return new CorpusContentFingerprint(manifestHash, entries.Count, DateTime.UtcNow);
    }
}
