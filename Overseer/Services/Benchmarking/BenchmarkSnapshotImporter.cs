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
    public const int MaxDigestChars = 2000;

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

    private async Task<(BenchmarkGameSnapshot Board, BenchmarkSuite Suite)> ProcessAndPersistAsync(
        string normalizedText, string captureMethod, BoardMetadata meta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new ArgumentException(
                "Captured snapshot flattened to empty text. A dump that flattens to nothing is a capture failure, not a valid board.",
                nameof(normalizedText));
        }

        if (string.IsNullOrWhiteSpace(meta.Name))
        {
            throw new ArgumentException("Board name must not be empty.", nameof(meta));
        }

        string finalText = normalizedText;
        if (finalText.Length > DefaultMaxSnapshotChars)
        {
            finalText = finalText.Substring(0, DefaultMaxSnapshotChars)
                + "\n\n[SNAPSHOT TRUNCATED at "
                + DefaultMaxSnapshotChars + " characters.]";
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(finalText));
        string sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        string digestText;
        if (finalText.Length <= MaxDigestChars)
        {
            digestText = finalText;
        }
        else
        {
            int lastNewline = finalText.LastIndexOf('\n', MaxDigestChars);
            digestText = lastNewline > 0
                ? finalText.Substring(0, lastNewline).Trim()
                : finalText.Substring(0, MaxDigestChars).Trim();
        }

        string finalName = meta.Name;
        int counter = 1;

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
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

            string suiteBaseName = $"Board: {board.Name}";
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
                Description = $"Benchmark question suite bound to game board '{board.Name}' (captured via {board.CaptureMethod}, {board.CharCount} characters, SHA-256 {shaPrefix}).",
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
                finalName = $"{meta.Name} ({counter})";
            }
        }

        throw new InvalidOperationException("Failed to save snapshot after maximum retry attempts.");
    }
}
