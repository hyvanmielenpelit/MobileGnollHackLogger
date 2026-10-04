using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Overseer.Services;

/// <summary>
/// The current <see cref="CorpusContentFingerprint"/> of each of the five tool corpora, keyed
/// <c>gnollhackWiki</c>, <c>gnollhackSource</c>, <c>knowledgeBase</c>, <c>nethackWiki</c> and
/// <c>nethackSource</c> in that order. An entry is null while its service has not finished its
/// first index pass.
/// </summary>
public class CorpusIndexFingerprintProvider
{
    public static readonly IReadOnlyList<string> CorpusKeys = new[]
    {
        "gnollhackWiki", "gnollhackSource", "knowledgeBase", "nethackWiki", "nethackSource"
    };

    private readonly WikiService _wiki;
    private readonly SourceCodeService _source;
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly NetHackWikiService _netHackWiki;
    private readonly NetHackSourceCodeService _netHackSource;

    /// <summary>For a subclass that supplies its own <see cref="Snapshot"/>.</summary>
    protected CorpusIndexFingerprintProvider()
    {
        _wiki = null!;
        _source = null!;
        _knowledgeBase = null!;
        _netHackWiki = null!;
        _netHackSource = null!;
    }

    public CorpusIndexFingerprintProvider(
        WikiService wiki,
        SourceCodeService source,
        KnowledgeBaseService knowledgeBase,
        NetHackWikiService netHackWiki,
        NetHackSourceCodeService netHackSource)
    {
        _wiki = wiki;
        _source = source;
        _knowledgeBase = knowledgeBase;
        _netHackWiki = netHackWiki;
        _netHackSource = netHackSource;
    }

    /// <summary>
    /// Reads a run's <c>CorpusIndexFingerprintsJson</c>: null when the column is null or unreadable;
    /// otherwise every key of <see cref="CorpusKeys"/>, its value null when that index was not indexed yet.
    /// </summary>
    public static IReadOnlyDictionary<string, CorpusContentFingerprint?>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var result = new Dictionary<string, CorpusContentFingerprint?>();
            foreach (string key in CorpusKeys)
            {
                CorpusContentFingerprint? fingerprint = null;
                if (doc.RootElement.TryGetProperty(key, out var entry)
                    && entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("sha256", out var sha)
                    && sha.ValueKind == JsonValueKind.String)
                {
                    int fileCount = entry.TryGetProperty("fileCount", out var count) && count.TryGetInt32(out int n) ? n : 0;
                    DateTime indexedAt = entry.TryGetProperty("indexedAtUtc", out var at) && at.TryGetDateTime(out var t)
                        ? t.ToUniversalTime()
                        : default;
                    fingerprint = new CorpusContentFingerprint(sha.GetString()!, fileCount, indexedAt);
                }
                result[key] = fingerprint;
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public virtual IReadOnlyDictionary<string, CorpusContentFingerprint?> Snapshot()
        => new Dictionary<string, CorpusContentFingerprint?>
        {
            ["gnollhackWiki"] = _wiki.ContentFingerprint,
            ["gnollhackSource"] = _source.ContentFingerprint,
            ["knowledgeBase"] = _knowledgeBase.ContentFingerprint,
            ["nethackWiki"] = _netHackWiki.ContentFingerprint,
            ["nethackSource"] = _netHackSource.ContentFingerprint
        };
}
