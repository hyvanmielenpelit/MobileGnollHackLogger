namespace Overseer.Services.Benchmarking;

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;

public record BoardMetadata(
    string Name,
    string? Notes = null,
    string? SourceGnollHackVersion = null,
    DateTime? CapturedAtUtc = null,
    long? SourceChatSessionId = null
);

public class BenchmarkSnapshotImporter
{
    public const int DefaultMaxSnapshotChars = 60000;

    private readonly ApplicationDbContext _dbContext;

    public BenchmarkSnapshotImporter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(BenchmarkGameSnapshot Board, BenchmarkSuite Suite)> FromClientTextAsync(
        string flattenedText, BoardMetadata meta, CancellationToken ct = default)
    {
        string normalized = Normalize(flattenedText, isHtml: false);
        return await ProcessAndPersistAsync(normalized, "ClientRefresh", meta, ct);
    }

    public async Task<(BenchmarkGameSnapshot Board, BenchmarkSuite Suite)> FromRawHtmlAsync(
        string html, BoardMetadata meta, CancellationToken ct = default)
    {
        string sanitized = DumpHtmlSanitizer.Sanitize(html);
        return await ProcessAndPersistAsync(sanitized, "ServerUpload", meta, ct);
    }

    public async Task<(BenchmarkGameSnapshot Board, BenchmarkSuite Suite)> FromSessionAttachmentAsync(
        string attachedText, BoardMetadata meta, CancellationToken ct = default)
    {
        string normalized = Normalize(attachedText, isHtml: false);
        return await ProcessAndPersistAsync(normalized, "SessionAttachment", meta, ct);
    }

    /// <summary>The capture method of a board attached by a suite YAML import.</summary>
    public const string YamlImportCaptureMethod = "YamlImport";

    private static readonly string TruncationMarker =
        "\n\n[SNAPSHOT TRUNCATED at " + DefaultMaxSnapshotChars + " characters.]";

    /// <summary>The text a board stores for already-normalized snapshot text -- cut at
    /// <see cref="DefaultMaxSnapshotChars"/> with a truncation marker, and left alone when it
    /// already carries exactly that cut -- and its lower-case hex SHA-256. Anything that compares
    /// against a stored board's hash must hash through this.</summary>
    public static (string Text, string Sha256) PrepareBoardText(string normalizedText)
    {
        string finalText = normalizedText;
        if (finalText.Length > DefaultMaxSnapshotChars && !IsAlreadyTruncated(finalText))
        {
            finalText = finalText.Substring(0, DefaultMaxSnapshotChars) + TruncationMarker;
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(finalText));
        return (finalText, Convert.ToHexString(hashBytes).ToLowerInvariant());
    }

    /// <summary>True for text that is exactly one cut at the cap followed by the marker.</summary>
    private static bool IsAlreadyTruncated(string text)
    {
        return text.Length == DefaultMaxSnapshotChars + TruncationMarker.Length
            && text.EndsWith(TruncationMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact text a board stores for this upload, its SHA-256 and whether it is cut at the cap.
    /// The import preflight and the import itself both go through this, so they can never disagree
    /// with what <see cref="CreateForSuiteAsync"/> would store.
    /// </summary>
    public static (string Text, string Sha256, bool Truncated) StoredForm(string content, bool isHtml)
    {
        string normalized = Normalize(content, isHtml);
        var (finalText, sha256) = PrepareBoardText(normalized);
        return (finalText, sha256, IsAlreadyTruncated(finalText));
    }

    /// <summary>HTML is flattened; flat text is normalized, which unifies its line endings.</summary>
    private static string Normalize(string content, bool isHtml)
    {
        if (isHtml)
        {
            return DumpHtmlSanitizer.Sanitize(content);
        }

        return DumpHtmlSanitizer.NormalizeFlattenedText(content ?? string.Empty);
    }

    /// <summary>
    /// A stored snapshot whose text hashes to <paramref name="sha256"/>, with the suite that owns
    /// it. One that no suite references is preferred when several match.
    /// </summary>
    public async Task<(BenchmarkGameSnapshot? Board, BenchmarkSuite? OwningSuite)> FindIdenticalAsync(
        string sha256, CancellationToken ct = default)
    {
        var candidates = await _dbContext.BenchmarkGameSnapshots
            .Where(s => s.Sha256 == sha256)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return (null, null);
        }

        var ids = candidates.Select(c => c.Id).ToList();
        var owners = await _dbContext.BenchmarkSuites
            .Where(s => s.GameSnapshotId != null && ids.Contains(s.GameSnapshotId.Value))
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            if (owners.All(o => o.GameSnapshotId != candidate.Id))
            {
                return (candidate, null);
            }
        }

        var first = candidates[0];
        return (first, owners.FirstOrDefault(o => o.GameSnapshotId == first.Id));
    }

    /// <summary>
    /// Attaches a board to a suite that has none: an identical stored snapshot that no suite owns
    /// is reused as it stands, and anything else is stored as a new board. Saves once, so the
    /// suite, its questions and the board land together.
    /// </summary>
    public async Task<(BenchmarkGameSnapshot Board, bool Reused)> AttachOrCreateForSuiteAsync(
        BenchmarkSuite suite, string content, bool isHtml, BoardMetadata meta, string captureMethod, CancellationToken ct = default)
    {
        if (suite.GameSnapshotId != null || suite.GameSnapshot != null)
        {
            throw new InvalidOperationException("This suite already has a snapshot.");
        }

        var (_, sha256, _) = StoredForm(content, isHtml);
        var (identical, owner) = await FindIdenticalAsync(sha256, ct);

        BenchmarkGameSnapshot board;
        bool reused = identical != null && owner == null;
        if (reused)
        {
            // The stored board keeps its own name, version, notes and capture method.
            board = identical!;
            suite.GameSnapshot = board;
        }
        else
        {
            (board, _) = await BuildBoardAsync(Normalize(content, isHtml), captureMethod, meta, 1, ct);
            _dbContext.BenchmarkGameSnapshots.Add(board);
            suite.GameSnapshot = board;
        }

        suite.ModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        return (board, reused);
    }

