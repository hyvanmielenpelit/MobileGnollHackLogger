using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Security;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The per-user limits on chat turns: the concurrency gate on operator-funded answers, the
/// rate limiter on handoff turns, the message cap in <c>ChatController.Send</c>, and the
/// <c>InitialPrompt</c> truncation in <c>SessionController.Create</c>.
/// </summary>
public class UserTurnLimitsTests : IDisposable
{
    private const string Owner = "turn-limits-user";
    private const string GameUserName = "gnollhacker";
    private const string Secret = "test-antiforgery-token";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "OverseerTurnLimits_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) { CurrentValue = value; }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Makes the detached generation task fault as soon as it starts.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Scope creation refused by the test fixture.");
    }

    /// <summary>Holds the detached generation task until released, then makes it fault.</summary>
    private sealed class HeldScopeFactory : IServiceScopeFactory
    {
        public ManualResetEventSlim Release { get; } = new(false);

        public IServiceScope CreateScope()
        {
            Release.Wait(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("Scope creation refused by the test fixture.");
        }
    }

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        public ConcurrentQueue<SecurityEventEntry> Entries { get; } = new();

        public void Record(SecurityEventEntry entry) => Entries.Enqueue(entry);
    }

    private static UserTurnConcurrencyGate Gate(int maxConcurrentTurnsPerUser)
        => new(new StaticOptionsMonitor<AiUsageGuardOptions>(
            new AiUsageGuardOptions { MaxConcurrentTurnsPerUser = maxConcurrentTurnsPerUser }));

    private static IOptions<AiUsageGuardOptions> Guard(int maxMessageCharacters = 100000, int maxInitialPromptCharacters = 2000)
        => Microsoft.Extensions.Options.Options.Create(new AiUsageGuardOptions
        {
            MaxMessageCharacters = maxMessageCharacters,
            MaxInitialPromptCharacters = maxInitialPromptCharacters
        });

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig(int? chatPermitsPerMinute = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AntiForgeryToken", Secret },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "PrivacySettings:Ephemeral:TimeoutMinutes", "30" },
            { "PrivacySettings:RateLimits:ChatPermitsPerMinute", chatPermitsPerMinute?.ToString() },
            { "ConversationsDataLocation", _dataDirectory },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        for (int waited = 0; waited < 2000; waited += 10)
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), failure);
    }

    // ── UserTurnConcurrencyGate ─────────────────────────────────────────────────

    [Fact]
    public void TheGateAdmitsUpToTheLimitAndRefusesBeyondIt()
    {
        var gate = Gate(2);

        Assert.True(gate.TryEnter(Owner));
        Assert.True(gate.TryEnter(Owner));
        Assert.False(gate.TryEnter(Owner));
        Assert.Equal(2, gate.CountFor(Owner));

        // Another user has slots of their own.
        Assert.True(gate.TryEnter("someone-else"));
    }

    [Fact]
    public void ExitingFreesASlotAndRemovesTheEntryAtZero()
    {
        var gate = Gate(1);

        Assert.True(gate.TryEnter(Owner));
        Assert.False(gate.TryEnter(Owner));

        gate.Exit(Owner);
        Assert.Equal(0, gate.CountFor(Owner));
        Assert.Equal(0, gate.TrackedUserCount);

        Assert.True(gate.TryEnter(Owner));
    }

    [Fact]
    public void ALimitOfZeroNeverRefuses()
    {
        var gate = Gate(0);

        for (int i = 0; i < 100; i++)
            Assert.True(gate.TryEnter(Owner));

        Assert.Equal(100, gate.CountFor(Owner));
    }

    [Fact]
    public void AnExitWithoutAnEntryLeavesNoNegativeCount()
    {
        var gate = Gate(1);

        gate.Exit(Owner);

        Assert.Equal(0, gate.CountFor(Owner));
        Assert.Equal(0, gate.TrackedUserCount);
        Assert.True(gate.TryEnter(Owner));
    }

    [Fact]
    public async Task ConcurrentEntriesAndExitsNeverExceedTheLimitOrGoNegative()
    {
        const int limit = 3;
        var gate = Gate(limit);
        int violations = 0;

        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++)
            {
                if (gate.TryEnter(Owner))
                {
                    int held = gate.CountFor(Owner);
                    if (held < 1 || held > limit) Interlocked.Increment(ref violations);
                    gate.Exit(Owner);
                }

                if (gate.CountFor(Owner) < 0) Interlocked.Increment(ref violations);
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(0, violations);
        Assert.Equal(0, gate.CountFor(Owner));
        Assert.Equal(0, gate.TrackedUserCount);
    }

    // ── UserTurnRateLimiter ─────────────────────────────────────────────────────

    [Fact]
    public void TheThirtyFirstTurnInAWindowIsRefusedForThatUserOnly()
    {
        using var limiter = new UserTurnRateLimiter(new ConfigurationBuilder().Build());

        for (int i = 0; i < 30; i++)
            Assert.True(limiter.TryAcquire("alice"), $"Permit {i + 1} was refused.");

        Assert.False(limiter.TryAcquire("alice"));
        Assert.True(limiter.TryAcquire("bob"));
    }

    // ── ChatController.Send ─────────────────────────────────────────────────────

    private static ChatController CreateChatController(
        ApplicationDbContext db, IConfiguration config, OngoingChatManager ongoing, IServiceScopeFactory scopeFactory)
    {
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return new ChatController(
            db, null!, config, null!, null!, null!, ongoing, scopeFactory, null!,
            retention, null!, new AttachmentValidator(config),
            new ConfidentialityPostureService(config), new ConfidentialPolicyResolver(config),
            new ContentProtectionService(new ConfigurationContentKeyRing(config)),
            new EphemeralSessionStore(config, startSweeper: false))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Owner) }, "TestAuth"))
                }
            }
        };
    }

    private static async Task AddOwnModelAsync(ApplicationDbContext db)
    {
        db.UserAiModels.Add(new UserAiModel { AspNetUserId = Owner, Provider = "Anthropic", ModelId = "own-model", OrderIndex = 0 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static bool IsCapRefusal(IActionResult result)
        => result is BadRequestObjectResult { Value: string text } && text.Contains("The limit is", StringComparison.Ordinal);

    [Fact]
    public async Task AnOperatorFundedMessageOverTheCapIsRefusedWithTheLimit()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());

        var result = await controller.Send(
            new SendMessageRequest { Message = new string('x', 11), SystemModelId = 1 },
            guardOptions: Guard(maxMessageCharacters: 10));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        string text = Assert.IsType<string>(bad.Value);
        Assert.Contains($"The limit is {10:N0} characters.", text);
        Assert.Contains($"Your message is {11:N0} characters long.", text);
        Assert.Empty(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOperatorFundedMessageAtTheCapIsAccepted()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());

        var result = await controller.Send(
            new SendMessageRequest { Message = new string('x', 10), SystemModelId = 1 },
            guardOptions: Guard(maxMessageCharacters: 10));

        Assert.False(IsCapRefusal(result));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AMessageOverTheCapOnTheUsersOwnModelIsNotRefused()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());

        var result = await controller.Send(
            new SendMessageRequest { Message = new string('x', 11), UserModelId = 7 },
            guardOptions: Guard(maxMessageCharacters: 10));

        Assert.False(IsCapRefusal(result));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AMessageOverTheCapNamingNoModelIsRefusedWhenTheUserHasNoModelOfTheirOwn()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());

        var result = await controller.Send(
            new SendMessageRequest { Message = new string('x', 11) },
            guardOptions: Guard(maxMessageCharacters: 10));

        Assert.True(IsCapRefusal(result));
    }

    [Fact]
    public async Task AMessageOverTheCapNamingNoModelIsNotRefusedWhenTheUserHasAModelOfTheirOwn()
    {
        using var db = CreateDb();
        await AddOwnModelAsync(db);
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());

        var result = await controller.Send(
            new SendMessageRequest { Message = new string('x', 11) },
            guardOptions: Guard(maxMessageCharacters: 10));

        Assert.False(IsCapRefusal(result));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ATurnOnTheUsersOwnModelNeitherEntersNorExitsTheGate()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        var controller = CreateChatController(db, config, ongoing, new ThrowingScopeFactory());

        // The only slot is taken, so an entry would be refused with 429.
        var gate = Gate(1);
        Assert.True(gate.TryEnter(Owner));

        var result = await controller.Send(
            new SendMessageRequest { Message = "On my own key", UserModelId = 7 },
            guardOptions: Guard(), turnGate: gate);

        var ok = Assert.IsType<OkObjectResult>(result);
        string wire = (string)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
        Assert.True(SessionRef.TryParse(wire, out var sessionRef));

        await WaitUntilAsync(() => ongoing.TryGet(sessionRef)?.IsCompleted == true,
            "The detached generation task did not complete.");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, gate.CountFor(Owner));
    }

    [Fact]
    public async Task AnOperatorFundedTurnHoldsAGateSlotUntilTheDetachedTaskEnds()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var scopeFactory = new HeldScopeFactory();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), scopeFactory);
        var gate = Gate(2);

        try
        {
            var result = await controller.Send(
                new SendMessageRequest { Message = "On the operator's key", SystemModelId = 1 },
                guardOptions: Guard(), turnGate: gate);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, gate.CountFor(Owner));
        }
        finally
        {
            scopeFactory.Release.Set();
        }

        await WaitUntilAsync(() => gate.CountFor(Owner) == 0, "The gate slot was not released.");
        Assert.Equal(0, gate.TrackedUserCount);
    }

    [Fact]
    public async Task AnOperatorFundedTurnBeyondTheGateIsRefusedWith429()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var controller = CreateChatController(db, config, new OngoingChatManager(config), new ThrowingScopeFactory());
        var gate = Gate(1);
        Assert.True(gate.TryEnter(Owner));

        var result = await controller.Send(
            new SendMessageRequest { Message = "One too many", SystemModelId = 1 },
            guardOptions: Guard(), turnGate: gate);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, refused.StatusCode);
        Assert.Equal(1, gate.CountFor(Owner));
    }

    [Fact]
    public async Task AGateSlotIsReleasedWhenTheSessionAlreadyHasAGenerationRunning()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.ChatSession.Add(new ChatSession
        {
            Id = 42, AspNetUserId = Owner, Title = "Busy",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        using var running = new CancellationTokenSource();
        Assert.True(ongoing.TryStart(SessionRef.Persistent(42), running, out _));

        var controller = CreateChatController(db, config, ongoing, new ThrowingScopeFactory());
        var gate = Gate(2);

        var result = await controller.Send(
            new SendMessageRequest { Message = "Again", SessionId = "42", SystemModelId = 1 },
            guardOptions: Guard(), turnGate: gate);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(0, gate.CountFor(Owner));
        Assert.Equal(0, gate.TrackedUserCount);
    }

    // ── SessionController.Create ────────────────────────────────────────────────
    // Hand-written Identity stubs, as in SessionControllerCreateTests: only finding the user
    // and checking the password are reached.

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

    private static SessionController CreateSessionController(
        ApplicationDbContext db, IConfiguration config, OngoingChatManager ongoing)
    {
        var store = new StubUserStore
        {
            User = new ApplicationUser { Id = Owner, UserName = GameUserName }
        };
        var signInManager = new StubSignInManager(new StubUserManager(store));
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return new SessionController(
            signInManager, db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), config,
            new ThrowingScopeFactory(), ongoing, retention,
            new ConfidentialPolicyResolver(config),
            new ContentProtectionService(new ConfigurationContentKeyRing(config)),
            NullLogger<SessionController>.Instance);
    }

    private static CreateSessionRequest HandoffRequest(string initialPrompt) => new()
    {
        UserName = GameUserName,
        Password = "correct-password",
        AntiForgeryToken = Secret,
        SnapshotText = "Dlvl 3",
        IsGnollHackSession = true,
        InitialPrompt = initialPrompt
    };

    private static (long SessionId, string HandoffToken) ReadCreated(IActionResult result)
    {
        dynamic value = Assert.IsType<OkObjectResult>(result).Value!;
        return ((long)value.sessionId, (string)value.handoffToken);
    }

    [Fact]
    public async Task AnOverLongInitialPromptIsTruncatedAndTheTurnStillStarts()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        var controller = CreateSessionController(db, config, ongoing);
        var gate = Gate(4);

        var request = HandoffRequest(new string('q', 50));
        var result = await controller.Create(request, guardOptions: Guard(maxInitialPromptCharacters: 20), turnGate: gate);

        var (sessionId, token) = ReadCreated(result);
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(new string('q', 20), request.InitialPrompt);
        Assert.NotNull(ongoing.TryGet(SessionRef.Persistent(sessionId)));

        await WaitUntilAsync(() => gate.CountFor(Owner) == 0, "The gate slot was not released.");
    }

    [Fact]
    public async Task AFaultedHandoffGreetingRecordsAnErrorOnTheSession()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        var controller = CreateSessionController(db, config, ongoing);

        var result = await controller.Create(HandoffRequest("Hello, Overseer"), guardOptions: Guard(), turnGate: Gate(4));

        var (sessionId, _) = ReadCreated(result);
        var sessionRef = SessionRef.Persistent(sessionId);
        await WaitUntilAsync(() => ongoing.TryGet(sessionRef)?.IsCompleted == true,
            "The background turn did not complete.");

        // The scope factory throws, so the greeting faults before it generates anything.
        var state = ongoing.TryGet(sessionRef)!;
        Assert.Contains(state.AccumulatedEvents,
            e => e.Type == "error" && e.Data == "The generation failed unexpectedly.");
    }

    [Fact]
    public async Task ARefusedRateLimitStillReturnsTheSessionAndTheHandoffToken()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var config = CreateConfig(chatPermitsPerMinute: 1);
        var ongoing = new OngoingChatManager(config);
        var controller = CreateSessionController(db, config, ongoing);
        var gate = Gate(4);
        var events = new RecordingSecurityEvents();

        using var limiter = new UserTurnRateLimiter(config);
        Assert.True(limiter.TryAcquire(GameUserName));

        var result = await controller.Create(HandoffRequest("Hello, Overseer"),
            securityEvents: events, guardOptions: Guard(), turnLimiter: limiter, turnGate: gate);

        var (sessionId, token) = ReadCreated(result);
        Assert.False(string.IsNullOrEmpty(token));
        Assert.NotNull(await db.ChatSession.FindAsync([sessionId], ct));

        // No greeting turn was started, and no gate slot was taken.
        Assert.Null(ongoing.TryGet(SessionRef.Persistent(sessionId)));
        Assert.Equal(0, gate.CountFor(Owner));

        var rejected = Assert.Single(events.Entries);
        Assert.Equal(SecurityEventKind.RateLimitRejected, rejected.Kind);
        Assert.Equal("session-create-turn", rejected.Code);
        Assert.Equal(Owner, rejected.UserId);
    }

    [Fact]
    public async Task ARefusedGateStillReturnsTheSessionAndRecordsNothing()
    {
        using var db = CreateDb();
        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        var controller = CreateSessionController(db, config, ongoing);
        var gate = Gate(1);
        Assert.True(gate.TryEnter(Owner));
        var events = new RecordingSecurityEvents();
        using var limiter = new UserTurnRateLimiter(config);

        var result = await controller.Create(HandoffRequest("Hello, Overseer"),
            securityEvents: events, guardOptions: Guard(), turnLimiter: limiter, turnGate: gate);

        var (sessionId, token) = ReadCreated(result);
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Null(ongoing.TryGet(SessionRef.Persistent(sessionId)));
        Assert.Equal(1, gate.CountFor(Owner));
        Assert.Empty(events.Entries);
    }

    [Fact]
    public async Task AHandoffTurnOnTheUsersOwnModelPassesAFullGate()
    {
        using var db = CreateDb();
        await AddOwnModelAsync(db);
        var config = CreateConfig();
        var ongoing = new OngoingChatManager(config);
        var controller = CreateSessionController(db, config, ongoing);
        var gate = Gate(1);
        Assert.True(gate.TryEnter(Owner));

        var result = await controller.Create(HandoffRequest("Hello, Overseer"), guardOptions: Guard(), turnGate: gate);

        var (sessionId, _) = ReadCreated(result);
        Assert.NotNull(ongoing.TryGet(SessionRef.Persistent(sessionId)));

        await WaitUntilAsync(() => ongoing.TryGet(SessionRef.Persistent(sessionId))?.IsCompleted == true,
            "The background turn did not complete.");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, gate.CountFor(Owner));
    }
}
