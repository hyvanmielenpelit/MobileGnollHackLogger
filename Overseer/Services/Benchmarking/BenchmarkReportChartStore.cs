namespace Overseer.Services.Benchmarking;

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Models;

/// <summary>
/// Report-document chart images on disk, one folder per document under
/// <c>Benchmark:ReportPack:ChartsDataLocation</c>: <c>&lt;root&gt;/&lt;documentId&gt;/manifest.json</c> plus
/// <c>&lt;figureKey&gt;.&lt;named|anonymized&gt;.png</c>. Every path is built from a numeric id, a known figure
/// key and a fixed naming word, and must resolve inside the root. A document's set is replaced whole,
/// through a staging folder, under a per-document lock.
/// </summary>
public class BenchmarkReportChartStore
{
    public const string ConfigurationKey = "Benchmark:ReportPack:ChartsDataLocation";

    public const string NotConfiguredMessage =
        "Chart storage is not configured. Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder.";

    public const string Named = "named";
    public const string Anonymized = "anonymized";

    public const string ManifestFileName = "manifest.json";
    public const string StagingFolderName = ".staging";

    public const int MaxCharts = 16;
    public const int MaxTitleChars = 200;
    public const int MaxCaptionChars = 500;
    public const int MaxAltTextChars = 1000;
    public const int MinDimensionPx = 320;
    public const int MaxDimensionPx = 4096;
    public const int MaxPngBytes = 4 * 1024 * 1024;

    private const string DataUrlPrefix = "data:image/png;base64,";

    /// <summary>The longest base64 text that can decode to <see cref="MaxPngBytes"/>, whitespace aside.</summary>
    private const int MaxBase64Chars = (MaxPngBytes + 2) / 3 * 4;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly Regex SettingsHashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<BenchmarkReportChartStore> _logger;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _documentLocks = new();

    /// <summary>The full root path without a trailing separator; null when unconfigured.</summary>
    private readonly string? _root;

    /// <summary><see cref="_root"/> with one trailing separator, the prefix every built path must carry.</summary>
    private readonly string? _rootPrefix;

    private int _unconfiguredWarningLogged;

    public BenchmarkReportChartStore(IConfiguration configuration, ILogger<BenchmarkReportChartStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? NullLogger<BenchmarkReportChartStore>.Instance;

        string configured = configuration[ConfigurationKey] ?? string.Empty;
        if (IsUsableRoot(configured))
        {
            string full = Path.GetFullPath(configured.Trim());
            string trimmed = Path.TrimEndingDirectorySeparator(full);
            _root = trimmed;
            _rootPrefix = Path.EndsInDirectorySeparator(trimmed) ? trimmed : trimmed + Path.DirectorySeparatorChar;
        }
    }

    /// <summary>True when the configured root is an absolute path.</summary>
    public bool IsConfigured => _root != null;

    /// <summary>The full root path, or null when unconfigured.</summary>
    public string? RootPath => _root;

