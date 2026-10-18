using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GnollHackServer.Data.Legal;

/// <summary>A Terms document as read from the server's wiki clone.</summary>
/// <param name="Markdown">The page text with <c>[[wiki links]]</c> rewritten to absolute Markdown links.</param>
/// <param name="ContentSha256">Lower-case hex SHA-256 of the raw file bytes.</param>
/// <param name="Version">Null when neither the document nor Git gives one.</param>
/// <param name="VersionLabel">The version as the document writes it, for display only.</param>
/// <param name="VersionOrigin"><c>document</c>, <c>git</c>, or null.</param>
public sealed record LegalDocument(
    string Key,
    string Title,
    string Markdown,
    string ContentSha256,
    string? Version,
    string? VersionLabel,
    string? VersionOrigin,
    string WikiUrl,
    string? PrivacyPolicyUrl);

/// <summary>
/// Reads the Terms documents from the wiki folder on disk (<c>WikiPath</c>), not from the wiki
/// retrieval index, and derives each one's version and content hash. A file is re-read only when
/// its last-write time or length changes.
/// </summary>
public sealed partial class LegalDocumentReader
{
    public const int VersionLineSearchLimit = 20;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);

    private readonly LegalDocumentOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LegalDocumentReader>? _logger;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private sealed record CacheEntry(string Path, DateTime LastWriteUtc, long Length, LegalDocument Document);

    [GeneratedRegex(@"\[\[([^\]\|]+?)(?:\|([^\]]+?))?\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex WikiLink();

    public LegalDocumentReader(IOptions<LegalDocumentOptions> options, IConfiguration configuration, ILogger<LegalDocumentReader>? logger = null)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>The document keys this host serves.</summary>
    public IReadOnlyCollection<string> DocumentKeys => _options.Documents.Keys;

    public bool IsConfigured(string key) => _options.Documents.ContainsKey(key);

    /// <summary>The wiki URL of a configured document's page, or null.</summary>
    public string? GetWikiUrl(string key)
        => _options.Documents.TryGetValue(key, out var definition) ? PageUrl(definition.WikiPageName) : null;

    /// <summary>The wiki URL of a configured document's privacy policy, or null.</summary>
    public string? GetPrivacyPolicyUrl(string key)
        => _options.Documents.TryGetValue(key, out var definition) && !string.IsNullOrWhiteSpace(definition.PrivacyPolicyPageName)
            ? PageUrl(definition.PrivacyPolicyPageName)
            : null;

    /// <summary>The page title of a configured document, or null.</summary>
    public string? GetTitle(string key)
        => _options.Documents.TryGetValue(key, out var definition) ? definition.WikiPageName : null;

    /// <summary>The absolute wiki URL of a page.</summary>
    public string PageUrl(string pageName)
    {
        string baseUrl = _options.WikiBaseUrl.EndsWith('/') ? _options.WikiBaseUrl : _options.WikiBaseUrl + "/";
        return baseUrl + Uri.EscapeDataString(pageName) + ".md";
    }

    public Task<LegalDocument?> GetAsync(string key) => Task.FromResult(Get(key));

    /// <summary>The document, or null when it is not configured or its file cannot be read.</summary>
    public LegalDocument? Get(string key)
    {
        if (!_options.Documents.TryGetValue(key, out var definition) || string.IsNullOrWhiteSpace(definition.WikiPageName))
        {
            return null;
        }

        string? wikiPath = _configuration["WikiPath"];
        if (string.IsNullOrWhiteSpace(wikiPath))
        {
            return null;
        }

        string fileName = definition.WikiPageName + ".md";
        string path = Path.Combine(wikiPath, fileName);

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)
                    && cached.Path == path
                    && cached.LastWriteUtc == info.LastWriteTimeUtc
                    && cached.Length == info.Length)
                {
                    return cached.Document;
                }
            }

            byte[] bytes = File.ReadAllBytes(path);
            var document = Build(key, definition, wikiPath, fileName, bytes);

            lock (_lock)
            {
                _cache[key] = new CacheEntry(path, info.LastWriteTimeUtc, info.Length, document);
            }

            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "The Terms document {Key} could not be read from {Path}", key, path);
            return null;
        }
    }

    private LegalDocument Build(string key, LegalDocumentDefinition definition, string wikiPath, string fileName, byte[] bytes)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string text = Decode(bytes);

        string? version = null, label = null, origin = null;

        if (_options.VersionSource == LegalVersionSource.DocumentDate)
        {
            (version, label) = ReadVersionLine(key, text);
            if (version != null)
            {
                origin = "document";
            }
        }

        if (version == null)
        {
            version = ReadGitDate(wikiPath, fileName);
            if (version != null)
            {
                origin = "git";
                label = LegalVersion.FormatLabel(version);
                if (_options.VersionSource == LegalVersionSource.DocumentDate)
                {
                    _logger?.LogWarning("The Terms document {Key} has no readable 'Last updated' line; its version {Version} comes from Git", key, version);
                }
            }
        }

        return new LegalDocument(
            key,
            definition.WikiPageName,
            RewriteWikiLinks(text),
            hash,
            version,
            version == null ? null : label,
            origin,
            PageUrl(definition.WikiPageName),
            string.IsNullOrWhiteSpace(definition.PrivacyPolicyPageName) ? null : PageUrl(definition.PrivacyPolicyPageName));
    }

    private static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        ReadOnlySpan<byte> bom = Encoding.UTF8.Preamble;
        if (span.StartsWith(bom))
        {
            span = span[bom.Length..];
        }

        return Encoding.UTF8.GetString(span);
    }

    private (string? Version, string? Label) ReadVersionLine(string key, string text)
    {
        using var reader = new StringReader(text);
        for (int i = 0; i < VersionLineSearchLimit; i++)
        {
            string? line = reader.ReadLine();
            if (line == null)
            {
                break;
            }

            switch (LegalVersion.TryParseLastUpdatedLine(line, out string? version, out string? label))
            {
                case LegalVersion.LineMatch.Parsed:
                    return (version, label);
                case LegalVersion.LineMatch.Unparsable:
                    _logger?.LogWarning("The Terms document {Key} has a 'Last updated' line whose date '{Text}' is not in the form '18 October 2026' or '2026-10-18'", key, label);
                    return (null, null);
            }
        }

        return (null, null);
    }

    /// <summary>The <c>yyyy-MM-dd</c> date of the last commit that touched the file, or null on any failure.</summary>
    private string? ReadGitDate(string wikiPath, string fileName)
    {
        try
        {
            var startInfo = new ProcessStartInfo(string.IsNullOrWhiteSpace(_options.GitExecutable) ? "git" : _options.GitExecutable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in new[] { "-C", wikiPath, "log", "-1", "--format=%cs", "--", fileName })
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(GitTimeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                _logger?.LogWarning("git log timed out reading the date of {File}", fileName);
                return null;
            }

            if (process.ExitCode != 0)
            {
                _logger?.LogWarning("git log failed reading the date of {File}: {Error}", fileName, stderr.Result.Trim());
                return null;
            }

            string output = stdout.Result.Trim();
            return LegalVersion.TryParse(output, out var date, out _) ? LegalVersion.Format(date) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger?.LogWarning(ex, "git could not be run to read the date of {File}", fileName);
            return null;
        }
    }

    /// <summary>
    /// Rewrites Gollum links, <c>[[Page]]</c> and <c>[[label|Page]]</c>, to absolute Markdown links.
    /// Nothing else is touched.
    /// </summary>
    public string RewriteWikiLinks(string markdown)
        => WikiLink().Replace(markdown, m =>
        {
            bool hasLabel = m.Groups[2].Success;
            string page = (hasLabel ? m.Groups[2].Value : m.Groups[1].Value).Trim();
            string label = m.Groups[1].Value.Trim();
            return $"[{label}]({PageUrl(page)})";
        });
}
