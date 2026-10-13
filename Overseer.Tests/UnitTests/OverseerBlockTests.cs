using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Security;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Security;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// An administrator's block on a user's Overseer AI access: the service that sets, lifts and caches it, the
/// action filter, the game client's session-create endpoint, and the chat turn itself.
/// </summary>
public class OverseerBlockTests : IDisposable
{
    private const string AdminId = "admin-1";
    private const string TargetId = "target-user-1";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-block-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    // ── Shared fixtures ─────────────────────────────────────────────────────────

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        public List<SecurityEventEntry> Entries { get; } = new();

        public void Record(SecurityEventEntry entry)
        {
            lock (Entries) Entries.Add(entry);
        }
    }

    private static ServiceProvider BuildDbServices(string databaseName, Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task SeedUserAsync(IServiceProvider sp, string userId, DateTime? blockedUtc = null, string? reason = null)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "user-" + userId,
            OverseerBlockedUtc = blockedUtc,
            OverseerBlockReason = reason,
            OverseerBlockedByUserId = blockedUtc.HasValue ? AdminId : null
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ApplicationUser> ReadUserAsync(IServiceProvider sp, string userId)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.OfType<ApplicationUser>().AsNoTracking()
            .SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
    }

    /// <summary>Changes the block columns directly, bypassing the service and its cache eviction.</summary>
    private static async Task WriteBlockDirectlyAsync(IServiceProvider sp, string userId, DateTime? blockedUtc, string? reason)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.OfType<ApplicationUser>()
            .SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        user.OverseerBlockedUtc = blockedUtc;
        user.OverseerBlockReason = reason;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static MemoryCache NewSizedCache() => new(new MemoryCacheOptions { SizeLimit = 10000 });

    private static OverseerAccessService NewAccessService(ServiceProvider sp, IMemoryCache cache, ISecurityEventRecorder? recorder = null)
        => new(sp.GetRequiredService<IServiceScopeFactory>(), cache, recorder);

    private static object? ReadProperty(object value, string name)
        => value.GetType().GetProperty(name)!.GetValue(value);

    // ── OverseerAccessService: BlockAsync / UnblockAsync ────────────────────────

    [Fact]
    public async Task BlockAsync_WritesTheBlockFieldsAndRecordsAdminBlocked()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        var before = DateTime.UtcNow;
        string? error = await access.BlockAsync(TargetId, "  Abusive prompts  ", AdminId);

        Assert.Null(error);
        var user = await ReadUserAsync(sp, TargetId);
        Assert.NotNull(user.OverseerBlockedUtc);
        Assert.InRange(user.OverseerBlockedUtc!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("Abusive prompts", user.OverseerBlockReason);
        Assert.Equal(AdminId, user.OverseerBlockedByUserId);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(SecurityEventKind.AdminBlocked, entry.Kind);
        Assert.Equal(TargetId, entry.UserId);
        Assert.Equal("Abusive prompts", entry.Detail);
    }

    [Fact]
    public async Task BlockAsync_RefusesTheAdministratorsOwnAccount()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, AdminId);
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        string? error = await access.BlockAsync(AdminId, "Testing", AdminId);

        Assert.NotNull(error);
        var user = await ReadUserAsync(sp, AdminId);
        Assert.Null(user.OverseerBlockedUtc);
        Assert.Null(user.OverseerBlockReason);
        Assert.Null(user.OverseerBlockedByUserId);
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task BlockAsync_RefusesAReasonOver256Characters()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        string? error = await access.BlockAsync(TargetId, new string('x', OverseerAccessService.MaxReasonLength + 1), AdminId);

        Assert.NotNull(error);
        Assert.Null((await ReadUserAsync(sp, TargetId)).OverseerBlockedUtc);
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task BlockAsync_AcceptsAReasonOfExactly256Characters()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);

        string reason = new string('y', OverseerAccessService.MaxReasonLength);
        Assert.Equal(256, reason.Length);

        Assert.Null(await access.BlockAsync(TargetId, reason, AdminId));
        Assert.Equal(reason, (await ReadUserAsync(sp, TargetId)).OverseerBlockReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlockAsync_RefusesAnEmptyReason(string reason)
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        Assert.NotNull(await access.BlockAsync(TargetId, reason, AdminId));
        Assert.Null((await ReadUserAsync(sp, TargetId)).OverseerBlockedUtc);
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task BlockAsync_RefusesAnUnknownUser()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        Assert.NotNull(await access.BlockAsync("no-such-user", "Reason", AdminId));
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task UnblockAsync_ClearsTheBlockFieldsAndRecordsAdminUnblocked()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId, DateTime.UtcNow.AddHours(-1), "Abusive prompts");
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        Assert.Null(await access.UnblockAsync(TargetId, AdminId));

        var user = await ReadUserAsync(sp, TargetId);
        Assert.Null(user.OverseerBlockedUtc);
        Assert.Null(user.OverseerBlockReason);
        Assert.Null(user.OverseerBlockedByUserId);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(SecurityEventKind.AdminUnblocked, entry.Kind);
        Assert.Equal(TargetId, entry.UserId);
        Assert.Equal("by " + AdminId, entry.Detail);
    }

    [Fact]
    public async Task UnblockAsync_OfAUserWhoIsNotBlocked_RecordsNothing()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        Assert.Null(await access.UnblockAsync(TargetId, AdminId));
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task UnblockAsync_RefusesAnUnknownUser()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        using var cache = NewSizedCache();
        var recorder = new RecordingSecurityEvents();
        var access = NewAccessService(sp, cache, recorder);

        Assert.NotNull(await access.UnblockAsync("no-such-user", AdminId));
        Assert.Empty(recorder.Entries);
    }

    private static async Task<List<AdminUserAction>> ReadAdminLogAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AdminUserActions.AsNoTracking().OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BlockAndUnblock_WriteAnAdminLogRowWithTheAdministrator()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        await SeedUserAsync(sp, AdminId);
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);

        Assert.Null(await access.BlockAsync(TargetId, "Abusive prompts", AdminId));
        Assert.Null(await access.UnblockAsync(TargetId, AdminId));
        // Lifting a block that is not there is not a change.
        Assert.Null(await access.UnblockAsync(TargetId, AdminId));

        var rows = await ReadAdminLogAsync(sp);
        Assert.Equal(new[] { AdminUserActionKind.AiChatBlocked, AdminUserActionKind.AiChatUnblocked }, rows.Select(r => r.Kind));
        Assert.All(rows, r =>
        {
            Assert.Equal(AdminId, r.ActorUserId);
            Assert.Equal("user-" + AdminId, r.ActorUserName);
            Assert.Equal(TargetId, r.SubjectUserId);
            Assert.Equal("user-" + TargetId, r.SubjectUserName);
        });
        Assert.Equal("Abusive prompts", rows[0].Reason);
    }

    [Fact]
    public async Task ARefusedBlockWritesNoAdminLogRow()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);

        Assert.NotNull(await access.BlockAsync(TargetId, "", AdminId));

        Assert.Empty(await ReadAdminLogAsync(sp));
    }

    // ── OverseerAccessService: GetBlockAsync and its cache ──────────────────────

    [Fact]
    public async Task GetBlockAsync_ReturnsTheBlockAndItsReason()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        var blockedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await SeedUserAsync(sp, TargetId, blockedAt, "Abusive prompts");
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);

        var block = await access.GetBlockAsync(TargetId, TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Equal(blockedAt, block!.BlockedUtc);
        Assert.Equal("Abusive prompts", block.Reason);
    }

    [Fact]
    public async Task GetBlockAsync_ReturnsNullForAnUnblockedUnknownOrEmptyUser()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await access.GetBlockAsync(TargetId, ct));
        Assert.Null(await access.GetBlockAsync("no-such-user", ct));
        Assert.Null(await access.GetBlockAsync("", ct));
    }

    [Fact]
    public async Task GetBlockAsync_CachesTheBlock_AndUnblockAsyncEvictsIt()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId, DateTime.UtcNow, "Abusive prompts");
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);
        var ct = TestContext.Current.CancellationToken;

        Assert.NotNull(await access.GetBlockAsync(TargetId, ct));

        // Lifted behind the service's back: the cached block still answers.
        await WriteBlockDirectlyAsync(sp, TargetId, null, null);
        Assert.NotNull(await access.GetBlockAsync(TargetId, ct));

        // Lifted through the service: the entry is evicted and the next read sees the database.
        await WriteBlockDirectlyAsync(sp, TargetId, DateTime.UtcNow, "Reinstated for the eviction check");
        Assert.Null(await access.UnblockAsync(TargetId, AdminId));
        Assert.Null(await access.GetBlockAsync(TargetId, ct));
    }

    [Fact]
    public async Task GetBlockAsync_CachesNotBlocked_AndBlockAsyncEvictsIt()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);
        using var cache = NewSizedCache();
        var access = NewAccessService(sp, cache);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await access.GetBlockAsync(TargetId, ct));

        // Blocked behind the service's back: the cached "not blocked" still answers.
        await WriteBlockDirectlyAsync(sp, TargetId, DateTime.UtcNow, "Direct write");
        Assert.Null(await access.GetBlockAsync(TargetId, ct));

        Assert.Null(await access.BlockAsync(TargetId, "Abusive prompts", AdminId));
        var block = await access.GetBlockAsync(TargetId, ct);
        Assert.NotNull(block);
        Assert.Equal("Abusive prompts", block!.Reason);
    }

    [Fact]
    public void BlockedBody_CarriesTheCodeTheMessageAndTheReason()
    {
        object body = OverseerAccessService.BlockedBody(new OverseerBlock(DateTime.UtcNow, "Abusive prompts"));

        Assert.Equal("overseer_blocked", ReadProperty(body, "code"));
        Assert.Equal(OverseerAccessService.BlockedMessage, ReadProperty(body, "message"));
        Assert.Equal("Abusive prompts", ReadProperty(body, "reason"));
    }

    // ── RequireOverseerAccessAttribute ──────────────────────────────────────────

    private static async Task<(ActionExecutingContext Context, bool NextCalled)> RunFilterAsync(
        IServiceProvider requestServices, string? userId)
    {
        var identity = userId == null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = requestServices
        };
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        bool nextCalled = false;
        await new RequireOverseerAccessAttribute().OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (context, nextCalled);
    }

    private static ServiceProvider BuildFilterServices(string databaseName)
        => BuildDbServices(databaseName, services =>
        {
            services.AddSingleton<IMemoryCache>(_ => NewSizedCache());
            services.AddSingleton<OverseerAccessService>();
        });

    [Fact]
    public async Task Filter_RefusesABlockedUserWith403AndTheOverseerBlockedCode()
    {
        using var sp = BuildFilterServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId, DateTime.UtcNow, "Abusive prompts");

        var (context, nextCalled) = await RunFilterAsync(sp, TargetId);

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("overseer_blocked", ReadProperty(result.Value!, "code"));
        Assert.Equal("Abusive prompts", ReadProperty(result.Value!, "reason"));
    }

    [Fact]
    public async Task Filter_CallsNextForAnUnblockedUser()
    {
        using var sp = BuildFilterServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId);

        var (context, nextCalled) = await RunFilterAsync(sp, TargetId);

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Filter_CallsNextForAnAnonymousRequest()
    {
        using var sp = BuildFilterServices(Guid.NewGuid().ToString());

        var (context, nextCalled) = await RunFilterAsync(sp, userId: null);

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Filter_CallsNextWhenTheAccessServiceIsNotRegistered()
    {
        using var sp = BuildDbServices(Guid.NewGuid().ToString());
        await SeedUserAsync(sp, TargetId, DateTime.UtcNow, "Abusive prompts");

        var (context, nextCalled) = await RunFilterAsync(sp, TargetId);

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public void ChatControllerSend_CarriesTheFilter()
    {
        var send = typeof(ChatController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(ChatController.Send));

        Assert.NotNull(send.GetCustomAttribute<RequireOverseerAccessAttribute>());
    }

    // ── SessionController.Create ────────────────────────────────────────────────
    // Identity stubs hand-written as in SessionControllerCreateTests: the test project has no mocking library.

    private const string SessionSecret = "test-antiforgery-token";
    private const string SessionUserId = "game-user-1";
    private const string SessionUserName = "gnollhacker";

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public ApplicationUser? User { get; set; }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult(User);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult(User);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName?.ToUpperInvariant());

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public void Dispose() { }
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly StubUserStore _store;

        public StubUserManager(StubUserStore store)
            : base(store, null!, null!, Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(), null!, null!, null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _store = store;
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName)
            => Task.FromResult(_store.User);
    }

    private sealed class StubClaimsPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
            => Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth")));
    }

    private sealed class StubSignInManager : SignInManager<ApplicationUser>
    {
        public StubSignInManager(UserManager<ApplicationUser> userManager)
            : base(userManager, new HttpContextAccessor(), new StubClaimsPrincipalFactory(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                null!, null!)
        {
        }

        public override Task<Microsoft.AspNetCore.Identity.SignInResult> CheckPasswordSignInAsync(
            ApplicationUser user, string password, bool lockoutOnFailure)
            => Task.FromResult(password == "correct-password"
                ? Microsoft.AspNetCore.Identity.SignInResult.Success
                : Microsoft.AspNetCore.Identity.SignInResult.Failed);
    }

    /// <summary>Answers every block lookup with a fixed block and counts the lookups.</summary>
    private sealed class FixedBlockAccessService : OverseerAccessService
    {
        private readonly OverseerBlock? _block;

        public int Lookups { get; private set; }

        public FixedBlockAccessService(OverseerBlock? block)
            : base(null!, new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 }))
        {
            _block = block;
        }

        public override Task<OverseerBlock?> GetBlockAsync(string userId, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult(_block);
        }
    }

    private IConfiguration CreateSessionConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AntiForgeryToken", SessionSecret },
            { "ConversationsDataLocation", _dataDirectory },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private static ApplicationDbContext CreateSessionDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SessionController CreateSessionController(ApplicationDbContext db, IConfiguration config, bool userExists)
    {
        var store = new StubUserStore
        {
            User = userExists ? new ApplicationUser { Id = SessionUserId, UserName = SessionUserName } : null
        };
        var signInManager = new StubSignInManager(new StubUserManager(store));
        var protection = new ContentProtectionService(new ConfigurationContentKeyRing(config));
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return new SessionController(
            signInManager, db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), config,
            null!, new OngoingChatManager(config), retention,
            new ConfidentialPolicyResolver(config), protection,
            NullLogger<SessionController>.Instance);
    }

    private static CreateSessionRequest SessionRequest(string userName = SessionUserName, string password = "correct-password") => new()
    {
        UserName = userName,
        Password = password,
        AntiForgeryToken = SessionSecret,
        SnapshotText = "Dlvl 3",
        IsGnollHackSession = true
    };

    [Fact]
    public async Task SessionCreate_RefusesABlockedUserWith403AfterACorrectPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateSessionConfig();
        using var db = CreateSessionDb();
        var controller = CreateSessionController(db, config, userExists: true);
        var access = new FixedBlockAccessService(new OverseerBlock(DateTime.UtcNow, "Abusive prompts"));

        var result = await controller.Create(SessionRequest(), access, new RecordingSecurityEvents());

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Equal("overseer_blocked", ReadProperty(objectResult.Value!, "code"));
        Assert.Equal("Abusive prompts", ReadProperty(objectResult.Value!, "reason"));
        Assert.Equal(1, access.Lookups);

        Assert.Empty(await db.ChatSession.ToListAsync(ct));
        Assert.Empty(await db.ChatMessage.ToListAsync(ct));
    }

    [Fact]
    public async Task SessionCreate_AWrongPasswordIsUnauthorizedBeforeTheBlockIsConsulted()
    {
        var config = CreateSessionConfig();
        using var db = CreateSessionDb();
        var controller = CreateSessionController(db, config, userExists: true);
        var access = new FixedBlockAccessService(new OverseerBlock(DateTime.UtcNow, "Abusive prompts"));
        var recorder = new RecordingSecurityEvents();

        var result = await controller.Create(SessionRequest(password: "wrong-password"), access, recorder);

        // The block is not disclosed to someone who does not hold the password.
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, access.Lookups);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(SecurityEventKind.LoginFailed, entry.Kind);
        Assert.Equal(SessionUserId, entry.UserId);
        Assert.Equal("session-create", entry.Code);
    }

    [Fact]
    public async Task SessionCreate_AnUnblockedUserStillGetsASession()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CreateSessionConfig();
        using var db = CreateSessionDb();
        var controller = CreateSessionController(db, config, userExists: true);
        var access = new FixedBlockAccessService(block: null);

        var result = await controller.Create(SessionRequest(), access, new RecordingSecurityEvents());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, access.Lookups);
        Assert.Single(await db.ChatSession.ToListAsync(ct));
    }

    [Fact]
    public async Task SessionCreate_AnUnknownUserRecordsLoginFailedWithoutTheAttemptedName()
    {
        var config = CreateSessionConfig();
        using var db = CreateSessionDb();
        var controller = CreateSessionController(db, config, userExists: false);
        var recorder = new RecordingSecurityEvents();
        const string attemptedName = "someone-who-does-not-exist";

        var result = await controller.Create(SessionRequest(userName: attemptedName), new FixedBlockAccessService(null), recorder);

        Assert.IsType<UnauthorizedObjectResult>(result);
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(SecurityEventKind.LoginFailed, entry.Kind);
        Assert.Null(entry.UserId);
        Assert.Equal("unknown-user", entry.Detail);
        Assert.Equal("session-create", entry.Code);

        // No field of the recorded entry carries the name that was tried.
        Assert.DoesNotContain(attemptedName, entry.ToString());
    }

    // ── ChatService turn ────────────────────────────────────────────────────────
    // The container follows EphemeralTurnTests, with OverseerAccessService registered as Program.cs does.

    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"stub\":true}")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class StubAiProvider : IAiProvider
    {
        public const string ReplyText = "A stub answer.";

        private int _requestBodies;

        /// <summary>How many chat request bodies were built, that is, how many model calls the turn made.</summary>
        public int RequestBodies => Volatile.Read(ref _requestBodies);

        public string ProviderName => OverseerBlockTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            Interlocked.Increment(ref _requestBodies);
            return new Dictionary<string, object> { { "model", modelId } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
            => new { role, content = text, images = imageAttachments?.Count ?? 0 };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://stub.ai.test/stream", "/stream");

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = ReplyText };
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Stub Title";
    }

    private sealed class StubClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;
        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, ErrorMessage = "no client" });
    }

    private ServiceProvider BuildChatContainer(string databaseName, bool registerAccessService = true)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                { "PrivacySettings:ActiveKeyVersion", null }
            })
            .Build();

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        services.AddLogging();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, StubClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddSingleton<StubAiProvider>();
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<StubAiProvider>());
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<ParallelExecutionResolver>();
        services.AddSingleton<AttachmentValidator>();
        services.AddSingleton<IAntiMalwareScanner, NullAntiMalwareScanner>();
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
        services.AddSingleton<ConfidentialityPostureService>();
        services.AddSingleton<IContentKeyRing, ConfigurationContentKeyRing>();
        services.AddSingleton<ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), logger: null, startSweeper: false));
        services.AddScoped<ModelPricingService>();
        services.AddScoped<SystemAiConfigService>();
        services.AddScoped<ChatService>();
        if (registerAccessService)
        {
            services.AddSingleton<OverseerAccessService>();
        }
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedChatUserAsync(ServiceProvider container, string? blockReason)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "block-tester",
            OverseerBlockedUtc = blockReason == null ? null : DateTime.UtcNow,
            OverseerBlockReason = blockReason,
            OverseerBlockedByUserId = blockReason == null ? null : AdminId
        });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = userId,
            Provider = ProviderName,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
        });
        db.UserAiModels.Add(new UserAiModel
        {
            AspNetUserId = userId,
            Provider = ProviderName,
            ModelId = ModelId,
            OrderIndex = 0
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    private static ChatSession Template(string userId) => new()
    {
        AspNetUserId = userId,
        Title = "Block test chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    /// <summary>Runs one turn through GenerateAndBroadcastMessageAsync and returns the events it accumulated.</summary>
    private static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container, SessionRef sessionRef, string userId)
    {
        using var scope = container.CreateScope();
        var ongoing = scope.ServiceProvider.GetRequiredService<OngoingChatManager>();
        var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
        using var cts = new CancellationTokenSource();
        Assert.True(ongoing.TryStart(sessionRef, cts, out var state));

        await chatService.GenerateAndBroadcastMessageAsync(
            sessionRef, "What should I do next?", null, userId, false, TestContext.Current.CancellationToken);

        return state.AccumulatedEvents.ToList();
    }

    [Fact]
    public async Task ChatTurn_ForABlockedUser_EmitsOverseerBlockedAndGeneratesNothing()
    {
        using var container = BuildChatContainer(Guid.NewGuid().ToString());
        string userId = await SeedChatUserAsync(container, blockReason: "Abusive prompts");
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = store.Create(userId, Template(userId));

        var events = await RunTurnAsync(container, held.Ref, userId);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Equal("overseer_blocked", error.ErrorCode);
        Assert.Equal(OverseerAccessService.BlockedMessage, error.Data);
        Assert.Equal("Abusive prompts", error.Detail);
        Assert.Equal(held.Ref.ToWireString(), error.SessionId);

        Assert.DoesNotContain(events, e => e.Type == "user_message_created");
        Assert.DoesNotContain(events, e => e.Type == "chunk");
        Assert.Contains(events, e => e.Type == "done");
        Assert.Equal(0, stub.RequestBodies);
        Assert.Equal(0, held.MessageCount);
    }

    [Fact]
    public async Task ChatTurn_ForAnUnblockedUser_ReachesTheProvider()
    {
        // The control: the assertions above are absences, which a turn broken for another reason would also produce.
        using var container = BuildChatContainer(Guid.NewGuid().ToString());
        string userId = await SeedChatUserAsync(container, blockReason: null);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = store.Create(userId, Template(userId));

        var events = await RunTurnAsync(container, held.Ref, userId);

        Assert.DoesNotContain(events, e => e.ErrorCode == OverseerAccessService.BlockedCode);
        Assert.True(stub.RequestBodies > 0);
        Assert.Equal(2, held.MessageCount);
    }

    [Fact]
    public async Task GetOverseerBlockAsync_ReadsTheRegisteredAccessService()
    {
        using var container = BuildChatContainer(Guid.NewGuid().ToString());
        string blocked = await SeedChatUserAsync(container, blockReason: "Abusive prompts");
        string unblocked = await SeedChatUserAsync(container, blockReason: null);

        using var scope = container.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();

        var block = await chatService.GetOverseerBlockAsync(blocked);
        Assert.NotNull(block);
        Assert.Equal("Abusive prompts", block!.Reason);
        Assert.Null(await chatService.GetOverseerBlockAsync(unblocked));
    }

    [Fact]
    public async Task GetOverseerBlockAsync_IsNullWhenTheAccessServiceIsNotRegistered()
    {
        using var container = BuildChatContainer(Guid.NewGuid().ToString(), registerAccessService: false);
        string blocked = await SeedChatUserAsync(container, blockReason: "Abusive prompts");

        using var scope = container.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();

        Assert.Null(await chatService.GetOverseerBlockAsync(blocked));
    }
}