    /// <summary>A chart root setting is usable when it is a fully qualified path.</summary>
    public static bool IsUsableRoot(string? value)
        => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value.Trim());

    public static bool IsKnownNaming(string? naming) => naming == Named || naming == Anonymized;

    /// <summary>The file name of one chart image, for a figure key and naming already checked.</summary>
    public static string ChartFileName(string figureKey, string naming) => figureKey + "." + naming + ".png";

    // ---------------------------------------------------------------- validation

    /// <summary>
    /// Checks and decodes one upload's charts. Refuses the whole upload, with a message naming the
    /// chart and the problem, on the first chart that fails.
    /// </summary>
    public static IReadOnlyList<ValidatedChart> ValidateCharts(IReadOnlyList<ReportDocumentChartUpload>? uploads)
    {
        if (uploads == null || uploads.Count == 0)
        {
            throw new ChartStoreException("No charts were sent. To remove a document's charts, delete them instead.");
        }

        if (uploads.Count > MaxCharts)
        {
            throw new ChartStoreException($"{uploads.Count} charts were sent; at most {MaxCharts} are allowed.");
        }

        var seen = new HashSet<(string FigureKey, string Naming)>();
        var validated = new List<ValidatedChart>(uploads.Count);
        string? settingsHash = null;

        for (int i = 0; i < uploads.Count; i++)
        {
            var upload = uploads[i];
            if (upload == null)
            {
                throw new ChartStoreException($"Chart {i + 1} is empty.");
            }

            string figureKey = upload.FigureKey ?? string.Empty;
            string naming = upload.Naming ?? string.Empty;

            if (figureKey.Length == 0 || !BenchmarkReportChartPlacement.IsKnown(figureKey))
            {
                throw new ChartStoreException($"Chart {i + 1} has an unknown figure key \"{Clip(figureKey)}\".");
            }

            string label = $"Chart {i + 1} ({figureKey}, {Clip(naming)})";

            if (!IsKnownNaming(naming))
            {
                throw new ChartStoreException($"{label} has the naming \"{Clip(naming)}\"; it must be \"{Named}\" or \"{Anonymized}\".");
            }

            if (!seen.Add((figureKey, naming)))
            {
                throw new ChartStoreException($"{label} is sent more than once.");
            }

            string title = upload.Title ?? string.Empty;
            string caption = upload.Caption ?? string.Empty;
            string altText = upload.AltText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(altText))
            {
                throw new ChartStoreException($"{label} has no alt text.");
            }

            if (title.Length > MaxTitleChars)
            {
                throw new ChartStoreException($"{label} has a title of {title.Length} characters; the limit is {MaxTitleChars}.");
            }

            if (caption.Length > MaxCaptionChars)
            {
                throw new ChartStoreException($"{label} has a caption of {caption.Length} characters; the limit is {MaxCaptionChars}.");
            }

            if (altText.Length > MaxAltTextChars)
            {
                throw new ChartStoreException($"{label} has alt text of {altText.Length} characters; the limit is {MaxAltTextChars}.");
            }

            string hash = upload.SettingsHash ?? string.Empty;
            if (!SettingsHashPattern.IsMatch(hash))
            {
                throw new ChartStoreException($"{label} has a settings hash that is not 64 hexadecimal characters.");
            }

            hash = hash.ToLowerInvariant();
            if (settingsHash == null)
            {
                settingsHash = hash;
            }
            else if (!string.Equals(settingsHash, hash, StringComparison.Ordinal))
            {
                throw new ChartStoreException($"{label} has a different settings hash from chart 1; one upload must come from one set of chart settings.");
            }

            byte[] png = DecodeBase64(upload.PngBase64, label);

            if (png.Length > MaxPngBytes)
            {
                throw new ChartStoreException($"{label} is {png.Length:N0} bytes; the limit is {MaxPngBytes:N0}.");
            }

            if (!TryReadPngSize(png, out int width, out int height))
            {
                throw new ChartStoreException($"{label} is not a PNG image.");
            }

            if (width < MinDimensionPx || width > MaxDimensionPx || height < MinDimensionPx || height > MaxDimensionPx)
            {
                throw new ChartStoreException(
                    $"{label} is {width}x{height} pixels; each side must be {MinDimensionPx} to {MaxDimensionPx} pixels.");
            }

            validated.Add(new ValidatedChart
            {
                FigureKey = figureKey,
                Naming = naming,
                Title = title,
                Caption = caption,
                AltText = altText,
                SettingsHash = hash,
                Png = png,
                WidthPx = width,
                HeightPx = height,
                Sha256 = Sha256Hex(png)
            });
        }

        return validated;
    }

    /// <summary>Reads the width and height from a PNG's signature and IHDR chunk.</summary>
    public static bool TryReadPngSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Signature (8), IHDR length (4), type (4), width (4), height (4).
        if (png.Length < 24 || !png[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(png.Slice(8, 4)) != 13
            || png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R')
        {
            return false;
        }

        uint w = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(16, 4));
        uint h = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(20, 4));
        if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] DecodeBase64(string? text, string label)
    {
        string body = text ?? string.Empty;
        if (body.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            body = body[DataUrlPrefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ChartStoreException($"{label} has no image data.");
        }

        if (body.Length > MaxBase64Chars + 1024)
        {
            throw new ChartStoreException($"{label} is larger than {MaxPngBytes:N0} bytes.");
        }

        try
        {
            return Convert.FromBase64String(body);
        }
        catch (FormatException)
        {
            throw new ChartStoreException($"{label} is not valid base64.");
        }
    }

    private static string Clip(string value) => value.Length <= 40 ? value : value[..40] + "...";

    // ---------------------------------------------------------------- paths

    /// <summary>The chart folder of one document.</summary>
    public string GetDocumentFolder(long documentId)
    {
        string root = RequireRoot();
        if (documentId <= 0)
        {
            throw new ChartStoreException($"{documentId} is not a report document id.");
        }

        return Contain(Path.Combine(root, documentId.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The path of one chart image; refuses an unknown figure key or naming.</summary>
    public string GetChartFilePath(long documentId, string figureKey, string naming)
        => Contain(Path.Combine(GetDocumentFolder(documentId), CheckedFileName(figureKey, naming)));

    private static string CheckedFileName(string? figureKey, string? naming)
    {
        if (string.IsNullOrEmpty(figureKey) || !BenchmarkReportChartPlacement.IsKnown(figureKey))
        {
            throw new ChartStoreException($"\"{Clip(figureKey ?? string.Empty)}\" is not a known figure key.");
        }

        if (!IsKnownNaming(naming))
        {
            throw new ChartStoreException($"\"{Clip(naming ?? string.Empty)}\" is not a chart naming.");
        }

        return ChartFileName(figureKey, naming!);
    }

    /// <summary>Returns the full path, or throws when it does not resolve strictly inside the root.</summary>
    private string Contain(string path)
    {
        string prefix = _rootPrefix ?? throw new ChartStoreException(NotConfiguredMessage);
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.Ordinal) || full.Length == prefix.Length)
        {
            throw new ChartStoreException("A chart path resolved outside the chart storage folder.");
        }

        return full;
    }

    private string RequireRoot() => _root ?? throw new ChartStoreException(NotConfiguredMessage);

    private string StagingRoot() => Contain(Path.Combine(RequireRoot(), StagingFolderName));

    private SemaphoreSlim LockFor(long documentId) => _documentLocks.GetOrAdd(documentId, _ => new SemaphoreSlim(1, 1));

    private static bool TryParseDocumentFolderName(string name, out long documentId)
        => long.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out documentId)
            && documentId > 0
            && name == documentId.ToString(CultureInfo.InvariantCulture);

    private void WarnUnconfiguredOnce()
    {
        if (Interlocked.Exchange(ref _unconfiguredWarningLogged, 1) == 0)
        {
            _logger.LogWarning("Report chart storage is not configured ({Key} is empty or not absolute); documents render without charts.", ConfigurationKey);
        }
    }

    // ---------------------------------------------------------------- writes

    /// <summary>Writes one file. Overridable so tests can fail a write part-way through a set.</summary>
    protected virtual Task WriteFileAsync(string path, byte[] contents, CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, contents, cancellationToken);

    /// <summary>
    /// Replaces a document's whole chart set: everything is written into a staging folder, the old
    /// folder is deleted and the staging folder moved into its place. A failure removes the staging
    /// folder and leaves the old set as it was.
    /// </summary>
    public async Task<ReportDocumentChartsSummaryDto> SetChartsAsync(
        long documentId,
        IReadOnlyList<ValidatedChart> validated,
        CancellationToken cancellationToken = default)
    {
        string target = GetDocumentFolder(documentId);
        if (validated == null || validated.Count == 0)
        {
            throw new ChartStoreException("No charts were sent. To remove a document's charts, delete them instead.");
        }

        var manifest = new BenchmarkReportChartManifest
        {
            DocumentId = documentId,
            SettingsHash = validated[0].SettingsHash,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var chart in validated)
        {
            manifest.Charts.Add(new BenchmarkReportChartManifestEntry
            {
                FigureKey = chart.FigureKey,
                Naming = chart.Naming,
                File = CheckedFileName(chart.FigureKey, chart.Naming),
                Sha256 = chart.Sha256,
                WidthPx = chart.WidthPx,
                HeightPx = chart.HeightPx,
                Title = chart.Title,
                Caption = chart.Caption,
                AltText = chart.AltText
            });
        }

        if (manifest.Charts.Select(c => c.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Charts.Count)
        {
            throw new ChartStoreException("The same chart appears more than once.");
        }

        string staging = Contain(Path.Combine(StagingRoot(), $"{documentId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}"));
        var gate = LockFor(documentId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(staging);

            for (int i = 0; i < validated.Count; i++)
            {
                await WriteFileAsync(Contain(Path.Combine(staging, manifest.Charts[i].File)), validated[i].Png, cancellationToken);
            }

            byte[] manifestBytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(manifest, ManifestJsonOptions));
            await WriteFileAsync(Contain(Path.Combine(staging, ManifestFileName)), manifestBytes, cancellationToken);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(staging, target);
        }
        catch
        {
            TryDeleteFolder(staging);
            throw;
        }
        finally
        {
            gate.Release();
        }

        _logger.LogInformation("Stored {Count} report charts for document {DocumentId}.", manifest.Charts.Count, documentId);
        return Summarize(manifest);
    }

    /// <summary>Deletes a document's chart folder. Returns whether there was one.</summary>
    public async Task<bool> DeleteChartsAsync(long documentId, CancellationToken cancellationToken = default)
    {
        string target = GetDocumentFolder(documentId);

        var gate = LockFor(documentId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(target))
            {
                return false;
            }

            Directory.Delete(target, recursive: true);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove the chart staging folder {Path}.", path);
        }
    }

    // ---------------------------------------------------------------- reads

    /// <summary>
    /// A document's charts in one naming, in manifest order. A missing or altered file, or an unreadable
    /// manifest, is skipped with a warning: a render never fails because of its charts.
    /// </summary>
    public async Task<IReadOnlyList<BenchmarkReportRenderChart>> LoadAsync(
        long documentId,
        string naming,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            WarnUnconfiguredOnce();
            return Array.Empty<BenchmarkReportRenderChart>();
        }

        if (!IsKnownNaming(naming) || documentId <= 0)
        {
            _logger.LogWarning("Report charts requested for document {DocumentId} in an unknown naming {Naming}.", documentId, naming);
            return Array.Empty<BenchmarkReportRenderChart>();
        }

        var gate = LockFor(documentId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = ReadManifest(documentId);
            if (manifest == null)
            {
                return Array.Empty<BenchmarkReportRenderChart>();
            }

            var charts = new List<BenchmarkReportRenderChart>();
            foreach (var entry in manifest.Charts.Where(c => c.Naming == naming))
            {
                try
                {
                    string path = GetChartFilePath(documentId, entry.FigureKey, entry.Naming);
                    if (!File.Exists(path))
                    {
                        _logger.LogWarning("Report chart {File} of document {DocumentId} is missing; it is left out.", entry.File, documentId);
                        continue;
                    }

                    byte[] png = await File.ReadAllBytesAsync(path, cancellationToken);
                    string sha = Sha256Hex(png);
                    if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Report chart {File} of document {DocumentId} does not match its manifest checksum; it is left out.", entry.File, documentId);
                        continue;
                    }

                    if (!TryReadPngSize(png, out int width, out int height))
                    {
                        _logger.LogWarning("Report chart {File} of document {DocumentId} is not a PNG; it is left out.", entry.File, documentId);
                        continue;
                    }

                    charts.Add(new BenchmarkReportRenderChart
                    {
                        FigureKey = entry.FigureKey,
                        Title = entry.Title ?? string.Empty,
                        Caption = entry.Caption ?? string.Empty,
                        AltText = entry.AltText ?? string.Empty,
                        Png = png,
                        WidthPx = width,
                        HeightPx = height,
                        Sha256 = sha
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Report chart {File} of document {DocumentId} could not be read; it is left out.", entry.File, documentId);
                }
            }

            return charts;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A document's stored chart set from its manifest alone; null when it has none.</summary>
    public ReportDocumentChartsSummaryDto? ReadSummary(long documentId)
    {
        if (!IsConfigured)
        {
            WarnUnconfiguredOnce();
            return null;
        }

        if (documentId <= 0)
        {
            return null;
        }

        var manifest = ReadManifest(documentId);
        return manifest == null ? null : Summarize(manifest);
    }

    private BenchmarkReportChartManifest? ReadManifest(long documentId)
    {
        string path;
        try
        {
            path = Contain(Path.Combine(GetDocumentFolder(documentId), ManifestFileName));
        }
        catch (ChartStoreException ex)
        {
            _logger.LogWarning(ex, "The chart manifest path of document {DocumentId} could not be built.", documentId);
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<BenchmarkReportChartManifest>(File.ReadAllText(path, Utf8NoBom), ManifestJsonOptions);
            if (manifest == null)
            {
                _logger.LogWarning("The chart manifest of document {DocumentId} is empty.", documentId);
                return null;
            }

            manifest.Charts = (manifest.Charts ?? new List<BenchmarkReportChartManifestEntry>())
                .Where(c => c != null)
                .ToList();
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The chart manifest of document {DocumentId} could not be read.", documentId);
            return null;
        }
    }

    private static ReportDocumentChartsSummaryDto Summarize(BenchmarkReportChartManifest manifest) => new()
    {
        DocumentId = manifest.DocumentId,
        ChartCount = manifest.Charts.Count,
        FigureKeys = manifest.Charts.Select(c => c.FigureKey).Distinct(StringComparer.Ordinal).ToList(),
        SettingsHash = manifest.SettingsHash ?? string.Empty
    };

    // ---------------------------------------------------------------- maintenance

    /// <summary>
    /// Document folders plus staging folders, and the files and bytes inside them. Anything else in
    /// the root is not counted. Zeroes when unconfigured or when the root does not exist.
    /// </summary>
    public (int FolderCount, int FileCount, long TotalBytes) GetDiskMetrics()
    {
        if (_root == null || !Directory.Exists(_root))
        {
            return (0, 0, 0);
        }

        int folders = 0;
        int files = 0;
        long bytes = 0;

        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            string name = Path.GetFileName(dir);
            if (TryParseDocumentFolderName(name, out _))
            {
                folders++;
                var (f, b) = CountFiles(dir);
                files += f;
                bytes += b;
            }
            else if (name == StagingFolderName)
            {
                folders += SafeEnumerateDirectories(dir).Count();
                var (f, b) = CountFiles(dir);
                files += f;
                bytes += b;
            }
        }

        return (folders, files, bytes);
    }

    /// <summary>
    /// Deletes, or in a dry run only counts, every document folder and the staging folders, each under
    /// its document's lock. Any other entry in the root is listed in
    /// <see cref="ReportChartClearResult.LeftAlone"/> and kept, as is the root itself.
    /// </summary>
    public async Task<ReportChartClearResult> ClearAllAsync(
        bool dryRun,
        IEnumerable<long> existingDocumentIds,
        CancellationToken cancellationToken = default)
    {
        string root = RequireRoot();
        var result = new ReportChartClearResult();
        if (!Directory.Exists(root))
        {
            return result;
        }

        var existing = new HashSet<long>(existingDocumentIds ?? Array.Empty<long>());

        foreach (var entry in Directory.EnumerateFileSystemEntries(root).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(entry);
            bool isDirectory = Directory.Exists(entry);

            if (isDirectory && TryParseDocumentFolderName(name, out long documentId))
            {
                var gate = LockFor(documentId);
                await gate.WaitAsync(cancellationToken);
                try
                {
                    string folder = GetDocumentFolder(documentId);
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    var (files, bytes) = CountFiles(folder);
                    result.FolderCount++;
                    result.FileCount += files;
                    result.Bytes += bytes;
                    if (!existing.Contains(documentId))
                    {
                        result.OrphanFolderCount++;
                    }

                    if (!dryRun)
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            else if (isDirectory && name == StagingFolderName)
            {
                await ClearStagingAsync(dryRun, result, cancellationToken);
            }
            else
            {
                result.LeftAlone.Add(isDirectory ? name + Path.DirectorySeparatorChar : name);
            }
        }

        _logger.LogInformation(
            "{Action} {Folders} report chart folders ({Files} files, {Bytes} bytes); {Orphans} for documents that no longer exist.",
            dryRun ? "Would delete" : "Deleted", result.FolderCount, result.FileCount, result.Bytes, result.OrphanFolderCount);

        return result;
    }

    private async Task ClearStagingAsync(bool dryRun, ReportChartClearResult result, CancellationToken cancellationToken)
    {
        string staging = StagingRoot();

        foreach (var dir in SafeEnumerateDirectories(staging).ToList())
        {
            string name = Path.GetFileName(dir);
            int dash = name.IndexOf('-');
            SemaphoreSlim? gate = dash > 0 && TryParseDocumentFolderName(name[..dash], out long documentId)
                ? LockFor(documentId)
                : null;

            if (gate != null)
            {
                await gate.WaitAsync(cancellationToken);
            }

            try
            {
                string folder = Contain(dir);
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var (files, bytes) = CountFiles(folder);
                result.FolderCount++;
                result.FileCount += files;
                result.Bytes += bytes;

                if (!dryRun)
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            finally
            {
                gate?.Release();
            }
        }

        foreach (var file in Directory.EnumerateFiles(staging).ToList())
        {
            result.FileCount++;
            result.Bytes += SafeLength(file);
            if (!dryRun)
            {
                File.Delete(Contain(file));
            }
        }

        if (!dryRun)
        {
            try
            {
                Directory.Delete(staging, recursive: false);
            }
            catch (IOException)
            {
                // A write began after the sweep; its staging folder stays.
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
        => Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Enumerable.Empty<string>();

    private static (int Files, long Bytes) CountFiles(string folder)
    {
        int files = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            files++;
            bytes += SafeLength(file);
        }

        return (files, bytes);
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }
}
