using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Privacy;
using Overseer.Services.Security;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(DateTime.UtcNow.Ticks - DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Records security events, and runs <see cref="OnRecord"/> for each one.</summary>
internal sealed class RecordingAdminSecurityEvents : ISecurityEventRecorder
{
    public List<SecurityEventEntry> Entries { get; } = new();

    public Action<SecurityEventEntry>? OnRecord { get; set; }

    public void Record(SecurityEventEntry entry)
    {
        Entries.Add(entry);
        OnRecord?.Invoke(entry);
    }
}

/// <summary>Records the emails sent; <see cref="Throw"/> makes every send fail.</summary>
internal sealed class RecordingAdminEmailSender : IEmailSender
{
    public List<(string Email, string Subject, string Html)> Sent { get; } = new();

    public bool Throw { get; set; }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (Throw) throw new InvalidOperationException("The email service is down.");
        Sent.Add((email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}

/// <summary>Records the operations an execute queues; the test runs them itself.</summary>
internal sealed class RecordingBulkQueue : IAdminBulkOperationQueue
{
    public List<long> Queued { get; } = new();

    public void Enqueue(long operationId) => Queued.Add(operationId);
}

/// <summary>
/// The container the admin user-management tests share: SQLite in memory (the erasure runs set-based updates),
/// Identity with token providers, the review and access services, the bulk runner, and the incognito session store.
/// The acting administrator is "boss"; "deputy" is another administrator.
/// </summary>
internal sealed class AdminUserTestHost : IDisposable
{
    public const string ActorId = "actor-boss";
    public const string ActorName = "boss";
    public const string OtherAdminId = "admin-deputy";
    public const string OtherAdminName = "deputy";

    private readonly SqliteConnection _connection;
    private readonly IServiceScope _scope;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "AdminUserTests_" + Guid.NewGuid().ToString("N"));
    public ServiceProvider Provider { get; }
    public MutableTimeProvider Time { get; } = new();
    public RecordingAdminSecurityEvents Events { get; } = new();
    public RecordingBulkQueue Queue { get; } = new();
    public RecordingAdminEmailSender Emails { get; } = new();
    public AdminUserManagementOptions Options { get; } = new();
    public IConfiguration Configuration { get; }
    public EphemeralSessionStore Ephemeral { get; }

    public AdminUserTestHost(bool erasurePaths = true, bool registerEmailSender = true)
    {
        Directory.CreateDirectory(Path.Combine(Root, "dumplogs"));
        Directory.CreateDirectory(Path.Combine(Root, "conversations"));

        _connection = SqliteSchemaTemplate.OpenCopy();

        var values = new Dictionary<string, string?> { ["Admins"] = ActorName + ", " + OtherAdminName };
        if (erasurePaths)
        {
            values["DumpLogPath"] = Path.Combine(Root, "dumplogs");
            values["ConversationsDataLocation"] = Path.Combine(Root, "conversations");
        }
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Ephemeral = new EphemeralSessionStore(Configuration, startSweeper: false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddSingleton(Configuration);
        services.AddSingleton(new AccountHoldOptions());
        services.AddSingleton(Options);
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ISecurityEventRecorder>(Events);
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 }));
        services.AddSingleton<OverseerAccessService>();
        services.AddSingleton(Ephemeral);
        if (registerEmailSender) services.AddSingleton<IEmailSender>(Emails);
        services.AddScoped<AccountDeletionReviewService>();
        services.AddScoped<AdminUserManagementService>();
        services.AddScoped<AdminBulkUserOperationService>();
        services.AddSingleton<IAdminBulkOperationQueue>(Queue);
        services.AddSingleton<AdminBulkOperationRunner>();
        Provider = services.BuildServiceProvider();

        _scope = Provider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        Provider.Dispose();
        Ephemeral.Dispose();
        _connection.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public ApplicationDbContext Db => Services.GetRequiredService<ApplicationDbContext>();
    public AdminUserManagementService Users => Services.GetRequiredService<AdminUserManagementService>();
    public AdminBulkUserOperationService Bulk => Services.GetRequiredService<AdminBulkUserOperationService>();
    public AdminBulkOperationRunner Runner => Provider.GetRequiredService<AdminBulkOperationRunner>();
    public AdminActor Actor => new(ActorId, ActorName);
    public DateTime NowUtc => Time.Now.UtcDateTime;

    /// <summary>The two administrators.</summary>
    public async Task SeedAdministratorsAsync()
    {
        await AddUserAsync(ActorId, ActorName);
        await AddUserAsync(OtherAdminId, OtherAdminName);
    }

    /// <summary>A confirmed account registered a year ago that never signed in, unless <paramref name="configure"/> says otherwise.</summary>
    public async Task<ApplicationUser> AddUserAsync(string id, string name, Action<ApplicationUser>? configure = null)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = name + "@example.com",
            NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            CreatedUtc = NowUtc.AddDays(-365)
        };
        configure?.Invoke(user);
        Db.Users.Add(user);
        await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Db.ChangeTracker.Clear();
        return user;
    }

    public async Task<ApplicationUser?> ReadUserAsync(string id)
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.OfType<ApplicationUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, TestContext.Current.CancellationToken);
    }

    public async Task<List<AdminUserAction>> AuditRowsAsync(string subjectUserId)
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AdminUserActions.AsNoTracking().Where(a => a.SubjectUserId == subjectUserId).OrderBy(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task AddHoldAsync(string userId)
    {
        Db.AccountHolds.Add(new AccountHold { AspNetUserId = userId, Reason = "Abuse report", PlacedByUserId = ActorId, PlacedUtc = NowUtc });
        await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Db.ChangeTracker.Clear();
    }

    public async Task AddPendingRequestAsync(string userId, string userName)
    {
        Db.AccountDeletionRequests.Add(new AccountDeletionRequest
        {
            AspNetUserId = userId,
            UserNameSnapshot = userName,
            RequestedUtc = NowUtc.AddDays(-1),
            Status = AccountDeletionRequestStatus.Pending,
            HoldReasons = "Manual hold"
        });
        await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Db.ChangeTracker.Clear();
    }

    public async Task AddGameLogAsync(string userId)
    {
        Db.GameLog.Add(new GameLog { AspNetUserId = userId, Name = "x", CreatedDate = NowUtc });
        await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Db.ChangeTracker.Clear();
    }

    public async Task AddChatAsync(string userId)
    {
        Db.ChatSession.Add(new ChatSession { AspNetUserId = userId, Title = "Chat", CreatedUtc = NowUtc, LastMessageUtc = NowUtc });
        await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Db.ChangeTracker.Clear();
    }

    /// <summary>An open incognito session owned by <paramref name="userId"/>, held only in <see cref="Ephemeral"/>.</summary>
    public EphemeralSession OpenIncognito(string userId) => Ephemeral.Create(userId, new ChatSession
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    });
}

