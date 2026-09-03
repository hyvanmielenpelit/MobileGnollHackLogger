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
    DateTime? CapturedAtUtc = null
);

public class DuplicateBoardNameException : InvalidOperationException
{
    public long ExistingBoardId { get; }
    public string BoardName { get; }

    public DuplicateBoardNameException(string name, long existingBoardId)
        : base($"A benchmark game snapshot with name '{name}' already exists (ID: {existingBoardId}).")
    {
        BoardName = name;
        ExistingBoardId = existingBoardId;
    }
}

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

        var existing = await _dbContext.BenchmarkGameSnapshots
            .FirstOrDefaultAsync(s => s.Name == meta.Name, ct);
        if (existing != null)
        {
            throw new DuplicateBoardNameException(meta.Name, existing.Id);
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

        var board = new BenchmarkGameSnapshot
        {
            Name = meta.Name,
            SanitizedText = finalText,
            DigestText = digestText,
            CharCount = finalText.Length,
            Sha256 = sha256,
            CaptureMethod = captureMethod,
            SourceGnollHackVersion = meta.SourceGnollHackVersion,
            Notes = meta.Notes,
            CapturedAtUtc = meta.CapturedAtUtc ?? DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        _dbContext.BenchmarkGameSnapshots.Add(board);

        string suiteBaseName = $"Board: {board.Name}";
        string finalSuiteName = suiteBaseName;
        int counter = 1;
        while (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == finalSuiteName, ct))
        {
            counter++;
            finalSuiteName = $"{suiteBaseName} ({counter})";
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

        await _dbContext.SaveChangesAsync(ct);

        return (board, suite);
    }
}