    /// <summary>Creates a board from uploaded text or HTML and attaches it to <paramref name="suite"/>. An existing snapshot is removed only when <paramref name="replaceExisting"/> is true.</summary>
    public async Task<BenchmarkGameSnapshot> CreateForSuiteAsync(BenchmarkSuite suite, string content, bool isHtml, BoardMetadata meta, bool replaceExisting, CancellationToken ct = default)
    {
        string normalized = Normalize(content, isHtml);
        string captureMethod = isHtml ? "ServerUpload" : "TextUpload";

        var (board, _) = await BuildBoardAsync(normalized, captureMethod, meta, 1, ct);

        if (suite.GameSnapshotId is long oldId)
        {
            if (!replaceExisting)
            {
                throw new InvalidOperationException("This suite already has a snapshot.");
            }

            var old = await _dbContext.BenchmarkGameSnapshots.FirstOrDefaultAsync(s => s.Id == oldId, ct);
            if (old != null)
            {
                _dbContext.BenchmarkGameSnapshots.Remove(old);
            }
        }

        _dbContext.BenchmarkGameSnapshots.Add(board);
        suite.GameSnapshot = board;
        suite.ModifiedAtUtc = DateTime.UtcNow;

        // The removal and the attachment land in one save.
        await _dbContext.SaveChangesAsync(ct);
        return board;
    }

    /// <summary>Validates, truncates, hashes and digests the text and picks a free board name at or
    /// after <paramref name="startCounter"/>. The board is not added to the context.</summary>
    private async Task<(BenchmarkGameSnapshot Board, int Counter)> BuildBoardAsync(
        string normalizedText, string captureMethod, BoardMetadata meta, int startCounter, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new ArgumentException(
                "Captured snapshot flattened to empty text. A dump that flattens to nothing is a capture failure, not a valid snapshot.",
                nameof(normalizedText));
        }

        if (string.IsNullOrWhiteSpace(meta.Name))
        {
            throw new ArgumentException("Snapshot name must not be empty.", nameof(meta));
        }

        var (finalText, sha256) = PrepareBoardText(normalizedText);

        string digestText = BenchmarkSnapshotDigestBuilder.Build(finalText);

        int counter = startCounter;
        string finalName = counter <= 1 ? meta.Name : $"{meta.Name} ({counter})";
        while (await _dbContext.BenchmarkGameSnapshots.AnyAsync(s => s.Name == finalName, ct))
        {
            counter++;
            finalName = $"{meta.Name} ({counter})";
        }

        var board = new BenchmarkGameSnapshot
        {
            Name = finalName,
            SanitizedText = finalText,
            DigestText = digestText,
            CharCount = finalText.Length,
            Sha256 = sha256,
            CaptureMethod = captureMethod,
            SourceGnollHackVersion = meta.SourceGnollHackVersion,
            Notes = meta.Notes,
            SourceChatSessionId = meta.SourceChatSessionId,
            CapturedAtUtc = meta.CapturedAtUtc ?? DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        return (board, counter);
    }

    private async Task<(BenchmarkGameSnapshot Board, BenchmarkSuite Suite)> ProcessAndPersistAsync(
        string normalizedText, string captureMethod, BoardMetadata meta, CancellationToken ct)
    {
        int counter = 1;

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (board, usedCounter) = await BuildBoardAsync(normalizedText, captureMethod, meta, counter, ct);
            counter = usedCounter;

            string suiteBaseName = $"Snapshot: {board.Name}";
            string finalSuiteName = suiteBaseName;
            int suiteCounter = 1;
            while (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == finalSuiteName, ct))
            {
                suiteCounter++;
                finalSuiteName = $"{suiteBaseName} ({suiteCounter})";
            }

            string shaPrefix = board.Sha256.Length >= 12 ? board.Sha256[..12] : board.Sha256;
            var suite = new BenchmarkSuite
            {
                Name = finalSuiteName,
                Description = $"Benchmark question suite bound to game snapshot '{board.Name}' (captured via {board.CaptureMethod}, {board.CharCount} characters, SHA-256 {shaPrefix}).",
                GameSnapshot = board,
                HasGeneratedQuestions = false,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow
            };

            _dbContext.BenchmarkGameSnapshots.Add(board);
            _dbContext.BenchmarkSuites.Add(suite);

            var profile = await _dbContext.BenchmarkScoringProfiles
                .FirstOrDefaultAsync(p => p.Name == "Situational Advisor", ct);
            if (profile == null)
            {
                profile = new BenchmarkScoringProfile
                {
                    Name = "Situational Advisor",
                    SpeedTargetMs = 25000,
                    SpeedDecayK = 20.0,
                    IsDefault = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    ModifiedAtUtc = DateTime.UtcNow
                };
                _dbContext.BenchmarkScoringProfiles.Add(profile);
            }

            try
            {
                await _dbContext.SaveChangesAsync(ct);
                return (board, suite);
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                _dbContext.Entry(board).State = EntityState.Detached;
                _dbContext.Entry(suite).State = EntityState.Detached;
                if (profile != null && _dbContext.Entry(profile).State == EntityState.Added)
                {
                    _dbContext.Entry(profile).State = EntityState.Detached;
                }
                counter++;
            }
        }

        throw new InvalidOperationException("Failed to save snapshot after maximum retry attempts.");
    }
}