/// <summary>
/// The per-user admin actions: each refuses self and administrator targets as specified, changes exactly its
/// columns, and writes one admin-log row and its security event; deletion refuses on every refusal code, a wrong
/// typed name and a stale acknowledgment set, and logs before it erases.
/// </summary>
public sealed class AdminUserManagementServiceTests : IDisposable
{
    private const string TargetId = "target-1";
    private const string TargetName = "victim";

    private readonly AdminUserTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<ApplicationUser> SeedTargetAsync(Action<ApplicationUser>? configure = null) => _host.AddUserAsync(TargetId, TargetName, configure);

    // --- Self and administrator targets ---

    public static TheoryData<string> RefusedForSelfAndAdministrators => new()
    {
        "lockout", "unlock", "ban", "unban", "confirm-email", "reset-two-factor", "change-email", "block", "delete"
    };

    private static async Task<(AdminResultStatus Status, string? Code)> RunForCodeAsync(AdminUserManagementService users, AdminActor actor,
        AdminUserTestHost host, string action, string id)
    {
        var ct = TestContext.Current.CancellationToken;
        switch (action)
        {
            case "lockout": { var r = await users.LockOutAsync(actor, id, host.Time.Now.AddDays(3), "Spam", ct: ct); return (r.Status, r.Code); }
            case "unlock": { var r = await users.UnlockAsync(actor, id, ct: ct); return (r.Status, r.Code); }
            case "ban": { var r = await users.BanAsync(actor, id, "Cheating", ct: ct); return (r.Status, r.Code); }
            case "unban": { var r = await users.UnbanAsync(actor, id, ct: ct); return (r.Status, r.Code); }
            case "confirm-email": { var r = await users.ConfirmEmailAsync(actor, id, ct); return (r.Status, r.Code); }
            case "reset-two-factor": { var r = await users.ResetTwoFactorAsync(actor, id, null, ct); return (r.Status, r.Code); }
            case "change-email": { var r = await users.ChangeEmailAsync(actor, id, "new@example.com", false, "Lost mailbox", ct); return (r.Status, r.Code); }
            case "block": { var r = await users.BlockFromAiChatAsync(actor, id, "Abuse", ct); return (r.Status, r.Code); }
            case "sign-out": { var r = await users.SignOutEverywhereAsync(actor, id, ct: ct); return (r.Status, r.Code); }
            case "game-restrictions": { var r = await users.SetGameRestrictionsAsync(actor, id, true, false, null, ct); return (r.Status, r.Code); }
            case "delete": { var r = await users.DeleteAsync(actor, id, id == AdminUserTestHost.ActorId ? AdminUserTestHost.ActorName : AdminUserTestHost.OtherAdminName, "Spam", Array.Empty<string>(), ct); return (r.Status, r.Code); }
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    [Theory]
    [MemberData(nameof(RefusedForSelfAndAdministrators))]
    public async Task AnActionRefusesTheAdministratorsOwnAccount(string action)
    {
        await _host.SeedAdministratorsAsync();

        var (status, code) = await RunForCodeAsync(_host.Users, _host.Actor, _host, action, AdminUserTestHost.ActorId);

        Assert.Equal(AdminResultStatus.Conflict, status);
        Assert.Equal(AdminRefusalCodes.Self, code);
        Assert.Empty(await _host.AuditRowsAsync(AdminUserTestHost.ActorId));
        Assert.Empty(_host.Events.Entries);
        Assert.Empty(_host.Emails.Sent);
    }

    [Theory]
    [MemberData(nameof(RefusedForSelfAndAdministrators))]
    [InlineData("sign-out")]
    public async Task AnActionRefusesAnotherAdministrator(string action)
    {
        await _host.SeedAdministratorsAsync();

        var (status, code) = await RunForCodeAsync(_host.Users, _host.Actor, _host, action, AdminUserTestHost.OtherAdminId);

        Assert.Equal(AdminResultStatus.Conflict, status);
        Assert.Equal(AdminRefusalCodes.Administrator, code);
        Assert.Empty(await _host.AuditRowsAsync(AdminUserTestHost.OtherAdminId));
        Assert.Empty(_host.Emails.Sent);
        var deputy = await _host.ReadUserAsync(AdminUserTestHost.OtherAdminId);
        Assert.NotNull(deputy);
        Assert.NotEqual(true, deputy!.IsBanned);
    }

    [Fact]
    public async Task SignOutEverywhereIsAllowedOnTheAdministratorsOwnAccount()
    {
        await _host.SeedAdministratorsAsync();
        string? stamp = (await _host.ReadUserAsync(AdminUserTestHost.ActorId))!.SecurityStamp;

        var (status, _) = await RunForCodeAsync(_host.Users, _host.Actor, _host, "sign-out", AdminUserTestHost.ActorId);

        Assert.Equal(AdminResultStatus.Ok, status);
        Assert.NotEqual(stamp, (await _host.ReadUserAsync(AdminUserTestHost.ActorId))!.SecurityStamp);
        Assert.Equal(AdminUserActionKind.SignedOutEverywhere, Assert.Single(await _host.AuditRowsAsync(AdminUserTestHost.ActorId)).Kind);
        Assert.Equal(SecurityEventKind.AdminSignedOut, Assert.Single(_host.Events.Entries).Kind);
    }

    [Theory]
    [InlineData(AdminUserTestHost.ActorId)]
    [InlineData(AdminUserTestHost.OtherAdminId)]
    public async Task GameRestrictionsAreAllowedOnSelfAndOnAdministrators(string id)
    {
        await _host.SeedAdministratorsAsync();

        var (status, _) = await RunForCodeAsync(_host.Users, _host.Actor, _host, "game-restrictions", id);

        Assert.Equal(AdminResultStatus.Ok, status);
        var user = await _host.ReadUserAsync(id);
        Assert.True(user!.IsGameLogBanned);
        Assert.False(user.IsBonesBanned);
        Assert.Equal(AdminUserActionKind.GameRestrictionsChanged, Assert.Single(await _host.AuditRowsAsync(id)).Kind);
    }

    // --- Lockout ---

    [Fact]
    public async Task LockOutSetsTheLockoutTheAdminColumnsAndANewStamp_AndWritesOneRowAndItsEvent()
    {
        await _host.SeedAdministratorsAsync();
        var seeded = await SeedTargetAsync();
        var until = _host.Time.Now.AddDays(7);

        var result = await _host.Users.LockOutAsync(_host.Actor, TargetId, until, "  Spamming the forum  ", ct: TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.True(user!.LockoutEnabled);
        Assert.Equal(until, user.LockoutEnd);
        Assert.Equal(until, user.AdminLockoutEndUtc);
        Assert.Equal("Spamming the forum", user.AdminLockoutReason);
        Assert.Equal(AdminUserTestHost.ActorId, user.AdminLockoutByUserId);
        Assert.NotEqual(seeded.SecurityStamp, user.SecurityStamp);

        var row = Assert.Single(await _host.AuditRowsAsync(TargetId));
        Assert.Equal(AdminUserActionKind.LockedOut, row.Kind);
        Assert.Equal(AdminUserTestHost.ActorId, row.ActorUserId);
        Assert.Equal(AdminUserTestHost.ActorName, row.ActorUserName);
        Assert.Equal(TargetName, row.SubjectUserName);
        Assert.Equal("Spamming the forum", row.Reason);
        Assert.StartsWith("Before: In good standing. After: Locked out until ", row.Detail);
        Assert.Null(row.BulkOperationId);

        var entry = Assert.Single(_host.Events.Entries);
        Assert.Equal(SecurityEventKind.AdminLockedOut, entry.Kind);
        Assert.Equal(TargetId, entry.UserId);

        Assert.Equal("In good standing", result.Value!.Before.Text);
        Assert.Contains(AdminUserStatusKind.LockedOut, result.Value.After.Statuses);
        Assert.True(result.Value.User!.LockoutIsAdminSet);
        Assert.Equal("Spamming the forum", result.Value.User.AdminLockoutReason);
    }

    [Theory]
    [InlineData(-60.0)]
    [InlineData(0.0)]
    [InlineData(366.0 * 24 * 60)]
    public async Task LockOutRefusesAnEndOutsideTheAllowedRange(double minutesAhead)
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.LockOutAsync(_host.Actor, TargetId, _host.Time.Now.AddMinutes(minutesAhead), "Spam",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal(AdminRefusalCodes.Until, result.Code);
        Assert.Null((await _host.ReadUserAsync(TargetId))!.LockoutEnd);
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
    }

    [Fact]
    public async Task LockOutAcceptsTheMaximumAndRequiresAReason()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        var ct = TestContext.Current.CancellationToken;

        var noReason = await _host.Users.LockOutAsync(_host.Actor, TargetId, _host.Time.Now.AddDays(1), "  ", ct: ct);
        var max = await _host.Users.LockOutAsync(_host.Actor, TargetId, _host.Time.Now.AddDays(_host.Options.LockoutMaxDays), "Spam", ct: ct);

        Assert.Equal(AdminResultStatus.BadRequest, noReason.Status);
        Assert.Equal(AdminRefusalCodes.Reason, noReason.Code);
        Assert.Equal(AdminResultStatus.Ok, max.Status);
    }

    [Fact]
    public async Task UnlockEndsAnAutomaticLockoutAndResetsTheFailedCount()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync(u =>
        {
            u.LockoutEnd = new DateTimeOffset(DateTime.UtcNow.AddMinutes(10), TimeSpan.Zero);
            u.AccessFailedCount = 7;
        });

        var result = await _host.Users.UnlockAsync(_host.Actor, TargetId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.Null(user!.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.Null(user.AdminLockoutEndUtc);
        Assert.Equal(AdminUserActionKind.Unlocked, Assert.Single(await _host.AuditRowsAsync(TargetId)).Kind);
        Assert.Equal(SecurityEventKind.AdminUnlocked, Assert.Single(_host.Events.Entries).Kind);
    }

    [Fact]
    public async Task UnlockingAnAccountThatIsNotLockedOutIsAlreadyDone()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.UnlockAsync(_host.Actor, TargetId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(AdminRefusalCodes.AlreadyDone, result.Code);
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
    }

    // --- Ban ---

    [Fact]
    public async Task BanSetsAllFourColumnsAndRenewsTheStamp_AndUnbanClearsThem()
    {
        await _host.SeedAdministratorsAsync();
        var seeded = await SeedTargetAsync();
        var ct = TestContext.Current.CancellationToken;

        var banned = await _host.Users.BanAsync(_host.Actor, TargetId, "Cheating", ct: ct);

        Assert.Equal(AdminResultStatus.Ok, banned.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.True(user!.IsBanned);
        Assert.NotNull(user.BannedUtc);
        Assert.Equal("Cheating", user.BanReason);
        Assert.Equal(AdminUserTestHost.ActorId, user.BannedByUserId);
        Assert.NotEqual(seeded.SecurityStamp, user.SecurityStamp);
        Assert.Contains(AdminUserStatusKind.Banned, banned.Value!.After.Statuses);

        var again = await _host.Users.BanAsync(_host.Actor, TargetId, "Again", ct: ct);
        Assert.Equal(AdminRefusalCodes.AlreadyDone, again.Code);

        var unbanned = await _host.Users.UnbanAsync(_host.Actor, TargetId, ct: ct);

        Assert.Equal(AdminResultStatus.Ok, unbanned.Status);
        user = await _host.ReadUserAsync(TargetId);
        Assert.False(user!.IsBanned);
        Assert.Null(user.BannedUtc);
        Assert.Null(user.BanReason);
        Assert.Null(user.BannedByUserId);

        var rows = await _host.AuditRowsAsync(TargetId);
        Assert.Equal(new[] { AdminUserActionKind.Banned, AdminUserActionKind.Unbanned }, rows.Select(r => r.Kind));
        Assert.Equal(new[] { SecurityEventKind.AdminBanned, SecurityEventKind.AdminUnbanned }, _host.Events.Entries.Select(e => e.Kind));
    }

    // --- Email and two-factor ---

    [Fact]
    public async Task ConfirmEmailMarksTheAddressConfirmed()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync(u => u.EmailConfirmed = false);

        var result = await _host.Users.ConfirmEmailAsync(_host.Actor, TargetId, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.True((await _host.ReadUserAsync(TargetId))!.EmailConfirmed);
        Assert.Equal(SecurityEventKind.AdminEmailConfirmed, Assert.Single(_host.Events.Entries).Kind);
    }

    [Fact]
    public async Task ResetTwoFactorTurnsItOffAndRenewsTheStamp()
    {
        await _host.SeedAdministratorsAsync();
        var seeded = await SeedTargetAsync(u => u.TwoFactorEnabled = true);

        var result = await _host.Users.ResetTwoFactorAsync(_host.Actor, TargetId, "Lost phone", TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.False(user!.TwoFactorEnabled);
        Assert.NotEqual(seeded.SecurityStamp, user.SecurityStamp);
        Assert.Equal("Lost phone", Assert.Single(await _host.AuditRowsAsync(TargetId)).Reason);
    }

    private const string OldEmail = TargetName + "@example.com";
    private const string NewEmail = "new@example.com";

    [Fact]
    public async Task ChangeEmailReplacesTheAddressUnconfirmedAndKeepsTheUserName()
    {
        await _host.SeedAdministratorsAsync();
        var seeded = await SeedTargetAsync();

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, "  " + NewEmail + "  ", false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.Equal(NewEmail, user!.Email);
        Assert.Equal(NewEmail.ToUpperInvariant(), user.NormalizedEmail);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(TargetName, user.UserName);
        Assert.NotEqual(seeded.SecurityStamp, user.SecurityStamp);

        var row = Assert.Single(await _host.AuditRowsAsync(TargetId));
        Assert.Equal(AdminUserActionKind.EmailChanged, row.Kind);
        Assert.Equal("Lost mailbox", row.Reason);
        Assert.DoesNotContain(OldEmail, row.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(NewEmail, row.Detail!, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(_host.Events.Entries);
        Assert.Equal(SecurityEventKind.AdminEmailChanged, entry.Kind);
        Assert.Contains(AdminUserStatusKind.EmailUnconfirmed, result.Value!.After.Statuses);
    }

    [Fact]
    public async Task ChangeEmailEmailsTheNewAddressTheInstructions()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, NewEmail, false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        var sent = Assert.Single(_host.Emails.Sent);
        Assert.Equal(NewEmail, sent.Email);
        Assert.Contains("https://account.gnollhack.com/Identity/Account/ResendEmailConfirmation", sent.Html);
        Assert.Contains(TargetName, sent.Html);
        Assert.StartsWith("Email changed. Instructions", result.Value!.Summary);
    }

    [Fact]
    public async Task ChangeEmailCanMarkTheNewAddressConfirmed()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, NewEmail, true, "Verified by phone",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.Equal(NewEmail, user!.Email);
        Assert.True(user.EmailConfirmed);
        Assert.DoesNotContain(AdminUserStatusKind.EmailUnconfirmed, result.Value!.After.Statuses);
        Assert.Empty(_host.Emails.Sent);
    }

    [Fact]
    public async Task ChangeEmailStandsWhenTheInstructionsCannotBeSent()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        _host.Emails.Throw = true;

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, NewEmail, false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.StartsWith("Email changed, but the instructions email could not be sent.", result.Value!.Summary);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.Equal(NewEmail, user!.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(AdminUserActionKind.EmailChanged, Assert.Single(await _host.AuditRowsAsync(TargetId)).Kind);
        Assert.Equal(SecurityEventKind.AdminEmailChanged, Assert.Single(_host.Events.Entries).Kind);
    }

    [Fact]
    public async Task ChangeEmailStandsWhenNoEmailSenderIsConfigured()
    {
        using var host = new AdminUserTestHost(registerEmailSender: false);
        await host.SeedAdministratorsAsync();
        await host.AddUserAsync(TargetId, TargetName);

        var result = await host.Users.ChangeEmailAsync(host.Actor, TargetId, NewEmail, false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.StartsWith("Email changed, but the instructions email could not be sent.", result.Value!.Summary);
        Assert.Equal(NewEmail, (await host.ReadUserAsync(TargetId))!.Email);
        Assert.Single(await host.AuditRowsAsync(TargetId));
    }

    [Fact]
    public async Task ChangeEmailRefusesAnAddressAnotherAccountUses()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        await _host.AddUserAsync("other-1", "other");

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, "Other@Example.com", false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(AdminRefusalCodes.EmailTaken, result.Code);
        var user = await _host.ReadUserAsync(TargetId);
        Assert.Equal(OldEmail, user!.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
        Assert.Empty(_host.Events.Entries);
        Assert.Empty(_host.Emails.Sent);
    }

    [Fact]
    public async Task ChangeEmailRefusesTheCurrentAddress()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, OldEmail.ToUpperInvariant(), false, "Lost mailbox",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(AdminRefusalCodes.AlreadyDone, result.Code);
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
    }

    public static TheoryData<string?, string, string> InvalidChangeEmailInputs => new()
    {
        { null, NewEmail, AdminRefusalCodes.Reason },
        { "Lost mailbox", "", AdminRefusalCodes.Email },
        { "Lost mailbox", "not-an-address", AdminRefusalCodes.Email },
        { "Lost mailbox", new string('a', 257 - "@example.com".Length) + "@example.com", AdminRefusalCodes.Email },
    };

    [Theory]
    [MemberData(nameof(InvalidChangeEmailInputs))]
    public async Task ChangeEmailValidatesTheReasonAndTheAddress(string? reason, string email, string code)
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.ChangeEmailAsync(_host.Actor, TargetId, email, false, reason,
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal(code, result.Code);
        Assert.Equal(OldEmail, (await _host.ReadUserAsync(TargetId))!.Email);
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
        Assert.Empty(_host.Emails.Sent);
    }

    [Fact]
    public async Task TheCardCarriesTheJunethackName()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync(u => u.JunetHackUserName = "gnoll_fan");

        var result = await _host.Users.GetAsync(_host.Actor, TargetId, TestContext.Current.CancellationToken);

        Assert.Equal("gnoll_fan", result.Value!.JunetHackUserName);
    }

    // --- The AI-chat block through this service ---

    [Fact]
    public async Task BlockingFromTheAiChatWritesOneAdminLogRow()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.BlockFromAiChatAsync(_host.Actor, TargetId, "Abusive prompts", TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Contains(AdminUserStatusKind.AiChatBlocked, result.Value!.After.Statuses);
        var row = Assert.Single(await _host.AuditRowsAsync(TargetId));
        Assert.Equal(AdminUserActionKind.AiChatBlocked, row.Kind);
        Assert.Equal(AdminUserTestHost.ActorName, row.ActorUserName);
    }

    // --- Deletion ---

    [Fact]
    public async Task DeleteErasesTheAccountAndLogsItsNameAndCounts()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        await _host.AddGameLogAsync(TargetId);

        var result = await _host.Users.DeleteAsync(_host.Actor, TargetId, " " + TargetName + " ", "Spam account",
            new[] { AdminUserManagementService.CategoryGameLogs }, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.True(result.Value!.Deleted);
        Assert.Null(result.Value.User);
        Assert.Null(await _host.ReadUserAsync(TargetId));

        var row = Assert.Single(await _host.AuditRowsAsync(TargetId));
        Assert.Equal(AdminUserActionKind.Deleted, row.Kind);
        Assert.Equal(TargetName, row.SubjectUserName);
        Assert.Equal("Spam account", row.Reason);
        Assert.StartsWith("Deleted victim. Game logs: 1", row.Detail);
        Assert.DoesNotContain("@", row.Detail);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("pending-request")]
    public async Task DeleteIsRefusedWhileTheAccountIsHeldOrHasAPendingRequest(string code)
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        if (code == AdminRefusalCodes.Hold) await _host.AddHoldAsync(TargetId);
        else await _host.AddPendingRequestAsync(TargetId, TargetName);

        var impact = await _host.Users.GetDeletionImpactAsync(_host.Actor, TargetId, TestContext.Current.CancellationToken);
        var result = await _host.Users.DeleteAsync(_host.Actor, TargetId, TargetName, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Contains(impact.Value!.Refusals, r => r.Code == code);
        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(code, result.Code);
        Assert.NotNull(await _host.ReadUserAsync(TargetId));
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
    }

    [Fact]
    public async Task DeleteIsRefusedWhileTheErasurePathsAreMissing()
    {
        using var host = new AdminUserTestHost(erasurePaths: false);
        await host.SeedAdministratorsAsync();
        await host.AddUserAsync(TargetId, TargetName);

        var result = await host.Users.DeleteAsync(host.Actor, TargetId, TargetName, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(AdminRefusalCodes.ErasurePaths, result.Code);
        Assert.NotNull(await host.ReadUserAsync(TargetId));
    }

    [Theory]
    [InlineData("Victim")]
    [InlineData("someone-else")]
    [InlineData("")]
    [InlineData(null)]
    public async Task DeleteIsRefusedOnAWrongTypedName(string? typed)
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();

        var result = await _host.Users.DeleteAsync(_host.Actor, TargetId, typed, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal(AdminRefusalCodes.ConfirmName, result.Code);
        Assert.NotNull(await _host.ReadUserAsync(TargetId));
    }

    /// <summary>The account holds game logs and a chat, so exactly those two must be acknowledged.</summary>
    public static TheoryData<string[]> WrongAcknowledgments => new()
    {
        Array.Empty<string>(),
        new[] { AdminUserManagementService.CategoryGameLogs },
        new[] { AdminUserManagementService.CategoryChats },
        new[] { AdminUserManagementService.CategoryGameLogs, AdminUserManagementService.CategoryChats, AdminUserManagementService.CategoryBones },
        new[] { AdminUserManagementService.CategoryGameLogs, AdminUserManagementService.CategoryChats, AdminUserManagementService.CategoryAiUsage }
    };

    [Theory]
    [MemberData(nameof(WrongAcknowledgments))]
    public async Task DeleteIsRefusedWhenTheAcknowledgedCategoriesAreNotExactlyTheImportantOnes(string[] acknowledged)
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        await _host.AddGameLogAsync(TargetId);
        await _host.AddChatAsync(TargetId);

        var result = await _host.Users.DeleteAsync(_host.Actor, TargetId, TargetName, "Spam", acknowledged, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal(AdminRefusalCodes.Acknowledgments, result.Code);
        Assert.NotNull(await _host.ReadUserAsync(TargetId));
        Assert.Empty(await _host.AuditRowsAsync(TargetId));
    }

    private sealed class FailingDeleteUserManager : UserManager<ApplicationUser>
    {
        public FailingDeleteUserManager(IServiceProvider sp)
            : base(sp.GetRequiredService<IUserStore<ApplicationUser>>(), sp.GetRequiredService<IOptions<IdentityOptions>>(),
                sp.GetRequiredService<IPasswordHasher<ApplicationUser>>(), sp.GetServices<IUserValidator<ApplicationUser>>(),
                sp.GetServices<IPasswordValidator<ApplicationUser>>(), sp.GetRequiredService<ILookupNormalizer>(),
                sp.GetRequiredService<IdentityErrorDescriber>(), sp, NullLogger<UserManager<ApplicationUser>>.Instance)
        {
        }

        public override Task<IdentityResult> DeleteAsync(ApplicationUser user) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Test", Description = "The store refused the delete." }));
    }

    [Fact]
    public async Task DeleteWritesTheAuditRowEvenWhenTheErasureThrows()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        var sp = _host.Services;
        var users = new AdminUserManagementService(_host.Db, new FailingDeleteUserManager(sp), _host.Configuration, _host.Options,
            sp.GetRequiredService<AccountDeletionReviewService>(), sp.GetRequiredService<OverseerAccessService>(),
            NullLogger<AdminUserManagementService>.Instance, _host.Events, _host.Time);

        var result = await users.DeleteAsync(_host.Actor, TargetId, TargetName, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Failed, result.Status);
        Assert.Equal(AdminRefusalCodes.ErasureFailed, result.Code);
        Assert.NotNull(await _host.ReadUserAsync(TargetId));
        var rows = await _host.AuditRowsAsync(TargetId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(AdminUserActionKind.Deleted, r.Kind));
        Assert.StartsWith("Deleted victim.", rows[0].Detail);
        Assert.StartsWith("Deletion failed: ", rows[1].Detail);
    }

    [Fact]
    public async Task DeleteClosesEveryIncognitoSessionOfTheAccountAndNoOtherAccounts()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        var first = _host.OpenIncognito(TargetId);
        var second = _host.OpenIncognito(TargetId);
        var bystander = _host.OpenIncognito(AdminUserTestHost.OtherAdminId);

        var result = await _host.Users.DeleteAsync(_host.Actor, TargetId, TargetName, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Null(_host.Ephemeral.Get(first.Ref, TargetId));
        Assert.Null(_host.Ephemeral.Get(second.Ref, TargetId));
        Assert.True(first.IsErased);
        Assert.True(second.IsErased);
        Assert.NotNull(_host.Ephemeral.Get(bystander.Ref, AdminUserTestHost.OtherAdminId));
        Assert.False(bystander.IsErased);
    }

    [Fact]
    public async Task AFailedDeletionLeavesTheAccountsIncognitoSessionsOpen()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        var held = _host.OpenIncognito(TargetId);
        var sp = _host.Services;
        var users = new AdminUserManagementService(_host.Db, new FailingDeleteUserManager(sp), _host.Configuration, _host.Options,
            sp.GetRequiredService<AccountDeletionReviewService>(), sp.GetRequiredService<OverseerAccessService>(),
            NullLogger<AdminUserManagementService>.Instance, _host.Events, _host.Time, _host.Ephemeral);

        var result = await users.DeleteAsync(_host.Actor, TargetId, TargetName, "Spam", Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Failed, result.Status);
        Assert.NotNull(await _host.ReadUserAsync(TargetId));
        Assert.NotNull(_host.Ephemeral.Get(held.Ref, TargetId));
        Assert.False(held.IsErased);
    }

    [Fact]
    public async Task TheImpactCountsWhatADeletionWouldErase()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        await _host.AddGameLogAsync(TargetId);
        await _host.AddGameLogAsync(TargetId);
        await _host.AddChatAsync(TargetId);

        var impact = await _host.Users.GetDeletionImpactAsync(_host.Actor, TargetId, TestContext.Current.CancellationToken);

        Assert.Empty(impact.Value!.Refusals);
        var byKey = impact.Value.Categories.ToDictionary(c => c.Key);
        Assert.Equal(2, byKey[AdminUserManagementService.CategoryGameLogs].Count);
        Assert.True(byKey[AdminUserManagementService.CategoryGameLogs].Important);
        Assert.Equal(1, byKey[AdminUserManagementService.CategoryChats].Count);
        Assert.False(byKey[AdminUserManagementService.CategoryBenchmarkRecords].Important);
    }

    // --- The list ---

    [Fact]
    public async Task TheListSearchesEmailClampsTheTakeAndMarksSelfAndAdministrators()
    {
        await _host.SeedAdministratorsAsync();
        await SeedTargetAsync();
        var ct = TestContext.Current.CancellationToken;

        var byEmail = await _host.Users.ListAsync(_host.Actor, new AdminUserCriteria { Search = "VICTIM@EXAMPLE" }, "name", 0, 10, ct);
        var all = await _host.Users.ListAsync(_host.Actor, new AdminUserCriteria(), "name", -5, 1000, ct);

        Assert.Equal(TargetId, Assert.Single(byEmail.Rows).Id);
        Assert.Equal(1, byEmail.TotalCount);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(new[] { "boss", "deputy", "victim" }, all.Rows.Select(r => r.UserName));
        Assert.True(all.Rows[0].IsSelf);
        Assert.True(all.Rows[1].IsAdministrator);
        Assert.Contains(AdminUserStatusKind.Administrator, all.Rows[1].Statuses);
        Assert.Equal(new[] { AdminUserStatusKind.GoodStanding }, all.Rows[2].Statuses);
    }
}
