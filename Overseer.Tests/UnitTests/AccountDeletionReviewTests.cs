using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Security;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The administrator's review of held account deletions: approve (which erases), decline, placing and
/// releasing holds, the overdue highlight, and the admin alert that counts what waits. SQLite in memory,
/// because approving runs the erasure's set-based updates.
/// </summary>
public sealed class AccountDeletionReviewTests : IDisposable
{
    private const string AdminId = "admin-1";
    private const string UserId = "doomed-user";
    private const string UserName = "doomed";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "AccountDeletionReviewTests_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RecordingSecurityEvents _events = new();
    private readonly EphemeralSessionStore _ephemeral = new(new ConfigurationBuilder().Build(), startSweeper: false);

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        public List<SecurityEventEntry> Entries { get; } = new();

        public void Record(SecurityEventEntry entry) => Entries.Add(entry);
    }

    public AccountDeletionReviewTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dumplogs"));
        Directory.CreateDirectory(Path.Combine(_root, "conversations"));

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
        _ephemeral.Dispose();
        _connection.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private IConfiguration Config(bool dumpLogPath = true, bool conversationsDataLocation = true)
    {
        var values = new Dictionary<string, string?>();
        if (dumpLogPath) values["DumpLogPath"] = Path.Combine(_root, "dumplogs");
        if (conversationsDataLocation) values["ConversationsDataLocation"] = Path.Combine(_root, "conversations");
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private AccountDeletionReviewService Service(IConfiguration? config = null, AccountHoldOptions? options = null) =>
        new(_db, _userManager, config ?? Config(), options ?? new AccountHoldOptions(),
            NullLogger<AccountDeletionReviewService>.Instance, _scope.ServiceProvider, _events, _ephemeral);

    private EphemeralSession OpenIncognito(string userId) => _ephemeral.Create(userId, new ChatSession
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    });

    private async Task<long> SeedPendingRequestAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserName, NormalizedUserName = UserName.ToUpperInvariant(), Email = "doomed@example.com" });
        _db.Users.Add(new ApplicationUser { Id = AdminId, UserName = "admin-name", NormalizedUserName = "ADMIN-NAME", Email = "admin@example.com" });
        var request = new AccountDeletionRequest
        {
            AspNetUserId = UserId,
            UserNameSnapshot = UserName,
            EmailSnapshot = "doomed@example.com",
            RequestedUtc = DateTime.UtcNow.AddDays(-2),
            Status = AccountDeletionRequestStatus.Pending,
            HoldReasons = "Manual hold placed by admin-name on 2026-10-11: abuse report"
        };
        _db.AccountDeletionRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return request.Id;
    }

    private async Task<AccountDeletionRequest> StoredRequestAsync(long id)
    {
        _db.ChangeTracker.Clear();
        return await _db.AccountDeletionRequests.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }

    private Task<bool> UserExistsAsync() => _db.Users.AnyAsync(u => u.Id == UserId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ApproveErasesTheAccountAndCompletesTheRequest()
    {
        long id = await SeedPendingRequestAsync();
        DateTime before = DateTime.UtcNow;

        var result = await Service().ApproveAsync(id, UserName, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        Assert.False(await UserExistsAsync());

        var stored = await StoredRequestAsync(id);
        Assert.Equal(AccountDeletionRequestStatus.Completed, stored.Status);
        Assert.Equal(AdminId, stored.DecidedByUserId);
        Assert.NotNull(stored.DecidedUtc);
        Assert.InRange(stored.DecidedUtc!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Null(stored.AspNetUserId);
        Assert.Null(stored.UserNameSnapshot);
        Assert.Null(stored.EmailSnapshot);

        // No email sender is registered, so the decision stands and the missing sender is reported.
        Assert.NotNull(result.Decision);
        Assert.False(result.Decision!.EmailSent);
        Assert.Equal("No email sender is configured.", result.Decision.EmailError);
    }

    [Fact]
    public async Task ApproveClosesEveryIncognitoSessionOfTheAccountAndNoOtherAccounts()
    {
        long id = await SeedPendingRequestAsync();
        var first = OpenIncognito(UserId);
        var second = OpenIncognito(UserId);
        var bystander = OpenIncognito(AdminId);

        var result = await Service().ApproveAsync(id, UserName, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        Assert.Null(_ephemeral.Get(first.Ref, UserId));
        Assert.Null(_ephemeral.Get(second.Ref, UserId));
        Assert.True(first.IsErased);
        Assert.True(second.IsErased);
        Assert.NotNull(_ephemeral.Get(bystander.Ref, AdminId));
        Assert.False(bystander.IsErased);
    }

    [Theory]
    [InlineData(false, true, "DumpLogPath")]
    [InlineData(true, false, "ConversationsDataLocation")]
    public async Task ApproveIsRefusedWhileAnErasurePathIsMissing(bool dumpLogPath, bool conversations, string missing)
    {
        long id = await SeedPendingRequestAsync();

        var result = await Service(Config(dumpLogPath, conversations)).ApproveAsync(id, UserName, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Conflict, result.Status);
        Assert.Contains(missing, result.Message);
        Assert.True(await UserExistsAsync());
        Assert.Equal(AccountDeletionRequestStatus.Pending, (await StoredRequestAsync(id)).Status);
    }

    [Theory]
    [InlineData("Doomed")]
    [InlineData("someone-else")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ApproveIsRefusedOnAWrongConfirmationName(string? typed)
    {
        long id = await SeedPendingRequestAsync();

        var result = await Service().ApproveAsync(id, typed, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.BadRequest, result.Status);
        Assert.True(await UserExistsAsync());
        Assert.Equal(AccountDeletionRequestStatus.Pending, (await StoredRequestAsync(id)).Status);
    }

    [Fact]
    public async Task ApproveAcceptsTheNameWithSurroundingWhitespace()
    {
        long id = await SeedPendingRequestAsync();

        var result = await Service().ApproveAsync(id, "  " + UserName + " ", AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        Assert.False(await UserExistsAsync());
    }

    [Fact]
    public async Task ADecidedRequestCannotBeApproved()
    {
        long id = await SeedPendingRequestAsync();
        await Service().DeclineAsync(id, "Kept for the investigation.", AdminId, TestContext.Current.CancellationToken);

        var result = await Service().ApproveAsync(id, UserName, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Conflict, result.Status);
        Assert.True(await UserExistsAsync());
    }

    [Fact]
    public async Task DeclineKeepsTheAccountAndStoresTheNote()
    {
        long id = await SeedPendingRequestAsync();

        var result = await Service().DeclineAsync(id, "  Kept for the investigation.  ", AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        Assert.True(await UserExistsAsync());
        var stored = await StoredRequestAsync(id);
        Assert.Equal(AccountDeletionRequestStatus.Declined, stored.Status);
        Assert.Equal("Kept for the investigation.", stored.DecisionNote);
        Assert.Equal(AdminId, stored.DecidedByUserId);
        Assert.NotNull(stored.DecidedUtc);
        Assert.Equal(UserId, stored.AspNetUserId);
        Assert.Equal(UserName, stored.UserNameSnapshot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task DeclineRequiresANote(string? note)
    {
        long id = await SeedPendingRequestAsync();

        var result = await Service().DeclineAsync(id, note, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.BadRequest, result.Status);
        Assert.Equal(AccountDeletionRequestStatus.Pending, (await StoredRequestAsync(id)).Status);
    }

    [Fact]
    public async Task PlacingAHoldWritesTheHoldAndAnEvent()
    {
        await SeedPendingRequestAsync();
        var ct = TestContext.Current.CancellationToken;

        var result = await Service().PlaceHoldAsync(UserId, "  Abuse report under review  ", AdminId, ct);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        var hold = await _db.AccountHolds.AsNoTracking().SingleAsync(ct);
        Assert.Equal(UserId, hold.AspNetUserId);
        Assert.Equal("Abuse report under review", hold.Reason);
        Assert.Equal(AdminId, hold.PlacedByUserId);
        Assert.Null(hold.ReleasedUtc);

        var entry = Assert.Single(_events.Entries);
        Assert.Equal(SecurityEventKind.AccountHoldPlaced, entry.Kind);
        Assert.Equal(UserId, entry.UserId);
        Assert.Equal("Abuse report under review", entry.Detail);
    }

    [Fact]
    public async Task ASecondActiveHoldIsRefused()
    {
        await SeedPendingRequestAsync();
        var ct = TestContext.Current.CancellationToken;
        await Service().PlaceHoldAsync(UserId, "First", AdminId, ct);

        var result = await Service().PlaceHoldAsync(UserId, "Second", AdminId, ct);

        Assert.Equal(ReviewActionStatus.Conflict, result.Status);
        Assert.Equal(1, await _db.AccountHolds.CountAsync(ct));
        Assert.Single(_events.Entries);
    }

    [Fact]
    public async Task AHoldNeedsAReasonAndAnExistingUser()
    {
        await SeedPendingRequestAsync();
        var ct = TestContext.Current.CancellationToken;

        var noReason = await Service().PlaceHoldAsync(UserId, " ", AdminId, ct);
        var tooLong = await Service().PlaceHoldAsync(UserId, new string('r', AccountDeletionReviewService.MaxHoldReasonLength + 1), AdminId, ct);
        var noUser = await Service().PlaceHoldAsync("no-such-user", "Reason", AdminId, ct);

        Assert.Equal(ReviewActionStatus.BadRequest, noReason.Status);
        Assert.Equal(ReviewActionStatus.BadRequest, tooLong.Status);
        Assert.Equal(ReviewActionStatus.NotFound, noUser.Status);
        Assert.False(await _db.AccountHolds.AnyAsync(ct));
        Assert.Empty(_events.Entries);
    }

    [Fact]
    public async Task ReleasingAHoldStampsItAndWritesAnEvent()
    {
        await SeedPendingRequestAsync();
        var ct = TestContext.Current.CancellationToken;
        await Service().PlaceHoldAsync(UserId, "Abuse report under review", AdminId, ct);
        _events.Entries.Clear();

        var result = await Service().ReleaseHoldAsync(UserId, "admin-2", ct);

        Assert.Equal(ReviewActionStatus.Ok, result.Status);
        _db.ChangeTracker.Clear();
        var hold = await _db.AccountHolds.AsNoTracking().SingleAsync(ct);
        Assert.NotNull(hold.ReleasedUtc);
        Assert.Equal("admin-2", hold.ReleasedByUserId);

        var entry = Assert.Single(_events.Entries);
        Assert.Equal(SecurityEventKind.AccountHoldReleased, entry.Kind);
        Assert.Equal(UserId, entry.UserId);
        Assert.Equal("Abuse report under review", entry.Detail);
    }

    [Fact]
    public async Task ReleasingWithoutAnActiveHoldIsNotFound()
    {
        await SeedPendingRequestAsync();

        var result = await Service().ReleaseHoldAsync(UserId, AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewActionStatus.NotFound, result.Status);
        Assert.Empty(_events.Entries);
    }

    private async Task<List<AdminUserAction>> AdminLogAsync()
    {
        _db.ChangeTracker.Clear();
        return await _db.AdminUserActions.AsNoTracking().OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApprovingWritesAnAdminLogRowThatOutlivesTheAccount()
    {
        long id = await SeedPendingRequestAsync();

        await Service().ApproveAsync(id, UserName, AdminId, TestContext.Current.CancellationToken);

        var row = Assert.Single(await AdminLogAsync());
        Assert.Equal(AdminUserActionKind.DeletionRequestApproved, row.Kind);
        Assert.Equal(AdminId, row.ActorUserId);
        Assert.Equal("admin-name", row.ActorUserName);
        Assert.Equal(UserId, row.SubjectUserId);
        Assert.Equal(UserName, row.SubjectUserName);
        Assert.DoesNotContain("@", row.Detail ?? string.Empty);
    }

    [Fact]
    public async Task DecliningWritesAnAdminLogRowWithTheNote()
    {
        long id = await SeedPendingRequestAsync();

        await Service().DeclineAsync(id, "Kept for the investigation.", AdminId, TestContext.Current.CancellationToken);

        var row = Assert.Single(await AdminLogAsync());
        Assert.Equal(AdminUserActionKind.DeletionRequestDeclined, row.Kind);
        Assert.Equal("Kept for the investigation.", row.Reason);
        Assert.Equal(UserName, row.SubjectUserName);
    }

    [Fact]
    public async Task PlacingAndReleasingAHoldWriteAdminLogRows()
    {
        await SeedPendingRequestAsync();
        var ct = TestContext.Current.CancellationToken;

        await Service().PlaceHoldAsync(UserId, "Abuse report under review", AdminId, ct);
        await Service().ReleaseHoldAsync(UserId, "admin-2", ct);
        await Service().PlaceHoldAsync(UserId, " ", AdminId, ct);

        var rows = await AdminLogAsync();
        Assert.Equal(new[] { AdminUserActionKind.HoldPlaced, AdminUserActionKind.HoldReleased }, rows.Select(r => r.Kind));
        Assert.Equal("admin-name", rows[0].ActorUserName);
        // An administrator id with no account is logged by its id.
        Assert.Equal("admin-2", rows[1].ActorUserName);
        Assert.All(rows, r => Assert.Equal(UserName, r.SubjectUserName));
    }

    [Fact]
    public async Task ARequestIsOverdueAtTwentyFiveDays()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime now = DateTime.UtcNow;
        _db.AccountDeletionRequests.AddRange(
            new AccountDeletionRequest { AspNetUserId = "u-25", UserNameSnapshot = "twentyfive", RequestedUtc = now.AddDays(-25).AddMinutes(-5), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Manual hold" },
            new AccountDeletionRequest { AspNetUserId = "u-24", UserNameSnapshot = "twentyfour", RequestedUtc = now.AddDays(-24).AddMinutes(-5), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Manual hold" },
            new AccountDeletionRequest { UserNameSnapshot = null, RequestedUtc = now.AddDays(-40), Status = AccountDeletionRequestStatus.Declined, HoldReasons = "Automatic: x" });
        await _db.SaveChangesAsync(ct);

        var page = await Service().ListAsync(null, null, 0, 20, ct);

        Assert.True(page.Rows.Single(r => r.UserName == "twentyfive").IsOverdue);
        Assert.Equal(25, page.Rows.Single(r => r.UserName == "twentyfive").AgeDays);
        Assert.False(page.Rows.Single(r => r.UserName == "twentyfour").IsOverdue);
        Assert.False(page.Rows.Single(r => r.Status == "Declined").IsOverdue);
        Assert.Equal(2, page.PendingCount);
        Assert.Equal(1, page.OverdueCount);
        Assert.True(page.ErasurePathsConfigured);
    }

    [Fact]
    public async Task CountPendingReturnsThePendingAndOverdueCountsAndTheNewestId()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var old = new AccountDeletionRequest { AspNetUserId = "u-old", UserNameSnapshot = "old", RequestedUtc = now.AddDays(-30), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Manual hold" };
        var declined = new AccountDeletionRequest { AspNetUserId = "u-declined", UserNameSnapshot = "declined", RequestedUtc = now.AddDays(-40), Status = AccountDeletionRequestStatus.Declined, HoldReasons = "Manual hold" };
        _db.AccountDeletionRequests.AddRange(old, declined);
        await _db.SaveChangesAsync(ct);
        var recent = new AccountDeletionRequest { AspNetUserId = "u-new", UserNameSnapshot = "new", RequestedUtc = now.AddDays(-1), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Automatic: x" };
        _db.AccountDeletionRequests.Add(recent);
        await _db.SaveChangesAsync(ct);

        var (pending, overdue, newestId) = await AccountDeletionReviewService.CountPendingAsync(_db, new AccountHoldOptions(), now, ct);

        Assert.Equal(2, pending);
        Assert.Equal(1, overdue);
        Assert.Equal(recent.Id, newestId);
    }

    [Fact]
    public async Task CountPendingWithNothingPendingIsEmpty()
    {
        var (pending, overdue, newestId) = await AccountDeletionReviewService.CountPendingAsync(
            _db, new AccountHoldOptions(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(0, pending);
        Assert.Equal(0, overdue);
        Assert.Null(newestId);
    }

    /// <summary>A ConfigHealthService over an in-memory database seeded with the given requests.</summary>
    private static (ConfigHealthService Health, ServiceProvider Provider) HealthWith(params AccountDeletionRequest[] requests)
    {
        string name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton(new AccountHoldOptions());
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AccountDeletionRequests.AddRange(requests);
            db.SaveChanges();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        return (new ConfigHealthService(config, provider.GetRequiredService<IServiceScopeFactory>()), provider);
    }

    [Fact]
    public void TheAdminAlertCountsPendingAndOverdueRequests()
    {
        DateTime now = DateTime.UtcNow;
        var old = new AccountDeletionRequest { AspNetUserId = "u-old", UserNameSnapshot = "old", RequestedUtc = now.AddDays(-26), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Manual hold" };
        var recent = new AccountDeletionRequest { AspNetUserId = "u-new", UserNameSnapshot = "new", RequestedUtc = now.AddDays(-1), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Automatic: x" };
        var declined = new AccountDeletionRequest { AspNetUserId = "u-declined", UserNameSnapshot = "declined", RequestedUtc = now.AddDays(-60), Status = AccountDeletionRequestStatus.Declined, HoldReasons = "Manual hold" };
        var (health, provider) = HealthWith(old, declined, recent);
        using var _ = provider;

        var alert = Assert.Single(health.GetSystemAlerts(), a => a.Id.StartsWith("account-deletion-review:", StringComparison.Ordinal));

        Assert.Equal($"account-deletion-review:{recent.Id}", alert.Id);
        Assert.Equal("2 account deletions await review (1 overdue).", alert.Message);
        Assert.Equal("/admin?tab=users&subtab=deletions", alert.LinkUrl);
        Assert.Equal("error", alert.Type);
    }

    [Fact]
    public void TheAdminAlertForOneRequestThatIsNotOverdueIsAWarning()
    {
        var recent = new AccountDeletionRequest { AspNetUserId = "u-new", UserNameSnapshot = "new", RequestedUtc = DateTime.UtcNow.AddDays(-1), Status = AccountDeletionRequestStatus.Pending, HoldReasons = "Manual hold" };
        var (health, provider) = HealthWith(recent);
        using var _ = provider;

        var alert = Assert.Single(health.GetSystemAlerts(), a => a.Id.StartsWith("account-deletion-review:", StringComparison.Ordinal));

        Assert.Equal("1 account deletion awaits review.", alert.Message);
        Assert.Equal("warning", alert.Type);
    }

    [Fact]
    public void NoPendingRequestsRaiseNoAlert()
    {
        var declined = new AccountDeletionRequest { AspNetUserId = "u", UserNameSnapshot = "x", RequestedUtc = DateTime.UtcNow.AddDays(-60), Status = AccountDeletionRequestStatus.Declined, HoldReasons = "Manual hold" };
        var (health, provider) = HealthWith(declined);
        using var _ = provider;

        Assert.DoesNotContain(health.GetSystemAlerts(), a => a.Id.StartsWith("account-deletion-review:", StringComparison.Ordinal));
    }
}
