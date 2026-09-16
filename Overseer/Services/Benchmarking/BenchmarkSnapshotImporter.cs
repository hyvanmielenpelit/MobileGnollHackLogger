namespace Overseer.Services.Benchmarking;

using System;
using System.IO;
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
        string normalized = DumpHtmlSanitizer.NormalizeFlattenedText(flattenedText);
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
        string normalized = DumpHtmlSanitizer.NormalizeFlattenedText(attachedText);
        return await ProcessAndPersistAsync(normalized, "SessionAttachment", meta, ct);
    }

    /// <summary>The text a board stores for already-normalized snapshot text -- cut at
    /// <see cref="DefaultMaxSnapshotChars"/> with a truncation marker -- and its lower-case hex
    /// SHA-256. Anything that compares against a stored board's hash must hash through this.</summary>
    public static (string Text, string Sha256) PrepareBoardText(string normalizedText)
    {
        string finalText = normalizedText;
        if (finalText.Length > DefaultMaxSnapshotChars)
        {
            finalText = finalText.Substring(0, DefaultMaxSnapshotChars)
                + "\n\n[SNAPSHOT TRUNCATED at "
                + DefaultMaxSnapshotChars + " characters.]";
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(finalText));
        return (finalText, Convert.ToHexString(hashBytes).ToLowerInvariant());
    }

    /// <summary>Creates a board from uploaded text or HTML and attaches it to <paramref name="suite"/>. An existing snapshot is removed only when <paramref name="replaceExisting"/> is true.</summary>
    public async Task<BenchmarkGameSnapshot> CreateForSuiteAsync(BenchmarkSuite suite, string content, bool isHtml, BoardMetadata meta, bool replaceExisting, CancellationToken ct = default)
    {
        string normalized;
        string captureMethod;
        if (isHtml)
        {
            normalized = DumpHtmlSanitizer.Sanitize(content);
            captureMethod = "ServerUpload";
        }
        else
        {
            // NormalizeFlattenedText keeps single CRLFs, so line endings are unified first.
            string text = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            normalized = DumpHtmlSanitizer.NormalizeFlattenedText(text);
            captureMethod = "TextUpload";
        }

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
