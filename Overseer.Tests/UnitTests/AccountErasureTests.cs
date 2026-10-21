using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Privacy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The account erasure shared by self-service deletion and an approved review: rows scrubbed, removed or
/// disassociated, files deleted, the user deleted. SQLite in memory, because the erasure runs set-based
/// updates the in-memory provider cannot translate.
/// </summary>
public sealed class AccountErasureTests : IDisposable
{
    private const string UserId = "erase-user";
    private const string UserName = "eraser";
    private const string OtherUserId = "keep-user";
    private const string OtherUserName = "keeper";

    private static readonly DateTime Seeded = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "AccountErasureTests_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountErasureTests()
    {
        Directory.CreateDirectory(_root);

        _connection = SqliteSchemaTemplate.OpenCopy();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _connection.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string DumpLogRoot => Path.Combine(_root, "dumplogs");
    private string ConversationsRoot => Path.Combine(_root, "conversations");
    private string BonesRoot => Path.Combine(_root, "bones");

    private sealed record Seed(long GameLogId, long[] SessionIds, string BonesFile, long OwnRequestLogId,
        long NameOnlyRequestLogId, long OtherRequestLogId);

    /// <summary>
    /// Two users; the erased one has a game log, two chat sessions, two bones rows (one file missing), a bones
    /// transaction, a save file tracking row, a security event and request logs by id and by name. Defaults
    /// of getutcdate() are set explicitly because SQLite has no such function.
    /// </summary>
    private async Task<Seed> SeedAsync(bool createFiles = true)
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserName, NormalizedUserName = UserName.ToUpperInvariant(), Email = "eraser@example.com" });
        _db.Users.Add(new ApplicationUser { Id = OtherUserId, UserName = OtherUserName, NormalizedUserName = OtherUserName.ToUpperInvariant(), Email = "keeper@example.com" });
        await _db.SaveChangesAsync(ct);

        var gameLog = new GameLog
        {
            AspNetUserId = UserId,
            Name = UserName,
            CharacterName = "Hero",
            Points = 12345,
            Turns = 999,
            DeathText = "killed by a gnoll lord",
            CreatedDate = Seeded,
            ByteStart = 100,
            ByteEnd = 200,
            ByteLength = 100
        };
        var otherGameLog = new GameLog { AspNetUserId = OtherUserId, Name = OtherUserName, Points = 7, CreatedDate = Seeded };
        _db.GameLog.AddRange(gameLog, otherGameLog);

        var session1 = new ChatSession { AspNetUserId = UserId, Title = "First", CreatedUtc = Seeded, LastMessageUtc = Seeded };
        var session2 = new ChatSession { AspNetUserId = UserId, Title = "Second", CreatedUtc = Seeded, LastMessageUtc = Seeded };
        var otherSession = new ChatSession { AspNetUserId = OtherUserId, Title = "Kept", CreatedUtc = Seeded, LastMessageUtc = Seeded };
        _db.ChatSession.AddRange(session1, session2, otherSession);

        string bonesFile = Path.Combine(BonesRoot, "bon-present.dat");
        _db.Bones.AddRange(
            new Bones { AspNetUserId = UserId, BonesFilePath = bonesFile, OriginalFileName = "bon-present", Created = Seeded },
            new Bones { AspNetUserId = UserId, BonesFilePath = Path.Combine(BonesRoot, "bon-missing.dat"), OriginalFileName = "bon-missing", Created = Seeded },
            new Bones { AspNetUserId = OtherUserId, BonesFilePath = Path.Combine(BonesRoot, "bon-other.dat"), OriginalFileName = "bon-other", Created = Seeded });
        _db.BonesTransactions.AddRange(
            new BonesTransaction { AspNetUserId = UserId, Date = Seeded, Type = TransactionType.Upload, BonesId = 1 },
            new BonesTransaction { AspNetUserId = OtherUserId, Date = Seeded, Type = TransactionType.Download, BonesId = 3 });
        _db.SaveFileTrackings.AddRange(
            new SaveFileTracking { AspNetUserId = UserId, CreatedDate = Seeded, TimeStamp = 1, Sha256 = "aa" },
            new SaveFileTracking { AspNetUserId = OtherUserId, CreatedDate = Seeded, TimeStamp = 2, Sha256 = "bb" });

        var ownLog = new RequestInfo { AspNetUserId = UserId, RequestUserName = UserName, FirstDate = Seeded, LastDate = Seeded, Count = 1, RequestPath = "/api/log" };
        var nameOnlyLog = new RequestInfo { RequestUserName = UserName, FirstDate = Seeded, LastDate = Seeded, Count = 1, RequestPath = "/api/bones" };
        var otherLog = new RequestInfo { AspNetUserId = OtherUserId, RequestUserName = OtherUserName, FirstDate = Seeded, LastDate = Seeded, Count = 1, RequestPath = "/api/log" };
        _db.RequestLogs.AddRange(ownLog, nameOnlyLog, otherLog);

        _db.SecurityEvents.Add(new SecurityEvent
        {
            AspNetUserId = UserId,
            Kind = SecurityEventKind.LoginFailed,
            OccurredUtc = Seeded,
            LastOccurredUtc = Seeded,
            Count = 1
        });
        await _db.SaveChangesAsync(ct);

        if (createFiles)
        {
            Directory.CreateDirectory(BonesRoot);
            File.WriteAllText(bonesFile, "bones");
            File.WriteAllText(Path.Combine(BonesRoot, "bon-other.dat"), "bones");

            Directory.CreateDirectory(Path.Combine(DumpLogRoot, UserName));
            File.WriteAllText(Path.Combine(DumpLogRoot, UserName, "dump.txt"), "dump");
            Directory.CreateDirectory(Path.Combine(DumpLogRoot, OtherUserName));

            foreach (var session in new[] { session1, session2, otherSession })
            {
                Directory.CreateDirectory(Path.Combine(ConversationsRoot, session.Id.ToString()));
                File.WriteAllText(Path.Combine(ConversationsRoot, session.Id.ToString(), "attachment.bin"), "x");
            }
        }

        _db.ChangeTracker.Clear();
        return new Seed(gameLog.Id, new[] { session1.Id, session2.Id, otherSession.Id }, bonesFile, ownLog.Id, nameOnlyLog.Id, otherLog.Id);
    }

    private async Task<AccountErasureResult> EraseAsync(AccountErasurePaths paths)
    {
        var user = await _userManager.FindByIdAsync(UserId);
        Assert.NotNull(user);
        var result = await AccountErasure.EraseAsync(_db, _userManager, user, paths, TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();
        return result;
    }

    [Fact]
    public async Task GameLogsAreScrubbedButKept()
    {
        var seed = await SeedAsync();

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        var log = await _db.GameLog.AsNoTracking().SingleAsync(g => g.Id == seed.GameLogId, TestContext.Current.CancellationToken);
        Assert.Null(log.AspNetUserId);
        Assert.Null(log.Name);
        Assert.Null(log.CharacterName);
        Assert.Null(log.DeathText);
        Assert.Equal(0L, log.Points);
        Assert.Equal(0, log.Turns);
        Assert.Equal(Seeded, log.CreatedDate);
        Assert.Equal(100L, log.ByteStart);
        Assert.Equal(200L, log.ByteEnd);
        Assert.Equal(100L, log.ByteLength);

        var other = await _db.GameLog.AsNoTracking().SingleAsync(g => g.AspNetUserId == OtherUserId, TestContext.Current.CancellationToken);
        Assert.Equal(OtherUserName, other.Name);
        Assert.Equal(7L, other.Points);
    }

    [Fact]
    public async Task SessionsBonesTransactionsAndSaveTrackingAreRemoved()
    {
        await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        Assert.False(await _db.ChatSession.AnyAsync(s => s.AspNetUserId == UserId, ct));
        Assert.False(await _db.Bones.AnyAsync(b => b.AspNetUserId == UserId, ct));
        Assert.False(await _db.BonesTransactions.AnyAsync(b => b.AspNetUserId == UserId, ct));
        Assert.False(await _db.SaveFileTrackings.AnyAsync(s => s.AspNetUserId == UserId, ct));

        Assert.Equal(1, await _db.ChatSession.CountAsync(s => s.AspNetUserId == OtherUserId, ct));
        Assert.Equal(1, await _db.Bones.CountAsync(b => b.AspNetUserId == OtherUserId, ct));
        Assert.Equal(1, await _db.BonesTransactions.CountAsync(b => b.AspNetUserId == OtherUserId, ct));
        Assert.Equal(1, await _db.SaveFileTrackings.CountAsync(s => s.AspNetUserId == OtherUserId, ct));
    }

    [Fact]
    public async Task RequestLogsAreDisassociatedByIdAndByName()
    {
        var seed = await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        var own = await _db.RequestLogs.AsNoTracking().SingleAsync(r => r.Id == seed.OwnRequestLogId, ct);
        var nameOnly = await _db.RequestLogs.AsNoTracking().SingleAsync(r => r.Id == seed.NameOnlyRequestLogId, ct);
        var other = await _db.RequestLogs.AsNoTracking().SingleAsync(r => r.Id == seed.OtherRequestLogId, ct);

        Assert.Null(own.AspNetUserId);
        Assert.Null(own.RequestUserName);
        Assert.Equal("/api/log", own.RequestPath);
        Assert.Null(nameOnly.RequestUserName);
        Assert.Equal(OtherUserId, other.AspNetUserId);
        Assert.Equal(OtherUserName, other.RequestUserName);
    }

    [Fact]
    public async Task TheUserIsDeletedWithTheRowsThatCascade()
    {
        await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        Assert.False(await _db.Users.AnyAsync(u => u.Id == UserId, ct));
        Assert.True(await _db.Users.AnyAsync(u => u.Id == OtherUserId, ct));
        Assert.False(await _db.SecurityEvents.AnyAsync(e => e.AspNetUserId == UserId, ct));
    }

    [Fact]
    public async Task TheDumplogAndAttachmentFoldersAreRemoved()
    {
        var seed = await SeedAsync();

        var result = await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        Assert.False(Directory.Exists(Path.Combine(DumpLogRoot, UserName)));
        Assert.True(Directory.Exists(Path.Combine(DumpLogRoot, OtherUserName)));
        Assert.False(Directory.Exists(Path.Combine(ConversationsRoot, seed.SessionIds[0].ToString())));
        Assert.False(Directory.Exists(Path.Combine(ConversationsRoot, seed.SessionIds[1].ToString())));
        Assert.True(Directory.Exists(Path.Combine(ConversationsRoot, seed.SessionIds[2].ToString())));
        Assert.False(File.Exists(seed.BonesFile));
        Assert.True(File.Exists(Path.Combine(BonesRoot, "bon-other.dat")));
        Assert.Equal(0, result.FoldersMissing);
    }

    [Fact]
    public async Task AMissingBonesFileIsCounted()
    {
        await SeedAsync();

        var result = await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        Assert.Equal(1, result.BonesFilesMissing);
    }

    [Fact]
    public async Task NullPathsSkipTheFileStepsButStillEraseTheRows()
    {
        var seed = await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        var result = await EraseAsync(new AccountErasurePaths(null, null));

        Assert.True(Directory.Exists(Path.Combine(DumpLogRoot, UserName)));
        Assert.True(Directory.Exists(Path.Combine(ConversationsRoot, seed.SessionIds[0].ToString())));
        Assert.Equal(0, result.FoldersMissing);
        Assert.False(await _db.Users.AnyAsync(u => u.Id == UserId, ct));
        Assert.False(await _db.ChatSession.AnyAsync(s => s.AspNetUserId == UserId, ct));
    }

    [Fact]
    public async Task BenchmarkRowsAreKeptWithTheUsersAttributionRemoved()
    {
        await SeedAsync();
        var ct = TestContext.Current.CancellationToken;
        // The benchmark rows reference parents this test does not seed; the assertion is on the nulled columns.
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;", ct);

        var group = new BenchmarkRunGroup { Name = "Group", CreatedByUserId = UserId };
        var batteryRun = new BenchmarkBatteryRun
        {
            BatteryName = "Battery", DefinitionJson = "{}", DefinitionSha256 = "sha", StartRequestJson = "{}", StartedByUserId = UserId
        };
        // Snapshots are unique by hash, so both runs share one candidate and one assessor row.
        var candidate = Overseer.Tests.Helpers.BenchmarkModelSnapshots.Candidate();
        var assessor = Overseer.Tests.Helpers.BenchmarkModelSnapshots.Assessor();
        _db.BenchmarkRuns.AddRange(
            new BenchmarkRun { SuiteName = "Suite", StartedByUserId = UserId, TestedModelSnapshot = candidate, AssessorModelSnapshot = assessor },
            new BenchmarkRun { SuiteName = "Suite", StartedByUserId = OtherUserId, TestedModelSnapshot = candidate, AssessorModelSnapshot = assessor });
        _db.BenchmarkRunSeries.Add(new BenchmarkRunSeries { SuiteName = "Suite", StartRequestJson = "{}", StartedByUserId = UserId });
        _db.BenchmarkRunGroups.Add(group);
        _db.BenchmarkGroupAnalyses.Add(new BenchmarkGroupAnalysis
        {
            BenchmarkRunGroup = group, MemberRunIdsJson = "[]", ResultJson = "{}", ComputedByUserId = UserId
        });
        _db.BenchmarkBatteries.Add(new BenchmarkBattery { Name = "Battery", DefinitionSha256 = "sha", CreatedByUserId = UserId });
        _db.BenchmarkBatteryRuns.Add(batteryRun);
        _db.BenchmarkBatteryAnalyses.Add(new BenchmarkBatteryAnalysis
        {
            BenchmarkBatteryRun = batteryRun, MemberRunIdsJson = "[]", ResultJson = "{}", DefinitionSha256 = "sha", ComputedByUserId = UserId
        });
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        Assert.Equal(2, await _db.BenchmarkRuns.CountAsync(ct));
        Assert.False(await _db.BenchmarkRuns.AnyAsync(r => r.StartedByUserId == UserId, ct));
        Assert.True(await _db.BenchmarkRuns.AnyAsync(r => r.StartedByUserId == OtherUserId, ct));
        Assert.Null((await _db.BenchmarkRunSeries.AsNoTracking().SingleAsync(ct)).StartedByUserId);
        Assert.Null((await _db.BenchmarkRunGroups.AsNoTracking().SingleAsync(ct)).CreatedByUserId);
        Assert.Null((await _db.BenchmarkGroupAnalyses.AsNoTracking().SingleAsync(ct)).ComputedByUserId);
        Assert.Null((await _db.BenchmarkBatteries.AsNoTracking().SingleAsync(ct)).CreatedByUserId);
        Assert.Null((await _db.BenchmarkBatteryRuns.AsNoTracking().SingleAsync(ct)).StartedByUserId);
        Assert.Null((await _db.BenchmarkBatteryAnalyses.AsNoTracking().SingleAsync(ct)).ComputedByUserId);
        Assert.False(await _db.Users.AnyAsync(u => u.Id == UserId, ct));
    }

    [Fact]
    public async Task FeedbackReportsGoWithTheUserAndTheIssuesStay()
    {
        var seed = await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        var triaged = new OverseerFeedbackIssue
        {
            Fingerprint = new string('a', 64), Category = "missing_information", Subject = "Dilithium crystal", Summary = "Price not found.",
            FirstSeenUtc = Seeded, LastSeenUtc = Seeded, OccurrenceCount = 2, Status = OverseerFeedbackStatus.Acknowledged,
            TriagedByUserId = UserId, TriagedUtc = Seeded
        };
        var onlyErased = new OverseerFeedbackIssue
        {
            Fingerprint = new string('b', 64), Category = "tool_problem", Subject = "wiki_search", Summary = "Returned nothing.",
            FirstSeenUtc = Seeded, LastSeenUtc = Seeded, OccurrenceCount = 1
        };
        _db.OverseerFeedbackIssues.AddRange(triaged, onlyErased);
        await _db.SaveChangesAsync(ct);

        OverseerFeedbackReport Report(OverseerFeedbackIssue issue, long sessionId, string userId) => new()
        {
            IssueId = issue.Id, Fingerprint = issue.Fingerprint, CreatedUtc = Seeded, Category = issue.Category,
            Subject = issue.Subject, Summary = issue.Summary, ChatSessionId = sessionId, AspNetUserId = userId
        };
        _db.OverseerFeedbackReports.AddRange(
            Report(triaged, seed.SessionIds[0], UserId),
            Report(triaged, seed.SessionIds[2], OtherUserId),
            Report(onlyErased, seed.SessionIds[1], UserId));
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        await EraseAsync(new AccountErasurePaths(DumpLogRoot, ConversationsRoot));

        var remaining = Assert.Single(await _db.OverseerFeedbackReports.AsNoTracking().ToListAsync(ct));
        Assert.Equal(OtherUserId, remaining.AspNetUserId);
        Assert.Equal(seed.SessionIds[2], remaining.ChatSessionId);

        var issues = await _db.OverseerFeedbackIssues.AsNoTracking().ToListAsync(ct);
        Assert.Equal(2, issues.Count);
        var keptTriaged = issues.Single(i => i.Id == triaged.Id);
        Assert.Equal(2, keptTriaged.OccurrenceCount);
        Assert.Equal(UserId, keptTriaged.TriagedByUserId);
        Assert.Equal(1, issues.Single(i => i.Id == onlyErased.Id).OccurrenceCount);
    }

    [Fact]
    public async Task MissingBaseFoldersAreCounted()
    {
        await SeedAsync(createFiles: false);

        var result = await EraseAsync(new AccountErasurePaths(
            Path.Combine(_root, "no-such-dumplogs"), Path.Combine(_root, "no-such-conversations")));

        Assert.Equal(2, result.FoldersMissing);
        Assert.Equal(2, result.BonesFilesMissing);
    }
}
