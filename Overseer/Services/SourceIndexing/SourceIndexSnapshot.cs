using System;
using System.Collections.Generic;

namespace Overseer.Services.SourceIndexing;

/// <summary>
/// One complete source index: the files, the constants and the parsed game data read in one pass.
/// Never changed after it is published; a new pass builds a new snapshot and replaces the reference.
/// </summary>
internal sealed class SourceIndexSnapshot
{
    /// <summary>The snapshot a service holds before its first pass publishes; <see cref="Generation"/> 0.</summary>
    public static SourceIndexSnapshot Empty { get; } = CreateEmpty();

    /// <summary>Repository-relative, forward-slashed path to the file, ordinal-ignore-case.</summary>
    public IReadOnlyDictionary<string, SourceCodeService.SourceDocument> Documents { get; init; } =
        new Dictionary<string, SourceCodeService.SourceDocument>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SourceCodeService.ConstantInfo> Constants { get; init; } =
        new Dictionary<string, SourceCodeService.ConstantInfo>(StringComparer.OrdinalIgnoreCase);

    public GameDataParser Parser { get; init; } = new();
    public ObjectsMacroResolver ItemResolver { get; init; } = null!;
    public CorpusContentFingerprint? Fingerprint { get; init; }

    /// <summary>The HEAD SHA read before the files were read; null outside a Git working tree.</summary>
    public string? HeadSha { get; init; }

    public string? Branch { get; init; }
    public DateTime IndexedAtUtc { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Increments with every publish of the owning service.</summary>
    public int Generation { get; init; }

    public long TotalLines { get; init; }

    /// <summary>Files left out for exceeding <c>MaxSourceFileSizeKB</c>.</summary>
    public IReadOnlyList<(string Path, long Bytes)> SkippedBySize { get; init; } = Array.Empty<(string Path, long Bytes)>();

    /// <summary>Files left out for lying under a dot-, <c>bin</c> or <c>obj</c> directory.</summary>
    public int SkippedExcludedDirectories { get; init; }

    private static SourceIndexSnapshot CreateEmpty()
    {
        var parser = new GameDataParser();
        return new SourceIndexSnapshot
        {
            Parser = parser,
            ItemResolver = new ObjectsMacroResolver(parser),
            Generation = 0
        };
    }
}
